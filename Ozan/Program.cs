using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ozan.Core;
using System.Runtime.Serialization;
using Ozan.Tg;
using System.Reflection;
using Standard.Licensing;
using Standard.Licensing.Validation;
using License = Standard.Licensing.License;

[assembly: Obfuscation(Feature = "encrypt symbol names with password iuyYQ76GaAuwJtnhMNM8DN8pDfqNAhVXyKe5UJ6ZjMz1iyBUnOi8S5aXhfywKvt8", Exclude = false)]
namespace Ozan
{
    internal class Program
    {
        static void EnsureDirectoryExists(string relativePath)
        {
            string targetDirectory = Path.Combine(Environment.CurrentDirectory, relativePath);

            if (!Directory.Exists(targetDirectory))
            {
                try
                {
                    Directory.CreateDirectory(targetDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建目录时发生错误：{ex.Message}");
                }
            }
 
        }
        public static string GenerateRandomAndroidPhoneModel()
        {
            string[] androidModels =
            {
                "Galaxy S21", "Galaxy A52", "Galaxy Note 20", "Galaxy Z Fold 3",
                "P40 Pro", "Mate 30", "Nova 5T",
                "Mi 11", "Redmi Note 10", "POCO X3",
                "OnePlus 9 Pro", "OnePlus Nord", "OnePlus 8T",
                "Pixel 6", "Pixel 5a", "Pixel 4a",
                "Xperia 1 III", "Xperia 5 III", "Xperia 10 III",
                "G8 ThinQ", "V60 ThinQ", "Velvet"
            };

            Random rand = new Random();
            int modelIndex = rand.Next(androidModels.Length);

            return androidModels[modelIndex];
        }

        [Obfuscation(Feature = "virtualization", Exclude = false)]
        public static License Lic;

        [Obfuscation(Feature = "virtualization", Exclude = false)]
        static async Task Main(string[] args)
        {
            EnsureDirectoryExists("Ozan");
            EnsureDirectoryExists("Tdata");
            EnsureDirectoryExists("TSession");
            var app = new CommandApp();
            app.Configure(Configuration);
            try
            {
                await app.RunAsync(args);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        [Obfuscation(Feature = "virtualization", Exclude = false)]
        private static void Configuration(IConfigurator config)
        {
            try
            {

                    config.AddCommand<OzanLoginCommand>("OzanLogin").WithAlias("ol").WithDescription("用于在当前Ozan目录生成Ozan凭据文件");
                    config.AddBranch<PaySettings>("Pay", add =>
                    {
                        add.SetDescription("Tg Premium 自动开会员,支持多个端.");
                        add.AddCommand<PremiumOzanCommand>("Ozan").WithDescription("使用Ozan提取3ds验证码自动开通会员,会随机使用可用卡号付款.");
                    });
              

            }
            catch (Exception e)
            {

            }
        }


        public sealed class OzanLoginCommand : AsyncCommand<OzanLoginCommand.Settings>
        {
            public sealed class Settings : CommandSettings
            {
                [Description("Ozan登录手机号-包含区号例子:12405526555")]
                [CommandArgument(0, "<Phone>")]
                public string Phone { get; init; }

                [Description("登录密码")]
                [CommandArgument(1, "<PassWd>")]
                public string PassWd { get; init; }

                [Description("代理文本-例子:socks5://127.0.0.1:10808,https://127.0.0.1:10809")]
                [CommandOption("-p|--proxy")]
                public string Proxy { get; init; } = "";

                [Description("模拟的设备码,设置得对可以不收验证码")]
                [CommandArgument(2, "[AppVersion]")]
                [CommandOption("-d|--deviceid")]
                public string DeviceId { get; init; } = Guid.NewGuid().ToString();

                [Description("模拟的安卓版本号")]
                [CommandOption("-v|--androidversion")]
                public int AndroidVersion { get; init; } = new Random().Next(9, 13);

                [Description("模拟的安卓型号")]
                [CommandOption("-m|--androidmodel")]
                public string AndroidModel { get; init; } = GenerateRandomAndroidPhoneModel();

                [Description("模拟的App版本号")]
                [CommandOption("-s|--appversion")]
                public string AppVersion { get; init; } = "3.2.15";


                public override ValidationResult Validate()
                {
                    //判断PassWd长度是否不等于6 是就报错
                    return PassWd.Length != 6 ? ValidationResult.Error("PassWd长度必须为6") : ValidationResult.Success();
                }
            }

            public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
            {
              return  await Run(context, settings);
            }

            [Obfuscation(Feature = "flatten", Exclude = false)]

            private async Task<int> Run(CommandContext context, Settings settings)
            {
                //判断settings.Phone是否是+开始是就去掉
                var phone = settings.Phone;
                if (phone.StartsWith("+"))
                {
                    phone = phone[1..];
                }

                Api ozan = null;
                if (string.IsNullOrEmpty(settings.Proxy))
                {
                    ozan = new Api(settings.DeviceId,
                        settings.AndroidVersion is < 9 or > 13
                            ? $"{new Random().Next(9, 13)}"
                            : $"{settings.AndroidVersion}",
                        settings.AndroidModel,settings.AppVersion);
                }
                else
                {
                    ozan = new Api(settings.DeviceId,
                        settings.AndroidVersion is < 9 or > 13
                            ? $"{new Random().Next(9, 13)}"
                            : $"{settings.AndroidVersion}",
                        settings.AndroidModel, settings.AppVersion, new WebProxy(settings.Proxy));
                }


                AnsiConsole.MarkupLine("[bold yellow]Ozan Start[/]");

                string errorStr = "";
                AnsiConsole.MarkupLine("[yellow]DeviceId[/] 验证");
                await ozan.ClientCredentials();
                var response = await ozan.UserDevice(phone);
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {

                    AnsiConsole.MarkupLine("[yellow]DeviceId[/] 验证失败");
                    response = await ozan.PreVerification(phone);
                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        AnsiConsole.MarkupLine("[yellow]手机验证码[/] 获取中");
                        //json序列化
                        var json = JsonConvert.DeserializeObject<JObject>(response.Content);
                        //判断json是否有errorCode 并且等于文本MFA_REQUIRED
                        if (json.TryGetValue("errorCode", out var errorCode) && errorCode.Value<string>() == "MFA_REQUIRED")
                        {
                            var code = AnsiConsole.Ask<string>("请输入手机获取到的[bold green]验证码[/]:");
                            var mfaToken = json["mfaToken"].Value<string>();
                            response = await ozan.PreVerificationMfa(phone, mfaToken, code);
                            if (response.IsSuccessStatusCode)
                            {
                                AnsiConsole.MarkupLine("[yellow]验证码[/]验证 [green]成功[/]");

                                //验证码正确
                                json = JsonConvert.DeserializeObject<JObject>(response.Content);
                                var access_token = json["access_token"].Value<string>();
                                response = await ozan.Password(phone, settings.PassWd, access_token);
                                if (response.IsSuccessStatusCode)
                                {
                                    AnsiConsole.MarkupLine("[yellow]密码[/]验证 [green]成功[/]");

                                    access_token = json["access_token"].Value<string>();
                                    await Export(ozan, phone, settings.PassWd, access_token);
                                    goto End;
                                }
                                errorStr = $"密码验证失败:{response.Content}";

                            }
                            else
                            {
                                errorStr = $"手机验证码失败:{response.Content}";

                            }
                        }
                        else
                        {
                            errorStr = $"未知的返回,不需要验证码？:{response.Content}";
                        }

                    }
                    else
                    {
                        errorStr = $"预授权失败？:{response.Content}";

                    }
                    AnsiConsole.MarkupLine($"发生错误 [red]{errorStr}[/]");

                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    AnsiConsole.MarkupLine("Device [green]有效[/]");
                    response = await ozan.Password(phone, settings.PassWd);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        AnsiConsole.MarkupLine("密码验证 [green]成功[/]");
                        var json = JsonConvert.DeserializeObject<JObject>(response.Content);
                        var access_token = json["access_token"].Value<string>();
                        await Export(ozan, phone, settings.PassWd, access_token);
                        goto End;
                    }
                    errorStr = $"密码验证失败:{response.Content}";
                    AnsiConsole.MarkupLine($"发生错误 [red]{errorStr}[/]");
                }
                End: 
                AnsiConsole.MarkupLine("[bold green]Ozan End[/]");
                return 0;
            }

            private async Task Export(Api ozan,string phone,string pwd,string token)
            {
                var outE = new OzanFile()
                {
                    Phone = phone,
                    PassWord = pwd,
                    Token = token,
                    DeviceCode = ozan.deviceCode,
                    AndroidVer = ozan.androidVer,
                    AndroidModel = ozan.androidModel,
                    AppVer = ozan.appVer
                };
                var str = JsonConvert.SerializeObject(outE);
                await File.WriteAllTextAsync($".\\Ozan\\{phone}.json", str);
            }


            public class OzanFile
            {
                public string Phone { get; set; }
                public string PassWord { get; set; }
                public string Token { get; set; }
                public string DeviceCode { get; set; }
                public string AndroidVer { get; set; }
                public string AndroidModel { get; set; }
                public string AppVer { get; set; }
                public string AccountHolderId { get; set; }


                public List<ICardInfo> CardInfos { get; set; } = new List<ICardInfo>();
            }
        }
    }
}