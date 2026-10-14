using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.Data;

namespace Tamiza.Api.Performance;

/// <summary>
/// Computes the performance dashboard of a project from its stored submissions (design D4) and keeps the figures for a short
/// time (design D5). One instance serves the whole process: it owns the cache, the computations in flight and the slots.
/// </summary>
public sealed class PerformanceDashboard : IDisposable
{
    /// <summary>How long figures are shown again, counted from the start of the statement that computed them.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(60);

    private const int CacheEntries = 64;

    /// <summary>What decides that two requests want the same figures, and that nothing has arrived since they were computed.</summary>
    private readonly record struct CacheKey(
        Guid ProjectId,
        DateOnly? From,
        DateOnly? To,
        string TimeZone,
        DateTimeOffset FormCheckedAt,
        string? EnumeratorField,
        DateTimeOffset? FrameUpdatedAt,
        DateTimeOffset? LastWebhookAt,
        DateTimeOffset? LastSyncFinishedAt);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly PerformanceOptions _options;
    private readonly IPerformanceProbe _probe;
    private readonly ILogger<PerformanceDashboard> _logger;

    // Dedicated, with a size limit so that ranges asked for once cannot grow it without bound.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheEntries });
    private readonly ConcurrentDictionary<CacheKey, Lazy<Task<PerformanceDashboardResult>>> _inFlight = new();
    private readonly SemaphoreSlim _slots;

    public PerformanceDashboard(
        IServiceScopeFactory scopes, TimeProvider time, IOptions<PerformanceOptions> options, IPerformanceProbe probe, ILogger<PerformanceDashboard> logger)
    {
        _scopes = scopes;
        _time = time;
        _options = options.Value;
        _probe = probe;
        _logger = logger;
        _slots = new SemaphoreSlim(_options.MaxConcurrentComputations, _options.MaxConcurrentComputations);
    }

    /// <summary>
    /// The figures for the project and range: the cached ones while nothing has arrived and they are under a minute old,
    /// otherwise a computation, shared with any identical request in flight. Throws <see cref="DashboardTimeoutException"/> when
    /// the computation takes too long.
    /// </summary>
    /// <param name="db">The request's context, used to read the project's settings and the freshness marker.</param>
    /// <param name="requestAborted">Stops this caller's wait, never the computation: others may be waiting for it.</param>
    public async Task<PerformanceDashboardResult> GetAsync(
        TamizaDbContext db, Guid projectId, DateOnly? from, DateOnly? to, CancellationToken requestAborted)
    {
        var inputs = await LoadAsync(db, projectId, from, to, requestAborted);
        var key = KeyOf(inputs);
        if (TryGetFresh(key, out var cached))
        {
            return cached;
        }

        Lazy<Task<PerformanceDashboardResult>>? created = null;
        created = new Lazy<Task<PerformanceDashboardResult>>(() => Task.Run(() => ComputeAsync(key, inputs, created!)));
        var shared = _inFlight.GetOrAdd(key, created);
        return await shared.Value.WaitAsync(requestAborted);
    }

    private static async Task<PerformanceInputs> LoadAsync(TamizaDbContext db, Guid projectId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.TimeZone, p.FormFields, p.FormChoices, p.EnumeratorField, p.FormCheckedAt, p.LastWebhookAt })
            .SingleAsync(ct);
        var frame = await db.SamplingFrames.AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .Select(f => new FrameInput(f.Dimensions, f.Targets, f.UpdatedAt))
            .SingleOrDefaultAsync(ct);
        var lastSync = await db.Syncs.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.FinishedAt != null)
            .MaxAsync(s => s.FinishedAt, ct);
        return new PerformanceInputs(
            projectId, from, to, project.TimeZone, project.FormFields, project.FormChoices, project.EnumeratorField, project.FormCheckedAt,
            frame, project.LastWebhookAt, lastSync);
    }

    private static CacheKey KeyOf(PerformanceInputs i) =>
        new(i.ProjectId, i.From, i.To, i.TimeZone, i.FormCheckedAt, i.EnumeratorField, i.Frame?.UpdatedAt, i.LastWebhookAt, i.LastSyncFinishedAt);

    private bool TryGetFresh(CacheKey key, out PerformanceDashboardResult result)
    {
        // The expiry is absolute, from the start of the statement, and is read against the injected clock.
        if (_cache.TryGetValue(key, out PerformanceDashboardResult? cached) && cached is not null
            && _time.GetUtcNow() < cached.ComputedAt + Freshness)
        {
            result = cached;
            return true;
        }

        result = null!;
        return false;
    }

    private async Task<PerformanceDashboardResult> ComputeAsync(CacheKey key, PerformanceInputs inputs, Lazy<Task<PerformanceDashboardResult>> self)
    {
        try
        {
            // A computation that finished between the caller's look at the cache and the registration of this one.
            if (TryGetFresh(key, out var cached))
            {
                return cached;
            }

            if (!await _slots.WaitAsync(_options.QueueTimeout))
            {
                throw new DashboardTimeoutException();
            }

            try
            {
                await _probe.ComputationStartedAsync(inputs.ProjectId);
                var result = PerformanceResults.Build(inputs, await RunStatementAsync(inputs));
                _cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
                return result;
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (Exception ex) when (ex is not DashboardTimeoutException)
        {
            // Nobody may be awaiting it any more, and a failure is not cached.
            _logger.LogError(ex, "The performance dashboard of project {ProjectId} failed.", inputs.ProjectId);
            throw;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<CacheKey, Lazy<Task<PerformanceDashboardResult>>>(key, self));
        }
    }

    /// <summary>
    /// Runs the statement in its own scope and connection, in a read-only transaction whose time limit and time zone are set
    /// with <c>set_config</c> (<c>SET</c> takes no parameters). It is bound only by <c>statement_timeout</c>.
    /// </summary>
    private async Task<RawFigures> RunStatementAsync(PerformanceInputs inputs)
    {
        var plan = PerformancePlan.Create(inputs.Fields, inputs.EnumeratorField, inputs.Frame?.Dimensions);
        var dimensions = plan.Dimensions.Count;
        var sleep = _options.TestStatementSleep > TimeSpan.Zero;
        var sql = PerformanceSql.Build(dimensions, inputs.From is not null, inputs.To is not null, sleep);

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(CancellationToken.None);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
            await using (var setup = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await setup.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using (var settings = new NpgsqlCommand(
                "SELECT set_config('statement_timeout', @ms, true), set_config('TimeZone', 'UTC', true)", connection, transaction))
            {
                settings.Parameters.Add(Text("ms", ((long)_options.StatementTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)));
                await settings.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction)
            {
                // PostgreSQL's limit must be the one that ends a long statement, not the client's.
                CommandTimeout = (int)Math.Ceiling(_options.StatementTimeout.TotalSeconds) + 30,
            };
            command.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.Uuid) { Value = inputs.ProjectId });
            command.Parameters.Add(Text("tz", inputs.TimeZone));
            command.Parameters.Add(Text("k_start", plan.StartKey));
            command.Parameters.Add(Text("k_end", plan.EndKey));
            command.Parameters.Add(Text("k_enum", plan.EnumeratorKey));
            for (var i = 0; i < dimensions; i++)
            {
                command.Parameters.Add(Text($"k{i}", plan.Dimensions[i].Field));
            }

            if (inputs.From is { } from)
            {
                command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
            }

            if (inputs.To is { } to)
            {
                command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
            }

            if (dimensions > 0)
            {
                var rows = inputs.Frame!.Targets.Select(t => new { values = t.Values, target = t.Target });
                command.Parameters.Add(new NpgsqlParameter("frame", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(rows, Json) });
            }

            if (sleep)
            {
                command.Parameters.Add(new NpgsqlParameter("sleep", NpgsqlDbType.Double) { Value = _options.TestStatementSleep.TotalSeconds });
            }

            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            await reader.ReadAsync(CancellationToken.None);
            return Read(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            throw new DashboardTimeoutException(ex);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static RawFigures Read(NpgsqlDataReader reader)
    {
        double? Float(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

        using var days = JsonDocument.Parse(reader.GetString(3));
        using var enumerators = JsonDocument.Parse(reader.GetString(11));
        using var achieved = JsonDocument.Parse(reader.GetString(12));
        using var outsideTop = JsonDocument.Parse(reader.GetString(14));
        return new RawFigures(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            days.RootElement.EnumerateArray()
                .Select(d => new DayCount(DateOnly.ParseExact(d.GetProperty("day").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), d.GetProperty("n").GetInt64()))
                .ToList(),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            Float(7),
            Float(8),
            Float(9),
            Float(10),
            enumerators.RootElement.EnumerateArray().Select(e => new RawEnumerator(
                    e.GetProperty("name").ValueKind == JsonValueKind.Null ? null : e.GetProperty("name").GetString(),
                    e.GetProperty("n").GetInt64(),
                    e.GetProperty("mean").ValueKind == JsonValueKind.Null ? null : e.GetProperty("mean").GetDouble(),
                    e.GetProperty("outliers").GetInt64(),
                    e.GetProperty("last").ValueKind == JsonValueKind.Null ? null : DateTimeOffset.Parse(e.GetProperty("last").GetString()!, CultureInfo.InvariantCulture)))
                .ToList(),
            achieved.RootElement.EnumerateArray().Select(a => a.GetInt64()).ToList(),
            reader.GetInt64(13),
            outsideTop.RootElement.EnumerateArray().Select(o => new RawOutside(
                    o.GetProperty("values").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.GetString()).ToList(),
                    o.GetProperty("n").GetInt64()))
                .ToList());
    }

    public void Dispose() => _cache.Dispose();
}
