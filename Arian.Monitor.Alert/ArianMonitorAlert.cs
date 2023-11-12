using System.Net;
using Azure.Monitor.Query;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using System.Globalization;
using Azure.Core;
using static System.Net.WebRequestMethods;
using System;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;

namespace Arian.Monitor.Alert
{
    public class ArianMonitorAlert
    {
        private readonly ILogger _logger;

        public ArianMonitorAlert(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ArianMonitorAlert>();
        }

        [Function("ArianMonitorAlert")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var accessToken =await GetAccessToken();

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Request body: {requestBody}", requestBody);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string alertId = data?.data?.essentials?.alertId;
            string alertRule = data?.data?.essentials?.alertRule;
            string firedDateTime = data?.data?.essentials?.firedDateTime;
            string logApiEndpoint = data?.data?.alertContext?.condition?.allOf[0]?.linkToFilteredSearchResultsAPI;

            string errorMessage;
            var response = req.CreateResponse(HttpStatusCode.OK);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

                var result = await client.GetAsync(logApiEndpoint);


                //returning a string based Json
                string content = await result.Content.ReadAsStringAsync();

                MonitorResult mr = JsonConvert.DeserializeObject<MonitorResult>(content);
                //you can look through the various rows/columns or combine all as aprt of your exception reporting from log analytics workspace.
                var a = mr.tables[0].rows[0];
                errorMessage = a[12].ToString();
                // Anything else you want to do with the result
            }

            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            // Create the hyperlink using Slack's message formatting  
            string hyperlink = $"<{errorMessage}|Click here>";

            // Include the hyperlink, emojis, and additional text in the JSON text  
            string jsonText = $"{{\"text\": \"*:rotating_light: Alert! :rotating_light:*\\n*Alert ID:* {alertId}\\n*Alert Rule:* {alertRule}\\n*Fired Date/Time:* {firedDateTime}\\n*Link to Error Message:* {hyperlink} :point_right: :mag:\"}}";

            using (HttpClient client = new HttpClient())
            {
                HttpRequestMessage slackMessage = new HttpRequestMessage(HttpMethod.Post, "SLACK_WEBHOOK_URI");
                slackMessage.Content = new StringContent(jsonText, Encoding.UTF8, "application/json");

                await client.SendAsync(slackMessage);
            }


            response.WriteString("Sent error message details to Slack channel");

            return response;
        }

        private async Task<AccessToken> GetAccessToken()
        {

            var tokenCredential = new DefaultAzureCredential();
            var accessToken = await tokenCredential.GetTokenAsync(
                new TokenRequestContext(scopes: new string[] { "https://api.loganalytics.io" + "/.default" }) { }
            );

            return accessToken;

        }
    }
}

