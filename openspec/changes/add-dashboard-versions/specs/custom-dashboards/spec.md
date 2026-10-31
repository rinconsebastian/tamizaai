# Spec Delta

## MODIFIED Requirements

### Requirement: Dashboard header
Each dashboard SHALL show its name as a heading above its widgets. For every member, viewers included, the heading SHALL be followed by the action to print the dashboard whenever it is offered (see `dashboard-printing`). For an `analyst` or `admin`, the heading SHALL also be followed by the actions to add a dashboard and to rename, duplicate, move or remove this one, and to open its version history (see `dashboard-versions`), except while one of its versions is shown. At every width from 360 pixels the whole name and these actions SHALL be visible without scrolling the page horizontally.

#### Scenario: Dashboard header at phone width
- **WHEN** an `analyst` opens a dashboard with widgets whose name has 40 characters and no space, in a viewport 360 pixels wide
- **THEN** the whole name, the Print action, the action to add a dashboard and the dashboard's actions are visible, and the page does not scroll horizontally

#### Scenario: Viewer's dashboard header
- **WHEN** a `viewer` opens a dashboard that has widgets
- **THEN** the heading is followed by the Print action and by no other action

#### Scenario: Version history offered
- **WHEN** an `analyst` opens the actions of a dashboard
- **THEN** **Version history** is among them

### Requirement: Roles for dashboards
Only an `analyst` or `admin` SHALL be able to add, rename, move, duplicate or remove a dashboard, change its filters, manage its scheduled email, or read and restore its versions. A `viewer` SHALL be refused with 403 and SHALL NOT be offered these actions. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer refused
- **WHEN** a `viewer` asks the API to add, rename, move, duplicate or remove a dashboard, to change its filters, to read, save, remove or send its scheduled email, or to list, read or restore its versions
- **THEN** the API responds 403 and the project's dashboards are unchanged

#### Scenario: No actions for a viewer
- **WHEN** a `viewer` opens a dashboard
- **THEN** no action to add, rename, move, duplicate or remove a dashboard, to edit its filters, to schedule its email, or to open its version history, is shown

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests one of its dashboards or its versions, or asks to change them
- **THEN** the API responds 404
