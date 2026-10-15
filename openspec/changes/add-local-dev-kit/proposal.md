# Proposal

## Why

The script workbench made writing scripts in the browser faster, but each look at the data is still a round trip through the worker: a new process that starts the script from the beginning, with no variables kept in between. Analysts who already work in Jupyter want to explore the data in their own notebook, with variables that persist between cells, and then upload a script that behaves the same on the server. This is the last item agreed before real data is loaded (`docs/tamiza-brief.md` section 8, item 7c). It reuses the sample test's machinery and the runner, which never touches the database, both delivered in `add-script-workbench`.

## What Changes

- **Download a sample** in the Scripts section. Next to "Test on sample", analysts and admins can request a sample of the project to work on locally, using the same sample-size field. The worker prepares it ahead of queued runs, reusing the sample a test cached: the flattened sample (script 1's input), the saved script 1's result on it (the input of script 2 and of every indicator), the project's reference tables, and `context.json` (the form dictionary, label codes and column labels a script's `ctx` is built from). If the saved script 1 fails on the sample, the download still succeeds with the flattened tables, and it says so.
- **One zip per download, ready to open.** Besides the data, it contains the `tamiza_sdk` package from the server that prepared it, a `requirements.txt` that pins the server's library versions, a `manifest.json` describing the contents, a README and a starter notebook. Nothing is installed from or published to a package index.
- **Single-use link.** A prepared sample is kept for one hour and downloaded through the existing single-use links (`GET /api/v1/data-exports/{token}`, five minutes, one use each). Each analyst keeps at most one per project, and a new request replaces the previous one. A size limit set by the deployment (`TAMIZA_SAMPLE_DOWNLOAD_MB`, default 100) keeps large reference tables from filling the database.
- **`tamiza_sdk`**, built from the code the runner already uses, which never touches the database:
  - `load_context(folder)` builds the same `ctx` a script receives: the form dictionary, the reference tables and `ctx.log`. `ctx.show` displays the value in the notebook.
  - `load_tables(source, folder)` gives the `raw` or `clean` tables as dictionaries of DataFrames, read the same way the runner reads them, so the column types are identical.
  - `check(script, kind, name=None, ...)` runs a contract function, or a `.py` file loaded the way the server loads scripts, on the sample. It applies the run's own validation and messages, and returns the tables as they would be published. For an indicator, it also returns what the indicators dashboard will offer: measures, default measure, ratio, dimensions and geometry columns.
- **Upload** stays the existing "Upload .py" button. The SDK never contacts the server.
- **Refactor, no behavior change.** The runner's database-free parts move into the `tamiza_sdk` package inside `tamiza-analytics`: the `ctx` classes, the contract and output validation, Parquet reading and writing, name rules, and the dashboard facts. The worker imports them from there, so the server and the SDK cannot drift apart.
- A test waiting in the queue can now also wait for a sample being prepared, and its status says so.
- `docs/scripts.md` gains "Writing scripts on your computer". `docs/operations.md`, `compose.yaml` and `.env.example` document the size limit and where prepared samples are stored.

No breaking changes: scripts, runs, tests and their API keep working as they do today.

## Capabilities

### New Capabilities
- `local-dev-kit`: downloading a project sample for local work (request, preparation by the worker, contents and format of the zip, size limit, lifetime, single-use download, who can download it) and the `tamiza_sdk` functions that read it and check a script the way a run does.

### Modified Capabilities
- `pipeline`: in "Test queue and lifetime", the worker also prepares sample downloads, after queued tests and before queued runs; a waiting test can wait for a sample being prepared, and its status says so.

## Impact

- **tamiza-analytics**: a new top-level package `src/tamiza_sdk/` in the same distribution (`context.py`, `contract.py`, `tables.py`, `names.py`, `dashboard.py`, `bundle.py`, `notebook.py`). `runner.py`, `interchange.py`, `names.py` and `preview.py` import their database-free parts from it. New modules `sample_downloads.py` (claim, prepare, store, expire) and `kit.py` (writes the folder and the zip), with the README and notebook templates as package data. `sample_runs.py` shares its flatten and saved-script-1 preparation with the downloads. `pipeline.Processor` claims tests, then downloads, then runs. New setting `TAMIZA_SAMPLE_DOWNLOAD_MB`.
- **tamiza-api**: migration `0010_add_local_dev_kit.sql` (`tamiza.sample_downloads` and `tamiza.sample_download_parts`); endpoints `POST /api/v1/projects/{projectId}/sample-downloads`, `GET …/sample-downloads/{downloadId}` and `POST …/sample-downloads/{downloadId}/link`, which copies the stored zip into the export directory and registers a link in `ExportRegistry`; `ScriptTestWait` gains `download`.
- **tamiza-ui**: a "Download sample" action and a status panel in the Scripts section (`project-scripts.ts`, a new `sample-download-panel.ts`), `pipeline.service.ts`, `en.json`, and the waiting text of tests.
- **Data protection**: the zip holds submissions of the project. Analysts can already read every submission through scripts and tests, so the download does not widen who can see the data, but it lets them copy it off the server. The UI and the README say so. `add-audit-log` should record sample downloads along with the other operations it covers.
- **Deployment and docs**: `compose.yaml`, `.env.example`, `docs/operations.md`, `docs/scripts.md`, and the status of item 7c in `docs/tamiza-brief.md`.
- **Out of scope**: publishing the SDK on PyPI or serving it as a wheel; downloading the full published tables instead of a sample; the SDK calling the API (downloading or uploading from Python); the dashboard view's warnings in `check` (a sample test in Tamiza still shows them); time and memory limits locally; turning downloads off for a deployment or a project.
