# Design

## Context

Builds on `add-kobo-ingestion` (main specs `ingestion`, `projects`). Relevant current state:

- **analytics** (`tamiza-analytics/src/tamiza`): `worker.py` only runs `SELECT 1` and touches a heartbeat file; `settings.py` reads `TAMIZA_ANALYTICS_DATABASE_URL` (libpq keywords, password in `PGPASSWORD`) and `TAMIZA_WORKER_POLL_SECONDS`; `logs.py` writes JSON lines. Unit tests use fakes; there are no database tests yet. The image runs as uid 10001 with a root-owned venv at `/opt/venv` and no `pip`. Installed: pandas 3.0.6, numpy, polars, duckdb 1.5.6, geopandas (with shapely and pyogrio), openpyxl, psycopg 3. pyarrow was not installed; this change adds it (D5). The container's memory limit defaults to 1536 MiB.
- **api**: owns schema `tamiza` through DbUp scripts (`0001`, `0002`). `IngestionScheduler` ticks every 30 s and runs `SyncRunner`; `SyncQueue` uses a partial unique index for one active run per project. `ProjectAccess`/`RequireProjectRole` give 404/403. `KoboForm.Parse` keeps `{name, xpath, type, label}` per survey row with `type` spaces turned into underscores (`select_one municipalities` → `select_one_municipalities`); choice lists are not read.
- **raw_submissions** (schema `tamiza`): `data` jsonb as Kobo sends it. Group questions appear as keys with their path (`respondent/age`); a repeat group is a key holding an array of objects whose keys carry the full path (`members/name`).
- **UI**: Angular 22, Tailwind/Flowbite components in `shared/ui`, project page tabs (`overview`, `data`, `members`, `sampling-frame`, `webhook`). CSP allows inline styles, not inline scripts.

Requirements are in `specs/pipeline` and `specs/projects`; motivation in `proposal.md`. User decisions for this change: split the pipeline in two (this change, then `add-pipeline-authoring`), CodeMirror 6 editor, run after synchronization on by default.

## Goals / Non-Goals

**Goals:**

- A worker that runs the whole chain for 500,000 submissions inside the analytics container's memory, publishing atomically.
- User code never runs in the worker process and never sees database credentials.
- Tables and names stable enough that the dashboard changes can read `p_<id>_*` schemas directly.

**Non-Goals:**

- Sample test run with preview, reference tables (`add-pipeline-authoring`).
- Reading published tables through the API (the dashboard changes add those endpoints).
- Cancelling runs, several runs at once, LISTEN/NOTIFY wake-ups (polling every 5 s is enough).
- PostGIS geometry columns, a security sandbox, syntax checking on save (the API is .NET; errors appear in the run log).

## Decisions

### D1. Data model (DbUp `0003_add_pipeline.sql`)

| Table | Columns | Notes |
|---|---|---|
| `script_versions` | `id` (uuid v7), `project_id` (FK cascade), `kind` (`clean`\|`output`\|`indicators`), `number` (int, per project and kind, from 1), `code` (text, null = removed), `author_id` (FK users), `created_at` | Unique (`project_id`, `kind`, `number`). Rows are never updated. |
| `runs` | `id` (uuid v7), `project_id` (FK cascade), `trigger` (`manual`\|`sync`\|`schedule`), `status` (`queued`\|`running`\|`succeeded`\|`failed`), `requested_by` (FK users, null), `clean_version_id`, `output_version_id`, `indicators_version_id` (FK `script_versions`, null), `queued_at`, `started_at`, `finished_at`, `error_code`, `error` | Partial unique index on (`project_id`) where `status = 'queued'` (one queued per project); index (`project_id`, `queued_at` desc). |
| `run_steps` | `run_id` (FK cascade), `step` (`flatten`\|`clean`\|`output`\|`indicators`), `status` (`succeeded`\|`failed`\|`skipped`), `started_at`, `finished_at`, `input_rows`, `output_rows` (bigint, null), `tables` (jsonb: `[{name, rows}]`), `log` (text), `error` (text) | PK (`run_id`, `step`). |

`projects` gains `run_after_sync` (bool, default true), `run_interval_hours` (int null, check 1–168), `next_run_at` (timestamptz null) and `form_choices` (jsonb, default `[]`). `form_fields` entries gain `listName` (null for non-select questions).

`kind` uses names rather than 1/2/3 so the log and API read clearly; the UI still shows "1. Clean", "2. Output", "3. Indicators". The versions a run uses are resolved and stored when the worker claims it (D4), so a save during the run does not affect it.

### D2. Form snapshot with choices

`KoboForm.Parse` also reads `content.choices` (`[{list_name, name, label: [..]}]`) into `form_choices: [{listName, name, label}]`, and sets each field's `listName` from `select_from_list_name`, or from the second word of `select_one x` / `select_multiple x` when that is how the type is written. The stored field `type` keeps today's underscore form so the field check and existing data stay valid. `FormSnapshot.Apply` writes both columns. Existing projects get choices on their next synchronization or re-check.

### D3. Flattening

Runs in the worker process (trusted code), streaming `SELECT data FROM tamiza.raw_submissions WHERE project_id = %s AND NOT deleted ORDER BY kobo_id` with a server-side cursor in batches of 2,000.

- **Table layout** comes from the form snapshot plus the data: a repeat group is any key whose value is a list of objects. Its table name is the last path segment of the key; nested repeats nest. Each object's keys become columns of that table.
- **Columns:** the last path segment of the key (question name). If two keys in one table reduce to the same name, the later one uses its full path (`spouse/age` → `spouse_age`). Names are lowercased, non `[a-z0-9_]` characters become `_`, a leading digit gets a `c_` prefix, and anything over 63 bytes is cut to 56 plus `_` and a 6-character hash. The same rule de-duplicates with `_2`, `_3`.
- **System columns:** main table `_id` (bigint), `_uuid`, `_submission_time` (timestamptz), `_version`; repeat tables `_row_id` (bigint, sequential within the run), `_parent_id` (the parent's `_id` or `_row_id`), `_parent_table`, `_index` (1-based position). Other Kobo system keys (`_attachments`, `_geolocation`, `_notes`, `_tags`, `_validation_status`, `_status`, `_submitted_by`, `formhub/uuid`, `meta/*`) are dropped, except `meta/instanceID` → `_instance_id`.
- **Types** from the field's type in the snapshot: `integer` → bigint; `decimal`, `range` → double precision; `date`, `today` → date; `start`, `end`, `dateTime` → timestamptz; everything else text (including `select_multiple`, `time`, `geopoint`, `calculate`). A failed conversion stores null and increments a per-column counter written to the step log ("age: 3 values could not be read as integer"). Keys absent from the snapshot are text.
- **Output:** each table is accumulated as column lists per batch and written to Parquet in the run's working directory with DuckDB (`duckdb.from_df` on a pandas DataFrame per batch, appended through a DuckDB table and `COPY ... TO 'main.parquet'`). Parquet is both the input of script 1 and the source for publishing `p_<id>_raw` (D6).

*Alternatives:* flattening in SQL with `jsonb_to_recordset` (cannot handle per-form column sets and nested repeats generically); in .NET (the brief assigns flattening to `analytics`, next to pandas).

### D4. Run lifecycle in the worker

`worker.py` keeps its loop and heartbeat and adds, per cycle:

1. On startup: `UPDATE tamiza.runs SET status='failed', error_code='run.interrupted' ... WHERE status='running'` (one worker, as one `api`).
2. Claim: `UPDATE runs SET status='running', started_at=now(), clean_version_id=(latest clean version), ... WHERE id = (SELECT id FROM runs WHERE status='queued' ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING ...`. A removed latest version (null code) resolves to that version, meaning "no script".
3. Execute the steps (D5) in a fresh working directory `/tmp/tamiza-runs/<run id>/`, writing each `run_steps` row as the step ends.
4. Finish: `succeeded` if no step failed, else `failed` with the first failure's step as `error_code` (`step.clean`, ...). The directory is removed in a `finally`.

One run at a time per worker. The heartbeat is touched by a background thread during long runs so the healthcheck stays green.

### D5. Script execution in a child process

For each script step the worker:

1. Writes the input tables as Parquet (`input/<table>.parquet`), the form dictionary as JSON, and the script as `script.py` into a step directory.
2. Starts `python -I -m tamiza.runner <step dir> <kind>` with `cwd` = step directory and `env` = only `PATH`, `HOME` (step dir), `LANG`, `TZ=UTC`, `OMP_NUM_THREADS=2`, `POLARS_MAX_THREADS=2`. No `PG*` or `TAMIZA_*` variables. `stdout`/`stderr` go to a log file.
3. Watches it: wall clock against `TAMIZA_RUN_TIMEOUT_SECONDS` (default 600) and resident memory read from `/proc/<pid>/status` (`VmRSS`, children included through `/proc/<pid>/task/*/children`) every 250 ms against `TAMIZA_RUN_MEMORY_MB` (default 1024). On breach it kills the process group and fails the step with code `limit.time` or `limit.memory`.
4. Reads `result.json` (status, error, traceback, output tables) and the output Parquet files.

`tamiza.runner` (trusted code, inside the child): loads input Parquet with DuckDB into pandas (`duckdb.read_parquet(...).df()`), builds `ctx`, `exec`s the script in a fresh module namespace, calls the contract function, validates the return type, converts geometry columns to WKT (`shapely.to_wkt`) and other unsupported object columns to text, and writes each DataFrame to Parquet through DuckDB. Tracebacks are trimmed to frames from `script.py`.

**`ctx`:** `ctx.form.questions` (DataFrame: `name`, `path`, `type`, `label`, `list_name`), `ctx.form.choices` (DataFrame: `list_name`, `name`, `label`), `ctx.form.label(question, value)` (label lookup, `None` if unknown), `ctx.log` (a `logging.Logger` writing `LEVEL message` lines to the step log), `ctx.project_name`. `add-pipeline-authoring` adds `ctx.reference`.

**Why Parquet:** user output must not be unpickled in the worker (that would run the user's code there), and CSV loses types.

**pyarrow (added after task 9.2).** The design first relied on DuckDB alone because pyarrow was not in the image. Stack verification showed that without pyarrow pandas 3 keeps each text value as a Python object: the `main` table of 500,000 submissions took 798 MiB instead of 65 MiB, and the sample clean script exceeded the 1024 MB limit while loading. With the user's approval, `pyarrow==25.0.1` joins the pinned dependencies (image +150 MB). `interchange` now reads and writes Parquet with pyarrow: reading converts column by column and frees the Arrow buffers (peak 514 instead of 725 MiB), and writing shares the frame's Arrow-backed text instead of copying it into DuckDB (the output write had peaked at 1,428 MiB). DuckDB still flattens, describes Parquet types for publishing and streams rows to PostgreSQL. Scripts can also use pyarrow.

**Round-trip behavior (pandas 3.0.6, pyarrow 25.0.1; task 2.1, updated after 9.2).** Text (Arrow-backed `str`), floats and time-zone-aware and naive datetimes round-trip unchanged. Integer and boolean columns arrive as pandas' nullable `Int64` and `boolean`, so a missing value never turns IDs into floats. `datetime.date` objects come back as `datetime64` (stored as `DATE` in Parquet and PostgreSQL); categoricals come back as text; object columns of mixed types become text. Fetching time-zone-aware timestamps as Python objects needs `pytz`, which is not installed, so `interchange.iter_rows` converts them to naive UTC inside DuckDB before the database load. A named index (for example after `groupby`) becomes columns; an unnamed one is dropped.

*Alternatives:* `multiprocessing` with pickled DataFrames (unpickling untrusted data in the worker); `RLIMIT_AS` for memory (numpy and DuckDB reserve large virtual ranges and fail spuriously); a separate container per run (needs the Docker socket, out of scope).

### D6. Publishing

Schema names: `p_<project id as 32 hex>_raw|clean|output|indicators` (at most 45 characters), built only from the UUID. Each step publishes in its own transaction:

```sql
DROP SCHEMA IF EXISTS "p_<id>_<step>__next" CASCADE;
CREATE SCHEMA "p_<id>_<step>__next";
-- CREATE TABLE ... ; COPY ... FROM STDIN for each table (from Parquet, in batches)
-- then, in one transaction:
DROP SCHEMA IF EXISTS "p_<id>_<step>" CASCADE;
ALTER SCHEMA "p_<id>_<step>__next" RENAME TO "p_<id>_<step>";
```

Loading happens before the swap transaction, so the lock is held only for the drop and rename. Column types map from Parquet/DuckDB types: integers → bigint, floats → double precision, booleans → boolean, dates → date, timestamps → timestamptz (naive treated as UTC), decimals → numeric, everything else → text. Rows go through `psycopg` `COPY ... FROM STDIN` with `write_row`. A step whose script is absent drops its published schema (output, indicators) or publishes the flattened tables (clean). Identifiers are always validated by D3's rule and quoted with `psycopg.sql.Identifier`.

**Validation** before publishing: at least one table for scripts 1 and 2; table names must already satisfy the rule (`^[a-z][a-z0-9_]{0,62}$`), since analysts choose them deliberately and dashboards select them by name; column names are normalized with D3's rule (`Household size` → `household_size`) and the step fails only if two columns of a table normalize to the same name; script 3 has the five required columns after normalization. The script 3 DataFrame is published as table `indicators`. The step log lists any column that was renamed.

### D7. Triggers and queue (api)

- **Manual:** `POST /projects/{id}/runs` (analyst) inserts a queued run. When the project already has a queued run, the partial unique index rejects the insert and the API answers 409 `run.already_queued`, so the UI can say a run is already waiting. Automatic triggers use `ON CONFLICT DO NOTHING` instead.
- **After synchronization:** `SyncRunner`, on success with created + updated + deleted > 0 and `run_after_sync`, inserts a queued run with trigger `sync` (`ON CONFLICT DO NOTHING`). Because only queued runs are unique, a running run plus one queued follow-up is allowed.
- **Schedule:** the `IngestionScheduler` tick also queues runs for projects with `run_interval_hours` set and `next_run_at <= now`, then sets `next_run_at = now + interval`. Setting the schedule computes `next_run_at` from the last run's `started_at` (or now).
- The webhook path never touches `runs`.

### D8. API surface

| Method and path | Minimum role | Purpose |
|---|---|---|
| `GET /projects/{id}/scripts` | viewer | Current version of each kind: `{ kind, versionId, number, code, removed, author, createdAt }`. |
| `PUT /projects/{id}/scripts/{kind}` | analyst | `{ code }` → new version (201). Empty or null code is rejected; ≤ 200 KB. |
| `DELETE /projects/{id}/scripts/{kind}` | analyst | New version marked removed. |
| `GET /projects/{id}/scripts/{kind}/versions` | viewer | Versions newest first, without code. |
| `GET /projects/{id}/scripts/{kind}/versions/{number}` | viewer | One version with code. |
| `POST /projects/{id}/scripts/{kind}/versions/{number}/restore` | analyst | Saves that code as a new version. |
| `POST /projects/{id}/runs` | analyst | Queue a manual run → 202; 409 `run.already_queued`. |
| `GET /projects/{id}/runs?page=&pageSize=` | viewer | Runs newest first with step summaries. |
| `GET /projects/{id}/runs/{runId}` | viewer | Run with every step, log and error. |
| `GET` / `PATCH /projects/{id}/pipeline` | viewer / admin | `{ runAfterSync, runIntervalHours, nextRunAt }`. |

Logs are capped at 1 MB per step (the worker keeps the last 1 MB and notes the truncation).

### D9. UI

- **Scripts tab** (`projects/:id/scripts`, every member), after Data.
  - Selector for the three scripts with status badges (current version, "not set", "removed").
  - CodeMirror 6 editor (`codemirror`, `@codemirror/lang-python`), read-only for viewers, wrapped in a `tmz-code-editor` component (ControlValueAccessor-free: `value` input, `changed` output). It is lazily loaded with the route, so it does not grow the initial bundle.
  - Save, remove (confirmation), upload a `.py` file into the editor, and a template button that inserts the contract function with a docstring.
  - Version history in a side panel (`lg`) or below (phones); selecting a version shows its code read-only with "Restore".
  - Unsaved changes trigger a confirmation when switching script or leaving the route.
- **Runs** on the same tab: "Run now" (analyst+), the run list (table at `md`+, cards below), and a run details dialog with one section per step: status, duration, rows, tables, log in a scrollable `pre`, error. Polls every 5 s while a run is queued or running (same token pattern as the Data section).
- **Settings** (admin): run after synchronization toggle, schedule hours or none.

### D10. Configuration

`TAMIZA_RUN_TIMEOUT_SECONDS` (default 600) and `TAMIZA_RUN_MEMORY_MB` (default 1024) are read by `analytics`, added to `compose.yaml` and `.env.example`. The default memory limit plus the worker leaves headroom inside `ANALYTICS_MEMORY_LIMIT` (1536 MiB); `docs/operations.md` explains raising both together.

### D11. Test strategy

- **Python unit tests:** naming rule (collisions, long names, digits, unicode), type conversion per question type, flattening of fixture submissions (groups, repeats, nested repeats, removed questions, deleted rows excluded), result validation, `ctx.form.label`, limit watchdog with a script that sleeps or allocates.
- **Python integration tests** (`testcontainers` with `postgis/postgis:17-3.5`, schema created by applying `tamiza-api/src/dbUp/Scripts/*.sql` in order): a full run with all three scripts; no scripts; script 1 failing (previous publication kept, 2 and 3 skipped); script 2 failing with 3 published; missing indicator column; time and memory limits; environment without `PG*`; interrupted run on startup; versions resolved at claim time; 20,000-submission flatten under a memory budget.
- **API tests:** script endpoints and versions (roles, size limit, restore numbering), run endpoints (roles, 409, paging, details), pipeline settings, run queued after a sync with changes and not without, schedule due time, choice parsing (`select_from_list_name` and `select_one x` forms), `SchemaDriftTests`.
- **UI tests:** editor read-only for viewers, save/remove/restore, unsaved-change confirmation, run now and polling, run details rendering for each step status, settings form per role.
- **Stack verification:** fake Kobo with 500,000 submissions including a repeat group → full run with three sample scripts; record durations and peak memory of `analytics`; check that dashboards-to-be can `SELECT` from each published schema; screenshots at 360 and 1280 px.

## Risks / Trade-offs

- [User code runs as the same OS user as the worker and could read the worker's environment through `/proc` or reach the database host] → not a sandbox by design (brief §4); only analysts and admins save code; `harden-deployment` adds a least-privilege database role and no Internet for `analytics`. Documented in `docs/scripts.md` and the spec.
- [Type conversions between pandas and Parquet surprise analysts (nullable integers, categoricals, mixed objects)] → fixed, documented mapping (D5); unit tests on each dtype.
- [Scripts on large forms near the memory limit] → measured on 500,000 submissions (see Verification Results); text uses Arrow memory; limits are configurable.
- [Memory: flattening 500,000 submissions plus a script's DataFrames inside 1536 MiB] → flattening streams in batches and writes Parquet incrementally; the script runs in a separate process after the worker releases its data; limits are configurable and documented; measured in stack verification.
- [RSS polling misses a spike shorter than 250 ms] → the container limit remains the last line; an OOM-killed child is reported as `limit.memory` when its exit signal is SIGKILL.
- [Column names change when a form adds a question with a colliding name] → collisions resolve deterministically by order in the form; documented; dashboards key on names.
- [Schema swap blocks readers briefly] → only the drop and rename run inside the swap transaction.
- [A large `run_steps.log`] → capped at 1 MB per step.

## Verification Results

Measured on the development stack (Compose; `analytics` limit 1536 MiB, script limit 1024 MB) with a fake Kobo serving 500,000 submissions with a `members` repeat group (about 1.2 KB each), running the three sample scripts from `docs/scripts.md`:

| Step | Rows in → out | Duration |
|---|---|---|
| flatten | 500,000 → 1,500,001 (`main` 500,000, `members` 1,000,001) | 96.9 s |
| clean (drops 10,000 test submissions) | 1,500,001 → 1,470,000 | 11.0 s |
| output | 1,470,000 → 490,000 | 2.8 s |
| indicators | 1,470,000 → 6 | 2.9 s |

The `analytics` container (worker plus script process) peaked at 1,024 MiB; the clean script process alone at about 870 MiB. The default limits (600 s, 1024 MB) stay. Also verified on the stack: a run queued after the project's first synchronization and after a later one with changes; a failing script 3 keeps the previous indicators and shows the traceback; Scripts section and run details without horizontal scroll at 360 and 1280 px.

## Migration Plan

- `0003_add_pipeline.sql` creates the three tables and the project columns; existing projects get `run_after_sync = true` and no schedule. Nothing runs until the next synchronization with changes or a manual run.
- The analytics image changes its entrypoint behavior only (same command); `compose.yaml` passes the two new variables.
- Rollback: previous images ignore the new tables; per-project schemas can be dropped with `DROP SCHEMA ... CASCADE` (documented).

## Open Questions

- Whether to keep more than the last published version of each schema for comparison. Not needed by the dashboards; can be added on the same naming scheme.
