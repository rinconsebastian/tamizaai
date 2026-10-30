using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

public sealed class McpOptionsTests
{
    private const string ApiAudience = "tamiza-api";

    /// <summary>Binds like the registration does: the flat variables from the root, the tuning limits from the Mcp section.</summary>
    private static McpOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key.Replace("__", ":"), v => (string?)v.Value))
            .Build();
        var options = new McpOptions();
        configuration.Bind(options);
        configuration.GetSection(McpOptions.Section).Bind(options);
        return options;
    }

    private static string[] Failures(params (string Key, string Value)[] values) => FailuresOf(Bind(values));

    private static string[] FailuresOf(McpOptions options) =>
        new McpOptionsValidator(Options.Create(new OidcOptions { Audience = ApiAudience })).Validate(null, options).Failures?.ToArray() ?? [];

    [Fact]
    public void Defaults_apply_when_no_variable_is_set()
    {
        var options = Bind();

        Assert.False(options.Enabled);
        Assert.Equal("tamiza-mcp", options.Audience);
        Assert.Equal("tamiza-mcp", options.ClientId);
        Assert.Equal(33418, options.CallbackPort);
        Assert.Equal(McpAccess.Aggregates, options.MaxAccess);
        Assert.Equal(60_000, options.MaxResultCharacters);
        Assert.Equal(4, options.MaxConcurrentCallsPerUser);
        Assert.Equal(120, options.CallsPerMinutePerUser);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ProfileStatementTimeout);
        Assert.Equal(180, options.CallRetentionDays);
        Assert.Empty(Failures());
    }

    [Fact]
    public void Every_flat_and_section_key_binds_from_its_variable()
    {
        var options = Bind(
            ("TAMIZA_MCP_ENABLED", "true"), ("TAMIZA_MCP_AUDIENCE", "https://tamiza.example.org/mcp"), ("TAMIZA_MCP_CLIENT_ID", "claude-tamiza"),
            ("TAMIZA_MCP_CALLBACK_PORT", "40000"), ("TAMIZA_MCP_MAX_DATA_ACCESS", "rows"),
            ("Mcp__MaxResultCharacters", "30000"), ("Mcp__MaxConcurrentCallsPerUser", "2"), ("Mcp__CallsPerMinutePerUser", "60"),
            ("Mcp__ProfileStatementTimeout", "00:02:00"), ("Mcp__CallRetentionDays", "30"));

        Assert.True(options.Enabled);
        Assert.Equal("https://tamiza.example.org/mcp", options.Audience);
        Assert.Equal("claude-tamiza", options.ClientId);
        Assert.Equal(40000, options.CallbackPort);
        Assert.Equal(McpAccess.Rows, options.MaxAccess);
        Assert.Equal(30000, options.MaxResultCharacters);
        Assert.Equal(2, options.MaxConcurrentCallsPerUser);
        Assert.Equal(60, options.CallsPerMinutePerUser);
        Assert.Equal(TimeSpan.FromMinutes(2), options.ProfileStatementTimeout);
        Assert.Equal(30, options.CallRetentionDays);
        Assert.Empty(FailuresOf(options));
    }

    [Theory]
    [InlineData("", McpAccess.Aggregates)]
    [InlineData("  ", McpAccess.Aggregates)]
    [InlineData("aggregates", McpAccess.Aggregates)]
    [InlineData("AGGREGATES", McpAccess.Aggregates)]
    [InlineData(" rows ", McpAccess.Rows)]
    [InlineData("Rows", McpAccess.Rows)]
    public void The_maximum_is_summaries_unless_the_operator_sets_rows(string value, McpAccess expected)
    {
        var options = Bind(("TAMIZA_MCP_MAX_DATA_ACCESS", value));

        Assert.Equal(expected, options.MaxAccess);
        Assert.Empty(FailuresOf(options));
    }

    [Theory]
    [InlineData("everything")]
    [InlineData("off")]
    [InlineData("summaries")]
    public void An_unknown_maximum_is_refused_naming_the_variable_and_reads_as_the_strictest(string value)
    {
        var options = Bind(("TAMIZA_MCP_MAX_DATA_ACCESS", value));

        var failure = Assert.Single(FailuresOf(options));
        Assert.Contains("TAMIZA_MCP_MAX_DATA_ACCESS", failure);
        Assert.Contains("aggregates or rows", failure);
        Assert.Equal(McpAccess.Aggregates, options.MaxAccess);
    }

    [Fact]
    public void A_blank_audience_or_client_uses_the_default()
    {
        var options = Bind(("TAMIZA_MCP_AUDIENCE", " "), ("TAMIZA_MCP_CLIENT_ID", ""));

        Assert.Equal("tamiza-mcp", options.Audience);
        Assert.Equal("tamiza-mcp", options.ClientId);
    }

    [Fact]
    public void The_audience_may_not_equal_the_apis_when_the_server_is_enabled()
    {
        var failure = Assert.Single(Failures(("TAMIZA_MCP_ENABLED", "true"), ("TAMIZA_MCP_AUDIENCE", ApiAudience)));

        Assert.Contains("TAMIZA_MCP_AUDIENCE", failure);
        Assert.Contains("OIDC_AUDIENCE", failure);
    }

    [Fact]
    public void The_audience_of_a_disabled_server_is_not_checked()
    {
        Assert.Empty(Failures(("TAMIZA_MCP_AUDIENCE", ApiAudience)));
    }

    [Theory]
    [InlineData("TAMIZA_MCP_CALLBACK_PORT", "1023")]
    [InlineData("TAMIZA_MCP_CALLBACK_PORT", "65536")]
    [InlineData("Mcp__MaxResultCharacters", "9999")]
    [InlineData("Mcp__MaxResultCharacters", "140001")]
    [InlineData("Mcp__MaxConcurrentCallsPerUser", "0")]
    [InlineData("Mcp__MaxConcurrentCallsPerUser", "17")]
    [InlineData("Mcp__CallsPerMinutePerUser", "9")]
    [InlineData("Mcp__CallsPerMinutePerUser", "1001")]
    [InlineData("Mcp__ProfileStatementTimeout", "00:00:04")]
    [InlineData("Mcp__ProfileStatementTimeout", "00:05:01")]
    [InlineData("Mcp__CallRetentionDays", "6")]
    [InlineData("Mcp__CallRetentionDays", "3651")]
    public void A_number_out_of_range_is_refused_naming_the_variable(string key, string value)
    {
        var failure = Assert.Single(Failures((key, value)));

        Assert.Contains(key, failure);
    }

    [Theory]
    [InlineData("TAMIZA_MCP_CALLBACK_PORT", "1024")]
    [InlineData("TAMIZA_MCP_CALLBACK_PORT", "65535")]
    [InlineData("Mcp__MaxResultCharacters", "10000")]
    [InlineData("Mcp__MaxResultCharacters", "140000")]
    [InlineData("Mcp__MaxConcurrentCallsPerUser", "16")]
    [InlineData("Mcp__CallsPerMinutePerUser", "1000")]
    [InlineData("Mcp__ProfileStatementTimeout", "00:00:05")]
    [InlineData("Mcp__ProfileStatementTimeout", "00:05:00")]
    [InlineData("Mcp__CallRetentionDays", "3650")]
    public void The_ends_of_each_range_are_accepted(string key, string value) => Assert.Empty(Failures((key, value)));

    [Theory]
    [InlineData("https://tamiza.example.org", false)]
    [InlineData("https://tamiza.example.org/", false)]
    [InlineData("http://localhost:8088", false)]
    [InlineData("http://127.0.0.1:8088", false)]
    [InlineData("http://tamiza.example.org", true)]
    [InlineData("http://192.168.1.20:8088", true)]
    [InlineData("not a url", true)]
    public void Only_a_public_https_address_is_reachable_by_the_hosted_apps(string publicUrl, bool notReachable) =>
        Assert.Equal(notReachable, McpOptions.IsNotReachableByHostedApps(publicUrl));
}
