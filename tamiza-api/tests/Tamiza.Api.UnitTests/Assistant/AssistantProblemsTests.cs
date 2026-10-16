using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Assistant;
using Tamiza.Api.Http;

namespace Tamiza.Api.UnitTests.Assistant;

/// <summary>The Problem Details of design D10: status, code, title and extensions of each, and the Retry-After header.</summary>
public sealed class AssistantProblemsTests
{
    /// <summary>Runs the result the way the framework does and returns the response.</summary>
    private static async Task<(DefaultHttpContext Http, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddOptions().ConfigureHttpJsonOptions(_ => { }).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        return (http, http.Response.Body.Length == 0 ? default : await JsonSerializer.DeserializeAsync<JsonElement>(http.Response.Body));
    }

    public static TheoryData<string, int, string> Simple() => new()
    {
        { nameof(AssistantProblems.Disabled), 503, "assistant.disabled" },
        { nameof(AssistantProblems.Misconfigured), 503, "assistant.misconfigured" },
        { nameof(AssistantProblems.Unavailable), 502, "assistant.unavailable" },
        { nameof(AssistantProblems.Timeout), 504, "assistant.timeout" },
        { nameof(AssistantProblems.Busy), 409, "assistant.busy" },
        { nameof(AssistantProblems.TestStale), 409, "assistant.testStale" },
        { nameof(AssistantProblems.ContextTooLarge), 422, "assistant.contextTooLarge" },
    };

    [Theory]
    [MemberData(nameof(Simple))]
    public async Task Each_problem_has_its_status_and_code_and_a_title_and_detail_in_English(string method, int status, string code)
    {
        var result = (ProblemHttpResult)typeof(AssistantProblems).GetMethod(method)!.Invoke(null, null)!;

        var (http, body) = await ExecuteAsync(result);

        Assert.Equal(status, http.Response.StatusCode);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
        // Only the code is machine-readable here, next to the standard members.
        Assert.Equal(["code", "detail", "status", "title", "type"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Rate_limited_carries_retry_after_in_the_body_and_the_header()
    {
        var (http, body) = await ExecuteAsync(AssistantProblems.RateLimited(30));

        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("assistant.rateLimited", body.GetProperty("code").GetString());
        Assert.Equal(30, body.GetProperty("retryAfter").GetInt32());
        Assert.Equal("30", http.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Rate_limited_without_a_hint_has_no_retry_after()
    {
        var (http, body) = await ExecuteAsync(AssistantProblems.RateLimited(null));

        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("assistant.rateLimited", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("retryAfter", out _));
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Budget_says_when_it_renews()
    {
        var renews = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        var (http, body) = await ExecuteAsync(AssistantProblems.Budget(renews));

        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("assistant.budget", body.GetProperty("code").GetString());
        Assert.Equal(renews, body.GetProperty("renewsAt").GetDateTimeOffset());
        Assert.Contains("2026-10-17 00:00 UTC", body.GetProperty("detail").GetString());
        Assert.False(http.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public void Status_adds_the_code_and_any_extensions_to_the_problem()
    {
        var problem = Problems.Status(418, "x.teapot", "A title", "A detail", new Dictionary<string, object?> { ["more"] = 7 });

        Assert.Equal(418, problem.StatusCode);
        Assert.Equal("A title", problem.ProblemDetails.Title);
        Assert.Equal("A detail", problem.ProblemDetails.Detail);
        Assert.Equal("x.teapot", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(7, problem.ProblemDetails.Extensions["more"]);
    }

    [Fact]
    public void Status_without_extensions_has_only_the_code()
    {
        var problem = Problems.Status(409, "x.busy", "T", "D");

        Assert.Equal(["code"], problem.ProblemDetails.Extensions.Keys.ToArray());
    }

    [Theory]
    [InlineData(AssistantFailureKind.Unavailable, 502, "assistant.unavailable")]
    [InlineData(AssistantFailureKind.Misconfigured, 503, "assistant.misconfigured")]
    [InlineData(AssistantFailureKind.RateLimited, 429, "assistant.rateLimited")]
    [InlineData(AssistantFailureKind.Timeout, 504, "assistant.timeout")]
    public async Task A_failure_of_the_provider_maps_to_its_problem(AssistantFailureKind kind, int status, string code)
    {
        var failure = new AssistantFailure(kind, code, kind == AssistantFailureKind.RateLimited ? 12 : null, ProviderMayHaveWorked: false);

        var (http, body) = await ExecuteAsync(AssistantProblems.Of(failure));

        Assert.Equal(status, http.Response.StatusCode);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(kind == AssistantFailureKind.RateLimited, http.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task A_cancelled_call_has_no_problem_to_report()
    {
        var (http, _) = await ExecuteAsync(AssistantProblems.Of(new AssistantFailure(AssistantFailureKind.Cancelled, null, null, true)));

        Assert.Equal(AssistantProblems.ClientClosedRequest, http.Response.StatusCode);
    }
}
