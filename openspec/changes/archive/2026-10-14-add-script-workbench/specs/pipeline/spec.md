# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

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
