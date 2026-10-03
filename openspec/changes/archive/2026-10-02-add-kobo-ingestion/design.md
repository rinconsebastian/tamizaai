# Design

## Context

Builds on `add-project-management` (archived; main specs `projects`, `auth`, `web-ui`) and `adopt-dbup-migrations`. Relevant current state:

- **Webhook contract already published.** Admins see `{TAMIZA_PUBLIC_URL}/api/v1/webhooks/kobo/{projectId}` with HTTP Basic, username `tamiza` (`WebhookSecret.Username`) and the secret decrypted by `SecretProtector` (`Projects/ProjectSecrets.cs`). Admins may already have configured Kobo REST Services against it; the endpoint must match exactly.
- **Kobo client.** `KoboClient` (`Kobo/KoboClient.cs`) sends `Authorization: Token`, maps errors to `KoboErrorCodes`, and reads only through the named `HttpClient` "kobo" whose connect callback enforces the SSRF policy (`Kobo/KoboHttp.cs`). It buffers responses (10 MB cap, 20 s timeout), which suits form documents but not submission pages.
- **Form snapshot.** `ProjectEndpoints.ApplySnapshot` (private) writes `form_name`, `form_fields`, `form_checked_at` after a re-check.
- **Auth.** The fallback policy requires an authenticated user; endpoints opt out with `AllowAnonymous`. The JWT handler ignores non-Bearer `Authorization` headers, so a Basic header reaches an anonymous endpoint untouched. Project endpoints use `RequireProjectRole` (404 for non-members, 403 for lower roles).
- **Schema.** DbUp owns schema `tamiza` (`src/dbUp/Scripts/0001_initial_schema.sql`); `SchemaDriftTests` compares the scripts with the EF model. Integration tests run Testcontainers PostGIS and WireMock.Net (`KoboStub`).
- **Deployment.** One `api` replica in `compose.yaml`; the API is reached through the `ui` nginx under `/api/` (`client_max_body_size 20m`).
- **UI.** Project page with tabs `overview`, `members`, `sampling-frame`, `webhook` (admin). The webhook tab and `docs/kobo.md` say ingestion arrives "in a later release".

Requirements are in `specs/ingestion` and `specs/projects`; motivation in `proposal.md`.

## Goals / Non-Goals

**Goals:**

- A webhook that does one indexed write and answers well under 500 ms.
- Synchronization that handles 500,000 submissions per project with bounded memory, survives restarts and partial failures, and never marks submissions as deleted from an incomplete picture.
- A storage shape (`raw_submissions`) that `add-python-pipeline` and the performance dashboard read directly.

**Non-Goals:**

- Flattening, starting the script chain after a synchronization, and the "run after sync" setting (`add-python-pipeline` reads the `syncs` table to add it).
- Attachments: their URLs stay inside the JSON; files are not downloaded.
- Running several `api` replicas (D7 assumes one).
- Rate limiting the webhook. The secret has 256 bits, so guessing is not a concern; flooding is left to the reverse proxy.
- Audit events (`add-audit-log`).

## Decisions

### D1. Data model

DbUp script `0002_add_ingestion.sql`, schema `tamiza`:

| Table | Columns | Notes |
|---|---|---|
| `raw_submissions` | `id` (bigint identity), `project_id` (FK cascade), `kobo_id` (bigint), `kobo_uuid` (varchar 64), `form_version` (varchar 128, null), `submitted_at` (timestamptz, null), `data` (jsonb), `origin` (`webhook`\|`sync`, check), `received_at`, `updated_at`, `deleted` (bool, default false), `deleted_at` (null) | Unique `(project_id, kobo_id)`; index `(project_id, kobo_uuid)`. |
| `syncs` | `id` (uuid v7), `project_id` (FK cascade), `kind` (`incremental`\|`full`\|`reconciliation`), `trigger` (`schedule`\|`manual`\|`initial`), `status` (`queued`\|`running`\|`succeeded`\|`failed`), `requested_by` (FK users, null), `queued_at`, `started_at` (null), `finished_at` (null), `created_count`, `updated_count`, `deleted_count` (int, default 0), `error_code` (null), `error` (text, null) | Partial unique index `ix_syncs_project_id_active` on `(project_id) WHERE status IN ('queued','running')`; index `(project_id, queued_at DESC)`. |

`projects` gains `sync_interval_hours` (int, default 12, check 1–168), `reconcile_interval_hours` (int, default 24, check 1–168), `next_sync_at` (timestamptz, default `now()`), `next_reconcile_at` (timestamptz, default `now() + interval '1 day'`) and `last_webhook_at` (null). Every new column has a default, so existing projects get `next_sync_at = now()` and backfill on the first scheduler tick after the upgrade, and a rolled-back image can still insert projects.

- `id` is a bigint identity rather than a uuid: it is internal, the table is the largest in the schema, and bulk inserts need no client-side keys.
- `raw_submissions` lives in schema `tamiza`, not in `p_<id>_raw`: its shape is fixed and the API owns it. `p_<id>_raw` holds the flattened, form-dependent tables that `add-python-pipeline` creates.
- `submitted_at` is parsed from `_submission_time` (Kobo sends UTC without an offset). `received_at` and `origin` are set on insert only (spec "Record origin").

*Alternative:* store the next due times implicitly (`last start + interval`). Rejected: the scheduler's due query and the retry-after-failure rule (D7) are simpler with explicit columns.

### D2. Submission identity: `_id`, not `_uuid`

The brief keys submissions on `_uuid`. When a submission is edited in Kobo, Kobo updates the same record: `_id` stays, `_uuid` takes the new `meta/instanceID`. Keying on `_uuid` would turn every edit into a second row. The upsert key is therefore `(project_id, kobo_id)`. `kobo_uuid` is stored and indexed for lookups; a resend of the same `_uuid` has the same `_id`, so the brief's duplicate scenario still holds.

*Alternatives:* `meta/rootUuid` (stable across edits, but only present in recent Kobo versions); `_uuid` with `meta/deprecatedID` chaining (more state, and fails when an intermediate version is missed).

### D3. Upsert and counting

One SQL statement serves the webhook (one row) and synchronization (one page), sent with Npgsql array parameters:

```sql
INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, submitted_at, data, origin, received_at, updated_at)
SELECT @project_id, r.kobo_id, r.kobo_uuid, r.form_version, r.submitted_at, r.data, @origin, @now, @now
FROM unnest(@kobo_ids, @kobo_uuids, @form_versions, @submitted_ats, @datas::jsonb[]) AS r(kobo_id, kobo_uuid, form_version, submitted_at, data)
ON CONFLICT (project_id, kobo_id) DO UPDATE
SET data = excluded.data, kobo_uuid = excluded.kobo_uuid, form_version = excluded.form_version,
    submitted_at = excluded.submitted_at, updated_at = excluded.updated_at, deleted = false, deleted_at = NULL
WHERE raw_submissions.data IS DISTINCT FROM excluded.data OR raw_submissions.deleted
RETURNING (xmax = 0) AS inserted;
```

Returned rows with `inserted` count as created, the others as updated; rows skipped by the `WHERE` are unchanged and not counted (spec "Synchronization log"). `jsonb` equality ignores key order and whitespace. Duplicate `_id`s inside one page are removed before the statement (last one wins), since `ON CONFLICT` cannot touch a row twice. EF Core stays the access layer for the other tables; this statement and the deletion marking (D6) use raw SQL through the DbContext's connection.

### D4. Webhook endpoint

`POST /api/v1/webhooks/kobo/{projectId:guid}`, `AllowAnonymous`, in a new `Ingestion/WebhookEndpoints.cs`. Steps, in order:

1. Parse `Authorization: Basic`. Read the project's `encrypted_webhook_secret` (single-column query). Missing header, wrong username, unknown project, undecryptable secret or wrong secret all give the same 401 Problem Details with `WWW-Authenticate: Basic realm="tamiza"`. The secret is compared with `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes.
2. Require a JSON content type (`application/json` or `+json`), else 415 with code `webhook.unsupported_media_type`.
3. Limit the body to 10 MB (`IHttpMaxRequestBodySizeFeature`, set after authentication), else 413.
4. Parse with `JsonDocument`. Not an object, or `_id` not an integer, or `_uuid` not a non-empty string → 400 with code `webhook.invalid_submission` and a message naming the field.
5. Run the upsert (D3) with origin `webhook`, and set `projects.last_webhook_at` in the same transaction.
6. Respond 200 with an empty body.

Nothing is queued and no synchronization is triggered (brief: "never on every webhook"). The request does one primary-key-sized read, one Data Protection decrypt (microseconds) and one upsert; there is no cache to invalidate when the secret is regenerated.

Malformed deliveries are logged at Information with the project ID and the reason, never with the body or the `Authorization` header. A body over the limit gets 413 with code `webhook.too_large`.

NUL characters (`\u0000`) are removed from strings before storing, here and in synchronization: `jsonb` rejects them, and one such record would otherwise fail every page that contains it.

### D5. Reading submissions from Kobo

`KoboClient` gains `ReadSubmissionsAsync(server, uid, token, query, fields, pageSize, ct)`, an `IAsyncEnumerable` of pages:

- `GET {server}/api/v2/assets/{uid}/data/?format=json&limit={pageSize}&sort={"_id":1}&query={...}[&fields=["_id"]]`.
- **Keyset paging:** each page adds `"_id": {"$gt": <last _id>}` to the query, instead of `start` offsets, so submissions arriving or being deleted mid-read neither shift nor skip pages. Paging stops at a page with fewer than `pageSize` results.
- **Incremental filter:** `{"_submission_time": {"$gte": "<cutoff, UTC, yyyy-MM-ddTHH:mm:ss>"}}`.
- **Streaming:** requests use `HttpCompletionOption.ResponseHeadersRead`; the body is read through a length-limited stream (50 MB per page) and parsed with `JsonDocument.ParseAsync`. Each result becomes `(kobo_id, kobo_uuid, form_version, submitted_at, raw JSON text)`. Pages go through a second named client, `kobo-data`, with the same SSRF-safe handler but no overall `HttpClient.Timeout`; a per-page timeout (2 minutes) bounds headers and body together instead.
- Page size 500 by default; with Kobo's form-sized records this keeps a page well under the cap.
- Same SSRF-protected client and error mapping as today. Records without a valid `_id` or `_uuid` fail the run with `kobo.unexpected_response` rather than being skipped silently.

### D6. Synchronization kinds

| Kind | Reads | Then |
|---|---|---|
| `incremental` | Submissions with `_submission_time >= cutoff`, where cutoff = `started_at` of the last succeeded `incremental` or `full` sync minus the safety margin (1 h). Without one, there is no filter and the run is recorded as `full`. | Upserts each page (origin `sync`). |
| `full` | Every submission. | Upserts each page, collecting the `_id`s seen; then marks deletions as below. |
| `reconciliation` | Only `_id`s (`fields=["_id"]`, page size 5,000). | Marks deletions. |

Every kind first re-reads the asset (`GetAssetAsync`) and applies the form snapshot (spec `projects`, "Form field check"); `ApplySnapshot` moves to a shared `FormSnapshot.Apply(project, asset, now)` used by both the project endpoints and synchronization. The cutoff uses the previous run's start, not its end, so submissions that arrived during that run are read again.

**Deletion marking** runs only after every page was read without error. The collected IDs are copied (binary `COPY`) into a temporary table, then one statement marks the rest:

```sql
UPDATE tamiza.raw_submissions s SET deleted = true, deleted_at = @now
WHERE s.project_id = @project_id AND NOT s.deleted AND s.received_at < @run_started_at
  AND NOT EXISTS (SELECT 1 FROM kobo_ids k WHERE k.kobo_id = s.kobo_id);
```

The `received_at < run start` guard keeps submissions that arrived by webhook after the listing passed their `_id` from being marked. Zero IDs from a complete listing marks everything (spec "Form emptied in Kobo"). A full sync also counts as a reconciliation for scheduling (D7).

**Progress and partial failure.** Each page is upserted in its own transaction and the run's counters are updated with it, so stored pages survive a later failure and the UI can show progress. A failed run never becomes the cutoff for the next one.

### D7. Scheduler and queue

A hosted `BackgroundService`, `IngestionScheduler`, runs in `api`. There is no queue container (brief); the `syncs` table is the queue.

- **Tick** every 30 s, or at once when `IngestionSignal` (a singleton wrapping a `Channel`) is poked by a manual request or a project creation.
- **Enqueue due work:** for each project with `next_sync_at <= now`, insert a queued `incremental` sync (trigger `initial` when the project has no succeeded sync, else `schedule`) with `ON CONFLICT DO NOTHING` on the active index, so a due run waits while another is active (spec "One synchronization at a time"). Same for `next_reconcile_at` and `reconciliation`.
- **Claim:** `UPDATE syncs SET status='running', started_at=now WHERE id = (SELECT id FROM syncs WHERE status='queued' ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING ...`, up to 2 concurrent runs across projects (bounds memory and load on Kobo servers).
- **Due times** are set when a run starts: `next_sync_at = started_at + interval` for `incremental`/`full`, `next_reconcile_at = started_at + interval` for `reconciliation` and `full`. When a scheduled run fails, `next_* = finished_at + min(1 h, interval)`. Changing an interval recomputes `next_* = last start of that kind + new interval` (or now if there is none).
- **Restart:** on startup, before the first tick, every `running` sync becomes `failed` with code `sync.interrupted`. Queued ones stay queued. This relies on a single `api` replica (Non-Goals); with several, a lease column would replace it.
- **Project creation** sets `next_sync_at = created_at` and pokes the signal, so the backfill starts within seconds (spec "Backfill on project creation"). It runs as `full` with trigger `initial`.

Settings live in an `IngestionOptions` class bound from the `Ingestion` configuration section (tick, concurrency, page sizes, page byte cap, page timeout, safety margin, retry delay, webhook body limit), with the defaults above. They are not exposed in `.env.example`; `docs/operations.md` documents the useful ones. `SchedulerEnabled` (default `true`) lets the integration-test factory keep the scheduler off, so seeded projects never call Kobo; ingestion tests turn it on with a 200 ms tick.

Under `WebApplicationFactory`, hosted services can start before `Program` finishes migrating, so the scheduler first awaits `MigrationStatus.WhenCompleted`.

*Alternatives:* Quartz.NET or Hangfire (a dependency and extra tables for one periodic job); running synchronization in `analytics` (the brief assigns Kobo synchronization to `api`, which owns the token and its decryption keys).

### D8. API surface

| Method and path | Minimum role | Purpose |
|---|---|---|
| `POST /webhooks/kobo/{projectId}` | Basic (D4) | Webhook intake. |
| `GET /projects/{id}/ingestion` | viewer | `{ submissionCount, deletedCount, lastWebhookAt, lastSync, activeSync, nextSyncAt, nextReconcileAt, syncIntervalHours, reconcileIntervalHours }`. |
| `PATCH /projects/{id}/ingestion` | admin | `{ syncIntervalHours?, reconcileIntervalHours? }`, 400 outside 1–168. |
| `POST /projects/{id}/syncs` | analyst | `{ kind: "incremental" \| "full" }` → 202 with the queued sync; 409 `sync.in_progress` when the active index rejects it. |
| `GET /projects/{id}/syncs?page=&pageSize=` | viewer | `{ items, total }`, newest first, page size ≤ 100 (default 20). |

Sync items carry `id, kind, trigger, status, requestedBy { id, name } | null, queuedAt, startedAt, finishedAt, createdCount, updatedCount, deletedCount, errorCode, error`. New error codes: `sync.in_progress`, `sync.interrupted`, `sync.failed` (unexpected error, details in the API log), `webhook.unsupported_media_type`, `webhook.invalid_submission`, `webhook.too_large`; synchronization failures reuse `kobo.*` and `kobo.token_unreadable`.

### D9. UI

- **Data tab** (`projects/:id/data`, every member), placed after Overview:
  - Status card: active submissions, deleted, last webhook ("none received yet"), last sync with status badge, next sync and reconciliation.
  - Actions (analyst+): "Sync now" and "Full resync" (the latter behind a confirmation dialog explaining it re-reads every submission). Both disable while a sync is active; `sync.in_progress` shows a toast.
  - Schedule form (admin): two number inputs in hours with inline errors.
  - Log: table in an `overflow-x-auto` wrapper at `md`+, stacked cards below; pagination; status badges; the error message on failed rows.
  - While a sync is queued or running, the page polls `GET ingestion` every 5 s and refreshes the first log page when it ends; polling stops when the component is destroyed.
- **Webhook tab:** drop the `ingestionNote`; add the step "choose **JSON** as the type, leave field subset and custom wrapper empty"; show the last webhook time.
- `en.json` keys for the new texts and error codes; services in `core/projects/ingestion.service.ts`.

### D10. Test strategy

- **API unit tests:** parsing a Kobo result into the upsert row (`_id` as number, missing `__version__`, `_submission_time` without offset), Basic header parsing, query building (cutoff formatting, keyset clause), due-time arithmetic.
- **API integration tests** (Testcontainers + WireMock; `KoboStub` answers the data endpoint with a callback that applies `limit`, the `query` filters and `fields` to an in-memory list, and can inject failures and delays; the scheduler runs on real time with a 200 ms tick, and due times are asserted on the stored timestamps or forced by updating them, so no fake clock is needed):
  - Webhook: valid, wrong/missing secret, unknown project, regenerated secret, wrong username, XML 415, missing `_id` 400, 413, duplicate, edit with new `_uuid`, deleted record restored, origin unchanged after a later webhook, `last_webhook_at` set; no secret or body in captured logs.
  - Synchronization: initial backfill across 3 pages, incremental cutoff sent to Kobo, lost-webhook record stored with origin `sync`, created/updated/unchanged counts, failure on page 4 keeps pages 1–3 and does not move the cutoff, token rejected, token unreadable, retry due time, form snapshot refreshed.
  - Reconciliation and full sync: deletion marked, incomplete listing marks nothing, record received after the run started is kept, empty form marks all, full sync updates an edited record.
  - Concurrency: second manual request gets 409; a `running` row becomes `sync.interrupted` on startup; due scheduled run waits for the active one.
  - Endpoints: roles for each endpoint, interval validation, log paging.
  - `SchemaDriftTests` covers the new script.
- **UI unit tests:** the data tab per role, polling start/stop, confirmation for full resync, `sync.in_progress` message, schedule form errors, the webhook tab's new step.
- **Stack verification:** a real Kobo form (or a WireMock container on the stack network with private networks enabled): configure the REST Service, submit from Enketo, see the record within seconds; delete one in Kobo and reconcile; measure webhook latency through nginx (p95 < 500 ms over 100 requests); backfill a 500,000-row stub and record duration and peak `api` memory against the 512 MB limit.

## Risks / Trade-offs

- [A synchronization page fetched before an edit is written after the webhook stored the edit, reverting it] → narrow window; the next full resync or the incremental margin fixes rows submitted recently. Documented.
- [Edits made in Kobo without a webhook delivery are missed by incremental sync, since `_submission_time` does not change on edit] → "Full resync" re-reads everything; documented in `docs/kobo.md`.
- [Kobo's `query` with `$gt` on `_id` behaves differently on some server versions] → verified against kf.kobotoolbox.org during stack verification; if it fails, fall back to `start` offsets with `sort={"_id":1}`, keeping the same client interface.
- [Large forms make 500-record pages exceed 50 MB] → the page fails with a clear `kobo.unexpected_response`; page size and cap are options an operator can lower or raise in `appsettings`.
- [Memory on full sync / reconciliation of 500,000 records] → pages are streamed and stored one at a time; the IDs are collected in memory (a `long` each, about 4 MB for 500,000) and copied into the temporary table once the listing is complete.
- [Deletion marking from wrong data] → only after a complete listing, never for records received after the run started, and soft deletes are restored when the record shows up again.
- [`raw_submissions` grows large (JSONB, many projects)] → sized for the brief's reference volume; `add-backup-restore` covers dumps. Project deletion (not yet offered) would cascade.
- [Startup cleanup assumes one replica] → stated in Non-Goals; Compose runs one.

## Verification Results

Measured on the development stack (Compose, 512 MiB `api` limit) against a fake Kobo server on the stack network that implements the same paging, filter and `fields` parameters; no real Kobo token was available, so Kobo's REST Service was simulated with HTTP requests following the same contract.

- **Webhook latency through the `ui` nginx:** 100 deliveries of about 1 KB, all 200; p50 7.5 ms, p95 10.5 ms, max 74 ms.
- **Back-fill of 500,000 submissions** (about 1.2 KB each, 1,000 pages of 500): 44 s, 499,998 stored (2 deleted in the fake), `raw_submissions` 630 MB; `api` peak 132 MiB.
- **Reconciliation of 500,000 IDs** (100 pages of 5,000): 6 s, 3 deletions found; `api` peak 95 MiB.
- **Flows:** initial back-fill on creation, webhook 200/401/415, a submission missed by the webhook stored by "Sync now" with origin `sync`, 409 for a second request, a scheduled reconciliation marking deletions; Data and Webhook sections without horizontal scroll at 360 and 1280 px.

The page size and byte cap defaults stay as designed. Not verified: `$gt` paging and the `fields` parameter against a real kf.kobotoolbox.org server (risk above).

## Migration Plan

- `0002_add_ingestion.sql` creates the two tables, adds the project columns and sets the first due times; applied on API startup. Existing projects backfill on the first tick after upgrading.
- Admins who already configured Kobo's REST Service start receiving submissions with no change on their side, provided the service type is JSON (Kobo's default). Deliveries Kobo attempted before the upgrade failed and are recovered by the initial synchronization.
- Rollback: deploy the previous image. Its DbUp run ignores the unknown script record, and the extra tables and columns are unused. Re-upgrading resumes from the stored data.
