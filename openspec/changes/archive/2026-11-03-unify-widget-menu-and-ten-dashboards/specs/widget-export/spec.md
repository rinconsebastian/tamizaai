## RENAMED Requirements

- FROM: `### Requirement: Export button`
- TO: `### Requirement: Export in the widget menu`

## MODIFIED Requirements

### Requirement: Export in the widget menu
Every member SHALL find an "Export" group in the menu of each widget of a project's own dashboards (see `dashboard-indicators` "Widget menu"), under a visible heading, after "Show as table" and before the editing actions. It SHALL offer "PNG image", "SVG image" and "Excel workbook (XLSX)" as items of the menu itself, with no submenu. It SHALL be offered wherever the widget menu is, and SHALL NOT be offered in the widget form's preview or in the script editor.

#### Scenario: Viewer exports
- **WHEN** a `viewer` opens the menu of the column chart "Coverage by municipality" on "Housing"
- **THEN** its "Export" group offers "PNG image", "SVG image" and "Excel workbook (XLSX)"

#### Scenario: Analyst's widget header
- **WHEN** an `analyst` looks at the header of "Coverage by municipality"
- **THEN** it shows no Export button of its own: the formats are in the widget's menu, between "Show as table" and "Edit widget"

#### Scenario: Formats in the menu itself
- **WHEN** a member opens the menu of "Coverage by municipality" and moves down with the arrow keys
- **THEN** they reach "PNG image", "SVG image" and "Excel workbook (XLSX)" without opening another menu, and a screen reader says they belong to the "Export" group

#### Scenario: Phone width
- **WHEN** a member opens "Housing" on a screen 360 pixels wide
- **THEN** every widget's export formats can be reached from its menu without scrolling the page sideways

#### Scenario: Printed sheet
- **WHEN** a member prints "Housing"
- **THEN** no widget's menu or export format is on the sheet

#### Scenario: Widget form preview
- **WHEN** an `analyst` previews a widget in the widget form
- **THEN** the preview offers no export

### Requirement: Formats that cannot be exported yet
A format that cannot be exported SHALL stay in the widget menu's "Export" group, announced as unavailable, with the reason on a second line. Every format SHALL be unavailable while the widget is loading, after it failed to load, while its indicator is not published, while its table lacks a column the widget needs, and when no row matches. A map's images SHALL also be unavailable while the map is being drawn or moved, or when it has no shape to draw; its workbook SHALL stay available.

#### Scenario: Widget still loading
- **WHEN** a member opens the menu of a widget that is still loading
- **THEN** the three formats are unavailable, each reading "The widget is still loading."

#### Scenario: Indicator not published
- **WHEN** a widget's indicator `coverage` has not published a table
- **THEN** the three formats are unavailable, each saying that `coverage` has not published yet

#### Scenario: Column removed
- **WHEN** a script change removed the column `municipality` that a widget groups by
- **THEN** the three formats are unavailable, each naming `municipality` as missing from the table

#### Scenario: No rows
- **WHEN** the dashboard filter "Municipality" is `Rionegro` and no row of a widget's table matches
- **THEN** the three formats are unavailable, each reading "No rows match this widget's query."

#### Scenario: Map still being drawn
- **WHEN** a member opens the menu of a map that is still being drawn
- **THEN** "PNG image" and "SVG image" are unavailable, reading "The map is still being drawn.", and "Excel workbook (XLSX)" is available

#### Scenario: Row limit reached
- **WHEN** a widget shows only the first 5,000 rows because its query matches more
- **THEN** the three formats are available, and each file carries the row-limit note

### Requirement: Feedback while a widget export runs
While a widget's file is being prepared, that widget's menu button SHALL show that it is busy and its name SHALL say so, but the menu SHALL still open: its three formats SHALL be unavailable, each reading "Preparing the export…", and its other items SHALL stay usable. The format items are the controls that started the export in the sense of `web-ui` "Feedback while an action runs". Other widgets' menus SHALL stay as they were. The export SHALL end with exactly one message: the image saved, the workbook ready with its number of rows, or the reason it failed (see `web-ui` "Feedback while an action runs").

#### Scenario: Two widgets
- **WHEN** a member exports the workbook of "Coverage by municipality" while "Type of dwelling" is on the same dashboard
- **THEN** only the menu button of "Coverage by municipality" shows that it is busy, and the formats of "Type of dwelling" can still be chosen

#### Scenario: Menu of a widget being exported
- **WHEN** a member opens the menu of "Coverage by municipality" while its workbook is being prepared
- **THEN** "PNG image", "SVG image" and "Excel workbook (XLSX)" are unavailable, each reading "Preparing the export…", and "Show as table" can be chosen

#### Scenario: Export ends
- **WHEN** that workbook is ready
- **THEN** the menu button no longer shows that it is busy, and the three formats are available again

#### Scenario: Workbook ready
- **WHEN** the workbook of a bar chart with 32 bars is ready
- **THEN** one message says the workbook of "Coverage by municipality" is ready with 32 rows, and the browser downloads it

#### Scenario: Image saved
- **WHEN** a member exports "Coverage by municipality" as an SVG image
- **THEN** one message says it was saved as an SVG image, and the browser saves the file
