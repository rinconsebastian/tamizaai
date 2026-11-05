# Spec Delta

## ADDED Requirements

### Requirement: Base column of an indicator
An indicator SHALL be able to declare one numeric column of its table as its base: the unweighted count of units, such as households, that each row stands for. The declaration SHALL be saved with the indicator's column names on every version of its script and restored with a version. Declaring a second column as the base SHALL be rejected with an error on that column. A base column that the published table lacks, or does not hold as a number, SHALL be ignored.

#### Scenario: Base declared
- **WHEN** an analyst saves `comercio_gastos_resumen` with `hogares` declared as its base
- **THEN** its widgets count their n on `hogares`

#### Scenario: Two base columns
- **WHEN** an analyst saves an indicator with `hogares` and `personas` both declared as its base
- **THEN** the API responds 400 with an error on `personas`, and no version is created

#### Scenario: Base restored with a version
- **WHEN** an analyst declares a base in version 4 of an indicator and then restores version 3, which declared none
- **THEN** version 5 declares no base

#### Scenario: Base column no longer published
- **WHEN** the published table of an indicator that declares `hogares` as its base has no column `hogares`
- **THEN** its widgets count their n as if it declared no base, and none of them is stale

### Requirement: Values that mean no data in an indicator column
An indicator SHALL be able to declare, for a column that is not a number, up to 20 values that mean no data, such as "Sin dato" or "No sabe", each of 1 to 500 characters and matched as exact text. Saving more than 20, an empty value, a value listed twice or one holding a NUL character SHALL be rejected with an error on that column. The declaration SHALL change no widget that does not leave those values out.

#### Scenario: Block with no comuna
- **WHEN** an analyst declares "Sin dato" as no data for `comuna` of `teatro_hoy`
- **THEN** a widget that leaves out no-data values drops the "Sin dato" rows, and every other widget draws as before

#### Scenario: Matched as exact text
- **WHEN** `comuna` declares "Sin dato" as no data
- **THEN** a row whose `comuna` is `SIN NOMBRE` or `sin dato` is not left out

#### Scenario: Too many values
- **WHEN** an analyst declares 21 values that mean no data for one column
- **THEN** the API responds 400 with an error on that column naming the limit of 20

### Requirement: Answer columns of an indicator
An indicator SHALL be able to declare columns that are not numbers as answer columns, of single or multiple choice: the columns that name a question and its answers in a long table with a row per answer, such as `variable`, `orden` and `categoria`, where every unit of the base is in the denominator of every answer. Declaring another kind of answers SHALL be rejected with an error on that column. The declaration SHALL change no plotted value.

#### Scenario: Long table of answers
- **WHEN** an analyst declares `variable`, `orden` and `categoria` of `comercio_lugar_compra` as the answers of single-choice questions
- **THEN** the n of a widget over several of its answers counts each household once

#### Scenario: Multiple-choice answers
- **WHEN** an analyst declares `categoria` of `teatro_barreras` as the answers of a multiple-choice question
- **THEN** the declaration is saved, and its widgets plot the values they plotted before

#### Scenario: Unknown kind of answers
- **WHEN** an analyst declares `categoria` as answers of the kind `ranking`
- **THEN** the API responds 400 with an error on `categoria`

### Requirement: Base metadata entered with the column names
The indicator's save form SHALL offer, in its column names section, a choice of base among the numeric columns and, for each other column, its values that mean no data and whether it holds the answers of a single- or a multiple-choice question. They SHALL be saved, restored and shown on an older version as the column names are.

#### Scenario: Save form
- **WHEN** an analyst opens the details of `teatro_hoy` in the script editor
- **THEN** its column names section offers `numerator`, `denominator` and `value` as the base, and values that mean no data and a kind of answers for `categoria` and `comuna`

#### Scenario: Older version
- **WHEN** an analyst looks at an older version of `teatro_hoy`
- **THEN** its base, values that mean no data and answer columns are shown as text

#### Scenario: Column no longer in the table
- **WHEN** `teatro_hoy` declares `estrato_hogar` as answers, and neither its latest test nor its published table has that column
- **THEN** the section lists `estrato_hogar` after the other columns as not in the table, with its declaration and a way to clear it

### Requirement: Limit of columns with base metadata
A column that carries a base, values that mean no data or a kind of answers SHALL count, with the columns that carry a label, toward one limit of 100 columns per version of an indicator. Saving facts for more SHALL be refused, with an error on the column names and nothing saved.

#### Scenario: Too many columns with facts
- **WHEN** an analyst saves an indicator with a label for 60 columns and values that mean no data for 41 others
- **THEN** the API responds 400 with an error on the column names saying an indicator labels at most 100 columns, and no version is created
