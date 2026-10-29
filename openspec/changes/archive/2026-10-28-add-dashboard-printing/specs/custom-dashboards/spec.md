# Spec Delta

## MODIFIED Requirements

### Requirement: Dashboard header
Each dashboard SHALL show its name as a heading above its widgets. For every member, viewers included, the heading SHALL be followed by the action to print the dashboard whenever it is offered (see `dashboard-printing`). For an `analyst` or `admin`, the heading SHALL also be followed by the actions to add a dashboard and to rename, duplicate, move or remove this one. At every width from 360 pixels the whole name and these actions SHALL be visible without scrolling the page horizontally.

#### Scenario: Dashboard header at phone width
- **WHEN** an `analyst` opens a dashboard with widgets whose name has 40 characters and no space, in a viewport 360 pixels wide
- **THEN** the whole name, the Print action, the action to add a dashboard and the dashboard's actions are visible, and the page does not scroll horizontally

#### Scenario: Viewer's dashboard header
- **WHEN** a `viewer` opens a dashboard that has widgets
- **THEN** the heading is followed by the Print action and by no other action
