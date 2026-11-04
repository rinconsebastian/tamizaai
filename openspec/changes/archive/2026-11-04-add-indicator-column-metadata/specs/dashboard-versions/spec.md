# Spec Delta

## MODIFIED Requirements

### Requirement: Dashboard versions
Each of a project's own dashboards SHALL have a history of versions. A version SHALL hold the dashboard's settings (its name, its filters with their defaults, its palette, its colors kept by value and its other display settings) and its widgets, each with every setting it is saved with: its title, chart type, query, display settings (how it is drawn, such as its color or scale, its orientation and its own column labels), the dashboard filters it ignores, its position and its size. An indicator's column labels belong to the versions of its script, not to a dashboard's. A version SHALL record its number, who made it, when its last change was saved, where it was made (Tamiza or an AI client), and what changed. The newest version SHALL be the dashboard as it is.

#### Scenario: Widget added
- **WHEN** an `analyst` adds the widget "Water source" to "Housing", whose newest version is 4
- **THEN** "Housing" has version 5, attributed to that analyst at that time, holding the new widget with the others, and saying the widget "Water source" was added

#### Scenario: Numbers never reused
- **WHEN** "Housing" has versions 3 to 12 and an `analyst` restores version 9
- **THEN** the restore is version 13

#### Scenario: Orientation restored
- **WHEN** an `analyst` changes a box plot of "Housing" from vertical to horizontal, and then restores the version before that change
- **THEN** the box plot is vertical again, and the restore is a new version

#### Scenario: Widget's column labels restored
- **WHEN** an `analyst` changes a column chart's own label for `denominator` on "Teatro" from "Hogares" to "Hogares que asistieron", and then restores the version before that change
- **THEN** the chart names its denominator "Hogares" again, and the restore is a new version

#### Scenario: Indicator's labels in a version
- **WHEN** an `analyst` changes the label of `comuna` of `teatro_hoy` after version 5 of "Teatro" was recorded, and then views version 5
- **THEN** the widgets of version 5 name `comuna` with the indicator's current label
