## MODIFIED Requirements

### Requirement: Duplicating a dashboard
An `analyst` or `admin` SHALL be able to duplicate a dashboard under a new name, proposed as the original's name followed by " (copy)" and shortened to fit 40 characters. The copy SHALL be placed right after the original and SHALL hold a copy of each of its widgets, with the same title, chart type, query, ignored filters, color, position and size, a copy of its filters, in the same order with the same defaults, and its palette and colors kept by value. Later changes to one SHALL NOT change the other. The copy SHALL have no scheduled email (see `dashboard-reports`). Duplicating SHALL be refused when the project already has 7 dashboards.

#### Scenario: Dashboard duplicated
- **WHEN** an `analyst` duplicates "Housing", which holds 8 widgets, as "Housing 2026"
- **THEN** "Housing 2026" comes right after "Housing" and holds 8 widgets in the same positions and sizes

#### Scenario: Copies are independent
- **WHEN** the `analyst` then removes a widget from "Housing 2026"
- **THEN** "Housing" still holds its 8 widgets

#### Scenario: Proposed name
- **WHEN** an `analyst` asks to duplicate "Housing"
- **THEN** the name field reads "Housing (copy)" and can be changed before saving

#### Scenario: Proposed name for a long name
- **WHEN** an `analyst` asks to duplicate a dashboard whose name has 40 characters
- **THEN** the name field reads the first 33 characters of the name followed by " (copy)"

#### Scenario: Duplicate at the limit
- **WHEN** a project has 7 dashboards and an `analyst` asks the API to duplicate one
- **THEN** the API responds 409 with a message saying a project can have at most 7 dashboards

#### Scenario: Recent move included
- **WHEN** an `analyst` drags a widget to a new place and duplicates its dashboard right away
- **THEN** the copy holds that widget in its new place

#### Scenario: Original above the widget limit
- **WHEN** an operator has lowered the widget limit to 20 and an `analyst` duplicates a dashboard that holds 30 widgets
- **THEN** the API responds 400 with an error saying a dashboard can have at most 20 widgets, the analyst sees that message, and no dashboard is added

#### Scenario: Filters duplicated
- **WHEN** an `analyst` duplicates "Housing", whose filters are "Municipality" and "Round" with the default 3, and one of whose widgets ignores "Municipality"
- **THEN** "Housing (copy)" has the filters "Municipality" and "Round" with the default 3, and the copy of that widget ignores "Municipality"

#### Scenario: Filters of a copy are independent
- **WHEN** the `analyst` then removes the filter "Round" from "Housing (copy)"
- **THEN** "Housing" still has the filter "Round"

#### Scenario: Scheduled email not copied
- **WHEN** an `analyst` duplicates "Housing", which has an active scheduled email
- **THEN** "Housing (copy)" has no scheduled email, and only "Housing" is sent

#### Scenario: Colors duplicated
- **WHEN** an `analyst` duplicates "Housing", which has the palette *Earth*, keeps color 5 for `Female` of `sex`, and holds a column chart in color 3
- **THEN** "Housing (copy)" has the palette *Earth*, keeps color 5 for `Female`, and its copy of the column chart is in color 3

#### Scenario: Colors of a copy are independent
- **WHEN** the `analyst` then changes the palette of "Housing (copy)" to *Vivid*
- **THEN** "Housing" keeps the palette *Earth*
