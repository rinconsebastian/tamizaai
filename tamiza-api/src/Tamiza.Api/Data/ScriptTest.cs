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

    /// <summary>The 1-based line of the <c># %%</c> marker the draft is run through; null for a whole test.</summary>
    public int? StopLine { get; set; }

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

/// <summary>
/// A step of a test as the worker records it: <see cref="Step"/> uses the run step names and <see cref="Status"/> is
/// <c>succeeded</c>, <c>failed</c> or, for a draft run through a cell, <c>stopped</c>. Steps recorded before the script workbench
/// lack the last four members and read with them empty.
/// </summary>
/// <param name="Reused">The step's result came from an earlier test with the same inputs.</param>
/// <param name="StoppedAtLine">The line of the <c># %%</c> marker a stopped draft reached.</param>
/// <param name="Shown">What the draft passed to <c>ctx.show</c>, in call order.</param>
/// <param name="Dashboard">For a succeeded indicator draft, its table as the indicators dashboard will offer it.</param>
public sealed record TestStep(
    string Step,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? InputRows,
    long? OutputRows,
    List<TestTable> Tables,
    string Log,
    string? Error,
    bool Reused = false,
    int? StoppedAtLine = null,
    List<ShownPreview>? Shown = null,
    IndicatorTestView? Dashboard = null);

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

/// <summary>
/// One <c>ctx.show</c> call of a draft: a table preview of the value at the call, or <see cref="Text"/> for a value that is not a table.
/// <see cref="Rows"/> is the row count of the value shown (the preview keeps the first rows).
/// </summary>
public sealed record ShownPreview(string Name, int Line, long Rows, TestTable? Table, string? Text);

/// <summary>
/// Facts about an indicator draft's table, from which the UI builds the dashboard view and its warnings: the measures the dashboard
/// will offer (numeric columns), its default measure, whether the ratio of <c>numerator</c> to <c>denominator</c> exists, the
/// dimensions (the other columns) and the text columns that hold WKT geometries.
/// </summary>
/// <param name="DefaultMeasureNulls">Rows with no value in the default measure.</param>
/// <param name="DefaultMeasureMax">The largest value of the default measure, null when there is none.</param>
/// <param name="Totals">The sum of each measure over the whole table.</param>
/// <param name="DuplicateRows">Rows beyond the first for each combination of dimension values.</param>
/// <param name="Charts">Per dimension, the groups to chart, for the first 10 dimensions.</param>
public sealed record IndicatorTestView(
    List<string> Measures,
    string? DefaultMeasure,
    bool Ratio,
    List<string> Dimensions,
    string? DefaultDimension,
    List<string> Geometry,
    long Rows,
    long DefaultMeasureNulls,
    double? DefaultMeasureMax,
    Dictionary<string, double> Totals,
    long DuplicateRows,
    Dictionary<string, DimensionChart> Charts);

/// <summary>The distinct values of a dimension and, when there are at most 50, the sums per value (empty last).</summary>
public sealed record DimensionChart(long Values, List<ChartGroup>? Groups);

/// <summary><see cref="Value"/> is the sum of the default measure for the group; the others are those of the ratio's columns, when it exists.</summary>
public sealed record ChartGroup(string? Group, double? Value, double? Numerator, double? Denominator);
