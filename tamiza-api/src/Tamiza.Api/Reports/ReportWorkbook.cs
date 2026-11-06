using Tamiza.Api.CleanData;

namespace Tamiza.Api.Reports;

/// <summary>
/// The workbook a scheduled email attaches (design D11): a "Summary" sheet in text, then a sheet per widget that is not a stat tile,
/// holding its whole table with numbers as numbers in the indicator's format. MiniExcel keys a column's number format by its header
/// for the whole workbook, so a header that two sheets need with different formats gets the later sheet's number in brackets.
/// </summary>
public static class ReportWorkbook
{
    /// <summary>The attachment's file name: the project, the dashboard and the day of sending in the project's time zone.</summary>
    public static string FileName(ReportModel model) =>
        $"{ExportFileName.Slug(model.ProjectName)}_{ExportFileName.Slug(model.DashboardName, "dashboard")}_"
        + $"{model.Format.LocalDay(model.GeneratedAt):yyyy-MM-dd}.xlsx";

    /// <summary>Writes the workbook to <paramref name="path"/> and returns its size in bytes.</summary>
    public static long Write(ReportModel model, string path)
    {
        var text = model.Text;
        var sheets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var formats = new Dictionary<string, string?>(StringComparer.Ordinal);

        string Item(string key) => text[key];
        var summaryItem = Item("item");
        var summaryValue = Item("value");
        var summary = new List<Dictionary<string, object?>>
        {
            Line(Item("project"), model.ProjectName),
            Line(Item("dashboard"), model.DashboardName),
            Line(Item("generated"), model.Format.Moment(model.GeneratedAt)),
            Line(Item("dataPublished"), model.DataPublishedAt is { } at ? model.Format.Moment(at) : Item("dataNotPublished")),
        };
        summary.AddRange(model.Filters.Count == 0
            ? [Line(Item("filters"), Item("noFilters"))]
            : model.Filters.Select(f => Line($"{Item("filters")}: {f.Label}", f.Value)));
        if (model.Source is { } source)
        {
            summary.Add(Line(Item("source"), source));
        }

        foreach (var stat in model.Stats)
        {
            // The value, then the tile's lines after it (design D7 of improve-stat-cards).
            summary.Add(Line(stat.Title, string.Join("; ", stat.StatLines.Prepend(stat.StatValue ?? stat.Reason ?? ""))));
        }

        // A widget's subtitle and footnote, then its reference lines, come before its notes, each under the widget's title.
        foreach (var widget in model.Widgets.Where(w =>
                     w.Subtitle is not null || w.Footnote is not null || w.ReferenceLines.Count > 0 || w.Notes.Count > 0 || (!w.IsStat && w.Reason is not null)))
        {
            if (widget.Subtitle is { } subtitle)
            {
                summary.Add(Line($"{Item("subtitle")}: {widget.Title}", subtitle));
            }

            if (widget.Footnote is { } footnote)
            {
                summary.Add(Line($"{Item("footnote")}: {widget.Title}", footnote));
            }

            if (widget.ReferenceLines.Count > 0)
            {
                summary.Add(Line($"{Item("referenceLines")}: {widget.Title}", string.Join(" · ", widget.ReferenceLines.Select(l => l.Line))));
            }

            if (widget.Notes.Count > 0 || (!widget.IsStat && widget.Reason is not null))
            {
                summary.Add(Line($"{Item("notes")}: {widget.Title}", string.Join(" ", widget.Notes.Prepend(widget.IsStat ? null : widget.Reason).OfType<string>())));
            }
        }

        // The summary holds text, which a number format leaves as it is: its headers never block a widget sheet's format.
        sheets[SheetName(text["summary"], sheets)] = summary;

        Dictionary<string, object?> Line(string item, string value) => new() { [summaryItem] = item, [summaryValue] = WorkbookSheets.Cut(value) };

        foreach (var widget in model.Tables)
        {
            var name = SheetName($"{widget.Position}. {widget.Title}", sheets);
            if (widget.Reason is { } reason)
            {
                sheets[name] = new List<Dictionary<string, object?>> { new() { [Item("notes")] = reason } };
                continue;
            }

            var headers = WorkbookSheets.Headers(widget.Columns, widget.Position, formats);
            sheets[name] = WorkbookSheets.Table(widget.Rows, headers);
        }

        formats.TryAdd(summaryItem, null);
        formats.TryAdd(summaryValue, null);
        return WorkbookSheets.Save(path, sheets, formats);
    }

    /// <summary>
    /// A name Excel accepts (31 characters at most, none of <c>[ ] : * ? / \</c>, not starting or ending with an apostrophe, not
    /// "History"), unique in the workbook whatever its case.
    /// </summary>
    public static string SheetName(string wanted, IReadOnlyDictionary<string, object> taken)
    {
        var name = XlsxExport.SheetName(wanted).Trim().Trim('\'');
        if (name.Length == 0 || name.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            name = $"{name} 1".Trim();
        }

        var unique = name;
        for (var n = 2; taken.ContainsKey(unique); n++)
        {
            var suffix = $" ({n})";
            unique = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)].TrimEnd('\'') : name) + suffix;
        }

        return unique;
    }
}
