using Chubby.Core.Model;

namespace Chubby.Core.Utils;

public interface ICheckDigitStrategy
{
    string CalculateCheckDigit(ClientHandle handle, byte[] serverSecretBytes);
    bool VerifyCheckDigit(ClientHandle handle, string checkDigit, byte[] serverSecretBytes);
}
