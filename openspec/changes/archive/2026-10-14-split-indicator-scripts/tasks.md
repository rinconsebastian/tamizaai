# Tasks

## 1. Schema (api)

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0008_split_indicator_scripts.sql` (design D1): delete `indicators` script versions, tests and run steps, every `dashboard_widgets` row and every `p_<32 hex>_indicators` schema; `script_versions` gains `name`, `label`, `description`, `unit`, `format` with their checks and the new unique index, kind `indicator`; `runs` drops `indicators_version_id` and gains `indicator_versions jsonb`; `run_steps` gains `name` in its primary key and the step `indicator`; `script_tests` gains `name`; mirror in `TamizaDbContext` (`ScriptKind.Indicator`, the new properties, `RunStep` key); verify `SchemaDriftTests`, `ScriptConventionTests` and a `SchemaMigratorTests` upgrade from `0007` seeded with a script 3, a widget and an indicator schema, asserting all three are gone

## 2. Indicator scripts in the worker (analytics)

- [x] 2.1 Add the `indicator` kind to `runner.py` (`compute`, one DataFrame, at least one numeric column, the table named after the indicator) and `names.py`, and to `launcher.run_script`'s callers (design D2); verify `test_runner.py` for the scenarios `Indicator returns a dictionary`, `No numeric column`, `Rate by municipality` (four columns published), geometry still as WKT, and a numeric-only nullable column accepted
- [x] 2.2 Add `publish.publish_table` (per-table staging and swap with the same lock-timeout loop) and `publish.drop_tables_except` (design D3); verify integration tests for the scenarios `Export of another indicator during a publish` (a reader of table A does not delay B), a reader of the same table delaying the swap within the limit, the timeout failing the indicator with the previous table kept, and `Indicator removed`
- [x] 2.3 Change `pipeline.claim` to resolve `indicator_versions`, and `_execute_steps` to run the indicators phase after `output` one at a time in name order, recording `indicator`/`name` steps, skipped indicator steps when flatten or clean fails, and the run's error naming the failed indicator (design D3); verify integration tests for the scenarios `One indicator fails`, `Script 2 fails`, `Script 1 fails` (indicator steps skipped), `Last indicator removed`, `No scripts at all`, `Run uses the current versions`, `Indicator added during a run`, and the per-step durations in the log
- [x] 2.4 Change `sample_runs` to test one indicator by name, naming its output table (design D4); verify the scenarios `Testing an indicator`, `Script 1 fails in a test of an indicator` and `Invalid output in a test`

## 3. Script, test and run endpoints (api)

- [x] 3.1 Rework `ScriptEndpoints` (design D4): the list with `indicators`, the `/scripts/indicators/{name}` routes, metadata validation (name rule, label 1–120, description ≤ 1,000, unit ≤ 32, format), the 100-indicator limit, `ScriptVersionDetails` with metadata; verify integration tests for the scenarios `Indicator created`, `Invalid name`, `Indicator limit`, `Metadata restored with a version`, `Indicator versions are per indicator`, `Viewer saves`, and that the OpenAPI document lists the routes
- [x] 3.2 Add `name` to `ScriptTestRequest`/`ScriptTestDetails` (required for `indicator`), and rework `RunEndpoints` models (`RunVersions.Indicators`, step `name`, indicator steps ordered by name) (design D4); verify integration tests for a test without a name rejected, `Run details` with indicator steps by name, and `Run uses the current versions`

## 4. Indicators dashboard API (api)

- [x] 4.1 Rework the `Indicators` module (design D5, D6): overview listing the indicators with metadata and publish state per table, `GET /indicators/{name}/columns` with `numeric` and `defaults`, the query over one table with `measure` or `ratio` and an optional `groupBy`, `MissingReferences` with `indicator`, and `POST /indicators/{name}/exports`; widget `config` validation per chart type (`groupBy` required where the spec says); verify integration tests for the scenarios `No indicator has published`, `Viewer opens a published dashboard`, `Default measure`, `Duplicate rows summed`, `Rate over several rows`, `Zero denominator`, `Row limit`, `Extra column grouped on`, `Indicator not published`, `Column removed by a script change`, `Viewer exports`, `Widget limit`, `Viewer cannot edit`, and a missing `groupBy` on a column chart rejected

## 5. Scripts page (ui)

- [x] 5.1 Rework `pipeline.service.ts` and `project-scripts.ts` (design D7): the indicator list, the add-indicator dialog with the name rule checked as the user types, the metadata fields above the editor, per-indicator save, remove, versions, restore and test, the `compute` template, and the run details with indicator steps by label and name; update `en.json`; verify `project-scripts.spec.ts` for creating an indicator, the dirty state from metadata, switching between indicators with unsaved changes, a test of an indicator sending its name, the 100 limit message, and the run details

## 6. Indicators dashboard (ui)

- [x] 6.1 Rework `indicators.service.ts`, the overview (indicator list with publish state), the widget dialog (indicator, measure or ratio, group-by, filters, geometry), the query and the widgets for one series, and add `format.ts` applied to the stat tile, axes, legends, tooltips and tables (design D6, D8); verify specs for the scenarios `Rate shown as a percentage`, `Count with a unit`, `Single figure`, `One indicator, several groups`, `Trend over a grouped column`, `Shares of a total`, `Indicator not published`, `Column removed by a script change`, and the dialog requiring a group column for a column chart

## 7. Docs

- [x] 7.1 Rewrite the indicator sections of `docs/scripts.md` and `docs/indicators-dashboard.md`, and update `docs/tamiza-brief.md` (sections 5.4, 5.7, 6 and 8 item 7a) and the README status (design D9); verify links resolve

## 8. Stack verification and release

- [x] 8.1 In a separate Compose project, create a project with 30 indicator scripts (one raising, one with no numeric column, one with geometry), run it, remove one indicator and run again, and export an indicator while another publishes; record in the design's Verification Results: the run status and error, which tables exist, the per-indicator durations, and that the export completed
