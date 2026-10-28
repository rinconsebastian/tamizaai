using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tamiza.Api.IntegrationTests.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>
/// The report model of a dashboard, read from published indicator tables: each widget as its "Show as table" view lists it, or the
/// reason it cannot be shown (design D7).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportDataTests(PostgresFixture postgres)
{
    private const string HouseholdColumns = "municipality text, round text, value numeric, address text, target bigint, geometry text";

    private async Task<IndicatorsHarness> HarnessAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("households", HouseholdColumns, """
            ('Cali', '1', 120, 'Calle 1', 20, 'POINT(1 1)'),
            ('Pasto', '2', 80, NULL, 15, 'POINT(2 2)'),
            ('Tumaco', '10', 40, 'Calle 3', NULL, 'POINT(3 3)'),
            (NULL, '2', 10, NULL, NULL, NULL)
            """, label: "Households visited", format: "count", unit: "households");
        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 0.42), ('Pasto', 0.5)", label: "Coverage", format: "percent");
        return h;
    }

    private static object Widget(string title, string chartType, object config, int y, int x = 0, int w = 6, int height = 2) =>
        new { title, chartType, config, layout = new { x, y, w, h = height } };

    private static async Task<ReportModel> BuildAsync(IndicatorsHarness h, string locale = "en-US", Guid? dashboardId = null)
    {
        await using var scope = h.Factory.Services.CreateAsyncScope();
        var model = await scope.ServiceProvider.GetRequiredService<ReportDataBuilder>().BuildAsync(dashboardId ?? h.DashboardId, locale, CancellationToken.None);
        return model!;
    }

    private static string[] Texts(ReportWidget widget, int column) => widget.Rows.Select(r => r[column].Text).ToArray();

    [Fact]
    public async Task Shapes_each_widget_like_its_show_as_table_view_in_reading_order()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("By municipality", "column", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 0));
        await h.AddWidgetAsync(Widget("Coverage", "stat", new { indicator = "coverage", measure = "value" }, y: 2, x: 6, w: 3));
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 2, x: 0, w: 3));
        await h.AddWidgetAsync(Widget("Distribution", "histogram", new { indicator = "households", measure = "value", buckets = 2 }, y: 4));
        await h.AddWidgetAsync(Widget("Share", "pie", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 6));
        await h.AddWidgetAsync(Widget("By round", "line", new { indicator = "households", measure = "value", groupBy = "round" }, y: 8));
        await h.AddWidgetAsync(Widget("Map", "map", new
        {
            indicator = "households", measure = "value", groupBy = "municipality", geometryColumn = "geometry",
            tooltip = new object[] { new { column = "address", label = "Dirección" }, new { column = "target" } },
        }, y: 10));

        var model = await BuildAsync(h);

        Assert.Equal(["By municipality", "Total", "Coverage", "Distribution", "Share", "By round", "Map"], model.Widgets.Select(w => w.Title));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], model.Widgets.Select(w => w.Position));
        Assert.False(model.NothingToSend);

        // Stat tiles first in the body: a stat tile never groups, even when its saved config names a group.
        Assert.Equal(["250 households", "92%"], model.Stats.Select(s => s.StatValue));

        // Rows without a group value read "(none)".
        var bars = model.Widgets[0];
        Assert.Equal(["municipality", "Households visited: value"], bars.Columns.Select(c => c.Header));
        Assert.Contains("(none)", Texts(bars, 0));
        Assert.Equal("#,##0", bars.Columns[1].ExcelFormat);
        Assert.Equal("households", bars.Columns[1].Unit);

        // Histogram: one value per row, binned into the widget's two ranges.
        var histogram = model.Widgets[3];
        Assert.Equal(["Range", "Rows of Households visited: value"], histogram.Columns.Select(c => c.Header));
        Assert.Equal(["10–65", "65–120"], Texts(histogram, 0));
        Assert.Equal(["2", "2"], Texts(histogram, 1));

        // Pie chart: every group value with its share.
        var pie = model.Widgets[4];
        Assert.Equal(["municipality", "Households visited: value", "Share"], pie.Columns.Select(c => c.Header));
        Assert.Equal(4, pie.Rows.Count);
        Assert.Equal("48%", pie.Rows.Single(r => r[0].Text == "Cali")[2].Text);

        // Line chart order: natural, 2 before 10.
        Assert.Equal(["1", "2", "10"], Texts(model.Widgets[5], 0));

        // Map: the group, the plotted value and the tooltip columns under their labels, without the geometry.
        var map = model.Widgets[6];
        Assert.Equal(["municipality", "Households visited: value", "Dirección", "target"], map.Columns.Select(c => c.Header));
        var pasto = map.Rows.Single(r => r[0].Text == "Pasto");
        Assert.Equal(["Pasto", "80 households", "—", "15"], pasto.Select(c => c.Text));
        Assert.Null(pasto[2].Value);
        Assert.Equal(15d, pasto[3].Value);
        Assert.Equal("#,##0", map.Columns[3].ExcelFormat);
    }

    [Fact]
    public async Task Indicator_not_published()
    {
        await using var h = await HarnessAsync();
        await h.AddIndicatorAsync("visits", label: "Visits");
        await h.AddWidgetAsync(Widget("Visits", "stat", new { indicator = "visits", measure = "value" }, y: 0));
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "households", measure = "value" }, y: 2));

        var model = await BuildAsync(h);

        Assert.Equal(WidgetOutcome.NotPublished, model.Widgets[0].Outcome);
        Assert.Equal("Visits has not published yet.", model.Widgets[0].Reason);
        Assert.Equal(WidgetOutcome.Shown, model.Widgets[1].Outcome);
        Assert.False(model.NothingToSend);
    }

    [Fact]
    public async Task Nothing_published_yet_is_nothing_to_send()
    {
        await using var h = await HarnessAsync();
        await h.AddIndicatorAsync("visits", label: "Visits");
        await h.AddWidgetAsync(Widget("Visits", "stat", new { indicator = "visits", measure = "value" }, y: 0));

        Assert.True((await BuildAsync(h)).NothingToSend);
    }

    [Fact]
    public async Task Settings_no_longer_fit()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("By municipality", "bar", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 0));
        await h.AddWidgetAsync(Widget("Coverage", "stat", new { indicator = "coverage", measure = "value" }, y: 2));
        // A script change publishes the measure column as text.
        await h.PublishAsync("households", "municipality text, value text", "('Cali', 'many')", format: "count");

        var model = await BuildAsync(h);

        Assert.Equal(WidgetOutcome.NoLongerFits, model.Widgets[0].Outcome);
        Assert.Equal("This widget's settings no longer fit its indicator's table.", model.Widgets[0].Reason);
        Assert.Equal("92%", model.Widgets[1].StatValue);
        Assert.False(model.NothingToSend);
    }

    [Fact]
    public async Task Removed_indicator_uses_two_decimals_no_unit_and_its_name()
    {
        await using var h = await HarnessAsync();
        await h.PublishAsync("legacy", "municipality text, value numeric", "('Cali', 12.5)", script: false);
        await h.ExecuteAsync("""
            INSERT INTO tamiza.dashboard_widgets (project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by)
            SELECT @project, @dashboard, 'stat', 'Legacy', '{"indicator":"legacy","measure":"value"}', '{"x":0,"y":0,"w":3,"h":2}', id, id
            FROM tamiza.users WHERE keycloak_sub = 'grace'
            """, ("project", h.ProjectId), ("dashboard", h.DashboardId));

        var widget = Assert.Single((await BuildAsync(h)).Widgets);

        Assert.Equal("12.50", widget.StatValue);
        Assert.Equal("legacy: value", widget.Caption);
    }

    [Fact]
    public async Task A_widget_with_no_matching_rows_counts_as_read()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("Nowhere", "bar", new
        {
            indicator = "households", measure = "value", groupBy = "municipality",
            filters = new object[] { new { column = "municipality", op = "in", values = new[] { "Nowhere" } } },
        }, y: 0));

        var model = await BuildAsync(h);

        Assert.Equal(WidgetOutcome.NoRows, model.Widgets[0].Outcome);
        Assert.Equal("No rows match this widget’s query.", model.Widgets[0].Reason);
        Assert.False(model.NothingToSend);
    }

    [Fact]
    public async Task Default_applied_ignored_filter_noted_and_filters_listed()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new object[]
        {
            new { column = "round", label = "Round", kind = "text", @default = new { values = new[] { "2" } } },
            new { column = "municipality", label = "Municipality", kind = "text" },
        });
        await h.AddWidgetAsync(Widget("Narrowed", "bar", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 0));
        await h.AddWidgetAsync(Widget("Every round", "bar", new
        {
            indicator = "households", measure = "value", groupBy = "municipality", ignoredDashboardFilters = new[] { "round" },
        }, y: 2));
        await h.AddWidgetAsync(Widget("Coverage", "bar", new { indicator = "coverage", measure = "value", groupBy = "municipality" }, y: 4));

        var model = await BuildAsync(h);

        Assert.Equal([new ReportFilterLine("Round", "2")], model.Filters);
        Assert.Equal(["Pasto", "(none)"], Texts(model.Widgets[0], 0));
        Assert.Empty(model.Widgets[0].Notes);
        Assert.Equal(4, model.Widgets[1].Rows.Count);
        Assert.Equal(["Not filtered by: Round"], model.Widgets[1].Notes);
        // The coverage table has no round column: the filter in force does not narrow it either.
        Assert.Equal(["Not filtered by: Round"], model.Widgets[2].Notes);
    }

    [Fact]
    public async Task Read_timed_out_only_for_the_locked_widget()
    {
        await using var h = await HarnessAsync(new Dictionary<string, string?> { ["DataDashboard:LockTimeout"] = "00:00:00.5" });
        await h.AddWidgetAsync(Widget("Locked", "stat", new { indicator = "households", measure = "value" }, y: 0));
        await h.AddWidgetAsync(Widget("Coverage", "stat", new { indicator = "coverage", measure = "value" }, y: 2));
        await using var holder = new NpgsqlConnection(h.Db);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"LOCK TABLE \"{h.Schema}\".households IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        var model = await BuildAsync(h);

        Assert.Equal(WidgetOutcome.TimedOut, model.Widgets[0].Outcome);
        Assert.Equal("This widget could not be read in time.", model.Widgets[0].Reason);
        Assert.Equal("92%", model.Widgets[1].StatValue);
        Assert.False(model.NothingToSend);
    }

    [Fact]
    public async Task Names_kept_and_header_values()
    {
        await using var h = await HarnessAsync();
        await h.ExecuteAsync("UPDATE tamiza.dashboards SET name = 'Vivienda' WHERE id = @id", ("id", h.DashboardId));
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "households", measure = "value" }, y: 0));

        var model = await BuildAsync(h, "en-US");

        Assert.Equal("Vivienda", model.DashboardName);
        Assert.Equal("Coverage 2026", model.ProjectName);
        Assert.NotNull(model.DataPublishedAt);
        Assert.Equal($"https://tamiza.test/projects/{h.ProjectId}/dashboards/{h.DashboardId}", model.DashboardAddress);
        Assert.Equal("Summary", model.Text["summary"]);
    }
}
