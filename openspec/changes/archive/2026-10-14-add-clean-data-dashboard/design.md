# Design

## Context

Builds on `add-python-pipeline`, `add-pipeline-authoring`, `add-choice-labels`, `add-question-labels` and `add-performance-dashboard` (main specs `pipeline`, `projects`, `auth`, `web-ui`, `dashboard-performance`). See `proposal.md` for the motivation. Relevant current state:

- **Published schemas.** The analytics worker publishes each step into `p_<32 hex>_<step>` (`raw`, `clean`, `output`, `indicators`), built only from the project's UUID (`names.schema_name`).
  - `publish.publish` loads the tables into `<schema>__next`, then, in one transaction, `DROP SCHEMA <published> CASCADE` and `ALTER SCHEMA <next> RENAME TO <published>`. The worker's connection is in autocommit mode and sets no `lock_timeout`, so the swap waits as long as any reader holds a lock on a published table, and while it waits every new reader of those tables queues behind it.
  - Without script 1 the clean step publishes the flattened tables, with their question labels as column comments. Without script 2 the output schema is dropped. Tables returned by scripts carry no comments.
  - Table names are `^[a-z][a-z0-9_]{0,62}$`, column names `^[a-z_][a-z0-9_]{0,62}$`.
  - Column types are only `bigint`, `numeric`, `double precision`, `boolean`, `date`, `timestamptz` (naive timestamps taken as UTC) and `text`. Geometry is published as WKT text.
  - Tables are written once with `COPY` and never updated until the next swap, so a row's `ctid` is stable for the life of the published table.
- **Run records.** `tamiza.run_steps (run_id, step, status, finished_at, tables jsonb, …)`, where `tables` is `[{name, rows}]` for the tables a step produced. Steps are `flatten`, `clean`, `output`, `indicators`.
- **Reading from the API.** The API connects with the same database role as the worker, so it can read the published schemas. Nothing in the API reads them yet. The performance dashboard set the pattern for heavy reads: its own scope, a read-only transaction, `statement_timeout` and `TimeZone` through `set_config(..., true)`, a `SemaphoreSlim` limiting concurrent computations, `Problems.Unavailable` with `dashboard.timeout`, and options bound from a configuration section with data annotations.
- **Limits.** `api` has 512 MiB and `db` 1 GiB. nginx proxies `/api/` with the default 60 s read timeout and buffers responses (default `proxy_buffering on`). There is no file download in the UI yet. The CSP has `form-action 'self'`, and the UI talks to the API with a bearer token added by an HTTP interceptor, so a plain navigation to an API URL carries no token.
- **Spreadsheets.** The API already depends on MiniExcel (sampling frame and reference table import), which can write XLSX from a streaming `IEnumerable` or `IDataReader` with low memory.
- **UI.** Project sections are lazy child routes of `projects/:id`; tabs are built in `ProjectPage.tabs()`, Performance first. The shared UI has `card`, `badge`, `alert`, `skeleton`, `table`, `dropdown-menu`, `confirm-dialog` and `toast`. `table-preview.ts` already shows a table with column names, labels, types and cut cells, and `ColumnType` is `integer | decimal | boolean | date | timestamp | text`. Unit tests run with vitest and jsdom.
- **Projects** have `time_zone` (IANA, default `UTC`), validated against `pg_timezone_names`.

Requirements are in `specs/dashboard-data`, `specs/auth` and `specs/pipeline`. User decisions for this change:
- The dashboard opens on the output tables, falls back to the clean tables, and switches between them when both exist. Raw is not offered.
- Typed filters: text contains/equals/is one of (from frequent values), ranges for numbers, dates and timestamps, true/false, empty/not empty, combined with AND.
- Every member, viewers included, can export.
- Exports are streamed through a single-use, short-lived link.

## Goals / Non-Goals

**Goals:**

- No user text ever reaches SQL as an identifier without being matched against PostgreSQL's catalog first, and every value is a parameter.
- A page of a 500,000-row table, filtered and sorted on any column, answers within the 25 s limit; the common case (no sort, first pages) answers in well under a second.
- An export of 500,000 rows keeps `api` memory flat, and holds locks on the published tables only while it reads them from PostgreSQL, not while a slow client downloads.
- A run that publishes during a long read never makes other readers wait more than a few seconds.

**Non-Goals:**

- Indexes, statistics or summary tables on the published schemas.
- Caching pages. Published tables change only when a run swaps them, and each request is a direct read.
- Several API instances. The download links and their files live in one API process.

## Decisions

### D1. Endpoints (api)

All under `/api/v1/projects/{projectId}/data`, with `.RequireProjectRole(ProjectRole.Viewer)` (non-member 404). `{source}` is `output` or `clean`; anything else is 404.

| Method and path | Purpose |
|---|---|
| `GET /data` | Sources and tables: for each published source, `publishedAt`, `runId` and `tables: [{name, rowCount}]`, plus `defaultSource` (`output`, `clean` or null) and `timeZone`. |
| `POST /data/{source}/{table}/query` | Body `{filters, sort, page, pageSize}`. Returns `{columns: [{name, type, label}], rows, cutCells, total, page, pageSize, publishedAt, runId}`. |
| `GET /data/{source}/{table}/columns/{column}/values?search=` | The 50 most frequent values with counts, for text and boolean columns. |
| `POST /data/{source}/{table}/exports` | Body `{format: csv|xlsx, filters, sort}`. Prepares the file and returns `{downloadUrl, fileName, rowCount, cutCells, expiresAt}`. |
| `GET /api/v1/data-exports/{token}` | Public (`AllowAnonymous`). Streams the file once. |

- **POST for the query.** Filters are a list of typed objects; a JSON body keeps them typed and documented in OpenAPI. The query is read-only, so a POST is only a transport choice. The UI keeps the same state in the page address (D7).
- **Filter shape.** `{column, op, value?, values?, min?, max?}` with `op` in `contains`, `equals`, `in`, `range`, `empty`, `notEmpty`. One filter per column, at most 50 filters, at most 100 values in `in`, at most 500 characters per text value.
- **Sort shape.** `{column, direction: asc|desc}` or null.
- **Published time.** For a source, `publishedAt` and `runId` come from the latest `run_steps` row of the project with `step = <source>` and `status = 'succeeded'`. While the schema exists that row is the run that published it. Row counts come from that row's `tables`, falling back to none for a table not listed there.
- **Errors.** `data.tableNotFound` (404) for a source or table that is not published, `data.columnNotFound` (404) for a column the table lacks in the values endpoint, validation problems on `filters[i]`, `sort` or `pageSize` (naming the column), `data.timeout` (503), `data.exportTooLarge` (422, with the limit), `data.xlsxTooManyRows` (422), `data.exportBusy` (503, when every export slot stays taken), and plain 404 for an unknown or used download token.

*Alternative:* `GET` with filters encoded in the query string. It works, but nested typed filters become an ad hoc grammar the API must parse and document.

### D2. Name resolution and SQL building (api)

- **Schema.** `p_{projectId:N}_{source}`, built from the route's `Guid` and the two allowed sources. Never from text.
- **Table and columns.** One catalog query per request, parameterized by schema and table name:
  `SELECT a.attname, format_type(a.atttypid, a.atttypmod), col_description(a.attrelid, a.attnum) FROM pg_attribute a JOIN pg_class c … JOIN pg_namespace n … WHERE n.nspname = @schema AND c.relname = @table AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped ORDER BY a.attnum`.
  No rows means 404 `data.tableNotFound`. Every column a filter or sort names must be in that list, compared exactly; otherwise a validation problem. Identifiers are then written with Npgsql's quoting (`"…"` with doubled quotes) from the catalog's own spelling.
- **Types.** `bigint`/`integer`/`smallint` → `integer`; `numeric`/`double precision`/`real` → `decimal`; `boolean`; `date`; `timestamp with time zone`/`timestamp without time zone` → `timestamp`; anything else → `text`. The type decides which filter ops are allowed.
- **Values.** Always `NpgsqlParameter`s with explicit `NpgsqlDbType`. A value that does not parse as the column's type (`long`, `decimal`, `DateOnly`, `bool`) is a validation problem before any SQL runs.
- **Predicates:**
  - `contains`: `col ILIKE '%' || @v || '%' ESCAPE '\'`, with `%`, `_` and `\` in `@v` escaped.
  - `equals`: `col = @v`.
  - `in`: `col = ANY(@values)` with a typed array.
  - `range` on numbers and dates: `col >= @min` / `col <= @max`.
  - `range` on timestamps: days in the project's zone, `col >= (@min::date)::timestamp AT TIME ZONE @tz` and `col < ((@max::date + 1)::timestamp AT TIME ZONE @tz)`, the same form the performance dashboard uses.
  - `empty`: `col IS NULL`, or for text `(col IS NULL OR col = '')`; `notEmpty` its negation.
- **Order.** `ORDER BY col ASC|DESC NULLS LAST, ctid` with a sort, `ORDER BY ctid` without one. `ctid` follows the order `COPY` wrote the rows in, which is the order the script returned them, and makes ties stable between pages.
- **Paging.** `LIMIT @size OFFSET @offset`. The count is a separate `SELECT count(*)` with the same `WHERE`, in the same transaction, so both see the same published table.

*Alternative:* a query builder library. The SQL is small and its safety rests on the catalog check, which a library would not do for us.

### D3. Reading (api)

A `DataDashboard` singleton, like `PerformanceDashboard`, owns the slots and the options:

- Each request runs in a read-only transaction with `set_config('statement_timeout', @ms, true)`, `set_config('TimeZone', @projectTz, true)` and `set_config('lock_timeout', @ms, true)` (5 s, so a request does not hang behind a swap; a lock timeout is reported as `data.timeout` too).
- **The table is locked first.** The catalog lookup finds out that the name is a published table; `LOCK TABLE ... IN ACCESS SHARE MODE` then keeps a run's swap from replacing it until the transaction ends (the swap queues behind the lock and the request's `lock_timeout` bounds the wait); a second catalog lookup gives the columns every following statement sees. A table the run replaced in between is a 404. So the catalog query, the count and the page always read one version of the table.
- **Paging sorts narrow rows.** `ORDER BY ctid` over whole rows makes PostgreSQL sort, and spill to disk, every row of a 60-column table to return fifty. The page query therefore pages only the row positions and the sort key in a subquery and reads just those rows by position: `SELECT ... FROM t WHERE ctid = ANY (ARRAY(SELECT ctid FROM t [WHERE ...] ORDER BY <sort>, ctid LIMIT  OFFSET )) ORDER BY <sort>, ctid`. The table is locked, so positions cannot move (see Verification Results for the effect).
- **Rendering values.** Rows are read with `NpgsqlDataReader` and written as JSON: integers as numbers, `numeric` as a JSON number when it fits a `decimal` and otherwise as a string, `double` as a number (NaN and infinities as strings), dates as `YYYY-MM-DD`, timestamps as ISO 8601 with the offset of the project's zone (the reader returns UTC and .NET converts with the IANA zone, which `TimeZoneInfo` resolves on Linux), text as is.
- **Cutting text.** In SQL, `left(col, 501)` for text columns on the page query, so a 10 MB cell never reaches the API. A value of 501 characters is cut to 500 and its `[row, column]` is added to `cutCells`, the same shape the preview uses.
- **Frequent values.** `SELECT col, count(*) FROM t WHERE col IS NOT NULL [AND col ILIKE @search] GROUP BY col ORDER BY count(*) DESC, col LIMIT 50`, values longer than the 500 characters a filter accepts are left out, because a cut value could not be picked as an exact match. It ignores the other filters, so the list does not change as filters are added.
- **Slots.** At most 4 page or value requests at a time across projects (`MaxConcurrentQueries`); a request that waits longer than the queue timeout gets `data.timeout`. Requests are cancelled when the caller leaves (unlike the performance dashboard, nothing is shared between callers).
- **Options** (`DataDashboard` section): `StatementTimeout` 25 s, `QueueTimeout` 25 s, `MaxConcurrentQueries` 4, `LockTimeout` 5 s, `ExportStatementTimeout` 5 min, `MaxConcurrentExports` 2, `MaxExportFileSize` 1 GiB, `ExportLinkLifetime` 5 min, `ExportDirectory` `/tmp/tamiza-exports`.

*Alternative:* `count(*) OVER ()` in the page query. It forces PostgreSQL to read every matching row before returning the first, even without a sort, which the separate count does anyway but the page query then avoids.

### D4. Exports (api)

**Prepare, then download.** `POST …/exports` does all the database work and writes the file; the download only streams a finished file.

1. Validate the request like a query (D2). Take an export slot (`MaxConcurrentExports`; waiting longer than the queue timeout gives `data.exportBusy`).
2. In a read-only transaction with `ExportStatementTimeout`, the project's `TimeZone` and the lock timeout, run `count(*)` first. For XLSX above 1,048,575 rows answer `data.xlsxTooManyRows` and stop.
3. Run the `SELECT` with `CommandBehavior.SequentialAccess` and write each row as it is read to `<ExportDirectory>/<random>.export`, counting bytes. With a sort the statement has an `ORDER BY` (ties by position). Without one it has none: the transaction sets `synchronize_seqscans` and `max_parallel_workers_per_gather` off, so the serial scan returns rows in the order they were published, and PostgreSQL does not sort every wide row of the table (a sort of the whole of the 500,000-row table spilled about 560 MB to disk). Past `MaxExportFileSize` the transaction is rolled back, the file deleted and `data.exportTooLarge` returned.
   - **CSV:** a `StreamWriter` with UTF-8 and BOM, RFC 4180 quoting, `CultureInfo.InvariantCulture`, dates `yyyy-MM-dd`, timestamps converted to the project's zone and written `yyyy-MM-ddTHH:mm:sszzz`, booleans `true`/`false`, empty for null.
   - **XLSX:** MiniExcel's `SaveAs` from an `IEnumerable<IDictionary<string, object?>>` fed by the reader, with the sheet named after the table. Numbers as numbers, dates and timestamps as `DateTime` in the project's zone (Excel has no zones), booleans as booleans, and text as strings. MiniExcel writes strings as inline or shared strings, never formulas, so `=1+1` stays text (covered by a test). Strings over 32,767 characters are cut and counted.
4. Commit, release the slot, and register the file in an in-memory `ExportRegistry` under a token of 32 random bytes (`RandomNumberGenerator`, base64url), with project, user, file path, file name, content type and expiry (`ExportLinkLifetime`). Return `downloadUrl = /api/v1/data-exports/{token}`.

**Download.** `GET /api/v1/data-exports/{token}` is `AllowAnonymous`:
- Removes the entry atomically (`TryRemove`), so a second request finds nothing. Unknown, used or expired → plain 404.
- Streams the file with `Results.File(stream, contentType, fileName)` (`Content-Disposition: attachment`, `Cache-Control: no-store`), opened with `FileOptions.DeleteOnClose`, so the file is gone when the response ends, completed or aborted.
- File name: `<project-slug>_<table>_<yyyy-MM-dd>.<ext>`, the slug reduced to ASCII letters, digits and dashes.

**Cleanup.** A hosted service sweeps expired entries every minute and deletes their files, and clears `ExportDirectory` at startup (entries do not survive a restart).

**Why prepare first:** the database read is bounded by `ExportStatementTimeout` and runs at disk speed, so locks on the published tables are held only for that; a slow or paused browser download no longer holds them. Errors (too many rows, too large, timeout) come back as JSON the UI can translate, instead of a broken browser download.

**Why a link:** the browser can only stream a large download to disk when it navigates to a URL; `HttpClient` with a blob would hold the whole file in the tab's memory. A navigation carries no bearer token, so the link itself is the credential: random, single-use, short-lived, for one file, issued after the role check (`specs/auth`). nginx's access log records the path, but by then the link is used or about to expire, and `Referrer-Policy: no-referrer` is already set.

**nginx.** The export path cannot be singled out by prefix, so it gets a regex location `~ ^/api/v1/projects/[^/]+/data/[^/]+/[^/]+/exports$` with `proxy_read_timeout 360s` and the same proxy settings and security headers as `/api/`. A `location ^~ /api/v1/data-exports/` sets `proxy_buffering off`, so the download streams straight through.

*Alternatives:*
- Streaming the query straight into the download response: no temporary file, but locks are held for the whole download and failures surface as truncated files.
- A background job with a status endpoint: needed only if preparation regularly exceeds a few minutes; the synchronous POST with a 6-minute ceiling covers 500,000 rows (task 6.1 measures it).
- A signed URL (HMAC with expiry) instead of a registry: stateless, but not single-use, and the file still lives in one process.

### D5. Publishing waits in short attempts (analytics)

In `publish.publish`, the swap transaction starts with `SET LOCAL lock_timeout = '5s'`. On `LockNotAvailable` (SQLSTATE `55P03`) the transaction rolls back, the staging schema is kept, and the swap is retried after a pause that grows from 1 s to 10 s, until `TAMIZA_PUBLISH_WAIT_SECONDS` (default 900, longer than the API's export time limit) has passed. Then the step fails with "The published tables were in use for more than 15 minutes; the previous tables stay published." and the staging schema is dropped.

- While an attempt waits, new readers queue behind it for at most 5 s; between attempts they proceed.
- `DROP SCHEMA … CASCADE` takes its locks table by table, and a failed attempt releases what it took when it rolls back, so readers never see a partial drop.
- The step's log records how long publishing waited when it waited more than 5 s.

*Alternative:* leave the swap unbounded and give readers a `lock_timeout` only. Readers would then fail for as long as one export runs, instead of waiting a few seconds.

### D6. UI

- **Route and tab.** `clean-data` child route; **Clean data** is the second tab, after Performance, visible to every role (`project.tabs.cleanData`).
- **Page (`project-clean-data.ts`)** with a `DataService`:
  - **Header:** a segmented switch Output / Clean (only when both are published), a table `<select>` with row counts, "Published {time} by run #{n}" linking to the run for analysts and admins, and "Times in {zone}".
  - **Toolbar:** active filters as removable chips, "Clear filters", the matching count, the export dropdown (CSV, XLSX).
  - **Table:** `tmz-table-container` scrolling both ways with a sticky header. Each header shows name, label (truncated, full in `title`) and type, a sort button cycling asc → desc → none, and a filter button. Cut cells marked as in `table-preview`. Empty values as `—` with a screen-reader text; an empty string as an empty cell.
  - **Filter dialog** (CDK dialog, full-screen below `md`): the ops for the column's type; for *is one of*, the frequent values with counts, a search box (debounced 300 ms) and checkboxes. Range inputs are `number` or `date` inputs.
  - **Paging:** previous/next, page number, page size select. Changing filters or sort returns to page 1.
  - **Export:** a toast "Preparing export…", then the UI sets `window.location.href = downloadUrl` (same origin, so the CSP allows it), then a toast with the row count and, for XLSX, the cut cells. Errors use the translated codes.
  - **Empty states:** nothing published (link to Scripts for analysts and admins, text for viewers), empty table, no rows match (with Clear filters).
- **Address state.** Query parameters `source`, `table`, `sort` (`col:asc`), `page`, `size` and `f` (the filter list as compact JSON, base64url-encoded). On load the page validates them against the returned columns, drops what no longer applies and shows an `alert` listing what was dropped. `replaceUrl` for paging, `push` for table and filter changes.
- **Republished.** The page keeps the `publishedAt` it last saw; when a response brings a different one, an info alert says "The tables were republished at {time}." A `data.tableNotFound` reloads `GET /data` and selects the default table with an alert.
- **Translations.** `en.json` gains `cleanData.*` and the `errors.codes.data.*` entries.

*Alternative:* a data-grid library (AG Grid community, ngx-datatable). Server-side paging and our own filter dialog are a small surface, and a grid would bring its own styling against Tailwind and Flowbite.

### D7. Docs

- **`docs/clean-data-dashboard.md` (new):** sources and the fallback, filters and how each type is matched, timestamps and the zone, the CSV and XLSX formats (BOM, cut cells, the XLSX row limit, CSV not defused for spreadsheet formulas), the download link, limits and the options that set them.
- **`docs/scripts.md`:** script 2's tables are what the dashboard shows; table order is row order; column comments become header labels.
- **`docs/operations.md`:** `DataDashboard__*` options, `TAMIZA_PUBLISH_WAIT_SECONDS`, the export directory inside the `api` container and its disk use.
- **README status and `docs/tamiza-brief.md`:** section 5.6 and the plan.

### D8. Test strategy

- **API unit tests:** filter validation per type and op (unknown column, wrong type, reversed range, too many values, escaping of `%` and `_` in `contains`); the type mapping; CSV writing (quoting, BOM, invariant numbers, timestamps with offset); the export registry (single use under concurrent `TryRemove`, expiry with the injected `TimeProvider`, sweeper deletes files); file name slug.
- **API integration** (Testcontainers; published schemas created with SQL as the worker would, including column comments and a `run_steps` row):
  - every scenario of `specs/dashboard-data`: sources and fallback, nothing published, table list and counts, paging and page size, labels, cut text, sort with nulls last and stable ties, default order, each filter op, timestamps in Bogotá, empty vs empty string, frequent values with search;
  - names: a table name with a quote or `;`, an unknown source, a column from another table, all rejected before SQL;
  - roles: viewer 200 everywhere including exports, non-member 404;
  - exports: CSV and XLSX contents read back (MiniExcel), `=1+1` stays text, XLSX row limit (with the limit lowered by a test-only option), size limit leaves no file, link single-use, link expiry, file gone after download and after an aborted download;
  - republishing: swap the schema between two page requests and check `publishedAt` and the new rows; drop a table and check `data.tableNotFound`;
  - time limit with a test-only `pg_sleep`, and `lock_timeout` when a test holds an `ACCESS EXCLUSIVE` lock.
- **Analytics:** `publish` with a concurrent reader holding a lock: the swap retries and succeeds after release; with a short `TAMIZA_PUBLISH_WAIT_SECONDS` it fails with the message and the previous schema is intact; a reader started during an attempt waits at most one attempt.
- **UI:** `project-clean-data.spec.ts`: switch shown only with both sources; empty states per role; sort cycling; each filter dialog by type; chips and Clear; address round trip, including a dropped filter; republished alert; table removed; export flow and error codes; tab second.
- **Screens:** headless Chrome screenshots at 360 px and 1280 px.
- **Performance:** in a separate Compose project, a published table of 500,000 rows and 60 columns (about 1 KB per row): first page without sort; page 5,000 sorted by a text column; a `contains` filter; frequent values of a text column; CSV and XLSX exports of all rows (time, file size, `api` peak memory); webhook latency and a page request while an export reads; a run publishing during an export.

## Risks / Trade-offs

- **[A sorted page or count scans the whole table]** → No indexes on published tables; a full sort of 500,000 rows takes a few seconds, inside the 25 s limit. Task 6.1 records it. If too slow, the follow-up is indexes chosen by the script, a change of its own.
- **[Exports compete with webhook writes and the performance dashboard for `db` I/O]** → At most 2 exports and 4 queries at a time, each with a time limit. Task 6.1 measures webhook latency during an export.
- **[Temporary files fill the `api` container's disk]** → `MaxExportFileSize` × `MaxConcurrentExports` plus files waiting for download (at most the files of the last 5 minutes); deleted on download, expiry and startup. Documented in `docs/operations.md`.
- **[A run's publish waits for an export, up to `ExportStatementTimeout`]** → Waits in 5 s attempts (D5), so readers are not held; the run takes longer. Documented.
- **[Links live in one process]** → A restart invalidates pending links, and several API replicas would not share them. Self-hosting runs one `api`; a shared store comes with the managed service.
- **[A leaked link gives the file to whoever opens it first]** → It works once, for 5 minutes, and is opened by the same browser right after issue. The bearer token never appears in a URL.
- **[CSV values starting with `=`, `+`, `-` or `@` run as formulas when opened in a spreadsheet]** → The user chose to keep values as published; XLSX is safe and recommended in the docs for spreadsheet users.
- **[Very wide tables (300+ columns) give heavy pages]** → 100 rows × 300 columns × 500 characters is at most about 15 MB; the default 50 rows and typical cells keep it far smaller. Column hiding is a follow-up.
- **[`ctid` order is the physical order]** → It matches the script's order because tables are written once by `COPY` and never updated. If publishing ever updates rows in place, the default order must change to an explicit row number column.

## Migration Plan

- No database migration. The API reads the published schemas that runs already produce, so projects that have run show their tables at once.
- The analytics change applies to the next publish; it changes nothing for runs without concurrent readers.
- The nginx config ships in the `ui` image.
- **Rollback:** older images ignore everything this change adds; there is no data to remove except temporary files, which go with the container.

## Verification Results

Measured on the development machine in a separate Compose project (nginx, `api` with its 512 MiB limit, `analytics`, and `db` with its 1 GiB limit, default PostgreSQL settings), through nginx, signed in with tokens from a fake OIDC provider, with the project created through the real API against a fake Kobo. The published table is one clean table of **500,000 rows and 60 columns** (8 typed columns and 52 text columns), 1,097 bytes per row on average and 559 MB with its TOAST. A second, 80-column table was used for the phone layout.

**Pages** (`POST .../query`, 50 rows unless noted; "first" is the first request after the change deployed, then the repeat):

| Request | Whole-row `ORDER BY ctid` (first version) | Narrow paging (as built) |
|---|---|---|
| First page, no sort | 1.10 s | 0.33 s first, 0.09 to 0.11 s repeated |
| Page 1 sorted by a text column | 1.14 s | 0.13 s |
| Page 5,000 of 100 rows, no sort | 5.44 s | 0.24 s |
| Page 5,000 of 100 rows, sorted by a text column | 5.09 s | 1.60 s |
| Page 5,000 of 100 rows, sorted by a decimal column | not measured | 0.56 s |
| Text `contains` filter, first and repeated | 0.75 s, 0.26 s | 0.27 s |
| Integer range plus text equals | 0.12 s | 0.12 s |
| Range, sorted by a text column, page 3 | not measured | 0.12 s |
| Frequent values of a 40-value column | 0.09 s | 0.09 s |
| Frequent values of a column with 500,000 distinct values | 0.70 s | 0.70 s |

The cause of the first version's times (`EXPLAIN (ANALYZE, BUFFERS)` on the same data): `count(*)` takes 48 ms with a parallel scan, but `ORDER BY ctid LIMIT 100 OFFSET 499900` sorted every 1 KB row, an external merge sort of 3.7 s that wrote about 560 MB of temporary files. Sorting only `(ctid)` takes 0.40 s, and `(name, ctid)` 1.45 s, with the 100 rows then read by position. The response is about 63 KB for 50 rows and 123 KB for 100.

**Exports of all 500,000 rows** (`POST .../exports`, then the download from the link, through nginx):

| Export | Prepare | File | Download | `api` peak | `db` peak |
|---|---|---|---|---|---|
| CSV, no sort | 3.4 to 4.0 s | 546,833,654 bytes (522 MiB), 500,001 lines | 1.7 to 1.9 s | 109 MiB | 710 MiB |
| XLSX, no sort | 15.6 s | 365,737,028 bytes (349 MiB), 30 million cells | 1.3 s | 173 MiB | 711 MiB |
| XLSX sorted by a text column | 22.2 s | as above | | 187 MiB | 857 MiB |

The `api` stayed far under its 512 MiB limit (the writers stream), no container was restarted or killed, and no file was left in the export directory after a download. The `db` figures include the operating system's cache of the table.

**While an export reads the table.** With the 22 s sorted XLSX export running, 48 webhook deliveries and 48 page requests were interleaved (one of each every 0.2 s): every one answered 200. Webhook latency was 7 ms median, 11 ms at most (7 ms, 86 ms at most, idle; the target is 500 ms). Page requests took 108 ms median, 146 ms at the 95th percentile and 914 ms at most.

**A run publishing during an export.** A run was requested 4 s into a 19.9 s export of the table that the run replaces. The run's clean step waited for the export and its log says **"Publishing waited 14 s for readers of the published tables."**; the run succeeded 16 s after it was requested, when the export finished. Page requests during the wait took 1.0 s, 5.3 s and 5.4 s, that is, held for at most one 5 s attempt, and succeeded. After the swap, a request for the table that the run no longer published answered 404 `data.tableNotFound`.

**Phone and desktop** (headless Chrome, signed in through the fake provider, 1280 and 360 px): the page did not scroll sideways at either width with the 60-column and the 80-column table; the table scrolled inside its container; the filter dialog filled the 360 by 740 screen and was a 544 px panel at 1280; the export menu opened on both.

**Not measured:** a cold operating-system cache, three or more exports at once (the limit is two), and the time of a run's own publish of a 500,000-row table (this change adds no migration).
