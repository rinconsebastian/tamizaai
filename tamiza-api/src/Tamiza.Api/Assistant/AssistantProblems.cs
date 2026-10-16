using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Tamiza.Api.Http;

namespace Tamiza.Api.Assistant;

/// <summary>
/// The Problem Details the assistant's routes answer with (design D10). The UI translates the <c>code</c>; the title and detail are
/// for people reading the response, and never hold what the provider said.
/// </summary>
public static class AssistantProblems
{
    public static ProblemHttpResult Disabled() =>
        Problems.Status(StatusCodes.Status503ServiceUnavailable, AssistantErrorCodes.Disabled,
            "The assistant is not available.", "The AI assistant is not configured for this deployment.");

    public static ProblemHttpResult Misconfigured() =>
        Problems.Status(StatusCodes.Status503ServiceUnavailable, AssistantErrorCodes.Misconfigured,
            "The assistant is misconfigured.", "The provider refused the key, the model or a request field. Contact the administrator.");

    public static ProblemHttpResult Unavailable() =>
        Problems.Status(StatusCodes.Status502BadGateway, AssistantErrorCodes.Unavailable,
            "The provider could not answer.", "The provider could not be reached or gave no usable answer.");

    public static ProblemHttpResult Timeout() =>
        Problems.Status(StatusCodes.Status504GatewayTimeout, AssistantErrorCodes.Timeout,
            "The provider did not answer in time.", "The provider did not answer within the configured time.");

    /// <summary>429 with <c>retryAfter</c> in seconds and the <c>Retry-After</c> header when the provider said how long to wait.</summary>
    public static IResult RateLimited(int? retryAfterSeconds)
    {
        var extensions = retryAfterSeconds is { } seconds ? new Dictionary<string, object?> { ["retryAfter"] = seconds } : null;
        var problem = Problems.Status(StatusCodes.Status429TooManyRequests, AssistantErrorCodes.RateLimited,
            "The provider is limiting requests.", "The provider is limiting the requests it accepts. Try again later.", extensions);
        return retryAfterSeconds is { } after ? new ProblemWithRetryAfter(problem, after) : problem;
    }

    /// <summary>429 with <c>renewsAt</c>, the instant the user's daily budget starts again.</summary>
    public static ProblemHttpResult Budget(DateTimeOffset renewsAt) =>
        Problems.Status(StatusCodes.Status429TooManyRequests, AssistantErrorCodes.Budget,
            "The daily budget is spent.", $"The daily token budget is spent. It renews at {renewsAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC.",
            new Dictionary<string, object?> { ["renewsAt"] = renewsAt });

    public static ProblemHttpResult Busy() =>
        Problems.Status(StatusCodes.Status409Conflict, AssistantErrorCodes.Busy,
            "A request is already in progress.", "Wait for your other assistant request to end, or cancel it.");

    public static ProblemHttpResult TestStale() =>
        Problems.Status(StatusCodes.Status409Conflict, AssistantErrorCodes.TestStale,
            "The test does not match the request.", "The test belongs to another script or ran other text. Test again first.");

    public static ProblemHttpResult ContextTooLarge() =>
        Problems.Status(StatusCodes.Status422UnprocessableEntity, AssistantErrorCodes.ContextTooLarge,
            "The script is too large for the assistant.", "The script and its context are larger than the assistant accepts.");

    /// <summary>The response for a call that ended without an answer. A cancelled call has no response to give: nobody is listening.</summary>
    public static IResult Of(AssistantFailure failure) => failure.Kind switch
    {
        AssistantFailureKind.Misconfigured => Misconfigured(),
        AssistantFailureKind.RateLimited => RateLimited(failure.RetryAfterSeconds),
        AssistantFailureKind.Timeout => Timeout(),
        AssistantFailureKind.Cancelled => TypedResults.StatusCode(ClientClosedRequest),
        _ => Unavailable(),
    };

    /// <summary>The status nginx uses for a request the client abandoned; only logged, never delivered.</summary>
    public const int ClientClosedRequest = 499;

    /// <summary>A Problem Details response that also sets the <c>Retry-After</c> header, in seconds.</summary>
    private sealed class ProblemWithRetryAfter(ProblemHttpResult problem, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return problem.ExecuteAsync(httpContext);
        }
    }
}
