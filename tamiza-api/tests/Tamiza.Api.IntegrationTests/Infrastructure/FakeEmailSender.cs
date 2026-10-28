using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MimeKit;
using Tamiza.Api.Email;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>One message as the SMTP server would have taken it: the message and the addresses of its envelope.</summary>
public sealed record SentEmail(MimeMessage Message, IReadOnlyList<string> Recipients);

/// <summary>
/// Stands in for the SMTP server: it keeps every message it is given, stamped as the real sender stamps it, with the envelope MailKit
/// would build from its To, Cc and Bcc. <see cref="Fail"/> makes the next sends fail with an outcome instead.
/// </summary>
public sealed class FakeEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => [.. _sent];

    /// <summary>The outcome of the next sends; null delivers them.</summary>
    public EmailResult? Fail { get; set; }

    public Task<EmailResult> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Fail is { } failure)
        {
            return Task.FromResult(failure);
        }

        EmailStamp.Apply(message, options.Value);
        var recipients = message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Select(m => m.Address).ToList();
        _sent.Enqueue(new SentEmail(message, recipients));
        return Task.FromResult(EmailResult.Sent);
    }
}
