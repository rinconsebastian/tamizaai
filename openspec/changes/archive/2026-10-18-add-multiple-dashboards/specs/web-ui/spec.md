# Spec Delta

## MODIFIED Requirements

### Requirement: Project navigation
The project page SHALL offer these top-level sections to every member, in this order: the Performance and Clean data dashboards, the project's own dashboards in their order, and Settings, set apart at the end of the row. Settings SHALL group the configuration sections General, Submissions, Scripts, Reference data, Sampling frame, Members and, for admins only, Webhook, and SHALL open on General. Each section and dashboard SHALL keep its own address, so links and bookmarks keep working.

#### Scenario: Viewer at desktop width
- **WHEN** a `viewer` opens a project whose dashboards are "Indicators" and "Field", in a viewport 1024 pixels wide
- **THEN** the tab row shows Performance, Clean data, Indicators, Field and Settings, and no other section is listed anywhere

#### Scenario: Admin opens a configuration section
- **WHEN** an `admin` opens the project's Members section
- **THEN** Settings is marked as current, a secondary navigation under the tab row lists General, Submissions, Scripts, Reference data, Sampling frame, Members and Webhook, and Members is marked as the current page

#### Scenario: Analyst opens a configuration section
- **WHEN** an `analyst` opens the project's Scripts section
- **THEN** the secondary navigation lists the configuration sections without Webhook

#### Scenario: Dashboard open
- **WHEN** a member opens Performance, Clean data or any of the project's own dashboards
- **THEN** no secondary navigation is shown

#### Scenario: Dashboard marked as current
- **WHEN** a member opens the dashboard "Field" of a project whose dashboards are "Indicators" and "Field"
- **THEN** the "Field" tab is marked as the current page, and no other tab is

#### Scenario: Bookmark to a configuration section
- **WHEN** a member opens the address of the Scripts section directly
- **THEN** the Scripts section is shown with Settings marked as current

#### Scenario: Project opens on the performance dashboard
- **WHEN** a member opens a project's address without a section
- **THEN** the performance dashboard is shown and Performance is marked as the current page

### Requirement: Project navigation on narrow screens
Below 768 pixels wide the project page SHALL offer every section in one native select, listing Performance, Clean data and the project's own dashboards first, in the order of the tab row, and the configuration sections grouped under Settings, with the open section selected. No second navigation control SHALL be shown at that width.

#### Scenario: Phone width
- **WHEN** an `analyst` opens the Submissions section of a project whose only dashboard is "Indicators", in a viewport 360 pixels wide
- **THEN** one select shows Performance, Clean data, Indicators and a Settings group with General, Submissions, Scripts, Reference data, Sampling frame and Members, Submissions is selected, and the page does not scroll horizontally

#### Scenario: Switching from the select
- **WHEN** a member chooses the dashboard "Field" in the select
- **THEN** the "Field" dashboard is shown

## ADDED Requirements

### Requirement: Project tab row that does not fit
From 768 pixels wide, tabs that do not fit in one row SHALL scroll sideways inside the row, never scrolling the page. Settings SHALL stay visible at its end, the open tab SHALL be brought into view, and each side that hides tabs SHALL show it with a control that scrolls the row. A dashboard name too long for its tab SHALL be cut with an ellipsis, and its full name SHALL stay available on hover, on keyboard focus and to assistive technology.

#### Scenario: Seven dashboards at tablet width
- **WHEN** a member opens a project with 7 dashboards, each named with at least 20 characters, in a viewport 1024 pixels wide
- **THEN** the tab row scrolls inside itself, Settings is visible at its end, the hidden side shows that more tabs are there, and the page does not scroll horizontally

#### Scenario: Open tab brought into view
- **WHEN** a member opens the address of the seventh of 7 dashboards, each named with at least 20 characters, in a viewport 768 pixels wide
- **THEN** that dashboard's tab is visible in the row

#### Scenario: Page stays in place
- **WHEN** a member who has scrolled down to the pager of the Clean data dashboard, in a viewport 1024 pixels wide, moves to the next page
- **THEN** the page keeps its vertical scroll position

#### Scenario: Long name
- **WHEN** a project has a dashboard named "Cobertura de vacunación por departamento"
- **THEN** its tab shows the start of the name followed by an ellipsis, hovering or focusing it with the keyboard shows the whole name, and a screen reader reads the whole name
