# Design

## Context

- **Script kinds today.** `script_versions (project_id, kind, number)` with `kind` in `clean|output|indicators`; `runs` carries `clean_version_id`, `output_version_id` and `indicators_version_id`, resolved when the run is claimed (`pipeline.claim`); `run_steps (run_id, step)` with `step` in `flatten|clean|output|indicators`; `script_tests.kind` in the same three. The API's `ScriptKind` and `RunStepName` enums, `pipeline.service.ts`'s `ScriptKind`, and the scripts page's three toggle buttons mirror them.
- **Execution.** `pipeline._execute_steps` runs flatten, clean, then `output` and `indicators` in a loop, each through `launcher.run_script` in its own process with its own limits; `_script` returns the output tables or marks the step failed. `runner._validated` is the whole contract: for `indicators`, one DataFrame, columns reduced, the five names present. `sample_runs` runs the same chain on a sample for one `kind`.
- **Publishing.** `publish.publish` loads every table of a step into `<schema>__next` and swaps schemas in one transaction with a 5 s lock timeout, retried with growing pauses up to `TAMIZA_PUBLISH_WAIT_SECONDS`; `publish.drop` removes a schema. There is no per-table swap.
- **Dashboard.** The `Indicators` module reads the one table `p_<id>_indicators.indicators` through `TableCatalog`; a widget's config is `{indicators[], groupBy, valueField, filters, geometryColumn, buckets}`, and `IndicatorRow` sums `valueField` per indicator and group; `publishedAt`/`runId` come from the latest succeeded `run_steps` row with `step = 'indicators'`. The export points the shared export machinery at that one table.
- **Naming.** `names.is_valid_table_name` (lowercase letters, digits, underscores, starting with a letter, at most 63 characters) is what scripts' table names must satisfy; `names.schema_name` builds `p_<32 hex>_<step>`.
- **No real data yet.** The user decided a clean cut: existing script 3 versions, their tests, the `indicators` run steps, every `p_<id>_indicators` schema and every widget are deleted by the migration.

Requirements are in `specs/pipeline` and `specs/dashboard-indicators`. User decisions for this change:
- The measure is any numeric column, `value` by default; `numerator` and `denominator` are optional and enable a ratio of sums.
- Metadata per indicator: label, description, unit, format.
- One indicator per widget; comparing indicators across tables comes later.
- No backward compatibility and no migration of existing indicator scripts or widgets.

## Goals / Non-Goals

**Goals:**
- One indicator failing never prevents the others from publishing, and never removes its own previous table.
- The contract an analyst must meet is one sentence: return a DataFrame with the dimensions you need and at least one numeric column.
- The dashboard stops guessing what a number is: the indicator says it.
- Everything per indicator reuses the per-step machinery that exists: process isolation, limits, logs, test runs, versions.

**Non-Goals:**
- Parallel execution of indicator scripts.
- Sharing code between scripts beyond what script 1 and 2 publish.
- Widgets over several indicator tables.
- Keeping the single `indicators` table readable.

## Decisions

### D1. Data model (DbUp `0008_split_indicator_scripts.sql`)

- `script_versions`: `ALTER` the check to `kind IN ('clean', 'output', 'indicator')`; add `name varchar(63)` (NULL for `clean` and `output`, required for `indicator`, checked with `CHECK ((kind = 'indicator') = (name IS NOT NULL))`), `label text`, `description text`, `unit varchar(32)`, `format varchar(16)` with `CHECK (format IS NULL OR format IN ('count', 'decimal', 'percent', 'currency'))`; replace the unique index with one on `(project_id, kind, name, number)` declared `NULLS NOT DISTINCT` (PostgreSQL 15 and later), so the clean and output scripts, whose name is NULL, still have one sequence of numbers; EF Core maps it with `AreNullsDistinct(false)`, which an expression index could not be. Metadata lives on the version, so a save records code and metadata together and a restore brings both back. The delete of `kind = 'indicators'` rows runs before the check changes.
- `runs`: drop `indicators_version_id` and its index; add `indicator_versions jsonb NOT NULL DEFAULT '[]'`, a list of `{name, versionId, number}` resolved when the run is claimed, in name order. A list rather than a join table: it is a snapshot read with the run, like `reference_tables` on the same row.
- `run_steps`: add `name varchar(63) NOT NULL DEFAULT ''`; primary key `(run_id, step, name)`; `step` check `IN ('flatten', 'clean', 'output', 'indicator')`; `CHECK ((step = 'indicator') = (name <> ''))`. Rows with `step = 'indicators'` are deleted first.
- `script_tests`: `kind` check as `script_versions`; add `name varchar(63)` with the same `(kind = 'indicator') = (name IS NOT NULL)` check; rows with `kind = 'indicators'` deleted.
- `dashboard_widgets`: `DELETE` all rows (their config names a column that no longer exists); the table keeps its shape, `config` changes meaning (D6).
- A `DO` block drops every schema named `p_<32 hex>_indicators` (`CASCADE`), so the dashboard starts from "nothing published".
- Mirrored in `TamizaDbContext`: `ScriptKind.Indicators` becomes `Indicator`, `ScriptVersion` gains `Name`, `Label`, `Description`, `Unit`, `Format`; `Run.IndicatorVersions` (`HasJsonColumn<T>()`); `RunStep.Name`; `ScriptTest.Name`.

*Alternative:* an `indicators` table holding name and metadata with `script_versions` referencing it by id. Rejected: metadata would then not be versioned with the code, and a removed-then-recreated indicator would need its own lifecycle; with the name on the version, the current indicator list is "the latest version per name whose code is not null", the same rule as today's "current script".

### D2. Indicator scripts and their contract (analytics)

- `runner.FUNCTIONS` gains `indicator: "compute"`. `compute(cleaned, ctx)` returns one `pd.DataFrame`. `_validated("indicator", returned, name)`: a DataFrame (not a dict); columns reduced and distinct as for every step; **at least one numeric column** (integer, floating or nullable numeric dtype after `_plain`), else "The indicator table has no numeric column; add at least one, such as `value`"; the table is `{name: frame}`. Nothing else is required: `value`, `numerator` and `denominator` are conventions the dashboard looks for, not rules.
- The step directory, limits, log and traceback handling are unchanged; the kind passed to `run_script` is `indicator` and the output table name is the indicator's name, which was validated with `names.is_valid_table_name` when the script was created (D4) and is re-checked by `publish._load`.
- The template an analyst inserts for a new indicator returns one dimension column and `value`, `numerator` and `denominator`, so the default widget works out of the box.

*Alternative:* keep `compute_indicators` as the function name. Rejected: the plural says "all indicators", which is exactly what the script no longer does.

### D3. Run execution and per-table publishing (analytics)

- `pipeline.claim` resolves, besides `clean` and `output`, the current version of every indicator of the project: the latest version per name, keeping those whose `code IS NOT NULL`, stored in `runs.indicator_versions`. `ClaimedRun.versions` keeps `clean`/`output`; `ClaimedRun.indicators` is the ordered list.
- After the clean step, `output` runs as today. Then the indicators phase: first `publish.drop_tables_except(connection, project_id, "indicators", names)` removes from the schema any table not in the current list (an indicator that was removed), creating the schema if it does not exist; then, for each indicator in name order, a step `indicator`/`name` runs `_script` with the same `cleaned` inputs and, on success, `publish.publish_table(connection, project_id, "indicators", Table(name, path))`. A failing indicator records its step as failed and the loop continues. When the project has no indicators the schema is dropped, as `publish.drop` does today for a missing script 3.
- `publish.publish_table` loads the table into the staging schema `<schema>__next` (the same `_load`; a staging schema rather than a `<name>__next` table, because a 63-character indicator name leaves no room for a suffix), then, with the same lock-timeout-and-retry loop as `_swap`, in one transaction: `DROP TABLE IF EXISTS <schema>.<name>` and `ALTER TABLE <schema>__next.<name> SET SCHEMA <schema>`. The staging schema is dropped afterwards, whatever happened. The wait rules of "Publishing while tables are read" apply unchanged: the exclusive lock is on one table, so readers of the other indicator tables are not held at all. A failure before the rename drops the staging table and leaves the published one.
- `execute` ends the run as `failed` when any step failed; `error_code` is `step.indicator` and `error` names the indicator and its first error line. The run's other steps are what they are: a failed indicator step does not skip the ones after it.
- Skipped recording: when flatten or clean fails, one skipped `indicator` step per claimed indicator is recorded, so the run details list them.

*Alternatives:*
- Keep the schema swap and copy forward the previous tables of failed indicators into `__next`. Rejected: it copies every table on every run and makes the failed indicator's table a silent copy, while the per-table swap is the natural unit now that each table has its own step.
- Run indicator scripts in parallel processes. Rejected for this change: each script may use up to the memory limit, and the worker's memory budget allows one at a time (operations.md keeps 512 MB between the container limit and one script's limit).

### D4. Script, test and run endpoints (api)

- **Scripts.** `GET /api/v1/projects/{id}/scripts` returns `{clean, output, indicators: [IndicatorScript]}`, where `IndicatorScript` is `{name, label, description, unit, format, current: ScriptVersionDetails}` for every name whose latest version has code. Routes `PUT|DELETE /scripts/indicators/{name}`, `GET /scripts/indicators/{name}/versions[/{number}]` and `POST .../restore` mirror `/scripts/{kind}`; `PUT` body is `{code, label, description?, unit?, format}`. The first `PUT` of a name creates the indicator; `name` must satisfy `names.is_valid_table_name`'s rule (validated in the API with the same regular expression as `ColumnNames`), `label` is required (1–120 characters), `description` at most 1,000, `unit` at most 32, `format` one of the four. At most 100 indicators with a current version per project; creating the 101st is a validation error naming the limit. `DELETE` creates a removed version, as today. `ScriptVersionDetails` gains `name`, `label`, `description`, `unit` and `format`.
- **Tests.** `ScriptTestRequest` gains `name`, required when `kind` is `indicator`; the test runs the draft as that indicator (its output table takes the name). `ScriptTestDetails` carries `name`.
- **Runs.** `RunVersions` becomes `{clean, output, indicators: [{name, number}]}`; `StepSummary`/`StepDetails` gain `name` (empty for the three fixed steps) and `StepDetails` the `label` the indicator had in the version the run used and `RunStepName.Indicator` replaces `Indicators`; steps are listed flatten, clean, output, then indicators in name order. The run's `error` names the failed indicator.

### D5. Indicators dashboard API (api)

- **Overview** `GET /indicators`: `{indicators: [{name, label, description, unit, format, published, publishedAt, runId, rows}], widgets, limits}`. The list is the project's current indicator scripts joined with the tables the schema has and the latest succeeded `run_steps` row with `step = 'indicator'` and that `name`; `published` is false for an indicator whose table is not there yet. The dashboard's empty state is "no indicator has published yet" when every entry is unpublished or there are none.
- **Columns** `GET /indicators/{name}/columns`: `TableCatalog` on `p_<id>_indicators.<name>`, plus `numeric: [names]` and `defaults: {measure, groupBy}` — `value` when present else the first numeric column; the first non-numeric column, if any, for `groupBy`.
- **Query** `POST /indicators/query` with the body `{config, chartType?}`, `config` being the widget config of D6 and `chartType` letting a histogram with no group column plot each row's measure instead of one sum: validates against that table's catalog, builds `SELECT <groupBy>, sum(<measure>) ... GROUP BY` or, for a ratio, `sum(<numerator>) / nullif(sum(<denominator>), 0)`; one row per group value (one row in total when `groupBy` is null, as a stat tile uses). Rows whose measure (or both ratio columns) is NULL are left out, as today. `IndicatorRow` becomes `{group, value, geometry}`; `MissingReferences` becomes `{indicator: bool, columns: []}` — a widget whose indicator table is not published is reported as missing, not failed.
- **Export** `POST /indicators/{name}/exports`: the shared export machinery on that table; 404 `indicators.notPublished` when it is not there.
- Limits unchanged (`MaxWidgetsPerProject`, `MaxWidgetQueryRows`).

### D6. Widget configuration and formatting (api + ui)

- `config` becomes `{indicator: string, measure: string | null, ratio: {numerator, denominator} | null, groupBy: string | null, filters, geometryColumn, buckets}`. Exactly one of `measure` and `ratio` is set; `measure` defaults to the table's default measure when both are left out. `groupBy` is required for bar, column, line, pie and map, optional for a stat tile and a histogram (a histogram over rows uses the measure of each row).
- The widget dialog: pick the indicator (with its label), then measure or ratio among the numeric columns, group-by among the columns, filters, and for a map the geometry column. The indicator's format and unit are shown next to the measure.
- Formatting (`ui/indicators/format.ts`): `count` → integer with thousands separators; `decimal` → two decimals; `percent` → the value times 100 with `%`, for a ratio or a measure alike, so a rate stored as a fraction reads as a percentage; `currency` → two decimals with the unit as the currency code when it is one (`Intl.NumberFormat` with `currency`), else prefixed. Other units are appended. The histogram's axis, legends, tooltips, stat tile and "Show as table" use the same formatter.
- A stat tile over a ratio shows `sum(numerator) / sum(denominator)` of the filtered rows, which is the correct aggregate for a rate.

*Alternative:* keep `valueField` restricted to the three names and derive "is this a rate" from the presence of `numerator`. Rejected by the user's decision 1: the measure is free, and the indicator's format is explicit.

### D7. Scripts page (ui)

- The page keeps the `1. Clean` and `2. Output` entries and replaces `3. Indicators` with an **Indicators** list: one entry per indicator (label, name, version badge), an **Add indicator** button (dialog: name, label, description, unit, format, with the name checked against the identifier rule as the user types) and, for the selected indicator, the same editor, Save, Insert template, Upload, Remove, versions card and Test on sample as the other scripts. Saving an indicator sends code and metadata; the metadata fields sit above the editor and make the form dirty like the code does.
- The run details list the indicator steps by label with the name, and a failed indicator's error.

### D8. Degrading gracefully (ui + api)

A widget whose indicator table is not published (never run, removed, or its last run failed before it ever published) shows "`<label>` has not published yet" with a link to the scripts page for analysts, instead of a chart; a widget whose measure, ratio column, group column or geometry column the table no longer has names them, as today. The overview still lists every widget.

### D9. Docs

- **`docs/scripts.md`:** section "The chain" (script 3 becomes "indicator scripts"), section 3 rewritten as "Indicator scripts: `compute(cleaned, ctx)`" with the one-sentence contract, the conventions (`value`, `numerator`/`denominator`, dimension columns, `geometry`), the metadata and one example per shape (a rate by municipality, a series by round), "Rules for what scripts return", "Where the results live" (one table per indicator).
- **`docs/indicators-dashboard.md`:** "What an indicator script must provide", measure and ratio, formatting, per-table export.
- **`docs/tamiza-brief.md`:** section 5.4 (contract, optional scripts, execution order, indicator output validation), section 5.7 (widget canvas paragraph), section 6 (`script_versions`, `runs`, `run_steps`), section 8 item 7a.
- **README status.**

## Risks / Trade-offs

- **[Dozens of indicators make runs longer]** → each script is one process start (about a second) plus its own work; indicators run after `output`, one at a time, and the run log shows each duration. Parallel execution is a follow-up if real projects need it.
- **[Per-table swap under readers]** → the lock is per table and the retry loop is the same as the schema swap's; a reader of indicator A never blocks indicator B. Task 2.2 tests both the wait and the timeout.
- **[`percent` ambiguity: fraction or already ×100]** → the format is defined as "a fraction shown as a percentage" in the spec, the docs and the dialog's hint; an analyst who stores 42 for 42% picks `decimal` with unit `%`.
- **[Metadata on versions duplicates data]** → a few short strings per version; the list of indicators is one query ordered by name.
- **[Migration deletes work]** → by the user's decision, before real data; the script says what it deletes in its header comment.

## Migration Plan

- `0008_split_indicator_scripts.sql` deletes the `indicators` script versions, tests and run steps, every widget and every `p_<id>_indicators` schema, then alters the tables and checks. It runs in the API's startup migration. The analytics worker must be deployed with the same version: an older worker would claim runs with a `kind` the new checks reject.
- After deployment every project's Indicators tab shows the empty state and the scripts page shows no indicators; analysts create them one by one.
- **Rollback:** not supported for data; an older image fails its `SchemaDriftTests`-shaped expectations on `script_versions`. Restore a backup taken before the migration.

## Verification Results

Measured on 2026-10-14 in a separate Compose project (`tamizaverify`: PostgreSQL, API and worker built from this change, 3,000 stored submissions, the worker's limits at their defaults apart from a 60 s publish wait), with a project of 30 indicator scripts: 27 plain rates by municipality, `ind_28_flaky` (valid in the first run, raising in the second), `ind_29_text_only` (no numeric column) and `ind_30_geometry` (a geopandas point column).

**Run 1.** The run ended `failed` with `step.indicator` and the error `Indicator ind_29_text_only: The indicator table has no numeric column; add at least one, …`. 29 tables existed in `p_<id>_indicators`; the only one missing was `ind_29_text_only`. `ind_30_geometry` was published as `municipality text, value double precision, geometry text` (WKT). Each indicator step took 0.6 to 0.8 s (one process start plus its work); flatten took 0.3 s and the whole indicators phase about 25 s.

**Run 2** (after `ind_28_flaky` was saved with a script that raises, `ind_05` was removed, and an export of `ind_10` was started and held for about 25 s by the API's test sleep). The run ended `failed` with `Indicator ind_28_flaky: ValueError: the data changed under this script` (the first failed indicator in name order; `ind_29_text_only` failed too and its step says so). 28 tables existed: `ind_05` was gone, and `ind_28_flaky` kept its 4 rows from run 1. Every indicator step took 0.6 to 0.8 s except `ind_10`, whose swap waited for the export: 14.9 s. The indicators published before `ind_10` (`ind_01` to `ind_09`) were not delayed by the export, as the one-table swap intends. The export finished after 25.2 s with its single-use link; the downloaded CSV had the header and the 4 rows of `ind_10`.

Not measured here: indicator scripts that each take long, or hundreds of indicators; a run is the sum of its steps, so 30 indicators cost about 25 s and the log shows each duration.
