using Npgsql;
using NpgsqlTypes;

namespace Tamiza.Api.CleanData;

/// <summary>
/// Finds a published table in PostgreSQL's catalog. Every table and column name that later reaches SQL has been returned by
/// this lookup, so request text is never used as an identifier (design D2).
/// </summary>
public static class TableCatalog
{
    private const string Sql = """
        SELECT a.attname, format_type(a.atttypid, a.atttypmod), col_description(a.attrelid, a.attnum)
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
        WHERE n.nspname = @schema AND c.relname = @table AND c.relkind = 'r'
        ORDER BY a.attnum
        """;

    /// <summary>
    /// The columns of the ordinary table <paramref name="table"/> in <paramref name="schema"/>, in table order, or null when
    /// the schema has no such table. A view, an index or a table in any other schema is not found. A table without columns
    /// is found and has an empty list.
    /// </summary>
    public static async Task<IReadOnlyList<TableColumn>?> FindAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string schema, string table, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("schema", NpgsqlDbType.Text) { Value = schema });
        command.Parameters.Add(new NpgsqlParameter("table", NpgsqlDbType.Text) { Value = table });
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = new List<TableColumn>();
        var found = false;
        while (await reader.ReadAsync(ct))
        {
            found = true;
            if (reader.IsDBNull(0))
            {
                continue; // the table exists and has no columns
            }

            columns.Add(new TableColumn(
                reader.GetString(0),
                ColumnTypes.FromPostgres(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return found ? columns : null;
    }
}
