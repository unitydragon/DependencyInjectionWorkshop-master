using Dapper;
using SlackAPI;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;

namespace DependencyInjectionWorkshop.Models
{
    public class AuthenticationService
    {
        public bool Verify(string accountId, string password, string otp)
        {
            string currentPassword;
            //確認帳號有無被鎖住
            var httpClient = new HttpClient() { BaseAddress = new Uri("http://joey.com/") };
            var isLockedResponse = httpClient.PostAsJsonAsync("api/failedCounter/IsLocked", accountId).Result;

            isLockedResponse.EnsureSuccessStatusCode();
            if (isLockedResponse.Content.ReadAsAsync<bool>().Result)
            {
                throw new FailedTooManyTimesException();

            }
            //進入資料庫拿取帳號密碼資料
            using (var connection = new SqlConnection("datasource=db,password=abc"))
            {
                currentPassword = connection.Query<string>("spGetUserPassword", new { Id = accountId },
                    commandType: CommandType.StoredProcedure).SingleOrDefault();
            }
            //加密使用者輸入的密碼與資料庫的比對
            var crypt = new System.Security.Cryptography.SHA256Managed();
            var hash = new StringBuilder();
            var crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(password));
            foreach (var theByte in crypto)
            {
                hash.Append(theByte.ToString("x2"));
            }

            var hashPassword = hash.ToString();

            //獲取服務端的OTP
            var response = httpClient.PostAsJsonAsync("api/otps", accountId).Result;
            string currentOtp;
            if (response.IsSuccessStatusCode)
            {
                currentOtp = response.Content.ReadAsAsync<string>().Result;
            }
            else
            {
                throw new Exception($"web api error, accountId:{accountId}");
            }


            if (hashPassword == currentPassword && otp == currentOtp)
            {
                var resetResponse = httpClient.PostAsJsonAsync("api/failedCounter/Reset", accountId).Result;
                return true;
            }
            else
            {
                var slackClient = new SlackClient("my api token");
                slackClient.PostMessage(postResponse => { }, "my channel", "my message", "my bot name");

                var addFailedCountResponse = httpClient.PostAsJsonAsync("api/failedCounter/Add", accountId).Result;

                addFailedCountResponse.EnsureSuccessStatusCode();

                var failedCountResponse =
                    httpClient.PostAsJsonAsync("api/failedCounter/GetFailedCount", accountId).Result;

                failedCountResponse.EnsureSuccessStatusCode();

                var failedCount = failedCountResponse.Content.ReadAsAsync<int>().Result;
                var logger = NLog.LogManager.GetCurrentClassLogger();
                logger.Info($"accountId:{accountId} failed times:{failedCount}");

                return false;
            }
        }

        [Serializable]
        private class FailedTooManyTimesException : Exception
        {
            public FailedTooManyTimesException()
            {
            }

            public FailedTooManyTimesException(string message) : base(message)
            {
            }

            public FailedTooManyTimesException(string message, Exception innerException) : base(message, innerException)
            {
            }

            protected FailedTooManyTimesException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
        }
    }
}