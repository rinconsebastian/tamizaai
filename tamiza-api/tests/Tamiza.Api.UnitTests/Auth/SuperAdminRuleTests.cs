using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.UnitTests.Auth;

public sealed class SuperAdminRuleTests
{
    private static SuperAdminRule Rule(string emails) => new(Options.Create(new TamizaOptions { SuperAdminEmails = emails }));

    private static TokenIdentity Identity(string? email, bool verified = true) => new("sub", "Name", email, verified);

    [Fact]
    public void Listed_and_verified_email_is_superadmin() =>
        Assert.True(Rule("ada@example.org").IsSuperAdmin(Identity("ada@example.org")));

    [Fact]
    public void Listed_but_unverified_email_is_not_superadmin() =>
        Assert.False(Rule("ada@example.org").IsSuperAdmin(Identity("ada@example.org", verified: false)));

    [Fact]
    public void Unlisted_email_is_not_superadmin() =>
        Assert.False(Rule("ada@example.org").IsSuperAdmin(Identity("grace@example.org")));

    [Theory]
    [InlineData("ADA@Example.org", "ada@example.org")]
    [InlineData("ada@example.org", "Ada@EXAMPLE.org")]
    public void Comparison_ignores_case(string configured, string tokenEmail) =>
        Assert.True(Rule(configured).IsSuperAdmin(Identity(tokenEmail)));

    [Fact]
    public void List_entries_are_trimmed_and_empty_entries_ignored() =>
        Assert.True(Rule(" grace@example.org , ,ada@example.org ").IsSuperAdmin(Identity("ada@example.org")));

    [Fact]
    public void Email_dropped_from_configuration_is_no_longer_superadmin()
    {
        var identity = Identity("ada@example.org");
        Assert.True(Rule("ada@example.org,grace@example.org").IsSuperAdmin(identity));
        Assert.False(Rule("grace@example.org").IsSuperAdmin(identity));
    }

    [Fact]
    public void Token_without_email_is_not_superadmin() =>
        Assert.False(Rule("ada@example.org").IsSuperAdmin(Identity(null)));

    [Fact]
    public void Empty_configuration_grants_nobody() =>
        Assert.False(Rule("").IsSuperAdmin(Identity("ada@example.org")));
}
