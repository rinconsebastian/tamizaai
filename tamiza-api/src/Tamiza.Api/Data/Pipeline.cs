namespace Tamiza.Api.Data;

/// <summary>An immutable version of one of a project's scripts. <see cref="Code"/> is null when the script was removed.</summary>
public sealed class ScriptVersion
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public ScriptKind Kind { get; set; }

    /// <summary>1, 2, 3... per project and kind.</summary>
    public int Number { get; set; }

    public string? Code { get; set; }

    public Guid AuthorId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? Author { get; set; }
}

/// <summary>The three scripts of the chain: 1 <c>clean</c>, 2 <c>prepare_output</c>, 3 <c>compute_indicators</c>.</summary>
public enum ScriptKind
{
    Clean,
    Output,
    Indicators,
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

    public Guid? IndicatorsVersionId { get; set; }

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

public enum RunStepName
{
    Flatten,
    Clean,
    Output,
    Indicators,
}

public enum RunStepStatus
{
    Succeeded,
    Failed,
    Skipped,
}
