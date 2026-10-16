using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Assistant;

/// <summary>One turn of the conversation as the browser sends it: <see cref="Role"/> is <c>user</c> or <c>assistant</c>.</summary>
public sealed record AssistantTurn(string? Role, string? Content);

/// <summary>The cell a <c>cell</c> request rewrites: the 1-based line of its <c># %%</c> marker in the draft.</summary>
public sealed record AssistantFocus(int? Line);

/// <summary>The dashboard warning a <c>fix</c> asks about. Only the params its kind uses are kept (see <see cref="AssistantWarning.Create"/>).</summary>
public sealed record AssistantWarningInput(string? Key, Dictionary<string, JsonElement>? Params);

/// <summary>The body of <c>POST …/assistant/context</c> and <c>POST …/assistant/completions</c> (design D3).</summary>
public sealed record AssistantRequest(
    string? Kind,
    string? Name,
    string? Action,
    List<AssistantTurn>? Messages,
    string? Draft,
    AssistantFocus? Focus,
    IndicatorDetails? Details,
    Guid? TestId,
    AssistantWarningInput? Warning);

/// <summary>The body of <c>PUT …/assistant/calls/{callId}/outcome</c>: <paramref name="Applied"/> is <c>script</c>, <c>cell</c> or <c>insert</c>.</summary>
public sealed record AssistantOutcomeRequest(string? Applied);

/// <summary>What the disclosure line of the Assistant tab shows. The key is never returned.</summary>
public sealed record AssistantInfo(string Model, string Host);

/// <summary>The request the assistant would send, as text, without sending it.</summary>
/// <param name="Bytes">The UTF-8 size of the system message and the turns.</param>
/// <param name="Omitted">What was left out to fit the configured context size.</param>
/// <param name="TestExpired">A fix named a test that is no longer available, so the text holds no test facts.</param>
public sealed record AssistantContextResponse(string Model, string Host, string Text, long Bytes, IReadOnlyList<string> Omitted, bool TestExpired);

/// <summary>The tokens a request used. When <see cref="Estimated"/>, only <see cref="TotalTokens"/> is set, from the size of what was sent and received.</summary>
public sealed record AssistantTokens(int? PromptTokens, int? CompletionTokens, int? TotalTokens, bool Estimated);

/// <summary>The answer to a completion: the explanation, the proposed code and what the request cost.</summary>
/// <param name="CallId">The record of the request; the browser reports how the proposal was applied against it.</param>
/// <param name="Explanation">The answer's text with each code block replaced by a note such as <c>[proposed code: 34 lines]</c>.</param>
/// <param name="Truncated">The provider stopped at its output limit, so the last block may be incomplete.</param>
/// <param name="TestExpired">A fix named a test that is no longer available, so the request went without test facts.</param>
public sealed record AssistantCompletionResponse(
    Guid CallId,
    string Explanation,
    IReadOnlyList<AssistantBlock> Blocks,
    bool Truncated,
    bool TestExpired,
    IReadOnlyList<string> Omitted,
    AssistantTokens Usage);

/// <summary>
/// The routes of the script assistant (design D3). They are open to analysts and admins of the project and answer 503
/// <c>assistant.disabled</c> while the deployment has not configured a provider.
/// </summary>
public static class AssistantEndpoints
{
    public const int MaxTurns = 39;
    public const int MaxTurnBytes = 16 * 1024;
    public const int MaxTurnsBytes = 64 * 1024;
    public const int MaxDraftBytes = 64 * 1024;

    private static readonly string[] Actions = ["script", "cell", "fix"];
    private static readonly string[] Outcomes = ["script", "cell", "insert"];

    public static RouteGroupBuilder MapAssistantEndpoints(this RouteGroupBuilder api)
    {
        var assistant = api.MapGroup("/projects/{projectId:guid}/assistant").WithTags("Assistant");
        assistant.MapGet("", GetInfo).RequireProjectRole(ProjectRole.Analyst).WithName("GetAssistant")
            .Produces<AssistantInfo>().ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        assistant.MapPost("/context", ContextAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetAssistantContext")
            .Produces<AssistantContextResponse>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        assistant.MapPost("/completions", CompleteAsync).RequireProjectRole(ProjectRole.Analyst).WithName("CompleteAssistant")
            .Produces<AssistantCompletionResponse>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).ProducesProblem(StatusCodes.Status504GatewayTimeout);
        assistant.MapPut("/calls/{callId:guid}/outcome", RecordOutcomeAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RecordAssistantOutcome")
            .Produces(StatusCodes.Status204NoContent).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return api;
    }

    private static IResult GetInfo(IOptions<AssistantOptions> options) =>
        options.Value.Enabled
            ? TypedResults.Ok(new AssistantInfo(options.Value.Model, options.Value.Host!))
            : AssistantProblems.Disabled();

    /// <summary>The exact request the next completion would send, for "Show what will be sent" and "Copy context". No provider call, no record, no budget.</summary>
    private static async Task<IResult> ContextAsync(
        Guid projectId, AssistantRequest? body, IOptions<AssistantOptions> options, ContextAssembler assembler,
        TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return AssistantProblems.Disabled();
        }

        var prepared = await PrepareAsync(projectId, body, forCompletion: false, settings, assembler, db, user, time, ct);
        return prepared.Error ?? TypedResults.Ok(new AssistantContextResponse(
            settings.Model, settings.Host!, prepared.Assembled!.Text, prepared.Assembled.Bytes, prepared.Assembled.Omitted, prepared.TestExpired));
    }

    private static async Task<IResult> CompleteAsync(
        Guid projectId, AssistantRequest? body, IOptions<AssistantOptions> options, ContextAssembler assembler, AssistantClient client,
        AssistantLedger ledger, TamizaDbContext db, ICurrentUser user, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return AssistantProblems.Disabled();
        }

        var prepared = await PrepareAsync(projectId, body, forCompletion: true, settings, assembler, db, user, time, ct);
        if (prepared.Error is not null)
        {
            return prepared.Error;
        }

        var assembled = prepared.Assembled!;
        var started = time.GetUtcNow();
        var used = await ledger.TokensTodayAsync(user.Id, started, ct);
        if (used >= settings.DailyTokensPerUser)
        {
            return AssistantProblems.Budget(AssistantLedger.DayStart(started).AddDays(1));
        }

        var call = new AssistantCall
        {
            Id = Guid.CreateVersion7(started),
            ProjectId = projectId,
            UserId = user.Id,
            Kind = prepared.Kind,
            Name = prepared.Name,
            Action = prepared.Action,
            TestId = prepared.Action == AssistantAction.Fix ? body!.TestId : null,
            Model = settings.Model.Length > 200 ? settings.Model[..200] : settings.Model,
            PromptKit = AssistantPrompts.Version,
            Context = new AssistantCallContext(new Dictionary<string, int>(assembled.BlockBytes), [.. assembled.Omitted], assembled.HasTest),
            Status = AssistantCallStatus.Running,
            StartedAt = started,
        };
        if (!await ledger.BeginAsync(call, ct))
        {
            return AssistantProblems.Busy();
        }

        AssistantResult? result = null;
        try
        {
            result = await client.CompleteAsync(assembled.Messages, ct);
        }
        finally
        {
            // The record must end even when the caller went away: it frees the user's one request and holds the cost.
            using var fresh = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await ledger.FinishAsync(call, result, cancelled: ct.IsCancellationRequested, time.GetUtcNow(), fresh.Token);
            }
            catch (Exception exception) when (result is not null)
            {
                // Do not turn an answer the provider gave into an error because its record could not be closed.
                loggers.CreateLogger("Tamiza.Api.Assistant.AssistantEndpoints")
                    .LogError(exception, "The record of AI assistant request {CallId} could not be closed.", call.Id);
            }
        }

        if (result.Failure is { } failure)
        {
            return AssistantProblems.Of(failure);
        }

        var answer = result.Answer!;
        var split = AssistantAnswerParser.Split(answer.Text);
        var tokens = call.TokensEstimated
            ? new AssistantTokens(null, null, call.TotalTokens, Estimated: true)
            : new AssistantTokens(answer.PromptTokens, answer.CompletionTokens, answer.Tokens, Estimated: false);
        return TypedResults.Ok(new AssistantCompletionResponse(
            call.Id, split.Explanation, split.Blocks, answer.Truncated, prepared.TestExpired, assembled.Omitted, tokens));
    }

    private static async Task<IResult> RecordOutcomeAsync(
        Guid projectId, Guid callId, AssistantOutcomeRequest? body, IOptions<AssistantOptions> options, TamizaDbContext db, ICurrentUser user, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return AssistantProblems.Disabled();
        }

        if (body?.Applied is not { } applied || !Outcomes.Contains(applied, StringComparer.OrdinalIgnoreCase))
        {
            return Problems.Validation("applied", "Say how the proposal was applied: script, cell or insert.");
        }

        var outcome = Enum.Parse<AssistantOutcome>(applied, ignoreCase: true);
        var updated = await db.AssistantCalls
            .Where(c => c.Id == callId && c.ProjectId == projectId && c.UserId == user.Id && c.Status == AssistantCallStatus.Succeeded)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Outcome, outcome), ct);
        return updated == 0
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Request not found.")
            : TypedResults.NoContent();
    }

    /// <summary>What both routes that assemble a request share: validation, the fix's test and the assembly itself.</summary>
    private sealed record Prepared(
        IResult? Error, AssembledContext? Assembled, ScriptKind Kind, string? Name, AssistantAction Action, bool TestExpired)
    {
        public static Prepared Fail(IResult error) => new(error, null, default, null, default, false);
    }

    private static async Task<Prepared> PrepareAsync(
        Guid projectId, AssistantRequest? body, bool forCompletion, AssistantOptions settings, ContextAssembler assembler,
        TamizaDbContext db, ICurrentUser user, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var valid = Validate(body, forCompletion, errors);
        if (errors.Any || valid is null)
        {
            return Prepared.Fail(Problems.Validation(errors.ToDictionary()));
        }

        var (kind, name, action) = (valid.Kind, valid.Name, valid.Action);
        if (Encoding.UTF8.GetByteCount(valid.Draft) > MaxDraftBytes)
        {
            return Prepared.Fail(AssistantProblems.ContextTooLarge());
        }

        // A fix uses only a test that this user started and that has not expired. A test that is gone is not an error: the request
        // goes without test facts and the browser is told. A test that is there but ran other text or another script is.
        ScriptTest? test = null;
        var testExpired = false;
        if (action == AssistantAction.Fix)
        {
            test = await ScriptTestEndpoints.FindOwnAsync(db, projectId, valid.TestId!.Value, user.Id, time.GetUtcNow(), ct);
            if (test is null)
            {
                testExpired = true;
            }
            else if (test.Kind != kind || test.Name != name || !string.Equals(test.Code, valid.Draft, StringComparison.Ordinal))
            {
                return Prepared.Fail(AssistantProblems.TestStale());
            }
        }

        var request = new ContextRequest(
            kind.ToString().ToLowerInvariant(), name, action.ToString().ToLowerInvariant(), valid.Turns, valid.Draft,
            valid.FocusLine, kind == ScriptKind.Indicator ? valid.Details : null, test, test is null ? null : valid.Warning);
        AssembledContext assembled;
        try
        {
            assembled = await assembler.AssembleAsync(projectId, request, settings.MaxContextKb * 1024L, ct);
        }
        catch (DataTimeoutException)
        {
            return Prepared.Fail(DataEndpoints.Timeout());
        }
        catch (ArgumentException) when (action == AssistantAction.Cell)
        {
            // The draft's line breaks differ from the editor's (a lone carriage return): the marker is not where the browser said.
            return Prepared.Fail(Problems.Validation("focus.line", "Choose a line that starts a cell with # %%."));
        }

        return assembled.Fits
            ? new Prepared(null, assembled, kind, name, action, testExpired)
            : Prepared.Fail(AssistantProblems.ContextTooLarge());
    }

    /// <summary>A request that passed validation, with its values in the form the assembler takes.</summary>
    private sealed record Valid(
        ScriptKind Kind, string? Name, AssistantAction Action, List<AssistantMessage> Turns, string Draft, int? FocusLine,
        IndicatorDetails? Details, Guid? TestId, AssistantWarning? Warning);

    private static Valid? Validate(AssistantRequest? body, bool forCompletion, FieldErrors errors)
    {
        if (body is null)
        {
            errors.Add("body", "Send the request as JSON.");
            return null;
        }

        var kind = ScriptKind.Clean;
        if (!Enum.TryParse(body.Kind, ignoreCase: true, out kind) || !Enum.IsDefined(kind) || int.TryParse(body.Kind, out _))
        {
            errors.Add("kind", "Choose the script: clean, output or indicator.");
        }

        string? name = null;
        if (kind == ScriptKind.Indicator)
        {
            name = body.Name;
            if (!ColumnNames.IsValidTableName(name))
            {
                errors.Add("name", "Name the indicator, using lowercase letters, digits and underscores, starting with a letter.");
            }
        }
        else if (!string.IsNullOrEmpty(body.Name))
        {
            errors.Add("name", "Only an indicator has a name.");
        }

        var action = AssistantAction.Script;
        if (body.Action is null || !Actions.Contains(body.Action, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("action", "Choose the action: script, cell or fix.");
        }
        else
        {
            action = Enum.Parse<AssistantAction>(body.Action, ignoreCase: true);
        }

        var draft = body.Draft ?? "";
        var turns = ValidateTurns(body.Messages, forCompletion, errors);

        int? focusLine = null;
        if (action == AssistantAction.Cell)
        {
            focusLine = body.Focus?.Line;
            if (focusLine is not { } line || !ScriptTestEndpoints.IsMarkerLine(draft, line))
            {
                errors.Add("focus.line", "Choose a line that starts a cell with # %%.");
            }
        }

        var details = ValidateDetails(body.Details, errors);

        AssistantWarning? warning = null;
        if (action == AssistantAction.Fix)
        {
            if (body.TestId is null || body.TestId == Guid.Empty)
            {
                errors.Add("testId", "A fix needs the test it refers to.");
            }

            if (body.Warning is { } input)
            {
                warning = AssistantWarning.Create(input.Key, input.Params);
                if (warning is null)
                {
                    errors.Add("warning.key", "Choose one of the dashboard warnings.");
                }
            }
        }

        return errors.Any ? null : new Valid(kind, name, action, turns, draft, focusLine, details, body.TestId, warning);
    }

    /// <summary>
    /// The conversation must alternate, start with the analyst and, for a completion, end with their non-blank request. The view of
    /// what will be sent also accepts none, or a blank last turn, which it leaves out.
    /// </summary>
    private static List<AssistantMessage> ValidateTurns(List<AssistantTurn>? messages, bool forCompletion, FieldErrors errors)
    {
        var turns = new List<AssistantMessage>();
        var sent = messages ?? [];
        if (sent.Any(t => t is null || t.Role is null || t.Content is null
                          || !(t.Role.Equals("user", StringComparison.OrdinalIgnoreCase) || t.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))))
        {
            errors.Add("messages", "Each message needs a role of user or assistant and some text.");
            return turns;
        }

        turns.AddRange(sent.Select(t => new AssistantMessage(t.Role!.ToLowerInvariant(), t.Content!)));
        if (!forCompletion && turns.Count > 0 && turns[^1] is { Role: "user" } last && string.IsNullOrWhiteSpace(last.Content))
        {
            turns.RemoveAt(turns.Count - 1);
        }

        if (forCompletion && turns.Count == 0)
        {
            errors.Add("messages", "Write a request.");
        }
        else if (turns.Count > MaxTurns)
        {
            errors.Add("messages", $"A request can carry at most {MaxTurns} messages.");
        }
        else if (turns.Where((t, index) => t.Role != (index % 2 == 0 ? "user" : "assistant")).Any())
        {
            errors.Add("messages", "The messages must alternate between you and the assistant, starting with you.");
        }
        else if (forCompletion && turns[^1].Role != "user")
        {
            errors.Add("messages", "The last message must be yours.");
        }
        else if (turns.Any(t => string.IsNullOrWhiteSpace(t.Content)))
        {
            errors.Add("messages", "A message cannot be blank.");
        }
        else if (turns.Any(t => Encoding.UTF8.GetByteCount(t.Content) > MaxTurnBytes))
        {
            errors.Add("messages", $"A message can be at most {MaxTurnBytes / 1024} KB.");
        }
        else if (turns.Sum(t => (long)Encoding.UTF8.GetByteCount(t.Content)) > MaxTurnsBytes)
        {
            errors.Add("messages", $"The messages can be at most {MaxTurnsBytes / 1024} KB together.");
        }

        return turns;
    }

    /// <summary>The details fields as the analyst has them, within the limits the details are saved with.</summary>
    private static IndicatorDetails? ValidateDetails(IndicatorDetails? details, FieldErrors errors)
    {
        if (details is null)
        {
            return null;
        }

        var label = details.Label?.Trim();
        var description = details.Description?.Trim();
        var unit = details.Unit?.Trim();
        var format = details.Format?.Trim().ToLowerInvariant();
        if (label is { Length: > ScriptEndpoints.MaxLabelLength })
        {
            errors.Add("details.label", $"The label can have up to {ScriptEndpoints.MaxLabelLength} characters.");
        }

        if (description is { Length: > ScriptEndpoints.MaxDescriptionLength })
        {
            errors.Add("details.description", $"The description can have up to {ScriptEndpoints.MaxDescriptionLength:N0} characters.");
        }

        if (unit is { Length: > ScriptEndpoints.MaxUnitLength })
        {
            errors.Add("details.unit", $"The unit can have up to {ScriptEndpoints.MaxUnitLength} characters.");
        }

        if (!string.IsNullOrEmpty(format) && !ScriptEndpoints.Formats.Contains(format))
        {
            errors.Add("details.format", $"Choose a format: {string.Join(", ", ScriptEndpoints.Formats)}.");
        }

        return new IndicatorDetails(label, description, unit, string.IsNullOrEmpty(format) ? null : format);
    }
}
