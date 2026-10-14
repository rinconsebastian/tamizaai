using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Ingestion;

/// <summary>Which versions a fetch pass takes: the ones that are due, or every one that is not available.</summary>
public enum FetchMode
{
    /// <summary>Pending versions, and failed ones whose next attempt has come.</summary>
    Due,

    /// <summary>Every version whose definition is not available, whatever its next attempt.</summary>
    All,
}

/// <summary>
/// Reads and writes <c>form_versions</c> with set-based SQL. Every write that changes a definition's status is guarded by
/// <c>status &lt;&gt; 'available'</c>, so two passes over the same version cannot undo each other, and a fetched definition
/// (which Kobo never changes) is never overwritten.
/// </summary>
public sealed class FormVersionStore(TamizaDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The delay after the <paramref name="attempts"/>-th failure in a row: one hour, doubling, up to a day.</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromHours(Math.Min(Math.Pow(2, Math.Clamp(attempts - 1, 0, 5)), 24));

    /// <summary>
    /// Records the versions of the project's stored submissions that have no row yet. This covers submissions stored by an
    /// older image after a rollback, or by any path that skipped the recording.
    /// </summary>
    public Task RecordUsedVersionsAsync(Guid projectId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at)
            SELECT DISTINCT @project_id, form_version, 'pending', @now, @now
            FROM tamiza.raw_submissions
            WHERE project_id = @project_id AND NOT deleted AND form_version IS NOT NULL
            ORDER BY form_version
            ON CONFLICT DO NOTHING
            """,
            [new NpgsqlParameter("project_id", projectId), new NpgsqlParameter("now", now)],
            cancellationToken);

    /// <summary>
    /// How many versions used by the project's non-deleted submissions have no definition: failed, not found or pending,
    /// counting a version without a row as pending. The distinct versions are found by stepping through the
    /// <c>(project_id, form_version)</c> index one version at a time, so the cost follows the number of versions, not of
    /// submissions.
    /// </summary>
    public async Task<int> CountUnavailableAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.Database.SqlQueryRaw<int>(
                """
                WITH RECURSIVE used(uid) AS (
                    (SELECT form_version FROM tamiza.raw_submissions
                     WHERE project_id = @project_id AND NOT deleted AND form_version IS NOT NULL
                     ORDER BY form_version LIMIT 1)
                    UNION ALL
                    SELECT (SELECT form_version FROM tamiza.raw_submissions
                            WHERE project_id = @project_id AND NOT deleted AND form_version > used.uid
                            ORDER BY form_version LIMIT 1)
                    FROM used WHERE used.uid IS NOT NULL
                )
                SELECT count(*)::int AS "Value"
                FROM used u
                LEFT JOIN tamiza.form_versions v ON v.project_id = @project_id AND v.uid = u.uid
                WHERE u.uid IS NOT NULL AND (v.uid IS NULL OR v.status <> 'available')
                """,
                new NpgsqlParameter("project_id", projectId))
            .SingleAsync(cancellationToken);

    /// <summary>The uids of the project's versions a pass of <paramref name="mode"/> takes, in a stable order.</summary>
    public async Task<List<string>> SelectAsync(Guid projectId, FetchMode mode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var versions = db.FormVersions.AsNoTracking().Where(v => v.ProjectId == projectId);
        versions = mode == FetchMode.All
            ? versions.Where(v => v.Status != FormVersionStatus.Available)
            : versions.Where(v => v.Status == FormVersionStatus.Pending || (v.Status == FormVersionStatus.Failed && v.NextAttemptAt <= now));
        return await versions.OrderBy(v => v.RecordedAt).ThenBy(v => v.Uid).Select(v => v.Uid).ToListAsync(cancellationToken);
    }

    /// <summary>The projects that have versions due, for the background pass.</summary>
    public async Task<List<Guid>> ProjectsWithDueVersionsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.FormVersions.AsNoTracking()
            .Where(v => v.Status == FormVersionStatus.Pending || (v.Status == FormVersionStatus.Failed && v.NextAttemptAt <= now))
            .Select(v => v.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>A stored definition of the version, or null when it is not available.</summary>
    public Task<FormVersion?> FindAvailableAsync(Guid projectId, string uid, CancellationToken cancellationToken) =>
        db.FormVersions.AsNoTracking()
            .SingleOrDefaultAsync(v => v.ProjectId == projectId && v.Uid == uid && v.Status == FormVersionStatus.Available, cancellationToken);

    /// <summary>Stores a row built by a snapshot: a definition, or the reason it could not be read.</summary>
    public Task SaveAsync(FormVersion version, CancellationToken cancellationToken)
    {
        var now = version.RecordedAt;
        var error = new KoboError(version.ErrorCode ?? "", version.Error ?? "");
        return version.Status switch
        {
            FormVersionStatus.Available => SaveAvailableAsync(version.ProjectId, version.Uid, version.Fields, version.Choices, version.DeployedAt, now, cancellationToken),
            FormVersionStatus.NotFound => SaveNotFoundAsync(version.ProjectId, version.Uid, error, now, cancellationToken),
            FormVersionStatus.Failed => SaveFailedAsync(version.ProjectId, version.Uid, error, now, cancellationToken),
            _ => throw new ArgumentException("A snapshot stores a definition or a failure, not a pending version.", nameof(version)),
        };
    }

    /// <summary>Keeps the fetched definition. The deployment date of an already known version is not lost.</summary>
    public Task SaveAvailableAsync(
        Guid projectId, string uid, IReadOnlyList<FormField> fields, IReadOnlyList<FormChoice> choices, DateTimeOffset? deployedAt, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, fields, choices, deployed_at, recorded_at, fetched_at, attempts)
            VALUES (@project_id, @uid, 'available', @fields, @choices, @deployed_at, @now, @now, 0)
            ON CONFLICT (project_id, uid) DO UPDATE
            SET status = 'available', fields = excluded.fields, choices = excluded.choices,
                deployed_at = coalesce(excluded.deployed_at, form_versions.deployed_at), fetched_at = excluded.fetched_at,
                attempts = 0, next_attempt_at = NULL, error_code = NULL, error = NULL
            WHERE form_versions.status <> 'available'
            """,
            [
                new NpgsqlParameter("project_id", projectId),
                new NpgsqlParameter("uid", uid),
                new NpgsqlParameter("fields", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(fields, Json) },
                new NpgsqlParameter("choices", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(choices, Json) },
                new NpgsqlParameter("deployed_at", NpgsqlDbType.TimestampTz) { Value = (object?)deployedAt ?? DBNull.Value },
                new NpgsqlParameter("now", now),
            ],
            cancellationToken);

    /// <summary>
    /// Marks the version failed and schedules the next attempt: one hour after the first failure, then doubling up to a day.
    /// </summary>
    public Task SaveFailedAsync(Guid projectId, string uid, KoboError error, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, attempts, next_attempt_at, error_code, error)
            VALUES (@project_id, @uid, 'failed', @now, 1, @now + interval '1 hour', @error_code, @error)
            ON CONFLICT (project_id, uid) DO UPDATE
            SET status = 'failed', attempts = form_versions.attempts + 1,
                next_attempt_at = @now + least(interval '1 hour' * power(2, least(form_versions.attempts, 5)), interval '24 hours'),
                error_code = excluded.error_code, error = excluded.error
            WHERE form_versions.status <> 'available'
            """,
            [
                new NpgsqlParameter("project_id", projectId),
                new NpgsqlParameter("uid", uid),
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("error_code", error.Code),
                new NpgsqlParameter("error", error.Message),
            ],
            cancellationToken);

    /// <summary>Marks the version as unknown to Kobo; nothing retries it until someone re-checks the form or replaces the token.</summary>
    public Task SaveNotFoundAsync(Guid projectId, string uid, KoboError error, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, error_code, error)
            VALUES (@project_id, @uid, 'not_found', @now, @error_code, @error)
            ON CONFLICT (project_id, uid) DO UPDATE
            SET status = 'not_found', next_attempt_at = NULL, error_code = excluded.error_code, error = excluded.error
            WHERE form_versions.status <> 'available'
            """,
            [
                new NpgsqlParameter("project_id", projectId),
                new NpgsqlParameter("uid", uid),
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("error_code", error.Code),
                new NpgsqlParameter("error", error.Message),
            ],
            cancellationToken);

    /// <summary>Makes the given versions pending and due at once, for the background pass to take next.</summary>
    public Task MakeDueAsync(Guid projectId, IReadOnlyCollection<string> uids, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE tamiza.form_versions
            SET status = 'pending', next_attempt_at = @now
            WHERE project_id = @project_id AND uid = ANY(@uids) AND status <> 'available'
            """,
            [new NpgsqlParameter("project_id", projectId), new NpgsqlParameter("uids", uids.ToArray()), new NpgsqlParameter("now", now)],
            cancellationToken);

    /// <summary>
    /// A new token gives failed and not found versions a fresh start: they become pending and due at once, with their
    /// failure count cleared.
    /// </summary>
    public Task MakeUnavailableDueAsync(Guid projectId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            """
            UPDATE tamiza.form_versions
            SET status = 'pending', attempts = 0, next_attempt_at = @now, error_code = NULL, error = NULL
            WHERE project_id = @project_id AND status IN ('failed', 'not_found')
            """,
            [new NpgsqlParameter("project_id", projectId), new NpgsqlParameter("now", now)],
            cancellationToken);
}
