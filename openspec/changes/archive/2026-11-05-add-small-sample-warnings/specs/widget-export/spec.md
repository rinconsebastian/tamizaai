# Spec Delta

## ADDED Requirements

### Requirement: Base n and small bases in widget images
A widget's PNG or SVG image SHALL draw its marks dimmed as the screen does, and SHALL show under its chart, before its footnote, the small-base note, the base n line and the no-data line that the widget shows.

#### Scenario: Dimmed columns in an image
- **WHEN** a member exports as a PNG image "2.1. Asistió, por comuna", whose `COMUNA 7` and `SIN NOMBRE` columns are dimmed
- **THEN** the image shows those two columns lighter with their dashed outline, and the note that marks with a base below 30 are dimmed

#### Scenario: Lines in an image
- **WHEN** a member exports as an SVG image a widget that shows "n = 223 Hogares que asistieron" and "No sabe: 2.24%" under its chart
- **THEN** the image shows both lines under its chart

#### Scenario: Lines before the footnote in an image
- **WHEN** a member exports as a PNG image "2.4. En qué lugares", which shows "n = 223 Hogares que asistieron" and has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the image shows the n line under the chart and the footnote under the n line, above the footer

### Requirement: Base n and small bases in a widget workbook
A widget's workbook SHALL apply the base n line and the small-base threshold that the member's screen applies to the widget, which the page sends with the request, and SHALL leave out no-data values as the widget does. Its data sheet and "Summary" SHALL then be the widget's table and notes in a scheduled email. A request without base settings SHALL give the workbook given before they existed.

#### Scenario: Threshold in force on screen
- **WHEN** a member exports the workbook of "2.1. Asistió, por comuna" on "Teatro", whose threshold is 30
- **THEN** its data sheet has an n column, and its "Summary" says that 2 values have a base below 30

#### Scenario: Version without a threshold
- **WHEN** "Teatro" has the threshold 30 now and an `analyst` exports a widget while its version 9, which had none, is shown
- **THEN** the workbook has no n column and no small-base note

#### Scenario: Request without base settings
- **WHEN** a page from a release before base settings asks for a widget's workbook
- **THEN** the workbook is the one that release gave
