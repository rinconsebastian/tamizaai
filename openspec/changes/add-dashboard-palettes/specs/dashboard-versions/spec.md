## MODIFIED Requirements

### Requirement: Changes that make a version
Every change a person saves to a dashboard in Tamiza SHALL make a new version, however small: adding, renaming or duplicating the dashboard, saving its filters, saving its colors (its palette and its colors kept by value), and adding, editing, removing, moving or resizing a widget, or moving one between dashboards. One action SHALL make one version of each dashboard it changes. A save that leaves a dashboard as its newest version holds SHALL make none. An AI client's changes follow `mcp-server` "One version per request".

#### Scenario: One character
- **WHEN** an `analyst` changes the title of a widget on "Housing" from "Coverage" to "Coverage."
- **THEN** "Housing" has a new version with that title

#### Scenario: Two drags
- **WHEN** an `analyst` drags a widget of "Housing" to a new place and then drags another
- **THEN** "Housing" has two new versions, the second holding both new places

#### Scenario: Filters saved unchanged
- **WHEN** an `analyst` opens the filters of "Housing" and saves them without changing anything
- **THEN** "Housing" has no new version

#### Scenario: Widget form saved unchanged
- **WHEN** an `analyst` opens a widget of "Housing" saved before series settings existed and saves the form without changing anything
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

#### Scenario: Palette changed
- **WHEN** an `analyst` changes the palette of "Housing" from *Tamiza* to *Earth*
- **THEN** "Housing" has a new version with the palette *Earth*, saying its colors changed

#### Scenario: Colors saved unchanged
- **WHEN** an `analyst` opens **Colors** on "Housing" and saves without changing anything
- **THEN** "Housing" has no new version

#### Scenario: Widget color changed
- **WHEN** an `analyst` changes a column chart of "Housing" from color 1 to color 3
- **THEN** "Housing" has a new version saying the widget changed

#### Scenario: Dashboard saved before palettes
- **WHEN** a dashboard's newest version was recorded before palettes existed, and an `analyst` opens **Colors** and saves the palette *Tamiza* with no colors kept by value
- **THEN** the dashboard has no new version

### Requirement: Viewing a version
From the version history, an `analyst` or `admin` SHALL be able to view a version on the dashboard page: its name, filters, colors and widgets with the current data, read-only, with a notice naming the version, its author and time, and actions to go back to the current version and to restore an older one. Its filters SHALL offer its widgets' values. While it is shown, the page SHALL offer no other action that changes or prints the dashboard, and a sheet printed from the browser SHALL name the version.

#### Scenario: Viewing an older version
- **WHEN** an `analyst` views version 9 of "Housing", which had a widget since removed
- **THEN** the page shows that widget with today's data, a notice names version 9, its author and time, and no action to add, edit, move or remove a widget, or to print, is offered

#### Scenario: Back to the current version
- **WHEN** that `analyst` chooses to go back to the current version
- **THEN** the page shows "Housing" as it is now

#### Scenario: Filter values of a version
- **WHEN** an `analyst` views a version of "Housing" whose filter "Source" is on a column of a widget removed since, and opens that filter
- **THEN** it offers the values of that widget's indicator

#### Scenario: Printed from the browser
- **WHEN** an `analyst` prints from the browser's menu while version 9 of "Housing" is shown
- **THEN** the sheet says it is version 9, with its author and time, and shows no button or notice

#### Scenario: Colors of a version
- **WHEN** "Housing" now has the palette *Earth* and an `analyst` views its version 4, which had the palette *Tamiza*
- **THEN** the page shows version 4's widgets in the colors of *Tamiza*, with version 4's colors kept by value

### Requirement: Restoring a version
An `analyst` or `admin` SHALL be able to restore a kept version after confirming. The dashboard's name, filters, colors and widgets SHALL become those of that version, recorded as a new version that names the version restored, so the state it replaced stays in the history. The dashboard SHALL keep its address, its place and its scheduled email. A restore that would leave the dashboard as it is SHALL change nothing and say so.

#### Scenario: Removed widget brought back
- **WHEN** an `analyst` removed the widget "Water source" from "Housing" in version 8 and restores version 7
- **THEN** after confirming, "Housing" shows "Water source" again, version 9 says version 7 was restored, and one message says so

#### Scenario: Restore undone
- **WHEN** that `analyst` then restores version 8
- **THEN** "Housing" is again as it was in version 8, as version 10

#### Scenario: Version equal to the current one
- **WHEN** an `analyst` restores version 8 of "Housing" while its current version 10 holds the same dashboard
- **THEN** nothing changes, no version is added, and the message says "Housing" already is as in version 8

#### Scenario: Scheduled email kept
- **WHEN** an `analyst` restores an older version of "Housing", whose scheduled email is active
- **THEN** the email keeps its recipients, days and time, and its next send shows the restored widgets

#### Scenario: Filter values in the address
- **WHEN** the restoring member's page address holds a value for a filter that the restored version does not define
- **THEN** that value is dropped from the address with the notice for filters no longer defined

#### Scenario: Colors restored
- **WHEN** an `analyst` changed the palette of "Housing" to *Earth* and kept color 5 for `Female` in version 6, and restores version 5
- **THEN** "Housing" has version 5's palette and colors kept by value again

#### Scenario: Version recorded before palettes
- **WHEN** an `analyst` restores a version of "Housing" recorded before palettes existed, while "Housing" has the palette *Earth*
- **THEN** "Housing" has the palette *Tamiza* and no colors kept by value
