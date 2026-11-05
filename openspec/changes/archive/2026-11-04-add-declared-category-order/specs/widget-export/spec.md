# Spec Delta

## ADDED Requirements

### Requirement: Declared category order in widget files
A widget's PNG or SVG image SHALL show its values in the order and with the text the member sees on screen. A widget's workbook SHALL list its groups and series in that order and with that text, from the widget's settings as the member's page shows them, its own order included, and from its indicator's current column metadata.

#### Scenario: Image in the declared order
- **WHEN** a member exports as a PNG image a column chart whose series follow a declared order with prefixes hidden
- **THEN** the image shows the segments and the legend in that order, without the prefixes

#### Scenario: Workbook in the widget's order
- **WHEN** a member exports the workbook of a widget with its own order of series
- **THEN** the data sheet's series columns follow that order

#### Scenario: Workbook of a version
- **WHEN** an `analyst` exports the workbook of a widget of version 9 of "2. Teatro", where that widget had its own order of groups that it no longer has
- **THEN** the data sheet lists the groups in the order the widget had in version 9
