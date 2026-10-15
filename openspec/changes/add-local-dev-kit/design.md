# Design

## Context

See proposal.md for the motivation and specs/ for the required behavior. The pieces this change builds on:

- **Runner.** `tamiza-analytics/src/tamiza/runner.py` runs one script in a child process (`python -I -u -m tamiza.runner <step dir> <kind> [<name>] [--show] [--stop-line N]`). It reads `context.json`, `input/*.parquet` and `reference/*.parquet`, builds `ctx` (`Form`, `ReferenceTables`, `Context`), loads the script (`_load`: AST, `exec` in a fresh namespace, `linecache` for tracebacks), validates what it returns (`_validated`, `_normalize_columns`, `_plain`) and writes `output/*.parquet` and `result.json`. It imports only `cells`, `interchange`, `names` and `shows`, and never touches the database. `interchange.read_parquet` and `write_parquet` use pyarrow only; `describe`, `count_rows` and `iter_rows` use DuckDB and serve the publisher. `preview.dashboard_view` and `column_type` use pandas and pyarrow only. `launcher.py` is Linux-only (`/proc`, `os.killpg`) and stays on the server.
- **Sample tests.** `sample_runs.py` claims a queued `tamiza.script_tests` row ahead of runs. It resolves the current script 1 version at claim time and flattens a hash-ordered sample, or reuses it from `sample_cache` (keyed by everything flattening read). It then runs or reuses the saved script 1 (keyed by the flatten key, the version and the reference tables' update times) and runs the draft. The test's work directory is removed when it ends; only the cache entries (Parquet and `step.json`) stay on the worker's disk. `project.context` (`project_name`, `questions`, `choices`, and after flattening `label_codes` and `columns`) is exactly what `context.json` holds.
- **Downloads.** The API has no access to the worker's disk: `compose.yaml` shares no volume between `api` and `analytics`, which talk only through PostgreSQL. Downloads use `ExportRegistry`: an in-memory map of 32-byte random tokens to files in `DataDashboardOptions.ExportDirectory`, taken once, expiring after `ExportLinkLifetime` (5 minutes). `GET /api/v1/data-exports/{token}` (anonymous) streams the file with delete-on-close, and nginx proxies that path without buffering.
- **Schema.** DbUp scripts in `tamiza-api/src/dbUp/Scripts` (the last one is `0009_add_script_workbench.sql`). `SchemaDriftTests` requires every table to be mapped in `TamizaDbContext`, except the journal.
- **Upload.** The Scripts section already loads a local `.py` into the editor ("Upload .py").

## Goals / Non-Goals

**Goals:**
- One copy of the contract code: the worker and the SDK run the same functions for building `ctx`, loading a script, validating output and reading Parquet, so the same sample and script give the same tables.
- The SDK runs anywhere Python 3.12 and the pinned libraries run, with only the downloaded folder: no database driver, no DuckDB, no POSIX-only calls, no network.
- Preparing a download costs about as much as a test that reuses the cached sample; storing it is bounded (one per user and project, one hour, a size limit).
- No new container, volume or public endpoint; the download reuses the existing single-use link endpoint unchanged.

**Non-Goals:**
- A notebook kernel on the server, or the SDK reaching the server (no tokens, no upload from Python).
- Local time or memory limits, or process isolation: `check` runs in the analyst's own Python process.
- The dashboard view's warnings in `check` (they live in the UI's `indicator-view.ts`; a sample test after uploading shows them).
- Downloading published tables or more than 5,000 submissions.

## Decisions

### D1. `tamiza_sdk` as a second package of the analytics distribution

`tamiza-analytics/src/tamiza_sdk/` becomes a top-level package next to `src/tamiza/`, built into the same wheel (`[tool.hatch.build.targets.wheel] packages = ["src/tamiza", "src/tamiza_sdk"]`). The database-free code moves into it, and the worker imports it from there:

| SDK module | Moved from | Contents |
|---|---|---|
| `tables.py` | `interchange.py` | `read_parquet`, `write_parquet` and their helpers (pyarrow only). `interchange` re-exports them and keeps the DuckDB functions. |
| `names.py` | `names.py` | `reduce`, `is_valid_table_name`. `tamiza.names` imports them and keeps `deduplicate`, `is_identifier`, `schema_name`. |
| `context.py` | `runner.py` | `Form`, `ReferenceTables`, `ReferenceTableNotFound`, `Context`. `Context` takes an optional `show` callable instead of a `ShowRecorder`; `runner` passes `recorder.record` when `--show` is given. |
| `contract.py` | `runner.py` | `FUNCTIONS`, `ScriptError`, `load_function(source, filename, function_name, prepare=None)` (parse, optional AST hook, compile, `exec` in a fresh namespace, look up the function), `validated(kind, returned, name)` returning the tables and the column renames, `plain`. |
| `dashboard.py` | `preview.py` | `column_type`, `dashboard_view` and their helpers. `preview` imports them. |
| `bundle.py` | new | Reading a downloaded folder: the manifest, the format check, the source folders, the version warnings (D3). |
| `notebook.py` | new | The local `ctx.show` (D3). |
| `__init__.py` | new | `load_context`, `load_tables`, `check`, `CheckResult`, `ScriptError`, `__version__`. |

`runner.py` keeps the process concerns: argument parsing, `linecache` for `script.py`, the cell-stop hook (passed to `load_function` as `prepare`), `result.json`, the `ScriptError`/`MarkerError`/`SystemExit` handling, and the traceback limited to `script.py`. `validated` returns the renames instead of printing them, and the runner prints the same `INFO Table x: column 'A' published as 'a'.` lines, so run logs are unchanged. `runner` re-exports `Context`, `Form` and `ReferenceTables`, so `test_ctx_members.py` and other imports keep working.

The rule that keeps the SDK portable is enforced by a unit test. It parses every module of `tamiza_sdk` and fails on imports of `tamiza`, `psycopg`, `duckdb`, `socket`, `urllib`, `http`, `subprocess`, `signal`, `resource` or `fcntl`, and on `os.killpg` or `/proc`. geopandas stays a lazy import inside `plain`, as today, and IPython a lazy import inside `notebook.py`. `tamiza_sdk.__version__` is a literal checked against `tamiza.__version__` and `pyproject.toml` by a test. `tamiza/__init__.py` does not import the SDK, so the healthcheck (`python -m tamiza.health`) never loads pandas.

*Alternatives:* a separate `tamiza-sdk/` project that the worker depends on (the analytics image's build context would have to become the repository root, and `compose.yaml` and the Dockerfile would change for no behavioral gain); a copy of the code written for the SDK (it would drift from the server, which defeats the purpose).

### D2. The SDK's public functions

```python
import tamiza_sdk as tz

ctx = tz.load_context(folder=".")                    # Context, as a script receives it
raw = tz.load_tables("raw", folder=".")              # dict[str, DataFrame]: script 1's input
cleaned = tz.load_tables("clean", folder=".")        # script 2's and the indicators' input
result = tz.check("rate.py", "indicator", name="rate", tables=None, folder=".", show=True)
result.tables       # dict[str, DataFrame] as they would be published
result.renamed      # [(table, before, after)]
result.dashboard    # dashboard_view facts for an indicator, else None
```

- `load_context` reads `context.json` and points `ReferenceTables` at `reference/`. `ctx.log` is the `tamiza.script` logger with one stdout handler and the server's `%(levelname)s %(message)s` format (handlers are reset on each call, as the runner does). `ctx.show` is the notebook display of D3.
- `load_tables(source)` accepts `raw` or `clean` and reads every Parquet file of the source folder with `tables.read_parquet`, so the dtypes are the runner's: nullable integers and booleans, and dates as `datetime64`. With the manifest's `clean.status` set to `none`, `clean` reads `raw/`; with `failed`, it raises `tz.ScriptError` with script 1's version and error and suggests `load_tables("raw")`.
- `check(script, kind, name=None, *, tables=None, folder=".", show=True)`:
  - `script` is a callable, which is called as is, or a path. A path is read and passed to `contract.load_function` with the file's real path as the file name, so tracebacks show the file's lines from disk and the namespace is fresh.
  - The inputs are `load_tables("raw")` for `clean` and `load_tables("clean")` otherwise, read again for each check. When `tables` is given, the inputs are deep copies of it. `ctx` is a new `load_context`; with `show=False`, `ctx.show` does nothing, as in a run.
  - The returned value goes through `contract.validated`. Contract violations raise `tz.ScriptError` with the run's message. The script's own exceptions are not caught, so the notebook shows the full traceback and `%debug` works.
  - For `indicator`, `dashboard` is `dashboard.dashboard_view` of the table, written to a temporary Parquet file through `tables.write_parquet`, because the facts are defined over the published file's types.
  - `CheckResult` has a short `__repr__` and `_repr_html_`: each table with its rows and its columns with the preview type vocabulary, the renamed columns and, for an indicator, the measures, default measure, ratio, dimensions and geometry columns.

*Alternatives:* `check` writing the tables to Parquet and reading them back so they match the published types exactly (adopted only for the dashboard facts; the analyst wants the DataFrames their script returned, already normalized); running the check in a subprocess with the runner (it would add isolation, but break `%debug` and the notebook's state, which are the reason to work locally).

### D3. Local `ctx.show` and version warnings

`notebook.show(value, name)` displays a header (`name`, or `shown N` counted per context, and the row count for tables) and then the value. Inside IPython (`IPython.get_ipython()` is not `None`), it uses `IPython.display.display`, so pandas renders its own HTML. Outside, it uses `print` of the header and the value's `repr`, which pandas truncates. It returns the value. There is no 20-call limit and no snapshot: the notebook keeps what was displayed.

`bundle.open(folder)` reads `manifest.json` once per folder. It raises `ScriptError` when `format` is greater than the SDK's `FORMAT`, naming the SDK included in that download as the one to use. It warns once per session (`warnings.warn`, category `tz.EnvironmentWarning`) when `sys.version_info[:2]`, or `importlib.metadata.version()` of a library in `manifest.libraries`, differs from the server's. It also warns when `tamiza_sdk.__version__` differs from `manifest.tamiza`. A library that is not installed is reported only when it is imported, by Python itself.

### D4. Download layout and manifest

The zip holds one folder, `tamiza-sample-<UTC yyyymmdd-hhmmss>/`, so two downloads extract side by side:

```
README.md                 how to set up, use the three functions, upload, and keep the data safe
getting-started.ipynb     load, look, a compute() to edit, check(), save to a .py, check the file
requirements.txt          the libraries scripts can use, pinned (D5)
manifest.json
context.json              exactly what the runner reads
raw/<table>.parquet       the flattened sample
clean/<table>.parquet     the saved script 1's result; absent when there is no script 1 or it failed
reference/<table>.parquet
logs/flatten.txt          the flatten step's log, as a test shows it
logs/clean.txt            the saved script 1's log and error, when it ran
tamiza_sdk/*.py           the SDK's source, copied from the worker's installed package
```

```json
{
  "format": 1,
  "tamiza": "0.1.0",
  "createdAt": "2026-10-20T14:03:11Z",
  "project": { "id": "…", "name": "Water survey 2026" },
  "sample": { "requested": 500, "submissions": 320 },
  "python": "3.12.11",
  "libraries": { "pandas": "3.0.6", "pyarrow": "25.0.1", "numpy": "2.5.3", "polars": "1.44.2", "duckdb": "1.5.6", "geopandas": "1.2.0", "openpyxl": "3.1.5" },
  "raw": { "tables": [{ "name": "main", "rows": 320 }, { "name": "members", "rows": 871 }] },
  "clean": { "status": "succeeded", "version": 2, "error": null, "tables": [{ "name": "main", "rows": 318 }] },
  "reference": [{ "name": "population", "rows": 42, "updatedAt": "…" }]
}
```

`clean.status` is `succeeded`, `failed` (with `version` and the first line of the error) or `none`. Parquet files are stored in the zip uncompressed (`ZIP_STORED`, since they are already compressed); text files are deflated. The README and the notebook are static files in `tamiza/kit/` (package data), with the version and dates filled in. The notebook's code cells are plain Python, with no magics: saving the function to a file is shown with `pathlib.Path("my_indicator.py").write_text(...)` in a markdown note and a cell. That way a test can run every code cell with `exec`, with no Jupyter installed.

### D5. Pinned requirements

`kit.LIBRARIES = ("duckdb", "geopandas", "numpy", "openpyxl", "pandas", "polars", "pyarrow")`, the libraries `docs/scripts.md` lists for scripts. `requirements.txt` is written from `importlib.metadata.version()` of each, with a comment naming the server's Python version. A unit test fails when `pyproject.toml`'s dependencies, minus `psycopg`, differ from `LIBRARIES`, so a library added to the image cannot be forgotten in the kit. Jupyter itself is not pinned; the README installs `jupyterlab` next to the requirements.

### D6. The worker job

`tamiza/sample_downloads.py` mirrors `sample_runs.py`:

- `claim` starts the oldest queued download (`FOR UPDATE SKIP LOCKED`) and records the current script 1 version in the same statement, as a test's claim does. `pipeline.Processor` tries tests first, then downloads, then runs. At start it marks downloads left `running` as failed (`download.interrupted`), and on each cycle it deletes downloads that ended more than an hour ago (the parts go with them).
- Preparation is shared with tests. `sample_runs` gains `prepare_inputs(connection, project_id, sample_size, clean_version_id, settings, work_dir, on_step)`, extracted from `_execute_steps`, which runs the flatten step and the saved script 1 step with the cache and calls `on_step` as each ends. It returns the project context, the reference tables, the raw tables and, when script 1 ran, either its tables or its failed step. Tests pass an `on_step` that writes `script_tests.steps` as today and then run the draft; downloads keep the steps for `logs/`. Downloads protect their cache keys like tests do.
- A failed flatten fails the download (`step.flatten`, the error's first line). A failed script 1 does not: the manifest records it and the zip has no `clean/`.
- `kit.write(folder, prepared, manifest)` writes the layout of D4 in the work directory, then `kit.zip(folder, path)`. If the zip is larger than `TAMIZA_SAMPLE_DOWNLOAD_MB` (default 100, at least 1), the download fails with `download.tooLarge` and the message *The sample is 130 MB, more than the 100 MB this server allows. Choose a smaller sample.*. Reference tables are usually the cause, so the log lists their sizes.
- Storing: in one transaction, the zip is read in 1 MiB parts into `tamiza.sample_download_parts`, and the download row gets `status = 'succeeded'`, `size_bytes`, `file_sha256` and `contents` (the manifest without `libraries`, which the UI shows). A reader never sees a ready download without its parts. Memory stays at one part.
- The work directory is removed whatever happens, as for tests.

*Alternatives:* a volume shared by `api` and `analytics` for the zip (it couples two containers that only meet in the database today, needs matching file ownership, and conflicts with `harden-deployment`'s separation); building a bundle for every test (most tests are never downloaded, and with "Run cell" there are many tests); PostgreSQL large objects (Npgsql's large object API is deprecated, and orphaned objects need their own cleanup).

### D7. Schema (`0010_add_local_dev_kit.sql`)

```sql
CREATE TABLE tamiza.sample_downloads (
    id uuid NOT NULL, project_id uuid NOT NULL, requested_by uuid NOT NULL,
    sample_size integer NOT NULL, status text NOT NULL,
    clean_version_id uuid NULL,
    queued_at timestamptz NOT NULL, started_at timestamptz NULL, finished_at timestamptz NULL,
    size_bytes bigint NULL, file_sha256 text NULL, contents jsonb NULL,
    error_code text NULL, error text NULL,
    CONSTRAINT pk_sample_downloads PRIMARY KEY (id),
    CONSTRAINT ck_sample_downloads_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    CONSTRAINT ck_sample_downloads_sample_size CHECK (sample_size BETWEEN 1 AND 5000),
    CONSTRAINT fk_sample_downloads_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_sample_downloads_users_requested_by FOREIGN KEY (requested_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_sample_downloads_script_versions_clean_version_id FOREIGN KEY (clean_version_id) REFERENCES tamiza.script_versions (id)
);
CREATE INDEX ix_sample_downloads_requested_by ON tamiza.sample_downloads (requested_by);
CREATE UNIQUE INDEX ix_sample_downloads_project_id_requested_by_queued ON tamiza.sample_downloads (project_id, requested_by) WHERE status = 'queued';
CREATE INDEX ix_sample_downloads_status_queued_at ON tamiza.sample_downloads (status, queued_at);
CREATE INDEX ix_sample_downloads_finished_at ON tamiza.sample_downloads (finished_at);

CREATE TABLE tamiza.sample_download_parts (
    download_id uuid NOT NULL, part integer NOT NULL, data bytea NOT NULL,
    CONSTRAINT pk_sample_download_parts PRIMARY KEY (download_id, part),
    CONSTRAINT fk_sample_download_parts_sample_downloads_download_id FOREIGN KEY (download_id) REFERENCES tamiza.sample_downloads (id) ON DELETE CASCADE
);
ALTER TABLE tamiza.sample_download_parts ALTER COLUMN data SET STORAGE EXTERNAL; -- zip data does not compress
```

Both tables are mapped in `TamizaDbContext` (`SampleDownload`, `SampleDownloadPart`). The API never loads `Data` through EF; it streams it (D8).

### D8. API

All under `/api/v1/projects/{projectId}/sample-downloads`, tag `Pipeline`, role `analyst`, visible only to the requester. A download that ended more than an hour ago is treated as missing.

| Endpoint | Behavior |
|---|---|
| `POST ""` `{ sampleSize? }` → 202 `SampleDownloadDetails` | Validates 1–5,000 (default 500). In one transaction, it deletes the user's downloads in the project that are not `running` and inserts a queued one (retrying on the unique index, as `ScriptTestEndpoints.QueueAsync` does). |
| `GET "/{downloadId}"` → `SampleDownloadDetails` | Status, `waitingFor`, timestamps, `expiresAt` (`finishedAt` + 1 h, for a succeeded download), `sizeBytes`, `contents`, `errorCode`, `error`. |
| `POST "/{downloadId}/link"` → `{ downloadUrl, fileName, sizeBytes, expiresAt }` | 409 `sampleDownload.notReady` while queued or running, and 409 `sampleDownload.failed` for a failed one. Otherwise, it waits for a slot (`MaxConcurrentExports`, `QueueTimeout`, else 503 `sampleDownload.busy`). It then streams the parts in order (`CommandBehavior.SequentialAccess`, `GetStream`) into `ExportRegistry.NewFilePath()`, checks the length against `size_bytes`, and registers the file (`application/zip`, `ExportFileName.Create(project.Name, "sample", today, "zip")`). |

`waitingFor` of a queued download is `run` when a run is running; `test` when a test is running or queued; `download` when another download is running or queued before it. `ScriptTestWait` gains `Download`: a queued test waits for a running download (D6's order means queued downloads never hold back a test). The link uses the existing `GET /api/v1/data-exports/{token}`; nginx needs no new location, since the link request finishes in seconds for 100 MB and stays under the default proxy timeout.

### D9. UI

In `project-scripts.ts`, the "Test on sample" form gets a second button, "Download sample" (`type="button"`, shown under the same conditions). It reads the same sample size field and its validation, then calls `PipelineService.startSampleDownload`. The section keeps the latest download in a signal and renders `app-sample-download-panel` above the test panel. The panel:

- polls `GET` every `SCRIPT_TEST_POLL_MS` while the download is queued or running, and shows what it waits for;
- when it is ready: the submissions, the raw and clean tables with their rows, the reference tables, the size, the script 1 version, and a warning when script 1 failed (with its error, and that only the flattened tables are included); a "Download .zip" button that requests a link and calls `Downloader.start`; and a notice that the file holds the project's submissions and should be kept as safely as the survey data and deleted when no longer needed;
- on a 404 (expired, or replaced from another tab): says that the download expired and offers "Prepare again" with the same size.

The test panel's waiting text gains `scriptTests.waitingForDownload`. All strings go in `en.json`.

### D10. Documentation

`docs/scripts.md` gains "Writing scripts on your computer":

- download and unzip;
- create an environment with Python 3.12 and run `pip install -r requirements.txt jupyterlab`;
- the three functions;
- the loop: explore in the notebook, move the function and its imports into a `.py`, `check("file.py", …)`, "Upload .py", "Test on sample";
- what differs from the server (no limits or isolation, one sample, the versions warning);
- keeping the data safe.

Its code blocks import `tamiza_sdk`. `test_docs_examples.examples()` skips blocks that mention `tamiza_sdk`, and a new test runs them in order against a real download. `docs/operations.md` documents `TAMIZA_SAMPLE_DOWNLOAD_MB`, the one-hour lifetime and the database space prepared samples take.

## Risks / Trade-offs

- [Blobs in PostgreSQL grow the database and its WAL] → At most one download per user and project, deleted an hour after it is ready, capped at 100 MB by default. The parts are `STORAGE EXTERNAL`, so PostgreSQL does not try to compress them. `docs/operations.md` gives the worst case (analysts × projects × limit) and the setting to lower it.
- [Survey data copied off the server] → Analysts can already read every submission through tests and scripts. The download is limited to analysts and admins, visible only to its requester, and the UI and README say how to keep it. `add-audit-log` should add a `sample_download` event; this change records none, like the changes before it.
- [Moving the runner's code changes a run's behavior] → The move keeps the classes and messages (`ScriptError` is the same class the runner catches). The existing `test_runner.py`, `test_show.py`, `test_launcher*.py` and the integration suites pass unchanged, except for import paths. The run log's rename lines are compared verbatim.
- [The local environment differs from the server's (another Python, pandas 2.x, Windows paths)] → Pinned requirements and a once-per-session warning for each difference. The SDK uses `pathlib`, reads text as UTF-8 and calls no POSIX-only API (the D1 test). The parity test runs on Linux only; running the starter notebook on Windows is a manual check (task 8.3).
- [The sample cache is evicted between a test and a download] → The download recomputes the flatten and script 1 steps. That is slower, but gives the same result, since the keys cover every input.
- [A link request copies up to 100 MB while the analyst waits] → This takes seconds and is bounded by the export slots. A failed copy deletes its file, and the analyst can ask again.
- [`check` runs analysts' code in their own Python] → It is their own code on their own machine. Running it does not touch the server.

## Migration Plan

1. `0010_add_local_dev_kit.sql` adds the two tables. The API applies it before it reports healthy, and `analytics` starts after `api` is healthy, so the worker never polls a missing table. No existing data changes.
2. The new setting has a default, so existing `.env` files keep working. `compose.yaml` passes `TAMIZA_SAMPLE_DOWNLOAD_MB` to `analytics`.
3. Rollback: an earlier release ignores the two tables. They can be dropped by hand, or left in place: they hold nothing older than an hour.

## Open Questions

- Should operators be able to turn sample downloads off for a deployment or a project, for data-protection policies? Deferred: nothing in this design prevents adding a setting that hides the action and rejects the request.

## Deviations from the plan

- **`validated` fills a list instead of returning the renames.** `contract.validated(kind, returned, name, renames)` appends each `(table, before, after)` to a list the caller passes, so the runner still logs the renames of the tables it had processed when a later table fails, exactly as before the move (D1 said it returns them).
- **The templates live in `tamiza/kit_files/`, not `tamiza/kit/`.** A `kit/` directory beside `kit.py` would be ambiguous to read.
- **`load_context` and `load_tables` are in `tamiza_sdk/local.py` and `check` in `tamiza_sdk/checking.py`**, re-exported by `__init__`, rather than all in `__init__`; `build_context` (context.py) is the one function the runner and `load_context` share for the logger and `ctx`. `Context` takes the `show` callable that D1 describes, and the runner passes the recorder's `record`.
- **The zip keeps empty folders.** Found by the end-to-end check: a project without reference tables produced a zip without `reference/`, though the spec lists one folder per source. `zip_folder` writes an entry for an empty folder, and a test pins it.
- **The starter notebook checks with `tables=cleaned`.** So that it runs when script 1 failed on the sample (there are no downloaded clean tables then); the notebook falls back to the flattened tables and says why.
- **`WithExportSlotAsync` on `DataDashboard`.** D8 needs the export slots, which `DataDashboard` owns; it exposes one internal method that runs an action in a slot and throws the existing `DataExportBusyException`.
- **`HasJsonObjectColumn`.** The existing JSON column mapping handles lists only; `contents` is one object, so `JsonColumn` gained a single-object overload.
- **Unexpected failures of a download have a code of their own.** A failure that is not the flatten step or the size limit (the database or the disk) is recorded as `download.failed` with the exception's type and message; the design named only `step.flatten`, `download.tooLarge` and `download.interrupted`.
- **Every finished download is deleted an hour after it ended**, failed ones too, not only ready ones, so a failed row does not wait for its requester to replace it.
- **The action sits in the test form.** "Download sample" is hidden with the form, so also while an old version is on view, not only for viewers. A viewer never sees it, as specified.

## Verification Results

Real stack in a separate Compose project (`tamizaverify`, port 8189, images tagged `verify`), a fake OIDC provider and a fake Kobo on the host (14 submissions with a repeat group, one of them a test the script drops), and headless Chrome over the DevTools protocol. The user's own stack was not touched; the verification stack, its volumes and images, the fake servers and the temporary files were removed afterwards.

- **Flow, 23 of 23 checks.** The project was created and synchronized through the API; script 1 and an indicator were saved. In the Scripts section, "Download sample" sat in the form of the sample size; the panel followed the download to ready (14 submissions, `main` 14 and `members` 28 rows, clean `main` 13 and `members` 26 rows from script 1 version 1, 36 KB) and showed the data notice. The browser saved `verification-survey_sample_<date>.zip` through a single-use link behind the UI's nginx.
- **Zip.** One `tamiza-sample-<timestamp>/` folder with the kit; the manifest says 14 submissions and script 1 succeeded; the whole archive was searched for the Kobo token, the database password, the asset UID and the host name, and none was found.
- **Offline notebook.** The starter notebook ran in the test image with `--network none`, no repository mounted and the server's packages blocked: the network was unreachable, the SDK that ran was the folder's own, and the check passed.
- **Same table.** After changing the grouping to `municipality` in the unzipped file, the local check gave Buga 4, Cali 5, Palmira 4; "Upload .py" and "Test on sample" of 500 on the server gave the same columns and rows.
- **Link and expiry.** The used link answered 404; with `finished_at` moved back two hours the API answered 404 for the download, the page said it was no longer available, and the worker deleted the row and its parts within seconds.
- **Found and fixed.** The first run showed the zip had no `reference/` folder for a project without reference tables (see the deviations); after the fix the analytics image was rebuilt and the flow passed again.
- **Windows.** Python 3.12.10 on Windows 11, a virtual environment, `pip install -r requirements.txt jupyterlab` from a kit written by the real code, and `jupyter nbconvert --execute` on the starter notebook: its 5 code cells ran in a real kernel with no error output and the check of the example indicator passed (pandas 3.0.6, as pinned).
- **Suites.** Analytics 582 passed with `ruff check` and `ruff format --check` clean; .NET 511 unit and 712 integration passed; UI 703 passed, the only two failures being the known polling tests (`project-data.spec.ts`, `project-scripts.spec.ts`).
- **Independent review.** After implementation, four reviewers (worker and kit, SDK, API and database, conformance to the specs) each reported candidate defects and two skeptics tried to refute every one against the code. Fixed as a result: a fault of the worker in script 1's step was blamed on script 1 and its server traceback was written into `logs/clean.txt` (it now fails the download as `download.failed` without a traceback); work directories left by a killed worker were never removed (they are swept at start); a request made while a download was being prepared could leave a user with two ready downloads in one project (a ready download now deletes the user's earlier finished ones in its own transaction); the kit README said the server blocks the network; and `check` returned frames that had not been through Parquet, so a chained script saw `int64` and a gapped index where the server's next step sees `Int64` and a fresh index (its tables are now read back from Parquet). Not changed: link requests are not capped, as for the existing data-export links, whose copies are bounded by the export slots and expire after five minutes.
