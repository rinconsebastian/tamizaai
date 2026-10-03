using System.Globalization;
using MiniExcelLibs;
using MiniExcelLibs.Csv;

namespace Tamiza.Api.Projects;

public sealed record PreviewRow(int Row, IReadOnlyList<string> Values, int? Target);

public sealed record RowError(int Row, string Message);

/// <summary>What an imported file would produce; nothing is saved until the user maps the dimensions and saves.</summary>
public sealed record ImportPreview(IReadOnlyList<string> Dimensions, IReadOnlyList<PreviewRow> Rows, IReadOnlyList<RowError> Errors);

/// <summary>Reads a sampling frame from CSV or XLSX: header row with the dimensions and a <c>target</c> column.</summary>
public static class SamplingFrameImport
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const string TargetColumn = "target";

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

    /// <summary>Returns the preview, or a file-level error message (unreadable file, too many rows).</summary>
    public static (ImportPreview? Preview, string? FileError) Read(Stream content, ExcelType type)
    {
        List<List<object?>> table;
        try
        {
            table = ReadTable(content, type);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (null, "The file could not be read. Save it as CSV (UTF-8) or XLSX and try again.");
        }

        var headerIndex = table.FindIndex(row => row.Any(HasValue));
        if (headerIndex < 0)
        {
            return (null, "The file is empty.");
        }

        var header = table[headerIndex].Select(Text).ToList();
        var dataRows = table.Skip(headerIndex + 1)
            .Select((cells, offset) => (Number: headerIndex + offset + 2, Cells: cells))
            .Where(r => r.Cells.Any(HasValue))
            .ToList();
        if (dataRows.Count > SamplingFrameValidator.MaxRows)
        {
            return (null, $"The file has more than {SamplingFrameValidator.Format(SamplingFrameValidator.MaxRows)} rows.");
        }

        var headerRow = headerIndex + 1;
        var errors = new List<RowError>();
        var targetColumn = header.FindIndex(h => string.Equals(h, TargetColumn, StringComparison.OrdinalIgnoreCase));
        if (targetColumn < 0)
        {
            errors.Add(new RowError(headerRow, "The file needs a 'target' column with the number of surveys for each row."));
        }

        var dimensionColumns = Enumerable.Range(0, header.Count).Where(i => i != targetColumn && header[i].Length > 0).ToList();
        var dimensions = SamplingFrameValidator.ValidateDimensionNames(
            dimensionColumns.Select(i => (string?)header[i]).ToList(),
            (_, message) => errors.Add(new RowError(headerRow, message)));

        var rows = dataRows.Select(r => (
            Values: (IReadOnlyList<string?>?)dimensionColumns.Select(i => (string?)Text(Cell(r.Cells, i))).ToList(),
            Target: targetColumn < 0 ? null : Number(Cell(r.Cells, targetColumn)))).ToList();
        var targets = SamplingFrameValidator.ValidateTargets(dimensions.Count, rows,
            (index, part, message) =>
            {
                // Without a target column, the header error already explains every missing target.
                if (part != "target" || targetColumn >= 0)
                {
                    errors.Add(new RowError(index is null ? headerRow : dataRows[index.Value].Number, message));
                }
            },
            index => $"row {dataRows[index].Number}");

        var preview = dataRows.Select((r, i) => new PreviewRow(r.Number, targets[i].Values, rows[i].Target is { } t && t == decimal.Truncate(t) ? (int?)t : null)).ToList();
        return (new ImportPreview(dimensions, preview, errors.OrderBy(e => e.Row).ToList()), null);
    }

    private static List<List<object?>> ReadTable(Stream content, ExcelType type)
    {
        MiniExcelLibs.IConfiguration? configuration = null;
        if (type == ExcelType.CSV)
        {
            configuration = new CsvConfiguration { Seperator = DetectSeparator(content) };
        }

        return MiniExcel.Query(content, useHeaderRow: false, excelType: type, configuration: configuration)
            .Take(SamplingFrameValidator.MaxRows + 2)
            .Select(row => ((IDictionary<string, object?>)row).Values.ToList())
            .ToList();
    }

    /// <summary>Spreadsheets in many locales (including Spanish) export CSV with ';' instead of ','.</summary>
    private static char DetectSeparator(Stream content)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var firstLine = reader.ReadLine() ?? "";
        content.Position = 0;
        return firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
    }

    private static object? Cell(List<object?> cells, int index) => index < cells.Count ? cells[index] : null;

    private static bool HasValue(object? cell) => Text(cell).Length > 0;

    private static string Text(object? cell) => cell switch
    {
        null => "",
        double number => number.ToString(CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(cell, CultureInfo.InvariantCulture)?.Trim() ?? "",
    };

    private static decimal? Number(object? cell) => cell switch
    {
        double number when !double.IsNaN(number) && Math.Abs(number) < 1e15 => (decimal)number,
        _ => decimal.TryParse(Text(cell), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
    };
}
