using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Turns what a REST handler returns into what a tool gives (design D6): the value of a success, or an <see cref="McpRefusal"/> that
/// carries the same code, field errors and message the endpoint gives. The handlers run unchanged, so their rules cannot drift.
/// </summary>
public static class McpResults
{
    /// <summary>The value of a successful result, or null when it has none (a 204, or an accepted request without a body). Throws <see cref="McpRefusal"/> otherwise.</summary>
    public static object? Unwrap(IResult result)
    {
        switch (result)
        {
            case INestedHttpResult nested:
                return Unwrap(nested.Result);

            case ValidationProblem validation:
                throw McpRefusal.Validation(validation.ProblemDetails.Errors);

            case ProblemHttpResult problem:
                throw Refusal(problem.ProblemDetails);

            case IValueHttpResult valued when (result as IStatusCodeHttpResult)?.StatusCode is null or (>= 200 and < 300):
                return valued.Value;

            case IStatusCodeHttpResult { StatusCode: >= 200 and < 300 }:
                return null;

            case IStatusCodeHttpResult { StatusCode: StatusCodes.Status404NotFound }:
                throw McpRefusal.NotFound();

            case IStatusCodeHttpResult status:
                throw McpRefusal.Failed($"The request was not accepted ({status.StatusCode}).", status.StatusCode);

            default:
                throw McpRefusal.Failed("The request gave no usable result.");
        }
    }

    /// <summary><see cref="Unwrap"/> for a handler that returns <typeparamref name="T"/> when it succeeds.</summary>
    public static T Unwrap<T>(IResult result) =>
        Unwrap(result) is T value ? value : throw McpRefusal.Failed("The request gave no result.");

    private static McpRefusal Refusal(ProblemDetails problem)
    {
        var code = problem.Extensions.TryGetValue("code", out var raw) ? raw?.ToString() : null;
        var message = string.IsNullOrWhiteSpace(problem.Detail) ? problem.Title ?? "The request was refused." : problem.Detail;
        var details = problem.Extensions
            .Where(e => e.Key != "code")
            .ToDictionary(e => e.Key, e => (object?)e.Value);
        if (problem.Status is { } status)
        {
            details["status"] = status;
        }

        return problem.Status == StatusCodes.Status404NotFound && code is null
            ? McpRefusal.NotFound(problem.Title is { Length: > 0 } title ? title.TrimEnd('.') : "The item")
            : new McpRefusal(code ?? McpRefusal.FailedCode, message, details);
    }
}
