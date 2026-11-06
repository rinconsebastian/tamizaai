# widget-export Specification

## Purpose

Lets every member take one widget of a project's own dashboard out of Tamiza, as a PNG image or an SVG image for a report or a slide, or as an Excel workbook holding the data behind it.

## Requirements

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

### Requirement: Widget image as shown
A PNG or SVG image SHALL show the widget as the member sees it when exporting: the same rows, the filter values in force, the chart or the table as the member shows it, the member's map view, and, while a version is shown, that version's widget and colors. Making an image SHALL send no request to the API and SHALL save nothing.

#### Scenario: Filter in force
- **WHEN** a member picks `Cali` in the dashboard filter "Municipality" and exports a widget as a PNG image
- **THEN** the image shows the widget's chart narrowed to `Cali`, as on screen

#### Scenario: Widget shown as a table
- **WHEN** a member shows "Coverage by municipality" as a table and exports it as an SVG image
- **THEN** the image shows the table, not the chart

#### Scenario: Data republished since the page was loaded
- **WHEN** a run republishes `coverage` while a member's page still shows the earlier rows, and the member exports a widget over it as a PNG image
- **THEN** the image shows the rows on the member's screen

### Requirement: Parts of a widget image
An image SHALL hold, top to bottom: the widget's full title, its whole subtitle when it has one, the notes shown above its chart, its caption unless the widget hides it, its chart or table, its whole legend, key or scale, the notes shown below it, its whole footnote when it has one, and a footer. It SHALL NOT hold tooltips, buttons, handles, map controls, menus, focus or hover marks, the indicator's description, or text meant only for screen readers.

#### Scenario: Long title
- **WHEN** a widget's title has 120 characters and is cut with an ellipsis on screen
- **THEN** its image shows the 120 characters, over more than one line

#### Scenario: Stacked columns
- **WHEN** a member exports a stacked column chart with 4 series as a PNG image
- **THEN** the image shows every segment and the legend naming the 4 series

#### Scenario: Pie legend
- **WHEN** a pie chart's legend has 12 rows and scrolls inside its box on screen
- **THEN** its image shows the 12 rows

#### Scenario: Box plot
- **WHEN** a member exports a box plot with 6 boxes, 23 of whose outliers are not drawn
- **THEN** the image shows the 6 boxes, the key explaining each mark, and the note that 23 outliers are not drawn

#### Scenario: Stat tile
- **WHEN** a member exports the stat tile "Households visited" as an SVG image
- **THEN** the image shows its title, its value and its description

#### Scenario: Tooltip open
- **WHEN** a bar's tooltip is open when a member exports the chart
- **THEN** the image shows no tooltip and no bar drawn as pointed at

#### Scenario: Subtitle and footnote
- **WHEN** a member exports "2.4. En qué lugares", whose subtitle shows two cut lines on screen and whose footnote is "No suman 100 %: respuesta múltiple.", as a PNG image
- **THEN** the image shows the whole subtitle under the title, and the footnote under the chart and its notes, above the footer

#### Scenario: Caption hidden
- **WHEN** a member exports a column chart that hides its caption
- **THEN** the image shows no caption line

#### Scenario: Own caption text
- **WHEN** a member exports a bar chart grouped by `categoria` whose caption text is "% de quienes asistieron", in the English interface
- **THEN** the image's caption line reads "% de quienes asistieron by categoria"

### Requirement: Footer of a widget image
An image's footer SHALL name the project and the dashboard, the version while one is shown, every filter of the dashboard with its value in force or "All", when the widget's indicator was published as the page knows it, and when the image was made, in the project's time zone, which it SHALL name. It SHALL give the source of the dashboard, or of the version shown, when it has one. It SHALL carry no logo.

#### Scenario: Footer
- **WHEN** a member of "Census 2026", whose time zone is `America/Bogota`, exports a widget of "Housing" with "Municipality" set to `Cali` and "Round" without a value
- **THEN** the footer names "Census 2026" and "Housing", reads "Municipality: Cali" and "Round: All", and gives when the data was published and when the image was exported, naming `America/Bogota`

#### Scenario: Version shown
- **WHEN** an `analyst` exports a widget while version 9 of "Housing" is shown
- **THEN** the footer says it is version 9, with its author and time

#### Scenario: Dashboard without filters
- **WHEN** a member exports a widget of a dashboard with no filters
- **THEN** the footer has no filter line

#### Scenario: Source in the footer
- **WHEN** a member exports a widget of "Teatro", whose source is "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas", in the English interface
- **THEN** the footer reads "Source: Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas"

#### Scenario: Dashboard without a source
- **WHEN** a member exports a widget of a dashboard with no source
- **THEN** the footer has no source line

### Requirement: Tables in widget images
An image of a table, a cross table or a widget shown as a table, SHALL hold its header and at most 100 rows, and the columns that fit in 2,400 pixels, always keeping the first column. Its cells SHALL read as on screen. The image SHALL say how many rows and columns it left out and that the workbook holds them all.

#### Scenario: Long table
- **WHEN** a member exports as a PNG image a bar chart shown as a table with 128 rows
- **THEN** the image shows the first 100 rows and says that 28 rows are not shown in the image and that the workbook has them all

#### Scenario: One row over
- **WHEN** the table has 101 rows
- **THEN** the image says that 1 row is not shown in the image

#### Scenario: Wide cross table
- **WHEN** a member exports a cross table with 30 series columns that do not all fit in 2,400 pixels
- **THEN** the image keeps the group column and the columns that fit, and says how many columns are not shown in the image

### Requirement: Map images
A map's image SHALL hold its shapes, clipped to the member's current view, and its legend. It SHALL NOT hold basemap tiles, the Google logo or map credits, and making it SHALL request no tile. While the member shows a basemap, the menu's image formats and the image itself SHALL say that the basemap is not included.

#### Scenario: Zoomed map
- **WHEN** a member zooms a map of municipalities in on Cali and exports it as a PNG image
- **THEN** the image shows the shapes in the member's view, with the map's legend

#### Scenario: Map over a basemap
- **WHEN** a member shows the Satellite basemap on a map and exports it as an SVG image
- **THEN** the image holds the shapes and the legend, no tile, logo or credit, and a note saying that the basemap is not included in the image

### Requirement: Colors of widget images
An image SHALL have a white background and SHALL use the light colors of the dashboard's palette and of the colors kept by value, whatever the screen's theme. An SVG image SHALL keep those colors when it is opened outside Tamiza.

#### Scenario: Palette kept
- **WHEN** "Housing" has the palette *Earth* and keeps color 5 for `Female`, and a member exports a pie chart grouped by `sex` as an SVG image
- **THEN** the image opened in another program shows `Female` in color 5 of *Earth* and the other slices in their *Earth* colors

#### Scenario: Gaps between segments
- **WHEN** a member exports a stacked column chart as a PNG image
- **THEN** the gaps between its segments are white

### Requirement: SVG image format
An SVG image SHALL be a standalone file of the widget's on-screen width, whose text stays text. It SHALL hold no script, style sheet, embedded HTML or reference to another file. Names and values that look like markup SHALL be written as text.

#### Scenario: Opened elsewhere
- **WHEN** a member opens an exported SVG image in another browser or a vector editor
- **THEN** it shows the same marks, colors and text as in Tamiza, and its text can be selected or edited

#### Scenario: Markup in a value
- **WHEN** a group value of a bar chart is `<script>alert(1)</script>`
- **THEN** the SVG image shows that value as text and holds no script element

### Requirement: PNG image format
A PNG image SHALL be the SVG image drawn at twice its size and marked 192 dots per inch, so a word processor places it at its on-screen size. When twice its size passes 16,000,000 pixels or 8,192 pixels on a side, it SHALL be drawn as large as fits. A widget that does not fit at its on-screen size SHALL be refused with a message suggesting the SVG image.

#### Scenario: Usual widget
- **WHEN** a member exports as a PNG image a widget whose image is 600 by 340 pixels on screen
- **THEN** the file is 1,200 by 680 pixels, marked 192 dots per inch

#### Scenario: Tall widget
- **WHEN** a widget's image is 1,232 by 3,600 pixels on screen
- **THEN** the PNG image is drawn at less than twice its size and holds at most 16,000,000 pixels

#### Scenario: Too large
- **WHEN** a widget's image would not fit in 16,000,000 pixels even at its on-screen size
- **THEN** no file is saved, and the message says the widget is too large for a PNG image and suggests the SVG image

### Requirement: Rows of a widget workbook
A widget's workbook SHALL be prepared by the API from the widget's settings and the dashboard's filter values in force as the member's screen sent them, with the filters the widget ignores left out, reading its indicator's table as published at that moment. It SHALL hold the rows the widget plots, up to the widget row limit.

#### Scenario: Filter in force
- **WHEN** a member with "Municipality" set to `Cali` exports the workbook of a widget grouped by `neighborhood`
- **THEN** the data sheet lists only the neighborhoods of `Cali`

#### Scenario: Ignored filter
- **WHEN** a widget ignores the filter "Municipality", set to `Cali`, and a member exports its workbook
- **THEN** the data sheet lists every municipality, and the "Summary" sheet lists "Municipality" under the filters that do not narrow the widget

#### Scenario: Cleared default
- **WHEN** the filter "Round" has the default 3 and a member cleared it before exporting
- **THEN** the workbook holds the rows of every round

#### Scenario: Widget of a version
- **WHEN** an `analyst` exports the workbook of a widget of version 9 of "Housing" that has since been removed
- **THEN** the workbook holds that widget's rows with today's data, and its "Summary" sheet names version 9

### Requirement: Sheets of a widget workbook
A widget's workbook SHALL have two sheets. The first SHALL be named after the widget's title, within Excel's rules, and hold the widget's table with the columns and rows of that widget's table in a scheduled email's workbook (see `dashboard-reports` "Widget tables in a scheduled email"). A stat tile's sheet SHALL hold one column and one row with its value. The second SHALL be "Summary".

#### Scenario: Stacked columns
- **WHEN** a member exports the workbook of a stacked column chart grouped by `municipality` with the series `water_source`
- **THEN** its first sheet lists each municipality with one column per water source, a "Total" column and a "Total" row

#### Scenario: Data sheet first
- **WHEN** a member opens the workbook of "Coverage by municipality"
- **THEN** its sheets are "Coverage by municipality" and then "Summary"

#### Scenario: Long title
- **WHEN** a widget's title has 60 characters, one of them `/`
- **THEN** its data sheet's name is shortened to 31 characters, without the `/`

#### Scenario: Stat tile
- **WHEN** a member exports the workbook of the stat tile "Households visited", whose value is 1,234
- **THEN** its first sheet has one header naming what is plotted and its unit, and one cell holding the number 1234

#### Scenario: Map
- **WHEN** a member exports the workbook of a map of blocks with the tooltip columns "Dirección" and "Meta muestral"
- **THEN** the data sheet lists each block, its plotted value, its address and its sample target, and no shape kind or geometry

### Requirement: Summary of a widget workbook
The "Summary" sheet SHALL list the project, the dashboard, the version when one is shown and the dashboard's source (the version's while a version is shown), the widget's title and subtitle, its indicator, what it plots, each filter of the dashboard with its value in force or "All", the filters that do not narrow the widget, when the indicator's table was published, when the workbook was made in the project's time zone, the number of rows, and the widget's footnote and notes.

#### Scenario: Summary
- **WHEN** a member of "Census 2026" exports, with "Municipality" set to `Cali`, the workbook of a widget whose query reached the row limit
- **THEN** its "Summary" sheet names "Census 2026", "Housing", the widget, its indicator, "Municipality" with `Cali`, the publication time, the export time naming `America/Bogota`, the number of rows, and the row-limit note

#### Scenario: Dashboard without filters
- **WHEN** a member exports the workbook of a widget of a dashboard with no filters
- **THEN** its "Summary" sheet says that no filter applies

#### Scenario: Widget texts in the summary
- **WHEN** a member exports the workbook of "2.4. En qué lugares" of "Teatro", which has a subtitle, a footnote and the caption text "% de quienes asistieron", and "Teatro" has a source
- **THEN** its "Summary" sheet lists the source, the subtitle, "% de quienes asistieron" as what it plots, and the footnote

#### Scenario: No texts in the summary
- **WHEN** a member exports the workbook of a widget with no subtitle or footnote, on a dashboard with no source
- **THEN** its "Summary" sheet has no line for a source, a subtitle or a footnote

### Requirement: Numbers in a widget workbook
The data sheet's numbers SHALL be numbers in the formats of a scheduled email's workbook (see `dashboard-reports` "Numbers in the workbook"), and a stat tile's value SHALL follow the indicator's format. Text SHALL never be written as a formula, and a cell longer than 32,767 characters SHALL be cut to that length.

#### Scenario: Percentage cell
- **WHEN** a column chart over `piped_water_rate`, whose format is `percent`, has the value 0.42 for `Cali`
- **THEN** its cell in the workbook holds the number 0.42, shown as a percentage

#### Scenario: Median of a count
- **WHEN** a box plot over `household_size`, whose format is `count`, has a box whose median is 4.5
- **THEN** that box's median cell holds the number 4.5, shown with two decimals

#### Scenario: Text that looks like a formula
- **WHEN** a group value is `=1+1`
- **THEN** its cell holds the text `=1+1`, not a formula

#### Scenario: Long text
- **WHEN** a tooltip value of a map has 40,000 characters
- **THEN** its cell holds its first 32,767 characters

### Requirement: Language of widget files
An image's own words, such as its notes and footer, SHALL be in the interface language, and its numbers SHALL read as on screen. A workbook's own words, such as "Summary", "Total", "(none)", "Other" and its summary items, and its dates and range texts, SHALL follow the member's interface language and regional format. Names, labels and data values SHALL stay as they were created.

#### Scenario: Spanish member
- **WHEN** a member whose UI is in Spanish with `es-CO` exports the workbook of a stacked column chart titled "Coverage by municipality"
- **THEN** its sheets are "Coverage by municipality" and "Resumen", and its total column reads "Total"

#### Scenario: Spanish image
- **WHEN** that member exports the chart as a PNG image
- **THEN** its footer is in Spanish, and its values read as on the member's screen, such as `12.345,5`

### Requirement: Data newer than the page
When the indicator's table was republished after the member's page read it, the workbook SHALL hold the newly published rows, and the success message SHALL say when they were published and that reloading the page shows them.

#### Scenario: Run finished after the page loaded
- **WHEN** a run republishes `coverage` at 10:41 after a member opened "Housing", and the member then exports the workbook of a widget over `coverage`
- **THEN** the workbook holds the new rows, and the message says that it holds data published at 10:41, after the page was loaded, and that reloading shows it

### Requirement: Widget workbook download
A widget's workbook SHALL be offered through a single-use download link that expires a few minutes after it was prepared (see `auth` "Single-use download links"), and the prepared file SHALL be deleted once downloaded or expired. A workbook larger than the deployment's export file size limit SHALL be refused with a message naming the limit and leave no file behind.

#### Scenario: Link used twice
- **WHEN** a widget's workbook link is opened a second time
- **THEN** the API responds 404 and serves nothing

#### Scenario: Too large
- **WHEN** a widget's workbook grows past the deployment's export file size limit
- **THEN** the export fails with a message naming the limit, and no file is left

### Requirement: Refused widget workbooks
The API SHALL refuse a workbook, with a message, when a filter the screen sent is not a filter of the dashboard or of the version shown with the same kind, when the widget's table lacks a column it needs, when its indicator is not published, and when reading it takes longer than the time limit. The message for changed filters SHALL say to reload the dashboard.

#### Scenario: Filters changed since the page was opened
- **WHEN** an `analyst` removes the filter "Round" from "Housing" while a member's page still shows it, and the member exports a widget's workbook
- **THEN** the API responds 409, and the member reads that the dashboard's filters changed and that reloading the dashboard lets them export

#### Scenario: Column gone
- **WHEN** a run publishes `coverage` without the column `municipality` that the widget groups by, before a member's export
- **THEN** the API responds 422 with a message naming `municipality`

#### Scenario: Read too long
- **WHEN** reading a widget's rows takes longer than the time limit
- **THEN** the export fails with a message saying it took too long, and other members' requests are not stopped by it

### Requirement: Widget export file names
A widget's file SHALL be named with the project, the dashboard and the widget's title, each in lower-case ASCII with dashes, then the version shown, then the project's day as `YYYY-MM-DD`. An empty part SHALL read "dashboard" or "widget".

#### Scenario: File name
- **WHEN** a member of "Census 2026", in `America/Bogota`, exports "Coverage by municipality" of "Housing" as a PNG image on November 2, 2026, local time
- **THEN** the file is named `census-2026_housing_coverage-by-municipality_2026-11-02.png`

#### Scenario: Version in the name
- **WHEN** an `analyst` exports the workbook of that widget while version 9 is shown
- **THEN** the file is named `census-2026_housing_coverage-by-municipality_v9_2026-11-02.xlsx`

#### Scenario: Accents and symbols
- **WHEN** a widget is titled "Cobertura / Población rural"
- **THEN** its part of the file name reads `cobertura-poblacion-rural`

#### Scenario: Title without letters
- **WHEN** a widget's title holds only emoji
- **THEN** its part of the file name reads `widget`

### Requirement: Roles for widget exports
Every member of the project, `viewer`, `analyst` and `admin`, SHALL be able to export a widget's images and its workbook. Exporting a widget of a version SHALL be refused to a `viewer`, who cannot view versions. A user who is not a member SHALL get no file.

#### Scenario: Every role
- **WHEN** a `viewer`, an `analyst` and an `admin` each ask the API for the workbook of a widget of "Housing"
- **THEN** each gets a download link

#### Scenario: Not a member
- **WHEN** a user who is not a member of "Census 2026" asks the API for a widget's workbook
- **THEN** the API responds 404 and issues no link

#### Scenario: Viewer asks for a version
- **WHEN** a `viewer` asks the API for the workbook of a widget of version 9 of "Housing"
- **THEN** the API responds 403 and issues no link

### Requirement: Column labels in widget files
An image of a widget SHALL name its columns as the screen does, in its caption and its table. A widget's workbook SHALL name them as a scheduled email's workbook does, with the widget's own labels as the member's screen sent them and its indicator's labels as the current version of its script gives them, and its "Summary" sheet SHALL say what the widget plots with those names.

#### Scenario: Image caption
- **WHEN** a member exports as a PNG image a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy` grouped by `comuna`, whose indicator labels them "Hogares que asistieron", "Hogares" and "Comuna"
- **THEN** the image's caption reads "Teatro hoy: Hogares que asistieron / Hogares by Comuna"

#### Scenario: Widget's own label in the workbook
- **WHEN** a member exports the workbook of that chart, whose own label for `denominator` is "Hogares de la base E1"
- **THEN** the data sheet's value header and the "Summary" sheet's line of what it plots read "Teatro hoy: Hogares que asistieron / Hogares de la base E1"

#### Scenario: Widget of a version
- **WHEN** an `analyst` exports the workbook of a widget of version 9 of "Teatro", whose own labels differ from the widget's labels today
- **THEN** the workbook names the columns with version 9's labels

### Requirement: Widget context in a widget workbook
A widget's workbook SHALL be prepared from the widget's saved settings as the page shows them, not only from its query. Its value column's header and its "Summary" line of what it plots SHALL use the widget's own caption text when it has one, and still name what it plots when its caption is hidden; the header SHALL name the widget's own unit, and a map's tooltip columns SHALL be headed by their labels and written in their formats, as in a scheduled email's workbook.

#### Scenario: Own text and unit in the header
- **WHEN** a member exports the workbook of the column chart "2.2" over `teatro_hoy`, whose caption text is "% de quienes asistieron" and whose unit is "de quienes asistieron"
- **THEN** its value column is headed "% de quienes asistieron" and names the unit "de quienes asistieron"

#### Scenario: Map tooltip labels
- **WHEN** a member exports the workbook of a map whose tooltip column `meta` has the label "Meta muestral" and the format `count`
- **THEN** that column of the data sheet is headed "Meta muestral" and holds whole numbers, as in the dashboard's email

#### Scenario: Hidden caption in the workbook
- **WHEN** a member exports the workbook of a column chart that hides its caption
- **THEN** its value column is still headed by what the widget plots

### Requirement: Chart annotations in widget images
An image of a bar, column, line or pie widget SHALL show its reference lines with the labels drawn at them, its steps and its data labels as the member sees them. Its legend SHALL list each reference line drawn with its dash pattern, label and formatted value, after the series of a widget with a series. An image of such a widget shown as a table SHALL list the lines under the table. The lines SHALL come before the footnote. An image of a widget without them SHALL have the parts it has today.

#### Scenario: Mean line in an image
- **WHEN** a member exports as a PNG image the column chart "2.1. Asistió, por comuna" over `teatro_hoy`, which has a `mean` line at 34.17%
- **THEN** the image shows the dashed line across the columns, and its legend reads "Mean: 34.17% de los hogares" next to a dashed swatch

#### Scenario: Data labels in an image
- **WHEN** a member exports as an SVG image a bar chart with data labels
- **THEN** the image shows each label the chart draws on screen, and no label the chart left out

#### Scenario: Shown as a table
- **WHEN** a member shows a column chart of a `percent` indicator with no unit and an `overall` line at 45.99% as a table, and exports it as a PNG image
- **THEN** the image shows the table and, under it, "Overall: 45.99%"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image the column chart "2.1. Asistió, por comuna", which has a footnote and a `mean` line
- **THEN** the image shows the legend with the line under the columns, the footnote under the legend, and the footer under the footnote

### Requirement: Reference lines in a widget workbook
The "Summary" sheet of the workbook of a widget with reference lines SHALL list each line drawn, with its label and its value as the widget's values are written, in the order the widget lists them, after what the widget plots. The data sheet SHALL keep the widget's table, with no row or column for the lines.

#### Scenario: Lines in the summary
- **WHEN** a member exports the workbook of a column chart of a `percent` indicator with no unit, with a `mean` line at 34.17% and a `value` line of 0.5 labelled "Meta"
- **THEN** its "Summary" sheet lists the reference lines as "Mean: 34.17%" and "Meta: 50%", and its data sheet lists the comunas as before

#### Scenario: Widget without reference lines
- **WHEN** a member exports the workbook of a column chart without reference lines
- **THEN** its "Summary" sheet has no reference line item

### Requirement: Declared category order in widget files
A widget's PNG or SVG image SHALL show its values in the order and with the text the member sees on screen. A widget's workbook SHALL list its groups and series in that order and with that text, from the widget's settings as the member's page shows them, its own order included, and from its indicator's current column metadata.

#### Scenario: Image in the declared order
- **WHEN** a member exports as a PNG image a column chart whose series follow a declared order with prefixes hidden
- **THEN** the image shows the segments and the legend in that order, without the prefixes

#### Scenario: Workbook in the widget's order
- **WHEN** a member exports the workbook of a widget with its own order of series
- **THEN** the data sheet's series columns follow that order

#### Scenario: Workbook of a version
- **WHEN** an `analyst` exports the workbook of a widget of version 9 of "2. Teatro", where that widget had its own order of groups that it no longer has
- **THEN** the data sheet lists the groups in the order the widget had in version 9

### Requirement: Base n and small bases in widget images
A widget's PNG or SVG image SHALL draw its marks dimmed as the screen does, and SHALL show under its chart, before its footnote, the small-base note, the base n line and the no-data line that the widget shows.

#### Scenario: Dimmed columns in an image
- **WHEN** a member exports as a PNG image "2.1. Asistió, por comuna", whose `COMUNA 7` and `SIN NOMBRE` columns are dimmed
- **THEN** the image shows those two columns lighter with their dashed outline, and the note that marks with a base below 30 are dimmed

#### Scenario: Lines in an image
- **WHEN** a member exports as an SVG image a widget that shows "n = 223 Hogares que asistieron" and "No sabe: 2.24%" under its chart
- **THEN** the image shows both lines under its chart

#### Scenario: Lines before the footnote in an image
- **WHEN** a member exports as a PNG image "2.4. En qué lugares", which shows "n = 223 Hogares que asistieron" and has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the image shows the n line under the chart and the footnote under the n line, above the footer

### Requirement: Base n and small bases in a widget workbook
A widget's workbook SHALL apply the base n line and the small-base threshold that the member's screen applies to the widget, which the page sends with the request, and SHALL leave out no-data values as the widget does. Its data sheet and "Summary" SHALL then be the widget's table and notes in a scheduled email. A request without base settings SHALL give the workbook given before they existed.

#### Scenario: Threshold in force on screen
- **WHEN** a member exports the workbook of "2.1. Asistió, por comuna" on "Teatro", whose threshold is 30
- **THEN** its data sheet has an n column, and its "Summary" says that 2 values have a base below 30

#### Scenario: Version without a threshold
- **WHEN** "Teatro" has the threshold 30 now and an `analyst` exports a widget while its version 9, which had none, is shown
- **THEN** the workbook has no n column and no small-base note

#### Scenario: Request without base settings
- **WHEN** a page from a release before base settings asks for a widget's workbook
- **THEN** the workbook is the one that release gave

### Requirement: Stat tile in a widget image
An image of a stat tile SHALL draw its value at the size its tile gives it on screen, at most 30 px and at least 18 px, broken only between words; a word wider than the image SHALL widen the image instead of being cut. Under the value it SHALL show what the tile plots, naming its statistic, unless the widget hides its caption, then the secondary lines the tile shows at that moment, as the tile words them, before the notes, the footnote and the footer.

#### Scenario: Smaller value
- **WHEN** a member exports the stat tile `46.85% de los hogares`, 3 columns wide, as an SVG image
- **THEN** the image draws its value at no more than 30 px, not at 36 px, with its title and what it plots

#### Scenario: Secondary lines in the image
- **WHEN** a member exports as a PNG image a stat tile that shows `n = 476 Hogares` and `Without filters: 46.85% de los hogares` under the dashboard filter "Comuna"
- **THEN** the image shows both lines under what the tile plots

#### Scenario: Statistic in the image
- **WHEN** a member exports a stat tile with the statistic median
- **THEN** what the image says the tile plots starts with "Median of"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image a stat tile that has a footnote and shows `n = 476 Hogares`
- **THEN** the image shows the value, what the tile plots, the n line, the footnote and the footer, in that order

#### Scenario: Long figure
- **WHEN** a member exports a stat tile 1 column wide that shows `1,016,279 pesos`
- **THEN** the image shows `1,016,279` whole, on one line, and is as wide as it needs

### Requirement: Stat tile lines in a widget workbook
A stat tile's widget workbook SHALL keep one column and one row holding its value on its data sheet. Its "Summary" sheet SHALL name the tile's statistic in what it plots, and SHALL have one more line for each secondary line the tile shows, after what it plots, with the same text as the tile.

#### Scenario: Median tile's workbook
- **WHEN** a member exports the workbook of a stat tile with the statistic median
- **THEN** its data sheet holds one number, the median, and what its "Summary" sheet says the tile plots starts with "Median of"

#### Scenario: Secondary lines in the Summary
- **WHEN** a member exports, with "Comuna" set to `COMUNA 1`, the workbook of a stat tile that shows both secondary lines
- **THEN** its "Summary" sheet has a line with the tile's n and a line with its value without the dashboard filters, as the tile shows them
