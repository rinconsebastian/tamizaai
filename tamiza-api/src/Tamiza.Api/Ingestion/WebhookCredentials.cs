using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Ingestion;

/// <summary>Checks the HTTP Basic credentials Kobo's REST Service sends: username <c>tamiza</c>, the project's secret.</summary>
public sealed class WebhookCredentials(TamizaDbContext db, SecretProtector secrets)
{
    /// <summary>
    /// False for a missing or malformed header, a wrong username or secret, an unknown project or a secret that can no
    /// longer be decrypted. Callers answer every case the same way, so the response never reveals which one it was.
    /// </summary>
    public async Task<bool> AreValidAsync(Guid projectId, string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (!TryParseBasic(authorizationHeader, out var username, out var password) || username != WebhookSecret.Username)
        {
            return false;
        }

        var encrypted = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.EncryptedWebhookSecret)
            .SingleOrDefaultAsync(cancellationToken);
        if (encrypted is null || !secrets.TryUnprotect(encrypted, out var secret))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Reads <c>Basic base64(username:password)</c>. The password may itself contain colons.</summary>
    public static bool TryParseBasic(string? header, out string username, out string password)
    {
        username = password = "";
        const string scheme = "Basic ";
        if (header is null || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[scheme.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var colon = decoded.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        username = decoded[..colon];
        password = decoded[(colon + 1)..];
        return true;
    }
}
