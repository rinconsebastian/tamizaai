using System.Net.Mail;
using MimeKit;

namespace Tamiza.Api.Email;

/// <summary>
/// What counts as an email address for the sender and for recipients: a bare address with a domain, as members type it, that the
/// message builder can also send to. The check inviting a member uses (<c>MemberEndpoints</c>) refuses an address without a domain
/// such as <c>luis</c>, which MimeKit's parser accepts by default; MimeKit's parser refuses forms .NET accepts, such as a local part
/// ending in a dot (<c>ana.@x.org</c>). An address must pass both and come back unchanged.
/// </summary>
public static class EmailAddressRule
{
    /// <summary>The longest address an SMTP server must accept in a MAIL FROM or RCPT TO command.</summary>
    public const int MaxLength = 254;

    /// <summary>
    /// Whether <paramref name="address"/>, once trimmed, is a bare email address: no display name such as <c>Ana &lt;ana@x.org&gt;</c>,
    /// no comment, a domain, and a form MimeKit can address.
    /// </summary>
    public static bool IsValid(string? address)
    {
        var trimmed = address?.Trim() ?? "";
        return trimmed.Length is > 0 and <= MaxLength
               && MailAddress.TryCreate(trimmed, out var parsed) && parsed.Address == trimmed
               && MailboxAddress.TryParse(trimmed, out var mailbox) && mailbox.Address == trimmed;
    }
}
