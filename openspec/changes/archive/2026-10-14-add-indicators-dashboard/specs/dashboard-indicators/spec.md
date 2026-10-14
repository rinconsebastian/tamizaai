# Spec Delta

## Purpose

Let a project member build their own view of the project's computed indicators: a canvas of widgets, each a chart or map over the published indicator table, added, arranged and configured rather than fixed by the dashboard itself.

## ADDED Requirements

### Requirement: Indicators dashboard
Every project member SHALL be able to open the project's indicators dashboard. While script 3 has never published, it SHALL show that no indicators are published yet, with instructions, and SHALL still list any widgets already configured. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a published dashboard
- **WHEN** a `viewer` opens the indicators dashboard of a project whose script 3 has published
- **THEN** the dashboard's widgets are shown

#### Scenario: Script 3 has not published
- **WHEN** a project's script 3 has never published
- **THEN** the dashboard says no indicators are published yet, with instructions, and still lists any configured widgets

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its indicators dashboard
- **THEN** the API responds 404

### Requirement: Widgets
A member with the `analyst` or `admin` role SHALL be able to add, edit and remove widgets on the dashboard. Each widget SHALL have a title, a chart type (stat, bar, column, line, histogram, pie or map), and a query over the published indicator table. A `viewer` SHALL see the dashboard's widgets but SHALL NOT be able to add, edit, remove or arrange them.

#### Scenario: Analyst adds a widget
- **WHEN** an `analyst` adds a column chart titled "Coverage by municipality"
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
A widget's query SHALL be able to name any column of the published indicator table, not only `indicator`, `disaggregation`, `value`, `numerator` and `denominator`, discovered the same way the clean data dashboard discovers a table's columns.

#### Scenario: Extra column grouped on
- **WHEN** script 3's output has an extra column `year` and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `year`

### Requirement: Widget query
A widget's query SHALL select one or more indicators, a column to group by (`disaggregation` by default), the field to plot (`value`, `numerator` or `denominator`), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same indicator and group value SHALL be summed before being plotted.

#### Scenario: Default grouping
- **WHEN** a widget is created without choosing a group column
- **THEN** it groups by `disaggregation`

#### Scenario: Duplicate rows summed
- **WHEN** two rows of the published table share the indicator `coverage` and the disaggregation `Cali`, with values 40 and 35
- **THEN** the widget plots 75 for `Cali`

#### Scenario: Row limit
- **WHEN** a widget's query matches more rows than the configured limit
- **THEN** the widget shows that the limit was reached instead of silently showing only some of the rows

### Requirement: Stat tile
A stat tile SHALL show the plotted field's value for exactly one indicator, summed over every row the query matches.

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `response_rate` with no filters
- **THEN** it shows one number, the sum of `response_rate`'s plotted field over every row

### Requirement: Bar and column charts
A bar or column chart SHALL plot the group values against the plotted field, bars for group values and columns for indicators when more than one is selected, one series per indicator when more than one is selected.

#### Scenario: One indicator, several groups
- **WHEN** a column chart is configured for the indicator `coverage` grouped by `disaggregation`
- **THEN** it shows one column per disaggregation value

#### Scenario: Several indicators compared
- **WHEN** a bar chart is configured for the indicators `coverage` and `target` grouped by `disaggregation`
- **THEN** it shows one series per indicator, sharing the same disaggregation values on the axis

### Requirement: Line chart
A line chart SHALL plot the group values, ordered by their natural or chosen order, against the plotted field, one line per indicator when more than one is selected.

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

### Requirement: Histogram
A histogram SHALL bucket the plotted field's values, over the rows the query matches, into equal-width ranges and count how many rows fall in each, with a configurable number of buckets.

#### Scenario: Distribution across many groups
- **WHEN** a histogram is configured for the indicator `coverage` with 10 buckets, over 80 disaggregation values ranging from 10% to 140%
- **THEN** it shows 10 bars, each counting the disaggregation values whose coverage falls in that bar's range

### Requirement: Pie chart
A pie chart SHALL show exactly one indicator's plotted field as one slice per group value, sized by its share of the total.

#### Scenario: Shares of a total
- **WHEN** a pie chart is configured for the indicator `submissions` grouped by `disaggregation`
- **THEN** each disaggregation value is one slice, sized by its share of the summed total

### Requirement: Map widgets
A map widget SHALL use exactly one indicator and a geometry column the published table has. Rows whose geometry is a polygon SHALL be shown as filled regions, colored by the plotted field on a sequential scale; rows whose geometry is a point SHALL be shown as markers, colored or sized the same way. A row whose geometry is missing or cannot be parsed SHALL be left out of the map and counted.

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
A widget whose indicator, group column or geometry column the published table no longer has SHALL show a message naming what is missing instead of failing the dashboard.

#### Scenario: Column removed by a script change
- **WHEN** a script change removes the column a widget grouped by
- **THEN** the widget shows a message naming that column instead of its chart

### Requirement: Indicator table export
Every project member SHALL be able to export the full published indicator table, exactly as published, to CSV and XLSX, through the same single-use download link as the clean data dashboard.

#### Scenario: Viewer exports
- **WHEN** a `viewer` exports the indicator table to XLSX
- **THEN** they receive a single-use download link to a file with every published row and column
