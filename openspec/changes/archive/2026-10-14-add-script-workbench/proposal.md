# Proposal

## Why

Writing a script in Tamiza today means typing into a plain editor without seeing the tables and columns the script receives, without completion, without Tab indentation, and with one feedback loop: a whole sample test that re-flattens the sample and re-runs script 1 every time and ends as failed until the function returns a valid result. Real data is about to be loaded (agreed order in `docs/tamiza-brief.md` section 8, item 7b), so the authoring loop has to become fast and guided before analysts write their first real indicators.

## What Changes

- **Inputs panel** beside the editor: the tables and columns the selected script receives, with their types, row counts and question labels, read from the schema the last run published (the raw tables for script 1, the clean tables for script 2 and the indicators), plus the project's reference tables and the form's questions. A new read-only API endpoint serves it.
- **Editor assistance**: completion of `ctx` members, input table names, column names, reference table names and question names; Tab and Shift+Tab indent and outdent with four spaces, with a documented key to leave the editor; basic linting that never blocks saving (syntax errors, a missing or misnamed contract function, mixed tabs and spaces, unknown `ctx` members, input and reference tables the project does not have).
- **Inline `ctx` reference**: signatures and descriptions in completions, on hover, and in a reference tab of the side panel.
- **Templates**: a menu of several templates per script kind instead of one, written with `# %%` cells and `ctx.show`, kept as Python files that an automated test runs.
- **`ctx.show(data, name)`**: a script can show a DataFrame, Series or value; in a sample test each call becomes a named preview with the line it came from, kept even when the script fails afterwards; in a run it does nothing.
- **Run through a cell**: `# %%` markers split a script into cells. "Run cell" re-runs the draft from the start, in a fresh process, and stops when execution reaches the end of that cell, without validating the output, showing the previews and log produced so far. There is no persistent kernel.
- **Cached sample**: a test reuses the flattened sample, and the result of the saved script 1, from an earlier test when nothing they depend on changed (submissions in the sample, form, labels, reference tables, script 1 version), so repeated tests and cell runs skip those steps. The cache lives in the worker's working directory with a size limit.
- **"As the dashboard will see it"** for an indicator test: the columns classified the way the indicators dashboard will offer them (measures, default measure, ratio, group columns, geometry columns), warnings for common mistakes, and a stat tile and a bar chart by a chosen group column computed over the whole test table and shown with the indicator's format and unit.
- `docs/scripts.md` gains sections on cells, `ctx.show`, the inputs panel and the dashboard view.

No breaking changes: saved scripts, runs and the test API keep working; a script that never calls `ctx.show` or uses markers behaves as before.

## Capabilities

### New Capabilities
- `script-editor`: the Scripts section's editor and its side panel: the inputs panel and the endpoint behind it, completion, indentation, linting, the inline `ctx` reference, templates and the cell actions.

### Modified Capabilities
- `pipeline`: `ctx` gains `show`; the sample test gains running through a cell, previews from `ctx.show`, reuse of a cached sample, and the dashboard view of an indicator's table; the requirement "Sample test of one script" now allows reuse and stopping at a cell.

## Impact

- **tamiza-ui**: `shared/ui/code-editor.ts` (completion, lint, hover, Tab keymap and cell widgets as CodeMirror extensions), `project-scripts.ts` (side panel, template menu, cell actions), `script-test-panel.ts` (previews from `ctx.show`, the stopped state, the dashboard view reusing the indicators dashboard's chart and format code), `pipeline.service.ts`, translations, template `.py` files with a text loader in `angular.json`. Adds `@codemirror/autocomplete`, `@codemirror/lint`, `@codemirror/language`, `@codemirror/view`, `@codemirror/state` and `@codemirror/commands` as direct dependencies, pinned to the versions `codemirror` already installs.
- **tamiza-api**: a new `GET /api/v1/projects/{projectId}/script-inputs` endpoint; `script-tests` accepts an optional `stopAtLine` and returns it with each test; a DbUp migration adds `stop_line` to `script_tests`; test step status gains `stopped`, and test steps gain `shown` and, for indicators, `dashboard`.
- **tamiza-analytics**: `runner.py` (`ctx.show`, stopping at a marker), `launcher.py` (collecting previews even when the child is stopped), `sample_runs.py` (sample cache, stop line, dashboard view), `preview.py`; a new setting `TAMIZA_TEST_CACHE_MB`.
- **Deployment**: `compose.yaml` and `.env.example` document `TAMIZA_TEST_CACHE_MB`.
- **Docs**: `docs/scripts.md`; its examples stay covered by `test_docs_examples.py`.
- Out of scope, as agreed: a persistent kernel, downloading test inputs and a local SDK (`add-local-dev-kit`), a helper module shared by indicator scripts.
