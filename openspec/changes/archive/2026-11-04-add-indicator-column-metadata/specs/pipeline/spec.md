# Spec Delta

## MODIFIED Requirements

### Requirement: Indicator metadata
An indicator SHALL have a label, an optional description, an optional unit, a format that is one of `count`, `decimal`, `percent` (a fraction shown as a percentage) or `currency`, and optional labels for the columns of its table (see "Column labels of an indicator"). The metadata SHALL be saved with every version of the indicator's script.

#### Scenario: Metadata saved
- **WHEN** an analyst creates the indicator `piped_water_rate` with the label `Households with piped water` and the format `percent`
- **THEN** the project lists the indicator with that label and format

#### Scenario: Metadata restored with a version
- **WHEN** an analyst saves version 2 of an indicator with a new label and then restores version 1
- **THEN** version 3 has the code and the label of version 1

#### Scenario: Column labels restored with a version
- **WHEN** an analyst saves version 2 of `teatro_hoy` with the label "Hogares que asistieron" for `numerator`, saves version 3 without it, and then restores version 2
- **THEN** version 4 has the code of version 2 and the label "Hogares que asistieron" for `numerator`

## ADDED Requirements

### Requirement: Column labels of an indicator
An indicator SHALL be able to give a label to each of up to 100 columns of its table: 1 to 80 characters, shown for that column instead of the column's own name. A label SHALL be trimmed, and an empty one SHALL mean the column has none. A column SHALL be named as its table publishes it, and SHALL NOT be checked against the published table, so a label can be given before the first run and is kept while a script leaves the column out. A label change SHALL NOT make a dashboard version.

#### Scenario: Labels of a ratio
- **WHEN** an analyst saves `teatro_hoy` with the label "Hogares que asistieron" for `numerator` and "Hogares" for `denominator`
- **THEN** the project lists `teatro_hoy` with those two labels, and its other columns with none

#### Scenario: Label trimmed
- **WHEN** an analyst saves the label "  Comuna  " for `comuna`
- **THEN** the label is saved as "Comuna"

#### Scenario: Label cleared
- **WHEN** an analyst empties the label of `comuna` and saves `teatro_hoy`
- **THEN** `comuna` has no label, and widgets name it `comuna`

#### Scenario: Label before the first run
- **WHEN** an analyst saves an indicator that has never published with the label "Grupo de edad" for `grupo_edad`
- **THEN** the label is saved, and the indicator's widgets show it once a run publishes the column

#### Scenario: No dashboard version
- **WHEN** an analyst changes the label of `numerator` of `teatro_hoy`, which 20 widgets of "Teatro" plot
- **THEN** those widgets show the new label the next time they are drawn, and "Teatro" has no new version

### Requirement: Column labels that are refused
Saving an indicator SHALL be refused, with an error on the column and nothing saved, when a label is longer than 80 characters or holds a control character, when a label is given for a name that an indicator table's column cannot have (lowercase letters, digits and underscores, not starting with a digit, up to 63 characters), or when more than 100 columns have a label. A property of a column that the API does not know SHALL be ignored, not refused.

#### Scenario: Label too long
- **WHEN** an analyst saves a label of 81 characters for `numerator`
- **THEN** the API responds 400 with an error on the label of `numerator` saying a label has at most 80 characters, and no version is created

#### Scenario: Not a column's name
- **WHEN** an analyst saves a label for the column `Hogar Menores`
- **THEN** the API responds 400 with an error on the column labels saying `Hogar Menores` is not a column's name

#### Scenario: Line break in a label
- **WHEN** an analyst saves a label for `numerator` that holds a line break
- **THEN** the API responds 400 with an error on the label of `numerator`

#### Scenario: Too many labels
- **WHEN** an analyst saves labels for 101 columns
- **THEN** the API responds 400 with an error on the column labels saying an indicator labels at most 100 columns

#### Scenario: Unknown property
- **WHEN** an AI client saves `numerator` with a label "Hogares" and a property `colour`
- **THEN** the label is saved and the property `colour` is not

### Requirement: Column labels kept by a save that leaves them out
A save of an indicator that gives no column labels SHALL keep those of its current version, as they are stored. A save that gives them SHALL replace them all, and an empty set SHALL clear them. An indicator saved without column labels when it has no current version, because it is new or was removed, SHALL have none. A removal SHALL keep the labels of the earlier versions, so restoring one of them brings its labels back.

#### Scenario: Code saved without labels
- **WHEN** version 4 of `teatro_hoy` labels `numerator` "Hogares que asistieron", and a save of new code gives its label, unit and format but no column labels
- **THEN** version 5 keeps the label "Hogares que asistieron" for `numerator`

#### Scenario: Labels replaced
- **WHEN** version 5 of `teatro_hoy` labels `numerator` and `denominator`, and a save gives only the label "Comuna" for `comuna`
- **THEN** version 6 labels `comuna` only

#### Scenario: Labels cleared
- **WHEN** a save of `teatro_hoy` gives an empty set of column labels
- **THEN** its new version labels no column

#### Scenario: Removed and created again
- **WHEN** an analyst removes `teatro_hoy`, whose last version labels `numerator`, and later saves a new script under the same name without column labels
- **THEN** the new version labels no column, and restoring the version before the removal brings the label of `numerator` back

### Requirement: Column names in the indicator's details
The details of an indicator SHALL offer a "Column names" section with one optional label per column of its table: the columns the latest test of the draft returned, or without one the published table's, in the table's order. A column with a saved label that this list lacks SHALL follow them, marked as not in the table, so its label can be kept or cleared. Labels being typed SHALL show in the test's dashboard view. A version viewed read-only SHALL show its labels as text.

#### Scenario: Columns of the published table
- **WHEN** an analyst opens the details of `teatro_hoy`, published with the columns `variable`, `orden`, `categoria`, `comuna`, `estrato`, `value`, `numerator` and `denominator`, and not tested since
- **THEN** the "Column names" section lists those columns in that order, each with its label or an empty field

#### Scenario: Columns of the latest test
- **WHEN** the latest test of the draft of `teatro_barreras` returned the column `barrera_tipo`, which its published table does not have
- **THEN** the section lists `barrera_tipo` among the columns of the test

#### Scenario: Column no longer in the table
- **WHEN** `teatro_hoy` labels `estrato_hogar`, which neither its latest test nor its published table has
- **THEN** the section lists `estrato_hogar` after the other columns as not in the table, with its label and a way to clear it

#### Scenario: New indicator
- **WHEN** an analyst adds an indicator that has never been tested or published
- **THEN** the section says that its columns are listed after a test, and the indicator is saved with no column labels

#### Scenario: Test view uses the labels being typed
- **WHEN** an analyst types "Hogares que asistieron" for `numerator` of `teatro_hoy` without saving, and looks at the dashboard view of its latest test
- **THEN** the view's ratio tile reads "Teatro hoy: Hogares que asistieron / denominator"

#### Scenario: Older version viewed
- **WHEN** an analyst views version 2 of `teatro_hoy`, which labels `comuna` "Comuna"
- **THEN** the details show "Comuna" for `comuna` as text, with no field to change it
