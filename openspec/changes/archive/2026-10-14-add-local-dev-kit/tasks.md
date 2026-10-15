# Tasks

## 1. SDK package extracted from the runner (analytics)

- [x] 1.1 Create `tamiza-analytics/src/tamiza_sdk/` with `tables.py` (Parquet read and write, moved from `interchange`), `names.py` (`reduce`, `is_valid_table_name`) and `dashboard.py` (`column_type`, `dashboard_view`, moved from `preview`). Leave `interchange`, `names` and `preview` re-exporting them, and add the package to `[tool.hatch.build.targets.wheel]` (design D1). Verify that `test_interchange.py`, `test_names.py`, `test_preview.py` and `test_dashboard_view.py` pass unchanged, and that a wheel built in the test image contains both `tamiza/` and `tamiza_sdk/`.
- [x] 1.2 Move `Form`, `ReferenceTables`, `ReferenceTableNotFound` and `Context` to `tamiza_sdk/context.py`, with `Context` taking an optional `show` callable. Move `FUNCTIONS`, `ScriptError`, `load_function`, `validated` (now returning the renames) and `plain` to `tamiza_sdk/contract.py`. Make `runner.py` use them, print the rename lines itself and re-export the `ctx` classes (design D1). Verify that `test_runner.py`, `test_show.py`, `test_launcher.py`, `test_launcher_show.py`, `test_cells.py` and `test_ctx_members.py` pass with only import changes, and add an assertion that a run's log still holds `INFO Table households: column 'Household size' published as 'household_size'.`
- [x] 1.3 Add two tests (design D1). The SDK portability test fails on any import of `tamiza`, `psycopg`, `duckdb`, `socket`, `urllib`, `http`, `subprocess`, `signal`, `resource` or `fcntl`, and on `os.killpg` or `/proc`, in any `tamiza_sdk` module. The version test checks that `tamiza_sdk.__version__` equals `tamiza.__version__` and the version in `pyproject.toml`. Verify that both pass, that the portability test fails on a temporary `import psycopg` in an SDK module, and that `python -m tamiza.health` does not import pandas (check `sys.modules`).

## 2. SDK functions (analytics)

- [x] 2.1 Add `bundle.py` (manifest reading, `FORMAT = 1`, an `EnvironmentWarning` once per session for Python, library and SDK version differences) and `notebook.py` (`show` with an IPython display and a print fallback). Add a test fixture that builds a download folder from Parquet files and a manifest (design D3, D4). Verify unit tests for the scenarios `Different pandas version` (warned once, tables still load), `Unknown download format` (refused with a message naming the included SDK), a Python minor version difference, and `Show in a notebook`, both with a stub IPython shell and with the print fallback.
- [x] 2.2 Add `load_context` and `load_tables` (design D2). Verify unit tests for the scenarios:
  - `Label lookup locally`;
  - `Unknown reference table locally` (the same message as the runner's);
  - `Same types as on the server` (a nullable integer with a missing value, compared with what `runner.run_step` gives a script);
  - `Clean tables after script 1 failed`;
  - `Project without script 1` (`clean` reads `raw/`);
  - new objects on each call.
- [x] 2.3 Add `check` and `CheckResult` (design D2). Verify unit tests for the scenarios:
  - `Indicator without a numeric column` (the same message as a run's);
  - `Columns renamed as in a run`;
  - `Script raises` (the traceback names the file and line 9);
  - `Inputs not changed`;
  - `Chain scripts locally`;
  - `Helper defined only in the notebook` (`NameError`);
  - `Missing contract function`;
  - `Rate by municipality checked locally` (dashboard facts).

  Also verify that an indicator checked without `name` is rejected, that `show=False` displays nothing, and the text and HTML summaries.

## 3. Download kit (analytics)

- [x] 3.1 Add `tamiza/kit.py` with `LIBRARIES`, `requirements.txt`, the manifest of design D4, and the layout with `tamiza_sdk/*.py` copied from the installed package. The zip holds one `tamiza-sample-<timestamp>/` folder, with Parquet files stored and text deflated (design D4, D5). Verify unit tests for:
  - the layout;
  - the scenario `Libraries pinned to the server` (versions equal to `importlib.metadata`, no `psycopg`);
  - `LIBRARIES` matching the dependencies in `pyproject.toml` minus `psycopg`;
  - the manifest of the scenario `Manifest of a download`;
  - no `clean/` folder when the status is `failed` or `none`;
  - the Parquet entries stored as `ZIP_STORED`.
- [x] 3.2 Add the README and `getting-started.ipynb` templates under `tamiza/kit/` as package data, with plain-Python code cells (design D4). They cover setup with Python 3.12, the three functions, the loop through "Upload .py", and keeping the data safe. Verify with a test that extracts a written kit to a temporary folder and runs every code cell of the notebook with `exec`, in a `python -I` subprocess whose working directory is that folder and in which `tamiza`, `psycopg` and `duckdb` cannot be imported. The run must end with a successful check of the example indicator (scenario `No network`). Also verify that the wheel built in 1.1 contains both templates.

## 4. Schema (api)

- [x] 4.1 Add `tamiza-api/src/dbUp/Scripts/0010_add_local_dev_kit.sql` with `sample_downloads` and `sample_download_parts` (design D7), and map `SampleDownload` and `SampleDownloadPart` in `TamizaDbContext`. Verify `SchemaDriftTests`, a `SchemaMigratorTests` upgrade from `0009` that keeps existing tests, and that deleting a project deletes its downloads and their parts.

## 5. Worker job (analytics)

- [x] 5.1 Extract `sample_runs.prepare_inputs(…, on_step)` from `_execute_steps` (the flatten and saved script 1 steps, with the cache). Tests use it with an `on_step` that writes `script_tests.steps` (design D6). Verify that `test_sample_runs.py`, `test_sample_workbench.py` and `test_templates.py` pass unchanged.
- [x] 5.2 Add the setting `TAMIZA_SAMPLE_DOWNLOAD_MB` (default 100, at least 1) to `settings.py`, `compose.yaml` and `.env.example`. Document it in `docs/operations.md`, with the one-hour lifetime and the worst-case database space (design D6, Risks). Verify `test_settings.py` for the default, a custom value, and rejected `0` and text values.
- [x] 5.3 Add `sample_downloads.py` (design D6). It claims a download with the current script 1 version and prepares it through `prepare_inputs`. It writes and zips the kit through `kit` and applies the size limit. It stores the parts and `contents` in one transaction. It fails interrupted downloads at start and deletes expired ones on each cycle. In `pipeline.Processor`, claim tests, then downloads, then runs. Verify integration tests for:
  - the scenarios `Analyst requests a sample` (published schemas and versions unchanged), `Form with a repeat group`, `Labels in the downloaded tables`, `Project without script 1` and `Deleted submissions`;
  - `Script 1 raises an exception` and `Download after a test` (both steps reused);
  - `Manifest of a download` and `Large reference tables` (`download.tooLarge`, no parts kept);
  - `Worker restarted while preparing` and `Expired download` (row and parts deleted);
  - `Download while a run is running` (order with a queued run) and `Test while a sample download is prepared` (order with a queued download);
  - the stored parts reassembling into a zip whose SHA-256 matches;
  - `Nothing secret in the download` (the zip searched for the test database's password, a stored Kobo token and a webhook secret).
- [x] 5.4 Add the parity integration test of the scenario `Same results as a sample test` (design D2). For an indicator, a script 2 and a script 1 (the docs' examples), it runs a sample test and a download of the same size, then `check` from the extracted zip in a subprocess. It compares each table's columns, types, row count and first 100 rows with the test's previews. Verify that it passes, and that it fails when `check` is made to skip the column renames.

## 6. API (api)

- [x] 6.1 Add `POST` and `GET /api/v1/projects/{projectId}/sample-downloads[/{downloadId}]`, with validation, replacement of the user's downloads that are not running, `waitingFor` and `expiresAt`. Add `ScriptTestWait.Download` for tests (design D8). Verify integration tests for:
  - the scenarios `Viewer requests a sample` (403), `Not a member` (404) and `Sample size out of range`;
  - `New request replaces a ready download` and `Another member's download` (404);
  - `Expired download` (404 after an hour, with `TimeProvider`);
  - `waitingFor` for a running run, a queued test and an earlier download;
  - a test waiting for a running download;
  - the routes in the OpenAPI document.
- [x] 6.2 Add `POST …/sample-downloads/{downloadId}/link` (design D8). It streams the parts into the export directory under an export slot, checks the length, and registers a single-use link named by `ExportFileName.Create(…, "sample", …, "zip")`. Verify integration tests for:
  - the scenario `Download the zip`: the bytes downloaded through `GET /api/v1/data-exports/{token}` equal the stored parts, with type `application/zip` and the attachment name;
  - `Link used twice` (404, then a new link works);
  - `Not ready yet` (409 `sampleDownload.notReady`) and a failed download (409 `sampleDownload.failed`);
  - parts shorter than `size_bytes`, which leave no file;
  - an expired download (404).

## 7. Scripts section and documentation (ui, docs)

- [x] 7.1 Build the UI of design D9:
  - `startSampleDownload`, `sampleDownload` and `sampleDownloadLink` in `pipeline.service.ts`;
  - the "Download sample" button in the test form;
  - `sample-download-panel.ts`: polling, waiting text, the ready summary, the script 1 warning, the data notice, the download through `Downloader`, and the expired state with "Prepare again";
  - `scriptTests.waitingForDownload` and the new strings in `en.json`.

  Verify specs for the scenarios `Prepare and download`, `Viewer`, `Script 1 failed` and `Download expired`, the sample size validation shared with "Test on sample", a test panel waiting for a download, and the panel's layout at phone width.
- [x] 7.2 Write "Writing scripts on your computer" in `docs/scripts.md` (design D10), and mention it where "Upload .py" is described. Make `test_docs_examples.examples()` skip blocks that mention `tamiza_sdk`, and add a test that runs those blocks in order in a download's folder. Verify that both docs tests pass, and that the documented commands (`pip install -r requirements.txt jupyterlab` and the three functions) match the kit's README.

## 8. End-to-end verification

- [x] 8.1 Check the whole flow on a separate verification stack (Compose project `tamizaverify`, fake OIDC and Kobo, as in earlier changes):
  1. Seed a project with a repeat group, a script 1, an indicator and a reference table.
  2. Through headless Chrome, request a sample download, wait until it is ready and download the zip through the link.
  3. Extract the zip and run the starter notebook's cells in the `tamiza-analytics-test` image with `--network none`.
  4. Upload the example indicator with "Upload .py" and test it on a sample of the same size.

  Verify that the test succeeds with the same table as the local check, that a second use of the link responds 404, and that the download can no longer be read once its `finished_at` is moved back two hours with `psql`.
- [x] 8.2 Run the full analytics, API and UI suites. Verify that they pass, except the two known UI polling failures (`project-data.spec.ts`, `project-scripts.spec.ts`).
- [x] 8.3 If a Windows Python 3.12 is available, run the starter notebook of the downloaded zip on Windows after `pip install -r requirements.txt jupyterlab`. Verify that every cell runs; otherwise, record in this file that the check was not possible and why.
- [x] 8.4 Mark item 7c as implemented in `docs/tamiza-brief.md`, section 8. Verify that the line reads like items 7a and 7b.

## Workflow follow-up

- Archive the change with `/opsx:archive` after review, so the `local-dev-kit` spec is created and the `pipeline` delta is merged into `openspec/specs`.
- Commit the work; the user commits after reviewing.
