using Ozan.Tg._3ds;
using RestSharp;
using Starksoft.Net.Proxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TL;
using WTelegramClient.Extensions.Updates;

namespace Ozan.Tg
{
    public class Client
    {
        public WTelegram.Client client;
        private WebProxy proxy = null;
        private string api_id;
        private string api_hash;
        private readonly TgAuth auth;

        /// <summary>
        /// 实例化
        /// </summary>
        /// <param name="api_id">tg api id</param>
        /// <param name="api_hash">tg api hash</param>
        /// <param name="s5ProxyStr">s5代理文本 socks5://ip:port</param>
        public Client(string api_id, string api_hash, TgAuth auth,string s5ProxyStr)
        {
           
            this.api_id = api_id;
            this.api_hash = api_hash;
            this.auth = auth;
            if (!string.IsNullOrEmpty(s5ProxyStr))
            {
                proxy = new WebProxy(s5ProxyStr);
            }
        }

        /// <summary>
        /// 开始连接
        /// </summary>
        /// <param name="auth"></param>
        /// <returns></returns>
        public async Task Connect()
        {
            client = new WTelegram.Client((a) =>
            {
                return a switch
                {
                    "api_id" => api_id,
                    "api_hash" => api_hash,
                    _ => null
                };
            }, auth);
            WTelegram.Helpers.Log = (lvl, str) => { };
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (this.proxy != null)
            {
                client.TcpHandler = async (address, port) =>
                {
                    var p = new Socks5ProxyClient(proxy.Address.Host, proxy.Address.Port);

                    return p.CreateConnection(address, port);

                };
            }
            await client.ConnectAsync();
        }


        /// <summary>
        /// 获取TG付款最后的3DS验证连接
        /// </summary>
        /// <param name="card">银行卡信息</param>
        /// <param name="isAnnual">是否是年付 默认是月付</param>
        /// <returns></returns>
        public async Task<string?> GetPremiumPay3ds(ICardInfo card, bool isAnnual)
        {
            var fd = await client.Contacts_ResolveUsername("PremiumBot");
            Payments_PaymentResultBase result = null;
            client.OnUpdate += async up =>
            {
                foreach (var update in up.UpdateList)
                {
                    if (update is UpdateNewMessage {message: Message {Peer: PeerUser {user_id: 5314653481} send} mg})
                    {
                        if (mg is {media: MessageMediaInvoice {title:"Telegram Premium Subscription" or "Annual Premium Subscription" } invoice})
                        {
                            //是支付发票
                            var inv = new InputInvoiceMessage()
                            {
                                msg_id = mg.id,
                                peer = new InputPeerUser(send.user_id, fd.User.access_hash)
                            };

                            var pay = await client.Payments_GetPaymentForm(inv);
                            var json = await GetPayForm(pay.url, card);
                            if (!string.IsNullOrEmpty(json))
                            {
                                var info = new InputPaymentCredentials()
                                {
                                    data = new DataJSON()
                                    {
                                        data = json
                                    },
                                };
                                result = await client.Payments_SendPaymentForm(inv.msg_id, inv, info);
                            }
                        }
                    }
                }
            };
            string send = isAnnual ? "/upgrade" : "/start";
            await client.SendMessageAsync(new InputPeerUser(fd.User.ID, fd.User.access_hash), send);
            //等等3DS验证
            while (result == null)
            {
                await Task.Delay(1000);
            }
            if (result is Payments_PaymentVerificationNeeded r)
            {
                return r.url;
            }
            return null;
        }



        public async Task<bool> StopPremium()
        {
            var fd = await client.Contacts_ResolveUsername("PremiumBot");
            bool? result = null;
            client.OnUpdate += async up =>
            {
                foreach (var update in up.UpdateList)
                {
                    if (update is UpdateNewMessage {message: Message {Peer: PeerUser {user_id: 5314653481} send} mg})
                    {
                        //在 mg.message 里面寻找内容 stop your recurring subscription
                        if (mg.message.Contains("stop your recurring subscription"))
                        {
                            await client.SendMessageAsync(new InputPeerUser(fd.User.ID, fd.User.access_hash), "Yes, stop the subscription");
                            return;
                        }
                        //mg.message 寻找 To cancel your subscription
                        if (mg.message.Contains("To cancel your subscription"))
                        {
                            await client.SendMessageAsync(new InputPeerUser(fd.User.ID, fd.User.access_hash), "The price was too high");
                            return;
                        }

                        if (mg.message.Contains("Recurring payments are now stopped"))
                        {
                            result = true;
                        }
                 


                    }
                }

            };
            await client.SendMessageAsync(new InputPeerUser(fd.User.ID, fd.User.access_hash), "/stop");

            while (true)
            {
                if (result.HasValue)
                {
                    return result.Value;
                }

                await Task.Delay(1000);
            }
        }


        private async Task<string> GetPayForm(string url, ICardInfo cardInfo)
        {
            //解析url 获取路径
            var uri = new Uri(url);
            var path = uri.PathAndQuery;
            //替换path根路径改为/api
            path = path.Replace(uri.Segments[1], "api/");
            //修改client的User-Agent
            var options = new RestClientOptions($"https://{uri.Host}")
            {
                UserAgent =
                    "Mozilla/5.0 (Linux; Android 10; Redmi K30 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4389.105 Mobile Safari/537.36",
                Proxy = new WebProxy("127.0.0.1", 10809)
            };
            var client = new RestClient(options);
            var request = new RestRequest(path, Method.Post);
            request.AddHeader("Origin", uri.Host);
            request.AddHeader("Referer", url);
            var card = cardInfo.cardNumber;
            var expirationMonth = cardInfo.cardExpiryMonth;
            var expirationYear = cardInfo.cardExpiryYear;
            var securityCode = cardInfo.cardCvv;
            var post =
                $"{{\"customerDetails\":{{}},\"paymentDetails\":{{\"card\":{{\"number\":\"{card}\",\"expirationMonth\":\"{expirationMonth}\",\"expirationYear\":\"{expirationYear}\",\"securityCode\":\"{securityCode}\"}}}},\"systemInfo\":{{\"deviceSessionId\":null}}}}";
            request.AddJsonBody(post, ContentType.Json);
            var response = await client.ExecuteAsync(request);
            return response.Content ?? "";
        }
    }


    public interface ICardInfo
    {
        public string cardNumber { get; set; }
        public string cardHolderName { get; set; }
        public string cardExpiryMonth { get; set; }
        public string cardExpiryYear { get; set; }
        public string cardCvv { get; set; }
    }
}