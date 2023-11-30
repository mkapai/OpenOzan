using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ozan.Core
{
    public class Core
    {



        public static string getSignatureKey(string str = "com.ozan.android")
        {
#if DEBUG
            var bytes = Encoding.UTF8.GetBytes(str);
            var hash = SHA256.Create().ComputeHash(bytes);
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLower();
            var xor = Convert.FromHexString("000003025B00055649060B5A004C04030055480E5700064B06000F090658050302550150");
            var key = new StringBuilder();
            ulong v3 = 0;
            do
            {
                ulong v6 = (ulong)hex.Length;
                var op = (v3 | v6) >> 32;
                ulong v5;
                if (op > 0)
                {
                    v5 = v3 % v6;
                }
                else
                {
                    v5 = v3 % v6;
                }
                key.Append((char)(hex[(int)v5] ^ xor[v3]));
                ++v3;

            } while (v3 != 36);
            return key.ToString();

#else

            return "";

#endif
        }


        public static string getSignature(string phone, string time)
        {
#if DEBUG
            var key = getSignatureKey();
            var signStr = $"{key}:{phone}:{time}";
            var signBytes = Encoding.UTF8.GetBytes(signStr);
            var signHash = SHA256.Create().ComputeHash(signBytes);
            var signHex = BitConverter.ToString(signHash).Replace("-", "").ToLower();
            return signHex;
#else
return NetworkAuth.getSignature(phone, time);
#endif





        }




    }
}
