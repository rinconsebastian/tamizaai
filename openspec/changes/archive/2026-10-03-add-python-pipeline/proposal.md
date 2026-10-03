# Proposal

## Why

Submissions now arrive in Tamiza as raw Kobo JSON, but analysts still export them to Jupyter to clean them and compute indicators. The core promise of the product is to run that work inside Tamiza: the analyst's own Python, on every new batch of data, with results published where the clean-data and indicator dashboards will read them. Nothing downstream (dashboards 2 and 3) can be built until this chain exists.

This is the first of two pipeline changes. A follow-up, `add-pipeline-authoring`, adds the sample test run with preview and project reference data.

## What Changes

- **Flattening.** Before any script, the worker turns the project's non-deleted raw submissions into tables in the schema `p_<id>_raw`: one main table and one table per repeat group, each row keyed to its parent. Columns follow the form's questions and take types from the form (numbers, dates, times as text where unknown).
- **Three optional scripts with a fixed contract.** Each project can have `clean(raw, ctx)`, `prepare_output(cleaned, ctx)` and `compute_indicators(cleaned, ctx)`. `ctx` exposes the form dictionary (questions, choice lists and labels) and a logger whose messages appear in the run log. Without script 1, the cleaned data is the raw data; scripts 2 and 3 are simply skipped when absent.
- **Execution order and atomic publishing.** Script 1 runs first; scripts 2 and 3 depend only on it. Each step writes to a temporary schema and replaces its published schema (`p_<id>_clean`, `p_<id>_output`, `p_<id>_indicators`) only if it succeeds and passes validation, so dashboards always see the last good version. Script 3's output must have the columns `indicator`, `disaggregation`, `value`, `numerator`, `denominator`.
- **Script versioning.** Every save creates an immutable version. Each run records the version of each script it used, and per step its duration, input and output rows, logs and errors.
- **Run triggers.** Runs start manually, after a synchronization that changed data (on by default, admins can turn it off), or on a schedule an admin sets. A webhook never starts a run.
- **Run limits and isolation.** Each script runs in a separate process with configurable time and memory limits, no database credentials and no way to install packages. This is not a security sandbox (out of scope for the MVP); saving code remains limited to analysts and admins.
- **Scripts section in the UI.** A Python code editor (CodeMirror 6) for each script, with version history, a "Run now" action, the run log with per-step details, and the pipeline settings for admins.
- **Form dictionary.** The project's form snapshot also keeps the choice lists, so scripts can translate codes into labels.

Not in this change: the sample test run with preview and reference data (`add-pipeline-authoring`); the dashboards that read the published schemas; cancelling a running run; PostGIS geometry columns (geometries are published as WKT text); a security sandbox; audit events (`add-audit-log`).

## Capabilities

### New Capabilities

- `pipeline`: flattening of raw submissions, the three-script contract and `ctx`, default behavior without scripts, execution order, atomic publishing and output validation, script versioning, runs and their log, run triggers and settings, run limits and process isolation.

### Modified Capabilities

- `projects`: the form snapshot taken on creation, re-check and synchronization also keeps the form's choice lists (names and labels), not only its fields.

## Impact

- **analytics:** the worker grows from a database heartbeat into the run executor: it claims queued runs, flattens submissions, runs each script in a child process with limits, validates outputs and publishes schemas. New modules for flattening, the script runner, publishing and `ctx`.
- **api:** script and version endpoints, run endpoints (start, list, details with logs), pipeline settings; queuing a run after a synchronization with changes and on schedule (in the existing ingestion scheduler); the form snapshot gains choice lists.
- **Database:** new tables `script_versions`, `runs` and `run_steps`, and pipeline settings on `projects` (DbUp script `0003`); per-project schemas `p_<id>_raw`, `p_<id>_clean`, `p_<id>_output`, `p_<id>_indicators` created at run time by `analytics`.
- **UI:** a new **Scripts** section with the code editor, versions, runs and settings. New dependency: CodeMirror 6 (`codemirror`, `@codemirror/lang-python`; MIT).
- **Configuration:** new optional variables for run limits (`TAMIZA_RUN_TIMEOUT_SECONDS`, `TAMIZA_RUN_MEMORY_MB`) in `compose.yaml` and `.env.example`.
- **Tests:** the Python integration tests need PostgreSQL; `analytics` adds `testcontainers` as a development dependency.
- **Dependencies (analytics):** `pyarrow` joins the pinned runtime dependencies (decided during verification: without it, text columns of 500,000 submissions took 12 times the memory and exceeded the script limit).
- **Docs:** a guide for writing scripts (`docs/scripts.md`), the brief's change plan updated with the split.
