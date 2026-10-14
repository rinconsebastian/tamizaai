using Tamiza.Api.CleanData;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class ColumnTypesTests
{
    // What the analytics publisher creates (interchange.postgres_type) and how PostgreSQL's format_type spells it.
    [Theory]
    [InlineData("bigint", DataColumnType.Integer)]
    [InlineData("integer", DataColumnType.Integer)]
    [InlineData("smallint", DataColumnType.Integer)]
    [InlineData("numeric", DataColumnType.Decimal)]
    [InlineData("numeric(12,2)", DataColumnType.Decimal)]
    [InlineData("double precision", DataColumnType.Decimal)]
    [InlineData("real", DataColumnType.Decimal)]
    [InlineData("boolean", DataColumnType.Boolean)]
    [InlineData("date", DataColumnType.Date)]
    [InlineData("timestamp with time zone", DataColumnType.Timestamp)]
    [InlineData("timestamp without time zone", DataColumnType.Timestamp)]
    [InlineData("timestamp(3) with time zone", DataColumnType.Timestamp)]
    [InlineData("text", DataColumnType.Text)]
    [InlineData("character varying(20)", DataColumnType.Text)]
    [InlineData("uuid", DataColumnType.Text)]
    [InlineData("jsonb", DataColumnType.Text)]
    [InlineData("time without time zone", DataColumnType.Text)]
    public void Postgres_types_map_to_the_dashboards_types(string formatType, DataColumnType expected) =>
        Assert.Equal(expected, ColumnTypes.FromPostgres(formatType));

    [Fact]
    public void Schema_names_are_the_projects_id_without_dashes_and_the_source()
    {
        var id = Guid.Parse("01a0ffdc-1204-7018-8195-fe685d20c5c9");

        Assert.Equal("p_01a0ffdc120470188195fe685d20c5c9_output", DataSources.SchemaName(id, "output"));
        Assert.Equal("p_01a0ffdc120470188195fe685d20c5c9_clean", DataSources.SchemaName(id, "clean"));
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("indicators")]
    [InlineData("output; DROP SCHEMA public")]
    [InlineData("OUTPUT")]
    [InlineData("")]
    public void Only_output_and_clean_are_sources(string source)
    {
        Assert.False(DataSources.IsKnown(source));
        Assert.Throws<ArgumentOutOfRangeException>(() => DataSources.SchemaName(Guid.NewGuid(), source));
    }

    [Theory]
    [InlineData("age", "\"age\"")]
    [InlineData("has \"quote\"", "\"has \"\"quote\"\"\"")]
    [InlineData("a;b", "\"a;b\"")]
    public void Identifiers_are_quoted_with_doubled_quotes(string name, string quoted) =>
        Assert.Equal(quoted, DataSources.Quote(name));
}
