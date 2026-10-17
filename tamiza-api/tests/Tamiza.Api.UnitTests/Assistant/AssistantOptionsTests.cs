using Microsoft.Extensions.Configuration;
using Tamiza.Api.Assistant;

namespace Tamiza.Api.UnitTests.Assistant;

public sealed class AssistantOptionsTests
{
    private const string Url = "TAMIZA_ASSISTANT_BASE_URL";
    private const string Model = "TAMIZA_ASSISTANT_MODEL";
    private const string Key = "TAMIZA_ASSISTANT_API_KEY";
    private const string Extra = "TAMIZA_ASSISTANT_EXTRA_BODY";
    private const string Timeout = "TAMIZA_ASSISTANT_TIMEOUT_SECONDS";
    private const string Budget = "TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER";
    private const string Context = "TAMIZA_ASSISTANT_MAX_CONTEXT_KB";

    private static AssistantOptions Bind(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
        var options = new AssistantOptions();
        configuration.Bind(options);
        return options;
    }

    private static string[] Failures(params (string Key, string Value)[] values) =>
        new AssistantOptionsValidator().Validate(null, Bind(values)).Failures?.ToArray() ?? [];

    [Fact]
    public void Defaults_apply_when_no_variable_is_set()
    {
        var options = Bind();

        Assert.Equal("", options.BaseUrl);
        Assert.Equal("", options.Model);
        Assert.Equal("", options.ApiKey);
        Assert.Equal("", options.ExtraBody);
        Assert.Equal(120, options.TimeoutSeconds);
        Assert.Equal(500_000, options.DailyTokensPerUser);
        Assert.Equal(128, options.MaxContextKb);
        Assert.False(options.Enabled);
        Assert.Empty(Failures());
    }

    [Fact]
    public void Numbers_bind_from_their_variables()
    {
        var options = Bind((Timeout, "30"), (Budget, "2000"), (Context, "256"));

        Assert.Equal((30, 2000, 256), (options.TimeoutSeconds, options.DailyTokensPerUser, options.MaxContextKb));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void Enabled_needs_all_three_of_the_base_url_the_model_and_the_key(bool url, bool model, bool key, bool enabled)
    {
        var values = new List<(string, string)>();
        if (url)
        {
            values.Add((Url, "https://api.example.org/v1"));
        }

        if (model)
        {
            values.Add((Model, "some-model"));
        }

        if (key)
        {
            values.Add((Key, "sk-test"));
        }

        Assert.Equal(enabled, Bind(values.ToArray()).Enabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void A_blank_value_counts_as_absent(string blank)
    {
        Assert.False(Bind((Url, "https://api.example.org/v1"), (Model, blank), (Key, "sk-test")).Enabled);
        Assert.False(Bind((Url, "https://api.example.org/v1"), (Model, "m"), (Key, blank)).Enabled);
        Assert.False(Bind((Url, blank), (Model, "m"), (Key, "sk-test")).Enabled);
        Assert.Empty(Failures((Url, blank), (Model, blank), (Key, blank), (Extra, blank)));
    }

    [Fact]
    public void Text_values_are_trimmed()
    {
        var options = Bind((Url, " https://api.example.org/v1 "), (Model, " m\n"), (Key, "sk-test\r\n"));

        Assert.Equal("m", options.Model);
        Assert.Equal("sk-test", options.ApiKey);
        Assert.Equal("https://api.example.org/v1", options.BaseUrl);
    }

    [Fact]
    public void Missing_variables_are_named_in_a_fixed_order()
    {
        Assert.Equal([Url, Model, Key], Bind().MissingVariables());
        Assert.Equal([Key], Bind((Url, "https://a.test/v1"), (Model, "m")).MissingVariables());
        Assert.Equal([Url, Key], Bind((Model, "m")).MissingVariables());
        Assert.Empty(Bind((Url, "https://a.test/v1"), (Model, "m"), (Key, "k")).MissingVariables());
    }

    [Theory]
    [InlineData("https://api.example.org/v1", "https://api.example.org/v1/")]
    [InlineData("https://api.example.org/v1/", "https://api.example.org/v1/")]
    [InlineData("http://192.168.1.20:11434/v1/", "http://192.168.1.20:11434/v1/")]
    [InlineData("http://host.docker.internal:11434", "http://host.docker.internal:11434/")]
    public void The_base_url_gets_a_trailing_slash(string configured, string expected)
    {
        var options = Bind((Url, configured));

        Assert.Equal(expected, options.BaseUri!.AbsoluteUri);
        Assert.Equal(expected + "chat/completions", new Uri(options.BaseUri, "chat/completions").AbsoluteUri);
        Assert.Empty(Failures((Url, configured)));
    }

    [Fact]
    public void The_host_is_only_the_host_name()
    {
        Assert.Equal("generativelanguage.googleapis.com", Bind((Url, "https://generativelanguage.googleapis.com/v1beta/openai/")).Host);
        Assert.Null(Bind().Host);
    }

    [Theory]
    [InlineData("api.example.org/v1")]
    [InlineData("localhost:11434/v1")]
    [InlineData("ftp://api.example.org/v1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://")]
    [InlineData("/v1")]
    [InlineData("https://user:secret@api.example.org/v1")]
    [InlineData("https://api.example.org/v1?key=abc")]
    [InlineData("https://api.example.org/v1#frag")]
    public void A_malformed_base_url_is_rejected_naming_its_variable(string value)
    {
        var failure = Assert.Single(Failures((Url, value)));

        Assert.Contains(Url, failure);
        Assert.DoesNotContain("secret", failure);
    }

    [Theory]
    [InlineData("""{"reasoning_effort":"low"}""")]
    [InlineData("""{"extra_body":{"thinking":{"budget":0}},"temperature":0.2}""")]
    [InlineData("{}")]
    public void Extra_fields_that_are_a_json_object_are_accepted(string value)
    {
        Assert.Empty(Failures((Extra, value)));
        Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(value)!.ToJsonString(), Bind((Extra, value)).ExtraFields().ToJsonString());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"model":"other"}""")]
    [InlineData("""{"messages":[]}""")]
    [InlineData("""{"stream":true}""")]
    [InlineData("""{"temperature":0,"stream":false}""")]
    public void Extra_fields_that_are_not_an_object_or_set_a_reserved_field_are_rejected(string value)
    {
        var failure = Assert.Single(Failures((Extra, value)));

        Assert.Contains(Extra, failure);
        Assert.Empty(Bind((Extra, value)).ExtraFields());
    }

    [Theory]
    [InlineData(Timeout, "9")]
    [InlineData(Timeout, "241")]
    [InlineData(Timeout, "0")]
    [InlineData(Budget, "999")]
    [InlineData(Budget, "100000001")]
    [InlineData(Context, "16")]
    [InlineData(Context, "63")]
    [InlineData(Context, "1025")]
    public void A_number_out_of_range_is_rejected_naming_its_variable(string variable, string value)
    {
        var failure = Assert.Single(Failures((variable, value)));

        Assert.Contains(variable, failure);
    }

    [Theory]
    [InlineData(Timeout, "10")]
    [InlineData(Timeout, "240")]
    [InlineData(Budget, "1000")]
    [InlineData(Budget, "100000000")]
    [InlineData(Context, "64")]
    [InlineData(Context, "1024")]
    public void The_ends_of_each_range_are_accepted(string variable, string value) =>
        Assert.Empty(Failures((variable, value)));

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var failures = Failures((Url, "nope"), (Extra, "[]"), (Timeout, "1"), (Budget, "1"), (Context, "16"));

        Assert.Equal(5, failures.Length);
        foreach (var variable in new[] { Url, Extra, Timeout, Budget, Context })
        {
            Assert.Contains(failures, failure => failure.Contains(variable));
        }
    }

    [Fact]
    public void Usage_costs_the_larger_of_the_total_and_the_sum_of_the_parts()
    {
        Assert.Equal(120, new AssistantUsage(100, 20, 120).Tokens);
        Assert.Equal(900, new AssistantUsage(100, 20, 900).Tokens);
        Assert.Equal(120, new AssistantUsage(100, 20, 50).Tokens);
        Assert.Equal(120, new AssistantUsage(100, 20, null).Tokens);
        Assert.Equal(77, new AssistantUsage(null, null, 77).Tokens);
        Assert.Null(new AssistantUsage(null, null, null).Tokens);
    }

    [Fact]
    public void The_estimate_is_a_quarter_of_the_bytes_sent_and_received_rounded_up()
    {
        Assert.Equal(0, new AssistantResult(null, null, 0, 0).EstimatedTokens);
        Assert.Equal(1, new AssistantResult(null, null, 1, 0).EstimatedTokens);
        Assert.Equal(3, new AssistantResult(null, null, 8, 3).EstimatedTokens);
        Assert.Equal(250, new AssistantResult(null, null, 900, 100).EstimatedTokens);
    }
}
