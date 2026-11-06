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
    public void A_stat_tile_and_a_table_are_described_with_the_column_labels_of_their_captions()
    {
        const string caption = "Teatro hoy: Hogares que asistieron / Hogares";
        var bars = Bars(2, "Por comuna", 2) with
        {
            Caption = caption,
            Columns = [new ReportColumn("Comuna"), new ReportColumn(caption, "#,##0")],
        };
        var model = Model([Stat(1, "Asistencia", "33%") with { Caption = caption }, bars]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.Contains(caption, html);
        Assert.Contains(">Comuna</th>", html);
        Assert.Contains($">{caption}</th>", html);
        Assert.Contains(caption, text);
        Assert.DoesNotContain("numerator", html);
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
    public void A_box_table_shows_its_nine_columns_whole_and_ten_rows_in_the_body_and_the_text()
    {
        var model = Model([BoxTable(1, "Household size", 12)]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        foreach (var header in new[] { "municipality", "Values", "Minimum", "Q1", "Median", "Q3", "Maximum", "Mean", "Outliers" })
        {
            Assert.Contains($">{header}</th>", html);
        }

        Assert.Contains("Municipality 10<", html);
        Assert.DoesNotContain("Municipality 11<", html);
        Assert.Contains("2 more rows", html);
        Assert.DoesNotContain("more columns", html);
        Assert.Contains("municipality | Values | Minimum | Q1 | Median | Q3 | Maximum | Mean | Outliers", text);
        Assert.Contains("2 more rows", text);
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

    private const string Source = "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas";

    [Fact]
    public void Source_in_the_header_in_the_body_and_the_plain_text()
    {
        var model = Model([Stat(1, "Total", "250")], source: Source);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.Contains($">Source</td><td style=\"padding:2px 0;\">{Source}</td>", html);
        Assert.Contains($"Source: {Source}", text);
        Assert.True(html.IndexOf("Round: 3", StringComparison.Ordinal) < html.IndexOf(Source, StringComparison.Ordinal));
        Assert.Contains($"Fuente: {Source}", ReportPlainText.Write(Model([Stat(1, "Total", "250")], "es-CO", source: Source), true, shown, Options));
    }

    [Fact]
    public void Dashboard_without_a_source()
    {
        var model = Model([Stat(1, "Total", "250")]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.DoesNotContain(">Source<", html);
        Assert.DoesNotContain("Source:", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_subtitle_follows_the_title_of_a_block_and_of_a_stat_tile()
    {
        const string subtitle = "¿En qué lugares ha visto teatro en el último año? (E4)";
        var model = Model([Stat(1, "Total", "250") with { Subtitle = subtitle }, Bars(2, "2.4. En qué lugares", 2) with { Subtitle = subtitle }]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.Contains($">2.4. En qué lugares</h2><p style=\"margin:0 0 4px;font-size:12px;color:#5b6270;\">{subtitle}</p><p", html);
        Assert.Contains($">Total</p><p style=\"margin:2px 0 0;font-size:12px;color:#5b6270;\">{subtitle}</p>", html);
        Assert.Contains($"2.4. En qué lugares\n{subtitle}\nHouseholds visited: value".ReplaceLineEndings(), text.ReplaceLineEndings());
        Assert.Contains($"- Total: 250 (Total: value){Environment.NewLine}  {subtitle}", text);
    }

    [Fact]
    public void A_hidden_caption_is_left_out_of_a_stat_tile_and_a_block_but_not_of_their_tables()
    {
        var bars = Bars(2, "By municipality", 2) with { CaptionHidden = true };
        var model = Model([Stat(1, "Fuera de Duitama", "12%") with { CaptionHidden = true }, bars]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.DoesNotContain("Fuera de Duitama: value", html);
        Assert.DoesNotContain(">Households visited: value</p>", html);
        Assert.Contains(">Households visited: value</th>", html);
        Assert.Contains("- Fuera de Duitama: 12%" + Environment.NewLine, text);
        Assert.DoesNotContain("(Fuera de Duitama: value)", text);
        Assert.Contains("municipality | Households visited: value", text);
        Assert.DoesNotContain($"By municipality{Environment.NewLine}Households visited: value{Environment.NewLine}", text);
    }

    [Fact]
    public void An_own_caption_text_is_the_caption_line_of_a_block()
    {
        var model = Model([Bars(1, "2.2", 2) with { Caption = "% de quienes asistieron" }]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.Contains(">% de quienes asistieron</p>", html);
        Assert.Contains($"2.2{Environment.NewLine}% de quienes asistieron{Environment.NewLine}", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_footnote_follows_the_table_and_the_lines_that_say_what_the_body_leaves_out()
    {
        const string footnote = "No suman 100 %: respuesta múltiple.";
        var model = Model([Bars(1, "2.4. En qué lugares", 12) with { Footnote = footnote }]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.True(html.IndexOf("</table><p", html.IndexOf("Municipality 10<", StringComparison.Ordinal), StringComparison.Ordinal) > 0);
        Assert.True(html.IndexOf("2 more rows", StringComparison.Ordinal) < html.IndexOf(footnote, StringComparison.Ordinal));
        Assert.True(text.IndexOf("2 more rows", StringComparison.Ordinal) < text.IndexOf(footnote, StringComparison.Ordinal));
        Assert.Contains($"2 more rows{Environment.NewLine}{footnote}", text);
        Assert.Equal(1, html.Split(footnote).Length - 1);
    }

    private static ReportReferenceLine Line(string kind, string label, double value, string text) => new(kind, label, value, text);

    [Fact]
    public void Reference_lines_come_under_the_table_one_per_line_in_the_widgets_order()
    {
        var bars = Bars(1, "2.1. Asistió, por comuna", 2) with
        {
            ReferenceLines =
            [
                Line("mean", "Mean", 0.3417, "34.17% de los hogares"),
                Line("overall", "Overall", 0.4599, "45.99% de los hogares"),
            ],
        };
        var model = Model([bars]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.True(html.IndexOf("</table><p", html.IndexOf("Municipality 2<", StringComparison.Ordinal), StringComparison.Ordinal) > 0);
        Assert.Contains("<p style=\"margin:4px 0 0;font-size:12px;color:#5b6270;\">Mean: 34.17% de los hogares</p><p style=\"margin:4px 0 0;font-size:12px;color:#5b6270;\">Overall: 45.99% de los hogares</p>", html);
        Assert.Contains($"Municipality 2 | 2{Environment.NewLine}Mean: 34.17% de los hogares{Environment.NewLine}Overall: 45.99% de los hogares{Environment.NewLine}", text);
    }

    [Fact]
    public void Reference_lines_follow_the_lines_that_say_what_the_body_leaves_out_and_come_before_the_footnote()
    {
        const string footnote = "No suman 100 %: respuesta múltiple.";
        var bars = Bars(1, "2.4. En qué lugares", 12) with
        {
            Footnote = footnote,
            ReferenceLines = [Line("mean", "Mean", 0.3, "30% de los hogares")],
        };
        var model = Model([bars]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.True(html.IndexOf("2 more rows", StringComparison.Ordinal) < html.IndexOf("Mean: 30%", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Mean: 30%", StringComparison.Ordinal) < html.IndexOf(footnote, StringComparison.Ordinal));
        Assert.Contains($"2 more rows{Environment.NewLine}Mean: 30% de los hogares{Environment.NewLine}{footnote}", text);
    }

    [Fact]
    public void Reference_lines_of_a_wide_cross_table_follow_the_line_with_its_hidden_columns()
    {
        var model = Model([CrossTable(1, "Sources", series: 25) with { ReferenceLines = [Line("overall", "Overall", 0.2, "20%")], Footnote = "Nota" }]);

        var (html, _) = ReportHtml.Write(model, true, Options);

        Assert.True(html.IndexOf("19 more columns", StringComparison.Ordinal) < html.IndexOf("Overall: 20%", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Overall: 20%", StringComparison.Ordinal) < html.IndexOf(">Nota<", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_lines_are_written_with_the_default_labels_of_the_emails_language_and_are_encoded()
    {
        var text = ReportText.For("es-CO");
        var bars = Bars(1, "2.1. Asistió, por comuna", 1) with
        {
            ReferenceLines =
            [
                Line("mean", text["referenceMean"], 0.3417, "34,17% de los hogares"),
                Line("overall", text["referenceOverall"], 0.4599, "45,99% de los hogares"),
                Line("value", "<b>Meta</b>", 0.5, "50%"),
            ],
        };
        var model = Model([bars], locale: "es-CO");

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.Contains(">Promedio: 34,17% de los hogares</p>", html);
        Assert.Contains(">Valor general: 45,99% de los hogares</p>", html);
        Assert.Contains("&lt;b&gt;Meta&lt;/b&gt;: 50%", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains($"Promedio: 34,17% de los hogares{Environment.NewLine}", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_widget_that_is_not_shown_writes_no_reference_lines()
    {
        var widget = Unreadable(1, "Visits", "This widget could not be read in time.") with { ReferenceLines = [Line("mean", "Mean", 0.3, "30%")] };
        var model = Model([widget]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.DoesNotContain("Mean: 30%", html);
        Assert.DoesNotContain("Mean: 30%", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_footnote_of_a_wide_cross_table_follows_the_line_with_its_hidden_columns()
    {
        var model = Model([CrossTable(1, "Sources", series: 25) with { Footnote = "Nota" }]);

        var (html, _) = ReportHtml.Write(model, true, Options);

        Assert.True(html.IndexOf("19 more columns", StringComparison.Ordinal) < html.IndexOf(">Nota<", StringComparison.Ordinal));
    }

    [Fact]
    public void A_footnote_of_a_stat_tile_comes_after_its_value_caption_and_notes()
    {
        var stat = Stat(1, "Fuera de Duitama", "12%") with { Footnote = "Sin dato aparte (E5).", Notes = ["Not filtered by: Round"] };
        var model = Model([stat]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.True(html.IndexOf("12%", StringComparison.Ordinal) < html.IndexOf("Fuera de Duitama: value", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Not filtered by: Round", StringComparison.Ordinal) < html.IndexOf("Sin dato aparte (E5).", StringComparison.Ordinal));
        Assert.Contains($"  Not filtered by: Round{Environment.NewLine}  Sin dato aparte (E5).", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_footnote_of_a_widget_not_shown_follows_its_reason()
    {
        var widget = Unreadable(1, "Visits", "This widget could not be read in time.") with { Footnote = "Sin ponderar.", Subtitle = "¿Cuántas visitas?" };
        var model = Model([widget]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        Assert.True(html.IndexOf("could not be read in time", StringComparison.Ordinal) < html.IndexOf("Sin ponderar.", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Visits</h2>", StringComparison.Ordinal) < html.IndexOf("¿Cuántas visitas?", StringComparison.Ordinal));
        Assert.Contains($"could not be read in time.{Environment.NewLine}Sin ponderar.", text);
    }

    [Fact]
    public void The_texts_are_encoded()
    {
        var model = Model([Bars(1, "2.2", 1) with { Subtitle = "<b>x</b>", Footnote = "<script>alert(1)</script>" }], source: "A & <i>B</i>");

        var (html, _) = ReportHtml.Write(model, true, Options);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<i>", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.Contains("A &amp; &lt;i&gt;B&lt;/i&gt;", html);
    }

    [Fact]
    public void Email_of_widgets_without_these_settings_has_no_trace_of_them()
    {
        var model = Model([Stat(1, "Total", "250 households"), Bars(2, "By municipality", 2)]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        // Without a subtitle or a footnote a block is its title, its caption, its notes and its table, as before.
        Assert.Contains("By municipality</h2><p style=\"margin:0 0 6px;font-size:12px;color:#5b6270;\">Households visited: value</p><table", html);
        Assert.DoesNotContain("margin:6px 0 0;font-size:12px", html);
        Assert.DoesNotContain("margin:2px 0 0;font-size:12px", html);
        Assert.Contains($"By municipality{Environment.NewLine}Households visited: value{Environment.NewLine}municipality |", ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_block_that_grows_past_the_body_budget_with_a_500_character_footnote_is_listed_as_not_shown()
    {
        var footnote = new string('n', 500);
        var options = new ReportOptions { MaxBodyKb = 6, RowsPerBlock = 10 };
        var widgets = new[] { Bars(1, "First", 10) with { Footnote = footnote }, Bars(2, "Second", 10) with { Footnote = footnote } };

        var (html, shown) = ReportHtml.Write(Model(widgets), true, options);

        Assert.True(Encoding.UTF8.GetByteCount(html) <= options.MaxBodyKb * 1024);
        Assert.Equal([1], shown);
        Assert.Contains("<li>Second</li>", html);
        Assert.Equal(1, html.Split(footnote).Length - 1);
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

    // The declared category order (add-declared-category-order): the model arrives arranged, and the body and the text write it as it is.
    private static ReportWidget Answers(int groups) =>
        new(1, "2.8. Iría al teatro, por comuna", "bar", "Teatro conteo: conteo", WidgetOutcome.Shown,
            [new ReportColumn("comuna"), .. new[] { "Sí", "Tal vez", "No", "No sabe" }.Select(h => new ReportColumn(h, "0.0%")), new ReportColumn("Total", "#,##0")],
            Enumerable.Range(1, groups).Select(g => (IReadOnlyList<ReportCell>)
                [new ReportCell($"COMUNA {g}", $"COMUNA {g}"), .. Enumerable.Range(1, 4).Select(i => new ReportCell(i / 10d, $"{i * 10}%")), new ReportCell(60d, "60")]).ToList(),
            null, null, [], 4);

    [Fact]
    public void The_series_of_a_table_in_a_declared_order_are_written_in_that_order_with_ten_rows_in_the_body_and_the_text()
    {
        var model = Model([Answers(12)]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        var headers = new[] { ">comuna</th>", ">Sí</th>", ">Tal vez</th>", ">No</th>", ">No sabe</th>", ">Total</th>" };
        Assert.Equal(headers.OrderBy(h => html.IndexOf(h, StringComparison.Ordinal)), headers);
        Assert.Contains("COMUNA 10<", html);
        Assert.DoesNotContain("COMUNA 11<", html);
        Assert.Contains("2 more rows", html);
        Assert.Contains("comuna | Sí | Tal vez | No | No sabe | Total", text);
        Assert.Contains("2 more rows", text);
    }

    [Fact]
    public void The_mark_and_the_notes_of_small_bases_are_in_the_body_and_the_text_above_the_table_with_the_footnote_last()
    {
        const string footnote = "No suman 100 %: respuesta múltiple.";
        var widget = SmallBases(1, "2.1. Asistió, por comuna") with { Footnote = footnote };
        var model = Model([widget]);

        var (html, shown) = ReportHtml.Write(model, true, Options);
        var text = ReportPlainText.Write(model, true, shown, Options);

        foreach (var body in new[] { html, text })
        {
            Assert.Contains("50% *", body);
            Assert.Contains("45% *", body);
            Assert.DoesNotContain("COMUNA 1 *", body);
            // The notes come above the table and the footnote after it.
            var table = body.IndexOf("COMUNA 1", StringComparison.Ordinal);
            Assert.True(body.IndexOf("2 values have a base below 30, marked *.", StringComparison.Ordinal) is var small && small >= 0 && small < table);
            Assert.True(body.IndexOf("n = 157 Hogares que asistieron", StringComparison.Ordinal) is var n && n > small && n < table);
            Assert.True(body.IndexOf("Sin dato: 50%", StringComparison.Ordinal) is var noData && noData > n && noData < table);
            Assert.True(body.IndexOf(footnote, StringComparison.Ordinal) > body.LastIndexOf("SIN NOMBRE", StringComparison.Ordinal));
        }

        Assert.Contains(">n</th>", html);
        Assert.Contains($"Comuna | Teatro hoy: numerator / denominator | n{Environment.NewLine}COMUNA 1 | 50% | 121", text);
    }

    [Fact]
    public void Dashboards_of_widgets_with_small_bases_still_fit_the_budget_of_the_body()
    {
        var widgets = Enumerable.Range(1, 50).Select(i => SmallBases(i, $"Widget {i}") with { Footnote = "Respuesta múltiple." }).ToList();

        var (html, shown) = ReportHtml.Write(Model(widgets), true, Options);

        Assert.True(Encoding.UTF8.GetByteCount(html) <= Options.MaxBodyKb * 1024);
        Assert.InRange(shown.Count, 1, 50);
        Assert.Contains("50% *", html);
    }

    private static ReportWidget Tile(string value = "46.85% de los hogares") =>
        Stat(1, "Asistieron", value) with { StatLines = ["n = 476 Hogares", "Without filters: 50%"] };

    [Fact]
    public void A_stat_tiles_value_is_written_at_20_pixels_and_not_at_24()
    {
        var (html, _) = ReportHtml.Write(Model([Stat(1, "Teatro", "46.85% de los hogares")]), true, Options);

        Assert.Contains("font-size:20px;font-weight:bold", html);
        Assert.DoesNotContain("font-size:24px", html);
    }

    [Fact]
    public void A_stat_tiles_lines_come_after_what_it_plots_in_the_muted_color_and_before_its_notes_and_footnote()
    {
        var stat = Tile() with { Subtitle = "Hogares", Notes = ["Not filtered by: Round"], Footnote = "Sin dato aparte (E5)." };
        var model = Model([stat]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        var order = new[] { "Asistieron", "Hogares</p>", "46.85% de los hogares", "Asistieron: value", "n = 476 Hogares", "Without filters: 50%", "Not filtered by: Round", "Sin dato aparte (E5)." }
            .Select(part => html.IndexOf(part, StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.Order(), order);
        Assert.Contains("margin:2px 0 0;font-size:12px;color:#5b6270;\">n = 476 Hogares</p>", html);
        // The plain text puts them after the value on the tile's own line, then the subtitle, the notes and the footnote.
        var text = ReportPlainText.Write(model, true, shown, Options);
        Assert.Contains("- Asistieron: 46.85% de los hogares; n = 476 Hogares; Without filters: 50% (Asistieron: value)", text);
        Assert.Contains($"  Not filtered by: Round{Environment.NewLine}  Sin dato aparte (E5).", text);
    }

    [Fact]
    public void A_stat_tile_that_hides_its_caption_still_has_its_lines()
    {
        var model = Model([Tile() with { CaptionHidden = true }]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.DoesNotContain("Asistieron: value", html);
        Assert.Contains("n = 476 Hogares", html);
        Assert.Contains("- Asistieron: 46.85% de los hogares; n = 476 Hogares; Without filters: 50%" + Environment.NewLine, ReportPlainText.Write(model, true, shown, Options));
    }

    [Fact]
    public void A_stat_tile_without_lines_has_the_cell_and_the_line_it_had()
    {
        var model = Model([Stat(1, "Total", "250 households")]);

        var (html, shown) = ReportHtml.Write(model, true, Options);

        Assert.DoesNotContain("margin:2px 0 0;font-size:12px", html);
        Assert.Contains("- Total: 250 households (Total: value)" + Environment.NewLine, ReportPlainText.Write(model, true, shown, Options));
    }
}
