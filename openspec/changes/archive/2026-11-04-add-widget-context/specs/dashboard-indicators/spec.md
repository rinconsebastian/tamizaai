# Spec Delta

## MODIFIED Requirements

### Requirement: Indicator formatting
A widget SHALL show its plotted values with the format and unit of its indicator, the widget's own unit taking the place of the indicator's when the widget has one (see "Widget unit"): `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value. The same SHALL apply to a stat tile, axes, legends, tooltips and "Show as table"; a map's tooltip columns use their own format. A box plot over a `count` indicator SHALL show its figures as `decimal` does, with two decimals and the unit, because its quartiles, medians and means can fall between whole numbers; a box plot's numbers of values and of outliers SHALL be whole numbers whatever the indicator's format.

#### Scenario: Rate shown as a percentage
- **WHEN** the indicator `piped_water_rate` has the format `percent` and a stat tile shows the value 0.42
- **THEN** the tile shows `42%`

#### Scenario: Count with a unit
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`, and a column chart shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households`

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over the indicator `survey_progress`, whose format is `percent`, shows the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** the tooltip reads the plotted value as a percentage and the target as `20`, not `2,000%`

#### Scenario: Median of a count
- **WHEN** a box plot over the indicator `household_size`, whose format is `count` and unit `people`, has a box of 1,200 values whose median is 4.5
- **THEN** the box's median reads `4.50 people` and its number of values reads `1,200`

#### Scenario: Widget's own unit
- **WHEN** a column chart over `households_visited`, whose format is `count` and unit `households`, has the unit `households with children` of its own and shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households with children`

### Requirement: Indicators dashboard and its indicators
Every project member SHALL be able to open any dashboard of the project, which SHALL list the project's indicators with their label, unit and format and whether each has published a table. The dashboard's widgets SHALL come first on the page, followed by the dashboard's source when it has one (see `custom-dashboards` "Dashboard source shown"); the list of indicators MAY follow them inside a disclosure that is collapsed once any indicator has published and opens on demand. While no indicator has published, every dashboard SHALL say so, with instructions, SHALL show the list of indicators expanded as part of that message, and SHALL still list the widgets already configured on it. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a published dashboard
- **WHEN** a `viewer` opens a dashboard of a project where at least one indicator has published
- **THEN** the dashboard's widgets are shown

#### Scenario: Indicator list behind a disclosure
- **WHEN** a member opens a dashboard of a project with 3 indicators, 2 of them published
- **THEN** the widgets come first, a collapsed disclosure reads "Indicators (3) · 2 published", and opening it lists the three indicators with label, unit, format and whether each has published

#### Scenario: No indicator has published
- **WHEN** none of a project's indicators has published, or the project has none
- **THEN** each dashboard says no indicator has published yet, with instructions and the list of indicators expanded, and still lists the widgets configured on it

#### Scenario: Same indicators on every dashboard
- **WHEN** a member opens the dashboard "Health" and then the dashboard "Field" of the same project
- **THEN** both list the same indicators, each with its export

#### Scenario: Link to the scripts from any dashboard
- **WHEN** no indicator has published and an `analyst` follows the link to the scripts from any dashboard
- **THEN** the project's Scripts section opens

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its indicators or one of its dashboards
- **THEN** the API responds 404

#### Scenario: Source after the widgets
- **WHEN** a member opens "Teatro", whose source is "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas"
- **THEN** the source is shown under the widgets and before the list of indicators

### Requirement: Stat tile
A stat tile SHALL show one number for its indicator: the measure summed over every row the query matches, or the ratio of sums, formatted with the indicator's format and unit, the widget's own unit taking the place of the indicator's when the widget has one (see "Widget unit").

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `households_visited` with no filters and the measure `value`
- **THEN** it shows one number, the sum of `value` over every row

#### Scenario: Stat tile with its own unit
- **WHEN** the stat tile "Centro cultural o casa de la cultura" over `teatro_hoy`, whose format is `percent` and unit "de los hogares", has the unit "de quienes asistieron" and the value 0.2422
- **THEN** it shows "24.22% de quienes asistieron" in the English interface

### Requirement: Map tooltip
Pointing at a shape, tapping it, or reaching it from the keyboard SHALL show its tooltip. The tooltip SHALL show the group value as its heading, then the plotted value with the indicator's label, format and unit, the widget's own caption text and unit taking the place of the indicator's label and unit when the widget has them (see "Widget caption text" and "Widget unit"), then the widget's tooltip columns in their order, each with its label. Its text, labels included, SHALL be shown as text, never as markup.

#### Scenario: City block
- **WHEN** a member points at block `0042` on a map of the indicator `survey_progress` (label "Survey progress", format `percent`) whose tooltip columns are `address` labelled "Dirección", `sample_target` labelled "Meta muestral" and `achieved` labelled "Encuestas logradas", and the block's row has `Cra 5 # 10-20`, 20, 9 and a value of 0.45
- **THEN** the tooltip reads `0042`, `Survey progress: 45%`, `Dirección: Cra 5 # 10-20`, `Meta muestral: 20` and `Encuestas logradas: 9`

#### Scenario: Markup in a value
- **WHEN** a tooltip column holds the text `<img src=x onerror=alert(1)>`, or a tooltip column's label is that text
- **THEN** the tooltip shows those characters as text, and nothing runs

#### Scenario: Own caption text and unit in a map tooltip
- **WHEN** the same map has the caption text "Avance de la muestra" and the unit "de la meta", and a member points at block `0042`
- **THEN** the tooltip's line of the plotted value reads `Avance de la muestra: 45% de la meta`, and the tooltip columns read as before

## ADDED Requirements

### Requirement: Widget subtitle
A widget of any chart type SHALL be able to have a subtitle of 1 to 200 characters, such as the question its chart answers, shown under its title in muted text to every member. The subtitle SHALL NOT change the widget's rows. A widget without a subtitle SHALL show none.

#### Scenario: Subtitle under the title
- **WHEN** an `analyst` gives the bar chart "2.4. En qué lugares" of "Teatro" the subtitle "¿En qué lugares ha visto teatro en el último año? (E4)"
- **THEN** every member who opens "Teatro" sees that subtitle under the chart's title

#### Scenario: Subtitle of a stat tile
- **WHEN** the stat tile "Centro cultural o casa de la cultura" has the subtitle "% de quienes asistieron (E4)"
- **THEN** the subtitle shows under the tile's title, above its figure

### Requirement: Widget footnote
A widget of any chart type SHALL be able to have a footnote of 1 to 500 characters, shown at its foot under its chart, its table, or its message that no row matches. The footnote SHALL NOT change the widget's rows. A widget without a footnote SHALL show none.

#### Scenario: Footnote under the chart
- **WHEN** the bar chart "2.4. En qué lugares" has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the footnote shows under its bars

#### Scenario: Footnote under the table
- **WHEN** a member shows that bar chart as a table
- **THEN** the footnote shows under the table

### Requirement: Widget caption text
A widget SHALL be able to name what it plots with its own text of 1 to 200 characters, which replaces the indicator's label and its measure or ratio wherever the widget names what it plots: the line above a chart with no series and above a pie, map, heat map or cross table, a stat tile's label, the tooltip line of the plotted value, the value column of "Show as table", and the chart's accessible name. The rest of each line SHALL stay.

#### Scenario: Own text above the bars
- **WHEN** a bar chart over the ratio of `numerator` to `denominator` of `teatro_hoy`, grouped by `categoria`, has the caption text "% de quienes asistieron", in the English interface
- **THEN** the line above its bars reads "% de quienes asistieron by categoria"

#### Scenario: Own text in the table
- **WHEN** a member shows that bar chart as a table
- **THEN** its value column is headed "% de quienes asistieron"

#### Scenario: Own text on a stat tile
- **WHEN** the stat tile "Centro cultural o casa de la cultura" over `teatro_hoy` has the caption text "de quienes asistieron"
- **THEN** the label under its figure reads "de quienes asistieron"

#### Scenario: Series legend kept
- **WHEN** a bar chart of `teatro_conteo` by `comuna`, stacked to 100% with the series `respuesta`, has the caption text "Iría al teatro (I1)"
- **THEN** its legend still lists "1. Sí", "2. Tal vez", "3. No" and "4. No sabe", and each mark's tooltip names its value "Iría al teatro (I1)"

### Requirement: Widget caption hidden
A widget SHALL be able to hide the line that names what it plots: the line above a chart with no series and above a pie, map, heat map or cross table, and a stat tile's label. Its accessible name, its tooltips, its "Show as table" view and the value columns of its email and workbook tables SHALL still name what is plotted. A series legend SHALL NOT be hidden by it.

#### Scenario: Hidden above the columns
- **WHEN** the column chart "2.1. Asistió, por comuna" hides its caption
- **THEN** no line shows above its columns, and a screen reader still reads what the chart plots

#### Scenario: Hidden on a stat tile
- **WHEN** the stat tile "Fuera de Duitama" hides its caption
- **THEN** it shows its title and its figure, with no label under the figure

#### Scenario: Table header kept
- **WHEN** a member shows the column chart "2.1. Asistió, por comuna" as a table
- **THEN** its value column still names what is plotted

#### Scenario: Legend kept
- **WHEN** a stacked bar chart with the series `barrera_tipo` hides its caption
- **THEN** its legend of "Acceso", "Interés" and "Otra" is still shown

### Requirement: Widget unit
A widget SHALL be able to have its own unit of 1 to 32 characters, which replaces its indicator's unit wherever the widget writes a value with its unit: its chart, tooltips, "Show as table", images, the scheduled email, both workbooks, and the values MCP formats for it. The indicator's format SHALL still apply, and the indicator's other widgets SHALL keep its unit.

#### Scenario: Base of a subgroup
- **WHEN** the column chart "2.2" over `teatro_hoy`, whose format is `percent` and unit "de los hogares", has the unit "de quienes asistieron", and one of its columns is 0.1121
- **THEN** that column's value reads "11.21% de quienes asistieron" in the English interface

#### Scenario: Other widgets keep the indicator's unit
- **WHEN** another widget of "Teatro" plots `teatro_hoy` and has no unit of its own
- **THEN** its values end with "de los hogares"

#### Scenario: Unit of a box plot
- **WHEN** a box plot over `teatro_precio_hogar`, whose format is `count` and unit "pesos", has the unit "pesos por boleta" and a median of 40,000
- **THEN** its median reads "40,000.00 pesos por boleta" in the English interface

### Requirement: Widget indicator description
A widget SHALL be able to offer its indicator's description: with the option on and an indicator that has a description, its header SHALL have a button named after the indicator that opens the description next to it and closes it again, from the pointer or the keyboard. Without the option, or when the indicator has no description, there SHALL be no button. The description SHALL NOT be printed or exported.

#### Scenario: Description opened
- **WHEN** a `viewer` opens "Teatro", whose bar chart "2.4. En qué lugares" offers the description of `teatro_hoy`, and chooses the chart's description button
- **THEN** the description of `teatro_hoy` opens next to the button

#### Scenario: Closed with Escape
- **WHEN** a keyboard user opens that description and presses Escape
- **THEN** the description closes and focus is back on its button

#### Scenario: Indicator without a description
- **WHEN** a widget has the option on and its indicator has no description
- **THEN** its header has no description button

#### Scenario: Not printed or exported
- **WHEN** a member prints "Teatro" and exports "2.4. En qué lugares" as a PNG image
- **THEN** neither the sheet nor the image holds the description button or the description

### Requirement: Widget context validation
Saving a widget SHALL be refused, with an error on the field, when its subtitle passes 200 characters, its footnote 500, its caption text 200 or its unit 32, when one of these texts holds a line break or another control character, or when its caption has its own text and is also hidden. A text that is empty or only spaces SHALL count as not set, and texts SHALL be saved without surrounding spaces. Every chart type SHALL take them.

#### Scenario: Long subtitle
- **WHEN** a request saves a widget with a subtitle of 201 characters
- **THEN** the API responds 400 with an error on `config.display.subtitle` naming 200 characters, and nothing is saved

#### Scenario: Line break in a footnote
- **WHEN** a request saves a widget whose footnote holds a line break
- **THEN** the API responds 400 with an error on `config.display.footnote`, and nothing is saved

#### Scenario: Caption with a text and hidden
- **WHEN** a request saves a caption with the text "% de quienes asistieron" that is also hidden
- **THEN** the API responds 400 with an error on `config.display.caption`, and nothing is saved

#### Scenario: Long unit
- **WHEN** a request saves a widget with a unit of 33 characters
- **THEN** the API responds 400 with an error on `config.display.unit` naming 32 characters, and nothing is saved

#### Scenario: Blank subtitle
- **WHEN** a request saves the subtitle "   "
- **THEN** the widget is saved with no subtitle

#### Scenario: Every chart type
- **WHEN** a request saves a subtitle, a footnote, a caption text and a unit on a map, a stat tile and a Likert widget
- **THEN** each widget is saved with them

#### Scenario: Chart type changed
- **WHEN** an `analyst` turns a bar chart that has a subtitle and a unit into a pie chart
- **THEN** the pie chart is saved with the same subtitle and unit

### Requirement: Widget context in the widget form
The widget form SHALL let an `analyst` or `admin` set a widget's subtitle, footnote, caption (default, own text or hidden), unit, with the indicator's unit given as a hint, and whether the description button shows, which SHALL be unavailable, saying why, when the indicator has no description. Changing any of them SHALL redraw the preview without querying the indicator again. An error from the API SHALL show on its field.

#### Scenario: Preview without a query
- **WHEN** an `analyst` types a footnote and hides the caption in the widget form
- **THEN** the preview shows the footnote and no caption line, and no new query is sent

#### Scenario: Unit hint
- **WHEN** an `analyst` edits the unit of a widget over `teatro_hoy`
- **THEN** the unit field says that the indicator's unit is "de los hogares"

#### Scenario: Description option unavailable
- **WHEN** the widget's indicator has no description
- **THEN** the option to show the description is unavailable and says that the indicator has none

#### Scenario: Saved option on an indicator that lost its description
- **WHEN** a widget saved with the description option on is edited after its indicator lost its description
- **THEN** the option stays available, still checked, so that it can be turned off

#### Scenario: Error on its field
- **WHEN** the API refuses a footnote of 501 characters
- **THEN** the error shows on the footnote field

### Requirement: Widget context in a tile
A widget's tile SHALL keep its size: its subtitle and footnote SHALL take room from its plot. On screen each SHALL show at most two lines, or one on a tile two rows high, a cut text ending with an ellipsis, read whole by screen readers and shown whole on pointing. A chart that does not fit its room SHALL be cut at its own box, never over the texts, and a stat tile short of room SHALL keep its figure whole and cut its label.

#### Scenario: Long subtitle on a column chart
- **WHEN** a column chart 6 columns wide and 4 rows high has a subtitle of 180 characters that needs three lines
- **THEN** its tile is as tall as before, the subtitle shows two lines ending with an ellipsis, and the columns are drawn in the room left

#### Scenario: Stat tile two rows high
- **WHEN** a stat tile 3 columns wide and 2 rows high has a subtitle and a footnote
- **THEN** its tile is as tall as before, each text shows on one line, and its figure shows whole between them

#### Scenario: Whole text on pointing
- **WHEN** a member points at a cut subtitle
- **THEN** its whole text is shown

### Requirement: Widget context warning in the widget form
The widget form SHALL warn, without refusing the save, when a widget whose tile is two rows high or less has a subtitle or a footnote, or when its subtitle and footnote printed whole would fill half of its tile. The warning SHALL say that the texts take room from the chart or the figure and that a taller tile shows more of them.

#### Scenario: Warning in the form
- **WHEN** an `analyst` adds a footnote to a stat tile 2 rows high
- **THEN** the form says that the texts take room from the figure and that a taller tile shows more of them

#### Scenario: Warning for long texts
- **WHEN** an `analyst` gives a column chart 4 columns wide and 3 rows high a footnote of 500 characters
- **THEN** the form says that the texts take room from the chart and that a taller tile shows more of them

#### Scenario: No warning on a tall tile
- **WHEN** an `analyst` gives a column chart 6 columns wide and 4 rows high a subtitle of 80 characters
- **THEN** the form shows no warning

### Requirement: Widget context kept with the widget
A widget's subtitle, footnote, caption, unit and description option SHALL be saved with its other display settings: a dashboard version SHALL hold them, a restore SHALL bring them back, a duplicated dashboard's widgets SHALL have them, and a widget moved to another dashboard SHALL keep them. Changing one of them SHALL make a version, and saving the widget form unchanged SHALL make none.

#### Scenario: Subtitle restored
- **WHEN** an `analyst` adds a subtitle to a widget of "Teatro" and then restores the version before that change
- **THEN** the widget has no subtitle again, and the restore is a new version

#### Scenario: Copied with the dashboard
- **WHEN** an `analyst` duplicates "Teatro", whose bar chart "2.4. En qué lugares" has a subtitle, a footnote and a hidden caption
- **THEN** that bar chart in the copy has the same subtitle and footnote, and its caption is hidden

#### Scenario: Moved widget
- **WHEN** an `analyst` moves that bar chart to "Segmentos"
- **THEN** it keeps its subtitle, footnote, caption, unit and description button

#### Scenario: Saved unchanged
- **WHEN** an `analyst` opens a widget that has a subtitle and saves the form without changing anything
- **THEN** the dashboard has no new version

#### Scenario: Unit changed
- **WHEN** an `analyst` changes only the unit of a widget of "Teatro"
- **THEN** "Teatro" has a new version saying the widget changed

### Requirement: Widget context left unset
A widget with no subtitle, footnote, caption setting, unit or description option SHALL look, print, export and be sent exactly as before these settings existed, and its stored settings SHALL stay byte for byte as they were saved, so its dashboard makes no version until someone changes it.

#### Scenario: Widgets saved before
- **WHEN** a member opens "Teatro", none of whose 44 widgets has any of these settings
- **THEN** every widget shows the same title, caption, values and units as before

#### Scenario: Form saved unchanged before
- **WHEN** an `analyst` opens a column chart in color 2 saved before these settings existed and saves the form without changing anything
- **THEN** its stored settings are unchanged and the dashboard has no new version
