## MODIFIED Requirements

### Requirement: Viewing a version
From the version history, an `analyst` or `admin` SHALL be able to view a version on the dashboard page: its name, filters, colors and widgets with the current data, read-only, with a notice naming the version, its author and time, and actions to return to the current version or restore an older one. Its filters SHALL offer its widgets' values. While it is shown, the page SHALL offer no other action that changes or prints the dashboard, and a browser-printed sheet SHALL name the version. Each widget's menu SHALL stay offered with "Show as table" and its export formats but without the editing actions, and the files it makes SHALL name the version.

#### Scenario: Viewing an older version
- **WHEN** an `analyst` views version 9 of "Housing", which had a widget since removed
- **THEN** the page shows that widget with today's data, a notice names version 9, its author and time, and no action to add, edit, move or remove a widget, or to print, is offered

#### Scenario: Widget menu of a version
- **WHEN** an `analyst` opens the menu of a widget while version 9 of "Housing" is shown
- **THEN** it holds "Show as table" and the "Export" group, and neither "Edit widget" nor "Remove widget"

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

#### Scenario: Exporting a widget of a version
- **WHEN** an `analyst` exports, as a PNG image and as a workbook, the widget of version 9 of "Housing" that has since been removed
- **THEN** both files show that widget with today's data, the image's footer and the workbook's "Summary" sheet name version 9, and both file names hold `_v9` before the day
