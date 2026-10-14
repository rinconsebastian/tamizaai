using System.IO.Compression;
using System.Text;
using MiniExcelLibs;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class ExportWritersTests
{
    private static readonly TableColumn[] Columns =
    [
        new("name", DataColumnType.Text, null),
        new("age", DataColumnType.Integer, null),
        new("income", DataColumnType.Decimal, null),
        new("consent", DataColumnType.Boolean, null),
        new("born", DataColumnType.Date, null),
        new("at", DataColumnType.Timestamp, null),
    ];

    private static object?[] Row(string? name, long? age = null, decimal? income = null, bool? consent = null, string? born = null, string? at = null) =>
        [name, age, income, consent, born, at];

    private static byte[] Csv(IEnumerable<object?[]> rows, IReadOnlyList<TableColumn>? columns = null)
    {
        using var stream = new MemoryStream();
        CsvExport.Write(stream, columns ?? Columns, rows);
        return stream.ToArray();
    }

    private static string CsvText(IEnumerable<object?[]> rows, IReadOnlyList<TableColumn>? columns = null) =>
        new UTF8Encoding(false).GetString(Csv(rows, columns));

    // CSV

    [Fact]
    public void A_csv_starts_with_a_utf8_byte_order_mark_then_the_header_of_column_names()
    {
        var bytes = Csv([]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("﻿name,age,income,consent,born,at\r\n", new UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void Commas_quotes_and_line_breaks_are_quoted_with_quotes_doubled()
    {
        var text = CsvText([
            Row("Bogotá, D.C."),
            Row("He said \"hi\""),
            Row("two\nlines"),
            Row("carriage\rreturn"),
            Row("plain"),
        ]);

        Assert.Equal(
            "﻿name,age,income,consent,born,at\r\n\"Bogotá, D.C.\",,,,,\r\n\"He said \"\"hi\"\"\",,,,,\r\n\"two\nlines\",,,,,\r\n\"carriage\rreturn\",,,,,\r\nplain,,,,,\r\n",
            text);
    }

    [Fact]
    public void Accents_survive_as_utf8_bytes()
    {
        var bytes = Csv([Row("Bogotá ñ ü 😀")]);

        Assert.Contains("Bogotá ñ ü 😀", new UTF8Encoding(false).GetString(bytes));
        Assert.Contains((byte)0xC3, bytes); // á is two bytes
    }

    [Fact]
    public void Values_are_written_with_invariant_formatting_and_empty_values_as_empty_fields()
    {
        var text = CsvText([Row("Ana", 31, 1234.50m, true, "2026-09-01", "2026-09-01T21:30:00-05:00"), Row(null, null, null, false)]);

        var lines = text.Split("\r\n");
        Assert.Equal("Ana,31,1234.50,true,2026-09-01,2026-09-01T21:30:00-05:00", lines[1]);
        Assert.Equal(",,,false,,", lines[2]);
    }

    [Fact]
    public void Decimals_use_a_dot_whatever_the_culture_of_the_server()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-CO");

            Assert.Contains("1234.5", CsvText([Row("a", income: 1234.5m)]));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_value_that_looks_like_a_formula_is_written_as_published()
    {
        var text = CsvText([Row("=1+1"), Row("@SUM(A1)"), Row("-5+3"), Row("+cmd")]);

        Assert.Contains("\r\n=1+1,", text);
        Assert.Contains("\r\n@SUM(A1),", text);
    }

    [Fact]
    public void An_empty_text_is_an_empty_field()
    {
        Assert.Contains("\r\n,31,", CsvText([Row("", 31)]));
    }

    [Fact]
    public void The_outcome_counts_the_data_rows_and_no_cut_cells()
    {
        using var stream = new MemoryStream();

        var outcome = CsvExport.Write(stream, Columns, [Row("a"), Row("b"), Row("c")]);

        Assert.Equal(new ExportOutcome(3, 0), outcome);
    }

    [Fact]
    public void A_csv_that_outgrows_the_limit_stops_the_writer()
    {
        using var file = new MemoryStream();
        using var limited = new LimitedStream(file, 1000);
        var rows = Enumerable.Range(0, 100_000).Select(n => Row("row number " + n));
        var produced = 0;

        Assert.Throws<ExportTooLargeException>(() => CsvExport.Write(limited, Columns, rows.Select(r => { produced++; return r; })));

        Assert.True(produced < 10_000, $"{produced} of 100000 rows were produced before the limit stopped the writer");
        Assert.True(file.Length <= 1000 + 64 * 1024); // at most one buffer past the limit reached the file
    }

    [Fact]
    public void The_limit_exception_names_the_limit()
    {
        var ex = new ExportTooLargeException(5000);

        Assert.Equal(5000, ex.Limit);
        Assert.Contains("5000", ex.Message);
    }

    // XLSX

    private static byte[] Xlsx(IEnumerable<object?[]> rows, IReadOnlyList<TableColumn>? columns = null, string sheet = "persons")
    {
        using var stream = new MemoryStream();
        XlsxExport.Write(stream, sheet, columns ?? Columns, rows);
        return stream.ToArray();
    }

    private static string Entry(byte[] xlsx, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx));
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static List<IDictionary<string, object?>> Read(byte[] xlsx) =>
        new MemoryStream(xlsx).Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();

    [Fact]
    public void A_workbook_has_one_sheet_named_after_the_table_a_header_and_typed_cells()
    {
        var xlsx = Xlsx([Row("Ana", 31, 1234.5m, true, "2026-09-01", "2026-09-01T21:30:00-05:00")]);

        var workbook = Entry(xlsx, "xl/workbook.xml");
        var rows = Read(xlsx);

        Assert.Contains("name=\"persons\"", workbook);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(workbook, @"<(?:\w+:)?sheet\s"));
        var row = Assert.Single(rows);
        Assert.Equal(["name", "age", "income", "consent", "born", "at"], row.Keys);
        Assert.Equal("Ana", row["name"]);
        Assert.Equal(31, Convert.ToInt64(row["age"]));
        Assert.Equal(1234.5, Convert.ToDouble(row["income"]));
        Assert.Equal(true, row["consent"]);
        Assert.Equal(new DateTime(2026, 9, 1), Assert.IsType<DateTime>(row["born"]).Date);
        Assert.Equal(new DateTime(2026, 9, 1, 21, 30, 0), Assert.IsType<DateTime>(row["at"]));
    }

    [Fact]
    public void Text_that_looks_like_a_formula_is_text_in_the_file_not_a_formula()
    {
        var xlsx = Xlsx([Row("=1+1"), Row("@SUM(A1)"), Row("+cmd|' /C calc'!A0"), Row("-2+3")]);

        var sheet = Entry(xlsx, "xl/worksheets/sheet1.xml");
        var strings = Entry(xlsx, "xl/sharedStrings.xml") is var shared && shared.Length > 0 ? shared : "";

        Assert.DoesNotContain("<f>", sheet);
        Assert.DoesNotContain("<f ", sheet);
        Assert.Contains("=1+1", sheet + strings);
        Assert.Equal(["=1+1", "@SUM(A1)", "+cmd|' /C calc'!A0", "-2+3"], Read(xlsx).Select(r => (string)r["name"]!));
    }

    [Fact]
    public void A_cell_over_32767_characters_is_cut_and_counted()
    {
        using var stream = new MemoryStream();
        var outcome = XlsxExport.Write(stream, "persons", Columns, [Row(new string('x', 40_000)), Row("short"), Row(new string('y', 32_767))]);

        Assert.Equal(new ExportOutcome(3, 1), outcome);
        var names = Read(stream.ToArray()).Select(r => (string)r["name"]!).ToArray();
        Assert.Equal(32_767, names[0].Length);
        Assert.Equal("short", names[1]);
        Assert.Equal(32_767, names[2].Length);
    }

    [Fact]
    public void A_cut_does_not_end_on_half_of_a_surrogate_pair()
    {
        var text = new string('a', 32_766) + "😀😀";

        var cell = XlsxExport.Cell(DataColumnType.Text, text, out var cut);

        Assert.True(cut);
        Assert.Equal(32_766, ((string)cell!).Length);
    }

    [Fact]
    public void A_workbook_without_rows_still_has_the_header()
    {
        var xlsx = Xlsx([]);

        var sheet = Entry(xlsx, "xl/worksheets/sheet1.xml");

        foreach (var name in new[] { "name", "age", "income", "consent", "born", "at" })
        {
            Assert.Contains(name, sheet + Entry(xlsx, "xl/sharedStrings.xml"));
        }
    }

    [Fact]
    public void Empty_values_are_empty_cells()
    {
        var rows = Read(Xlsx([Row("Ana", 31), Row(null)]));

        Assert.Null(rows[1]["name"]);
        Assert.Null(rows[1]["age"]);
    }

    [Fact]
    public void Numbers_and_dates_that_cannot_be_typed_stay_as_text()
    {
        Assert.Equal("NaN", XlsxExport.Cell(DataColumnType.Decimal, "NaN", out _));
        Assert.Equal("infinity", XlsxExport.Cell(DataColumnType.Timestamp, "infinity", out _));
        Assert.Equal("0044-03-15", XlsxExport.Cell(DataColumnType.Date, "0044-03-15", out _) is DateTime d ? "0044-03-15" : "?");
    }

    [Theory]
    [InlineData("persons", "persons")]
    [InlineData("a/b\\c:d*e?f[g]h", "a_b_c_d_e_f_g_h")]
    [InlineData("", "Sheet1")]
    [InlineData("a_table_with_a_very_long_name_over_the_limit", "a_table_with_a_very_long_name_o")]
    public void Sheet_names_follow_excels_rules(string table, string expected) =>
        Assert.Equal(expected, XlsxExport.SheetName(table));
}
