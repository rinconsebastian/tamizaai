# Spec Delta

## ADDED Requirements

### Requirement: Base n and no-data lines in a scheduled email
A scheduled email SHALL apply each widget's base settings and its dashboard's, as the dashboard does with its filters at their defaults. A widget's base n line and no-data line SHALL be among its notes, in the body ahead of its footnote and on the workbook's "Summary", and a widget with no series whose values have a base n and whose base n line or threshold is in force SHALL have the n column of its "Show as table" view, in the body and in its sheet.

#### Scenario: Base n line in the email
- **WHEN** "Teatro" shows its base n lines and one of its widgets has the n 223 with its denominator named "Hogares que asistieron"
- **THEN** that widget's notes in the body and on the "Summary" read "n = 223 Hogares que asistieron"

#### Scenario: n column in the email
- **WHEN** "Teatro" has the threshold 30 and its scheduled email holds a column chart by `comuna`
- **THEN** the chart's table in the body and in its sheet has an n column after the value, holding 16 for `COMUNA 7` as a whole number

#### Scenario: No-data line in the email
- **WHEN** a widget leaves out "Sin dato", which held 1 household of 476
- **THEN** its notes say what "Sin dato" held, as the line under the widget does

#### Scenario: Lines before the footnote
- **WHEN** "Teatro" shows its base n lines and its email holds the bar chart "2.4. En qué lugares", which has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** its block shows the n line among the notes above the table and the footnote after the table

#### Scenario: Email of a dashboard without base settings
- **WHEN** a dashboard and its widgets have no base settings
- **THEN** its scheduled email has the tables and notes it had before base settings existed

### Requirement: Small bases in a scheduled email
In a scheduled email, a value with a small base SHALL be followed by a mark in the body's table, and the widget's notes SHALL explain the mark and say how many values have a small base, in the body and on the workbook's "Summary". The workbook's cells SHALL hold the numbers alone.

#### Scenario: Marked value
- **WHEN** `COMUNA 7`, with the n 16, and `SIN NOMBRE`, with the n 20, are under the threshold 30
- **THEN** their values in the body's table carry the mark, and the widget's notes say that 2 values have a base below 30

#### Scenario: Workbook cell of a small base
- **WHEN** the workbook holds the value of `COMUNA 7`, 0.5
- **THEN** its cell holds the number 0.5 alone, and the "Summary" holds the note
