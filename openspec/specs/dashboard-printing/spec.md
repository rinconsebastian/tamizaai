# dashboard-printing Specification

## Purpose

Lets a project member print one of the project's own dashboards, or save it as PDF, as a clean sheet that keeps the dashboard's wide-screen columns and says what it shows, with no controls on it.

## Requirements

### Requirement: Print action
Each of the project's own dashboards SHALL offer every member, viewers included, an action to print it once its widgets are read and it has at least one widget. The Performance and Clean data dashboards SHALL NOT offer it. Printing SHALL NOT change the dashboard, its widgets, the filter values in force or the page address.

#### Scenario: Viewer prints a dashboard
- **WHEN** a `viewer` opens the dashboard "Housing", which has 6 widgets, and chooses Print
- **THEN** the browser's print dialog opens for "Housing"

#### Scenario: Dashboard without widgets
- **WHEN** a member opens a dashboard that has no widgets
- **THEN** no Print action is offered

#### Scenario: Permanent dashboards
- **WHEN** a member opens the Performance or the Clean data dashboard
- **THEN** no Print action is offered

#### Scenario: Nothing saved
- **WHEN** a member who filtered "Housing" by "Municipality: Cali" prints it
- **THEN** afterwards the dashboard still shows Cali, the address is unchanged, and nothing about the dashboard or its widgets is saved

### Requirement: Getting a dashboard ready to print
After the Print action is chosen, the page SHALL wait until every widget has finished loading and every map has drawn its shapes and basemap tiles, for at most 20 seconds, and then open the browser's print dialog. While it waits, the action SHALL show that it is busy, SHALL NOT be usable again, and screen readers SHALL be told the print is being prepared.

#### Scenario: Widgets still loading
- **WHEN** a member chooses Print while two widgets of "Housing" are still loading
- **THEN** the print dialog opens only once both have drawn

#### Scenario: Busy state announced
- **WHEN** a keyboard user chooses Print
- **THEN** the action shows that it is busy until the dialog opens, and a screen reader announces that the print is being prepared

#### Scenario: Widget that takes too long
- **WHEN** a widget is still loading 20 seconds after Print was chosen
- **THEN** the print dialog opens, and that widget's box on paper says it had not finished loading

### Requirement: Dashboard after printing
Once the browser's print dialog closes, whether the member printed or cancelled, the dashboard SHALL look as it did before Print was chosen, and focus SHALL return to the Print action.

#### Scenario: Print cancelled
- **WHEN** a member cancels the print dialog
- **THEN** the dashboard shows the same widgets, charts and map views as before, and focus is on the Print action

### Requirement: Content of a printed dashboard
A printed dashboard SHALL show only its content: the project's name and form, the dashboard's name, the print header, the filters in force and the widgets. It SHALL NOT show the app's top bar, the back link, the role badge, the project's tabs, the dashboard's actions, the widget toolbar, any widget's buttons or resize handle, map controls or hints, notices, toasts, tooltips, open menus or the list of indicators.

#### Scenario: Analyst prints
- **WHEN** an `analyst` prints "Housing", which has a filter bar, a map and a column chart
- **THEN** the sheets show the project, "Housing", the print header, the filters and the two widgets, and no button, menu, tab, handle or map control

#### Scenario: Widget whose indicator has not published
- **WHEN** an `analyst` prints a dashboard with a widget whose indicator has not published
- **THEN** that widget's box says the indicator has not published, without the link to the scripts

### Requirement: Print header
Above the widgets, a printed dashboard SHALL give when it was printed, to the minute, in the project's time zone, naming that zone, and the latest time one of its widgets' indicators was published. When none has published, it SHALL say so. Times SHALL use the interface's shared date format.

#### Scenario: Header
- **WHEN** a member with the Spanish UI prints "Housing" of "Census 2026" at 10:30 in `America/Bogota`, and its widgets' indicators were last published at 05:12 that day
- **THEN** the sheet names "Census 2026" and "Housing", gives 10:30 with `America/Bogota` as the print time, and 05:12 as the time the data was published

#### Scenario: Nothing published
- **WHEN** a member prints a dashboard none of whose widgets' indicators has published
- **THEN** the header says the data has not been published yet

### Requirement: Filters on a printed dashboard
A printed dashboard that has filters SHALL list each of them in order, as plain text, with its label and the value in force, or "All" when it has none, as the filter bar shows them on the member's screen. A dashboard without filters SHALL print no filter line.

#### Scenario: Filters in force
- **WHEN** a member prints "Housing" with "Municipality" set to `Cali` and "Round" with no value
- **THEN** the sheet reads "Municipality: Cali" and "Round: All", with no button to clear or reset them

#### Scenario: No filters
- **WHEN** a member prints a dashboard that has no filters
- **THEN** the sheet shows no filter line

### Requirement: Twelve columns on paper
A printed dashboard SHALL place its widgets on the 12-column grid, each in its saved column, width and height in rows, whatever the width of the paper or of the member's screen. The phone layout, one widget per row, SHALL apply only to screens.

#### Scenario: Widgets side by side
- **WHEN** "Housing" has a stat tile 3 columns wide and a column chart 9 columns wide in the same row, and a member prints it
- **THEN** the sheet shows the two side by side, the chart three times as wide as the tile

#### Scenario: Printed from a phone
- **WHEN** a member prints "Housing" from a phone, whose screen shows one widget per row
- **THEN** the sheets show the same columns as on a wide screen

### Requirement: Sheets in either orientation
A printed dashboard SHALL ask the browser for 10 mm margins and SHALL leave the orientation to the member, in the browser's print dialog. On landscape A4 or Letter paper the dashboard SHALL print at full size. On a sheet narrower than the dashboard, such as portrait A4 or Letter, the whole dashboard SHALL be scaled down to the sheet's width, keeping its 12 columns and its proportions. In either orientation no widget SHALL be turned sideways or cut off at the side.

#### Scenario: Landscape Letter paper
- **WHEN** a member prints a dashboard with a widget in its last column on landscape Letter paper
- **THEN** the dashboard prints at full size and that widget's right edge is on the sheet

#### Scenario: Portrait A4 paper
- **WHEN** a member picks portrait A4 in the print dialog for the same dashboard
- **THEN** the sheets are upright, the dashboard is scaled down to the sheet's width with its 12 columns, and that widget's right edge is on the sheet

### Requirement: Widgets kept whole across sheets
A widget that fits on one sheet SHALL NOT be split between two: when it would cross the end of a sheet, it SHALL start on the next one, and no widget SHALL overlap another. A widget taller than a sheet SHALL start at the top of a sheet and continue on the next.

#### Scenario: Widget at the end of a sheet
- **WHEN** a map 5 rows high starts 2 rows before the end of the first sheet
- **THEN** the map is printed whole at the top of the second sheet

#### Scenario: Widget taller than a sheet
- **WHEN** a bar chart is 14 rows high and a sheet holds 8 rows
- **THEN** the chart starts at the top of a sheet and continues on the next one

#### Scenario: Scaled-down sheets
- **WHEN** the dashboard is scaled down to a portrait sheet and a widget moves whole to the next sheet
- **THEN** nothing of that widget, not even a line of its border, shows at the foot of the previous sheet

### Requirement: Charts on paper
Every chart, pie chart, heat map, population pyramid, Likert widget and stat tile SHALL print whole inside its widget's box, with all its marks, labels and legend. Each widget SHALL print as the member shows it at that moment, as a chart or as a table.

#### Scenario: Column chart
- **WHEN** a member prints a column chart with 12 columns
- **THEN** the sheet shows the 12 columns and their axis labels inside the widget's box

#### Scenario: Widget shown as a table
- **WHEN** a member shows one widget of "Housing" as a table and prints the dashboard
- **THEN** that widget prints as a table and the others as charts

#### Scenario: Heat map
- **WHEN** a member prints a heat map with 10 rows and 8 columns
- **THEN** the sheet shows its 80 cells, its row and column labels and the legend of its scale inside the widget's box

#### Scenario: Stacked chart
- **WHEN** a member prints a stacked column chart with 4 series
- **THEN** the sheet shows every segment and the legend naming the 4 series

### Requirement: Maps on paper
A map SHALL print its shapes, its basemap and its legend, with the basemap's attribution. A map the member has not zoomed or moved SHALL show all its shapes; one they have zoomed or moved SHALL show the area they were looking at.

#### Scenario: Map with a basemap
- **WHEN** a member prints a map with the OpenStreetMap basemap that they have not zoomed
- **THEN** the sheet shows every shape over the basemap, the legend, and the OpenStreetMap attribution, and no zoom button

#### Scenario: Zoomed map
- **WHEN** a member zooms a map into one municipality and prints the dashboard
- **THEN** the sheet shows that municipality's area

### Requirement: Colors on paper
Chart marks, map shapes and legend swatches SHALL print in their colors even when the browser's option to print background graphics is off. The sheet's background SHALL be white, and widgets SHALL have a thin border and no shadow.

#### Scenario: Background graphics off
- **WHEN** a member prints a map in a browser whose option to print background graphics is off
- **THEN** the map's legend swatches print in their colors

### Requirement: Tables on paper
A widget shown as a table, and a cross table widget, SHALL print the rows that fit in its box. When some rows do not fit, it SHALL say how many were left out. A table whose columns do not all fit SHALL print the group column and the columns that fit, and SHALL say how many columns were left out.

#### Scenario: Long table
- **WHEN** a member prints a widget shown as a table with 40 rows, of which 12 fit in its box
- **THEN** the sheet shows those 12 rows and says that 28 rows were not printed

#### Scenario: Short table
- **WHEN** a member prints a widget shown as a table whose 5 rows all fit
- **THEN** the sheet shows the 5 rows and no note about rows left out

#### Scenario: Wide cross table
- **WHEN** a member prints a cross table with 20 series columns, of which 9 fit in its box
- **THEN** the sheet shows the group column and those 9 columns, and says that 12 columns were not printed

### Requirement: Document title while printing
While the print dialog is open, the page's title SHALL be the project's name, the dashboard's name and the date it was printed, written year-month-day in the project's time zone. Afterwards it SHALL go back to what it was.

#### Scenario: PDF file name
- **WHEN** a member prints "Housing" of "Census 2026" on 28 October 2026 and chooses to save as PDF
- **THEN** the browser offers a file name made of "Census 2026", "Housing" and "2026-10-28"

### Requirement: Printing from the browser's menu
Printing one of the project's own dashboards from the browser's own menu or shortcut SHALL give the same sheets as the Print action, except that it SHALL NOT wait: a widget still loading SHALL say on paper that it had not finished loading, and a map SHALL print the basemap tiles it already has. Afterwards the dashboard SHALL look as it did before.

#### Scenario: Ctrl+P
- **WHEN** a member presses Ctrl+P on "Housing", whose widgets have all loaded, without using the Print action
- **THEN** the sheets have no controls, show the print header, the filters and the 12 columns, and every chart is whole inside its box

#### Scenario: Ctrl+P while a widget loads
- **WHEN** a member presses Ctrl+P while one widget of "Housing" is still loading
- **THEN** the print dialog opens at once, and that widget's box on paper says it had not finished loading
