using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Tamiza.Api.Email;
using Step = Tamiza.Api.Email.MailKitEmailSender.Step;

namespace Tamiza.Api.UnitTests.Email;

/// <summary>The outcome of each MailKit exception, by the step of the SMTP conversation it comes from (design D1).</summary>
public sealed class MailKitEmailSenderTests
{
    private static EmailOutcome? Outcome(Step step, Exception exception) => MailKitEmailSender.Classify(step, exception)?.Outcome;

    public static TheoryData<Exception> ConnectFailures() => new()
    {
        new NotSupportedException("The SMTP server does not support the STARTTLS extension."),
        new SslHandshakeException("The remote certificate is invalid."),
        new SocketException((int)SocketError.ConnectionRefused),
        new IOException("Failed to resolve host"),
        new TimeoutException(),
        new OperationCanceledException(),
        new SmtpProtocolException("The SMTP server has unexpectedly disconnected."),
        new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.TransactionFailed, "554 no service"),
    };

    [Theory]
    [MemberData(nameof(ConnectFailures))]
    public void Anything_that_fails_while_connecting_means_the_server_is_unavailable(Exception exception) =>
        Assert.Equal(EmailOutcome.Unavailable, Outcome(Step.Connect, exception));

    [Fact]
    public void Credentials_refused() =>
        Assert.Equal(EmailOutcome.AuthFailed, Outcome(Step.Authenticate, new AuthenticationException("535 5.7.8 Authentication credentials invalid")));

    [Fact]
    public void A_server_without_authentication_while_credentials_are_set() =>
        Assert.Equal(EmailOutcome.AuthFailed, Outcome(Step.Authenticate, new NotSupportedException("The SMTP server does not support authentication.")));

    [Theory]
    [InlineData(Step.Authenticate)]
    [InlineData(Step.Send)]
    public void A_lost_connection_later_is_still_unavailable(Step step)
    {
        Assert.Equal(EmailOutcome.Unavailable, Outcome(step, new IOException("Connection reset")));
        Assert.Equal(EmailOutcome.Unavailable, Outcome(step, new TimeoutException()));
        Assert.Equal(EmailOutcome.Unavailable, Outcome(step, new SmtpProtocolException("disconnected")));
    }

    [Fact]
    public void Authentication_required() =>
        Assert.Equal(EmailOutcome.AuthFailed, Outcome(Step.Send, new ServiceNotAuthenticatedException("530 5.7.0 Authentication required")));

    [Fact]
    public void A_refused_recipient_is_named()
    {
        var refused = new MailboxAddress("", "nobody@example.org");
        var result = MailKitEmailSender.Classify(
            Step.Send, new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, refused, "550 no such user"));

        Assert.Equal(EmailOutcome.Rejected, result?.Outcome);
        Assert.Equal("nobody@example.org", result?.RefusedAddress);
        Assert.Equal("email.rejected", result?.Code);
    }

    [Theory]
    [InlineData(SmtpErrorCode.SenderNotAccepted)]
    [InlineData(SmtpErrorCode.MessageNotAccepted)]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode)]
    public void A_refused_sender_or_message_is_rejected_without_an_address(SmtpErrorCode code)
    {
        var result = MailKitEmailSender.Classify(Step.Send, new SmtpCommandException(code, SmtpStatusCode.TransactionFailed, "554 refused"));

        Assert.Equal(EmailOutcome.Rejected, result?.Outcome);
        Assert.Null(result?.RefusedAddress);
    }

    [Fact]
    public void Other_exceptions_are_not_smtp_failures()
    {
        Assert.Null(MailKitEmailSender.Classify(Step.Send, new InvalidOperationException()));
        Assert.Null(MailKitEmailSender.Classify(Step.Authenticate, new ArgumentException()));
    }

    [Theory]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    [InlineData("tls", SecureSocketOptions.SslOnConnect)]
    [InlineData("none", SecureSocketOptions.None)]
    public void Starttls_is_required_and_auto_is_never_used(string security, SecureSocketOptions expected) =>
        Assert.Equal(expected, MailKitEmailSender.SocketOptions(security));

    [Fact]
    public void Codes_of_each_outcome()
    {
        Assert.Null(EmailResult.Sent.Code);
        Assert.Equal("email.unavailable", new EmailResult(EmailOutcome.Unavailable).Code);
        Assert.Equal("email.authFailed", new EmailResult(EmailOutcome.AuthFailed).Code);
    }
}
