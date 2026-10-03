using Tamiza.Api.Data;

namespace Tamiza.Api.Pipeline;

public sealed record Person(Guid Id, string Name);

/// <summary>A script version without its code, for history lists.</summary>
public sealed record ScriptVersionSummary(Guid VersionId, ScriptKind Kind, int Number, bool Removed, Person Author, DateTimeOffset CreatedAt);

public sealed record ScriptVersionDetails(
    Guid VersionId, ScriptKind Kind, int Number, bool Removed, string? Code, Person Author, DateTimeOffset CreatedAt);

/// <summary>Each of the three scripts with its current version, or null when it was never saved.</summary>
public sealed record ProjectScript(ScriptKind Kind, ScriptVersionDetails? Current);

public sealed record SaveScriptRequest(string? Code);

public sealed record StepSummary(RunStepName Step, RunStepStatus Status, long? InputRows, long? OutputRows);

public sealed record StepDetails(
    RunStepName Step,
    RunStepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? InputRows,
    long? OutputRows,
    IReadOnlyList<StepTable> Tables,
    string Log,
    string? Error);

/// <summary>The version number of each script a run used; null when it had none.</summary>
public sealed record RunVersions(int? Clean, int? Output, int? Indicators);

public sealed record RunSummary(
    Guid Id,
    RunTrigger Trigger,
    RunStatus Status,
    Person? RequestedBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    RunVersions Versions,
    IReadOnlyList<StepSummary> Steps,
    string? ErrorCode,
    string? Error);

public sealed record RunDetails(RunSummary Run, IReadOnlyList<StepDetails> Steps);

public sealed record RunPage(IReadOnlyList<RunSummary> Items, int Total);

/// <summary>The full pipeline settings; <see cref="RunIntervalHours"/> null means no schedule.</summary>
public sealed record PipelineSettings(bool RunAfterSync, int? RunIntervalHours, DateTimeOffset? NextRunAt);

public sealed record PipelineSettingsRequest(bool? RunAfterSync, int? RunIntervalHours);
