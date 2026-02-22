using System.Security.Cryptography;
using System.Text;
using System.IO;
using System;
using Chubby.Core.Model;

namespace Chubby.Core.Utils
{
    public class HmacCheckDigitStrategy : ICheckDigitStrategy
    {
        public string CalculateCheckDigit(ClientHandle handle, byte[] serverSecretBytes)
        {
            using (var hmac = new HMACMD5(serverSecretBytes))
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(handle.HandleId);
                    writer.Write(handle.Path);
                    writer.Write(handle.InstanceNumber);
                    writer.Write((int)handle.Permission);
                    writer.Write((uint)handle.SubscribedEventsMask);
                    stream.Position = 0;
                    byte[] hash = hmac.ComputeHash(stream);
                    return Convert.ToBase64String(hash);
                }
            }
        }

        public bool VerifyCheckDigit(ClientHandle handle, string checkDigit, byte[] serverSecretBytes)
        {
            string expectedCheckDigit = CalculateCheckDigit(handle, serverSecretBytes);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedCheckDigit), Encoding.UTF8.GetBytes(checkDigit));
        }
    }
}
