using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Assistant;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Assistant;

[Collection(PostgresCollection.Name)]
public sealed class AssistantClientTests(PostgresFixture postgres) : IDisposable
{
    private readonly AssistantStub _stub = new();

    private static readonly IReadOnlyList<AssistantMessage> Conversation =
    [
        new("system", "You write pandas."),
        new("user", "Share of households with piped water, by municipality."),
    ];

    private async Task<TamizaApiFactory> FactoryAsync(Dictionary<string, string?>? extraSettings = null, TimeSpan? timeout = null)
    {
        var settings = _stub.Settings();
        foreach (var (name, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[name] = value;
        }

        return new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings,
            services => services.Configure<AssistantHttpOptions>(o => o.TimeoutOverride = timeout));
    }

    private static AssistantClient Client(TamizaApiFactory factory)
    {
        factory.CreateClient().Dispose(); // start the host
        return factory.Services.GetRequiredService<AssistantClient>();
    }

    private async Task<(AssistantResult Result, TamizaApiFactory Factory)> CompleteAsync(
        Dictionary<string, string?>? settings = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var factory = await FactoryAsync(settings, timeout);
        return (await Client(factory).CompleteAsync(Conversation, cancellationToken), factory);
    }

    [Fact]
    public async Task Sends_the_model_the_messages_and_the_key_in_the_authorization_header()
    {
        _stub.Answer("Here is the script.");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.True(result.Succeeded);
        Assert.Equal("Here is the script.", result.Answer!.Text);
        var request = Assert.Single(_stub.Requests());
        Assert.Equal($"Bearer {AssistantStub.Key}", request.Authorization);
        Assert.Equal(AssistantStub.Model, request.Body["model"]!.GetValue<string>());
        Assert.Equal(2, request.Messages.Count);
        Assert.Equal("system", request.Messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You write pandas.", request.Messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("user", request.Messages[1]!["role"]!.GetValue<string>());
        Assert.Equal(Conversation[1].Content, request.Messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sends_no_field_the_providers_disagree_on_unless_the_extra_fields_set_it()
    {
        var (_, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal(["messages", "model"], Assert.Single(_stub.Requests()).Fields);
    }

    [Fact]
    public async Task Merges_the_extra_fields_into_every_request()
    {
        var extra = new Dictionary<string, string?>
        {
            ["TAMIZA_ASSISTANT_EXTRA_BODY"] = """{"reasoning_effort":"low","temperature":0.2,"extra_body":{"thinking":{"budget":0}}}""",
        };
        var (_, factory) = await CompleteAsync(extra);
        await using var owned = factory;
        await Client(factory).CompleteAsync(Conversation, default);

        Assert.Equal(2, _stub.Requests().Count);
        Assert.All(_stub.Requests(), request =>
        {
            Assert.Equal(["extra_body", "messages", "model", "reasoning_effort", "temperature"], request.Fields);
            Assert.Equal("low", request.Body["reasoning_effort"]!.GetValue<string>());
            Assert.Equal(0.2, request.Body["temperature"]!.GetValue<double>());
            Assert.Equal(0, request.Body["extra_body"]!["thinking"]!["budget"]!.GetValue<int>());
        });
    }

    [Fact]
    public async Task Resolves_the_path_under_a_base_url_with_or_without_a_trailing_slash()
    {
        var (_, withSlash) = await CompleteAsync(new Dictionary<string, string?> { ["TAMIZA_ASSISTANT_BASE_URL"] = _stub.BaseUrl + "/" });
        await using var owned = withSlash;
        var (_, without) = await CompleteAsync();
        await using var owned2 = without;

        Assert.Equal(2, _stub.RequestsAt(AssistantStub.Path));
    }

    [Fact]
    public async Task Reaches_a_stub_on_localhost_without_any_private_network_flag()
    {
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal("localhost", new Uri(_stub.Url).Host);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Reads_text_from_array_content()
    {
        _stub.RespondRaw(200, """
            {"choices":[{"message":{"role":"assistant","content":[{"type":"text","text":"First. "},{"type":"text","text":"Second."}]},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}
            """);
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal("First. Second.", result.Answer!.Text);
        Assert.False(result.Answer.Truncated);
        Assert.Equal("stop", result.Answer.FinishReason);
    }

    [Fact]
    public async Task Reports_the_tokens_the_provider_gave()
    {
        _stub.Answer("ok", usage: """{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal((100, 20, 120), (result.Answer!.PromptTokens, result.Answer.CompletionTokens, result.Answer.TotalTokens));
        Assert.Equal(120, result.ReportedTokens);
    }

    [Fact]
    public async Task Without_usage_the_tokens_are_unknown_and_an_estimate_is_available()
    {
        _stub.Answer("abcdefgh", usage: null);
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.True(result.Succeeded);
        Assert.Null(result.Answer!.Tokens);
        Assert.Null(result.ReportedTokens);
        Assert.Equal((null, null, null), (result.Answer.PromptTokens, result.Answer.CompletionTokens, result.Answer.TotalTokens));
        var sent = Conversation.Sum(m => Encoding.UTF8.GetByteCount(m.Content));
        Assert.Equal(sent, result.SentBytes);
        Assert.Equal(8, result.ReceivedBytes);
        Assert.Equal((sent + 8 + 3) / 4, result.EstimatedTokens);
    }

    [Fact]
    public async Task A_total_larger_than_prompt_plus_completion_is_what_the_call_cost()
    {
        _stub.Answer("ok", usage: """{"prompt_tokens":100,"completion_tokens":20,"total_tokens":900}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal(900, result.Answer!.Tokens);
    }

    [Fact]
    public async Task A_total_smaller_than_prompt_plus_completion_is_not_trusted()
    {
        _stub.Answer("ok", usage: """{"prompt_tokens":100,"completion_tokens":20,"total_tokens":50}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.Equal(120, result.Answer!.Tokens);
    }

    [Fact]
    public async Task Length_with_text_is_a_truncated_success()
    {
        _stub.Answer("def compute(cleaned, ctx", "length");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.True(result.Succeeded);
        Assert.True(result.Answer!.Truncated);
        Assert.Equal("length", result.Answer.FinishReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Length_without_text_is_unavailable(string text)
    {
        _stub.Answer(text, "length");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: true);
        Assert.Contains(factory.Logs.Lines, line => line.StartsWith("Warning ", StringComparison.Ordinal) && line.Contains("output limit"));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("content_filter")]
    public async Task Error_and_content_filter_give_no_proposal_even_with_partial_text(string finishReason)
    {
        _stub.Answer("def compute(", finishReason);
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: true);
        Assert.Equal(120, result.ReportedTokens);
        Assert.Contains(factory.Logs.Lines, line => line.StartsWith("Warning ", StringComparison.Ordinal) && line.Contains(finishReason));
    }

    [Theory]
    [InlineData("eos_token")]
    [InlineData(null)]
    public async Task An_unknown_or_missing_finish_reason_with_text_still_succeeds(string? finishReason)
    {
        _stub.Answer("All good.", finishReason);
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.True(result.Succeeded);
        Assert.False(result.Answer!.Truncated);
        Assert.Equal(finishReason, result.Answer.FinishReason);
    }

    [Fact]
    public async Task A_200_holding_only_an_error_object_is_unavailable()
    {
        _stub.RespondRaw(200, """{"error":{"message":"Quota exceeded for this model","code":429}}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: true);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}]}""")]
    public async Task A_200_without_a_usable_answer_is_unavailable(string body)
    {
        _stub.RespondRaw(200, body);
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: true);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task Key_model_and_field_problems_are_misconfigured(int status)
    {
        _stub.RespondRaw(status, """{"error":{"message":"PROVIDER-ERROR-BODY"}}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Misconfigured, AssistantErrorCodes.Misconfigured, mayHaveWorked: false);
        Assert.Contains(factory.Logs.Lines, line => line.StartsWith("Error ", StringComparison.Ordinal) && line.Contains(status.ToString()) && line.Contains("localhost"));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(418)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Other_failures_are_unavailable(int status)
    {
        _stub.RespondRaw(status, """{"error":{"message":"PROVIDER-ERROR-BODY"}}""");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: false);
        Assert.Contains(factory.Logs.Lines, line => line.StartsWith("Warning ", StringComparison.Ordinal) && line.Contains(status.ToString()));
    }

    [Fact]
    public async Task Rate_limit_carries_the_retry_after_seconds_the_provider_sent()
    {
        _stub.RespondRaw(429, "{}", new Dictionary<string, string> { ["Retry-After"] = "37" });
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.RateLimited, AssistantErrorCodes.RateLimited, mayHaveWorked: false);
        Assert.Equal(37, result.Failure!.RetryAfterSeconds);
    }

    [Fact]
    public async Task Rate_limit_reads_a_retry_after_date()
    {
        var date = DateTimeOffset.UtcNow.AddSeconds(90).ToString("R");
        _stub.RespondRaw(429, "{}", new Dictionary<string, string> { ["Retry-After"] = date });
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        Assert.InRange(result.Failure!.RetryAfterSeconds!.Value, 85, 92);
    }

    [Fact]
    public async Task Rate_limit_without_retry_after_has_none()
    {
        _stub.RespondRaw(429, "{}");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.RateLimited, AssistantErrorCodes.RateLimited, mayHaveWorked: false);
        Assert.Null(result.Failure!.RetryAfterSeconds);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_so_the_key_never_goes_to_another_address()
    {
        _stub.Redirect("/elsewhere");
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: false);
        Assert.Equal(0, _stub.RequestsAt("/elsewhere"));
        Assert.Equal(1, _stub.RequestsAt(AssistantStub.Path));
    }

    [Fact]
    public async Task A_provider_that_is_too_slow_times_out()
    {
        _stub.RespondRaw(200, AssistantStub.ChatBody("late"), delay: TimeSpan.FromSeconds(5));
        var (result, factory) = await CompleteAsync(timeout: TimeSpan.FromSeconds(1));
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Timeout, AssistantErrorCodes.Timeout, mayHaveWorked: true);
    }

    [Fact]
    public async Task A_cancelled_caller_ends_the_call_without_an_error_code()
    {
        _stub.RespondRaw(200, AssistantStub.ChatBody("late"), delay: TimeSpan.FromSeconds(10));
        await using var factory = await FactoryAsync();
        var client = Client(factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var result = await client.CompleteAsync(Conversation, cancellation.Token);

        AssertFailure(result, AssistantFailureKind.Cancelled, expectedCode: null, mayHaveWorked: true);
        Assert.Equal(Conversation.Sum(m => Encoding.UTF8.GetByteCount(m.Content)), result.SentBytes);
        Assert.Equal(0, result.ReceivedBytes);
    }

    [Fact]
    public async Task A_body_over_four_mebibytes_is_unavailable()
    {
        _stub.Answer(new string('x', 5 * 1024 * 1024));
        var (result, factory) = await CompleteAsync();
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: true);
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_is_unavailable()
    {
        var unreachable = new Dictionary<string, string?> { ["TAMIZA_ASSISTANT_BASE_URL"] = "http://localhost:1/v1" };
        var (result, factory) = await CompleteAsync(unreachable);
        await using var owned = factory;

        AssertFailure(result, AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, mayHaveWorked: false);
    }

    [Fact]
    public async Task Refuses_to_call_when_the_assistant_is_not_configured()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync());
        var client = Client(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(Conversation, default));
        Assert.Empty(_stub.Requests());
    }

    [Fact]
    public async Task Neither_the_key_nor_a_response_body_reaches_the_logs()
    {
        await using var factory = await FactoryAsync();
        var client = Client(factory);

        _stub.RespondRaw(401, """{"error":{"message":"PROVIDER-ERROR-BODY"}}""");
        await client.CompleteAsync(Conversation, default);
        _stub.RespondRaw(500, """{"error":{"message":"PROVIDER-ERROR-BODY"}}""");
        await client.CompleteAsync(Conversation, default);
        _stub.RespondRaw(200, AssistantStub.ChatBody("PROVIDER-ANSWER-TEXT", "content_filter"));
        await client.CompleteAsync(Conversation, default);
        _stub.Answer("PROVIDER-ANSWER-TEXT");
        await client.CompleteAsync(Conversation, default);

        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(AssistantStub.Key));
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("PROVIDER-ERROR-BODY") || line.Contains("PROVIDER-ANSWER-TEXT"));
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(Conversation[1].Content));
    }

    [Fact]
    public async Task A_misconfigured_provider_is_logged_at_most_once_a_minute()
    {
        await using var factory = await FactoryAsync();
        var client = Client(factory);
        _stub.RespondRaw(401, "{}");

        for (var i = 0; i < 3; i++)
        {
            await client.CompleteAsync(Conversation, default);
        }

        Assert.Single(factory.Logs.Lines, line => line.StartsWith("Error ", StringComparison.Ordinal) && line.Contains("401"));
    }

    [Fact]
    public async Task A_402_is_logged_even_when_a_401_was_logged_a_moment_earlier()
    {
        await using var factory = await FactoryAsync();
        var client = Client(factory);

        _stub.RespondRaw(401, "{}");
        await client.CompleteAsync(Conversation, default);
        _stub.RespondRaw(402, "{}");
        await client.CompleteAsync(Conversation, default);

        Assert.Single(factory.Logs.Lines, line => line.StartsWith("Error ", StringComparison.Ordinal) && line.Contains("401"));
        Assert.Single(factory.Logs.Lines, line => line.StartsWith("Error ", StringComparison.Ordinal) && line.Contains("402"));
    }

    [Fact]
    public async Task Says_once_at_startup_which_variables_are_missing_when_only_some_are_set()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), Without(_stub.Settings(), "TAMIZA_ASSISTANT_API_KEY"));
        Client(factory);

        var warning = Assert.Single(factory.Logs.Lines, line => line.Contains("TAMIZA_ASSISTANT_API_KEY") && line.StartsWith("Warning ", StringComparison.Ordinal));
        Assert.DoesNotContain("TAMIZA_ASSISTANT_MODEL is", warning);
        Assert.DoesNotContain(AssistantStub.Key, warning);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Says_nothing_at_startup_when_none_or_all_of_the_three_are_set(int set)
    {
        var settings = set == 0 ? new Dictionary<string, string?>() : _stub.Settings();
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(), settings);
        Client(factory);

        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("AI assistant is off"));
    }

    [Fact]
    public async Task A_malformed_setting_stops_the_api_naming_the_variable()
    {
        await using var factory = new TamizaApiFactory(await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { ["TAMIZA_ASSISTANT_MAX_CONTEXT_KB"] = "16" });

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("TAMIZA_ASSISTANT_MAX_CONTEXT_KB", failure.ToString());
    }

    private static Dictionary<string, string?> Without(Dictionary<string, string?> settings, string name)
    {
        settings.Remove(name);
        return settings;
    }

    private static void AssertFailure(AssistantResult result, AssistantFailureKind kind, string? expectedCode, bool mayHaveWorked)
    {
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Equal(kind, result.Failure.Kind);
        Assert.Equal(expectedCode, result.Failure.Code);
        Assert.Equal(mayHaveWorked, result.Failure.ProviderMayHaveWorked);
    }

    public void Dispose() => _stub.Dispose();
}
