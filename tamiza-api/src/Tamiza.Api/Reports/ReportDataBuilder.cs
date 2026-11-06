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
            .Select(d => new { d.ProjectId, d.Name, d.Filters, d.Source, d.MinN, d.ShowN })
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
            var meta = MetaOf(widget, metadata.GetValueOrDefault(widget.Config.Indicator));
            // The widget's own base settings over the dashboard's (design D13 of add-small-sample-warnings).
            var bases = BaseFigures.InForce(widget.Config.Display, dashboard.MinN, dashboard.ShowN);
            built.Add(await BuildWidgetAsync(
                dashboard.ProjectId, i + 1, widget, WidgetQuery.AppliedDefaults(filters, widget.Config),
                notFiltered => WidgetQuery.NotNarrowedBy(filters, widget.Config, notFiltered), meta, format, text, bases, ct));
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
            text,
            dashboard.Source);
    }

    /// <param name="Columns">What the indicator's current version says about its columns (their labels); null when it says nothing or the indicator is gone.</param>
    private sealed record IndicatorMeta(string Label, string Format, string? Unit, IReadOnlyDictionary<string, IndicatorColumnMetadata>? Columns = null);

    /// <summary>
    /// How a widget writes its indicator. One the project no longer lists, such as a removed script whose table is still published, is
    /// written as the dashboard writes it (tamiza-ui plainIndicator): two decimals, no unit, and its name as its label. A widget's own unit
    /// replaces the indicator's, also when the indicator is gone (design D4 of the widget context).
    /// </summary>
    private static IndicatorMeta MetaOf(Widget widget, ScriptVersionDetails? indicator) =>
        new(indicator?.Label ?? widget.Config.Indicator, indicator?.Format ?? ReportFormat.Decimal, widget.Config.Display?.Unit ?? indicator?.Unit, indicator?.Columns);

    /// <summary>
    /// One widget's table as its workbook lists it (design D12 of add-widget-export): the same table, notes and reasons as the same widget
    /// in a scheduled email, read with the filters in force on a member's screen instead of the dashboard's defaults. The widget need not be
    /// saved: the page sends the settings it is showing, which a version's removed widget needs.
    /// </summary>
    /// <param name="applied">The filter values in force that narrow the widget: those it does not ignore.</param>
    /// <param name="notNarrowedBy">The labels of the filters in force that do not narrow it, given the columns the table lacked.</param>
    /// <param name="bases">The base settings the member's screen applies to the widget (design D13 of add-small-sample-warnings).</param>
    public async Task<WidgetExportContent> BuildWidgetExportAsync(
        Guid projectId, Widget widget, IReadOnlyList<AppliedDashboardFilter> applied, Func<IReadOnlyList<string>?, IReadOnlyList<string>> notNarrowedBy,
        ReportText text, ReportFormat format, BaseSettings bases, CancellationToken ct)
    {
        var indicator = (await ScriptEndpoints.CurrentIndicatorsAsync(db, projectId, ct)).FirstOrDefault(v => v.Name == widget.Config.Indicator);
        var meta = MetaOf(widget, indicator);
        return new WidgetExportContent(await BuildWidgetAsync(projectId, 1, widget, applied, notNarrowedBy, meta, format, text, bases, ct), meta.Label);
    }

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

    /// <summary>The widget's table or value, with the texts it carries whatever became of it: its subtitle, its footnote and whether it hides its caption.</summary>
    private async Task<ReportWidget> BuildWidgetAsync(
        Guid projectId, int position, Widget widget, IReadOnlyList<AppliedDashboardFilter> applied,
        Func<IReadOnlyList<string>?, IReadOnlyList<string>> notNarrowedBy, IndicatorMeta meta, ReportFormat format, ReportText text, BaseSettings bases,
        CancellationToken ct)
    {
        var built = await ReadWidgetAsync(projectId, position, widget, applied, notNarrowedBy, meta, format, text, bases, ct);
        var display = widget.Config.Display;
        return display is null
            ? built
            : built with { Subtitle = display.Subtitle, Footnote = display.Footnote, CaptionHidden = display.Caption?.Hidden is true };
    }

    private async Task<ReportWidget> ReadWidgetAsync(
        Guid projectId, int position, Widget widget, IReadOnlyList<AppliedDashboardFilter> applied,
        Func<IReadOnlyList<string>?, IReadOnlyList<string>> notNarrowedBy, IndicatorMeta meta, ReportFormat format, ReportText text, BaseSettings bases,
        CancellationToken ct)
    {
        // The name shown for each column, once for the widget: its own labels, else its indicator's current ones (design D10).
        var names = ColumnLabels.For(meta.Columns, widget.Config.Display?.Labels);
        // What the widget declares for the order of its values and the text they are shown with (add-declared-category-order, design D11):
        // its own order, else its indicator's current one. Null when it declares nothing, and every table is then built as it always was.
        var orders = CategoryOrder.For(widget.ChartType, widget.Config, meta.Columns);
        // A stat tile with a statistic names it in what it plots: "Median of {what}" (design D2 of improve-stat-cards).
        var caption = Plotted(widget.Config, meta.Label, names);
        if (widget.ChartType == ChartTypes.Stat && StatisticKey(widget.Config.Statistic) is { } statisticKey)
        {
            caption = text.Format(statisticKey, caption);
        }

        ReportWidget Without(WidgetOutcome outcome, string reason, IReadOnlyList<string>? notes = null) =>
            new(position, widget.Title, widget.ChartType, caption, outcome, [], [], null, reason, notes ?? []);

        IndicatorQueryOutcome outcome;
        try
        {
            outcome = await indicators.QueryAsync(
                projectId, WidgetQuery.For(widget.ChartType, widget.Config), widget.ChartType, ct, applied, WidgetQuery.Overall(widget.ChartType, widget.Config),
                WidgetQuery.Unfiltered(widget.ChartType, widget.Config));
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

        // A ratio that would add up several answers of a multiple-choice question has no rows, and a column to name (design D4b, D7 of
        // improve-stat-cards): it is a widget whose settings no longer fit, before the empty-rows branch.
        if (response.CombinedCategories is { } combined)
        {
            return Without(WidgetOutcome.NoLongerFits, text.Format("combinedCategories", combined));
        }

        var notes = new List<string>();
        var notNarrowed = notNarrowedBy(response.NotFiltered);
        if (notNarrowed.Count > 0)
        {
            notes.Add(text.Format("notFiltered", string.Join(", ", notNarrowed)));
        }

        if (response.RowLimitReached)
        {
            // A box plot reads every row of the table: what it counts, and what the limit cuts, is boxes.
            notes.Add(text.Format(widget.ChartType == ChartTypes.Boxplot ? "rowLimitBoxes" : "rowLimit", response.Rows.Count));
        }

        if (response.LeftOut is > 0 and var leftOut)
        {
            notes.Add(text.LeftOut(leftOut));
        }

        // The lines under a widget (design D11, D13 of add-small-sample-warnings): the overall line that is not drawn over answers, the small
        // bases, the base n and the no-data values, in that order, among the notes above the table (the footnote follows the table, last).
        var answerColumns = BaseFigures.AnswerColumnsOf(meta.Columns);
        // A stat tile draws its n line among its own lines (design D3 of improve-stat-cards), so it is not written a second time among its notes.
        List<string> BaseNotes(int small, bool nLineInNotes = true)
        {
            var figures = BaseFigures.Names(widget.Config.Ratio, names, BaseFigures.BaseColumnOf(meta.Columns));
            var lines = new List<string>();
            if (ReferenceLines.OverallLeftOut(widget.ChartType, widget.Config, answerColumns))
            {
                lines.Add(text["noOverall"]);
            }

            if (bases.MinN is { } threshold && BaseFigures.SmallValuesNote(small, threshold, format) is { } smallNote)
            {
                lines.Add(smallNote);
            }

            if (nLineInNotes && bases.ShowN && BaseFigures.NLine(response.N, figures.Base, format) is { } nLine)
            {
                lines.Add(nLine);
            }

            if (BaseFigures.NoDataLine(response.NoData, value => format.Value(value, meta.Format, meta.Unit), bases.ShowN, format) is { } noDataLine)
            {
                lines.Add(noDataLine);
            }

            return lines;
        }

        if (response.Rows.Count == 0)
        {
            return Without(WidgetOutcome.NoRows, text["empty"], [.. notes, .. BaseNotes(0)]);
        }

        if (response.Series is not null)
        {
            var (seriesColumns, seriesRows, seriesCount, seriesNotes, seriesSmall) = SeriesReportTable(response, widget, caption, meta, format, text, names, orders, bases);
            return new ReportWidget(
                position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, seriesColumns, seriesRows, null, null,
                [.. notes, .. seriesNotes, .. BaseNotes(seriesSmall)], seriesCount)
            {
                ReferenceLines = ReferenceLinesOf(widget, response, meta, format, text, answerColumns),
            };
        }

        if (widget.ChartType == ChartTypes.Stat)
        {
            return new(position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, [], [],
                format.Value(response.Rows[0].Value, meta.Format, meta.Unit), null, [.. notes, .. BaseNotes(0, nLineInNotes: false)],
                StatColumn: ValueColumn(caption, meta), StatCell: ValueCell(response.Rows[0].Value, meta, format))
            {
                StatLines = StatLinesOf(widget, response, meta, format, text, names, bases),
            };
        }

        // A widget made of a series that came back without one cannot be told apart from a settings change: it no longer fits.
        if (ChartTypes.NeedsSeries(widget.ChartType))
        {
            return Without(WidgetOutcome.NoLongerFits, text["noLongerFits"], notes);
        }

        // Every row of a bar, column, line, pie or map is a value of its table, and so is a value that can have a small base.
        var small = widget.ChartType is ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line or ChartTypes.Pie or ChartTypes.Map
            ? response.Rows.Count(row => BaseFigures.Small(row.N, bases.MinN))
            : 0;
        var (columns, rows) = widget.ChartType switch
        {
            ChartTypes.Histogram => HistogramTable(response.Rows, widget.Config, caption, meta, format, text),
            ChartTypes.Pie => PieTable(response.Rows, widget.Config, caption, meta, format, text, names, orders, bases),
            ChartTypes.Map => MapTable(response, widget.Config, caption, meta, format, text, names, orders, bases),
            ChartTypes.Bar or ChartTypes.Column or ChartTypes.Line =>
                AxisTable(response.Rows, widget.Config, caption, meta, format, text, names, natural: widget.ChartType == ChartTypes.Line, orders, bases),
            ChartTypes.Boxplot => BoxplotTable(response.Boxes ?? [], widget.Config, meta, format, text, names, orders),
            _ => throw new InvalidOperationException($"A {widget.ChartType} widget has no table."),
        };
        return new ReportWidget(position, widget.Title, widget.ChartType, caption, WidgetOutcome.Shown, columns, rows, null, null, [.. notes, .. BaseNotes(small)])
        {
            ReferenceLines = ReferenceLinesOf(widget, response, meta, format, text, answerColumns),
        };
    }

    /// <summary>The key of the words that name a statistic in what a tile plots ("Median of {0}"); null for none or an unknown one.</summary>
    public static string? StatisticKey(string? statistic) => statistic switch
    {
        StatStatistics.Median => "statMedian",
        StatStatistics.P25 => "statP25",
        StatStatistics.P75 => "statP75",
        StatStatistics.Mean => "statMean",
        _ => null,
    };

    /// <summary>
    /// The lines under a stat tile's figure (design D3, D7 of improve-stat-cards), in the order the tile draws them: its n line, when the
    /// tile chooses it (<c>display.secondary</c>) or the base n line is in force for it, and then its value without the dashboard filters,
    /// when the tile chose it and the query gave one. A tile with a statistic names its base column or nothing ("n = 37": a number of
    /// values); one without names its base column, else its denominator. Twin of the lines of <c>StatWidget</c> in tamiza-ui.
    /// </summary>
    private static IReadOnlyList<string> StatLinesOf(
        Widget widget, IndicatorQueryResponse response, IndicatorMeta meta, ReportFormat format, ReportText text, Func<string, string> names, BaseSettings bases)
    {
        var secondary = widget.Config.Display?.Secondary;
        var lines = new List<string>();
        if (secondary?.Contains(StatSecondaryLines.N) == true || bases.ShowN)
        {
            var baseColumn = BaseFigures.BaseColumnOf(meta.Columns);
            var name = widget.Config.Statistic is not null
                ? baseColumn is null ? null : names(baseColumn)
                : BaseFigures.Names(widget.Config.Ratio, names, baseColumn).Base;
            if (BaseFigures.NLine(response.N ?? response.Rows[0].N, name, format) is { } nLine)
            {
                lines.Add(nLine);
            }
        }

        if (secondary?.Contains(StatSecondaryLines.Unfiltered) == true && response.Unfiltered is { } unfiltered)
        {
            lines.Add(text.Format("withoutFilters", format.Value(unfiltered, meta.Format, meta.Unit)));
        }

        return lines;
    }

    /// <summary>
    /// The n column of a table with no series and the "*" after a value with a small base (design D11, D13 of add-small-sample-warnings).
    /// The column comes while the n line shows or a threshold is in force and the values have an n; the mark follows a value whose n is below
    /// the threshold, in the text only, so the workbook's cell keeps the number.
    /// </summary>
    private static bool HasNColumn(BaseSettings bases, IEnumerable<double?> ns) => (bases.ShowN || bases.MinN is not null) && ns.Any(n => n is not null);

    private static ReportColumn NColumn(ReportFormat format) => new(BaseFigures.NColumn(format), "#,##0");

    private static ReportCell NCell(double? n, ReportFormat format) => n is { } count ? new((double)count, format.Figure(count)) : new(null, "—");

    private static ReportCell Marked(ReportCell cell, double? n, BaseSettings bases) => BaseFigures.Small(n, bases.MinN) ? cell with { Text = cell.Text + " *" } : cell;

    /// <summary>
    /// The reference lines of a bar, column or line that is shown (design D10 of add-chart-annotations): the values of
    /// <see cref="ReferenceLines.Of"/>, each with its own label or the default of its kind in the email's language, and its value written
    /// as the widget's values are (the indicator's format and the widget's own unit); a <c>value</c> line of a layout stacked to 100% is a
    /// share, as that layout's cells are. Any other kind of widget has none.
    /// </summary>
    private static IReadOnlyList<ReportReferenceLine> ReferenceLinesOf(
        Widget widget, IndicatorQueryResponse response, IndicatorMeta meta, ReportFormat format, ReportText text, IReadOnlyCollection<string> answerColumns)
    {
        // An overall line over the indicator's declared answers is left out (design D16 of add-small-sample-warnings); its note is among the widget's notes.
        var lines = ReferenceLines.Of(widget.ChartType, widget.Config, response, answerColumns);
        if (lines.Count == 0)
        {
            return [];
        }

        // A layout means something only with a series (IndicatorValidation.CheckDisplay reads it the same way).
        var shares = widget.Config.SeriesBy is not null && SeriesTable.ShowsShares(widget.ChartType, widget.Config.Layout);
        return [.. lines.Select(line => new ReportReferenceLine(
            line.Kind,
            line.Label ?? text[ReportText.ReferenceLabelKey(line.Kind)],
            line.Value,
            shares ? format.Share(line.Value) : format.Value(line.Value, meta.Format, meta.Unit)))];
    }

    /// <summary>The header of a table's group column: the name shown for it, or none when the widget has no group column.</summary>
    private static string GroupHeader(string? groupBy, Func<string, string> names) => groupBy is null ? "" : names(groupBy);

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

    /// <summary>
    /// Bar, column and line: the group and the plotted value, as <c>axis-chart.ts</c> lists them. A line goes in natural order; the groups
    /// then follow the order the widget declares, and are written without their numbers when the column hides them.
    /// </summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) AxisTable(
        IReadOnlyList<IndicatorRow> rows, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text,
        Func<string, string> names, bool natural, CategoryOrders? orders, BaseSettings bases)
    {
        IReadOnlyList<IndicatorRow> ordered = natural ? rows.Order(NaturalOrder.ByGroup.Instance).ToList() : rows;
        ordered = CategoryOrder.Arrange(ordered, r => OrderEntry.OfValue(r.Group), orders?.Groups);
        var shown = GroupTexts(rows.Select(r => r.Group), orders);
        // The n column comes after the value, while a base setting is in force and the values have an n (design D13).
        var withN = HasNColumn(bases, rows.Select(r => r.N));
        IReadOnlyList<ReportColumn> columns = withN
            ? [new(GroupHeader(config.GroupBy, names)), ValueColumn(caption, meta), NColumn(format)]
            : [new(GroupHeader(config.GroupBy, names)), ValueColumn(caption, meta)];
        return (columns, ordered.Select(r =>
        {
            var cells = new List<ReportCell> { GroupCell(CategoryOrder.Shown(shown, r.Group), text), Marked(ValueCell(r.Value, meta, format), r.N, bases) };
            if (withN)
            {
                cells.Add(NCell(r.N, format));
            }

            return (IReadOnlyList<ReportCell>)cells;
        }).ToList());
    }

    /// <summary>The text each group is written with when its column hides numbered prefixes (design D5); null writes a group as it is.</summary>
    private static IReadOnlyDictionary<string, string>? GroupTexts(IEnumerable<string?> groups, CategoryOrders? orders) =>
        orders?.HideGroupPrefixes == true ? CategoryOrder.ShownValues(groups, true) : null;

    /// <summary>
    /// A widget with a series, as its cross table (design D8): the group, one column per series and a "Total" column, then a "Total" row.
    /// A 100% layout and a Likert widget show shares in the series columns and have no "Total" row.
    /// </summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>, int, IReadOnlyList<string>, int) SeriesReportTable(
        IndicatorQueryResponse response, Widget widget, string caption, IndicatorMeta meta, ReportFormat format, ReportText text, Func<string, string> names,
        CategoryOrders? orders, BaseSettings bases)
    {
        var table = CategoryOrder.ArrangeTable(SeriesTable.From(response, widget.ChartType, widget.Config.Layout), orders);
        var shares = SeriesTable.ShowsShares(widget.ChartType, widget.Config.Layout);
        var valueColumn = ValueColumn(caption, meta);
        var groupText = GroupTexts(table.Rows.Select(r => r.Group), orders);
        var seriesText = orders?.HideSeriesPrefixes == true
            ? CategoryOrder.ShownValues(table.Columns.Where(c => c.Kind == SeriesKinds.Value).Select(c => c.Series), true)
            : null;
        string Label(SeriesTableColumn column) => column.Kind switch
        {
            SeriesKinds.None => text["noGroup"],
            SeriesKinds.Other => text["other"],
            _ => CategoryOrder.Shown(seriesText, column.Series) ?? "",
        };

        var columns = new List<ReportColumn> { new(GroupHeader(widget.Config.GroupBy, names)) };
        columns.AddRange(table.Columns.Select(c => shares ? new ReportColumn(Label(c), "0.0%") : valueColumn with { Header = Label(c) }));
        columns.Add(valueColumn with { Header = text["total"] });

        ReportCell Value(double? value) => value is { } number ? ValueCell(number, meta, format) : new(null, "—");
        ReportCell Share(double? share) => share is { } fraction ? new(fraction, format.Share(fraction)) : new(null, "—");

        // A cell with a small base is followed by "*" (design D13 of add-small-sample-warnings); the totals are not marked.
        var small = 0;
        ReportCell Body(SeriesTableRow row, int i)
        {
            var cell = shares ? Share(row.Shares?[i]) : Value(row.Cells[i]);
            if (row.Cells[i] is null || !BaseFigures.Small(row.Figures?[i]?.N, bases.MinN))
            {
                return cell;
            }

            small++;
            return Marked(cell, row.Figures![i]!.N, bases);
        }

        var rows = new List<IReadOnlyList<ReportCell>>();
        foreach (var row in table.Rows)
        {
            var cells = new List<ReportCell> { GroupCell(CategoryOrder.Shown(groupText, row.Group), text) };
            cells.AddRange(row.Cells.Select((_, i) => Body(row, i)));
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
        return (columns, rows, table.Columns.Count, notes, small);
    }

    /// <summary>
    /// The format a box plot shows its figures in: a count is written as a decimal, because quartiles, medians and means can fall between
    /// whole numbers. A box's numbers of values and of outliers are always whole. Twin of <c>boxFormat</c> in tamiza-ui <c>indicators/format.ts</c>.
    /// </summary>
    public static string? BoxFormat(string? format) => format == ReportFormat.Count ? ReportFormat.Decimal : format;

    /// <summary>
    /// A box plot, as its "Show as table" view lists it (design D6, D8): one row per box, in the chart's order, with its number of values,
    /// its five figures, its mean and its number of outliers. The box of rows with no group value reads "(none)", and the one box of a
    /// widget with no group column "All rows".
    /// </summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) BoxplotTable(
        IReadOnlyList<IndicatorBox> boxes, WidgetConfig config, IndicatorMeta meta, ReportFormat format, ReportText text, Func<string, string> names,
        CategoryOrders? orders)
    {
        // In the order the widget declares for its boxes; as the query sent them (natural, or by median) without a declaration.
        boxes = CategoryOrder.Arrange(boxes, box => OrderEntry.OfValue(box.Group), orders?.Groups);
        var shown = GroupTexts(boxes.Select(box => box.Group), orders);
        var boxMeta = meta with { Format = BoxFormat(meta.Format) ?? ReportFormat.Decimal };
        ReportColumn Figure(string key) => ValueColumn(text[key], boxMeta);
        ReportCell Number(double value) => ValueCell(value, boxMeta, format);
        ReportCell Whole(long value) => new((double)value, format.Whole(value));
        var columns = new List<ReportColumn>
        {
            new(config.GroupBy is null ? text["group"] : names(config.GroupBy)),
            new(text["values"], "#,##0"),
            Figure("minimum"), Figure("q1"), Figure("median"), Figure("q3"), Figure("maximum"), Figure("mean"),
            new(text["outliers"], "#,##0"),
        };
        var rows = boxes.Select(box =>
        {
            var label = CategoryOrder.Shown(shown, box.Group) ?? text[config.GroupBy is null ? "allRows" : "noGroup"];
            return (IReadOnlyList<ReportCell>)
            [
                new(label, label), Whole(box.Values),
                Number(box.Min), Number(box.Q1), Number(box.Median), Number(box.Q3), Number(box.Max), Number(box.Mean),
                Whole(box.OutlierCount),
            ];
        }).ToList();
        return (columns, rows);
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

    /// <summary>A pie chart: every group value, in the order the rows list them (or the order the widget declares), with its share of the positive total.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) PieTable(
        IReadOnlyList<IndicatorRow> rows, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text, Func<string, string> names,
        CategoryOrders? orders, BaseSettings bases)
    {
        var total = rows.Sum(r => Math.Max(0, r.Value));
        var shown = GroupTexts(rows.Select(r => r.Group), orders);
        // The n column comes after the value and before the share (design D13 of add-small-sample-warnings).
        var withN = HasNColumn(bases, rows.Select(r => r.N));
        List<ReportColumn> columns = [new(GroupHeader(config.GroupBy, names)), ValueColumn(caption, meta)];
        if (withN)
        {
            columns.Add(NColumn(format));
        }

        columns.Add(new(text["share"], "0.0%"));
        return (columns,
            CategoryOrder.Arrange(rows, r => OrderEntry.OfValue(r.Group), orders?.Groups).Select(r =>
            {
                ReportCell share = r.Value > 0 && total > 0 ? new(r.Value / total, format.Share(r.Value / total)) : new(null, "—");
                var cells = new List<ReportCell> { GroupCell(CategoryOrder.Shown(shown, r.Group), text), Marked(ValueCell(r.Value, meta, format), r.N, bases) };
                if (withN)
                {
                    cells.Add(NCell(r.N, format));
                }

                cells.Add(share);
                return (IReadOnlyList<ReportCell>)cells;
            }).ToList());
    }

    /// <summary>A map: each shape's group, its plotted value and each tooltip column under its label, without the geometry.</summary>
    private static (IReadOnlyList<ReportColumn>, IReadOnlyList<IReadOnlyList<ReportCell>>) MapTable(
        IndicatorQueryResponse response, WidgetConfig config, string caption, IndicatorMeta meta, ReportFormat format, ReportText text, Func<string, string> names,
        CategoryOrders? orders, BaseSettings bases)
    {
        var shown = GroupTexts(response.Rows.Select(r => r.Group), orders);
        var items = config.Tooltip;
        var types = response.TooltipTypes ?? [];
        var columns = new List<ReportColumn> { new(GroupHeader(config.GroupBy, names)), ValueColumn(caption, meta) };
        // The n column comes after the value, before the tooltip columns (design D13 of add-small-sample-warnings).
        var withN = HasNColumn(bases, response.Rows.Select(r => r.N));
        if (withN)
        {
            columns.Add(NColumn(format));
        }

        for (var i = 0; i < items.Count; i++)
        {
            var type = i < types.Count ? types[i] : (DataColumnType?)null;
            columns.Add(new(items[i].Label?.Trim() is { Length: > 0 } label ? label : names(items[i].Column), TooltipExcelFormat(items[i], type)));
        }

        return (columns, response.Rows.Select(r =>
        {
            var cells = new List<ReportCell> { GroupCell(CategoryOrder.Shown(shown, r.Group), text), Marked(ValueCell(r.Value, meta, format), r.N, bases) };
            if (withN)
            {
                cells.Add(NCell(r.N, format));
            }

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

    /// <summary>
    /// What a widget plots, as <c>plotted</c> in tamiza-ui <c>indicators/format.ts</c> writes it: its own caption text when it has one, else
    /// the indicator's label, with the name shown for the measure, or for the ratio's two columns, when the widget names them (design D6).
    /// With no <paramref name="names"/>, the columns are written as they are.
    /// </summary>
    public static string Plotted(WidgetConfig config, string label, Func<string, string>? names = null)
    {
        // The widget's own caption text replaces the whole "what", the label and the column names together (design D3 of the widget context).
        if (config.Display?.Caption?.Text is { Length: > 0 } own)
        {
            return own;
        }

        names ??= column => column;
        return config.Ratio is { } ratio ? $"{label}: {names(ratio.Numerator)} / {names(ratio.Denominator)}"
            : config.Measure is { } measure ? $"{label}: {names(measure)}"
            : label;
    }

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
