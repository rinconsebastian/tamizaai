using System.Buffers.Text;
using Microsoft.AspNetCore.DataProtection;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class SecretProtectorTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();

    [Fact]
    public void Round_trips_a_secret()
    {
        var protector = new SecretProtector(_provider);
        var encrypted = protector.Protect("kobo-token");

        Assert.NotEqual("kobo-token", encrypted);
        Assert.True(protector.TryUnprotect(encrypted, out var plaintext));
        Assert.Equal("kobo-token", plaintext);
    }

    [Fact]
    public void A_protector_with_another_purpose_cannot_read_it()
    {
        var encrypted = new SecretProtector(_provider).Protect("kobo-token");
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => _provider.CreateProtector("Other.Purpose").Unprotect(encrypted));
    }

    [Fact]
    public void Unreadable_values_are_reported_instead_of_thrown() =>
        Assert.False(new SecretProtector(_provider).TryUnprotect("not-a-protected-value", out _));

    [Fact]
    public void Webhook_secrets_are_random_32_byte_base64url_strings()
    {
        var first = WebhookSecret.Generate();
        var second = WebhookSecret.Generate();

        Assert.NotEqual(first, second);
        Assert.Equal(32, Base64Url.DecodeFromChars(first).Length);
        Assert.DoesNotContain('+', first + second);
        Assert.DoesNotContain('/', first + second);
    }
}
