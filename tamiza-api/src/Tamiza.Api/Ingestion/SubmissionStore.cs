using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tamiza.Api.Data;

namespace Tamiza.Api.Ingestion;

public readonly record struct UpsertCounts(int Created, int Updated);

/// <summary>
/// Writes raw submissions with set-based SQL: one statement per webhook or page, and one to mark deletions. Runs in the
/// DbContext's current transaction when there is one.
/// </summary>
public sealed class SubmissionStore(TamizaDbContext db)
{
    // Rows whose JSON is unchanged (jsonb equality ignores key order and whitespace) are skipped, so they count as
    // neither created nor updated; xmax = 0 tells an inserted row from an updated one.
    private const string UpsertSql = """
        INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, submitted_at, data, origin, received_at, updated_at)
        SELECT @project_id, r.kobo_id, r.kobo_uuid, r.form_version, r.submitted_at, r.data::jsonb, @origin, @now, @now
        FROM unnest(@kobo_ids, @kobo_uuids, @form_versions, @submitted_ats, @datas) AS r(kobo_id, kobo_uuid, form_version, submitted_at, data)
        ON CONFLICT (project_id, kobo_id) DO UPDATE
        SET data = excluded.data, kobo_uuid = excluded.kobo_uuid, form_version = excluded.form_version,
            submitted_at = excluded.submitted_at, updated_at = excluded.updated_at, deleted = false, deleted_at = NULL
        WHERE raw_submissions.data IS DISTINCT FROM excluded.data OR raw_submissions.deleted
        RETURNING (xmax = 0) AS inserted
        """;

    // A version nobody has seen yet starts as pending and due; its definition is fetched later, never from this statement.
    // ORDER BY inserts in the order of the sorted uids, so concurrent transactions take their row locks in the same order.
    private const string RecordVersionsSql = """
        INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at)
        SELECT @project_id, v.uid, 'pending', @now, @now
        FROM unnest(@uids) WITH ORDINALITY AS v(uid, position)
        ORDER BY v.position
        ON CONFLICT DO NOTHING
        """;

    // Rows received after the listing started may have an _id the listing already passed, so they are never marked.
    private const string MarkDeletedSql = """
        UPDATE tamiza.raw_submissions s SET deleted = true, deleted_at = @now
        WHERE s.project_id = @project_id AND NOT s.deleted AND s.received_at < @listed_from
          AND NOT EXISTS (SELECT 1 FROM kobo_ids k WHERE k.kobo_id = s.kobo_id)
        """;

    /// <summary>Inserts new submissions and replaces changed ones. A submission marked as deleted is restored.</summary>
    public async Task<UpsertCounts> UpsertAsync(
        Guid projectId, IReadOnlyCollection<SubmissionRow> rows, SubmissionOrigin origin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return default;
        }

        // ON CONFLICT cannot touch the same row twice in one statement; the last copy of an _id wins.
        var unique = rows.GroupBy(r => r.KoboId).Select(g => g.Last()).ToArray();

        await using var scope = await OpenAsync(cancellationToken);
        await using var command = scope.Command(UpsertSql);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("origin", origin.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("kobo_ids", unique.Select(r => r.KoboId).ToArray());
        command.Parameters.AddWithValue("kobo_uuids", unique.Select(r => r.KoboUuid).ToArray());
        command.Parameters.AddWithValue("form_versions", unique.Select(r => r.FormVersion).ToArray());
        command.Parameters.AddWithValue("submitted_ats", unique.Select(r => r.SubmittedAt).ToArray());
        command.Parameters.AddWithValue("datas", unique.Select(r => r.Data).ToArray());

        int created = 0, updated = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetBoolean(0))
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
        }

        await RecordVersionsAsync(scope, projectId, unique, now, cancellationToken);
        return new UpsertCounts(created, updated);
    }

    /// <summary>
    /// Records the form versions of the stored rows in the same transaction, so a version is never in use without a row
    /// to fetch it from. It is one primary-key probe per version and contacts nobody, so the webhook keeps its response time.
    /// A submission without a version records nothing.
    /// </summary>
    private static async Task RecordVersionsAsync(ConnectionScope scope, Guid projectId, SubmissionRow[] rows, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var uids = rows.Select(r => r.FormVersion).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (uids.Length == 0)
        {
            return;
        }

        await using var command = scope.Command(RecordVersionsSql);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("uids", uids);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Marks as deleted the project's submissions whose <c>_id</c> is not in <paramref name="koboIds"/>, the complete
    /// list read from Kobo starting at <paramref name="listedFrom"/>. Returns how many were marked.
    /// </summary>
    public async Task<int> MarkDeletedAsync(
        Guid projectId, IReadOnlyCollection<long> koboIds, DateTimeOffset listedFrom, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            await using var scope = await OpenAsync(cancellationToken);
            await using (var create = scope.Command("CREATE TEMPORARY TABLE kobo_ids (kobo_id bigint NOT NULL) ON COMMIT DROP"))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var import = await scope.Connection.BeginBinaryImportAsync("COPY kobo_ids (kobo_id) FROM STDIN (FORMAT BINARY)", cancellationToken))
            {
                foreach (var id in koboIds)
                {
                    await import.WriteRowAsync(cancellationToken, id);
                }

                await import.CompleteAsync(cancellationToken);
            }

            await using (var analyze = scope.Command("ANALYZE kobo_ids"))
            {
                await analyze.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var mark = scope.Command(MarkDeletedSql);
            mark.Parameters.AddWithValue("project_id", projectId);
            mark.Parameters.AddWithValue("listed_from", listedFrom);
            mark.Parameters.AddWithValue("now", now);
            var marked = await mark.ExecuteNonQueryAsync(cancellationToken);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }

            return marked;
        }
        finally
        {
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }
    }

    private async Task<ConnectionScope> OpenAsync(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return new ConnectionScope(db);
    }

    /// <summary>The DbContext's open connection and current transaction; closing balances <c>OpenConnectionAsync</c>.</summary>
    private sealed class ConnectionScope(TamizaDbContext db) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = (NpgsqlConnection)db.Database.GetDbConnection();

        public NpgsqlCommand Command(string sql) =>
            new(sql, Connection, (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());

        public ValueTask DisposeAsync() => new(db.Database.CloseConnectionAsync());
    }
}
