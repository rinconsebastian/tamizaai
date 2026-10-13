using System.Text;
using System.Text.Json;
using MiniExcelLibs;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class ReferenceTableImportTests
{
    private static ReferenceImport Csv(string text, bool bom = false)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bom)
        {
            bytes = [.. Encoding.UTF8.GetPreamble(), .. bytes];
        }

        var (import, error) = ReferenceTableImport.Read(new MemoryStream(bytes), ExcelType.CSV);
        Assert.Null(error);
        return import!;
    }

    private static string? CsvError(string text) => ReferenceTableImport.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), ExcelType.CSV).Error;

    private static ReferenceImport Xlsx(IEnumerable<Dictionary<string, object?>> rows)
    {
        var stream = new MemoryStream();
        stream.SaveAs(rows, excelType: ExcelType.XLSX);
        stream.Position = 0;
        var (import, error) = ReferenceTableImport.Read(stream, ExcelType.XLSX);
        Assert.Null(error);
        return import!;
    }

    private static JsonElement Rows(ReferenceImport import) => JsonDocument.Parse(import.RowsJson).RootElement;

    private static string[] Types(ReferenceImport import) => import.Columns.Select(c => c.Type).ToArray();

    [Fact]
    public void Reduces_headers_detects_types_and_reports_renamed_columns()
    {
        var import = Csv("Municipality code;Year;Population\n05001;2026;2612958\n11001;2026;7968095\n");

        Assert.Equal(["municipality_code", "year", "population"], import.Columns.Select(c => c.Name));
        Assert.Equal(["text", "integer", "integer"], Types(import));
        Assert.Equal(["Municipality code", "Year", "Population"], import.Renamed.Select(r => r.Header));
        Assert.Equal(2, import.RowCount);
        Assert.Equal("""[["05001",2026,2612958],["11001",2026,7968095]]""", import.RowsJson);
    }

    [Fact]
    public void Unchanged_names_are_not_reported()
    {
        var import = Csv("code,value\na,1\n");

        Assert.Empty(import.Renamed);
    }

    [Fact]
    public void Accepts_a_decimal_comma_in_semicolon_files()
    {
        var import = Csv("name;rate\na;12,5\nb;3\n");

        Assert.Equal(["text", "decimal"], Types(import));
        Assert.Equal(12.5, Rows(import)[0][1].GetDouble());
        Assert.Equal(3, Rows(import)[1][1].GetDouble());
    }

    [Fact]
    public void A_comma_in_a_comma_separated_file_is_a_separator_not_a_decimal()
    {
        var import = Csv("name,rate\na,\"12,5\"\n");

        Assert.Equal(["text", "text"], Types(import));
    }

    [Fact]
    public void Codes_with_leading_zeros_stay_text()
    {
        var import = Csv("code\n05001\n11001\n");

        Assert.Equal(["text"], Types(import));
        Assert.Equal("05001", Rows(import)[0][0].GetString());
    }

    [Fact]
    public void Zero_and_decimals_below_one_are_numbers()
    {
        var import = Csv("a,b\n0,0.5\n-3,-0.25\n");

        Assert.Equal(["integer", "decimal"], Types(import));
    }

    [Fact]
    public void Iso_dates_are_dates_and_one_stray_value_makes_text()
    {
        var import = Csv("day,mixed\n2026-01-31,1\n2026-02-01,n/a\n");

        Assert.Equal(["date", "text"], Types(import));
        Assert.Equal("2026-01-31", Rows(import)[0][0].GetString());
        Assert.Equal("1", Rows(import)[0][1].GetString());
    }

    [Fact]
    public void Empty_cells_are_null_and_an_empty_column_is_text()
    {
        var import = Csv("a,b,c\n1,,x\n,,y\n");

        Assert.Equal(["integer", "text", "text"], Types(import));
        Assert.Equal("""[[1,null,"x"],[null,null,"y"]]""", import.RowsJson);
    }

    [Fact]
    public void An_empty_header_cell_becomes_column_and_names_are_deduplicated()
    {
        var import = Csv("Age,,age,\n1,2,3,4\n");

        Assert.Equal(["age", "column", "age_2", "column_2"], import.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Data_beyond_the_header_gets_its_own_column()
    {
        var import = Csv("a\n1,extra\n");

        Assert.Equal(["a", "column"], import.Columns.Select(c => c.Name));
        Assert.Equal("""[[1,"extra"]]""", import.RowsJson);
    }

    [Fact]
    public void Leading_and_blank_rows_are_skipped_and_a_bom_is_ignored()
    {
        var import = Csv("\nname,value\n\na,1\n", bom: true);

        Assert.Equal(["name", "value"], import.Columns.Select(c => c.Name));
        Assert.Equal(1, import.RowCount);
    }

    [Fact]
    public void Reads_xlsx_numbers_dates_and_date_times()
    {
        var import = Xlsx(
        [
            new() { ["Year"] = 2026, ["Rate"] = 0.5, ["Day"] = new DateTime(2026, 1, 31), ["Stamp"] = new DateTime(2026, 1, 31, 8, 30, 0), ["Code"] = "05001" },
            new() { ["Year"] = 2027, ["Rate"] = 1, ["Day"] = new DateTime(2026, 2, 1), ["Stamp"] = new DateTime(2026, 2, 1), ["Code"] = "11001" },
        ]);

        Assert.Equal(["year", "rate", "day", "stamp", "code"], import.Columns.Select(c => c.Name));
        Assert.Equal(["integer", "decimal", "date", "text", "text"], Types(import));
        Assert.Equal("""[[2026,0.5,"2026-01-31","2026-01-31 08:30:00","05001"],[2027,1,"2026-02-01","2026-02-01","11001"]]""", import.RowsJson);
    }

    [Fact]
    public void A_header_without_rows_is_rejected() => Assert.Contains("no rows", CsvError("a,b\n"));

    [Fact]
    public void An_empty_file_is_rejected() => Assert.Contains("empty", CsvError("\n\n"));

    [Fact]
    public void More_than_200_columns_are_rejected() =>
        Assert.Contains("200 columns", CsvError(string.Join(',', Enumerable.Range(1, 201).Select(i => $"c{i}")) + "\n" + string.Join(',', Enumerable.Repeat("1", 201)) + "\n"));

    [Fact]
    public void More_than_five_million_values_are_rejected()
    {
        var text = new StringBuilder(string.Join(',', Enumerable.Range(1, 20).Select(i => $"c{i}")) + "\n");
        var row = string.Join(',', Enumerable.Repeat("1", 20)) + "\n";
        for (var i = 0; i < 250_001; i++)
        {
            text.Append(row);
        }

        Assert.Contains("5,000,000 values", CsvError(text.ToString()));
    }

    [Fact]
    public void An_unreadable_xlsx_is_rejected()
    {
        var (_, error) = ReferenceTableImport.Read(new MemoryStream("not a zip"u8.ToArray()), ExcelType.XLSX);

        Assert.Contains("could not be read", error);
    }
}
