# Spec Delta

## ADDED Requirements

### Requirement: Column labels in widget files
An image of a widget SHALL name its columns as the screen does, in its caption and its table. A widget's workbook SHALL name them as a scheduled email's workbook does, with the widget's own labels as the member's screen sent them and its indicator's labels as the current version of its script gives them, and its "Summary" sheet SHALL say what the widget plots with those names.

#### Scenario: Image caption
- **WHEN** a member exports as a PNG image a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy` grouped by `comuna`, whose indicator labels them "Hogares que asistieron", "Hogares" and "Comuna"
- **THEN** the image's caption reads "Teatro hoy: Hogares que asistieron / Hogares by Comuna"

#### Scenario: Widget's own label in the workbook
- **WHEN** a member exports the workbook of that chart, whose own label for `denominator` is "Hogares de la base E1"
- **THEN** the data sheet's value header and the "Summary" sheet's line of what it plots read "Teatro hoy: Hogares que asistieron / Hogares de la base E1"

#### Scenario: Widget of a version
- **WHEN** an `analyst` exports the workbook of a widget of version 9 of "Teatro", whose own labels differ from the widget's labels today
- **THEN** the workbook names the columns with version 9's labels
