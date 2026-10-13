using System.Globalization;
using MiniExcelLibs;
using MiniExcelLibs.Csv;

namespace Tamiza.Api.Projects;

/// <summary>Reads the cells of a CSV or XLSX upload (first sheet), row by row, for the sampling frame and reference tables.</summary>
public static class SpreadsheetReader
{
    public static bool IsSupported(string fileName, out ExcelType type)
    {
        type = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => ExcelType.CSV,
            ".xlsx" => ExcelType.XLSX,
            _ => ExcelType.UNKNOWN,
        };
        return type != ExcelType.UNKNOWN;
    }

    /// <summary>
    /// The rows of the file, read lazily. CSV cells are strings; XLSX cells are strings, doubles, booleans or
    /// <see cref="DateTime"/>s. <paramref name="content"/> must be seekable (XLSX needs it, and CSV is sniffed for its
    /// separator first).
    /// </summary>
    public static IEnumerable<List<object?>> Rows(Stream content, ExcelType type, char separator = ',')
    {
        MiniExcelLibs.IConfiguration? configuration = type == ExcelType.CSV ? new CsvConfiguration { Seperator = separator } : null;
        return MiniExcel.Query(content, useHeaderRow: false, excelType: type, configuration: configuration)
            .Select(row => ((IDictionary<string, object?>)row).Values.ToList());
    }

    /// <summary>Spreadsheets in many locales (including Spanish) export CSV with ';' instead of ','.</summary>
    public static char DetectSeparator(Stream content)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var firstLine = reader.ReadLine() ?? "";
        content.Position = 0;
        return firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
    }

    public static object? Cell(List<object?> cells, int index) => index < cells.Count ? cells[index] : null;

    public static bool HasValue(object? cell) => Text(cell).Length > 0;

    /// <summary>The cell as trimmed text: numbers in invariant culture, dates as <c>yyyy-MM-dd</c>.</summary>
    public static string Text(object? cell) => cell switch
    {
        null => "",
        double number => number.ToString(CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(cell, CultureInfo.InvariantCulture)?.Trim() ?? "",
    };
}
