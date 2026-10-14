# Design

## Context

Builds on `add-project-management`, `add-kobo-ingestion`, `add-choice-labels` and `add-question-labels` (main specs `projects`, `ingestion`, `auth`, `web-ui`). See `proposal.md` for the motivation. Relevant current state:

- **Raw submissions.** Tamiza keeps its own copy of every submission in its database, written by the webhook and the synchronizations. The dashboard reads that copy and never contacts Kobo.
  - `tamiza.raw_submissions` has `project_id`, `kobo_id`, `kobo_uuid`, `form_version`, `submitted_at` (from `_submission_time`, parsed as UTC, null when missing or unreadable), `data` (jsonb, as Kobo sent it), `deleted`, `updated_at` and `deleted_at`.
  - Its indexes are a unique index on (`project_id`, `kobo_id`), one on (`project_id`, `kobo_uuid`), and the partial (`project_id`, `form_version`) `WHERE NOT deleted`.
  - No index covers `submitted_at` or the jsonb.
  - At 500,000 submissions of about 1.2 KB the table holds about 630 MB. A `GROUP BY form_version` over it took 0.49 s. No aggregate that reads jsonb keys has been measured.
- **Keys in `data`.**
  - Every answer is a top-level key named by its full path (`respondent/municipality`), including grouped questions. Repeat groups are arrays.
  - `start` and `end` carry the device's offset (`2026-09-01T09:40:00.000-05:00`). `_submission_time` has none and is UTC. Numbers arrive as strings.
  - The analytics worker reads a timestamp without an offset as UTC, and an unreadable one as empty.
- **Form snapshot.**
  - `projects.form_fields` is a list of `{name, xpath, type, label, listName, orOther}` for the deployed version, and `form_choices` holds the choice lists. `type` is `start`, `end` or `username` for those metadata fields.
  - A select's type is `select_one`, `select_one_<list>`, `select_multiple` or `select_multiple_<list>`.
  - A field's xpath reflects its group nesting, so a `start` inside a group is `grp/start`.
- **Field check.**
  - `FieldCheck.Evaluate(fields, enumeratorField)` returns four `MetricAvailability(metric, available, missingFields, hint)` entries: `dailyEvolution`, `completionTime`, `perEnumerator` and `coverage`. It is computed on every read of `ProjectDetails`.
  - `coverage` and `dailyEvolution` are always available.
  - `perEnumerator` is available with a `username` field or with `projects.enumerator_field` naming a snapshot xpath.
  - `PATCH /projects/{id}` (admin) sets `name` and `enumeratorField`.
- **Sampling frame.**
  - `tamiza.sampling_frames` has one row per project, with `dimensions` = `[{name, field}]` (field = xpath) and `targets` = `[{values: [...], target}]`, values in dimension order.
  - The frame allows up to 10 dimensions and 10,000 rows. Values are trimmed and kept with their case, and duplicates are detected case-insensitively.
  - A dimension's field only has to exist in the snapshot when the frame is saved.
  - `GET /sampling-frame` answers 204 when there is none.
- **API data access.**
  - EF Core with Npgsql. Raw SQL goes through `SqlQueryRaw` or `NpgsqlCommand` on the context's connection, with parameters.
  - Npgsql's command timeout is the default 30 s, and nginx's proxy read timeout is the default 60 s.
  - The memory limits are `api` 512 MiB and `db` 1 GiB. Webhook deliveries must answer within 500 ms.
- **UI.**
  - The project sections are lazy child routes of `projects/:id`, with `''` redirecting to `overview`. The tabs are built in `ProjectPage.tabs()`.
  - The Overview's card titled "Performance dashboard" lists the field check, with an admin-only enumerator select shown only when the metric is unavailable or already set.
  - The shared UI has `card`, `badge`, `alert`, `skeleton` and `table` (`tmz-table-container` scrolls inside), and lists use phone cards from `md` down. There is no chart library.
  - The CSP allows inline styles and inline SVG.
  - Unit tests run with vitest and jsdom through `project-harness.ts`.

Requirements are in `specs/dashboard-performance` and `specs/projects`. User decisions for this change:
- A day is the reception date (`_submission_time`) in a per-project time zone that admins set, defaulting to UTC.
- Completion-time outliers use Tukey's fences, with unusable durations counted apart.
- Coverage compares codes, trimmed and case-insensitive, with an "outside the frame" count and warnings for dimensions that cannot be counted.
- There is a date-range filter.
- The enumerator is the selected question, otherwise `username`, normalized.
- A new Performance tab is the project's landing page, with a manual Refresh.
- The field check reports coverage unavailable without a usable frame.

## Goals / Non-Goals

**Goals:**

- Figures are computed in PostgreSQL. Only small, bounded results reach the API (at most 10,000 coverage rows, one row per enumerator, one per day).
- One pass over the project's rows per request. Results stay correct at 500,000 submissions within the 25 s limit, with no effect on webhook latency beyond shared I/O.
- A malformed value in one submission never fails the dashboard. It is counted where it matters.
- The same availability rules in the Overview and the dashboard (one `FieldCheck`).

**Non-Goals:**

- Precomputed summary tables, or computing the dashboard in the analytics worker.
- Per-version locations of `start`, `end` or the enumerator (the deployed snapshot's xpaths are used for every submission).
- Changing how sampling frames are saved or validated.

## Decisions

### D1. Data model (DbUp `0006_add_performance_dashboard.sql`)

- `projects.time_zone varchar(64) NOT NULL DEFAULT 'UTC'`.
- `CREATE INDEX ix_raw_submissions_project_id_submitted_at ON tamiza.raw_submissions (project_id, submitted_at) WHERE NOT deleted`.
  - It narrows the scan when a date range is set. Without a range the statement reads every live row of the project from the heap, because the same pass reads `data`.
  - Inserts already maintain three indexes. One more narrow btree adds microseconds per row and does not threaten the 500 ms webhook target, which is checked in task 6.
- Both are mirrored in `TamizaDbContext` for `SchemaDriftTests`.

*Alternatives:*
- Expression indexes on `data->>'start'` and similar cannot be shared across projects, since the xpaths differ per form. The text-to-timestamptz cast is not immutable, so it cannot be indexed either.
- A summary table refreshed after each synchronization goes stale after webhook deliveries (the default sync interval is 12 h). It also adds a write path.

### D2. Time zone (api)

- `UpdateProjectRequest` gains `TimeZone`. It is admin-only, like the rest of `PATCH /projects/{id}`.
- A value is accepted when `SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = @tz)` is true. The database is what applies the zone, so it is the judge.
- An unknown value gives a validation error on `timeZone` naming the value.
- `ProjectDetails` gains `TimeZone`.

*Alternative:* `TimeZoneInfo.FindSystemTimeZoneById` accepts names the database might not know. The two lists can differ between images.

### D3. Field check (api)

`FieldCheck.Evaluate(fields, enumeratorField, frame)`. The callers load the project's `SamplingFrame` by primary key, one extra query when reading details.

- **`perEnumerator`:**
  - Unchanged in availability: a `username` field, or a selected field in the snapshot.
  - A new `FieldCheck.EnumeratorKey(fields, enumeratorField)` returns the key to group by: the selected xpath when it is in the snapshot, otherwise the xpath of the `username`-typed field, otherwise none. The dashboard uses this key.
- **`coverage`:**
  - Unavailable with hint `defineSamplingFrame` when there is no frame.
  - Unavailable with `missingFields` = the frame fields not in the snapshot, and hint `updateSamplingFrame`, when some are missing.
  - Otherwise available.
- **Coverage in the dashboard:** the `coverage` entry does not look at multiple-choice or repeat dimensions. The dashboard's coverage section is shown whenever a frame exists, with its own warnings, even when the Form check lists coverage as unavailable (a missing field). The section has no `available` flag of its own: it is `notConfigured` or `configured`.
- **`completionTime`:** unchanged. It looks up `start` and `end` by type, and a new `FieldCheck.TimeKeys(fields)` returns their xpaths.
- **Repeat groups in the snapshot.** `KoboForm` already tracks the groups a question sits in while it builds the path. `FormField` gains `InRepeat` (default false), true when any enclosing group is a repeat. It is stored in `projects.form_fields` and `form_versions.fields` with the other keys, so the dashboard knows which dimensions cannot be counted without looking at the data. Snapshots taken before this change lack the flag, and read as not in a repeat until the next synchronization or re-check rewrites them, which happens at every synchronization.
- **UI:** the enumerator select in the Overview is shown to admins whenever the form has questions, not only when the metric is unavailable.

### D4. Endpoint and computation (api)

`GET /api/v1/projects/{id}/performance?from=YYYY-MM-DD&to=YYYY-MM-DD`, with `.RequireProjectRole(ProjectRole.Viewer)` (non-member 404). Both dates are optional. `from > to`, or a date that does not parse, gives a validation problem on the field. The service reads only Tamiza's database.

The `PerformanceDashboard` service:

1. **Resolve.** It reads the project (time zone, snapshot, enumerator field) and the frame. Then it resolves, from D3:
   - the `start` and `end` keys;
   - the enumerator key;
   - for each frame dimension, its key and its countability: in the snapshot; not inside a repeat group (`FormField.InRepeat`, D3); not `select_multiple*`.
2. **Query.** It runs in its own scope (D5), in one read-only transaction with `statement_timeout` set to 25 s (an option, through `set_config('statement_timeout', @ms, true)`, since `SET` takes no parameters) and `TimeZone` set to `UTC` the same way (so timestamps without an offset read as UTC, as in analytics). One statement made of CTEs does the work. Every key is a parameter (`data ->> @k_start`), never concatenated:
   - **`base`:** the project's rows `WHERE project_id = @p AND NOT deleted`, read once per row. PostgreSQL does not cache a detoasted value between expressions, and a row of several KB is stored compressed or out of line, so every `data ->> key` would decompress the whole row again. `base` therefore takes the document once, with `CROSS JOIN LATERAL (SELECT r.data || '{}'::jsonb AS d OFFSET 0) x`, and reads every key from `x.d`. Rows are filtered by the range on the local day of `submitted_at`, written as `submitted_at >= (@from::date)::timestamp AT TIME ZONE @tz` and `< ((@to::date + 1)::timestamp AT TIME ZONE @tz)`, so the D1 index applies. With a range, rows without `submitted_at` drop out; without one they stay in totals, enumerators and coverage. Each row yields:
     - its local day, `(submitted_at AT TIME ZONE @tz)::date`;
     - its duration in seconds. `pg_input_is_valid` also accepts words such as `infinity` and `now`, and years so distant that subtracting two timestamps raises "interval out of range". So each value is first required to look like a date (`~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}'`), then to pass `pg_input_is_valid(..., 'timestamptz')`, then to be `isfinite`; the duration is `extract(epoch FROM end) - extract(epoch FROM start)`, which cannot overflow, and `end` must not be before `start`. Otherwise the duration is null and counted as unusable. SQL does not promise the order in which the parts of an `AND` are evaluated, so these checks are nested `CASE` expressions;
     - its enumerator, `nullif(lower(btrim(d ->> @k_enum, E' \t\r\n\u00A0')), '')`, plus an original spelling for display;
     - one normalized value per dimension, the same expression over `d ->> @k_i`, up to 10. Trimming removes spaces, tabs, line breaks and non-breaking spaces, like the frame's own trimming, and an answer of only those characters counts as empty.
   - **`fences`:** `percentile_cont(0.25)`, `percentile_cont(0.5)` and `percentile_cont(0.75)` over the usable durations, giving the lower and upper fences.
   - **Results.** The statement returns:
     - totals (all rows, rows without `submitted_at`);
     - day counts, with zero days filled in between the first and the last day with submissions inside the range (the whole project when none is set), so the series is never longer than the data;
     - completion figures (median; mean, usable and outlier counts against the fences; unusable durations), as finite `float8`;
     - one row per enumerator (count, mean without outliers, outliers, `max(submitted_at)`);
     - one row per combination of normalized dimension values (count).
3. **Coverage join.** It joins the frame in PostgreSQL. The frame's rows are passed as a jsonb parameter, unnested and normalized with the same `nullif(lower(btrim(...)), '')`, so .NET and PostgreSQL case rules never have to agree on what matches. This yields:
   - achieved per frame row;
   - outside the frame: the combination groups with no frame row, including any with a null value;
   - the 10 most frequent of those, with their original spellings.
4. **Labels.** In memory over the snapshot's `form_choices`, by the field's `listName` and the code, compared trimmed and ignoring case (an exact match wins when two choices differ only in case). This applies to the frame rows and to the outside-the-frame combinations. A code without a label shows only the code.

**Overall progress** = `sum(min(achieved, target)) / sum(target)` over the rows whose target is above 0. Surplus in one combination does not hide a shortfall in another. When no row has a target above 0 there is no overall progress.

**Response.** It gives `computedAt`, `timeZone`, the range, `availability` (the `FieldCheck` list) and four sections. Submissions, completion time and enumerators have an `available` flag and their figures. Coverage is `notConfigured`, or `configured` with dimensions (name, field, warning: `missingField` | `repeat` | `multipleChoice` | null), rows (values, labels, target, achieved, percent or null), totals, overall progress, `outsideFrame` and `outsideTop`.

**Time limit.** A statement stopped by `statement_timeout` (`57014`) maps to `503` with code `dashboard.timeout` and a translated message. A cancellation caused by a caller leaving is not a timeout (D5).

*Alternatives:*
- Several separate queries, one per part, would multiply the table scans by four.
- Computing the quartiles in .NET would need every duration in API memory.
- Reading the flattened `p_<id>_raw` tables would depend on runs, hold choice labels instead of codes, and lag behind webhooks.

### D5. Cache and concurrency (api)

- **Marker.** Webhook deliveries already write `projects.last_webhook_at` in the same transaction as the submissions, and a finished synchronization sets `finished_at` on its `syncs` row. Each request reads both (the project row is loaded anyway; the latest `finished_at` is one indexed query) and uses them as the freshness marker.
- **Cache.** A dedicated `MemoryCache` with a size limit (64 entries), so that varied ranges cannot grow it without bound. The key is (project, from, to, time zone, snapshot `formCheckedAt`, enumerator field, frame `updatedAt`, `last_webhook_at`, latest sync `finished_at`). An entry is also stale 60 s after `computedAt`, the time its statement started, checked against the injected `TimeProvider` (not a sliding expiry, and not 60 s from insertion). A new webhook delivery or finished synchronization therefore changes the key and the next request, a Refresh included, recomputes at once. While a synchronization is still running its submissions can lag by at most 60 s.
- **Shared computations.** Concurrent requests for the same key share one in-flight computation, through a per-key `Lazy<Task>` removed when it completes. It runs in its own scope (`IServiceScopeFactory`), with its own `DbContext` and `CancellationToken.None`, bounded only by `statement_timeout`. Each request awaits it with `task.WaitAsync(requestAborted)`, so a caller who leaves does not cancel the statement for the others. Failures are not cached. A semaphore lets at most 2 computations run at a time, across projects: a scan reads up to the project's whole table, `db` has 1 GiB, and the connection pool is shared with webhook deliveries. A request that has waited 25 s for its turn fails with the same `dashboard.timeout`.
- **Refresh.** Refresh asks again; it gets the cached figures only when nothing has arrived since and they are under a minute old. The page shows "Computed at 10:42".

*Alternative:* a fixed 60 s TTL without the marker is simpler, but a Refresh would then miss a submission that arrived seconds earlier, which the Refresh button promises not to do.

### D6. UI

- **Route and tabs.** A `performance` child route is the target of the `''` redirect. **Performance** is the first tab, visible to every role, with the key `project.tabs.performance`.
- **Page (`project-performance.ts`).** A `PerformanceService.get(id, from?, to?)`, and a toolbar with two date inputs, Clear and Refresh, plus "Computed at … (time zone)" and "Days in UTC" when the zone is the default. Then come four cards:
  - **Submissions:**
    - a stat tile;
    - a daily bar chart, drawn as an inline SVG with one bar per day, scaling to the card width;
    - a "Show as table" toggle with the same numbers, which also serves screen readers (the SVG has `role="img"` and a summary label);
    - the count without reception time.
  - **Completion time:**
    - tiles for the median, the mean without outliers, outliers, and unusable durations;
    - the fences in minutes;
    - when unavailable, the missing fields, how to enable them in Kobo, and a link to the Overview.
  - **Enumerators:**
    - a table at `md` and up, cards below;
    - sorting by submissions or name, with paging at 50;
    - "No enumerator" last;
    - when unavailable, an explanation, with a link to the Overview selector for admins.
  - **Coverage:**
    - overall progress and the outside-the-frame count, with the top combinations;
    - the dimension warnings as an `alert`;
    - the rows table (values with labels, target, achieved, percentage, and a progress bar capped at 100% while the text shows the real percentage), sortable by percentage with paging at 50;
    - without a frame, the empty state: a link to `sampling-frame` for analysts and admins, text only for viewers.
- **Overview.**
  - The card heading becomes "Form check", with a link to the dashboard, and it renders the hints.
  - The settings card gains the time zone `<select>` for admins: `Intl.supportedValuesOf('timeZone')` plus `UTC`, with the API as the final judge.
  - The enumerator select is shown to admins always.
- **Frame changes reach the Overview.** The Overview's check now depends on the frame, and the project details are loaded once per page. After the sampling frame page saves or imports a frame, it re-fetches the project details and updates the project context, so the Form check does not stay stale until a reload.
- **Formatting.** Numbers use `DecimalPipe`, and durations read like "24 min" or "1 h 05 min".
- **Translations.** `en.json` gains a `performance.*` namespace and `errors.codes.dashboard.timeout`.

*Alternative:* a chart library such as Chart.js or ECharts adds a dependency and its license notice, and draws on canvas, which jsdom cannot test. The charts here are one bar series and progress bars.

### D7. Docs

- **`docs/performance-dashboard.md` (new)** covers:
  - what each part shows and how it is computed (days, the zone, fences, normalization, overall progress);
  - what "outside the frame" means, and how to fix lost leading zeros (store codes as text in the file);
  - why multiple-choice and repeat dimensions cannot be counted;
  - when figures are recomputed (new webhook deliveries and finished synchronizations, at most one minute otherwise);
  - that the dashboard reads Tamiza's stored copy and never contacts Kobo.
- **`docs/kobo.md`:** section 5 (the Form check card, enumerator precedence, time zone), the sampling frame matching rules, and the roles table.
- **README status, and `docs/tamiza-brief.md`:** section 5.5, the data model with `projects.time_zone`, and the plan.

### D8. Test strategy

- **API unit tests:**
  - `FieldCheckTests`:
    - coverage without a frame (`defineSamplingFrame`) and with a missing field (`updateSamplingFrame` naming it);
    - `EnumeratorKey` precedence, and a stale selection falling back to `username` (or to none without it);
    - `TimeKeys` for grouped `start`;
    - `KoboForm` marking `InRepeat` for a question inside a repeat group, also inside a group inside a repeat, and not for a plain group.
  - Range validation.
  - The overall-progress formula, including every target being 0.
- **API integration** (Testcontainers, rows seeded into `raw_submissions` with explicit `data`, `submitted_at` and `deleted`):
  - each scenario of `specs/dashboard-performance` (Bogotá evening, default UTC, a submission without reception time, range, reversed range, one-sided ranges, daily zeros, outliers, unusable durations including `infinity`, `-infinity` and `now`, form without `end`, enumerator normalization, blank values and precedence, coverage with case and spaces and its label, over target, a target of 0, overall progress, a lost leading zero outside the frame, a removed field, `select_multiple`, a repeat dimension, no frame), with deleted submissions and the date range asserted on coverage as well as on the other parts;
  - roles (viewer 200, non-member 404);
  - the cache, with the injected `AdjustableTimeProvider`: a second request within 60 s is not recomputed, a request after 60 s is (with the new submission counted), a webhook delivery or a finished synchronization recomputes at once, and a changed frame or time zone recomputes;
  - concurrent requests: two identical requests run one computation (counted with a hook), and aborting the first still gives the second its figures;
  - the time limit, made deterministic by a test-only option that adds `pg_sleep` to the statement, then asserting `503 dashboard.timeout` and that the next request to the project answers 200.
  - In `ProjectsApiTests`: time zone accepted, unknown, and set by a non-admin.
  - `SchemaDriftTests`.
- **UI:**
  - `project-performance.spec.ts`, per role and state:
    - all available;
    - completion unavailable;
    - enumerator unavailable (with and without admin);
    - no frame (analyst with a link, viewer without);
    - warnings;
    - the outside-the-frame list;
    - the range sent as query parameters;
    - the timeout message;
    - the "Days in UTC" notice with the default zone, and the zone name otherwise;
    - a range with only one bound.
  - The tab first and the landing redirect.
  - The Overview: the renamed card, the time zone select for admins only, the enumerator select always for admins, and coverage turning available after a frame is saved.
  - The error-code test.
  - Headless Chrome screenshots at 360 px and 1280 px.
- **Performance:** 500,000 submissions seeded with SQL in a separate Compose project, once at about 1.2 KB and once at about 5 KB of jsonb that does not compress (stored out of line, as large forms are), with a frame of 3 dimensions, cold and warm, with and without a range. Record:
  - the endpoint time through nginx;
  - `EXPLAIN (ANALYZE, BUFFERS)` of the statement;
  - the `api` and `db` peak memory;
  - the statement time against the number of keys extracted, to confirm that each row is read once;
  - webhook latency while the dashboard computes, with several computations running at once for different ranges.

## Risks / Trade-offs

- **[A full pass over 630 MB on each computation, cold, with 1 GiB for `db`]** → One statement per request, the D1 index for ranges, the cache and shared in-flight computations, and the 25 s limit with a clear error. Task 6 records the times. If a cold pass exceeds about 10 s, the follow-up is a per-project summary table, which is a change of its own.
- **[Dashboard scans compete with webhook writes for I/O]** → Reads take no locks that writes wait on. Task 6 measures webhook latency during a computation.
- **[A snapshot taken before this change has no repeat flag]** → A dimension inside a repeat is not warned about until the next synchronization or re-check, which rewrites the snapshot. Counts are shown as they match either way.
- **[Scans compete with webhook writes]** → At most 2 computations run at a time, and task 6.1 measures webhook latency with several running.
- **[Grouping enumerators ignoring case can merge two people whose names differ only in case]** → This is the user's chosen rule. The display shows the first spelling seen.
- **[`start` and `end` located from the deployed version only]** → Older versions that placed them elsewhere count as unusable durations. That number is shown.
- **[A range of many years, or a stray reception time]** → The day series spans the first to the last day with submissions, so its length follows the data, not the range. It is not capped: one submission whose `_submission_time` is far from the others (a webhook body with a year such as 2999) would add that many zero days. Kobo sets the time and the webhook needs the project's secret, so this is unlikely; if it shows up, the follow-up is to list only days with submissions past a span of, say, ten years. The cache has a size limit.
- **[Time zone change re-buckets past days]** → Expected: it is a display setting. The dashboard shows the zone in use.
- **[Many enumerators or a 10,000-row frame make a large response]** → Bounded by the frame limits. About 1 MB at most, paged in the UI.

## Migration Plan

- `0006_add_performance_dashboard.sql` adds the column, with its default, and builds the partial index. On 500,000 rows, a similar index took 0.8 s (`add-choice-labels`). It runs in the API's startup migration.
- After deployment, projects open on the Performance tab, with days in UTC until an admin sets the zone. The field check reports coverage as unavailable for projects without a frame. That is new wording, not a new failure.
- **Rollback:** older images ignore the column and the index. To remove them by hand, also delete the script's row from `tamiza.schema_versions`.

## Verification Results

**Narrower than task 6.1.** This is a measurement of the real endpoint in the API's integration-test host, not the Compose-stack check: no nginx, no sign-in through a fake provider, no webhook latency under load, and no `EXPLAIN (ANALYZE, BUFFERS)`. The numbers are therefore the cost of the statement plus the API's own work, on a warm PostgreSQL container, with PostgreSQL's default `shared_buffers` and the machine's Docker VM.

Measured on the development machine, through `GET /api/v1/projects/{id}/performance` with the default 25 s limit, on 500,000 live submissions seeded with SQL (a form with `start`/`end` with offsets, 40 enumerators, a frame of 3 dimensions and 12 rows, submissions spread over 60 days):

| Request | Rows of about 1.2 KB (table 723 MB) | Rows of about 5 KB, not compressible (table 2,773 MB) |
|---|---|---|
| Whole project, first request | 4.95 s | 7.79 s |
| The same request again, within the minute | 0.01 s | 0.01 s |
| From 2026-07-10 (most of the data) | 3.89 s | 6.49 s |
| 2026-07-10 to 2026-07-20 | 0.81 s | 1.36 s |
| Whole project after changing the time zone | 4.78 s | 7.55 s |

- The response is about 11 KB. The test host, which includes the API, peaked at 177 MB.
- At 100,000 rows, extracting 3, 4 or 13 keys per row cost 0.65, 0.85 and 1.40 s (1.2 KB rows) and 1.5, 1.6 and 2.0 s (5 KB rows), so each row's document is read once and the cost grows with the rows, not with the keys.
- The 25 s limit was never reached, and both sizes stay under the "about 10 s" line of the first risk, so no summary table is needed for this volume.

**Not measured:** webhook latency while a computation runs, with several computations on different ranges at once; the `0006` migration time on a large table; the figures through nginx; PostgreSQL's plan.
