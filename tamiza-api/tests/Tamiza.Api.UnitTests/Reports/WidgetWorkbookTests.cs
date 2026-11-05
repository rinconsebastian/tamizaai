using System.IO.Compression;
using System.Xml.Linq;
using MiniExcelLibs;
using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;
using static Tamiza.Api.UnitTests.Reports.ReportSamples;

namespace Tamiza.Api.UnitTests.Reports;

/// <summary>A widget's own workbook read back with MiniExcel, and the cells' formats and formulas read from the package.</summary>
public sealed class WidgetWorkbookTests : IDisposable
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly DateTimeOffset Exported = new(2026, 11, 2, 15, 30, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"widget-{Guid.NewGuid():N}.xlsx");

    public void Dispose() => File.Delete(_path);

    private static WidgetWorkbookModel Model(
        ReportWidget widget, string locale = "en-US", IReadOnlyList<ReportFilterLine>? filters = null, IReadOnlyList<string>? notNarrowed = null,
        string? version = null, string? source = null)
    {
        var text = ReportText.For(locale);
        return new WidgetWorkbookModel(
            "Census 2026", "Housing", version, widget, "Households visited", "households_visited",
            filters ?? [new ReportFilterLine("Municipality", "Cali"), new ReportFilterLine("Round", text["allValues"])], notNarrowed ?? [],
            Generated.AddHours(-7), Exported, new ReportFormat(text.Culture, ReportFormat.ZoneOf("America/Bogota")), text, source);
    }

    private List<string> Write(WidgetWorkbookModel model)
    {
        WidgetWorkbook.Write(model, _path);
        return MiniExcel.GetSheetNames(_path);
    }

    private List<IDictionary<string, object?>> Rows(string sheet) =>
        MiniExcel.Query(_path, useHeaderRow: true, sheetName: sheet).Cast<IDictionary<string, object?>>().ToList();

    private static string[] Pair(IDictionary<string, object?> row) => [.. row.Values.Select(v => v?.ToString() ?? "")];

    private XDocument Part(string entry)
    {
        using var zip = ZipFile.OpenRead(_path);
        using var stream = zip.GetEntry(entry)!.Open();
        return XDocument.Load(stream);
    }

    /// <summary>The Excel number format of a cell of the first sheet, such as <c>0.00%</c>; null for the general format.</summary>
    private string? CellFormat(string cell)
    {
        var sheet = Part("xl/worksheets/sheet1.xml");
        var style = (int?)sheet.Descendants(Main + "c").Single(c => (string?)c.Attribute("r") == cell).Attribute("s") ?? 0;
        var styles = Part("xl/styles.xml");
        var numFmtId = (int)styles.Descendants(Main + "cellXfs").Single().Elements(Main + "xf").ElementAt(style).Attribute("numFmtId")!;
        return styles.Descendants(Main + "numFmt").SingleOrDefault(f => (int)f.Attribute("numFmtId")! == numFmtId)?.Attribute("formatCode")?.Value;
    }

    [Fact]
    public void The_data_sheet_comes_first_and_the_summary_second()
    {
        var names = Write(Model(Bars(1, "Coverage by municipality", 3)));

        Assert.Equal(["Coverage by municipality", "Summary"], names);
        Assert.Equal(3, Rows("Coverage by municipality").Count);
    }

    [Fact]
    public void A_widget_titled_summary_does_not_take_the_summary_sheets_name()
    {
        var names = Write(Model(Bars(1, "Summary", 2)));

        Assert.Equal(["Summary (2)", "Summary"], names);
    }

    [Fact]
    public void A_long_title_is_shortened_to_the_sheet_name_rules()
    {
        var names = Write(Model(Bars(1, "Coverage of households by municipality / round 2026 and 2027", 1)));

        Assert.True(names[0].Length <= 31);
        Assert.DoesNotContain('/', names[0]);
    }

    [Fact]
    public void Plotted_values_are_numbers_in_the_indicators_format()
    {
        Write(Model(Bars(1, "Coverage", 2, format: "count")));
        Assert.Equal("#,##0", CellFormat("B2"));
        Assert.Equal(1d, Convert.ToDouble(Rows("Coverage")[0].Values.Last()));

        Write(Model(Bars(1, "Coverage", 2, format: "percent")));
        Assert.Equal("0.00%", CellFormat("B2"));

        Write(Model(Bars(1, "Coverage", 2, format: "decimal")));
        Assert.Equal("#,##0.00", CellFormat("B2"));
    }

    [Fact]
    public void A_cross_table_keeps_its_total_column_and_row()
    {
        Write(Model(CrossTable(1, "Water by municipality", series: 2, groups: 3)));

        var rows = Rows("Water by municipality");
        Assert.Equal(4, rows.Count);
        Assert.Equal(["Total", "1", "2", "2"], Pair(rows[^1]));
        Assert.Equal("#,##0", CellFormat("C2"));
    }

    [Fact]
    public void A_box_plots_figures_have_two_decimals_and_its_counts_are_whole_numbers()
    {
        Write(Model(BoxTable(1, "Household size", 2)));

        Assert.Equal("#,##0", CellFormat("B2"));
        Assert.Equal("#,##0.00", CellFormat("E2"));
        Assert.Equal("#,##0", CellFormat("I2"));
    }

    [Fact]
    public void A_stat_tile_is_one_column_and_one_numeric_cell_in_the_indicators_format()
    {
        var stat = Stat(1, "Households visited", "1,234 households") with
        {
            StatColumn = new ReportColumn("Households visited: value", "#,##0", "households"),
            StatCell = new ReportCell(1234d, "1,234 households"),
        };

        var names = Write(Model(stat));

        Assert.Equal(["Households visited", "Summary"], names);
        var rows = Rows("Households visited");
        Assert.Single(rows);
        Assert.Equal(["Households visited: value (households)"], rows[0].Keys);
        Assert.Equal(1234d, Convert.ToDouble(rows[0].Values.Single()));
        Assert.Equal("#,##0", CellFormat("A2"));
    }

    [Fact]
    public void The_summary_says_what_the_table_is()
    {
        Write(Model(Bars(1, "Coverage", 4), notNarrowed: ["Round"]));

        var rows = Rows("Summary").Select(Pair).ToList();
        Assert.Equal(["Item", "Value"], Rows("Summary")[0].Keys);
        Assert.Equal(["Project", "Census 2026"], rows[0]);
        Assert.Equal(["Dashboard", "Housing"], rows[1]);
        Assert.Equal(["Widget", "Coverage"], rows[2]);
        Assert.Equal(["Indicator", "Households visited (households_visited)"], rows[3]);
        Assert.Equal(["Plotted", "Households visited: value"], rows[4]);
        Assert.Contains(rows, r => r.SequenceEqual(["Filters: Municipality", "Cali"]));
        Assert.Contains(rows, r => r.SequenceEqual(["Filters: Round", "All"]));
        Assert.Contains(rows, r => r.SequenceEqual(["Not filtered by", "Round"]));
        Assert.Contains(rows, r => r[0] == "Data published" && r[1].Contains("America/Bogota"));
        Assert.Contains(rows, r => r[0] == "Exported" && r[1].Contains("America/Bogota"));
        Assert.Contains(rows, r => r.SequenceEqual(["Rows", "4"]));
        Assert.DoesNotContain(rows, r => r[0] == "Version");
    }

    [Fact]
    public void Widget_texts_in_the_summary()
    {
        var widget = Bars(1, "2.4. En qué lugares", 4) with
        {
            Caption = "% de quienes asistieron", Subtitle = "¿En qué lugares ha visto teatro?", Footnote = "No suman 100 %: respuesta múltiple.",
            Notes = ["Only the first 5000 rows are shown: the query matches more."],
        };

        Write(Model(widget, version: "Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM", source: "Encuesta de mercado, 478 encuestas"));

        var rows = Rows("Summary").Select(Pair).ToList();
        var names = rows.Select(r => r[0]).ToList();
        Assert.Equal(["Project", "Dashboard", "Version", "Source", "Widget", "Subtitle", "Indicator", "Plotted"], names.Take(8));
        Assert.Equal(["Source", "Encuesta de mercado, 478 encuestas"], rows[3]);
        Assert.Equal(["Subtitle", "¿En qué lugares ha visto teatro?"], rows[5]);
        Assert.Equal(["Plotted", "% de quienes asistieron"], rows[7]);
        var rowsLine = names.IndexOf("Rows");
        Assert.Equal(["Footnote", "No suman 100 %: respuesta múltiple."], rows[rowsLine + 1]);
        Assert.Equal("Notes", rows[rowsLine + 2][0]);
    }

    [Fact]
    public void Reference_lines_are_one_item_after_what_is_plotted()
    {
        var widget = Bars(1, "2.1. Asistió, por comuna", 2) with
        {
            Footnote = "Sin ponderar.",
            ReferenceLines = [new ReportReferenceLine("mean", "Mean", 0.3417, "34.17%"), new ReportReferenceLine("value", "Meta", 0.5, "50%")],
        };

        Write(Model(widget));

        var rows = Rows("Summary").Select(Pair).ToList();
        var plotted = rows.FindIndex(r => r[0] == "Plotted");
        Assert.Equal(["Reference lines", "Mean: 34.17% · Meta: 50%"], rows[plotted + 1]);
        Assert.StartsWith("Filters", rows[plotted + 2][0]);
        // The data sheet is the widget's table, with no row or column for the lines.
        Assert.Equal(2, Rows("2.1. Asistió, por comuna").Count);

        Write(Model(widget, locale: "es-CO"));
        Assert.Contains(Rows("Resumen").Select(Pair), r => r.SequenceEqual(["Líneas de referencia", "Mean: 34.17% · Meta: 50%"]));
    }

    [Fact]
    public void No_texts_in_the_summary()
    {
        Write(Model(Bars(1, "Coverage", 2)));

        Assert.All(Rows("Summary").Select(Pair), r => Assert.DoesNotContain(r[0], new[] { "Source", "Subtitle", "Footnote", "Reference lines" }));
        Assert.Equal(["Project", "Dashboard", "Widget", "Indicator", "Plotted"], Rows("Summary").Select(Pair).Take(5).Select(r => r[0]));
    }

    [Fact]
    public void The_texts_of_a_spanish_member_use_the_spanish_words()
    {
        Write(Model(Bars(1, "Cobertura", 1) with { Subtitle = "s", Footnote = "n" }, locale: "es-CO", source: "f"));

        var names = Rows("Resumen").Select(Pair).Select(r => r[0]).ToList();
        Assert.Contains("Fuente", names);
        Assert.Contains("Subtítulo", names);
        Assert.Contains("Nota al pie", names);
    }

    [Fact]
    public void A_dashboard_without_filters_says_no_filter_applies()
    {
        Write(Model(Bars(1, "Coverage", 1), filters: []));

        Assert.Contains(Rows("Summary").Select(Pair), r => r.SequenceEqual(["Filters", "No filter applies"]));
    }

    [Fact]
    public void A_version_is_named_in_the_summary()
    {
        Write(Model(Bars(1, "Coverage", 1), version: "Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM"));

        Assert.Contains(Rows("Summary").Select(Pair), r => r[0] == "Version" && r[1].StartsWith("Version 9, saved by Ana Pérez"));
    }

    [Fact]
    public void Notes_are_listed_once_and_the_not_filtered_sentence_is_not_repeated()
    {
        var text = ReportText.For("en-US");
        var widget = Bars(1, "Coverage", 2) with
        {
            Notes = [text.Format("notFiltered", "Round"), text.Format("rowLimit", 5000)],
        };

        Write(Model(widget, notNarrowed: ["Round"]));

        var notes = Rows("Summary").Select(Pair).Single(r => r[0] == "Notes");
        Assert.Equal("Only the first 5000 rows are shown: the query matches more.", notes[1]);
    }

    [Fact]
    public void A_table_with_nothing_to_list_gives_its_reason()
    {
        var text = ReportText.For("en-US");
        var widget = new ReportWidget(1, "Coverage", ChartTypes.Bar, "Visits: value", WidgetOutcome.NoRows, [], [], null, text["empty"], []);

        var names = Write(Model(widget));

        Assert.Equal(["Coverage", "Summary"], names);
        Assert.Equal(["Notes"], Rows("Coverage")[0].Keys);
        Assert.Equal("No rows match this widget’s query.", Rows("Coverage")[0].Values.Single());
        Assert.Contains(Rows("Summary").Select(Pair), r => r.SequenceEqual(["Rows", "0"]));
    }

    [Fact]
    public void A_spanish_member_gets_the_words_of_the_spanish_workbook()
    {
        var names = Write(Model(CrossTable(1, "Cobertura", series: 1), locale: "es-CO", filters: [new ReportFilterLine("Ronda", "Todos")]));

        Assert.Equal(["Cobertura", "Resumen"], names);
        Assert.Equal(["Elemento", "Valor"], Rows("Resumen")[0].Keys);
        Assert.Contains(Rows("Resumen").Select(Pair), r => r.SequenceEqual(["Proyecto", "Census 2026"]));
        Assert.Contains(Rows("Resumen").Select(Pair), r => r.SequenceEqual(["Filtros: Ronda", "Todos"]));
        Assert.Contains(Rows("Cobertura")[^1].Keys, k => k == "Total");
    }

    [Fact]
    public void A_cell_of_forty_thousand_characters_is_cut_to_what_a_cell_holds()
    {
        var widget = Bars(1, "Coverage", 1) with
        {
            Rows = [[new ReportCell(new string('x', 40_000), "x"), new ReportCell(1d, "1")]],
        };

        Write(Model(widget));

        Assert.Equal(32_767, ((string)Rows("Coverage")[0].Values.First()!).Length);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("=HYPERLINK(\"https://example.test\",\"x\")")]
    [InlineData("https://example.test/path")]
    public void Text_that_looks_like_a_formula_or_a_link_is_written_as_text(string hostile)
    {
        var widget = new ReportWidget(
            1, hostile, ChartTypes.Column, hostile, WidgetOutcome.Shown,
            [new ReportColumn(hostile), new ReportColumn($"{hostile} series", "#,##0"), new ReportColumn("Tooltip")],
            [[new ReportCell(hostile, hostile), new ReportCell(1d, "1"), new ReportCell(hostile, hostile)]],
            null, null, []);

        Write(Model(widget, filters: [new ReportFilterLine(hostile, hostile)]));

        using var zip = ZipFile.OpenRead(_path);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet")))
        {
            using var stream = entry.Open();
            var sheet = XDocument.Load(stream);
            Assert.Empty(sheet.Descendants(Main + "f"));
            Assert.Empty(sheet.Descendants(Main + "hyperlink"));
        }

        Assert.Equal(hostile, Rows(MiniExcel.GetSheetNames(_path)[0])[0].Values.First());
    }
}
