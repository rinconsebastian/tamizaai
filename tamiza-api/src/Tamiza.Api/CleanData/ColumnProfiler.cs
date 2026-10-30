using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace Tamiza.Api.CleanData;

/// <summary>How a text column's values are listed: as the choices of a select question, or as short lists of values several rows share.</summary>
public enum ProfileSelect
{
    /// <summary>Every answer is one choice label.</summary>
    One,

    /// <summary>An answer is the labels of the chosen choices joined by <c>"; "</c>.</summary>
    Multiple,
}

/// <summary>A choice label or a value and how many rows hold it: a number, or <c>fewer than 5</c> when the count is small enough to point at a person.</summary>
public sealed record ProfileCount(string Value, object Count);

/// <summary>What is known of one column over every row of the table as last published (design D8).</summary>
public sealed record ColumnProfile(
    string Name,
    DataColumnType Type,
    string? Label,
    long Rows,
    long Empty,
    long Distinct,
    double? Min,
    double? Max,
    double? Mean,
    double? Q1,
    double? Median,
    double? Q3,
    string? Earliest,
    string? Latest,
    long? TrueCount,
    long? FalseCount,
    IReadOnlyList<ProfileCount>? Choices,
    long? MoreChoices,
    IReadOnlyList<ProfileCount>? Values,
    long? OtherValues,
    long? OtherRows,
    string? Note);

/// <param name="Rows">Rows of the table.</param>
/// <param name="OtherColumns">The columns that were not profiled because the table has more than the limit and the call named none.</param>
public sealed record TableProfile(string Table, long Rows, DateTimeOffset? PublishedAt, IReadOnlyList<ColumnProfile> Columns, IReadOnlyList<string> OtherColumns);

/// <summary>A column name the table does not have.</summary>
public sealed class ProfileColumnsNotFoundException(IReadOnlyList<string> names) : Exception("The table has no such column.")
{
    public IReadOnlyList<string> Names { get; } = names;
}

/// <summary>
/// Profiles the columns of a published table in SQL (design D8): counts, empty and distinct values, ranges and quartiles of numbers,
/// the earliest and latest of dates, the answers of select questions, and the values that several rows share in short lists. Free
/// text is never listed, and a count that could point at one person is reported as "fewer than 5". Results are kept 30 minutes per
/// publication, so a republish starts fresh.
/// </summary>
public sealed class ColumnProfiler(DataDashboard dashboard, IMemoryCache cache)
{
    /// <summary>The most columns profiled in one call.</summary>
    public const int MaxColumns = 40;

    /// <summary>Counts below this are reported as "fewer than N", and a value held by fewer rows is not listed.</summary>
    public const int MinGroupRows = 5;

    /// <summary>A text column with more distinct values than this is free text: no value is listed.</summary>
    public const int MaxListValues = 50;

    /// <summary>The most choice labels listed for a select question; the rest are counted.</summary>
    public const int MaxChoices = 200;

    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);

    public const string FewerThanFive = "fewer than 5";

    /// <summary>
    /// Profiles <paramref name="requested"/> (or the first <see cref="MaxColumns"/> columns) of <paramref name="table"/> in <paramref name="schema"/>.
    /// Returns null when the table is not published. <paramref name="step"/> is the run step whose publication versions the cache.
    /// </summary>
    /// <param name="selectOf">Says whether a column, by name, answers a select question, and of which kind.</param>
    /// <exception cref="ProfileColumnsNotFoundException">A requested column is not in the table.</exception>
    /// <exception cref="DataTimeoutException">The profile took longer than <paramref name="timeout"/>.</exception>
    public async Task<TableProfile?> ProfileAsync(
        Guid projectId, string schema, string step, string table, IReadOnlyList<string>? requested, Func<string, ProfileSelect?> selectOf,
        TimeSpan timeout, CancellationToken ct) =>
        await dashboard.InTransactionAsync(projectId, timeout, async (connection, transaction, _) =>
        {
            var columns = await DataDashboard.LockAndDescribeAsync(connection, transaction, schema, table, ct);
            if (columns is null)
            {
                return null;
            }

            var chosen = Choose(columns, requested, out var others);
            var published = await DataDashboard.LatestStepAsync(connection, transaction, projectId, step, ct);
            var key = $"profile|{schema}|{table}|{string.Join(',', chosen.Select(c => c.Name))}|{published?.RunId}|{published?.FinishedAt?.UtcTicks}";
            if (cache.TryGetValue(key, out TableProfile? cached) && cached is not null)
            {
                return cached;
            }

            // PostgreSQL stops a slow statement at the timeout; the driver must wait longer than that, or its own 30-second limit fires first.
            var commandSeconds = (int)Math.Ceiling(timeout.TotalSeconds) + 30;
            var profile = await ComputeAsync(connection, transaction, schema, table, chosen, others, selectOf, published?.FinishedAt, commandSeconds, ct);
            cache.Set(key, profile, CacheTime);
            return profile;
        }, ct);

    private static IReadOnlyList<TableColumn> Choose(IReadOnlyList<TableColumn> columns, IReadOnlyList<string>? requested, out IReadOnlyList<string> others)
    {
        if (requested is { Count: > 0 })
        {
            var byName = columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var missing = requested.Where(name => !byName.ContainsKey(name)).Distinct(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                throw new ProfileColumnsNotFoundException(missing);
            }

            var picked = requested.Distinct(StringComparer.Ordinal).Take(MaxColumns).Select(name => byName[name]).ToList();
            others = [];
            return picked;
        }

        others = columns.Skip(MaxColumns).Select(c => c.Name).ToList();
        return columns.Take(MaxColumns).ToList();
    }

    private static async Task<TableProfile> ComputeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, string table, IReadOnlyList<TableColumn> columns,
        IReadOnlyList<string> others, Func<string, ProfileSelect?> selectOf, DateTimeOffset? publishedAt, int commandSeconds, CancellationToken ct)
    {
        var from = $"{DataSources.Quote(schema)}.{DataSources.Quote(table)}";
        if (columns.Count == 0)
        {
            var rowsOnly = await ScalarAsync<long>(connection, transaction, $"SELECT count(*) FROM {from}", commandSeconds, ct);
            return new TableProfile(table, rowsOnly, publishedAt, [], others);
        }

        // One scan for the counts, ranges and quartiles of every column.
        var select = new List<string> { "count(*)" };
        foreach (var column in columns)
        {
            var q = DataSources.Quote(column.Name);
            select.Add(column.Type == DataColumnType.Text
                ? $"count(*) FILTER (WHERE {q} IS NULL OR btrim({q}) = '')"
                : $"count(*) FILTER (WHERE {q} IS NULL)");
            // Distinct text is counted on a 64-bit hash of the value: sorting 500,000 long strings took 8 seconds a column, sorting their hashes half a second.
            // Two different values share a hash with a probability around 1e-8 at that size, and the count is a description, not a key.
            select.Add(column.Type == DataColumnType.Text ? $"count(DISTINCT hashtextextended({q}, 0))" : $"count(DISTINCT {q})");
            switch (column.Type)
            {
                case DataColumnType.Integer or DataColumnType.Decimal:
                    select.Add($"min({q})::double precision");
                    select.Add($"max({q})::double precision");
                    select.Add($"avg({q})::double precision");
                    select.Add($"percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY {q}::double precision)");
                    break;
                case DataColumnType.Date or DataColumnType.Timestamp:
                    select.Add($"min({q})::text");
                    select.Add($"max({q})::text");
                    break;
                case DataColumnType.Boolean:
                    select.Add($"count(*) FILTER (WHERE {q})");
                    select.Add($"count(*) FILTER (WHERE NOT {q})");
                    break;
            }
        }

        long rows;
        var profiles = new List<ColumnProfile>();
        await using (var command = new NpgsqlCommand($"SELECT {string.Join(", ", select)} FROM {from}", connection, transaction) { CommandTimeout = commandSeconds })
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            var at = 0;
            rows = reader.GetInt64(at++);
            foreach (var column in columns)
            {
                var empty = reader.GetInt64(at++);
                var distinct = reader.GetInt64(at++);
                double? min = null, max = null, mean = null, q1 = null, median = null, q3 = null;
                string? earliest = null, latest = null;
                long? trueCount = null, falseCount = null;
                switch (column.Type)
                {
                    case DataColumnType.Integer or DataColumnType.Decimal:
                        min = Number(reader, at++);
                        max = Number(reader, at++);
                        mean = Number(reader, at++);
                        if (!reader.IsDBNull(at) && reader.GetFieldValue<double[]>(at) is [var a, var b, var c])
                        {
                            (q1, median, q3) = (a, b, c);
                        }

                        at++;
                        break;
                    case DataColumnType.Date or DataColumnType.Timestamp:
                        earliest = reader.IsDBNull(at) ? null : reader.GetString(at);
                        at++;
                        latest = reader.IsDBNull(at) ? null : reader.GetString(at);
                        at++;
                        break;
                    case DataColumnType.Boolean:
                        trueCount = reader.GetInt64(at++);
                        falseCount = reader.GetInt64(at++);
                        break;
                }

                profiles.Add(new ColumnProfile(
                    column.Name, column.Type, column.Label, rows, empty, distinct, Round(min), Round(max), Round(mean), Round(q1), Round(median), Round(q3),
                    earliest, latest, trueCount, falseCount, null, null, null, null, null, null));
            }
        }

        // The text columns that list values need one more read each.
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            if (profile.Type != DataColumnType.Text)
            {
                continue;
            }

            var q = DataSources.Quote(profile.Name);
            if (selectOf(profile.Name) is { } select1)
            {
                var counts = await GroupAsync(connection, transaction, from, q, select1 == ProfileSelect.Multiple, MaxChoices + 1, commandSeconds, ct);
                var shown = counts.Take(MaxChoices).Select(c => new ProfileCount(c.Value, c.Count < MinGroupRows ? FewerThanFive : c.Count)).ToList();
                profiles[i] = profile with { Choices = shown, MoreChoices = counts.Count > MaxChoices ? counts.Count - MaxChoices : null };
            }
            else if (profile.Distinct is > 0 and <= MaxListValues)
            {
                var counts = await GroupAsync(connection, transaction, from, q, split: false, MaxListValues + 1, commandSeconds, ct);
                var listed = counts.Where(c => c.Count >= MinGroupRows).Select(c => new ProfileCount(c.Value, c.Count)).ToList();
                var folded = counts.Where(c => c.Count < MinGroupRows).ToList();
                profiles[i] = profile with
                {
                    Values = listed,
                    OtherValues = folded.Count > 0 ? folded.Count : null,
                    OtherRows = folded.Count > 0 ? folded.Sum(c => c.Count) : null,
                };
            }
            else if (profile.Distinct > MaxListValues)
            {
                profiles[i] = profile with { Note = "Many distinct values: this looks like free text or an identifier, so no value is listed." };
            }
        }

        return new TableProfile(table, rows, publishedAt, profiles, others);
    }

    private static async Task<List<(string Value, long Count)>> GroupAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string from, string quoted, bool split, int limit, int commandSeconds, CancellationToken ct)
    {
        var sql = split
            ? $"SELECT v, count(*) FROM {from}, LATERAL unnest(string_to_array({quoted}, '; ')) AS v WHERE {quoted} IS NOT NULL AND btrim(v) <> '' GROUP BY v ORDER BY 2 DESC, 1 LIMIT @limit"
            : $"SELECT {quoted}, count(*) FROM {from} WHERE {quoted} IS NOT NULL AND btrim({quoted}) <> '' GROUP BY {quoted} ORDER BY 2 DESC, 1 LIMIT @limit";
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = commandSeconds };
        command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });
        var result = new List<(string, long)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return result;
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, int commandSeconds, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = commandSeconds };
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static double? Number(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>Quartiles and means carry 15 significant digits of noise; four decimals say what a model needs.</summary>
    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 4, MidpointRounding.AwayFromZero);
}
