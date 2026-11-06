# Spec Delta

## MODIFIED Requirements

### Requirement: Stat tile
A stat tile SHALL show one number for its indicator, formatted with the indicator's format and unit, the widget's own unit taking the place of the indicator's when the widget has one (see "Widget unit"): the measure summed over every row the query matches, or the ratio of sums, or, when the tile has a statistic, that statistic of the matching rows' values (see "Stat tile statistic"). Under the number it SHALL say what it plots, naming its statistic when it has one, unless the widget hides its caption (see "Widget caption hidden"), followed by the secondary lines it asks for (see "Stat tile secondary lines").

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `households_visited` with no filters and the measure `value`
- **THEN** it shows one number, the sum of `value` over every row

#### Scenario: Stat tile with its own unit
- **WHEN** the stat tile "Centro cultural o casa de la cultura" over `teatro_hoy`, whose format is `percent` and unit "de los hogares", has the unit "de quienes asistieron" and the value 0.2422
- **THEN** it shows "24.22% de quienes asistieron" in the English interface

#### Scenario: Statistic named under the value
- **WHEN** a stat tile over `teatro_precio_hogar`, filtered to the `variable` "Pagó por la última boleta (E7)", plots `value` with the statistic median
- **THEN** it shows `40,000 pesos`, and under it "Median of", followed by the indicator's label and `value`

#### Scenario: Ratio tile
- **WHEN** a stat tile over `teatro_hoy` plots the ratio of `numerator` to `denominator` with the filters `variable` "Asistió (E1)" and `categoria` "Sí"
- **THEN** it shows `46.85% de los hogares`, the sum of the numerators over the sum of the denominators

### Requirement: Ratio of sums
A widget SHALL be able to plot, instead of one measure, the ratio of the sums of two numeric columns of its indicator over the rows of each group, with no value for a group whose denominator sums to zero. A box plot SHALL instead divide each row's numerator by that row's own denominator (see "Box plot"), and so SHALL a stat tile with a statistic (see "Stat tile statistic"). When the indicator declares a column as the answers of one question (see `pipeline` "Answer columns of an indicator"), a ratio over several of its answers SHALL instead follow "Ratio over declared categories never summed across their values" and "Ratio over declared multiple-choice categories refused", wherever a ratio of sums is taken: a plotted value, a series combined into "Other", a total, or the overall value of a reference line.

#### Scenario: Rate over several rows
- **WHEN** the indicator `piped_water_rate` has three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50, and a widget plots the ratio of `numerator` to `denominator` grouped by `municipality`
- **THEN** it plots 0.5 for `Cali`

#### Scenario: Zero denominator
- **WHEN** a group's denominators sum to 0
- **THEN** the widget shows no value for that group

#### Scenario: Ratio of each row in a statistic
- **WHEN** a stat tile with the statistic median plots the ratio of `numerator` to `denominator`, and one of its rows has the numerator 3 and the denominator 4
- **THEN** that row is the value 0.75 among the values whose median the tile shows

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie, map, heat map, population pyramid, diverging Likert and cross table widgets), an optional series column (see "Series in a widget"), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value, and the same series value when there is a series, SHALL be summed, or combined as a ratio of sums, before being plotted, except in a box plot, where each row gives one value of its group's box (see "Box plot"), and in a stat tile with a statistic, where each row gives one value of the statistic (see "Stat tile statistic"). The row limit SHALL count what the widget plots: its groups, its pairs of group and series value when it has a series, the rows of a histogram with no group column, or the boxes of a box plot. On a dashboard, the rows SHALL also match the dashboard filters that narrow the widget (see `dashboard-filters`); those filters do not count toward the widget's 50.

For each value and each total it plots, the query SHALL also return its base n (see "Base n of a mark") and, for a ratio, the sums of its numerator and of its denominator; a measure whose indicator declares no base column SHALL return neither, and the rows of a histogram and the boxes of a box plot SHALL be returned as before. A stat tile with a statistic SHALL return its n (see "Stat tile statistic n") and no sums of its numerator or denominator. A widget that leaves out the values that mean no data SHALL leave their rows out of every value, total and count it returns (see "No-data values left out of a widget").

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

#### Scenario: Statistic rows not summed
- **WHEN** a stat tile with the statistic median plots `value` over three matching rows with the values 10,000, 40,000 and 90,000
- **THEN** it shows the median 40,000, not the sum 140,000

#### Scenario: Statistic of a ratio returns its n only
- **WHEN** a stat tile with the statistic median plots the ratio of `numerator` to `denominator` over 37 matching rows with a value
- **THEN** its query returns the median and the n 37, and no sums of its numerator or denominator

### Requirement: Base n of a mark
Each plotted value SHALL have a base n: the sum of the indicator's base column over the value's rows when the indicator declares one (see `pipeline` "Base column of an indicator"), else, for a ratio, the sum of its denominator. A measure with neither SHALL have none, nor SHALL a histogram's bar or a pie's "Other" slice. A share in a 100% layout or a Likert bar SHALL have its group's n, a box its number of values, and a stat tile with a statistic the n that "Stat tile statistic n" gives it, whatever the indicator declares. Every n SHALL be unweighted and counted after all filters.

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

#### Scenario: Statistic over a measure without a base
- **WHEN** a stat tile with the statistic median plots `value` of `teatro_precio_hogar`, which declares no base column, over 37 households
- **THEN** its n is 37

### Requirement: Base n counted once across answers
When the indicator declares answer columns (see `pipeline` "Answer columns of an indicator"), an answer SHALL be one combination of their values, and an n over the rows of several answers SHALL count the base once: the largest of the sums of the base over the rows of each answer, not their total. The declaration SHALL change no plotted value, total or order, except a ratio over several answers (see "Ratio over declared categories never summed across their values" and "Ratio over declared multiple-choice categories refused").

#### Scenario: Total over the answers
- **WHEN** a grouped column chart of `teatro_hoy`, whose `categoria` is declared as answers, is grouped by `comuna` with the series `categoria` for "Frecuencia (E2)", and the denominators of each answer of `COMUNA 1` sum to 57
- **THEN** the total of `COMUNA 1` has the n 57, not 57 times the number of answers

#### Scenario: Two questions in one widget
- **WHEN** a grouped column chart of `comercio_lugar_compra`, whose `variable` and `categoria` are declared as answers, splits the places by `variable` "Ropa y calzado" and "Tecnología", each asked of the same 476 households
- **THEN** the widget's n is 476, not 952

#### Scenario: Values unchanged by the declaration
- **WHEN** an analyst declares `categoria` of `teatro_hoy` as answers
- **THEN** every widget over `teatro_hoy` that plots a measure, or whose values and totals each hold one answer, plots the values, totals and order it plotted before

### Requirement: Base n of a widget
A widget SHALL have a base n when its marks have one and its indicator declares a base column or an answer column: the n over every row it plots, counted as a mark's is. A widget whose indicator declares neither SHALL have none, since its group column may hold the answers of one question, and adding their bases would count each unit once per answer; a stat tile with a statistic is the exception (see "Stat tile statistic n"). A histogram and a box plot SHALL have none.

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

#### Scenario: Statistic tile with nothing declared
- **WHEN** a stat tile with the statistic median over `teatro_precio_hogar`, which declares no base column and no answer column, covers 37 households
- **THEN** the widget's n is 37

## ADDED Requirements

### Requirement: Stat tile value size
A stat tile's value SHALL take its size from the room its tile gives it: as large as fits, at most 30 px and at least 18 px, below the 36 px tiles were drawn at before. It SHALL wrap only between words and SHALL NOT break inside a number. A word wider than the tile at 18 px SHALL be cut at the tile's edge, with the whole value still read by screen readers and shown when pointed at. This SHALL apply to every stat tile, saved before or after this change, with no setting.

#### Scenario: Default tile
- **WHEN** a member opens a stat tile of the default size, 3 by 2 cells, that shows `46.85% de los hogares` on a 1,280 px wide screen
- **THEN** its value is drawn at no more than 30 px, with `46.85%` whole on its line and any break between words

#### Scenario: Wide tile
- **WHEN** a member opens a stat tile 6 columns wide that shows `60,514 pesos`
- **THEN** its value is drawn at 30 px on one line

#### Scenario: Narrow tile
- **WHEN** a stat tile 2 columns wide shows `1,016,279 pesos`
- **THEN** no line break falls inside `1,016,279`, and its value is drawn at no less than 18 px

#### Scenario: Tile saved before the change
- **WHEN** a member opens a stat tile saved before this change, whose value was drawn at 36 px
- **THEN** its value is drawn at no more than 30 px, and its saved settings are unchanged

#### Scenario: Tile with texts
- **WHEN** a stat tile 3 columns wide and 2 rows high has a subtitle and a footnote that each take one line
- **THEN** its value is drawn whole, at no less than 18 px, and its label shows under it

#### Scenario: Preview of an indicator
- **WHEN** an `analyst` looks at the total tiles of an indicator's test in the script editor
- **THEN** their values follow the same sizes

### Requirement: Stat tile statistic
An `analyst` or `admin` SHALL be able to give a stat tile a statistic: the median, the first or third quartile (25th or 75th percentile) or the mean of the matching rows. Each row SHALL be one value, its measure or its own numerator over its own denominator, never summed; a row with no finite value SHALL be left out. The statistic SHALL cover every matching row, after the widget and dashboard filters, with quartiles as pandas' `quantile` computes them.

#### Scenario: Median that follows the dashboard filters
- **WHEN** a stat tile with the statistic median plots `value` of `teatro_precio_hogar` filtered to the `variable` "Pagó por la última boleta (E7)", and a member sets the dashboard filter "Comuna" to `COMUNA 1`
- **THEN** the tile goes from `40,000 pesos` to `35,000 pesos`, the median of the households of `COMUNA 1`

#### Scenario: Statistic with an own caption text
- **WHEN** a stat tile with the statistic median has the caption text "Precio de la boleta"
- **THEN** the label under its value reads "Median of Precio de la boleta"

#### Scenario: Quartiles of four values
- **WHEN** the matching rows of a stat tile over a `decimal` indicator hold the values 1, 2, 3 and 4
- **THEN** its first quartile reads `1.75`, its median `2.50`, its third quartile `3.25` and its mean `2.50`

#### Scenario: Mean in the indicator's format
- **WHEN** a stat tile with the statistic mean plots `value` over the 37 rows of "Pagó por la última boleta (E7)", whose mean is 60,513.51, in an indicator of the format `count` with the unit `pesos`
- **THEN** it shows `60,514 pesos`

#### Scenario: Rows with no value left out
- **WHEN** a stat tile with the statistic median plots a ratio, and 2 of its matching rows have the denominator 0 and 1 has no numerator
- **THEN** the median is taken over the other rows only

#### Scenario: No row with a value
- **WHEN** the filters of a stat tile with a statistic leave no row with a value
- **THEN** the tile says that no row matches its query, as a tile without a statistic does

#### Scenario: Large table
- **WHEN** a stat tile with the statistic median matches 200,000 rows and the row limit is 5,000
- **THEN** its median covers all 200,000 rows, and it does not say that the limit was reached

#### Scenario: Ignored dashboard filter
- **WHEN** a stat tile with the statistic median ignores the dashboard filter "Estrato", and a member sets "Estrato" to `Estrato 1`
- **THEN** the tile keeps the median of all its rows and says that it is not filtered by "Estrato"

### Requirement: Stat tile statistic n
A stat tile with a statistic SHALL have as its base n the number of values the statistic covers, as a box has, when its indicator declares no base column, and the sum of the base column over those values' rows when it declares one, whether or not it declares answer columns. That n SHALL be the tile's n line and SHALL decide its small base, and the API and MCP SHALL return it as the tile's n.

#### Scenario: Number of values of a median
- **WHEN** the median tile of "Pagó por la última boleta (E7)" over `teatro_precio_hogar`, which declares no base column, covers 37 households and shows the n line
- **THEN** under what it plots it reads `n = 37`

#### Scenario: Base column of a statistic
- **WHEN** a stat tile with the statistic mean plots `value` of an indicator that declares `hogares`, named "Hogares", as its base column, over 3 rows whose `hogares` are 1, 2 and 1
- **THEN** its n line reads `n = 4 Hogares`

#### Scenario: Small base of a median
- **WHEN** "Teatro" has the small-base threshold 30, and a member sets the dashboard filter "Comuna" to `COMUNA 7` over the median tile of "Pagó por la última boleta (E7)", which then covers 6 households
- **THEN** the tile has a small base and its figure is drawn dimmed

### Requirement: Stat tile statistic settings
The statistic SHALL be saved with the widget for every member, as `median`, `p25`, `p75` or `mean`, and SHALL be left out of the saved widget while the tile has none. Saving a statistic on a widget that is not a stat tile, or one that is none of those four, SHALL be rejected with an error on the statistic. Changing a stat tile into another chart type in the widget form SHALL drop its statistic. Choosing a statistic SHALL query the indicator again.

#### Scenario: Statistic chosen in the form
- **WHEN** an `analyst` picks "Median" for a stat tile in the widget form
- **THEN** the preview queries the indicator again and shows the median of the matching rows

#### Scenario: Statistic on a column chart
- **WHEN** an `analyst` saves a column chart with the statistic `median`
- **THEN** the API responds 400 with an error on the statistic

#### Scenario: Unknown statistic
- **WHEN** an `analyst` saves a stat tile with the statistic `p90`
- **THEN** the API responds 400 with an error on the statistic

#### Scenario: Stat tile turned into a bar chart
- **WHEN** an `analyst` edits a stat tile with the statistic median and changes its type to bar chart
- **THEN** the saved bar chart has no statistic

### Requirement: Stat tile secondary lines
A stat tile SHALL be able to show up to two secondary lines under what it plots, each chosen by an `analyst` or `admin`, the n line first: "n = {count}" with the base's name when it has one (see "Base n of a widget" and "Stat tile statistic n"), and "Without filters: {value}", only while a dashboard filter narrows the tile. A line with nothing to show SHALL be left out. The n line SHALL be the "Base n line under a widget", shown once.

#### Scenario: Base under the value
- **WHEN** the stat tile `46.85% de los hogares` over `teatro_hoy`, whose `categoria` is declared as the answers of one question and whose denominator is named "Hogares", shows the n line
- **THEN** under what it plots it reads `n = 476 Hogares`

#### Scenario: Base n line of the dashboard
- **WHEN** "Teatro" shows each widget's base n, and that tile does not choose the n line
- **THEN** it reads `n = 476 Hogares` once, under what it plots

#### Scenario: Nothing declared
- **WHEN** a stat tile with no statistic plots the ratio of `teatro_hoy`, whose indicator declares no base column and no answer column, and shows the n line
- **THEN** it shows no n line

#### Scenario: Value without the dashboard filters
- **WHEN** that tile shows the line without filters and a member sets the dashboard filter "Comuna" to `COMUNA 1`
- **THEN** it shows the value of the rows of `COMUNA 1`, and under it `Without filters: 46.85% de los hogares`

#### Scenario: No dashboard filter in force
- **WHEN** a stat tile shows the line without filters and no dashboard filter is set
- **THEN** it shows no "Without filters" line

#### Scenario: Filter the tile does not take
- **WHEN** a stat tile over `escuelas_pago_mediana`, whose table has no `comuna` column, shows the line without filters and a member sets "Comuna" to `COMUNA 1`
- **THEN** it shows no "Without filters" line, and says that it is not filtered by "Comuna"

#### Scenario: Median without the dashboard filters
- **WHEN** the median tile of "Pagó por la última boleta (E7)" shows the line without filters and a member sets "Comuna" to `COMUNA 1`
- **THEN** it shows `35,000 pesos`, and under it `Without filters: 40,000 pesos`

#### Scenario: Lines of a tile that hides its caption
- **WHEN** the stat tile `46.85% de los hogares` over `teatro_hoy` hides its caption and shows the n line
- **THEN** under its value it shows `n = 476 Hogares` and no label

#### Scenario: Tile short of room
- **WHEN** a stat tile 3 columns wide and 2 rows high has a footnote and shows both secondary lines
- **THEN** its value shows whole, and its label and lines are cut as far as the room needs

#### Scenario: Tile with texts and the n line
- **WHEN** a stat tile 3 columns wide and 2 rows high has a one-line subtitle and a one-line footnote and shows the n line
- **THEN** its tile is as tall as before, its value is drawn whole at no less than 18 px, and its label and n line are cut as far as the room needs

#### Scenario: Measure with no base
- **WHEN** a stat tile with no statistic plots the measure `value` of an indicator that declares no base column, and shows the n line
- **THEN** it shows no n line

### Requirement: Stat tile secondary line settings
The secondary lines SHALL be saved with the widget for every member, as a list of `n` and `unfiltered`, each at most once, and SHALL be left out of the saved widget while the tile has none. Saving them on a widget that is not a stat tile, or with another value or a repeated one, SHALL be rejected with an error on the secondary lines. Choosing the n line SHALL NOT query the indicator again; choosing the line without filters SHALL.

#### Scenario: n line chosen in the form
- **WHEN** an `analyst` checks "Base (n)" for a stat tile in the widget form
- **THEN** the preview shows the n line without querying the indicator again

#### Scenario: Line without filters chosen in the form
- **WHEN** an `analyst` checks "Value without the dashboard filters" while the dashboard filter "Comuna" is set to `COMUNA 1`
- **THEN** the preview queries the indicator again and shows the line

#### Scenario: Lines on a column chart
- **WHEN** an `analyst` saves a column chart with the secondary line `n`
- **THEN** the API responds 400 with an error on the secondary lines

#### Scenario: Repeated line
- **WHEN** an `analyst` saves a stat tile with the secondary lines `n` and `n`
- **THEN** the API responds 400 with an error on the secondary lines

#### Scenario: Stat tile turned into a column chart
- **WHEN** an `analyst` edits a stat tile with both secondary lines and changes its type to column chart
- **THEN** the saved column chart has no secondary lines

### Requirement: Stat tiles saved before statistics and secondary lines
A widget saved without a statistic or secondary lines SHALL be stored, queried, shown, exported, emailed, versioned, duplicated, moved and read through MCP exactly as before, apart from the size of a stat tile's value. Saving its form unchanged SHALL send the same settings and SHALL NOT make a dashboard version.

#### Scenario: Tile saved before
- **WHEN** a member opens the stat tile `46.85% de los hogares` saved before this change
- **THEN** it shows the same value and description as before, with no statistic and no secondary line

#### Scenario: Form saved unchanged
- **WHEN** an `analyst` opens the form of a stat tile saved before this change and saves it without changing anything
- **THEN** its stored settings are unchanged and the dashboard gets no new version

### Requirement: Ratio over declared categories never summed across their values
When a widget plots a ratio over an indicator that declares answer columns of single choice (see `pipeline` "Answer columns of an indicator"), each value or total over several answers SHALL count the base once, as its base n is counted (see "Base n counted once across answers"): the sum of its numerator over the largest of its per-answer denominator sums, taken over its own rows, per group, series value and cell. A value over one answer SHALL be the ratio of sums, as before.

#### Scenario: Several answers of one question
- **WHEN** `cruces_intencion` declares `categoria` as the answers of a single-choice question, and a stat tile plots its ratio with `variable` "Componentes con sí o tal vez" and `categoria` in `1`, `2`, `3` and `4`
- **THEN** it shows `95.17% de los hogares`, not `23.79% de los hogares`

#### Scenario: Several answers by comuna
- **WHEN** a column chart over that declared indicator plots its ratio grouped by `comuna`, keeping `variable` "Componentes con sí o tal vez" and `categoria` in `1`, `2`, `3` and `4`
- **THEN** each comuna's column divides the comuna's numerators by the largest of its answers' denominator sums, its households counted once, as the comuna's n counts them

#### Scenario: Published total unchanged
- **WHEN** a stat tile over the same declared indicator keeps `variable` "Al menos un componente" and `categoria` "5. Cualquier componente"
- **THEN** it shows `95.17% de los hogares`, as before

#### Scenario: Bars by category unchanged
- **WHEN** `teatro_intencion` declares `categoria` as the answers of a single-choice question, and a bar chart plots its ratio grouped by `categoria` with `variable` "Iría al teatro (I1)"
- **THEN** its bars read `74.58%`, `10.71%`, `12.39%` and `2.31%`, as before

#### Scenario: Total over the categories
- **WHEN** a column chart over that declared indicator plots its ratio grouped by `comuna` with the series `categoria` and `variable` "Iría al teatro (I1)"
- **THEN** its "Show as table" view gives each comuna the total 100%, its households counted once, instead of 25%

#### Scenario: Code column that moves with the answer
- **WHEN** `cruces_intencion` declares only `categoria`, and a column chart plots its ratio grouped by `orden` with `variable` "Componentes con sí o tal vez"
- **THEN** each column, which holds one answer, reads what it read before

#### Scenario: Group filter kept
- **WHEN** a column chart over `teatro_intencion`, declared as above, plots its ratio grouped by `comuna`, keeping `comuna` in its 8 values, `variable` "Iría al teatro (I1)" and `categoria` "1. Sí"
- **THEN** each column reads what it read before

#### Scenario: No declaration
- **WHEN** `cruces_intencion` declares no answer column, and a stat tile keeps `variable` "Componentes con sí o tal vez" and `categoria` in `1`, `2`, `3` and `4`
- **THEN** it shows `23.79% de los hogares`, as today

### Requirement: Ratio over declared multiple-choice categories refused
When a widget plots a ratio over an indicator that declares a column as the answers of a multiple-choice question, and a value it plots would add up rows of several of those answers, the widget SHALL show a message instead of its figures, saying why the figure would be wrong and how to avoid it. A total, a series combined into "Other", or a value without the dashboard filters that would add up several of those answers SHALL have no value.

#### Scenario: Card over two reasons
- **WHEN** `escuelas_razones_no` declares `categoria` as the answers of a multiple-choice question, and a stat tile plots its ratio with `variable` "Razón" and `categoria` in "Falta de tiempo · acceso" and "No les interesa · interés"
- **THEN** instead of a figure the tile says that adding up several answers of `categoria` counts a respondent once per answer, and to keep one answer, group or split by `categoria`, or plot a row the indicator publishes for any of them

#### Scenario: One answer kept
- **WHEN** a stat tile over that declared indicator keeps `variable` "Razón" and `categoria` "Falta de tiempo · acceso"
- **THEN** it shows its figure, as before

#### Scenario: Bars by answer
- **WHEN** a bar chart over that declared indicator plots its ratio grouped by `categoria` with `variable` "Razón"
- **THEN** it draws one bar per reason, as before

#### Scenario: Question with one answer in the rows
- **WHEN** `teatro_hoy` declares `categoria` as the answers of a multiple-choice question, and a stat tile keeps `variable` "Sin dato de precio (E7)", whose rows hold one `categoria`, with no filter on `categoria`
- **THEN** it shows its figure, as before

#### Scenario: Total over several answers
- **WHEN** a heat map over a declared multiple-choice indicator plots its ratio grouped by `categoria` with the series `comuna`
- **THEN** its cells and its "Total" column keep their values, and its "Total" row reads "—"

#### Scenario: Answers folded into Other
- **WHEN** a grouped column chart over a declared multiple-choice indicator with 12 answers plots its ratio grouped by `comuna` with the series `categoria`, so that 5 answers fold into "Other"
- **THEN** the 7 answers it shows keep their values, and its "Other" columns have no value

#### Scenario: Message in the preview
- **WHEN** an `analyst` builds the card over the two reasons in the widget form
- **THEN** the preview shows the message before the widget is saved

### Requirement: Hint for a ratio over several values of one column
The widget form SHALL show a hint, which SHALL NOT keep the widget from being saved, when the widget plots a ratio and one of its filters keeps several values of a text column that is neither its group column nor its series column and that its indicator does not declare as the answers of one question. The hint SHALL name the column, say that answers of one question added up count a respondent once per answer, and say how to avoid it.

#### Scenario: Card over several answers
- **WHEN** an `analyst` builds a stat tile over `cruces_intencion`, which declares no answer column, that plots its ratio and keeps `categoria` in `1`, `2`, `3` and `4`
- **THEN** the form shows the hint naming `categoria`, and the widget can be saved

#### Scenario: Filter on the group column
- **WHEN** an `analyst` edits a column chart grouped by `comuna` that keeps `comuna` in its 8 values
- **THEN** the form shows no hint

#### Scenario: Measure
- **WHEN** an `analyst` edits a column chart over the count indicator `teatro_conteo` that sums `value` and keeps several values of `respuesta`
- **THEN** the form shows no hint
