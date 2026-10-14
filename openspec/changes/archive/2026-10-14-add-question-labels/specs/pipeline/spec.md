# Spec Delta

## MODIFIED Requirements

### Requirement: Script context
Each script SHALL receive a `ctx` object that exposes the form dictionary, the project's reference tables and a logger. The form dictionary SHALL describe the project's current form: for each question, its name, path, type, label and choice list, and for each choice list its choice names and labels; it SHALL give the label of a code, and the code of a label used by the current form or by an earlier form version. It SHALL also list every column of the flattened tables that holds answers, not the system columns, with its table, the path of its question (the submission key for a column whose question the form no longer has) and its question label, and SHALL give the question label of a column by its table and column name. `ctx.reference` SHALL give each of the project's reference tables as a pandas DataFrame by its name, with the column names and types shown for that table in the project, and SHALL list the available names. Asking for a name the project does not have SHALL raise an error that lists the available names. Messages written through `ctx.log` and output printed by the script SHALL be stored in the step's log and shown in the run's details.

#### Scenario: Label for a choice
- **WHEN** script 2 looks up the choice list of the question `municipality` in `ctx.form` and the code `cali`
- **THEN** it obtains the label `Cali` as defined in the form

#### Scenario: Code for a label
- **WHEN** script 3 asks `ctx.form` for the code of the label `Cali` of the question `municipality`
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
- **WHEN** the project has a reference table `population` with the integer column `population_2026`, and script 3 divides a count by `ctx.reference["population"]["population_2026"]`
- **THEN** the script receives that column as integers and its indicators use those values

#### Scenario: Unknown reference table
- **WHEN** script 3 reads `ctx.reference["populaton"]` and the project only has `population`
- **THEN** the step fails and its log names `populaton` and lists `population` as available

#### Scenario: Project without reference tables
- **WHEN** a script lists the names in `ctx.reference` in a project without reference tables
- **THEN** it obtains an empty list and the step continues

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

## ADDED Requirements

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
