using Tamiza.Api.Accounts.Keycloak;

namespace Tamiza.Api.UnitTests.Accounts;

public sealed class KeycloakAddressTests
{
    [Fact]
    public void Default_address_comes_from_the_authority()
    {
        var address = KeycloakAddress.From("https://auth.example.org/realms/tamiza", null, null)!;

        Assert.Equal("tamiza", address.Realm);
        Assert.Equal("https://auth.example.org/realms/tamiza/protocol/openid-connect/token", address.TokenUrl);
        Assert.Equal("https://auth.example.org/admin/realms/tamiza", address.AdminBase);
    }

    [Fact]
    public void Internal_address_replaces_the_base_and_keeps_the_realm()
    {
        var address = KeycloakAddress.From("https://auth.example.org/realms/tamiza", null, "http://keycloak:8080")!;

        Assert.Equal("http://keycloak:8080/realms/tamiza/protocol/openid-connect/token", address.TokenUrl);
        Assert.Equal("http://keycloak:8080/admin/realms/tamiza", address.AdminBase);
    }

    [Fact]
    public void A_discovery_address_on_the_docker_host_gives_the_base()
    {
        var address = KeycloakAddress.From(
            "http://localhost:8080/realms/tamiza",
            "http://host.docker.internal:8080/realms/tamiza/.well-known/openid-configuration", null)!;

        Assert.Equal("http://host.docker.internal:8080/realms/tamiza/protocol/openid-connect/token", address.TokenUrl);
        Assert.Equal("http://host.docker.internal:8080/admin/realms/tamiza", address.AdminBase);
    }

    [Fact]
    public void The_explicit_address_wins_over_the_discovery_address()
    {
        var address = KeycloakAddress.From(
            "https://auth.example.org/realms/tamiza", "http://other:8080/realms/tamiza/.well-known/openid-configuration", "http://keycloak:8080")!;

        Assert.Equal("http://keycloak:8080/admin/realms/tamiza", address.AdminBase);
    }

    [Fact]
    public void A_legacy_auth_prefix_is_kept_in_the_base()
    {
        var address = KeycloakAddress.From("https://auth.example.org/auth/realms/tamiza", null, null)!;

        Assert.Equal("tamiza", address.Realm);
        Assert.Equal("https://auth.example.org/auth/admin/realms/tamiza", address.AdminBase);
        Assert.Equal("https://auth.example.org/auth/realms/tamiza/protocol/openid-connect/token", address.TokenUrl);
    }

    [Theory]
    [InlineData("https://auth.example.org/realms/tamiza/")]
    [InlineData("https://auth.example.org/realms/tamiza")]
    public void Trailing_slashes_do_not_matter(string authority)
    {
        Assert.Equal("https://auth.example.org/admin/realms/tamiza", KeycloakAddress.From(authority, null, "")!.AdminBase);
        Assert.Equal("https://kc.test/admin/realms/tamiza", KeycloakAddress.From(authority, null, "https://kc.test/")!.AdminBase);
    }

    [Theory]
    [InlineData("https://login.example.org/oidc")]
    [InlineData("https://auth.example.org/realms/")]
    [InlineData("https://auth.example.org/realms/a/b")]
    public void An_authority_without_a_realm_has_no_address(string authority)
    {
        Assert.Null(KeycloakAddress.From(authority, null, null));
    }
}
