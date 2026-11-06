# Spec Delta

## MODIFIED Requirements

### Requirement: Answer columns of an indicator
An indicator SHALL be able to declare columns that are not numbers as answer columns, of single or multiple choice: the columns that name a question and its answers in a long table with a row per answer, such as `variable`, `orden` and `categoria`, where every unit of the base is in the denominator of every answer. Declaring another kind of answers SHALL be rejected with an error on that column. The declaration SHALL change no plotted value, except a ratio over several of its answers (see `dashboard-indicators` "Ratio over declared categories never summed across their values" and "Ratio over declared multiple-choice categories refused").

#### Scenario: Long table of answers
- **WHEN** an analyst declares `variable`, `orden` and `categoria` of `comercio_lugar_compra` as the answers of single-choice questions
- **THEN** the n of a widget over several of its answers counts each household once

#### Scenario: Multiple-choice answers
- **WHEN** an analyst declares `categoria` of `teatro_barreras` as the answers of a multiple-choice question
- **THEN** the declaration is saved, and its widgets whose values each hold one answer plot the values they plotted before

#### Scenario: Unknown kind of answers
- **WHEN** an analyst declares `categoria` as answers of the kind `ranking`
- **THEN** the API responds 400 with an error on `categoria`
