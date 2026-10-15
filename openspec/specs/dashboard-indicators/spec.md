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
A widget's query SHALL be able to name any column of its indicator's published table, discovered the same way the clean data dashboard discovers a table's columns, and SHALL be offered the table's numeric columns as measures and every column as a group column.

#### Scenario: Extra column grouped on
- **WHEN** the indicator `attendance` publishes a table with a `round` column and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `round`

### Requirement: Stat tile
A stat tile SHALL show one number for its indicator: the measure summed over every row the query matches, or the ratio of sums, formatted with the indicator's format and unit.

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `households_visited` with no filters and the measure `value`
- **THEN** it shows one number, the sum of `value` over every row

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator, ordered by their natural or chosen order, against the measure or ratio, as one line.

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
A widget whose indicator has not published, or whose measure, ratio columns, group column or geometry column the published table no longer has, SHALL show a message naming what is missing instead of failing the dashboard.

#### Scenario: Indicator not published
- **WHEN** a widget is configured over the indicator `attendance`, whose script has never published
- **THEN** the widget says `attendance` has not published yet instead of its chart

#### Scenario: Column removed by a script change
- **WHEN** a script change removes the column a widget grouped by
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
A bar or column chart SHALL plot the group values of its indicator against the measure or ratio, one bar or column per group value.

#### Scenario: One indicator, several groups
- **WHEN** a column chart is configured for the indicator `coverage` grouped by `municipality`
- **THEN** it shows one column per municipality
