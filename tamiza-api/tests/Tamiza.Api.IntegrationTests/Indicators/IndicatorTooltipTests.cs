using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>A map's tooltip columns and basemap: saving them, and the values the map query returns for them.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorTooltipTests(PostgresFixture postgres)
{
    private const string Columns =
        "block text, round text, address text, target bigint, achieved bigint, progress numeric, visited_on date, seen_at timestamptz, ok boolean, "
        + "value numeric, numerator numeric, denominator numeric, geometry text";

    private static string Row(
        string block, string address = "NULL", string target = "NULL", string achieved = "NULL", string progress = "NULL", string round = "'1'",
        string visitedOn = "NULL", string seenAt = "NULL", string ok = "NULL", string value = "1", string numerator = "NULL", string denominator = "NULL") =>
        $"('{block}', {round}, {address}, {target}, {achieved}, {progress}, {visitedOn}, {seenAt}, {ok}, {value}, {numerator}, {denominator}, 'POINT(1 1)')";

    private async Task<IndicatorsHarness> WithAsync(params string[] rows)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("survey_progress", Columns, string.Join(",\n", rows), format: "percent");
        return h;
    }

    private static object Tooltip(params (string Column, string? Label, string? Format)[] items) =>
        items.Select(i => new { column = i.Column, label = i.Label, format = i.Format }).ToArray();

    private static object MapConfig(object? tooltip = null, object? filters = null, string? basemap = null, object? ratio = null) => new
    {
        indicator = "survey_progress", groupBy = "block", geometryColumn = "geometry", tooltip, filters, basemap, ratio,
    };

    private static object MapWidget(object? config = null, string title = "Blocks") =>
        new { title, chartType = "map", config = config ?? MapConfig() };

    private static JsonElement Single(JsonElement result) => result.GetProperty("rows").EnumerateArray().Single();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    // Saving

    [Fact]
    public async Task An_analyst_saves_a_map_with_three_tooltip_columns_and_a_basemap_and_every_member_reads_them_back()
    {
        await using var h = await WithAsync(Row("0042"));
        var tooltip = Tooltip(("address", "Dirección", null), ("target", "Meta muestral", "count"), ("achieved", null, null));

        var response = await h.PostWidgetAsync(MapWidget(MapConfig(tooltip, basemap: "satellite")));
        var created = (await ReadAsync(response)).GetProperty("config");
        var saved = (await h.GetWidgetsAsync(client: h.Viewer))[0].GetProperty("config");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        foreach (var config in new[] { created, saved })
        {
            Assert.Equal("satellite", config.GetProperty("basemap").GetString());
            var items = config.GetProperty("tooltip").EnumerateArray().ToArray();
            Assert.Equal(["address", "target", "achieved"], items.Select(i => i.GetProperty("column").GetString()).ToArray());
            Assert.Equal(["Dirección", "Meta muestral", null], items.Select(i => i.GetProperty("label").GetString()).ToArray());
            Assert.Equal([null, "count", null], items.Select(i => i.GetProperty("format").GetString()).ToArray());
        }
    }

    [Fact]
    public async Task A_stored_config_without_tooltip_and_basemap_reads_as_no_columns_and_no_basemap()
    {
        await using var h = await WithAsync(Row("0042"));
        var widget = await h.AddWidgetAsync(MapWidget(MapConfig(Tooltip(("address", null, null)), basemap: "streets")));
        await h.ExecuteAsync("UPDATE tamiza.dashboard_widgets SET config = config - 'tooltip' - 'basemap' WHERE id = @id", ("id", widget.GetProperty("id").GetGuid()));

        var config = (await h.GetWidgetsAsync())[0].GetProperty("config");

        Assert.Equal(0, config.GetProperty("tooltip").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("basemap").ValueKind);
    }

    [Fact]
    public async Task A_widget_created_without_either_field_has_an_empty_tooltip_and_no_basemap()
    {
        await using var h = await WithAsync(Row("0042"));

        var config = (await h.AddWidgetAsync(MapWidget())).GetProperty("config");

        Assert.Equal(0, config.GetProperty("tooltip").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("basemap").ValueKind);
    }

    [Fact]
    public async Task Six_tooltip_columns_are_rejected_with_a_message_naming_the_limit()
    {
        await using var h = await WithAsync(Row("0042"));
        var six = Tooltip(("address", null, null), ("target", null, null), ("achieved", null, null), ("progress", null, null), ("round", null, null), ("ok", null, null));

        var response = await h.PostWidgetAsync(MapWidget(MapConfig(six)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A map has at most 5 tooltip columns."], IndicatorsHarness.Errors(await ReadAsync(response), "config.tooltip"));
    }

    [Fact]
    public async Task Tooltip_columns_on_another_chart_type_are_rejected_on_the_tooltip()
    {
        await using var h = await WithAsync(Row("0042"));

        var response = await h.PostWidgetAsync(new
        {
            title = "Column", chartType = "column",
            config = new { indicator = "survey_progress", groupBy = "block", tooltip = Tooltip(("address", null, null)) },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Only map widgets can have tooltip columns."], IndicatorsHarness.Errors(await ReadAsync(response), "config.tooltip"));
    }

    [Fact]
    public async Task A_basemap_on_another_chart_type_is_rejected_on_the_basemap()
    {
        await using var h = await WithAsync(Row("0042"));

        var response = await h.PostWidgetAsync(new
        {
            title = "Pie", chartType = "pie", config = new { indicator = "survey_progress", groupBy = "block", basemap = "streets" },
        });
        var problem = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Only map widgets can have a basemap."], IndicatorsHarness.Errors(problem, "config.basemap"));
    }

    [Fact]
    public async Task A_basemap_that_is_neither_streets_nor_satellite_is_rejected()
    {
        await using var h = await WithAsync(Row("0042"));

        var response = await h.PostWidgetAsync(MapWidget(MapConfig(basemap: "hybrid")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["The basemap must be 'streets' or 'satellite'."], IndicatorsHarness.Errors(await ReadAsync(response), "config.basemap"));
    }

    [Fact]
    public async Task A_format_is_offered_for_numbers_only_and_an_unknown_column_is_named()
    {
        await using var h = await WithAsync(Row("0042"));

        var text = await h.PostWidgetAsync(MapWidget(MapConfig(Tooltip(("address", null, "percent")))));
        var unknown = await h.PostWidgetAsync(MapWidget(MapConfig(Tooltip(("target", null, null), ("gone", null, null)))));
        var number = await h.PostWidgetAsync(MapWidget(MapConfig(Tooltip(("progress", null, "percent")))));

        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Equal(["Only a number column can have a format."], IndicatorsHarness.Errors(await ReadAsync(text), "config.tooltip[0].format"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(["The table has no column 'gone'."], IndicatorsHarness.Errors(await ReadAsync(unknown), "config.tooltip[1].column"));
        Assert.Equal(HttpStatusCode.Created, number.StatusCode);
    }

    [Fact]
    public async Task A_widget_can_be_saved_with_tooltip_columns_before_its_indicator_has_published()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.AddIndicatorAsync("survey_progress");

        var response = await h.PostWidgetAsync(MapWidget(MapConfig(Tooltip(("address", null, "percent")))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode); // only what needs no table is checked
    }

    [Fact]
    public async Task A_patch_of_only_the_chart_type_rechecks_the_saved_tooltip_and_a_move_keeps_both_fields()
    {
        await using var h = await WithAsync(Row("0042"));
        var widget = await h.AddWidgetAsync(MapWidget(MapConfig(Tooltip(("address", "Dirección", null)), basemap: "streets")));
        var id = widget.GetProperty("id").GetGuid();

        var toBar = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { chartType = "bar" });
        var moved = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { layout = new { x = 6, y = 0, w = 6, h = 6 } });

        Assert.Equal(HttpStatusCode.BadRequest, toBar.StatusCode);
        Assert.Contains("config.tooltip", (await ReadAsync(toBar)).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var config = (await ReadAsync(moved)).GetProperty("config");
        Assert.Equal("address", config.GetProperty("tooltip")[0].GetProperty("column").GetString());
        Assert.Equal("streets", config.GetProperty("basemap").GetString());
    }

    [Fact]
    public async Task A_map_turned_into_a_bar_chart_with_empty_fields_has_no_tooltip_columns()
    {
        await using var h = await WithAsync(Row("0042"));
        var id = (await h.AddWidgetAsync(MapWidget(MapConfig(Tooltip(("address", null, null)), basemap: "streets")))).GetProperty("id").GetGuid();

        var response = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new
        {
            chartType = "bar",
            config = new { indicator = "survey_progress", groupBy = "block", tooltip = Array.Empty<object>(), basemap = (string?)null },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = (await ReadAsync(response)).GetProperty("config");
        Assert.Equal(0, config.GetProperty("tooltip").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("basemap").ValueKind);
    }

    // Values

    [Fact]
    public async Task One_row_per_block_each_tooltip_column_shows_that_rows_value_and_the_row_count_is_one()
    {
        await using var h = await WithAsync(Row("0042", address: "'Cra 5 # 10-20'", target: "20", achieved: "9", progress: "0.45", value: "0.45"));

        var result = await h.QueryAsync(
            MapConfig(Tooltip(("address", null, null), ("target", null, null), ("achieved", null, null), ("progress", null, null))), "map");

        var row = Single(result);
        Assert.Equal("0042", row.GetProperty("group").GetString());
        Assert.Equal(1, row.GetProperty("rows").GetInt64());
        var tooltip = row.GetProperty("tooltip");
        Assert.Equal("Cra 5 # 10-20", tooltip[0].GetString());
        Assert.Equal((20m, 9m, 0.45m), (tooltip[1].GetDecimal(), tooltip[2].GetDecimal(), tooltip[3].GetDecimal()));
        Assert.Equal(["text", "integer", "integer", "decimal"], result.GetProperty("tooltipTypes").EnumerateArray().Select(t => t.GetString()).ToArray());
    }

    [Fact]
    public async Task Two_rows_for_one_block_sum_the_numbers_and_show_the_smallest_text_and_say_how_many_rows()
    {
        await using var h = await WithAsync(
            Row("0042", address: "'Cra 5 # 10-22'", target: "12"), Row("0042", address: "'Cra 5 # 10-20'", target: "8"), Row("0043", address: "'Calle 1'", target: "5"));

        var result = await h.QueryAsync(MapConfig(Tooltip(("address", null, null), ("target", null, null))), "map");

        var block = result.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("group").GetString() == "0042");
        Assert.Equal(2, block.GetProperty("rows").GetInt64());
        Assert.Equal("Cra 5 # 10-20", block.GetProperty("tooltip")[0].GetString());
        Assert.Equal(20m, block.GetProperty("tooltip")[1].GetDecimal());
    }

    [Fact]
    public async Task The_widgets_filters_apply_to_the_tooltip_and_to_the_row_count()
    {
        await using var h = await WithAsync(
            Row("0042", target: "12", round: "'1'", address: "'Old'"), Row("0042", target: "8", round: "'2'", address: "'New'"));
        var filters = new[] { new { column = "round", op = "equals", value = "2" } };

        var row = Single(await h.QueryAsync(MapConfig(Tooltip(("target", null, null), ("address", null, null)), filters), "map"));

        Assert.Equal(1, row.GetProperty("rows").GetInt64());
        Assert.Equal(8m, row.GetProperty("tooltip")[0].GetDecimal());
        Assert.Equal("New", row.GetProperty("tooltip")[1].GetString());
    }

    [Fact]
    public async Task An_empty_text_counts_as_no_value_and_rows_of_only_empty_texts_give_null()
    {
        await using var h = await WithAsync(
            Row("0042", address: "''"), Row("0042", address: "'Cra 5 # 10-20'"), Row("0043", address: "''"), Row("0043", address: "''"));

        var result = await h.QueryAsync(MapConfig(Tooltip(("address", null, null))), "map");

        var rows = result.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!, r => r.GetProperty("tooltip")[0]);
        Assert.Equal("Cra 5 # 10-20", rows["0042"].GetString());
        Assert.Equal(JsonValueKind.Null, rows["0043"].ValueKind);
    }

    [Fact]
    public async Task A_tooltip_column_a_script_change_removed_is_reported_as_missing_after_the_other_columns()
    {
        await using var h = await WithAsync(Row("0042"));
        await h.PublishAsync("survey_progress", "block text, value numeric, geometry text", "('0042', 1, 'POINT(1 1)')");

        var result = await h.QueryAsync(MapConfig(Tooltip(("address", null, null), ("target", null, null))), "map");

        Assert.Equal(["address", "target"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
    }

    [Fact]
    public async Task A_ratio_widget_counts_rows_that_have_a_numerator_but_no_denominator()
    {
        await using var h = await WithAsync(
            Row("0042", numerator: "4", denominator: "10", target: "3"), Row("0042", numerator: "2", denominator: "NULL", target: "4"),
            Row("0042", numerator: "NULL", denominator: "NULL", target: "100"));
        var ratio = new { numerator = "numerator", denominator = "denominator" };

        var row = Single(await h.QueryAsync(MapConfig(Tooltip(("target", null, null)), ratio: ratio), "map"));

        Assert.Equal(0.6, row.GetProperty("value").GetDouble(), 6);
        Assert.Equal(2, row.GetProperty("rows").GetInt64()); // the row with neither is not one of the group's rows
        Assert.Equal(7m, row.GetProperty("tooltip")[0].GetDecimal());
    }

    [Fact]
    public async Task A_timestamp_is_the_earliest_in_the_projects_time_zone_with_its_offset()
    {
        await using var h = await WithAsync(
            Row("0042", seenAt: "'2026-09-02T10:00:00-05:00'"), Row("0042", seenAt: "'2026-09-01T21:30:05-05:00'"));
        await h.ExecuteAsync("UPDATE tamiza.projects SET time_zone = 'America/Bogota'");

        var row = Single(await h.QueryAsync(MapConfig(Tooltip(("seen_at", null, null))), "map"));

        Assert.Equal("2026-09-01T21:30:05-05:00", row.GetProperty("tooltip")[0].GetString());
    }

    [Fact]
    public async Task A_date_is_the_earliest_as_iso_text()
    {
        await using var h = await WithAsync(Row("0042", visitedOn: "'2026-09-02'"), Row("0042", visitedOn: "'2026-09-01'"));

        var result = await h.QueryAsync(MapConfig(Tooltip(("visited_on", null, null))), "map");

        Assert.Equal("2026-09-01", Single(result).GetProperty("tooltip")[0].GetString());
        Assert.Equal("date", result.GetProperty("tooltipTypes")[0].GetString());
    }

    [Fact]
    public async Task Mixed_booleans_give_false_and_all_true_gives_true()
    {
        await using var h = await WithAsync(Row("0042", ok: "true"), Row("0042", ok: "false"), Row("0043", ok: "true"), Row("0043", ok: "true"), Row("0044"));

        var result = await h.QueryAsync(MapConfig(Tooltip(("ok", null, null))), "map");

        var rows = result.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!, r => r.GetProperty("tooltip")[0]);
        Assert.Equal(JsonValueKind.False, rows["0042"].ValueKind);
        Assert.Equal(JsonValueKind.True, rows["0043"].ValueKind);
        Assert.Equal(JsonValueKind.Null, rows["0044"].ValueKind);
    }

    [Fact]
    public async Task A_text_of_250_characters_is_cut_to_200_and_an_ellipsis()
    {
        await using var h = await WithAsync(Row("0042", address: "repeat('x', 250)"), Row("0043", address: "repeat('y', 200)"));

        var result = await h.QueryAsync(MapConfig(Tooltip(("address", null, null))), "map");

        var rows = result.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("group").GetString()!, r => r.GetProperty("tooltip")[0].GetString()!);
        Assert.Equal(new string('x', 200) + "…", rows["0042"]);
        Assert.Equal(new string('y', 200), rows["0043"]);
    }

    [Fact]
    public async Task A_decimal_that_sums_to_nan_has_no_value()
    {
        await using var h = await WithAsync(Row("0042", progress: "'NaN'::numeric"), Row("0042", progress: "1"));

        var row = Single(await h.QueryAsync(MapConfig(Tooltip(("progress", null, null))), "map"));

        Assert.Equal(JsonValueKind.Null, row.GetProperty("tooltip")[0].ValueKind);
    }

    [Fact]
    public async Task A_map_without_tooltip_columns_still_returns_the_row_count_and_empty_tooltips()
    {
        await using var h = await WithAsync(Row("0042"), Row("0042"));

        var result = await h.QueryAsync(MapConfig(), "map");

        var row = Single(result);
        Assert.Equal(2, row.GetProperty("rows").GetInt64());
        Assert.Equal(0, row.GetProperty("tooltip").GetArrayLength());
        Assert.Equal(0, result.GetProperty("tooltipTypes").GetArrayLength());
    }

    [Fact]
    public async Task A_grouped_bar_query_has_no_rows_tooltip_or_tooltip_types_in_its_payload()
    {
        await using var h = await WithAsync(Row("0042", target: "3"));

        var result = await h.QueryAsync(new { indicator = "survey_progress", groupBy = "block" }, "bar");

        Assert.False(result.TryGetProperty("tooltipTypes", out _));
        var row = Single(result);
        Assert.False(row.TryGetProperty("rows", out _));
        Assert.False(row.TryGetProperty("tooltip", out _));
        Assert.Equal(["geometry", "group", "value"], row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_query_with_too_many_tooltip_columns_is_a_validation_error()
    {
        await using var h = await WithAsync(Row("0042"));
        var six = Enumerable.Repeat(("address", (string?)null, (string?)null), 6).ToArray();

        var response = await h.PostQueryAsync(MapConfig(Tooltip(six)), "map");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
