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
