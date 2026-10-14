using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary><c>POST /projects/{id}/data/{source}/{table}/query</c>: pages, sorting and filters of a published table.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataQueryTests(PostgresFixture postgres)
{
    private const string PersonColumns = "name text, municipality text, age bigint, income numeric, consent boolean, born date, at timestamptz, note text";

    // name, municipality, age, income, consent, born, at, note
    private const string PersonRows = """
        ('Ana',      'Cali',    17, 10.5, true,  '2026-08-31', '2026-09-01 04:59:59+00', NULL),
        ('Beto',     'Cali',    18, 20,   false, '2026-09-01', '2026-09-01 05:00:00+00', ''),
        ('Carla',    'Palmira', 29, NULL, NULL,  '2026-09-02', '2026-09-02 02:30:00+00', 'x'),
        ('Dani',     'Cali',    NULL, 7.25, false, NULL,       NULL,                     NULL),
        ('Eve WELL', 'Palmira', 40, 1,    true,  '2026-09-04', '2026-09-03 23:30:00-05', 'y')
        """;

    private static async Task<CleanDataHarness> WithPersonsAsync(PostgresFixture postgres, string? timeZone = null, Dictionary<string, string?>? settings = null)
    {
        var h = await CleanDataHarness.CreateAsync(postgres, timeZone, settings);
        await h.PublishAsync("output", ("persons", PersonColumns, PersonRows));
        return h;
    }

    private static string QueryUrl(CleanDataHarness h, string source = "output", string table = "persons") =>
        $"{h.Url}/{source}/{Uri.EscapeDataString(table)}/query";

    private static async Task<HttpResponseMessage> PostAsync(CleanDataHarness h, object? body = null, HttpClient? client = null, string source = "output", string table = "persons") =>
        await (client ?? h.Viewer).PostAsJsonAsync(QueryUrl(h, source, table), body ?? new { });

    private static async Task<JsonElement> QueryAsync(CleanDataHarness h, object? body = null, string source = "output", string table = "persons")
    {
        var response = await PostAsync(h, body, source: source, table: table);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string?[] Names(JsonElement page) =>
        page.GetProperty("rows").EnumerateArray().Select(r => r[0].GetString()).ToArray();

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    // Viewer opens the dashboard / Not a member

    [Fact]
    public async Task A_viewer_gets_a_page_with_columns_rows_total_and_where_it_was_published()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h);

        Assert.Equal(["name", "municipality", "age", "income", "consent", "born", "at", "note"],
            page.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(["text", "text", "integer", "decimal", "boolean", "date", "timestamp", "text"],
            page.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
        Assert.Equal(["Ana", "Beto", "Carla", "Dani", "Eve WELL"], Names(page));
        Assert.Equal((5, 1, 50, "UTC"), (page.GetProperty("total").GetInt32(), page.GetProperty("page").GetInt32(),
            page.GetProperty("pageSize").GetInt32(), page.GetProperty("timeZone").GetString()));
        Assert.Equal(JsonValueKind.String, page.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, page.GetProperty("runId").ValueKind);
    }

    [Fact]
    public async Task Values_come_as_numbers_booleans_days_and_text_with_empty_values_as_null()
    {
        await using var h = await WithPersonsAsync(postgres);

        var carla = (await QueryAsync(h)).GetProperty("rows")[2];
        var dani = (await QueryAsync(h)).GetProperty("rows")[3];

        Assert.Equal("Carla", carla[0].GetString());
        Assert.Equal(29, carla[2].GetInt64());
        Assert.Equal(JsonValueKind.Null, carla[3].ValueKind);
        Assert.Equal(JsonValueKind.Null, carla[4].ValueKind);
        Assert.Equal("2026-09-02", carla[5].GetString());
        Assert.Equal(7.25m, dani[3].GetDecimal());
        Assert.Equal(JsonValueKind.Null, dani[2].ValueKind);
        Assert.Equal(JsonValueKind.Null, dani[5].ValueKind);
    }

    [Fact]
    public async Task Every_role_may_query_and_a_non_member_gets_404()
    {
        await using var h = await WithPersonsAsync(postgres);
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, client: h.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, client: h.Analyst)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, client: h.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(h, client: stranger)).StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_body_reads_the_first_page()
    {
        await using var h = await WithPersonsAsync(postgres);

        var response = await h.Viewer.PostAsync(QueryUrl(h), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Names that are not published tables

    [Theory]
    [InlineData("output", "salaries")]
    [InlineData("output", "persons\"; DROP SCHEMA public CASCADE; --")]
    [InlineData("output", "persons; select 1")]
    [InlineData("output", "per sons")]
    [InlineData("output", "PERSONS")]
    [InlineData("clean", "persons")]
    [InlineData("raw", "persons")]
    [InlineData("indicators", "persons")]
    [InlineData("output; DROP SCHEMA public", "persons")]
    [InlineData("OUTPUT", "persons")]
    public async Task A_source_or_table_that_is_not_published_is_404_with_its_code(string source, string table)
    {
        await using var h = await WithPersonsAsync(postgres);

        var response = await PostAsync(h, source: source, table: table);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("data.tableNotFound", problem.GetProperty("code").GetString());
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    [Fact]
    public async Task A_view_and_another_projects_table_are_404()
    {
        await using var h = await WithPersonsAsync(postgres);
        var schema = h.Schema("output");
        await h.ExecuteAsync($"CREATE VIEW \"{schema}\".everyone AS SELECT name FROM \"{schema}\".persons");
        var other = Guid.NewGuid();
        await h.ExecuteAsync($"CREATE SCHEMA \"p_{other:N}_output\"; CREATE TABLE \"p_{other:N}_output\".secret (a bigint);");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(h, table: "everyone")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(h, table: "secret")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(h, table: $"p_{other:N}_output.secret")).StatusCode);
    }

    // Paging

    [Fact]
    public async Task The_second_page_of_120_rows_at_50_per_page_holds_rows_51_to_100()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("numbers", "n bigint", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".numbers SELECT g FROM generate_series(1, 120) g");

        var page = await QueryAsync(h, new { page = 2, pageSize = 50 }, table: "numbers");

        var values = page.GetProperty("rows").EnumerateArray().Select(r => r[0].GetInt64()).ToArray();
        Assert.Equal(Enumerable.Range(51, 50).Select(n => (long)n), values);
        Assert.Equal(120, page.GetProperty("total").GetInt64());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task The_last_page_may_be_short_and_a_page_past_the_end_is_empty()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("numbers", "n bigint", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".numbers SELECT g FROM generate_series(1, 120) g");

        var last = await QueryAsync(h, new { page = 3, pageSize = 50 }, table: "numbers");
        var beyond = await QueryAsync(h, new { page = 9, pageSize = 50 }, table: "numbers");

        Assert.Equal(20, last.GetProperty("rows").GetArrayLength());
        Assert.Equal(0, beyond.GetProperty("rows").GetArrayLength());
        Assert.Equal(120, beyond.GetProperty("total").GetInt64());
    }

    [Theory]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(51)]
    public async Task An_invalid_page_size_is_a_validation_error(int pageSize)
    {
        await using var h = await WithPersonsAsync(postgres);

        var response = await PostAsync(h, new { pageSize });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("25, 50, 100", Errors(problem, "pageSize")[0]);
    }

    // Columns

    [Fact]
    public async Task A_column_comment_is_the_questions_label_and_a_column_without_one_has_none()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "has_water text, _id bigint", "('Yes', 1)"));
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "clean", "main", "has_water", "Does the household have running water?");

        var page = await QueryAsync(h, source: "clean", table: "main");

        var columns = page.GetProperty("columns").EnumerateArray().ToArray();
        Assert.Equal("Does the household have running water?", columns[0].GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, columns[1].GetProperty("label").ValueKind);
    }

    [Fact]
    public async Task A_script_table_shows_no_labels()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h);

        Assert.All(page.GetProperty("columns").EnumerateArray(), c => Assert.Equal(JsonValueKind.Null, c.GetProperty("label").ValueKind));
    }

    [Fact]
    public async Task Text_over_500_characters_is_cut_and_marked()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "id bigint, comment text", "(1, 'short')"));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes VALUES (2, repeat('é', 2000)), (3, repeat('z', 500)), (4, repeat('z', 501))");

        var page = await QueryAsync(h, table: "notes");

        var comments = page.GetProperty("rows").EnumerateArray().Select(r => r[1].GetString()!).ToArray();
        Assert.Equal(["short", new string('é', 500), new string('z', 500), new string('z', 500)], comments);
        Assert.Equal([[1, 1], [3, 1]], page.GetProperty("cutCells").EnumerateArray().Select(c => c.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray());
    }

    [Fact]
    public async Task A_cut_never_splits_a_character_outside_the_basic_plane()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "comment text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes VALUES (repeat('😀', 600))");

        var page = await QueryAsync(h, table: "notes");

        var text = page.GetProperty("rows")[0][0].GetString()!;
        Assert.Equal(500, text.EnumerateRunes().Count());
        Assert.Equal(1, page.GetProperty("cutCells").GetArrayLength());
    }

    [Fact]
    public async Task Numbers_beyond_what_a_decimal_holds_come_as_text()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("n", "big numeric, ratio double precision", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".n VALUES (1e40, 'NaN'), (12345678901234567890.123, 0.1)");

        var rows = (await QueryAsync(h, table: "n")).GetProperty("rows");

        Assert.Equal(JsonValueKind.String, rows[0][0].ValueKind);
        Assert.Equal("NaN", rows[0][1].GetString());
        Assert.Equal(12345678901234567890.123m, rows[1][0].GetDecimal());
        Assert.Equal(0.1m, rows[1][1].GetDecimal());
    }

    // Sorting

    [Fact]
    public async Task Sorting_by_age_descending_puts_the_oldest_first_and_rows_without_age_last()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { sort = new { column = "age", direction = "desc" } });

        Assert.Equal(["Eve WELL", "Carla", "Beto", "Ana", "Dani"], Names(page));
    }

    [Fact]
    public async Task Sorting_ascending_also_puts_rows_without_the_value_last()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { sort = new { column = "age", direction = "asc" } });

        Assert.Equal(["Ana", "Beto", "Carla", "Eve WELL", "Dani"], Names(page));
    }

    [Fact]
    public async Task Without_a_sort_rows_keep_the_order_they_were_written_in()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("t", "name text", "('zeta'), ('alpha'), ('mu'), ('beta')"));

        var page = await QueryAsync(h, table: "t");

        Assert.Equal(["zeta", "alpha", "mu", "beta"], Names(page));
    }

    [Fact]
    public async Task Rows_with_equal_sort_values_keep_one_order_across_pages()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("t", "grp bigint, n bigint", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".t SELECT g % 2, g FROM generate_series(1, 120) g");

        var seen = new List<long>();
        foreach (var page in new[] { 1, 2, 3 })
        {
            var result = await QueryAsync(h, new { page, pageSize = 50, sort = new { column = "grp", direction = "asc" } }, table: "t");
            seen.AddRange(result.GetProperty("rows").EnumerateArray().Select(r => r[1].GetInt64()));
        }

        Assert.Equal(120, seen.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 120).Where(n => n % 2 == 0).Concat(Enumerable.Range(1, 120).Where(n => n % 2 == 1)).Select(n => (long)n), seen);
    }

    // Filters

    [Fact]
    public async Task Two_filters_both_have_to_match_and_the_total_counts_the_matches()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new
        {
            filters = new object[]
            {
                new { column = "municipality", op = "equals", value = "Cali" },
                new { column = "age", op = "range", min = "18", max = "29" },
            },
        });

        Assert.Equal(["Beto"], Names(page));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Contains_ignores_case()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { filters = new[] { new { column = "name", op = "contains", value = "well" } } });

        Assert.Equal(["Eve WELL"], Names(page));
    }

    [Fact]
    public async Task A_decimal_range_with_only_a_minimum_includes_it()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { filters = new[] { new { column = "income", op = "range", min = "10.5", max = (string?)null } } });

        Assert.Equal(["Ana", "Beto"], Names(page));
    }

    [Fact]
    public async Task A_timestamp_range_is_whole_days_in_the_projects_zone_including_the_last_day()
    {
        await using var h = await WithPersonsAsync(postgres, timeZone: "America/Bogota");

        // Bogota's 2026-09-01 runs from 05:00 UTC to 05:00 UTC the next day.
        var firstDay = await QueryAsync(h, new { filters = new[] { new { column = "at", op = "range", min = "2026-09-01", max = "2026-09-01" } } });
        var lateEvening = await QueryAsync(h, new { filters = new[] { new { column = "at", op = "range", min = "2026-09-03", max = "2026-09-03" } } });

        Assert.Equal(["Beto", "Carla"], Names(firstDay));
        Assert.Equal(["Eve WELL"], Names(lateEvening)); // 23:30 local time
    }

    [Fact]
    public async Task Empty_finds_rows_without_a_value_and_rows_with_an_empty_text()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { filters = new[] { new { column = "note", op = "empty" } } });
        var ages = await QueryAsync(h, new { filters = new[] { new { column = "age", op = "empty" } } });

        Assert.Equal(["Ana", "Beto", "Dani"], Names(page));
        Assert.Equal(["Dani"], Names(ages));
    }

    [Fact]
    public async Task A_true_false_filter_picks_the_chosen_values()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { filters = new[] { new { column = "consent", op = "in", values = new[] { "true" } } } });

        Assert.Equal(["Ana", "Eve WELL"], Names(page));
    }

    [Fact]
    public async Task Text_in_picks_any_of_the_values_and_the_total_follows_the_filter_not_the_page()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new
        {
            pageSize = 25,
            filters = new[] { new { column = "municipality", op = "in", values = new[] { "Palmira" } } },
        });

        Assert.Equal(["Carla", "Eve WELL"], Names(page));
        Assert.Equal(2, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_filter_value_that_looks_like_sql_is_only_ever_text()
    {
        await using var h = await WithPersonsAsync(postgres);

        var page = await QueryAsync(h, new { filters = new[] { new { column = "name", op = "equals", value = "x' OR '1'='1" } } });

        Assert.Empty(Names(page));
        Assert.Equal(0, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_is_a_validation_error_naming_the_column()
    {
        await using var h = await WithPersonsAsync(postgres);

        var response = await PostAsync(h, new { filters = new[] { new { column = "age", op = "range", min = "adult", max = (string?)null } } });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'age'", Errors(problem, "filters[0]")[0]);
    }

    [Fact]
    public async Task A_filter_or_sort_on_a_column_the_table_does_not_have_is_a_validation_error()
    {
        await using var h = await WithPersonsAsync(postgres);

        var filter = await PostAsync(h, new { filters = new[] { new { column = "salary", op = "equals", value = "1" } } });
        var sort = await PostAsync(h, new { sort = new { column = "salary", direction = "asc" } });

        Assert.Equal(HttpStatusCode.BadRequest, filter.StatusCode);
        Assert.Contains("'salary'", Errors(await filter.Content.ReadFromJsonAsync<JsonElement>(), "filters[0]")[0]);
        Assert.Equal(HttpStatusCode.BadRequest, sort.StatusCode);
        Assert.Contains("'salary'", Errors(await sort.Content.ReadFromJsonAsync<JsonElement>(), "sort")[0]);
    }

    [Fact]
    public async Task A_reversed_range_is_a_validation_error()
    {
        await using var h = await WithPersonsAsync(postgres);

        var response = await PostAsync(h, new { filters = new[] { new { column = "age", op = "range", min = "60", max = "18" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Timestamps in the project's zone

    [Fact]
    public async Task Timestamps_are_shown_in_the_projects_zone_with_their_offset()
    {
        await using var h = await WithPersonsAsync(postgres, timeZone: "America/Bogota");

        var page = await QueryAsync(h);

        var carla = page.GetProperty("rows")[2];
        Assert.Equal("2026-09-01T21:30:00-05:00", carla[6].GetString()); // 2026-09-02 02:30 UTC
        Assert.Equal("America/Bogota", page.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Without_a_zone_timestamps_are_in_utc()
    {
        await using var h = await WithPersonsAsync(postgres);

        var carla = (await QueryAsync(h)).GetProperty("rows")[2];

        Assert.Equal("2026-09-02T02:30:00+00:00", carla[6].GetString());
    }

    // Time limits

    [Fact]
    public async Task A_page_that_takes_too_long_is_503_and_the_project_answers_normally_afterwards()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres, settings: new()
        {
            ["DataDashboard:TestStatementSleep"] = "00:00:03",
            ["DataDashboard:StatementTimeout"] = "00:00:00.500",
        });
        await h.PublishAsync("output", ("persons", PersonColumns, PersonRows));

        var response = await PostAsync(h);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("data.timeout", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await h.Viewer.GetAsync(h.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Viewer.GetAsync($"/api/v1/projects/{h.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task A_table_locked_by_a_long_reader_or_a_swap_gives_503_after_the_lock_timeout_instead_of_hanging()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres, settings: new() { ["DataDashboard:LockTimeout"] = "00:00:00.500" });
        await h.PublishAsync("output", ("persons", PersonColumns, PersonRows));

        await using var holder = new NpgsqlConnection(h.Db);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var lockTable = new NpgsqlCommand($"LOCK TABLE \"{h.Schema("output")}\".persons IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await lockTable.ExecuteNonQueryAsync();
        }

        var started = DateTime.UtcNow;
        var response = await PostAsync(h);
        var waited = DateTime.UtcNow - started;
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("data.timeout", problem.GetProperty("code").GetString());
        Assert.True(waited < TimeSpan.FromSeconds(10), $"waited {waited}");

        await transaction.RollbackAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h)).StatusCode);
    }
}
