using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EFISupportApp
{
    public class Encrypt_Decrypt
    {
        string _encryptionKey=string.Empty;
        public Encrypt_Decrypt()
        {
            _encryptionKey = Constant.EncryptionKey;
        }

        public string EncryptDecrypt(string Operation, string Value, out string Result)
        {
            byte[] bytes = EncryptDecrypt(Operation, Value);
            if (Operation == "Encrypt")
                Result = Convert.ToBase64String(bytes);
            else
                Result = Encoding.UTF8.GetString(bytes);
            return Result;
        }

        private byte[] EncryptDecrypt(string Operation, string Value)
        {
            byte[] Bytes = null;
            byte[] saltBytes = new byte[] { 2, 1, 7, 3, 6, 4, 8, 5 };
            byte[] bytesToBeEncryptedDecrypted = GetStrinBytes(Operation, Value);
            byte[] passwordBytesEncryptDecrypt = Encoding.UTF8.GetBytes(_encryptionKey);
            passwordBytesEncryptDecrypt = SHA256.Create().ComputeHash(passwordBytesEncryptDecrypt);

            using (MemoryStream ms = new MemoryStream())
            {
                using (RijndaelManaged AES = new RijndaelManaged())
                {
                    AES.KeySize = 256;
                    AES.BlockSize = 128;
                    var key = new Rfc2898DeriveBytes(passwordBytesEncryptDecrypt, saltBytes, 1000);
                    AES.Key = key.GetBytes(AES.KeySize / 8);
                    AES.IV = key.GetBytes(AES.BlockSize / 8);
                    AES.Mode = CipherMode.CBC;

                    using (var cs = new CryptoStream(ms, CryptoTransform(AES, Operation), CryptoStreamMode.Write))
                    {
                        cs.Write(bytesToBeEncryptedDecrypted, 0, bytesToBeEncryptedDecrypted.Length);
                        cs.Close();
                    }
                    Bytes = ms.ToArray();
                }
            }
            return Bytes;
        }

        private byte[] GetStrinBytes(string Operation, string Value)
        {
            if (Operation == "Encrypt")
                return Encoding.UTF8.GetBytes(Value);
            else
                return Convert.FromBase64String(Value);
        }

        private ICryptoTransform CryptoTransform(RijndaelManaged AES, string Operation)
        {
            if (Operation == "Encrypt")
                return AES.CreateEncryptor();
            else
                return AES.CreateDecryptor();
        }

    }
}
