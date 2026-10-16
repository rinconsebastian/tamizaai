using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Assistant;
using Tamiza.Api.IntegrationTests.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Assistant;

/// <summary>The assistant's routes against the stub provider: access, validation, the budget, the one-request rule and the records.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AssistantApiTests(PostgresFixture postgres) : IDisposable
{
    private const string Draft = "def compute(cleaned, ctx):\n    return cleaned[\"main\"]\n";
    private const string Indicator = "piped_water_rate";

    private readonly AssistantStub _stub = new();

    public void Dispose() => _stub.Dispose();

    private Task<CleanDataHarness> CreateAsync(Dictionary<string, string?>? extra = null, Action<IServiceCollection>? services = null, AssistantStub? stub = null)
    {
        var settings = (stub ?? _stub).Settings();
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return CleanDataHarness.CreateAsync(postgres, settings: settings, configureServices: services);
    }

    private static string Url(CleanDataHarness h, string route = "") => $"/api/v1/projects/{h.ProjectId}/assistant{route}";

    /// <summary>A valid request for a whole indicator script; tests change or remove members.</summary>
    private static Dictionary<string, object?> Body(string action = "script", string? message = "rate of piped water by municipality") => new()
    {
        ["kind"] = "indicator",
        ["name"] = Indicator,
        ["action"] = action,
        ["messages"] = message is null ? new object[0] : new object[] { new { role = "user", content = message } },
        ["draft"] = Draft,
    };

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> PostAsync(HttpClient client, string url, object body, CancellationToken ct = default)
    {
        var response = await client.PostAsJsonAsync(url, body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return (response, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    /// <summary>The request as "Show what will be sent" prints it, from what the provider received.</summary>
    private static string TextOf(AssistantStub.Received request) =>
        string.Join("\n\n", request.Messages.Select(m => $"=== {m!["role"]!.GetValue<string>()} ===\n{m["content"]!.GetValue<string>()}"));

    private static string SystemOf(AssistantStub.Received request) => request.Messages[0]!["content"]!.GetValue<string>();

    private static async Task<JsonElement> CallsAsync(CleanDataHarness h) =>
        JsonDocument.Parse((await TestData.ScalarAsync<string>(h.Db,
            "SELECT coalesce(json_agg(row_to_json(c) ORDER BY started_at), '[]'::json)::text FROM tamiza.assistant_calls c"))!).RootElement.Clone();

    private static async Task<JsonElement> OnlyCallAsync(CleanDataHarness h)
    {
        var calls = await CallsAsync(h);
        Assert.Equal(1, calls.GetArrayLength());
        return calls[0];
    }

    private static async Task<Guid> UserIdAsync(CleanDataHarness h, string email) =>
        (await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.users WHERE email = @e", ("e", email)))!;

    private static string AnalystEmail => "grace@example.org";

    /// <summary>Seeds a finished test of the analyst's, as the worker would leave it: an indicator that failed with a KeyError.</summary>
    private static async Task<Guid> InsertTestAsync(
        CleanDataHarness h, string email = "grace@example.org", string kind = "indicator", string? name = Indicator, string code = Draft,
        TimeSpan? endedAgo = null, string? steps = null)
    {
        var id = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, name, code, sample_size, status, queued_at, started_at, finished_at, steps, error_code, error)
            VALUES (@id, @project, @user, @kind, @name, @code, 500, 'failed', now() - @ago, now() - @ago, now() - @ago, @steps::jsonb, 'step.indicator', 'KeyError: ''missing''')
            """,
            ("id", id), ("project", h.ProjectId), ("user", await UserIdAsync(h, email)), ("kind", kind), ("name", (object?)name ?? DBNull.Value),
            ("code", code), ("ago", endedAgo ?? TimeSpan.FromMinutes(1)), ("steps", steps ?? FailedIndicatorSteps));
        return id;
    }

    private const string FailedIndicatorSteps = """
        [{"step":"flatten","status":"succeeded","tables":[{"name":"main","rows":3,
            "columns":[{"name":"_id","type":"integer"},{"name":"water_source","type":"text","label":"Water source"}],
            "previewRows":3,"preview":[[1,"Juan Perez"],[2,"Maria Gomez"],[3,"Ana Torres"]]}],"log":""},
         {"step":"indicator","status":"failed","tables":[],
          "log":"INFO main cleaned\nprinted Juan Perez\nPlain print of Maria Gomez",
          "error":"KeyError: 'missing'\n\nTraceback (most recent call last):\n  File \"script.py\", line 2, in compute\n    return cleaned[\"main\"][\"missing\"]\nKeyError: 'missing'"}]
        """;

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    // ---- availability and access -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_the_variables_every_route_answers_503_and_nothing_is_sent()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var routes = new (HttpMethod Method, string Route, object? Body)[]
        {
            (HttpMethod.Get, "", null),
            (HttpMethod.Post, "/context", Body()),
            (HttpMethod.Post, "/completions", Body()),
            (HttpMethod.Put, $"/calls/{Guid.NewGuid()}/outcome", new { applied = "script" }),
        };
        foreach (var (method, route, body) in routes)
        {
            var request = new HttpRequestMessage(method, Url(h, route)) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
            var response = await h.Analyst.SendAsync(request);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{method} {route} answered {(int)response.StatusCode}.");
            Assert.Equal("assistant.disabled", Code(problem));
        }

        Assert.Equal(0, _stub.Requests().Count);
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task With_one_variable_missing_the_assistant_is_off_and_the_log_says_which_once()
    {
        var settings = _stub.Settings();
        settings.Remove("TAMIZA_ASSISTANT_API_KEY");
        await using var h = await CleanDataHarness.CreateAsync(postgres, settings: settings);

        var response = await h.Analyst.GetAsync(Url(h));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("assistant.disabled", Code(problem));
        var warnings = h.Factory.Logs.Lines.Where(l => l.Contains("TAMIZA_ASSISTANT_API_KEY") && l.Contains("is off")).ToList();
        Assert.Single(warnings);
        Assert.Contains("because TAMIZA_ASSISTANT_API_KEY is not set", warnings[0]);
    }

    [Fact]
    public async Task A_viewer_is_refused_and_a_non_member_gets_the_answer_for_a_missing_project_on_every_route()
    {
        await using var h = await CreateAsync();
        using var outsider = TestData.ClientFor(h.Factory, "zoe", "zoe@example.org");
        var routes = new (HttpMethod Method, string Route)[]
        {
            (HttpMethod.Get, ""), (HttpMethod.Post, "/context"), (HttpMethod.Post, "/completions"),
            (HttpMethod.Put, $"/calls/{Guid.NewGuid()}/outcome"),
        };

        foreach (var (method, route) in routes)
        {
            foreach (var (client, expected) in new[] { (h.Viewer, HttpStatusCode.Forbidden), (outsider, HttpStatusCode.NotFound) })
            {
                var request = new HttpRequestMessage(method, Url(h, route)) { Content = JsonContent.Create(route.Contains("outcome") ? new Dictionary<string, object?> { ["applied"] = "script" } : Body()) };
                var response = await client.SendAsync(request);

                Assert.True(response.StatusCode == expected, $"{method} {route} answered {(int)response.StatusCode} instead of {(int)expected}.");
            }
        }

        Assert.Equal(0, _stub.Requests().Count);
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task An_admin_and_an_analyst_can_read_the_disclosure_with_the_model_and_the_host_but_never_the_key()
    {
        await using var h = await CreateAsync();

        foreach (var client in new[] { h.Analyst, h.Admin })
        {
            var response = await client.GetAsync(Url(h));
            var text = await response.Content.ReadAsStringAsync();
            var info = JsonDocument.Parse(text).RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(AssistantStub.Model, info.GetProperty("model").GetString());
            Assert.Equal(new Uri(_stub.Url).Host, info.GetProperty("host").GetString());
            Assert.Equal(["host", "model"], info.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain(AssistantStub.Key, text);
        }
    }

    // ---- the provider -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_request_goes_to_a_provider_on_localhost_with_the_key_the_model_and_the_assembled_context()
    {
        await using var h = await CreateAsync();
        _stub.Answer("It reads main.\n\n```python\ndef compute(cleaned, ctx):\n    return cleaned[\"main\"]\n```");

        var (response, body) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(_stub.Requests());
        Assert.Equal($"Bearer {AssistantStub.Key}", request.Authorization);
        Assert.Equal(AssistantStub.Model, request.Body["model"]!.GetValue<string>());
        Assert.Equal(["system", "user"], request.Messages.Select(m => m!["role"]!.GetValue<string>()).ToArray());
        Assert.Equal("rate of piped water by municipality", request.Messages[1]!["content"]!.GetValue<string>());
        Assert.Contains("<rules>", SystemOf(request));
        Assert.Contains("<draft>", SystemOf(request));
        Assert.Equal("It reads main.\n\n[proposed code: 2 lines]", body.GetProperty("explanation").GetString());
        var block = Assert.Single(body.GetProperty("blocks").EnumerateArray());
        Assert.Equal("def compute(cleaned, ctx):\n    return cleaned[\"main\"]", block.GetProperty("code").GetString());
        Assert.True(block.GetProperty("complete").GetBoolean());
        Assert.False(body.GetProperty("truncated").GetBoolean());
        Assert.False(body.GetProperty("testExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("omitted").ValueKind);
        var usage = body.GetProperty("usage");
        Assert.Equal((100, 20, 120, false), (usage.GetProperty("promptTokens").GetInt32(), usage.GetProperty("completionTokens").GetInt32(),
            usage.GetProperty("totalTokens").GetInt32(), usage.GetProperty("estimated").GetBoolean()));
        Assert.NotEqual(Guid.Empty, body.GetProperty("callId").GetGuid());
    }

    [Fact]
    public async Task Changing_the_variables_and_restarting_sends_the_next_request_to_the_new_provider()
    {
        using var second = new AssistantStub();
        second.Answer("From the second provider.");
        await using var first = await CreateAsync();
        await using var other = await CreateAsync(stub: second, extra: new() { ["TAMIZA_ASSISTANT_MODEL"] = "other-model", ["TAMIZA_ASSISTANT_API_KEY"] = "sk-other-key" });
        second.RespondRaw(200, AssistantStub.ChatBody("From the second provider."), key: "sk-other-key");

        await PostAsync(first.Analyst, Url(first, "/completions"), Body());
        var (response, body) = await PostAsync(other.Analyst, Url(other, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("From the second provider.", body.GetProperty("explanation").GetString());
        var one = Assert.Single(_stub.Requests());
        var two = Assert.Single(second.Requests());
        Assert.Equal((AssistantStub.Model, $"Bearer {AssistantStub.Key}"), (one.Body["model"]!.GetValue<string>(), one.Authorization));
        Assert.Equal(("other-model", "Bearer sk-other-key"), (two.Body["model"]!.GetValue<string>(), two.Authorization));
    }

    [Fact]
    public async Task The_extra_fields_are_in_every_request_body()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_EXTRA_BODY"] = """{"reasoning_effort":"none","chat_template_kwargs":{"enable_thinking":false}}""" });

        await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(2, _stub.Requests().Count);
        foreach (var request in _stub.Requests())
        {
            Assert.Equal("none", request.Body["reasoning_effort"]!.GetValue<string>());
            Assert.False(request.Body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_the_provider_is_reported_unavailable()
    {
        await using var h = await CreateAsync();
        _stub.Redirect("/elsewhere");

        var (response, body) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("assistant.unavailable", Code(body));
        Assert.Equal(0, _stub.RequestsAt("/elsewhere"));
        var call = await OnlyCallAsync(h);
        Assert.Equal("failed", call.GetProperty("status").GetString());
        Assert.Equal("assistant.unavailable", call.GetProperty("error_code").GetString());
    }

    // ---- /context ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task What_will_be_sent_is_the_text_of_the_request_that_follows_without_calling_the_provider_or_recording_anything()
    {
        await using var h = await CreateAsync();
        var body = Body();
        body["messages"] = new object[]
        {
            new { role = "user", content = "first ask" }, new { role = "assistant", content = "first answer" }, new { role = "user", content = "second ask" },
        };

        var (shown, context) = await PostAsync(h.Analyst, Url(h, "/context"), body);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
        Assert.Equal(["bytes", "host", "model", "omitted", "testExpired", "text"],
            context.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(AssistantStub.Model, context.GetProperty("model").GetString());

        await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(context.GetProperty("text").GetString(), TextOf(Assert.Single(_stub.Requests())));
        Assert.Equal(context.GetProperty("bytes").GetInt64(),
            System.Text.Encoding.UTF8.GetByteCount(string.Concat(Assert.Single(_stub.Requests()).Messages.Select(m => m!["content"]!.GetValue<string>()))));
    }

    [Fact]
    public async Task What_will_be_sent_before_typing_shows_the_context_without_a_request()
    {
        await using var h = await CreateAsync();

        var (none, withNone) = await PostAsync(h.Analyst, Url(h, "/context"), Body(message: null));
        var (blank, withBlank) = await PostAsync(h.Analyst, Url(h, "/context"), Body(message: "   "));

        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        Assert.Equal(HttpStatusCode.OK, blank.StatusCode);
        Assert.Equal(withNone.GetProperty("text").GetString(), withBlank.GetProperty("text").GetString());
        Assert.Contains("<draft>", withNone.GetProperty("text").GetString());
        Assert.DoesNotContain("=== user ===", withNone.GetProperty("text").GetString());
        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task What_will_be_sent_is_not_held_back_by_the_budget_or_a_request_in_progress()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER"] = "1000" });
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.assistant_calls (id, project_id, user_id, kind, action, model, prompt_kit, context, status, total_tokens, started_at)
            VALUES (gen_random_uuid(), @p, @u, 'clean', 'script', 'm', 1, '{}', 'running', 5000, now())
            """, ("p", h.ProjectId), ("u", await UserIdAsync(h, AnalystEmail)));

        var (response, _) = await PostAsync(h.Analyst, Url(h, "/context"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- size ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_draft_over_64_KB_is_refused_as_too_large_and_nothing_is_recorded()
    {
        await using var h = await CreateAsync();
        var body = Body();
        body["draft"] = "x = 1\n" + new string('#', 70 * 1024);

        foreach (var route in new[] { "/context", "/completions" })
        {
            var (response, problem) = await PostAsync(h.Analyst, Url(h, route), body);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("assistant.contextTooLarge", Code(problem));
        }

        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task A_draft_that_still_does_not_fit_after_everything_was_left_out_is_refused_with_the_same_message()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_MAX_CONTEXT_KB"] = "64" });
        var body = Body();
        body["draft"] = "x = 1\n" + new string('#', 60 * 1024);

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("assistant.contextTooLarge", Code(problem));
        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task What_was_left_out_to_fit_is_listed_and_kept_in_the_record()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_MAX_CONTEXT_KB"] = "64" });
        var turns = new List<object>();
        for (var i = 0; i < 6; i++)
        {
            turns.Add(new { role = "user", content = $"ask {i} " + new string('a', 3000) });
            turns.Add(new { role = "assistant", content = $"answer {i} " + new string('b', 3000) });
        }

        turns.Add(new { role = "user", content = "last ask" });
        var body = Body();
        body["messages"] = turns;
        body["draft"] = "x = 1\n" + new string('#', 30 * 1024);

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var omitted = answer.GetProperty("omitted").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(omitted, o => o!.StartsWith("earlier turns:", StringComparison.Ordinal));
        var request = Assert.Single(_stub.Requests());
        Assert.Equal("user", request.Messages[1]!["role"]!.GetValue<string>());
        Assert.Equal("last ask", request.Messages[^1]!["content"]!.GetValue<string>());
        var recorded = (await OnlyCallAsync(h)).GetProperty("context");
        Assert.Equal(omitted, recorded.GetProperty("omitted").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    // ---- validation ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_rule_of_the_request_is_checked_with_a_field_error_and_nothing_is_sent_or_recorded()
    {
        await using var h = await CreateAsync();
        var big = new string('a', 17 * 1024);
        var medium = new string('b', 15 * 1024);
        object User(string text) => new { role = "user", content = text };
        object Bot(string text) => new { role = "assistant", content = text };

        var cases = new (string What, Action<Dictionary<string, object?>> Change, string Field)[]
        {
            ("turns that do not alternate", b => b["messages"] = new[] { User("a"), User("b") }, "messages"),
            ("two assistant turns in a row", b => b["messages"] = new[] { User("a"), Bot("b"), Bot("c"), User("d") }, "messages"),
            ("a start with an assistant turn", b => b["messages"] = new[] { Bot("a"), User("b") }, "messages"),
            ("an end with an assistant turn", b => b["messages"] = new[] { User("a"), Bot("b") }, "messages"),
            ("a blank last user turn", b => b["messages"] = new[] { User("a"), Bot("b"), User("  ") }, "messages"),
            ("no messages", b => b["messages"] = Array.Empty<object>(), "messages"),
            ("a blank turn in the middle", b => b["messages"] = new[] { User("a"), Bot(" "), User("c") }, "messages"),
            ("a turn over 16 KB", b => b["messages"] = new[] { User(big) }, "messages"),
            ("more than 64 KB of turns", b => b["messages"] = new[] { User(medium), Bot(medium), User(medium), Bot(medium), User(medium) }, "messages"),
            ("more than 39 turns", b => b["messages"] = Enumerable.Range(0, 41).Select(i => i % 2 == 0 ? User("u") : Bot("a")).ToArray(), "messages"),
            ("an unknown role", b => b["messages"] = new[] { new { role = "system", content = "x" } }, "messages"),
            ("a cell request without a focus", b => b["action"] = "cell", "focus.line"),
            ("a cell request on a line that is not a marker", b => { b["action"] = "cell"; b["focus"] = new { line = 2 }; }, "focus.line"),
            ("a cell request beyond the last line", b => { b["action"] = "cell"; b["focus"] = new { line = 99 }; }, "focus.line"),
            ("a fix without a test", b => b["action"] = "fix", "testId"),
            ("a name on script 1", b => { b["kind"] = "clean"; }, "name"),
            ("an indicator without a name", b => b.Remove("name"), "name"),
            ("an invalid indicator name", b => b["name"] = "Piped Water", "name"),
            ("an unknown kind", b => b["kind"] = "indicators", "kind"),
            ("a numeric kind", b => b["kind"] = "1", "kind"),
            ("an unknown action", b => b["action"] = "review", "action"),
            ("no action", b => b.Remove("action"), "action"),
            ("a label over its limit", b => b["details"] = new { label = new string('l', 121) }, "details.label"),
            ("a description over its limit", b => b["details"] = new { description = new string('d', 1001) }, "details.description"),
            ("a unit over its limit", b => b["details"] = new { unit = new string('u', 33) }, "details.unit"),
            ("an unknown format", b => b["details"] = new { format = "hexadecimal" }, "details.format"),
            ("an unknown warning key", b => { b["action"] = "fix"; b["testId"] = Guid.NewGuid(); b["warning"] = new { key = "somethingElse" }; }, "warning.key"),
        };

        foreach (var (what, change, field) in cases)
        {
            var body = Body();
            change(body);

            var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{what}: answered {(int)response.StatusCode}.");
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"{what}: no error for '{field}'.");
        }

        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task A_missing_body_is_a_validation_error()
    {
        await using var h = await CreateAsync();

        var response = await h.Analyst.PostAsync(Url(h, "/completions"), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_cell_request_names_the_cell_and_a_clean_request_needs_no_name()
    {
        await using var h = await CreateAsync();
        var body = Body("cell");
        body["draft"] = "import pandas as pd\n\n# %% Rate\nx = 1\n";
        body["focus"] = new { line = 3 };

        var (cell, _) = await PostAsync(h.Analyst, Url(h, "/completions"), body);
        var (clean, _) = await PostAsync(h.Analyst, Url(h, "/completions"), new Dictionary<string, object?>
        {
            ["kind"] = "clean", ["action"] = "script", ["messages"] = new[] { new { role = "user", content = "drop test submissions" } }, ["draft"] = "",
        });

        Assert.Equal(HttpStatusCode.OK, cell.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);
        Assert.Contains("rewrite one cell. The cell is lines 3 to 4", SystemOf(_stub.Requests()[0]));
        Assert.Contains("Script 1 (clean)", SystemOf(_stub.Requests()[1]));
    }

    // ---- fix and test facts -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_fix_sends_the_facts_of_the_analysts_test_and_none_of_its_values()
    {
        await using var h = await CreateAsync();
        var testId = await InsertTestAsync(h);
        var body = Body("fix", "fix the error");
        body["testId"] = testId;

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(answer.GetProperty("testExpired").GetBoolean());
        var system = SystemOf(Assert.Single(_stub.Requests()));
        Assert.Contains("\n<test>\n", system);
        Assert.Contains("KeyError", system);
        Assert.Contains("line 2 in compute", system);
        Assert.Contains("INFO main cleaned", system);
        foreach (var value in new[] { "Juan", "Perez", "Maria", "Gomez", "Torres", "Plain print" })
        {
            Assert.DoesNotContain(value, system);
        }

        var call = await OnlyCallAsync(h);
        Assert.Equal("fix", call.GetProperty("action").GetString());
        Assert.Equal(testId, call.GetProperty("test_id").GetGuid());
        Assert.True(call.GetProperty("context").GetProperty("hasTest").GetBoolean());
    }

    [Fact]
    public async Task A_test_that_has_expired_is_answered_without_it_and_the_warning_goes_with_it()
    {
        await using var h = await CreateAsync();
        var expired = await InsertTestAsync(h, endedAgo: TimeSpan.FromHours(25));
        var body = Body("fix", "fix the warning");
        body["testId"] = expired;
        body["warning"] = new { key = "noValue", @params = new { measure = "secret_measure" } };

        foreach (var testId in new[] { expired, Guid.NewGuid() })
        {
            body["testId"] = testId;
            var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(answer.GetProperty("testExpired").GetBoolean());
        }

        foreach (var request in _stub.Requests())
        {
            var system = SystemOf(request);
            Assert.DoesNotContain("\n<test>\n", system);
            Assert.DoesNotContain("secret_measure", system);
            Assert.Contains("no test facts", system);
        }

        var (_, context) = await PostAsync(h.Analyst, Url(h, "/context"), body);
        Assert.True(context.GetProperty("testExpired").GetBoolean());
        Assert.False((await CallsAsync(h))[0].GetProperty("context").GetProperty("hasTest").GetBoolean());
    }

    [Fact]
    public async Task Another_analysts_test_gives_no_facts()
    {
        await using var h = await CreateAsync();
        var admins = await InsertTestAsync(h, email: "ada@example.org");
        var body = Body("fix", "fix it");
        body["testId"] = admins;

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(answer.GetProperty("testExpired").GetBoolean());
        Assert.DoesNotContain("\n<test>\n", SystemOf(Assert.Single(_stub.Requests())));
    }

    [Fact]
    public async Task A_test_of_other_text_or_another_script_is_stale_and_nothing_is_sent()
    {
        await using var h = await CreateAsync();
        var other = await InsertTestAsync(h, code: Draft + "# edited\n");
        var otherScript = await InsertTestAsync(h, name: "another_indicator");
        var otherKind = await InsertTestAsync(h, kind: "output", name: null);

        foreach (var testId in new[] { other, otherScript, otherKind })
        {
            var body = Body("fix", "fix it");
            body["testId"] = testId;

            var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("assistant.testStale", Code(problem));
        }

        Assert.Empty(_stub.Requests());
        Assert.Equal(0, (await CallsAsync(h)).GetArrayLength());
    }

    [Fact]
    public async Task Warning_params_outside_the_per_key_list_are_dropped_before_the_request_is_assembled()
    {
        await using var h = await CreateAsync();
        var testId = await InsertTestAsync(h);
        var body = Body("fix", "fix the warning");
        body["testId"] = testId;
        body["warning"] = new { key = "percentAboveOne", @params = new { max = 87.5, shown = "Juan Perez", measure = "value" } };

        var (response, _) = await PostAsync(h.Analyst, Url(h, "/completions"), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var system = SystemOf(Assert.Single(_stub.Requests()));
        Assert.Contains("percentAboveOne", system);
        Assert.DoesNotContain("87.5", system);
        Assert.DoesNotContain("Juan Perez", system);
    }

    // ---- answers ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_answer_cut_off_at_the_output_limit_is_truncated_with_an_incomplete_block()
    {
        await using var h = await CreateAsync();
        _stub.Answer("Here it is:\n```python\ndef compute(cleaned, ctx):\n    x = ", finishReason: "length");

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(answer.GetProperty("truncated").GetBoolean());
        Assert.False(Assert.Single(answer.GetProperty("blocks").EnumerateArray()).GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Several_blocks_and_a_missing_block_are_reported_as_they_come()
    {
        await using var h = await CreateAsync();
        _stub.Answer("A:\n```python\na = 1\n```\nB:\n```python\nb = 2\n```");
        var (_, two) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        _stub.Answer("Which column holds the municipality?");
        var (_, none) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(2, two.GetProperty("blocks").GetArrayLength());
        Assert.Equal("A:\n[proposed code: 1 lines]\nB:\n[proposed code: 1 lines]", two.GetProperty("explanation").GetString());
        Assert.Equal(0, none.GetProperty("blocks").GetArrayLength());
        Assert.Equal("Which column holds the municipality?", none.GetProperty("explanation").GetString());
    }

    // ---- budget -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Once_the_days_tokens_reach_the_budget_the_request_is_refused_with_the_time_it_renews()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER"] = "1000" });
        var analyst = await UserIdAsync(h, AnalystEmail);
        var other = await TestData.InsertProjectAsync(h.Db, analyst, "Another project");
        // 400 here, 600 in another project: the count is per user across projects. Yesterday's and another user's do not count.
        await InsertCallAsync(h, h.ProjectId, analyst, tokens: 400);
        await InsertCallAsync(h, other, analyst, tokens: 600, status: "failed");
        await InsertCallAsync(h, h.ProjectId, analyst, tokens: 9000, ago: TimeSpan.FromDays(2));
        await InsertCallAsync(h, h.ProjectId, await UserIdAsync(h, "ada@example.org"), tokens: 9000);

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        var (admin, _) = await PostAsync(h.Admin, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("assistant.budget", Code(problem));
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(1), problem.GetProperty("renewsAt").GetDateTimeOffset().UtcDateTime);
        // The admin has 9000 tokens of their own today.
        Assert.Equal(HttpStatusCode.TooManyRequests, admin.StatusCode);
        Assert.Empty(_stub.Requests());
    }

    [Fact]
    public async Task A_users_day_starts_again_at_midnight_utc()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER"] = "1000" });
        await InsertCallAsync(h, h.ProjectId, await UserIdAsync(h, AnalystEmail), tokens: 5000, ago: TimeSpan.FromHours(30));

        var (response, _) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reasoning_tokens_that_only_the_total_reports_count_toward_the_budget()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER"] = "1725" });
        _stub.Answer("Done.", usage: """{"prompt_tokens":758,"completion_tokens":102,"total_tokens":1725}""");

        var (first, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        var (second, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1725, answer.GetProperty("usage").GetProperty("totalTokens").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("assistant.budget", Code(problem));
        var call = (await CallsAsync(h))[0];
        Assert.Equal((758, 102, 1725, false),
            (call.GetProperty("prompt_tokens").GetInt32(), call.GetProperty("completion_tokens").GetInt32(), call.GetProperty("total_tokens").GetInt32(),
                call.GetProperty("tokens_estimated").GetBoolean()));
        Assert.Single(_stub.Requests());
    }

    [Fact]
    public async Task Without_usage_the_tokens_are_estimated_from_the_size_of_what_was_sent_and_received()
    {
        await using var h = await CreateAsync();
        var answerText = new string('y', 400);
        _stub.Answer(answerText, usage: null);

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var usage = answer.GetProperty("usage");
        Assert.True(usage.GetProperty("estimated").GetBoolean());
        var sent = Assert.Single(_stub.Requests()).Messages.Sum(m => System.Text.Encoding.UTF8.GetByteCount(m!["content"]!.GetValue<string>()));
        Assert.Equal((sent + 400 + 3) / 4, usage.GetProperty("totalTokens").GetInt32());
        var call = await OnlyCallAsync(h);
        Assert.True(call.GetProperty("tokens_estimated").GetBoolean());
        Assert.Equal(usage.GetProperty("totalTokens").GetInt32(), call.GetProperty("total_tokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("prompt_tokens").ValueKind);
    }

    private static Task InsertCallAsync(CleanDataHarness h, Guid project, Guid user, int tokens, string status = "succeeded", TimeSpan? ago = null) =>
        TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.assistant_calls (id, project_id, user_id, kind, action, model, prompt_kit, context, status, total_tokens, started_at)
            VALUES (gen_random_uuid(), @p, @u, 'clean', 'script', 'm', 1, '{}', @status, @tokens, now() - @ago)
            """, ("p", project), ("u", user), ("tokens", tokens), ("status", status), ("ago", ago ?? TimeSpan.Zero));

    // ---- failures -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_key_a_refused_credit_and_a_rate_limit_cost_nothing_and_are_reported_in_their_own_words()
    {
        await using var h = await CreateAsync();
        var cases = new (int Status, HttpStatusCode Expected, string Code)[]
        {
            (401, HttpStatusCode.ServiceUnavailable, "assistant.misconfigured"),
            (402, HttpStatusCode.ServiceUnavailable, "assistant.misconfigured"),
            (400, HttpStatusCode.ServiceUnavailable, "assistant.misconfigured"),
            (500, HttpStatusCode.BadGateway, "assistant.unavailable"),
            (418, HttpStatusCode.BadGateway, "assistant.unavailable"),
        };
        foreach (var (status, expected, code) in cases)
        {
            _stub.RespondRaw(status, """{"error":{"message":"PROVIDER-SECRET-BODY sk-leak"}}""");

            var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

            Assert.True(response.StatusCode == expected, $"{status}: answered {(int)response.StatusCode}.");
            Assert.Equal(code, Code(problem));
            Assert.DoesNotContain("PROVIDER-SECRET-BODY", problem.GetRawText());
        }

        _stub.RespondRaw(429, """{"error":"slow down"}""", new Dictionary<string, string> { ["Retry-After"] = "30" });
        var (limited, rate) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("assistant.rateLimited", Code(rate));
        Assert.Equal(30, rate.GetProperty("retryAfter").GetInt32());
        Assert.Equal("30", limited.Headers.GetValues("Retry-After").Single());
        var calls = await CallsAsync(h);
        Assert.Equal(cases.Length + 1, calls.GetArrayLength());
        foreach (var call in calls.EnumerateArray())
        {
            Assert.Equal("failed", call.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, call.GetProperty("total_tokens").ValueKind);
            Assert.False(call.GetProperty("tokens_estimated").GetBoolean());
        }

        Assert.Equal(["assistant.misconfigured", "assistant.misconfigured", "assistant.misconfigured", "assistant.unavailable", "assistant.unavailable", "assistant.rateLimited"],
            calls.EnumerateArray().Select(c => c.GetProperty("error_code").GetString()).ToArray());
        Assert.DoesNotContain(h.Factory.Logs.Lines, l => l.Contains("PROVIDER-SECRET-BODY") || l.Contains(AssistantStub.Key));
    }

    [Fact]
    public async Task A_rate_limit_without_a_hint_has_no_retry_after()
    {
        await using var h = await CreateAsync();
        _stub.RespondRaw(429, "{}");

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.False(problem.TryGetProperty("retryAfter", out _));
        Assert.False(response.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_wrong_key_is_reported_as_misconfigured_and_the_log_never_holds_it()
    {
        await using var h = await CreateAsync(new() { ["TAMIZA_ASSISTANT_API_KEY"] = "sk-wrong-key-77" });

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("assistant.misconfigured", Code(problem));
        Assert.Contains(h.Factory.Logs.Lines, l => l.Contains("401"));
        Assert.DoesNotContain(h.Factory.Logs.Lines, l => l.Contains("sk-wrong-key-77"));
        Assert.DoesNotContain("sk-wrong-key-77", problem.GetRawText());
    }

    [Fact]
    public async Task An_answer_that_failed_while_writing_gives_no_proposal()
    {
        await using var h = await CreateAsync();
        _stub.Answer("def compute(cleaned, ctx):\n    par", finishReason: "error");

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("assistant.unavailable", Code(problem));
        Assert.DoesNotContain("def compute", problem.GetRawText());
        var call = await OnlyCallAsync(h);
        // The provider may have spent tokens on it: they are counted from its usage.
        Assert.Equal(120, call.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public async Task A_provider_that_does_not_answer_in_time_is_a_gateway_timeout_that_costs_an_estimate()
    {
        await using var h = await CreateAsync(services: s => s.Configure<AssistantHttpOptions>(o => o.TimeoutOverride = TimeSpan.FromMilliseconds(400)));
        _stub.RespondRaw(200, AssistantStub.ChatBody("late"), delay: TimeSpan.FromSeconds(5));

        var (response, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("assistant.timeout", Code(problem));
        var call = await OnlyCallAsync(h);
        Assert.Equal("failed", call.GetProperty("status").GetString());
        Assert.Equal("assistant.timeout", call.GetProperty("error_code").GetString());
        Assert.True(call.GetProperty("tokens_estimated").GetBoolean());
        Assert.True(call.GetProperty("total_tokens").GetInt32() > 0);
    }

    // ---- one request at a time and cancelling -------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_request_while_one_is_in_progress_is_refused_as_busy_and_an_other_user_is_not_held_back()
    {
        await using var h = await CreateAsync();
        _stub.RespondRaw(200, AssistantStub.ChatBody("slow"), delay: TimeSpan.FromSeconds(3));
        var first = PostAsync(h.Analyst, Url(h, "/completions"), Body());
        await WaitForAsync(async () => (await CallsAsync(h)).GetArrayLength() == 1, "the first record");

        var (second, problem) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        var (admin, _) = await PostAsync(h.Admin, Url(h, "/completions"), Body());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("assistant.busy", Code(problem));
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first).Response.StatusCode);
        // The refused request left no record, and the user can ask again.
        Assert.Equal(2, (await CallsAsync(h)).GetArrayLength());
        _stub.Answer("quick");
        var (third, _) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task Cancelling_ends_the_request_to_the_provider_and_records_it_as_cancelled_with_an_estimate()
    {
        await using var h = await CreateAsync();
        _stub.RespondRaw(200, AssistantStub.ChatBody("never read"), delay: TimeSpan.FromSeconds(60));
        using var cancel = new CancellationTokenSource();
        var request = PostAsync(h.Analyst, Url(h, "/completions"), Body(), cancel.Token);
        await WaitForAsync(async () => (await CallsAsync(h)).GetArrayLength() == 1, "the running record");
        // Give the api time to send the request, which the stub holds for a minute.
        await Task.Delay(500);
        var started = DateTime.UtcNow;

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await WaitForAsync(async () => (await OnlyCallAsync(h)).GetProperty("status").GetString() != "running", "the record to end", seconds: 20);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "The api kept waiting for the provider.");
        var call = await OnlyCallAsync(h);
        Assert.Equal("cancelled", call.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, call.GetProperty("error_code").ValueKind);
        Assert.True(call.GetProperty("tokens_estimated").GetBoolean());
        Assert.True(call.GetProperty("total_tokens").GetInt32() > 0);
        Assert.NotEqual(JsonValueKind.Null, call.GetProperty("finished_at").ValueKind);
        // The user can ask again at once, and the cancelled request's estimate counts toward the day.
        _stub.Answer("again");
        var (next, _) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    // ---- records ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_request_is_recorded_without_any_text_and_the_outcome_is_added_when_the_proposal_is_applied()
    {
        await using var h = await CreateAsync();
        var testId = await InsertTestAsync(h);
        const string secretAsk = "SECRET-ASK-TEXT";
        const string secretAnswer = "SECRET-ANSWER-TEXT";
        const string secretDraft = "SECRET_DRAFT_NAME";
        _stub.Answer($"{secretAnswer}\n```python\ndef compute(cleaned, ctx):\n    return 1\n```");
        var body = Body("fix", secretAsk);
        body["testId"] = testId;
        body["draft"] = Draft + $"{secretDraft} = 1\n";
        // The test ran this text, so it is not stale.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET code = @c WHERE id = @id", ("c", (string)body["draft"]!), ("id", testId));

        var (response, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), body);
        var callId = answer.GetProperty("callId").GetGuid();
        var outcome = await h.Analyst.PutAsJsonAsync(Url(h, $"/calls/{callId}/outcome"), new { applied = "script" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, outcome.StatusCode);
        var call = await OnlyCallAsync(h);
        Assert.Equal(callId, call.GetProperty("id").GetGuid());
        Assert.Equal(h.ProjectId, call.GetProperty("project_id").GetGuid());
        Assert.Equal(await UserIdAsync(h, AnalystEmail), call.GetProperty("user_id").GetGuid());
        Assert.Equal(("indicator", Indicator, "fix", AssistantStub.Model), (call.GetProperty("kind").GetString(), call.GetProperty("name").GetString(),
            call.GetProperty("action").GetString(), call.GetProperty("model").GetString()));
        Assert.Equal(AssistantPrompts.Version, call.GetProperty("prompt_kit").GetInt32());
        Assert.Equal(("succeeded", 100, 20, 120, false, "script"), (call.GetProperty("status").GetString(), call.GetProperty("prompt_tokens").GetInt32(),
            call.GetProperty("completion_tokens").GetInt32(), call.GetProperty("total_tokens").GetInt32(), call.GetProperty("tokens_estimated").GetBoolean(),
            call.GetProperty("outcome").GetString()));
        Assert.NotEqual(JsonValueKind.Null, call.GetProperty("finished_at").ValueKind);
        var context = call.GetProperty("context");
        Assert.True(context.GetProperty("hasTest").GetBoolean());
        foreach (var block in new[] { "rules", "examples", "project", "schema", "script1", "test", "draft", "turns" })
        {
            Assert.True(context.GetProperty("blocks").TryGetProperty(block, out var size) && size.GetInt32() > 0, $"No size for the block '{block}'.");
        }

        var row = call.GetRawText();
        foreach (var text in new[] { secretAsk, secretAnswer, secretDraft, "def compute", "KeyError", "Juan" })
        {
            Assert.DoesNotContain(text, row);
        }
    }

    [Fact]
    public async Task The_outcome_can_be_script_cell_or_insert_and_nothing_else()
    {
        await using var h = await CreateAsync();
        var (_, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        var url = Url(h, $"/calls/{answer.GetProperty("callId").GetGuid()}/outcome");

        foreach (var applied in new[] { "cell", "insert", "script" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.PutAsJsonAsync(url, new { applied })).StatusCode);
            Assert.Equal(applied, (await OnlyCallAsync(h)).GetProperty("outcome").GetString());
        }

        foreach (var invalid in new object[] { new { applied = "everything" }, new { applied = (string?)null }, new { } })
        {
            var response = await h.Analyst.PutAsJsonAsync(url, invalid);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal("script", (await OnlyCallAsync(h)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Only_the_requester_can_report_the_outcome_of_a_call_and_only_a_call_that_gave_a_proposal()
    {
        await using var h = await CreateAsync();
        var (_, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        var callId = answer.GetProperty("callId").GetGuid();
        _stub.RespondRaw(500, "{}");
        await PostAsync(h.Admin, Url(h, "/completions"), Body());
        var failed = (await CallsAsync(h)).EnumerateArray().Single(c => c.GetProperty("status").GetString() == "failed").GetProperty("id").GetGuid();

        var byAdmin = await h.Admin.PutAsJsonAsync(Url(h, $"/calls/{callId}/outcome"), new { applied = "script" });
        var ofFailed = await h.Admin.PutAsJsonAsync(Url(h, $"/calls/{failed}/outcome"), new { applied = "script" });
        var unknown = await h.Analyst.PutAsJsonAsync(Url(h, $"/calls/{Guid.NewGuid()}/outcome"), new { applied = "script" });

        Assert.Equal(HttpStatusCode.NotFound, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ofFailed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.All((await CallsAsync(h)).EnumerateArray(), c => Assert.Equal(JsonValueKind.Null, c.GetProperty("outcome").ValueKind));
    }

    [Fact]
    public async Task A_record_belongs_to_the_project_it_was_made_in()
    {
        await using var h = await CreateAsync();
        var analyst = await UserIdAsync(h, AnalystEmail);
        var other = await TestData.InsertProjectAsync(h.Db, analyst, "Another project");
        await TestData.AddMemberAsync(h.Db, other, analyst, "analyst");
        var (_, answer) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());

        var response = await h.Analyst.PutAsJsonAsync($"/api/v1/projects/{other}/assistant/calls/{answer.GetProperty("callId").GetGuid()}/outcome", new { applied = "script" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- OpenAPI ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_four_routes_are_in_the_openapi_document()
    {
        await using var h = await CreateAsync();

        var document = await h.Analyst.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths");
        var assistant = paths.GetProperty("/api/v1/projects/{projectId}/assistant");
        Assert.True(assistant.TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/assistant/context").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/assistant/completions").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/assistant/calls/{callId}/outcome").TryGetProperty("put", out _));
        Assert.Equal("Assistant", assistant.GetProperty("get").GetProperty("tags")[0].GetString());
    }

    // ---- startup --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task At_startup_requests_a_previous_process_left_running_are_failed_as_interrupted_and_newer_ones_are_left()
    {
        await using var h = await CreateAsync();
        var analyst = await UserIdAsync(h, AnalystEmail);
        var admin = await UserIdAsync(h, "ada@example.org");
        const string insert = """
            INSERT INTO tamiza.assistant_calls (id, project_id, user_id, kind, action, model, prompt_kit, context, status, started_at)
            VALUES (@id, @p, @u, 'clean', 'script', 'm', 1, '{}', 'running', now() + @offset)
            """;
        var old = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await TestData.ExecuteAsync(h.Db, insert, ("id", old), ("p", h.ProjectId), ("u", analyst), ("offset", TimeSpan.FromHours(-1)));
        await TestData.ExecuteAsync(h.Db, insert, ("id", newer), ("p", h.ProjectId), ("u", admin), ("offset", TimeSpan.FromHours(1)));

        // A new api process starts against the same database.
        await using var restarted = new TamizaApiFactory(h.Db);
        await TestData.SignInAsync(restarted, "ada", "ada@example.org");
        await WaitForAsync(async () => await TestData.ScalarAsync<string>(h.Db, "SELECT status FROM tamiza.assistant_calls WHERE id = @id", ("id", old)) != "running",
            "the startup step");

        var calls = (await CallsAsync(h)).EnumerateArray().ToDictionary(c => c.GetProperty("id").GetGuid());
        Assert.Equal(("failed", "assistant.interrupted"), (calls[old].GetProperty("status").GetString(), calls[old].GetProperty("error_code").GetString()));
        Assert.NotEqual(JsonValueKind.Null, calls[old].GetProperty("finished_at").ValueKind);
        Assert.Equal("running", calls[newer].GetProperty("status").GetString());
        Assert.Contains(restarted.Logs.Lines, l => l.Contains("interrupted") && l.Contains("1"));
        // The interrupted request no longer holds the user's one place.
        var (response, _) = await PostAsync(h.Analyst, Url(h, "/completions"), Body());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
