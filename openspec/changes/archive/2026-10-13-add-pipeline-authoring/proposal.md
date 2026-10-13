# Proposal

## Why

Analysts can now save scripts and run the chain, but the only way to try a change is to save it and start a full run on every submission, which creates a version per attempt, takes minutes on large projects and replaces the published tables if it succeeds. Scripts also cannot use the outside tables that indicators routinely need (population projections, municipality codes, price lists), so analysts still hard-code them or compute those indicators outside Tamiza. This change completes the pipeline split agreed in `add-python-pipeline`: a sample test run with preview, and project reference data available from `ctx`.

## What Changes

- **Sample test run.** An `analyst` or `admin` can test the script open in the editor, saved or not, on a sample of the project's submissions (500 by default, up to 5,000). The test flattens the sample, runs the saved script 1 when testing script 2 or 3, then runs the draft. Nothing is published and no version is created. The test uses the same contract, output validation and time and memory limits as a run.
- **Preview.** A finished test shows each step's status, row counts, log and error with traceback, and for every table it produced its columns with their types and the first 100 rows.
- **Sharing the worker.** Tests use the existing analytics worker: a queued test starts before any queued run but waits for a running run or test to finish, and the UI says what it is waiting for. A new test replaces the same user's test that has not started yet. Test results are visible only to the person who requested them and are deleted after 24 hours.
- **Reference tables.** An `analyst` or `admin` can upload a CSV or XLSX file (up to 10 MB) as a named reference table, replace it with a new file, and delete it. Column names are reduced with the same rule as question names, and column types (integer, decimal, date, text) are detected from the values. Every member can list the tables and preview their columns and first rows.
- **`ctx.reference`.** Scripts read reference tables as pandas DataFrames by name (`ctx.reference["population"]`), in runs and in tests. Each run records which reference tables it used and when each was last updated.
- **UI.** A "Test on sample" action and a results panel in the Scripts section; a new **Reference data** section in the project.
- **Docs.** `docs/scripts.md` gains "Testing on a sample" and "Reference data".

Not in this change: testing a chain of several unsaved drafts at once; cancelling a test or a run; reference tables shared across projects (the brief's pending decision; stays per project); downloading a reference table's original file; audit events (`add-audit-log`).

## Capabilities

### New Capabilities

None. Both features belong to the existing `pipeline` capability, as in the brief.

### Modified Capabilities

- `pipeline`: adds the sample test run and its preview, and project reference tables; the script context gains `ctx.reference`; the run log records the reference tables each run used.

## Impact

- **analytics:** the worker claims queued tests before queued runs, runs them on a sampled flatten without publishing, writes previews into the test's result and deletes expired tests. Reference tables are written as Parquet into each step directory, and the runner exposes them through a lazily loading `ctx.reference`.
- **api:** endpoints to queue and read script tests; endpoints to upload, replace, delete, list and preview reference tables (CSV/XLSX parsing with MiniExcel, as for the sampling frame); a .NET port of the column naming rule, checked against test cases shared with the Python suite; run details include the reference tables used.
- **Database:** DbUp script `0004_add_pipeline_authoring.sql` with `script_tests`, `reference_tables` and `reference_table_contents`, and `runs.reference_tables`.
- **UI:** test action, sample size field and results panel in the Scripts section; a Reference data tab with upload, replace, delete and preview.
- **Docs:** `docs/scripts.md`.
- **Configuration and dependencies:** none new.
