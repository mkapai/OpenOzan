using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ozan.Core;

namespace Ozan.Tg
{
    public class TdataConv
    {
        public static QDataStream ReadFile(string name)
        {
            using var fileStream = new FileStream(name, FileMode.Open, FileAccess.Read);
            var magic = new byte[4];
            fileStream.Read(magic, 0, 4);
            if (!Encoding.UTF8.GetString(magic).Equals("TDF$"))
            {
                throw new Exception("Invalid magic");
            }

            var versionBytes = new byte[4];
            fileStream.Read(versionBytes, 0, 4);
            var dataLen = (int) fileStream.Length - 8 - 16;
            var data = new byte[dataLen];
            fileStream.Read(data, 0, dataLen);
            var digest = new byte[16];
            fileStream.Seek(-16, SeekOrigin.End);
            fileStream.Read(digest, 0, 16);
            var md5 = MD5.Create();
            md5.TransformBlock(data, 0, data.Length, null, 0);
            md5.TransformBlock(BitConverter.GetBytes(dataLen), 0, 4, null, 0);
            md5.TransformBlock(versionBytes, 0, 4, null, 0);
            md5.TransformFinalBlock(magic, 0, 4);
            if (!digest.SequenceEqual(md5.Hash))
            {
                throw new Exception("Invalid digest");
            }

            return new QDataStream(data);
        }

        private static byte[] AesDecryptLocal(byte[] ciphertext, byte[] authKey, byte[] key128)
        {
            var (key, iv) = PrepareAesOldMtp(authKey, key128, false);
            return DecryptIGE(ciphertext, key, iv);
        }

        private static byte[] DecryptIGE(byte[] ciphertext, byte[] key, byte[] iv)
        {
            var i = new AesIge
            {
                IV = iv,
                Key = key,
            };
            return i.Decrypt(ciphertext);
        }

        private static QDataStream DecryptLocal(byte[] data, byte[] key)
        {
            var encryptedKey = data.Take(16).ToArray();
            var decryptedData = AesDecryptLocal(data.Skip(16).ToArray(), key, encryptedKey);
            var sha1 = SHA1.Create();
            sha1.TransformFinalBlock(decryptedData, 0, decryptedData.Length);

            if (!encryptedKey.SequenceEqual(sha1.Hash.Take(16).ToArray()))
            {
                throw new Exception("Failed to decrypt");
            }

            int length = BitConverter.ToInt32(decryptedData.Take(4).ToArray(), 0);
            var resultData = decryptedData.Skip(4).Take(length).ToArray();
            return new QDataStream(resultData);
        }

        private static  (byte[], byte[]) PrepareAesOldMtp(byte[] authKey, byte[] msgKey, bool send)
        {
            var x = send ? 0 : 8;

            using var sha1A = SHA1.Create();
            using var sha1B = SHA1.Create();
            using var sha1C = SHA1.Create();
            using var sha1D = SHA1.Create();
            sha1A.TransformBlock(msgKey, 0, msgKey.Length, null, 0);
            sha1A.TransformFinalBlock(authKey, x, 32);
            var a = sha1A.Hash;

            sha1B.TransformBlock(authKey, 32 + x, 16, null, 0);
            sha1B.TransformBlock(msgKey, 0, msgKey.Length, null, 0);
            sha1B.TransformFinalBlock(authKey, 48 + x, 16);
            var b = sha1B.Hash;

            sha1C.TransformBlock(authKey, 64 + x, 32, null, 0);
            sha1C.TransformFinalBlock(msgKey, 0, msgKey.Length);
            var c = sha1C.Hash;

            sha1D.TransformBlock(msgKey, 0, msgKey.Length, null, 0);
            sha1D.TransformFinalBlock(authKey, 96 + x, 32);
            var d = sha1D.Hash;

            var key = ConcatenateArrays(a.Take(8).ToArray(), b.Skip(8).ToArray(), c.Skip(4).Take(12).ToArray());
            var iv = ConcatenateArrays(a.Skip(8).ToArray(), b.Take(8).ToArray(), c.Skip(16).ToArray(),
                d.Take(8).ToArray());

            return (key, iv);
        }


        private static byte[] CreateLocalKey(string passcode, byte[] salt)
        {
            var iterations = passcode.Length > 0 ? 100000 : 1;
            using var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(ConcatenateArrays(salt, Encoding.UTF8.GetBytes(passcode), salt));
            using var rfc2898 = new Rfc2898DeriveBytes(hash, salt, iterations, HashAlgorithmName.SHA512);
            return rfc2898.GetBytes(256);
        }

        private static byte[] ConcatenateArrays(params byte[][] arrays)
        {
            using var stream = new MemoryStream();
            foreach (var array in arrays)
            {
                stream.Write(array, 0, array.Length);
            }

            return stream.ToArray();
        }


        private static QDataStream ReadEncryptedFile(string name, byte[] key)
        {
            var stream = ReadFile(name);
            var encryptedData = stream.ReadBuffer();
            return DecryptLocal(encryptedData, key);
        }

        private static (long,int, byte[]) ReadUserAuth(string directory, byte[] localKey, int index)
        {
            var name = AccountDataString(index);
            var path = Path.Combine(directory, $"{name}s");
            var stream = ReadEncryptedFile(path, localKey);

            if (stream.ReadInt32() != 0x4B)
            {
                throw new Exception("Unsupported user auth config");
            }

            var subStream = new QDataStream(stream.ReadBuffer());

            long userId = subStream.ReadUInt32();
            var mainDc = subStream.ReadUInt32();

            if (userId == 0xFFFFFFFF && mainDc == 0xFFFFFFFF)
            {
                userId = subStream.ReadUInt64();
                mainDc = subStream.ReadUInt32();
            }


            var length = subStream.ReadInt32();
            for (int i = 0; i < length; i++)
            {
                var authDc = subStream.ReadInt32();
                var authKey = subStream.Read(256);

                if (authDc == mainDc)
                {
                    return (userId,authDc, authKey);
                }
            }

            throw new Exception("Invalid user auth config");
        }

        private static string AccountDataString(int index = 0)
        {
            var s = "data";
            if (index > 0)
            {
                s += $"#{index + 1}";
            }

            using var md5 = MD5.Create();
            var digest = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
            var reversedDigest = digest.Take(8).Reverse().ToArray();
            var str = Convert.ToHexString(reversedDigest).ToUpper().Reverse().ToArray();
            return new string(str);
        }
        [Obfuscation(Feature = "code control flow obfuscation", Exclude = false)]


        public static async Task<Tuple<long,int, byte[]>[]> ConvertTData(string path)
        {
            var stream = ReadFile(Path.Combine(path, "key_datas"));
            var salt = stream.ReadBuffer();
            if (salt.Length != 32)
            {
                throw new Exception("Invalid salt length");
            }

            var keyEncrypted = stream.ReadBuffer();
            var infoEncrypted = stream.ReadBuffer();

            var passcodeKey = CreateLocalKey("", salt);
            var keyInnerData = DecryptLocal(keyEncrypted, passcodeKey);
            var localKey = keyInnerData.Read(256);
            if (localKey.Length != 256)
            {
                throw new Exception("Invalid local key");
            }

            var infoData = DecryptLocal(infoEncrypted, localKey);
            var count = infoData.ReadInt32();
            var authKeys = new Tuple<long,int, byte[]>[count];

            for (int i = 0; i < count; i++)
            {
                var index = infoData.ReadInt32();
                var (userId,dc, key) = ReadUserAuth(path, localKey, index);
                authKeys[i] = new(userId,dc, (key));
            }
            return authKeys;
        }
        [Obfuscation(Feature = "code control flow obfuscation", Exclude = false)]

        public static async Task<Tuple<long, int, byte[]>[]> ConvertSession(string path)
        {
            var connectionString = $"Data Source={path}";
            await using SQLiteConnection connection = new SQLiteConnection(connectionString);
            await connection.OpenAsync();
            string query = "SELECT * FROM sessions LIMIT 1;";
            await using SQLiteCommand command = new SQLiteCommand(query, connection);
            await using SQLiteDataReader reader = command.ExecuteReader();
            // Check if there is at least one row
            if (reader.Read())
            {
                // Access data from the first row
                int dc_id = Convert.ToInt32(reader["dc_id"]);
                string server_address = reader["server_address"].ToString();
                int port = Convert.ToInt32(reader["port"]);
                var auth_key = reader["auth_key"] as byte[];

                return new[] {new Tuple<long, int, byte[]>(0, dc_id, auth_key)};
            }
            throw new Exception("Invalid session");
        }

        public class QDataStream
        {
            private readonly MemoryStream stream;

            public QDataStream(byte[] data)
            {
                stream = new MemoryStream(data);
            }

            public byte[]? Read(int n)
            {
                n = Math.Max(n, 0);
                var data = new byte[n];
                int bytesRead = stream.Read(data, 0, n);

                if (n != 0 && bytesRead == 0)
                {
                    return null;
                }

                if (bytesRead != n)
                {
                    throw new Exception("unexpected eof");
                }

                return data;
            }

            public byte[]? ReadBuffer()
            {
                var lengthBytes = Read(4);
                if (lengthBytes == null)
                {
                    return null;
                }
                if (BitConverter.IsLittleEndian)
                {
                    lengthBytes = lengthBytes.Reverse().ToArray();
                }
                int length = BitConverter.ToInt32(lengthBytes, 0);
                var data = Read(length);
                if (data == null)
                {
                    throw new Exception("unexpected eof");
                }
                return data;
            }

            public int ReadInt32()
            {
                var data = Read(4);
                if (BitConverter.IsLittleEndian)
                {
                    data = data?.Reverse().ToArray();
                }

                return BitConverter.ToInt32(data, 0);
            }

            public long ReadUInt64()
            {
                var data = Read(8);
                if (BitConverter.IsLittleEndian)
                {
                    data = data?.Reverse().ToArray();
                }

                return BitConverter.ToInt64(data, 0);
            }

            public uint ReadUInt32()
            {
                var data = Read(4);
                if (BitConverter.IsLittleEndian)
                {
                    data = data?.Reverse().ToArray();
                }

                return BitConverter.ToUInt32(data, 0);
            }
        }
    }
}