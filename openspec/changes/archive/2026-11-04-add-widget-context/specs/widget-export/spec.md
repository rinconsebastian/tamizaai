# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
