using System.Globalization;
using MiniExcelLibs;

namespace Tamiza.Api.Projects;

public sealed record PreviewRow(int Row, IReadOnlyList<string> Values, int? Target);

public sealed record RowError(int Row, string Message);

/// <summary>A column the import did not read: a note (its name starts with <c>#</c>) or values under a column without a name.</summary>
/// <param name="Column">The spreadsheet letter of the column.</param>
public sealed record IgnoredColumn(string Column, string? Name, string Reason);

/// <summary>
/// What an imported file would produce; nothing is saved until the user maps the dimensions and saves. Rows whose target is
/// empty are left out and listed by number in <see cref="RowsWithoutTarget"/>.
/// </summary>
public sealed record ImportPreview(
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<PreviewRow> Rows,
    IReadOnlyList<RowError> Errors,
    IReadOnlyList<int> RowsWithoutTarget,
    IReadOnlyList<IgnoredColumn> IgnoredColumns);

/// <summary>
/// Reads a sampling frame from CSV or XLSX (its first sheet): the first non-empty row names the dimensions and a <c>target</c>
/// column, and each row below it is one combination. Columns whose name starts with <c>#</c> are notes, and rows with an empty
/// target are left out (design D4, D5).
/// </summary>
public static class SamplingFrameImport
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const string TargetColumn = "target";
    public const char NotePrefix = '#';

    public const string NoteReason = "note";
    public const string UnnamedReason = "unnamed";

    /// <summary>Whether the importer reads a column of this name as a note rather than a dimension.</summary>
    public static bool IsNote(string header) => header.Trim().StartsWith(NotePrefix);

    /// <summary>Returns the preview, or a file-level error message (unreadable file, too many rows).</summary>
    public static (ImportPreview? Preview, string? FileError) Read(Stream content, ExcelType type)
    {
        List<object?>? header = null;
        var headerRow = 0;
        var dataRows = new List<(int Number, List<object?> Cells)>();
        try
        {
            var separator = type == ExcelType.CSV ? SpreadsheetReader.DetectSeparator(content) : ',';
            var position = 0;
            foreach (var cells in SpreadsheetReader.Rows(content, type, separator, keepBlankLines: true))
            {
                // Positions count blank rows too, so they are the row numbers a spreadsheet program shows.
                position++;
                if (!cells.Any(HasValue))
                {
                    continue;
                }

                if (header is null)
                {
                    header = cells;
                    headerRow = position;
                    continue;
                }

                if (dataRows.Count == SamplingFrameValidator.MaxRows)
                {
                    return (null, $"The file has more than {SamplingFrameValidator.Format(SamplingFrameValidator.MaxRows)} rows.");
                }

                dataRows.Add((position, cells));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (null, "The file could not be read. Save it as CSV (UTF-8) or XLSX and try again.");
        }

        if (header is null)
        {
            return (null, "The file is empty.");
        }

        var headers = header.Select(Text).ToList();
        var errors = new List<RowError>();
        var targetColumn = headers.FindIndex(h => string.Equals(h, TargetColumn, StringComparison.OrdinalIgnoreCase));
        if (targetColumn < 0)
        {
            errors.Add(new RowError(headerRow, "The file needs a 'target' column with the number of surveys for each row."));
        }

        var ignored = IgnoredColumns(headers, dataRows.Select(r => r.Cells));
        var dimensionColumns = Enumerable.Range(0, headers.Count)
            .Where(i => i != targetColumn && headers[i].Length > 0 && !IsNote(headers[i]))
            .ToList();
        var dimensions = SamplingFrameValidator.ValidateDimensionNames(
            dimensionColumns.Select(i => (string?)headers[i]).ToList(),
            (_, message) => errors.Add(new RowError(headerRow, message)));

        // A row without a target is not planned: it is neither checked nor compared with the other rows.
        var withoutTarget = targetColumn < 0 ? [] : dataRows.Where(r => !HasValue(Cell(r.Cells, targetColumn))).ToList();
        var kept = targetColumn < 0 ? dataRows : dataRows.Where(r => HasValue(Cell(r.Cells, targetColumn))).ToList();

        var rows = kept.Select(r => (
            Values: (IReadOnlyList<string?>?)dimensionColumns.Select(i => (string?)Text(Cell(r.Cells, i))).ToList(),
            Target: targetColumn < 0 ? null : Number(Cell(r.Cells, targetColumn)))).ToList();
        var targets = SamplingFrameValidator.ValidateTargets(dimensions.Count, rows,
            (index, part, message) =>
            {
                // Without a target column, the header error already explains every missing target.
                if (part != "target" || targetColumn >= 0)
                {
                    errors.Add(new RowError(index is null ? headerRow : kept[index.Value].Number, message));
                }
            },
            index => $"row {kept[index].Number}");

        var preview = kept.Select((r, i) => new PreviewRow(r.Number, targets[i].Values, WholeTarget(rows[i].Target))).ToList();
        return (new ImportPreview(
            dimensions, preview, errors.OrderBy(e => e.Row).ToList(), withoutTarget.Select(r => r.Number).ToList(), ignored), null);
    }

    /// <summary>Note columns, and columns without a name that hold values, in column order.</summary>
    private static List<IgnoredColumn> IgnoredColumns(List<string> headers, IEnumerable<List<object?>> rows)
    {
        var width = headers.Count;
        var unnamedWithValues = new SortedSet<int>();
        foreach (var cells in rows)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                if ((i >= width || headers[i].Length == 0) && HasValue(cells[i]))
                {
                    unnamedWithValues.Add(i);
                }
            }
        }

        var ignored = new List<(int Index, IgnoredColumn Column)>();
        for (var i = 0; i < width; i++)
        {
            if (headers[i].Length > 0 && IsNote(headers[i]))
            {
                ignored.Add((i, new IgnoredColumn(ColumnLetter(i), headers[i], NoteReason)));
            }
        }

        ignored.AddRange(unnamedWithValues.Select(i => (i, new IgnoredColumn(ColumnLetter(i), null, UnnamedReason))));
        return ignored.OrderBy(c => c.Index).Select(c => c.Column).ToList();
    }

    /// <summary>The spreadsheet letter of a zero-based column index: A, B, …, Z, AA, AB, ….</summary>
    public static string ColumnLetter(int index)
    {
        var letters = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + (n - 1) % 26) + letters;
        }

        return letters;
    }

    private static int? WholeTarget(decimal? target) =>
        target is { } t && t == decimal.Truncate(t) && t is >= int.MinValue and <= int.MaxValue ? (int)t : null;

    private static object? Cell(List<object?> cells, int index) => SpreadsheetReader.Cell(cells, index);

    private static bool HasValue(object? cell) => SpreadsheetReader.HasValue(cell);

    private static string Text(object? cell) => SpreadsheetReader.Text(cell);

    private static decimal? Number(object? cell) => cell switch
    {
        double number when !double.IsNaN(number) && Math.Abs(number) < 1e15 => (decimal)number,
        _ => decimal.TryParse(Text(cell), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
    };
}
