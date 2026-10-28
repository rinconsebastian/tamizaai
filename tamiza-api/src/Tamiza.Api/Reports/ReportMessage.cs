using MimeKit;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Reports;

/// <summary>
/// The message of a scheduled email (design D12). Every recipient is in Bcc; To is an empty group, so that the sender address is not an
/// SMTP recipient (MailKit builds the envelope from To, Cc and Bcc) and needs no mailbox. The sender, the date, the message ID and the
/// mark of an automatic message are stamped when it is sent (<c>EmailStamp</c>).
/// </summary>
public static class ReportMessage
{
    public static MimeMessage Build(
        ReportModel model, string html, string text, (string FileName, byte[] Content)? workbook, IEnumerable<string> recipients, string? replyTo)
    {
        var message = new MimeMessage();
        message.To.Add(new GroupAddress("undisclosed-recipients"));
        foreach (var recipient in recipients)
        {
            message.Bcc.Add(MailboxAddress.Parse(recipient));
        }

        if (!string.IsNullOrWhiteSpace(replyTo) && MailboxAddress.TryParse(replyTo, out var reply))
        {
            message.ReplyTo.Add(reply);
        }

        // Names members wrote travel in the subject: no line break may start another header.
        message.Subject = model.Subject.Replace('\r', ' ').Replace('\n', ' ');
        var body = new BodyBuilder { HtmlBody = html, TextBody = text };
        if (workbook is { } file)
        {
            body.Attachments.Add(file.FileName, file.Content, ContentType.Parse(DataDashboard.XlsxContentType));
        }

        message.Body = body.ToMessageBody();
        return message;
    }
}
