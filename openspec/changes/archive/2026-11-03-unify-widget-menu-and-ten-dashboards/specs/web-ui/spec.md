## MODIFIED Requirements

### Requirement: Project tab row that does not fit
From 768 pixels wide, tabs that do not fit in one row SHALL scroll sideways inside the row, never scrolling the page. Settings, when shown, SHALL stay visible at its end, the open tab SHALL be brought into view, and each side that hides tabs SHALL show it with a control that scrolls the row. A dashboard name too long for its tab SHALL be cut with an ellipsis, and its full name SHALL stay available on hover, on keyboard focus and to assistive technology.

#### Scenario: Seven dashboards at tablet width
- **WHEN** an `analyst` opens a project with 7 dashboards, each named with at least 20 characters, in a viewport 1024 pixels wide
- **THEN** the tab row scrolls inside itself, Settings is visible at its end, the hidden side shows that more tabs are there, and the page does not scroll horizontally

#### Scenario: Ten dashboards at tablet width
- **WHEN** an `analyst` opens a project with 10 dashboards, each named with at least 20 characters, in a viewport 1024 pixels wide
- **THEN** the tab row scrolls inside itself, Settings is visible at its end, the hidden side shows that more tabs are there, and the page does not scroll horizontally

#### Scenario: Open tab brought into view
- **WHEN** a member opens the address of the tenth of 10 dashboards, each named with at least 20 characters, in a viewport 768 pixels wide
- **THEN** that dashboard's tab is visible in the row

#### Scenario: Page stays in place
- **WHEN** a member who has scrolled down to the pager of the Clean data dashboard, in a viewport 1024 pixels wide, moves to the next page
- **THEN** the page keeps its vertical scroll position

#### Scenario: Long name
- **WHEN** a project has a dashboard named "Cobertura de vacunación por departamento"
- **THEN** its tab shows the start of the name followed by an ellipsis, hovering or focusing it with the keyboard shows the whole name, and a screen reader reads the whole name
