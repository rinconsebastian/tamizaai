using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Tamiza.Api.Projects;

/// <summary>Encrypts the Kobo token and the webhook secret with the API's Data Protection key ring.</summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    public const string Purpose = "Tamiza.Projects.Secrets.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <summary>False when the value cannot be decrypted, for example after the key ring was lost.</summary>
    public bool TryUnprotect(string protectedValue, out string plaintext)
    {
        try
        {
            plaintext = _protector.Unprotect(protectedValue);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = "";
            return false;
        }
    }
}

public static class WebhookSecret
{
    public const string Username = "tamiza";

    /// <summary>32 random bytes, base64url-encoded.</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
