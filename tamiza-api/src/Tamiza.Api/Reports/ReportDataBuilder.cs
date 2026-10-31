using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.CleanData;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Reports;

/// <summary>
/// Reads a dashboard as a member sees it when opening it, filters at their defaults, and turns each widget into what its "Show as
/// table" view lists (design D7). It takes the page's steps that the API does not: the query by chart type and the filters in force
/// (<see cref="WidgetQuery"/>), histogram binning, the natural order of a line, a pie's shares, and the texts of a missing group or
/// value. Widgets are read one after another, so a report holds at most one of the query slots members share.
/// </summary>
public sealed class ReportDataBuilder(
    TamizaDbContext db, IndicatorDashboard indicators, [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time, IOptions<TamizaOptions> tamiza)
{
    /// <summary>The model of the dashboard's email in <paramref name="locale"/>, or null when the dashboard does not exist.</summary>
    public async Task<ReportModel?> BuildAsync(Guid dashboardId, string locale, CancellationToken ct)
    {
        var dashboard = await db.Dashboards.AsNoTracking()
            .Where(d => d.Id == dashboardId)
            .Select(d => new { d.ProjectId, d.Name, d.Filters })
            .SingleOrDefaultAsync(ct);
        if (dashboard is null)
        {
            return null;
        }

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == dashboard.ProjectId)
            .Select(p => new { p.Name, p.TimeZone })
            .SingleAsync(ct);
        var text = ReportText.For(locale);
        var format = new ReportFormat(text.Culture, ReportFormat.ZoneOf(project.TimeZone));
        var filters = DashboardEndpoints.ReadFilters(dashboard.Filters);
        var widgets = await DashboardEndpoints.ReadWidgetsAsync(db, dashboardId, ct);
        var metadata = (await ScriptEndpoints.CurrentIndicatorsAsync(db, dashboard.ProjectId, ct))
            .Where(v => v.Name is not null)
            .ToDictionary(v => v.Name!, StringComparer.Ordinal);
        var generatedAt = time.GetUtcNow();

        var built = new List<ReportWidget>(widgets.Count);
        for (var i = 0; i < widgets.Count; i++)
        {
            var widget = widgets[i];
            // An indicator the project no longer lists, such as a removed script whose table is still published, is written as the
            // dashboard writes it (tamiza-ui plainIndicator): two decimals, no unit, and its name as its label.
            var indicator = metadata.GetValueOrDefault(widget.Config.Indicator);
            var meta = new IndicatorMeta(
                indicator?.Label ?? widget.Config.Indicator, indicator?.Format ?? ReportFormat.Decimal, indicator?.Unit);
            built.Add(await BuildWidgetAsync(dashboard.ProjectId, i + 1, widget, filters, meta, format, text, ct));
        }

        return new ReportModel(
            dashboard.ProjectId,
            project.Name,
            dashboardId,
            dashboard.Name,
            generatedAt,
            await PublishedAtAsync(dashboard.ProjectId, widgets, ct),
            filters.Where(f => f.Default is not null).Select(f => new ReportFilterLine(f.Label, Describe(f, f.Default!, text))).ToList(),
            built,
            $"{tamiza.Value.PublicUrl.TrimEnd('/')}/projects/{dashboard.ProjectId}/dashboards/{dashboardId}",
            format,
            text);
    }

    private sealed record IndicatorMeta(string Label, string Format, string? Unit);

    private async Task<DateTimeOffset?> PublishedAtAsync(Guid projectId, IReadOnlyList<Widget> widgets, CancellationToken ct)
    {
        try
        {
            var publications = await indicators.GetPublicationsAsync(projectId, ct);
            return widgets
                .Select(w => publications.GetValueOrDefault(w.Config.Indicator)?.PublishedAt)
                .Where(at => at is not null)
                .DefaultIfEmpty(null)
                .Max();
        }
        catch (DataTimeoutException)
        {
            return null;
        }
    }

    private async Task<ReportWidget> BuildWidgetAsync(
        Guid projectId, int position, Widget widget, IReadOnlyList<DashboardFilter> filters, IndicatorMeta meta, ReportFormat format,
        ReportText text, CancellationToken ct)
    {
        var caption = Plotted(widget.Config, meta.Label);
        ReportWidget Without(WidgetOutcome outcome, string reason, IReadOnlyList<string>? notes = null) =>
            new(position, widget.Title, widget.ChartType, caption, outcome, [], [], null, reason, notes ?? []);

        IndicatorQueryOutcome outcome;
        try
        {
            outcome = await indicators.QueryAsync(
                projectId, WidgetQuery.For(widget.ChartType, widget.Config), widget.ChartType, ct, WidgetQuery.AppliedDefaults(filters, widget.Config));
        }
        catch (DataTimeoutException)
        {
            return Without(WidgetOutcome.TimedOut, text["timedOut"]);
        }

        // Validation errors: the table changed under the saved settings (the dashboard shows "the query was refused").
        if (outcome.Response is not { } response)
        {
            return Without(WidgetOutcome.NoLongerFits, text["noLongerFits"]);
        }

        if (!response.Published || response.Missing.Indicator)
        {
            return Without(WidgetOutcome.NotPublished, text.Format("notPublished", meta.Label));
        }

        if (response.Missing.Columns.Count > 0)
        {
            return Without(WidgetOutcome.MissingColumns, text.Format("missingColumns", string.Join(", ", response.Missing.Columns)));
        }

        var notes = new List<string>();
        var notNarrowed = WidgetQuery.NotNarrowedBy(filters, widget.Config, response.NotFiltered);
        if (notNarrowed.Count > 0)
        {
            notes.Add(text.Format("notFiltered", string.Join(", ", notNarrowed)));
        }

        if (response.RowLimitReached)
        {
            notes.Add(text.Format("rowLimit", response.Rows.Count));
        }

        if (response.LeftOut is > 0 and var leftOut)
        {
            notes.Add(text.LeftOut(leftOut));
        }

        if (response.Rows.Count == 0)
        {
            return Without(WidgetOutcome.NoRows, text["empty"], notes);
        }

        if (response.Series is not null)
        {
            var (seriesColumns, seriesRows, seriesCount, seriesNotes) = SeriesReportTable(response, widget, caption, meta, format, text);
            return new(position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, seriesColumns, seriesRows, null, null, [.. notes, .. seriesNotes], seriesCount);
        }

        if (widget.ChartType == ChartTypes.Stat)
        {
            return new(position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, [], [],
                format.Value(response.Rows[0].Value, meta.Format, meta.Unit), null, notes);
        }

        // A widget made of a series that came back without one cannot be told apart from a settings change: it no longer fits.
        if (ChartTypes.NeedsSeries(widget.ChartType))
        {
            return Without(WidgetOutcome.NoLongerFits, text["noLongerFits"], notes);
        }

        var (columns, rows) = widget.ChartType switch
        {
            ChartTypes.Histogram => HistogramTable(response.Rows, widget.Config, caption, meta, format, text),
            ChartTypes.Pie => PieTable(response.Rows, widget.Config, caption, meta, format, text),
            ChartTypes.Map => MapTable(response, widget.Config, caption, meta, format, text),
            ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line =>
                AxisTable(response.Rows, widget.Config, caption, meta, format, text, natural: widget.ChartType == ChartTypes.Line),
            _ => throw new InvalidOperationException($"A {widget.ChartType} widget has no table."),
        };
        return new(position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, columns, rows, null, null, notes);
    }

    private static ReportColumn ValueColumn(string caption, IndicatorMeta meta) =>
        new(caption, ExcelFormat(meta.Format), meta.Format == ReportFormat.Percent && meta.Unit?.Trim() == "%" ? null : meta.Unit);

    /// <summary>The Excel number format of a plotted value (design D11).</summary>
    public static string ExcelFormat(string? format) => format switch
    {
        ReportFormat.Count => "#,##0",
        ReportFormat.Percent => "0.00%",
        _ => "#,##0.00",
    };

    private static ReportCell ValueCell(double value, IndicatorMeta meta, ReportFormat format) => new(value, format.Value(value, meta.Format, meta.Unit));

    private static ReportCell GroupCell(string? group, ReportText text) => new(group ?? text["noGroup"], group ?? text["noGroup"]);

    /// <summary>Bar, column and line: the group and the plotted value, as <c>axis-chart.ts</c> lists them. A line goes in natural order.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) AxisTable(
        IReadOnlyList<IndicatorRow> rows, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text, bool natural)
    {
        IEnumerable<IndicatorRow> ordered = natural ? rows.Order(NaturalOrder.ByGroup.Instance) : rows;
        return ([new(config.GroupBy ?? ""), ValueColumn(caption, meta)],
            ordered.Select(r => (IReadOnlyList<ReportCell>)[GroupCell(r.Group, text), ValueCell(r.Value, meta, format)]).ToList());
    }

    /// <summary>
    /// A widget with a series, as its cross table (design D8): the group, one column per series and a "Total" column, then a "Total" row.
    /// A 100% layout and a Likert widget show shares in the series columns and have no "Total" row.
    /// </summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>, int, IReadOnlyList<string>) SeriesReportTable(
        IndicatorQueryResponse response, Widget widget, string caption, IndicatorMeta meta, ReportFormat format, ReportText text)
    {
        var table = SeriesTable.From(response, widget.ChartType, widget.Config.Layout);
        var shares = SeriesTable.ShowsShares(widget.ChartType, widget.Config.Layout);
        var valueColumn = ValueColumn(caption, meta);
        string Label(SeriesTableColumn column) => column.Kind switch
        {
            SeriesKinds.None => text["noGroup"],
            SeriesKinds.Other => text["other"],
            _ => column.Series ?? "",
        };

        var columns = new List<ReportColumn> { new(widget.Config.GroupBy ?? "") };
        columns.AddRange(table.Columns.Select(c => shares ? new ReportColumn(Label(c), "0.0%") : valueColumn with { Header = Label(c) }));
        columns.Add(valueColumn with { Header = text["total"] });

        ReportCell Value(double? value) => value is { } number ? ValueCell(number, meta, format) : new(null, "—");
        ReportCell Share(double? share) => share is { } fraction ? new(fraction, format.Share(fraction)) : new(null, "—");

        var rows = new List<IReadOnlyList<ReportCell>>();
        foreach (var row in table.Rows)
        {
            var cells = new List<ReportCell> { GroupCell(row.Group, text) };
            cells.AddRange(row.Cells.Select((value, i) => shares ? Share(row.Shares?[i]) : Value(value)));
            cells.Add(Value(row.Total));
            rows.Add(cells);
        }

        // As in "Show as table": a table of shares has the group's total in its own column and no "Total" row.
        if (!shares)
        {
            var total = new List<ReportCell> { new(text["total"], text["total"]) };
            total.AddRange(table.Columns.Select(c => Value(c.Total)));
            total.Add(Value(table.GrandTotal));
            rows.Add(total);
        }

        var notes = table.NegativeLeftOut > 0 ? new[] { text.NegativeLeftOut(table.NegativeLeftOut) } : [];
        return (columns, rows, table.Columns.Count, notes);
    }

    /// <summary>A histogram: each range of the plotted value and how many rows fall in it.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) HistogramTable(
        IReadOnlyList<IndicatorRow> rows, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text)
    {
        var buckets = Histogram(rows.Select(r => r.Value), config.Buckets ?? WidgetConfig.DefaultBuckets);
        return ([new(text["range"]), new(text.Format("rowsOf", caption), "#,##0")],
            buckets.Select(b =>
            {
                var range = $"{format.Plain(b.From, meta.Format)}–{format.Plain(b.To, meta.Format)}";
                return (IReadOnlyList<ReportCell>)[new(range, range), new((double)b.Count, format.Whole(b.Count))];
            }).ToList());
    }

    /// <summary>A pie chart: every group value, in the order the rows list them, with its share of the positive total.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) PieTable(
        IReadOnlyList<IndicatorRow> rows, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text)
    {
        var total = rows.Sum(r => Math.Max(0, r.Value));
        return ([new(config.GroupBy ?? ""), ValueColumn(caption, meta), new(text["share"], "0.0%")],
            rows.Select(r =>
            {
                ReportCell share = r.Value > 0 && total > 0 ? new(r.Value / total, format.Share(r.Value / total)) : new(null, "—");
                return (IReadOnlyList<ReportCell>)[GroupCell(r.Group, text), ValueCell(r.Value, meta, format), share];
            }).ToList());
    }

    /// <summary>A map: each shape's group, its plotted value and each tooltip column under its label, without the geometry.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) MapTable(
        IndicatorQueryResponse response, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text)
    {
        var items = config.Tooltip;
        var types = response.TooltipTypes ?? [];
        var columns = new List<ReportColumn> { new(config.GroupBy ?? ""), ValueColumn(caption, meta) };
        for (var i = 0; i < items.Count; i++)
        {
            var type = i < types.Count ? types[i] : (DataColumnType?)null;
            columns.Add(new(items[i].Label?.Trim() is { Length: > 0 } label ? label : items[i].Column, TooltipExcelFormat(items[i], type)));
        }

        return (columns, response.Rows.Select(r =>
        {
            var cells = new List<ReportCell> { GroupCell(r.Group, text), ValueCell(r.Value, meta, format) };
            for (var i = 0; i < items.Count; i++)
            {
                cells.Add(TooltipCell(r.Tooltip is { } values && i < values.Count ? values[i] : null, i < types.Count ? types[i] : null, items[i], format, text));
            }

            return (IReadOnlyList<ReportCell>)cells;
        }).ToList());
    }

    /// <summary>A tooltip column's Excel format: its own format while it is a number, else by its type; null for text, dates and true/false.</summary>
    private static string? TooltipExcelFormat(WidgetTooltipItem item, DataColumnType? type) =>
        type is DataColumnType.Integer or DataColumnType.Decimal
            ? item.Format switch
            {
                TooltipFormats.Count => "#,##0",
                TooltipFormats.Decimal => "#,##0.00",
                TooltipFormats.Percent => "0.00%",
                _ => type == DataColumnType.Integer ? "#,##0" : "#,##0.00",
            }
            : null;

    /// <summary>One tooltip value, as <c>map-tooltip.ts</c> writes it: a missing value reads as a dash and stays empty in the workbook.</summary>
    private static ReportCell TooltipCell(object? value, DataColumnType? type, WidgetTooltipItem item, ReportFormat format, ReportText text)
    {
        switch (value)
        {
            case null:
                return new(null, "—");
            case bool flag:
                return new(text[flag ? "yes" : "no"], text[flag ? "yes" : "no"]);
            case decimal or double or int or long:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                var shown = format.TooltipNumber(number, type is DataColumnType.Integer or DataColumnType.Decimal ? item.Format : null, type == DataColumnType.Integer);
                return new(number, shown);
            default:
                var raw = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                var written = type switch
                {
                    DataColumnType.Date => format.DateText(raw),
                    DataColumnType.Timestamp => format.TimestampText(raw),
                    _ => raw,
                };
                return new(written, written);
        }
    }

    /// <summary>
    /// Buckets values into <paramref name="count"/> equal-width ranges between the smallest and the largest, and counts each. Twin of
    /// <c>histogram</c> in tamiza-ui <c>indicators/chart-data.ts</c>: the last range takes its upper end too.
    /// </summary>
    public static IReadOnlyList<(double From, double To, int Count)> Histogram(IEnumerable<double> values, int count)
    {
        var finite = values.Where(double.IsFinite).ToList();
        if (finite.Count == 0)
        {
            return [];
        }

        var size = Math.Max(1, count);
        var min = finite.Min();
        var max = finite.Max();
        if (max == min)
        {
            max = min + 1; // every row falls in the first bucket
        }

        var width = (max - min) / size;
        var counts = new int[size];
        foreach (var value in finite)
        {
            counts[Math.Min(size - 1, (int)Math.Floor((value - min) / width))]++;
        }

        return Enumerable.Range(0, size).Select(i => (min + i * width, i == size - 1 ? max : min + (i + 1) * width, counts[i])).ToList();
    }

    /// <summary>What a widget plots, as <c>plotted</c> in tamiza-ui <c>indicators/format.ts</c> writes it.</summary>
    public static string Plotted(WidgetConfig config, string label) =>
        config.Ratio is { } ratio ? $"{label}: {ratio.Numerator} / {ratio.Denominator}"
        : config.Measure is { } measure ? $"{label}: {measure}"
        : label;

    /// <summary>A filter's default as the filter bar writes it (<c>describeValue</c> in tamiza-ui <c>indicators/dashboard-filters.ts</c>).</summary>
    public static string Describe(DashboardFilter filter, DashboardFilterValue value, ReportText text)
    {
        if (DashboardFilterKinds.TakesValues(filter.Kind))
        {
            return string.Join(", ", (value.Values ?? []).Select(v =>
                filter.Kind == DashboardFilterKinds.Boolean ? text[v == "true" ? "true" : "false"] : v == "" ? text["emptyText"] : v));
        }

        if (!string.IsNullOrEmpty(value.Min) && !string.IsNullOrEmpty(value.Max))
        {
            return value.Min == value.Max ? value.Min : text.Format("range.both", value.Min, value.Max);
        }

        return !string.IsNullOrEmpty(value.Min) ? text.Format("range.min", value.Min) : text.Format("range.max", value.Max);
    }
}
