using System.Text;
using Chubby.Core.Model;


namespace Chubby.Core.Utils;

public class CheckDigitCalculator
{
    private readonly byte[] _serverSecretBytes;
    private readonly ICheckDigitStrategy _strategy;

    public CheckDigitCalculator(ICheckDigitStrategy strategy)
    {
        _serverSecretBytes = Encoding.UTF8.GetBytes("ThisIsACryptoGraphicServerSecret");
        _strategy = strategy;
    }

    public string CalculateCheckDigit(ClientHandle handle)
    {
        return _strategy.CalculateCheckDigit(handle, _serverSecretBytes);
    }

    public bool VerifyCheckDigit(ClientHandle handle, string? checkDigit)
    {
        if (checkDigit is null)
        {
            return false;
        }
        return _strategy.VerifyCheckDigit(handle, checkDigit, _serverSecretBytes);
    }
}

