namespace Tamiza.Api.Reports;

/// <summary>Everything a widget's own workbook says (design D13 of add-widget-export).</summary>
/// <param name="VersionText">The version being viewed, written out ("Version 9, saved by Ana on …"), or null on the current dashboard.</param>
/// <param name="Widget">The widget's table as the email lists it; the sheet is built from it.</param>
/// <param name="IndicatorName">The indicator's name, written next to its label.</param>
/// <param name="Filters">Every filter of the dashboard with its value in force, or the text for "All", in the filter bar's order.</param>
/// <param name="NotNarrowedBy">The labels of the filters in force that do not narrow the widget.</param>
/// <param name="PublishedAt">When the widget's indicator was published; null when it has not been.</param>
public sealed record WidgetWorkbookModel(
    string ProjectName,
    string DashboardName,
    string? VersionText,
    ReportWidget Widget,
    string IndicatorLabel,
    string IndicatorName,
    IReadOnlyList<ReportFilterLine> Filters,
    IReadOnlyList<string> NotNarrowedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset ExportedAt,
    ReportFormat Format,
    ReportText Text);

/// <summary>
/// The workbook of one widget (design D13): its table first, named after its title, so a reader, <c>read_excel</c> or Power Query
/// lands on the data, then a "Summary" sheet saying what the table is. The table is the widget's table in a scheduled email's
/// workbook, written by the same code, so the two never disagree.
/// </summary>
public static class WidgetWorkbook
{
    /// <summary>
    /// Writes the workbook to <paramref name="path"/> and returns its size in bytes. A file that grows past <paramref name="maxBytes"/>
    /// throws <see cref="CleanData.ExportTooLargeException"/>.
    /// </summary>
    public static long Write(WidgetWorkbookModel model, string path, long? maxBytes = null)
    {
        var text = model.Text;
        var widget = model.Widget;
        var formats = new Dictionary<string, string?>(StringComparer.Ordinal);
        var sheets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // The data sheet is written first and takes its name against the Summary's, so a widget titled "Summary" is not the Summary.
        var summaryName = ReportWorkbook.SheetName(text["summary"], sheets);
        var dataName = ReportWorkbook.SheetName(widget.Title, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [summaryName] = 0 });

        var (columns, rows) = TableOf(widget);
        var rowCount = RowCountOf(widget);
        if (columns.Count == 0)
        {
            // Nothing could be listed, such as a table that lost its rows between the page and the file: the sheet says why.
            sheets[dataName] = new List<Dictionary<string, object?>> { new() { [text["notes"]] = WorkbookSheets.Cut(widget.Reason ?? text["empty"]) } };
            formats.TryAdd(text["notes"], null);
        }
        else
        {
            sheets[dataName] = WorkbookSheets.Table(rows, WorkbookSheets.Headers(columns, widget.Position, formats));
        }

        var item = text["item"];
        var value = text["value"];
        Dictionary<string, object?> Line(string name, string content) => new() { [item] = name, [value] = WorkbookSheets.Cut(content) };

        var summary = new List<Dictionary<string, object?>> { Line(text["project"], model.ProjectName), Line(text["dashboard"], model.DashboardName) };
        if (model.VersionText is { } version)
        {
            summary.Add(Line(text["version"], version));
        }

        summary.Add(Line(text["widget"], widget.Title));
        summary.Add(Line(text["indicator"], $"{model.IndicatorLabel} ({model.IndicatorName})"));
        summary.Add(Line(text["plotted"], widget.Caption));
        summary.AddRange(model.Filters.Count == 0
            ? [Line(text["filters"], text["noFilters"])]
            : model.Filters.Select(f => Line($"{text["filters"]}: {f.Label}", f.Value)));
        if (model.NotNarrowedBy.Count > 0)
        {
            summary.Add(Line(text["notFilteredBy"], string.Join(", ", model.NotNarrowedBy)));
        }

        summary.Add(Line(text["dataPublished"], model.PublishedAt is { } published ? model.Format.Moment(published) : text["dataNotPublished"]));
        summary.Add(Line(text["exported"], model.Format.Moment(model.ExportedAt)));
        summary.Add(Line(text["rows"], rowCount.ToString("N0", text.Culture)));

        // The "Not filtered by" note repeats the line above: the other notes and the reason are what is left to say.
        var sentence = model.NotNarrowedBy.Count > 0 ? text.Format("notFiltered", string.Join(", ", model.NotNarrowedBy)) : null;
        var notes = widget.Notes.Where(n => n != sentence).Prepend(columns.Count == 0 ? widget.Reason : null).OfType<string>().ToList();
        if (notes.Count > 0)
        {
            summary.Add(Line(text["notes"], string.Join(" ", notes)));
        }

        formats.TryAdd(item, null);
        formats.TryAdd(value, null);
        sheets[summaryName] = summary;
        return WorkbookSheets.Save(path, sheets, formats, maxBytes);
    }

    /// <summary>The rows the data sheet lists: a stat tile's one, none when the sheet only gives the reason.</summary>
    public static int RowCountOf(ReportWidget widget) => TableOf(widget) is { Columns.Count: > 0 } table ? table.Rows.Count : 0;

    /// <summary>The columns and rows to write: a stat tile is one column and one row holding its value as a number.</summary>
    private static (IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<ReportCell>> Rows) TableOf(ReportWidget widget) =>
        widget is { IsStat: true, StatColumn: { } column, StatCell: { } cell }
            ? ([column], [[cell]])
            : (widget.Columns, widget.Rows);
}
