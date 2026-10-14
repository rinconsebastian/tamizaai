using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;
using static Tamiza.Api.IntegrationTests.Performance.PerformanceHarness;

namespace Tamiza.Api.IntegrationTests.Performance;

/// <summary>
/// The dashboard's submissions, days, range, completion time and enumerators (tasks 3.1), with rows written straight into
/// <c>raw_submissions</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PerformanceDashboardTests(PostgresFixture postgres)
{
    private const string Minutes20 = "2026-09-01T08:20:00.000Z";

    private static List<(string Day, long Count)> Days(JsonElement dashboard) =>
        dashboard.GetProperty("submissions").GetProperty("days").EnumerateArray()
            .Select(d => (d.GetProperty("day").GetString()!, d.GetProperty("count").GetInt64())).ToList();

    private static JsonElement Completion(JsonElement dashboard) => dashboard.GetProperty("completionTime");

    private static List<JsonElement> Enumerators(JsonElement dashboard) =>
        dashboard.GetProperty("enumerators").GetProperty("rows").EnumerateArray().ToList();

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static (string, string?)[] Times(string start, string end, params (string, string?)[] more) =>
        [("start", start), ("end", end), .. more];

    // Viewer opens a project

    [Fact]
    public async Task A_viewer_gets_the_dashboard_with_its_sections()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddManyAsync(3, Doc(("username", "ana")));

        var response = await h.RawGetAsync();
        var dashboard = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.String, dashboard.GetProperty("computedAt").ValueKind);
        Assert.Equal("UTC", dashboard.GetProperty("timeZone").GetString());
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("to").ValueKind);
        Assert.Equal(["dailyEvolution", "completionTime", "perEnumerator", "coverage"],
            dashboard.GetProperty("availability").EnumerateArray().Select(m => m.GetProperty("metric").GetString()));
        Assert.Equal(3, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal("notConfigured", dashboard.GetProperty("coverage").GetProperty("state").GetString());
    }

    // Not a member

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404()
    {
        await using var h = await CreateAsync(postgres);
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");

        var response = await h.RawGetAsync(client: stranger);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_project_gets_404()
    {
        await using var h = await CreateAsync(postgres);

        var response = await h.Viewer.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/performance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Deleted submissions

    [Fact]
    public async Task Deleted_submissions_are_left_out_of_every_part()
    {
        await using var h = await CreateAsync(postgres);
        Assert.Equal(HttpStatusCode.OK, (await h.SaveFrameAsync(Frame(["municipality"], (["cali"], 100)))).StatusCode);
        var answers = Times("2026-09-01T08:00:00.000Z", Minutes20, ("username", "ana"), ("municipality", "cali"));
        await h.AddManyAsync(97, Doc(answers));
        await h.AddManyAsync(3, Doc(answers), deleted: true);

        var dashboard = await h.GetAsync();

        Assert.Equal(97, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal([("2026-09-01", 97L)], Days(dashboard));
        Assert.Equal(97, Completion(dashboard).GetProperty("usableDurations").GetInt64());
        Assert.Equal(97, Enumerators(dashboard).Single().GetProperty("submissions").GetInt64());
        Assert.Equal(97, dashboard.GetProperty("coverage").GetProperty("rows")[0].GetProperty("achieved").GetInt64());
    }

    // Independent of the scripts, Kobo unreachable

    [Fact]
    public async Task The_dashboard_does_not_depend_on_scripts_runs_or_on_Kobo()
    {
        await using var h = await CreateAsync(postgres);
        // The project points at a Kobo server that does not answer, has no scripts, and its last run failed.
        await h.ExecuteAsync("UPDATE tamiza.projects SET kobo_server_url = 'https://kobo.invalid'");
        await h.ExecuteAsync(
            "INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, finished_at, error_code, error) VALUES (@id, @p, 'manual', 'failed', now(), now(), 'run.failed', 'boom')",
            ("id", Guid.NewGuid()), ("p", h.ProjectId));
        await h.AddManyAsync(4, Doc(("username", "ana")));

        var dashboard = await h.GetAsync();

        Assert.Equal(4, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    // Days in the project's time zone

    [Fact]
    public async Task An_evening_submission_belongs_to_the_day_in_the_project_time_zone()
    {
        await using var h = await CreateAsync(postgres, timeZone: "America/Bogota");
        await h.AddAsync(Doc(("username", "ana")), "2026-09-02T02:30:00Z");

        var dashboard = await h.GetAsync();

        Assert.Equal("America/Bogota", dashboard.GetProperty("timeZone").GetString());
        Assert.Equal([("2026-09-01", 1L)], Days(dashboard));
    }

    [Fact]
    public async Task Without_a_time_zone_days_are_counted_in_UTC()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(("username", "ana")), "2026-09-02T02:30:00Z");

        var dashboard = await h.GetAsync();

        Assert.Equal("UTC", dashboard.GetProperty("timeZone").GetString());
        Assert.Equal([("2026-09-02", 1L)], Days(dashboard));
    }

    [Fact]
    public async Task A_submission_without_reception_time_is_counted_but_belongs_to_no_day()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(("username", "ana")), "2026-09-01T10:00:00Z");
        await h.AddManyAsync(2, Doc(("username", "ben")), submittedAt: null);

        var all = await h.GetAsync();
        var ranged = await h.GetAsync("?from=2026-09-01&to=2026-09-01");

        var submissions = all.GetProperty("submissions");
        Assert.Equal(3, submissions.GetProperty("total").GetInt64());
        Assert.Equal(2, submissions.GetProperty("withoutReceptionTime").GetInt64());
        Assert.Equal([("2026-09-01", 1L)], Days(all));
        Assert.Equal(["ana", "ben"], Enumerators(all).Select(e => e.GetProperty("name").GetString()).Order());
        Assert.Equal(1, ranged.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal(0, ranged.GetProperty("submissions").GetProperty("withoutReceptionTime").GetInt64());
        Assert.Equal(["ana"], Enumerators(ranged).Select(e => e.GetProperty("name").GetString()));
    }

    // Date range

    [Fact]
    public async Task A_range_applies_to_every_part_and_counts_the_days_of_the_project_time_zone()
    {
        await using var h = await CreateAsync(postgres, timeZone: "America/Bogota");
        Assert.Equal(HttpStatusCode.OK, (await h.SaveFrameAsync(Frame(["municipality"], (["cali"], 100)))).StatusCode);
        string Answers(string who) => Doc(Times("2026-09-01T08:00:00.000Z", Minutes20, ("username", who), ("municipality", "cali")));

        // Bogotá is UTC-5: the range 09-01..09-07 runs from 09-01 05:00Z up to, not including, 09-08 05:00Z.
        await h.AddAsync(Answers("early"), "2026-09-01T04:59:00Z");
        await h.AddAsync(Answers("first"), "2026-09-01T05:00:00Z");
        await h.AddAsync(Answers("last"), "2026-09-08T04:59:00Z");
        await h.AddAsync(Answers("late"), "2026-09-08T05:00:00Z");
        await h.AddAsync(Answers("deleted"), "2026-09-03T12:00:00Z", deleted: true);
        await h.AddAsync(Answers("no time"), null);

        var dashboard = await h.GetAsync("?from=2026-09-01&to=2026-09-07");

        Assert.Equal("2026-09-01", dashboard.GetProperty("from").GetString());
        Assert.Equal("2026-09-07", dashboard.GetProperty("to").GetString());
        var submissions = dashboard.GetProperty("submissions");
        Assert.Equal(2, submissions.GetProperty("total").GetInt64());
        Assert.Equal(0, submissions.GetProperty("withoutReceptionTime").GetInt64());
        Assert.Equal(("2026-09-01", 1L), Days(dashboard).First());
        Assert.Equal(("2026-09-07", 1L), Days(dashboard).Last());
        Assert.Equal(2, Completion(dashboard).GetProperty("usableDurations").GetInt64());
        Assert.Equal(["first", "last"], Enumerators(dashboard).Select(e => e.GetProperty("name").GetString()).Order());
        var coverage = dashboard.GetProperty("coverage");
        Assert.Equal(2, coverage.GetProperty("rows")[0].GetProperty("achieved").GetInt64());
        Assert.Equal(0, coverage.GetProperty("outsideFrame").GetInt64());
    }

    [Fact]
    public async Task A_reversed_range_is_rejected_on_the_first_day()
    {
        await using var h = await CreateAsync(postgres);

        var response = await h.RawGetAsync("?from=2026-09-07&to=2026-09-01");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("first day is after the last", Errors(problem, "from").Single());
    }

    [Theory]
    [InlineData("?from=yesterday", "from")]
    [InlineData("?to=2026-13-01", "to")]
    [InlineData("?from=2026-02-30", "from")]
    [InlineData("?to=01/09/2026", "to")]
    public async Task A_date_that_does_not_parse_is_rejected_on_its_field(string query, string field)
    {
        await using var h = await CreateAsync(postgres);

        var response = await h.RawGetAsync(query);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("YYYY-MM-DD", Errors(problem, field).Single());
    }

    [Fact]
    public async Task A_range_with_only_a_first_day_runs_from_the_first_submission_after_it()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(), "2026-09-03T10:00:00Z");
        await h.AddAsync(Doc(), "2026-09-06T10:00:00Z");
        await h.AddAsync(Doc(), "2026-09-09T10:00:00Z");

        var dashboard = await h.GetAsync("?from=2026-09-05");

        Assert.Equal([("2026-09-06", 1L), ("2026-09-07", 0L), ("2026-09-08", 0L), ("2026-09-09", 1L)], Days(dashboard));
        Assert.Equal("2026-09-05", dashboard.GetProperty("from").GetString());
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("to").ValueKind);
    }

    [Fact]
    public async Task A_range_with_only_a_last_day_ends_at_the_last_submission_before_it()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(), "2026-09-03T10:00:00Z");
        await h.AddAsync(Doc(), "2026-09-06T10:00:00Z");
        await h.AddAsync(Doc(), "2026-09-09T10:00:00Z");

        var dashboard = await h.GetAsync("?to=2026-09-06");

        Assert.Equal([("2026-09-03", 1L), ("2026-09-04", 0L), ("2026-09-05", 0L), ("2026-09-06", 1L)], Days(dashboard));
    }

    [Fact]
    public async Task A_range_from_year_1_to_year_9999_returns_only_the_days_with_data_between()
    {
        await using var h = await CreateAsync(postgres, timeZone: "Asia/Tokyo");
        await h.AddAsync(Doc(), "2026-09-01T10:00:00Z");
        await h.AddAsync(Doc(), "2026-09-03T10:00:00Z");

        var dashboard = await h.GetAsync("?from=0001-01-01&to=9999-12-31");

        Assert.Equal([("2026-09-01", 1L), ("2026-09-02", 0L), ("2026-09-03", 1L)], Days(dashboard));
        Assert.Equal("0001-01-01", dashboard.GetProperty("from").GetString());
        Assert.Equal("9999-12-31", dashboard.GetProperty("to").GetString());
    }

    // Daily counts

    [Fact]
    public async Task Days_without_submissions_between_the_first_and_the_last_are_zero()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddManyAsync(10, Doc(), "2026-09-01T10:00:00Z");
        await h.AddManyAsync(4, Doc(), "2026-09-03T10:00:00Z");

        var dashboard = await h.GetAsync();

        Assert.Equal(14, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal([("2026-09-01", 10L), ("2026-09-02", 0L), ("2026-09-03", 4L)], Days(dashboard));
    }

    [Fact]
    public async Task A_project_without_submissions_has_empty_figures()
    {
        await using var h = await CreateAsync(postgres);

        var dashboard = await h.GetAsync();

        Assert.Equal(0, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Empty(Days(dashboard));
        Assert.Equal(0, Completion(dashboard).GetProperty("usableDurations").GetInt64());
        Assert.Equal(JsonValueKind.Null, Completion(dashboard).GetProperty("medianSeconds").ValueKind);
        Assert.Empty(Enumerators(dashboard));
    }

    // Completion time

    [Fact]
    public async Task Outliers_are_left_out_of_the_mean()
    {
        await using var h = await CreateAsync(postgres);
        foreach (var minutes in new[] { 20, 21, 22, 23, 25, 26, 27, 28, 30, 360 })
        {
            await h.AddTimedAsync(minutes, "ana");
        }

        var completion = Completion(await h.GetAsync());

        Assert.True(completion.GetProperty("available").GetBoolean());
        Assert.Equal(10, completion.GetProperty("usableDurations").GetInt64());
        Assert.Equal(0, completion.GetProperty("unusableDurations").GetInt64());
        Assert.Equal(1, completion.GetProperty("outliers").GetInt64());
        Assert.Equal(25.5 * 60, completion.GetProperty("medianSeconds").GetDouble(), 3);
        Assert.Equal(222.0 / 9 * 60, completion.GetProperty("meanWithoutOutliersSeconds").GetDouble(), 3);
        Assert.Equal(14.0 * 60, completion.GetProperty("lowerFenceSeconds").GetDouble(), 3);
        Assert.Equal(36.0 * 60, completion.GetProperty("upperFenceSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task Durations_that_cannot_be_used_are_counted_and_left_out_of_every_figure()
    {
        await using var h = await CreateAsync(postgres);
        foreach (var minutes in new[] { 20, 25, 30 })
        {
            await h.AddTimedAsync(minutes, "ana");
        }

        var unusable = new (string, string?)[][]
        {
            [("start", "2026-09-01T10:00:00.000Z"), ("end", "2026-09-01T09:00:00.000Z")],
            [("start", "2026-09-01T10:00:00.000Z")],
            [("start", "not a date"), ("end", "2026-09-01T09:00:00.000Z")],
            [("start", "infinity"), ("end", "infinity")],
            [("start", "-infinity"), ("end", "2026-09-01T09:00:00.000Z")],
            [("start", "2026-09-01T09:00:00.000Z"), ("end", "now")],
            [("start", "2026-09-01T09:00:00.000Z"), ("end", "10000-01-01T00:00:00Z")],
            [("start", "2026-02-30T09:00:00.000Z"), ("end", "2026-09-01T09:00:00.000Z")],
            [("start", ""), ("end", "")],
        };
        foreach (var answers in unusable)
        {
            await h.AddAsync(Doc(answers));
        }

        var dashboard = await h.GetAsync();
        var completion = Completion(dashboard);

        Assert.Equal(12, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Equal(3, completion.GetProperty("usableDurations").GetInt64());
        Assert.Equal(9, completion.GetProperty("unusableDurations").GetInt64());
        Assert.Equal(0, completion.GetProperty("outliers").GetInt64());
        Assert.Equal(25 * 60, completion.GetProperty("medianSeconds").GetDouble(), 3);
        Assert.Equal(25 * 60, completion.GetProperty("meanWithoutOutliersSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task Offsets_Z_and_none_are_read_as_written_and_a_missing_offset_as_UTC()
    {
        await using var h = await CreateAsync(postgres, timeZone: "America/Bogota");
        await h.AddAsync(Doc(Times("2026-09-01T09:00:00.000-05:00", "2026-09-01T09:30:00.000-05:00")));     // 30 min
        await h.AddAsync(Doc(Times("2026-09-01T14:00:00Z", "2026-09-01T14:20:00Z")));                       // 20 min
        await h.AddAsync(Doc(Times("2026-09-01T14:00:00", "2026-09-01T14:10:00")));                         // 10 min, UTC
        await h.AddAsync(Doc(Times("2026-09-01T14:00:00", "2026-09-01T09:45:00.000-05:00")));               // 45 min

        var completion = Completion(await h.GetAsync());

        Assert.Equal(4, completion.GetProperty("usableDurations").GetInt64());
        Assert.Equal(25 * 60, completion.GetProperty("medianSeconds").GetDouble(), 3);
        Assert.Equal(26.25 * 60, completion.GetProperty("meanWithoutOutliersSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task Start_and_end_are_found_by_their_path_when_they_sit_in_a_group()
    {
        const string fields = """
            [{"name":"start","xpath":"grp/start","type":"start"},
             {"name":"end","xpath":"grp/end","type":"end"},
             {"name":"username","xpath":"username","type":"username"}]
            """;
        await using var h = await CreateAsync(postgres, fieldsJson: fields, choicesJson: "[]");
        await h.AddAsync(Doc(("grp/start", "2026-09-01T09:00:00.000-05:00"), ("grp/end", "2026-09-01T09:12:00.000-05:00"), ("username", "ana")));
        await h.AddAsync(Doc(("start", "2026-09-01T09:00:00.000-05:00"), ("end", "2026-09-01T09:50:00.000-05:00"), ("username", "ana")));

        var completion = Completion(await h.GetAsync());

        Assert.True(completion.GetProperty("available").GetBoolean());
        Assert.Equal(1, completion.GetProperty("usableDurations").GetInt64());
        Assert.Equal(1, completion.GetProperty("unusableDurations").GetInt64());
        Assert.Equal(12 * 60, completion.GetProperty("medianSeconds").GetDouble(), 3);
    }

    [Fact]
    public async Task A_form_without_end_says_so_and_the_rest_is_still_shown()
    {
        const string fields = """
            [{"name":"start","xpath":"start","type":"start"},{"name":"username","xpath":"username","type":"username"}]
            """;
        await using var h = await CreateAsync(postgres, fieldsJson: fields, choicesJson: "[]");
        await h.AddAsync(Doc(("start", "2026-09-01T09:00:00Z"), ("end", "2026-09-01T09:30:00Z"), ("username", "ana")));

        var dashboard = await h.GetAsync();
        var completion = Completion(dashboard);

        Assert.False(completion.GetProperty("available").GetBoolean());
        Assert.Equal(["end"], completion.GetProperty("missingFields").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(JsonValueKind.Null, completion.GetProperty("usableDurations").ValueKind);
        Assert.Equal(JsonValueKind.Null, completion.GetProperty("medianSeconds").ValueKind);
        Assert.Equal(1, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
        Assert.Single(Enumerators(dashboard));
    }

    // Enumerators

    [Fact]
    public async Task The_same_enumerator_written_differently_is_one_enumerator()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(("username", "Ana")), "2026-09-01T10:00:00Z");
        await h.AddAsync(Doc(("username", "ana ")), "2026-09-03T10:00:00Z");
        await h.AddAsync(Doc(("username", "ANA")), "2026-09-02T10:00:00Z");
        await h.AddAsync(Doc(("username", " ana\t")), "2026-09-01T11:00:00Z");

        var rows = Enumerators(await h.GetAsync());

        var row = Assert.Single(rows);
        Assert.Equal("ana", row.GetProperty("name").GetString()!.ToLowerInvariant());
        Assert.Equal(4, row.GetProperty("submissions").GetInt64());
        Assert.Equal(DateTimeOffset.Parse("2026-09-03T10:00:00Z"), row.GetProperty("lastSubmissionAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Submissions_without_an_enumerator_are_shown_together()
    {
        await using var h = await CreateAsync(postgres);
        await h.AddAsync(Doc(("start", "2026-09-01T09:00:00Z")));                       // missing
        await h.AddAsync(Doc(("username", "")));                                         // empty
        await h.AddAsync(Doc(("username", "   ")));                                      // only spaces
        await h.AddAsync(Doc(("username", "\t \r\n")));                             // only other white space
        await h.AddAsync("""{"username": null}""");                                       // null
        await h.AddAsync(Doc(("username", "ben")));

        var rows = Enumerators(await h.GetAsync());

        Assert.Equal(2, rows.Count);
        var none = rows.Single(r => r.GetProperty("name").ValueKind == JsonValueKind.Null);
        Assert.Equal(5, none.GetProperty("submissions").GetInt64());
        Assert.Equal(1, rows.Single(r => r.GetProperty("name").GetString() == "ben").GetProperty("submissions").GetInt64());
    }

    [Fact]
    public async Task Each_enumerator_has_a_mean_without_outliers_and_their_outliers()
    {
        await using var h = await CreateAsync(postgres);
        foreach (var minutes in new[] { 20, 22, 24, 26, 28, 600 })
        {
            await h.AddTimedAsync(minutes, "ana");
        }

        foreach (var minutes in new[] { 30, 32, 34 })
        {
            await h.AddTimedAsync(minutes, "ben");
        }

        var rows = Enumerators(await h.GetAsync());
        var ana = rows.Single(r => r.GetProperty("name").GetString() == "ana");
        var ben = rows.Single(r => r.GetProperty("name").GetString() == "ben");

        Assert.Equal(6, ana.GetProperty("submissions").GetInt64());
        Assert.Equal(1, ana.GetProperty("outliers").GetInt64());
        Assert.Equal(24 * 60, ana.GetProperty("meanSeconds").GetDouble(), 3);
        Assert.Equal(0, ben.GetProperty("outliers").GetInt64());
        Assert.Equal(32 * 60, ben.GetProperty("meanSeconds").GetDouble(), 3);
        Assert.Equal("ana", rows[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_selected_question_is_used_over_username()
    {
        await using var h = await CreateAsync(postgres, enumeratorField: "enumerator_code");
        await h.AddAsync(Doc(("username", "kobo_user"), ("enumerator_code", "E-17")));
        await h.AddAsync(Doc(("username", "kobo_user"), ("enumerator_code", "e-17 ")));
        await h.AddAsync(Doc(("username", "kobo_user")));

        var dashboard = await h.GetAsync();
        var rows = Enumerators(dashboard);

        Assert.True(dashboard.GetProperty("enumerators").GetProperty("available").GetBoolean());
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Single(r => r.GetProperty("name").ValueKind == JsonValueKind.String).GetProperty("submissions").GetInt64());
        Assert.Equal(1, rows.Single(r => r.GetProperty("name").ValueKind == JsonValueKind.Null).GetProperty("submissions").GetInt64());
    }

    [Fact]
    public async Task A_selected_question_removed_by_a_redeploy_falls_back_to_username()
    {
        await using var h = await CreateAsync(postgres, enumeratorField: "removed_question");
        await h.AddAsync(Doc(("username", "ana"), ("removed_question", "E-17")));

        var rows = Enumerators(await h.GetAsync());

        Assert.Equal("ana", Assert.Single(rows).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Without_username_a_removed_selection_leaves_no_enumerator_identifier()
    {
        const string fields = """
            [{"name":"start","xpath":"start","type":"start"},{"name":"end","xpath":"end","type":"end"}]
            """;
        await using var h = await CreateAsync(postgres, fieldsJson: fields, choicesJson: "[]", enumeratorField: "removed_question");
        await h.AddAsync(Doc(("username", "ana"), ("removed_question", "E-17")));

        var dashboard = await h.GetAsync();
        var enumerators = dashboard.GetProperty("enumerators");

        Assert.False(enumerators.GetProperty("available").GetBoolean());
        Assert.Equal("selectEnumeratorField", enumerators.GetProperty("hint").GetString());
        Assert.Empty(Enumerators(dashboard));
        Assert.Equal(1, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Without_username_and_without_a_selection_there_is_no_enumerator_identifier()
    {
        const string fields = """[{"name":"municipality","xpath":"municipality","type":"select_one"}]""";
        await using var h = await CreateAsync(postgres, fieldsJson: fields, choicesJson: "[]");
        await h.AddAsync(Doc(("username", "ana")));

        var enumerators = (await h.GetAsync()).GetProperty("enumerators");

        Assert.False(enumerators.GetProperty("available").GetBoolean());
        Assert.Equal("selectEnumeratorField", enumerators.GetProperty("hint").GetString());
    }

    // Keys travel as parameters

    [Fact]
    public async Task A_key_with_quotes_in_the_form_is_a_value_and_not_part_of_the_statement()
    {
        const string key = "it's\" a ; DROP TABLE tamiza.raw_submissions; --";
        var fields = JsonSerializer.Serialize(new[]
        {
            new { name = "start", xpath = "start", type = "start" },
            new { name = "q", xpath = key, type = "username" },
        });
        await using var h = await CreateAsync(postgres, fieldsJson: fields, choicesJson: "[]");
        await h.AddAsync(JsonSerializer.Serialize(new Dictionary<string, string> { [key] = "ana" }));

        var dashboard = await h.GetAsync();

        Assert.Equal("ana", Assert.Single(Enumerators(dashboard)).GetProperty("name").GetString());
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.raw_submissions"));
    }

    // OpenAPI

    [Fact]
    public async Task The_openapi_document_lists_the_performance_operation_and_its_range()
    {
        await using var h = await CreateAsync(postgres);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var operation = document.GetProperty("paths").GetProperty("/api/v1/projects/{projectId}/performance").GetProperty("get");

        Assert.Equal("GetPerformanceDashboard", operation.GetProperty("operationId").GetString());
        Assert.Equal(["projectId", "from", "to"], operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("PerformanceDashboardResult", out _));
    }
}
