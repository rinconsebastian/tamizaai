using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Pipeline;

public sealed record Person(Guid Id, string Name);

/// <summary>A script version without its code, for history lists. <c>Name</c> and <c>Label</c> are set for indicators.</summary>
public sealed record ScriptVersionSummary(
    Guid VersionId, ScriptKind Kind, string? Name, string? Label, int Number, bool Removed, Person Author, DateTimeOffset CreatedAt);

/// <summary>A version with its code and, for an indicator, its metadata (null when the script was removed).</summary>
public sealed record ScriptVersionDetails(
    Guid VersionId,
    ScriptKind Kind,
    string? Name,
    int Number,
    bool Removed,
    string? Code,
    string? Label,
    string? Description,
    string? Unit,
    string? Format,
    Person Author,
    DateTimeOffset CreatedAt);

/// <summary>Script 1 or script 2 with its current version, or null when it was never saved.</summary>
public sealed record ProjectScript(ScriptKind Kind, ScriptVersionDetails? Current);

/// <summary>An indicator with the metadata and code of its current version.</summary>
public sealed record IndicatorScript(string Name, string Label, string? Description, string? Unit, string Format, ScriptVersionDetails Current);

/// <summary>The scripts of a project: the two fixed ones and its current indicators, ordered by name.</summary>
public sealed record ScriptList(ProjectScript Clean, ProjectScript Output, IReadOnlyList<IndicatorScript> Indicators);

public sealed record SaveScriptRequest(string? Code);

public sealed record SaveIndicatorRequest(string? Code, string? Label, string? Description, string? Unit, string? Format);

/// <summary><c>Name</c> is the indicator's name for an indicator step and empty for the three fixed steps.</summary>
public sealed record StepSummary(RunStepName Step, string Name, RunStepStatus Status, long? InputRows, long? OutputRows);

/// <summary><c>Label</c> is the label the indicator had in the version the run used.</summary>
public sealed record StepDetails(
    RunStepName Step,
    string Name,
    string? Label,
    RunStepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? InputRows,
    long? OutputRows,
    IReadOnlyList<StepTable> Tables,
    string Log,
    string? Error);

/// <summary>The version number of script 1 and script 2 a run used (null when it had none) and of each indicator, by name.</summary>
public sealed record RunVersions(int? Clean, int? Output, IReadOnlyList<RunIndicatorNumber> Indicators);

public sealed record RunIndicatorNumber(string Name, int Number);

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

/// <summary><c>Name</c> is the indicator to test as, required when <c>Kind</c> is <c>indicator</c>.</summary>
/// <param name="StopAtLine">The 1-based line of a <c># %%</c> marker of <paramref name="Code"/> to run through; omit for a whole test.</param>
public sealed record ScriptTestRequest(string? Kind, string? Name, string? Code, int? SampleSize, int? StopAtLine = null);

/// <summary>What a queued test waits for: a running run, or another test that runs or is queued before it.</summary>
public enum ScriptTestWait
{
    Run,
    Test,
}

/// <summary>
/// A test and, as its steps end, their logs and table previews. <see cref="CleanVersion"/> is the saved script 1 it ran
/// before an output or indicator draft.
/// </summary>
public sealed record ScriptTestDetails(
    Guid Id,
    ScriptKind Kind,
    string? Name,
    RunStatus Status,
    int SampleSize,
    int? StopAtLine,
    string Code,
    ScriptTestWait? WaitingFor,
    int? CleanVersion,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorCode,
    string? Error,
    IReadOnlyList<TestStep> Steps);
