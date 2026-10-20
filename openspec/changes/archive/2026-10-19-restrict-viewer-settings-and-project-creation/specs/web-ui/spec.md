# Spec Delta

## MODIFIED Requirements

### Requirement: Project navigation
The project page SHALL offer these top-level sections to every member, in this order: the Performance and Clean data dashboards, the project's own dashboards in their order, and, to an `analyst` or an `admin` only, Settings, set apart at the end of the row. A superadmin acts as `admin`. Settings SHALL group the configuration sections General, Submissions, Scripts, Reference data, Sampling frame, Members and, for admins only, Webhook, and SHALL open on General. Each section and dashboard SHALL keep its own address, so links and bookmarks keep working for the members it is offered to.

#### Scenario: Viewer at desktop width
- **WHEN** a `viewer` opens a project whose dashboards are "Indicators" and "Field", in a viewport 1024 pixels wide
- **THEN** the tab row shows Performance, Clean data, Indicators and Field, Settings is not shown, and no other section is listed anywhere

#### Scenario: Analyst at desktop width
- **WHEN** an `analyst` opens the same project in a viewport 1024 pixels wide
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
- **WHEN** an `analyst` opens the address of the Scripts section directly
- **THEN** the Scripts section is shown with Settings marked as current

#### Scenario: Project opens on the performance dashboard
- **WHEN** a member opens a project's address without a section
- **THEN** the performance dashboard is shown and Performance is marked as the current page

### Requirement: Project navigation on narrow screens
Below 768 pixels wide the project page SHALL offer every section it offers the member in one native select, listing Performance, Clean data and the project's own dashboards first, in the order of the tab row, and, when Settings is offered, the configuration sections grouped under Settings, with the open section selected. No second navigation control SHALL be shown at that width.

#### Scenario: Phone width
- **WHEN** an `analyst` opens the Submissions section of a project whose only dashboard is "Indicators", in a viewport 360 pixels wide
- **THEN** one select shows Performance, Clean data, Indicators and a Settings group with General, Submissions, Scripts, Reference data, Sampling frame and Members, Submissions is selected, and the page does not scroll horizontally

#### Scenario: Viewer at phone width
- **WHEN** a `viewer` opens the performance dashboard of a project whose only dashboard is "Indicators", in a viewport 360 pixels wide
- **THEN** one select shows Performance, Clean data and Indicators with no Settings group, Performance is selected, and the page does not scroll horizontally

#### Scenario: Switching from the select
- **WHEN** a member chooses the dashboard "Field" in the select
- **THEN** the "Field" dashboard is shown

### Requirement: Keyboard and screen-reader navigation of the project page
Every section link of the project page SHALL be reachable with Tab and activated with Enter. Exactly one link SHALL carry the current-page state at any time; while a configuration section is open, Settings SHALL be announced as current and the open section as the current page. Moving between configuration sections SHALL move focus to the newly current secondary link only when the navigation started inside the page, never on first load.

#### Scenario: Keyboard user reaches a configuration section
- **WHEN** a keyboard user who is an `analyst` of the project, on the performance dashboard, presses Tab through the tab row and activates Settings with Enter
- **THEN** the General section opens, Settings is announced as current and General as the current page

#### Scenario: First load does not move focus
- **WHEN** an `analyst` opens the address of the Members section directly
- **THEN** focus stays where the browser puts it on page load

### Requirement: Set-once settings shown as values
A project setting that is set once and rarely changed (synchronization and run schedules, the enumerator question, an indicator's metadata, the sampling frame, the Kobo API token) SHALL be shown as its current value to every member who may read it. The controls that change it SHALL appear only on request by a member who may change it, and SHALL hide again after a successful save. Validation errors SHALL still appear next to the field they concern.

#### Scenario: Admin reads the schedules
- **WHEN** an `admin` opens the Submissions section of a project that synchronizes every 12 hours and checks deletions every 24 hours
- **THEN** both intervals are shown as a line of text with an action to edit them, and no input is shown until that action is used

#### Scenario: Viewer reads the schedules
- **WHEN** a `viewer` opens the address of the same section
- **THEN** the performance dashboard is shown in its place, with neither the line of text nor an action to edit

#### Scenario: Analyst reads the schedules
- **WHEN** an `analyst` opens the same section
- **THEN** the same line of text is shown without an action to edit

#### Scenario: Edit and save
- **WHEN** an `admin` uses the edit action, enters 6 hours and saves
- **THEN** the inputs hide again and the line reads the new interval

#### Scenario: Rejected value
- **WHEN** an `admin` enters 200 hours and saves
- **THEN** the inputs stay open and the message naming the allowed range appears next to the interval field

### Requirement: Project tab row that does not fit
From 768 pixels wide, tabs that do not fit in one row SHALL scroll sideways inside the row, never scrolling the page. Settings, when shown, SHALL stay visible at its end, the open tab SHALL be brought into view, and each side that hides tabs SHALL show it with a control that scrolls the row. A dashboard name too long for its tab SHALL be cut with an ellipsis, and its full name SHALL stay available on hover, on keyboard focus and to assistive technology.

#### Scenario: Seven dashboards at tablet width
- **WHEN** an `analyst` opens a project with 7 dashboards, each named with at least 20 characters, in a viewport 1024 pixels wide
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

## ADDED Requirements

### Requirement: Configuration sections closed to viewers
A `viewer` who opens the address of a configuration section (General, Submissions, Scripts, Reference data, Sampling frame, Members or Webhook) SHALL be shown the performance dashboard instead, with no message. That address SHALL be replaced in the browser history, and none of the section's data SHALL be requested. The same SHALL happen when an `admin` changes their own role to `viewer` in Members. No page a `viewer` opens SHALL make a request refused for their role.

#### Scenario: Viewer opens a configuration bookmark
- **WHEN** a `viewer` opens the address of the project's Scripts section from a bookmark
- **THEN** the performance dashboard is shown with Performance marked as the current page, no message is shown, and none of the Scripts section's data is requested

#### Scenario: Going back after the redirect
- **WHEN** a `viewer` who was taken from the Members address to the performance dashboard goes back in the browser
- **THEN** the browser shows the page that came before the Members address, not the Members address

#### Scenario: Viewer opens the Webhook address
- **WHEN** a `viewer` opens the address of the project's Webhook section
- **THEN** the performance dashboard is shown in its place

#### Scenario: Own role lowered to viewer
- **WHEN** an `admin` of a project with two admins changes their own role to `viewer` in the Members section
- **THEN** the performance dashboard is shown in place of Members, the Members address is replaced in the browser history, and no error message is shown

#### Scenario: No failed request for a viewer
- **WHEN** a `viewer` opens Performance, Clean data and each of the project's own dashboards
- **THEN** no request the page makes is refused with 403, and no error message is shown
