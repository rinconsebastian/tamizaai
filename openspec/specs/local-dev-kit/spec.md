# local-dev-kit Specification

## Purpose

Lets analysts write and check scripts on their own computer, in Jupyter or any Python editor: they download a sample of a project's data, together with what `ctx` is built from, and use a small package that reproduces the server's contract, so a script that passes locally behaves the same once uploaded.

## Requirements

### Requirement: Sample download request
An `analyst` or `admin` SHALL be able to request a sample download of a project, with a sample size from 1 to 5,000 submissions, 500 by default. A request SHALL NOT create a script version, change a published schema or appear in the run list. Viewers SHALL NOT request downloads.

#### Scenario: Analyst requests a sample
- **WHEN** an analyst requests a sample download of 500 submissions
- **THEN** the download is queued with that sample size, and the published schemas and script versions are unchanged

#### Scenario: Viewer requests a sample
- **WHEN** a `viewer` requests a sample download
- **THEN** the API responds 403 and nothing is queued

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests a sample download
- **THEN** the API responds as for a project that does not exist and nothing is queued

#### Scenario: Sample size out of range
- **WHEN** an analyst requests a sample download of 10,000 submissions
- **THEN** the API rejects the request with a validation error and nothing is queued

### Requirement: Sample download contents
A sample download SHALL hold the tables a test of the same sample size would give the scripts: the flattened sample (script 1's input) and the result of the current saved script 1 on it (the input of script 2 and of the indicators). It SHALL also hold the project's reference tables with the types scripts receive, and the data that `ctx` is built from: the project name, the form dictionary, the label codes of every form version and the question labels of the flattened columns.

#### Scenario: Form with a repeat group
- **WHEN** an analyst downloads a sample of 500 from a project whose form has the repeat group `members` and whose script 1 is at version 3
- **THEN** the download holds the flattened tables `main` and `members` for the same 500 submissions a test of 500 would use, the tables version 3 of script 1 returns for them, and the project's reference tables

#### Scenario: Labels in the downloaded tables
- **WHEN** the form's select question `water_source` has the choice `piped` labeled `Piped`
- **THEN** the downloaded flattened tables hold `Piped`, as the tables a test gives the scripts do

#### Scenario: Project without script 1
- **WHEN** an analyst downloads a sample of a project that has no script 1
- **THEN** the download holds the flattened tables, and they are also what the download gives as the clean tables, as in a run

#### Scenario: Deleted submissions
- **WHEN** a submission was deleted in Kobo
- **THEN** it is not in any downloaded table

### Requirement: Script 1 failing on the sample
When the saved script 1 fails on the sample, the download SHALL still succeed, with the flattened tables and without clean tables. The download and its status SHALL say that script 1 failed, give its version and error, and name the flattened tables as the input script 1 receives.

#### Scenario: Script 1 raises an exception
- **WHEN** version 4 of script 1 raises `KeyError: 'age'` on the sample
- **THEN** the download succeeds with the flattened tables only, and its status says that version 4 of script 1 failed with `KeyError: 'age'`

### Requirement: Reuse of a cached sample
Preparing a download SHALL reuse the flattened sample, and the result of the saved script 1, that an earlier test or download kept, under the same conditions as a test's reuse. Reuse SHALL NOT change what a download holds.

#### Scenario: Download after a test
- **WHEN** an analyst tests an indicator on 500 submissions and then requests a sample download of 500, and nothing changed in between
- **THEN** the download is prepared without flattening the sample or running script 1 again, and holds the same tables the test used

### Requirement: Download format
A download SHALL be one zip file. It SHALL hold, in one folder, the tables as Parquet files, one folder per source (`raw`, `clean`, `reference`), and `context.json` in the format the server's script runner reads. It SHALL also hold a manifest with the format version, the server version, the creation time, the project, the sample size and number of submissions, the script 1 version and outcome, every table with its row count, and the Python and library versions of the server.

#### Scenario: Manifest of a download
- **WHEN** an analyst downloads a sample of 500 from a project with 320 submissions, script 1 at version 2 and the reference table `population`
- **THEN** the manifest gives 500 as the requested size, 320 submissions, version 2 of script 1 as succeeded, each raw and clean table with its rows, `population` with its rows and last update, and the server's Python and pandas versions

### Requirement: Kit files in the download
A download SHALL hold the SDK package from the server that prepared it and a requirements file that pins the versions of the libraries scripts can use on that server. It SHALL also hold a README and a starter notebook that load the sample, check an indicator and explain how to upload the script. It SHALL NOT hold credentials, server addresses or tokens.

#### Scenario: Libraries pinned to the server
- **WHEN** the server's analytics image has pandas 3.0.6 and pyarrow 25.0.1
- **THEN** the requirements file in the download pins `pandas==3.0.6` and `pyarrow==25.0.1`, and lists no database driver

#### Scenario: Nothing secret in the download
- **WHEN** the downloaded zip is searched for the database password, the project's Kobo token or its webhook secret
- **THEN** none of them is found

### Requirement: Sample download size limit
A download SHALL be limited to a size set by the deployment, 100 MB by default. A sample whose zip would exceed it SHALL fail with a message that gives its size and the limit and suggests a smaller sample, and nothing SHALL be kept for it.

#### Scenario: Large reference tables
- **WHEN** the limit is 100 MB and the project's reference tables make the zip 130 MB
- **THEN** the download fails with a message that the sample is 130 MB and the limit is 100 MB, and no file is kept

### Requirement: Sample download queue
Sample downloads SHALL be prepared by the same worker as tests and runs, one job at a time, after queued tests and before queued runs. While a download waits, its status SHALL say whether it waits for a run, a test or another download. A user SHALL have at most one download per project that is queued or ready: a new request SHALL replace it, but SHALL NOT affect one that is being prepared.

#### Scenario: Download while a run is running
- **WHEN** an analyst requests a sample download while a run is running and another run is queued
- **THEN** the download says that it waits for a run, is prepared when the running run ends, and the queued run starts after it

#### Scenario: New request replaces a ready download
- **WHEN** an analyst whose earlier download in the project is ready requests a new one
- **THEN** the earlier download can no longer be read or downloaded, and the new one is queued

#### Scenario: Worker restarted while preparing
- **WHEN** the worker restarts while a download is being prepared
- **THEN** the download is recorded as failed because it was interrupted

### Requirement: Sample download lifetime and visibility
A sample download SHALL be visible only to the user who requested it. A prepared download SHALL be kept for one hour after it is ready; after that, it SHALL no longer be readable or downloadable, and its file SHALL be deleted.

#### Scenario: Another member's download
- **WHEN** an admin requests the status of a sample download that an analyst requested
- **THEN** the API responds 404

#### Scenario: Expired download
- **WHEN** a sample download became ready more than one hour ago
- **THEN** its status can no longer be read, no link can be issued for it, and its file is deleted

### Requirement: Sample download through a single-use link
While a prepared download is kept, its requester SHALL be able to ask for a download link as many times as they need. Each link SHALL be a single-use download link and SHALL serve the zip as an attachment named after the project and the date. A link SHALL NOT be issued for a download that is not ready.

#### Scenario: Download the zip
- **WHEN** the requester asks for a link to a ready download and the browser opens it
- **THEN** the browser saves a zip named after the project and the date, identical to the one the worker prepared

#### Scenario: Link used twice
- **WHEN** a link was already used to download the zip
- **THEN** opening it again responds 404, and the requester can ask for a new link

#### Scenario: Not ready yet
- **WHEN** the requester asks for a link while the download is still queued or being prepared
- **THEN** the API responds with a conflict and issues no link

### Requirement: Sample download in the Scripts section
For analysts and admins, the Scripts section SHALL offer "Download sample" next to "Test on sample", with the same sample size field. While the download is prepared, it SHALL show its status and what it waits for, updated without reloading. When it is ready, it SHALL show the number of submissions, the tables with their rows, the script 1 outcome and the size, a button that downloads it, and a notice that the file holds the project's submissions.

#### Scenario: Prepare and download
- **WHEN** an analyst with a sample size of 300 clicks "Download sample" and the download becomes ready
- **THEN** the section shows the 300 submissions, the tables with their rows and the size, and clicking the download button saves the zip

#### Scenario: Viewer
- **WHEN** a `viewer` opens the address of the project's Scripts section
- **THEN** the project's performance dashboard opens instead, and no "Download sample" action is shown

#### Scenario: Script 1 failed
- **WHEN** the download is ready but the saved script 1 failed on the sample
- **THEN** the section shows a warning with script 1's version and error, and says that the zip holds only the flattened tables

#### Scenario: Download expired
- **WHEN** the analyst clicks the download button more than one hour after the download became ready
- **THEN** the section says that the download expired and offers to prepare a new one

### Requirement: Loading the context locally
The SDK SHALL build, from a downloaded folder, a `ctx` with the members a script receives on the server and the same answers for the same data: the form dictionary with its lookups, the reference tables, the log and the project name. Writing to `ctx.log` SHALL print the message. `ctx.show` SHALL display the value in a notebook, or print it outside one, and SHALL return it unchanged.

#### Scenario: Label lookup locally
- **WHEN** an analyst loads the context of a download whose form labels the code `cali` of `municipality` as `Cali` and calls `ctx.form.label("municipality", "cali")`
- **THEN** they obtain `Cali`, as a script on the server would

#### Scenario: Unknown reference table locally
- **WHEN** an analyst reads `ctx.reference["populaton"]` from a download that has only `population`
- **THEN** the same error as on the server is raised, naming `population` as available

#### Scenario: Show in a notebook
- **WHEN** an analyst calls `ctx.show(main, "main")` in a Jupyter notebook
- **THEN** the notebook displays `main` under the name `main`, and the call returns `main`

### Requirement: Loading the tables locally
The SDK SHALL give the downloaded `raw` or `clean` tables as a dictionary of table names to pandas DataFrames, with the same column types the server's scripts receive, and as new objects on each call. When script 1 failed on the sample, asking for the `clean` tables SHALL raise an error that gives script 1's error and suggests the `raw` tables.

#### Scenario: Same types as on the server
- **WHEN** the flattened table `main` has an integer column `age` with a missing value
- **THEN** the local `main["age"]` has pandas' nullable integer type with a missing value, as in a script on the server

#### Scenario: Clean tables after script 1 failed
- **WHEN** an analyst asks for the `clean` tables of a download in which script 1 failed
- **THEN** an error says that script 1 failed on this sample, gives its error, and suggests loading the `raw` tables

### Requirement: Checking a script locally
The SDK SHALL check a contract function, or a `.py` file, for a script kind and, for an indicator, a name. It SHALL run the script on new copies of its inputs: the raw tables for script 1, the clean tables otherwise, or tables the analyst passes. It SHALL apply a run's output validation with a run's messages and return the tables as they would be published, or raise the script's own exception with its traceback.

#### Scenario: Indicator without a numeric column
- **WHEN** an analyst checks an indicator whose `compute` returns only text columns
- **THEN** the check fails with the message a run would give, that the table has no numeric column

#### Scenario: Columns renamed as in a run
- **WHEN** a checked script 2 returns a table with the column `Household size`
- **THEN** the check reports that `Household size` is published as `household_size`, and the returned table has `household_size`

#### Scenario: Script raises
- **WHEN** a checked `.py` file raises `KeyError: 'age'` on its line 9
- **THEN** the check raises that `KeyError`, with a traceback that points at line 9 of the file

#### Scenario: Inputs not changed
- **WHEN** a checked script 1 drops rows from `raw["main"]` in place
- **THEN** the tables the analyst loaded earlier with the SDK are unchanged

#### Scenario: Chain scripts locally
- **WHEN** an analyst checks a local script 1 and passes the tables it returns to the check of an indicator
- **THEN** the indicator is run on those tables instead of the downloaded clean tables

### Requirement: Scripts checked as the server loads them
When the SDK checks a `.py` file, it SHALL run the file in a fresh namespace, as the server does, and look up the kind's contract function in it. Names the analyst defined only in the notebook SHALL NOT be visible to the file.

#### Scenario: Helper defined only in the notebook
- **WHEN** a `.py` file calls `tidy(main)` and `tidy` was defined in a notebook cell but not in the file
- **THEN** the check fails with a `NameError` for `tidy`, as the uploaded script would on the server

#### Scenario: Missing contract function
- **WHEN** a `.py` checked as script 2 defines `prepare(cleaned, ctx)` and no `prepare_output`
- **THEN** the check fails with the message a run gives, that `prepare_output` is not defined

### Requirement: Dashboard facts of an indicator checked locally
A check of an indicator SHALL also return its table as the indicators dashboard will offer it: the measures, the default measure, whether the ratio of `numerator` to `denominator` is available, the dimensions and the text columns holding WKT geometries.

#### Scenario: Rate by municipality checked locally
- **WHEN** an analyst checks an indicator returning `municipality`, `value`, `numerator` and `denominator`
- **THEN** the check gives `value`, `numerator` and `denominator` as measures, `value` as the default, the ratio as available and `municipality` as the dimension

### Requirement: Same results as a sample test
For the same downloaded sample and the same script, a successful check SHALL return the same tables as a sample test of that script: the same names, columns, column types and values. This does not apply to time and memory limits, which only the server applies.

#### Scenario: Indicator checked and tested
- **WHEN** an analyst checks an indicator on a downloaded sample of 500, uploads the file with "Upload .py" and tests it on a sample of 500 with nothing changed in between
- **THEN** the test's table has the same columns, types and rows as the table the check returned

### Requirement: Environment differences reported
The SDK SHALL warn, once per session, when the local Python minor version or the version of a pinned library differs from the server's recorded in the download. It SHALL refuse, with a message, a download whose format it does not know.

#### Scenario: Different pandas version
- **WHEN** an analyst with pandas 2.2 installed loads a download made by a server with pandas 3.0.6
- **THEN** the SDK warns that pandas 2.2 differs from the server's 3.0.6 and that results may differ, and still loads the tables

#### Scenario: Unknown download format
- **WHEN** the SDK of one download is used on a folder whose manifest has a newer format version
- **THEN** it refuses with a message to use the SDK included in that download

### Requirement: SDK works offline
The SDK SHALL work without the Tamiza server, the database or a network connection, on Windows, macOS and Linux, using only the downloaded folder and the libraries in its requirements file.

#### Scenario: No network
- **WHEN** an analyst with no network connection opens the starter notebook of a download, with the pinned libraries installed
- **THEN** every cell of the notebook runs and the check of the example indicator succeeds
