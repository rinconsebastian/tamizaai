# Spec Delta

## MODIFIED Requirements

### Requirement: Widgets
A member with the `analyst` or `admin` role SHALL be able to add, edit and remove widgets on any dashboard. Each widget SHALL belong to the one dashboard it is added on or moved to, and have a title, a chart type (stat, bar, column, line, histogram, pie or map), and a query over exactly one indicator's published table. A dashboard SHALL hold at most the configured number of widgets. A `viewer` SHALL see every dashboard's widgets but SHALL NOT be able to add, edit, remove or arrange them.

#### Scenario: Analyst adds a widget
- **WHEN** an `analyst` adds a column chart titled "Coverage by municipality" over the indicator `coverage` on the dashboard "Health"
- **THEN** the widget is saved on "Health" and shown to every member who opens "Health", and on no other dashboard

#### Scenario: Viewer cannot edit
- **WHEN** a `viewer` tries to add, edit, remove or move a widget
- **THEN** the API responds 403

#### Scenario: Widget limit
- **WHEN** a dashboard already has the maximum number of widgets
- **THEN** adding another to it fails with a message naming the limit

#### Scenario: Limit counted per dashboard
- **WHEN** the dashboard "Health" has the maximum number of widgets and an `analyst` adds a widget to the dashboard "Field", which holds 3
- **THEN** the widget is added to "Field"

### Requirement: Widget layout
A widget SHALL have a position and size on its dashboard's grid that an `analyst` or `admin` can move and resize. Each dashboard SHALL have a grid of its own: a widget added without a position SHALL be placed below the widgets of its own dashboard. Reading order, for keyboard navigation and for a page showing several widgets' tables at once, SHALL follow the grid top-to-bottom, left-to-right.

#### Scenario: Widget moved
- **WHEN** an `analyst` drags a widget to a new position
- **THEN** the new position is saved and shown to every member who opens the dashboard afterward

#### Scenario: First widget of a dashboard
- **WHEN** the dashboard "Health" holds widgets down to row 30 and an `analyst` adds the first widget to the dashboard "Field"
- **THEN** the widget is placed at the top of "Field"

### Requirement: Indicators dashboard and its indicators
Every project member SHALL be able to open any dashboard of the project, which SHALL list the project's indicators with their label, unit and format and whether each has published a table. The dashboard's widgets SHALL come first on the page; the list of indicators MAY follow them inside a disclosure that is collapsed once any indicator has published and opens on demand. While no indicator has published, every dashboard SHALL say so, with instructions, SHALL show the list of indicators expanded as part of that message, and SHALL still list the widgets already configured on it. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a published dashboard
- **WHEN** a `viewer` opens a dashboard of a project where at least one indicator has published
- **THEN** the dashboard's widgets are shown

#### Scenario: Indicator list behind a disclosure
- **WHEN** a member opens a dashboard of a project with 3 indicators, 2 of them published
- **THEN** the widgets come first, a collapsed disclosure reads "Indicators (3) · 2 published", and opening it lists the three indicators with label, unit, format and whether each has published

#### Scenario: No indicator has published
- **WHEN** none of a project's indicators has published, or the project has none
- **THEN** each dashboard says no indicator has published yet, with instructions and the list of indicators expanded, and still lists the widgets configured on it

#### Scenario: Same indicators on every dashboard
- **WHEN** a member opens the dashboard "Health" and then the dashboard "Field" of the same project
- **THEN** both list the same indicators, each with its export

#### Scenario: Link to the scripts from any dashboard
- **WHEN** no indicator has published and an `analyst` follows the link to the scripts from any dashboard
- **THEN** the project's Scripts section opens

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its indicators or one of its dashboards
- **THEN** the API responds 404

## ADDED Requirements

### Requirement: Moving a widget to another dashboard
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep its title, chart type and query, and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

#### Scenario: Widget moved to another dashboard
- **WHEN** an `analyst` edits the widget "Coverage by municipality" on the dashboard "Health" and chooses the dashboard "Field"
- **THEN** the widget is shown on "Field", below its widgets, and no longer on "Health"

#### Scenario: Stale widget moved
- **WHEN** a script change removed the column a widget groups by, and an `analyst` moves the widget to "Field" without changing anything else
- **THEN** the widget moves and still shows a message naming the missing column

#### Scenario: Full dashboard
- **WHEN** the dashboard "Field" holds the maximum number of widgets and an `analyst` moves a widget to it
- **THEN** the API responds 400 with an error on the dashboard naming the limit, and the widget stays where it was

#### Scenario: Dashboard of another project
- **WHEN** an `analyst` asks the API to move a widget to a dashboard of another project
- **THEN** the API responds 400 with an error on the dashboard, and the widget stays where it was

#### Scenario: Target removed meanwhile
- **WHEN** an `analyst` moves a widget to "Field" just after another analyst removed "Field"
- **THEN** the form shows an error on the dashboard, the widget stays where it was, and "Field" is no longer offered in the form or the tab row

#### Scenario: New widget
- **WHEN** an `analyst` adds a widget
- **THEN** the widget form offers no dashboard choice, and the widget is added to the open dashboard
