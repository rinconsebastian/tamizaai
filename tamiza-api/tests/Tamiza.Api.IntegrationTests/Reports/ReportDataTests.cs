using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tamiza.Api.IntegrationTests.Indicators;
using Tamiza.Api.Indicators;
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

    // Widgets with a series (design D8)

    private const string SourceColumns = "municipality text, water_source text, value numeric, numerator numeric, denominator numeric";

    private async Task<IndicatorsHarness> WithSourcesAsync(string rows = """
        ('Cali', 'Piped', 35, 7, 10), ('Cali', 'Well', 15, 3, 10), ('Palmira', 'Piped', 20, 2, 10), ('Buga', 'Well', 7, 1, 10)
        """)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("households_by_source", SourceColumns, rows, label: "Households", format: "count", unit: "households");
        return h;
    }

    private static object SeriesWidget(string title, string chartType, object config) => Widget(title, chartType, config, y: 0, w: 12, height: 4);

    [Fact]
    public async Task A_stacked_chart_is_its_cross_table_with_a_total_column_and_a_total_row()
    {
        await using var h = await WithSourcesAsync();
        await h.AddWidgetAsync(SeriesWidget("Sources", "column", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked",
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(WidgetOutcome.Shown, widget.Outcome);
        Assert.Equal(["municipality", "Piped", "Well", "Total"], widget.Columns.Select(c => c.Header));
        Assert.Equal(2, widget.SeriesColumns);
        Assert.Equal(
            [["Buga", "—", "7 households", "7 households"], ["Cali", "35 households", "15 households", "50 households"],
             ["Palmira", "20 households", "—", "20 households"], ["Total", "55 households", "22 households", "77 households"]],
            widget.Rows.Select(r => r.Select(c => c.Text).ToArray()).ToArray());
        // The workbook's cells are numbers with the indicator's number format; a pair without a value is empty.
        Assert.Equal(7d, widget.Rows[0][2].Value);
        Assert.Null(widget.Rows[0][1].Value);
        Assert.All(widget.Columns.Skip(1), c => Assert.Equal("#,##0", c.ExcelFormat));
        Assert.Equal("households", widget.Columns[1].Unit);
        Assert.Empty(widget.Notes);
    }

    [Fact]
    public async Task A_widget_stacked_to_100_percent_shows_shares_and_a_total_column_but_no_total_row()
    {
        await using var h = await WithSourcesAsync();
        await h.AddWidgetAsync(SeriesWidget("Composition", "bar", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "percent",
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("0.0%", widget.Columns[1].ExcelFormat);
        var cali = widget.Rows.Single(r => r[0].Text == "Cali");
        Assert.Equal(["Cali", "70%", "30%", "50 households"], cali.Select(c => c.Text));
        Assert.Equal(0.7, (double)cali[1].Value!, 6);
        // As in "Show as table": the group totals are a column, and there is no total row.
        Assert.Equal(["Buga", "Cali", "Palmira"], Texts(widget, 0));
    }

    [Fact]
    public async Task A_ratio_in_a_cross_table_has_ratio_totals()
    {
        await using var h = await WithSourcesAsync();
        await h.AddWidgetAsync(SeriesWidget("Rate", "crosstab", new
        {
            indicator = "households_by_source", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality", seriesBy = "water_source",
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        // Cali: 7 of 10 piped, 3 of 10 well, and 10 of 20 in all.
        var cali = widget.Rows.Single(r => r[0].Text == "Cali");
        Assert.Equal([0.7, 0.3, 0.5], cali.Skip(1).Select(c => Math.Round((double)c.Value!, 6)));
        Assert.Equal(13.0 / 40, (double)widget.Rows[^1][^1].Value!, 6);
    }

    [Fact]
    public async Task A_pyramid_lists_its_two_sides_and_a_total_and_notes_the_rows_it_left_out()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await using var owner = h;
        await h.PublishAsync("population", "age_band text, sex text, value numeric",
            "('0-4', 'Female', 100), ('0-4', 'Male', 90), ('5-9', 'Female', 80), ('5-9', 'Other', 5), (NULL, 'Male', 3)", label: "Population", format: "count");
        await h.AddWidgetAsync(SeriesWidget("Pyramid", "pyramid", new
        {
            indicator = "population", measure = "value", groupBy = "age_band", seriesBy = "sex", pyramid = new { left = "Female", right = "Male" },
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["age_band", "Female", "Male", "Total"], widget.Columns.Select(c => c.Header));
        Assert.Equal(["0-4", "5-9", "Total"], Texts(widget, 0));
        Assert.Equal("2 rows were left out: their group or value is not one the chart shows.", Assert.Single(widget.Notes));
    }

    [Fact]
    public async Task A_likert_widget_shows_shares_and_the_workbook_holds_them_as_numbers()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await using var owner = h;
        await h.PublishAsync("agreement", "municipality text, answer text, value numeric",
            "('Cali', 'No', 30), ('Cali', 'Neither', 20), ('Cali', 'Yes', 50)", label: "Agreement", format: "count");
        await h.AddWidgetAsync(SeriesWidget("Agreement", "likert", new
        {
            indicator = "agreement", measure = "value", groupBy = "municipality", seriesBy = "answer",
            scale = new object[] { new { value = "No", side = "negative" }, new { value = "Neither", side = "neutral" }, new { value = "Yes", side = "positive" } },
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["Cali", "30%", "20%", "50%", "100"], widget.Rows[0].Select(c => c.Text));
        Assert.Equal(0.3, (double)widget.Rows[0][1].Value!, 6);
        Assert.Equal("0.0%", widget.Columns[1].ExcelFormat);
        Assert.Empty(widget.Notes);
    }

    [Fact]
    public async Task Negative_values_set_aside_from_the_shares_are_noted()
    {
        await using var h = await WithSourcesAsync("('Cali', 'Piped', 60, 0, 1), ('Cali', 'Well', -5, 0, 1), ('Cali', 'Rain', 40, 0, 1)");
        await h.AddWidgetAsync(SeriesWidget("Composition", "column", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "percent",
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("1 negative value was left out of the shares.", Assert.Single(widget.Notes));
        // Natural order of the series: Piped, Rain, Well. The negative cell has no share, and the row total still counts it.
        Assert.Equal(["Cali", "60%", "40%", "—", "95 households"], widget.Rows[0].Select(c => c.Text));
    }

    [Fact]
    public async Task Series_columns_other_and_none_read_in_the_emails_language()
    {
        var rows = string.Join(", ", Enumerable.Range(1, 10).Select(i => $"('A', 's{i}', {i}, 0, 1)")) + ", ('A', NULL, 1, 0, 1)";
        await using var h = await WithSourcesAsync(rows);
        await h.AddWidgetAsync(SeriesWidget("Sources", "column", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked",
        }));

        var english = (await BuildAsync(h, "en-US")).Widgets.Single();
        var spanish = (await BuildAsync(h, "es-CO")).Widgets.Single();

        Assert.Equal(["(none)", "Other", "Total"], english.Columns.TakeLast(3).Select(c => c.Header));
        Assert.Equal(["(ninguno)", "Otros", "Total"], spanish.Columns.TakeLast(3).Select(c => c.Header));
        Assert.Equal(8, english.SeriesColumns);
    }

    [Fact]
    public async Task A_widget_made_of_a_series_whose_series_column_was_removed_is_stale_and_the_email_still_goes()
    {
        await using var h = await WithSourcesAsync();
        await h.AddWidgetAsync(SeriesWidget("Sources", "heatmap", new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source",
        }));
        await h.AddWidgetAsync(Widget("Plain", "column", new { indicator = "households_by_source", measure = "value", groupBy = "municipality" }, y: 4));
        await h.PublishAsync("households_by_source", "municipality text, value numeric", "('Cali', 1)");

        var model = await BuildAsync(h);

        Assert.Equal(WidgetOutcome.MissingColumns, model.Widgets[0].Outcome);
        Assert.Contains("water_source", model.Widgets[0].Reason);
        Assert.Equal(WidgetOutcome.Shown, model.Widgets[1].Outcome);
    }

    [Fact]
    public async Task Every_chart_type_builds_a_table_or_a_value()
    {
        await using var h = await WithSourcesAsync();
        var configs = new Dictionary<string, object>
        {
            ["stat"] = new { indicator = "households_by_source", measure = "value" },
            ["bar"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
            ["column"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
            ["line"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
            ["histogram"] = new { indicator = "households_by_source", measure = "value", buckets = 3 },
            ["pie"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
            ["heatmap"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
            ["crosstab"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
            ["pyramid"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Piped", right = "Well" } },
            ["likert"] = new
            {
                indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source",
                scale = new object[] { new { value = "Piped", side = "negative" }, new { value = "Well", side = "positive" } },
            },
        };
        var y = 0;
        foreach (var (chartType, config) in configs)
        {
            await h.AddWidgetAsync(Widget(chartType, chartType, config, y: y));
            y += 4;
        }

        var model = await BuildAsync(h);

        Assert.Equal(configs.Count, model.Widgets.Count);
        Assert.All(model.Widgets, w => Assert.Equal(WidgetOutcome.Shown, w.Outcome));
        Assert.All(model.Widgets.Where(w => !w.IsStat), w => Assert.NotEmpty(w.Rows));
        // The map needs a geometry column, and is covered by the test of the shapes; every other kind is here.
        Assert.Equal(ChartTypes.All.Where(t => t != ChartTypes.Map).Order(), model.Widgets.Select(w => w.ChartType).Order());
    }
}
