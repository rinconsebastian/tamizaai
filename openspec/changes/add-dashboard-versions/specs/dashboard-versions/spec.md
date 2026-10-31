# Spec Delta

## Purpose

Keep a short history of each of a project's own dashboards, recorded automatically on every change a person or an AI client makes, so that any of the last 10 versions can be viewed and restored.

## ADDED Requirements

### Requirement: Dashboard versions
Each of a project's own dashboards SHALL have a history of versions. A version SHALL hold the dashboard's name, its filters with their defaults, and its widgets with their title, chart type, query, the dashboard filters they ignore, position and size. It SHALL record its number, who made it, when, whether it was made in Tamiza or through an AI client, and what changed. The newest version SHALL be the dashboard as it is.

#### Scenario: Widget added
- **WHEN** an `analyst` adds the widget "Water source" to "Housing", whose newest version is 4
- **THEN** "Housing" has version 5, attributed to that analyst at that time, holding the new widget with the others, and saying the widget "Water source" was added

#### Scenario: Numbers never reused
- **WHEN** "Housing" has versions 3 to 12 and an `analyst` restores version 9
- **THEN** the restore is version 13

### Requirement: Changes that make a version
Every change a person saves to a dashboard SHALL make a new version, however small. That covers adding, renaming or duplicating the dashboard, saving its filters, and adding, editing, removing, moving or resizing a widget, or moving a widget to or from another dashboard. One action SHALL make one version of each dashboard it changes. A save that leaves a dashboard as its newest version holds SHALL make none.

#### Scenario: One character
- **WHEN** an `analyst` changes the title of a widget on "Housing" from "Coverage" to "Coverage."
- **THEN** "Housing" has a new version with that title

#### Scenario: Filters saved unchanged
- **WHEN** an `analyst` opens the filters of "Housing" and saves them without changing anything
- **THEN** "Housing" has no new version

#### Scenario: Widget moved to another dashboard
- **WHEN** an `analyst` moves the widget "Coverage by municipality" from "Health" to "Field"
- **THEN** "Health" has a new version saying the widget went to "Field", and "Field" has a new version saying it came from "Health"

#### Scenario: Drag that pushes other widgets
- **WHEN** an `analyst` drags a widget onto a place that pushes three other widgets down
- **THEN** the dashboard has one new version that holds the new place of all four

#### Scenario: New dashboard
- **WHEN** an `analyst` adds the dashboard "Field"
- **THEN** "Field" has version 1, with no widgets, saying the dashboard was added

### Requirement: Changes that are not dashboard changes
Runs and the data they publish, synchronizations, the filter values a member picks, a member's basemap or map view, showing widgets as tables, printing, the order of the project's dashboards and the dashboard's scheduled email SHALL NOT make versions, and a restore SHALL NOT change them.

#### Scenario: Run publishes new data
- **WHEN** a run publishes new rows for the indicators of "Housing"
- **THEN** its widgets show the new data and "Housing" has no new version

#### Scenario: Dashboard moved
- **WHEN** an `analyst` moves "Housing" to the left of "Health"
- **THEN** neither dashboard has a new version

#### Scenario: Scheduled email saved
- **WHEN** an `analyst` schedules the email of "Housing"
- **THEN** "Housing" has no new version

#### Scenario: Filter values
- **WHEN** a member picks the value "Rionegro" in the filter "Municipality" of "Housing"
- **THEN** "Housing" has no new version

### Requirement: Moves and resizes in a row
Moves and resizes of widgets that one person makes on one dashboard, each less than one minute after the previous one, with no other change to that dashboard between them, SHALL make one version, which holds the last arrangement. Any other change, by that person or anyone else, SHALL end it, and the next move SHALL make a new version.

#### Scenario: Rearranging a dashboard
- **WHEN** an `analyst` makes five drags on "Housing" within forty seconds
- **THEN** "Housing" has one new version, holding the arrangement after the fifth drag

#### Scenario: Pause between moves
- **WHEN** an `analyst` drags a widget on "Housing" and drags another two minutes later
- **THEN** "Housing" has two new versions

#### Scenario: Other change in between
- **WHEN** an `analyst` drags a widget, renames another widget, and drags a third within a minute
- **THEN** "Housing" has three new versions

### Requirement: State before the history began
A dashboard that existed before version history SHALL have a version 1 that holds its state when version history began. That version SHALL say it holds the state before version history, attributed to the person who last changed the dashboard or one of its widgets, at that time.

#### Scenario: First change after the upgrade
- **WHEN** an `analyst` removes a widget from "Housing" for the first time after the upgrade
- **THEN** "Housing" has version 1 holding the widget and version 2 without it, and restoring version 1 brings the widget back

### Requirement: Versions kept
A dashboard SHALL keep its 10 newest versions, the current one included. A new version SHALL delete the oldest when the dashboard already has 10. Version numbers SHALL keep counting up.

#### Scenario: Eleventh version
- **WHEN** "Housing" has versions 1 to 10 and an `analyst` adds a widget
- **THEN** "Housing" keeps versions 2 to 11

#### Scenario: Version no longer kept
- **WHEN** an `analyst` asks the API to restore version 1 of a dashboard that keeps versions 2 to 11
- **THEN** the API responds 404 with the code `dashboards.versionNotFound`, and the dashboard is unchanged

### Requirement: Version history of a dashboard
For an `analyst` or `admin`, the dashboard's actions SHALL include **Version history**. It SHALL list the kept versions, newest first, each with its number, what changed, who made it and when, and which one is current. A version made through an AI client SHALL show "via MCP" and the request the client gave. It SHALL fit a viewport 360 pixels wide without scrolling the page sideways.

#### Scenario: History listed
- **WHEN** an `analyst` opens the version history of "Housing", whose versions are 3 to 12
- **THEN** versions 12 to 3 are listed, version 12 is marked current, and each shows what changed, its author and its time

#### Scenario: Version made by Claude
- **WHEN** Claude added five widgets to "Housing" for an analyst's request "Add five water indicators"
- **THEN** the history shows one version by that analyst, marked "via MCP", with "Add five water indicators" and the five widgets added

#### Scenario: Several changes in one version
- **WHEN** a version holds more changes than the history shows
- **THEN** the history shows the first ones and how many more there are

#### Scenario: History at phone width
- **WHEN** an `analyst` opens the version history in a viewport 360 pixels wide
- **THEN** every version's number, changes, author and time and its actions are visible, and the page does not scroll horizontally

#### Scenario: Move saved before the history opens
- **WHEN** an `analyst` drags a widget and opens the version history at once
- **THEN** the current version holds the widget in its new place

### Requirement: Viewing a version
From the version history, an `analyst` or `admin` SHALL be able to view a version on the dashboard page. The page SHALL show the version's name, filters and widgets with the current data, read-only, with a notice naming the version, its author and time. The notice SHALL offer to go back to the current version and, for an older version, to restore it. Nothing on the page SHALL change the dashboard while a version is shown.

#### Scenario: Viewing an older version
- **WHEN** an `analyst` views version 9 of "Housing", which had a widget since removed
- **THEN** the page shows that widget with today's data, a notice names version 9, its author and time, and no action to edit, move or remove a widget is offered

#### Scenario: Back to the current version
- **WHEN** that `analyst` chooses to go back to the current version
- **THEN** the page shows "Housing" as it is now

### Requirement: Restoring a version
An `analyst` or `admin` SHALL be able to restore a kept version after confirming. The dashboard's name, filters and widgets SHALL become those of that version, recorded as a new version that names the version restored, so the state it replaced stays in the history. The dashboard SHALL keep its address, its place and its scheduled email. Restoring a version that equals the current one SHALL change nothing.

#### Scenario: Removed widget brought back
- **WHEN** an `analyst` removed the widget "Water source" from "Housing" in version 8 and restores version 7
- **THEN** after confirming, "Housing" shows "Water source" again, version 9 says version 7 was restored, and one message says so

#### Scenario: Restore undone
- **WHEN** that `analyst` then restores version 8
- **THEN** "Housing" is again as it was in version 8, as version 10

#### Scenario: Scheduled email kept
- **WHEN** an `analyst` restores an older version of "Housing", whose scheduled email is active
- **THEN** the email keeps its recipients, days and time, and its next send shows the restored widgets

#### Scenario: Filter values in the address
- **WHEN** a member's page address holds a value for a filter that the restored version does not define
- **THEN** that value is dropped from the address with the notice for filters no longer defined

### Requirement: Restored name
A restore SHALL keep the dashboard's current name when another dashboard of the project now has the version's name, ignoring case, and the message after the restore SHALL say that the name was kept and why.

#### Scenario: Name taken
- **WHEN** "Water" was renamed "Agua", another dashboard was then named "Water", and an `analyst` restores a version of "Agua" named "Water"
- **THEN** the widgets and filters of that version are restored, the dashboard is still named "Agua", and the message says another dashboard is named "Water"

### Requirement: Restored widgets
A restore SHALL bring back the widgets of the version with their settings. A widget of the version that is now on another dashboard SHALL be restored as a new widget, and the other dashboard SHALL NOT change. A restored widget whose indicator or columns are gone SHALL be restored as it was and show the message of a stale widget.

#### Scenario: Widget since moved to another dashboard
- **WHEN** the widget "Coverage" was moved from "Health" to "Field" and an `analyst` restores a version of "Health" that holds it
- **THEN** "Health" holds a copy of "Coverage", and "Field" still holds "Coverage"

#### Scenario: Indicator gone
- **WHEN** an `analyst` restores a version whose widget uses an indicator that was removed since
- **THEN** the widget is restored and shows the message that its indicator is not published

### Requirement: Restore over the widget limit
A restore of a version that holds more widgets than one dashboard may now hold SHALL be refused with an error naming the limit, and SHALL change nothing.

#### Scenario: Limit lowered
- **WHEN** an operator has lowered the widget limit to 20 and an `analyst` restores a version that holds 30 widgets
- **THEN** the API responds 400 with an error saying a dashboard can have at most 20 widgets, the analyst sees that message, and the dashboard is unchanged

### Requirement: Restoring while others change
A restore SHALL replace the dashboard as it is when the restore is saved, even when it changed after the history was opened. The state replaced SHALL stay in the history. A restore of a version no longer kept SHALL be refused as not found, and the history SHALL list the versions kept. A restore of a removed dashboard SHALL be handled as any change to a removed dashboard.

#### Scenario: Changed meanwhile
- **WHEN** an `analyst` opens the history of "Housing", another analyst adds a widget, and the first restores version 7
- **THEN** "Housing" is as in version 7, and the version with the other analyst's widget stays in the history

#### Scenario: Version deleted meanwhile
- **WHEN** an `analyst` restores the oldest version listed after ten other changes deleted it
- **THEN** the analyst is told that version is no longer kept, the dashboard is unchanged, and the history lists the versions kept

#### Scenario: Dashboard removed meanwhile
- **WHEN** an `analyst` restores a version of "Housing" just after another analyst removed "Housing"
- **THEN** nothing is restored, and the analyst is told "Housing" no longer exists and sees the project's first dashboard

### Requirement: Versions of duplicated and removed dashboards
A copy made by duplicating a dashboard SHALL start its own history with a version 1 that holds the copy and says which dashboard it was duplicated from. The original's versions SHALL stay with the original. Removing a dashboard SHALL remove its versions with it.

#### Scenario: Copy
- **WHEN** an `analyst` duplicates "Housing", which has versions 3 to 12
- **THEN** "Housing (copy)" has only version 1, saying it was duplicated from "Housing", and "Housing" keeps versions 3 to 12

#### Scenario: Removed dashboard
- **WHEN** an `analyst` removes "Housing"
- **THEN** its versions are removed with it, and none of them can be listed or restored
