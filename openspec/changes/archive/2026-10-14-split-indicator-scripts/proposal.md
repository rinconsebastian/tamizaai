# Proposal

## Why

Script 3 returns one table with the columns `indicator`, `disaggregation`, `value`, `numerator` and `denominator`, and every indicator of the project has to fit that mold. The user who is about to load real data found the mold unclear in practice: whether `disaggregation` is free text or `dimension=value`, whether the dimensions go in that column or in extra columns, whether `value` is a count, a ratio or a percentage (the dashboard's widgets sum rows, which is right for counts and silently wrong for rates), and what a mean or a median puts in `numerator`. The only validation is that the five columns exist. And one exception in one indicator fails the whole step, so no indicator publishes.

An indicator is naturally one function returning one small table shaped for that indicator — a rate by municipality, a series by round, a top ten. Giving each indicator its own script and its own published table removes the mold, the ambiguities that came with it, and the shared failure, while reusing what already exists: the per-step process isolation, the sample test, the published-schema reading of the clean data and indicators dashboards. No real data has been loaded yet, so the single-table contract is dropped without migration.

## What Changes

- **Indicator scripts instead of script 3.** A project has any number of named indicator scripts (up to 100). Each defines `compute(cleaned, ctx)` returning one DataFrame, published as the table with the indicator's name in `p_<id>_indicators`. `compute_indicators` and the single `indicators` table are gone.
- **Metadata per indicator.** An indicator has a name (an identifier, the same rules as a table name, immutable), a label, an optional description, an optional unit and a format (`count`, `decimal`, `percent` or `currency`), saved with each version of its script. The dashboard formats values with them.
- **A freer contract.** An indicator's table carries any dimension columns and at least one numeric column. `value` is the measure by default when present; `numerator` and `denominator` are optional and let widgets plot a ratio of sums. A geometry column (WKT) is still how a map gets its shapes. Validation at test and run time: at least one numeric column, valid and distinct column names, and the usual naming rules.
- **One table at a time.** Each indicator publishes its own table with a per-table swap inside the schema, under the same wait-for-readers rules as a schema swap. An indicator that fails keeps its previous table and does not stop the others; the run still ends as failed, naming the indicator. Tables of indicators that no longer exist are dropped at the next run.
- **Runs, tests and versions per indicator.** A run records one step per indicator with the version it used; a sample test tests one indicator script; versions, restore and removal work per indicator as they did for script 3.
- **Widgets plot one indicator table.** A widget names an indicator, a measure (any numeric column, or a ratio of two), a column to group by (any column), and filters; the `indicator` and `disaggregation` columns and the multi-indicator series are gone. Values are formatted by the indicator's format and unit, so a stat tile over a rate shows a percentage, not a sum of percentages. Export is per indicator table.
- **Scripts page.** The page lists the clean script, the output script and the indicators, with add, remove and per-indicator versions and tests.

Not in this change:
- A shared helper module that indicator scripts import; common preparation goes in script 1 or script 2.
- Widgets that compare several indicator tables (`add-indicators-dashboard`'s multi-series charts); one indicator per widget.
- Running indicator scripts in parallel; they run one after another in the same worker.
- Migrating an existing script 3 or `indicators` table; both are removed.
- The editor and sample-test improvements of `add-script-workbench`, and the download kit of `add-local-dev-kit`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `pipeline`: named indicator scripts with metadata replace script 3; a per-table publish for the indicator schema; per-indicator run steps, versions and tests; the freer output contract.
- `dashboard-indicators`: a widget plots one indicator table with a free measure, a ratio of sums, and the indicator's format and unit; columns, query and export are per table.

## Impact

- **analytics:** `runner.py` (`compute` contract and validation of an indicator table), `pipeline.py` (one step per indicator, versions per indicator, dropping removed tables), `sample_runs.py` (tests of one indicator), `publish.py` (per-table swap), `names.py`.
- **api:** `ScriptEndpoints` (indicator routes with name and metadata, the 100-indicator limit), `ScriptTestEndpoints`, `RunEndpoints` (per-indicator steps and versions), the `Indicators` module (overview lists indicator tables with metadata; columns, query and export per table; widget config with `indicator`, `measure`, `ratio` and `groupBy`), EF Core entities.
- **Database:** DbUp script `0008_split_indicator_scripts.sql`: `script_versions` gains `name`, `label`, `description`, `unit` and `format`, kind `indicators` becomes `indicator`; `runs` loses `indicators_version_id` and gains `indicator_versions`; `run_steps` gains `name` in its key and the step `indicator`; `script_tests` gains `name`; existing `indicators` versions, tests and steps and every `p_<id>_indicators` schema are deleted; `dashboard_widgets` rows are deleted.
- **UI:** the scripts page (indicator list, add dialog with metadata, per-indicator editor, versions and tests), the runs list and details, the indicators dashboard (overview of indicator tables, widget dialog with indicator, measure, ratio and group-by, formatting), i18n.
- **Docs:** `docs/scripts.md`, `docs/indicators-dashboard.md`, `docs/tamiza-brief.md` (sections 5.4, 5.7, 6 and 8), the README status.
- **Configuration and dependencies:** none new.
