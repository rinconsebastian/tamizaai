# Design

## Context

Builds on `add-python-pipeline` (main spec `pipeline`). Relevant current state:

- **analytics** (`tamiza-analytics/src/tamiza`): `worker.py` calls `pipeline.Processor` once per cycle; the processor fails runs left `running` on its first call, then `claim()`s the oldest queued run with `FOR UPDATE SKIP LOCKED` and `execute()`s it. `_execute_steps` interleaves flattening, script steps and `publish.publish`. `flatten(form_fields, submissions, out_dir)` takes a factory of submission dicts, so a sampled query can be passed in. `launcher.run_script(work_dir, kind, code, inputs, context, out_dir, limits)` prepares a step directory (`script.py`, `context.json`, `input/*.parquet`), runs `python -I -u -m tamiza.runner` with a clean environment and limits, and returns a `ScriptOutcome` with output Parquet paths. `runner.Context` has `project_name`, `form`, `log`. `names.reduce` / `deduplicate` / `is_valid_table_name` hold the naming rules; `interchange` reads and writes Parquet with pyarrow and describes column types.
- **api**: `ScriptEndpoints` (`MaxCodeBytes` = 200 KB, `ScriptKind` = `clean|output|indicators`), `RunEndpoints` (`RunDetails`), `RunQueue`. `SamplingFrameImport` already reads CSV/XLSX with MiniExcel, detects `;` vs `,`, and is mapped with `.DisableAntiforgery()` and a `RequestSizeLimit`. DbUp scripts `0001`–`0003`; `SchemaDriftTests` compares the EF model with them.
- **Database:** `raw_submissions` has an index on (`project_id`, `kobo_uuid`). Projects are never deleted today, but every project table cascades on `project_id`.
- **UI:** Scripts tab (`project-scripts.ts`) with the CodeMirror editor, versions, `pipeline-runs.ts`, `run-details-dialog.ts`, `pipeline-settings.ts`; `pipeline.service.ts`; tabs in `project-page.ts`; `shared/ui` components (card, table, badge, alert, confirm dialog, toast, form field). Polling uses a 5 s token pattern in the Data and Scripts sections.

Requirements are in `specs/pipeline`; motivation in `proposal.md`. User decisions for this change: a test runs the editor's draft without saving it; tests jump ahead of queued runs but wait for a running one.

## Goals / Non-Goals

**Goals:**

- A test of 500 submissions returns in seconds when the worker is idle, with the exact contract, validation and limits of a run, so a passing test predicts a passing run.
- Tests never touch published schemas, script versions or the run list.
- Reference data has one parser (the API) and one representation the worker turns into typed DataFrames.

**Non-Goals:**

- A second worker or concurrent execution (memory stays within `ANALYTICS_MEMORY_LIMIT`).
- Pushing test progress to the UI (polling is enough), cancelling tests.
- Querying reference tables with SQL from outside Tamiza, or from the dashboards.

## Decisions

### D1. Data model (DbUp `0004_add_pipeline_authoring.sql`)

| Table | Columns | Notes |
|---|---|---|
| `script_tests` | `id` (uuid v7), `project_id` (FK cascade), `requested_by` (FK users), `kind` (`clean`\|`output`\|`indicators`), `code` (text), `sample_size` (int, check 1–5,000), `status` (`queued`\|`running`\|`succeeded`\|`failed`), `clean_version_id` (FK `script_versions`, null), `queued_at`, `started_at`, `finished_at`, `error_code`, `error`, `steps` (jsonb, default `[]`) | Partial unique index (`project_id`, `requested_by`) where `status = 'queued'`; index (`status`, `queued_at`); index (`finished_at`). |
| `reference_tables` | `id` (uuid v7), `project_id` (FK cascade), `name` (varchar 63), `file_name` (varchar 255), `columns` (jsonb `[{name, header, type}]`), `row_count` (int), `created_at`, `updated_by` (FK users), `updated_at` | Unique (`project_id`, `name`); check `name ~ '^[a-z][a-z0-9_]{0,62}$'`. |
| `reference_table_contents` | `reference_table_id` (PK, FK cascade), `rows` (jsonb: array of arrays in column order) | Separate table so EF never loads rows with the metadata. |

`runs` gains `reference_tables` (jsonb, default `[]`): `[{name, rows, updatedAt}]`, written by the worker when the run starts.

*Alternatives:* storing reference tables as real tables in a `p_<id>_reference` schema created by the API (dynamic DDL from .NET in schemas `analytics` otherwise owns, and replace/rename logic in two languages); keeping the uploaded file and parsing it in the worker (no immediate feedback on bad files, and two parsers for the preview). Keeping test results in `runs`/`run_steps` would need a flag on every run query and different retention; a separate table is simpler.

### D2. Test lifecycle (api + worker)

- **Queue (api):** `POST /projects/{id}/script-tests` validates `kind`, non-empty `code` ≤ `MaxCodeBytes`, `sampleSize` 1–5,000 (default 500), then in one transaction deletes the requester's queued test in the project and inserts the new one. If the worker claims the old test between the two, the delete affects no row and the unique index (queued only) still admits the insert, so a running test is never touched.
- **Claim (worker):** `Processor.__call__` first claims the oldest queued test (`FOR UPDATE SKIP LOCKED`), resolving `clean_version_id` to the latest clean version for `output`/`indicators` tests; only when there is none does it claim a run. With one worker executing one job at a time, this gives "ahead of queued runs, behind the running job" without further coordination.
- **Startup:** `fail_interrupted` also marks `running` tests failed with `test.interrupted`.
- **Expiry:** each cycle deletes tests with `finished_at < now() - 24 h`; the API also filters on that bound so an expired test reads as 404 even if the worker is down.
- **Waiting reason (api):** for a queued test, `waitingFor` is `run` when a run is `running`, `test` when another test is `running` or queued earlier, else null (about to start). The worker is shared by every project, so this counts jobs of all projects.

### D3. Test execution (`tamiza/sample_runs.py`)

1. Sample: `SELECT data FROM tamiza.raw_submissions WHERE project_id = %s AND NOT deleted ORDER BY md5(kobo_uuid), kobo_id LIMIT %s`. Hash order spreads the sample across the project and is stable while the submissions do not change; with the (`project_id`, `kobo_uuid`) index, 500,000 rows sort to a top-N in about a second.
2. `flatten(form_fields, sampled, work_dir / "raw")`.
3. For `output`/`indicators`: run the resolved clean version with `run_script`, or pass the flattened tables when there is none or it was removed.
4. Run the draft with `run_script`.
5. After each step, append a step record with previews (D4) to `steps` and update the row, so the UI sees steps as they finish; finish with `succeeded` or `failed` and `error_code` `step.<name>` for the first failed step, as runs do.

The script context (`project_name`, form dictionary) and reference loading (D6) are extracted from `pipeline._execute_steps` into shared helpers so runs and tests build `ctx` identically. Nothing is published; the work directory is removed in a `finally`.

### D4. Preview (`tamiza/preview.py`)

For each output Parquet file the worker (trusted code; Parquet is data, never executed) reads the first batch of 100 rows with `pyarrow.parquet.ParquetFile.iter_batches` and builds `{name, rows, columns: [{name, type}], previewRows, preview: [[...]], cut: bool}`:

- **Types** use one vocabulary for previews and reference tables: `integer`, `decimal`, `boolean`, `date`, `timestamp`, `text` (other Arrow types → `text`).
- **Values:** integers beyond ±2^53 as strings (JavaScript precision), decimals as numbers (NaN/inf as null), dates `YYYY-MM-DD`, timestamps ISO 8601 UTC, text cut at 500 characters with `…` and the cell listed as cut.
- **Size:** if the serialized preview exceeds 1 MB, rows are dropped from the end until it fits and `previewRows` says how many remain. Only the first 20 tables of a step get columns and rows; the rest list name and row count.

Step logs are capped at 1 MB like run steps.

### D5. Reference upload (api, `Projects/ReferenceTableImport.cs`)

- The CSV/XLSX reading in `SamplingFrameImport` (`ReadTable`, `DetectSeparator`, cell helpers) moves to a shared `SpreadsheetReader`, used by both imports.
- **Header** = first non-empty row; empty header cells reduce to `column`. Names go through a .NET port of `names.reduce` and `deduplicate` (`Pipeline/ColumnNames.cs`): NFKD, drop non-ASCII, lowercase, `[^a-z0-9_]+` → `_`, collapse and trim trailing `_`, `c_` before a leading digit, cut at 56 + `_` + first 6 hex of SHA-1 of the UTF-8 original. Both suites read the same cases from `tamiza-analytics/tests/fixtures/column_names.json` (linked into the .NET test project) so the rules cannot drift.
- **Type detection** per column over non-empty cells: XLSX numeric cells are numbers; CSV/text cells are numbers when they match `^-?\d+(\.\d+)?([eE][+-]?\d+)?$` after replacing a single `,` with `.` in semicolon files, and do not match `^-?0\d` (codes like `05001`). Integer if every number is whole and within ±2^53 (XLSX stores doubles) or parses as `long` (CSV); decimal if every value is a number; date if every value is an XLSX date without time or `YYYY-MM-DD` text; otherwise text. Text keeps the trimmed cell text; XLSX date-times in text columns become `YYYY-MM-DD HH:mm:ss`.
- **Limits:** 10 MB (request limit 20 MB as for the sampling frame), at least one data row, at most 200 columns, at most 5,000,000 values, 50 tables per project. The value limit bounds memory: an XLSX file is compressed, so 10 MB can hold far more cells than a CSV, and the API runs within 512 MiB. Parsing streams the rows twice from the buffered file (types first, then the JSON rows) instead of holding every cell. Errors are validation problems on `file` or `name`; a duplicate name is 409 `reference.exists`.
- **Storage:** values in column order (`long`, `double`, `"YYYY-MM-DD"`, string, null) as `reference_table_contents.rows`; metadata and contents written in one transaction. Replace keeps `id`, `name`, `created_at` and updates the rest.
- The response includes `renamed: [{header, name}]` so the UI can report renamed columns.

### D6. Reference tables in scripts (worker + runner)

- `tamiza/reference.py`: `load(connection, project_id, out_dir)` streams each table's rows with a server-side cursor over `jsonb_array_elements(rows)` in batches of 5,000, builds typed Arrow arrays (`integer` → int64, `decimal` → float64, `date` → date32, `text` → string) and writes `out_dir/<name>.parquet`; it returns `[{name, rows, updatedAt, path}]`. Names come from the database but are re-checked with `names.is_valid_table_name` before becoming file names.
- Runs load reference tables once, right after claiming and before flattening, and store the summary in `runs.reference_tables`. Tests load them the same way at start.
- `run_script` gains a `reference: dict[str, Path]` argument and copies the files into `step_dir/reference/`, like the inputs: a hard link would let a script that writes to its copy change what later steps receive.
- `runner.ReferenceTables` is a read-only `Mapping[str, DataFrame]` that lists the files and reads each with `interchange.read_parquet` on first access (cached), so scripts pay only for the tables they use. A missing key raises `ReferenceTableNotFound` (a `KeyError` subclass whose message names the key and lists the available names). `Context` gains `reference`.

### D7. API surface

| Method and path | Minimum role | Purpose |
|---|---|---|
| `POST /projects/{id}/script-tests` | analyst | `{ kind, code, sampleSize? }` → 202 with the test; replaces the requester's queued test. |
| `GET /projects/{id}/script-tests/{testId}` | analyst, requester only | `{ id, kind, status, sampleSize, waitingFor, cleanVersion, queuedAt, startedAt, finishedAt, errorCode, error, steps }`; 404 for others or after expiry. |
| `GET /projects/{id}/reference-tables` | viewer | List with name, file name, rows, columns count, updated by and at. |
| `POST /projects/{id}/reference-tables` | analyst | Multipart `file`, `name` → 201 with the table and `renamed`; 409 `reference.exists`. |
| `PUT /projects/{id}/reference-tables/{name}` | analyst | Multipart `file` → 200 with the table and `renamed`; 404 if absent. |
| `DELETE /projects/{id}/reference-tables/{name}` | analyst | 204. |
| `GET /projects/{id}/reference-tables/{name}` | viewer | Columns with types and the first 100 rows (`jsonb_path_query_array(rows, '$[0 to 99]')`). |

`RunDetails` gains `referenceTables: [{ name, rows, updatedAt }]`. Route names are validated with the table-name rule before any query.

### D8. UI

- **Scripts tab:** for analysts and admins, a sample size field (default 500) and "Test on sample" next to Save, enabled whenever the editor has code. The results panel (`script-test-panel.ts`) sits below the editor card, full width, and polls the test every 2 s while it is queued or running ("Waiting for a run to finish", "Waiting for another test", "Running…"). Each step is a collapsible section with status, duration, rows, log in a scrollable `pre` and error; tables are chosen with a selector and shown in a preview table with column types in the header, inside its own horizontal scroll container so the page never scrolls sideways at 360 px. When the editor text differs from the tested code, the panel says so. The last test stays in component state only.
- **Run details dialog:** lists the reference tables the run used.
- **Reference data tab** (`projects/:id/reference`, every member), after Scripts: a table at `md`+ and cards below with name, file, rows, columns, updated by and at; for analysts and admins, Upload (dialog with file input and a name prefilled from the file name), Replace (file picker then confirmation), Delete (confirmation). Preview opens a dialog with columns, types and rows. After an upload, renamed columns are shown in an alert.
- New strings in `public/i18n/en.json`, including the error codes `reference.exists`, `test.interrupted`.

### D9. Test strategy

- **Python unit:** `column_names.json` cases for `names.reduce`/`deduplicate`; preview building (types, big integers, cut text, 1 MB row trimming, 20-table cap); `ReferenceTables` mapping (lazy load, unknown key message, empty project); runner with `ctx.reference`.
- **Python integration** (existing testcontainers setup): test of script 1 with a draft, nothing published and no version; test of script 2 using the saved clean version; script 1 failing in a test of script 3; stable sample and small project; tests claimed before queued runs; interrupted test; expiry; reference tables loaded with types into a run and recorded in `runs.reference_tables`; replacement during a run not seen by that run.
- **API:** `ColumnNames` against the shared cases; reference import (headers, types, decimal comma, leading zeros, XLSX dates, limits, errors); reference endpoints (roles, 409, replace, delete, preview, 50-table limit); script-test endpoints (roles, validation, replacement of the queued test, requester-only, expiry, `waitingFor`); run details with reference tables; `SchemaDriftTests`.
- **UI:** test action hidden for viewers, polling and waiting messages, step and table preview rendering, stale-draft note; reference tab per role, upload with renamed columns, 409 handling, replace and delete confirmations, preview dialog.
- **Stack verification:** test of each sample script from `docs/scripts.md` on 500 of the 500,000 fake submissions (time to result with an idle worker); a test queued during a full run starts after it and before a queued run; an indicator script using an uploaded population table; screenshots at 360 and 1280 px.

## Risks / Trade-offs

- [A test waits up to a full run's duration (minutes) when a run is running] → accepted by the user; the panel says what it waits for; runs are infrequent (after synchronizations or on schedule).
- [Draft code is stored in `script_tests` for up to 24 hours and runs like saved code] → same trust model as saved scripts: only analysts and admins can start tests; the code is visible only to its author through the API and expires.
- [Sorting by `md5(kobo_uuid)` reads every non-deleted submission of the project for each test] → about a second at 500,000 submissions; measured in stack verification; a stored sample key can be added later without changing behavior.
- [Type detection surprises (a column with one stray text value becomes text)] → the upload shows each column's type and the preview; documented in `docs/scripts.md`; scripts can convert types themselves.
- [Reference rows as JSONB are larger than the file and parsed in the worker] → 10 MB limit; the worker streams rows in batches into Parquet instead of loading the whole array.
- [Two implementations of the naming rule] → shared test cases run by both suites.
- [A large test result in `steps`] → preview caps (100 rows, 1 MB per table, 20 tables, 500 characters per value) and 1 MB logs.

## Verification Results

Measured on the development stack (Compose; `analytics` limit 1536 MiB, worker polling every 5 s) against the 500,000-submission fake Kobo project with the three sample scripts from `docs/scripts.md`, each tested on 500 submissions with an idle worker:

| Tested script | Steps (duration, rows in → out) | Queued → finished |
|---|---|---|
| 1. clean | flatten 0.7 s (500 → 1,507), clean 0.5 s (1,507 → 1,478) | 2.5 s |
| 2. output | flatten 1.6 s, saved clean 0.5 s, output draft 0.5 s (1,478 → 489) | 7.7 s |
| 3. indicators | flatten 0.5 s, saved clean 0.5 s, indicators draft 0.5 s (1,478 → 6) | 1.9 s |

The sample query (`ORDER BY md5(kobo_uuid), kobo_id LIMIT 500` over 500,000 submissions) took 340–380 ms. The time to result is dominated by the worker's polling interval (up to 5 s before an idle worker claims the test), not by the test itself.

Also verified on the stack:

- A test started while a full run of the 500,000-submission project was running reported `waitingFor = run`, started 4 ms after that run finished, and finished before a run of another project that had been queued before the test.
- A semicolon-separated CSV with the headers `Municipality;DANE code;Households 2026;Avg size` and decimal commas uploaded as `municipality` (text), `dane_code` and `households_2026` (integer) and `avg_size` (decimal), with the four renamings reported. An indicators script dividing by `ctx.reference["population"]["households_2026"]` published the expected values, and the run details listed `population` with its 5 rows and update time.

## Migration Plan

- `0004_add_pipeline_authoring.sql` creates the three tables and adds `runs.reference_tables` with default `[]`; existing runs show no reference tables.
- No new configuration. Images deploy together; an old worker ignores `script_tests` (tests stay queued until the new worker starts), and an old API ignores the new tables.
- Rollback: previous images ignore the new tables and column; they can be dropped by hand.

## Open Questions

- Whether test results should survive a page reload (for example the user's latest test per script). Not required by the specs; the API already keeps them for 24 hours, so this is a UI-only addition.
