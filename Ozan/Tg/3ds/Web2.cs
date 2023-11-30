using AngleSharp;
using AngleSharp.Html.Parser;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using AngleSharp.Dom;

namespace Ozan.Tg._3ds
{
    public static class Web2
    {
        private static HtmlParser htmlParser = new HtmlParser();










        public static async Task<RestResponse> OzanGoPay(string url,  Func<string,Task<string>> getCodeFunc, IWebProxy prox = null)
        {
            //判断url host 是否是 smart-glocal.com
            if (url.IndexOf("smart-glocal.com") == -1)
            {
                var res = new RestResponse
                {
                    Content = "url host 不是 smart-glocal.com,暂时不支持其他域",
                    StatusCode = HttpStatusCode.NotFound
                };
                return res;
            }

           
            var go = await AutoForm( url, async (res, req, dic) =>
            {
                //打印来源res网站
                //打印提交req网站
                if (req.Resource == "https://3d.payten.com.tr/mdpaympi/MerchantServer")
                {
                    foreach (var nv in dic)
                    {
                        var name = nv.Key;
                        var val = name switch
                        {
                            "TDS2_Navigator_language" => "en",
                            "TDS2_Navigator_jsEnabled" => "true",
                            "TDS2_Navigator_javaEnabled" => "false",
                            "TDS2_Screen_colorDepth" => "24",
                            "TDS2_Screen_height" => "1920",
                            "TDS2_Screen_width" => "1080",
                            "TDS2_Screen_pixelDepth" => "24",
                            "TDS2_TimezoneOffset" => "0",
                            _ => nv.Value
                        };
                        req.AddOrUpdateParameter(name, val);
                    }
                }

                //判断是否是3d支付页面 如果是就等待验证码然后提交
                if (req.Resource == "https://inter3ds.intertech.com.tr/Inter3DS/Defaultv2.aspx")
                {
                    if (dic.Keys.Any(o => o == "HiddenReferenceNumber") && dic.Keys.Any(o => o == "TextBoxCode") && dic.Keys.Any(o => o == "HiddenDurationText"))
                    {
                        //设置TextBoxCode

                        string durationText = dic["HiddenDurationText"];//durationText格式为 02:59 剩余分钟:剩余秒数
                        string referenceNumber = dic["HiddenReferenceNumber"];
                        //保存当前时间 好计算新的durationText
                        var startTime = DateTime.Now;
                        string code = await getCodeFunc.Invoke(referenceNumber);
                        if (string.IsNullOrEmpty(code))
                        {
                            return -1;
                        }
                        //计算新的durationText
                        var endTime = DateTime.Now;
                        var duration = endTime - startTime;
                        var newDurationText = durationText.Split(':');
                        var newDuration =
                            new TimeSpan(int.Parse(newDurationText[0]), int.Parse(newDurationText[1]), 0) -
                            duration;
                        durationText = newDuration.Minutes + ":" + newDuration.Seconds;
                        foreach (var nv in dic)
                        {
                            var name = nv.Key;
                            var val = name switch
                            {
                                "TextBoxCode" => code,
                                "HiddenDurationText" => durationText,
                                _ => nv.Value
                            };
                            req.AddOrUpdateParameter(name, val);
                        }

                        //随机生成ButtonSend.x ButtonSend.y
                        var random = new Random();
                        req.AddOrUpdateParameter("ButtonSend.x", random.Next(0, 500));
                        req.AddOrUpdateParameter("ButtonSend.y", random.Next(0, 500));
                    }
                }
                return 0;
            }, prox);
            return go;

        }


        


        private static async Task<RestResponse> AutoForm(string url,Func<RestResponse, RestRequest, IDictionary<string, string>, Task<int>> func, IWebProxy proxy = null)
        {
            var options = new RestClientOptions
            {
                UserAgent = "Mozilla/5.0 (Linux; Android 10; Redmi K30 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.210 Mobile Safari/537.36",
                Proxy = proxy,
                MaxTimeout = -1,
                Expect100Continue = false,
                CookieContainer = new CookieContainer()
            };
            var headers = new Dictionary<string, string>
            {
                { "Accept-Language", "en" }
            };
            var client = new RestClient(options);
            client.AddDefaultHeaders(headers);
            var request = new RestRequest(url);
            var response = await client.ExecuteAsync(request);
            do
            {
                if (response.IsSuccessStatusCode)
                {
                    var document = htmlParser.ParseDocument(response.Content);
                    var form = document.All
                        .OfType<IHtmlFormElement>()
                        .FirstOrDefault();
                    if (form != null)
                    {
                        var (action, method, formFields) = await AnalysisForm(form);
                        action = GetUrl(response.ResponseUri, action);
                        request = new RestRequest(action, method == "get" ? Method.Get : Method.Post);
                        foreach (var kv in formFields)
                        {
                            request.AddParameter(kv.Key, kv.Value);
                        }
                        
                        switch (await func.Invoke(response, request, formFields))
                        {
                            case -1:
                                var ex = new RestResponse();
                                ex.Content = "没有获取到3DS验证码!";
                                ex.StatusCode = HttpStatusCode.BadGateway;
                                return ex;
                            
                        }
                        response = await client.ExecuteAsync(request);
                    }
                    else
                    {
                        //没有form表单了结束了
                        return response;
                    }
                }
                else
                {
                    //状态不对结束了
                    return response;
                }
                //随机等待1-3秒
                await Task.Delay(new Random().Next(500, 2000));
            } while (true);

        }








        private static string GetUrl(Uri uri,string action)
        {
            if (action.StartsWith("/"))
            {
                action = uri.Scheme + "://" + uri.Host + action;
            }
            else if (action.StartsWith("./"))
            {
                action = uri.Scheme + "://" + uri.Host + uri.AbsolutePath.Replace(uri.AbsolutePath.Split('/').Last(), "") + action.Replace("./", "");
            }
            else if (action.StartsWith("../"))
            {
                action = uri.Scheme + "://" + uri.Host + uri.AbsolutePath.Replace(uri.AbsolutePath.Split('/').Last(), "") + action.Replace("../", "");
            }
            else if (action.StartsWith("http"))
            {
                //不用管
            }
            else
            {
                action = uri.Scheme + "://" + uri.Host + uri.AbsolutePath.Replace(uri.AbsolutePath.Split('/').Last(), "") + action;
            }
            return action;
        }



        private static async Task<Tuple<string,string,IDictionary<string,string>>> AnalysisForm(IHtmlFormElement form)
        {
            var action = form.Action;


            var method = form.Method.ToLower();
            // 获取表单中所有提交的数据
            var formData = new ConcurrentDictionary<string, string>();
            foreach (var formControl in form.GetElementsByTagName("textarea").Concat(form.GetElementsByTagName("input")).Concat(form.GetElementsByTagName("select")))
            {
                // 尝试获取 name 属性，如果不存在，不写入
                var fieldName = formControl.GetAttribute("name");

                // 添加到 formData 字典 name不能为空
                if (!string.IsNullOrEmpty(fieldName))
                {
                    // 获取字段值
                    string fieldValue = "";

                    if (formControl is IHtmlInputElement inputElement)
                    {
                        // 处理 input 元素，包括各种类型的 input
                        switch (inputElement.Type.ToLower())
                        {
                            case "text":
                            case "password":
                            case "hidden":
                            case "email":
                            case "number":
                            case "search":
                            case "tel":
                            case "url":
                                fieldValue = inputElement.Value;
                                break;

                            case "checkbox":
                                fieldValue = inputElement.IsChecked ? "on" : "";
                                break;

                            case "radio":
                                // 只添加选中的 radio 按钮的值
                                if (inputElement.IsChecked)
                                {
                                    fieldValue = inputElement.Value;
                                }
                                break;

                                // 处理其他类型的 input，如果有需要，请根据需要添加更多的 case
                        }
                    }
                    else if (formControl is IHtmlTextAreaElement textAreaElement)
                    {
                        // 处理 textarea 元素
                        fieldValue = textAreaElement.Value;
                    }
                    else if (formControl is IHtmlSelectElement selectElement)
                    {
                        // 处理 select 元素，获取选中的 option 的值
                        var selectedOptions = selectElement.Options.Where(o => o.HasAttribute("selected"));
                        fieldValue = selectedOptions.Aggregate(fieldValue, (current, option) => current + (option.Value + ","));
                        fieldValue = fieldValue.TrimEnd(',');
                    }
                    formData.AddOrUpdate(fieldName, fieldValue, (_, _) => fieldValue);
                }
            }

            return new (action, method, formData);

        }
    }

}
