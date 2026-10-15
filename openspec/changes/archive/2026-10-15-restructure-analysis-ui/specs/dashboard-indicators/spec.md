# Spec Delta

## MODIFIED Requirements

### Requirement: Indicators dashboard and its indicators
Every project member SHALL be able to open the project's indicators dashboard, which SHALL list the project's indicators with their label, unit and format and whether each has published a table. The widgets SHALL come first on the page; the list of indicators MAY follow them inside a disclosure that is collapsed once any indicator has published and opens on demand. While no indicator has published, it SHALL say so, with instructions, SHALL show the list of indicators expanded as part of that message, and SHALL still list any widgets already configured. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a published dashboard
- **WHEN** a `viewer` opens the indicators dashboard of a project where at least one indicator has published
- **THEN** the dashboard's widgets are shown

#### Scenario: Indicator list behind a disclosure
- **WHEN** a member opens the indicators dashboard of a project with 3 indicators, 2 of them published
- **THEN** the widgets come first, a collapsed disclosure reads "Indicators (3) · 2 published", and opening it lists the three indicators with label, unit, format and whether each has published

#### Scenario: No indicator has published
- **WHEN** none of a project's indicators has published, or the project has none
- **THEN** the dashboard says no indicator has published yet, with instructions and the list of indicators expanded, and still lists any configured widgets

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its indicators dashboard
- **THEN** the API responds 404
