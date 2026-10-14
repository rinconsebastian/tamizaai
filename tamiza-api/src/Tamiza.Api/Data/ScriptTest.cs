using System.Text.Json;

namespace Tamiza.Api.Data;

/// <summary>
/// A test of a draft script on a sample of the project's submissions. Queued here, executed by the analytics worker
/// ahead of queued runs; nothing it produces is published.
/// </summary>
public sealed class ScriptTest
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RequestedBy { get; set; }

    public ScriptKind Kind { get; set; }

    /// <summary>The indicator the draft is tested as; null for the clean and output scripts.</summary>
    public string? Name { get; set; }

    /// <summary>The tested code, as it was in the editor; it need not be saved.</summary>
    public required string Code { get; set; }

    public int SampleSize { get; set; }

    public RunStatus Status { get; set; }

    /// <summary>The saved script 1 the worker ran before an output or indicators draft; resolved when it claimed the test.</summary>
    public Guid? CleanVersionId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary><c>step.&lt;name&gt;</c> for the first failed step, or <c>test.interrupted</c>.</summary>
    public string? ErrorCode { get; set; }

    public string? Error { get; set; }

    /// <summary>One entry per step the worker ran, written as each step ends.</summary>
    public List<TestStep> Steps { get; set; } = [];
}

/// <summary>A step of a test as the worker records it: <see cref="Step"/> and <see cref="Status"/> use the run step names.</summary>
public sealed record TestStep(
    string Step,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? InputRows,
    long? OutputRows,
    List<TestTable> Tables,
    string Log,
    string? Error);

/// <summary>
/// A table a test step produced. Only the first tables of a step carry <see cref="Columns"/> and a <see cref="Preview"/>
/// of its first rows; <see cref="CutCells"/> lists the <c>[row, column]</c> of text values that were cut.
/// </summary>
public sealed record TestTable(
    string Name,
    long Rows,
    List<TestColumn>? Columns,
    int PreviewRows,
    List<List<JsonElement>>? Preview,
    List<List<int>>? CutCells);

/// <summary>
/// <see cref="Type"/> is <c>integer</c>, <c>decimal</c>, <c>boolean</c>, <c>date</c>, <c>timestamp</c> or <c>text</c>.
/// <see cref="Label"/> is the question's label in the form's default language, or null when the column has none.
/// </summary>
public sealed record TestColumn(string Name, string Type, string? Label = null);
