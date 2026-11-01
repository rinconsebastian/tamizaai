using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>PUT /projects/{id}/dashboards/{dashboardId}/colors</c>, the palette and colors kept by value a dashboard's GET returns, and a
/// widget's color and scale in its config (dashboard-colors). What a colors save does to the dashboard's versions is
/// <c>DashboardVersionTests</c>'.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardColorsTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static object Kept(string column, string value, int color) => new { column, value, color };

    private static string Palette(JsonElement dashboard) => dashboard.GetProperty("palette").GetString()!;

    /// <summary>The colors kept by value as <c>column|value|color</c>, in the order the dashboard returns them.</summary>
    private static string[] KeptColors(JsonElement colors) =>
        colors.GetProperty("valueColors").EnumerateArray()
            .Select(c => $"{c.GetProperty("column").GetString()}|{c.GetProperty("value").GetString()}|{c.GetProperty("color").GetInt32()}")
            .ToArray();

    private async Task<IndicatorsHarness> CreateAsync() => await IndicatorsHarness.CreateAsync(postgres);

    /// <summary>A project whose indicator <c>households</c> has published a table with a group, a series and a geometry column.</summary>
    private async Task<IndicatorsHarness> PublishedAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "households", "municipality text, water_source text, geometry text, value numeric, numerator numeric, denominator numeric",
            "('Cali', 'Piped', 'POINT(0 0)', 40, 4, 10), ('Palmira', 'Well', 'POINT(1 1)', 60, 6, 10)");
        return h;
    }

    private static object ColumnChart(object? display = null, string? seriesBy = null, string title = "By municipality") =>
        new { title, chartType = "column", config = new { indicator = "households", groupBy = "municipality", seriesBy, display } };

    private static JsonElement ConfigOf(JsonElement widget) => widget.GetProperty("config");

    private static Task<string> StoredConfigAsync(IndicatorsHarness h, JsonElement widget) =>
        TestData.ScalarAsync<string>(h.Db, "SELECT config::text FROM tamiza.dashboard_widgets WHERE id = @id", ("id", widget.GetProperty("id").GetGuid()))!;

    // Dashboard palette

    [Fact]
    public async Task A_dashboard_saved_before_palettes_has_the_palette_tamiza_and_keeps_no_colors()
    {
        await using var h = await CreateAsync();
        // Inserted as the scripts before palettes left dashboards: with no palette and no colors in the statement.
        var field = await h.InsertDashboardAsync("Field");

        foreach (var id in new[] { h.DashboardId, field })
        {
            var dashboard = await h.GetDashboardAsync(id, h.Viewer);
            Assert.Equal("tamiza", Palette(dashboard));
            Assert.Equal(JsonValueKind.Array, dashboard.GetProperty("valueColors").ValueKind);
            Assert.Equal(0, dashboard.GetProperty("valueColors").GetArrayLength());
        }
    }

    [Fact]
    public async Task A_dashboard_added_through_the_api_starts_on_tamiza_with_no_colors_kept()
    {
        await using var h = await CreateAsync();
        var added = await h.AddDashboardAsync("Field");

        var dashboard = await h.GetDashboardAsync(added);

        Assert.Equal("tamiza", Palette(dashboard));
        Assert.Equal(0, dashboard.GetProperty("valueColors").GetArrayLength());
    }

    [Fact]
    public async Task An_analyst_chooses_a_palette_that_every_member_reads_on_that_dashboard_only()
    {
        await using var h = await CreateAsync();
        var field = await h.AddDashboardAsync("Field");

        var saved = await h.SaveColorsAsync("earth", Array.Empty<object>());

        Assert.Equal("earth", Palette(saved));
        Assert.Equal(0, saved.GetProperty("valueColors").GetArrayLength());
        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            Assert.Equal("earth", Palette(await h.GetDashboardAsync(client: client)));
        }

        Assert.Equal("tamiza", Palette(await h.GetDashboardAsync(field, h.Viewer)));
        Assert.Equal("earth", await TestData.ScalarAsync<string>(h.Db, "SELECT palette FROM tamiza.dashboards WHERE id = @id", ("id", h.DashboardId)));
    }

    [Theory]
    [InlineData("tamiza")]
    [InlineData("institutional")]
    [InlineData("earth")]
    [InlineData("vivid")]
    public async Task Each_palette_is_saved_and_read_back(string palette)
    {
        await using var h = await CreateAsync();

        Assert.Equal(palette, Palette(await h.SaveColorsAsync(palette, Array.Empty<object>(), h.Admin)));
        Assert.Equal(palette, Palette(await h.GetDashboardAsync(client: h.Viewer)));
    }

    [Fact]
    public async Task A_viewer_cannot_change_the_palette()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutColorsAsync("earth", new[] { Kept("sex", "Female", 5) }, h.Viewer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal("tamiza", Palette(await h.GetDashboardAsync(client: h.Viewer)));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_the_404_of_a_project_that_does_not_exist()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutColorsAsync("earth", Array.Empty<object>(), h.Outsider);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_color_code_is_not_a_palette_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        foreach (var palette in new[] { "#ff0000", "neon", "Earth", "" })
        {
            var response = await h.PutColorsAsync(palette, Array.Empty<object>());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["Choose one of the palettes: tamiza, institutional, earth, vivid."], IndicatorsHarness.Errors(await ReadAsync(response), "palette"));
        }

        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_body_with_no_palette_or_no_list_is_refused_on_each()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var neither = await h.Analyst.PutAsJsonAsync(h.ColorsUrl(), new { });
        var noList = await h.PutColorsAsync("earth", null);

        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        var problem = await ReadAsync(neither);
        Assert.Single(IndicatorsHarness.Errors(problem, "palette"));
        Assert.Single(IndicatorsHarness.Errors(problem, "valueColors"));
        Assert.Equal(HttpStatusCode.BadRequest, noList.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(noList), "valueColors"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task Colors_saved_on_a_removed_dashboard_are_refused_as_not_found()
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        var response = await h.PutColorsAsync("earth", Array.Empty<object>(), dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Of_two_saves_the_later_wins()
    {
        await using var h = await CreateAsync();

        await h.SaveColorsAsync("earth", new[] { Kept("sex", "Female", 5) });
        await h.SaveColorsAsync("vivid", Array.Empty<object>(), h.Admin);

        var dashboard = await h.GetDashboardAsync();
        Assert.Equal("vivid", Palette(dashboard));
        Assert.Empty(KeptColors(dashboard));
    }

    // Colors kept by value

    [Fact]
    public async Task A_color_kept_for_a_value_is_saved_and_every_member_reads_it_in_the_order_it_was_saved()
    {
        await using var h = await CreateAsync();

        var saved = await h.SaveColorsAsync("earth", new[] { Kept("zone", "Rural", 3), Kept("sex", "Female", 5), Kept("sex", "Male", 1) });

        Assert.Equal(["zone|Rural|3", "sex|Female|5", "sex|Male|1"], KeptColors(saved));
        foreach (var client in new[] { h.Viewer, h.Analyst })
        {
            var dashboard = await h.GetDashboardAsync(client: client);
            Assert.Equal("earth", Palette(dashboard));
            Assert.Equal(["zone|Rural|3", "sex|Female|5", "sex|Male|1"], KeptColors(dashboard));
        }

        // Stored as a JSON list of camelCase objects, in the order sent.
        Assert.Equal("zone|Rural|3,sex|Female|5,sex|Male|1", await TestData.ScalarAsync<string>(h.Db,
            """
            SELECT string_agg(format('%s|%s|%s', e ->> 'column', e ->> 'value', e ->> 'color'), ',' ORDER BY n)
            FROM tamiza.dashboards, jsonb_array_elements(value_colors) WITH ORDINALITY AS t (e, n)
            WHERE id = @id
            """,
            ("id", h.DashboardId)));
    }

    [Fact]
    public async Task The_same_color_in_two_columns_is_saved()
    {
        await using var h = await CreateAsync();

        var saved = await h.SaveColorsAsync("tamiza", new[] { Kept("sex", "Female", 5), Kept("zone", "Rural", 5) });

        Assert.Equal(["sex|Female|5", "zone|Rural|5"], KeptColors(saved));
    }

    [Fact]
    public async Task A_color_used_twice_in_a_column_is_refused_on_the_second_entry_and_nothing_is_saved()
    {
        await using var h = await CreateAsync();
        await h.SaveColorsAsync("earth", new[] { Kept("zone", "Rural", 2) });
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutColorsAsync("vivid", new[] { Kept("sex", "Female", 5), Kept("sex", "Male", 5) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(["sex already uses color 5."], IndicatorsHarness.Errors(problem, "valueColors[1]"));
        Assert.DoesNotContain("valueColors[0]", problem.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_value_kept_twice_in_a_column_is_refused_on_the_second_entry()
    {
        await using var h = await CreateAsync();

        var response = await h.PutColorsAsync("earth", new[] { Kept("sex", "Female", 5), Kept("sex", "Female", 6) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["sex already keeps a color for Female."], IndicatorsHarness.Errors(await ReadAsync(response), "valueColors[1]"));
    }

    [Fact]
    public async Task Each_entry_is_refused_on_its_own_field()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutColorsAsync("earth", new object?[]
        {
            Kept("Sex", "Female", 5),
            new { column = "sex", value = (string?)null, color = 5 },
            Kept("sex", new string('v', 501), 5),
            Kept("sex", "Male", 9),
            Kept("sex", "Other", 0),
            new { column = "sex", value = "Unknown" },
            null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["valueColors[0].column", "valueColors[1].value", "valueColors[2].value", "valueColors[3].color", "valueColors[4].color", "valueColors[5].color", "valueColors[6]"],
            errors);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_dashboard_keeps_64_colors_and_not_65()
    {
        await using var h = await CreateAsync();
        object[] Entries(int count) => [.. Enumerable.Range(0, count).Select(i => Kept($"column_{i / 8}", $"value {i}", i % 8 + 1))];

        var saved = await h.SaveColorsAsync("earth", Entries(64));
        var before = await h.StoredDashboardsAsync();
        var response = await h.PutColorsAsync("earth", Entries(65));

        Assert.Equal(64, saved.GetProperty("valueColors").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A dashboard keeps at most 64 colors by value."], IndicatorsHarness.Errors(await ReadAsync(response), "valueColors"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_color_is_kept_for_a_value_no_table_has_and_a_value_may_be_empty_or_differ_only_in_case()
    {
        await using var h = await CreateAsync();

        var saved = await h.SaveColorsAsync("earth", new[] { Kept("water_source", "Lake", 4), Kept("water_source", "", 2), Kept("water_source", "piped", 6), Kept("water_source", "Piped", 7) });

        Assert.Equal(["water_source|Lake|4", "water_source||2", "water_source|piped|6", "water_source|Piped|7"], KeptColors(saved));
    }

    [Fact]
    public async Task Saving_no_colors_clears_them_and_keeps_the_palette_sent()
    {
        await using var h = await CreateAsync();
        await h.SaveColorsAsync("earth", new[] { Kept("sex", "Female", 5) });

        var saved = await h.SaveColorsAsync("earth", Array.Empty<object>());

        Assert.Empty(KeptColors(saved));
        Assert.Empty(KeptColors(await h.GetDashboardAsync()));
        Assert.Equal("earth", Palette(await h.GetDashboardAsync()));
    }

    // Saving what is stored

    [Fact]
    public async Task Saving_the_colors_a_dashboard_has_writes_nothing_and_keeps_who_and_when()
    {
        await using var h = await CreateAsync();
        await h.SaveColorsAsync("earth", new[] { Kept("sex", "Female", 5), Kept("sex", "Male", 1) });
        var stored = await h.StoredDashboardsAsync();

        // Another member sends the same palette and entries again, the whole and in the same order.
        var again = await h.SaveColorsAsync("earth", new[] { Kept("sex", "Female", 5), Kept("sex", "Male", 1) }, h.Admin);

        Assert.Equal("earth", Palette(again));
        Assert.Equal(["sex|Female|5", "sex|Male|1"], KeptColors(again));
        Assert.Equal(stored, await h.StoredDashboardsAsync()); // updated_at and updated_by included
    }

    [Fact]
    public async Task Saving_the_default_colors_of_a_dashboard_saved_before_palettes_writes_nothing()
    {
        await using var h = await CreateAsync();
        var stored = await h.StoredDashboardsAsync();

        var saved = await h.SaveColorsAsync("tamiza", Array.Empty<object>());

        Assert.Equal("tamiza", Palette(saved));
        Assert.Equal(stored, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task The_same_colors_in_another_order_are_a_change()
    {
        await using var h = await CreateAsync();
        await h.SaveColorsAsync("earth", new[] { Kept("sex", "Female", 5), Kept("sex", "Male", 1) });
        var stored = await h.StoredDashboardsAsync();

        await h.SaveColorsAsync("earth", new[] { Kept("sex", "Male", 1), Kept("sex", "Female", 5) }, h.Admin);

        Assert.NotEqual(stored, await h.StoredDashboardsAsync());
        Assert.Equal(["sex|Male|1", "sex|Female|5"], KeptColors(await h.GetDashboardAsync()));
    }

    [Fact]
    public async Task Saving_colors_records_who_and_when_and_touches_nothing_else()
    {
        await using var h = await CreateAsync();
        var widgets = await h.StoredWidgetsAsync();
        var before = (await h.StoredDashboardsAsync()).Single().Split('|');

        await h.SaveColorsAsync("vivid", new[] { Kept("sex", "Female", 5) });

        var after = (await h.StoredDashboardsAsync()).Single().Split('|');
        // position, id, name and normalized name, then the filters, stay; the editor and the time, the palette and the colors change.
        Assert.Equal(before[..5], after[..5]);
        Assert.NotEqual(before[5], after[5]);
        Assert.NotEqual(before[6], after[6]);
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db,
            "SELECT count(*) FROM tamiza.dashboards d JOIN tamiza.users u ON u.id = d.updated_by WHERE d.id = @id AND u.name = 'grace'",
            ("id", h.DashboardId)));
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    // Widget color

    [Fact]
    public async Task A_column_chart_with_no_series_is_saved_in_color_3_and_read_back_in_it()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));

        Assert.Equal(3, ConfigOf(created).GetProperty("display").GetProperty("color").GetInt32());
        Assert.False(ConfigOf(created).GetProperty("display").TryGetProperty("ramp", out _));
        var read = (await h.GetWidgetsAsync(client: h.Viewer))[0];
        Assert.Equal(3, ConfigOf(read).GetProperty("display").GetProperty("color").GetInt32());
        Assert.Contains("\"display\": {\"color\": 3}", await StoredConfigAsync(h, created));
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    public async Task A_bar_column_or_line_chart_with_no_series_takes_a_color(string chartType)
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(new { title = "W", chartType, config = new { indicator = "households", groupBy = "municipality", display = new { color = 8 } } });

        Assert.Equal(8, ConfigOf(created).GetProperty("display").GetProperty("color").GetInt32());
    }

    [Fact]
    public async Task A_histogram_takes_a_color()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(new { title = "Spread", chartType = "histogram", config = new { indicator = "households", measure = "value", display = new { color = 2 } } });

        Assert.Equal(2, ConfigOf(created).GetProperty("display").GetProperty("color").GetInt32());
    }

    [Fact]
    public async Task A_widget_saved_without_a_color_has_no_display_in_what_is_stored_or_returned()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(ColumnChart());

        Assert.False(ConfigOf(created).TryGetProperty("display", out _));
        Assert.False(ConfigOf((await h.GetWidgetsAsync())[0]).TryGetProperty("display", out _));
        Assert.DoesNotContain("display", await StoredConfigAsync(h, created));
        // Neither does a display that sets nothing.
        var empty = await h.AddWidgetAsync(ColumnChart(new { }, title: "Empty display"));
        Assert.False(ConfigOf(empty).TryGetProperty("display", out _));
        Assert.DoesNotContain("display", await StoredConfigAsync(h, empty));
    }

    [Fact]
    public async Task A_widget_saved_before_colors_reads_without_a_color_and_keeps_its_stored_config_when_renamed()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(ColumnChart());
        var id = created.GetProperty("id").GetGuid();
        // As an earlier release stored it: no display, and fields that have since been added left out.
        await h.ExecuteAsync(
            """UPDATE tamiza.dashboard_widgets SET config = '{"indicator":"households","groupBy":"municipality"}' WHERE id = @id""", ("id", id));

        var read = (await h.GetWidgetsAsync(client: h.Viewer))[0];
        var renamed = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { title = "Renamed" });

        Assert.False(ConfigOf(read).TryGetProperty("display", out _));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.False(ConfigOf(await ReadAsync(renamed)).TryGetProperty("display", out _));
        Assert.Equal("""{"groupBy": "municipality", "indicator": "households"}""", await StoredConfigAsync(h, created));
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    public async Task A_chart_with_a_series_is_refused_a_color_on_the_color(string chartType)
    {
        await using var h = await PublishedAsync();
        var before = await h.StoredWidgetsAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "By source", chartType, config = new { indicator = "households", groupBy = "municipality", seriesBy = "water_source", display = new { color = 3 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A widget with a series takes its colors from its series."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.color"));
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_widget_of_any_other_kind_is_refused_a_color_on_the_color()
    {
        await using var h = await PublishedAsync();
        var kinds = new (string Type, object Config)[]
        {
            ("stat", new { indicator = "households", display = new { color = 3 } }),
            ("pie", new { indicator = "households", groupBy = "municipality", display = new { color = 3 } }),
            ("map", new { indicator = "households", groupBy = "municipality", geometryColumn = "geometry", display = new { color = 3 } }),
            ("heatmap", new { indicator = "households", groupBy = "municipality", seriesBy = "water_source", display = new { color = 3 } }),
        };

        foreach (var (type, config) in kinds)
        {
            var response = await h.PostWidgetAsync(new { title = type, chartType = type, config });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal([$"A {type} widget has no color of its own."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.color"));
        }

        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task A_color_outside_1_to_8_is_refused_on_the_color(int color)
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(ColumnChart(new { color }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["The color must be a number from 1 to 8."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.color"));
    }

    [Fact]
    public async Task Gaining_a_series_while_keeping_a_color_is_refused_on_the_color_and_nothing_is_saved()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var before = await h.StoredWidgetsAsync();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new
        {
            config = new { indicator = "households", groupBy = "municipality", seriesBy = "water_source", display = new { color = 3 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "config.display.color"));
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task Changing_the_chart_type_of_a_widget_that_keeps_a_color_is_checked_against_its_color()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var url = h.WidgetUrl(created.GetProperty("id").GetGuid());

        var toPie = await h.Analyst.PatchJsonAsync(url, new { chartType = "pie" });
        var toBar = await h.Analyst.PatchJsonAsync(url, new { chartType = "bar" });

        Assert.Equal(HttpStatusCode.BadRequest, toPie.StatusCode);
        Assert.Equal(["A pie widget has no color of its own."], IndicatorsHarness.Errors(await ReadAsync(toPie), "config.display.color"));
        Assert.Equal(HttpStatusCode.OK, toBar.StatusCode);
        Assert.Equal(3, ConfigOf(await ReadAsync(toBar)).GetProperty("display").GetProperty("color").GetInt32());
    }

    // Widget sequential scale

    [Fact]
    public async Task A_map_is_saved_on_the_green_scale_and_read_back_on_it()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(new
        {
            title = "Map", chartType = "map", config = new { indicator = "households", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp = "green" } },
        });

        Assert.Equal("green", ConfigOf(created).GetProperty("display").GetProperty("ramp").GetString());
        Assert.False(ConfigOf(created).GetProperty("display").TryGetProperty("color", out _));
        Assert.Equal("green", ConfigOf((await h.GetWidgetsAsync(client: h.Viewer))[0]).GetProperty("display").GetProperty("ramp").GetString());
    }

    [Fact]
    public async Task Every_scale_is_saved_on_a_heat_map()
    {
        await using var h = await PublishedAsync();

        foreach (var scale in new[] { "blue", "teal", "green", "orange", "red", "purple", "gray" })
        {
            var created = await h.AddWidgetAsync(new
            {
                title = scale, chartType = "heatmap",
                config = new { indicator = "households", groupBy = "municipality", seriesBy = "water_source", display = new { ramp = scale } },
            });

            Assert.Equal(scale, ConfigOf(created).GetProperty("display").GetProperty("ramp").GetString());
        }
    }

    [Fact]
    public async Task A_heat_map_with_no_scale_has_no_display_so_it_uses_its_palettes_scale()
    {
        await using var h = await PublishedAsync();

        var created = await h.AddWidgetAsync(new
        {
            title = "Heat", chartType = "heatmap", config = new { indicator = "households", groupBy = "municipality", seriesBy = "water_source" },
        });

        Assert.False(ConfigOf(created).TryGetProperty("display", out _));
    }

    [Fact]
    public async Task A_scale_on_a_pie_chart_is_refused_on_the_scale()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostWidgetAsync(new
        {
            title = "Share", chartType = "pie", config = new { indicator = "households", groupBy = "municipality", display = new { ramp = "green" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Only map and heatmap widgets have a scale."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.ramp"));
        Assert.Empty(await h.StoredWidgetsAsync());
    }

    [Theory]
    [InlineData("stat")]
    [InlineData("bar")]
    [InlineData("column")]
    [InlineData("line")]
    [InlineData("histogram")]
    public async Task A_scale_on_any_other_kind_is_refused_on_the_scale(string chartType)
    {
        await using var h = await PublishedAsync();
        var config = new { indicator = "households", measure = "value", groupBy = chartType is "stat" or "histogram" ? null : "municipality", display = new { ramp = "green" } };

        var response = await h.PostWidgetAsync(new { title = "W", chartType, config });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "config.display.ramp"));
    }

    [Fact]
    public async Task A_scale_that_is_not_one_of_the_seven_is_refused_on_the_scale()
    {
        await using var h = await PublishedAsync();

        foreach (var ramp in new[] { "Green", "#00ff00", "earth", "magenta" })
        {
            var response = await h.PostWidgetAsync(new
            {
                title = "Map", chartType = "map",
                config = new { indicator = "households", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp } },
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["The scale must be one of blue, teal, green, orange, red, purple, gray."], IndicatorsHarness.Errors(await ReadAsync(response), "config.display.ramp"));
        }
    }

    // A widget follows its dashboard

    [Fact]
    public async Task A_widget_keeps_its_color_when_it_is_moved_to_a_dashboard_with_another_palette()
    {
        await using var h = await PublishedAsync();
        var field = await h.AddDashboardAsync("Field");
        await h.SaveColorsAsync("earth", Array.Empty<object>());
        await h.SaveColorsAsync("institutional", Array.Empty<object>(), dashboardId: field);
        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var stored = await StoredConfigAsync(h, created);

        var moved = await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { dashboardId = field });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var landed = (await h.GetWidgetsAsync(field, h.Viewer))[0];
        Assert.Equal(3, ConfigOf(landed).GetProperty("display").GetProperty("color").GetInt32());
        Assert.Equal(stored, await StoredConfigAsync(h, created)); // the config is not even rewritten
        Assert.Equal(0, (await h.GetWidgetsAsync()).GetArrayLength());
        // Each dashboard keeps its own palette, which is what the widget is drawn in.
        Assert.Equal("earth", Palette(await h.GetDashboardAsync()));
        Assert.Equal("institutional", Palette(await h.GetDashboardAsync(field)));
    }

    [Fact]
    public async Task A_widget_keeps_its_scale_when_it_is_moved()
    {
        await using var h = await PublishedAsync();
        var field = await h.AddDashboardAsync("Field");
        var created = await h.AddWidgetAsync(new
        {
            title = "Map", chartType = "map", config = new { indicator = "households", groupBy = "municipality", geometryColumn = "geometry", display = new { ramp = "green" } },
        });

        await h.Analyst.PatchJsonAsync(h.WidgetUrl(created.GetProperty("id").GetGuid()), new { dashboardId = field });

        Assert.Equal("green", ConfigOf((await h.GetWidgetsAsync(field))[0]).GetProperty("display").GetProperty("ramp").GetString());
    }

    [Fact]
    public async Task Changing_the_palette_does_not_touch_the_widgets()
    {
        await using var h = await PublishedAsync();
        await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var widgets = await h.StoredWidgetsAsync();

        await h.SaveColorsAsync("vivid", new[] { Kept("municipality", "Cali", 2) });

        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    // A widget with a color through PATCH and GET

    [Fact]
    public async Task A_widget_with_a_color_read_back_and_saved_again_through_patch_is_unchanged()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var url = h.WidgetUrl(created.GetProperty("id").GetGuid());
        var widgets = await h.StoredWidgetsAsync();

        // What the form does when it is saved without a change: it sends the config it was given.
        var read = (await h.GetWidgetsAsync(client: h.Viewer))[0];
        var saved = await h.Analyst.PatchJsonAsync(url, new { title = read.GetProperty("title").GetString(), chartType = "column", config = read.GetProperty("config") });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(read.GetProperty("config").GetRawText(), ConfigOf(await ReadAsync(saved)).GetRawText());
        Assert.Equal(widgets, await h.StoredWidgetsAsync()); // updated_at included
        Assert.Equal(read.GetRawText(), (await h.GetWidgetsAsync())[0].GetRawText());
    }

    [Fact]
    public async Task A_widgets_color_is_changed_and_removed_through_patch()
    {
        await using var h = await PublishedAsync();
        var created = await h.AddWidgetAsync(ColumnChart(new { color = 3 }));
        var url = h.WidgetUrl(created.GetProperty("id").GetGuid());

        var changed = await h.Analyst.PatchJsonAsync(url, new { config = new { indicator = "households", groupBy = "municipality", display = new { color = 5 } } });
        var title = await h.Analyst.PatchJsonAsync(url, new { title = "Renamed" });
        var removed = await h.Analyst.PatchJsonAsync(url, new { config = new { indicator = "households", groupBy = "municipality" } });

        Assert.Equal(5, ConfigOf(await ReadAsync(changed)).GetProperty("display").GetProperty("color").GetInt32());
        Assert.Equal(5, ConfigOf(await ReadAsync(title)).GetProperty("display").GetProperty("color").GetInt32()); // a rename leaves the query and display
        Assert.False(ConfigOf(await ReadAsync(removed)).TryGetProperty("display", out _));
        Assert.False(ConfigOf((await h.GetWidgetsAsync())[0]).TryGetProperty("display", out _));
    }

    // A query ignores how a widget is drawn

    [Fact]
    public async Task A_query_answers_the_same_with_or_without_a_display_in_its_config()
    {
        await using var h = await PublishedAsync();

        var plain = await h.QueryAsync(new { indicator = "households", groupBy = "municipality" }, "column");
        var drawn = await h.QueryAsync(new { indicator = "households", groupBy = "municipality", display = new { color = 3 } }, "column");

        Assert.Equal(plain.GetRawText(), drawn.GetRawText());
    }

    [Fact]
    public async Task A_query_with_a_color_outside_1_to_8_is_refused_on_the_color()
    {
        await using var h = await PublishedAsync();

        var response = await h.PostQueryAsync(new { indicator = "households", groupBy = "municipality", display = new { color = 9 } }, "column");

        // The query's config is the body's own, so its errors are not prefixed by "config.".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "display.color"));
    }
}
