# custom-dashboards Specification

## Purpose

Let a project organize its indicator widgets into up to 7 dashboards of its own, each with a name, shown as tabs after the Performance and Clean data dashboards.

## Requirements

### Requirement: Project dashboards
A project SHALL have at least 1 and at most 7 dashboards of its own, in an order. Each dashboard SHALL have a name and its own widgets (see `dashboard-indicators`). Every project member SHALL see every dashboard of the project. The Performance and Clean data dashboards SHALL NOT count among them: they cannot be renamed, moved, duplicated or removed, and they always come before the project's own dashboards.

#### Scenario: Viewer sees every dashboard
- **WHEN** a `viewer` opens a project whose dashboards are "Housing", "Health" and "Field", in that order
- **THEN** the three are offered after Performance and Clean data, in that order

#### Scenario: Permanent dashboards
- **WHEN** an `analyst` opens the Performance or the Clean data dashboard
- **THEN** no action to rename, move, duplicate or remove it is offered

### Requirement: First dashboard
A new project SHALL start with one dashboard named "Indicators" and no widgets. A project that existed before dashboards were introduced SHALL get one dashboard named "Indicators" that holds all of its widgets, each with the same title, chart type, query, position and size.

#### Scenario: New project
- **WHEN** a user creates a project
- **THEN** the project has one dashboard, "Indicators", with no widgets

#### Scenario: Existing project upgraded
- **WHEN** a deployment is upgraded while one of its projects holds 12 widgets
- **THEN** that project has one dashboard, "Indicators", holding the 12 widgets in the same positions and sizes

### Requirement: Dashboard names
A dashboard's name SHALL be 1 to 40 characters long once leading and trailing spaces are removed, and SHALL contain no control characters. Two dashboards of a project SHALL NOT have names that differ only in case, and no dashboard SHALL be named "Performance", "Clean data" or "Settings", in any case. A rejected name SHALL be answered with an error on the name. A name SHALL be shown exactly as saved, never translated.

#### Scenario: Name trimmed
- **WHEN** an `analyst` names a dashboard "  Salud  "
- **THEN** it is saved and shown as "Salud"

#### Scenario: Name too long
- **WHEN** an `analyst` names a dashboard with 41 characters
- **THEN** the API responds 400 with an error on the name saying it can have at most 40 characters

#### Scenario: Name taken
- **WHEN** a project has a dashboard "Salud" and an `analyst` adds a dashboard named "SALUD", or renames another dashboard to it
- **THEN** the API responds 400 with an error on the name saying another dashboard of the project has it

#### Scenario: Same name in another project
- **WHEN** another project has a dashboard "Salud" and an `analyst` adds a dashboard "Salud" to this project
- **THEN** the dashboard is added

#### Scenario: Name of a permanent tab
- **WHEN** an `analyst` names a dashboard "clean data"
- **THEN** the API responds 400 with an error on the name saying it is reserved

#### Scenario: Name shown as typed
- **WHEN** an `analyst` names a dashboard "<b>Avance</b>"
- **THEN** its tab, the select and every message that names it show those characters as text, and nothing is rendered as markup

### Requirement: Dashboard header
Each dashboard SHALL show its name as a heading above its widgets. For an `analyst` or `admin`, the heading SHALL be followed by the actions to add a dashboard and to rename, duplicate, move or remove this one. At every width from 360 pixels the whole name and these actions SHALL be visible without scrolling the page horizontally.

#### Scenario: Dashboard header at phone width
- **WHEN** an `analyst` opens a dashboard whose name has 40 characters and no space, in a viewport 360 pixels wide
- **THEN** the whole name, the action to add a dashboard and the dashboard's actions are visible, and the page does not scroll horizontally

### Requirement: Adding a dashboard
An `analyst` or `admin` SHALL be able to add a dashboard by giving its name. It SHALL be placed after the project's other dashboards, start with no widgets, and open for the member who added it. A project that already has 7 dashboards SHALL refuse another with a message naming the limit, and the action to add one SHALL then be unavailable and say why.

#### Scenario: Analyst adds a dashboard
- **WHEN** an `analyst` of a project whose only dashboard is "Indicators" adds "Field progress"
- **THEN** "Field progress" opens for the analyst with no widgets, and every member who opens the project afterwards sees it after "Indicators"

#### Scenario: Eighth dashboard
- **WHEN** a project has 7 dashboards and an `admin` asks the API to add another
- **THEN** the API responds 409 with a message saying a project can have at most 7 dashboards

#### Scenario: Add action at the limit
- **WHEN** an `analyst` opens a dashboard of a project that has 7 dashboards
- **THEN** the actions to add and to duplicate a dashboard are unavailable, can still be reached from the keyboard, and say the project has the most dashboards it can hold

#### Scenario: Two added at the same moment
- **WHEN** two analysts each add a dashboard at the same moment to a project that has 6
- **THEN** one dashboard is added and the other request is refused with the limit

### Requirement: Renaming a dashboard
An `analyst` or `admin` SHALL be able to rename a dashboard. Its widgets, its place among the project's dashboards and its address SHALL stay the same, and the member SHALL be told the dashboard was renamed.

#### Scenario: Analyst renames a dashboard
- **WHEN** an `analyst` renames "Indicators" to "Coverage"
- **THEN** the analyst is told it was renamed, and its tab reads "Coverage" for every member who opens the project afterwards, with the same widgets and in the same place

### Requirement: Dashboard order
An `analyst` or `admin` SHALL be able to move a dashboard one place to the left or to the right among the project's dashboards. The first dashboard SHALL NOT move left and the last SHALL NOT move right. The new order SHALL be saved for every member, and the dashboard's new position SHALL be announced to screen readers. While a move is being saved, the move actions SHALL NOT be usable.

#### Scenario: Move right
- **WHEN** an `analyst` moves "Housing" to the right in a project whose dashboards are "Housing", "Health" and "Field"
- **THEN** every member who opens the project afterwards sees "Health", "Housing" and "Field", in that order

#### Scenario: First dashboard
- **WHEN** an `analyst` opens the actions of the project's first dashboard
- **THEN** moving it left is unavailable, and moving it right is available when the project has another dashboard

#### Scenario: Position announced
- **WHEN** a keyboard user moves the second of 4 dashboards to the right
- **THEN** a screen reader announces that it is now third of 4

#### Scenario: Two moves in a row
- **WHEN** an `analyst` moves the first of 4 dashboards to the right twice in a row
- **THEN** it ends up third of 4

### Requirement: Duplicating a dashboard
An `analyst` or `admin` SHALL be able to duplicate a dashboard under a new name, proposed as the original's name followed by " (copy)" and shortened to fit 40 characters. The copy SHALL be placed right after the original and SHALL hold a copy of each of its widgets, with the same title, chart type, query, ignored filters, position and size, and a copy of its filters, in the same order with the same defaults. Later changes to one SHALL NOT change the other. The copy SHALL have no scheduled email (see `dashboard-reports`). Duplicating SHALL be refused when the project already has 7 dashboards.

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

### Requirement: Removing a dashboard
An `analyst` or `admin` SHALL be able to remove a dashboard together with all its widgets, after confirming a message that says how many widgets will be removed. The last dashboard of a project SHALL NOT be removable. After a removal the member SHALL be told what was removed and taken to the dashboard now in its place, or to the new last dashboard when the removed one was last.

#### Scenario: Dashboard with widgets removed
- **WHEN** an `analyst` removes "Health", which holds 5 widgets
- **THEN** the confirmation says 5 widgets will be removed, and once confirmed "Health" and its 5 widgets are gone for every member

#### Scenario: Next dashboard opens
- **WHEN** an `analyst` removes "Health" from a project whose dashboards are "Housing", "Health" and "Field"
- **THEN** "Field" opens with no notice that a dashboard does not exist

#### Scenario: Last dashboard removed
- **WHEN** an `analyst` removes "Field" from a project whose dashboards are "Housing", "Health" and "Field"
- **THEN** "Health" opens

#### Scenario: Only dashboard
- **WHEN** a project has one dashboard
- **THEN** the action to remove it is unavailable, and a request to the API to remove it is answered 409 with a message saying a project needs at least one dashboard

#### Scenario: Last two removed at the same moment
- **WHEN** two analysts each remove a different one of a project's 2 dashboards at the same moment
- **THEN** one dashboard is removed and the other request is refused because a project needs at least one dashboard

### Requirement: Roles for dashboards
Only an `analyst` or `admin` SHALL be able to add, rename, move, duplicate or remove a dashboard, change its filters, or manage its scheduled email. A `viewer` SHALL be refused with 403 and SHALL NOT be offered these actions. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer refused
- **WHEN** a `viewer` asks the API to add, rename, move, duplicate or remove a dashboard, to change its filters, or to read, save, remove or send its scheduled email
- **THEN** the API responds 403 and the project's dashboards are unchanged

#### Scenario: No actions for a viewer
- **WHEN** a `viewer` opens a dashboard
- **THEN** no action to add, rename, move, duplicate or remove a dashboard, to edit its filters, or to schedule its email, is shown

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests one of its dashboards or asks to change them
- **THEN** the API responds 404

### Requirement: Dashboard address
Each dashboard SHALL have its own address, built from an identifier that does not change when the dashboard is renamed or moved, so links and bookmarks to it keep working. The address SHALL NOT contain any part of the dashboard's name.

#### Scenario: Bookmark after a rename
- **WHEN** a member bookmarks the dashboard "Health", and an `analyst` then renames it to "Salud" and moves it left
- **THEN** the bookmark opens "Salud"

### Requirement: Former indicators address
The address of the former Indicators tab, `/projects/<id>/indicators`, SHALL open the project's first dashboard and replace the address with that dashboard's own.

#### Scenario: Old bookmark
- **WHEN** a member opens a bookmark to `/projects/<id>/indicators` of a project whose dashboards are "Health" and "Field"
- **THEN** "Health" is shown and the address becomes the address of "Health"

### Requirement: Dashboard that does not exist
Opening the address of a dashboard the project does not have, such as a removed one, SHALL open the project's first dashboard instead and replace the address with its own. A notice, announced to screen readers, SHALL say the dashboard asked for does not exist and name the one shown. It SHALL stay until dismissed or until another dashboard opens. The API SHALL answer a request for such a dashboard with 404 and a code saying the dashboard was not found.

#### Scenario: Bookmark to a removed dashboard
- **WHEN** a member opens a bookmark to a removed dashboard of a project whose first dashboard is "Health"
- **THEN** "Health" is shown, its tab is the current page, and a notice says the dashboard asked for does not exist and "Health" is shown instead

#### Scenario: Notice cleared
- **WHEN** that member then opens another dashboard, and then "Health" again
- **THEN** no notice is shown

#### Scenario: Notice dismissed
- **WHEN** that member dismisses the notice
- **THEN** the notice is gone and focus is on the heading of "Health"

#### Scenario: Dashboard of another project
- **WHEN** a member requests, under their project's address, a dashboard that belongs to another project
- **THEN** the API responds 404 with the code that the dashboard was not found

### Requirement: Changes by two members at the same time
A change to a dashboard that another member has removed, including to one of its widgets, SHALL be refused as not found and save nothing; the member SHALL then be told and taken to the project's first dashboard. A move based on an order that has changed since the member's page loaded SHALL be refused, and their dashboards reloaded. Otherwise the last change SHALL win.

#### Scenario: Widget added to a removed dashboard
- **WHEN** one `analyst` removes "Housing", the project's first dashboard, while another, whose page still shows "Housing", saves a new widget on it
- **THEN** the widget is not saved, and the second analyst is told "Housing" no longer exists and sees "Health", now the first dashboard

#### Scenario: Widget edited on a removed dashboard
- **WHEN** one `analyst` removes "Health" while another saves a change to one of its widgets
- **THEN** the change is not saved, and the second analyst is told "Health" no longer exists and sees the project's first dashboard

#### Scenario: Order changed meanwhile
- **WHEN** one `analyst` adds a dashboard, and another, whose page still shows the earlier dashboards, moves one of them
- **THEN** the move is refused with a message saying the dashboards have changed, and the second analyst's tab row shows the current dashboards

#### Scenario: Moved meanwhile
- **WHEN** one `analyst` moves "Field" to the left, and another, whose page still shows the earlier order, moves "Housing" to the right
- **THEN** the second move is refused with a message saying the dashboards have changed, and "Field" keeps its new place

#### Scenario: Two renames
- **WHEN** two analysts rename the same dashboard, one to "Salud" and then the other to "Health"
- **THEN** the dashboard is named "Health"

### Requirement: Focus after dashboard actions
After a member adds, duplicates or removes a dashboard, or a refused change opens the first dashboard, focus SHALL move to the heading of the dashboard that opens. After a rename or a move, or when the name dialog is cancelled, focus SHALL return to the control that started it. Focus SHALL NOT move when a dashboard is opened from its address or its tab.

#### Scenario: Keyboard user adds a dashboard
- **WHEN** a keyboard user adds the dashboard "Field progress"
- **THEN** "Field progress" opens and focus is on its heading

#### Scenario: Keyboard user moves a dashboard
- **WHEN** a keyboard user moves a dashboard to the right from its actions menu
- **THEN** focus returns to the actions menu button, so the move can be repeated

#### Scenario: Keyboard user renames a dashboard
- **WHEN** a keyboard user renames a dashboard from its actions menu
- **THEN** focus returns to the actions menu button

#### Scenario: Name dialog cancelled
- **WHEN** a keyboard user cancels the name dialog opened from the actions menu to duplicate a dashboard
- **THEN** focus returns to the actions menu button

#### Scenario: Dashboard opened from its tab
- **WHEN** a keyboard user activates the tab of the dashboard "Field"
- **THEN** "Field" opens and focus stays on its tab

#### Scenario: Focus after a refusal
- **WHEN** a keyboard user's change is refused because its dashboard was removed
- **THEN** the project's first dashboard opens and focus is on its heading
