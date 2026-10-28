using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;

namespace Tamiza.Api.Reports;

/// <summary>
/// The <c>dashboard_reports</c> table as the scheduler's queue (design D4, D5). There is no list of upcoming sends: a report is due when,
/// in its project's time zone, today is one of its weekdays, its time has passed, the scheduler has not taken today yet, and no save
/// made after today's time says otherwise. Each claim takes one report in one statement that repeats that test, so a save, a pause or
/// a time change made meanwhile wins, and a day taken is never taken twice.
/// </summary>
public sealed class ReportQueue(TamizaDbContext db)
{
    /// <summary>
    /// Takes the oldest pending "send now" request, without touching the schedule. Null when there is none.
    /// </summary>
    public async Task<Guid?> ClaimSendNowAsync(CancellationToken ct)
    {
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE tamiza.dashboard_reports SET send_now_pending = false
            WHERE dashboard_id = (
                SELECT dashboard_id FROM tamiza.dashboard_reports
                WHERE send_now_pending AND removed_at IS NULL
                ORDER BY send_now_requested_at, dashboard_id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING dashboard_id AS "Value"
            """).ToListAsync(ct);
        return claimed.Count == 0 ? null : claimed[0];
    }

    /// <summary>Takes one report due at <paramref name="now"/>, earliest time first, marking its day as taken. Null when none is due.</summary>
    public async Task<Guid?> ClaimDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE tamiza.dashboard_reports r SET last_scheduled_at = {now}
            FROM tamiza.dashboards d JOIN tamiza.projects p ON p.id = d.project_id
            WHERE d.id = r.dashboard_id
              AND r.dashboard_id = (
                  SELECT r2.dashboard_id
                  FROM tamiza.dashboard_reports r2
                  JOIN tamiza.dashboards d2 ON d2.id = r2.dashboard_id
                  JOIN tamiza.projects p2 ON p2.id = d2.project_id
                  WHERE r2.active AND r2.removed_at IS NULL
                    AND extract(isodow FROM ({now} AT TIME ZONE p2.time_zone))::smallint = ANY (r2.days)
                    AND ({now} AT TIME ZONE p2.time_zone)::time >= r2.send_time
                    AND (r2.last_scheduled_at IS NULL OR (r2.last_scheduled_at AT TIME ZONE p2.time_zone)::date < ({now} AT TIME ZONE p2.time_zone)::date)
                    AND (r2.not_before IS NULL OR (r2.not_before AT TIME ZONE p2.time_zone)::date < ({now} AT TIME ZONE p2.time_zone)::date)
                  ORDER BY r2.send_time, r2.dashboard_id
                  LIMIT 1
                  FOR UPDATE OF r2 SKIP LOCKED)
              AND r.active AND r.removed_at IS NULL
              AND extract(isodow FROM ({now} AT TIME ZONE p.time_zone))::smallint = ANY (r.days)
              AND ({now} AT TIME ZONE p.time_zone)::time >= r.send_time
              AND (r.last_scheduled_at IS NULL OR (r.last_scheduled_at AT TIME ZONE p.time_zone)::date < ({now} AT TIME ZONE p.time_zone)::date)
              AND (r.not_before IS NULL OR (r.not_before AT TIME ZONE p.time_zone)::date < ({now} AT TIME ZONE p.time_zone)::date)
            RETURNING r.dashboard_id AS "Value"
            """).ToListAsync(ct);
        return claimed.Count == 0 ? null : claimed[0];
    }
}
