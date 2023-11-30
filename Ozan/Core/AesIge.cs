using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ozan.Core
{
    /// <summary>
    /// Represents AES crypto provider for IGE mode
    /// </summary>
    public class AesIge
    {
        /// <summary>
        /// Stock AES engine
        /// </summary>
        private readonly Aes _aesEng;

        private byte[] _iv;
        /// <summary>
        /// Initialization vector
        /// </summary>
        public byte[] IV
        {
            get { return _iv; }
            set
            {
                _iv = value;
            }
        }
        /// <summary>
        /// Encrypt/Decrypt key size in bits
        /// </summary>
        public int KeySize
        {
            get { return _aesEng.KeySize; }
            set { _aesEng.KeySize = value; }
        }
        /// <summary>
        /// Encrypt/Decrypt key
        /// </summary>
        public byte[] Key
        {

            get { return _aesEng.Key; }
            set { _aesEng.Key = value; }
        }

        public AesIge()
        {
            _aesEng = Aes.Create();
            _aesEng.Mode = CipherMode.ECB;
            _aesEng.Padding = PaddingMode.None;
            _aesEng.KeySize = 256;
            _iv = RandomNumberGenerator.GetBytes(32);
        }

        /// <summary>
        /// Encrypt plain bytes to cipher data
        /// </summary>
        /// <param name="plainBytes">Open data for encrypt</param>
        /// <returns>Cipher data</returns>
        public byte[] Encrypt(byte[] plainBytes)
        {
            return CipherProcess(plainBytes, encryptMode: true);
        }

        /// <summary>
        /// Decrypt cipher data
        /// </summary>
        /// <param name="cipherBytes">Cipher data</param>
        /// <returns>Decrypted bytes data</returns>
        public byte[] Decrypt(byte[] cipherBytes)
        {
            return CipherProcess(cipherBytes, encryptMode: false);
        }

        /// <summary>
        /// AES IGE impl
        /// </summary>
        /// <param name="inputBytes">Input data for AES IGE</param>
        /// <param name="encryptMode">true - encrypt mode, false - decrypt mode</param>
        /// <returns>output bytes data</returns>
        private byte[] CipherProcess(byte[] inputBytes, bool encryptMode)
        {
            CheckInputByteArray(inputBytes);
            var blockSize = _aesEng.BlockSize / 8;
            var blocksCount = inputBytes.Length / blockSize;
            byte[] outputBytes = CreateBlocks(blocksCount);

            var cryptoTransformer = encryptMode ? _aesEng.CreateEncryptor() : _aesEng.CreateDecryptor();

            byte[] iv1 = ExtractBlock(IV, encryptMode ? 0 : 1);
            byte[] iv2 = ExtractBlock(IV, encryptMode ? 1 : 0);

            byte[] inputBlock;
            byte[] outputBlock = CreateBlock();

            for (int i = 0; i < blocksCount; i++)
            {
                inputBlock = ExtractBlock(inputBytes, i);

                XorBlocks(inputBlock, iv1, outputBlock);
                cryptoTransformer.TransformBlock(outputBlock, inputOffset: 0, inputCount: outputBlock.Length, outputBlock, outputOffset: 0);
                XorBlocks(outputBlock, iv2, outputBlock);

                CopyBlock(outputBlock, outputBytes, i);

                iv1 = outputBlock;
                iv2 = inputBlock;
            }

            return outputBytes;
        }

        private void CheckInputByteArray(byte[] byteArray)
        {
            if (byteArray == null)
            {
                throw new NullReferenceException("Input byte array can't be NULL");
            }
            if (byteArray.Length % (_aesEng.BlockSize / 8) != 0)
            {
                throw new ArgumentException("Input byte array length must be a multiple of the block size!");
            }
        }

        private byte[] ExtractBlock(byte[] bytes, int blockPosition)
        {
            var blockSize = _aesEng.BlockSize / 8;
            byte[] blockBytes = new byte[blockSize];
            Array.Copy(bytes, sourceIndex: blockPosition * blockSize, blockBytes, destinationIndex: 0, length: blockBytes.Length);
            return blockBytes;
        }

        private void CopyBlock(byte[] block, byte[] resultBytes, int blockPosition)
        {
            var blockSize = _aesEng.BlockSize / 8;
            Array.Copy(block, sourceIndex: 0, resultBytes, blockPosition * blockSize, blockSize);
        }

        private byte[] CreateBlock()
        {
            return CreateBlocks(blocksCount: 1);
        }

        private byte[] CreateBlocks(int blocksCount)
        {
            var blockSize = _aesEng.BlockSize / 8;
            return new byte[blockSize * blocksCount];
        }

        private void XorBlocks(byte[] block1, byte[] block2, byte[] resultBlock)
        {
            var blockSize = _aesEng.BlockSize / 8;
            for (int i = 0; i < blockSize; i++)
            {
                resultBlock[i] = (byte)(block1[i] ^ block2[i]);
            }
        }
    }
}
