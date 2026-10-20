using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class DataQueryBuilderTests
{
    private static readonly TableColumn[] Columns =
    [
        new("name", DataColumnType.Text, null),
        new("age", DataColumnType.Integer, "Age"),
        new("income", DataColumnType.Decimal, null),
        new("consent", DataColumnType.Boolean, null),
        new("born", DataColumnType.Date, null),
        new("at", DataColumnType.Timestamp, null),
        new("Mixed Case", DataColumnType.Text, null),
    ];

    private static (DataQuery? Query, FieldErrors Errors) Build(DataQueryRequest request, string zone = "UTC")
    {
        var errors = new FieldErrors();
        return (DataQueryBuilder.Build(request, Columns, zone, errors), errors);
    }

    private static DataQuery Valid(DataQueryRequest request, string zone = "UTC")
    {
        var (query, errors) = Build(request, zone);
        Assert.False(errors.Any, string.Join("; ", errors.ToDictionary().SelectMany(e => e.Value)));
        return query!;
    }

    private static string[] Messages(FieldErrors errors, string field) => errors.ToDictionary()[field];

    private static DataQueryRequest Filter(params DataFilter[] filters) => new(filters);

    // Defaults

    [Fact]
    public void An_empty_request_reads_page_1_of_50_in_the_order_the_script_wrote()
    {
        var query = Valid(new DataQueryRequest());

        Assert.Equal("", query.WhereClause);
        Assert.Equal("ctid", query.OrderBy);
        Assert.Equal((1, 50, 0L), (query.Page, query.PageSize, query.Offset));
        Assert.Empty(query.Parameters);
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 100, 200)]
    public void The_offset_follows_the_page_and_size(int page, int size, long offset) =>
        Assert.Equal(offset, Valid(new DataQueryRequest(Page: page, PageSize: size)).Offset);

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_page_size_other_than_25_50_or_100_is_rejected(int size)
    {
        var (query, errors) = Build(new DataQueryRequest(PageSize: size));

        Assert.Null(query);
        Assert.Contains("25, 50, 100", Messages(errors, "pageSize")[0]);
    }

    [Fact]
    public void A_page_below_1_is_rejected()
    {
        var (_, errors) = Build(new DataQueryRequest(Page: 0));

        Assert.Single(Messages(errors, "page"));
    }

    // Sort

    [Fact]
    public void A_sort_quotes_the_column_puts_empty_values_last_and_ends_with_the_row_position()
    {
        Assert.Equal("\"age\" DESC NULLS LAST, ctid", Valid(new DataQueryRequest(Sort: new DataSort("age", "desc"))).OrderBy);
        Assert.Equal("\"Mixed Case\" ASC NULLS LAST, ctid", Valid(new DataQueryRequest(Sort: new DataSort("Mixed Case", "asc"))).OrderBy);
    }

    [Theory]
    [InlineData("salary", "asc", "no column 'salary'")]
    [InlineData("age", "ascending", "'asc' or 'desc'")]
    [InlineData("age\"; DROP TABLE x; --", "asc", "no column")]
    public void A_sort_on_an_unknown_column_or_with_a_bad_direction_is_rejected(string column, string direction, string message)
    {
        var (query, errors) = Build(new DataQueryRequest(Sort: new DataSort(column, direction)));

        Assert.Null(query);
        Assert.Contains(message, Messages(errors, "sort")[0]);
    }

    // Unknown columns, duplicates, counts

    [Fact]
    public void A_filter_on_a_column_the_table_does_not_have_is_rejected_naming_the_column()
    {
        var (query, errors) = Build(Filter(new DataFilter("salary", "equals", "x")));

        Assert.Null(query);
        Assert.Contains("'salary'", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void Column_names_are_matched_exactly()
    {
        var (_, errors) = Build(Filter(new DataFilter("Name", "equals", "x")));

        Assert.Contains("'Name'", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void Two_filters_on_one_column_are_rejected()
    {
        var (_, errors) = Build(Filter(new DataFilter("name", "equals", "a"), new DataFilter("name", "contains", "b")));

        Assert.Contains("more than one filter", Messages(errors, "filters[1]")[0]);
    }

    [Fact]
    public void More_than_50_filters_are_rejected()
    {
        var filters = Enumerable.Range(0, 51).Select(_ => new DataFilter("name", "empty")).ToArray();

        var (_, errors) = Build(Filter(filters));

        Assert.Contains("At most 50", Messages(errors, "filters")[0]);
    }

    // Text

    [Fact]
    public void Contains_is_a_case_insensitive_match_with_the_wildcards_of_the_value_escaped()
    {
        var query = Valid(Filter(new DataFilter("name", "contains", "50%_ off\\")));

        Assert.Equal("\"name\"::text ILIKE '%' || @f0 || '%' ESCAPE '\\'", query.Where);
        var parameter = Assert.Single(query.Parameters);
        Assert.Equal(("f0", NpgsqlDbType.Text, "50\\%\\_ off\\\\"), (parameter.Name, parameter.Type, parameter.Value));
    }

    [Fact]
    public void Equals_compares_the_exact_value_as_a_parameter()
    {
        var query = Valid(Filter(new DataFilter("name", "equals", "Cali'; DROP TABLE x; --")));

        Assert.Equal("\"name\"::text = @f0", query.Where);
        Assert.Equal("Cali'; DROP TABLE x; --", query.Parameters.Single().Value);
        Assert.DoesNotContain("DROP", query.Where);
    }

    [Fact]
    public void In_matches_any_of_the_chosen_values()
    {
        var query = Valid(Filter(new DataFilter("name", "in", Values: ["Cali", "Palmira"])));

        Assert.Equal("\"name\"::text = ANY(@f0)", query.Where);
        Assert.Equal(new[] { "Cali", "Palmira" }, (string[])query.Parameters.Single().Value);
    }

    [Fact]
    public void A_text_filter_without_its_value_or_with_too_long_a_value_is_rejected()
    {
        var (_, errors) = Build(Filter(new DataFilter("name", "equals"), new DataFilter("Mixed Case", "contains", new string('x', 501))));

        Assert.Contains("needs a value", Messages(errors, "filters[0]")[0]);
        Assert.Contains("longer than 500", Messages(errors, "filters[1]")[0]);
    }

    [Fact]
    public void In_needs_values_and_accepts_at_most_100()
    {
        var (_, errors) = Build(Filter(
            new DataFilter("name", "in", Values: []),
            new DataFilter("Mixed Case", "in", Values: Enumerable.Range(0, 101).Select(n => n.ToString()).ToArray())));

        Assert.Contains("at least one value", Messages(errors, "filters[0]")[0]);
        Assert.Contains("at most 100", Messages(errors, "filters[1]")[0]);
    }

    // Ranges

    [Fact]
    public void An_integer_range_includes_both_ends()
    {
        var query = Valid(Filter(new DataFilter("age", "range", Min: "18", Max: "29")));

        Assert.Equal("\"age\" >= @f0min AND \"age\" <= @f0max", query.Where);
        Assert.Equal([18L, 29L], query.Parameters.Select(p => p.Value));
        Assert.All(query.Parameters, p => Assert.Equal(NpgsqlDbType.Bigint, p.Type));
    }

    [Fact]
    public void A_range_may_have_only_one_end()
    {
        Assert.Equal("\"income\" >= @f0min", Valid(Filter(new DataFilter("income", "range", Min: "500000.5"))).Where);
        Assert.Equal("\"born\" <= @f0max", Valid(Filter(new DataFilter("born", "range", Max: "2026-09-01"))).Where);
    }

    [Fact]
    public void A_decimal_range_reads_a_dot_as_the_separator_whatever_the_culture()
    {
        var query = Valid(Filter(new DataFilter("income", "range", Min: "1234.50")));

        Assert.Equal(1234.50m, query.Parameters.Single().Value);
    }

    [Theory]
    [InlineData("age", "adult", null)]
    [InlineData("age", "1.5", null)]
    [InlineData("age", null, "99999999999999999999")]
    [InlineData("income", "1,5", null)]
    [InlineData("income", "NaN", null)]
    [InlineData("born", "01/09/2026", null)]
    [InlineData("born", "2026-13-01", null)]
    [InlineData("at", "yesterday", null)]
    public void A_range_value_that_cannot_be_read_as_the_columns_type_is_rejected_naming_the_column(string column, string? min, string? max)
    {
        var (query, errors) = Build(Filter(new DataFilter(column, "range", Min: min, Max: max)));

        Assert.Null(query);
        Assert.Contains($"'{column}'", Messages(errors, "filters[0]")[0]);
    }

    [Theory]
    [InlineData("age", "60", "18")]
    [InlineData("income", "10.5", "10.4")]
    [InlineData("born", "2026-09-02", "2026-09-01")]
    [InlineData("at", "2026-09-02", "2026-09-01")]
    public void A_range_whose_minimum_is_above_its_maximum_is_rejected(string column, string min, string max)
    {
        var (_, errors) = Build(Filter(new DataFilter(column, "range", Min: min, Max: max)));

        Assert.Contains("minimum is greater than the maximum", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void A_range_with_no_end_is_rejected()
    {
        var (_, errors) = Build(Filter(new DataFilter("age", "range")));

        Assert.Contains("minimum or a maximum", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void A_timestamp_range_is_in_days_of_the_projects_zone_and_includes_the_whole_last_day()
    {
        var query = Valid(Filter(new DataFilter("at", "range", Min: "2026-09-01", Max: "2026-09-01")), zone: "America/Bogota");

        Assert.Equal(
            "\"at\" >= (@f0min::date)::timestamp AT TIME ZONE @tz AND \"at\" < ((@f0max::date + 1)::timestamp AT TIME ZONE @tz)",
            query.Where);
        Assert.Equal("America/Bogota", query.Parameters.Single(p => p.Name == "tz").Value);
        Assert.Equal(new DateOnly(2026, 9, 1), query.Parameters.Single(p => p.Name == "f0max").Value);
    }

    [Fact]
    public void The_zone_is_bound_once_for_several_timestamp_filters_and_not_at_all_without_one()
    {
        Assert.DoesNotContain(Valid(new DataQueryRequest(Page: 1)).Parameters, p => p.Name == "tz");

        var two = new TableColumn[] { new("a", DataColumnType.Timestamp, null), new("b", DataColumnType.Timestamp, null) };
        var built = DataQueryBuilder.Build(
            Filter(new DataFilter("a", "range", Min: "2026-09-01"), new DataFilter("b", "range", Max: "2026-09-02")), two, "UTC", new FieldErrors());

        Assert.Single(built!.Parameters, p => p.Name == "tz");
    }

    // A second set of filters for the same command

    [Fact]
    public void A_parameter_prefix_names_the_second_set_and_keeps_the_zone_parameter()
    {
        var errors = new FieldErrors();
        var query = DataQueryBuilder.Build(
            Filter(new DataFilter("name", "in", Values: ["Cali"]), new DataFilter("at", "range", Min: "2026-09-01")),
            Columns, "America/Bogota", errors, parameterPrefix: "d", fieldName: "dashboardFilters");

        Assert.False(errors.Any);
        Assert.Equal("\"name\"::text = ANY(@d0) AND \"at\" >= (@d1min::date)::timestamp AT TIME ZONE @tz", query!.Where);
        Assert.Equal(new[] { "d0", "d1min", "tz" }, query.Parameters.Select(p => p.Name));
    }

    [Fact]
    public void A_field_name_keys_the_errors_of_the_second_set()
    {
        var errors = new FieldErrors();
        DataQueryBuilder.Build(
            Filter(new DataFilter("name", "in", Values: ["Cali"]), new DataFilter("age", "range", Min: "adult")),
            Columns, "UTC", errors, parameterPrefix: "d", fieldName: "dashboardFilters");

        Assert.Equal(["dashboardFilters[1]"], errors.ToDictionary().Keys);
        Assert.Contains("age", errors.ToDictionary()["dashboardFilters[1]"].Single());
    }

    // True/false and empty

    [Fact]
    public void A_true_false_column_filters_by_the_chosen_values()
    {
        var query = Valid(Filter(new DataFilter("consent", "in", Values: ["true"])));

        Assert.Equal("\"consent\" = ANY(@f0)", query.Where);
        Assert.Equal(new[] { true }, (bool[])query.Parameters.Single().Value);
    }

    [Fact]
    public void A_true_false_value_that_is_neither_is_rejected()
    {
        var (_, errors) = Build(Filter(new DataFilter("consent", "in", Values: ["maybe"])));

        Assert.Contains("'maybe' is not true or false", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void Empty_for_text_counts_an_empty_string_and_for_other_columns_only_null()
    {
        Assert.Equal("(\"name\" IS NULL OR \"name\"::text = '')", Valid(Filter(new DataFilter("name", "empty"))).Where);
        Assert.Equal("\"age\" IS NULL", Valid(Filter(new DataFilter("age", "empty"))).Where);
        Assert.Equal("(\"name\" IS NOT NULL AND \"name\"::text <> '')", Valid(Filter(new DataFilter("name", "notEmpty"))).Where);
        Assert.Equal("\"born\" IS NOT NULL", Valid(Filter(new DataFilter("born", "notEmpty"))).Where);
    }

    // Ops that do not fit the type

    [Theory]
    [InlineData("age", "contains")]
    [InlineData("age", "equals")]
    [InlineData("age", "in")]
    [InlineData("name", "range")]
    [InlineData("consent", "range")]
    [InlineData("consent", "contains")]
    [InlineData("at", "equals")]
    [InlineData("name", "startsWith")]
    [InlineData("name", "")]
    public void A_filter_the_columns_type_does_not_support_is_rejected(string column, string op)
    {
        var (query, errors) = Build(Filter(new DataFilter(column, op, "x", ["x"], "1", "2")));

        Assert.Null(query);
        Assert.Contains($"'{column}'", Messages(errors, "filters[0]")[0]);
    }

    [Fact]
    public void Several_filters_are_joined_with_and_and_keep_their_own_parameters()
    {
        var query = Valid(Filter(
            new DataFilter("name", "equals", "Cali"),
            new DataFilter("age", "range", Min: "18", Max: "29"),
            new DataFilter("consent", "in", Values: ["true", "false"])));

        Assert.Equal(
            "\"name\"::text = @f0 AND \"age\" >= @f1min AND \"age\" <= @f1max AND \"consent\" = ANY(@f2)", query.Where);
        Assert.Equal(["f0", "f1min", "f1max", "f2"], query.Parameters.Select(p => p.Name));
        Assert.StartsWith(" WHERE ", query.WhereClause);
    }

    [Fact]
    public void Every_error_is_collected_before_the_request_is_refused()
    {
        var (query, errors) = Build(new DataQueryRequest(
            [new DataFilter("salary", "equals", "x"), new DataFilter("age", "range", Min: "adult")], new DataSort("age", "up"), 0, 7));

        Assert.Null(query);
        Assert.Equal(["filters[0]", "filters[1]", "page", "pageSize", "sort"], errors.ToDictionary().Keys.Order());
    }

    [Fact]
    public void Parameters_can_be_created_for_more_than_one_command()
    {
        var query = Valid(Filter(new DataFilter("name", "equals", "Cali")));

        var first = query.Parameters.Select(p => p.ToParameter()).ToList();
        var second = query.Parameters.Select(p => p.ToParameter()).ToList();

        Assert.NotSame(first[0], second[0]);
        Assert.IsType<NpgsqlParameter>(first[0]);
    }

    [Theory]
    [InlineData("50%", "50\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("plain", "plain")]
    public void Like_wildcards_are_escaped(string value, string escaped) =>
        Assert.Equal(escaped, DataQueryBuilder.EscapeLike(value));
}
