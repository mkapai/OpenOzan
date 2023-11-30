using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Ozan.Core;
using WTelegram;

namespace Ozan.Tg
{
    public class TgAuth : Stream
    {
        private byte[] _data = Array.Empty<byte>();
        private string session_key;
        public TgAuth(string key)
        {
            session_key = key;
        }


        public TgAuth ForPath(string path)
        {
            var data = File.ReadAllText(path);
            return ForJsonString(data);
        }

        public TgAuth ForJsonString(string json)
        {
            json = repair(json);
            encode(Encoding.UTF8.GetBytes(json));
            return this;
        }

        [Obfuscation(Feature = "flatten", Exclude = false)]

        public TgAuth ForExAuth(ExAuto exAuto)
        {

            foreach (KeyValuePair<int, ExAuto.DCSession> dc in exAuto.DCSessions)
            {
                var sha1 = SHA1.Create();
                var authKeyHash = sha1.ComputeHash(dc.Value.AuthKey);
                if (dc.Value.AuthKeyID == 0)
                {


                    dc.Value.AuthKeyID = BinaryPrimitives.ReadInt64LittleEndian(authKeyHash.AsSpan(12));


                }

                if (dc.Value.Id == 0)
                {
                    dc.Value.Id = dc.Value.AuthKeyID;
                }
            }
            var json = JsonConvert.SerializeObject(exAuto);
            encode(Encoding.UTF8.GetBytes(json));
            return this;
        }




        private string repair(string str)
        {
            var exAuto = JsonConvert.DeserializeObject<ExAuto>(str);
            foreach (KeyValuePair<int, ExAuto.DCSession> dc in exAuto.DCSessions)
            {
                var sha1 = SHA1.Create();
                var authKeyHash = sha1.ComputeHash(dc.Value.AuthKey);
                if (dc.Value.AuthKeyID == 0)
                {
                    dc.Value.AuthKeyID = BinaryPrimitives.ReadInt64LittleEndian(authKeyHash.AsSpan(12));
                }
            }
            return JsonConvert.SerializeObject(exAuto);
        }

        private void encode(byte[] data)
        {
            var rgbKey = Convert.FromHexString(session_key);
            using var _sha256 = SHA256.Create();
            using var aes = System.Security.Cryptography.Aes.Create();
            //随机生成个IV
            var iv = data[0..16];
            var _encryptor = aes.CreateEncryptor(rgbKey, iv);
            int encryptedLen = 64 + (data.Length & ~15);
            var hash_bytes = new byte[32];
            _encryptor.TransformBlock(_sha256.ComputeHash(data, 0, data.Length), 0, 32, hash_bytes, 0);
            var data_bytes = new byte[encryptedLen - 64];
            _encryptor.TransformBlock(data, 0, encryptedLen - 64, data_bytes, 0);
            var encrypted = _encryptor.TransformFinalBlock(data, encryptedLen - 64, data.Length & 15);
            _data = iv.Concat(hash_bytes).Concat(data_bytes).Concat(encrypted).ToArray();
        }

        public byte[] uncode()
        {
            var input = _data;
            var rgbKey = Convert.FromHexString(session_key);
            using var aes = System.Security.Cryptography.Aes.Create();
            using var sha256 = SHA256.Create();
            using var decryptor = aes.CreateDecryptor(rgbKey, input[0..16]);
            var utf8Json = decryptor.TransformFinalBlock(input, 16, input.Length - 16);
            if (!sha256.ComputeHash(utf8Json, 32, utf8Json.Length - 32).SequenceEqual(utf8Json[0..32]))
                throw new WTException("Integrity check failed in session loading");
            return utf8Json[32..];
        }


        public override int Read(byte[] buffer, int offset, int count)
        {
            var len = Math.Min(count, _data.Length);
            if(len == 0) return 0;
            Array.Copy(_data, 0, buffer, offset, len);
            _data = _data.Skip(len).ToArray();
            return len;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            //往_data 累加数据
            var wte = buffer.Skip(offset).Take(count).ToArray();
           // uncode(wte);
           _data = wte;
        }


        public override string ToString()
        {
            return Encoding.UTF8.GetString(uncode());
        }

        public override int GetHashCode()
        {
            return _data.GetHashCode();
        }

        public override long Length => _data.Length;
        public override long Position { get => 0; set { } }
        public override bool CanSeek => false;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Flush() { }




    }

    public class ExAuto
    {
        public int ApiId;
        public long UserId;
        public int MainDC;
        public Dictionary<int, DCSession> DCSessions = new();
        public TL.DcOption[] DcOptions;

        public class DCSession
        {
            public long Id;
            public long AuthKeyID;
            public byte[] AuthKey;      // 2048-bit = 256 bytes
            public long UserId;
            public long Salt;
            public SortedList<DateTime, long> Salts;
            public int Seqno;
            public long ServerTicksOffset;
            public long LastSentMsgId;
            public TL.DcOption DataCenter;
            public bool WithoutUpdates;
        }
    }
}
