using System.Text.Json.Serialization;

namespace Tamiza.Api.Indicators;

/// <summary>One column of a <see cref="SeriesTable"/>: a series and its total.</summary>
public sealed record SeriesTableColumn(string? Series, string Kind, double? Total, long Rows);

/// <summary>
/// The base figures of one cell (design D2 of add-small-sample-warnings): a ratio's numerator and denominator sums and the cell's n, each
/// null when the cell has none. A cell of a table of shares has only the n of its group.
/// </summary>
public sealed record CellFigures(double? Numerator, double? Denominator, double? N);

/// <summary>
/// One row of a <see cref="SeriesTable"/>: a group, its total, and one cell per column (null where the pair has no value). <paramref name="Shares"/>
/// is null unless the chart shows shares. <paramref name="Figures"/> holds the figures of each cell, null for a cell that has none, and is left
/// out while no cell of the table has any (a measure with no base column), so the grid of an answer without figures is what it was.
/// </summary>
public sealed record SeriesTableRow(
    string? Group, double? Total, long Rows, IReadOnlyList<double?> Cells, IReadOnlyList<double?>? Shares,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CellFigures?>? Figures = null);

/// <summary>
/// The grid every consumer of a widget with a series reads (design D4, D12): the chart's tooltips, "Show as table", the emails and the MCP
/// tools. It is derived from a query response and never recomputes a total. Twin of <c>series-table.ts</c> in tamiza-ui; both are tested
/// against <c>indicators/testing/series-tables.json</c>.
/// </summary>
/// <param name="NegativeLeftOut">The cells set aside from the shares because their value is below zero.</param>
/// <param name="LeftOut">The rows a population pyramid or a Likert widget left out.</param>
public sealed record SeriesTable(
    IReadOnlyList<SeriesTableColumn> Columns,
    IReadOnlyList<SeriesTableRow> Rows,
    double? GrandTotal,
    long GrandRows,
    int NegativeLeftOut,
    long LeftOut,
    bool RowLimitReached)
{
    /// <summary>Whether a chart shows each cell as a share of its row: Likert bars, and bars or columns stacked to 100%.</summary>
    public static bool ShowsShares(string chartType, string? layout) =>
        chartType == ChartTypes.Likert || (chartType is ChartTypes.Bar or ChartTypes.Column && layout == SeriesLayouts.Percent);

    /// <summary>The grid of a response that has a series. A response without one is an empty grid.</summary>
    public static SeriesTable From(IndicatorQueryResponse response, string chartType, string? layout)
    {
        var series = response.Series ?? [];
        var totals = response.Totals;
        var columns = series
            .Select((info, i) => totals is not null && i < totals.Series.Count
                ? new SeriesTableColumn(info.Value, info.Kind, totals.Series[i].Value, totals.Series[i].Rows)
                : new SeriesTableColumn(info.Value, info.Kind, null, 0))
            .ToList();

        var index = new Dictionary<(string Kind, string? Value), int>();
        for (var i = 0; i < series.Count; i++)
        {
            index[(series[i].Kind, series[i].Value)] = i;
        }

        // A group is a text or a missing value: the flag keeps a group named like the placeholder apart from a missing one.
        var cells = new Dictionary<(bool Missing, string Group), double?[]>();
        var cellFigures = new Dictionary<(bool Missing, string Group), CellFigures?[]>();
        foreach (var cell in response.Rows.Where(r => r.SeriesKind is not null))
        {
            if (!index.TryGetValue((cell.SeriesKind!, cell.SeriesKind == SeriesKinds.Value ? cell.Series : null), out var at))
            {
                continue;
            }

            var key = (cell.Group is null, cell.Group ?? "");
            if (!cells.TryGetValue(key, out var row))
            {
                cells[key] = row = new double?[series.Count];
                cellFigures[key] = new CellFigures?[series.Count];
            }

            row[at] = cell.Value;
            cellFigures[key][at] = FiguresOf(cell.Numerator, cell.Denominator, cell.N);
        }

        var shares = ShowsShares(chartType, layout);
        var negative = 0;
        var rows = new List<SeriesTableRow>();
        foreach (var group in totals?.Groups ?? [])
        {
            var key = (group.Group is null, group.Group ?? "");
            var values = cells.GetValueOrDefault(key) ?? new double?[series.Count];
            // A share is over its row, so its base is the row's: every cell of a table of shares takes the n of its group (design D2).
            var figures = shares
                ? values.Select(value => value is not null && group.N is { } groupN ? new CellFigures(null, null, groupN) : null).ToArray()
                : cellFigures.GetValueOrDefault(key) ?? new CellFigures?[series.Count];
            rows.Add(new SeriesTableRow(group.Group, group.Value, group.Rows, values, shares ? SharesOf(values, ref negative) : null, figures));
        }

        if (!rows.Any(row => row.Figures!.Any(cell => cell is not null)))
        {
            rows = rows.Select(row => row with { Figures = null }).ToList();
        }

        return new SeriesTable(
            columns, rows, totals?.All.Value, totals?.All.Rows ?? 0, negative, response.LeftOut ?? 0, response.RowLimitReached);
    }

    /// <summary>The figures a response row or total carries, or null when it has none of the three.</summary>
    private static CellFigures? FiguresOf(double? numerator, double? denominator, double? n) =>
        numerator is null && denominator is null && n is null ? null : new CellFigures(numerator, denominator, n);

    /// <summary>
    /// Each cell over the sum of the row's cells that are not below zero. A cell with no value, or below zero, has no share (each of the
    /// latter is counted); a row that sums to zero has none.
    /// </summary>
    private static double?[] SharesOf(double?[] cells, ref int negative)
    {
        var sum = cells.Where(c => c is >= 0).Sum(c => c!.Value);
        var shares = new double?[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            switch (cells[i])
            {
                case < 0:
                    negative++;
                    break;
                case { } value when sum > 0:
                    shares[i] = value / sum;
                    break;
            }
        }

        return shares;
    }
}
