using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiniExcelLibs;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;

namespace Tamiza.Api.Projects;

/// <summary>A header whose column name differs from it after the naming rules.</summary>
public sealed record RenamedColumn(string Header, string Name);

/// <summary>A parsed reference table: its columns, and its rows as a JSON array of arrays in column order.</summary>
public sealed record ReferenceImport(List<ReferenceColumn> Columns, int RowCount, string RowsJson, List<RenamedColumn> Renamed);

/// <summary>
/// Reads a reference table from CSV or XLSX: the first non-empty row is the header, names follow the column naming
/// rules and each column's type (integer, decimal, date or text) comes from its non-empty values. The file is read
/// twice, types first and then the rows, so only the JSON output is held in memory.
/// </summary>
public static partial class ReferenceTableImport
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxColumns = 200;
    public const long MaxValues = 5_000_000;

    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Date = "date";
    public const string Text = "text";

    /// <summary>Whole numbers stored as doubles (XLSX) are exact up to 2^53.</summary>
    private const double MaxExactInteger = 9_007_199_254_740_992;

    /// <summary>Returns the table, or a message for the whole file.</summary>
    public static (ReferenceImport? Import, string? Error) Read(Stream content, ExcelType type)
    {
        try
        {
            return ReadChecked(content, type);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (null, "The file could not be read. Save it as CSV (UTF-8) or XLSX and try again.");
        }
    }

    private static (ReferenceImport? Import, string? Error) ReadChecked(Stream content, ExcelType type)
    {
        var semicolons = type == ExcelType.CSV && SpreadsheetReader.DetectSeparator(content) == ';';
        var separator = semicolons ? ';' : ',';

        // Pass 1: header, column count, row count and what every column's values can be.
        var headerIndex = -1;
        List<object?> header = [];
        var columns = new List<ColumnState>();
        var rowCount = 0;
        var index = -1;
        foreach (var cells in SpreadsheetReader.Rows(content, type, separator))
        {
            index++;
            var last = cells.FindLastIndex(SpreadsheetReader.HasValue);
            if (last < 0)
            {
                continue;
            }

            if (headerIndex < 0)
            {
                headerIndex = index;
                header = cells;
                Widen(columns, last + 1);
                continue;
            }

            Widen(columns, last + 1);
            if (columns.Count > MaxColumns)
            {
                return (null, $"The file has more than {MaxColumns} columns.");
            }

            rowCount++;
            if ((long)rowCount * columns.Count > MaxValues)
            {
                return (null, $"The file has more than {MaxValues.ToString("N0", CultureInfo.InvariantCulture)} values (rows times columns).");
            }

            for (var i = 0; i <= last; i++)
            {
                columns[i].See(Classify(cells[i], semicolons));
            }
        }

        if (headerIndex < 0)
        {
            return (null, "The file is empty.");
        }

        if (columns.Count > MaxColumns)
        {
            return (null, $"The file has more than {MaxColumns} columns.");
        }

        if (rowCount == 0)
        {
            return (null, "The file has no rows below its header.");
        }

        var headers = Enumerable.Range(0, columns.Count).Select(i => SpreadsheetReader.Text(SpreadsheetReader.Cell(header, i))).ToList();
        var names = ColumnNames.Deduplicate(headers.Select(ColumnNames.Reduce));
        var definitions = columns.Select((column, i) => new ReferenceColumn(names[i], headers[i], column.Type)).ToList();
        var renamed = definitions.Where(c => c.Header != c.Name).Select(c => new RenamedColumn(c.Header, c.Name)).ToList();

        // Pass 2: the rows as JSON, typed.
        content.Position = 0;
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartArray();
            index = -1;
            foreach (var cells in SpreadsheetReader.Rows(content, type, separator))
            {
                index++;
                if (index <= headerIndex || !cells.Any(SpreadsheetReader.HasValue))
                {
                    continue;
                }

                writer.WriteStartArray();
                for (var i = 0; i < definitions.Count; i++)
                {
                    WriteValue(writer, SpreadsheetReader.Cell(cells, i), definitions[i].Type, semicolons);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        return (new ReferenceImport(definitions, rowCount, Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length), renamed), null);
    }

    private static void Widen(List<ColumnState> columns, int count)
    {
        while (columns.Count < count)
        {
            // Rows seen before this column existed had no value in it.
            columns.Add(new ColumnState());
        }
    }

    private enum Kind
    {
        Empty,
        Integer,
        Decimal,
        Date,
        Text,
    }

    private sealed class ColumnState
    {
        private bool any;
        private bool numbers = true;
        private bool integers = true;
        private bool dates = true;

        public string Type => !any ? Text : dates ? Date : numbers ? (integers ? Integer : Decimal) : Text;

        public void See(Kind kind)
        {
            if (kind == Kind.Empty)
            {
                return;
            }

            any = true;
            numbers &= kind is Kind.Integer or Kind.Decimal;
            integers &= kind == Kind.Integer;
            dates &= kind == Kind.Date;
        }
    }

    private static Kind Classify(object? cell, bool semicolons) => cell switch
    {
        _ when !SpreadsheetReader.HasValue(cell) => Kind.Empty,
        double number => double.IsFinite(number) && Math.Floor(number) == number && Math.Abs(number) <= MaxExactInteger ? Kind.Integer : Kind.Decimal,
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? Kind.Date : Kind.Text,
        string text => ClassifyText(text.Trim(), semicolons),
        _ => Kind.Text,
    };

    private static Kind ClassifyText(string text, bool semicolons)
    {
        if (IsoDate().IsMatch(text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Kind.Date;
        }

        if (ParseNumber(text, semicolons) is not { } normalized)
        {
            return Kind.Text;
        }

        return long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? Kind.Integer : Kind.Decimal;
    }

    /// <summary>The number in invariant form, or null when the text is not a number (codes with leading zeros are not).</summary>
    private static string? ParseNumber(string text, bool semicolons)
    {
        if (semicolons && text.Count(c => c == ',') == 1 && !text.Contains('.'))
        {
            text = text.Replace(',', '.');
        }

        if (!NumberText().IsMatch(text) || LeadingZero().IsMatch(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? text : null;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? cell, string type, bool semicolons)
    {
        if (!SpreadsheetReader.HasValue(cell))
        {
            writer.WriteNullValue();
            return;
        }

        switch (type)
        {
            case Integer:
                writer.WriteNumberValue(cell is double whole ? (long)whole : long.Parse(ParseNumber(((string)cell!).Trim(), semicolons)!, CultureInfo.InvariantCulture));
                break;
            case Decimal:
                writer.WriteNumberValue(cell is double number ? number : double.Parse(ParseNumber(((string)cell!).Trim(), semicolons)!, CultureInfo.InvariantCulture));
                break;
            case Date:
                writer.WriteStringValue(cell is DateTime date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ((string)cell!).Trim());
                break;
            default:
                writer.WriteStringValue(cell is DateTime { TimeOfDay.Ticks: > 0 } dateTime
                    ? dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : SpreadsheetReader.Text(cell));
                break;
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^-?\d+(\.\d+)?([eE][+-]?\d+)?$")]
    private static partial Regex NumberText();

    [GeneratedRegex(@"^-?0\d")]
    private static partial Regex LeadingZero();
}
