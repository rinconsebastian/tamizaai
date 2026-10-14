using Npgsql;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary>Runs the SQL that <see cref="DataQueryBuilder"/> writes against a real table, to check what it matches.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataQuerySqlTests(PostgresFixture postgres)
{
    private const string Schema = "p_00000000000000000000000000000001_output";

    private async Task<string> SeedAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        await TestData.ExecuteAsync(db, $"""
            CREATE SCHEMA "{Schema}";
            CREATE TABLE "{Schema}".t (name text, age bigint, income numeric, consent boolean, born date, at timestamptz);
            INSERT INTO "{Schema}".t VALUES
              ('50% off', 17, 10.5, true,  '2026-08-31', '2026-09-01 04:59:59+00'),
              ('50 percent', 18, 20, false, '2026-09-01', '2026-09-01 05:00:00+00'),
              ('Well',    29, NULL, NULL,   '2026-09-02', '2026-09-02 04:59:59+00'),
              ('WELL',    30, 5, true,     NULL,         '2026-09-02 05:00:00+00'),
              ('deep well', NULL, 7.25, false, '2026-09-03', NULL),
              ('a_b',     40, 1, true,     '2026-09-04', '2026-09-03 23:30:00-05'),
              ('axb',     41, 2, true,     '2026-09-05', '2026-09-05 12:00:00+00'),
              ('',        50, 3, false,    '2026-09-06', '2026-09-06 12:00:00+00'),
              (NULL,      51, 4, NULL,     NULL,         NULL);
            """);
        return db;
    }

    private static async Task<List<string?>> RunAsync(string db, DataQueryRequest request, string zone = "UTC")
    {
        var columns = new TableColumn[]
        {
            new("name", DataColumnType.Text, null), new("age", DataColumnType.Integer, null), new("income", DataColumnType.Decimal, null),
            new("consent", DataColumnType.Boolean, null), new("born", DataColumnType.Date, null), new("at", DataColumnType.Timestamp, null),
        };
        var errors = new FieldErrors();
        var query = DataQueryBuilder.Build(request, columns, zone, errors);
        Assert.False(errors.Any, string.Join("; ", errors.ToDictionary().SelectMany(e => e.Value)));

        await using var connection = new NpgsqlConnection(db);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT name FROM \"{Schema}\".t{query!.WhereClause} ORDER BY {query.OrderBy}", connection);
        foreach (var parameter in query.Parameters)
        {
            command.Parameters.Add(parameter.ToParameter());
        }

        var names = new List<string?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return names;
    }

    private static DataQueryRequest Filter(DataFilter filter, DataSort? sort = null) => new([filter], sort);

    [Fact]
    public async Task Contains_ignores_case()
    {
        var db = await SeedAsync();

        var names = await RunAsync(db, Filter(new DataFilter("name", "contains", "well")));

        Assert.Equal(["Well", "WELL", "deep well"], names);
    }

    [Fact]
    public async Task Contains_with_a_percent_and_an_underscore_matches_only_those_characters()
    {
        var db = await SeedAsync();

        Assert.Equal(["50% off"], await RunAsync(db, Filter(new DataFilter("name", "contains", "50%"))));
        Assert.Equal(["a_b"], await RunAsync(db, Filter(new DataFilter("name", "contains", "a_b"))));
    }

    [Fact]
    public async Task Contains_a_percent_and_underscore_together_matches_nothing_unless_the_text_has_both()
    {
        var db = await SeedAsync();

        Assert.Empty(await RunAsync(db, Filter(new DataFilter("name", "contains", "50%_"))));
        Assert.Equal(["a_b"], await RunAsync(db, Filter(new DataFilter("name", "contains", "_b"))));
    }

    [Fact]
    public async Task Equals_is_exact_and_in_picks_any_of_the_values()
    {
        var db = await SeedAsync();

        Assert.Equal(["Well"], await RunAsync(db, Filter(new DataFilter("name", "equals", "Well"))));
        Assert.Equal(["Well", "WELL"], await RunAsync(db, Filter(new DataFilter("name", "in", Values: ["WELL", "Well"]))));
        Assert.Equal([""], await RunAsync(db, Filter(new DataFilter("name", "equals", ""))));
    }

    [Fact]
    public async Task Two_filters_must_both_match()
    {
        var db = await SeedAsync();

        var names = await RunAsync(db, new DataQueryRequest([
            new DataFilter("name", "contains", "well"), new DataFilter("age", "range", Min: "18", Max: "29")]));

        Assert.Equal(["Well"], names);
    }

    [Fact]
    public async Task A_range_includes_both_ends_and_a_missing_end_is_open()
    {
        var db = await SeedAsync();

        Assert.Equal(["50 percent", "Well"], await RunAsync(db, Filter(new DataFilter("age", "range", Min: "18", Max: "29"))));
        Assert.Equal(["a_b", "axb", "", null], await RunAsync(db, Filter(new DataFilter("age", "range", Min: "40"), new DataSort("age", "asc"))));
        Assert.Equal(["50% off", "50 percent", "WELL", "deep well"], await RunAsync(db, Filter(new DataFilter("income", "range", Min: "5", Max: "20"), new DataSort("age", "asc"))));
    }

    [Fact]
    public async Task A_decimal_range_keeps_the_fraction()
    {
        var db = await SeedAsync();

        var names = await RunAsync(db, Filter(new DataFilter("income", "range", Min: "7.25", Max: "10.5"), new DataSort("income", "asc")));

        Assert.Equal(["deep well", "50% off"], names);
    }

    [Fact]
    public async Task A_date_range_compares_days()
    {
        var db = await SeedAsync();

        var names = await RunAsync(db, Filter(new DataFilter("born", "range", Min: "2026-09-01", Max: "2026-09-02"), new DataSort("born", "asc")));

        Assert.Equal(["50 percent", "Well"], names);
    }

    [Fact]
    public async Task A_timestamp_range_covers_whole_days_of_the_projects_zone_including_the_last_one()
    {
        var db = await SeedAsync();

        // 2026-09-01 in Bogota is 05:00 UTC on the 1st up to (not including) 05:00 UTC on the 2nd.
        var bogota = await RunAsync(db, Filter(new DataFilter("at", "range", Min: "2026-09-01", Max: "2026-09-01"), new DataSort("at", "asc")), "America/Bogota");
        var utc = await RunAsync(db, Filter(new DataFilter("at", "range", Min: "2026-09-01", Max: "2026-09-01"), new DataSort("at", "asc")));
        // The row at 2026-09-03 23:30 local time (-05) belongs to 2026-09-03 in Bogota and to 2026-09-04 in UTC.
        var lateBogota = await RunAsync(db, Filter(new DataFilter("at", "range", Min: "2026-09-03", Max: "2026-09-03")), "America/Bogota");
        var lateUtc = await RunAsync(db, Filter(new DataFilter("at", "range", Min: "2026-09-03", Max: "2026-09-03")));

        Assert.Equal(["50 percent", "Well"], bogota);
        Assert.Equal(["50% off", "50 percent"], utc);
        Assert.Equal(["a_b"], lateBogota);
        Assert.Empty(lateUtc);
    }

    [Fact]
    public async Task Empty_for_text_includes_an_empty_string_and_for_numbers_only_null()
    {
        var db = await SeedAsync();

        Assert.Equal(["", null], await RunAsync(db, Filter(new DataFilter("name", "empty"), new DataSort("name", "desc"))));
        Assert.Equal(["deep well"], await RunAsync(db, Filter(new DataFilter("age", "empty"))));
        Assert.Equal(7, (await RunAsync(db, Filter(new DataFilter("name", "notEmpty")))).Count);
    }

    [Fact]
    public async Task True_false_filters_pick_the_chosen_values()
    {
        var db = await SeedAsync();

        Assert.Equal(["50% off", "WELL", "a_b", "axb"], await RunAsync(db, Filter(new DataFilter("consent", "in", Values: ["true"]), new DataSort("age", "asc"))));
    }

    [Fact]
    public async Task Sorting_puts_empty_values_last_in_both_directions()
    {
        var db = await SeedAsync();

        var ascending = await RunAsync(db, new DataQueryRequest(Sort: new DataSort("age", "asc")));
        var descending = await RunAsync(db, new DataQueryRequest(Sort: new DataSort("age", "desc")));

        Assert.Equal("deep well", ascending[^1]);
        Assert.Equal("50% off", ascending[0]);
        Assert.Equal("deep well", descending[^1]);
        Assert.Null(descending[0]); // the row of age 51 has no name
    }

    [Fact]
    public async Task Without_a_sort_rows_come_back_in_the_order_they_were_written()
    {
        var db = await SeedAsync();

        var names = await RunAsync(db, new DataQueryRequest());

        Assert.Equal(["50% off", "50 percent", "Well", "WELL", "deep well", "a_b", "axb", "", null], names);
    }
}
