# web-ui Specification

## Purpose

Shared behavior of the web interface across all screens: it works from phone width to desktop, offers consistent navigation, and tells users what happened when an action fails.

## Requirements

### Requirement: Responsive layout
Every screen SHALL be usable at viewport widths from 360 CSS pixels to desktop sizes without horizontal scrolling of the page. Content SHALL reflow to the available width. Tables wider than the screen SHALL scroll horizontally inside their own container, without the page scrolling.

#### Scenario: Phone width
- **WHEN** any screen is shown in a viewport 360 pixels wide
- **THEN** all its content and controls are reachable without horizontal page scrolling

#### Scenario: Wide table on a phone
- **WHEN** a table with more columns than fit on screen is shown in a narrow viewport
- **THEN** the table scrolls horizontally inside its container while the rest of the page stays in place

#### Scenario: Desktop width
- **WHEN** a screen is shown in a viewport 1280 pixels wide or more
- **THEN** the content uses the extra width, for example placing forms and lists side by side, instead of a single phone-width column

### Requirement: Navigation shell
Every signed-in screen SHALL show a top bar with the product name, the navigation (Projects), the user's name and the sign-out action. On narrow screens the navigation and user actions SHALL collapse into a menu button. The menu SHALL be operable with the keyboard.

#### Scenario: Narrow screen
- **WHEN** a signed-in screen is shown in a viewport narrower than 768 pixels
- **THEN** the top bar shows the product name and a menu button, and opening the menu reveals the navigation, the user's name and sign-out

#### Scenario: Keyboard use
- **WHEN** a keyboard user focuses the menu button and presses Enter, then Escape
- **THEN** the menu opens on Enter and closes on Escape, returning focus to the button

### Requirement: Feedback on failed actions
When an action fails, the UI SHALL show a message that explains why, using the error the API returned. Validation errors for a form field SHALL appear next to that field. The UI SHALL NOT fail silently. A message the API wrote SHALL be shown as received. A message the UI writes itself, such as the one for an API error code it knows or for a server it cannot reach, SHALL be in the interface language (see `interface-language`).

#### Scenario: Field validation error
- **WHEN** the API rejects a form because one field is invalid
- **THEN** the message for that field appears next to it and the rest of the input is kept

#### Scenario: Request fails
- **WHEN** the API responds with an error that is not tied to a field, or cannot be reached
- **THEN** the UI shows a message describing the problem

#### Scenario: Server unreachable in Spanish
- **WHEN** a member with the Spanish UI saves a form while the API cannot be reached
- **THEN** the UI says in Spanish that it could not reach the server

#### Scenario: API message in the Spanish UI
- **WHEN** a member with the Spanish UI acts on a widget that was just deleted and the API answers "Widget not found." without a code
- **THEN** the UI shows "Widget not found."

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

### Requirement: Date and time display
Every date and time the UI shows SHALL be written to the minute, without seconds, in one format shared by every screen. The format SHALL name the month, and SHALL follow the order, clock and words of the interface's regional format (see `interface-language`).

#### Scenario: Synchronization time
- **WHEN** a synchronization finished at 22:00:01 on 2 October 2026
- **THEN** the log shows it as the date and 22:00 (or 10:00 PM), without seconds

#### Scenario: American English
- **WHEN** the browser entry that chose English is `en-US` and the UI shows 21:30 on 1 September 2026
- **THEN** it reads `Sep 1, 2026, 9:30 PM`

#### Scenario: British English
- **WHEN** the browser entry that chose English is `en-GB` and the UI shows the same moment
- **THEN** it reads `1 Sept 2026, 21:30`

#### Scenario: Colombian Spanish
- **WHEN** the browser entry that chose Spanish is `es-CO` and the UI shows the same moment
- **THEN** it reads `1 de sept de 2026, 9:30 p. m.`

#### Scenario: Spanish from Spain
- **WHEN** the browser entry that chose Spanish is `es-ES` and the UI shows the same moment
- **THEN** it reads `1 sept 2026, 21:30`

### Requirement: Feedback while an action runs
While an action the user started takes longer than a moment (preparing an export, a download, a test), the control that started it SHALL show that it is busy and SHALL NOT be usable again until the action ends; other controls of the same kind SHALL stay usable. When the action ends, the UI SHALL show exactly one message, a success or a failure. A notice the UI inserts into the page after an action, such as a dropped filter or republished tables, SHALL be announced to screen readers.

#### Scenario: Export of one indicator
- **WHEN** a member exports the table of the indicator `coverage` from a list of three indicators
- **THEN** only the export control of `coverage` shows the busy state, the other two stay usable, and one message appears when the file is ready

#### Scenario: Filter dropped after a run
- **WHEN** a run removes the column a member filtered by and the next page loads
- **THEN** the notice that the filter was removed is announced to screen readers without stealing focus

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
