namespace Tamiza.Api.Data;

/// <summary>Local record of a person who signed in through Keycloak.</summary>
public sealed class User
{
    public Guid Id { get; set; }

    /// <summary>The token's <c>sub</c> claim; the identity key.</summary>
    public required string KeycloakSub { get; set; }

    public required string Name { get; set; }

    public string? Email { get; set; }

    /// <summary>Whether Keycloak marked the email as verified in the latest token. Invitations only match verified emails.</summary>
    public bool EmailVerified { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
