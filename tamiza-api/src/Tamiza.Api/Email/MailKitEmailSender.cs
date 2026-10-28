using System.Net.Security;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tamiza.Api.Email;

/// <summary>
/// Sends through the operator's SMTP server with MailKit, one connection per message: there are a few sends a day. STARTTLS is
/// required, never attempted (<see cref="SecureSocketOptions.Auto"/> would fall back to plain text silently), and certificates are
/// validated. Logs carry the host, the port, the step and the outcome, never the password or the server's reply text.
/// </summary>
public sealed class MailKitEmailSender(IOptions<EmailOptions> options, ILogger<MailKitEmailSender> logger) : IEmailSender
{
    /// <summary>The step of an SMTP conversation an exception comes from: the same exception type means different things in each.</summary>
    public enum Step
    {
        Connect,
        Authenticate,
        Send,
    }

    /// <summary>Tests only: trusts a test server's certificate. Production leaves it null, so the system's validation applies.</summary>
    public RemoteCertificateValidationCallback? CertificateValidation { get; init; }

    public async Task<EmailResult> SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Email is not configured.");
        }

        EmailStamp.Apply(message, settings);
        // The timeout bounds getting a usable connection as a whole (connect, TLS, sign in). While the message is sent, it bounds each
        // read and write instead (SmtpClient.Timeout), so a large attachment on a slow link is not cut off while the server answers.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        using var client = new SmtpClient { Timeout = (int)settings.Timeout.TotalMilliseconds };
        if (CertificateValidation is not null)
        {
            client.ServerCertificateValidationCallback = CertificateValidation;
        }

        var step = Step.Connect;
        try
        {
            await client.ConnectAsync(settings.Host, settings.EffectivePort, SocketOptions(settings.SecurityMode), timeout.Token);
            if (settings.HasCredentials)
            {
                step = Step.Authenticate;
                await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
            }

            step = Step.Send;
            await client.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Classify(step, exception) is { } failure)
        {
            logger.LogWarning(
                "The SMTP server {Host}:{Port} could not take a message during {Step}: {Outcome} ({Error}, status {Status}).",
                settings.Host, settings.EffectivePort, step, failure.Outcome, exception.GetType().Name, (exception as SmtpCommandException)?.StatusCode);
            return failure;
        }

        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The server has the message; a failed QUIT changes nothing for the recipients.
            logger.LogDebug("The SMTP server {Host}:{Port} did not close the connection cleanly ({Error}).", settings.Host, settings.EffectivePort, exception.GetType().Name);
        }

        return EmailResult.Sent;
    }

    public static SecureSocketOptions SocketOptions(string security) => security switch
    {
        EmailOptions.Tls => SecureSocketOptions.SslOnConnect,
        EmailOptions.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };

    /// <summary>
    /// The outcome of an exception thrown during <paramref name="step"/>, or null for one that is not an SMTP failure: the caller
    /// lets that propagate as an unexpected error. A cancellation reaching here comes from the timeout, since the caller rethrows
    /// one that comes from shutdown.
    /// </summary>
    public static EmailResult? Classify(Step step, Exception exception)
    {
        if (exception is OperationCanceledException or TimeoutException or IOException or SocketException or SmtpProtocolException)
        {
            return new EmailResult(EmailOutcome.Unavailable);
        }

        return step switch
        {
            // Greeting, EHLO and STARTTLS: whatever goes wrong, the server could not be used, including a refused greeting
            // (SmtpCommandException), no STARTTLS (NotSupportedException) and a certificate that does not validate.
            Step.Connect => new EmailResult(EmailOutcome.Unavailable),
            Step.Authenticate => exception is AuthenticationException or NotSupportedException or SmtpCommandException
                ? new EmailResult(EmailOutcome.AuthFailed)
                : null,
            _ => exception switch
            {
                // A 530 at MAIL FROM, RCPT TO or DATA: the relay wants credentials that are not set.
                ServiceNotAuthenticatedException => new EmailResult(EmailOutcome.AuthFailed),
                SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } refused =>
                    new EmailResult(EmailOutcome.Rejected, refused.Mailbox?.Address),
                SmtpCommandException => new EmailResult(EmailOutcome.Rejected),
                _ => null,
            },
        };
    }
}
