# Tasks

## 1. Schema and form snapshot (api)

- [x] 1.1 Add `src/dbUp/Scripts/0003_add_pipeline.sql` with `script_versions`, `runs`, `run_steps` and the `projects` columns `run_after_sync`, `run_interval_hours`, `next_run_at`, `form_choices` (design D1), and the matching entities and mapping in `TamizaDbContext`; verify `SchemaDriftTests` and `SchemaMigratorTests` pass, including a database at `0002` with existing projects upgraded to `run_after_sync = true`
- [x] 1.2 Parse `content.choices` and each field's choice list (`select_from_list_name`, or the word after `select_one`/`select_multiple`) in `KoboForm.Parse`, store them through `FormSnapshot.Apply` in `form_choices` and `form_fields[].listName`; verify unit tests with both type forms, labels per translation and fields without a list, and an integration test that creating a project and re-checking store the choices

## 2. Data interchange (analytics)

- [x] 2.1 Spike first: write a unit test module proving that pandas 3 DataFrames with `str`, nullable `Int64`, `float64`, `bool`, `datetime64[ns, UTC]`, naive datetimes, `date` objects, categoricals and mixed object columns round-trip through DuckDB to Parquet and back without pyarrow; implement `tamiza.interchange` (`write_parquet(df, path)`, `read_parquet(path) -> DataFrame`, dtype normalization) until it passes; if a dtype cannot round-trip, record the conversion in the design before continuing
- [x] 2.2 Implement the naming rule (`tamiza.names`: reduce, de-duplicate with `_2`, truncate with hash, validate table names, quote with `psycopg.sql.Identifier`); verify unit tests for collisions, 63-byte limit with multibyte characters, leading digits, reserved words and unchanged valid names

## 3. Flattening (analytics)

- [x] 3.1 Implement type conversion per question type (design D3) with per-column failure counters; verify unit tests for every listed type, bad values becoming null with the counter incremented, and unknown types as text
- [x] 3.2 Implement `tamiza.flatten`: stream submissions in batches, split repeat groups (nested included) into tables with `_row_id`, `_parent_id`, `_parent_table`, `_index`, keep system columns, drop the listed Kobo keys, resolve column names from the form snapshot plus data, and write one Parquet file per table incrementally; verify unit tests with fixture submissions for groups, a repeat answered 3 times, a nested repeat, a question no longer in the form, colliding names (`spouse_age`) and a log listing unreadable values

## 4. Script runner (analytics)

- [x] 4.1 Implement `tamiza.runner` (child entry point): load input Parquet, build `ctx` (`form.questions`, `form.choices`, `form.label`, `log`, `project_name`), execute `script.py` in a fresh namespace, call the contract function, validate the return type, convert geometry to WKT and unsupported objects to text, write outputs and `result.json` with a traceback trimmed to `script.py` frames; verify unit tests running the runner in-process for each contract, missing function, wrong return type, exception with line number, `ctx.log` and `print` order in the log, and `ctx.form.label`
- [x] 4.2 Implement the child process launcher with the clean environment, working directory, wall-clock timeout and RSS watchdog that kills the process group (design D5); verify tests: a sleeping script fails with `limit.time`, an allocating script with `limit.memory`, a script printing `os.environ` shows no `PG*` or `TAMIZA_*` variable, files written by the script are gone after the step, `import requests_not_installed` fails with `ModuleNotFoundError` in the log
- [x] 4.3 Implement output validation (design D6): at least one table for scripts 1 and 2, strict table names, normalized column names with renamings logged, collisions rejected, the five indicator columns; verify unit tests for each rule and the messages naming the table or column

## 5. Publishing and runs (analytics)

- [x] 5.1 Add `testcontainers` (development group) and a pytest fixture that starts PostGIS, applies `tamiza-api/src/dbUp/Scripts/*.sql` in order and seeds a project with raw submissions; verify a smoke test creates the schema and reads the seeded rows
- [x] 5.2 Implement `tamiza.publish`: create `p_<id>_<step>__next`, create tables from Parquet types, `COPY` rows in batches, then drop and rename in one transaction; drop the schema for absent scripts 2 and 3; verify integration tests: tables and types match, a reader in another connection sees the old or the new tables but never a mix, a failed load leaves the published schema untouched, names are quoted
- [x] 5.3 Implement the run lifecycle in `worker.py` (design D4): mark interrupted runs on startup, claim with `FOR UPDATE SKIP LOCKED` resolving script versions, execute flatten → clean → output and indicators, write `run_steps` with timings, rows, tables, log (capped at 1 MB) and error, finish the run, remove the working directory, keep the heartbeat alive from a thread during long runs; verify integration tests: full run with three scripts, no scripts (clean = raw, 2 and 3 skipped and their schemas dropped), script 1 failing (previous clean kept, 2 and 3 skipped), script 2 failing with indicators published, missing indicator column, versions recorded at claim time even if a newer version is saved during the run, an interrupted run marked failed at startup, deleted submissions excluded
- [x] 5.4 Add `TAMIZA_RUN_TIMEOUT_SECONDS` and `TAMIZA_RUN_MEMORY_MB` to `settings.py`, `compose.yaml` and `.env.example`; verify settings unit tests (defaults, invalid values) and `scripts/check-env-example.sh`

## 6. API

- [x] 6.1 Implement the script endpoints (design D8): current scripts, save, remove, versions list, one version, restore, with roles and the 200 KB limit; verify integration tests: numbering per kind, viewer 403 on save, remove creates a removed version, restore copies code into a new number, oversized code 400, non-member 404
- [x] 6.2 Implement the run endpoints: start (analyst, 202, 409 `run.already_queued`), list with step summaries and paging, details with logs; verify integration tests for roles, the 409, a queued run while another is running being allowed, and details showing each step
- [x] 6.3 Implement pipeline settings (`GET`/`PATCH /pipeline`, admin, 1–168 or null) and the triggers: `SyncRunner` queues a `sync` run after a successful synchronization with changes when `run_after_sync` is on; the scheduler tick queues `schedule` runs when `next_run_at` is due and moves it; verify integration tests: run queued after a sync with changes, none without changes or with the setting off, none from a webhook, schedule due time from the last run start, analyst `PATCH` 403, out-of-range 400
- [x] 6.4 Check the OpenAPI document lists the new operations; verify in an integration test by operation ID

## 7. UI

- [x] 7.1 Add CodeMirror 6 (`codemirror`, `@codemirror/lang-python`) and a `tmz-code-editor` component (value input, change output, read-only mode, Tailwind-themed); verify a unit test that typing emits the new value and read-only blocks edits, and that `npm run build` keeps the editor out of the initial bundle
- [x] 7.2 Add `core/projects/pipeline.service.ts` and the `scripts` route and tab; build the script selector, editor, save, remove with confirmation, `.py` upload, contract template and version history with restore, plus the unsaved-changes confirmation; verify unit tests per role, save and restore requests, the template text for each kind and the confirmation when switching scripts with unsaved edits
- [x] 7.3 Build "Run now", the run list (table and cards), the run details dialog with each step's status, duration, rows, tables, log and error, polling while a run is active, and the admin settings form; verify unit tests for the 409 message, each step status, polling start and stop, and settings errors
- [x] 7.4 Add `en.json` keys for every new text and error code (`run.already_queued`, `run.interrupted`, `limit.time`, `limit.memory`, `step.*`) and extend the error-code translation test; verify the test lists every code the API and worker record
- [x] 7.5 Verify the Scripts section and run details at 360 px and 1280 px in headless Chrome (no horizontal page scroll; the editor and logs scroll inside their containers) with screenshots

## 8. Documentation

- [x] 8.1 Write `docs/scripts.md`: the three contracts with complete examples, `ctx`, how raw tables and columns are named (repeat groups, parent keys, collisions), default behavior without scripts, triggers and settings, limits, what isolation does and does not protect, and reading the run log; link it from `docs/kobo.md` and the README
- [x] 8.2 Update `docs/operations.md` (run limit variables and their relation to `ANALYTICS_MEMORY_LIMIT`, per-project schemas and how to drop them), README status, and `docs/tamiza-brief.md` section 8 with `add-pipeline-authoring` after this change; verify links resolve and `scripts/check-env-example.sh` passes

## 9. Stack verification

- [x] 9.1 On the Compose stack with a fake Kobo serving submissions with a repeat group: sync, then a run with three sample scripts from `docs/scripts.md`; check each `p_<id>_*` schema with `psql`, a failing script 3 keeps the previous indicators, and a run queued automatically after a sync with changes
- [x] 9.2 Run the chain on 500,000 submissions; record per-step durations and peak `analytics` memory against its 1536 MiB limit in the design, and adjust the default limits if needed
