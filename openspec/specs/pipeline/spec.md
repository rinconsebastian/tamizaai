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

### Requirement: Script context
Each script SHALL receive a `ctx` object that exposes the form dictionary, the project's reference tables and a logger. The form dictionary SHALL describe the project's current form: for each question, its name, path, type, label and choice list, and for each choice list its choice names and labels; it SHALL give the label of a code, and the code of a label used by the current form or by an earlier form version. It SHALL also list every column of the flattened tables that holds answers, not the system columns, with its table, the path of its question (the submission key for a column whose question the form no longer has) and its question label, and SHALL give the question label of a column by its table and column name. `ctx.reference` SHALL give each of the project's reference tables as a pandas DataFrame by its name, with the column names and types shown for that table in the project, and SHALL list the available names. Asking for a name the project does not have SHALL raise an error that lists the available names. Messages written through `ctx.log` and output printed by the script SHALL be stored in the step's log and shown in the run's details.

#### Scenario: Label for a choice
- **WHEN** script 2 looks up the choice list of the question `municipality` in `ctx.form` and the code `cali`
- **THEN** it obtains the label `Cali` as defined in the form

#### Scenario: Code for a label
- **WHEN** an indicator script asks `ctx.form` for the code of the label `Cali` of the question `municipality`
- **THEN** it obtains `cali`, and can join the main table with a reference table keyed by codes

#### Scenario: Label of an earlier version
- **WHEN** the choice `1` of `has_water` was labeled `Si` in version `vA` and is labeled `Sí` in the current form, and a script asks for the code of `Si`
- **THEN** it obtains `1`

#### Scenario: Unknown or repeated label
- **WHEN** a script asks for the code of a label that no choice of the question's list has in any version, or that two choices of the same list share
- **THEN** it obtains no code

#### Scenario: Question label of a column
- **WHEN** the groups `head` and `spouse` both contain a question `age`, the deployed version labels `spouse/age` as `Age of the spouse`, and script 2 asks `ctx.form` for the label of the column `spouse_age` of the table `main`
- **THEN** it obtains `Age of the spouse`, and the list of columns shows `spouse_age` with the path `spouse/age` and does not include `_id`

#### Scenario: Column without a question label
- **WHEN** a script asks `ctx.form` for the label of the system column `_id`, of a column whose question is not in the deployed version, or of a column the flattened tables do not have
- **THEN** it obtains no label

#### Scenario: Script messages
- **WHEN** script 1 calls `ctx.log.info("Removed 12 test submissions")` and `print("done")`
- **THEN** both lines appear in the log of the clean step, in order

#### Scenario: Reference table in a script
- **WHEN** the project has a reference table `population` with the integer column `population_2026`, and an indicator script divides a count by `ctx.reference["population"]["population_2026"]`
- **THEN** the script receives that column as integers and its indicators use those values

#### Scenario: Unknown reference table
- **WHEN** an indicator script reads `ctx.reference["populaton"]` and the project only has `population`
- **THEN** the step fails and its log names `populaton` and lists `population` as available

#### Scenario: Project without reference tables
- **WHEN** a script lists the names in `ctx.reference` in a project without reference tables
- **THEN** it obtains an empty list and the step continues

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
An `analyst` or `admin` SHALL be able to save a script; every save SHALL create a new immutable version with its author and time, and the latest version SHALL be the one runs use. For an indicator script the version SHALL also hold the indicator's metadata. Removing a script SHALL also create a version, marked as removed. Only an `analyst` or `admin` SHALL be able to read the project's scripts and every version of every script; a `viewer` SHALL be refused with 403. Restoring an older version SHALL save its code, and for an indicator its metadata, as a new version. A script larger than 200 KB SHALL be rejected.

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

#### Scenario: Viewer reads a script
- **WHEN** a `viewer` asks for the project's scripts, the versions of script 1 or version 2 of the indicator `coverage`
- **THEN** the API responds 403 and returns no code

### Requirement: Runs and the run log
Each run SHALL record its trigger, the user who started it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the version of script 1 and script 2 it used and the version of each indicator script by name, the reference tables it used with their row counts and the time each was last updated, and one entry per step (flatten, clean, output, and one per indicator with its name) with its status (`succeeded`, `failed` or `skipped`), duration, input and output row counts, log and error. A run SHALL read the reference tables once when it starts, so replacing a reference table during a run SHALL NOT affect that run. A run SHALL succeed only when no step failed; a run with a failed indicator step SHALL name that indicator in its error. Only an `analyst` or `admin` SHALL be able to list the project's runs, newest first, and read each run's details; a `viewer` SHALL be refused with 403. A run interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Run details
- **WHEN** an `analyst` opens a finished run
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

#### Scenario: Viewer asks for the runs
- **WHEN** a `viewer` asks for the project's run list or for the details of a finished run
- **THEN** the API responds 403 and returns no step, log or traceback

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
A project `admin` SHALL be able to turn running after synchronization on or off (on by default) and to set a schedule as a whole number of hours from 1 to 168, or no schedule (the default). A scheduled run SHALL be due one interval after the start of the project's last run. Only an `analyst` or `admin` SHALL be able to read the settings; a `viewer` SHALL be refused with 403.

#### Scenario: Schedule set
- **WHEN** an admin sets the schedule to 24 hours
- **THEN** a run with trigger `schedule` is queued 24 hours after the last run started

#### Scenario: Run after synchronization turned off
- **WHEN** an admin turns running after synchronization off and a synchronization with changes finishes
- **THEN** no run is queued

#### Scenario: Analyst changes the settings
- **WHEN** an `analyst` tries to change the pipeline settings
- **THEN** the API responds 403 and the settings are unchanged

#### Scenario: Analyst reads the settings
- **WHEN** an `analyst` asks for the project's pipeline settings
- **THEN** the API returns whether running after synchronization is on and the schedule

#### Scenario: Viewer reads the settings
- **WHEN** a `viewer` asks for the project's pipeline settings
- **THEN** the API responds 403 and returns no schedule

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

### Requirement: Test queue and lifetime
Tests SHALL be executed by the same worker as runs and sample downloads, one at a time. A queued test SHALL start before any queued sample download or run, but SHALL wait for a running run, test or sample download to finish; while it waits, its status SHALL say whether it waits for a run, for another test or for a sample download. A user SHALL have at most one queued test per project: a new test SHALL replace that user's queued test in the project, and SHALL NOT affect a test that is already running. A test SHALL be visible only to the user who requested it. A finished test SHALL be deleted 24 hours after it ended. A test interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Test while a run is running
- **WHEN** an analyst starts a test while a run is running and another run is queued
- **THEN** the test shows that it waits for a run, starts when the running run ends, and the queued run starts after the test

#### Scenario: Test while a sample download is prepared
- **WHEN** an analyst starts a test while the worker prepares a sample download and another download is queued
- **THEN** the test shows that it waits for a sample download, starts when that download is ready, and the queued download is prepared after the test

#### Scenario: New test replaces a queued one
- **WHEN** an analyst starts a test while their previous test in the project has not started
- **THEN** the previous test is removed and only the new one runs

#### Scenario: Another member's test
- **WHEN** an admin requests a test that another analyst started
- **THEN** the API responds 404

#### Scenario: Expired test
- **WHEN** a test ended more than 24 hours ago
- **THEN** it can no longer be read

#### Scenario: Worker restarted during a test
- **WHEN** the worker restarts while a test is running
- **THEN** the test is recorded as failed because it was interrupted

### Requirement: Test preview
A finished test SHALL show, for each step it ran (flatten, the saved script 1 when used, and the tested script), its status, duration, input and output row counts, log and, for a failed step, the error and traceback. For every table a successful step produced, it SHALL show the table's name and row count, and for the first 20 tables of each step also its column names and types, the question label of each flattened column that has one, and its first 100 rows. Text values longer than 500 characters SHALL be cut and marked as cut, and when a table's 100 rows would exceed 1 MB, the preview SHALL show fewer rows and say how many. While a test is queued or running, the UI SHALL show its status and update it without reloading the page.

#### Scenario: Preview of a cleaned table
- **WHEN** a test of script 1 succeeds and returns a table `main` with 480 rows
- **THEN** the test shows `main` with 480 rows, its columns and types, and its first 100 rows

#### Scenario: Flattened tables in the preview
- **WHEN** a test of script 2 succeeds on a form with a repeat group `members`
- **THEN** the preview also shows the flattened `main` and `members` tables the scripts received

#### Scenario: Question labels in the preview
- **WHEN** a test succeeds on a form whose deployed version labels the question `has_water` as `Does the household have running water?`
- **THEN** the flattened `main` table shows that label with the column `has_water`, and the system columns and the tables returned by scripts show no label

#### Scenario: Failed test
- **WHEN** a draft raises `KeyError: 'age'`
- **THEN** the test shows the failed step with the traceback pointing at the draft's line, and the tables of the steps before it

### Requirement: Reference tables
An `analyst` or `admin` SHALL be able to upload a CSV or XLSX file of at most 10 MB as a project reference table with a name, to replace a reference table's rows with a new file, and to delete a reference table. A project SHALL have at most 50 reference tables. Names SHALL be unique in the project and SHALL consist of lowercase letters, digits and underscores, start with a letter and have at most 63 characters. Only an `analyst` or `admin` SHALL be able to list the project's reference tables, with name, original file name, row and column counts, and who last updated each one and when, and to preview a table's columns with their types and its first 100 rows; a `viewer` SHALL be refused with 403.

#### Scenario: Upload
- **WHEN** an analyst uploads `population.xlsx` as `population`
- **THEN** the project lists the reference table `population` with its rows and columns, and the next run's scripts can read it

#### Scenario: Name already used
- **WHEN** an analyst uploads a new file with the name `population` and the project already has a table with that name
- **THEN** the API rejects the upload with a conflict and the existing table is unchanged

#### Scenario: Replace
- **WHEN** an analyst replaces `population` with a new file
- **THEN** the table keeps its name, its rows and columns are those of the new file, and its last update shows the analyst and the time

#### Scenario: Invalid name
- **WHEN** an analyst uploads a file with the name `Population 2026`
- **THEN** the API rejects it with a message naming the allowed characters

#### Scenario: Viewer uploads
- **WHEN** a `viewer` tries to upload, replace or delete a reference table
- **THEN** the API responds 403 and nothing changes

#### Scenario: Analyst previews
- **WHEN** an `analyst` opens the reference table `population`
- **THEN** they see its columns with types and its first 100 rows

#### Scenario: Viewer previews
- **WHEN** a `viewer` asks for the list of reference tables or for the reference table `population`
- **THEN** the API responds 403 and returns no rows

### Requirement: Reference table file reading
The header SHALL be the first non-empty row; XLSX files SHALL be read from their first sheet, and CSV files SHALL be UTF-8 with commas or semicolons as separators. A file SHALL have at least one data row, at most 200 columns and at most 5,000,000 values (rows times columns). Column names SHALL be reduced with the naming rules for question names and, when two reduce to the same name, the later ones SHALL get a numbered suffix (`age`, `age_2`); the upload SHALL report every column it renamed. Each column's type SHALL be detected from its non-empty values: integer when all are whole numbers, decimal when all are numbers, date when all are dates, and text otherwise. Numbers SHALL use a dot as decimal separator, or a comma in files separated by semicolons. A value with a leading zero followed by more digits, such as `05001`, SHALL NOT count as a number, so codes keep their zeros. Empty cells SHALL be empty values.

#### Scenario: Column names and types
- **WHEN** an analyst uploads a CSV with the header `Municipality code;Year;Population` and rows such as `05001;2026;2612958`
- **THEN** the table has the text column `municipality_code`, the integer columns `year` and `population`, and the upload reports the three renamed columns

#### Scenario: Decimal comma
- **WHEN** a CSV separated by semicolons has the value `12,5` in a column of numbers
- **THEN** the column is decimal and holds 12.5

#### Scenario: Codes with leading zeros
- **WHEN** a column holds `05001` and `11001`
- **THEN** the column is text and keeps `05001`

#### Scenario: Unsupported file
- **WHEN** an analyst uploads a `.ods` file
- **THEN** the API rejects it with a message that only CSV and XLSX files are accepted

#### Scenario: File too large
- **WHEN** an analyst uploads a file larger than 10 MB
- **THEN** the API rejects it with a message naming the 10 MB limit and nothing is stored

#### Scenario: Too many values
- **WHEN** an analyst uploads an XLSX file under 10 MB whose sheet holds 300,000 rows of 20 columns
- **THEN** the API rejects it with a message naming the 5,000,000-value limit and nothing is stored

#### Scenario: Header only
- **WHEN** an analyst uploads a file with a header row and no data rows
- **THEN** the API rejects it with a message that the file has no rows

### Requirement: Choice labels in flattened tables
The flatten step SHALL write each answer to a `select_one` or `select_multiple` question as the label of its choice in the form's default language, taken from the definition of the form version the submission was made with. Answers in repeat tables SHALL use their submission's version. A `select_multiple` answer SHALL become the labels of its choices in the order of the answer, joined by `; `. The stored raw submissions SHALL NOT change.

#### Scenario: Yes and No
- **WHEN** the question `has_water` uses a list where `1` is labeled `Yes` and `2` is labeled `No`, and a submission answers `2`
- **THEN** the main table's `has_water` column holds `No` for that submission

#### Scenario: Multiple choices
- **WHEN** a `select_multiple` question `services` is answered `1 3`, where `1` is labeled `Water` and `3` is labeled `Electric power`
- **THEN** the `services` column holds `Water; Electric power`

#### Scenario: Label changed between versions
- **WHEN** the choice `1` is labeled `Si` in version `vA` and `Sí` in version `vB`, and one submission of each version answers `1`
- **THEN** the submission made with `vA` holds `Si` and the one made with `vB` holds `Sí`

#### Scenario: Repeat group
- **WHEN** a submission made with `vA` answers the select question `sex` in three instances of the repeat group `members`
- **THEN** the three `members` rows hold the labels defined in `vA`

#### Scenario: Question removed from the current form
- **WHEN** older submissions made with `vA` answer the select question `old_question`, which the current form no longer has
- **THEN** the main table's `old_question` column holds the labels defined in `vA`

#### Scenario: Labels in a sample test
- **WHEN** an analyst tests script 1 on a sample
- **THEN** the flattened tables the draft receives, and the preview shows, hold labels

### Requirement: Labels for submissions without their version's definition
When the definition of a submission's form version is not available, because the submission has no form version or its version is pending, failed or not found, the flatten step SHALL translate that submission's answers with the project's current form. The step's log SHALL report, for each such version and for the submissions without a version, how many submissions used the current form.

#### Scenario: Version not fetched yet
- **WHEN** a run flattens 15 submissions made with `vB`, whose definition is still pending
- **THEN** their answers carry the labels of the current form, and the flatten step's log reports that 15 submissions of `vB` used the current form

#### Scenario: Submission without a version
- **WHEN** a submission without `__version__` answers a select question
- **THEN** its answer carries the label of the current form, and the log counts it among the submissions without a version

### Requirement: Other option
When a select question has Kobo's "or other" option, the flatten step SHALL label its answer `other` as `Other`, the label Kobo shows for that option in every language. The text the respondent typed SHALL stay in its own column.

#### Scenario: Answer other
- **WHEN** the question `water_source` has the "or other" option and a submission answers `other` and types `Rainwater` in `water_source_other`
- **THEN** the `water_source` column holds `Other` and the `water_source_other` column holds `Rainwater`

### Requirement: Answers without a label
A code that has no label in its version's choice list, because the list has no such choice or its default-language label is empty, SHALL take its label from the project's current form when the current form has one, and SHALL otherwise be kept as received. The step's log SHALL report per column how many values took the current form's label and how many were kept. Answers to questions without a choice list in the form SHALL be kept as received without being reported.

#### Scenario: Choice added in a later version
- **WHEN** a submission made with `vA` was edited in Kobo with the current version `vB`, and its answer `4` exists only in `vB`, labeled `Well`
- **THEN** the cell holds `Well`, and the log reports one value of that column labeled from the current form

#### Scenario: Code without a label
- **WHEN** 2 submissions answer `9` to a select question whose list has no choice `9` in their version or in the current form
- **THEN** both cells hold `9`, and the log reports 2 values of that column kept as codes

#### Scenario: Choices from a file
- **WHEN** a `select_one_from_file` question is answered `05001`
- **THEN** the cell holds `05001` and the log reports nothing for that column

#### Scenario: Kobo rank question
- **WHEN** a form has a Kobo ranking question whose levels are answered with choice names
- **THEN** those cells hold the names as received and the log reports nothing for them

### Requirement: Question labels for flattened columns
Each column of a flattened table that holds the answers to a question of the form's deployed version SHALL carry that question's label in the form's default language, from the deployed version only, whatever version each answer was given with. Labels SHALL be published as column comments with their tables in the same swap, in the raw schema and in the clean schema when it publishes the flattened tables. Labels SHALL NOT change column names, order, types or values.

#### Scenario: Label as a column comment
- **WHEN** a run flattens a form whose deployed version labels the question `has_water` as `Does the household have running water?`
- **THEN** the column `has_water` of the raw table `main` has that label as its comment and keeps its name and position, and the flatten log names the form version the labels came from

#### Scenario: Question relabeled between versions
- **WHEN** the question `hh_size` was labeled `Household size` in version `vA` and is labeled `Number of people in the household` in the deployed version `vB`, and submissions of both versions are flattened
- **THEN** the column `hh_size` carries only `Number of people in the household`

#### Scenario: Renamed column and repeat table
- **WHEN** the groups `head` and `spouse` both contain a labeled question `age`, and the repeat group `members` contains the labeled question `member_age`
- **THEN** the columns `age` and `spouse_age` of `main` carry the labels of `head/age` and `spouse/age`, and the column `member_age` of `members` carries its own label

#### Scenario: Clean schema without script 1
- **WHEN** a run publishes the flattened tables as the clean schema because the project has no script 1
- **THEN** the clean tables carry the same column labels as the raw tables, and the tables returned by scripts 2 and 3 carry none

#### Scenario: Form redeployed with a new label
- **WHEN** the form is redeployed with a new label for `has_water` and a synchronization updates the snapshot
- **THEN** the published tables keep the earlier label until the next run, and that run publishes the new one

### Requirement: Columns without a question label
A column SHALL carry no question label when its question is not in the deployed version, when its question has no label in the default language, or when it is a system column. When question labels are applied, the flatten log SHALL report, per table, how many columns have a question that is not in the deployed version, and name at most 20 of them followed by the number of the others.

#### Scenario: Question removed from the deployed version
- **WHEN** older submissions answer `old_question`, which the deployed version no longer has
- **THEN** the column `old_question` keeps its answers and has no label, and the flatten log reports that `main` has one column whose question is not in the deployed form version and names `old_question`

#### Scenario: Many columns not in the deployed version
- **WHEN** 25 columns of `main` hold answers to questions the deployed version no longer has
- **THEN** the flatten log gives their count for `main`, names 20 of them and says that 5 more exist

#### Scenario: Question without a default-language label
- **WHEN** the deployed version labels the question `notes` only in English and the form's default language is Spanish
- **THEN** the column `notes` has no label, and the flatten log does not report it

#### Scenario: System columns
- **WHEN** a run flattens a form with a repeat group
- **THEN** the system columns of every table have no label and are not reported

### Requirement: Question labels for the text of an "or other" option
The free-text column of a question with Kobo's "or other" option belongs to that select question. It SHALL take the question's label followed by `: Specify other` when the question has a label, and SHALL otherwise carry no label and not be reported in the flatten log.

#### Scenario: Or other text
- **WHEN** the question `water_source`, labeled `Main water source`, has Kobo's "or other" option and a submission fills `water_source_other`
- **THEN** the column `water_source_other` carries the label `Main water source: Specify other`

#### Scenario: Or other text of an unlabeled question
- **WHEN** the question `water_source` has Kobo's "or other" option and only an English label, with Spanish as the default language
- **THEN** the columns `water_source` and `water_source_other` have no label, and the flatten log does not report them

### Requirement: Question labels need the deployed version
Question labels SHALL be read from the stored definition of the form's deployed version, and SHALL be applied only when that definition is available. When the project has no recorded deployed version, or its definition has not been read, the step SHALL publish its tables without question labels and its log SHALL give the reason instead of the per-table report. The flattened tables and their choice labels SHALL be unaffected.

#### Scenario: Deployed version unreadable
- **WHEN** Kobo could not return the definition of the deployed version `vC`, so the project's snapshot was taken from the form's latest content, and a run starts
- **THEN** the run succeeds, its tables carry no question labels, `ctx.form` gives no column label, and the flatten log says that the definition of the deployed form version `vC` is not available and has no per-table report

#### Scenario: Deployed version readable again
- **WHEN** the definition of `vC` is read later, by a retry, a re-check or the background fetch, while the project's snapshot still holds the latest content, and the next run starts
- **THEN** that run publishes the question labels of `vC`, not those of the latest content

#### Scenario: No deployed version recorded
- **WHEN** the form has never been deployed
- **THEN** a run publishes no question labels and its flatten log says that no deployed form version is recorded

### Requirement: Publishing while tables are read
When a reader, such as a dashboard page or an export, is reading a step's published tables, the replacement of that schema SHALL wait for it in attempts of a few seconds, so that other readers are never held for longer than one attempt. A step that cannot replace its schema within a limited time SHALL fail with a message that the published tables were in use, and SHALL leave the previous tables published.

#### Scenario: Export running while a run publishes
- **WHEN** script 2 finishes while a member's export is still reading the output tables, and another member opens a page of the clean data dashboard
- **THEN** the page is answered within a few seconds, and the step publishes its tables once the export has read them

#### Scenario: Tables in use for too long
- **WHEN** the output tables are read without pause for longer than the step's publishing time limit
- **THEN** the step fails with a message that the published tables were in use, and the previous output tables stay published

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
An `analyst` or `admin` SHALL be able to test script 1, script 2 or one indicator script by name on a sample, with unsaved code sent in the request. A test SHALL flatten the sample, or reuse a flattened sample, then for script 2 or an indicator run the saved script 1 or reuse its result (or use the flattened tables without one), then run the tested code with a run's contract, `ctx`, validation and limits, or through one cell. A test SHALL NOT create a version, change a published schema or appear in the run list. Viewers SHALL NOT start tests.

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

### Requirement: Showing data from a script
`ctx.show(data, name)` SHALL accept a DataFrame, a Series or any other value and an optional name, and SHALL return the value unchanged. In a sample test, each call made by the tested script SHALL record a preview of the value as it was at the call, with its name, or `shown N` without one, and the script line that called it. In a run, `ctx.show` SHALL record nothing and SHALL NOT add to the step's log.

#### Scenario: Show in a test
- **WHEN** a draft indicator calls `ctx.show(main, "main")` on line 6 of a test whose sample has 500 submissions
- **THEN** the test shows a preview named `main` from line 6 with 500 rows, its columns and types, and its first 100 rows

#### Scenario: Value changed after the call
- **WHEN** a script shows `main` and then drops half of its rows
- **THEN** the preview shows the rows `main` had at the call

#### Scenario: Value that is not a table
- **WHEN** a script calls `ctx.show(len(main))` as its first call
- **THEN** the test shows a preview named `shown 1` with the text `500`

#### Scenario: Show in a run
- **WHEN** a saved indicator that calls `ctx.show` runs in a pipeline run
- **THEN** the step's log and published table are the same as without the call

### Requirement: Previews shown by a test
A test SHALL keep the first 20 previews shown by the tested script, and SHALL log one line when it ignores later calls. A table preview SHALL follow the rules of a test's table previews and give the full row count; a text preview SHALL be cut at 2,000 characters. Previews recorded before the script fails, raises or is stopped by a limit SHALL be kept. Calls made by the saved script 1 that a test runs first SHALL NOT be recorded.

#### Scenario: Failure after a show
- **WHEN** a draft shows `main` and then raises `KeyError: 'age'`
- **THEN** the failed test shows the traceback and the preview of `main`

#### Scenario: Time limit after a show
- **WHEN** a draft shows `main` and then runs past the deployment's time limit
- **THEN** the test fails with the time-limit message and still shows the preview of `main`

#### Scenario: Too many calls
- **WHEN** a draft calls `ctx.show` 25 times
- **THEN** the test shows the first 20 previews and its log says that 5 calls were not kept

#### Scenario: Saved script 1 shows
- **WHEN** the saved script 1 calls `ctx.show` and an analyst tests an indicator
- **THEN** the test shows only the previews of the indicator draft

### Requirement: Test previews in the UI
The test result SHALL list the tested step's previews in the order they were shown, with each one's name and line, and SHALL show the selected one as a table, or as text for a value that is not a table, using the same table view as the other previews.

#### Scenario: Choose a preview
- **WHEN** a test shows the previews `main` and `rate`, and the analyst selects `rate`
- **THEN** the result shows the rows of `rate` with its line number

### Requirement: Test through a cell
A test request SHALL be able to name a stop line, which SHALL be the line of a `# %%` marker in the tested code. Such a test SHALL run the tested code from its start in a new process, stop the first time execution reaches the marker, record the tested step as `stopped` with its log and previews and no tables, skip the output validation, and succeed. Nothing SHALL be kept from one test for the next, other than the reused sample.

#### Scenario: Stop after loading
- **WHEN** an analyst tests an indicator through its first cell, which shows `main`, while the later cells that return the result are unfinished
- **THEN** the test succeeds, the indicator's step is `stopped` with the preview of `main`, and there is no validation error

#### Scenario: Marker inside a loop
- **WHEN** the stop marker sits inside a `for` loop over three municipalities
- **THEN** the test stops in the first iteration, when execution first reaches the marker

#### Scenario: Marker never reached
- **WHEN** the stop marker sits inside an `if` branch that the sample never enters
- **THEN** the script runs to its end and the test is validated and reported as a whole test

#### Scenario: Nothing kept between tests
- **WHEN** a test through a cell assigns the module-level name `cache`, and the next test's code reads `cache` without assigning it
- **THEN** the next test fails with a `NameError` for `cache`

#### Scenario: Stop line that is not a marker
- **WHEN** a test request names line 4, and line 4 of its code is not a `# %%` marker
- **THEN** the API rejects the request with a validation error for the stop line and nothing is queued

#### Scenario: Marker between parts of one statement
- **WHEN** the stop marker sits between two lines of one call spread over several lines
- **THEN** the step fails with a message that the marker must be on its own line between two statements

### Requirement: Reuse of a test's sample
A test SHALL reuse the flattened sample of an earlier test when its sample holds the same submissions, unchanged, and the form, the labels of its versions and the question labels are unchanged. It SHALL also reuse the result of the saved script 1 when the current version of script 1 and the reference tables are also unchanged. Reused steps SHALL show the tables, row counts and log first recorded, marked as reused. Reuse SHALL NOT change a test's outcome.

#### Scenario: Second test reuses both steps
- **WHEN** an analyst tests an indicator twice and nothing changed in between
- **THEN** the second test shows its flatten and script 1 steps as reused, with the same row counts, and runs only the draft

#### Scenario: Submission edited
- **WHEN** a submission in the sample is updated by a synchronization between two tests
- **THEN** the second test flattens the sample again

#### Scenario: Script 1 saved
- **WHEN** an analyst saves a new version of script 1 between two tests of an indicator
- **THEN** the second test reuses the flattened sample and runs the new version of script 1

#### Scenario: Reference table replaced
- **WHEN** a reference table is replaced between two tests of script 2
- **THEN** the second test runs script 1 again

#### Scenario: Different sample size
- **WHEN** an analyst tests with 500 submissions and then with 1,000
- **THEN** the second test flattens its sample

### Requirement: Size of the reused samples
The worker SHALL keep reusable samples on its own disk, under a size limit set by the deployment, and SHALL remove the least recently used samples when a new one would exceed it. A sample that alone exceeds the limit SHALL NOT be kept. Removing a sample SHALL only make the next test compute it again.

#### Scenario: Limit reached
- **WHEN** keeping a new sample would exceed the limit
- **THEN** the samples used least recently are removed until it fits, and the test succeeds

### Requirement: Dashboard view of an indicator test
A succeeded test of an indicator SHALL show its table as the indicators dashboard will offer it: the measures, the default measure, whether the ratio `numerator`/`denominator` is available, the dimensions (the other columns) and the text columns holding WKT geometries. It SHALL show the default measure, and the ratio when available, summed over the whole table, and a bar chart of either by a chosen dimension, with the format and unit entered for the indicator.

#### Scenario: Rate by municipality
- **WHEN** a test of `piped_water_rate`, with the format `percent`, returns the columns `municipality`, `value`, `numerator` and `denominator` over 12 municipalities
- **THEN** the view lists `value`, `numerator` and `denominator` as measures with `value` as the default, offers the ratio, offers `municipality` as the dimension to chart by, shows the ratio of the sums as a percentage, and charts the 12 municipalities

#### Scenario: Format changed after the test
- **WHEN** the analyst changes the indicator's format from `decimal` to `percent` after the test ended
- **THEN** the view shows the same figures as percentages without a new test

#### Scenario: Dimension with many values
- **WHEN** a dimension has 300 distinct values in the test table
- **THEN** it is listed as a dimension but not offered for the chart, with a note that it has more than 50 values

#### Scenario: Geometry column
- **WHEN** the table has a text column `geometry` whose values are WKT polygons
- **THEN** the view lists `geometry` as a geometry column a map can use

### Requirement: Warnings in the dashboard view
The dashboard view SHALL warn when the table has no rows; has no `value` column, naming the default measure used instead; has `numerator` without `denominator` or the reverse; has empty values in the default measure; has a format `percent` with a value above 1; has integer columns named like dimensions (`year`, `month`, `code`, `id` or ending in `_id` or `_code`), which would be offered as measures; or has rows that share the values of every dimension, which widgets add up.

#### Scenario: No value column
- **WHEN** an indicator returns the columns `municipality` and `households`
- **THEN** the view warns that there is no `value` column and that widgets will plot `households` by default

#### Scenario: Percentage above one
- **WHEN** an indicator with the format `percent` returns the value `45.2`
- **THEN** the view warns that values above 1 show as more than 100%

#### Scenario: Year as a number
- **WHEN** an indicator returns an integer column `year` next to `value`
- **THEN** the view warns that `year` will be offered as a measure and suggests returning it as text

#### Scenario: Rows added up
- **WHEN** two rows of the table have the same `municipality` and no other dimension
- **THEN** the view warns that widgets will add up the rows that share a municipality
