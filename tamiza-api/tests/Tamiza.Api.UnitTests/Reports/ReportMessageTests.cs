using MimeKit;
using Tamiza.Api.Email;
using Tamiza.Api.Reports;
using static Tamiza.Api.UnitTests.Reports.ReportSamples;

namespace Tamiza.Api.UnitTests.Reports;

public sealed class ReportMessageTests
{
    private static readonly EmailOptions Smtp = new() { Host = "smtp.example.org", From = "reports@example.org" };

    private static MimeMessage Build(ReportModel? model = null, string? replyTo = "maria@ngo.org", bool workbook = true)
    {
        var message = ReportMessage.Build(model ?? Model([Stat(1, "Total", "250")]), "<p>html</p>", "text",
            workbook ? ("census-2026_housing_2026-10-28.xlsx", new byte[] { 1, 2, 3 }) : null, ["ana@partner.org", "luis@city.gov.co"], replyTo);
        EmailStamp.Apply(message, Smtp);
        return message;
    }

    /// <summary>The SMTP envelope MailKit builds from the message: its To, Cc and Bcc mailboxes.</summary>
    private static string[] Envelope(MimeMessage message) =>
        [.. message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes).Select(m => m.Address)];

    [Fact]
    public void Recipients_hidden_from_each_other()
    {
        var message = Build();

        Assert.Equal(["ana@partner.org", "luis@city.gov.co"], message.Bcc.Mailboxes.Select(m => m.Address));
        Assert.Empty(message.To.Mailboxes);
        Assert.Empty(message.Cc);
        Assert.Equal("undisclosed-recipients: ;", message.To.ToString());
    }

    [Fact]
    public void Sender_not_a_recipient()
    {
        var message = Build();

        Assert.Equal("reports@example.org", message.From.Mailboxes.Single().Address);
        Assert.DoesNotContain("reports@example.org", Envelope(message));
        Assert.Equal(["ana@partner.org", "luis@city.gov.co"], Envelope(message));
    }

    [Fact]
    public void Replies() => Assert.Equal("maria@ngo.org", Build().ReplyTo.Mailboxes.Single().Address);

    [Fact]
    public void A_saver_without_an_email_gives_no_reply_to() => Assert.Empty(Build(replyTo: null).ReplyTo);

    [Fact]
    public void Subject() => Assert.Equal("Census 2026 – Housing – Oct 28, 2026", Build().Subject);

    [Fact]
    public void Line_breaks_in_names_never_reach_the_headers()
    {
        var message = Build(Model([Stat(1, "Total", "250")], project: "Census\r\nBcc: spy@example.org"));

        Assert.DoesNotContain('\n', message.Subject);
        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain("spy@example.org", Envelope(message));
    }

    [Fact]
    public void Html_and_text_alternatives_with_the_workbook()
    {
        var message = Build();

        Assert.Equal("<p>html</p>", message.HtmlBody);
        Assert.Equal("text", message.TextBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("census-2026_housing_2026-10-28.xlsx", attachment.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", attachment.ContentType.MimeType);
        Assert.Equal("auto-generated", message.Headers[EmailStamp.AutoSubmittedHeader]);
    }

    [Fact]
    public void Without_the_workbook_there_is_no_attachment() => Assert.Empty(Build(workbook: false).Attachments);
}
