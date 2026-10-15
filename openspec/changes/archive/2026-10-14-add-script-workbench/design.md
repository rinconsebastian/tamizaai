# Design

## Context

See proposal.md for the motivation. The pieces this change builds on:

- **Editor.** `tamiza-ui/src/app/shared/ui/code-editor.ts` wraps CodeMirror 6 (`codemirror` 6.0.2 `basicSetup` plus `@codemirror/lang-python` 6.2.1). `basicSetup` already brings autocompletion, the lint keymap and Python's keyword and local-name completions; nothing binds Tab, so Tab leaves the editor. `project-scripts.ts` owns the selected script, the draft, the metadata fields, save, versions and the "Test on sample" form; `script-test-panel.ts` polls a test and shows each step's log and table previews. Templates are three strings in `pipeline.service.ts`.
- **Tests.** `POST /api/v1/projects/{projectId}/script-tests` queues a row in `tamiza.script_tests` (kind, name, code, sample size). The worker (`sample_runs.py`) claims it ahead of runs, flattens a hash-ordered sample, runs the saved script 1 first for script 2 and indicators, runs the draft through `launcher.run_script` (child process `python -I -u -m tamiza.runner <step dir> <kind> [<name>]`, clean environment, time and memory limits, step directory removed afterwards), and writes each step's JSON, with previews from `preview.step_tables`, into `script_tests.steps`. The API reads `steps` as `List<TestStep>`, so fields it does not declare are dropped.
- **Published tables.** The worker publishes `p_<id>_raw` (flatten step) and `p_<id>_clean` (clean step, the flattened tables when there is no script 1), with question labels as column comments. The data dashboard reads PostgreSQL's catalog (`TableCatalog`, `DataDashboard.TableNamesAsync`) and row counts from the latest succeeded run step (`DataDashboard.LatestStepAsync`), inside read-only transactions with a statement timeout; it does not offer the raw schema.
- **Indicators dashboard rules.** A measure is an `integer` or `decimal` column (`IndicatorValidation.IsNumeric`); the default measure is `value`, else the first numeric column; a new widget groups by the first non-numeric column; any text column can be a map's geometry; a bar chart orders groups by the group value, empty last. `format.ts` (`valueFormatter`) formats values by format and unit, and `AxisChart` and `StatWidget` render `QueryRow[]`.

## Goals / Non-Goals

**Goals:**
- Every editor aid works from data the page already has or one new read-only endpoint; nothing in the editor needs the worker.
- The checks are pure functions over the Lezer syntax tree, unit-testable without a browser.
- A test through a cell has exactly the semantics of a whole test up to the marker: same process isolation, contract, `ctx`, limits and tracebacks with the script's own line numbers.
- Reuse never changes an outcome: a cache key covers everything flattening and the saved script 1 read.

**Non-Goals:**
- A persistent kernel, variables kept between tests, or running code outside the worker.
- Type inference: a column completion only follows a variable assigned directly from a table; there is no check of column names.
- Python-accurate linting: the checks are hints; CPython in the worker stays the authority.
- Downloading test inputs or a local SDK (`add-local-dev-kit`).

## Decisions

### D1. Script inputs endpoint

`GET /api/v1/projects/{projectId}/script-inputs?kind=clean|output|indicator`, role `viewer`, tag `Pipeline`. A separate path rather than `/scripts/inputs`, which would sit among the `/scripts/{kind}` routes. Response:

```json
{
  "source": "raw",            // "raw" for clean, "clean" for output and indicator
  "publishedAt": "…", "runId": "…",   // null when nothing is published
  "tables": [{ "name": "main", "rows": 500, "columns": [{ "name": "has_water", "type": "text", "label": "Does the household…" }] }],
  "referenceTables": [{ "name": "population", "columns": [{ "name": "population_2026", "type": "integer" }] }],
  "questions": [{ "name": "municipality", "path": "municipality", "type": "select_one", "label": "Municipality" }]
}
```

Tables and columns come from one catalog query over the schema (`pg_class`, `pg_attribute`, `col_description`, ordered by table name and `attnum`), run through `DataDashboard.InTransactionAsync` so it gets the statement timeout and the read-only transaction; catalog reads take no lock on the published tables, so a publish never waits for the panel. The schema name is built from the route's project id and one of two fixed words, never from request text. Row counts and `publishedAt` come from `LatestStepAsync` with the step `flatten` or `clean`; a table missing from the step's list gets `rows: null`. Column types use the dashboard's `ColumnTypes.FromPostgres`. Reference tables come from `reference_tables.columns`, questions from `projects.form_fields`.

*Alternatives:* the latest test's input tables (rejected by the user: empty until a test runs and gone after 24 hours); extending the data overview (it hides the raw schema and has no columns).

The UI loads the inputs once per source (`raw` or `clean`) while the page is open, again when the runs list sees a run end, and on a refresh button in the panel.

### D2. Editor extensions and the analysis module

`CodeEditor` stays generic and gains an `extensions` input (applied through a `Compartment`, like `readonly`), an `insertAtCursor(text)` method and a `problems` output. Everything Python-specific for scripts lives in `pages/projects/script-editor/`:

- `script-analysis.ts`: pure functions over `pythonLanguage.parser.parse(text)` and the text: `cells(text)` (marker lines by `/^[ \t]*# %%/`), `contract(tree, kind)` (the top-level `def` of `clean`/`prepare_output`/`compute`, its parameter names), `aliases(tree, contract)` (names assigned `<first param>["t"]`, optionally `.copy()`), `subscriptContext(tree, pos)` (what a string inside `[...]` indexes: the first parameter, an alias, `ctx.reference`, a reference table, or anything else), `attributeChain(tree, pos)` (for `ctx.form.label`), and `check(tree, text, kind, inputs)` returning diagnostics. Vitest runs these in Node with no DOM.
- `ctx-members.json`: the members of `ctx`, `ctx.form`, `ctx.log` and `ctx.reference` with their kind and signature, the single list behind completion, hover, the unknown-member check and the reference tab. Descriptions and examples are translation keys in `en.json` (`scripts.ctx.<path>`). An analytics test compares the JSON with the public attributes of `runner.Context`, `runner.Form`, `runner.ReferenceTables` and the `logging.Logger` methods it lists, so the reference cannot drift from the runner.
- `script-assist.ts`: builds the CodeMirror extensions from `{ kind, inputs, canRun, onRunCell }`: a completion source registered with `pythonLanguage.data.of({ autocomplete })` (it adds to Python's own sources and handles string nodes, which those skip), `linter(…, { delay: 500 })` with `lintGutter()`, `hoverTooltip` over `ctx` chains, the cell widgets, the keymap, and `indentUnit.of("    ")`.

The checks the spec lists map to: Lezer error nodes (`⚠`) → syntax error at the node; no contract `def` → error on line 1; parameter count ≠ 2 → warning on the `def`; a line whose leading whitespace has both a tab and a space → warning; an attribute of the contract's second parameter (or of `ctx.form`/`ctx.log` through it) not in `ctx-members.json` → warning listing the members; a string subscript of `<ctx>.reference` not among the reference tables → warning; a string subscript of the first parameter not among the input tables and not the target of a subscript assignment on that parameter anywhere in the script → warning, only when inputs are published. The problems list under the editor reads the lint state (`forEachDiagnostic`) and moves the cursor to a problem when activated.

*Alternatives:* server-side linting with `ruff` (the API cannot reach `analytics` except through the job queue, so it would be as slow as a test); Pyodide in the browser (megabytes of download for hints); column-name checks (false positives on columns the script adds would teach analysts to ignore the marks).

### D3. Keyboard

A `Prec.high` keymap: Tab runs `indentMore` when a selection spans text, otherwise inserts spaces to the next multiple of four columns; Shift-Tab runs `indentLess`; Mod-Enter runs the cell holding the cursor (it overrides `basicSetup`'s `insertBlankLine` only while running is possible). Leaving the editor uses CodeMirror's built-in tab-focus mode: Escape makes the next Tab, within two seconds, move the focus. A hint under the editor says so. `EditorState.tabSize` stays 4.

### D4. Templates as Python files

Templates move to `tamiza-ui/src/app/pages/projects/script-templates/<kind>/<id>.py`, imported as text through `"loader": { ".py": "text" }` in the application build options of `angular.json` (the unit-test builder takes its options from the build target) and a `declare module '*.py'` typing. `templates.ts` lists them with their kind and the translation keys of their name and description. Each starts with constants naming the columns to adapt (`GROUP = "_version"  # change to a column of cleaned["main"]`) and uses only the system columns, so it runs on any form. Initial set: script 1 *Drop test submissions* and *Fix types and recode*; script 2 *Columns for the dashboard* and *Join a repeat group to main*; indicators *Count by group*, *Rate by group* (`numerator`, `denominator`, `value`) and *Monthly series* (by month of `_submission_time`). An analytics integration test runs every `.py` under that folder in a sample test of a form with only a main table and expects success, the same way `test_docs_examples.py` reads `docs/scripts.md`. The files live inside `tamiza-ui` because the UI image builds from that folder.

Choosing a template asks for confirmation when the draft is dirty, then calls `editor.replace` (one undoable transaction).

### D5. `ctx.show` in the runner

The runner gains options: `python -I -u -m tamiza.runner <step dir> <kind> [<name>] [--show] [--stop-line N]`. Without `--show` (runs), `Context.show` returns its argument and does nothing else. With it:

- The caller's line is the first frame up the stack whose file is `script.py`.
- A DataFrame (or Series, via `to_frame()`) is snapshotted as `head(100)` through the same column normalization as outputs and written with `interchange.write_parquet` to `shows/<n>.parquet`; any other value becomes `repr()` cut at 2,000 characters. If writing fails, the preview is the text of the error.
- After each call, one JSON line `{"name", "line", "rows", "file" | "text"}` is appended and flushed to `shows/index.jsonl`, so the previews survive an exception or a kill by the time or memory limit.
- After 20 calls, later ones only increase a counter; the runner prints one `INFO` line with the count at the end.

`launcher.run_script` gains `show: bool` and returns `ScriptOutcome.shown` (the index entries, their files moved to the caller's out directory) before it removes the step directory, whatever the outcome. `sample_runs` passes `show=True` for the draft only, never for the saved script 1, and turns entries into previews with `preview.table_preview` (the recorded `rows` replaces the file's row count) or a text preview. Each test step JSON gains `shown: [{ name, line, rows, table | text }]`.

*Alternative:* building the preview JSON in the child: it would duplicate `preview.py`'s value handling, and a killed child would leave nothing.

### D6. Stopping at a marker

The runner compiles the script from an AST in which a call `__tamiza_stop__()` is inserted at the marker; the namespace defines `__tamiza_stop__` to raise `CellStop(BaseException)`, which `run_step` reports as `{"status": "stopped", "line": N}` with no output written and no validation. The inserted node takes the marker's line and every other node keeps its own, so tracebacks are unchanged.

Placement: the marker's column is the indentation of its `#`. The runner walks statement lists (module, function, class, `if`/`for`/`while`/`with`/`try` bodies and their `else`/`finally`/handlers, `match` cases) and picks the deepest list whose statements start at that column and for which the marker lies after the end of one statement and before the start of the next, or after the end of the last one when no enclosing statement starts before the marker. No such list (the marker sits inside a multi-line expression or a string) fails the step with the message the spec gives.

The API checks only that `stopAtLine` is between 1 and the number of lines of `code` and that the line matches the marker rule, and stores it in a new nullable column `script_tests.stop_line` (`0009_add_script_workbench.sql`, `CHECK (stop_line > 0)`); `ScriptTestDetails` returns `stopAtLine`. The worker passes `--stop-line` for the draft only. The test step status gains `stopped`, used only by tests; the test itself ends `succeeded`.

*Alternatives:* `sys.settrace` (a Python call per frame entry, pandas included, and a stop that fires in a helper defined below the marker); `sys.monitoring` LINE events (cheap, but the same ambiguity about which frame should stop, and raising from a callback is awkward); cutting the source at the marker (loses helpers defined below and breaks blocks that continue after it).

A stop raised inside user code wrapped in `except BaseException` or a bare `except:` is swallowed; `docs/scripts.md` says so.

### D7. Reusing the sample

The sample query is split: first `SELECT id, updated_at, form_version … ORDER BY md5(kobo_uuid), kobo_id LIMIT n` (no JSONB), then, only on a miss, the `data` of those ids in that order. Labels and question labels are loaded as today, before hashing.

- **Flatten key**: SHA-256 over a format version, the package version, the project id, the sample's `(id, updated_at, form_version)` list, `form_fields` and `form_choices`, the labels' content (`label_codes()` and the per-version choices it was built from) and the question labels.
- **Clean key**: the flatten key, the claimed `clean_version_id` and the reference tables' `(name, updated_at)`.
- **Entry**: `<run_work_dir>/sample-cache/<key>/` with the step's Parquet files and a `step.json` holding what the test step and later steps need (table list with column labels, the step JSON with previews, `ctx` columns, `label_codes`, input rows). Built in a `.tmp-<uuid>` directory and renamed, so a crash never leaves a half entry.
- **Use**: a hit copies nothing: later steps read the cached Parquet as inputs (`run_script` already copies inputs into the step directory). The step JSON gets `reused: true` and its first log plus `Reused from an earlier test.`; its duration is the lookup's.
- **Limits**: `TAMIZA_TEST_CACHE_MB` (default 512, `0` turns reuse off). After writing an entry, the least recently used entries (by the mtime touched on each hit) are removed until the total fits; an entry larger than the limit is not kept. Entries unused for 24 hours, the test lifetime, are removed at claim time, so the data of a deleted project does not linger. Leftover `.tmp-*` directories are removed when the worker starts.

Reference tables are still loaded for every test (they are small and the scripts read them from the step directory).

*Alternatives:* caching in PostgreSQL (more I/O on the shared database for a single-worker optimization); keeping a process alive between tests (the persistent kernel the brief defers).

### D8. Dashboard view

When the draft of an indicator succeeds, `preview.dashboard_view(path)` reads the output Parquet and returns a `dashboard` object in the step JSON:

```json
{ "measures": ["value", "numerator", "denominator"], "defaultMeasure": "value",
  "ratio": true, "dimensions": ["municipality"], "defaultDimension": "municipality",
  "geometry": [], "rows": 12, "defaultMeasureNulls": 0, "defaultMeasureMax": 0.93,
  "totals": { "value": 6.1, "numerator": 3810, "denominator": 5022 },
  "duplicateRows": 0,
  "charts": { "municipality": { "values": 12, "groups": [{ "group": "Cali", "value": 0.61, "numerator": 310, "denominator": 508 }] } } }
```

Measures follow `preview.column_type` (`integer`, `decimal`), which matches the dashboard's types after publishing. Geometry columns are text columns whose first 20 non-empty values match a WKT prefix (`POINT`, `LINESTRING`, `POLYGON`, their `MULTI` forms, `GEOMETRYCOLLECTION`), without importing geopandas. `charts` holds, for the first 10 dimensions with at most 50 distinct values (empty counts as a value), the sums per value of the default measure and, when the ratio exists, of `numerator` and `denominator`, ordered by value with empty last; a dimension with more values is listed with `values` and no groups. `duplicateRows` counts rows beyond the first for each combination of dimension values.

The UI turns these facts into the warnings, so their texts stay in translations and the percent check can use the format the analyst is editing (the test request does not carry metadata). It renders the stat tiles and the bar chart with `StatWidget` and `AxisChart`, building `QueryRow[]` and a `WidgetConfig` from `charts`, and formats with `valueFormatter` from the metadata fields' current values, so a format change re-renders without a new test.

### D9. API models

`TestStep` gains `Reused` (bool, default false), `StoppedAtLine` (int?), `Shown` (`List<ShownPreview>?`: name, line, rows, `TestTable? Table`, `string? Text`) and `Dashboard` (`IndicatorTestView?`, typed so OpenAPI documents it); `ScriptTestRequest` and `ScriptTestDetails` gain `StopAtLine`. Old rows deserialize with the new fields empty.

## Risks / Trade-offs

- [Lezer and CPython disagree on rare syntax] → syntax marks are hints, never block saving (spec), and the message says the sample test is authoritative.
- [AST placement picks the wrong block for an oddly indented marker] → placement uses the marker's own indentation; unit tests cover markers at function level, in loops, after a block's last statement and inside an expression; a marker that cannot be placed fails with a clear message instead of guessing.
- [The cache serves data the key does not cover] → the key includes every input flattening and script 1 read, plus the package version; integration tests change each input (a submission, script 1, a reference table, a form version's labels) and expect recomputation; `TAMIZA_TEST_CACHE_MB=0` turns reuse off.
- [Disk use on a small VPS] → bounded by `TAMIZA_TEST_CACHE_MB`, removed after 24 hours unused, and stored in the container's work directory that is discarded on recreation.
- [Previews written by the child cost time on large frames] → only `head(100)` is written, and at most 20 per test.
- [The view's group order differs from PostgreSQL's collation for accented text] → only the order of bars in a preview; noted in `docs/scripts.md`.
- [The `.py` text loader does not reach the unit-test builder] → task 5.1 verifies it first; fallback: a small Node script generating `templates.generated.ts` from the `.py` files before build and test.
- [The panel lags script 1 changes until the next run] → the panel shows the publish time (spec scenario "Script changed after the last run"); the test preview shows the draft's real inputs.

## Migration Plan

`0009_add_script_workbench.sql` adds the nullable `script_tests.stop_line`; old API and worker versions ignore it, so the deploy order does not matter. Steps written before the change lack `shown`, `dashboard` and `reused` and read as empty. Rolling back leaves the column unused. No published data changes. Templates replace the old three; saved scripts are untouched.

## Deviations from the plan

- **`problems` as a callback, not an output.** `CodeEditor` gained `extensions`, `insertAtCursor`, `cursorLine` and `goTo`, but the problems list is fed by a callback of the assist extensions (`AssistOptions.problems`) from the linter itself, so the editor stays free of lint types (D2).
- **Reuse is keyed by the sample's submissions, not by its requested size.** A project with fewer submissions than two sample sizes uses the same submissions for both, so the second test reuses the first (same result, so reuse is correct). The spec scenario "Different sample size" holds when the project has more submissions than the smaller size; the integration test uses 40 submissions with samples of 10 and 20.
- **A running test protects its own cache entries.** `SampleCache.put` takes a `protect` list so that storing script 1's result never evicts the flattened sample the same test is still reading (D7 only said the new entry is kept).
- **Geometry columns stay in `dimensions`.** The facts list every non-numeric column as a dimension, as the dashboard does, and `geometry` is the subset that holds WKT; charts skip them (D8).
- **Dimension-like names are detected on measures by name only.** The facts carry no per-measure type, so the warning for a numeric column named `year`, `code`, `id`… also fires for a decimal column with such a name; the spec says integer.
- **Hover and lint are pure exported functions** (`hoverTarget`, `lintProblems`) so they are tested without layout; the CodeMirror tooltip and linter only present their results.
- **The side panel sits in the right column above Versions** (`lg:grid-cols-4` stays), and below the editor on a phone.

## Verification Results

Real stack in a separate Compose project (`tamizaverify`, port 8189, images tagged `verify`), a fake OIDC provider and a fake Kobo on the host, 2,000 submissions inserted with SQL into a form with a repeat group, and headless Chrome over the DevTools protocol. Script 1 pauses 1.5 s so that reusing its result is measurable on this small data. The user's own stack was not touched; the verification stack, its volumes and images and the fake servers were removed afterwards.

- **Migration.** The API applied `0009_add_script_workbench.sql` on start; one test row ended with `stop_line = 24`.
- **First run.** Succeeded: flatten 7,000 rows (`main` 2,000 and `members` 5,000), clean 6,600, `piped_water_rate` 3 rows.
- **Inputs panel.** For script 1: "The flattened tables script 1 receives", `main` 2,000 rows and `members` 5,000 rows, the publish time, the main columns with types and the question labels (`has_water` with "Does the household have running water?"), and the form's six questions. After selecting the indicator it switched to the clean tables: `main` 1,600 rows (script 1 drops `enum0`) and `members` 5,000.
- **Completion.** Typing `cleaned["main"]["` offered the nine columns of `main`, each with type and table (`has_water` `text, main`, `_id` `integer, main`…). The template menu of an indicator listed *Count by group*, *Rate by group* and *Monthly series* with their descriptions.
- **Run cell.** The rate script showed three "Run cell" buttons. Running the second stopped at line 24 (the third marker): the step was `stopped`, the test succeeded with no validation, and the previews were `main` (line 11, 1,600 rows) and `rate` (line 22, 3 rows); the page said "Ran through the cell Count the households with piped water in each municipality and stopped at line 24…".
- **Reuse.** The test through the cell computed flatten in 0.2 s and script 1 in 2.0 s. The two whole tests that followed (sample 2,000) showed both steps as *Reused* with 0.0 s, the same row counts (7,000 and 6,600) and the log first recorded, and ran only the indicator (0.5 s each). The cache directory held four entries, 376 KB.
- **Dashboard view.** For `piped_water_rate` (format `percent`): measures `value`, `numerator`, `denominator` with `value` by default, a rate available, dimension `municipality`; the default measure summed gave `150.37%` and the ratio of sums `50.13%`; the bar chart by municipality gave Buga 50.19%, Cali 50.09%, Palmira 50.09%; no warnings. The gap between 150% and 50% is the reason `numerator` and `denominator` exist.
- **Intermittent test-runner crash.** On this Windows machine the UI test worker died at start about one run in four (exit code 3221225477) while the same files passed in the next run; both full UI runs that completed had exactly the two known polling failures.
