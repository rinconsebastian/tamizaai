using Microsoft.AspNetCore.Http.HttpResults;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Http;

/// <summary>Problem Details responses with the machine-readable <c>code</c> extension the UI translates.</summary>
public static class Problems
{
    public static ValidationProblem Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static ValidationProblem Validation(IDictionary<string, string[]> errors) => TypedResults.ValidationProblem(errors);

    public static ProblemHttpResult Kobo(KoboError error) =>
        TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The Kobo connection check failed.",
            detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static ProblemHttpResult Conflict(string code, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The request conflicts with the project's current state.",
            detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Unavailable(string code, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The request could not be completed in time.",
            detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// A Problem Details response with any status, the <c>code</c> the UI translates and any further machine-readable
    /// <paramref name="extensions"/> (for example <c>retryAfter</c>). <see cref="Unavailable"/> fits only when the title "could not be
    /// completed in time" does.
    /// </summary>
    public static ProblemHttpResult Status(
        int status, string code, string title, string detail, IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var all = new Dictionary<string, object?> { ["code"] = code };
        foreach (var (name, value) in extensions ?? new Dictionary<string, object?>())
        {
            all[name] = value;
        }

        return TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: all);
    }
}

/// <summary>Collects field errors keyed by camelCase path.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }

        messages.Add(message);
    }

    public IDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
