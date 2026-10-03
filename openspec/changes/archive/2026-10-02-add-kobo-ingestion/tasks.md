# Tasks

## 1. Schema and model

- [x] 1.1 Add `src/dbUp/Scripts/0002_add_ingestion.sql`: `raw_submissions` and `syncs` with the columns, checks, unique and partial unique indexes from design D1, and the five new `projects` columns with their defaults and range checks; verify `SchemaMigratorTests` applies it on an empty database and on one at `0001`, and existing projects get `next_sync_at` set
- [x] 1.2 Add the `RawSubmission` and `Sync` entities, the new `Project` properties and their mapping in `TamizaDbContext` (enum-like columns as lowercase text); verify `SchemaDriftTests` reports no difference between the scripts and the EF model
- [x] 1.3 Add `IngestionOptions` (tick, max concurrent runs, page sizes, page byte cap, page timeout, safety margin, retry delay) bound from the `Ingestion` section with design D7 defaults, and register the ingestion services in an `AddTamizaIngestion()` extension called from `Program.cs`; verify a unit test binds overrides and the defaults

## 2. Submission storage

- [x] 2.1 Implement the parser that turns one Kobo submission JSON element into an upsert row (`_id` integer, non-empty `_uuid`, optional `__version__`, `_submission_time` read as UTC, raw JSON text), reporting which field is invalid; verify unit tests for a complete record, `_id` as a string or missing, empty `_uuid`, missing `__version__` and a time without an offset
- [x] 2.2 Implement `SubmissionStore.UpsertAsync(projectId, rows, origin)` with the single statement from design D3 (de-duplicating `_id` within the batch, last wins) returning created and updated counts; verify integration tests: insert counts as created, changed JSON as updated, identical JSON (different key order) as neither, a deleted row is restored and counted as updated, and `origin`/`received_at` never change on update
- [x] 2.3 Implement deletion marking (binary `COPY` of collected IDs into a temporary table, then the guarded `UPDATE` from design D6) returning the deleted count; verify integration tests: missing IDs are marked with `deleted_at`, rows received after the run start are kept, an empty ID set marks every row, other projects' rows are untouched

## 3. Webhook

- [x] 3.1 Implement Basic credential checking for a project (username `tamiza`, secret decrypted with `SecretProtector`, fixed-time comparison; unknown project, undecryptable secret and mismatches all fail the same way); verify unit tests for header parsing (missing, not Basic, bad base64, no colon) and integration tests for each failure returning the same 401 body with `WWW-Authenticate: Basic`
- [x] 3.2 Implement `POST /api/v1/webhooks/kobo/{projectId}` (`AllowAnonymous`) per design D4: authenticate before reading the body, 415 for non-JSON, 10 MB limit with 413, 400 `webhook.invalid_submission` naming the field, upsert with origin `webhook` and `last_webhook_at` in one transaction, empty 200; verify integration tests: valid delivery stored, wrong and missing secret, unknown project, regenerated secret rejects the old one, XML gets 415, missing `_id` gets 400, an 11 MB body gets 413, duplicate keeps one row, an edit with a new `_uuid` updates the row, and nothing is stored in any rejected case
- [x] 3.3 Keep the webhook out of logs: no body and no `Authorization` header in any log entry, rejected deliveries logged at Information with project ID and reason; verify an integration test asserting the secret and a marker value from the body never appear in captured logs

## 4. Kobo submission reads

- [x] 4.1 Add `KoboClient.ReadSubmissionsAsync` per design D5 (keyset paging on `_id` with `sort`, optional `_submission_time` cutoff, optional `fields=["_id"]`, `ResponseHeadersRead`, length-limited stream, per-page timeout, existing error mapping); verify unit tests for the generated `query` JSON and cutoff format, and integration tests against `KoboStub` serving 3 pages (requests carry the expected `$gt` values, paging stops on a short page), a page over the byte cap, a timeout, and 401 mid-read
- [x] 4.2 Extend `KoboStub` with paged submission data and ID-only responses matched on the `query` and `fields` parameters, and a fixture generator for N submissions; verify the stub tests in 4.1 use it
- [x] 4.3 Move `ProjectEndpoints.ApplySnapshot` into a shared `FormSnapshot.Apply` used by project endpoints and synchronization; verify existing project integration tests still pass

## 5. Synchronization runs

- [x] 5.1 Implement the run executor for `incremental`, `full` and `reconciliation` (design D6): decrypt the token (`kobo.token_unreadable` on failure), refresh the form snapshot, compute the cutoff from the last succeeded `incremental`/`full` start minus the margin (none → run as `full`), upsert page by page in separate transactions updating the run's counters, collect IDs and mark deletions only after a complete read, finish as `succeeded` or `failed` with `error_code` and `error`; verify integration tests: initial backfill of 3 pages, incremental sends the expected cutoff, a record missed by the webhook is stored with origin `sync`, counts for new/changed/identical records, failure on page 4 keeps pages 1–3 and leaves the cutoff unchanged, token rejected and token unreadable recorded as failed, form redeployed with `start`/`end` makes completion time available after the run
- [x] 5.2 Verify reconciliation and full sync through the executor with integration tests: a record deleted in Kobo is marked and counted, an incomplete listing marks nothing and fails with the reason, a record received by webhook after the run started is kept, a form with zero submissions marks every record, and a full sync updates a record edited in Kobo
- [x] 5.3 Implement `IngestionScheduler` (hosted service) and `IngestionSignal` per design D7: startup marks `running` syncs as failed `sync.interrupted`; each tick enqueues due `incremental` (trigger `initial` without a succeeded sync, else `schedule`) and `reconciliation` runs with `ON CONFLICT DO NOTHING`, claims queued runs with `FOR UPDATE SKIP LOCKED` up to the concurrency limit, sets due times on start, and applies the retry delay after a failed scheduled run; verify integration tests with `FakeTimeProvider`: a project falls due after its interval, a due run waits while another is active, a failed scheduled run with a 12 h interval is due again 1 h later, a `running` row is marked interrupted at startup and the next run proceeds, and two projects run concurrently but never two runs of one project
- [x] 5.4 Set `next_sync_at = created_at` on project creation and poke the signal; verify an integration test that creating a project against a stub with existing submissions stores them within a few ticks, recorded as `full` with trigger `initial`

## 6. Ingestion API

- [x] 6.1 Implement `GET /projects/{id}/ingestion` (viewer) and `PATCH /projects/{id}/ingestion` (admin, 1–168 hours, recomputing due times) per design D8; verify integration tests: status counts active and deleted submissions, `lastWebhookAt` null until a delivery, `activeSync` while one is queued, analyst `PATCH` gets 403, 0 and 200 hours get 400 with the range, a new interval moves `nextSyncAt` to last start plus the interval
- [x] 6.2 Implement `POST /projects/{id}/syncs` (analyst; `incremental` or `full`; 202 with the queued run; 409 `sync.in_progress` from the active index) and `GET /projects/{id}/syncs` (viewer; newest first; `page`, `pageSize` ≤ 100); verify integration tests: viewer `POST` gets 403, a second request while one is active gets 409, the run records `requestedBy`, paging returns the right slices and `total`, a non-member gets 404
- [x] 6.3 Check the OpenAPI document lists the new endpoints with their request and response types; verify by fetching `/api/v1/openapi.json` in an integration test and asserting the operation IDs

## 7. UI

- [x] 7.1 Add `core/projects/ingestion.service.ts` (status, settings, start sync, log page) and the `data` child route and tab (all members, after Overview); verify unit tests for the service URLs and that the tab appears for a viewer
- [x] 7.2 Build the Data section status card and actions (design D9): counts, last webhook ("none received yet"), last sync badge, next due times; "Sync now" and "Full resync" with confirmation for analysts and admins, disabled while a run is active, toast for `sync.in_progress`; poll every 5 s while active and stop on destroy; verify unit tests for each role's controls, the confirmation dialog, the 409 message, and polling start and stop with fake timers
- [x] 7.3 Add the schedule form (admin) and the synchronization log (table at `md`+ in an `overflow-x-auto` wrapper, stacked cards below, pagination, error text on failed runs); verify unit tests for inline range errors, the read-only view for non-admins, and log rendering of each status
- [x] 7.4 Update the webhook section: remove the `ingestionNote`, add the "type JSON, no field subset or custom wrapper" step, show the last webhook time; add `en.json` keys for every new text and error code (`sync.in_progress`, `sync.interrupted`, `kobo.token_unreadable`, `webhook.*`); verify the webhook unit tests and an i18n test that every code used has a key
- [x] 7.5 Verify the Data and Webhook sections at 360 px and 1280 px in headless Chrome (`scrollWidth <= clientWidth`, screenshots captured)
- [x] 7.6 Serve `/i18n/` with `location ^~` in `tamiza-ui/nginx.conf` so translations are revalidated (`no-cache`) instead of matching the static-file regex and being cached as immutable, which hid the new keys from browsers that had loaded `en.json` before; verify `curl -I /i18n/en.json` returns `Cache-Control: no-cache` and fingerprinted bundles keep `immutable`

## 8. Documentation

- [x] 8.1 Update `docs/kobo.md`: remove the "later release" note, add the JSON type and field-subset guidance to the REST Service steps, and add sections on synchronization (schedule, Sync now, Full resync and when to use it), deletion reconciliation and the Data section, plus troubleshooting rows for 401/415 deliveries in Kobo's REST Service log and failed synchronizations; verify by following it during group 9
- [x] 8.2 Update `README.md` status and `docs/operations.md` (outbound Kobo traffic from scheduled synchronization, the `Ingestion` settings in `appsettings` and the single-replica assumption); verify links resolve and `scripts/check-env-example.sh` passes

## 9. Stack verification

- [x] 9.1 With the full Compose stack and a real Kobo form (or a WireMock container on the stack network with private networks enabled): configure the REST Service from `docs/kobo.md`, submit a record and see it in the Data section within seconds; disable the REST Service, submit another and see it after "Sync now"; delete one in Kobo and see it marked after reconciliation
- [x] 9.2 Measure webhook latency through the `ui` nginx (100 deliveries, p95 under 500 ms) and back-fill 500,000 stub submissions, recording duration and peak `api` memory against its 512 MB limit; confirm or adjust the page size and byte cap defaults and note the results in the change
