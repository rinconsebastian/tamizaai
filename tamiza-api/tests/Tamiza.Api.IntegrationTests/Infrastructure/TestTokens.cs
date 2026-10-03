using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Issues access tokens the way Keycloak would, signed with a key the test API trusts.</summary>
public static class TestTokens
{
    public const string Issuer = "https://idp.test/realms/tamiza";
    public const string Audience = "tamiza-api";

    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    /// <summary>Replaces discovery with a static configuration that trusts <see cref="SigningKey"/>.</summary>
    public static void TrustTestKey(JwtBearerOptions options)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(SigningKey);
        options.Configuration = configuration;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }

    public static string Create(
        string subject,
        string? name = "Ada Lovelace",
        string? email = "ada@example.org",
        bool emailVerified = true,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? key = null)
    {
        var now = DateTime.UtcNow;
        var expiry = expires ?? now.AddMinutes(5);
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["email_verified"] = emailVerified };
        if (name is not null)
        {
            claims["name"] = name;
        }

        if (email is not null)
        {
            claims["email"] = email;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiry.AddMinutes(-10),
            NotBefore = expiry.AddMinutes(-10),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
