## ADDED Requirements

### Requirement: Widget menu
Every member SHALL find one menu button on each widget of a project's own dashboards, at the right of its title and named after it. It SHALL be the only control in the header besides the editors' move handle. Its menu SHALL hold, in this order, Show as table, the Export group (see `widget-export`) and, for those who may edit, the editing actions, with a separator between each part it holds.

#### Scenario: Analyst's widget header
- **WHEN** an `analyst` looks at the header of the column chart "Coverage by municipality"
- **THEN** it shows the move handle, the title and one menu button named after "Coverage by municipality", in that order, and no other button

#### Scenario: Analyst's widget menu
- **WHEN** that `analyst` opens the menu of "Coverage by municipality"
- **THEN** it holds "Show as table", then an "Export" group with "PNG image", "SVG image" and "Excel workbook (XLSX)", then "Edit widget" and "Remove widget", each part set apart from the next

#### Scenario: Viewer's widget menu
- **WHEN** a `viewer` opens the menu of "Coverage by municipality"
- **THEN** it holds "Show as table" and the "Export" group, and no action to edit or remove the widget

#### Scenario: Keyboard
- **WHEN** a member moves to the menu button of a widget with the keyboard and opens it with Enter
- **THEN** the arrow keys move through its items, Escape closes it, and focus goes back to the button

### Requirement: Show as table in the widget menu
A widget's menu SHALL offer "Show as table" as an item that is checked while the widget shows its table instead of its chart, and choosing it SHALL switch between the two and close the menu. It SHALL be offered only while the widget has rows to draw as a chart: never for a stat tile or a cross table. A switch SHALL be announced to screen readers. "Show all as tables" on the toolbar SHALL reset the choices made on single widgets.

#### Scenario: Switched to a table
- **WHEN** a member chooses "Show as table" in the menu of "Coverage by municipality"
- **THEN** the widget shows its table, a screen reader hears that "Coverage by municipality" is shown as a table, and the item reads as checked the next time the menu opens

#### Scenario: Back to the chart
- **WHEN** the member chooses the checked "Show as table" again
- **THEN** the widget shows its chart again and the item reads as unchecked

#### Scenario: Stat tile
- **WHEN** a member opens the menu of a stat tile
- **THEN** it offers no "Show as table"

#### Scenario: No rows to draw
- **WHEN** no row of a column chart's table matches the dashboard's filters
- **THEN** its menu offers no "Show as table"

#### Scenario: Show all as tables
- **WHEN** a member has shown one widget as a table and then chooses "Show all as tables" and "Show all as charts"
- **THEN** every widget shows its chart, that one included

### Requirement: Editing actions in the widget menu
An `analyst` or `admin` SHALL find "Edit widget" and "Remove widget" as the last part of each widget's menu, "Remove widget" marked as destructive. They SHALL NOT be offered to a `viewer`, nor while a version of the dashboard is shown. Closing the widget form without saving SHALL return focus to the widget's menu button.

#### Scenario: Analyst edits a widget
- **WHEN** an `analyst` chooses "Edit widget" in the menu of "Coverage by municipality"
- **THEN** the widget form opens with that widget

#### Scenario: Form cancelled
- **WHEN** the `analyst` closes that form without saving
- **THEN** focus is on the menu button of "Coverage by municipality"

#### Scenario: Analyst removes a widget
- **WHEN** an `analyst` chooses "Remove widget" in the menu of "Coverage by municipality" and confirms
- **THEN** the widget is removed from the dashboard

### Requirement: Widget menu at every width and on paper
The widget menu button SHALL be offered at every width from 360 pixels, with no breakpoint that hides it. It SHALL NOT be printed, and the widget form's preview SHALL NOT offer it.

#### Scenario: Phone width
- **WHEN** a member opens "Housing" on a screen 360 pixels wide
- **THEN** every widget's menu button can be reached without scrolling the page sideways

#### Scenario: Printed sheet
- **WHEN** a member prints "Housing"
- **THEN** no widget's menu button is on the sheet

#### Scenario: Widget form preview
- **WHEN** an `analyst` previews a widget in the widget form
- **THEN** the preview offers no widget menu, and keeps its own switch between chart and table
