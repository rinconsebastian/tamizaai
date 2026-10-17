using System.Globalization;
using System.Text.RegularExpressions;
using Tamiza.Api.Data;

namespace Tamiza.Api.Assistant;

/// <summary>An exception as the analytics worker records it, reduced to what a fix may send (design D5).</summary>
/// <param name="HasTraceback">False for a message that is not an exception of the script: a contract violation, a limit, a worker failure.</param>
/// <param name="Exception">With a traceback, the first line at column 0 after the frames, <c>&lt;type&gt;: &lt;first line of the message&gt;</c>; otherwise null.</param>
/// <param name="Frames">The frames of <c>script.py</c>, oldest first.</param>
/// <param name="Message">Without a traceback, the whole message; otherwise empty.</param>
public sealed record ParsedError(bool HasTraceback, string? Exception, IReadOnlyList<ErrorFrame> Frames, string Message);

/// <summary>A frame of the script's traceback: its line and, except for a syntax error, the function it was in.</summary>
public sealed record ErrorFrame(int Line, string? Function);

/// <summary>
/// Renders the content of the <c>&lt;test&gt;</c> block of a fix (design D5): the shape of the analyst's last test, its error and its
/// log lines, and the dashboard warning asked about. It never reads a preview, the cut cells, the text of a <c>ctx.show</c> value,
/// the totals, the largest value or a chart group. Every name and text goes through the <see cref="Masker"/>.
/// </summary>
public static partial class TestFacts
{
    /// <summary>The most log lines sent: the last ones that start with a log level.</summary>
    public const int MaxLogLines = 60;

    /// <summary>A longer log line or error line is cut, so that one <c>ctx.log</c> of a whole table cannot fill the request.</summary>
    public const int MaxLineChars = 500;

    /// <summary>The lines of a message without a traceback that are sent.</summary>
    public const int MaxMessageLines = 20;

    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "succeeded", "failed", "stopped", "skipped", "running", "queued" };
    private static readonly HashSet<string> StepNames = new(StringComparer.Ordinal) { "flatten", "clean", "output", "indicator" };
    private static readonly HashSet<string> ColumnTypes = new(StringComparer.Ordinal) { "integer", "decimal", "boolean", "date", "timestamp", "text" };

    /// <summary>
    /// The block's content for <paramref name="test"/>. <paramref name="warning"/> is the dashboard warning asked about, if any; its
    /// <c>measure</c> or <c>column</c> is dropped here when the test has no such column.
    /// </summary>
    public static string Render(ScriptTest test, AssistantWarning? warning, Masker masker)
    {
        var lines = new List<string>();
        var name = test.Kind == ScriptKind.Indicator && test.Name is not null ? $" {masker.Mask(test.Name)}" : "";
        var cell = test.StopLine is { } stop ? $", run through the cell at line {stop.ToString(CultureInfo.InvariantCulture)}" : "";
        lines.Add($"The analyst's last test of this script: the {test.Kind.ToString().ToLowerInvariant()} script{name} on a sample of {test.SampleSize.ToString(CultureInfo.InvariantCulture)} submissions{cell}; the test {test.Status.ToString().ToLowerInvariant()}.");

        var draftStep = DraftStep(test.Kind);
        TestStep? draft = null;
        if (test.Steps.Count == 0)
        {
            lines.Add("No step was recorded.");
        }

        foreach (var step in test.Steps)
        {
            var isDraft = string.Equals(step.Step, draftStep, StringComparison.Ordinal);
            draft = isDraft ? step : draft;
            RenderStep(lines, step, isDraft, test.Kind, masker);
        }

        if (draft?.Dashboard is { } dashboard)
        {
            RenderDashboard(lines, dashboard, masker);
        }

        if (warning is not null)
        {
            lines.Add(RenderWarning(warning, KnownColumns(draft), masker));
        }

        return string.Join('\n', lines);
    }

    /// <summary>Splits an error into the parts a fix may send (design D5).</summary>
    public static ParsedError ParseError(string error)
    {
        var lines = error.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var frames = new List<ErrorFrame>();
        var lastFrame = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (Frame().Match(lines[index]) is { Success: true } frame)
            {
                frames.Add(new ErrorFrame(
                    int.Parse(frame.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
                    frame.Groups[2].Success ? frame.Groups[2].Value : null));
                lastFrame = index;
            }
        }

        var header = Array.FindIndex(lines, line => line.StartsWith("Traceback (most recent call last)", StringComparison.Ordinal));
        if (lastFrame < 0 && header < 0)
        {
            return new ParsedError(false, null, [], error.Trim());
        }

        string? exception = null;
        for (var index = Math.Max(lastFrame, header) + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("Traceback (most recent call last)", StringComparison.Ordinal))
            {
                exception = line.TrimEnd();
                break;
            }
        }

        return new ParsedError(true, exception, frames, "");
    }

    /// <summary>The lines of a step's log that start with a log level, the last <see cref="MaxLogLines"/> of them. A printed line never qualifies, even when it looks like one.</summary>
    public static IReadOnlyList<(string Level, string Text)> LogLines(string? log)
    {
        if (string.IsNullOrEmpty(log))
        {
            return [];
        }

        return log.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => LogLine().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
            .TakeLast(MaxLogLines)
            .ToList();
    }

    private static string DraftStep(ScriptKind kind) => kind switch
    {
        ScriptKind.Clean => "clean",
        ScriptKind.Output => "output",
        _ => "indicator",
    };

    private static void RenderStep(List<string> lines, TestStep step, bool isDraft, ScriptKind kind, Masker masker)
    {
        var name = StepNames.Contains(step.Step) ? step.Step : masker.Mask(step.Step);
        var status = Statuses.Contains(step.Status) ? step.Status : masker.Mask(step.Status);
        var note = isDraft ? " (the draft)" : step.Step == "clean" && kind != ScriptKind.Clean ? " (the saved script 1)" : "";
        var stopped = step.StoppedAtLine is { } stoppedAt ? $", stopped at line {stoppedAt.ToString(CultureInfo.InvariantCulture)}" : "";
        var reused = step.Reused ? ", result of an earlier test with the same inputs" : "";
        lines.Add($"- step {name}{note}: {status}{stopped}{reused}");

        foreach (var table in step.Tables)
        {
            lines.Add($"  - table {masker.Mask(table.Name)}: {table.Rows.ToString(CultureInfo.InvariantCulture)} rows{ColumnList(table.Columns, masker)}");
        }

        foreach (var shown in step.Shown ?? [])
        {
            var what = shown.Table is not null
                ? $"{shown.Rows.ToString(CultureInfo.InvariantCulture)} rows{ColumnList(shown.Table.Columns, masker)}"
                : "a text value";
            lines.Add($"  - ctx.show \"{masker.Mask(shown.Name)}\" at line {shown.Line.ToString(CultureInfo.InvariantCulture)}: {what}");
        }

        if (!isDraft)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(step.Error))
        {
            RenderError(lines, step.Error, masker);
        }

        var log = LogLines(step.Log);
        if (log.Count > 0)
        {
            lines.Add($"  log lines with a level (the last {log.Count.ToString(CultureInfo.InvariantCulture)}):");
            foreach (var (level, text) in log)
            {
                lines.Add($"    {level} {MaskLine(text, masker)}");
            }
        }
    }

    private static string ColumnList(List<TestColumn>? columns, Masker masker) =>
        columns is null
            ? ""
            : "; columns: " + string.Join(", ", columns.Select(c => $"{masker.Mask(c.Name)} ({(ColumnTypes.Contains(c.Type) ? c.Type : masker.Mask(c.Type))})"));

    private static void RenderError(List<string> lines, string error, Masker masker)
    {
        var parsed = ParseError(error);
        if (!parsed.HasTraceback)
        {
            var message = parsed.Message.Split('\n').Take(MaxMessageLines).Select(line => MaskLine(line.TrimEnd(), masker)).ToList();
            lines.Add($"  error: {message[0]}");
            lines.AddRange(message.Skip(1).Select(line => $"    {line}"));
            return;
        }

        if (parsed.Exception is not null)
        {
            lines.Add($"  error: {MaskLine(parsed.Exception, masker)}");
        }
        else
        {
            lines.Add("  error: the script failed");
        }

        foreach (var frame in parsed.Frames)
        {
            var where = frame.Function is null ? "" : $" in {masker.Mask(frame.Function)}";
            lines.Add($"    line {frame.Line.ToString(CultureInfo.InvariantCulture)}{where}");
        }
    }

    private static string MaskLine(string text, Masker masker) =>
        masker.Mask(text.Length > MaxLineChars ? text[..MaxLineChars] + Masker.Ellipsis : text);

    private static void RenderDashboard(List<string> lines, IndicatorTestView view, Masker masker)
    {
        string Names(IEnumerable<string> names) => names.Any() ? string.Join(", ", names.Select(masker.Mask)) : "none";
        string Default(string? name) => name is null ? "" : $" (default: {masker.Mask(name)})";

        lines.Add("Dashboard view of the indicator's table, as the indicators dashboard would offer it:");
        lines.Add($"- measures: {Names(view.Measures)}{Default(view.DefaultMeasure)}");
        lines.Add($"- ratio of numerator to denominator: {(view.Ratio ? "yes" : "no")}");
        lines.Add($"- dimensions: {Names(view.Dimensions)}{Default(view.DefaultDimension)}");
        lines.Add($"- geometry columns: {Names(view.Geometry)}");
        lines.Add($"- rows: {view.Rows.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"- rows with an empty default measure: {view.DefaultMeasureNulls.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"- duplicate rows (rows that share every dimension): {view.DuplicateRows.ToString(CultureInfo.InvariantCulture)}");
        if (view.Charts.Count > 0)
        {
            lines.Add("- distinct values per dimension: "
                + string.Join(", ", view.Charts.Select(chart => $"{masker.Mask(chart.Key)} {chart.Value.Values.ToString(CultureInfo.InvariantCulture)}")));
        }
    }

    private static HashSet<string> KnownColumns(TestStep? draft)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (draft is null)
        {
            return known;
        }

        foreach (var column in draft.Tables.SelectMany(t => t.Columns ?? []))
        {
            known.Add(column.Name);
        }

        if (draft.Dashboard is { } view)
        {
            known.UnionWith(view.Measures);
            known.UnionWith(view.Dimensions);
            known.UnionWith(view.Geometry);
        }

        return known;
    }

    private static string RenderWarning(AssistantWarning warning, HashSet<string> known, Masker masker)
    {
        // A name is sent only when it is a column of the test's tables, and masked like the test's other names.
        string? Column(string? name) => name is not null && known.Contains(name) ? masker.Mask(name) : null;
        var count = warning.Count?.ToString(CultureInfo.InvariantCulture);

        var what = warning.Key switch
        {
            "noRows" => "the table has no rows",
            "noValue" => Column(warning.Measure) is { } measure
                ? $"the table has no column named value, so the dashboard plots {measure} instead"
                : "the table has no column named value, so the dashboard plots another numeric column",
            "ratioIncomplete" => "the table has only one of the columns numerator and denominator",
            "emptyValues" => (count, Column(warning.Measure)) switch
            {
                ({ } n, { } measure) => $"{n} rows have no value in the default measure {measure}",
                ({ } n, null) => $"{n} rows have no value in the default measure",
                (null, { } measure) => $"some rows have no value in the default measure {measure}",
                _ => "some rows have no value in the default measure",
            },
            "percentAboveOne" => "the indicator's format is percent and values of the default measure are above 1",
            "dimensionAsMeasure" => Column(warning.Column) is { } column
                ? $"the numeric column {column} looks like a dimension and is offered as a measure"
                : "a numeric column that looks like a dimension is offered as a measure",
            "duplicateRows" => count is { } n
                ? $"{n} rows share the values of every dimension with another row"
                : "some rows share the values of every dimension with another row",
            _ => "a warning of the dashboard view",
        };
        return $"The analyst asks about this warning of the dashboard view ({warning.Key}): {what}.";
    }

    [GeneratedRegex(@"^  File ""script\.py"", line (\d+)(?:, in (.+))?$")]
    private static partial Regex Frame();

    [GeneratedRegex(@"^(INFO|WARNING|ERROR|CRITICAL) (.*)$")]
    private static partial Regex LogLine();
}
