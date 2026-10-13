# Spec Delta

## MODIFIED Requirements

### Requirement: Script context
Each script SHALL receive a `ctx` object that exposes the form dictionary, the project's reference tables and a logger. The form dictionary SHALL include, for each question, its name, path, type, label and choice list, and for each choice list its choice names and labels. `ctx.reference` SHALL give each of the project's reference tables as a pandas DataFrame by its name, with the column names and types shown for that table in the project, and SHALL list the available names. Asking for a name the project does not have SHALL raise an error that lists the available names. Messages written through `ctx.log` and output printed by the script SHALL be stored in the step's log and shown in the run's details.

#### Scenario: Label for a choice
- **WHEN** script 2 looks up the choice list of the question `municipality` in `ctx.form` and the code `cali`
- **THEN** it obtains the label `Cali` as defined in the form

#### Scenario: Script messages
- **WHEN** script 1 calls `ctx.log.info("Removed 12 test submissions")` and `print("done")`
- **THEN** both lines appear in the log of the clean step, in order

#### Scenario: Reference table in a script
- **WHEN** the project has a reference table `population` with the integer column `population_2026`, and script 3 divides a count by `ctx.reference["population"]["population_2026"]`
- **THEN** the script receives that column as integers and its indicators use those values

#### Scenario: Unknown reference table
- **WHEN** script 3 reads `ctx.reference["populaton"]` and the project only has `population`
- **THEN** the step fails and its log names `populaton` and lists `population` as available

#### Scenario: Project without reference tables
- **WHEN** a script lists the names in `ctx.reference` in a project without reference tables
- **THEN** it obtains an empty list and the step continues

### Requirement: Runs and the run log
Each run SHALL record its trigger, the user who started it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the version of each script it used, the reference tables it used with their row counts and the time each was last updated, and one entry per step (flatten, clean, output, indicators) with its status (`succeeded`, `failed` or `skipped`), duration, input and output row counts, log and error. A run SHALL read the reference tables once when it starts, so replacing a reference table during a run SHALL NOT affect that run. A run SHALL succeed only when no step failed. Every project member SHALL be able to list the project's runs, newest first, and read each run's details. A run interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Run details
- **WHEN** a member opens a finished run
- **THEN** they see each step with status, duration, rows in and out, its log and, for a failed step, the error and traceback, and the reference tables the run used

#### Scenario: Run uses the current versions
- **WHEN** script 1 is at version 4 and script 3 at version 2 when a run starts, and an analyst saves script 1 again during the run
- **THEN** the run records and uses version 4 of script 1 and version 2 of script 3

#### Scenario: Reference table replaced during a run
- **WHEN** an analyst replaces the reference table `population` while a run is running
- **THEN** the run uses the rows `population` had when the run started and records that table's earlier update time, and the next run uses the new rows

#### Scenario: Worker restarted
- **WHEN** the worker restarts while a run is running
- **THEN** that run is recorded as failed because it was interrupted, and later runs proceed

## ADDED Requirements

### Requirement: Sample test run
An `analyst` or `admin` SHALL be able to test one script on a sample of the project's submissions, using code they send with the request that need not be saved. The sample SHALL hold the requested number of non-deleted submissions, from 1 to 5,000 and 500 by default, or all of them when the project has fewer; it SHALL be spread across the project's submissions rather than only the newest, and consecutive tests SHALL use the same submissions while the project's submissions do not change. A test SHALL flatten the sample; a test of script 2 or 3 SHALL then run the current saved version of script 1, or use the flattened tables when there is none; and it SHALL run the tested code with the same contract, `ctx`, output validation and time and memory limits as a run. A test SHALL NOT create a script version, change any published schema or appear in the project's run list. Viewers SHALL NOT be able to start tests.

#### Scenario: Draft tested without saving
- **WHEN** an analyst tests script 1 with code that differs from its current version
- **THEN** the test runs that code, script 1 keeps the same current version, and the published clean schema is unchanged

#### Scenario: Testing script 2
- **WHEN** an analyst tests a draft of script 2 while script 1 is at version 3
- **THEN** the test flattens the sample, runs version 3 of script 1 on it, and runs the draft on its result

#### Scenario: Script 1 fails in a test of script 3
- **WHEN** the current version of script 1 raises an exception during a test of script 3
- **THEN** the test fails with script 1's error and traceback, and the draft of script 3 is not run

#### Scenario: Small project
- **WHEN** an analyst tests with a sample of 500 in a project with 120 non-deleted submissions
- **THEN** the test uses all 120 submissions

#### Scenario: Same sample twice
- **WHEN** an analyst runs two tests one after the other and no submission was added, changed or deleted in between
- **THEN** both tests use the same submissions

#### Scenario: Invalid output in a test
- **WHEN** a draft of script 3 returns a DataFrame without the column `denominator`
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

### Requirement: Test queue and lifetime
Tests SHALL be executed by the same worker as runs, one at a time. A queued test SHALL start before any queued run, but SHALL wait for a running run or test to finish; while it waits, its status SHALL say whether it waits for a run or for another test. A user SHALL have at most one queued test per project: a new test SHALL replace that user's queued test in the project, and SHALL NOT affect a test that is already running. A test SHALL be visible only to the user who requested it. A finished test SHALL be deleted 24 hours after it ended. A test interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Test while a run is running
- **WHEN** an analyst starts a test while a run is running and another run is queued
- **THEN** the test shows that it waits for a run, starts when the running run ends, and the queued run starts after the test

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
A finished test SHALL show, for each step it ran (flatten, the saved script 1 when used, and the tested script), its status, duration, input and output row counts, log and, for a failed step, the error and traceback. For every table a successful step produced, it SHALL show the table's name and row count, and for the first 20 tables of each step also its column names and types and its first 100 rows. Text values longer than 500 characters SHALL be cut and marked as cut, and when a table's 100 rows would exceed 1 MB, the preview SHALL show fewer rows and say how many. While a test is queued or running, the UI SHALL show its status and update it without reloading the page.

#### Scenario: Preview of a cleaned table
- **WHEN** a test of script 1 succeeds and returns a table `main` with 480 rows
- **THEN** the test shows `main` with 480 rows, its columns and types, and its first 100 rows

#### Scenario: Flattened tables in the preview
- **WHEN** a test of script 2 succeeds on a form with a repeat group `members`
- **THEN** the preview also shows the flattened `main` and `members` tables the scripts received

#### Scenario: Failed test
- **WHEN** a draft raises `KeyError: 'age'`
- **THEN** the test shows the failed step with the traceback pointing at the draft's line, and the tables of the steps before it

### Requirement: Reference tables
An `analyst` or `admin` SHALL be able to upload a CSV or XLSX file of at most 10 MB as a project reference table with a name, to replace a reference table's rows with a new file, and to delete a reference table. A project SHALL have at most 50 reference tables. Names SHALL be unique in the project and SHALL consist of lowercase letters, digits and underscores, start with a letter and have at most 63 characters. Every project member SHALL be able to list the project's reference tables, with name, original file name, row and column counts, and who last updated each one and when, and to preview a table's columns with their types and its first 100 rows.

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

#### Scenario: Viewer previews
- **WHEN** a `viewer` opens the reference table `population`
- **THEN** they see its columns with types and its first 100 rows

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
