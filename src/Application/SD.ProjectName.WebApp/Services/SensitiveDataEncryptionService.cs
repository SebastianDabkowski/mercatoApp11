using System;
using Microsoft.AspNetCore.DataProtection;

namespace SD.ProjectName.WebApp.Services;

public interface ISensitiveDataEncryptionService
{
    string Protect(string? plainText);

    string Reveal(string? protectedText);
}

public class SensitiveDataEncryptionService : ISensitiveDataEncryptionService
{
    private readonly IDataProtector _protector;

    public SensitiveDataEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("sensitive-data");
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
