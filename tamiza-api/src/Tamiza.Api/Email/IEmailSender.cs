using MimeKit;

namespace Tamiza.Api.Email;

public enum EmailOutcome
{
    Sent,

    /// <summary>No connection, no secure connection, a server that refused the connection itself, or the timeout passed.</summary>
    Unavailable,

    /// <summary>The server refused the credentials, offers no authentication while they are set, or requires it while none are.</summary>
    AuthFailed,

    /// <summary>The server refused the sender, the message or a recipient. Nobody received the message.</summary>
    Rejected,
}

/// <summary>What happened to one message. <see cref="RefusedAddress"/> names the recipient the server refused, when it refused one.</summary>
public sealed record EmailResult(EmailOutcome Outcome, string? RefusedAddress = null)
{
    public static readonly EmailResult Sent = new(EmailOutcome.Sent);

    public bool Succeeded => Outcome == EmailOutcome.Sent;

    /// <summary>The code a failure is recorded and shown under; null for a sent message.</summary>
    public string? Code => Outcome switch
    {
        EmailOutcome.Unavailable => "email.unavailable",
        EmailOutcome.AuthFailed => "email.authFailed",
        EmailOutcome.Rejected => "email.rejected",
        _ => null,
    };
}

/// <summary>Sends one message through the operator's SMTP server. The SMTP envelope is built from the To, Cc and Bcc headers.</summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends <paramref name="message"/>, or reports why it was not sent: a failed send delivers to no one. Throws only for an
    /// unexpected error, or <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<EmailResult> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}
