using System.Data;
using System.Globalization;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace Tamiza.Api.CleanData;

/// <summary>
/// Writes rows as an XLSX workbook with one sheet (design D4): a header row of column names and each value in a cell of its
/// type. Numbers are numbers, days and timestamps are dates (Excel has no time zones, so a timestamp keeps the project's local
/// time), true/false are booleans and text is text, never a formula. Text longer than Excel's 32,767-character cell is cut.
/// </summary>
public static class XlsxExport
{
    /// <summary>The most characters Excel keeps in one cell.</summary>
    public const int MaxCellLength = 32_767;

    private const int MaxSheetNameLength = 31;

    /// <param name="rows">Rows of typed values in the order of <paramref name="columns"/>, as the page query reads them but not cut.</param>
    public static ExportOutcome Write(Stream output, string sheetName, IReadOnlyList<TableColumn> columns, IEnumerable<object?[]> rows)
    {
        long count = 0;
        long cut = 0;
        var names = columns.Select(c => c.Name).ToArray();
        var cells = rows.Select(row =>
        {
            count++;
            var cell = new Dictionary<string, object?>(names.Length);
            for (var i = 0; i < names.Length; i++)
            {
                var value = Cell(columns[i].Type, row[i], out var wasCut);
                cut += wasCut ? 1 : 0;
                cell[names[i]] = value;
            }

            return cell;
        });

        var configuration = new OpenXmlConfiguration { FastMode = false, EnableAutoWidth = false };
        using var enumerator = cells.GetEnumerator();
        if (enumerator.MoveNext())
        {
            MiniExcel.SaveAs(output, Chain(enumerator), printHeader: true, sheetName: SheetName(sheetName), configuration: configuration);
        }
        else
        {
            // No rows: an empty enumerable would give a sheet without a header, so describe the columns through a table.
            var table = new DataTable();
            foreach (var name in names)
            {
                table.Columns.Add(name, typeof(string));
            }

            MiniExcel.SaveAs(output, table, printHeader: true, sheetName: SheetName(sheetName), configuration: configuration);
        }

        return new ExportOutcome(count, cut);
    }

    /// <summary>The value as an Excel cell, and whether it was cut to fit one.</summary>
    public static object? Cell(DataColumnType type, object? value, out bool cut)
    {
        cut = false;
        switch (value)
        {
            case null:
                return null;
            case string text:
                switch (type)
                {
                    case DataColumnType.Date when DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day):
                        return day;
                    case DataColumnType.Timestamp when text.Length >= 19
                        && DateTime.TryParseExact(text[..19], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local):
                        return local;
                    default:
                        return CutToCell(text, out cut);
                }

            default:
                return value; // long, decimal, bool
        }
    }

    /// <summary>A sheet name Excel accepts: at most 31 characters and none of <c>[ ] : * ? / \</c>.</summary>
    public static string SheetName(string table)
    {
        var name = new string(table.Select(c => "[]:*?/\\".Contains(c, StringComparison.Ordinal) ? '_' : c).ToArray());
        if (name.Length > MaxSheetNameLength)
        {
            name = name[..MaxSheetNameLength];
        }

        return name.Length == 0 ? "Sheet1" : name;
    }

    private static string CutToCell(string text, out bool cut)
    {
        cut = text.Length > MaxCellLength;
        if (!cut)
        {
            return text;
        }

        // Do not end on the first half of a surrogate pair.
        var length = char.IsHighSurrogate(text[MaxCellLength - 1]) ? MaxCellLength - 1 : MaxCellLength;
        return text[..length];
    }

    private static IEnumerable<Dictionary<string, object?>> Chain(IEnumerator<Dictionary<string, object?>> started)
    {
        do
        {
            yield return started.Current;
        }
        while (started.MoveNext());
    }
}
