# Tasks

## 1. Schema and naming rule

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0004_add_pipeline_authoring.sql` with `script_tests`, `reference_tables`, `reference_table_contents` and `runs.reference_tables` (design D1), plus the matching entities and mapping in `TamizaDbContext`; verify `SchemaDriftTests` and `SchemaMigratorTests` pass, including a database at `0003` with existing runs upgraded to `reference_tables = []`
- [x] 1.2 Add `tamiza-analytics/tests/fixtures/column_names.json` with naming cases (accents, punctuation, trailing underscores, leading digits, empty names, names over 63 characters with multibyte characters, duplicates for `deduplicate`) and make `test_names.py` read it; verify the Python tests pass against the existing `names.reduce`/`deduplicate`
- [x] 1.3 Port the rule to `Tamiza.Api/Pipeline/ColumnNames.cs` (`Reduce`, `Deduplicate`, `IsValidTableName`) and link the fixture into `Tamiza.Api.UnitTests`; verify a unit test runs every case from the shared file

## 2. Reference tables (api)

- [x] 2.1 Move the CSV/XLSX reading of `SamplingFrameImport` (`ReadTable`, `DetectSeparator`, cell helpers) into a shared `SpreadsheetReader` without changing behavior; verify the existing sampling-frame unit and integration tests still pass
- [x] 2.2 Implement `ReferenceTableImport` (design D5): header row, reduced and de-duplicated column names with the renamed list, type detection (integer, decimal, date, text; decimal comma in semicolon files; leading-zero codes as text; XLSX dates and date-times), at least one data row, at most 200 columns, typed row values; verify unit tests for each scenario in the `Reference table file reading` requirement plus XLSX numeric and date cells, empty cells and an empty header cell
- [x] 2.3 Implement the reference endpoints (design D7): list, upload (`409 reference.exists`, 50-table limit, 10 MB limit, name rule), replace, delete and preview of 100 rows, with roles; verify integration tests: viewer 403 on upload, replace and delete, viewer can list and preview, duplicate name 409 leaves the table unchanged, replace keeps the name and updates who and when, `.ods` and oversized files rejected, header-only file rejected, non-member 404

## 3. Script tests (api)

- [x] 3.1 Implement `POST /script-tests` and `GET /script-tests/{testId}` (design D2, D7): validation of kind, code size and sample size 1–5,000 with default 500, replacement of the requester's queued test in one transaction, requester-only reads, 404 after 24 hours, `waitingFor` from running runs and earlier tests of any project; verify integration tests for each, including viewer 403, sample size 10,000 rejected, and that queuing a test creates no script version and no run
- [x] 3.2 Add `referenceTables` to `RunDetails` and check the OpenAPI document lists the new operations by operation ID; verify the run details and OpenAPI integration tests

## 4. Reference tables in scripts (analytics)

- [x] 4.1 Implement `tamiza/reference.py` (design D6): stream each table's rows with a server-side cursor, build typed Arrow columns, write `<name>.parquet`, return name, rows, update time and path; verify integration tests with integer, decimal, date and text columns and nulls round-tripping into pandas as `Int64`, `float64`, `datetime64` and `str`
- [x] 4.2 Add `ReferenceTables` (lazy, cached, read-only mapping with `ReferenceTableNotFound` listing the available names) to `runner.Context` as `ctx.reference`, and a `reference` argument to `launcher.run_script` that links the files into `step_dir/reference/`; verify runner unit tests: a script reads a table, an unknown name fails with both names in the error, an empty project lists no names, and an unread table is never loaded
- [x] 4.3 Load reference tables once at the start of each run, pass them to every script step and record `runs.reference_tables`; extract the `ctx` and reference setup from `pipeline._execute_steps` into helpers tests can reuse; verify integration tests: an indicators script dividing by a reference column, the run's recorded tables, and a table replaced during a run not seen by that run but seen by the next

## 5. Script tests (analytics)

- [x] 5.1 Implement `tamiza/preview.py` (design D4): first 100 rows per table, the shared type vocabulary, big integers as strings, dates and timestamps as ISO text, text cut at 500 characters and marked, row trimming to 1 MB with the shown count, 20 tables per step; verify unit tests for each rule
- [x] 5.2 Implement `tamiza/sample_runs.py` (design D3): hash-ordered sample, flatten, saved clean version for output and indicators tests, the draft, step records with previews written as each step ends, no publishing, working directory removed; verify integration tests: a draft of script 1 tested with no new version and the published clean schema unchanged, a test of script 2 using the saved version 3 of script 1, script 1 failing in a test of script 3 with the draft not run, a missing indicator column failing with the run's message, a project with fewer submissions than the sample, two tests using the same submissions, deleted submissions excluded
- [x] 5.3 Wire tests into the worker (design D2): claim a queued test before a queued run, mark running tests `test.interrupted` on startup, delete tests 24 hours after they finished; verify integration tests: with a queued run and a queued test, the test runs first; an interrupted test is marked failed; an expired test is deleted; a test exceeding the time limit fails with the limit message

## 6. UI

- [x] 6.1 Add the script-test calls to `pipeline.service.ts` and the "Test on sample" action with the sample size field in the Scripts section, hidden for viewers; build `script-test-panel.ts` with polling every 2 s while queued or running, waiting messages, collapsible steps with status, duration, rows, log and error, the table selector and preview table with column types, the cut-value and fewer-rows notes, and the stale-draft note; verify unit tests per role, the request body, polling start and stop, each waiting message, a failed step with traceback and the preview rendering
- [x] 6.2 Show the reference tables a run used in the run details dialog; verify a unit test rendering them and the empty case
- [x] 6.3 Add `core/projects/reference.service.ts`, the `reference` route and the Reference data tab after Scripts: list (table at `md`+, cards below), upload dialog with name prefilled from the file name, renamed-columns alert, 409 message, replace and delete with confirmations, preview dialog; verify unit tests per role, the multipart request, the 409 and validation messages, both confirmations and the preview dialog
- [x] 6.4 Add `en.json` keys for every new text and the error codes `reference.exists` and `test.interrupted`, and extend the error-code translation test; verify the test lists every code the API and worker record
- [x] 6.5 Verify the Scripts section with a test result and the Reference data tab with its dialogs at 360 px and 1280 px in headless Chrome (no horizontal page scroll; previews and logs scroll inside their containers) with screenshots

## 7. Documentation

- [x] 7.1 Update `docs/scripts.md`: a "Testing on a sample" section (draft vs saved code, which scripts run, sample size and selection, waiting behavior, preview limits, 24-hour lifetime, same limits as runs), a "Reference data" section (upload, replace, delete, file rules, column naming, type detection with the leading-zero and decimal-comma rules, per-project scope), `ctx.reference` in the `ctx` table with an example, and the reference tables in the run log; verify links resolve
- [x] 7.2 Update README status and `docs/tamiza-brief.md` (section 6 data model with `script_tests` and the reference tables as stored); verify links resolve

## 8. Stack verification

- [x] 8.1 On the Compose stack with the 500,000-submission fake Kobo: test each sample script from `docs/scripts.md` on 500 submissions with an idle worker and record the time to result and the sample query duration in the design; start a test during a full run with another run queued and check it starts after the running run and before the queued one; upload a population table and run an indicators script that uses it, checking the published indicators and the run details
