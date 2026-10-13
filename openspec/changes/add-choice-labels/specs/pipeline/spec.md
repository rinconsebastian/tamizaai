# Spec Delta

## MODIFIED Requirements

### Requirement: Script context
Each script SHALL receive a `ctx` object that exposes the form dictionary, the project's reference tables and a logger. The form dictionary SHALL describe the project's current form: for each question, its name, path, type, label and choice list, and for each choice list its choice names and labels; it SHALL give the label of a code, and the code of a label used by the current form or by an earlier form version. `ctx.reference` SHALL give each of the project's reference tables as a pandas DataFrame by its name, with the column names and types shown for that table in the project, and SHALL list the available names. Asking for a name the project does not have SHALL raise an error that lists the available names. Messages written through `ctx.log` and output printed by the script SHALL be stored in the step's log and shown in the run's details.

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

## ADDED Requirements

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
