using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Reflection;
using AngleSharp.Io;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ozan.Core;
using Ozan.Tg._3ds;
using Spectre.Console;
using Spectre.Console.Cli;
using TL;
using WTelegramClient.Extensions.Updates;
using static Ozan.Program.OzanLoginCommand;

namespace Ozan.Tg
{
    public class Card : ICardInfo
    {
        public string cardNumber { get; set; }
        public string cardHolderName { get; set; }
        public string cardExpiryMonth { get; set; }
        public string cardExpiryYear { get; set; }
        public string cardCvv { get; set; }
    }

    public class PaySettings : CommandSettings
    {
        [Description("TG API_ID")]
        [CommandArgument(0, "[ApiId]")]
        public string ApiId { get; init; } = "23056861";

        [Description("TG api_hash")]
        [CommandArgument(1, "[ApiHash]")]
        public string ApiHash { get; init; } = "23ec1f145112a49d9b08515c8701ce18";

        [Description("使用代理,只支持socks5")]
        [CommandOption("--proxy")]
        public string Proxy { get; init; } = "";


        static PaySettings()
        {
#if DEBUG

            Month = "49.99";
            Annual = "449.99";
#else

          (Month,Annual) = NetworkAuth.getTl();
#endif
        }

        [Obfuscation(Feature = "virtualization", Exclude = false)]
        public static string Month;

        [Obfuscation(Feature = "virtualization", Exclude = false)]
        public static string Annual;
    }


    public class PremiumOzanCommand : AsyncCommand<PremiumOzanCommand.Settings>
    {
        public sealed class Settings : PaySettings
        {
            [Description("是否开通年费,默认是月费")]
            [CommandOption("-a|--annual")]
            public bool Annual { get; init; } = false;

            [Description("Ozan 凭据目录")]
            [CommandOption("-p|--opath")]
            public string OzanPath { get; init; } = ".\\Ozan";

            [Description("Tdata 目录-包含有tdata目录的目录,能识别所选目录下的一,二级目录下有tdata")]
            [CommandOption("-t|--tpath")]
            public string TdfPath { get; init; } = ".\\Tdata";

            [Description("Session 目录")]
            [CommandOption("-s|--spath")]
            public string SessionPath { get; init; } = ".\\TSession";


            public override ValidationResult Validate()
            {
                var workPath = Environment.CurrentDirectory;
                var OzanPath = Path.GetFullPath(this.OzanPath, workPath);
                if (!Directory.Exists(OzanPath))
                {
                    return ValidationResult.Error("Ozan凭据 目录为空!");
                }

                var TdfPath = Path.GetFullPath(this.TdfPath, workPath);
                if (!Directory.Exists(TdfPath))
                {
                    return ValidationResult.Error("TdfPath 目录为空!");
                }

                var SessionPath = Path.GetFullPath(this.SessionPath, workPath);
                if (!Directory.Exists(SessionPath))
                {
                    return ValidationResult.Error("SessionPath 目录为空!");
                }

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var workPath = Environment.CurrentDirectory;
            //判断代理是否为空
            WebProxy proxy = null;
            if (!string.IsNullOrEmpty(settings.Proxy))
            {
                proxy = new WebProxy(settings.Proxy);
            }

            ConcurrentQueue<OzanFile> ozan_queue = new();
            ConcurrentQueue<Client> clients_queue = new();

            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var otask = ctx.AddTask("[yellow]Ozan[/] 正在收集数据.");
                    var tg = ctx.AddTask("[yellow]TG[/] 正在收集数据.");
                    var OzanRun = Task.Run(async () =>
                    {
                        var OzanPath = Path.GetFullPath(settings.OzanPath, workPath);
                        string[] jsonFiles = Directory.GetFiles(OzanPath, "*.json");
                        if (jsonFiles.Length == 0) return;
                        otask.MaxValue(jsonFiles.Length);
                        foreach (var jsonFile in jsonFiles)
                        {
                            try
                            {
                                var jsonContent = await File.ReadAllTextAsync(jsonFile);
                                var json = JsonConvert.DeserializeObject<OzanFile>(jsonContent);
                                var api = new Api(json.DeviceCode, json.AndroidVer, json.AndroidModel, json.AppVer,
                                    proxy);
                                await api.ClientCredentials();
                                var res = await api.Password(json.Phone, json.PassWord);
                                if (res.IsSuccessStatusCode)
                                {
                                    var obj = JsonConvert.DeserializeObject<JObject>(res.Content);
                                    var access_token = obj["access_token"].ToString();
                                    var accountHolderId = obj["accountHolderId"].ToString();
                                    var accountHolderStatus = obj["accountHolderStatus"].ToString();

                                    if (accountHolderStatus == "ACTIVE")
                                    {
                                        json.Token = access_token;
                                        json.AccountHolderId = accountHolderId;
                                        var list = await api.CardList(access_token, accountHolderId);
                                        if (list.IsSuccessStatusCode && !string.IsNullOrEmpty(list.Content))
                                        {
                                            JObject cardList = JsonConvert.DeserializeObject<JObject>(list.Content);
                                            var listCard = new List<ICardInfo>();
                                            foreach (var card in cardList["content"].ToObject<JArray>())
                                            {
                                                var id = card["id"].Value<string>();

                                                var info = await api.CardInfo(json.Token, id, json.AccountHolderId);
                                                if (info.IsSuccessStatusCode)
                                                {
                                                    var cardInfo = JsonConvert.DeserializeObject<JObject>(info.Content);
                                                    if (cardInfo["cardStatus"].Value<string>() == "ACTIVE")
                                                    {
                                                        var objCard = new Card
                                                        {
                                                            cardCvv = cardInfo["cvv"].Value<string>(),
                                                            cardNumber = cardInfo["pan"].Value<string>()
                                                        };
                                                        var expiryDate = cardInfo["expiryDate"].Value<string>();
                                                        //把expiryDate 原本的YYYY-MM的格式转换成MM/YY
                                                        var arr = expiryDate.Split("-");
                                                        objCard.cardExpiryMonth = arr[1];
                                                        objCard.cardExpiryYear = arr[0].Substring(2, 2);
                                                        listCard.Add(objCard);
                                                    }
                                                }
                                            }
                                            json.CardInfos = listCard;
                                            ozan_queue.Enqueue(json);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                //解析失败
                                AnsiConsole.WriteException(e);
                            }

                            otask.Increment(1);
                        }
                    });
                    var OTgRun = Task.Run(async () =>
                    {
                        var TdfPath = Path.GetFullPath(settings.TdfPath, workPath);
                        var tdfDirs = Directory.GetDirectories(TdfPath);

                        //扫描SessionPath目录下的 .session文件
                        var SessionPath = Path.GetFullPath(settings.SessionPath, workPath);
                        var sessionFiles = Directory.GetFiles(SessionPath, "*.session");
                        if (tdfDirs.Length + sessionFiles.Length == 0) return;
                        tg.MaxValue(tdfDirs.Length + sessionFiles.Length);
                        //处理tdata
                        foreach (var pDir in tdfDirs)
                        {
                            try
                            {
                                var tdfDir = Path.GetFullPath(pDir, workPath);
                                var tdfFiles = Directory.GetFiles(tdfDir, "key_datas");
                                if (tdfFiles.Length > 0)
                                {
                                    var auth = await Tdf2Auth(tdfDir, settings.ApiId, settings.ApiHash);
                                    clients_queue.Enqueue(
                                        new Client(settings.ApiId, settings.ApiHash, auth, settings.Proxy));
                                }
                                else
                                {
                                    //获取tdfDir下的子目录 遍历子目录下的key_datas文件
                                    var tdfSubDirs = Directory.GetDirectories(tdfDir);
                                    tg.MaxValue += tdfSubDirs.Length;
                                    foreach (var subDir in tdfSubDirs)
                                    {
                                        var tdfSubDir = Path.GetFullPath(subDir, workPath);
                                        var tdfSubFiles = Directory.GetFiles(tdfSubDir, "key_datas");
                                        if (tdfSubFiles.Length > 0)
                                        {
                                            var auth = await Tdf2Auth(tdfSubDir, settings.ApiId, settings.ApiHash);
                                            clients_queue.Enqueue(new Client(settings.ApiId, settings.ApiHash, auth,
                                                settings.Proxy));
                                        }

                                        tg.Increment(1);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                AnsiConsole.WriteException(e);
                            }

                            tg.Increment(1);
                        }

                        //处理session文件
                        foreach (var sessionFile in sessionFiles)
                        {
                            try
                            {
                                var auth = await Session2Auth(sessionFile, settings.ApiId, settings.ApiHash);
                                clients_queue.Enqueue(
                                    new Client(settings.ApiId, settings.ApiHash, auth, settings.Proxy));
                            }
                            catch (Exception e)
                            {
                                AnsiConsole.WriteException(e);
                            }

                            tg.Increment(1);
                        }
                    });
                    await Task.WhenAll(OTgRun, OzanRun);
                });

            if (ozan_queue.Count == 0 && clients_queue.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]Ozan或TG为空[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[green]数据收集完成![/],操作进行中..");

            var rescode = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Smiley)
                .StartAsync("开通会员中", async ctx =>
            {
                var tl = settings.Annual ? PaySettings.Annual : PaySettings.Month;
                while (true)
                {
                    try
                    {
                        if (!ozan_queue.TryDequeue(out var oz))
                        {
                            AnsiConsole.MarkupLine($"Ozan已用完,结束");
                            return 0;
                        }

                        var api = new Api(oz.DeviceCode, oz.AndroidVer, oz.AndroidModel, oz.AppVer, proxy);

                        // Check if the amount is sufficient
                        var balances = await api.Balances(oz.Token, oz.AccountHolderId);
                        if (!balances.IsSuccessStatusCode)
                        {
                            AnsiConsole.MarkupLine($"[red]Ozan验证失败![/] Ozan:{oz.Phone},Ozan无法获取余额");
                            continue;
                        }

                        var json = JsonConvert.DeserializeObject<JObject>(balances.Content);
                        var bz = json["actualBalancesGroupedByCurrency"].ToObject<JArray>();
                        if (oz.CardInfos.Count <= 0)
                        {
                            AnsiConsole.MarkupLine($"[red]Ozan无卡跳过![/] Ozan:{oz.Phone}");
                            continue;
                        }
                        var balance = bz.Where(b => b["currency"].Value<string>() == "TRY").Select(b => b["value"].Value<Single>())
                            .FirstOrDefault(0);
                        //计算余额能开通多少个会员
                        var danjia = Convert.ToSingle(tl);
                        //只取整数
                        var count = (int)(balance / danjia);
                        if (count <= 0)
                        {
                            //金额不足全部GG
                            AnsiConsole.MarkupLine($"[red]Ozan验证失败![/] Ozan:{oz.Phone},Ozan余额不足");
                            continue;
                        }
                        AnsiConsole.MarkupLine($"[green]Ozan验证成功![/] Ozan:{oz.Phone},剩余:{balance},可开:{count}");

                        //循环count次 开通成功会员
                        int dopay = 0;
                        do
                        {
                            if (dopay >= count) break;
                            var card = oz.CardInfos[new Random().Next(0, oz.CardInfos.Count)];
                            if (!clients_queue.TryDequeue(out var cli))
                            {
                                AnsiConsole.MarkupLine($"Tg已经开完,结束");
                                return 1;
                            }
                            await cli.Connect();
                            var my = (await cli.client.Users_GetUsers(new InputUserSelf()))[0] as User;
                            var url = await cli.GetPremiumPay3ds(card, settings.Annual);
                            if (string.IsNullOrEmpty(url))
                            {
                                AnsiConsole.MarkupLine($"[red]开通会员失败![/] Ozan:{oz.Phone},Tg:{my.phone},无法获取付款链接");
                                continue;
                            }
                            var response = await Web2.OzanGoPay(url, async (s) =>
                            {
                                try
                                {
                                    int i = 0;
                                    while (i++ < 30)
                                    {
                                        var notifi = await api.Notifications(oz.Token, oz.AccountHolderId);
                                        if (notifi.IsSuccessStatusCode && !string.IsNullOrEmpty(notifi.Content))
                                        {
                                            var notifiJson = JsonConvert.DeserializeObject<JObject>(notifi.Content);

                                            if (notifiJson.TryGetValue("errorCode", out var errorCode))
                                            {
                                                break; // Exit the loop if there is an error code
                                            }

                                            foreach (var _3ds in notifiJson["content"].ToObject<JArray>())
                                            {
                                                if (_3ds["notificationType"].Value<string>() == "VERIFICATION_3D")
                                                {
                                                    var content = _3ds["content"].Value<string>();
                                                    var pip = $"{s} referans nolu";

                                                    // Check if content contains pip
                                                    if (content.Contains(pip))
                                                    {
                                                        pip = $"{tl}TL'lik";
                                                        if (content.Contains(pip))
                                                        {
                                                            return _3ds["3DCode"].Value<string>();
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        await Task.Delay(3000);
                                    }
                                }
                                catch (Exception e)
                                {
                                    AnsiConsole.MarkupLine($"[red]获取3DS验证码失败![/]");
                                    AnsiConsole.WriteException(e);
                                }
                                return "";
                            }, proxy);

                            if (response.IsSuccessStatusCode && response.Content.Contains("Redirecting...") &&
                                response.Content.Contains("callbackInfo") && response.Content.Contains("window.opener"))
                            {
                                AnsiConsole.MarkupLine($"[green]开通会员成功![/] Ozan:{oz.Phone},Tg:{my.phone}");
                                await cli.StopPremium();
                                dopay++;
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[red]开通会员失败![/] Ozan:{oz.Phone},Tg:{my.phone},远程返回:{response.Content}");

                            }

                        } while (true);

                    }
                    catch (Exception e)
                    {
                       AnsiConsole.WriteException(e);
                    }
                }

            });
            return 0;
        }


        private static Dictionary<int, string> dcIp = new()
        {
            {1, "149.154.175.50"},
            {2, "149.154.167.51"},
            {3, "149.154.175.100"},
            {4, "149.154.167.91"},
            {5, "149.154.171.5"},
        };

        private async Task<TgAuth> Tdf2Auth(string path, string ApiId, string ApiHash)
        {
            TgAuth n = new TgAuth(ApiHash);
            var sson = await TdataConv.ConvertTData(path);
            var exAuto = new ExAuto
            {
                ApiId = Convert.ToInt32(ApiId)
            };
            foreach (var tuple in sson)
            {
                var dc = new ExAuto.DCSession
                {
                    Id = 1,
                    AuthKey = tuple.Item3,
                    DataCenter = new DcOption()
                    {
                        id = tuple.Item2,
                        ip_address = dcIp[tuple.Item2],
                        port = 443,
                    }
                };
                exAuto.DCSessions.Add(tuple.Item2, dc);
                exAuto.UserId = tuple.Item1;
                exAuto.MainDC = tuple.Item2;
            }


            n.ForExAuth(exAuto);
            return n;
        }

        private async Task<TgAuth> Session2Auth(string path, string ApiId, string ApiHash)
        {
            TgAuth n = new TgAuth(ApiHash);
            var sson = await TdataConv.ConvertSession(path);
            var exAuto = new ExAuto
            {
                ApiId = Convert.ToInt32(ApiId)
            };
            foreach (var tuple in sson)
            {
                var dc = new ExAuto.DCSession
                {
                    Id = 1,
                    AuthKey = tuple.Item3,
                    DataCenter = new DcOption()
                    {
                        id = tuple.Item2,
                        ip_address = dcIp[tuple.Item2],
                        port = 443,
                    }
                };
                exAuto.DCSessions.Add(tuple.Item2, dc);
                exAuto.UserId = tuple.Item1;
                exAuto.MainDC = tuple.Item2;
            }

            n.ForExAuth(exAuto);
            return n;
        }
    }
}