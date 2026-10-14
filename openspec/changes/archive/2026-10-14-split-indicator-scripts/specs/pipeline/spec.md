# Spec Delta

## ADDED Requirements

### Requirement: Indicator scripts
A project SHALL have any number of indicator scripts, up to 100, each with a name that follows the rules for table names and cannot be changed. Each indicator script SHALL define `compute(cleaned, ctx)` returning one pandas DataFrame, which SHALL be published as the table with the indicator's name in the project's indicator schema.

#### Scenario: Indicator created
- **WHEN** an analyst creates the indicator `piped_water_rate` with a script
- **THEN** the project lists that indicator at version 1, and the next run publishes the table `piped_water_rate` in the indicator schema

#### Scenario: Invalid name
- **WHEN** an analyst creates an indicator named `Piped water`
- **THEN** the API rejects it with a message naming the allowed characters

#### Scenario: Indicator limit
- **WHEN** a project already has 100 indicators and an analyst creates another
- **THEN** the API rejects it with a message naming the limit

### Requirement: Indicator metadata
An indicator SHALL have a label, an optional description, an optional unit, and a format that is one of `count`, `decimal`, `percent` (a fraction shown as a percentage) or `currency`. The metadata SHALL be saved with every version of the indicator's script.

#### Scenario: Metadata saved
- **WHEN** an analyst creates the indicator `piped_water_rate` with the label `Households with piped water` and the format `percent`
- **THEN** the project lists the indicator with that label and format

#### Scenario: Metadata restored with a version
- **WHEN** an analyst saves version 2 of an indicator with a new label and then restores version 1
- **THEN** version 3 has the code and the label of version 1

### Requirement: Indicator output contract
An indicator script's table SHALL have at least one numeric column; any other columns are its dimensions. By convention, and not by rule, `value` is the measure a widget plots by default, and `numerator` and `denominator` let a widget plot a ratio of sums. A geometry column SHALL be published as WKT text. A table without a numeric column SHALL fail the indicator's step with a message saying so, and nothing SHALL be published for that indicator.

#### Scenario: Rate by municipality
- **WHEN** the indicator `piped_water_rate` returns a table with the columns `municipality`, `value`, `numerator` and `denominator`
- **THEN** the table is published with those four columns and the dashboard offers `municipality` as a group column and `value` as the default measure

#### Scenario: No numeric column
- **WHEN** an indicator returns a table whose columns are all text
- **THEN** its step fails with a message that the table has no numeric column, and its previous table stays published

### Requirement: Publishing one indicator table
Each indicator SHALL replace only its own table in the indicator schema, in a single transaction that waits for readers under the same rules as a schema replacement, so readers of other indicator tables are not held. Tables of indicators the project no longer has SHALL be removed at the start of the run's indicator steps, and the schema SHALL be removed when the project has no indicators.

#### Scenario: Indicator removed
- **WHEN** an analyst removes the indicator `old_rate` and a run follows
- **THEN** the table `old_rate` no longer exists after the run and the other indicator tables are untouched

#### Scenario: Export of another indicator during a publish
- **WHEN** an export of `piped_water_rate` is running while `attendance` publishes
- **THEN** `attendance` publishes without waiting for the export

### Requirement: Optional scripts and their defaults
Every script SHALL be optional. Without script 1, the cleaned data SHALL be the flattened data and SHALL be published as the clean schema. Without script 2, that step SHALL be skipped and the output schema SHALL be removed. Without indicator scripts, no indicator step SHALL run and the indicator schema SHALL be removed, so later readers know there is no output or indicator table.

#### Scenario: No scripts at all
- **WHEN** a project without scripts runs
- **THEN** the raw and clean schemas contain the flattened tables, there are no output or indicator schemas, and the run succeeds with step 2 marked as skipped and no indicator steps

#### Scenario: Last indicator removed
- **WHEN** an analyst removes the project's only indicator and a run follows
- **THEN** the indicator schema no longer exists after the run

### Requirement: Output validation of scripts
Scripts 1 and 2 SHALL return at least one table, named by the rules for table names. Column names SHALL be reduced with the naming rules, the renaming SHALL be reported in the step's log, and two columns of a table SHALL NOT reduce to the same name. An indicator script's table SHALL have at least one numeric column. If validation fails, the step SHALL fail with a message naming the problem and nothing SHALL be published for that step. Geometry columns SHALL be published as text in WKT format.

#### Scenario: Indicator without a numeric column
- **WHEN** an indicator script returns a DataFrame with only the text column `municipality`
- **THEN** the step fails with a message that the table has no numeric column and the previous table of that indicator stays published

#### Scenario: Invalid table name
- **WHEN** script 2 returns a table named `my table!`
- **THEN** the step fails with a message naming the table and the allowed characters

#### Scenario: Column names from pandas
- **WHEN** script 1 returns a table with the columns `Household size` and `Age`
- **THEN** the published table has the columns `household_size` and `age`, and the step's log lists both renamings

#### Scenario: Columns that collide
- **WHEN** script 2 returns a table with the columns `Age` and `age`
- **THEN** the step fails with a message naming both columns

### Requirement: Test sample
A test's sample SHALL hold the requested number of non-deleted submissions, from 1 to 5,000 and 500 by default, or all of them when the project has fewer; it SHALL be spread across the project's submissions rather than only the newest, and consecutive tests SHALL use the same submissions while the project's submissions do not change.

#### Scenario: Sample spread across submissions
- **WHEN** an analyst tests with a sample of 500 in a project with 20,000 submissions
- **THEN** the sample is not the 500 newest submissions

### Requirement: Sample test of one script
An `analyst` or `admin` SHALL be able to test script 1, script 2 or one indicator script by name on a sample, with unsaved code sent in the request. A test SHALL flatten the sample, then for script 2 or an indicator run the saved script 1 (or use the flattened tables without one), then run the tested code with a run's contract, `ctx`, validation and limits. A test SHALL NOT create a version, change a published schema or appear in the run list. Viewers SHALL NOT start tests.

#### Scenario: Draft tested without saving
- **WHEN** an analyst tests script 1 with code that differs from its current version
- **THEN** the test runs that code, script 1 keeps the same current version, and the published clean schema is unchanged

#### Scenario: Testing script 2
- **WHEN** an analyst tests a draft of script 2 while script 1 is at version 3
- **THEN** the test flattens the sample, runs version 3 of script 1 on it, and runs the draft on its result

#### Scenario: Testing an indicator
- **WHEN** an analyst tests a draft of the indicator `coverage`
- **THEN** the test runs the draft as `coverage` and its preview shows the table `coverage`

#### Scenario: Script 1 fails in a test of an indicator
- **WHEN** the current version of script 1 raises an exception during a test of an indicator
- **THEN** the test fails with script 1's error and traceback, and the draft is not run

#### Scenario: Small project
- **WHEN** an analyst tests with a sample of 500 in a project with 120 non-deleted submissions
- **THEN** the test uses all 120 submissions

#### Scenario: Same sample twice
- **WHEN** an analyst runs two tests one after the other and no submission was added, changed or deleted in between
- **THEN** both tests use the same submissions

#### Scenario: Invalid output in a test
- **WHEN** a draft of an indicator returns a DataFrame without a numeric column
- **THEN** the test fails with the same message a run would show and nothing is published

#### Scenario: Time limit in a test
- **WHEN** a draft runs longer than the deployment's time limit
- **THEN** it is stopped and the test fails with a message that it exceeded the time limit

#### Scenario: Sample size out of range
- **WHEN** an analyst requests a test with a sample of 10,000 submissions
- **THEN** the API rejects the request with a validation error and nothing is queued

#### Scenario: Viewer tests a script
- **WHEN** a `viewer` tries to start a test
- **THEN** the API responds 403 and nothing is queued

## MODIFIED Requirements

### Requirement: Three scripts with a fixed contract
Each project SHALL have two optional scripts with a fixed signature, script 1 `clean(raw, ctx)` returning a dictionary of table names to pandas DataFrames and script 2 `prepare_output(cleaned, ctx)` returning a dictionary of DataFrames, and any number of indicator scripts, each defining `compute(cleaned, ctx)` returning one DataFrame. `raw` and `cleaned` SHALL be dictionaries of table names to pandas DataFrames. A script that does not define its function, raises an exception or returns another type SHALL fail its step with a message naming the problem; a Python traceback SHALL be shown for exceptions.

#### Scenario: Script 1 cleans the main table
- **WHEN** script 1 returns `{"main": df}` where `df` drops the rows of test submissions
- **THEN** the published clean schema has a `main` table without those rows

#### Scenario: Missing function
- **WHEN** script 2 defines `prepare(cleaned, ctx)` instead of `prepare_output`
- **THEN** the step fails with a message that `prepare_output` is not defined

#### Scenario: Wrong return type
- **WHEN** script 1 returns a list
- **THEN** the step fails with a message that `clean` must return a dictionary of DataFrames

#### Scenario: Indicator returns a dictionary
- **WHEN** an indicator script's `compute` returns `{"rate": df}`
- **THEN** its step fails with a message that `compute` must return a DataFrame

#### Scenario: Exception in a script
- **WHEN** an indicator script raises `KeyError: 'age'`
- **THEN** its step fails and its log shows the traceback with the line of the script that raised it

### Requirement: Execution order
Flattening SHALL run first, then script 1. Script 2 and the indicator scripts SHALL depend only on the result of script 1: each SHALL run when script 1 succeeded or is absent, regardless of the others' outcome. Indicator scripts SHALL run after script 2, one at a time, in the order of their names. If flattening or script 1 fails, script 2 and the indicator scripts SHALL NOT run and SHALL be marked as skipped.

#### Scenario: Script 1 fails
- **WHEN** script 1 raises an exception
- **THEN** script 2 and the indicator scripts do not run, and the run ends as failed

#### Scenario: Script 2 fails
- **WHEN** script 2 fails and every indicator script succeeds
- **THEN** the indicator tables are published and the run ends as failed with script 2's error

#### Scenario: One indicator fails
- **WHEN** the indicator `attendance` raises an exception and the indicators `coverage` and `piped_water_rate` succeed
- **THEN** `coverage` and `piped_water_rate` are published, `attendance` keeps its previous table, and the run ends as failed naming `attendance`

### Requirement: Atomic publishing
Script 1, script 2 and the flattening SHALL write their tables to a temporary schema and SHALL replace their published schema in a single transaction only when the step finished without errors and its output passed validation. Each indicator SHALL replace only its own table the same way. Readers SHALL see either the previous published tables or the new ones, never a mix. A failed step SHALL leave its previously published schema or table unchanged.

#### Scenario: Script failure
- **WHEN** a script raises an exception
- **THEN** the dashboards keep reading the last published version and the run ends as `failed` with the traceback visible

#### Scenario: Successful step
- **WHEN** script 2 succeeds with tables `households` and `persons`
- **THEN** the output schema contains exactly those two tables, and tables from earlier runs that script 2 no longer returns are gone

### Requirement: Script versioning
An `analyst` or `admin` SHALL be able to save a script; every save SHALL create a new immutable version with its author and time, and the latest version SHALL be the one runs use. For an indicator script the version SHALL also hold the indicator's metadata. Removing a script SHALL also create a version, marked as removed. Every member SHALL be able to read every version of every script. Restoring an older version SHALL save its code, and for an indicator its metadata, as a new version. A script larger than 200 KB SHALL be rejected.

#### Scenario: Save creates a version
- **WHEN** an analyst saves script 1 twice
- **THEN** the project has two versions of script 1, with author and time, and the second is current

#### Scenario: Viewer saves
- **WHEN** a `viewer` tries to save a script
- **THEN** the API responds 403 and no version is created

#### Scenario: Restore
- **WHEN** an analyst restores version 3 of script 2 while version 5 is current
- **THEN** version 6 is created with the code of version 3 and becomes current

#### Scenario: Indicator versions are per indicator
- **WHEN** an analyst saves the indicator `coverage` three times and the indicator `attendance` once
- **THEN** `coverage` is at version 3 and `attendance` at version 1

### Requirement: Runs and the run log
Each run SHALL record its trigger, the user who started it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the version of script 1 and script 2 it used and the version of each indicator script by name, the reference tables it used with their row counts and the time each was last updated, and one entry per step (flatten, clean, output, and one per indicator with its name) with its status (`succeeded`, `failed` or `skipped`), duration, input and output row counts, log and error. A run SHALL read the reference tables once when it starts, so replacing a reference table during a run SHALL NOT affect that run. A run SHALL succeed only when no step failed; a run with a failed indicator step SHALL name that indicator in its error. Every project member SHALL be able to list the project's runs, newest first, and read each run's details. A run interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Run details
- **WHEN** a member opens a finished run
- **THEN** they see each step, the indicator steps by label and name, with status, duration, rows in and out, its log and, for a failed step, the error and traceback, and the reference tables the run used

#### Scenario: Run uses the current versions
- **WHEN** script 1 is at version 4 and the indicator `coverage` at version 2 when a run starts, and an analyst saves script 1 again during the run
- **THEN** the run records and uses version 4 of script 1 and version 2 of `coverage`

#### Scenario: Indicator added during a run
- **WHEN** an analyst creates the indicator `attendance` while a run is running
- **THEN** that run does not compute `attendance`, and the next run does

#### Scenario: Reference table replaced during a run
- **WHEN** an analyst replaces the reference table `population` while a run is running
- **THEN** the run uses the rows `population` had when the run started and records that table's earlier update time, and the next run uses the new rows

#### Scenario: Worker restarted
- **WHEN** the worker restarts while a run is running
- **THEN** that run is recorded as failed because it was interrupted, and later runs proceed

## REMOVED Requirements

### Requirement: Optional scripts with default behavior
**Reason**: Replaced by `Optional scripts and their defaults`, whose scenarios describe indicator scripts instead of script 3.
**Migration**: None; the change drops the single-table contract without migration.

### Requirement: Output validation
**Reason**: Replaced by `Output validation of scripts`, whose scenarios describe indicator scripts instead of script 3.
**Migration**: None; the change drops the single-table contract without migration.

### Requirement: Sample test run
**Reason**: Replaced by `Sample test of one script`, whose scenarios describe indicator scripts instead of script 3.
**Migration**: None; the change drops the single-table contract without migration.
