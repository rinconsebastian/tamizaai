using Tamiza.Api.Reports;

namespace Tamiza.Api.UnitTests.Reports;

public sealed class ReportTextTests
{
    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var (english, spanish) = ReportText.Keys;

        Assert.Equal(english.Order(StringComparer.Ordinal), spanish.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("es-CO")]
    [InlineData("es-MX")]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    public void Spanish_and_english_locales_are_accepted(string locale) => Assert.True(ReportText.IsSupported(locale));

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a locale at all")]
    public void Other_locales_are_refused(string? locale) => Assert.False(ReportText.IsSupported(locale));

    [Theory]
    [InlineData("es-CO", "es-CO")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("en-CO", "en")]
    [InlineData("es-DE", "es")]
    [InlineData("en-MX", "en")]
    public void A_region_the_server_has_no_data_for_resolves_to_its_language(string locale, string resolved)
    {
        Assert.Equal(resolved, ReportText.Resolve(locale));
        Assert.Equal(resolved, ReportText.For(resolved).Culture.Name);
    }

    [Fact]
    public void The_language_follows_the_locale()
    {
        Assert.Equal("Resumen", ReportText.For("es-CO")["summary"]);
        Assert.Equal("Summary", ReportText.For("en-GB")["summary"]);
    }

    [Fact]
    public void The_summary_sheet_of_a_widget_workbook_has_its_words_in_both_languages()
    {
        var english = ReportText.For("en-US");
        var spanish = ReportText.For("es-CO");

        Assert.Equal("Item", english["item"]);
        Assert.Equal("Elemento", spanish["item"]);
        Assert.Equal("Not filtered by", english["notFilteredBy"]);
        Assert.Equal("Sin filtrar por", spanish["notFilteredBy"]);
        Assert.Equal("All", english["allValues"]);
        Assert.Equal("Todos", spanish["allValues"]);
        // As the UI's dashboards.print.version, so the file and the page say the same.
        Assert.Equal("Version 9, saved by Ana on Oct 31, 2026", english.Format("versionLine", 9, "Ana", "Oct 31, 2026"));
        Assert.Equal("Versión 9, guardada por Ana el 31 oct 2026", spanish.Format("versionLine", 9, "Ana", "31 oct 2026"));
        foreach (var key in new[] { "indicator", "plotted", "exported", "version", "rows" })
        {
            Assert.NotEqual(english[key], spanish[key]);
        }
    }

    [Fact]
    public void The_default_labels_of_the_reference_lines_are_the_words_of_the_pages_two_languages()
    {
        // As indicators.referenceLines.kinds in tamiza-ui's en.json and es.json.
        var english = ReportText.For("en-US");
        var spanish = ReportText.For("es-CO");

        Assert.Equal(["Mean", "Median", "Overall", "Reference"], new[] { "referenceMean", "referenceMedian", "referenceOverall", "referenceValue" }.Select(k => english[k]));
        Assert.Equal(["Promedio", "Mediana", "Valor general", "Referencia"], new[] { "referenceMean", "referenceMedian", "referenceOverall", "referenceValue" }.Select(k => spanish[k]));
        Assert.Equal("Reference lines", english["referenceLines"]);
        Assert.Equal("Líneas de referencia", spanish["referenceLines"]);
    }

    [Fact]
    public void More_rows_has_a_singular_and_a_plural()
    {
        Assert.Equal("1 more row", ReportText.For("en-US").MoreRows(1));
        Assert.Equal("22 more rows", ReportText.For("en-US").MoreRows(22));
        Assert.Equal("22 filas más", ReportText.For("es-CO").MoreRows(22));
    }

    [Fact]
    public void A_stat_tile_names_its_statistic_and_its_line_without_filters_in_both_languages()
    {
        // As indicators.stat in tamiza-ui's en.json and es.json.
        var english = ReportText.For("en-US");
        var spanish = ReportText.For("es-CO");
        string[] keys = ["statMedian", "statP25", "statP75", "statMean"];

        Assert.Equal(
            ["Median of Precio", "First quartile of Precio", "Third quartile of Precio", "Mean of Precio", "Without filters: 50%"],
            keys.Select(k => english.Format(k, "Precio")).Append(english.Format("withoutFilters", "50%")));
        Assert.Equal(
            ["Mediana de Precio", "Primer cuartil de Precio", "Tercer cuartil de Precio", "Promedio de Precio", "Sin filtros: 50%"],
            keys.Select(k => spanish.Format(k, "Precio")).Append(spanish.Format("withoutFilters", "50%")));
    }

    [Fact]
    public void The_reason_of_a_card_that_would_add_up_several_answers_names_the_column_in_both_languages()
    {
        var english = ReportText.For("en-US").Format("combinedCategories", "categoria");
        var spanish = ReportText.For("es-CO").Format("combinedCategories", "categoria");

        Assert.StartsWith("categoria holds the answers of a multiple-choice question.", english);
        Assert.Contains("group or split the widget by categoria", english);
        Assert.StartsWith("categoria contiene las respuestas de una pregunta de selección múltiple.", spanish);
        Assert.Contains("agrupa o divide el widget por categoria", spanish);
    }
}
