using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MiniExcelLibs;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class SamplingFrameImportTests
{
    private static ImportPreview Csv(string text)
    {
        var (preview, error) = SamplingFrameImport.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), ExcelType.CSV);
        Assert.Null(error);
        return preview!;
    }

    private static string? CsvError(string text) =>
        SamplingFrameImport.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), ExcelType.CSV).FileError;

    private static ImportPreview Xlsx(byte[] bytes)
    {
        var (preview, error) = SamplingFrameImport.Read(new MemoryStream(bytes), ExcelType.XLSX);
        Assert.Null(error);
        return preview!;
    }

    private static byte[] XlsxBytes(IEnumerable<Dictionary<string, object?>> rows)
    {
        using var stream = new MemoryStream();
        stream.SaveAs(rows, excelType: ExcelType.XLSX);
        return stream.ToArray();
    }

    /// <summary>Removes the given rows from the first sheet, leaving gaps in the row numbers as spreadsheet programs do.</summary>
    private static byte[] WithoutSheetRows(byte[] xlsx, params int[] rows)
    {
        using var stream = new MemoryStream();
        stream.Write(xlsx);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
            {
                xml = reader.ReadToEnd();
            }

            foreach (var row in rows)
            {
                xml = Regex.Replace(xml, $"<x:row r=\"{row}\">.*?</x:row>", "");
            }

            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write(xml);
        }

        return stream.ToArray();
    }

    [Fact]
    public void Note_column()
    {
        var preview = Csv("municipio,# municipio label,sexo,target\n05001,Medellín,f,10\n");

        Assert.Equal(["municipio", "sexo"], preview.Dimensions);
        Assert.Equal(["05001", "f"], preview.Rows[0].Values);
        var note = Assert.Single(preview.IgnoredColumns);
        Assert.Equal(new IgnoredColumn("B", "# municipio label", "note"), note);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void A_note_name_is_read_after_trimming()
    {
        var preview = Csv("municipio,  #mine,target\n05001,x,10\n");

        Assert.Equal(["municipio"], preview.Dimensions);
        Assert.Equal("#mine", Assert.Single(preview.IgnoredColumns).Name);
    }

    [Fact]
    public void Values_under_a_column_without_a_name()
    {
        var preview = Csv("municipio,sexo,target,,\n05001,f,10,,stray\n05002,m,12,,other\n05003,f,1,,\n");

        Assert.Equal(["municipio", "sexo"], preview.Dimensions);
        Assert.Equal(new IgnoredColumn("E", null, "unnamed"), Assert.Single(preview.IgnoredColumns));
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Values_beyond_the_header_width_are_listed_once_per_column_in_column_order()
    {
        var preview = Csv("# note,municipio,target\nx,05001,10,extra\ny,05002,12,more,again\n");

        Assert.Equal(
            [new IgnoredColumn("A", "# note", "note"), new IgnoredColumn("D", null, "unnamed"), new IgnoredColumn("E", null, "unnamed")],
            preview.IgnoredColumns);
    }

    [Fact]
    public void An_empty_column_without_a_name_is_not_listed()
    {
        var preview = Csv("municipio,,target\n05001,,10\n");

        Assert.Empty(preview.IgnoredColumns);
    }

    [Fact]
    public void Rows_without_a_target()
    {
        var preview = Csv("municipio,target\nA,1\nB,2\nC,\nD,4\nE,5\nF,6\nG,7\nH,  \nI,9\n");

        Assert.Equal([4, 9], preview.RowsWithoutTarget);
        Assert.DoesNotContain(preview.Rows, r => r.Row is 4 or 9);
        Assert.Equal(7, preview.Rows.Count);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Target_of_zero()
    {
        var preview = Csv("municipio,target\nA,1\nB,2\nC,3\nD,0\n");

        var row = Assert.Single(preview.Rows, r => r.Row == 5);
        Assert.Equal(0, row.Target);
        Assert.Empty(preview.RowsWithoutTarget);
    }

    [Fact]
    public void Left_out_row_repeats_a_combination()
    {
        var csv = new StringBuilder("municipio,sexo,target\n");
        for (var row = 2; row <= 12; row++)
        {
            csv.Append(row switch { 4 => "A,f,\n", 12 => "A,f,30\n", _ => $"Town {row},f,1\n" });
        }

        var preview = Csv(csv.ToString());

        Assert.Contains(preview.Rows, r => r.Row == 12 && r.Target == 30);
        Assert.Equal([4], preview.RowsWithoutTarget);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Every_row_without_a_target()
    {
        var preview = Csv("municipio,sexo,target\nA,f,\nA,m,\nB,f,\n");

        Assert.Equal(["municipio", "sexo"], preview.Dimensions);
        Assert.Empty(preview.Rows);
        Assert.Equal([2, 3, 4], preview.RowsWithoutTarget);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Without_a_target_column_no_row_is_left_out_and_the_header_error_stands()
    {
        var preview = Csv("municipio,sexo\nA,\nB,f\n");

        Assert.Empty(preview.RowsWithoutTarget);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Contains(preview.Errors, e => e.Row == 1 && e.Message.Contains("'target'"));
        Assert.Contains(preview.Errors, e => e.Row == 2 && e.Message == "Enter a value for every dimension.");
    }

    [Fact]
    public void A_header_only_file_gives_the_dimensions_and_no_rows()
    {
        var preview = Csv("barrio,target\n");

        Assert.Equal(["barrio"], preview.Dimensions);
        Assert.Empty(preview.Rows);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void A_non_empty_invalid_target_is_still_an_error()
    {
        var preview = Csv("municipio,target\nA,1.5\nB,-3\nC,x\n");

        Assert.Equal([2, 3, 4], preview.Errors.Select(e => e.Row));
        Assert.Empty(preview.RowsWithoutTarget);
    }

    [Fact]
    public void Blank_lines_between_rows_in_csv_do_not_drop_rows()
    {
        var csv = new StringBuilder("municipio;target\n");
        for (var i = 1; i <= SamplingFrameValidator.MaxRows; i++)
        {
            csv.Append($"Town {i};1\n");
            if (i % 2_000 == 0)
            {
                csv.Append(";;;\n");
            }
        }

        var preview = Csv(csv.ToString());

        Assert.Equal(SamplingFrameValidator.MaxRows, preview.Rows.Count);
        Assert.Equal("Town 10000", preview.Rows[^1].Values[0]);
    }

    [Fact]
    public void Blank_rows_between_rows_in_xlsx_do_not_drop_rows()
    {
        var rows = new List<Dictionary<string, object?>>();
        for (var i = 1; i <= SamplingFrameValidator.MaxRows; i++)
        {
            rows.Add(new() { ["municipio"] = $"Town {i}", ["target"] = 1 });
            if (i % 2_000 == 0)
            {
                rows.Add(new() { ["municipio"] = null, ["target"] = null });
            }
        }

        var preview = Xlsx(XlsxBytes(rows));

        Assert.Equal(SamplingFrameValidator.MaxRows, preview.Rows.Count);
        Assert.Equal("Town 10000", preview.Rows[^1].Values[0]);
    }

    [Fact]
    public void More_than_ten_thousand_non_blank_rows_are_refused()
    {
        var csv = new StringBuilder("municipio,target\n");
        for (var i = 0; i <= SamplingFrameValidator.MaxRows; i++)
        {
            csv.Append(i % 2 == 0 ? $"Town {i},1\n" : $"Town {i},\n");
        }

        Assert.Contains("more than 10,000 rows", CsvError(csv.ToString()));
    }

    [Fact]
    public void Row_numbers_after_an_empty_csv_line()
    {
        // Line 3 is empty, line 7 has an invalid target and line 8 has none.
        var preview = Csv("municipio,target\nA,1\n\nB,2\nC,3\nD,4\nE,1.5\nF,\nG,5\n");

        Assert.Equal(7, Assert.Single(preview.Errors).Row);
        Assert.Equal([8], preview.RowsWithoutTarget);
        Assert.Equal([2, 4, 5, 6, 7, 9], preview.Rows.Select(r => r.Row));
    }

    [Fact]
    public void Row_numbers_after_a_white_space_only_csv_line()
    {
        var preview = Csv("municipio;target\r\nA;1\r\n   \r\nB;1.5\r\nC;\r\n");

        Assert.Equal(4, Assert.Single(preview.Errors).Row);
        Assert.Equal([5], preview.RowsWithoutTarget);
    }

    [Fact]
    public void A_quoted_value_that_spans_lines_counts_as_one_row()
    {
        // Row 2's value spans three lines, one of them empty; it is one row, as a spreadsheet program shows it.
        var preview = Csv("municipio,target\n\"first\n\nsecond\",1\n\nB,1.5\nC,\n");

        Assert.Matches("^first\r?\n\r?\nsecond$", preview.Rows[0].Values[0]);
        Assert.Equal(4, Assert.Single(preview.Errors).Row);
        Assert.Equal([5], preview.RowsWithoutTarget);
    }

    [Fact]
    public void Leading_blank_lines_count_toward_row_numbers()
    {
        var preview = Csv("\n\nmunicipio,target\nA,x\n");

        Assert.Equal(4, Assert.Single(preview.Errors).Row);
    }

    [Fact]
    public void Row_numbers_after_an_xlsx_sheet_gap()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["municipio"] = "A", ["target"] = 1 },
            new() { ["municipio"] = null, ["target"] = null },
            new() { ["municipio"] = "B", ["target"] = 1.5 },
            new() { ["municipio"] = "C", ["target"] = null },
        };

        // Row 3 is missing from the sheet altogether.
        var preview = Xlsx(WithoutSheetRows(XlsxBytes(rows), 3));

        Assert.Equal(4, Assert.Single(preview.Errors).Row);
        Assert.Equal([5], preview.RowsWithoutTarget);
    }

    [Fact]
    public void Codes_keep_leading_zeros_when_stored_as_text()
    {
        var preview = Xlsx(XlsxBytes([new() { ["municipio"] = "05001", ["target"] = 3 }]));

        Assert.Equal("05001", preview.Rows[0].Values[0]);
        Assert.Equal(3, preview.Rows[0].Target);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_letters(int index, string letter) => Assert.Equal(letter, SamplingFrameImport.ColumnLetter(index));
}
