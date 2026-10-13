using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

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

/// <summary>A run with every step and the reference tables it read when it started.</summary>
public sealed record RunDetails(RunSummary Run, IReadOnlyList<StepDetails> Steps, IReadOnlyList<RunReferenceTable> ReferenceTables);

public sealed record RunPage(IReadOnlyList<RunSummary> Items, int Total);

/// <summary>The full pipeline settings; <see cref="RunIntervalHours"/> null means no schedule.</summary>
public sealed record PipelineSettings(bool RunAfterSync, int? RunIntervalHours, DateTimeOffset? NextRunAt);

public sealed record PipelineSettingsRequest(bool? RunAfterSync, int? RunIntervalHours);

/// <summary>A reference table without its rows.</summary>
public sealed record ReferenceTableSummary(
    string Name, string FileName, int RowCount, IReadOnlyList<ReferenceColumn> Columns, Person UpdatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>An uploaded or replaced table, with the headers whose column names differ from them.</summary>
public sealed record ReferenceTableUpload(ReferenceTableSummary Table, IReadOnlyList<RenamedColumn> Renamed);

/// <summary>A reference table with its first rows, values in column order.</summary>
public sealed record ReferenceTablePreview(ReferenceTableSummary Table, IReadOnlyList<List<JsonElement>> Rows);

public sealed record ScriptTestRequest(string? Kind, string? Code, int? SampleSize);

/// <summary>What a queued test waits for: a running run, or another test that runs or is queued before it.</summary>
public enum ScriptTestWait
{
    Run,
    Test,
}

/// <summary>
/// A test and, as its steps end, their logs and table previews. <see cref="CleanVersion"/> is the saved script 1 it ran
/// before an output or indicators draft.
/// </summary>
public sealed record ScriptTestDetails(
    Guid Id,
    ScriptKind Kind,
    RunStatus Status,
    int SampleSize,
    string Code,
    ScriptTestWait? WaitingFor,
    int? CleanVersion,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? Error,
    IReadOnlyList<TestStep> Steps);
