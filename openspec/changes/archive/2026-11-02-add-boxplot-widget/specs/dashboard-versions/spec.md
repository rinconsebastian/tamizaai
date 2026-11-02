## MODIFIED Requirements

### Requirement: Dashboard versions
Each of a project's own dashboards SHALL have a history of versions. A version SHALL hold the dashboard's name, its filters with their defaults, its palette and colors kept by value, and its widgets with their title, chart type, query, color or scale, orientation, the dashboard filters they ignore, position and size. It SHALL record its number, who made it, when its last change was saved, where it was made (Tamiza or an AI client), and what changed. The newest version SHALL be the dashboard as it is.

#### Scenario: Widget added
- **WHEN** an `analyst` adds the widget "Water source" to "Housing", whose newest version is 4
- **THEN** "Housing" has version 5, attributed to that analyst at that time, holding the new widget with the others, and saying the widget "Water source" was added

#### Scenario: Numbers never reused
- **WHEN** "Housing" has versions 3 to 12 and an `analyst` restores version 9
- **THEN** the restore is version 13

#### Scenario: Orientation restored
- **WHEN** an `analyst` changes a box plot of "Housing" from vertical to horizontal, and then restores the version before that change
- **THEN** the box plot is vertical again, and the restore is a new version
