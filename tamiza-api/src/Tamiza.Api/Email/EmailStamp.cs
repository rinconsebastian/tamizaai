using MimeKit;
using MimeKit.Utils;

namespace Tamiza.Api.Email;

/// <summary>What every message Tamiza sends carries, whichever feature built it: the configured sender, a date, a message ID and the
/// mark of an automatic message, so that out-of-office replies are not sent back.</summary>
public static class EmailStamp
{
    public const string AutoSubmittedHeader = "Auto-Submitted";

    public static void Apply(MimeMessage message, EmailOptions settings)
    {
        message.From.Clear();
        message.From.Add(new MailboxAddress(settings.SenderName, settings.From));
        message.Date = DateTimeOffset.UtcNow;
        message.MessageId = MimeUtils.GenerateMessageId(settings.From[(settings.From.LastIndexOf('@') + 1)..]);
        message.Headers.Replace(AutoSubmittedHeader, "auto-generated");
    }
}
