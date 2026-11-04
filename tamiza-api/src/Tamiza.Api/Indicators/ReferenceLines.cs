namespace Tamiza.Api.Indicators;

/// <summary>One reference line a chart can draw (design D3 of add-chart-annotations): its kind, the widget's own label or null for the kind's default, and its value.</summary>
public sealed record ReferenceLineValue(string Kind, string? Label, double Value);

/// <summary>
/// The values of a widget's reference lines, over the answer its query gave. Twin of <c>referenceLineValues</c> in tamiza-ui
/// (<c>indicators/reference-lines.ts</c>): both are tested against the cases of <c>indicators/testing/reference-lines.json</c>, so the
/// screen, the email, the workbooks and MCP agree. Change both together.
/// </summary>
public static class ReferenceLines
{
    /// <summary>
    /// The lines of a bar, column or line widget that have a value, in the order the widget lists them; every other kind has none.
    /// <list type="bullet">
    /// <item><c>mean</c> is the sum of the values the chart draws, added in the response's order, over their count.</item>
    /// <item><c>median</c> is the middle of them sorted, or the mean of the two middle ones when there is an even number, as pandas computes it.</item>
    /// <item><c>overall</c> is the response's <see cref="IndicatorQueryResponse.Overall"/>, or the grand total of a widget with a series.</item>
    /// <item><c>value</c> is its own number.</item>
    /// </list>
    /// A line with no value (no bars, an overall of no rows) is left out, and so is a mean or a median of a widget with a series, which
    /// validation refuses. A null line or a line of an unknown kind is left out too.
    /// </summary>
    public static IReadOnlyList<ReferenceLineValue> Of(string chartType, WidgetConfig config, IndicatorQueryResponse response)
    {
        if (chartType is not (ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line) || config.Display?.ReferenceLines is not { Count: > 0 } lines)
        {
            return [];
        }

        var withSeries = config.SeriesBy is not null;
        List<double> values = withSeries ? [] : [.. response.Rows.Select(row => row.Value)];
        var kept = new List<ReferenceLineValue>();
        foreach (var line in lines)
        {
            if (line is null || ValueOf(line, withSeries, values, response) is not { } value)
            {
                continue;
            }

            kept.Add(new ReferenceLineValue(line.Kind!, line.Label, value));
        }

        return kept;
    }

    private static double? ValueOf(WidgetReferenceLine line, bool withSeries, List<double> values, IndicatorQueryResponse response) => line.Kind switch
    {
        ReferenceLineKinds.Mean when !withSeries && values.Count > 0 => Mean(values),
        ReferenceLineKinds.Median when !withSeries && values.Count > 0 => Median(values),
        ReferenceLineKinds.Overall => Finite(withSeries ? response.Totals?.All.Value : response.Overall),
        ReferenceLineKinds.Value => Finite(line.Value),
        _ => null,
    };

    private static double? Finite(double? value) => value is { } number && double.IsFinite(number) ? number : null;

    /// <summary>The sum added one value after another in the order given, over the count: floating-point sums depend on the order, so the twin does the same.</summary>
    private static double Mean(List<double> values)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Count;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
