# dashboard-indicators Specification

## Purpose
Let a project member build their own view of the project's computed indicators: a canvas of widgets, each a chart or map over the published indicator table, added, arranged and configured rather than fixed by the dashboard itself.

## Requirements

### Requirement: Widgets
A member with the `analyst` or `admin` role SHALL be able to add, edit and remove widgets on the dashboard. Each widget SHALL have a title, a chart type (stat, bar, column, line, histogram, pie or map), and a query over exactly one indicator's published table. A `viewer` SHALL see the dashboard's widgets but SHALL NOT be able to add, edit, remove or arrange them.

#### Scenario: Analyst adds a widget
- **WHEN** an `analyst` adds a column chart titled "Coverage by municipality" over the indicator `coverage`
- **THEN** the widget is saved and shown to every member who opens the dashboard

#### Scenario: Viewer cannot edit
- **WHEN** a `viewer` tries to add, edit, remove or move a widget
- **THEN** the API responds 403

#### Scenario: Widget limit
- **WHEN** a project already has the maximum number of widgets
- **THEN** adding another fails with a message naming the limit

### Requirement: Widget layout
A widget SHALL have a position and size on a grid that an `analyst` or `admin` can move and resize. Reading order, for keyboard navigation and for a page showing several widgets' tables at once, SHALL follow the grid top-to-bottom, left-to-right.

#### Scenario: Widget moved
- **WHEN** an `analyst` drags a widget to a new position
- **THEN** the new position is saved and shown to every member who opens the dashboard afterward

### Requirement: Columns available to a widget
A widget's query SHALL be able to name any column of its indicator's published table, discovered the same way the clean data dashboard discovers a table's columns. It SHALL be offered the table's numeric columns as measures, every column as a group column, and, for a bar or column widget, every column as a sort column.

#### Scenario: Extra column grouped on
- **WHEN** the indicator `attendance` publishes a table with a `round` column and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `round`

#### Scenario: Extra column sorted on
- **WHEN** the indicator `coverage` publishes a table with a `sort_key` column and an analyst configures the order of a column chart over it
- **THEN** `sort_key` is offered as a sort column

### Requirement: Stat tile
A stat tile SHALL show one number for its indicator: the measure summed over every row the query matches, or the ratio of sums, formatted with the indicator's format and unit.

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `households_visited` with no filters and the measure `value`
- **THEN** it shows one number, the sum of `value` over every row

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, against the measure or ratio, as one line. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order").

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

### Requirement: Histogram
A histogram SHALL bucket the values of its measure or ratio, over the rows the query matches (or over the group values when it groups by a column), into equal-width ranges and count how many fall in each, with a configurable number of buckets.

#### Scenario: Distribution across many groups
- **WHEN** a histogram is configured for the indicator `coverage` with 10 buckets, grouped by `municipality`, over 80 municipalities with coverage ranging from 10% to 140%
- **THEN** it shows 10 bars, each counting the municipalities whose coverage falls in that bar's range

### Requirement: Pie chart
A pie chart SHALL show its indicator's measure as one slice per group value, sized by its share of the total.

#### Scenario: Shares of a total
- **WHEN** a pie chart is configured for the indicator `households_visited` grouped by `municipality`
- **THEN** each municipality is one slice, sized by its share of the summed total

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
- the published table no longer has its measure, its ratio columns, its group column, its geometry column or its sort column.

#### Scenario: Indicator not published
- **WHEN** a widget is configured over the indicator `attendance`, whose script has never published
- **THEN** the widget says `attendance` has not published yet instead of its chart

#### Scenario: Column removed by a script change
- **WHEN** a script change removes the column a widget grouped by
- **THEN** the widget shows a message naming that column instead of its chart

#### Scenario: Sort column removed by a script change
- **WHEN** a script change removes the column a column chart was ordered by
- **THEN** the widget shows a message naming that column instead of its chart

### Requirement: Indicator table export
Every project member SHALL be able to export the full published table of one indicator, exactly as published, to CSV and XLSX, through the same single-use download link as the clean data dashboard.

#### Scenario: Viewer exports
- **WHEN** a `viewer` exports the table of the indicator `coverage` to XLSX
- **THEN** they receive a single-use download link to a file with every published row and column of `coverage`

### Requirement: Indicator formatting
A widget SHALL show its values with the format and unit of its indicator: `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value. The same formatting SHALL apply to a stat tile, axes, legends, tooltips and the "Show as table" view.

#### Scenario: Rate shown as a percentage
- **WHEN** the indicator `piped_water_rate` has the format `percent` and a stat tile shows the value 0.42
- **THEN** the tile shows `42%`

#### Scenario: Count with a unit
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`, and a column chart shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households`

### Requirement: Ratio of sums
A widget SHALL be able to plot, instead of one measure, the ratio of the sums of two numeric columns of its indicator over the rows of each group, with no value for a group whose denominator sums to zero.

#### Scenario: Rate over several rows
- **WHEN** the indicator `piped_water_rate` has three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50, and a widget plots the ratio of `numerator` to `denominator` grouped by `municipality`
- **THEN** it plots 0.5 for `Cali`

#### Scenario: Zero denominator
- **WHEN** a group's denominators sum to 0
- **THEN** the widget shows no value for that group

### Requirement: Indicators dashboard and its indicators
Every project member SHALL be able to open the project's indicators dashboard, which SHALL list the project's indicators with their label, unit and format and whether each has published a table. The widgets SHALL come first on the page; the list of indicators MAY follow them inside a disclosure that is collapsed once any indicator has published and opens on demand. While no indicator has published, it SHALL say so, with instructions, SHALL show the list of indicators expanded as part of that message, and SHALL still list any widgets already configured. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a published dashboard
- **WHEN** a `viewer` opens the indicators dashboard of a project where at least one indicator has published
- **THEN** the dashboard's widgets are shown

#### Scenario: Indicator list behind a disclosure
- **WHEN** a member opens the indicators dashboard of a project with 3 indicators, 2 of them published
- **THEN** the widgets come first, a collapsed disclosure reads "Indicators (3) · 2 published", and opening it lists the three indicators with label, unit, format and whether each has published

#### Scenario: No indicator has published
- **WHEN** none of a project's indicators has published, or the project has none
- **THEN** the dashboard says no indicator has published yet, with instructions and the list of indicators expanded, and still lists any configured widgets

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its indicators dashboard
- **THEN** the API responds 404

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie and map widgets), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value SHALL be summed, or combined as a ratio of sums, before being plotted.

#### Scenario: Default measure
- **WHEN** a widget is created over an indicator whose table has the columns `municipality`, `value` and `count`, without choosing a measure
- **THEN** it plots `value`

#### Scenario: Duplicate rows summed
- **WHEN** two rows of the indicator `households_visited` share the municipality `Cali`, with values 40 and 35, and a widget sums `value` grouped by `municipality`
- **THEN** the widget plots 75 for `Cali`

#### Scenario: Row limit
- **WHEN** a widget's query matches more rows than the configured limit
- **THEN** the widget shows that the limit was reached instead of silently showing only some of the rows

### Requirement: Bar and column charts of one indicator
A bar or column chart SHALL plot the group values of its indicator against the measure or ratio, one bar or column per group value, in the widget's order (see "Bar and column order").

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
An `analyst` or `admin` SHALL be able to give a bar or column widget an order. The order is either by the plotted value (the measure or the ratio) or by one column of the indicator's published table, and either ascending or descending.

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

#### Scenario: Only bar and column widgets have an order
- **WHEN** an `analyst` saves a line chart with an order
- **THEN** the API responds 400 with an error on the widget's order

#### Scenario: Widget without an order
- **WHEN** a member opens a column chart that was saved without an order
- **THEN** its columns follow the default order

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
