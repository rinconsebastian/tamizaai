using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tamiza.Api.Accounts.Keycloak;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.UnitTests.Accounts;

public sealed class KeycloakAdminOptionsTests
{
    private const string RealmUrl = "https://auth.example.org/realms/tamiza";

    private static KeycloakAdminOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new KeycloakAdminOptions();
        configuration.Bind(options);
        return options;
    }

    private static string[] Failures(KeycloakAdminOptions options, string authority = RealmUrl) =>
        new KeycloakAdminOptionsValidator(Options.Create(new OidcOptions { Authority = authority })).Validate(null, options).Failures?.ToArray() ?? [];

    [Fact]
    public void Nothing_is_set_by_default()
    {
        var options = Bind();

        Assert.Equal("", options.ClientId);
        Assert.Equal("", options.ClientSecret);
        Assert.Equal("", options.Url);
        Assert.Null(options.LinkHours);
        Assert.False(options.Enabled);
        Assert.Empty(Failures(options, "https://login.example.org/oidc"));
    }

    [Fact]
    public void Compose_passes_blanks_for_unset_values()
    {
        var options = Bind(("TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID", ""), ("TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET", ""),
            ("TAMIZA_KEYCLOAK_ADMIN_URL", ""), ("TAMIZA_KEYCLOAK_LINK_HOURS", ""));

        Assert.False(options.Enabled);
        Assert.Null(options.LinkHours);
        Assert.Empty(Failures(options));
    }

    [Fact]
    public void Every_variable_binds_and_text_is_trimmed()
    {
        var options = Bind(("TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID", " tamiza-admin "), ("TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET", "s3cret\n"),
            ("TAMIZA_KEYCLOAK_ADMIN_URL", " http://keycloak:8080 "), ("TAMIZA_KEYCLOAK_LINK_HOURS", "24"));

        Assert.Equal("tamiza-admin", options.ClientId);
        Assert.Equal("s3cret", options.ClientSecret);
        Assert.Equal("http://keycloak:8080", options.Url);
        Assert.Equal(24, options.LinkHours);
        Assert.True(options.Enabled);
        Assert.Empty(Failures(options));
    }

    [Theory]
    [InlineData("id", "secret", true)]
    [InlineData("id", "", false)]
    [InlineData("", "secret", false)]
    [InlineData("  ", "secret", false)]
    [InlineData("", "", false)]
    public void Enabled_needs_both_the_client_id_and_the_secret(string id, string secret, bool enabled)
    {
        Assert.Equal(enabled, new KeycloakAdminOptions { ClientId = id, ClientSecret = secret }.Enabled);
    }

    [Fact]
    public void The_missing_variables_are_named()
    {
        Assert.Equal(["TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET"], new KeycloakAdminOptions { ClientId = "id" }.MissingVariables());
        Assert.Equal(["TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID"], new KeycloakAdminOptions { ClientSecret = "s" }.MissingVariables());
        Assert.Empty(new KeycloakAdminOptions { ClientId = "id", ClientSecret = "s" }.MissingVariables());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(720, true)]
    [InlineData(0, false)]
    [InlineData(721, false)]
    public void The_link_lifetime_must_be_between_1_and_720_hours(int hours, bool valid)
    {
        var failures = Failures(new KeycloakAdminOptions { LinkHours = hours });

        Assert.Equal(valid, failures.Length == 0);
        Assert.All(failures, failure => Assert.Contains("TAMIZA_KEYCLOAK_LINK_HOURS", failure));
    }

    [Theory]
    [InlineData("http://keycloak:8080", true)]
    [InlineData("https://auth.example.org/kc", true)]
    [InlineData("keycloak:8080", false)]
    [InlineData("ftp://auth.example.org", false)]
    [InlineData("https://user:pw@auth.example.org", false)]
    [InlineData("https://auth.example.org?x=1", false)]
    [InlineData("https://auth.example.org#x", false)]
    public void The_address_must_be_a_plain_http_or_https_url(string url, bool valid)
    {
        var failures = Failures(new KeycloakAdminOptions { Url = url });

        Assert.Equal(valid, failures.Length == 0);
        Assert.All(failures, failure => Assert.Contains("TAMIZA_KEYCLOAK_ADMIN_URL", failure));
    }

    [Fact]
    public void An_enabled_adapter_needs_a_realm_url_as_the_authority()
    {
        var enabled = new KeycloakAdminOptions { ClientId = "id", ClientSecret = "s" };

        Assert.Empty(Failures(enabled, RealmUrl));
        Assert.Empty(Failures(enabled, "https://auth.example.org/auth/realms/tamiza/"));
        Assert.Contains("OIDC_AUTHORITY", Assert.Single(Failures(enabled, "https://login.example.org/oidc")));
        Assert.Contains("OIDC_AUTHORITY", Assert.Single(Failures(enabled, "https://auth.example.org/realms/")));
    }

    [Fact]
    public void A_deployment_on_another_provider_is_never_checked_for_a_realm()
    {
        Assert.Empty(Failures(new KeycloakAdminOptions(), "https://accounts.example.com"));
    }
}
