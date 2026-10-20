# Spec Delta

## MODIFIED Requirements

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
