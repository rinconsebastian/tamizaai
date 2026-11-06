using System.Globalization;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/indicators/query</c> for a ratio over an indicator that declares the answers of a question (design D4 of
/// improve-stat-cards): single choice counts the base once across the answers, multiple choice shows a message instead of a sum of
/// answers. The tables are shaped like <c>cruces_intencion</c>, <c>teatro_intencion</c> and <c>escuelas_razones_no</c> (a long % table
/// with a row per comuna, question and answer, every household of a comuna in the denominator of every answer), with invented names and
/// values.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorAnswersQueryTests(PostgresFixture postgres)
{
    private const string Columns = "variable text, orden text, categoria text, comuna text, numerator numeric, denominator numeric";

    private static readonly object RatioOf = new { numerator = "numerator", denominator = "denominator" };

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Row(string variable, string orden, string categoria, string comuna, double numerator, double denominator) =>
        $"('{variable}', '{orden}', '{categoria}', '{comuna}', {F(numerator)}, {F(denominator)})";

    /// <summary>The columns a declaration names, each as the answers of a question of this kind (<c>single</c> or <c>multiple</c>).</summary>
    private static object Declare(string kind, params string[] columns) => columns.ToDictionary(column => column, _ => (object)new { categories = kind });

    private static object Equals(string column, string value) => new { column, op = "equals", value };

    private static object In(string column, params string[] values) => new { column, op = "in", values };

    private static double Value(JsonElement result) => result.GetProperty("rows")[0].GetProperty("value").GetDouble();

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string[] Rows(JsonElement result, string format = "0.########") =>
        result.GetProperty("rows").EnumerateArray()
            .Select(r => $"{r.GetProperty("group").GetString()}|{r.GetProperty("value").GetDouble().ToString(format, CultureInfo.InvariantCulture)}").ToArray();

    private static double[] Values(JsonElement result) =>
        result.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("value").GetDouble()).ToArray();

    private static string Message(JsonElement result) =>
        result.TryGetProperty("combinedCategories", out var column) ? column.GetString()! : "-";

    // cruces_intencion: "Componentes con sí o tal vez" has four answers, each given by a share of the households (0.42, 8.19, 41.39 and 45.17
    // percent, which add up to 95.17), and "Al menos un componente" has the published total of 95.17. COMUNA 1 has 6,000 households, COMUNA 2 4,000.

    private const string Components = "Componentes con sí o tal vez";

    private const string AtLeastOne = "Al menos un componente";

    private static readonly string[] FourAnswers = ["1", "2", "3", "4"];

    private static readonly (string Orden, string Answer, int First, int Second)[] ComponentAnswers =
        [("01", "1", 25, 17), ("02", "2", 500, 319), ("03", "3", 2500, 1639), ("04", "4", 2700, 1817)];

    private static List<string> CrucesRows(Func<string, string, bool>? keep = null)
    {
        var rows = new List<string>();
        foreach (var (orden, answer, first, second) in ComponentAnswers)
        {
            if (keep?.Invoke(answer, "COMUNA 1") ?? true)
            {
                rows.Add(Row(Components, orden, answer, "COMUNA 1", first, 6000));
            }

            if (keep?.Invoke(answer, "COMUNA 2") ?? true)
            {
                rows.Add(Row(Components, orden, answer, "COMUNA 2", second, 4000));
            }
        }

        rows.Add(Row(AtLeastOne, "05", "5. Cualquier componente", "COMUNA 1", 5725, 6000));
        rows.Add(Row(AtLeastOne, "05", "5. Cualquier componente", "COMUNA 2", 3792, 4000));
        return rows;
    }

    private async Task<IndicatorsHarness> WithCrucesAsync(object? declared, List<string>? rows = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("cruces_intencion", Columns, string.Join(",\n", rows ?? CrucesRows()), format: "percent", labels: declared);
        return h;
    }

    private static object Card(string? groupBy = null, string variable = Components, params string[] answers) => new
    {
        indicator = "cruces_intencion", ratio = RatioOf, groupBy,
        filters = new[] { Equals("variable", variable), answers.Length == 1 ? Equals("categoria", answers[0]) : In("categoria", answers) },
    };

    // Several answers of one question

    [Fact]
    public async Task Several_answers_of_one_question()
    {
        await using var h = await WithCrucesAsync(Declare("single", "categoria"));

        var result = await h.QueryAsync(Card(null, Components, FourAnswers), "stat");

        // 9,517 of the 10,000 households, not 9,517 of 4 x 10,000: 95.17 %, not 23.79 %.
        var row = result.GetProperty("rows")[0];
        Assert.Equal(0.9517, row.GetProperty("value").GetDouble(), 9);
        Assert.Equal(9517, Number(row, "numerator"));
        Assert.Equal(10000, Number(row, "denominator"));
        Assert.Equal(10000, Number(row, "n"));
        Assert.Equal(10000, Number(result, "n"));
    }

    [Fact]
    public async Task No_declaration()
    {
        await using var h = await WithCrucesAsync(null);

        var result = await h.QueryAsync(Card(null, Components, FourAnswers), "stat");

        Assert.Equal(0.237925, Value(result), 9);
        Assert.Equal(40000, Number(result.GetProperty("rows")[0], "denominator"));
    }

    [Fact]
    public async Task Several_answers_by_comuna()
    {
        await using var h = await WithCrucesAsync(Declare("single", "categoria"));

        var result = await h.QueryAsync(Card("comuna", Components, FourAnswers), "column");

        var rows = result.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(["COMUNA 1", "COMUNA 2"], rows.Select(r => r.GetProperty("group").GetString()));
        Assert.Equal([5725 / 6000.0, 3792 / 4000.0], rows.Select(r => r.GetProperty("value").GetDouble()).ToArray());
        // Each comuna's denominator, the base of its value, is its households counted once, and is its n.
        Assert.Equal([5725.0, 3792.0], rows.Select(r => Number(r, "numerator")!.Value));
        Assert.Equal([6000.0, 4000.0], rows.Select(r => Number(r, "denominator")!.Value));
        Assert.Equal([6000.0, 4000.0], rows.Select(r => Number(r, "n")!.Value));
        Assert.Equal(10000, Number(result, "n"));
    }

    [Fact]
    public async Task Published_total_unchanged()
    {
        await using var declared = await WithCrucesAsync(Declare("single", "categoria"));
        await using var undeclared = await WithCrucesAsync(null);
        var card = Card(null, AtLeastOne, "5. Cualquier componente");

        Assert.Equal(0.9517, Value(await declared.QueryAsync(card, "stat")), 9);
        Assert.Equal(0.9517, Value(await undeclared.QueryAsync(card, "stat")), 9);
    }

    [Fact]
    public async Task Code_column_that_moves_with_the_answer()
    {
        await using var declared = await WithCrucesAsync(Declare("single", "categoria"));
        await using var undeclared = await WithCrucesAsync(null);

        var config = Card("orden", Components, FourAnswers);
        var byCode = await declared.QueryAsync(config, "column");

        // Each column holds one answer, so it reads what it read before.
        Assert.Equal(Rows(await undeclared.QueryAsync(config, "column")), Rows(byCode));
        Assert.Equal(["01|0.0042", "02|0.0819", "03|0.4139", "04|0.4517"], Rows(byCode, "0.####"));
    }

    [Fact]
    public async Task Two_declared_columns_give_the_same_figure_as_one_for_a_widget_over_one_question()
    {
        await using var h = await WithCrucesAsync(Declare("single", "categoria"));
        var card = Card(null, Components, FourAnswers);
        var figures = new List<double> { Value(await h.QueryAsync(card, "stat")) };

        await h.LabelColumnsAsync("cruces_intencion", Declare("single", "categoria", "orden"), format: "percent");
        figures.Add(Value(await h.QueryAsync(card, "stat")));
        await h.LabelColumnsAsync("cruces_intencion", Declare("single", "variable", "orden", "categoria"), format: "percent");
        figures.Add(Value(await h.QueryAsync(card, "stat")));

        Assert.All(figures, figure => Assert.Equal(0.9517, figure, 9));
    }

    [Fact]
    public async Task Duplicate_rows_of_one_answer_are_still_summed()
    {
        var rows = CrucesRows().Where(r => !r.Contains("'01'")).ToList();
        // The first answer of COMUNA 1 comes as two rows: 10 + 15 of 3,000 + 3,000 households, and COMUNA 2's as it was.
        rows.Add(Row(Components, "01", "1", "COMUNA 1", 10, 3000));
        rows.Add(Row(Components, "01", "1", "COMUNA 1", 15, 3000));
        rows.Add(Row(Components, "01", "1", "COMUNA 2", 17, 4000));
        await using var h = await WithCrucesAsync(Declare("single", "categoria"), rows);

        var result = await h.QueryAsync(Card(null, Components, FourAnswers), "stat");

        Assert.Equal(0.9517, Value(result), 9);
        Assert.Equal(10000, Number(result.GetProperty("rows")[0], "denominator"));
    }

    [Fact]
    public async Task An_answer_missing_from_some_cells_gives_the_largest_per_answer_sum()
    {
        // Each answer is in one comuna only: the denominators are 6,000, 4,000, 6,000 and 4,000, and the largest is 6,000.
        var rows = CrucesRows((answer, comuna) => (answer, comuna) is ("1", "COMUNA 1") or ("2", "COMUNA 2") or ("3", "COMUNA 1") or ("4", "COMUNA 2"));
        await using var h = await WithCrucesAsync(Declare("single", "categoria"), rows);

        var result = await h.QueryAsync(Card(null, Components, FourAnswers), "stat");

        var row = result.GetProperty("rows")[0];
        Assert.Equal((25 + 319 + 2500 + 1817) / 6000.0, row.GetProperty("value").GetDouble(), 9);
        Assert.Equal(6000, Number(row, "denominator"));
        Assert.Equal(6000, Number(row, "n"));
    }

    [Fact]
    public async Task The_overall_value_of_a_reference_line_is_the_figure_of_the_tile()
    {
        await using var h = await WithCrucesAsync(Declare("single", "categoria"));

        var tile = await h.QueryAsync(Card(null, Components, FourAnswers), "stat");
        var columns = await h.QueryAsync(Card("comuna", Components, FourAnswers), "column", overall: true);

        Assert.Equal(Value(tile), Number(columns, "overall")!.Value, 12);
        Assert.Equal(0.9517, Number(columns, "overall")!.Value, 9);
    }

    [Fact]
    public async Task A_sort_by_value_follows_the_corrected_value_and_a_sort_by_a_column_still_orders_the_groups()
    {
        await using var h = await WithCrucesAsync(Declare("single", "categoria"));
        var byValue = new
        {
            indicator = "cruces_intencion", ratio = RatioOf, groupBy = "comuna", sort = new { by = "value", direction = "asc" },
            filters = new[] { Equals("variable", Components), In("categoria", FourAnswers) },
        };
        var byCode = new
        {
            indicator = "cruces_intencion", ratio = RatioOf, groupBy = "categoria", sort = new { by = "column", column = "orden", compareAs = "number", direction = "desc" },
            filters = new[] { Equals("variable", Components) },
        };

        var ascending = await h.QueryAsync(byValue, "column");
        var codes = await h.QueryAsync(byCode, "bar");

        // COMUNA 2 (94.8 %) is below COMUNA 1 (95.4 %); alphabetical order would put COMUNA 1 first.
        Assert.Equal(["COMUNA 2", "COMUNA 1"], ascending.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
        Assert.Equal(["4", "3", "2", "1"], codes.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()));
    }

    [Fact]
    public async Task The_rank_of_the_groups_of_a_series_widget_takes_the_corrected_value()
    {
        // X has four answers of 10 over 100 households, Y one answer of 20 over 100: 10 % and 20 % added up as separate bases, 40 % and 20 % counted once.
        var rows = string.Join(",\n",
            Row("Pregunta", "01", "1", "X", 10, 100), Row("Pregunta", "02", "2", "X", 10, 100), Row("Pregunta", "03", "3", "X", 10, 100), Row("Pregunta", "04", "4", "X", 10, 100),
            Row("Pregunta", "01", "1", "Y", 20, 100));
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("orden_prueba", Columns, rows, labels: Declare("single", "categoria"));
        await h.PublishAsync("orden_plano", Columns, rows);
        object Ranked(string indicator) => new { indicator, ratio = RatioOf, groupBy = "comuna", seriesBy = "categoria", sort = new { by = "value", direction = "asc" } };

        var declared = await h.QueryAsync(Ranked("orden_prueba"), "column");
        var plain = await h.QueryAsync(Ranked("orden_plano"), "column");

        string[] Groups(JsonElement result) => result.GetProperty("totals").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("group").GetString()!).ToArray();
        Assert.Equal(["Y", "X"], Groups(declared));
        Assert.Equal(["X", "Y"], Groups(plain));
        Assert.Equal([0.2, 0.4], declared.GetProperty("totals").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("value").GetDouble()).ToArray());
    }

    // teatro_intencion: one question, answered by every household, so the shares of a comuna add up to 100 %.

    private const string Intention = "Iría al teatro (I1)";

    private static readonly (string Orden, string Answer, int First, int Second, int Third)[] IntentionAnswers =
        [("1", "1. Sí", 2983, 2237, 2238), ("2", "2. Quizás", 429, 321, 321), ("3", "3. No", 496, 372, 371), ("4", "4. NS/NR", 92, 70, 70)];

    private static readonly string[] Comunas = ["COMUNA 1", "COMUNA 2", "COMUNA 3"];

    private static readonly int[] Households = [4000, 3000, 3000];

    private static string TeatroRows() =>
        string.Join(",\n", IntentionAnswers.SelectMany(a =>
            new[] { a.First, a.Second, a.Third }.Select((numerator, i) => Row(Intention, a.Orden, a.Answer, Comunas[i], numerator, Households[i]))));

    private async Task<IndicatorsHarness> WithTeatroAsync(object? declared)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_intencion", Columns, TeatroRows(), format: "percent", labels: declared);
        await h.PublishAsync("teatro_plano", Columns, TeatroRows(), format: "percent");
        return h;
    }

    [Fact]
    public async Task Bars_by_category_unchanged()
    {
        await using var h = await WithTeatroAsync(Declare("single", "categoria"));
        object Bars(string indicator) => new { indicator, ratio = RatioOf, groupBy = "categoria", filters = new[] { Equals("variable", Intention) } };

        var declared = await h.QueryAsync(Bars("teatro_intencion"), "bar");
        var undeclared = await h.QueryAsync(Bars("teatro_plano"), "bar");

        Assert.Equal(Rows(undeclared), Rows(declared));
        Assert.Equal(["1. Sí|0.7458", "2. Quizás|0.1071", "3. No|0.1239", "4. NS/NR|0.0232"], Rows(declared, "0.####"));
    }

    [Fact]
    public async Task Total_over_the_categories()
    {
        await using var h = await WithTeatroAsync(Declare("single", "categoria"));
        object ByComunaAndAnswer(string indicator) =>
            new { indicator, ratio = RatioOf, groupBy = "comuna", seriesBy = "categoria", filters = new[] { Equals("variable", Intention) } };

        var declared = await h.QueryAsync(ByComunaAndAnswer("teatro_intencion"), "column");
        var undeclared = await h.QueryAsync(ByComunaAndAnswer("teatro_plano"), "column");

        // Every comuna's total is its households counted once (100 %), not four times (25 %), and the cells are as they were.
        var groups = declared.GetProperty("totals").GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal([1.0, 1.0, 1.0], groups.Select(g => g.GetProperty("value").GetDouble()));
        Assert.Equal([4000.0, 3000.0, 3000.0], groups.Select(g => Number(g, "denominator")!.Value));
        Assert.Equal([4000.0, 3000.0, 3000.0], groups.Select(g => Number(g, "n")!.Value));
        Assert.Equal([0.25, 0.25, 0.25], undeclared.GetProperty("totals").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("value").GetDouble()));
        Assert.Equal(Rows(undeclared), Rows(declared));
        // The totals of the series and the grand total count the households once as well.
        var series = declared.GetProperty("totals").GetProperty("series").EnumerateArray().Select(s => Math.Round(s.GetProperty("value").GetDouble(), 4)).ToArray();
        Assert.Equal([0.7458, 0.1071, 0.1239, 0.0232], series);
        var all = declared.GetProperty("totals").GetProperty("all");
        Assert.Equal(1.0, all.GetProperty("value").GetDouble(), 12);
        Assert.Equal(10000, Number(all, "denominator"));
        Assert.Equal(10000, Number(declared, "n"));
        // Each cell's denominator is its comuna's households: one answer, one row.
        Assert.Equal([4000.0, 4000.0, 4000.0, 4000.0], declared.GetProperty("rows").EnumerateArray().Take(4).Select(r => Number(r, "denominator")!.Value));
    }

    [Fact]
    public async Task Group_filter_kept()
    {
        await using var h = await WithTeatroAsync(Declare("single", "categoria"));
        object Yes(string indicator) => new
        {
            indicator, ratio = RatioOf, groupBy = "comuna",
            filters = new[] { In("comuna", Comunas), Equals("variable", Intention), Equals("categoria", "1. Sí") },
        };

        var declared = await h.QueryAsync(Yes("teatro_intencion"), "column");
        var undeclared = await h.QueryAsync(Yes("teatro_plano"), "column");

        Assert.Equal(Rows(undeclared), Rows(declared));
        Assert.Equal([2983 / 4000.0, 2237 / 3000.0, 2238 / 3000.0], Values(declared));
    }

    // A map carries what its outer statement reads: the rows it combines, its geometry and its tooltip columns.

    private const string MapColumns =
        "block text, round text, address text, target bigint, progress numeric, visited_on date, seen_at timestamptz, ok boolean, numerator numeric, denominator numeric, geometry text";

    private static string MapRow(string block, string round, string address, int target, double progress, string visitedOn, string seenAt, string ok, int numerator, int denominator) =>
        $"('{block}', '{round}', '{address}', {target}, {F(progress)}, '{visitedOn}', '{seenAt}', {ok}, {numerator}, {denominator}, 'POINT(1 1)')";

    [Fact]
    public async Task A_map_over_one_answer_reads_what_it_read_before()
    {
        var rows = string.Join(",\n",
            MapRow("B1", "1", "Calle 1", 5, 0.5, "2026-03-01", "2026-03-01T10:00:00Z", "true", 4, 10),
            MapRow("B1", "1", "Calle 2", 7, 0.25, "2026-02-01", "2026-03-01T09:00:00Z", "false", 2, 10),
            MapRow("B1", "2", "Calle 3", 1, 0.1, "2026-01-01", "2026-03-02T09:00:00Z", "true", 9, 10),
            MapRow("B2", "1", "Otra", 3, 0.75, "2026-04-01", "2026-04-01T08:00:00Z", "true", 3, 10),
            MapRow("B2", "2", "Zeta", 2, 0.3, "2026-05-01", "2026-05-01T08:00:00Z", "true", 8, 10));
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("survey_declared", MapColumns, rows, labels: Declare("single", "round"));
        await h.PublishAsync("survey_plain", MapColumns, rows);
        object Map(string indicator) => new
        {
            indicator, ratio = RatioOf, groupBy = "block", geometryColumn = "geometry", filters = new[] { Equals("round", "1") },
            tooltip = new[]
            {
                new { column = "address", label = (string?)null, format = (string?)null }, new { column = "target", label = (string?)null, format = (string?)null },
                new { column = "visited_on", label = (string?)null, format = (string?)null },
                new { column = "seen_at", label = (string?)null, format = (string?)null }, new { column = "ok", label = (string?)null, format = (string?)null },
            },
        };

        var declared = await h.QueryAsync(Map("survey_declared"), "map");
        var plain = await h.QueryAsync(Map("survey_plain"), "map");

        Assert.Equal(plain.GetProperty("rows").ToString(), declared.GetProperty("rows").ToString());
        var first = declared.GetProperty("rows")[0];
        Assert.Equal("POINT(1 1)", first.GetProperty("geometry").GetString());
        Assert.Equal(2, first.GetProperty("rows").GetInt64());
        Assert.Equal(12, first.GetProperty("tooltip")[1].GetDouble());
        Assert.Equal("Calle 1", first.GetProperty("tooltip")[0].GetString());
        Assert.False(first.GetProperty("tooltip")[4].GetBoolean());
    }

    // escuelas_razones_no: a multiple-choice question, so the answers' shares add up to more than 100 % and adding them counts a respondent once
    // for each answer.

    private const string Reason = "Razón";

    private const string Time = "Falta de tiempo · acceso";

    private const string Interest = "No les interesa · interés";

    private const string Cost = "Costo";

    private static readonly (string Orden, string Answer, int First, int Second)[] Reasons =
        [("01", Time, 300, 110), ("02", Interest, 250, 120), ("03", Cost, 150, 60)];

    private static string ReasonRows(string? extra = null) =>
        string.Join(",\n", Reasons.SelectMany(r => new[] { Row(Reason, r.Orden, r.Answer, "COMUNA 1", r.First, 500), Row(Reason, r.Orden, r.Answer, "COMUNA 2", r.Second, 200) })
            .Concat(extra is null ? [] : [extra]));

    private async Task<IndicatorsHarness> WithReasonsAsync(object? declared, string? rows = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("escuelas_razones_no", Columns, rows ?? ReasonRows(), format: "percent", labels: declared);
        return h;
    }

    private static object ReasonCard(string? groupBy = null, string? seriesBy = null, string variable = Reason, object? answers = null, string indicator = "escuelas_razones_no") => new
    {
        indicator, ratio = RatioOf, groupBy, seriesBy,
        filters = answers is null ? new[] { Equals("variable", variable) } : new[] { Equals("variable", variable), answers },
    };

    [Fact]
    public async Task Card_over_two_reasons()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(ReasonCard(answers: In("categoria", Time, Interest)), "stat");

        Assert.Equal("categoria", Message(result));
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
        Assert.True(result.GetProperty("published").GetBoolean());
        Assert.False(result.GetProperty("missing").GetProperty("any").GetBoolean());
        Assert.False(result.TryGetProperty("unfiltered", out _));
        Assert.False(result.TryGetProperty("n", out _));
    }

    [Fact]
    public async Task One_answer_kept()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(ReasonCard(answers: Equals("categoria", Time)), "stat");

        Assert.Equal("-", Message(result));
        Assert.Equal(410 / 700.0, Value(result), 9);
    }

    [Fact]
    public async Task Bars_by_answer()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(ReasonCard(groupBy: "categoria"), "bar");

        Assert.Equal("-", Message(result));
        Assert.Equal(
            [$"{Cost}|0.3", $"{Time}|{F(Math.Round(410 / 700.0, 9))}", $"{Interest}|{F(Math.Round(370 / 700.0, 9))}"],
            result.GetProperty("rows").EnumerateArray().Select(r => $"{r.GetProperty("group").GetString()}|{F(Math.Round(r.GetProperty("value").GetDouble(), 9))}"));
    }

    [Fact]
    public async Task Question_with_one_answer_in_the_rows()
    {
        var rows = string.Join(",\n",
            Row("Sin dato de precio (E7)", "01", "Sin dato", "COMUNA 1", 20, 100), Row("Sin dato de precio (E7)", "01", "Sin dato", "COMUNA 2", 10, 100),
            Row("Medios (E3)", "01", "Radio", "COMUNA 1", 50, 100), Row("Medios (E3)", "02", "Redes", "COMUNA 1", 60, 100));
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", Columns, rows, format: "percent", labels: Declare("multiple", "categoria"));

        var single = await h.QueryAsync(new { indicator = "teatro_hoy", ratio = RatioOf, filters = new[] { Equals("variable", "Sin dato de precio (E7)") } }, "stat");
        var several = await h.QueryAsync(new { indicator = "teatro_hoy", ratio = RatioOf, filters = new[] { Equals("variable", "Medios (E3)") } }, "stat");

        Assert.Equal(0.15, Value(single), 9);
        Assert.Equal("-", Message(single));
        Assert.Equal("categoria", Message(several));
    }

    [Fact]
    public async Task Total_over_several_answers()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(ReasonCard(groupBy: "categoria", seriesBy: "comuna"), "heatmap");

        Assert.Equal("-", Message(result));
        // The cells and the total of each answer keep their values; the total of each comuna and the grand total add up the answers.
        Assert.Equal([0.3, 0.3, 0.5, 0.55, 0.6, 0.6], Values(result).Select(v => Math.Round(v, 9)).Order());
        var totals = result.GetProperty("totals");
        Assert.All(totals.GetProperty("groups").EnumerateArray(), g => Assert.NotEqual(JsonValueKind.Null, g.GetProperty("value").ValueKind));
        Assert.All(totals.GetProperty("series").EnumerateArray(), s => Assert.Equal(JsonValueKind.Null, s.GetProperty("value").ValueKind));
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("all").GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Answers_folded_into_Other()
    {
        // Twelve answers, the largest first: the seven largest are kept and the five others fold into "Other".
        var rows = string.Join(",\n", Enumerable.Range(1, 12).SelectMany(i => new[]
        {
            Row(Reason, i.ToString("00", CultureInfo.InvariantCulture), $"Razón {i:00}", "COMUNA 1", 130 - (10 * i), 1000),
            Row(Reason, i.ToString("00", CultureInfo.InvariantCulture), $"Razón {i:00}", "COMUNA 2", 120 - (10 * i), 1000),
        }));
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"), rows);

        var result = await h.QueryAsync(ReasonCard(groupBy: "comuna", seriesBy: "categoria"), "column");

        Assert.Equal("-", Message(result));
        var series = result.GetProperty("series").EnumerateArray().Select(s => s.GetProperty("kind").GetString()).ToArray();
        Assert.Equal(8, series.Length);
        Assert.Equal("other", series[^1]);
        // The seven answers it shows keep their values; "Other" has no cell, and no value in its total.
        var cells = result.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(14, cells.Length);
        Assert.DoesNotContain(cells, c => c.GetProperty("seriesKind").GetString() == "other");
        Assert.Equal(0.12, cells.First(c => c.GetProperty("group").GetString() == "COMUNA 1").GetProperty("value").GetDouble(), 9);
        var totals = result.GetProperty("totals");
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("series")[7].GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Number, totals.GetProperty("series")[0].GetProperty("value").ValueKind);
        Assert.All(totals.GetProperty("groups").EnumerateArray(), g => Assert.Equal(JsonValueKind.Null, g.GetProperty("value").ValueKind));
    }

    [Fact]
    public async Task A_dashboard_filter_that_keeps_one_answer_lets_the_tile_draw()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));
        var tile = ReasonCard();

        var refused = await h.QueryAsync(tile, "stat");
        var drawn = await h.QueryAsync(tile, "stat", dashboardFilters: new[] { new { column = "categoria", kind = "text", values = new[] { Time } } });

        Assert.Equal("categoria", Message(refused));
        Assert.Equal("-", Message(drawn));
        Assert.Equal(410 / 700.0, Value(drawn), 9);
    }

    [Fact]
    public async Task A_tile_whose_value_without_filters_spans_two_answers_gets_no_unfiltered()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(
            ReasonCard(), "stat", dashboardFilters: new[] { new { column = "categoria", kind = "text", values = new[] { Time } } }, unfiltered: true);

        Assert.Equal(410 / 700.0, Value(result), 9);
        Assert.False(result.TryGetProperty("unfiltered", out _));
    }

    [Fact]
    public async Task The_value_without_filters_of_a_tile_over_one_answer_is_still_given()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(
            ReasonCard(answers: Equals("categoria", Time)), "stat", dashboardFilters: new[] { new { column = "comuna", kind = "text", values = new[] { "COMUNA 1" } } },
            unfiltered: true);

        Assert.Equal(0.6, Value(result), 9);
        Assert.Equal(410 / 700.0, Number(result, "unfiltered")!.Value, 9);
    }

    [Fact]
    public async Task The_overall_value_of_a_line_over_several_answers_has_no_value()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(ReasonCard(groupBy: "categoria"), "column", overall: true);

        Assert.Equal(3, result.GetProperty("rows").GetArrayLength());
        Assert.False(result.TryGetProperty("overall", out _));
    }

    [Fact]
    public async Task The_group_order_of_a_series_widget_is_the_same_with_and_without_the_declaration()
    {
        var rows = ReasonRows();
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"), rows);
        await h.PublishAsync("razones_plano", Columns, rows, format: "percent");
        object Sorted(string indicator) => new
        {
            indicator, ratio = RatioOf, groupBy = "comuna", seriesBy = "categoria", sort = new { by = "value", direction = "desc" }, filters = new[] { Equals("variable", Reason) },
        };

        var declared = await h.QueryAsync(Sorted("escuelas_razones_no"), "column");
        var undeclared = await h.QueryAsync(Sorted("razones_plano"), "column");

        string[] Groups(JsonElement result) => result.GetProperty("totals").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("group").GetString()!).ToArray();
        // 48.3 % of COMUNA 2 against 46.7 % of COMUNA 1: the order the sort asks for is not the alphabetical one.
        Assert.Equal(["COMUNA 2", "COMUNA 1"], Groups(undeclared));
        Assert.Equal(Groups(undeclared), Groups(declared));
        Assert.Equal(undeclared.GetProperty("rows").ToString(), declared.GetProperty("rows").ToString());
    }

    // Which answers a widget adds up, with the question, the code and the answer declared

    [Fact]
    public async Task A_bar_chart_grouped_by_answer_passes_the_check_and_one_grouped_by_question_does_not()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "variable", "orden", "categoria"));

        var byAnswer = await h.QueryAsync(ReasonCard(groupBy: "categoria"), "bar");
        var byQuestion = await h.QueryAsync(ReasonCard(groupBy: "variable"), "bar");

        Assert.Equal("-", Message(byAnswer));
        Assert.Equal(3, byAnswer.GetProperty("rows").GetArrayLength());
        // "variable" holds one value, "orden" and "categoria" three: a tie, and the last column of the table is named.
        Assert.Equal("categoria", Message(byQuestion));
        Assert.Empty(byQuestion.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task A_tile_over_a_code_and_an_answer_in_two_values_names_the_answer_column()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "variable", "orden", "categoria"));
        var config = new
        {
            indicator = "escuelas_razones_no", ratio = RatioOf,
            filters = new[] { Equals("variable", Reason), In("orden", "01", "02"), In("categoria", Time, Interest) },
        };

        var result = await h.QueryAsync(config, "stat");

        Assert.Equal("categoria", Message(result));
    }

    [Fact]
    public async Task Mixed_kinds_are_taken_as_multiple_choice()
    {
        var declared = new Dictionary<string, object> { ["orden"] = new { categories = "single" }, ["categoria"] = new { categories = "multiple" } };
        await using var h = await WithReasonsAsync(declared);

        var result = await h.QueryAsync(ReasonCard(answers: In("categoria", Time, Interest)), "stat");

        Assert.Equal("categoria", Message(result));
    }

    [Fact]
    public async Task A_measure_is_never_refused_or_corrected()
    {
        var rows = string.Join(",\n", Reasons.Select(r => Row(Reason, r.Orden, r.Answer, "COMUNA 1", r.First, 500)));
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"), rows);

        var result = await h.QueryAsync(
            new { indicator = "escuelas_razones_no", measure = "numerator", filters = new[] { Equals("variable", Reason) } }, "stat");

        Assert.Equal("-", Message(result));
        Assert.Equal(700, Value(result));
    }

    [Fact]
    public async Task A_stat_tile_with_a_statistic_is_never_refused_or_corrected()
    {
        await using var h = await WithReasonsAsync(Declare("multiple", "categoria"));

        var result = await h.QueryAsync(
            new { indicator = "escuelas_razones_no", ratio = RatioOf, statistic = "median", filters = new[] { Equals("variable", Reason) } }, "stat");

        Assert.Equal("-", Message(result));
        Assert.Equal(0.525, Value(result), 9);
    }
}
