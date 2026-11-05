using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/indicators/query</c> for a widget that leaves out the values its indicator declares as meaning no data
/// (<c>missingFooter</c>, design D6 of add-small-sample-warnings), and the <c>noData</c> it reports. Names and values are invented.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorNoDataTests(PostgresFixture postgres)
{
    private const string Columns = "comuna text, categoria text, numerator numeric, denominator numeric";

    /// <summary>
    /// Six comunas: a real one named SIN NOMBRE, "sin dato" in lower case (not the declared text), the block-less "Sin dato" (4 households,
    /// all of whom attended), and rows that have no comuna at all.
    /// </summary>
    private const string Rows =
        "('COMUNA 1', 'Si', 150, 300), ('COMUNA 7', 'Si', 8, 16), ('SIN NOMBRE', 'Si', 10, 20), ('sin dato', 'Si', 1, 2), ('Sin dato', 'Si', 4, 4), (NULL, 'Si', 3, 6)";

    private static readonly object RatioOf = new { numerator = "numerator", denominator = "denominator" };

    private static readonly object SinDato = new { comuna = new { missing = new[] { "Sin dato" } } };

    private async Task<IndicatorsHarness> WithComunasAsync(object? declared = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", Columns, Rows, format: "percent", labels: declared ?? SinDato);
        return h;
    }

    private static object ByComuna(bool? missingFooter = true, object[]? filters = null) => new
    {
        indicator = "teatro_hoy", ratio = RatioOf, groupBy = "comuna", missingFooter, filters = filters ?? [],
    };

    private static string[] Groups(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString() ?? "(none)").ToArray();

    private static double Number(JsonElement element, string name) => element.GetProperty(name).GetDouble();

    // Comuna with no block

    [Fact]
    public async Task Comuna_with_no_block()
    {
        await using var h = await WithComunasAsync();

        var result = await h.QueryAsync(ByComuna(), "column");

        // No "Sin dato" column, and no `in` filter on comuna to get there.
        Assert.Equal(["COMUNA 1", "COMUNA 7", "SIN NOMBRE", "sin dato", "(none)"], Groups(result).Order(StringComparer.Ordinal).OrderBy(g => g == "(none)").ToArray());
        var noData = result.GetProperty("noData");
        Assert.Equal(["Sin dato"], noData.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(4, Number(noData, "numerator"));
        Assert.Equal(4, Number(noData, "denominator"));
        Assert.Equal(1, Number(noData, "value"));
        Assert.Equal(4, Number(noData, "n"));
        // A ratio has no share.
        Assert.False(noData.TryGetProperty("share", out _));
    }

    [Fact]
    public async Task Widget_without_the_option()
    {
        await using var h = await WithComunasAsync();

        foreach (var missingFooter in new bool?[] { null, false })
        {
            var result = await h.QueryAsync(ByComuna(missingFooter), "column");

            Assert.Equal(6, result.GetProperty("rows").GetArrayLength());
            Assert.Contains("Sin dato", Groups(result));
            Assert.False(result.TryGetProperty("noData", out _));
        }
    }

    [Fact]
    public async Task Nothing_left_out()
    {
        await using var h = await WithComunasAsync();

        // The widget's own filters already keep the no-data value out: there is nothing to say.
        var result = await h.QueryAsync(ByComuna(filters: [new { column = "comuna", op = "in", values = new[] { "COMUNA 1", "COMUNA 7" } }]), "column");

        Assert.Equal(["COMUNA 1", "COMUNA 7"], Groups(result));
        Assert.False(result.TryGetProperty("noData", out _));
    }

    [Fact]
    public async Task Rows_with_no_value_kept()
    {
        await using var h = await WithComunasAsync();

        var result = await h.QueryAsync(ByComuna(), "column");

        // The missing group, "(none)", is not a declared value: its column is drawn as before, and it is not in the no-data line.
        var none = result.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("group").ValueKind == JsonValueKind.Null);
        Assert.Equal(0.5, none.GetProperty("value").GetDouble());
        Assert.DoesNotContain("(none)", result.GetProperty("noData").GetProperty("values").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Matched_as_exact_text()
    {
        await using var h = await WithComunasAsync();

        var result = await h.QueryAsync(ByComuna(), "column");

        // SIN NOMBRE is a real comuna and "sin dato" is another text: neither is the declared "Sin dato".
        Assert.Contains("SIN NOMBRE", Groups(result));
        Assert.Contains("sin dato", Groups(result));
        Assert.DoesNotContain("Sin dato", Groups(result));
    }

    [Fact]
    public async Task The_left_out_rows_count_in_no_value_total_n_or_overall_value()
    {
        await using var h = await WithComunasAsync(new { comuna = new { missing = new[] { "Sin dato" } }, categoria = new { categories = "single" }, denominator = new { @base = true } });

        var with = await h.QueryAsync(ByComuna(), "column", overall: true);
        var without = await h.QueryAsync(ByComuna(missingFooter: false), "column", overall: true);

        // 172 of 344 without the "Sin dato" rows, 176 of 348 with them.
        Assert.Equal(172d / 344, Number(with, "overall"), 9);
        Assert.Equal(176d / 348, Number(without, "overall"), 9);
        Assert.Equal(344, Number(with, "n"));
        Assert.Equal(348, Number(without, "n"));
    }

    [Fact]
    public async Task Row_limit_counts_what_the_widget_plots()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new Dictionary<string, string?> { ["IndicatorDashboard:MaxWidgetQueryRows"] = "3" });
        await h.PublishAsync("teatro_hoy", Columns, Rows, labels: SinDato);

        var left = await h.QueryAsync(ByComuna(), "column");
        var kept = await h.QueryAsync(ByComuna(missingFooter: false), "column");

        Assert.True(kept.GetProperty("rowLimitReached").GetBoolean());
        Assert.True(left.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(3, left.GetProperty("rows").GetArrayLength());
        Assert.DoesNotContain("Sin dato", Groups(left));
    }

    // A series

    private const string CountColumns = "comuna text, respuesta text, value numeric, hogares bigint";

    /// <summary>476 households in COMUNA 1 ... answering one question: the answers add up, and 11 of them said "4. No sabe".</summary>
    private const string CountRows =
        "('COMUNA 1', '1. Si', 200, 200), ('COMUNA 1', '2. No', 220, 220), ('COMUNA 1', '3. Tal vez', 45, 45), ('COMUNA 1', '4. No sabe', 11, 11)";

    [Fact]
    public async Task Answer_left_out_of_a_series()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", CountColumns, CountRows, labels: new { respuesta = new { missing = new[] { "4. No sabe" } } });
        object config(bool? missingFooter) => new { indicator = "teatro_conteo", groupBy = "comuna", seriesBy = "respuesta", layout = "percent", missingFooter };

        var left = await h.QueryAsync(config(true), "column");
        var kept = await h.QueryAsync(config(null), "column");

        // The legend has no "4. No sabe", and the shares are over the other answers: 200 of 465.
        Assert.Equal(["1. Si", "2. No", "3. Tal vez"], left.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetString()).Order(StringComparer.Ordinal));
        Assert.Contains("4. No sabe", kept.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("value").GetString()));
        Assert.Equal(465, Number(left.GetProperty("totals").GetProperty("all"), "value"));
        Assert.Equal(476, Number(kept.GetProperty("totals").GetProperty("all"), "value"));
        Assert.False(kept.TryGetProperty("noData", out _));
    }

    [Fact]
    public async Task A_measures_share()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", CountColumns, CountRows, labels: new { respuesta = new { missing = new[] { "4. No sabe" } } });

        var result = await h.QueryAsync(
            new { indicator = "teatro_conteo", groupBy = "comuna", seriesBy = "respuesta", layout = "percent", missingFooter = true }, "column");

        // 11 of the 476 households: the share counts the left-out rows in the total.
        var noData = result.GetProperty("noData");
        Assert.Equal(["4. No sabe"], noData.GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(11, Number(noData, "value"));
        Assert.Equal(11d / 476, Number(noData, "share"), 9);
        Assert.False(noData.TryGetProperty("numerator", out _));
        Assert.False(noData.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task A_measures_n_is_the_base_of_the_left_out_rows()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", CountColumns, CountRows, labels: new { respuesta = new { missing = new[] { "4. No sabe" } }, hogares = new { @base = true } });

        var result = await h.QueryAsync(
            new { indicator = "teatro_conteo", groupBy = "comuna", seriesBy = "respuesta", layout = "percent", missingFooter = true }, "column");

        Assert.Equal(11, Number(result.GetProperty("noData"), "n"));
        Assert.Equal(465, Number(result, "n"));
    }

    // The n of the left-out rows with answers declared

    private static string LongRows()
    {
        var rows = new List<string>();
        (string Variable, string[] Answers)[] questions = [("Asistio (E1)", ["Si", "No"]), ("Frecuencia (E2)", ["Poco", "Mucho", "Nada"])];
        foreach (var (variable, answers) in questions)
        {
            for (var i = 0; i < answers.Length; i++)
            {
                foreach (var (comuna, households) in new[] { ("COMUNA 1", 300), ("Sin dato", 4) })
                {
                    rows.Add($"('{variable}', '{i + 1}', '{answers[i]}', '{comuna}', {households / answers.Length}, {households})");
                }
            }
        }

        return string.Join(",\n", rows);
    }

    [Fact]
    public async Task The_n_of_the_left_out_rows_with_answers_declared()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "teatro_hoy", "variable text, orden text, categoria text, comuna text, numerator numeric, denominator numeric", LongRows(), format: "percent",
            labels: new
            {
                variable = new { categories = "multiple" }, orden = new { categories = "multiple" }, categoria = new { categories = "multiple" },
                comuna = new { missing = new[] { "Sin dato" } },
            });

        var result = await h.QueryAsync(
            new { indicator = "teatro_hoy", ratio = RatioOf, groupBy = "comuna", missingFooter = true, filters = new[] { new { column = "variable", op = "equals", value = "Frecuencia (E2)" } } },
            "column");

        // Three answers each hold the 4 households of "Sin dato": its n is 4, not 12, and the denominators still add up to 12.
        var noData = result.GetProperty("noData");
        Assert.Equal(4, Number(noData, "n"));
        Assert.Equal(12, Number(noData, "denominator"));
        Assert.Equal(["COMUNA 1"], Groups(result));
        Assert.Equal(300, Number(result, "n"));
    }

    // A pyramid's rows left out

    [Fact]
    public async Task A_pyramids_left_out_count_without_them()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "poblacion", "edad text, sexo text, value numeric",
            "('0-4', 'F', 5), ('0-4', 'M', 4), ('0-4', 'X', 1), ('Sin dato', 'F', 2), ('Sin dato', 'X', 9)",
            labels: new { edad = new { missing = new[] { "Sin dato" } } });
        object config(bool? missingFooter) => new
        {
            indicator = "poblacion", groupBy = "edad", seriesBy = "sexo", pyramid = new { left = "F", right = "M" }, missingFooter,
        };

        var left = await h.QueryAsync(config(true), "pyramid");
        var kept = await h.QueryAsync(config(null), "pyramid");

        // Without the option the rows of other sexes are 2 (one of them in "Sin dato"); with it the "Sin dato" rows are no one's rows.
        Assert.Equal(2, kept.GetProperty("leftOut").GetInt64());
        Assert.Equal(1, left.GetProperty("leftOut").GetInt64());
        Assert.Equal(11, Number(left.GetProperty("noData"), "value"));
    }

    // The settings and the stored config

    [Fact]
    public async Task A_stat_tile_that_leaves_out_no_data_is_refused()
    {
        await using var h = await WithComunasAsync();

        var response = await h.PostQueryAsync(new { indicator = "teatro_hoy", ratio = RatioOf, missingFooter = true }, "stat");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(IndicatorsHarness.Errors(problem, "missingFooter"));
    }

    [Fact]
    public async Task The_option_with_nothing_declared_changes_nothing()
    {
        await using var h = await WithComunasAsync(new { categoria = new { categories = "single" } });

        var result = await h.QueryAsync(ByComuna(), "column");
        var plain = await h.QueryAsync(ByComuna(missingFooter: null), "column");

        Assert.Equal(plain.GetRawText(), result.GetRawText());
        Assert.False(result.TryGetProperty("noData", out _));
        Assert.Equal(6, result.GetProperty("rows").GetArrayLength());
    }
}
