# Spec Delta

## MODIFIED Requirements

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie, map, heat map, population pyramid, diverging Likert and cross table widgets), an optional series column (see "Series in a widget"), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value, and the same series value when there is a series, SHALL be summed, or combined as a ratio of sums, before being plotted, except in a box plot, where each row gives one value of its group's box (see "Box plot"). The row limit SHALL count what the widget plots: its groups, its pairs of group and series value when it has a series, the rows of a histogram with no group column, or the boxes of a box plot. On a dashboard, the rows SHALL also match the dashboard filters that narrow the widget (see `dashboard-filters`); those filters do not count toward the widget's 50.

For each value and each total it plots, the query SHALL also return its base n (see "Base n of a mark") and, for a ratio, the sums of its numerator and of its denominator; a measure whose indicator declares no base column SHALL return neither, and the rows of a histogram and the boxes of a box plot SHALL be returned as before. A widget that leaves out the values that mean no data SHALL leave their rows out of every value, total and count it returns (see "No-data values left out of a widget").

#### Scenario: Default measure
- **WHEN** a widget is created over an indicator whose table has the columns `municipality`, `value` and `count`, without choosing a measure
- **THEN** it plots `value`

#### Scenario: Duplicate rows summed
- **WHEN** two rows of the indicator `households_visited` share the municipality `Cali`, with values 40 and 35, and a widget sums `value` grouped by `municipality`
- **THEN** the widget plots 75 for `Cali`

#### Scenario: Rows that share a group and a series summed
- **WHEN** three rows of `households_by_source` have `municipality` `Cali` and `water_source` `Piped`, with values 10, 20 and 5, and a stacked column chart sums `value` grouped by `municipality` with the series `water_source`
- **THEN** the `Piped` segment of `Cali` is 35

#### Scenario: Row limit
- **WHEN** a widget's query matches more rows than the configured limit
- **THEN** the widget shows that the limit was reached instead of silently showing only some of the rows

#### Scenario: Dashboard filter before the sum
- **WHEN** the indicator `households_visited` has rows for `Cali` with `sex` `female` and value 40 and `sex` `male` and value 35, a widget sums `value` grouped by `municipality`, and a member sets the dashboard filter on `sex` to `female`
- **THEN** the widget plots 40 for `Cali`

#### Scenario: Widget with 50 filters on a filtered dashboard
- **WHEN** a widget has 50 filters of its own and a member sets a dashboard filter that narrows it
- **THEN** the widget shows its rows narrowed by all 51 filters

#### Scenario: Box plot rows not summed
- **WHEN** two rows of the indicator `household_income` share the municipality `Cali`, with values 400 and 900, and a box plot plots `value` grouped by `municipality`
- **THEN** `Cali`'s box holds the two values 400 and 900, not their sum

#### Scenario: Numerator and denominator returned
- **WHEN** a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy`, grouped by `comuna` and filtered to the variable "Asistió (E1)" and the answer "Sí", plots 0.5 for `COMUNA 7`
- **THEN** the query also returns for `COMUNA 7` the numerator 8, the denominator 16 and the n 16

#### Scenario: Measure without a base answers as before
- **WHEN** a widget sums `value` of `teatro_conteo`, whose indicator declares no base column
- **THEN** its query returns no numerator, denominator or n, the same answer it gave before base figures existed

### Requirement: Bar and column charts of one indicator
A bar or column chart SHALL plot the group values of its indicator against the measure or ratio, one bar or column per group value, in the widget's order (see "Bar and column order"). With a series, each bar SHALL be split by series value as its layout says (see "Bars and columns by series").

Without a chosen order, bars SHALL follow the group values in the group column's own order:
- numbers from smallest to largest;
- dates and timestamps from earliest to latest;
- false before true;
- text in alphabetical order.

A group value that is an empty text is not a missing group value: its bar has a blank label and takes its alphabetical place, which is first. The bar for a missing group value, shown as "(none)", SHALL come last.

A bar chart SHALL draw its first bar at the top, and a column chart SHALL draw its first column on the left. The "Show as table" view SHALL list the groups in the same order as the chart.

Without a series, each bar SHALL have the tooltip and the keyboard access of "Mark tooltips of charts without a series", and a bar with a small base SHALL be drawn as "Small bases drawn dimmed" says.

#### Scenario: One indicator, several groups
- **WHEN** a column chart is configured for the indicator `coverage` grouped by `municipality`
- **THEN** it shows one column per municipality

#### Scenario: Default order
- **WHEN** a column chart without an order is grouped by the integer column `round`, whose values are 10, 2 and 1
- **THEN** its columns read 1, 2, 10 from left to right, and its "Show as table" view lists them in the same order

#### Scenario: Missing group value last
- **WHEN** a bar chart without an order is grouped by `municipality`, and some rows have no municipality
- **THEN** the bar shown as "(none)" comes last

#### Scenario: Column tooltip without a series
- **WHEN** a member points at the `COMUNA 7` column of "2.1. Asistió, por comuna", a column chart of `teatro_hoy` with no series
- **THEN** a tooltip reads `COMUNA 7`, its value, and its numerator and denominator under their names

### Requirement: Marks of a widget with a series
A widget with a series SHALL show a legend of its series in their order, and SHALL never rely on color alone: hovering or focusing a bar, segment, point or cell SHALL show its group, its series value and its value in the indicator's format, followed by its base figures (see "Base figures in tooltips") and, for a small base, the note of "Small bases in tooltips and tables". A legend entry SHALL show its series' color next to its name in text colors.

#### Scenario: Segment tooltip
- **WHEN** a member hovers the `Piped` segment of `Cali` in a stacked column chart
- **THEN** a tooltip reads `Cali`, `Piped` and its value with the indicator's unit

#### Scenario: Keyboard
- **WHEN** a member moves keyboard focus onto a heat map's cells
- **THEN** each focused cell shows the same tooltip as on hover

#### Scenario: Cell tooltip with its base
- **WHEN** a member hovers a column of `COMUNA 7` in a grouped column chart of `cruces_intencion` filtered to "Sí o tal vez", grouped by `comuna` with the series `categoria`, on a dashboard whose small-base threshold is 30
- **THEN** the tooltip reads `COMUNA 7`, the column's `categoria`, its value, its numerator, its denominator and "Small base: n = 16 (below 30)"

### Requirement: Map tooltip
Pointing at a shape, tapping it, or reaching it from the keyboard SHALL show its tooltip. The tooltip SHALL show the group value as its heading, then the plotted value with the indicator's label, format and unit, the widget's own caption text and unit taking the place of the indicator's label and unit when the widget has them (see "Widget caption text" and "Widget unit"), then the base figures of "Base figures in tooltips" that are not among the widget's tooltip columns, then the widget's tooltip columns in their order, each with its label, and for a small base the note of "Small bases in tooltips and tables". Its text, labels included, SHALL be shown as text, never as markup.

#### Scenario: City block
- **WHEN** a member points at block `0042` on a map of the indicator `survey_progress` (label "Survey progress", format `percent`) whose tooltip columns are `address` labelled "Dirección", `sample_target` labelled "Meta muestral" and `achieved` labelled "Encuestas logradas", and the block's row has `Cra 5 # 10-20`, 20, 9 and a value of 0.45
- **THEN** the tooltip reads `0042`, `Survey progress: 45%`, `Dirección: Cra 5 # 10-20`, `Meta muestral: 20` and `Encuestas logradas: 9`

#### Scenario: Markup in a value
- **WHEN** a tooltip column holds the text `<img src=x onerror=alert(1)>`, or a tooltip column's label is that text
- **THEN** the tooltip shows those characters as text, and nothing runs

#### Scenario: Own caption text and unit in a map tooltip
- **WHEN** the same map has the caption text "Avance de la muestra" and the unit "de la meta", and a member points at block `0042`
- **THEN** the tooltip's line of the plotted value reads `Avance de la muestra: 45% de la meta`, and the tooltip columns read as before

#### Scenario: Ratio columns already in the tooltip
- **WHEN** a map plots the ratio of `registros` to `meta` and shows `registros` and `meta` as tooltip columns
- **THEN** its tooltip shows each of them once, as a tooltip column, with no base figure line of its own

#### Scenario: Ratio of a block
- **WHEN** a map plots the ratio of `registros` to `meta` with no tooltip columns, and block `0042` has 9 of 20
- **THEN** its tooltip reads `0042`, the value `45%`, `registros: 9` and `meta: 20`, and no n line

## ADDED Requirements

### Requirement: Base n of a mark
Each plotted value SHALL have a base n: the sum of the indicator's base column over the value's rows when the indicator declares one (see `pipeline` "Base column of an indicator"), else, for a ratio, the sum of its denominator. A measure with neither SHALL have none, nor SHALL a histogram's bar or a pie's "Other" slice. A share in a 100% layout or a Likert bar SHALL have its group's n, and a box its number of values. Every n SHALL be unweighted and counted after all filters.

#### Scenario: Denominator as the base
- **WHEN** a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy` is grouped by `comuna`, filtered to "Asistió (E1)" and "Sí", and the denominators of `COMUNA 7` sum to 16
- **THEN** the n of `COMUNA 7` is 16

#### Scenario: Declared base column
- **WHEN** `comercio_gastos_resumen` declares `hogares` as its base column, and a column chart of its `value` grouped by `estrato` has one row for `Estrato 2` whose `hogares` is 154
- **THEN** the n of `Estrato 2` is 154

#### Scenario: Measure without a base
- **WHEN** a stacked bar chart sums `value` of `teatro_conteo`, which declares no base column
- **THEN** its segments have no n

#### Scenario: Share of a group
- **WHEN** a 100% column chart of `teatro_conteo`, which declares `value` as its base column, is grouped by `comuna` with the series `respuesta`, and the rows of `COMUNA 7` add up to 16 households
- **THEN** every segment of `COMUNA 7` has the n 16

#### Scenario: Dashboard filter before the n
- **WHEN** a member sets the dashboard filter "Estrato" to `Estrato 4` over a column chart of `teatro_hoy` grouped by `comuna`
- **THEN** each column's n counts only the rows of `Estrato 4`

### Requirement: Base n counted once across answers
When the indicator declares answer columns (see `pipeline` "Answer columns of an indicator"), an answer SHALL be one combination of their values, and an n over the rows of several answers SHALL count the base once: the largest of the sums of the base over the rows of each answer, not their total. The declaration SHALL change no plotted value, total or order.

#### Scenario: Total over the answers
- **WHEN** a grouped column chart of `teatro_hoy`, whose `categoria` is declared as answers, is grouped by `comuna` with the series `categoria` for "Frecuencia (E2)", and the denominators of each answer of `COMUNA 1` sum to 57
- **THEN** the total of `COMUNA 1` has the n 57, not 57 times the number of answers

#### Scenario: Two questions in one widget
- **WHEN** a grouped column chart of `comercio_lugar_compra`, whose `variable` and `categoria` are declared as answers, splits the places by `variable` "Ropa y calzado" and "Tecnología", each asked of the same 476 households
- **THEN** the widget's n is 476, not 952

#### Scenario: Values unchanged by the declaration
- **WHEN** an analyst declares `categoria` of `teatro_hoy` as answers
- **THEN** every widget over `teatro_hoy` plots the values, totals and order it plotted before

### Requirement: Base n of a widget
A widget SHALL have a base n when its marks have one and its indicator declares a base column or an answer column: the n over every row it plots, counted as a mark's is. A widget whose indicator declares neither SHALL have none, since its group column may hold the answers of one question, and adding their bases would count each unit once per answer. A histogram and a box plot SHALL have none.

#### Scenario: Answers of one question
- **WHEN** a bar chart of `teatro_hoy`, whose `categoria` is declared as answers, plots the answers of "Lugar (E4)", whose denominators each sum to 223
- **THEN** the widget's n is 223

#### Scenario: Nothing declared
- **WHEN** the indicator of that bar chart declares no base column and no answer column
- **THEN** the widget has no base n

#### Scenario: Subgroups added up
- **WHEN** a column chart of `teatro_hoy`, whose `categoria` is declared as answers, is grouped by `comuna` and filtered to "Asistió (E1)" and "Sí"
- **THEN** the widget's n is the sum of its comunas' n, 476 when no filter leaves a comuna out

#### Scenario: Widget n after the filters
- **WHEN** a member sets the dashboard filter "Comuna" to `COMUNA 1` over that column chart
- **THEN** the widget's n is 121

### Requirement: Base n line under a widget
A dashboard SHALL have a setting, off by default, that shows each widget's base n under it, and a widget SHALL be able to show or hide it whatever the dashboard says. The line SHALL read "n = ", the n as a whole number with thousands separators, and the name of the base column, else of the denominator, as the dashboard names columns. A widget with no base n SHALL show no line, and its form SHALL say why.

#### Scenario: Line shown
- **WHEN** "Teatro" shows base n and a column chart over `teatro_hoy` has the n 223 and names its denominator "Hogares que asistieron"
- **THEN** the line under the chart reads "n = 223 Hogares que asistieron"

#### Scenario: Hidden by a widget
- **WHEN** "Teatro" shows base n and one of its widgets hides it
- **THEN** that widget shows no n line, and the others do

#### Scenario: Shown by a widget
- **WHEN** "Teatro" does not show base n and one of its widgets shows it
- **THEN** only that widget shows its n line

#### Scenario: Widget without a base n
- **WHEN** an analyst shows the base n on a widget whose indicator declares no base column and no answer column
- **THEN** the widget shows no line, and the form says that its indicator must declare its base column or its answer columns

### Requirement: Base lines under a widget in their place
A widget's small-base note, base n line and no-data line SHALL sit under its chart, in that order, above its footnote. They SHALL take their room from the plot as its subtitle and footnote do (see "Widget context in a tile"): the tile keeps its size, a chart that does not fit is cut at its own box and never over a line, and on screen a line longer than two lines, or than one on a tile two rows high or less, ends with an ellipsis, while on paper it prints whole.

#### Scenario: Lines above the footnote
- **WHEN** the bar chart "2.4. En qué lugares" of "Teatro" shows "n = 223 Hogares que asistieron" and has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the n line reads under the bars and the footnote under the n line

#### Scenario: Chart cut above the lines
- **WHEN** a column chart 6 columns wide and 4 rows high has a subtitle, a two-line footnote, the small-base note, the n line and the no-data line
- **THEN** its tile is as tall as before, the three lines show whole, and the columns are drawn in the room left, cut at their own box if they do not fit

### Requirement: Small-base threshold
A dashboard SHALL have an optional small-base threshold, a whole number from 1 to 10,000, off by default. A widget SHALL be able to follow the dashboard's, turn it off with 0, or set its own from 1 to 10,000; any other value SHALL be rejected with an error on the threshold. A value whose base n is below the threshold in force SHALL have a small base, and a value with no n SHALL never have one.

#### Scenario: Comunas below 30
- **WHEN** "Teatro" has the threshold 30 and its column chart by `comuna` has the n 16 for `COMUNA 7` and 121 for `COMUNA 1`
- **THEN** `COMUNA 7` has a small base and `COMUNA 1` does not

#### Scenario: Turned off for a widget
- **WHEN** a widget of "Teatro" sets its threshold to 0
- **THEN** none of its values has a small base

#### Scenario: Own threshold
- **WHEN** a widget sets the threshold 10 on "Teatro", whose threshold is 30
- **THEN** `COMUNA 7`, with the n 16, has no small base

#### Scenario: Threshold out of range
- **WHEN** an analyst saves a widget with the threshold 10001, or a dashboard with the threshold 0
- **THEN** the API responds 400 with an error on the threshold

#### Scenario: Value with no n
- **WHEN** "Teatro" has the threshold 30 and a stacked bar chart sums `value` of `teatro_conteo`, which declares no base column
- **THEN** none of its segments has a small base

### Requirement: Small bases drawn dimmed
A mark with a small base SHALL be drawn dimmed and never hidden: lighter, with a dashed outline that reads without color. A heat map's cell and a map's shape SHALL keep their shade and be marked with hatching or a dashed outline, so their scale reads the same. While a mark is dimmed, a note under the chart SHALL say that marks with a base below the threshold are dimmed. A stat tile with a small base SHALL dim its figure.

#### Scenario: Columns dimmed
- **WHEN** "2.1. Asistió, por comuna" has `COMUNA 7` (n 16) and `SIN NOMBRE` (n 20) under the threshold 30
- **THEN** those two columns are lighter with a dashed outline and keep their height, and a note under the chart says that marks with a base below 30 are dimmed

#### Scenario: Heat map cell
- **WHEN** a cell of a heat map has a small base
- **THEN** it keeps its shade and is hatched, with a dashed outline

#### Scenario: Nothing dimmed
- **WHEN** no mark of a widget has a small base
- **THEN** it shows no note about small bases

### Requirement: Small bases in tooltips and tables
The tooltip of a value with a small base SHALL say so with its n and the threshold, as "Small base: n = 16 (below 30)". "Show as table" SHALL mark that value and explain the mark under the table. While a widget with no series whose values have a base n shows its base n line or has a threshold in force, its "Show as table" view SHALL have an n column after the value.

#### Scenario: Small base in a tooltip
- **WHEN** a member points at the dimmed `COMUNA 7` column of "2.1. Asistió, por comuna"
- **THEN** its tooltip ends with "Small base: n = 16 (below 30)"

#### Scenario: Small base in a table
- **WHEN** a member shows that chart as a table
- **THEN** it has an n column, the value of `COMUNA 7` is marked, and a note under the table explains the mark

#### Scenario: Table without base settings
- **WHEN** a member shows as a table a widget with no threshold in force and no n line
- **THEN** the table has the columns it had before base settings existed

### Requirement: Mark tooltips of charts without a series
Pointing at, tapping or focusing a bar, a column, a point of a line, a bar of a histogram or a slice of a pie SHALL show its tooltip. Each of those charts SHALL be one Tab stop, as a chart with a series is: arrow keys move from mark to mark in the chart's order, Home and End go to the first and the last, Escape leaves, and the mark reached is read to screen readers.

#### Scenario: Tapping a slice
- **WHEN** a member taps the slice "Culturama (escuelas de formación)" of a pie on a phone
- **THEN** its tooltip opens and stays open until they tap elsewhere

#### Scenario: Stepping through columns
- **WHEN** a keyboard user tabs to a column chart without a series and presses the right arrow twice
- **THEN** the second column is highlighted, its tooltip is shown, and a screen reader reads it

#### Scenario: Bars run down
- **WHEN** a keyboard user tabs to a bar chart without a series and presses the down arrow
- **THEN** the first bar is highlighted and its tooltip is shown

### Requirement: Base figures in tooltips
After its value, a tooltip SHALL give a ratio's numerator and denominator sums under their names as the dashboard names columns, and the base n under the base column's name when it is not the denominator shown. These figures SHALL be whole numbers with thousands separators, with up to two decimals when not whole, never in the indicator's format or unit. A histogram's bar SHALL give its range and count only, and a tooltip that lists several series SHALL give each one's n only.

#### Scenario: Rate of a comuna
- **WHEN** the `COMUNA 1` column plots 56 of 121 households, the ratio's columns named "Hogares que asistieron" and "Hogares"
- **THEN** its tooltip reads `COMUNA 1`, its value, `Hogares que asistieron: 56` and `Hogares: 121`, and no n line

#### Scenario: Figures of a percent indicator
- **WHEN** that indicator's format is `percent` with the unit "de los hogares"
- **THEN** the numerator and denominator read `56` and `121`, never `5,600%` or with the unit

#### Scenario: Declared base in a tooltip
- **WHEN** a column chart of `comercio_gastos_resumen`, which declares `hogares` as its base column, shows `Estrato 2` over 154 households
- **THEN** its tooltip reads `Estrato 2`, its value and `n (hogares): 154`

#### Scenario: Histogram bar
- **WHEN** a member points at a bar of a histogram
- **THEN** its tooltip gives its range and how many rows fall in it, and no numerator, denominator or n

### Requirement: No-data values left out of a widget
A bar, column, line, pie, heat map, population pyramid, Likert or cross table widget SHALL be able to leave out the rows whose group or series value its indicator declares as meaning no data for that column (see `pipeline` "Values that mean no data in an indicator column"). Those rows SHALL then count in no value, total, series, n, overall value or row limit. Saving the option on another kind SHALL be rejected with an error on it.

#### Scenario: Comuna with no block
- **WHEN** `teatro_hoy` declares "Sin dato" as no data for `comuna`, and a column chart grouped by `comuna` leaves out no-data values
- **THEN** it draws no "Sin dato" column, with no `in` filter on `comuna`

#### Scenario: Answer left out of a series
- **WHEN** `teatro_conteo` declares "4. No sabe" as no data for `respuesta`, and a 100% column chart by `comuna` with the series `respuesta` leaves out no-data values
- **THEN** its legend has no "4. No sabe", and each comuna's shares are over the other answers

#### Scenario: Widget without the option
- **WHEN** a widget over `teatro_hoy` does not leave out no-data values
- **THEN** it plots the "Sin dato" rows as before

#### Scenario: Overall line without the left-out rows
- **WHEN** a column chart of `teatro_hoy` by `comuna` has an `overall` reference line and leaves out the "Sin dato" comuna
- **THEN** the overall value counts only the rows the columns plot

#### Scenario: Stat tile refused
- **WHEN** an analyst saves a stat tile that leaves out no-data values
- **THEN** the API responds 400 with an error on that option

### Requirement: No-data line under a widget
A widget that leaves out no-data values SHALL say under its chart what the left-out rows held, combined as one value in the widget's format, with its own unit when it has one: the left-out values that had rows, then that value, and for a measure also its share of the widget's total, as "Sin dato: 4.2%". With the base n line shown it SHALL add their n. It SHALL say nothing when no row was left out. Rows with no value, shown as "(none)", SHALL keep their mark.

#### Scenario: Rate of the left-out rows
- **WHEN** a column chart of the frequency answers of `teatro_hoy` leaves out "No sabe", whose rows hold 5 of 223 households
- **THEN** the line under it reads "No sabe: 2.24%" in the indicator's format

#### Scenario: Share of a count
- **WHEN** a 100% column chart of `teatro_conteo` leaves out "4. No sabe", which holds 11 of the 476 households
- **THEN** the line reads "4. No sabe: 11 hogares (2.3%)"

#### Scenario: Nothing left out
- **WHEN** a widget's own filters already keep the no-data values out
- **THEN** it shows no no-data line

#### Scenario: Rows with no value kept
- **WHEN** a widget leaves out no-data values and some of its rows have no `comuna`
- **THEN** their "(none)" column is drawn as before

### Requirement: Answer columns without an overall line
A bar, column or line widget whose group or series column its indicator declares as an answer column (see `pipeline` "Answer columns of an indicator") SHALL draw no `overall` reference line (see "Overall reference line"), on screen, in images, on paper, in a scheduled email or through MCP, and SHALL say under the chart that its bars are the answers of one question. Its other reference lines SHALL be drawn as before, and saving the widget SHALL NOT be refused for it.

#### Scenario: Overall line over places
- **WHEN** a bar chart of `teatro_hoy` grouped by `categoria` and filtered to "Lugar (E4)", whose `categoria` is declared as answers, has a `mean` and an `overall` line
- **THEN** it draws the mean line only, and a note under the chart reads "No overall line: the bars are the answers of one question."

#### Scenario: Overall line over comunas
- **WHEN** a column chart of `teatro_hoy` grouped by `comuna`, whose `categoria` is declared as answers, has an `overall` line
- **THEN** it draws the overall line as it did before the declaration

### Requirement: Base settings in the widget form
The widget form SHALL offer the base n line (as the dashboard says, shown or hidden), the small-base threshold (as the dashboard says, off, or a number) and, for the kinds that take it, leaving out the no-data values, naming those its indicator declares for the group and series columns. Changing the line or the threshold SHALL redraw the preview without a new query; leaving out values SHALL query again.

#### Scenario: Threshold previewed
- **WHEN** an analyst sets a widget's threshold to 30 in the form
- **THEN** the preview dims its small bases without querying the indicator again

#### Scenario: No-data values named
- **WHEN** an analyst edits a column chart of `teatro_hoy` grouped by `comuna`, whose indicator declares "Sin dato" as no data for `comuna`
- **THEN** the option to leave out no-data values names "Sin dato"

#### Scenario: Nothing declared for the columns
- **WHEN** the indicator declares no no-data value for the widget's group or series column
- **THEN** the form says so next to the option

### Requirement: Base settings of a dashboard
For an `analyst` or `admin`, the dashboard's actions SHALL include **Base sizes**, which sets whether its widgets show their base n line and its small-base threshold, saved for every member. A `viewer` SHALL NOT be able to change them. Saving them unchanged SHALL write nothing, and while a version is shown the action SHALL NOT be offered.

#### Scenario: Analyst sets the threshold
- **WHEN** an `analyst` sets the threshold of "Teatro" to 30 and turns on the base n line
- **THEN** every member who opens "Teatro" sees its small bases dimmed and the n lines

#### Scenario: Viewer cannot change base settings
- **WHEN** a `viewer` asks the API to change the base settings of "Teatro"
- **THEN** the API responds 403 and nothing changes

#### Scenario: Base settings saved unchanged
- **WHEN** an `analyst` opens **Base sizes** on "Teatro" and saves without changing anything
- **THEN** nothing is written and "Teatro" has no new version

### Requirement: Widgets and dashboards without base settings
A widget and a dashboard saved without base settings SHALL show no base n line, no small base and no left-out rows, and SHALL keep their stored settings when opened and saved unchanged, making no version. The only change they show SHALL be in their tooltips (see "Mark tooltips of charts without a series" and "Base figures in tooltips").

#### Scenario: Widget saved before base settings
- **WHEN** a member opens the column chart "2.1. Asistió, por comuna" saved before base settings existed
- **THEN** its columns, colors, caption, notes and table are as before, and only its tooltips give the new figures

#### Scenario: Form saved unchanged
- **WHEN** an analyst opens the form of that widget and saves it unchanged
- **THEN** its stored settings are unchanged and "Teatro" has no new version

### Requirement: Base settings of a moved widget
A widget moved to another dashboard SHALL keep its own base settings and its option to leave out no-data values, and SHALL follow the new dashboard's base settings for what it does not set.

#### Scenario: Moved to a dashboard with a threshold
- **WHEN** an analyst moves a widget with no threshold of its own from "Centro", which has none, to "Teatro", whose threshold is 30
- **THEN** on "Teatro" its small bases are dimmed

#### Scenario: Own setting kept by a move
- **WHEN** a widget that hides its base n line is moved to "Teatro", which shows it
- **THEN** on "Teatro" it still shows no n line
