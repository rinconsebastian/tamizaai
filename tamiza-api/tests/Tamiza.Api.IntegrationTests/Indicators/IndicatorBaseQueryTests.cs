using System.Globalization;
using System.Net;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/indicators/query</c>: the numerator, the denominator and the base n of each value, and the widget's n
/// (design D2 to D5 of add-small-sample-warnings). The table is shaped like <c>teatro_hoy</c>: a long % table with a row per comuna,
/// question and answer, every household of a comuna in the denominator of every answer. Names and values are invented.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorBaseQueryTests(PostgresFixture postgres)
{
    private const string Columns = "variable text, orden text, categoria text, comuna text, value numeric, numerator numeric, denominator numeric";

    /// <summary>The comunas and their households: 476 in all, 16 of them in COMUNA 7.</summary>
    private static readonly (string Comuna, int Households)[] Comunas = [("COMUNA 1", 300), ("COMUNA 2", 140), ("COMUNA 7", 16), ("SIN NOMBRE", 20)];

    /// <summary>The questions, each asked of every household, and the share (in quarters) of the households that gave each answer.</summary>
    private static readonly (string Variable, (string Answer, int Quarters)[] Answers)[] Questions =
    [
        ("Asistio (E1)", [("Si", 2), ("No", 2)]),
        ("Frecuencia (E2)", [("Poco", 1), ("Mucho", 1), ("Nada", 2)]),
        ("Ropa", [("Centro", 2), ("Online", 2)]),
        ("Tecnologia", [("Centro", 1), ("Online", 3)]),
    ];

    private static string LongRows(string? extra = null)
    {
        var rows = new List<string>();
        foreach (var (variable, answers) in Questions)
        {
            for (var i = 0; i < answers.Length; i++)
            {
                foreach (var (comuna, households) in Comunas)
                {
                    var numerator = households * answers[i].Quarters / 4;
                    rows.Add($"('{variable}', '{i + 1}', '{answers[i].Answer}', '{comuna}', 0, {numerator}, {households})");
                }
            }
        }

        if (extra is not null)
        {
            rows.Add(extra);
        }

        return string.Join(",\n", rows);
    }

    private static readonly object RatioOf = new { numerator = "numerator", denominator = "denominator" };

    /// <summary>The columns of the question, its code and the answer, declared as the answers of multiple-choice questions.</summary>
    private static readonly object DeclaredAnswers = new
    {
        variable = new { categories = "multiple" },
        orden = new { categories = "multiple" },
        categoria = new { categories = "multiple" },
    };

    private async Task<IndicatorsHarness> WithTeatroAsync(object? declared = null, string? rows = null, Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("teatro_hoy", Columns, rows ?? LongRows(), format: "percent", labels: declared);
        return h;
    }

    private static object Filter(string column, string value) => new { column, op = "equals", value };

    private static object Text(string column, params string[] values) => new { column, kind = "text", values };

    private static object Attended(string? groupBy = "comuna", string? seriesBy = null, object[]? extraFilters = null) => new
    {
        indicator = "teatro_hoy", ratio = RatioOf, groupBy, seriesBy,
        filters = new[] { Filter("variable", "Asistio (E1)"), Filter("categoria", "Si") }.Concat(extraFilters ?? []).ToArray(),
    };

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    /// <summary>The rows of a plain response as <c>group|value|numerator|denominator|n</c>, a missing figure as a dash.</summary>
    private static string[] Figures(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().Select(r =>
            $"{r.GetProperty("group").GetString() ?? "(none)"}|{r.GetProperty("value").GetDouble().ToString("0.###", CultureInfo.InvariantCulture)}"
            + $"|{Fig(r, "numerator")}|{Fig(r, "denominator")}|{Fig(r, "n")}").ToArray();

    private static string Fig(JsonElement element, string name) =>
        Number(element, name) is { } value ? value.ToString("0.###", CultureInfo.InvariantCulture) : "-";

    // Numerator and denominator returned

    [Fact]
    public async Task Numerator_and_denominator_returned()
    {
        await using var h = await WithTeatroAsync();

        var result = await h.QueryAsync(Attended(), "column");

        Assert.Equal(
            ["COMUNA 1|0.5|150|300|300", "COMUNA 2|0.5|70|140|140", "COMUNA 7|0.5|8|16|16", "SIN NOMBRE|0.5|10|20|20"],
            Figures(result));
    }

    [Fact]
    public async Task Denominator_as_the_base_and_no_widget_n_without_a_declaration()
    {
        await using var h = await WithTeatroAsync();

        var result = await h.QueryAsync(Attended(), "column");

        // COMUNA 7 rests on 16 households, but the engine cannot tell the comunas from the answers of one question: no n for the widget.
        Assert.Equal("16", Fig(result.GetProperty("rows")[2], "n"));
        Assert.False(result.TryGetProperty("n", out _));
    }

    // Nothing declared

    [Fact]
    public async Task Nothing_declared_gives_a_stat_tile_the_n_of_its_mark_and_no_widget_n()
    {
        await using var h = await WithTeatroAsync();

        var tile = await h.QueryAsync(Attended(groupBy: null), "stat");

        var row = tile.GetProperty("rows")[0];
        Assert.Equal(238, row.GetProperty("numerator").GetDouble());
        Assert.Equal(476, row.GetProperty("denominator").GetDouble());
        Assert.Equal(476, row.GetProperty("n").GetDouble());
        Assert.False(tile.TryGetProperty("n", out _));
    }

    // Declared base column

    private const string CountColumns = "estrato text, value numeric, hogares bigint";

    private const string CountRows = "('Estrato 2', 120.5, 154), ('Estrato 2', 80, 100), ('Estrato 3', 70, 90), ('Estrato 4', 10, 7)";

    [Fact]
    public async Task Declared_base_column()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("comercio_gastos_resumen", CountColumns, CountRows, labels: new { hogares = new { label = "Hogares", @base = true } });

        var result = await h.QueryAsync(new { indicator = "comercio_gastos_resumen", groupBy = "estrato" }, "column");

        // A measure with a base column returns only its n: the sum of the base over the rows of the value.
        Assert.Equal(["Estrato 2|200.5|-|-|254", "Estrato 3|70|-|-|90", "Estrato 4|10|-|-|7"], Figures(result));
        Assert.Equal(351, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task A_declared_base_column_is_the_n_of_a_ratio_and_the_denominator_is_still_returned()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("comercio_gastos_resumen", "estrato text, numerator numeric, denominator numeric, hogares bigint",
            "('Estrato 2', 5, 10, 154), ('Estrato 2', 5, 10, 100), ('Estrato 3', 1, 4, 90)", labels: new { hogares = new { @base = true } });

        var result = await h.QueryAsync(new { indicator = "comercio_gastos_resumen", groupBy = "estrato", ratio = RatioOf }, "column");

        Assert.Equal(["Estrato 2|0.5|10|20|254", "Estrato 3|0.25|1|4|90"], Figures(result));
        Assert.Equal(344, result.GetProperty("n").GetDouble());
    }

    // Base declared but not usable (pipeline: Base column no longer published)

    [Fact]
    public async Task Base_column_no_longer_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("comercio_gastos_resumen", "estrato text, value numeric", "('Estrato 2', 5), ('Estrato 3', 7)", labels: new { hogares = new { @base = true } });

        var result = await h.QueryAsync(new { indicator = "comercio_gastos_resumen", groupBy = "estrato" }, "column");

        // As if it declared no base: a measure has no n, and the widget is not stale.
        Assert.Equal(["Estrato 2|5|-|-|-", "Estrato 3|7|-|-|-"], Figures(result));
        Assert.False(result.TryGetProperty("n", out _));
        Assert.False(result.GetProperty("missing").GetProperty("any").GetBoolean());
    }

    [Fact]
    public async Task A_base_held_as_text_is_ignored()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("comercio_gastos_resumen", "estrato text, value numeric, hogares text", "('Estrato 2', 5, '154'), ('Estrato 3', 7, '90')",
            labels: new { hogares = new { @base = true } });

        var result = await h.QueryAsync(new { indicator = "comercio_gastos_resumen", groupBy = "estrato" }, "column");

        Assert.Equal(["Estrato 2|5|-|-|-", "Estrato 3|7|-|-|-"], Figures(result));
        Assert.False(result.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task An_indicator_whose_newest_version_is_a_removal_has_no_metadata()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);
        var declared = await h.QueryAsync(Attended(), "column");
        Assert.Equal(476, declared.GetProperty("n").GetDouble());

        // The script is removed; its table stays published until a run replaces it.
        var removed = await h.Analyst.DeleteAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/teatro_hoy");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var result = await h.QueryAsync(Attended(), "column");

        Assert.False(result.TryGetProperty("n", out _));
        Assert.Equal("300", Fig(result.GetProperty("rows")[0], "n"));
    }

    // Measure without a base

    [Fact]
    public async Task Measure_without_a_base()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", "pregunta text, respuesta text, value numeric", "('E1', 'Si', 223), ('E1', 'No', 253)");

        var result = await h.QueryAsync(new { indicator = "teatro_conteo", groupBy = "respuesta" }, "column");

        Assert.Equal(["No|253|-|-|-", "Si|223|-|-|-"], Figures(result));
        Assert.False(result.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task Measure_without_a_base_answers_as_before()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", "pregunta text, respuesta text, value numeric", "('E1', 'Si', 223), ('E1', 'No', 253)");
        const string missing = "\"missing\":{\"indicator\":false,\"columns\":[],\"any\":false}";

        // Byte for byte what the build before base figures answered, for every kind of widget that has no ratio and no base column.
        Assert.Equal(
            """{"published":true,"rows":[{"group":"No","value":253,"geometry":null},{"group":"Si","value":223,"geometry":null}],"rowLimitReached":false,MISSING}""".Replace("MISSING", missing),
            (await h.QueryAsync(new { indicator = "teatro_conteo", groupBy = "respuesta" }, "column")).GetRawText());
        Assert.Equal(
            """{"published":true,"rows":[{"group":null,"value":476,"geometry":null}],"rowLimitReached":false,MISSING}""".Replace("MISSING", missing),
            (await h.QueryAsync(new { indicator = "teatro_conteo" }, "stat")).GetRawText());
        var series = await h.QueryAsync(new { indicator = "teatro_conteo", groupBy = "pregunta", seriesBy = "respuesta" }, "column");
        Assert.Equal(
            """{"published":true,"rows":[{"group":"E1","value":253,"geometry":null,"rows":1,"series":"No","seriesKind":"value"},{"group":"E1","value":223,"geometry":null,"rows":1,"series":"Si","seriesKind":"value"}],"rowLimitReached":false,MISSING,"series":[{"value":"No","kind":"value"},{"value":"Si","kind":"value"}],"totals":{"groups":[{"group":"E1","value":476,"rows":2}],"series":[{"series":"No","kind":"value","value":253,"rows":1},{"series":"Si","kind":"value","value":223,"rows":1}],"all":{"value":476,"rows":2}}}""".Replace("MISSING", missing),
            series.GetRawText());
    }

    // A pinned response

    [Fact]
    public async Task A_ratio_column_charts_whole_response_is_pinned()
    {
        await using var h = await WithTeatroAsync();

        var result = await h.QueryAsync(
            new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "comuna", filters = new[] { Filter("variable", "Asistio (E1)"), Filter("categoria", "Si"), Filter("comuna", "COMUNA 7") } },
            "column");

        Assert.Equal(
            """{"published":true,"rows":[{"group":"COMUNA 7","value":0.5,"geometry":null,"numerator":8,"denominator":16,"n":16}],"rowLimitReached":false,"missing":{"indicator":false,"columns":[],"any":false}}""",
            result.GetRawText());
    }

    // The n of the answers of one question (design D3)

    [Fact]
    public async Task Answers_of_one_question()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        // The five answers of two questions' worth of rows would add up to many times 476; one question's two answers are one 476.
        var result = await h.QueryAsync(
            new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "categoria", filters = new[] { Filter("variable", "Asistio (E1)") } }, "bar");

        Assert.Equal(["No|0.5|238|476|476", "Si|0.5|238|476|476"], Figures(result));
        Assert.Equal(476, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task Total_over_the_answers()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        var result = await h.QueryAsync(
            new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "comuna", seriesBy = "categoria", filters = new[] { Filter("variable", "Frecuencia (E2)") } }, "column");
        var groups = result.GetProperty("totals").GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("group").GetString()!);

        // Three answers per comuna: the total of COMUNA 1 has the n 300, not 900.
        Assert.Equal(300, groups["COMUNA 1"].GetProperty("n").GetDouble());
        Assert.Equal(16, groups["COMUNA 7"].GetProperty("n").GetDouble());
        Assert.Equal(900, groups["COMUNA 1"].GetProperty("denominator").GetDouble());
        Assert.Equal(476, result.GetProperty("totals").GetProperty("all").GetProperty("n").GetDouble());
        Assert.Equal(476, result.GetProperty("n").GetDouble());
        // Each cell is one answer: its n is its group's households.
        Assert.All(result.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("group").GetString() == "COMUNA 7"),
            r => Assert.Equal(16, r.GetProperty("n").GetDouble()));
        // And a series' total counts every household once too.
        Assert.All(result.GetProperty("totals").GetProperty("series").EnumerateArray(), s => Assert.Equal(476, s.GetProperty("n").GetDouble()));
    }

    [Fact]
    public async Task Two_questions_in_one_widget()
    {
        object config(object[]? filters = null) => new
        {
            indicator = "teatro_hoy", ratio = RatioOf, groupBy = "categoria", seriesBy = "variable",
            filters = filters ?? [new { column = "variable", op = "in", values = new[] { "Ropa", "Tecnologia" } }],
        };
        await using var withBoth = await WithTeatroAsync(DeclaredAnswers);
        await using var onlyAnswers = await WithTeatroAsync(new { categoria = new { categories = "multiple" } });

        var both = await withBoth.QueryAsync(config(), "column");
        var alone = await onlyAnswers.QueryAsync(config(), "column");

        // The places share names across the two questions: grouped by categoria alone they add both questions' households.
        Assert.Equal(476, both.GetProperty("n").GetDouble());
        Assert.Equal(952, alone.GetProperty("n").GetDouble());
        // The plotted values are the same either way.
        Assert.Equal(
            both.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("value").GetDouble()),
            alone.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("value").GetDouble()));
    }

    [Fact]
    public async Task Subgroups_added_up()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        var result = await h.QueryAsync(Attended(), "column");

        Assert.Equal(["COMUNA 1|0.5|150|300|300", "COMUNA 2|0.5|70|140|140", "COMUNA 7|0.5|8|16|16", "SIN NOMBRE|0.5|10|20|20"], Figures(result));
        Assert.Equal(476, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task Dashboard_filter_before_the_n()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        var result = await h.QueryAsync(Attended(), "column", dashboardFilters: new[] { Text("comuna", "COMUNA 1", "COMUNA 7") });

        // Each column's n counts only the rows the filter keeps, and so does the widget's: 300 + 16.
        Assert.Equal(["COMUNA 1|0.5|150|300|300", "COMUNA 7|0.5|8|16|16"], Figures(result));
        Assert.Equal(316, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task Widget_n_after_the_filters()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        var result = await h.QueryAsync(Attended(), "column", dashboardFilters: new[] { Text("comuna", "COMUNA 1") });

        Assert.Equal(300, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task Values_unchanged_by_the_declaration()
    {
        await using var declared = await WithTeatroAsync(DeclaredAnswers);
        await using var undeclared = await WithTeatroAsync();

        foreach (var (chartType, config) in new (string, object)[]
                 {
                     ("column", Attended()),
                     ("bar", new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "categoria", filters = new[] { Filter("variable", "Frecuencia (E2)") } }),
                     ("column", Attended(seriesBy: "categoria")),
                     ("stat", Attended(groupBy: null)),
                 })
        {
            var with = await declared.QueryAsync(config, chartType);
            var without = await undeclared.QueryAsync(config, chartType);
            Assert.Equal(
                with.GetProperty("rows").EnumerateArray().Select(r => $"{r.GetProperty("group").GetString()}|{r.GetProperty("value").GetDouble():R}"),
                without.GetProperty("rows").EnumerateArray().Select(r => $"{r.GetProperty("group").GetString()}|{r.GetProperty("value").GetDouble():R}"));
        }
    }

    [Fact]
    public async Task Declaring_orden_with_categoria_gives_the_same_n_as_categoria_alone()
    {
        await using var both = await WithTeatroAsync(new { orden = new { categories = "single" }, categoria = new { categories = "single" } });
        await using var alone = await WithTeatroAsync(new { categoria = new { categories = "single" } });
        var config = new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "categoria", filters = new[] { Filter("variable", "Frecuencia (E2)") } };

        var withOrden = await both.QueryAsync(config, "bar");
        var withoutOrden = await alone.QueryAsync(config, "bar");

        Assert.Equal(Figures(withoutOrden), Figures(withOrden));
        Assert.Equal(476, withOrden.GetProperty("n").GetDouble());
        Assert.Equal(476, withoutOrden.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task A_stat_tile_over_several_answers_has_the_widget_n_of_the_answers()
    {
        await using var h = await WithTeatroAsync(DeclaredAnswers);

        // The two answers of E1 are 952 households' worth of denominators, but 476 households.
        var tile = await h.QueryAsync(new { indicator = "teatro_hoy", ratio = RatioOf, filters = new[] { Filter("variable", "Asistio (E1)") } }, "stat");

        Assert.Equal(952, tile.GetProperty("rows")[0].GetProperty("denominator").GetDouble());
        Assert.Equal(476, tile.GetProperty("rows")[0].GetProperty("n").GetDouble());
        Assert.Equal(476, tile.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task A_missing_groups_n()
    {
        // 6 households with no comuna, who answered "Si" to E1.
        await using var h = await WithTeatroAsync(DeclaredAnswers, LongRows("('Asistio (E1)', '1', 'Si', NULL, 0, 3, 6), ('Asistio (E1)', '2', 'No', NULL, 0, 3, 6)"));

        var result = await h.QueryAsync(Attended(), "column");

        Assert.Equal("(none)|0.5|3|6|6", Figures(result)[^1]);
        Assert.Equal(482, result.GetProperty("n").GetDouble());
    }

    [Fact]
    public async Task A_timestamp_group()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "teatro_semanal", "categoria text, semana timestamptz, numerator numeric, denominator numeric",
            """
            ('Si', '2026-01-05 00:00:00+00', 5, 10), ('No', '2026-01-05 00:00:00+00', 5, 10),
            ('Si', '2026-01-12 00:00:00+00', 3, 8), ('No', '2026-01-12 00:00:00+00', 5, 8)
            """, labels: new { categoria = new { categories = "single" } });

        var result = await h.QueryAsync(new { indicator = "teatro_semanal", ratio = RatioOf, groupBy = "semana" }, "line");

        // The counted statement writes the timestamps as the rows do, so each row finds its n.
        Assert.Equal(["2026-01-05T00:00:00Z|0.5|10|20|10", "2026-01-12T00:00:00Z|0.5|8|16|8"], Figures(result).Select(r => r.Replace("+00:00", "Z")));
        Assert.Equal(18, result.GetProperty("n").GetDouble());
    }

    // The widget n past the row limit

    [Fact]
    public async Task The_widget_n_past_the_row_limit_counts_every_group()
    {
        await using var counted = await WithTeatroAsync(DeclaredAnswers, settings: new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });
        await using var windowed = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });
        await windowed.PublishAsync("comercio_gastos_resumen", CountColumns, CountRows, labels: new { hogares = new { @base = true } });

        var byAnswers = await counted.QueryAsync(Attended(), "column");
        var byBase = await windowed.QueryAsync(new { indicator = "comercio_gastos_resumen", groupBy = "estrato" }, "column");

        Assert.True(byAnswers.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(2, byAnswers.GetProperty("rows").GetArrayLength());
        Assert.Equal(476, byAnswers.GetProperty("n").GetDouble());
        Assert.True(byBase.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(2, byBase.GetProperty("rows").GetArrayLength());
        Assert.Equal(351, byBase.GetProperty("n").GetDouble());
    }

    // A box plot has no n of its own

    [Fact]
    public async Task A_box_plots_rows_are_unchanged_by_every_declaration()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("centro_tarifa_hogar", "vehiculo text, value numeric, hogares bigint", "('Bus', 100, 1), ('Bus', 300, 1), ('Bus', 200, 1), ('Moto', 50, 1)");
        var config = new { indicator = "centro_tarifa_hogar", groupBy = "vehiculo" };

        var before = await h.QueryAsync(config, "boxplot");
        await h.LabelColumnsAsync("centro_tarifa_hogar", new { hogares = new { @base = true }, vehiculo = new { categories = "single", missing = new[] { "Moto" } } });
        var after = await h.QueryAsync(config, "boxplot");

        Assert.Equal(before.GetRawText(), after.GetRawText());
        Assert.False(after.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task A_histograms_rows_are_unchanged_by_every_declaration()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("centro_tarifa_hogar", "vehiculo text, value numeric, hogares bigint", "('Bus', 100, 1), ('Bus', 300, 1), ('Moto', 50, 1)");
        var config = new { indicator = "centro_tarifa_hogar" };

        var before = await h.QueryAsync(config, "histogram");
        await h.LabelColumnsAsync("centro_tarifa_hogar", new { hogares = new { @base = true } });
        var after = await h.QueryAsync(config, "histogram");

        Assert.Equal(before.GetRawText(), after.GetRawText());
    }
}
