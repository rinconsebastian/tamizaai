using Npgsql;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Creates the schemas, tables and run records that the analytics worker would publish, with plain SQL.</summary>
public static class PublishedTables
{
    public static string Schema(Guid projectId, string step) => $"p_{projectId:N}_{step}";

    /// <summary>
    /// Replaces the project's <paramref name="step"/> schema with the given tables, as one publish does. Each table is
    /// <c>(name, "column definitions", rows as SQL values)</c>, for example <c>("persons", "name text, age bigint", "('Ana', 31)")</c>.
    /// Records a succeeded step of a new run, so the dashboard knows when and by which run the tables were published.
    /// </summary>
    public static async Task<Guid> PublishAsync(
        string connectionString, Guid projectId, string step, params (string Name, string Columns, string Rows)[] tables)
    {
        var schema = Schema(projectId, step);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Run(connection, transaction, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        await Run(connection, transaction, $"CREATE SCHEMA \"{schema}\"");
        foreach (var (name, columns, rows) in tables)
        {
            await Run(connection, transaction, $"CREATE TABLE \"{schema}\".\"{name}\" ({columns})");
            if (rows.Length > 0)
            {
                await Run(connection, transaction, $"INSERT INTO \"{schema}\".\"{name}\" VALUES {rows}");
            }
        }

        await transaction.CommitAsync();
        return await RecordStepAsync(connectionString, projectId, step, tables.Select(t => (t.Name, CountRows(t.Rows))).ToArray());
    }

    /// <summary>
    /// Adds or replaces one table of the project's <paramref name="step"/> schema, as the worker's per-table publish of an indicator does:
    /// the other tables stay. Records a succeeded step for it (for <c>indicators</c>, an <c>indicator</c> step named after the table).
    /// </summary>
    public static async Task<Guid> PublishTableAsync(string connectionString, Guid projectId, string step, string name, string columns, string rows)
    {
        var schema = Schema(projectId, step);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Run(connection, transaction, $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"");
        await Run(connection, transaction, $"DROP TABLE IF EXISTS \"{schema}\".\"{name}\"");
        await Run(connection, transaction, $"CREATE TABLE \"{schema}\".\"{name}\" ({columns})");
        if (rows.Length > 0)
        {
            await Run(connection, transaction, $"INSERT INTO \"{schema}\".\"{name}\" VALUES {rows}");
        }

        await transaction.CommitAsync();
        return await RecordStepAsync(connectionString, projectId, step, [(name, CountRows(rows))]);
    }

    /// <summary>
    /// Records a run with one step, as the worker does when a step succeeds, and returns the run's id. The step <c>indicators</c> (the
    /// schema's name) is recorded as the worker records it: one <c>indicator</c> step per table, named after it; with no tables,
    /// <paramref name="indicator"/> names the one step (a failed indicator).
    /// </summary>
    public static async Task<Guid> RecordStepAsync(
        string connectionString, Guid projectId, string step, (string Name, long Rows)[] tables, string status = "succeeded",
        DateTimeOffset? finishedAt = null, string? indicator = null)
    {
        var runId = Guid.CreateVersion7();
        var finished = finishedAt ?? DateTimeOffset.UtcNow;
        await TestData.ExecuteAsync(connectionString,
            """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at)
            VALUES (@id, @project, 'manual', @runStatus, @finished, @finished, @finished)
            """,
            ("id", runId), ("project", projectId), ("runStatus", status == "succeeded" ? "succeeded" : "failed"), ("finished", finished));
        var steps = step == "indicators"
            ? (tables.Length > 0 ? tables.Select(t => (Name: t.Name, Tables: new[] { t })) : [(Name: indicator ?? throw new ArgumentException("Name the indicator.", nameof(indicator)), Tables: tables)])
                .Select(s => ("indicator", s.Name, s.Tables)).ToArray()
            : [(step, "", tables)];
        foreach (var (stepName, name, stepTables) in steps)
        {
            var json = "[" + string.Join(",", stepTables.Select(t => $"{{\"name\":\"{t.Name}\",\"rows\":{t.Rows}}}")) + "]";
            await TestData.ExecuteAsync(connectionString,
                """
                INSERT INTO tamiza.run_steps (run_id, step, name, status, started_at, finished_at, tables)
                VALUES (@id, @step, @name, @status, @finished, @finished, @tables::jsonb)
                """,
                ("id", runId), ("step", stepName), ("name", name), ("status", status), ("finished", finished), ("tables", json));
        }

        return runId;
    }

    public static Task DropAsync(string connectionString, Guid projectId, string step) =>
        TestData.ExecuteAsync(connectionString, $"DROP SCHEMA IF EXISTS \"{Schema(projectId, step)}\" CASCADE");

    public static Task CommentAsync(string connectionString, Guid projectId, string step, string table, string column, string label) =>
        TestData.ExecuteAsync(connectionString, $"COMMENT ON COLUMN \"{Schema(projectId, step)}\".\"{table}\".\"{column}\" IS '{label.Replace("'", "''")}'");

    // Counts the top-level tuples of a VALUES list: every "(" outside quotes that opens a row.
    private static long CountRows(string rows)
    {
        long count = 0;
        var depth = 0;
        var quoted = false;
        foreach (var c in rows)
        {
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '(')
            {
                if (depth++ == 0)
                {
                    count++;
                }
            }
            else if (!quoted && c == ')')
            {
                depth--;
            }
        }

        return count;
    }

    private static async Task Run(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
