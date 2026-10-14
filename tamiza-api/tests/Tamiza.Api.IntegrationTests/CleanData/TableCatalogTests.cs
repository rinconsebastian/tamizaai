using Npgsql;
using Tamiza.Api.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary>The catalog lookup of design D2: only ordinary tables of the project's own schema are found, by exact name.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TableCatalogTests(PostgresFixture postgres)
{
    private static string Schema(Guid projectId) => DataSources.SchemaName(projectId, DataSources.Output);

    private static async Task<IReadOnlyList<TableColumn>?> FindAsync(string connectionString, string schema, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await TableCatalog.FindAsync(connection, null, schema, table, CancellationToken.None);
    }

    [Fact]
    public async Task A_table_is_found_with_its_columns_types_and_labels_in_table_order()
    {
        var db = await postgres.CreateDatabaseAsync();
        var project = Guid.NewGuid();
        var schema = Schema(project);
        await TestData.ExecuteAsync(db, $"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}".persons (
                name text, age bigint, income double precision, share numeric(5,2), consent boolean,
                born date, at timestamptz, naive timestamp, "Mixed Case" text, _id bigint);
            COMMENT ON COLUMN "{schema}".persons.age IS 'How old is the person?';
            """);

        var columns = await FindAsync(db, schema, "persons");

        Assert.NotNull(columns);
        Assert.Equal(
            ["name", "age", "income", "share", "consent", "born", "at", "naive", "Mixed Case", "_id"],
            columns.Select(c => c.Name));
        Assert.Equal(
            [DataColumnType.Text, DataColumnType.Integer, DataColumnType.Decimal, DataColumnType.Decimal, DataColumnType.Boolean,
             DataColumnType.Date, DataColumnType.Timestamp, DataColumnType.Timestamp, DataColumnType.Text, DataColumnType.Integer],
            columns.Select(c => c.Type));
        Assert.Equal("How old is the person?", columns.Single(c => c.Name == "age").Label);
        Assert.Null(columns.Single(c => c.Name == "name").Label);
    }

    [Fact]
    public async Task A_dropped_column_is_not_listed()
    {
        var db = await postgres.CreateDatabaseAsync();
        var schema = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{schema}"; CREATE TABLE "{schema}".t (a bigint, b text, c text); ALTER TABLE "{schema}".t DROP COLUMN b;""");

        var columns = await FindAsync(db, schema, "t");

        Assert.Equal(["a", "c"], columns!.Select(c => c.Name));
    }

    [Fact]
    public async Task A_table_without_columns_is_found_with_an_empty_list()
    {
        var db = await postgres.CreateDatabaseAsync();
        var schema = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{schema}"; CREATE TABLE "{schema}".empty_table ();""");

        var columns = await FindAsync(db, schema, "empty_table");

        Assert.NotNull(columns);
        Assert.Empty(columns);
    }

    [Theory]
    [InlineData("persons\"; DROP SCHEMA public CASCADE; --")]
    [InlineData("persons; select 1")]
    [InlineData("per sons")]
    [InlineData("PERSONS")]
    [InlineData("persons ")]
    [InlineData("")]
    [InlineData("pg_class")]
    [InlineData("information_schema.tables")]
    public async Task Request_text_that_is_not_the_exact_name_of_a_table_finds_nothing(string requested)
    {
        var db = await postgres.CreateDatabaseAsync();
        var schema = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{schema}"; CREATE TABLE "{schema}".persons (a bigint);""");

        Assert.Null(await FindAsync(db, schema, requested));
        Assert.True(await TestData.ScalarAsync<bool>(db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    [Fact]
    public async Task A_view_is_not_found()
    {
        var db = await postgres.CreateDatabaseAsync();
        var schema = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{schema}"; CREATE TABLE "{schema}".t (a bigint); CREATE VIEW "{schema}".v AS SELECT a FROM "{schema}".t;""");

        Assert.Null(await FindAsync(db, schema, "v"));
        Assert.NotNull(await FindAsync(db, schema, "t"));
    }

    [Fact]
    public async Task A_table_of_another_projects_schema_is_not_found_through_this_projects_schema()
    {
        var db = await postgres.CreateDatabaseAsync();
        var mine = Schema(Guid.NewGuid());
        var theirs = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{mine}"; CREATE SCHEMA "{theirs}"; CREATE TABLE "{theirs}".secret (a bigint);""");

        Assert.Null(await FindAsync(db, mine, "secret"));
        Assert.Null(await FindAsync(db, mine, $"{theirs}.secret"));
    }

    [Fact]
    public async Task A_table_that_is_not_in_the_published_schema_is_not_found_even_if_it_exists_elsewhere()
    {
        var db = await postgres.CreateDatabaseAsync();
        var schema = Schema(Guid.NewGuid());
        await TestData.ExecuteAsync(db, $"""CREATE SCHEMA "{schema}"; CREATE TABLE public.metadata_table (a bigint);""");

        Assert.Null(await FindAsync(db, schema, "metadata_table"));
    }
}
