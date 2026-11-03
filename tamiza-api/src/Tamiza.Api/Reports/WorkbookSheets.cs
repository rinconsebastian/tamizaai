using System.Data;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.OpenXml;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Reports;

/// <summary>
/// What the two workbooks that hold a widget's table share (the scheduled email's and a widget's own): the headers of a table, its
/// rows, a header row for an empty table, and the file. MiniExcel keys a column's number format by its header for the whole workbook,
/// so a header that two sheets need with different formats gets the later sheet's number in brackets.
/// </summary>
internal static class WorkbookSheets
{
    /// <summary>
    /// The sheet's headers, each unique in the sheet and, where it needs a number format, unique in the workbook for that format. The
    /// unit is written in the header, since the cells hold bare numbers.
    /// </summary>
    public static List<string> Headers(IReadOnlyList<ReportColumn> columns, int position, Dictionary<string, string?> formats)
    {
        var headers = new List<string>();
        foreach (var column in columns)
        {
            var header = column.Unit is { Length: > 0 } unit ? $"{column.Header} ({unit})" : column.Header;
            header = header.Length == 0 ? "-" : header;
            if (formats.TryGetValue(header, out var taken) && taken != column.ExcelFormat)
            {
                header = $"{header} ({position})";
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

    /// <summary>A table's rows under <paramref name="headers"/>, a header row alone when there is none. Long text is cut to a cell.</summary>
    public static object Table(IReadOnlyList<IReadOnlyList<ReportCell>> rows, IReadOnlyList<string> headers)
    {
        var cells = rows.Select(row =>
        {
            var line = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++)
            {
                line[headers[i]] = row[i].Value is string value ? Cut(value) : row[i].Value;
            }

            return line;
        }).ToList();
        return cells.Count > 0 ? cells : EmptyTable(headers);
    }

    /// <summary>A header row for a table without rows: MiniExcel writes none for an empty list.</summary>
    public static DataTable EmptyTable(IReadOnlyList<string> headers)
    {
        var table = new DataTable();
        foreach (var header in headers)
        {
            table.Columns.Add(header, typeof(string));
        }

        return table;
    }

    /// <summary>
    /// Writes the sheets to <paramref name="path"/> and returns the file's size in bytes. With <paramref name="maxBytes"/>, a file that
    /// grows past it throws <see cref="ExportTooLargeException"/> instead of filling the disk.
    /// </summary>
    public static long Save(string path, Dictionary<string, object> sheets, Dictionary<string, string?> formats, long? maxBytes = null)
    {
        var columns = formats.Select(f => new DynamicExcelColumn(f.Key) { Format = f.Value, Width = 24 }).ToArray();
        var configuration = new OpenXmlConfiguration { FastMode = false, EnableAutoWidth = false, DynamicColumns = columns };
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Stream output = maxBytes is { } limit ? new LimitedStream(file, limit) : file;
            MiniExcel.SaveAs(output, sheets, printHeader: true, excelType: ExcelType.XLSX, configuration: configuration);
        }

        return new FileInfo(path).Length;
    }

    /// <summary>Text cut to the longest an XLSX cell holds.</summary>
    public static string Cut(string value) => XlsxExport.CutToCell(value, out _);
}
