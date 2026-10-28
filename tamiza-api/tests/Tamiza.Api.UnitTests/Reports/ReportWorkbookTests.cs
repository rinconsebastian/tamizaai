using System.IO.Compression;
using System.Xml.Linq;
using MiniExcelLibs;
using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;
using static Tamiza.Api.UnitTests.Reports.ReportSamples;

namespace Tamiza.Api.UnitTests.Reports;

/// <summary>The workbook read back with MiniExcel, and the number format of a cell read from the package's styles.</summary>
public sealed class ReportWorkbookTests : IDisposable
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.xlsx");

    public void Dispose() => File.Delete(_path);

    private List<string> Write(ReportModel model)
    {
        ReportWorkbook.Write(model, _path);
        return MiniExcel.GetSheetNames(_path);
    }

    private List<IDictionary<string, object?>> Rows(string sheet) =>
        MiniExcel.Query(_path, useHeaderRow: true, sheetName: sheet).Cast<IDictionary<string, object?>>().ToList();

    /// <summary>The Excel number format of a cell, such as <c>0.00%</c>; null for the general format.</summary>
    private string? CellFormat(int sheetIndex, string cell)
    {
        using var zip = ZipFile.OpenRead(_path);
        XDocument Read(string entry)
        {
            using var stream = zip.GetEntry(entry)!.Open();
            return XDocument.Load(stream);
        }

        var sheet = Read($"xl/worksheets/sheet{sheetIndex + 1}.xml");
        var style = (int?)sheet.Descendants(Main + "c").Single(c => (string?)c.Attribute("r") == cell).Attribute("s") ?? 0;
        var styles = Read("xl/styles.xml");
        var numFmtId = (int)styles.Descendants(Main + "cellXfs").Single().Elements(Main + "xf").ElementAt(style).Attribute("numFmtId")!;
        return styles.Descendants(Main + "numFmt").SingleOrDefault(f => (int)f.Attribute("numFmtId")! == numFmtId)?.Attribute("formatCode")?.Value;
    }

    private static ReportWidget Table(int position, string title, ReportColumn[] columns, params object?[][] rows) =>
        new(position, title, ChartTypes.Bar, "Caption", WidgetOutcome.Shown, columns,
            rows.Select(r => (IReadOnlyList<ReportCell>)r.Select(v => new ReportCell(v, v?.ToString() ?? "—")).ToList()).ToList(), null, null, []);

    [Fact]
    public void Summary_sheet()
    {
        var names = Write(Model([Stat(1, "Total", "250 households"), Stat(2, "Coverage", "92%")]));

        Assert.Equal(["Summary"], names);
        var rows = Rows("Summary");
        Assert.Equal(["Project", "Census 2026"], rows[0].Values.Select(v => v?.ToString()));
        Assert.Equal(["Dashboard", "Housing"], rows[1].Values.Select(v => v?.ToString()));
        Assert.Contains(rows, r => r.Values.Select(v => v?.ToString()).SequenceEqual(["Filters: Round", "3"]));
        Assert.Contains(rows, r => r.Values.Select(v => v?.ToString()).SequenceEqual(["Total", "250 households"]));
        Assert.Contains(rows, r => r.Values.Select(v => v?.ToString()).SequenceEqual(["Coverage", "92%"]));
    }

    [Fact]
    public void Sheets()
    {
        var names = Write(Model([Stat(1, "Total", "250"), Bars(2, "Coverage by municipality", 3), Bars(3, "Type of dwelling", 2)]));

        Assert.Equal(["Summary", "2. Coverage by municipality", "3. Type of dwelling"], names);
    }

    [Fact]
    public void Whole_table()
    {
        Write(Model([Bars(1, "Bars", 32)]));

        var rows = Rows("1. Bars");
        Assert.Equal(32, rows.Count);
        Assert.Equal(["municipality", "Households visited: value"], rows[0].Keys);
        Assert.Equal(32d, Convert.ToDouble(rows[31]["Households visited: value"]));
    }

    [Fact]
    public void Long_title()
    {
        var title = "Households visited by municipality / round and zone of 2026";

        var names = Write(Model([Bars(1, title, 1), Bars(2, title, 1)]));

        Assert.All(names, name => Assert.True(name.Length <= 31));
        Assert.All(names, name => Assert.DoesNotContain("/", name));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("History")]
    [InlineData("'quoted'")]
    public void Sheet_names_that_excel_refuses_are_changed(string title)
    {
        var name = ReportWorkbook.SheetName(title, new Dictionary<string, object>());

        Assert.NotEqual("History", name, StringComparer.OrdinalIgnoreCase);
        Assert.False(name.StartsWith('\'') || name.EndsWith('\''));
    }

    [Fact]
    public void Percentage_cell()
    {
        Write(Model([Table(1, "Coverage", [new ReportColumn("municipality"), new ReportColumn("Coverage: value", "0.00%")], ["Cali", 0.42])]));

        Assert.Equal(0.42, Convert.ToDouble(Rows("1. Coverage")[0]["Coverage: value"]));
        Assert.Equal("0.00%", CellFormat(1, "B2"));
    }

    [Fact]
    public void Tooltip_column_keeps_its_own_format()
    {
        Write(Model([Table(1, "Blocks",
            [new ReportColumn("block"), new ReportColumn("Progress: value", "0.00%"), new ReportColumn("sample_target", "#,##0")],
            ["0042", 0.5, 20d])]));

        Assert.Equal(20d, Convert.ToDouble(Rows("1. Blocks")[0]["sample_target"]));
        Assert.Equal("#,##0", CellFormat(1, "C2"));
        Assert.Equal("0.00%", CellFormat(1, "B2"));
    }

    [Fact]
    public void Histogram_counts_and_pie_shares()
    {
        Write(Model([
            Table(1, "Distribution", [new ReportColumn("Range"), new ReportColumn("Rows of Coverage", "#,##0")], ["10%–20%", 35d]),
            Table(2, "Share", [new ReportColumn("municipality"), new ReportColumn("Households", "#,##0"), new ReportColumn("Share", "0.0%")], ["Cali", 812d, 0.658]),
        ]));

        Assert.Equal("#,##0", CellFormat(1, "B2"));
        Assert.Equal("0.0%", CellFormat(2, "C2"));
        Assert.Equal(0.658, Convert.ToDouble(Rows("2. Share")[0]["Share"]));
    }

    [Fact]
    public void Unit_in_the_header()
    {
        Write(Model([Bars(1, "Visits", 2, unit: "households")]));

        Assert.Equal(["municipality", "Households visited: value (households)"], Rows("1. Visits")[0].Keys);
        Assert.Equal("#,##0", CellFormat(1, "B2"));
    }

    [Fact]
    public void A_header_needed_with_two_formats_is_renamed_on_the_later_sheet()
    {
        Write(Model([
            Table(1, "Counts", [new ReportColumn("Value", "#,##0")], [5d]),
            Table(2, "Rates", [new ReportColumn("Value", "0.00%")], [0.5]),
        ]));

        Assert.Equal(["Value"], Rows("1. Counts")[0].Keys);
        Assert.Equal(["Value (2)"], Rows("2. Rates")[0].Keys);
        Assert.Equal("#,##0", CellFormat(1, "A2"));
        Assert.Equal("0.00%", CellFormat(2, "A2"));
    }

    [Fact]
    public void A_widget_that_cannot_be_shown_has_its_reason_in_its_sheet()
    {
        Write(Model([Unreadable(1, "Visits", "Visits has not published yet.")]));

        Assert.Equal("Visits has not published yet.", Rows("1. Visits")[0]["Notes"]);
    }

    [Fact]
    public void Scheduled_email_workbook()
    {
        var names = Write(Model([Bars(1, "Vivienda por municipio", 1)], "es-CO"));

        Assert.Equal(["Resumen", "1. Vivienda por municipio"], names);
        Assert.Equal(["Proyecto", "Census 2026"], Rows("Resumen")[0].Values.Select(v => v?.ToString()));
    }

    [Fact]
    public void File_name_names_the_project_the_dashboard_and_the_day()
    {
        Assert.Equal("census-2026_housing_2026-10-28.xlsx", ReportWorkbook.FileName(Model([])));
        Assert.Equal("census-2026_dashboard_2026-10-28.xlsx", ReportWorkbook.FileName(Model([], dashboard: "住房")));
    }
}
