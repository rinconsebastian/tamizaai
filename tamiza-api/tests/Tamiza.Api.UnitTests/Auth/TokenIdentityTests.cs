using System.Security.Claims;
using Tamiza.Api.Auth;

namespace Tamiza.Api.UnitTests.Auth;

public sealed class TokenIdentityTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Reads_subject_name_email_and_verification()
    {
        var identity = TokenIdentity.FromPrincipal(Principal(("sub", "s1"), ("name", "Ada"), ("email", "ada@example.org"), ("email_verified", "true")));
        Assert.Equal(new TokenIdentity("s1", "Ada", "ada@example.org", true), identity);
    }

    [Fact]
    public void Name_falls_back_to_preferred_username_then_subject()
    {
        Assert.Equal("ada", TokenIdentity.FromPrincipal(Principal(("sub", "s1"), ("preferred_username", "ada")))!.Name);
        Assert.Equal("s1", TokenIdentity.FromPrincipal(Principal(("sub", "s1")))!.Name);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("not-a-bool")]
    public void Email_is_unverified_unless_the_claim_says_true(string value) =>
        Assert.False(TokenIdentity.FromPrincipal(Principal(("sub", "s1"), ("email_verified", value)))!.EmailVerified);

    [Fact]
    public void Missing_subject_yields_no_identity() =>
        Assert.Null(TokenIdentity.FromPrincipal(Principal(("name", "Ada"))));
}
