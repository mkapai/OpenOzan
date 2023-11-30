using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DeviceId;
using Newtonsoft.Json;
using RestSharp;

namespace Ozan.Core
{
    public class NetworkAuth
    {

        [Obfuscation(Feature = "virtualization", Exclude = false)]

        public static string getSignature(string phone, string time)
        {
            var lic = Program.Lic;
            var id =lic.Id.ToString();
            //加密
            var currentDeviceId = new DeviceIdBuilder()
                .AddMachineName()
                .AddMacAddress(excludeWireless: true)
                .ToString();
            var body = Encrypt(JsonConvert.SerializeObject(new { deviceId = currentDeviceId ,phone,time}), lic.ProductFeatures.Get("remote"));
            var client = new RestClient(Program.Lic.ProductFeatures.Get("host"));
            var req = new RestRequest("/api/go/getSignature", Method.Post);
            req.AddParameter("user_id",id);
            req.AddParameter("body", body);
            var response =  client.Execute(req);
            if (response.IsSuccessStatusCode)
            {
                return Decrypt(response.Content, lic.ProductFeatures.Get("remote"));
            }
            return "";
        }



        [Obfuscation(Feature = "virtualization", Exclude = false)]

        public static Tuple<string,string> getTl()
        {
            var id = Program.Lic.Id.ToString();
            //加密
            var currentDeviceId = new DeviceIdBuilder()
                .AddMachineName()
                .AddMacAddress(excludeWireless: true)
                .ToString();
            var body = Encrypt(JsonConvert.SerializeObject(new { deviceId = currentDeviceId }), Program.Lic.ProductFeatures.Get("remote"));
            var client = new RestClient(Program.Lic.ProductFeatures.Get("host"));
            var req = new RestRequest("/api/go/getTl", Method.Post);
            req.AddParameter("user_id", id);
            req.AddParameter("body", body);
            var response = client.Execute(req);
            if (response.IsSuccessStatusCode)
            {
                var str = Decrypt(response.Content, Program.Lic.ProductFeatures.Get("remote"));
                //分割str
                var strs = str.Split(',');
                return new Tuple<string, string>(strs[0], strs[1]);
            }
            return new("","");
        }        
        
        
        [Obfuscation(Feature = "virtualization", Exclude = false)]

        public static long getAuthKeyID(byte[] haxi)
        {
            var lic = Program.Lic;
            var id = lic.Id.ToString();
            //加密
            var currentDeviceId = new DeviceIdBuilder()
                .AddMachineName()
                .AddMacAddress(excludeWireless: true)
                .ToString();
            var body = Encrypt(JsonConvert.SerializeObject(new { deviceId = currentDeviceId, hash=Convert.ToHexString(haxi) }), lic.ProductFeatures.Get("remote"));
            var client = new RestClient(Program.Lic.ProductFeatures.Get("host"));
            var req = new RestRequest("/api/go/getAuthKeyID", Method.Post);
            req.AddParameter("user_id", id);
            req.AddParameter("body", body);
            var response = client.Execute(req);
            if (response.IsSuccessStatusCode)
            {
                var str = Decrypt(response.Content, lic.ProductFeatures.Get("remote"));
                return Convert.ToInt64(str);
            }

            return 0;
        }


       public static string Encrypt(string data, string key)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = Convert.FromHexString(key);
            aesAlg.IV = MD5.HashData(aesAlg.Key);
            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
            using MemoryStream msEncrypt = new MemoryStream();
            using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(data);
                }
            }
            return Convert.ToHexString(msEncrypt.ToArray());
        }



       public static string Decrypt(string data, string key)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = Convert.FromHexString(key);
            aesAlg.IV = MD5.HashData(aesAlg.Key);
            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
            using MemoryStream msDecrypt = new MemoryStream(Convert.FromHexString(data));
            using CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using StreamReader srDecrypt = new StreamReader(csDecrypt);
            return srDecrypt.ReadToEnd();
        }
    }
}
