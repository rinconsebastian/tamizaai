using System.Data;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.OpenXml;
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
        var summaryItem = Item("summary") == "Resumen" ? "Elemento" : "Item";
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
        foreach (var stat in model.Stats)
        {
            summary.Add(Line(stat.Title, stat.StatValue ?? stat.Reason ?? ""));
        }

        foreach (var widget in model.Widgets.Where(w => w.Notes.Count > 0 || (!w.IsStat && w.Reason is not null)))
        {
            summary.Add(Line($"{Item("notes")}: {widget.Title}", string.Join(" ", widget.Notes.Prepend(widget.IsStat ? null : widget.Reason).OfType<string>())));
        }

        // The summary holds text, which a number format leaves as it is: its headers never block a widget sheet's format.
        sheets[SheetName(text["summary"], sheets)] = summary;

        Dictionary<string, object?> Line(string item, string value) => new() { [summaryItem] = item, [summaryValue] = Cut(value) };

        foreach (var widget in model.Tables)
        {
            var name = SheetName($"{widget.Position}. {widget.Title}", sheets);
            if (widget.Reason is { } reason)
            {
                sheets[name] = new List<Dictionary<string, object?>> { new() { [Item("notes")] = reason } };
                continue;
            }

            var headers = Headers(widget, formats);
            var rows = widget.Rows.Select(row =>
            {
                var cells = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Count; i++)
                {
                    cells[headers[i]] = row[i].Value is string value ? Cut(value) : row[i].Value;
                }

                return cells;
            }).ToList();
            sheets[name] = rows.Count > 0 ? rows : EmptyTable(headers);
        }

        formats.TryAdd(summaryItem, null);
        formats.TryAdd(summaryValue, null);
        var columns = formats.Select(f => new DynamicExcelColumn(f.Key) { Format = f.Value, Width = 24 }).ToArray();
        var configuration = new OpenXmlConfiguration { FastMode = false, EnableAutoWidth = false, DynamicColumns = columns };
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            MiniExcel.SaveAs(file, sheets, printHeader: true, excelType: ExcelType.XLSX, configuration: configuration);
        }

        return new FileInfo(path).Length;
    }

    /// <summary>
    /// The sheet's headers, each unique in the sheet and, where it needs a number format, unique in the workbook for that format. The
    /// unit is written in the header, since the cells hold bare numbers.
    /// </summary>
    private static List<string> Headers(ReportWidget widget, Dictionary<string, string?> formats)
    {
        var headers = new List<string>();
        foreach (var column in widget.Columns)
        {
            var header = column.Unit is { Length: > 0 } unit ? $"{column.Header} ({unit})" : column.Header;
            header = header.Length == 0 ? "-" : header;
            if (formats.TryGetValue(header, out var taken) && taken != column.ExcelFormat)
            {
                header = $"{header} ({widget.Position})";
            }

            var unique = header;
            for (var n = 2; headers.Contains(unique, StringComparer.Ordinal); n++)
            {
                unique = $"{header} ({n})";
            }

            headers.Add(unique);
            formats.TryAdd(unique, column.ExcelFormat);
        }

        return headers;
    }

    /// <summary>A header row for a table without rows: MiniExcel writes none for an empty list.</summary>
    private static DataTable EmptyTable(IReadOnlyList<string> headers)
    {
        var table = new DataTable();
        foreach (var header in headers)
        {
            table.Columns.Add(header, typeof(string));
        }

        return table;
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

    private static string Cut(string value) => XlsxExport.CutToCell(value, out _);
}
