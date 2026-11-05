using System.Net;
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
            ["boxplot"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
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

    [Fact]
    public async Task A_box_plot_lists_each_box_in_the_charts_order_with_a_decimal_format_for_a_count_and_whole_numbers_for_its_counts()
    {
        await using var h = await HarnessAsync();
        await h.PublishAsync(
            "household_size", "municipality text, value numeric", "('Cali', 3), ('Cali', 4), ('Cali', 5), ('Cali', 6), ('Pasto', 2), (NULL, 7)",
            label: "Household size", format: "count", unit: "people");
        await h.AddWidgetAsync(Widget("Size", "boxplot", new
        {
            indicator = "household_size", groupBy = "municipality", sort = new { by = "value", direction = "desc" }, display = new { orientation = "horizontal" },
        }, y: 0));
        await h.AddWidgetAsync(Widget("Size of all", "boxplot", new { indicator = "household_size" }, y: 2));

        var model = await BuildAsync(h);
        var grouped = model.Widgets[0];
        var all = model.Widgets[1];

        Assert.Equal(WidgetOutcome.Shown, grouped.Outcome);
        Assert.Equal(
            ["municipality", "Values", "Minimum", "Q1", "Median", "Q3", "Maximum", "Mean", "Outliers"], grouped.Columns.Select(c => c.Header));
        // By median, largest first; a missing group reads "(none)".
        Assert.Equal(["(none)", "Cali", "Pasto"], Texts(grouped, 0));
        var cali = grouped.Rows.Single(r => r[0].Text == "Cali");
        Assert.Equal(["Cali", "4", "3.00 people", "3.75 people", "4.50 people", "5.25 people", "6.00 people", "4.50 people", "0"], cali.Select(c => c.Text));
        Assert.Equal(4.5, cali[4].Value);
        Assert.Equal(4d, cali[1].Value);
        // A count's figures have two decimals, and the box's counts are whole numbers.
        Assert.Equal(["#,##0", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0.00", "#,##0"], grouped.Columns.Skip(1).Select(c => c.ExcelFormat));
        Assert.All(grouped.Columns.Skip(2).Take(6), c => Assert.Equal("people", c.Unit));
        Assert.Null(grouped.Columns[1].Unit);

        // No group column: the one box is "All rows", under the header "Group".
        Assert.Equal("Group", all.Columns[0].Header);
        Assert.Equal(["All rows"], Texts(all, 0));
        Assert.Equal("6", all.Rows[0][1].Text);
    }

    [Fact]
    public async Task A_box_plot_that_reaches_the_limit_says_boxes_and_not_rows()
    {
        await using var h = await HarnessAsync(new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });
        await h.AddWidgetAsync(Widget("Households", "boxplot", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 0));
        await h.AddWidgetAsync(Widget("Bars", "bar", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 2));

        var model = await BuildAsync(h);

        Assert.Equal(["Only the first 2 boxes are shown: the query matches more."], model.Widgets[0].Notes);
        Assert.Equal(["Only the first 2 rows are shown: the query matches more."], model.Widgets[1].Notes);

        var spanish = await BuildAsync(h, "es-CO");
        Assert.Equal(["Solo se muestran las primeras 2 cajas: la consulta coincide con más."], spanish.Widgets[0].Notes);
    }

    [Fact]
    public async Task A_box_of_1200_values_writes_its_number_of_values_with_a_thousands_separator_and_as_a_whole_number()
    {
        await using var h = await HarnessAsync();
        await h.PublishAsync("household_size", "municipality text, value numeric", "", label: "Household size", format: "count", unit: "people");
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".household_size SELECT 'Cali', 3 + (g % 4) FROM generate_series(1, 1200) g");
        await h.AddWidgetAsync(Widget("Size", "boxplot", new { indicator = "household_size", groupBy = "municipality" }, y: 0));

        var model = await BuildAsync(h);
        var cali = Assert.Single(model.Widgets[0].Rows);

        Assert.Equal("1,200", cali[1].Text);
        Assert.Equal(1200d, cali[1].Value);
        Assert.Equal("0", cali[8].Text);
        // 3, 4, 5 and 6 in equal numbers: the median falls between whole numbers and is written with two decimals.
        Assert.Equal("4.50 people", cali[4].Text);
    }

    // Column labels (dashboard-reports: Column labels in a scheduled email)

    private const string TeatroColumns = "comuna text, estrato text, value numeric, numerator numeric, denominator numeric, geometry text, registros bigint, meta bigint";

    private static readonly object TeatroLabels = new
    {
        numerator = new { label = "Hogares que asistieron" },
        denominator = new { label = "Hogares" },
        comuna = new { label = "Comuna" },
        value = new { label = "Proporción" },
        registros = new { label = "Registros recibidos" },
    };

    private async Task<IndicatorsHarness> TeatroAsync(object? labels = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", TeatroColumns, """
            ('Zona 1', 'Bajo', 0.5, 5, 10, 'POINT(1 1)', 12, 20),
            ('Zona 2', 'Alto', 0.25, 5, 20, 'POINT(2 2)', 8, 20)
            """, label: "Teatro hoy", format: "decimal", labels: labels);
        return h;
    }

    private static object Ratio(string chartType = "column", object? display = null, string? groupBy = "comuna") => Widget("Asistencia", chartType, new
    {
        indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy, display,
    }, y: 0);

    [Fact]
    public async Task The_caption_and_the_headers_of_a_widget_name_its_columns_with_the_indicators_labels()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Ratio());

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares", widget.Caption);
        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares"], widget.Columns.Select(c => c.Header));
        Assert.Equal(["Zona 1", "Zona 2"], Texts(widget, 0));
    }

    [Fact]
    public async Task A_widgets_own_label_wins_over_the_indicators_and_a_column_without_either_keeps_its_name()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Ratio(display: new { labels = new { denominator = "Hogares de la base E1" } }, groupBy: "estrato"));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares de la base E1", widget.Caption);
        Assert.Equal(["estrato", "Teatro hoy: Hogares que asistieron / Hogares de la base E1"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_measure_is_named_in_the_caption()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Widget("Valor", "column", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("Teatro hoy: Proporción", widget.Caption);
        Assert.Equal(["Comuna", "Teatro hoy: Proporción"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_stat_tile_is_described_with_the_names_of_its_ratio()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Ratio("stat", groupBy: null));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares", widget.Caption);
        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares", widget.StatColumn!.Header);
        Assert.Equal("0.33", widget.StatValue);
    }

    [Fact]
    public async Task The_headers_of_every_kind_of_table_name_the_group_column_and_keep_the_answers_of_a_series()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Widget("Pie", "pie", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" }, y: 0));
        await h.AddWidgetAsync(Widget("Box", "boxplot", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" }, y: 2));
        await h.AddWidgetAsync(Widget("Box of all", "boxplot", new { indicator = "teatro_hoy", measure = "value" }, y: 4));
        await h.AddWidgetAsync(Widget("Histogram", "histogram", new { indicator = "teatro_hoy", measure = "value", buckets = 2 }, y: 6));
        await h.AddWidgetAsync(Widget("Stacked", "column", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna", seriesBy = "estrato", layout = "stacked" }, y: 8));
        await h.AddWidgetAsync(Widget("Cross", "crosstab", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna", seriesBy = "estrato" }, y: 10));
        await h.AddWidgetAsync(Widget("Line", "line", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" }, y: 12));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal(["Comuna", "Teatro hoy: Proporción", "Share"], widgets.Single(w => w.Title == "Pie").Columns.Select(c => c.Header));
        Assert.Equal("Comuna", widgets.Single(w => w.Title == "Box").Columns[0].Header);
        Assert.Equal("Group", widgets.Single(w => w.Title == "Box of all").Columns[0].Header);
        Assert.Equal(["Range", "Rows of Teatro hoy: Proporción"], widgets.Single(w => w.Title == "Histogram").Columns.Select(c => c.Header));
        Assert.Equal(["Comuna", "Alto", "Bajo", "Total"], widgets.Single(w => w.Title == "Stacked").Columns.Select(c => c.Header).Select(header => header.Split(" (")[0]));
        Assert.Equal("Comuna", widgets.Single(w => w.Title == "Cross").Columns[0].Header);
        Assert.Equal("Comuna", widgets.Single(w => w.Title == "Line").Columns[0].Header);
    }

    [Fact]
    public async Task A_maps_tooltip_column_without_its_own_label_takes_the_indicators_and_one_with_a_label_keeps_it()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Widget("Map", "map", new
        {
            indicator = "teatro_hoy", measure = "value", groupBy = "comuna", geometryColumn = "geometry",
            tooltip = new object[] { new { column = "registros" }, new { column = "meta", label = "Meta" }, new { column = "estrato" } },
        }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["Comuna", "Teatro hoy: Proporción", "Registros recibidos", "Meta", "estrato"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_label_changed_after_the_dashboard_was_built_shows_in_the_next_build()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Ratio());
        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares", (await BuildAsync(h)).Widgets.Single().Caption);

        await h.LabelColumnsAsync("teatro_hoy", new { numerator = new { label = "Hogares que asistieron" }, denominator = new { label = "Hogares encuestados" } }, label: "Teatro hoy");

        var widget = (await BuildAsync(h)).Widgets.Single();
        Assert.Equal("Teatro hoy: Hogares que asistieron / Hogares encuestados", widget.Caption);
        Assert.Equal(["comuna", "Teatro hoy: Hogares que asistieron / Hogares encuestados"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_dashboard_whose_widgets_and_indicators_label_nothing_names_the_columns_as_before()
    {
        await using var h = await TeatroAsync();
        await h.AddWidgetAsync(Ratio());
        await h.AddWidgetAsync(Widget("Valor", "column", new { indicator = "teatro_hoy", measure = "value", groupBy = "estrato" }, y: 2));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal("Teatro hoy: numerator / denominator", widgets[0].Caption);
        Assert.Equal(["comuna", "Teatro hoy: numerator / denominator"], widgets[0].Columns.Select(c => c.Header));
        Assert.Equal(["estrato", "Teatro hoy: value"], widgets[1].Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_removed_indicator_names_its_columns_as_they_are()
    {
        await using var h = await TeatroAsync(TeatroLabels);
        await h.AddWidgetAsync(Ratio());
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.DeleteAsync($"/api/v1/projects/{h.ProjectId}/scripts/indicators/teatro_hoy")).StatusCode);

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("teatro_hoy: numerator / denominator", widget.Caption);
    }

    // Widget context (dashboard-reports: Widget context in a scheduled email)

    private async Task<IndicatorsHarness> AsistenciaAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("encuesta_hoy", "zona text, value numeric", "('Zona 1', 0.1121), ('Zona 2', 0.5)", label: "Encuesta hoy", format: "percent", unit: "de los hogares");
        return h;
    }

    private static object Context(string chartType, object display, string groupBy = "zona") =>
        Widget("2.2", chartType, new { indicator = "encuesta_hoy", measure = "value", groupBy, display }, y: 0);

    [Fact]
    public async Task Own_caption_text_names_the_caption_line_and_the_value_columns_header()
    {
        await using var h = await AsistenciaAsync();
        await h.AddWidgetAsync(Context("bar", new { caption = new { text = "% de quienes asistieron" } }));
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "encuesta_hoy", measure = "value", display = new { caption = new { text = "% de quienes asistieron" } } }, y: 2));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal("% de quienes asistieron", widgets[0].Caption);
        Assert.Equal(["zona", "% de quienes asistieron"], widgets[0].Columns.Select(c => c.Header));
        Assert.Equal("de los hogares", widgets[0].Columns[1].Unit);
        Assert.False(widgets[0].CaptionHidden);
        Assert.Equal("% de quienes asistieron", widgets[1].StatColumn!.Header);
    }

    [Fact]
    public async Task A_hidden_caption_keeps_what_is_plotted_in_the_value_columns_header()
    {
        await using var h = await AsistenciaAsync();
        await h.AddWidgetAsync(Context("column", new { caption = new { hidden = true } }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.True(widget.CaptionHidden);
        Assert.Equal("Encuesta hoy: value", widget.Caption);
        Assert.Equal(["zona", "Encuesta hoy: value"], widget.Columns.Select(c => c.Header));
        Assert.Equal("de los hogares", widget.Columns[1].Unit);
    }

    [Fact]
    public async Task Own_unit_replaces_the_indicators_in_the_values_and_in_the_header()
    {
        await using var h = await AsistenciaAsync();
        await h.AddWidgetAsync(Context("column", new { unit = "de quienes asistieron" }));
        await h.AddWidgetAsync(Widget("Other", "column", new { indicator = "encuesta_hoy", measure = "value", groupBy = "zona" }, y: 2));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal(["11.21% de quienes asistieron", "50% de quienes asistieron"], Texts(widgets[0], 1));
        Assert.Equal("de quienes asistieron", widgets[0].Columns[1].Unit);
        // The indicator's other widgets keep its unit.
        Assert.Equal(["11.21% de los hogares", "50% de los hogares"], Texts(widgets[1], 1));
    }

    [Fact]
    public async Task A_stat_tile_and_a_box_plot_are_written_with_the_widgets_unit()
    {
        await using var h = await AsistenciaAsync();
        await h.PublishAsync("household_size", "zona text, value numeric", "('Zona 1', 3), ('Zona 1', 4), ('Zona 1', 5), ('Zona 2', 6)", label: "Household size", format: "count", unit: "people");
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "encuesta_hoy", measure = "value", display = new { unit = "de quienes asistieron" } }, y: 0));
        await h.AddWidgetAsync(Widget("Size", "boxplot", new { indicator = "household_size", groupBy = "zona", display = new { unit = "personas" } }, y: 2));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.EndsWith(" de quienes asistieron", widgets[0].StatValue);
        Assert.Equal("de quienes asistieron", widgets[0].StatColumn!.Unit);
        var zona1 = widgets[1].Rows.Single(r => r[0].Text == "Zona 1");
        Assert.Equal("4.00 personas", zona1[4].Text);
        Assert.All(widgets[1].Columns.Skip(2).Take(6), c => Assert.Equal("personas", c.Unit));
    }

    [Fact]
    public async Task A_removed_indicator_with_a_unit_of_its_own_and_with_a_caption_text_of_its_own_keeps_both()
    {
        await using var h = await HarnessAsync();
        await h.PublishAsync("teatro_precio_hogar", "zona text, value numeric", "('Zona 1', 40000)", script: false);
        await h.AddWidgetAsync(Widget("Precio", "boxplot", new { indicator = "teatro_precio_hogar", groupBy = "zona", display = new { unit = "pesos por boleta" } }, y: 0));
        await h.AddWidgetAsync(Widget("Tile", "stat", new { indicator = "teatro_precio_hogar", measure = "value", display = new { unit = "pesos por boleta", caption = new { text = "Precio de la boleta" } } }, y: 2));
        await h.AddWidgetAsync(Widget("Plain", "stat", new { indicator = "teatro_precio_hogar", measure = "value" }, y: 4));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal("40,000.00 pesos por boleta", widgets[0].Rows.Single()[4].Text);
        Assert.Equal("40,000.00 pesos por boleta", widgets[1].StatValue);
        Assert.Equal("Precio de la boleta", widgets[1].Caption);
        // Removed indicator, unchanged: two decimals, no unit, and its name.
        Assert.Equal("40,000.00", widgets[2].StatValue);
        Assert.Equal("teatro_precio_hogar: value", widgets[2].Caption);
    }

    [Fact]
    public async Task The_texts_of_a_widget_reach_its_model_whatever_became_of_it_and_the_dashboards_source_reaches_the_model()
    {
        await using var h = await HarnessAsync(new Dictionary<string, string?> { ["DataDashboard:LockTimeout"] = "00:00:00.5" });
        await h.SaveSourceAsync("Encuesta de mercado, 478 encuestas");
        var texts = new { subtitle = "¿Cuántos?", footnote = "No suman 100 %.", caption = new { hidden = true } };
        await h.AddWidgetAsync(Widget("Locked", "bar", new { indicator = "households", measure = "value", groupBy = "municipality", display = texts }, y: 0));
        await h.AddWidgetAsync(Widget("Coverage", "stat", new { indicator = "coverage", measure = "value", display = texts }, y: 2));
        await h.AddWidgetAsync(Widget("Bare", "stat", new { indicator = "coverage", measure = "value" }, y: 4));
        await using var holder = new NpgsqlConnection(h.Db);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"LOCK TABLE \"{h.Schema}\".households IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        var model = await BuildAsync(h);

        Assert.Equal("Encuesta de mercado, 478 encuestas", model.Source);
        Assert.Equal(WidgetOutcome.TimedOut, model.Widgets[0].Outcome);
        Assert.Equal(["¿Cuántos?", "No suman 100 %.", "True"], [model.Widgets[0].Subtitle, model.Widgets[0].Footnote, model.Widgets[0].CaptionHidden.ToString()]);
        Assert.Equal(WidgetOutcome.Shown, model.Widgets[1].Outcome);
        Assert.Equal(["¿Cuántos?", "No suman 100 %.", "True"], [model.Widgets[1].Subtitle, model.Widgets[1].Footnote, model.Widgets[1].CaptionHidden.ToString()]);
        Assert.Null(model.Widgets[2].Subtitle);
        Assert.Null(model.Widgets[2].Footnote);
        Assert.False(model.Widgets[2].CaptionHidden);
    }

    [Fact]
    public async Task A_dashboard_without_a_source_has_none_in_its_model()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("Total", "stat", new { indicator = "coverage", measure = "value" }, y: 0));

        Assert.Null((await BuildAsync(h)).Source);
    }

    // Reference lines (dashboard-reports: Reference lines in a scheduled email)

    private async Task<IndicatorsHarness> TasasAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "encuesta_hoy", "zona text, agua text, numerator numeric, denominator numeric",
            "('Zona 1', 'A', 1, 10), ('Zona 1', 'B', 2, 10), ('Zona 2', 'A', 6, 10), ('Zona 3', 'B', 3, 30)",
            label: "Encuesta hoy", format: "percent", unit: "de los hogares");
        return h;
    }

    private static object[] Lines(params object[] lines) => lines;

    private static object RatioWidget(string chartType, object display, string? seriesBy = null) =>
        Widget("2.1", chartType, seriesBy is null
            ? new { indicator = "encuesta_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "zona", display }
            : new { indicator = "encuesta_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "zona", seriesBy, display },
            y: 0, w: 12, height: 4);

    [Fact]
    public async Task A_column_chart_has_its_mean_overall_and_value_lines_with_the_indicators_unit()
    {
        await using var h = await TasasAsync();
        await h.AddWidgetAsync(RatioWidget("column", new
        {
            referenceLines = Lines(new { kind = "mean" }, new { kind = "overall" }, new { kind = "value", value = 0.5, label = "Meta" }),
        }));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(WidgetOutcome.Shown, widget.Outcome);
        // Zona 1 is 3/20, Zona 2 6/10 and Zona 3 3/30: the mean of the bars is not the overall value, 12/60.
        Assert.Equal(["mean", "overall", "value"], widget.ReferenceLines.Select(l => l.Kind));
        Assert.Equal(["Mean", "Overall", "Meta"], widget.ReferenceLines.Select(l => l.Label));
        Assert.Equal(["28.33% de los hogares", "20% de los hogares", "50% de los hogares"], widget.ReferenceLines.Select(l => l.Text));
        Assert.Equal(["Mean: 28.33% de los hogares", "Overall: 20% de los hogares", "Meta: 50% de los hogares"], widget.ReferenceLines.Select(l => l.Line));
        Assert.Equal((0.15 + 0.6 + 0.1) / 3, widget.ReferenceLines[0].Value, 9);
        Assert.Equal(0.2, widget.ReferenceLines[1].Value, 9);
        // The table is the one "Show as table" lists: the lines are not rows.
        Assert.Equal(["Zona 1", "Zona 2", "Zona 3"], Texts(widget, 0));
    }

    [Fact]
    public async Task The_default_labels_follow_the_emails_language_and_the_unit_is_the_widgets_own()
    {
        await using var h = await TasasAsync();
        await h.AddWidgetAsync(RatioWidget("bar", new
        {
            unit = "de quienes asistieron",
            referenceLines = Lines(new { kind = "mean" }, new { kind = "median" }, new { kind = "overall" }, new { kind = "value", value = 0.25 }),
        }));

        var widget = (await BuildAsync(h, "es-CO")).Widgets.Single();

        Assert.Equal(["Promedio", "Mediana", "Valor general", "Referencia"], widget.ReferenceLines.Select(l => l.Label));
        Assert.Equal("Mediana: 15% de quienes asistieron", widget.ReferenceLines[1].Line);
        Assert.Equal("Referencia: 25% de quienes asistieron", widget.ReferenceLines[3].Line);
    }

    [Fact]
    public async Task A_widget_with_a_series_reads_its_overall_line_from_its_totals_and_a_100_percent_value_line_is_a_share()
    {
        await using var h = await TasasAsync();
        await h.AddWidgetAsync(RatioWidget("column", new { referenceLines = Lines(new { kind = "overall" }) }, seriesBy: "agua"));
        await h.PublishAsync("hogares_por_agua", "zona text, agua text, value numeric", "('Zona 1', 'A', 30), ('Zona 1', 'B', 10), ('Zona 2', 'A', 5)", label: "Hogares", format: "count", unit: "hogares");
        await h.AddWidgetAsync(Widget("Composition", "bar", new
        {
            indicator = "hogares_por_agua", measure = "value", groupBy = "zona", seriesBy = "agua", layout = "percent",
            display = new { referenceLines = Lines(new { kind = "value", value = 0.5 }) },
        }, y: 4, w: 12, height: 4));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.Equal(["Overall: 20% de los hogares"], widgets[0].ReferenceLines.Select(l => l.Line));
        Assert.Equal(["Reference: 50%"], widgets[1].ReferenceLines.Select(l => l.Line));
    }

    [Fact]
    public async Task A_widget_without_reference_lines_or_that_is_not_shown_has_none()
    {
        await using var h = await TasasAsync();
        await h.AddIndicatorAsync("sin_publicar", label: "Sin publicar");
        await h.AddWidgetAsync(RatioWidget("column", new { subtitle = "¿Cuántos?" }));
        await h.AddWidgetAsync(Widget("Pie", "pie", new { indicator = "encuesta_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "zona" }, y: 4));
        await h.AddWidgetAsync(Widget("Gone", "column", new
        {
            indicator = "sin_publicar", measure = "value", groupBy = "zona", display = new { referenceLines = Lines(new { kind = "value", value = 1 }) },
        }, y: 6));

        var widgets = (await BuildAsync(h)).Widgets;

        Assert.All(widgets, w => Assert.Empty(w.ReferenceLines));
        Assert.Equal(WidgetOutcome.NotPublished, widgets[2].Outcome);
    }

    // The declared category order in a scheduled email (add-declared-category-order, design D11)

    private async Task<IndicatorsHarness> ConteoAsync(object? columns = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_conteo", "comuna text, respuesta text, conteo numeric", """
            ('COMUNA 1', '4. No sabe', 5), ('COMUNA 1', '2. Tal vez', 10), ('COMUNA 1', '1. Sí', 30), ('COMUNA 1', '3. No', 15),
            ('COMUNA 2', '1. Sí', 20), ('COMUNA 2', '3. No', 25)
            """, label: "Teatro conteo", format: "count", labels: columns);
        return h;
    }

    private static object PercentBar(string seriesBy = "respuesta", string groupBy = "comuna") => Widget("2.8. Iría al teatro, por comuna", "bar", new
    {
        indicator = "teatro_conteo", measure = "conteo", groupBy, seriesBy, layout = "percent",
    }, y: 0, w: 12, height: 4);

    [Fact]
    public async Task Answers_in_order_without_their_numbers()
    {
        await using var h = await ConteoAsync(new { respuesta = new { order = new[] { "1. Sí", "2. Tal vez", "3. No" }, last = new[] { "4. No sabe" }, stripPrefix = true } });
        await h.AddWidgetAsync(PercentBar());

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["comuna", "Sí", "Tal vez", "No", "No sabe", "Total"], widget.Columns.Select(c => c.Header));
        Assert.Equal(["COMUNA 1", "COMUNA 2"], Texts(widget, 0));
        Assert.Equal("50%", widget.Rows[0][1].Text);
    }

    [Fact]
    public async Task A_widget_that_declares_nothing_lists_its_series_as_before()
    {
        await using var h = await ConteoAsync(new { respuesta = new { label = "Respuesta" } });
        await h.AddWidgetAsync(PercentBar());

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["comuna", "1. Sí", "2. Tal vez", "3. No", "4. No sabe", "Total"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task Value_kept_last()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", "comuna text, value numeric", "('COMUNA 1', 5), ('Sin dato', 2), ('ZONA RURAL', 3), ('COMUNA 2', 7)", label: "Teatro hoy", format: "count",
            labels: new { comuna = new { last = new[] { "Sin dato" } } });
        await h.AddWidgetAsync(Widget("Por comuna", "column", new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal("Sin dato", Texts(widget, 0).Last());
        Assert.Equal(["COMUNA 1", "COMUNA 2", "ZONA RURAL", "Sin dato"], Texts(widget, 0));
    }

    [Fact]
    public async Task Widgets_own_order()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("Share", "pie", new
        {
            indicator = "households", measure = "value", groupBy = "municipality", display = new { order = new { group = new[] { "Pasto", "Cali" } } },
        }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["Pasto", "Cali"], Texts(widget, 0).Take(2));
        Assert.Equal("(none)", Texts(widget, 0).Last());
        Assert.Equal(4, widget.Rows.Count);
    }

    [Fact]
    public async Task A_widgets_own_order_replaces_the_indicators()
    {
        await using var h = await ConteoAsync(new { respuesta = new { order = new[] { "1. Sí", "2. Tal vez" }, stripPrefix = true } });
        await h.AddWidgetAsync(Widget("Respuestas", "column", new
        {
            indicator = "teatro_conteo", measure = "conteo", groupBy = "comuna", seriesBy = "respuesta", layout = "stacked",
            display = new { order = new { series = new[] { "4. No sabe", "3. No" } } },
        }, y: 0, w: 12, height: 4));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["comuna", "No sabe", "No", "Sí", "Tal vez", "Total"], widget.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task A_box_plot_ordered_by_median_keeps_a_value_kept_last_at_the_end()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(
            "household_size", "estrato text, value numeric", "('1', 9), ('1', 10), ('2', 4), ('2', 5), ('No sabe', 20), ('No sabe', 21), ('3', 1), ('3', 2)",
            label: "Household size", format: "count", labels: new { estrato = new { last = new[] { "No sabe" } } });
        await h.AddWidgetAsync(Widget("Size", "boxplot", new
        {
            indicator = "household_size", groupBy = "estrato", sort = new { by = "value", direction = "desc" },
        }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["1", "2", "3", "No sabe"], Texts(widget, 0));
    }

    [Fact]
    public async Task A_box_plot_in_a_declared_order_lists_its_boxes_so()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("tarifa", "vehiculo text, value numeric", "('Carro', 5), ('Carro', 6), ('Moto', 1), ('Moto', 2), ('Todos los vehículos', 3), ('Todos los vehículos', 4)",
            label: "Tarifa", format: "count");
        await h.AddWidgetAsync(Widget("Tarifa", "boxplot", new
        {
            indicator = "tarifa", groupBy = "vehiculo", display = new { order = new { group = new[] { "Todos los vehículos", "Carro", "Moto" } } },
        }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["Todos los vehículos", "Carro", "Moto"], Texts(widget, 0));
    }

    [Fact]
    public async Task The_groups_of_a_line_follow_the_declared_order_over_the_natural_one()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("demanda", "precio text, value numeric", "('$10.000', 30), ('$0', 40), ('$50.000', 5), ('$20.000', 20)", label: "Demanda", format: "count",
            labels: new { precio = new { order = new[] { "$50.000", "$20.000", "$10.000", "$0" } } });
        await h.AddWidgetAsync(Widget("Demanda", "line", new { indicator = "demanda", measure = "value", groupBy = "precio" }, y: 0));

        var widget = (await BuildAsync(h)).Widgets.Single();

        Assert.Equal(["$50.000", "$20.000", "$10.000", "$0"], Texts(widget, 0));
    }

    [Fact]
    public async Task A_dashboard_that_declares_nothing_builds_the_tables_it_always_built()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(Widget("By round", "line", new { indicator = "households", measure = "value", groupBy = "round" }, y: 0));
        await h.AddWidgetAsync(Widget("Share", "pie", new { indicator = "households", measure = "value", groupBy = "municipality" }, y: 2));

        var model = await BuildAsync(h);

        Assert.Equal(["1", "2", "10"], Texts(model.Widgets[0], 0));
        Assert.Equal(4, model.Widgets[1].Rows.Count);
    }
}
