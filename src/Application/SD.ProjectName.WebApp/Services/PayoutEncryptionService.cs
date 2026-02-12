using System;
using Microsoft.AspNetCore.DataProtection;

namespace SD.ProjectName.WebApp.Services;

public interface IPayoutEncryptionService
{
    string Protect(string? plainText);

    string Reveal(string? protectedText);
}

public class PayoutEncryptionService : IPayoutEncryptionService
{
    private readonly IDataProtector _protector;

    public PayoutEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("payout-credentials");
    }

    public string Protect(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        return _protector.Protect(plainText.Trim());
    }

    public string Reveal(string? protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
