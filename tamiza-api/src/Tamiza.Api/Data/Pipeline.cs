namespace Tamiza.Api.Data;

/// <summary>An immutable version of one of a project's scripts. <see cref="Code"/> is null when the script was removed.</summary>
public sealed class ScriptVersion
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public ScriptKind Kind { get; set; }

    /// <summary>The indicator's name, which is also its table's name; null for the clean and output scripts.</summary>
    public string? Name { get; set; }

    /// <summary>1, 2, 3... per project and kind, and per name for indicators.</summary>
    public int Number { get; set; }

    public string? Code { get; set; }

    /// <summary>The indicator's metadata at this version; null for the clean and output scripts and for a removed indicator.</summary>
    public string? Label { get; set; }

    public string? Description { get; set; }

    public string? Unit { get; set; }

    /// <summary><c>count</c>, <c>decimal</c>, <c>percent</c> or <c>currency</c>.</summary>
    public string? Format { get; set; }

    public Guid AuthorId { get; set; }

    /// <summary>Where the version was saved: the editor (<c>app</c>) or an AI client (<c>mcp</c>).</summary>
    public ScriptOrigin Origin { get; set; } = ScriptOrigin.App;

    public DateTimeOffset CreatedAt { get; set; }

    public User? Author { get; set; }
}

/// <summary>The scripts of the chain: 1 <c>clean</c>, 2 <c>prepare_output</c>, and any number of <c>compute</c> indicators.</summary>
public enum ScriptKind
{
    Clean,
    Output,
    Indicator,
}

/// <summary>One execution of the chain. Queued rows are the analytics worker's queue.</summary>
public sealed class Run
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public RunTrigger Trigger { get; set; }

    public RunStatus Status { get; set; }

    public Guid? RequestedBy { get; set; }

    /// <summary>The script versions the worker resolved when it claimed the run.</summary>
    public Guid? CleanVersionId { get; set; }

    public Guid? OutputVersionId { get; set; }

    /// <summary>The version of each indicator script the worker resolved when it claimed the run, in name order.</summary>
    public List<RunIndicatorVersion> IndicatorVersions { get; set; } = [];

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary><c>step.&lt;name&gt;</c> for the first failed step, or <c>run.interrupted</c>.</summary>
    public string? ErrorCode { get; set; }

    public string? Error { get; set; }

    /// <summary>The reference tables the worker read when the run started.</summary>
    public List<RunReferenceTable> ReferenceTables { get; set; } = [];

    public User? RequestedByUser { get; set; }

    public List<RunStep> Steps { get; set; } = [];
}

public enum RunTrigger
{
    Manual,
    Sync,
    Schedule,
}

public enum RunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>One step of a run, written by the analytics worker.</summary>
public sealed class RunStep
{
    public Guid RunId { get; set; }

    public RunStepName Step { get; set; }

    /// <summary>The indicator's name for an <see cref="RunStepName.Indicator"/> step; empty for the three fixed steps.</summary>
    public string Name { get; set; } = "";

    public RunStepStatus Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public long? InputRows { get; set; }

    public long? OutputRows { get; set; }

    /// <summary>The tables the step produced, with their row counts.</summary>
    public List<StepTable> Tables { get; set; } = [];

    public string Log { get; set; } = "";

    public string? Error { get; set; }
}

public sealed record StepTable(string Name, long Rows);

/// <summary>An indicator script version a run used, as the worker recorded it.</summary>
public sealed record RunIndicatorVersion(string Name, Guid VersionId, int Number);

public enum RunStepName
{
    Flatten,
    Clean,
    Output,
    Indicator,
}

public enum RunStepStatus
{
    Succeeded,
    Failed,
    Skipped,
}
