using ModelContextProtocol;
using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>
/// A tool call that Tamiza declines for a reason the client can act on: who may ask, what the project allows, a value the same rules as
/// the REST API refuse, or a guard on changes. It reaches the client as a tool result with <c>isError</c> and the body
/// <c>{ code, message, ... }</c>, never as a protocol error, and is recorded as <c>refused</c>.
/// </summary>
public sealed class McpRefusal(string code, string message, IReadOnlyDictionary<string, object?>? details = null) : McpException(message)
{
    public const string ProjectNotFoundCode = "mcp.projectNotFound";
    public const string ForbiddenCode = "mcp.forbidden";
    public const string ProjectOffCode = "mcp.projectOff";
    public const string RowsNotAllowedCode = "mcp.rowsNotAllowed";
    public const string ScriptChangedCode = "mcp.scriptChanged";
    public const string NameMismatchCode = "mcp.nameMismatch";
    public const string UnsafeCodeCode = "mcp.unsafeCode";
    public const string RateLimitedCode = "mcp.rateLimited";
    public const string ValidationCode = "mcp.validation";
    public const string NotFoundCode = "mcp.notFound";
    public const string FailedCode = "mcp.failed";
    public const string TimeoutCode = "mcp.timeout";

    public string Code { get; } = code;

    /// <summary>Further machine-readable facts: the field errors of a validation, the current version of a script, the seconds to wait.</summary>
    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? new Dictionary<string, object?>();

    /// <summary>The same answer as for a project that does not exist, so a client learns nothing about projects of others.</summary>
    public static McpRefusal ProjectNotFound() => new(ProjectNotFoundCode, "Project not found.");

    public static McpRefusal Forbidden(ProjectRole minimum) =>
        new(ForbiddenCode, $"This needs the {minimum.ToString().ToLowerInvariant()} role in the project.", new Dictionary<string, object?> { ["role"] = minimum.ToString().ToLowerInvariant() });

    public static McpRefusal ProjectOff() =>
        new(ProjectOffCode, "AI clients cannot use this project. An admin of the project can allow it in Settings > General.");

    public static McpRefusal RowsNotAllowed() =>
        new(RowsNotAllowedCode, "Reading rows is not allowed for this project. The project's admin (Settings > General) and the operator of the deployment decide it; summaries are available.");

    public static McpRefusal Validation(IDictionary<string, string[]> errors) =>
        new(ValidationCode, "A value was refused: " + string.Join(" ", errors.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}"))),
            new Dictionary<string, object?> { ["fields"] = errors });

    public static McpRefusal NotFound(string what = "The item", string? hint = null) =>
        new(NotFoundCode, $"{what} was not found." + (hint is null ? "" : " " + hint));

    public static McpRefusal Failed(string message, int? status = null, string? code = null) =>
        new(code ?? FailedCode, message, status is null ? null : new Dictionary<string, object?> { ["status"] = status });

    public static McpRefusal Timeout(string hint) => new(TimeoutCode, "The read took too long and was stopped. " + hint);

    public static McpRefusal RateLimited(int retryAfterSeconds) =>
        new(RateLimitedCode, $"Too many calls. Wait {retryAfterSeconds} seconds and call again.", new Dictionary<string, object?> { ["retryAfter"] = retryAfterSeconds });
}
