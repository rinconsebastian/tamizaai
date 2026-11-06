# dashboard-indicators Specification

## Purpose
Let a project member build their own view of the project's computed indicators: a canvas of widgets, each a chart or map over the published indicator table, added, arranged and configured rather than fixed by the dashboard itself.

## Requirements

### Requirement: Widgets
A member with the `analyst` or `admin` role SHALL be able to add, edit and remove widgets on any dashboard. Each widget SHALL belong to the one dashboard it is added on or moved to, and have a title, a chart type (stat, bar, column, line, histogram, pie, map, heat map, population pyramid, diverging Likert bars, cross table or box plot), and a query over exactly one indicator's published table. A dashboard SHALL hold at most the configured number of widgets. A `viewer` SHALL see every dashboard's widgets but SHALL NOT be able to add, edit, remove or arrange them.

#### Scenario: Analyst adds a widget
- **WHEN** an `analyst` adds a column chart titled "Coverage by municipality" over the indicator `coverage` on the dashboard "Health"
- **THEN** the widget is saved on "Health" and shown to every member who opens "Health", and on no other dashboard

#### Scenario: Viewer cannot edit
- **WHEN** a `viewer` tries to add, edit, remove or move a widget
- **THEN** the API responds 403

#### Scenario: Widget limit
- **WHEN** a dashboard already has the maximum number of widgets
- **THEN** adding another to it fails with a message naming the limit

#### Scenario: Limit counted per dashboard
- **WHEN** the dashboard "Health" has the maximum number of widgets and an `analyst` adds a widget to the dashboard "Field", which holds 3
- **THEN** the widget is added to "Field"

#### Scenario: Analyst adds a heat map
- **WHEN** an `analyst` adds a heat map titled "Water source by municipality" over the indicator `households_by_source` on the dashboard "Housing"
- **THEN** the widget is saved on "Housing" and shown to every member who opens it

#### Scenario: Analyst adds a box plot
- **WHEN** an `analyst` adds a box plot titled "Household income by municipality" over the indicator `household_income` on the dashboard "Housing"
- **THEN** the widget is saved on "Housing" and shown to every member who opens it

### Requirement: Widget layout
A widget SHALL have a position and size on its dashboard's grid that an `analyst` or `admin` can move and resize. Each dashboard SHALL have a grid of its own: a widget added without a position SHALL be placed below the widgets of its own dashboard. Reading order, for keyboard navigation and for a page showing several widgets' tables at once, SHALL follow the grid top-to-bottom, left-to-right.

#### Scenario: Widget moved
- **WHEN** an `analyst` drags a widget to a new position
- **THEN** the new position is saved and shown to every member who opens the dashboard afterward

#### Scenario: First widget of a dashboard
- **WHEN** the dashboard "Health" holds widgets down to row 30 and an `analyst` adds the first widget to the dashboard "Field"
- **THEN** the widget is placed at the top of "Field"

### Requirement: Columns available to a widget
A widget's query SHALL be able to name any column of its indicator's published table, discovered the same way the clean data dashboard discovers a table's columns. It SHALL be offered the table's numeric columns as measures, every column as a group column, every column other than its group column as a series column, for a bar or column widget every column as a sort column, and for a map widget every column except its geometry column as a tooltip column.

#### Scenario: Extra column grouped on
- **WHEN** the indicator `attendance` publishes a table with a `round` column and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `round`

#### Scenario: Extra column sorted on
- **WHEN** the indicator `coverage` publishes a table with a `sort_key` column and an analyst configures the order of a column chart over it
- **THEN** `sort_key` is offered as a sort column

#### Scenario: Extra column shown in a tooltip
- **WHEN** the indicator `survey_progress` publishes a table with the columns `block_code`, `address`, `sample_target`, `achieved`, `value` and `geometry`, and an analyst configures a map over it with `geometry` as its geometry column
- **THEN** `block_code`, `address`, `sample_target`, `achieved` and `value` are offered as tooltip columns, and `geometry` is not

#### Scenario: Series column offered
- **WHEN** the indicator `households_by_source` publishes a table with the columns `municipality`, `water_source` and `value`, and an analyst groups a column chart by `municipality`
- **THEN** `water_source` and `value` are offered as series columns, and `municipality` is not

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

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, or in a declared order when one applies (see "Declared category order applied"), against the measure or ratio. Without a series it SHALL draw one line; with a series it SHALL draw one line per series value (see "Series in a widget"), and a series that has no value for a group SHALL have a gap there instead of a point. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order"). Its points SHALL be joined by straight segments, or by steps when the widget says so (see "Step lines").

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

#### Scenario: One line per series
- **WHEN** a line chart over `attendance` is grouped by `round` with values `1`, `2` and `3`, with the series `municipality`, whose values are `Cali` and `Buga`
- **THEN** it draws two lines, one for `Cali` and one for `Buga`, each through its three rounds, with a legend naming both

#### Scenario: Gap in a line
- **WHEN** in that line chart `Buga` has no row for round `2`
- **THEN** the `Buga` line has points at rounds `1` and `3` and no segment through round `2`

#### Scenario: Straight segments by default
- **WHEN** a member opens the line chart "2.10. Cuánto pagaría por una boleta", saved without a choice of segments
- **THEN** its points are joined by straight segments, as before

#### Scenario: Points in a declared order
- **WHEN** the line chart "2.10. Cuánto pagaría por una boleta: curva de demanda" over `teatro_demanda`, grouped by `precio`, has its own order of groups `$50.000`, `$35.000`, `$20.000`, `$10.000`, `$0`
- **THEN** its points read in that order from left to right

### Requirement: Histogram
A histogram SHALL bucket the values of its measure or ratio, over the rows the query matches (or over the group values when it groups by a column), into equal-width ranges and count how many fall in each, with a configurable number of buckets.

#### Scenario: Distribution across many groups
- **WHEN** a histogram is configured for the indicator `coverage` with 10 buckets, grouped by `municipality`, over 80 municipalities with coverage ranging from 10% to 140%
- **THEN** it shows 10 bars, each counting the municipalities whose coverage falls in that bar's range

### Requirement: Pie chart
A pie chart SHALL show its indicator's measure as one slice per group value, sized by its share of the total. Its slices SHALL be drawn from the largest, with "Other" last, unless a declared order applies (see "Declared category order applied"): the slices then follow that order, and "Other" is still last. Its accessible summary SHALL name the largest slice, whatever the order. Its slices' colors and which group values fold into "Other" (past the eighth color, the smallest) SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees each slice in the same color. A declared order SHALL NOT change which group values fold into "Other". Its colors SHALL follow `dashboard-colors`.

#### Scenario: Shares of a total
- **WHEN** a pie chart is configured for the indicator `households_visited` grouped by `municipality`
- **THEN** each municipality is one slice, sized by its share of the summed total

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a pie chart grouped by `water_source` shows `Piped` larger than `Well`, and a member sets a dashboard filter after which `Well` is larger than `Piped`
- **THEN** `Piped` and `Well` keep their colors, and the slices are sized by the filtered rows

#### Scenario: Other kept under a dashboard filter
- **WHEN** a pie chart's own rows hold 10 group values, so the 3 smallest fold into "Other", and a member sets a dashboard filter after which one of those 3 is larger than the others
- **THEN** that value is still part of "Other"

#### Scenario: Largest slice first without a declared order
- **WHEN** a pie chart grouped by `water_source` with no declared order has 60 for `Piped`, 30 for `Well` and 10 for `River`
- **THEN** its slices and its legend read `Piped`, `Well`, `River`, as before declared orders existed

#### Scenario: Slices in a declared order
- **WHEN** the same pie chart has its own order of groups `River`, `Well`, `Piped`
- **THEN** its slices and its legend read `River`, `Well`, `Piped`, whatever their sizes

#### Scenario: Largest slice named in a declared order
- **WHEN** a screen reader reads the summary of that pie chart, in the order `River`, `Well`, `Piped`
- **THEN** the summary names `Piped` as the largest slice

#### Scenario: Declared order does not choose the slices kept
- **WHEN** a pie chart's own rows hold 10 group values, so the 3 smallest fold into "Other", and its order lists one of those 3 first
- **THEN** that value is still part of "Other", and "Other" is the last slice

### Requirement: Map widgets
A map widget SHALL use one indicator and a geometry column its published table has. Rows whose geometry is a polygon SHALL be shown as filled regions, colored by the measure or ratio on a sequential scale; rows whose geometry is a point SHALL be shown as markers, colored or sized the same way. A row whose geometry is missing or cannot be parsed SHALL be left out of the map and counted.

#### Scenario: Choropleth by region
- **WHEN** a map widget is configured for the indicator `coverage` with a `geometry` column holding one polygon per municipality
- **THEN** each municipality is shown filled, colored by its coverage value

#### Scenario: Points
- **WHEN** a map widget's geometry column holds point locations instead of polygons
- **THEN** each point is shown as a marker instead of a filled region

#### Scenario: Unparseable geometry
- **WHEN** 3 of a widget's rows have geometry text that cannot be parsed
- **THEN** the map shows the other rows and counts 3 left out

### Requirement: Stale widget configuration
A widget SHALL show a message naming what is missing, instead of failing the dashboard, in either of these cases:
- its indicator has not published;
- the published table no longer has its measure, its ratio columns, its group column, its series column, its geometry column, its sort column or one of its tooltip columns.

A dashboard filter whose column the table does not have, or has with another kind, SHALL NOT make a widget stale: the widget is shown without that filter (see `dashboard-filters`). A population pyramid's left or right value, or a Likert scale's value, that no row holds SHALL NOT make a widget stale: that side or that value is drawn empty.

#### Scenario: Indicator not published
- **WHEN** a widget is configured over the indicator `attendance`, whose script has never published
- **THEN** the widget says `attendance` has not published yet instead of its chart

#### Scenario: Column removed by a script change
- **WHEN** a script change removes the column a widget grouped by
- **THEN** the widget shows a message naming that column instead of its chart

#### Scenario: Series column removed by a script change
- **WHEN** a script change removes the column `water_source`, which a stacked column chart uses as its series
- **THEN** the widget shows a message naming `water_source` instead of its chart

#### Scenario: Sort column removed by a script change
- **WHEN** a script change removes the column a column chart was ordered by
- **THEN** the widget shows a message naming that column instead of its chart

#### Scenario: Tooltip column removed by a script change
- **WHEN** a script change removes the column `address`, which a map widget shows in its tooltip
- **THEN** the widget shows a message naming `address` instead of its map

#### Scenario: Dashboard filter column removed by a script change
- **WHEN** a script change removes the column `municipality` from an indicator, and a member has the dashboard filter on `municipality` set to `Cali`
- **THEN** the widgets of that indicator show their charts, unfiltered by it, and say they are not filtered by that filter

#### Scenario: Pyramid side with no rows
- **WHEN** a population pyramid has `Female` on the left and `Male` on the right, and a member sets the dashboard filter on `sex` to `Female`
- **THEN** the pyramid shows its left bars and an empty right side, and no message about missing columns

### Requirement: Indicator table export
Every project member SHALL be able to export the full published table of one indicator, exactly as published, to CSV and XLSX, through the same single-use download link as the clean data dashboard.

#### Scenario: Viewer exports
- **WHEN** a `viewer` exports the table of the indicator `coverage` to XLSX
- **THEN** they receive a single-use download link to a file with every published row and column of `coverage`

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

### Requirement: Bar and column order
An `analyst` or `admin` SHALL be able to give a bar or column widget an order. The order is either by the plotted value (the measure or the ratio) or by one column of the indicator's published table, and either ascending or descending. For a widget with a series, the plotted value of a bar SHALL be its total over every series value (see "Totals of a widget with a series"), in every layout, and the order SHALL never change the order of the series inside a bar.

When the sort column is not the group column, a bar's sort value SHALL be the smallest value of that column over the rows the bar stands for, compared as described in "Sort column compared as text or as a number". The same rule SHALL apply in both directions.

Bars with no sort value SHALL come last in both directions. Bars with equal sort values SHALL keep the default order between them in both directions. The order SHALL be applied before the row limit, so a widget that reaches the limit shows the first bars in its order.

A box plot SHALL have only the order described in "Box plot order". A widget whose chart type is not bar, column or box plot SHALL NOT have an order, and saving one with an order SHALL be rejected. A widget without an order SHALL use the default order, or its declared order of groups when one applies.

The order of this requirement is a sort. A declared order of values (see "Declared category order applied") is not a sort, and SHALL be allowed on the kinds "Declared category order of a widget" lists. A sort SHALL win over the order its indicator declares: with a sort, the sort SHALL decide the order of the bars, except that the values the indicator keeps last, and after them the bar shown as "(none)", SHALL come at the end. A declared order SHALL be applied to the bars the query returns, after the row limit. A widget SHALL NOT have both a sort and its own order of groups.

#### Scenario: Largest value first
- **WHEN** a bar chart of the indicator `households_visited` grouped by `municipality` plots 75 for `Cali`, 120 for `Palmira` and 40 for `Buga`, and is ordered by the plotted value descending
- **THEN** its bars read `Palmira`, `Cali`, `Buga` from top to bottom

#### Scenario: Ordered by another column
- **WHEN** the indicator `coverage` publishes an integer column `sort_key` holding 2 for `Cali`, 1 for `Palmira` and 3 for `Buga`, and a column chart grouped by `municipality` is ordered by `sort_key` ascending
- **THEN** its columns read `Palmira`, `Cali`, `Buga` from left to right

#### Scenario: Several values in one bar
- **WHEN** a column chart grouped by `municipality` is ordered by `sort_key` ascending, `Cali` has two rows whose `sort_key` is 5 and 1, and `Palmira` has one row whose `sort_key` is 3
- **THEN** `Cali`, whose smallest sort value is 1, comes before `Palmira`

#### Scenario: Several values in one bar, descending
- **WHEN** the same column chart is ordered by `sort_key` descending
- **THEN** `Palmira` comes before `Cali`, whose sort value is still its smallest, 1

#### Scenario: Bars without a sort value come last
- **WHEN** a column chart grouped by `municipality` is ordered by `sort_key` descending, and none of `Buga`'s rows has a `sort_key`
- **THEN** `Buga`'s column comes last

#### Scenario: Equal sort values keep the default order
- **WHEN** a column chart grouped by `municipality` is ordered by `sort_key`, and `Cali` and `Buga` both have the sort value 1
- **THEN** `Buga` comes before `Cali`, both ascending and descending

#### Scenario: Row limit follows the order
- **WHEN** a bar chart ordered by the plotted value descending matches more groups than the row limit
- **THEN** it shows that the limit was reached, and the bars it shows are the ones with the largest values

#### Scenario: Stacked bars ordered by their totals
- **WHEN** a stacked bar chart grouped by `municipality` with the series `water_source` has the totals 300 for `Cali` and 500 for `Palmira`, and is ordered by the plotted value descending
- **THEN** `Palmira`'s bar comes first, and inside both bars the series keep the same order

#### Scenario: Only bar and column widgets have an order
- **WHEN** an `analyst` saves a line chart with an order
- **THEN** the API responds 400 with an error on the widget's order

#### Scenario: Widget without an order
- **WHEN** a member opens a column chart that was saved without an order
- **THEN** its columns follow the default order

#### Scenario: Declared order without a sort
- **WHEN** the indicator `teatro_hoy` keeps `Sin dato` last for `comuna`, and a column chart over it grouped by `comuna`, with no sort and no filter on `comuna`, shows `COMUNA 1` to `COMUNA 7`, `SIN NOMBRE` and `Sin dato`
- **THEN** its columns end with `SIN NOMBRE` and then `Sin dato`

#### Scenario: Sorted bars keep the values kept last at the end
- **WHEN** the indicator `comercio_comer_fuera` keeps `Otro` last for `categoria`, and the bar chart "4.5. Dónde come la mayoría de las veces cuando sale" is ordered by the plotted value ascending, `Otro` being its smallest bar
- **THEN** `Otro` is its last bar, and the other bars keep their ascending order

#### Scenario: Series of sorted bars in their declared order
- **WHEN** a stacked bar chart ordered by the plotted value descending has the series `barrera_tipo`, whose indicator declares the order `Acceso`, `Interés`, `Otra`
- **THEN** its bars are ordered by their totals, and inside every bar the segments read `Acceso`, `Interés`, `Otra`

#### Scenario: Sort and own order of groups refused
- **WHEN** an `analyst` saves a column chart ordered by the column `orden` that also has its own order of groups
- **THEN** the API responds 400 with an error on the widget's order of groups

### Requirement: Sort column compared as text or as a number
A text, integer or decimal sort column SHALL be compared either as text or as a number, as the widget's order says. When the order does not say, a text column SHALL be compared as text, and an integer or decimal column as a number.

Compared as text, values SHALL be in alphabetical order, and an empty text SHALL count as no value.

Compared as a number, a text value SHALL be read as a number when both of these hold:
- It is written as an optional `+` or `-`, then either digits with an optional `.` and optional digits after it (`5`, `5.`, `5.25`) or a `.` followed by digits (`.5`), then an optional exponent (`e` or `E`, an optional sign and digits). ASCII spaces, tabs and line breaks are allowed before and after it.
- Its value fits the database's numbers.

Any other text value SHALL count as no value. That includes an empty text, a number written with a decimal comma or thousands separators, `NaN`, `Infinity`, and a value too large or too precise to hold, such as `1e1000000`. In a decimal column, `NaN` SHALL also count as no value.

Date, timestamp and true/false sort columns SHALL be compared in their own order: earliest first, and false before true. Saving an order that compares such a column as text or as a number SHALL be rejected. Sometimes a saved order compares a column as text or as a number, and that column has since become a date, timestamp or true/false column. The widget SHALL then show a message naming that column instead of its chart.

Reading a sort value SHALL never fail the widget or the dashboard.

#### Scenario: Text column compared as a number
- **WHEN** a column chart is grouped by the text column `round`, which holds `1`, `2` and `10`, and is ordered by `round` ascending, compared as a number
- **THEN** its columns read `1`, `2`, `10` from left to right

#### Scenario: Text column compared as text
- **WHEN** the same column chart is ordered by `round` ascending, compared as text
- **THEN** its columns read `1`, `10`, `2` from left to right

#### Scenario: Signs and decimals
- **WHEN** a column chart is ordered ascending by a text column compared as a number, and its groups' sort values are `1.5`, `-10`, `1.10` and `-5`
- **THEN** its columns read `-10`, `-5`, `1.10`, `1.5` from left to right

#### Scenario: Text that is not a number
- **WHEN** a column chart is ordered by the text column `round` ascending, compared as a number, and one group's `round` is `n/a`
- **THEN** that group's column comes last, and every column is still shown

#### Scenario: Number column compared as text
- **WHEN** a bar chart is ordered ascending by the integer column `code`, compared as text, and its groups' codes are 9 and 10
- **THEN** the bar with code 10 comes before the bar with code 9

#### Scenario: Date column in date order
- **WHEN** a bar chart is ordered descending by the date column `visited_on`, and each bar's rows share one `visited_on` date
- **THEN** the bar with the latest date comes first

#### Scenario: Date column compared as a number
- **WHEN** an `analyst` saves a bar chart ordered by the date column `visited_on`, compared as a number
- **THEN** the API responds 400 with an error naming `visited_on`

### Requirement: Map shapes at their geographic position
When every coordinate of a map's shapes is a longitude from -180 to 180 and a latitude from -90 to 90, the map SHALL treat them as degrees and draw the shapes at their geographic position on a Web Mercator map, north up. Otherwise it SHALL draw them in their relative positions, with no basemap. A map SHALL open fitted to the extent of its shapes.

#### Scenario: Municipalities in degrees
- **WHEN** a map's geometry column holds municipality polygons in longitude and latitude
- **THEN** the polygons are drawn at their geographic position, and the map opens showing all of them

#### Scenario: Coordinates that are not degrees
- **WHEN** a map's geometry column holds polygons in metres (EPSG:3116), and the widget has the basemap Streets
- **THEN** the polygons are drawn in their relative positions, no basemap is drawn, and the map says the basemap cannot be shown because the coordinates are not degrees

#### Scenario: A single point
- **WHEN** a map's only drawable row is one point
- **THEN** the map opens centred on that point, close enough to see the streets around it when a basemap is shown

### Requirement: Map zoom and pan
A member SHALL be able to zoom and pan every map, with or without a basemap and without an internet connection: with zoom-in, zoom-out and "Show all" buttons, by dragging and double-clicking with a mouse, with the wheel while holding Ctrl (⌘ on macOS), with two fingers on a touch screen, and from the keyboard. "Show all" SHALL return to the fitted view. A map SHALL NOT zoom out more than three levels beyond its fitted view.

#### Scenario: Zoom with the buttons
- **WHEN** a viewer presses zoom-in twice on a map of city blocks
- **THEN** the map shows a smaller area around the same centre, larger, and "Show all" brings back every block

#### Scenario: Offline zoom
- **WHEN** a deployment offers no basemap and has no internet access, and a member zooms into a map
- **THEN** the shapes are zoomed and no request leaves the deployment

#### Scenario: Zoom-out limit
- **WHEN** a member keeps zooming out of a map of one municipality
- **THEN** the map stops three levels beyond the view that fits the municipality

### Requirement: Map gestures do not trap the page
Scrolling over a map SHALL keep scrolling the page: a mouse wheel without Ctrl or ⌘ and a one-finger swipe on a touch screen SHALL NOT zoom or move the map. The map SHALL then show, for a moment, how to zoom or move it.

#### Scenario: Wheel without Ctrl
- **WHEN** a member scrolls the dashboard with the mouse wheel while the pointer is over a map
- **THEN** the page scrolls, the map keeps its zoom, and the map shows "Hold Ctrl and scroll to zoom" ("⌘" on macOS)

#### Scenario: Wheel with Ctrl
- **WHEN** a member holds Ctrl and turns the wheel over a map
- **THEN** the map zooms around the pointer and the page does not scroll

#### Scenario: One finger on a phone
- **WHEN** a member swipes with one finger over a map on a phone
- **THEN** the page scrolls, and the map shows "Use two fingers to move the map"

#### Scenario: Two fingers on a phone
- **WHEN** a member pinches with two fingers over a map on a phone
- **THEN** the map zooms

### Requirement: Map keyboard access
A map SHALL be reachable with Tab. While it has focus, the arrow keys SHALL pan it, `+` and `-` SHALL zoom it, and `0` or Home SHALL show all. Enter SHALL move into its shapes. There, the arrow keys move from shape to shape in the order of its "Show as table" view, showing each one's tooltip, and Escape returns to the map. Its shapes SHALL NOT be stops of their own in the page's Tab order.

#### Scenario: Stepping through blocks
- **WHEN** a keyboard user tabs to a map of 300 blocks, presses Enter and then the right arrow twice
- **THEN** the third block in the table order is highlighted and brought into view, its tooltip is shown, and a screen reader reads the tooltip's text

#### Scenario: One Tab stop
- **WHEN** a keyboard user presses Tab on a map that has focus
- **THEN** focus leaves the map for the next control, without visiting its shapes

#### Scenario: Escape in the widget form
- **WHEN** a keyboard user stepping through the shapes of the map preview in the widget form presses Escape
- **THEN** focus returns to the map and the form stays open; a second Escape closes the form

### Requirement: Map view is not saved
A map's zoom and position SHALL NOT be saved. They SHALL NOT change the widget for other members, SHALL NOT query the indicator again, and SHALL return to the fitted view when the widget's rows change or the page is opened again. Switching the widget to "Show as table" and back SHALL keep them.

#### Scenario: Zoom stays on the viewer's screen
- **WHEN** an analyst zooms into one neighbourhood of a map and another member opens the dashboard
- **THEN** the other member sees the map fitted to all its shapes

#### Scenario: Table and back
- **WHEN** a member zooms a map, switches it to "Show as table" and back
- **THEN** the map shows the same zoom and position

#### Scenario: No new query
- **WHEN** a member pans and zooms a map
- **THEN** the dashboard sends no new query for that widget

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

### Requirement: Pinned map tooltip
After a tap, a click or a keyboard selection, a shape's tooltip SHALL stay open until the member taps or clicks elsewhere, presses Escape, moves to another shape, scrolls the page or moves focus away, or the map's rows change. A tooltip opened by pointing SHALL close when the pointer leaves the shape.

#### Scenario: Tap on a phone
- **WHEN** a member taps a block on a phone
- **THEN** its tooltip opens and stays open until they tap elsewhere

#### Scenario: Pointer leaves
- **WHEN** a member points at a block and moves the pointer off it without clicking
- **THEN** the tooltip closes

### Requirement: Map tooltip columns
An `analyst` or `admin` SHALL be able to give a map widget up to 5 tooltip columns. Each is a column of the indicator's published table other than the geometry column, in a chosen order, with an optional label of up to 80 characters shown instead of the column name. Saving more than 5, one column twice, the geometry column, or tooltip columns on a widget that is not a map SHALL be rejected with an error on the widget's tooltip.

#### Scenario: Analyst adds tooltip columns
- **WHEN** an analyst saves a map with the tooltip columns `address` labelled "Dirección" and `sample_target` with no label
- **THEN** every member's tooltip shows "Dirección" and `sample_target`, in that order

#### Scenario: Too many tooltip columns
- **WHEN** an analyst saves a map with 6 tooltip columns
- **THEN** the API responds 400 with an error saying a map has at most 5

#### Scenario: Tooltip on another chart type
- **WHEN** an analyst saves a column chart with a tooltip column
- **THEN** the API responds 400 with an error on the widget's tooltip

#### Scenario: Map turned into another chart type
- **WHEN** an analyst edits a map that has tooltip columns and changes its type to bar chart
- **THEN** the saved bar chart has no tooltip columns

### Requirement: Tooltip column values over several rows
A tooltip column's value SHALL be computed over the rows of the shape's group that the widget's query matches. A numeric column SHALL be summed. Any other column SHALL show its smallest value: the earliest for dates and timestamps, false before true, and the first in alphabetical order for text, where an empty text counts as no value. When a group combines more than one row, the tooltip SHALL say how many.

#### Scenario: One row per block
- **WHEN** each block of the indicator `survey_progress` has exactly one row
- **THEN** each tooltip column shows that row's value, and the tooltip says nothing about combined rows

#### Scenario: Two rows for one block
- **WHEN** block `0042` has two rows, with `sample_target` 12 and 8 and `address` `Cra 5 # 10-20` and `Cra 5 # 10-22`
- **THEN** its tooltip shows `Meta muestral: 20`, `Dirección: Cra 5 # 10-20` and "Combines 2 rows"

#### Scenario: Filters apply to the tooltip
- **WHEN** a map has a filter `round is exactly 2`, and block `0042` has rows for rounds 1 and 2
- **THEN** its tooltip columns are computed over the round 2 row only

#### Scenario: Empty text in one row
- **WHEN** block `0042` has two rows whose `address` is an empty text and `Cra 5 # 10-20`
- **THEN** its tooltip shows `Dirección: Cra 5 # 10-20`

### Requirement: Tooltip column formats
A numeric tooltip column SHALL be shown with the format chosen for it, `count`, `decimal` or `percent` as in "Indicator formatting" but without a unit. With no format chosen, it SHALL be a whole number with thousands separators for an integer column, and up to two decimals for a decimal column. A format saved on a column that is no longer a number SHALL be ignored.

#### Scenario: Progress shown as a percentage
- **WHEN** a map shows the decimal tooltip column `progress` with the format `percent`, and a block's progress is 0.45
- **THEN** the tooltip shows `45%`

#### Scenario: Format offered for numbers only
- **WHEN** an analyst picks the text column `address` as a tooltip column
- **THEN** the form offers no format for it, and saving `address` with the format `percent` is rejected

#### Scenario: Column no longer a number
- **WHEN** a map shows `progress` with the format `percent`, and a script change publishes `progress` as text
- **THEN** the tooltip shows the text as it is, without the format, and the widget is not stale

### Requirement: Tooltip values that are not numbers
In a tooltip, dates SHALL follow "Date and time display" (see `web-ui`) and timestamps SHALL too, shown in the project's time zone. True/false SHALL read Yes or No, text over 200 characters SHALL be cut with an ellipsis, and no value SHALL read as a dash.

#### Scenario: Missing value
- **WHEN** a block has no address, no value or an empty text, in every row of its group
- **THEN** its tooltip shows `Dirección: —`

#### Scenario: Timestamp
- **WHEN** a block's tooltip column `last_visit` holds `2026-09-01 21:30:05` in the project's time zone, `America/Bogota`
- **THEN** the tooltip shows it to the minute in the shared date and time format, as `Sep 1, 2026, 9:30 PM`, whatever the viewer's own time zone

### Requirement: Tooltip columns in "Show as table"
A map's "Show as table" view SHALL list each shape's group value, its plotted value, its value for each tooltip column under that column's label, formatted as in its tooltip, and the kind of its geometry, in that column order.

#### Scenario: Table of blocks
- **WHEN** a member switches a map of blocks with the tooltip columns "Dirección" and "Meta muestral" to "Show as table"
- **THEN** each row lists the block, its progress, its address, its sample target and its kind of shape

### Requirement: Map basemap
An `analyst` or `admin` SHALL be able to give a map widget a basemap: None, or Streets or Satellite when the deployment offers it (see `map-basemaps`). A new map widget SHALL start with None. The basemap SHALL be drawn under the shapes and saved with the widget for every member. Changing it SHALL NOT query the indicator again. Saving a basemap on a widget that is not a map, or one that is neither Streets nor Satellite, SHALL be rejected.

#### Scenario: Analyst picks satellite
- **WHEN** a deployment offers Google basemaps and an analyst saves a map with the basemap Satellite
- **THEN** every member who opens the dashboard sees the map's shapes over satellite imagery

#### Scenario: Deployment without basemaps
- **WHEN** a deployment offers no basemap and an analyst creates a map widget, or edits one saved with None
- **THEN** the form shows no basemap choice and the map is saved with None

#### Scenario: Basemap on another chart type
- **WHEN** an analyst saves a pie chart with the basemap Streets
- **THEN** the API responds 400 with an error on the widget's basemap

### Requirement: Basemap switched by a member
Every member, viewers included, SHALL be able to switch the basemap of a map they are viewing to None or to any basemap the deployment offers. The switch SHALL apply only to that map on their screen, SHALL NOT be saved, and SHALL be announced to screen readers. It SHALL last until the member leaves or reopens the dashboard, or the widget's rows change.

#### Scenario: Viewer turns on satellite
- **WHEN** a viewer switches a map saved with Streets to Satellite and another member opens the dashboard
- **THEN** the viewer sees satellite imagery, the other member sees Streets, and the widget is unchanged

#### Scenario: Viewer turns a basemap on
- **WHEN** a deployment offers OpenStreetMap and a viewer opens a map saved with None
- **THEN** the viewer can switch it to Streets for themselves

#### Scenario: Viewer turns the basemap off
- **WHEN** a viewer switches a map saved with Satellite to None
- **THEN** the map draws no basemap and requests no tiles, and the widget still has Satellite for everyone else

### Requirement: Basemap not offered
A map whose saved basemap the deployment no longer offers SHALL be drawn without a basemap and without an error. This SHALL NOT make the widget stale. The widget form SHALL show the saved basemap as no longer offered, and saving the widget without changing it SHALL keep it.

#### Scenario: Provider changed by the operator
- **WHEN** a map was saved with Satellite under Google basemaps, and the operator switches the deployment to OpenStreetMap
- **THEN** the map is drawn without a basemap and with no error, its member switch offers Streets, and the widget form shows Satellite as no longer offered

#### Scenario: Saved basemap kept
- **WHEN** a map saved with Streets is edited on a deployment that now offers no basemap, and the analyst saves it after changing only its title
- **THEN** the form showed Streets as no longer offered beside None, and the widget keeps Streets

### Requirement: Basemap on small maps
A map SHALL draw its basemap only when it is large enough to show the basemap's attribution in full. A smaller map SHALL draw no basemap and request no tiles, and SHALL say that it must be enlarged to show the basemap.

#### Scenario: Map resized to one row
- **WHEN** an analyst resizes a map with the basemap Streets to one row of the grid
- **THEN** the map draws its shapes without the basemap, makes no tile request, and says "Enlarge the map to show the basemap"

### Requirement: Map controls on small maps
When a map is too small for its zoom and basemap buttons, they SHALL be gathered in one menu button. Zoom, "Show all" and the basemap switch SHALL stay available at every size.

#### Scenario: Small map on a phone
- **WHEN** a member opens, on a phone, a map whose box is too small for its buttons
- **THEN** a single map options button offers zoom in, zoom out, "Show all" and the basemap choices

### Requirement: Basemap unavailable
When a basemap's tiles cannot be loaded, the map SHALL go on showing its shapes, legend, zoom, tooltips and table without the basemap. It SHALL say once, in a notice announced to screen readers, that the basemap is unavailable. Shapes SHALL never wait for tiles.

#### Scenario: Tile server unreachable
- **WHEN** a map with the basemap Streets is opened from a network that cannot reach the tile server
- **THEN** the shapes are drawn at once, the map says "Basemap unavailable" once, and zoom, tooltips and "Show as table" keep working

### Requirement: Moving a widget to another dashboard
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep every setting it is saved with (its title, chart type, query, display settings, the dashboard filters it ignores and its size), and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

#### Scenario: Widget moved to another dashboard
- **WHEN** an `analyst` edits the widget "Coverage by municipality" on the dashboard "Health" and chooses the dashboard "Field"
- **THEN** the widget is shown on "Field", below its widgets, and no longer on "Health"

#### Scenario: Stale widget moved
- **WHEN** a script change removed the column a widget groups by, and an `analyst` moves the widget to "Field" without changing anything else
- **THEN** the widget moves and still shows a message naming the missing column

#### Scenario: Full dashboard
- **WHEN** the dashboard "Field" holds the maximum number of widgets and an `analyst` moves a widget to it
- **THEN** the API responds 400 with an error on the dashboard naming the limit, and the widget stays where it was

#### Scenario: Dashboard of another project
- **WHEN** an `analyst` asks the API to move a widget to a dashboard of another project
- **THEN** the API responds 400 with an error on the dashboard, and the widget stays where it was

#### Scenario: Target removed meanwhile
- **WHEN** an `analyst` moves a widget to "Field" just after another analyst removed "Field"
- **THEN** the form shows an error on the dashboard, the widget stays where it was, and "Field" is no longer offered in the form or the tab row

#### Scenario: New widget
- **WHEN** an `analyst` adds a widget
- **THEN** the widget form offers no dashboard choice, and the widget is added to the open dashboard

#### Scenario: Ignored filter kept by a move
- **WHEN** a widget on "Health" ignores the dashboard filter on `municipality`, and an `analyst` moves it to "Field", which also has a filter on `municipality`
- **THEN** on "Field" the widget ignores the filter on `municipality` too

#### Scenario: Horizontal box plot moved
- **WHEN** an `analyst` moves a horizontal box plot from "Health" to "Field"
- **THEN** it is horizontal on "Field"

#### Scenario: Column labels kept by a move
- **WHEN** an `analyst` moves a column chart of "Teatro" whose own label for `denominator` is "Hogares que asistieron" to "Cruces"
- **THEN** on "Cruces" the chart still names its denominator "Hogares que asistieron"

### Requirement: Ignoring dashboard filters
When adding or editing a widget, an `analyst` or `admin` SHALL be able to choose which of the dashboard's filters the widget ignores. The choice SHALL be saved with the widget, by the filters' columns, so a filter removed and later defined again on the same column is still ignored. A widget SHALL ignore at most 8 filters. A dashboard with no filters SHALL offer no such choice.

#### Scenario: Total that does not change
- **WHEN** an `analyst` makes the stat tile "Households visited, all municipalities" ignore the filter "Municipality", and a member sets "Municipality" to `Cali`
- **THEN** the stat tile still sums every municipality and says it is not filtered by "Municipality", while the other widgets show Cali

#### Scenario: Filter defined again
- **WHEN** a widget ignores the filter on `round`, and an `analyst` removes that filter and later adds a filter labelled "Visit round" on `round`
- **THEN** the widget ignores "Visit round"

#### Scenario: No filters to ignore
- **WHEN** an `analyst` edits a widget on a dashboard with no filters
- **THEN** the widget form offers no choice of filters to ignore

#### Scenario: Viewer cannot change what a widget ignores
- **WHEN** a `viewer` asks the API to change the filters a widget ignores
- **THEN** the API responds 403 and the widget is unchanged

### Requirement: Widget preview with the dashboard's filters
The widget form's preview SHALL apply the dashboard filter values in force on the member's screen when the form opened, except the filters the widget ignores, and SHALL say which filters it applies. Changing which filters the widget ignores SHALL update the preview.

#### Scenario: Preview filtered
- **WHEN** an `analyst` has set "Municipality" to `Cali` on their screen and opens the form of a widget over an indicator with a column `municipality`
- **THEN** the preview shows Cali's rows and says it is filtered by "Municipality"

#### Scenario: Ignoring updates the preview
- **WHEN** that `analyst` then marks "Municipality" as ignored in the form
- **THEN** the preview shows every municipality

### Requirement: Series in a widget
Bar, column and line widgets MAY have a series column; heat map, population pyramid, diverging Likert and cross table widgets SHALL have one; stat tile, histogram, pie, map and box plot widgets SHALL NOT. The series column SHALL differ from the group column. Saving a widget that breaks these rules SHALL be rejected with an error on the series column.

#### Scenario: Pie chart with a series
- **WHEN** an `analyst` saves a pie chart with the series `water_source`
- **THEN** the API responds 400 with an error on the series column

#### Scenario: Series equal to the group column
- **WHEN** an `analyst` saves a column chart grouped by `municipality` with the series `municipality`
- **THEN** the API responds 400 with an error on the series column

#### Scenario: Heat map without a series
- **WHEN** an `analyst` saves a heat map with no series column
- **THEN** the API responds 400 with an error on the series column

#### Scenario: Existing widgets unchanged
- **WHEN** a member opens a column chart saved before series existed
- **THEN** it shows one column per group, as before

#### Scenario: Box plot with a series
- **WHEN** an `analyst` saves a box plot grouped by `municipality` with the series `sex`
- **THEN** the API responds 400 with an error on the series column

### Requirement: Series order and colors
A widget's series values SHALL be listed in natural order, then a missing series value, shown as "(none)", then "Other" when some were combined (see "Number of series"). When the widget or its indicator declares an order for the series column, that order SHALL take the place of the natural order (see "Declared category order applied"), and "(none)" and "Other" SHALL still come last; a population pyramid's sides and a Likert widget's scale SHALL keep the order the widget gives them. The series a widget shows, their order and their colors SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees the same series in the same colors and legend. The colors SHALL be the dashboard's palette, with the colors kept by value of `dashboard-colors`.

#### Scenario: Natural order
- **WHEN** a stacked column chart has the series `round` with the values `10`, `2` and `1`
- **THEN** its legend and segments read `1`, `2`, `10`

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a stacked column chart shows `Piped`, `Well` and `River` in three colors, and a member sets a dashboard filter after which no row has `Well`
- **THEN** `Piped` and `River` keep their colors and the legend still lists `Well`

#### Scenario: Color kept by value
- **WHEN** a stacked column chart has the series `water_source` and its dashboard keeps color 6 for `River`
- **THEN** `River`'s segments and its legend entry are in color 6

#### Scenario: Answers in the order of the question
- **WHEN** the indicator `comercio_lugar_compra` declares for `categoria` the order `Centro de Duitama`, `Centro comercial de Duitama`, `Almacenes del barrio`, `Vendedores ambulantes`, and a grouped column chart over it is grouped by `estrato` with the series `categoria`
- **THEN** its legend and the columns of each estrato read in that order, not in alphabetical order

#### Scenario: Value kept last among the series
- **WHEN** a widget's series column holds `Sí`, `No`, `No sabe` and `Tal vez`, and its indicator declares no order but keeps `No sabe` last
- **THEN** its legend reads `No`, `Sí`, `Tal vez`, `No sabe`

#### Scenario: None and Other after the values kept last
- **WHEN** a stacked column chart shows 6 series, "(none)" and "Other", and its indicator keeps `Otra razón` last
- **THEN** its legend ends with `Otra razón`, "(none)" and "Other", in that order

#### Scenario: Pyramid sides keep their places
- **WHEN** the indicator `population` declares for `sex` the order `Male`, `Female`, and a population pyramid over it has `Female` on the left
- **THEN** `Female` is still drawn on the left and `Male` on the right

### Requirement: Number of series
A bar, column or line widget SHALL show at most 8 series, and a heat map or a cross table at most 30. When a widget's own rows hold more series values than that, it SHALL show the ones with the largest totals, one fewer than the limit, and SHALL combine the others into one series named "Other", summed or as a ratio of their sums. A missing series value SHALL count as one of the values and SHALL never be combined into "Other".

#### Scenario: Ten series in a stacked chart
- **WHEN** a stacked column chart's series column holds 10 values over the widget's rows
- **THEN** it shows the 7 values with the largest totals and an "Other" segment combining the other 3

#### Scenario: Heat map within its limit
- **WHEN** a heat map's series column holds 25 values
- **THEN** it shows 25 columns and no "Other" column

### Requirement: Totals of a widget with a series
A widget with a series SHALL have a total for each group, for each series value and for all its rows: the sum of the measure, or the ratio of the sums of the ratio's columns, over the rows each total covers. A total SHALL be computed from the rows, never by adding plotted values, and the row limit SHALL count the pairs of group and series value.

#### Scenario: Ratio totals
- **WHEN** a cross table plots the ratio of `numerator` to `denominator` grouped by `municipality` with the series `sex`, and `Cali` has 30 of 60 for `Female` and 10 of 40 for `Male`
- **THEN** `Cali`'s total is 0.4, not the sum or the mean of 0.5 and 0.25

### Requirement: Bars and columns by series
A bar or column widget with a series SHALL have a layout: grouped (one bar per series value, side by side, the default), stacked (segments end to end from the axis), or stacked to 100% (each bar's segments are shares of its total). A stacked or 100% widget SHALL plot a measure: saving one with a ratio SHALL be rejected, because ratios do not add up. In a stacked layout, values below zero SHALL stack below the axis; in a 100% layout they SHALL be left out of the shares and counted in a note.

#### Scenario: Grouped columns
- **WHEN** a grouped column chart of `households_by_source` is grouped by `municipality` with the series `water_source` holding `Piped` and `Well`
- **THEN** each municipality has two columns side by side, one per water source, with a legend

#### Scenario: Stacked to 100%
- **WHEN** a 100% stacked bar chart has, for `Cali`, 60 `Piped` and 40 `Well`
- **THEN** `Cali`'s bar reads 60% `Piped` and 40% `Well`, and its tooltip also gives the values 60 and 40

#### Scenario: Stacked ratio refused
- **WHEN** an `analyst` saves a stacked column chart that plots a ratio
- **THEN** the API responds 400 with an error on the layout

### Requirement: Heat map
A heat map SHALL draw its group values as rows, in natural order (2 before 10, a missing group last) or in a declared order when one applies (see "Declared category order applied"), and its series values as columns, each cell shaded by its value on a sequential scale from the smallest to the largest value shown, with a legend of that range. A cell with no value SHALL be drawn empty and marked as having no value, unlike a cell whose value is 0.

#### Scenario: Cells shaded
- **WHEN** a heat map of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the cell with the largest value is the darkest

#### Scenario: Age bands in natural order
- **WHEN** a heat map is grouped by the text column `age_band`, whose values are `0-4`, `5-9`, `10-14` and `80+`
- **THEN** its rows read `0-4`, `5-9`, `10-14`, `80+` from top to bottom, not in alphabetical order

#### Scenario: Empty cell
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell is drawn empty and its tooltip says it has no value

#### Scenario: Rows in a declared order
- **WHEN** the heat map "2.11. Lo que pagó frente a lo que pagaría" over `teatro_coherencia` follows its indicator's order for `rango_pagado`
- **THEN** its rows read in that order from top to bottom

### Requirement: Population pyramid
A population pyramid SHALL name two values of its series column, one drawn to the left and one to the right of a central axis, both required and different. Its group values SHALL be rows in natural order, or in a declared order when one applies (see "Declared category order applied"), the first at the bottom, with bars measured outward on one scale and labelled without signs. Rows with any other series value SHALL be left out, rows with no group or series value SHALL draw no bar, and the widget SHALL say how many rows it left out. Values SHALL follow the indicator's format.

#### Scenario: Age bands by sex
- **WHEN** a population pyramid over `population` is grouped by `age_band`, whose values are `0-4`, `5-9` and `10-14`, with the series `sex`, `Female` on the left and `Male` on the right
- **THEN** `0-4` is the bottom row, each band has a `Female` bar to the left and a `Male` bar to the right, and both sides use one scale

#### Scenario: Third value and missing values left out
- **WHEN** 12 rows of `population` have the `sex` `Other` and 3 rows have no `age_band`
- **THEN** the pyramid draws no bar for them and says that 15 rows were left out

#### Scenario: Percent indicator
- **WHEN** the indicator `population_share` has the format `percent` and a band's `Female` value is 0.052
- **THEN** that bar is labelled `5.2%`

#### Scenario: Same value on both sides
- **WHEN** an `analyst` saves a population pyramid with `Female` on both sides
- **THEN** the API responds 400 with an error on the right value

#### Scenario: Bands in a declared order
- **WHEN** a population pyramid over `population` grouped by `age_band` has its own order of groups `10-14`, `5-9`, `0-4`
- **THEN** `10-14` is the bottom row and `0-4` the top row

### Requirement: Diverging Likert bars
A diverging Likert widget SHALL have a scale: 2 to 11 values of its series column, in order, each marked negative, neutral or positive, negatives before the neutral and the neutral before the positives, with at most one neutral and at most 5 on each side. It SHALL plot a measure: saving one with a ratio SHALL be rejected.

#### Scenario: Five-point scale
- **WHEN** an `analyst` saves a Likert widget with the scale `Strongly disagree` and `Disagree` negative, `Neither` neutral, `Agree` and `Strongly agree` positive
- **THEN** the widget is saved

#### Scenario: Scale out of order
- **WHEN** an `analyst` saves a Likert scale with a positive value before a negative one
- **THEN** the API responds 400 with an error on the scale

### Requirement: Likert bars drawn
Each group of a Likert widget SHALL be one bar of its composition in percent over its scale's values, centered on the line between negative and positive. Negatives SHALL extend left and positives right, the values nearest the neutral nearest the center; the neutral share SHALL be split in half across the center. Values outside the scale SHALL be left out and counted, as the pyramid's.

#### Scenario: Composition around the center
- **WHEN** `Cali` has 10 `Strongly disagree`, 20 `Disagree`, 20 `Neither`, 30 `Agree` and 20 `Strongly agree`
- **THEN** its bar extends 40% to the left (30% from the negatives and half of the 20% neutral) and 60% to the right, and its tooltips give each value's share and its value

#### Scenario: Value outside the scale
- **WHEN** 7 rows have the answer `Don't know`, which is not in the scale
- **THEN** the bars leave them out and the widget says that 7 rows were left out

### Requirement: Cross table widget
A cross table widget SHALL show its group values as rows, in natural order (2 before 10, a missing group last) or in a declared order when one applies (see "Declared category order applied"), and its series values as columns, each cell holding the value of its pair in the indicator's format, with a "Total" column, a "Total" row and the grand total (see "Totals of a widget with a series"). A pair with no rows SHALL read "—". A table wider than its widget SHALL scroll inside it, keeping the group column in view.

#### Scenario: Contingency table
- **WHEN** a cross table of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the last row and column hold the totals

#### Scenario: Groups in natural order
- **WHEN** a cross table is grouped by the text column `round`, whose values are `1`, `2` and `10`
- **THEN** its rows read `1`, `2`, `10`

#### Scenario: Pair with no rows
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell reads "—" and the totals count only the rows there are

#### Scenario: Rows in a declared order
- **WHEN** a cross table over `teatro_hoy` is grouped by `comuna`, whose indicator keeps `Sin dato` last
- **THEN** its rows end with `SIN NOMBRE`, `Sin dato` and then the "Total" row

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

### Requirement: "Show as table" for a widget with a series
The "Show as table" view of a widget with a series SHALL be its cross table: groups as rows in the chart's order, series values as columns in their order, and the totals. A 100% layout and a Likert widget SHALL show each cell's share and the group's total as its own column; a population pyramid SHALL show its left and right columns and their total.

#### Scenario: Stacked chart as a table
- **WHEN** a member switches a stacked column chart of `households_by_source` to "Show as table"
- **THEN** it lists each municipality with one column per water source, a "Total" column and a "Total" row

#### Scenario: Likert as a table
- **WHEN** a member switches a Likert widget to "Show as table"
- **THEN** it lists each group with one share per scale value, in the scale's order, and the group's total

### Requirement: Box plot
A box plot SHALL draw one box per value of its group column, the box of rows with no group value labelled "(none)", or one box labelled "All rows" when it has no group column. Each box SHALL summarize the matching rows of its group, each row one value, never summed: the row's measure, or its numerator over its own denominator. A row whose value is missing, whose denominator is zero, or whose value is not a finite number SHALL be left out.

#### Scenario: Income by municipality
- **WHEN** a box plot over the indicator `household_income` plots `value` grouped by `municipality`, and the table has 120 rows for `Cali`, one per household
- **THEN** `Cali`'s box summarizes 120 values

#### Scenario: No group column
- **WHEN** a box plot over `household_income` has no group column
- **THEN** it draws one box, labelled "All rows", over every row the query matches

#### Scenario: Rows with no group value
- **WHEN** a box plot is grouped by `municipality` and some rows have no municipality
- **THEN** their box is labelled "(none)", in the chart, its tooltip and "Show as table"

#### Scenario: True/false groups
- **WHEN** a box plot of `household_income` is grouped by the true/false column `has_piped_water`
- **THEN** it draws a box for `false` and a box for `true`

#### Scenario: Ratio of each row
- **WHEN** a box plot plots the ratio of `numerator` to `denominator` grouped by `municipality`, and a row of `Cali` has the numerator 3 and the denominator 4
- **THEN** that row is the value 0.75 in `Cali`'s box

#### Scenario: Rows with no value left out
- **WHEN** 5 rows of `Cali` have no `value`, and the box plot plots `value`
- **THEN** `Cali`'s box leaves those 5 rows out of its values and of its number of values

#### Scenario: Zero denominator left out
- **WHEN** a box plot plots a ratio, and 2 rows of `Cali` have the denominator 0
- **THEN** `Cali`'s box leaves those 2 rows out

#### Scenario: Dashboard filter before the statistics
- **WHEN** a member sets the dashboard filter on `sex` to `Female` over a box plot of `household_income` grouped by `municipality`
- **THEN** each box summarizes only the rows whose `sex` is `Female`

### Requirement: Box plot statistics
Each box SHALL have the number of values it summarizes, their minimum, first quartile, median, third quartile, maximum and mean. Quartiles and the median SHALL be computed by linear interpolation between the sorted values, the default of pandas' `quantile`, so the values 1, 2, 3 and 4 have the first quartile 1.75, the median 2.5 and the third quartile 3.25. The statistics SHALL cover every value of the box, however many rows the table has.

#### Scenario: Four values
- **WHEN** a box's values are 1, 2, 3 and 4
- **THEN** it has 4 values, the minimum 1, the first quartile 1.75, the median 2.5, the third quartile 3.25, the maximum 4 and the mean 2.5

#### Scenario: One value
- **WHEN** a box has the single value 7
- **THEN** it has 1 value; its minimum, quartiles, median, maximum, mean and both whisker ends are 7; it has no outliers; and it is drawn as one line at 7

#### Scenario: Same figures as pandas
- **WHEN** an analyst computes `quantile([0.25, 0.5, 0.75])` of `value` by `municipality` with pandas on the published table of a box plot without filters, whose values are all finite numbers
- **THEN** the quartiles and medians match those of the box plot's boxes

#### Scenario: Large table
- **WHEN** a box plot's table has 200,000 rows for `Cali`
- **THEN** `Cali`'s box summarizes all 200,000 values

### Requirement: Box plot whiskers and outliers
A box's lower whisker SHALL end at its smallest value at or above the first quartile minus 1.5 times the interquartile range, and its upper whisker at its largest value at or below the third quartile plus 1.5 times that range. Values beyond the whiskers SHALL be outliers, drawn as points and counted. A box SHALL draw at most its 100 outliers farthest from its median, and the widget SHALL say how many outliers it did not draw.

#### Scenario: One outlier
- **WHEN** a box's values are 1, 2, 3, 4 and 20, so its first quartile is 2, its third quartile 4, and its whiskers may reach from -1 to 7
- **THEN** its whiskers end at 1 and 4, and 20 is drawn as its one outlier

#### Scenario: No outliers
- **WHEN** every value of a box lies within 1.5 times its interquartile range of the box
- **THEN** its whiskers end at its minimum and its maximum, and it has no outliers

#### Scenario: Many outliers
- **WHEN** a box has 130 outliers
- **THEN** it draws the 100 farthest from its median, its tooltip counts 130 outliers, and the widget says that 30 outliers are not drawn

### Requirement: Box plot order
A box plot without an order SHALL list its boxes in natural order (2 before 10, a missing group last), or in a declared order of groups when one applies (see "Declared category order applied"). An `analyst` or `admin` SHALL be able to order it by median, ascending or descending; boxes with equal medians SHALL keep the natural order between them. An order by median SHALL be applied before the row limit, which counts boxes. A box plot that reaches the limit SHALL say so. Ordering a box plot by a column, or one with no group column, SHALL be rejected with an error on the order.

An order by median SHALL win over the order its indicator declares, except that the values the indicator keeps last, and after them the box shown as "(none)", SHALL come at the end. A box plot SHALL NOT have both an order by median and its own order of boxes.

#### Scenario: Age bands in natural order
- **WHEN** a box plot without an order is grouped by the text column `age_band`, whose values are `0-4`, `5-9`, `10-14` and `80+`
- **THEN** its boxes read `0-4`, `5-9`, `10-14`, `80+`, not in alphabetical order

#### Scenario: Largest median first
- **WHEN** a box plot grouped by `municipality` has the medians 300 for `Cali`, 500 for `Palmira` and 200 for `Buga`, and is ordered by median descending
- **THEN** its boxes read `Palmira`, `Cali`, `Buga`

#### Scenario: Equal medians
- **WHEN** a box plot ordered by median has the median 300 for both `Cali` and `Buga`
- **THEN** `Buga` comes before `Cali`, both ascending and descending

#### Scenario: Ordered by a column
- **WHEN** an `analyst` saves a box plot ordered by the column `sort_key`
- **THEN** the API responds 400 with an error on the widget's order

#### Scenario: Row limit counts boxes
- **WHEN** the row limit is 5,000 and a box plot over a table of 200,000 rows is grouped by `municipality`, which has 40 values
- **THEN** it draws 40 boxes over all the rows, and does not say that the limit was reached

#### Scenario: Limit reached by median
- **WHEN** the row limit is 5 and a box plot ordered by median descending has 8 boxes
- **THEN** it draws the 5 boxes with the largest medians and says that the limit was reached

#### Scenario: Boxes in a declared order
- **WHEN** a box plot over `centro_tarifa_hogar` grouped by `vehiculo`, with no order by median, has its own order of groups `Todos los vehículos`, `Carro`, `Moto`
- **THEN** its boxes read `Todos los vehículos`, `Carro`, `Moto`

#### Scenario: Order by median keeps the values kept last at the end
- **WHEN** a box plot over `comercio_gastos_hogar` ordered by median descending is grouped by `estrato`, whose indicator keeps `No sabe` last
- **THEN** the `No sabe` box comes last, and the other boxes stay ordered by median

#### Scenario: Order by median and own order of boxes refused
- **WHEN** an `analyst` saves a box plot ordered by median that also has its own order of groups
- **THEN** the API responds 400 with an error on the widget's order of groups

### Requirement: Box plot orientation
An `analyst` or `admin` SHALL be able to draw a box plot vertical, its first box on the left (the default), or horizontal, its first box at the top. The orientation SHALL be saved with the widget for every member, and changing it SHALL NOT query the indicator again. Saving an orientation on a widget that is not a box plot, or one that is neither vertical nor horizontal, SHALL be rejected with an error on the orientation.

#### Scenario: Horizontal for long names
- **WHEN** an `analyst` saves a box plot grouped by `municipality` with the orientation horizontal
- **THEN** every member sees one box per row, the first at the top, with the municipality names on the left

#### Scenario: Saved without an orientation
- **WHEN** a member opens a box plot saved with no orientation
- **THEN** it is drawn vertical, its first box on the left

#### Scenario: Orientation on another chart type
- **WHEN** an `analyst` saves a column chart with the orientation horizontal
- **THEN** the API responds 400 with an error on the orientation

#### Scenario: Box plot turned into another chart type
- **WHEN** an `analyst` edits a horizontal box plot and changes its type to column chart
- **THEN** the saved column chart has no orientation

#### Scenario: No new query
- **WHEN** an `analyst` switches the orientation of a box plot in the widget form
- **THEN** the preview is redrawn without querying the indicator again

### Requirement: Box plot drawn
The boxes of a box plot SHALL share one linear scale. Each box SHALL span its first to its third quartile, with a line at the median, a cross at the mean, whiskers to their ends and outliers as points, in its colors (see `dashboard-colors`). The scale SHALL cover every whisker end, mean and outlier drawn and need not start at zero. A key SHALL say what the box, the line, the cross, the whiskers and the points show.

#### Scenario: Scale that does not start at zero
- **WHEN** every value of a box plot lies between 40 and 90
- **THEN** its scale runs over about 40 to 90, not from 0

#### Scenario: Mean beyond the drawn marks
- **WHEN** a box has 600 outliers, the 100 drawn are all above it, and its mean is below its lower whisker end
- **THEN** the scale reaches down to the mean and its cross is drawn inside the plot

#### Scenario: Key
- **WHEN** a member opens a box plot
- **THEN** a key under the plot says that the box holds the middle half of the values (Q1 to Q3), the line is the median, the cross is the mean, the whiskers reach up to 1.5 times the box's length beyond it, and the points are outliers

### Requirement: Box plot marks
Pointing at, tapping or focusing a box SHALL show its group, number of values, minimum, lower whisker end, first quartile, median, mean, third quartile, upper whisker end, maximum and number of outliers. Pointing at an outlier SHALL show its group and value. A box plot SHALL be one Tab stop: arrow keys move from box to box in its order, Home and End go to the first and last box, Escape leaves, and the focused box is read to screen readers.

#### Scenario: Box tooltip
- **WHEN** a member points at `Cali`'s box in a box plot of `household_income`
- **THEN** a tooltip reads `Cali` and its figures, each in the indicator's format and unit

#### Scenario: Stepping through boxes
- **WHEN** a keyboard user tabs to a vertical box plot and presses the right arrow three times
- **THEN** the first press enters the boxes at the first one, the third box in its order is highlighted, its tooltip is shown, and a screen reader reads its group and figures

#### Scenario: Horizontal keys
- **WHEN** a keyboard user tabs to a horizontal box plot and presses the down arrow twice
- **THEN** the second box is focused

#### Scenario: Outliers are not keyboard stops
- **WHEN** a keyboard user steps through a box plot whose boxes have outliers, with the arrows, Home and End
- **THEN** focus moves only from box to box, and each box's tooltip counts its outliers

#### Scenario: Outlier tooltip
- **WHEN** a member points at an outlier of `Cali`'s box whose value is 9,800
- **THEN** a tooltip reads `Cali` and `9,800` in the indicator's format and unit

### Requirement: "Show as table" for a box plot
The "Show as table" view of a box plot SHALL list its boxes in the chart's order, one row each, with its group value, its number of values, its minimum, first quartile, median, third quartile, maximum and mean in the indicator's format (see "Indicator formatting"), and its number of outliers.

#### Scenario: Box plot as a table
- **WHEN** a member switches a box plot of `household_income` grouped by `municipality` to "Show as table"
- **THEN** it lists one row per municipality, in the chart's order, with the columns group, values, minimum, Q1, median, Q3, maximum, mean and outliers

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

### Requirement: Column labels of a widget
A widget of any kind SHALL be able to give its own label, of 1 to 80 characters, to up to 10 columns, as a setting of how it is drawn. The name shown for a column SHALL be the widget's label, else its indicator's label (see `pipeline` "Column labels of an indicator"), else the column's own name. A widget's label SHALL be trimmed, an empty one SHALL mean none, and it SHALL NOT be checked against the published table. Changing the chart type SHALL keep the labels.

#### Scenario: Widget label over the indicator's
- **WHEN** `teatro_hoy` labels `denominator` "Hogares", and the column chart "2.2. Con qué frecuencia asiste" over it labels `denominator` "Hogares que asistieron"
- **THEN** that chart names its denominator "Hogares que asistieron", and the other widgets over `teatro_hoy` name it "Hogares"

#### Scenario: Indicator label used
- **WHEN** a widget over `teatro_hoy` gives `comuna` no label of its own, and `teatro_hoy` labels it "Comuna"
- **THEN** the widget names the column "Comuna"

#### Scenario: Column's own name
- **WHEN** neither a widget nor its indicator labels `hogar_menores`
- **THEN** the widget names the column `hogar_menores`

#### Scenario: Labels on a stat tile
- **WHEN** an `analyst` saves a stat tile with its own label for `numerator`
- **THEN** the stat tile is saved with that label

#### Scenario: Labelled column removed from the table
- **WHEN** a widget labels `estrato`, which it neither groups nor splits by, and a script change removes `estrato` from the indicator's table
- **THEN** the widget is drawn as before, with no message about a missing column

#### Scenario: Chart type changed
- **WHEN** an `analyst` changes a column chart with its own labels into a bar chart
- **THEN** the bar chart keeps the labels

### Requirement: Column labels of a widget that are refused
Saving a widget SHALL be refused, with an error on its labels and nothing saved, when it has more than 10 labels, when a label is longer than 80 characters or holds a control character, or when a label is given for a name that an indicator table's column cannot have (lowercase letters, digits and underscores, not starting with a digit, up to 63 characters).

#### Scenario: Too many labels
- **WHEN** an `analyst` saves a widget with labels for 11 columns
- **THEN** the API responds 400 with an error on the widget's labels saying a widget labels at most 10 columns

#### Scenario: Label too long
- **WHEN** an `analyst` saves a widget with a label of 81 characters for `numerator`
- **THEN** the API responds 400 with an error on the label of `numerator`

#### Scenario: Not a column's name
- **WHEN** an AI client saves a widget with a label for `Comuna`
- **THEN** the call is refused with an error on the widget's labels saying `Comuna` is not a column's name, and nothing is saved

### Requirement: Column labels in widget texts
Every text of a widget that names one of its columns SHALL use the name shown for it: the caption and accessible name of a chart, a stat tile's description, tooltips, a map's tooltip columns with no label of their own, and the headers of "Show as table" and cross tables. What is plotted SHALL read as the indicator's label and the names of its measure, or of its numerator and denominator. Filters, colors kept by value and the form's column choices SHALL keep the columns' own names.

#### Scenario: Caption of a ratio
- **WHEN** `teatro_hoy`, labelled "Teatro hoy", labels `numerator` "Hogares que asistieron", `denominator` "Hogares" and `comuna` "Comuna", and a column chart plots that ratio grouped by `comuna`
- **THEN** its caption reads "Teatro hoy: Hogares que asistieron / Hogares by Comuna"

#### Scenario: Stat tile description
- **WHEN** a stat tile plots the ratio of `numerator`, labelled "Hogares que asistieron", to `denominator`, labelled "Hogares", of `teatro_hoy`, labelled "Teatro hoy"
- **THEN** its description reads "Teatro hoy: Hogares que asistieron / Hogares"

#### Scenario: Measure named
- **WHEN** a stacked column chart over `teatro_conteo`, labelled "Teatro conteo", plots the measure `value`, labelled "Hogares", grouped by `pregunta`, labelled "Pregunta", with the series `respuesta`, labelled "Respuesta"
- **THEN** its accessible name says it plots "Teatro conteo: Hogares" by "Pregunta" and "Respuesta"

#### Scenario: Table headers
- **WHEN** a member shows the column chart of "Caption of a ratio" as a table
- **THEN** its headers read "Comuna" and "Teatro hoy: Hogares que asistieron / Hogares"

#### Scenario: Cross table headers
- **WHEN** a member shows the stacked column chart of "Measure named" as a table
- **THEN** its first header reads "Pregunta" and its series headers keep the answers, such as "1. Sí" and "2. Tal vez"

#### Scenario: Map tooltip column without a label
- **WHEN** a map's tooltip has the column `registros` with no label of its own, which its indicator labels "Registros recibidos", and the column `meta` with the label "Meta"
- **THEN** the tooltip and "Show as table" name them "Registros recibidos" and "Meta"

#### Scenario: Colors kept by value
- **WHEN** "Teatro" keeps color 8 for "1. Sí" of `respuesta`, and `teatro_conteo` labels `respuesta` "Respuesta"
- **THEN** the segments of "1. Sí" are still drawn in color 8

#### Scenario: Filters keep the column's name
- **WHEN** a widget with a label for `categoria` filters `categoria` equals "Sí"
- **THEN** the filter still names `categoria` in the widget form and keeps matching the rows whose `categoria` is "Sí"

### Requirement: Column labels in the widget form
The widget form SHALL offer a label for each column the widget shows: its measure, or its numerator and denominator, its group column and its series column. Each field SHALL show the indicator's label, else the column's name, as its placeholder, and the preview SHALL show the labels as typed without a new query. A form saved unchanged SHALL keep the widget's labels as saved. A column that the analyst replaces in the form SHALL lose the widget's label.

#### Scenario: Labels offered
- **WHEN** an `analyst` edits a column chart grouped by `comuna` of the ratio of `numerator` to `denominator` of `teatro_hoy`, which labels the three columns "Comuna", "Hogares que asistieron" and "Hogares"
- **THEN** the form offers a label field for each of the three columns, with the indicator's labels as placeholders

#### Scenario: Preview without a query
- **WHEN** the `analyst` types "Hogares que asistieron" as the label of `denominator`
- **THEN** the preview's caption reads it, and no query is sent

#### Scenario: Saved unchanged
- **WHEN** an `analyst` opens a widget of "Teatro" saved by an AI client with labels for `numerator` and `estrato`, which it does not show, and saves the form without changing anything
- **THEN** the widget keeps both labels, and "Teatro" has no new version

#### Scenario: Column replaced
- **WHEN** an `analyst` changes the group column of a widget with its own label for `comuna` to `estrato`, and saves
- **THEN** the saved widget has no label for `comuna`

#### Scenario: Tooltip column placeholder
- **WHEN** an `analyst` adds the tooltip column `registros`, which its indicator labels "Registros recibidos", to a map
- **THEN** the tooltip column's label field shows "Registros recibidos" as its placeholder

### Requirement: Widgets and indicators without column labels
A widget with no labels, over an indicator with none, SHALL look, read and export exactly as before column labels existed, naming its columns by their own names. A widget saved before column labels SHALL keep its stored settings as they are, so opening its form and saving it unchanged makes no version. A widget that plots its indicator's default measure without naming it SHALL keep the indicator's label alone as what it plots.

#### Scenario: Dashboard before labels
- **WHEN** a member opens "Teatro" after the upgrade, and none of its 44 widgets or of their indicators labels a column
- **THEN** every caption, description and table header names `numerator`, `denominator` and the group columns as before

#### Scenario: Stored settings kept
- **WHEN** the API reads and writes again a widget's settings stored before column labels existed
- **THEN** the stored text is the same, byte for byte

#### Scenario: Default measure
- **WHEN** a widget of "Quién respondió" names no measure and no ratio, and its indicator labels `value` "Personas"
- **THEN** its caption names what it plots with the indicator's label alone, as before

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

### Requirement: Reference lines on bars, columns and lines
An `analyst` or `admin` SHALL be able to give a bar, column or line widget up to 4 reference lines, saved with the widget for every member. Each line SHALL have a kind: `mean`, `median`, `overall` or `value`. A `value` line SHALL have its number. Any line MAY have a label of 1 to 40 characters; a line without one SHALL show its kind's default label in the interface language. A widget without reference lines SHALL be drawn as before.

#### Scenario: Mean and overall lines on a column chart
- **WHEN** an `analyst` saves the column chart "2.1. Asistió, por comuna" over `teatro_hoy` with a `mean` line and an `overall` line
- **THEN** every member who opens the dashboard "2. Teatro" sees both lines across its columns

#### Scenario: Default label in Spanish
- **WHEN** a column chart over `teatro_hoy`, whose format is `percent` and unit `de los hogares`, has a `mean` line at 0.3417 with no label, and the browser entry that chose Spanish for a member is `es-CO`
- **THEN** the line reads "Promedio: 34,17 % de los hogares"

#### Scenario: Own label
- **WHEN** a `value` line of 0.5 on a `percent` indicator with no unit has the label "Meta"
- **THEN** the line reads "Meta: 50%"

#### Scenario: Widget saved before reference lines
- **WHEN** a member opens a column chart saved before reference lines existed
- **THEN** it is drawn with no line and no key, on the same axis as before

### Requirement: Mean and median reference lines
A `mean` or `median` line SHALL be at the mean or the median of the values the chart draws: its bars, or the points of its line, after the widget's and the dashboard's filters, the bar of a missing group value included. The median of an even number of values SHALL be the mean of the two middle ones, as pandas computes it. When the widget reaches the row limit, these lines SHALL summarize the bars it draws.

#### Scenario: Four bars
- **WHEN** a column chart of `teatro_hoy` grouped by `comuna` draws the values 0.40, 0.50, 0.60 and 0.20, with a `mean` and a `median` line
- **THEN** the mean line is at 0.425 and the median line at 0.45

#### Scenario: Three bars
- **WHEN** a column chart draws the values 0.20, 0.50 and 0.60, with a `median` line
- **THEN** the median line is at 0.50

#### Scenario: Dashboard filter in force
- **WHEN** a member sets the dashboard filter "Estrato" to `Estrato 2` over a column chart of `teatro_hoy` grouped by `comuna` with a `mean` line
- **THEN** the mean line is the mean of the columns drawn under that filter

#### Scenario: Missing group value counted
- **WHEN** a bar chart grouped by `comuna` with a `mean` line draws a "(none)" bar for the rows with no comuna
- **THEN** that bar counts in the mean like any other bar

### Requirement: Overall reference line
An `overall` line SHALL be at the ratio of the sums of the widget's numerator and denominator over every row its query matches, with the widget's and the dashboard's filters, whatever bars are drawn and wherever the row limit falls: the figure a stat tile with the same indicator, ratio and filters shows. An `overall` line SHALL need a ratio. An `overall` line with no value, because the denominators sum to zero, SHALL NOT be drawn.

#### Scenario: Same figure as a stat tile
- **WHEN** a stat tile of `teatro_hoy` with the ratio of `numerator` to `denominator` and the filters `variable` = `Asistió (E1)` and `categoria` = `Sí` shows 46.85%, and a column chart grouped by `comuna` with the same ratio and filters has an `overall` line
- **THEN** the overall line is at 46.85%

#### Scenario: Overall unlike the mean
- **WHEN** a column chart of that ratio draws `COMUNA 1` with 60 of 121 households and `COMUNA 7` with 3 of 16, with a `mean` and an `overall` line
- **THEN** the mean line is at 34.17% and the overall line at 45.99%

#### Scenario: Answers of one question
- **WHEN** a bar chart of `teatro_hoy` grouped by `categoria` with the filter `variable` = `Lugar (E4)`, whose indicator declares no answer columns, in which every place has the same denominator in every cell of the table, has a `mean` and an `overall` line
- **THEN** both lines are at the same value

#### Scenario: Rows past the row limit
- **WHEN** a bar chart with an `overall` line matches more groups than the row limit
- **THEN** it says that the limit was reached, and its overall line still covers every row its query matches

### Requirement: Fixed-value reference line
A `value` line SHALL be drawn at its number, read in the indicator's units: a fraction for a `percent` indicator, a number of its unit for a `count` or `decimal` one. It SHALL show its value formatted as the widget's values are, with their unit.

#### Scenario: Target on a percent indicator
- **WHEN** the bar chart "2.4. En qué lugares" over `teatro_hoy`, whose format is `percent` and unit `de los hogares`, has a `value` line of 0.25 labelled "Meta"
- **THEN** the line is drawn at 25% and reads "Meta: 25% de los hogares"

#### Scenario: Count with a unit
- **WHEN** a column chart over `teatro_conteo`, whose format is `count` and unit `hogares`, has a `value` line of 100 with no label, in English
- **THEN** the line reads "Reference: 100 hogares"

### Requirement: Reference lines with a series
A grouped bar or column widget and a line widget with a series SHALL take `overall` and `value` lines, the `overall` line at the widget's total over all its rows. A stacked widget SHALL take only `value` lines. A 100% widget SHALL take only `value` lines whose number is a share from 0 to 1, drawn at that share of each bar. No widget with a series SHALL take a `mean` or a `median` line.

#### Scenario: Overall line on grouped columns
- **WHEN** a grouped column chart of `teatro_hoy` grouped by `comuna` with the series `estrato` and the filters `variable` = `Asistió (E1)` and `categoria` = `Sí` has an `overall` line
- **THEN** the line is at the total of all its rows, the grand total its "Show as table" view gives

#### Scenario: Half of every bar
- **WHEN** the 100% bar chart "2.8. Iría al teatro, por comuna" over `teatro_conteo`, with the series `respuesta`, has a `value` line of 0.5
- **THEN** the line crosses every bar at half its length and reads "Reference: 50%"

### Requirement: Reference line settings refused
Saving a widget SHALL be rejected, with an error on its reference lines or on the line at fault, when it has more than 4 lines; a line with no kind or an unknown one; a second `mean`, `median` or `overall` line; a `value` line without its number, or another kind with one; a label longer than 40 characters or holding a line break or another control character; a line its chart type or layout does not take; or an `overall` line over a measure.

#### Scenario: Five lines
- **WHEN** an `analyst` saves a column chart with 5 reference lines
- **THEN** the API responds 400 with an error on the reference lines naming the limit of 4

#### Scenario: Line on a pie chart
- **WHEN** an `analyst` saves a pie chart with a `mean` line
- **THEN** the API responds 400 with an error on the reference lines

#### Scenario: Mean line with a series
- **WHEN** an `analyst` saves the 100% bar chart "2.8. Iría al teatro, por comuna", which has the series `respuesta`, with a `mean` line
- **THEN** the API responds 400 with an error on that line's kind

#### Scenario: Overall line over a measure
- **WHEN** an `analyst` saves a bar chart that plots the measure `value` of `teatro_conteo` with an `overall` line
- **THEN** the API responds 400 with an error on that line's kind, saying that an overall line needs a ratio

#### Scenario: Value line without its number
- **WHEN** an `analyst` saves a column chart with a `value` line that has no number
- **THEN** the API responds 400 with an error on that line's value

#### Scenario: Share past the whole bar
- **WHEN** an `analyst` saves a 100% bar chart with a `value` line of 1.5
- **THEN** the API responds 400 with an error on that line's value, saying that it is a share from 0 to 1

#### Scenario: Line break in a label
- **WHEN** an `analyst` saves through the API a column chart whose `value` line has the label "Meta" followed by a line break and "2027"
- **THEN** the API responds 400 with an error on that line's label

### Requirement: Reference lines drawn
Each reference line SHALL be a dashed line across the plot at its value, vertical on a bar chart and horizontal on a column or line chart, in its own dash pattern and the text's ink, on an axis that reaches it. At its end it SHALL show its label and value, as tooltips write values, unless that text would not fit or would cover another's. A key under the plot, above the widget's footnote, SHALL list every line with that text, and the accessible description SHALL name each line and its value.

#### Scenario: Mean line on horizontal bars
- **WHEN** a member opens the bar chart "2.4. En qué lugares" with a `mean` line
- **THEN** a vertical dashed line crosses the bars at their mean, labelled with "Mean" and its value

#### Scenario: Axis widened
- **WHEN** every column of a chart is below 30% and it has a `value` line at 0.5
- **THEN** its axis reaches 50% and the line is drawn inside the plot

#### Scenario: Lines close together
- **WHEN** a column chart's `mean` and `median` lines are so close that their labels would overlap
- **THEN** only the first line's label is drawn at the lines, and the key lists both with their values

#### Scenario: Text too wide for the plot
- **WHEN** a narrow column chart over `teatro_hoy` has a `value` line of 0.5 labelled "Meta del plan de desarrollo municipal", whose text with its value and unit is wider than the plot
- **THEN** the line is drawn with no text at it, and the key reads "Meta del plan de desarrollo municipal: 50% de los hogares"

#### Scenario: Screen reader
- **WHEN** a screen reader reads a column chart with an `overall` line at 45.99%
- **THEN** it hears the line's label and 45.99% after the chart's summary

#### Scenario: Key above the footnote
- **WHEN** the column chart "2.1. Asistió, por comuna", with the footnote "No suman 100 %: respuesta múltiple.", has a `mean` line
- **THEN** the key listing the line shows under the columns, the footnote shows under the key, and the columns are drawn in the room left, as when a subtitle or a footnote takes room (see "Widget context in a tile")

### Requirement: Reference lines in "Show as table"
The "Show as table" view of a widget with reference lines SHALL keep its table as it is, and SHALL list under the table each line drawn, with its label and formatted value, in the order the widget lists them, above the widget's footnote.

#### Scenario: Column chart as a table
- **WHEN** a member switches the column chart "2.1. Asistió, por comuna" over `teatro_hoy`, with a `mean` line at 34.17% and an `overall` line at 45.99%, to "Show as table"
- **THEN** the table lists the comunas as before, and under it reads "Mean: 34.17% de los hogares" and "Overall: 45.99% de los hogares"

### Requirement: Step lines
An `analyst` or `admin` SHALL be able to draw a line widget, with or without a series, with steps: each point's value SHALL hold until the next point, where the line SHALL move straight to the next value. The choice SHALL be saved with the widget for every member, and changing it SHALL NOT query the indicator again. A gap in a series SHALL stay a gap. Saving a choice of segments other than straight or steps, or any choice on a widget that is not a line chart, SHALL be rejected.

#### Scenario: Demand curve with steps
- **WHEN** a member opens a line chart of `teatro_demanda` grouped by `precio`, drawn with steps, whose first two points are 98.03% at `$0` and 60% at `$10.000`
- **THEN** the line runs level at 98.03% from `$0` to `$10.000`, then drops straight to 60%

#### Scenario: Steps per series
- **WHEN** the line chart "Curva de demanda por comuna", with the series `comuna`, is drawn with steps
- **THEN** each comuna's line is drawn with steps, in its color

#### Scenario: Steps on a column chart
- **WHEN** an `analyst` saves a column chart with steps
- **THEN** the API responds 400 with an error on the choice of segments

### Requirement: Data labels on bars, columns, lines and pies
An `analyst` or `admin` SHALL be able to show data labels on a bar or column widget with no series or a grouped layout, and on a line or pie widget: each bar's formatted value at its end, each point's above it, each slice's on it. A label that does not fit its mark, or would cover a label already drawn, SHALL NOT be drawn. Data labels SHALL be off by default. Saving the setting on another kind, or data labels turned on with a stacked or 100% layout, SHALL be rejected.

#### Scenario: Value at the end of a bar
- **WHEN** a member opens the bar chart "2.4. En qué lugares" over `teatro_hoy` with data labels
- **THEN** the bar of "Centro cultural o casa de la cultura" reads "24.22% de los hogares" at its end

#### Scenario: Columns too narrow
- **WHEN** a grouped column chart with data labels has columns narrower than their labels
- **THEN** those labels are not drawn, and each column's tooltip still gives its value

#### Scenario: Thin slice
- **WHEN** a pie chart with data labels has a slice too thin for its label
- **THEN** that slice has no label and the other slices have theirs

#### Scenario: Off by default
- **WHEN** a member opens a bar chart saved without data labels
- **THEN** it draws no data label

#### Scenario: Stacked bars
- **WHEN** an `analyst` saves a stacked bar chart with data labels
- **THEN** the API responds 400 with an error on the data labels

### Requirement: Chart annotations in the widget form
The widget form SHALL offer only the reference lines, steps and data labels the widget takes, SHALL take a percent target as a percent, and SHALL say that over the answers of one question an `overall` line is only the mean of the bars. Editing them SHALL redraw the preview without a new query, except adding or removing an `overall` line on a widget with no series, which SHALL query once. A change of chart type, series or layout SHALL drop what the widget no longer takes.

#### Scenario: Label edited
- **WHEN** an `analyst` changes the label of a `mean` line in the widget form
- **THEN** the preview is redrawn without querying the indicator again

#### Scenario: Overall line added
- **WHEN** an `analyst` adds an `overall` line in the widget form of a column chart with no series
- **THEN** the preview queries the indicator once and draws the line

#### Scenario: Kinds offered with a series
- **WHEN** an `analyst` adds a reference line to a grouped column chart of a ratio with a series
- **THEN** the form offers only the `overall` and `value` kinds

#### Scenario: Percent typed in the form
- **WHEN** an `analyst` types 50 as the value of a `value` line on a `percent` indicator and saves
- **THEN** the line is drawn at 50%, and the widget is saved with the number 0.5

#### Scenario: Overall help
- **WHEN** an `analyst` picks the `overall` kind for the bar chart "2.4. En qué lugares"
- **THEN** the form says that when the bars are the answers of one question, such as places or reasons, the line is only their mean and not a total

#### Scenario: Column chart turned into a pie chart
- **WHEN** an `analyst` changes a column chart with a `mean` line and data labels into a pie chart
- **THEN** the saved pie chart has data labels and no reference lines

### Requirement: Chart annotations kept with the widget
Reference lines, steps and data labels SHALL be saved with the widget's display settings: a dashboard version, a duplicated dashboard and a widget moved to another dashboard SHALL keep them. A widget saved before they existed SHALL keep its stored settings, and make no new version, when its form is saved without a change.

#### Scenario: Line restored away
- **WHEN** an `analyst` adds a `mean` line to a column chart of "2. Teatro" and then restores the version before that change
- **THEN** the column chart has no mean line, and the restore is a new version

#### Scenario: Steps duplicated
- **WHEN** an `analyst` duplicates "2. Teatro", whose demand curve is drawn with steps
- **THEN** the copy's demand curve is drawn with steps

#### Scenario: Data labels moved
- **WHEN** an `analyst` moves a bar chart with data labels from "2. Teatro" to "5. Centro"
- **THEN** it shows its data labels on "5. Centro"

#### Scenario: Form saved unchanged
- **WHEN** an `analyst` opens a widget of "2. Teatro" saved before chart annotations existed and saves the form without changing anything
- **THEN** "2. Teatro" has no new version, and the widget's stored settings are unchanged

### Requirement: Declared category order of a widget
An `analyst` or `admin` SHALL be able to give a widget its own order of group values, on every kind but stat tile, histogram and map, and of series values, on a widget with a series other than a pyramid or a Likert widget: each a list of at most 100 texts of up to 500 characters, none twice; an empty list is no order. It SHALL be saved with the widget, kept by a copy or a move, and changing it SHALL NOT query again. Any other order SHALL be rejected with an error on it.

#### Scenario: Own order of answers
- **WHEN** an `analyst` gives a bar chart over `comercio_lugar_compra` grouped by `categoria` the order `Centro de Duitama`, `Centro comercial de Duitama`, `Almacenes del barrio`
- **THEN** every member sees its bars in that order, the other values after them

#### Scenario: Order of groups on a stat tile
- **WHEN** an `analyst` saves a stat tile, a histogram or a map with an order of groups
- **THEN** the API responds 400 with an error on the order of groups

#### Scenario: Order of groups without a group column
- **WHEN** an `analyst` saves a box plot with no group column and an order of groups
- **THEN** the API responds 400 with an error on the order of groups

#### Scenario: Order of series without a series
- **WHEN** an `analyst` saves a column chart with no series column and an order of series
- **THEN** the API responds 400 with an error on the order of series

#### Scenario: Order of series of a pyramid or a Likert widget
- **WHEN** an `analyst` saves a population pyramid or a Likert widget with an order of series
- **THEN** the API responds 400 with an error on the order of series, which says that the sides or the scale set that order

#### Scenario: Too many values
- **WHEN** an `analyst` saves a widget whose order of groups lists 101 values
- **THEN** the API responds 400 with an error on the order of groups naming the limit of 100

#### Scenario: Value listed twice
- **WHEN** an `analyst` saves a widget whose order of series lists `No sabe` twice
- **THEN** the API responds 400 with an error on the second `No sabe`

#### Scenario: Empty order
- **WHEN** an `analyst` saves a widget whose order of groups is an empty list
- **THEN** the widget is saved with no order of groups, and its stored settings are those of a widget without one

#### Scenario: Order kept by a copy
- **WHEN** an `analyst` duplicates "2. Teatro", which holds a widget with its own order of series
- **THEN** that widget's copy has the same order of series

#### Scenario: Order kept by a move
- **WHEN** an `analyst` moves a widget with its own order of groups from "2. Teatro" to "8. Cruces"
- **THEN** it follows the same order on "8. Cruces"

### Requirement: Declared category order applied
A widget's values SHALL follow its own order, else its indicator's order for the column, else today's order for its kind; a sort or an order by median keeps ordering groups (see "Bar and column order" and "Box plot order"). Listed values SHALL come first, in order, then the others in today's order, then the values kept last, then "(none)" and "Other". Values SHALL match as exact text, a listed value not shown SHALL be skipped, and an order SHALL NOT change which values are shown or combined.

#### Scenario: Indicator order for every widget
- **WHEN** `comercio_lugar_compra` declares an order for `categoria`, and two widgets on two dashboards group by `categoria` without an order of their own
- **THEN** both list its values in that order

#### Scenario: Widget order over the indicator's
- **WHEN** `comercio_lugar_compra` declares an order for `categoria`, and a bar chart over it has its own order of groups
- **THEN** the bar chart follows its own order

#### Scenario: Values not listed
- **WHEN** a cross table's indicator declares for `respuesta` the order `Sí`, `No`, and its series column also holds `Tal vez` and `No sabe`
- **THEN** its columns read `Sí`, `No`, `No sabe`, `Tal vez`, then "Total"

#### Scenario: Missing value after the values kept last
- **WHEN** `teatro_hoy` keeps `Sin dato` last for `comuna`, and a heat map over it grouped by `comuna` also has rows with no comuna
- **THEN** its last rows read `Sin dato` and then "(none)"

#### Scenario: Own order over a value kept last
- **WHEN** an indicator keeps `No sabe` last for `respuesta`, and a widget's own order of series lists `No sabe` second
- **THEN** the widget shows `No sabe` second

#### Scenario: Exact text
- **WHEN** an order lists `sí` and the widget shows `Sí`
- **THEN** `Sí` takes no place from the order and follows the listed values

#### Scenario: Order does not choose the series kept
- **WHEN** a stacked column chart's series column holds 10 values, and its order lists first a value whose total is among the 3 smallest
- **THEN** that value is still combined into "Other"

### Requirement: Declared category order in charts and tables
Wherever a widget lists its values, it SHALL use the order of "Declared category order applied": the axis of bars, columns, lines and box plots, the rows and columns of a heat map, a cross table and Likert bars, the rows of a pyramid, the slices and legend of a pie, a series legend and its marks, the order the arrow keys move through marks, and the rows and columns of "Show as table".

#### Scenario: Legend, segments and table
- **WHEN** a 100% column chart with the series `respuesta` follows the order `Sí`, `Tal vez`, `No`, `No sabe`
- **THEN** its legend, the segments of every column and the columns of its "Show as table" view read in that order

#### Scenario: Keyboard order
- **WHEN** a keyboard user steps with the right arrow through the columns of a heat map whose columns follow a declared order
- **THEN** the focus moves through the cells in that order

#### Scenario: Heat map as a table
- **WHEN** a member shows as a table a heat map whose rows follow its indicator's order for `rango_pagado`
- **THEN** the table's rows read in that order from top to bottom

### Requirement: Numbered category prefixes hidden
When an indicator column has `stripPrefix`, a widget SHALL show its values without a leading number of 1 to 3 digits, then `.`, `)`, `-`, `–` or `:` (spaces allowed before it) and a space ("1. Sí", "2) No", "3 - Tal vez"), unless what follows is empty or starts with a digit. Two values of a widget that would read alike SHALL keep their full text. The order, filters and colors kept by value SHALL still use the full value.

#### Scenario: Numbered answers
- **WHEN** `teatro_conteo` hides the prefixes of `respuesta`, and the 100% bar chart "2.8. Iría al teatro, por comuna" has the series `respuesta`
- **THEN** its legend reads `Sí`, `Tal vez`, `No`, `No sabe`, in the same order as before

#### Scenario: Everywhere the value is shown
- **WHEN** a member points at the `1. Sí` segment of that chart and then shows it as a table
- **THEN** the tooltip and the table read `Sí`

#### Scenario: Filter on the full value
- **WHEN** `teatro_intencion` hides the prefixes of `categoria`, and a stat tile filters `categoria` equal to `1. No pagaría`
- **THEN** the tile shows the same value as before

#### Scenario: Color kept for the full value
- **WHEN** "2. Teatro" keeps color 7 for `4. No sabe` of `respuesta`, and `teatro_conteo` hides the prefixes of `respuesta`
- **THEN** the segments and the legend entry that read `No sabe` are in color 7

#### Scenario: Ranges are not prefixes
- **WHEN** a column that hides prefixes holds `10 - 14` and `1.5 horas`
- **THEN** they read `10 - 14` and `1.5 horas`

#### Scenario: Values that would read alike
- **WHEN** a column that hides prefixes holds `1. Otro` and `6. Otro`, and a widget shows both
- **THEN** both read with their full text

#### Scenario: Filters offer the full values
- **WHEN** an `analyst` adds a widget filter on `respuesta` of `teatro_conteo`, which hides its prefixes
- **THEN** the values offered read `1. Sí`, `2. Tal vez`, `3. No` and `4. No sabe`

### Requirement: Declared category order in the widget form
The widget form SHALL offer, for the group column and for a series column, the default order (the indicator's, else the chart's own) or an order of the widget's own, set by moving the preview's values up and down with buttons that work from the keyboard. The order of groups SHALL be off, with the reason, while the widget has a sort or an order by median. Changing the kind SHALL drop an order the new kind does not take.

#### Scenario: Own order from the preview
- **WHEN** an `analyst` chooses an order of their own for the series `respuesta` in the widget form
- **THEN** the form lists the values the preview shows, in its current order, each with a button to move it up and one to move it down

#### Scenario: No new query
- **WHEN** that `analyst` moves `No sabe` to the end
- **THEN** the preview is redrawn in the new order without querying the indicator again

#### Scenario: Back to the default
- **WHEN** an `analyst` chooses the default order again and saves the widget
- **THEN** the widget is saved with no order of its own

#### Scenario: Saved unchanged
- **WHEN** an `analyst` opens a widget that has no order of its own and saves it without a change
- **THEN** its saved settings are the same as before, and no version is made

#### Scenario: Order of groups off while sorted
- **WHEN** a column chart is ordered by the plotted value
- **THEN** the form shows its order of groups off, saying that the sort decides it, and still offers its order of series

#### Scenario: Order dropped by a kind change
- **WHEN** an `analyst` changes a column chart that has its own order of series into a pie chart
- **THEN** the saved pie chart has no order of series

#### Scenario: Listed value no longer shown
- **WHEN** a widget's own order lists `No sabe`, which its preview no longer shows
- **THEN** the form lists `No sabe` apart, as not shown, with a button to remove it

#### Scenario: Colors follow the order
- **WHEN** an `analyst` sets an order of series whose values have no color kept by value
- **THEN** the form says that those values take the palette's colors in that order, and that colors kept by value fix a value's color

### Requirement: Widgets without a declared category order
A widget that has no order of its own, and whose indicator declares no order, no values kept last and no hidden prefixes for the columns it groups or splits by, SHALL show its values in the same order, with the same text and colors, as before declared orders existed: on screen, in images, on paper, in scheduled emails and workbooks, and in MCP results.

#### Scenario: Sorted by an order column
- **WHEN** a column chart is ordered by `orden` compared as text, whose values are `01` to `21`, and declares nothing
- **THEN** its columns read `01` to `21` from left to right, as before

#### Scenario: Demand curve
- **WHEN** the line chart "2.10. Cuánto pagaría por una boleta: curva de demanda" over `teatro_demanda` is grouped by `precio` and declares nothing
- **THEN** its points read `$0`, `$10.000`, `$20.000`, `$35.000`, `$50.000`, as before

#### Scenario: Numbered labels kept
- **WHEN** `teatro_conteo` declares nothing for `respuesta`
- **THEN** the legend of "2.8. Iría al teatro, por comuna" reads `1. Sí`, `2. Tal vez`, `3. No`, `4. No sabe`, as before

#### Scenario: Column names alone
- **WHEN** an indicator's column metadata gives `comuna` only a name to show
- **THEN** the widgets grouped by `comuna` keep their order and their colors

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
