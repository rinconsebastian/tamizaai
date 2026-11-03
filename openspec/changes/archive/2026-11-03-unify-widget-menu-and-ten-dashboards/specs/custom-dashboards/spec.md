## MODIFIED Requirements

### Requirement: Project dashboards
A project SHALL have at least 1 and at most 10 dashboards of its own, in an order. Each dashboard SHALL have a name and its own widgets (see `dashboard-indicators`). Every project member SHALL see every dashboard of the project. The Performance and Clean data dashboards SHALL NOT count among them: they cannot be renamed, moved, duplicated or removed, and they always come before the project's own dashboards.

#### Scenario: Viewer sees every dashboard
- **WHEN** a `viewer` opens a project whose dashboards are "Housing", "Health" and "Field", in that order
- **THEN** the three are offered after Performance and Clean data, in that order

#### Scenario: Permanent dashboards
- **WHEN** an `analyst` opens the Performance or the Clean data dashboard
- **THEN** no action to rename, move, duplicate or remove it is offered

#### Scenario: Ten dashboards
- **WHEN** an `analyst` of a project that has 9 dashboards adds one
- **THEN** the dashboard is added, and the project's 10 dashboards are offered after Performance and Clean data

### Requirement: Adding a dashboard
An `analyst` or `admin` SHALL be able to add a dashboard by giving its name. It SHALL be placed after the project's other dashboards, start with no widgets, and open for the member who added it. A project that already has 10 dashboards SHALL refuse another with a message naming the limit, and the action to add one SHALL then be unavailable and say why.

#### Scenario: Analyst adds a dashboard
- **WHEN** an `analyst` of a project whose only dashboard is "Indicators" adds "Field progress"
- **THEN** "Field progress" opens for the analyst with no widgets, and every member who opens the project afterwards sees it after "Indicators"

#### Scenario: Eighth dashboard
- **WHEN** a project has 7 dashboards and an `admin` asks the API to add another
- **THEN** the API adds the dashboard after the other seven

#### Scenario: Add action below the limit
- **WHEN** an `analyst` opens a dashboard of a project that has 8 dashboards
- **THEN** the actions to add and to duplicate a dashboard are available

#### Scenario: Eleventh dashboard
- **WHEN** a project has 10 dashboards and an `admin` asks the API to add another
- **THEN** the API responds 409 with a message saying a project can have at most 10 dashboards

#### Scenario: Add action at the limit
- **WHEN** an `analyst` opens a dashboard of a project that has 10 dashboards
- **THEN** the actions to add and to duplicate a dashboard are unavailable, can still be reached from the keyboard, and say the project has the most dashboards it can hold (10)

#### Scenario: Two added at the same moment
- **WHEN** two analysts each add a dashboard at the same moment to a project that has 9
- **THEN** one dashboard is added and the other request is refused with the limit

### Requirement: Duplicating a dashboard
An `analyst` or `admin` SHALL be able to duplicate a dashboard under a new name, proposed as the original's name followed by " (copy)" and shortened to fit 40 characters. The copy SHALL be placed right after the original and SHALL hold a copy of each of its widgets, with the same title, chart type, query, ignored filters, color, orientation, position and size, a copy of its filters, in the same order with the same defaults, and its palette and colors kept by value. Later changes to one SHALL NOT change the other. The copy SHALL have no scheduled email (see `dashboard-reports`). Duplicating SHALL be refused when the project already has 10 dashboards.

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
- **WHEN** a project has 10 dashboards and an `analyst` asks the API to duplicate one
- **THEN** the API responds 409 with a message saying a project can have at most 10 dashboards

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

#### Scenario: Orientation duplicated
- **WHEN** an `analyst` duplicates "Housing", which holds a horizontal box plot
- **THEN** the copy of the box plot in "Housing (copy)" is horizontal

#### Scenario: Duplicate of a seventh dashboard
- **WHEN** a project has 7 dashboards and an `analyst` duplicates one
- **THEN** the copy is added, and the project has 8 dashboards
