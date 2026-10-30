using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Settings of the MCP server for AI clients. The deployment settings are the flat <c>TAMIZA_MCP_*</c> variables; the tuning limits are
/// the <c>Mcp</c> configuration section (for example <c>Mcp__MaxResultCharacters</c>). The server exists only when
/// <see cref="Enabled"/>. Compose always passes optional strings, often empty, so a blank text means its default. Text values are trimmed.
/// </summary>
public sealed class McpOptions
{
    public const string Section = "Mcp";

    public const string EnabledVariable = "TAMIZA_MCP_ENABLED";
    public const string AudienceVariable = "TAMIZA_MCP_AUDIENCE";
    public const string ClientIdVariable = "TAMIZA_MCP_CLIENT_ID";
    public const string CallbackPortVariable = "TAMIZA_MCP_CALLBACK_PORT";
    public const string MaxDataAccessVariable = "TAMIZA_MCP_MAX_DATA_ACCESS";
    public const string MaxResultCharactersVariable = "Mcp__MaxResultCharacters";
    public const string MaxConcurrentCallsPerUserVariable = "Mcp__MaxConcurrentCallsPerUser";
    public const string CallsPerMinutePerUserVariable = "Mcp__CallsPerMinutePerUser";
    public const string ProfileStatementTimeoutVariable = "Mcp__ProfileStatementTimeout";
    public const string CallRetentionDaysVariable = "Mcp__CallRetentionDays";

    public const string DefaultAudience = "tamiza-mcp";
    public const string DefaultClientId = "tamiza-mcp";
    public const int DefaultCallbackPort = 33418;

    /// <summary>The path of the MCP endpoint.</summary>
    public const string Path = "/mcp";

    /// <summary>The path of the protected resource metadata of <see cref="Path"/> (RFC 9728: the well-known prefix, then the resource path).</summary>
    public const string ResourceMetadataPath = "/.well-known/oauth-protected-resource/mcp";

    /// <summary>The scope the protected resource metadata advertises, and the client scope that adds the audience in Keycloak.</summary>
    public const string Scope = "tamiza-mcp";

    private string _audience = DefaultAudience;
    private string _clientId = DefaultClientId;
    private string _maxDataAccess = "";

    [ConfigurationKeyName(EnabledVariable)]
    public bool Enabled { get; set; }

    /// <summary>The <c>aud</c> that <c>/mcp</c> requires; it must differ from the API's audience.</summary>
    [ConfigurationKeyName(AudienceVariable)]
    public string Audience { get => _audience; set => _audience = Or(value, DefaultAudience); }

    /// <summary>The public client registered in the identity provider; only shown on the connect page.</summary>
    [ConfigurationKeyName(ClientIdVariable)]
    public string ClientId { get => _clientId; set => _clientId = Or(value, DefaultClientId); }

    /// <summary>The loopback port registered for Claude Code, shown in its command.</summary>
    [ConfigurationKeyName(CallbackPortVariable)]
    public int CallbackPort { get; set; } = DefaultCallbackPort;

    /// <summary><c>aggregates</c> (the default when blank) or <c>rows</c>: the most any project of the deployment may allow.</summary>
    [ConfigurationKeyName(MaxDataAccessVariable)]
    public string MaxDataAccess { get => _maxDataAccess; set => _maxDataAccess = value?.Trim() ?? ""; }

    /// <summary>The most characters a tool result holds before it is cut.</summary>
    public int MaxResultCharacters { get; set; } = 60_000;

    public int MaxConcurrentCallsPerUser { get; set; } = 4;

    public int CallsPerMinutePerUser { get; set; } = 120;

    /// <summary>The time a column profile may take in PostgreSQL before it is stopped.</summary>
    public TimeSpan ProfileStatementTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public int CallRetentionDays { get; set; } = 180;

    /// <summary>
    /// The deployment's maximum access level for a project, <see cref="McpAccess.Aggregates"/> unless the operator set <c>rows</c>.
    /// An unknown value reads as the strictest, so a typo can never widen access; the validator stops the api before that matters.
    /// </summary>
    public McpAccess MaxAccess => TryParseMaxAccess(MaxDataAccess, out var access) ? access : McpAccess.Aggregates;

    /// <summary>Whether <paramref name="value"/> is blank (the default), <c>aggregates</c> or <c>rows</c>, ignoring case.</summary>
    public static bool TryParseMaxAccess(string? value, out McpAccess access)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "aggregates":
                access = McpAccess.Aggregates;
                return true;
            case "rows":
                access = McpAccess.Rows;
                return true;
            default:
                access = McpAccess.Aggregates;
                return false;
        }
    }

    /// <summary>
    /// Whether the hosted Claude apps cannot reach an address: they connect from the Internet over HTTPS, so anything else (other than
    /// <c>localhost</c>, which only Claude Code on the same computer reaches) only suits Claude Code.
    /// </summary>
    public static bool IsNotReachableByHostedApps(string publicUrl) =>
        !Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

/// <summary>
/// Stops the api at startup, naming the variable, for values that are typos the operator must see at deploy time. The audience may
/// not equal the API's, or a token for one would open the other; that is checked only when the server is enabled, because the audience
/// of a disabled server is never used.
/// </summary>
public sealed class McpOptionsValidator(IOptions<OidcOptions> oidc) : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        var failures = new List<string>();

        if (!McpOptions.TryParseMaxAccess(options.MaxDataAccess, out _))
        {
            failures.Add($"{McpOptions.MaxDataAccessVariable} must be aggregates or rows, not {options.MaxDataAccess}.");
        }

        if (options.Enabled && string.Equals(options.Audience, oidc.Value.Audience, StringComparison.Ordinal))
        {
            failures.Add($"{McpOptions.AudienceVariable} must differ from OIDC_AUDIENCE, or a token for the REST API would open the MCP server.");
        }

        CheckRange(failures, McpOptions.CallbackPortVariable, options.CallbackPort, 1_024, 65_535);
        CheckRange(failures, McpOptions.MaxResultCharactersVariable, options.MaxResultCharacters, 10_000, 140_000);
        CheckRange(failures, McpOptions.MaxConcurrentCallsPerUserVariable, options.MaxConcurrentCallsPerUser, 1, 16);
        CheckRange(failures, McpOptions.CallsPerMinutePerUserVariable, options.CallsPerMinutePerUser, 10, 1_000);
        CheckRange(failures, McpOptions.CallRetentionDaysVariable, options.CallRetentionDays, 7, 3_650);
        if (options.ProfileStatementTimeout < TimeSpan.FromSeconds(5) || options.ProfileStatementTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add($"{McpOptions.ProfileStatementTimeoutVariable} must be between 00:00:05 and 00:05:00, not {options.ProfileStatementTimeout}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string variable, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{variable} must be between {min:N0} and {max:N0}, not {value:N0}.");
        }
    }
}
