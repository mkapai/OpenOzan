using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RestSharp;

namespace Ozan.Core
{
    public class Api
    {

        public string deviceCode;
        public string androidVer;
        public string androidModel;
        public string appVer;
        private RestClientOptions options;
        private readonly List<KeyValuePair<string, string>> headers;

        private string clientToken;
        public Api(string deviceCode,string androidVer,string androidModel,string appVer= "3.2.15",IWebProxy proxy = null)
        {
            this.deviceCode = deviceCode;
            this.androidVer = androidVer;
            this.androidModel = androidModel;
            this.appVer = appVer;
            this.options = new RestClientOptions("https://op-prod-tr.ozan.com")
            {
                UserAgent = $"Ozan/v{appVer}/android/{androidVer}/{androidModel} DeviceId/{deviceCode}",
                Proxy = proxy,
                MaxTimeout = 60 * 1000
            };
            options.Expect100Continue = false;
            options.CookieContainer = new CookieContainer();
            headers = new List<KeyValuePair<string, string>>
            {
                new("X-ZONE-ID", "Asia/Taipei"),
                new("X-DEVICE-CODE", deviceCode),
                new("X-CHANNEL", "android"),
                new("Accept-Language", "en"),
                new("Authorization", "Basic b3phbi1hbmRyb2lkOkpUblFrRnlHOVo=")
            };
        }

        /// <summary>
        /// 获取临时密钥
        /// level 1
        /// </summary>
        /// <returns></returns>
        public async Task<RestResponse> ClientCredentials()
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/oauth/token", Method.Post);
            request.AddQueryParameter("grant_type", "client_credentials");
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("Content-Type", "application/json");
            var response = await client.ExecuteAsync(request);
            if (!string.IsNullOrEmpty(response.Content))
            {
                var json =JsonConvert.DeserializeObject<JObject>(response.Content);
                clientToken = json["access_token"].Value<string>();
            }

            return response;
        }


        /// <summary>
        /// 手机号预验证
        /// level 1
        /// </summary>
        /// <param name="phone">号码 905467790531格式</param>
        /// <returns></returns>
        public async Task<RestResponse> PreVerification(string phone)
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/oauth/token", Method.Post);
            request.AddQueryParameter("grant_type", "pre_verification");
            //生成13位时间戳
            var EPOCH = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("X-EPOCH", EPOCH);
            request.AddOrUpdateHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddOrUpdateHeader("X-SIGNATURE", Core.getSignature(phone, $"{EPOCH}"));
            request.AddParameter("username", phone);
            var response = await client.ExecuteAsync(request);
            return response;
        }

        static string FormatUSAPhoneNumber(string phoneNumber)
        {
            // 假设phoneNumber的长度是11位
            string formattedNumber = string.Format("+{0} {1}-{2}-{3}",
                phoneNumber.Substring(0, 1),    // +1
                phoneNumber.Substring(1, 3),    // 240
                phoneNumber.Substring(4, 3),    // 552
                phoneNumber.Substring(7, 4)     // 6555
            );

            return formattedNumber;
        }        
        static string FormatTRPhoneNumber(string phoneNumber)
        {
            // 假设phoneNumber的长度是11位
            string formattedNumber = string.Format("+{0} {1} {2} {3} {4}",
                phoneNumber.Substring(0, 2),    // +90
                phoneNumber.Substring(2, 3),    // 546
                phoneNumber.Substring(5, 3),    // 779
                phoneNumber.Substring(8, 2),    // 05
                phoneNumber.Substring(10, 2)    // 31
            );

            return formattedNumber;
        }

        /// <summary>
        /// 手机号验证码验证
        /// level 1
        /// </summary>
        /// <param name="phone">号码 +开头不会格式化
        /// 1和90会格式化+1 240-552-6555  +90 546 779 05 31</param>
        /// <param name="mfaToken">pre返回的token</param>
        /// <param name="mfaCode">手机验证码</param>
        /// <returns></returns>
        public async Task<RestResponse> PreVerificationMfa(string phone,string mfaToken,string mfaCode)
        {
            //phone如果是1开头格式化为+1 240-552-6555
            //phone如果是90开头格式化为+90 546 779 05 31
            if (phone.StartsWith("1"))
            {
                phone = FormatUSAPhoneNumber(phone);
            }
            if (phone.StartsWith("90"))
            {
                phone = FormatTRPhoneNumber(phone);
            }


            var client = new RestClient(options);
            var request = new RestRequest("/api/oauth/token", Method.Post);
            //生成13位时间戳
            var EPOCH = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("X-EPOCH", EPOCH);
            request.AddOrUpdateHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddOrUpdateHeader("X-SIGNATURE", Core.getSignature(phone, $"{EPOCH}"));
            request.AddQueryParameter("grant_type", "pre_verification");
            request.AddParameter("mfa_token", mfaToken);
            request.AddParameter("mfa_code", mfaCode);
            var response = await client.ExecuteAsync(request);
            return response;
        }


        /// <summary>
        /// 密码登录验证
        /// level 1
        /// </summary>
        /// <param name="phone">手机号 12405526555</param>
        /// <param name="password">密码</param>
        /// <param name="mfaToken">手机验证码之后的token</param>
        /// <returns></returns>
        public async Task<RestResponse> Password(string phone,string password, string accessToken = "")
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/oauth/token", Method.Post);
            request.AddQueryParameter("grant_type", "password");
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("Content-Type", "application/x-www-form-urlencoded");
            if (!string.IsNullOrEmpty(accessToken)) request.AddParameter("mfa_token", accessToken);
            request.AddParameter("password", password);
            request.AddParameter("username", phone);
            var response = await client.ExecuteAsync(request);
            return response;
        }




        /// <summary>
        /// 查询设备绑定此用户没有
        /// level 2
        /// </summary>
        /// <param name="phone"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<RestResponse> UserDevice(string phone)
        {
            //判断phone是否有+号 有就去除
            if (phone.Contains("+")) phone = phone.Replace("+", "");
            var client = new RestClient(options);
            var request = new RestRequest($"/api/users/{phone}");
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("Authorization", $"Bearer {clientToken}");
            var response = await client.ExecuteAsync(request);
            return response;
        }


        /// <summary>
        /// 获取数据
        /// level 3
        /// </summary>
        /// <param name="token"></param>
        /// <param name="holderId"></param>
        /// <returns></returns>
        public async Task<RestResponse> Me(string token, string holderId = "")
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/users/me");
            request.AddHeaders(headers);
            request.AddOrUpdateHeader("Authorization", $"Bearer {token}");
            if (!string.IsNullOrEmpty(holderId)) request.AddOrUpdateHeader("X-ACCOUNT-HOLDER-ID", holderId);
            var response = await client.ExecuteAsync(request);
            return response;
        }

        /// <summary>
        /// 获取余额
        /// level 3
        /// </summary>
        /// <param name="token"></param>
        /// <param name="holderId"></param>
        /// <returns></returns>
        public async Task<RestResponse> Balances(string token, string holderId = "")
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/balances");
            request.AddOrUpdateHeader("Authorization", $"Bearer {token}");
            if (!string.IsNullOrEmpty(holderId)) request.AddOrUpdateHeader("X-ACCOUNT-HOLDER-ID", holderId);

            var response = await client.ExecuteAsync(request);
            return response;
        }


        /// <summary>
        /// 获取通知
        /// level 3
        /// </summary>
        /// <param name="token"></param>
        /// <param name="page"></param>
        /// <param name="size"></param>
        /// <param name="holderId"></param>
        /// <returns></returns>
        public async Task<RestResponse> Notifications(string token, string holderId = "",int page = 0,int size = 20)
        {
            var client = new RestClient(options);
            var request = new RestRequest("/api/notifications/mobile/list");

            request.AddOrUpdateHeader("Authorization", $"Bearer {token}");
            if (!string.IsNullOrEmpty(holderId)) request.AddOrUpdateHeader("X-ACCOUNT-HOLDER-ID", holderId);
            request.AddQueryParameter("page", page);
            request.AddQueryParameter("size", size);
            var response = await client.ExecuteAsync(request);
            return response;
        }


        /// <summary>
        /// 获取银行卡详情信息
        /// level 3
        /// </summary>
        /// <param name="token"></param>
        /// <param name="cardId"></param>
        /// <param name="holderId"></param>
        /// <returns></returns>
        public async Task<RestResponse> CardInfo(string token, string cardId, string holderId = "" )
        {
            var client = new RestClient(options);
            var request = new RestRequest($"/api/cards/{cardId}");

            request.AddOrUpdateHeader("Authorization", $"Bearer {token}");
            if (!string.IsNullOrEmpty(holderId)) request.AddOrUpdateHeader("X-ACCOUNT-HOLDER-ID", holderId);
            var response = await client.ExecuteAsync(request);
            return response;
        }


        /// <summary>
        /// 获取银行卡列表
        /// level 3
        /// </summary>
        /// <param name="token"></param>
        /// <param name="holderId"></param>
        /// <returns></returns>
        public async Task<RestResponse> CardList(string token, string holderId = "")
        {

            var client = new RestClient(options);
            var request = new RestRequest($"/api/cards");

            request.AddOrUpdateHeader("Authorization", $"Bearer {token}");
            if (!string.IsNullOrEmpty(holderId)) request.AddOrUpdateHeader("X-ACCOUNT-HOLDER-ID", holderId);

            request.AddQueryParameter("cardStatus", "ACTIVE,BLOCKED,LOCKED,LOST,STOLEN,EXPIRED,PIN_BLOCKED");
            request.AddQueryParameter("page", 0);
            var response = await client.ExecuteAsync(request);
            return response;

        }

    }
}
