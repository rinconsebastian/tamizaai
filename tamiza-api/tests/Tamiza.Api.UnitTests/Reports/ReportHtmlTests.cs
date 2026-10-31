using System.Text;
using Tamiza.Api.Reports;
using static Tamiza.Api.UnitTests.Reports.ReportSamples;

namespace Tamiza.Api.UnitTests.Reports;

public sealed class ReportHtmlTests
{
    private static readonly ReportOptions Options = new();

    [Fact]
    public void Header()
    {
        var (html, _) = ReportHtml.Write(Model([Stat(1, "Total", "250 households")]), true, Options);

        Assert.Contains("Census 2026", html);
        Assert.Contains(">Housing</h1>", html);
        Assert.Contains("Generated", html);
        Assert.Contains("Oct 28, 2026, 7:00 AM (America/Bogota)", html);
        Assert.Contains("Data published", html);
        Assert.Contains("Oct 28, 2026, 12:00 AM (America/Bogota)", html);
        Assert.Contains("Round: 3", html);
    }

    [Fact]
    public void No_filter_applies()
    {
        var (html, _) = ReportHtml.Write(Model([Stat(1, "Total", "250")], filters: []), true, Options);

        Assert.Contains("No filter applies", html);
    }

    [Fact]
    public void Stat_tiles_come_before_the_tables()
    {
        var (html, _) = ReportHtml.Write(Model([Bars(1, "By municipality", 3), Stat(2, "Total", "250 households")]), true, Options);

        Assert.True(html.IndexOf("250 households", StringComparison.Ordinal) < html.IndexOf("By municipality", StringComparison.Ordinal));
    }

    [Fact]
    public void Long_table()
    {
        var (html, shown) = ReportHtml.Write(Model([Bars(1, "By municipality", 32)]), true, Options);

        Assert.Contains("Municipality 10<", html);
        Assert.DoesNotContain("Municipality 11<", html);
        Assert.Contains("22 more rows", html);
        Assert.Equal([1], shown);
        Assert.Contains("The full tables are in the attached workbook.", html);
    }

    [Fact]
    public void Large_dashboard()
    {
        var widgets = Enumerable.Range(1, 50).Select(i => Bars(i, $"Widget {i}", 10)).ToList();

        var (html, shown) = ReportHtml.Write(Model(widgets), true, Options);

        Assert.True(Encoding.UTF8.GetByteCount(html) <= Options.MaxBodyKb * 1024);
        Assert.InRange(shown.Count, 1, 49);
        Assert.Contains("these widgets are not shown in its body", html);
        Assert.Contains("<li>Widget 50</li>", html);
        // Blocks keep their order: those shown are the first ones.
        Assert.Equal(Enumerable.Range(1, shown.Count), shown.Order());
    }

    [Fact]
    public void Footer()
    {
        var (html, _) = ReportHtml.Write(Model([Stat(1, "Total", "250")]), true, Options);

        Assert.Contains("A member of Census 2026 scheduled this email from Tamiza.", html);
        Assert.Contains("To stop receiving it, reply to this email.", html);
        Assert.Contains("href=\"https://tamiza.test/projects/aa/dashboards/d1\"", html);
    }

    [Fact]
    public void Attachment_too_large()
    {
        var model = Model([Bars(1, "By municipality", 32)]);

        var (html, shown) = ReportHtml.Write(model, false, Options);
        var text = ReportPlainText.Write(model, false, shown, Options);

        foreach (var body in new[] { html, text })
        {
            Assert.Contains("The workbook was too large to attach.", body);
            Assert.DoesNotContain("attached workbook", body);
            Assert.Contains("22 more rows", body);
        }
    }

    [Fact]
    public void Member_written_values_are_encoded()
    {
        var (html, _) = ReportHtml.Write(
            Model([Bars(1, "<script>alert(1)</script>", 1)], project: "A & B", dashboard: "\"Housing\""), true, Options);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("A &amp; B", html);
        Assert.Contains("&quot;Housing&quot;", html);
    }

    [Fact]
    public void Reasons_and_notes_are_shown_in_place_of_the_table()
    {
        var (html, _) = ReportHtml.Write(Model([Unreadable(1, "Visits", "Visits has not published yet.")]), true, Options);

        Assert.Contains("Visits has not published yet.", html);
        Assert.DoesNotContain("<th", html);
    }

    [Fact]
    public void Text_reader()
    {
        var model = Model([Stat(1, "Total", "250 households"), Bars(2, "By municipality", 12), Unreadable(3, "Visits", "Visits has not published yet.")]);
        var (_, shown) = ReportHtml.Write(model, true, Options);

        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.Contains("Census 2026", text);
        Assert.Contains("Generated: Oct 28, 2026, 7:00 AM (America/Bogota)", text);
        Assert.Contains("- Total: 250 households", text);
        Assert.Contains("municipality | Households visited: value", text);
        Assert.Contains("Municipality 1 | 1", text);
        Assert.Contains("2 more rows", text);
        Assert.Contains("Visits has not published yet.", text);
        Assert.Contains("https://tamiza.test/projects/aa/dashboards/d1", text);
    }

    [Fact]
    public void Spanish_body()
    {
        var (html, _) = ReportHtml.Write(Model([Bars(1, "Por municipio", 12)], "es-CO"), true, Options);

        Assert.Contains("lang=\"es\"", html);
        Assert.Contains("Generado", html);
        Assert.Contains("2 filas más", html);
        Assert.Contains("28 oct 2026", html);
    }

    [Fact]
    public void A_cross_table_within_six_series_shows_every_column()
    {
        var (html, _) = ReportHtml.Write(Model([CrossTable(1, "Sources", series: 6)]), true, Options);

        foreach (var header in new[] { "S1", "S6", "Total" })
        {
            Assert.Contains($">{header}</th>", html);
        }

        Assert.DoesNotContain("more column", html);
    }

    [Fact]
    public void A_wide_cross_table_shows_six_series_and_the_total_and_says_how_many_columns_it_has_more()
    {
        var widget = CrossTable(1, "Sources", series: 25);

        var (html, _) = ReportHtml.Write(Model([widget]), true, Options);
        var text = ReportPlainText.Write(Model([widget]), true, new HashSet<int> { 1 }, Options);

        Assert.Equal(19, widget.HiddenSeriesColumns);
        Assert.Contains(">S6</th>", html);
        Assert.DoesNotContain(">S7</th>", html);
        Assert.Contains(">Total</th>", html);
        Assert.Contains("19 more columns", html);
        // A row keeps its cells in step with the headers it shows: group, six series, total.
        Assert.Contains("municipality | S1 | S2 | S3 | S4 | S5 | S6 | Total", text);
        Assert.Contains("Municipality 1 | 1 | 2 | 3 | 4 | 5 | 6 | 25", text);
        Assert.Contains("19 more columns", text);
        // The workbook holds every column.
        Assert.Equal(27, widget.Columns.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 26], widget.BodyColumns);
    }

    [Fact]
    public void One_more_column_reads_in_the_singular_and_in_spanish()
    {
        var widget = CrossTable(1, "Sources", series: 7);

        var (html, _) = ReportHtml.Write(Model([widget]), true, Options);
        var (spanish, _) = ReportHtml.Write(Model([widget], "es-CO"), true, Options);

        Assert.Contains("1 more column<", html);
        Assert.Contains("1 columna más", spanish);
    }

    [Fact]
    public void Fifty_wide_cross_tables_still_fit_the_size_of_the_body()
    {
        var widgets = Enumerable.Range(1, 50).Select(i => CrossTable(i, $"Widget {i}", series: 25, groups: 10)).ToList();

        var (html, shown) = ReportHtml.Write(Model(widgets), true, Options);

        Assert.True(Encoding.UTF8.GetByteCount(html) <= Options.MaxBodyKb * 1024);
        Assert.InRange(shown.Count, 1, 49);
        Assert.Contains("these widgets are not shown in its body", html);
        Assert.Contains("<li>Widget 50</li>", html);
        // A block that fits shows six series columns, never the twenty-five.
        Assert.DoesNotContain(">S7</th>", html);
    }
}
