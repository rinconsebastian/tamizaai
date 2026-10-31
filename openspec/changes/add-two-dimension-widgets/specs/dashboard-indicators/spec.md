# Spec Delta

## MODIFIED Requirements

### Requirement: Widgets
A member with the `analyst` or `admin` role SHALL be able to add, edit and remove widgets on any dashboard. Each widget SHALL belong to the one dashboard it is added on or moved to, and have a title, a chart type (stat, bar, column, line, histogram, pie, map, heat map, population pyramid, diverging Likert bars or cross table), and a query over exactly one indicator's published table. A dashboard SHALL hold at most the configured number of widgets. A `viewer` SHALL see every dashboard's widgets but SHALL NOT be able to add, edit, remove or arrange them.

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

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, against the measure or ratio. Without a series it SHALL draw one line; with a series it SHALL draw one line per series value (see "Series in a widget"), and a series that has no value for a group SHALL have a gap there instead of a point. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order").

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

#### Scenario: One line per series
- **WHEN** a line chart over `attendance` is grouped by `round` with values `1`, `2` and `3`, with the series `municipality`, whose values are `Cali` and `Buga`
- **THEN** it draws two lines, one for `Cali` and one for `Buga`, each through its three rounds, with a legend naming both

#### Scenario: Gap in a line
- **WHEN** in that line chart `Buga` has no row for round `2`
- **THEN** the `Buga` line has points at rounds `1` and `3` and no segment through round `2`

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

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie, map, heat map, population pyramid, diverging Likert and cross table widgets), an optional series column (see "Series in a widget"), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value, and the same series value when there is a series, SHALL be summed, or combined as a ratio of sums, before being plotted. On a dashboard, the rows SHALL also match the dashboard filters that narrow the widget (see `dashboard-filters`); those filters do not count toward the widget's 50.

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

### Requirement: Bar and column charts of one indicator
A bar or column chart SHALL plot the group values of its indicator against the measure or ratio, one bar or column per group value, in the widget's order (see "Bar and column order"). With a series, each bar SHALL be split by series value as its layout says (see "Bars and columns by series").

Without a chosen order, bars SHALL follow the group values in the group column's own order:
- numbers from smallest to largest;
- dates and timestamps from earliest to latest;
- false before true;
- text in alphabetical order.

A group value that is an empty text is not a missing group value: its bar has a blank label and takes its alphabetical place, which is first. The bar for a missing group value, shown as "(none)", SHALL come last.

A bar chart SHALL draw its first bar at the top, and a column chart SHALL draw its first column on the left. The "Show as table" view SHALL list the groups in the same order as the chart.

#### Scenario: One indicator, several groups
- **WHEN** a column chart is configured for the indicator `coverage` grouped by `municipality`
- **THEN** it shows one column per municipality

#### Scenario: Default order
- **WHEN** a column chart without an order is grouped by the integer column `round`, whose values are 10, 2 and 1
- **THEN** its columns read 1, 2, 10 from left to right, and its "Show as table" view lists them in the same order

#### Scenario: Missing group value last
- **WHEN** a bar chart without an order is grouped by `municipality`, and some rows have no municipality
- **THEN** the bar shown as "(none)" comes last

### Requirement: Bar and column order
An `analyst` or `admin` SHALL be able to give a bar or column widget an order. The order is either by the plotted value (the measure or the ratio) or by one column of the indicator's published table, and either ascending or descending. For a widget with a series, the plotted value of a bar SHALL be its total over every series value (see "Totals of a widget with a series"), in every layout, and the order SHALL never change the order of the series inside a bar.

When the sort column is not the group column, a bar's sort value SHALL be the smallest value of that column over the rows the bar stands for, compared as described in "Sort column compared as text or as a number". The same rule SHALL apply in both directions.

Bars with no sort value SHALL come last in both directions. Bars with equal sort values SHALL keep the default order between them in both directions. The order SHALL be applied before the row limit, so a widget that reaches the limit shows the first bars in its order.

A widget whose chart type is not bar or column SHALL NOT have an order, and saving one with an order SHALL be rejected. A widget without an order SHALL use the default order.

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

## ADDED Requirements

### Requirement: Series in a widget
Bar, column and line widgets MAY have a series column; heat map, population pyramid, diverging Likert and cross table widgets SHALL have one; stat tile, histogram, pie and map widgets SHALL NOT. The series column SHALL differ from the group column. Saving a widget that breaks these rules SHALL be rejected with an error on the series column.

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

### Requirement: Series order and colors
A widget's series values SHALL be listed in natural order, with a missing series value, shown as "(none)", last. The series a widget shows, their order and their colors SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees the same series in the same colors and legend.

#### Scenario: Natural order
- **WHEN** a stacked column chart has the series `round` with the values `10`, `2` and `1`
- **THEN** its legend and segments read `1`, `2`, `10`

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a stacked column chart shows `Piped`, `Well` and `River` in three colors, and a member sets a dashboard filter after which no row has `Well`
- **THEN** `Piped` and `River` keep their colors and the legend still lists `Well`

### Requirement: Number of series
A bar, column or line widget SHALL show at most 8 series, and a heat map or a cross table at most 30. When a widget's own rows hold more series values than that, it SHALL show the ones with the largest totals, one fewer than the limit, and SHALL combine the others into one series named "Other", last, summed or as a ratio of their sums.

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
A heat map SHALL draw its group values as rows, in the group column's own order as bars use, and its series values as columns, each cell shaded by its value on a sequential scale from the smallest to the largest value shown, with a legend of that range. A cell with no value SHALL be drawn empty and marked as having no value, unlike a cell whose value is 0.

#### Scenario: Cells shaded
- **WHEN** a heat map of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the cell with the largest value is the darkest

#### Scenario: Empty cell
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell is drawn empty and its tooltip says it has no value

### Requirement: Population pyramid
A population pyramid SHALL name two values of its series column, one drawn to the left and one to the right of a central axis, both required and different. Its group values SHALL be rows in natural order, the first at the bottom, with bars measured outward on one scale and labelled without signs. Rows with any other series value SHALL be left out, rows with no group or series value SHALL draw no bar, and the widget SHALL say how many rows it left out. Values SHALL follow the indicator's format.

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
A cross table widget SHALL show its group values as rows, in the order bars use, and its series values as columns, each cell holding the value of its pair in the indicator's format, with a "Total" column, a "Total" row and the grand total (see "Totals of a widget with a series"). A pair with no rows SHALL read "—". A table wider than its widget SHALL scroll inside it, keeping the group column in view.

#### Scenario: Contingency table
- **WHEN** a cross table of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the last row and column hold the totals

#### Scenario: Pair with no rows
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell reads "—" and the totals count only the rows there are

### Requirement: Marks of a widget with a series
A widget with a series SHALL show a legend of its series in their order, and SHALL never rely on color alone: hovering or focusing a bar, segment, point or cell SHALL show its group, its series value and its value in the indicator's format. A legend entry SHALL show its series' color next to its name in text colors.

#### Scenario: Segment tooltip
- **WHEN** a member hovers the `Piped` segment of `Cali` in a stacked column chart
- **THEN** a tooltip reads `Cali`, `Piped` and its value with the indicator's unit

#### Scenario: Keyboard
- **WHEN** a member moves keyboard focus onto a heat map's cells
- **THEN** each focused cell shows the same tooltip as on hover

### Requirement: "Show as table" for a widget with a series
The "Show as table" view of a widget with a series SHALL be its cross table: groups as rows in the chart's order, series values as columns in their order, and the totals. A 100% layout and a Likert widget SHALL show each cell's share and the group's total as its own column; a population pyramid SHALL show its left and right columns and their total.

#### Scenario: Stacked chart as a table
- **WHEN** a member switches a stacked column chart of `households_by_source` to "Show as table"
- **THEN** it lists each municipality with one column per water source, a "Total" column and a "Total" row

#### Scenario: Likert as a table
- **WHEN** a member switches a Likert widget to "Show as table"
- **THEN** it lists each group with one share per scale value, in the scale's order, and the group's total
