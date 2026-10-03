# pipeline Specification

## Purpose

Turn a project's raw Kobo submissions into published analysis tables: flatten them, run the analyst's cleaning, output and indicator scripts in a fixed order, and publish each result only when it is complete and valid.

## Requirements

### Requirement: Automatic flattening
Every run SHALL first flatten the project's raw submissions that are not marked as deleted into tables: one main table with one row per submission, and one table per repeat group with one row per repeat instance and a key to its parent row (the submission, or the enclosing repeat instance for nested repeats). Groups that are not repeats SHALL become columns of their parent table. Columns SHALL take their type from the form's question type: integers, decimals, dates and date-times as such, and every other type as text. A value that cannot be read as its question's type SHALL be stored as empty and counted in the step's log. Answers to questions that are no longer in the form SHALL be kept as text columns. The flattened tables SHALL be published in the project's raw schema like any other step.

#### Scenario: Form with a repeat group
- **WHEN** a run flattens 10 submissions of a form with a repeat group `members` answered 3 times in each submission
- **THEN** the raw schema has a main table with 10 rows and a `members` table with 30 rows, each with the `_id` of its submission

#### Scenario: Nested repeat
- **WHEN** a repeat group `visits` sits inside the repeat group `members`
- **THEN** each `visits` row carries the key of its `members` row, and each `members` row the `_id` of its submission

#### Scenario: Deleted submission
- **WHEN** a submission is marked as deleted
- **THEN** it does not appear in any flattened table

#### Scenario: Unreadable value
- **WHEN** an integer question holds the text `n/a` in one submission
- **THEN** that cell is empty and the flattening step's log reports one value that could not be read for that column

#### Scenario: Question removed from the form
- **WHEN** older submissions answer a question the current form no longer has
- **THEN** the main table keeps a text column with those answers

### Requirement: Table and column names
Table names SHALL be `main` for the submissions and the repeat group's name for each repeat table. Column names SHALL be the question names; when two questions in the same table share a name, the later ones SHALL use their full path instead. Every table SHALL also carry the system columns `_id`, `_uuid`, `_submission_time` and `_version` on the main table, and `_row_id`, `_parent_id` and `_index` on repeat tables. Names SHALL be reduced to lowercase letters, digits and underscores and at most 63 characters, with a short suffix added when that makes two names equal. Names supplied by scripts SHALL be validated the same way and SHALL NOT be used to build SQL without validation and quoting.

#### Scenario: Question inside a group
- **WHEN** the question `age` sits in the group `respondent`
- **THEN** the main table has the column `age`

#### Scenario: Same name in two groups
- **WHEN** the groups `head` and `spouse` both contain a question `age`
- **THEN** the main table has the columns `age` and `spouse_age`

### Requirement: Three scripts with a fixed contract
Each project SHALL have up to three optional Python scripts, each defining one function with a fixed signature: script 1 `clean(raw, ctx)` returning a dictionary of table names to pandas DataFrames, script 2 `prepare_output(cleaned, ctx)` returning a dictionary of DataFrames, and script 3 `compute_indicators(cleaned, ctx)` returning one DataFrame. `raw` and `cleaned` SHALL be dictionaries of table names to pandas DataFrames. A script that does not define its function, raises an exception or returns another type SHALL fail its step with a message naming the problem; a Python traceback SHALL be shown for exceptions.

#### Scenario: Script 1 cleans the main table
- **WHEN** script 1 returns `{"main": df}` where `df` drops the rows of test submissions
- **THEN** the published clean schema has a `main` table without those rows

#### Scenario: Missing function
- **WHEN** script 2 defines `prepare(cleaned, ctx)` instead of `prepare_output`
- **THEN** the step fails with a message that `prepare_output` is not defined

#### Scenario: Wrong return type
- **WHEN** script 1 returns a list
- **THEN** the step fails with a message that `clean` must return a dictionary of DataFrames

#### Scenario: Exception in a script
- **WHEN** script 3 raises `KeyError: 'age'`
- **THEN** the step fails and its log shows the traceback with the line of the script that raised it

### Requirement: Script context
Each script SHALL receive a `ctx` object that exposes the form dictionary and a logger. The form dictionary SHALL include, for each question, its name, path, type, label and choice list, and for each choice list its choice names and labels. Messages written through `ctx.log` and output printed by the script SHALL be stored in the step's log and shown in the run's details.

#### Scenario: Label for a choice
- **WHEN** script 2 looks up the choice list of the question `municipality` in `ctx.form` and the code `cali`
- **THEN** it obtains the label `Cali` as defined in the form

#### Scenario: Script messages
- **WHEN** script 1 calls `ctx.log.info("Removed 12 test submissions")` and `print("done")`
- **THEN** both lines appear in the log of the clean step, in order

### Requirement: Optional scripts with default behavior
Every script SHALL be optional. Without script 1, the cleaned data SHALL be the flattened data and SHALL be published as the clean schema. Without script 2 or script 3, that step SHALL be skipped and the corresponding published schema SHALL be removed, so later readers know there is no output or indicator table.

#### Scenario: No scripts at all
- **WHEN** a project without scripts runs
- **THEN** the raw and clean schemas contain the flattened tables, there are no output or indicator schemas, and the run succeeds with steps 2 and 3 marked as skipped

#### Scenario: Script 3 removed
- **WHEN** an analyst removes script 3 and a run follows
- **THEN** the indicator schema no longer exists after the run

### Requirement: Execution order
Flattening SHALL run first, then script 1. Scripts 2 and 3 SHALL depend only on the result of script 1: each SHALL run when script 1 succeeded or is absent, regardless of the other's outcome. If flattening or script 1 fails, scripts 2 and 3 SHALL NOT run and SHALL be marked as skipped.

#### Scenario: Script 1 fails
- **WHEN** script 1 raises an exception
- **THEN** scripts 2 and 3 do not run, and the run ends as failed

#### Scenario: Script 2 fails
- **WHEN** script 2 fails and script 3 succeeds
- **THEN** script 3's indicators are published and the run ends as failed with script 2's error

### Requirement: Atomic publishing
Each step SHALL write its tables to a temporary schema and SHALL replace its published schema in a single transaction only when the step finished without errors and its output passed validation. Readers SHALL see either the previous published tables or the new ones, never a mix. A failed step SHALL leave its previously published schema unchanged.

#### Scenario: Script failure
- **WHEN** a script raises an exception
- **THEN** the dashboards keep reading the last published version and the run ends as `failed` with the traceback visible

#### Scenario: Successful step
- **WHEN** script 2 succeeds with tables `households` and `persons`
- **THEN** the output schema contains exactly those two tables, and tables from earlier runs that script 2 no longer returns are gone

### Requirement: Output validation
Scripts 1 and 2 SHALL return at least one table. Table names SHALL already consist of lowercase letters, digits and underscores, start with a letter and have at most 63 characters. Column names SHALL be reduced with the naming rules, the renaming SHALL be reported in the step's log, and two columns of a table SHALL NOT reduce to the same name. The output of script 3 SHALL contain the columns `indicator`, `disaggregation`, `value`, `numerator` and `denominator`; other columns are allowed. If validation fails, the step SHALL fail with a message naming the problem and nothing SHALL be published for that step. Geometry columns SHALL be published as text in WKT format.

#### Scenario: Missing indicator column
- **WHEN** script 3 returns a DataFrame without the column `denominator`
- **THEN** the step fails with a message naming `denominator` and the previous indicators stay published

#### Scenario: Invalid table name
- **WHEN** script 2 returns a table named `my table!`
- **THEN** the step fails with a message naming the table and the allowed characters

#### Scenario: Column names from pandas
- **WHEN** script 1 returns a table with the columns `Household size` and `Age`
- **THEN** the published table has the columns `household_size` and `age`, and the step's log lists both renamings

#### Scenario: Columns that collide
- **WHEN** script 2 returns a table with the columns `Age` and `age`
- **THEN** the step fails with a message naming both columns

### Requirement: Script versioning
An `analyst` or `admin` SHALL be able to save a script; every save SHALL create a new immutable version with its author and time, and the latest version SHALL be the one runs use. Removing a script SHALL also create a version, marked as removed. Every member SHALL be able to read every version of every script. Restoring an older version SHALL save its code as a new version. A script larger than 200 KB SHALL be rejected.

#### Scenario: Save creates a version
- **WHEN** an analyst saves script 1 twice
- **THEN** the project has two versions of script 1, with author and time, and the second is current

#### Scenario: Viewer saves
- **WHEN** a `viewer` tries to save a script
- **THEN** the API responds 403 and no version is created

#### Scenario: Restore
- **WHEN** an analyst restores version 3 of script 2 while version 5 is current
- **THEN** version 6 is created with the code of version 3 and becomes current

### Requirement: Runs and the run log
Each run SHALL record its trigger, the user who started it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the version of each script it used, and one entry per step (flatten, clean, output, indicators) with its status (`succeeded`, `failed` or `skipped`), duration, input and output row counts, log and error. A run SHALL succeed only when no step failed. Every project member SHALL be able to list the project's runs, newest first, and read each run's details. A run interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Run details
- **WHEN** a member opens a finished run
- **THEN** they see each step with status, duration, rows in and out, its log and, for a failed step, the error and traceback

#### Scenario: Run uses the current versions
- **WHEN** script 1 is at version 4 and script 3 at version 2 when a run starts, and an analyst saves script 1 again during the run
- **THEN** the run records and uses version 4 of script 1 and version 2 of script 3

#### Scenario: Worker restarted
- **WHEN** the worker restarts while a run is running
- **THEN** that run is recorded as failed because it was interrupted, and later runs proceed

### Requirement: Run triggers
A run SHALL start manually when an `analyst` or `admin` requests it, after a synchronization that created, updated or deleted submissions when the project's run-after-synchronization setting is on, and on the project's schedule when one is set. A webhook delivery SHALL NEVER start a run. A project SHALL have at most one queued run: a trigger while a run is queued SHALL NOT add another, and a trigger while a run is running SHALL queue one run to follow it.

#### Scenario: Manual run
- **WHEN** an analyst clicks "Run now"
- **THEN** a run with trigger `manual` is queued and starts within seconds

#### Scenario: Synchronization with changes
- **WHEN** a synchronization stores 5 new submissions and run-after-synchronization is on
- **THEN** a run with trigger `sync` is queued

#### Scenario: Synchronization without changes
- **WHEN** a synchronization finds nothing new, changed or deleted
- **THEN** no run is queued

#### Scenario: Webhook
- **WHEN** a webhook delivery stores a submission
- **THEN** no run is queued

#### Scenario: Trigger during a run
- **WHEN** a synchronization with changes finishes while a run of the project is running and none is queued
- **THEN** one run is queued and starts after the running one ends

#### Scenario: Viewer requests a run
- **WHEN** a `viewer` tries to start a run
- **THEN** the API responds 403 and nothing is queued

### Requirement: Pipeline settings
A project `admin` SHALL be able to turn running after synchronization on or off (on by default) and to set a schedule as a whole number of hours from 1 to 168, or no schedule (the default). A scheduled run SHALL be due one interval after the start of the project's last run. Every member SHALL be able to read the settings.

#### Scenario: Schedule set
- **WHEN** an admin sets the schedule to 24 hours
- **THEN** a run with trigger `schedule` is queued 24 hours after the last run started

#### Scenario: Run after synchronization turned off
- **WHEN** an admin turns running after synchronization off and a synchronization with changes finishes
- **THEN** no run is queued

#### Scenario: Analyst changes the settings
- **WHEN** an `analyst` tries to change the pipeline settings
- **THEN** the API responds 403 and the settings are unchanged

### Requirement: Run limits
Each script SHALL run with a time limit and a memory limit that the operator configures for the deployment, by default 10 minutes and 1024 MB. A script that exceeds either SHALL be stopped and its step SHALL fail with a message naming the limit. Scripts SHALL only use the libraries installed in the analytics image and SHALL NOT be able to install packages.

#### Scenario: Time limit
- **WHEN** script 1 runs longer than the time limit
- **THEN** it is stopped, its step fails with a message that it exceeded the time limit, and scripts 2 and 3 are skipped

#### Scenario: Memory limit
- **WHEN** script 2 uses more memory than the memory limit
- **THEN** it is stopped and its step fails with a message that it exceeded the memory limit

#### Scenario: Missing library
- **WHEN** a script imports a library that is not installed
- **THEN** the step fails with the `ModuleNotFoundError` in its log

### Requirement: Script process isolation
Each script SHALL run in a separate process that receives its input as files and returns its output as files, in a working directory removed after the step. The process SHALL NOT receive the database connection settings or credentials in its environment. This isolation limits accidents; it is not a security boundary, which is why only `analyst` and `admin` can save code.

#### Scenario: Script reads the environment
- **WHEN** a script prints its environment variables
- **THEN** the step's log shows no database host, user or password

#### Scenario: Files left behind
- **WHEN** a script writes a file in its working directory
- **THEN** the file no longer exists after the step ends
