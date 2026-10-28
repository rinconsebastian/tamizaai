using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tamiza.Api.Email;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Email;

/// <summary>
/// The real sender against Mailpit, one container per case configured as the case needs (design D16): what the server took is read
/// back through Mailpit's API, so "no one received it" is checked, not assumed.
/// </summary>
public sealed class MailKitEmailSenderTests
{
    private const string Image = "axllent/mailpit:v1.31.5";
    private const string SenderAddress = "reports@example.org";

    private readonly CapturingLoggerProvider _logs = new();

    private static async Task<IContainer> MailpitAsync(Dictionary<string, string>? environment = null, (string Cert, string Key)? tls = null)
    {
        var builder = new ContainerBuilder(Image)
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")));
        foreach (var (name, value) in environment ?? [])
        {
            builder = builder.WithEnvironment(name, value);
        }

        if (tls is { } pair)
        {
            builder = builder
                .WithResourceMapping(Encoding.ASCII.GetBytes(pair.Cert), "/certs/cert.pem")
                .WithResourceMapping(Encoding.ASCII.GetBytes(pair.Key), "/certs/key.pem")
                .WithEnvironment("MP_SMTP_TLS_CERT", "/certs/cert.pem")
                .WithEnvironment("MP_SMTP_TLS_KEY", "/certs/key.pem");
        }

        var container = builder.Build();
        await container.StartAsync();
        return container;
    }

    /// <summary>A self-signed certificate for <paramref name="host"/>, as PEM, and its thumbprint.</summary>
    private static (string Cert, string Key, string Thumbprint) Certificate(string host)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem(), certificate.Thumbprint);
    }

    private MailKitEmailSender Sender(IContainer mailpit, string security, string? username = null, string? password = null, string? trustedThumbprint = null) =>
        Sender(mailpit.Hostname, mailpit.GetMappedPublicPort(1025), security, username, password, trustedThumbprint);

    private MailKitEmailSender Sender(string host, int port, string security, string? username = null, string? password = null, string? trustedThumbprint = null, int timeout = 30)
    {
        var options = Options.Create(new EmailOptions
        {
            Host = host,
            Port = port,
            Security = security,
            Username = username ?? "",
            Password = password ?? "",
            From = SenderAddress,
            TimeoutSeconds = timeout,
        });
        var logger = LoggerFactory.Create(logging => logging.AddProvider(_logs)).CreateLogger<MailKitEmailSender>();
        return new MailKitEmailSender(options, logger)
        {
            CertificateValidation = trustedThumbprint is null
                ? null
                : (_, certificate, _, _) => certificate is X509Certificate2 presented && presented.Thumbprint == trustedThumbprint,
        };
    }

    private static MimeMessage Report(params string[] recipients)
    {
        var message = new MimeMessage();
        message.To.Add(new GroupAddress("undisclosed-recipients"));
        foreach (var recipient in recipients)
        {
            message.Bcc.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = "Census 2026 – Housing – Oct 28, 2026";
        message.Body = new TextPart("plain") { Text = "Housing figures." };
        return message;
    }

    private static async Task<JsonElement[]> MessagesAsync(IContainer mailpit)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages");
        return [.. list.GetProperty("messages").EnumerateArray()];
    }

    private static async Task<JsonElement> HeadersAsync(IContainer mailpit, string id)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };
        return await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}/headers");
    }

    /// <summary>The addresses of one of the message's address lists; Mailpit writes null for an empty one.</summary>
    private static string[] Addresses(JsonElement message, string field) =>
        message.GetProperty(field) is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(a => a.GetProperty("Address").GetString()!)]
            : [];

    [Fact]
    public async Task Relay_without_authentication_delivers_to_the_bcc_recipients_only()
    {
        await using var mailpit = await MailpitAsync();

        var result = await Sender(mailpit, EmailOptions.None).SendAsync(Report("ana@partner.org", "luis@city.gov.co"), CancellationToken.None);

        Assert.Equal(EmailResult.Sent, result);
        var stored = Assert.Single(await MessagesAsync(mailpit));
        Assert.Equal(["ana@partner.org", "luis@city.gov.co"], Addresses(stored, "Bcc").Order());
        Assert.Empty(Addresses(stored, "To"));
        Assert.Empty(Addresses(stored, "Cc"));

        // Display name: no TAMIZA_SMTP_FROM_NAME gives "Tamiza".
        Assert.Equal("Tamiza", stored.GetProperty("From").GetProperty("Name").GetString());
        Assert.Equal(SenderAddress, stored.GetProperty("From").GetProperty("Address").GetString());

        // Automatic message marked, and the To header an empty group. (Mailpit writes a Bcc header of its own from the envelope.)
        var headers = await HeadersAsync(mailpit, stored.GetProperty("ID").GetString()!);
        Assert.Equal("auto-generated", headers.GetProperty("Auto-Submitted")[0].GetString());
        Assert.Equal("undisclosed-recipients: ;", headers.GetProperty("To")[0].GetString());
        Assert.EndsWith("@example.org>", headers.GetProperty("Message-Id")[0].GetString());
    }

    [Fact]
    public async Task Default_security_and_port_upgrades_with_starttls_then_authenticates()
    {
        var (cert, key, thumbprint) = Certificate("localhost");
        await using var mailpit = await MailpitAsync(
            new Dictionary<string, string> { ["MP_SMTP_REQUIRE_STARTTLS"] = "true", ["MP_SMTP_AUTH"] = "reports:s3cret-passw0rd" }, (cert, key));

        var result = await Sender("localhost", mailpit.GetMappedPublicPort(1025), EmailOptions.StartTls, "reports", "s3cret-passw0rd", thumbprint)
            .SendAsync(Report("ana@partner.org"), CancellationToken.None);

        Assert.Equal(EmailResult.Sent, result);
        Assert.Single(await MessagesAsync(mailpit));
    }

    [Fact]
    public async Task Server_without_starttls()
    {
        await using var mailpit = await MailpitAsync();

        var result = await Sender(mailpit, EmailOptions.StartTls).SendAsync(Report("ana@partner.org"), CancellationToken.None);

        Assert.Equal(EmailOutcome.Unavailable, result.Outcome);
        Assert.Empty(await MessagesAsync(mailpit));
    }

    [Fact]
    public async Task Untrusted_certificate()
    {
        var (cert, key, _) = Certificate("other.example");
        await using var mailpit = await MailpitAsync(new Dictionary<string, string> { ["MP_SMTP_REQUIRE_TLS"] = "true" }, (cert, key));

        var result = await Sender("localhost", mailpit.GetMappedPublicPort(1025), EmailOptions.Tls).SendAsync(Report("ana@partner.org"), CancellationToken.None);

        Assert.Equal(EmailOutcome.Unavailable, result.Outcome);
        Assert.Empty(await MessagesAsync(mailpit));
    }

    [Fact]
    public async Task Credentials_refused_and_the_log_holds_no_password()
    {
        await using var mailpit = await MailpitAsync(new Dictionary<string, string>
        {
            ["MP_SMTP_AUTH"] = "reports:s3cret-passw0rd",
            ["MP_SMTP_AUTH_ALLOW_INSECURE"] = "true",
        });

        var result = await Sender(mailpit, EmailOptions.None, "reports", "wr0ng-passw0rd").SendAsync(Report("ana@partner.org"), CancellationToken.None);

        Assert.Equal(EmailOutcome.AuthFailed, result.Outcome);
        Assert.Empty(await MessagesAsync(mailpit));
        var line = Assert.Single(_logs.Lines, l => l.Contains("could not take a message"));
        Assert.Contains(mailpit.Hostname, line);
        Assert.Contains("Authenticate", line);
        Assert.Contains("AuthFailed", line);
        Assert.DoesNotContain("wr0ng-passw0rd", string.Join('\n', _logs.Lines));
    }

    [Fact]
    public async Task Authentication_required()
    {
        await using var mailpit = await MailpitAsync(new Dictionary<string, string>
        {
            ["MP_SMTP_AUTH"] = "reports:s3cret-passw0rd",
            ["MP_SMTP_AUTH_ALLOW_INSECURE"] = "true",
        });

        var result = await Sender(mailpit, EmailOptions.None).SendAsync(Report("ana@partner.org"), CancellationToken.None);

        Assert.Equal(EmailOutcome.AuthFailed, result.Outcome);
        Assert.Empty(await MessagesAsync(mailpit));
    }

    [Fact]
    public async Task Recipient_refused_delivers_to_no_one_and_names_the_address()
    {
        await using var mailpit = await MailpitAsync(new Dictionary<string, string> { ["MP_SMTP_ALLOWED_RECIPIENTS"] = @"@partner\.org$" });

        var result = await Sender(mailpit, EmailOptions.None)
            .SendAsync(Report("ana@partner.org", "nobody@example.org", "luis@partner.org"), CancellationToken.None);

        Assert.Equal(EmailOutcome.Rejected, result.Outcome);
        Assert.Equal("nobody@example.org", result.RefusedAddress);
        Assert.Empty(await MessagesAsync(mailpit));
    }

    [Fact]
    public async Task Server_down_fails_after_the_timeout()
    {
        // A server that accepts the connection and never greets: an unreachable port would be refused at once.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = listener.AcceptTcpClientAsync();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clock = Stopwatch.StartNew();

        var result = await Sender("127.0.0.1", port, EmailOptions.None, timeout: 5).SendAsync(Report("ana@partner.org"), CancellationToken.None);

        clock.Stop();
        Assert.Equal(EmailOutcome.Unavailable, result.Outcome);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(20));
        (await accepted).Dispose();
    }
}
