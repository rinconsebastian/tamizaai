using System.Globalization;
using System.Text.RegularExpressions;
using Tamiza.Api.Assistant;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Mcp;

/// <summary>
/// A script test as an AI client reads it (design D7). At <see cref="McpAccess.Aggregates"/> it holds the shape of everything and the facts
/// of an indicator's dashboard view, never a row: previews, the values and text passed to <c>ctx.show</c> and printed lines are left out,
/// the error is reduced to the exception and the frames of the script, and every name that a script could have built from data goes
/// through the assistant's <see cref="Masker"/>. At <see cref="McpAccess.Rows"/> the previews, <c>ctx.show</c> values, error and log are included.
/// </summary>
public static partial class McpTestView
{
    public const int DefaultPreviewRows = 20;
    public const int MaxPreviewRows = 100;

    /// <summary>The most log lines of a step given when rows are allowed.</summary>
    private const int MaxFullLogLines = 200;

    private const string WithheldNote =
        "Table previews, the values passed to ctx.show and printed output need access to rows; this project allows summaries only. Errors are masked: words that could be a respondent's data appear as …";

    [GeneratedRegex(@"^(year|month|code|id)$|_(id|code)$")]
    private static partial Regex DimensionLike();

    public static object From(ScriptTestDetails test, McpAccess level, Masker? masker, int previewRows, string? indicatorFormat, bool done)
    {
        var rows = level >= McpAccess.Rows;
        masker = rows ? masker : masker ?? throw new ArgumentNullException(nameof(masker), "Summaries need a masker.");
        string N(string name) => rows ? name : masker!.Mask(name);
        var steps = test.Steps.Select(step => Step(step, rows, masker!, previewRows)).ToList();
        var draft = test.Steps.LastOrDefault(s => s.Dashboard is not null);

        return new
        {
            done,
            next = done ? null : $"The test is {test.Status.ToString().ToLowerInvariant()}. Call get_test with this testId and waitSeconds to read it when it ends.",
            testId = test.Id,
            kind = test.Kind,
            name = test.Name,
            status = test.Status,
            waitingFor = test.WaitingFor,
            sampleSize = test.SampleSize,
            stopAtLine = test.StopAtLine,
            cleanVersion = test.CleanVersion,
            queuedAt = test.QueuedAt,
            startedAt = test.StartedAt,
            finishedAt = test.FinishedAt,
            errorCode = test.ErrorCode,
            error = ErrorText(test.Error, rows, masker!),
            steps,
            warnings = draft?.Dashboard is { } view ? Warnings(view, indicatorFormat, N) : null,
            withheld = rows ? null : WithheldNote,
        };
    }

    private static object Step(TestStep step, bool rows, Masker masker, int previewRows)
    {
        string N(string name) => rows ? name : masker.Mask(name);
        return new
        {
            step = step.Step,
            status = step.Status,
            startedAt = step.StartedAt,
            finishedAt = step.FinishedAt,
            inputRows = step.InputRows,
            outputRows = step.OutputRows,
            reused = step.Reused ? true : (bool?)null,
            stoppedAtLine = step.StoppedAtLine,
            tables = step.Tables.Select(t => new
            {
                name = N(t.Name),
                rows = t.Rows,
                columns = t.Columns?.Select(c => new { name = N(c.Name), c.Type, c.Label }).ToList(),
                preview = rows ? Preview(t, previewRows) : null,
            }).ToList(),
            shown = (step.Shown ?? []).Select(shown => new
            {
                name = N(shown.Name),
                shown.Line,
                rows = shown.Rows,
                columns = shown.Table?.Columns?.Select(c => new { name = N(c.Name), c.Type }).ToList(),
                preview = rows && shown.Table is not null ? Preview(shown.Table, previewRows) : null,
                text = rows ? shown.Text : null,
                kind = shown.Table is not null ? "table" : "text",
            }).ToList(),
            error = ErrorText(step.Error, rows, masker),
            log = LogText(step.Log, rows, masker),
            dashboard = step.Dashboard is { } view ? Dashboard(view, N) : null,
        };
    }

    private static object? Preview(TestTable table, int previewRows) =>
        table.Preview is null
            ? null
            : new { rows = table.Preview.Take(previewRows).ToList(), cutCells = table.CutCells?.Where(c => c[0] < previewRows).ToList() };

    /// <summary>The error as given when rows are allowed; otherwise the exception and the lines of the script, masked as the assistant masks them.</summary>
    private static string? ErrorText(string? error, bool rows, Masker masker)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        if (rows)
        {
            return error;
        }

        var parsed = TestFacts.ParseError(error);
        if (!parsed.HasTraceback)
        {
            return string.Join('\n', parsed.Message.Split('\n').Take(TestFacts.MaxMessageLines).Select(line => Line(line.TrimEnd(), masker)));
        }

        var head = parsed.Exception is null ? "the script failed" : Line(parsed.Exception, masker);
        var frames = parsed.Frames.Select(f => $"line {f.Line.ToString(CultureInfo.InvariantCulture)}{(f.Function is null ? "" : " in " + masker.Mask(f.Function))}");
        return string.Join('\n', new[] { head }.Concat(frames));
    }

    private static object? LogText(string? log, bool rows, Masker masker)
    {
        if (string.IsNullOrEmpty(log))
        {
            return null;
        }

        if (rows)
        {
            return log.Replace("\r\n", "\n").Split('\n').TakeLast(MaxFullLogLines).ToList();
        }

        var leveled = TestFacts.LogLines(log);
        return leveled.Count == 0 ? null : leveled.Select(l => $"{l.Level} {Line(l.Text, masker)}").ToList();
    }

    private static string Line(string text, Masker masker) =>
        masker.Mask(text.Length > TestFacts.MaxLineChars ? text[..TestFacts.MaxLineChars] + Masker.Ellipsis : text);

    private static object Dashboard(IndicatorTestView view, Func<string, string> n) => new
    {
        measures = view.Measures.Select(n).ToList(),
        defaultMeasure = view.DefaultMeasure is null ? null : n(view.DefaultMeasure),
        ratio = view.Ratio,
        dimensions = view.Dimensions.Select(n).ToList(),
        defaultDimension = view.DefaultDimension is null ? null : n(view.DefaultDimension),
        geometry = view.Geometry.Select(n).ToList(),
        rows = view.Rows,
        emptyDefaultMeasure = view.DefaultMeasureNulls,
        defaultMeasureMax = view.DefaultMeasureMax,
        totals = view.Totals.ToDictionary(t => n(t.Key), t => t.Value),
        duplicateRows = view.DuplicateRows,
        charts = view.Charts.ToDictionary(
            c => n(c.Key),
            c => new { distinctValues = c.Value.Values, groups = c.Value.Groups?.Take(50).ToList(), tooMany = c.Value.Groups is null ? true : (bool?)null }),
    };

    /// <summary>What an indicator's table does that its author may not expect on the dashboard; the same checks the editor's view makes.</summary>
    private static List<object> Warnings(IndicatorTestView view, string? format, Func<string, string> n)
    {
        var warnings = new List<object>();
        var measure = view.DefaultMeasure is null ? "" : n(view.DefaultMeasure);
        if (view.Rows == 0)
        {
            warnings.Add(new { key = "noRows", message = "The table has no rows. Usually a filter that matches nothing (often a comparison with a code instead of a label) or a join on keys that do not match." });
        }

        if (!view.Measures.Contains("value") && measure.Length > 0)
        {
            warnings.Add(new { key = "noValue", message = $"There is no column named value, so the dashboard will plot '{measure}'. Name the measure value." });
        }

        if (view.Measures.Contains("numerator") != view.Measures.Contains("denominator"))
        {
            warnings.Add(new { key = "ratioIncomplete", message = "A rate needs both numerator and denominator; return both." });
        }

        if (view.DefaultMeasureNulls > 0)
        {
            warnings.Add(new { key = "emptyValues", count = view.DefaultMeasureNulls, message = $"{view.DefaultMeasureNulls} rows have no value in '{measure}'. Find where they come from (a join that did not match, a division by zero, a failed conversion)." });
        }

        if (format == "percent" && view.DefaultMeasureMax is > 1)
        {
            warnings.Add(new { key = "percentAboveOne", max = view.DefaultMeasureMax, message = $"The format is percent, which expects a fraction (0.42 shows as 42%), but values reach {view.DefaultMeasureMax}. Divide by 100 or by the denominator, or use the format decimal with the unit %." });
        }

        foreach (var column in view.Measures.Where(c => DimensionLike().IsMatch(c)))
        {
            warnings.Add(new { key = "dimensionAsMeasure", column = n(column), message = $"'{n(column)}' is numeric, so it is offered as a measure. If it is a dimension, return it as text (.astype(str))." });
        }

        if (view.DuplicateRows > 0)
        {
            warnings.Add(new { key = "duplicateRows", count = view.DuplicateRows, message = $"{view.DuplicateRows} rows share every dimension, and widgets add them up. Aggregate by the dimensions or add the dimension that tells them apart." });
        }

        return warnings;
    }
}
