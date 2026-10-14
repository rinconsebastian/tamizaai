using System.Globalization;
using System.Text;

namespace Tamiza.Api.CleanData;

/// <summary>What an export wrote: the data rows (not the header), and the cells cut to fit the format.</summary>
public sealed record ExportOutcome(long Rows, long CutCells);

/// <summary>
/// Writes rows as CSV (design D4): UTF-8 with a byte order mark so spreadsheets read accents, comma separated, quoted as in
/// RFC 4180, with CRLF line ends. Values are written as published: numbers and days with invariant formatting, timestamps as
/// ISO 8601 with the project's offset, true/false in lower case, and empty values as empty fields. Nothing is changed to
/// keep a spreadsheet from reading a value as a formula.
/// </summary>
public static class CsvExport
{
    /// <param name="rows">Rows of typed values in the order of <paramref name="columns"/>, as the page query reads them but not cut.</param>
    public static ExportOutcome Write(Stream output, IReadOnlyList<TableColumn> columns, IEnumerable<object?[]> rows)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        WriteLine(writer, columns.Select(c => c.Name));
        long count = 0;
        foreach (var row in rows)
        {
            WriteLine(writer, row.Select(Format));
            count++;
        }

        writer.Flush();
        return new ExportOutcome(count, 0);
    }

    /// <summary>A value as it is written to the file, before quoting.</summary>
    public static string Format(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        long whole => whole.ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable other => other.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Wraps the field in quotes, doubling quotes inside, when it holds a comma, a quote or a line break.</summary>
    public static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;

    private static void WriteLine(StreamWriter writer, IEnumerable<string> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                writer.Write(',');
            }

            writer.Write(Quote(field));
            first = false;
        }

        writer.Write("\r\n");
    }
}
