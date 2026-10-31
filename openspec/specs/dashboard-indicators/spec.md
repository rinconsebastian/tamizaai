# dashboard-indicators Specification

## Purpose
Let a project member build their own view of the project's computed indicators: a canvas of widgets, each a chart or map over the published indicator table, added, arranged and configured rather than fixed by the dashboard itself.

## Requirements

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
A stat tile SHALL show one number for its indicator: the measure summed over every row the query matches, or the ratio of sums, formatted with the indicator's format and unit.

#### Scenario: Single figure
- **WHEN** a stat tile is configured for the indicator `households_visited` with no filters and the measure `value`
- **THEN** it shows one number, the sum of `value` over every row

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
A widget SHALL show its plotted values with the format and unit of its indicator: `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value. The same SHALL apply to a stat tile, axes, legends, tooltips and "Show as table"; a map's tooltip columns use their own format.

#### Scenario: Rate shown as a percentage
- **WHEN** the indicator `piped_water_rate` has the format `percent` and a stat tile shows the value 0.42
- **THEN** the tile shows `42%`

#### Scenario: Count with a unit
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`, and a column chart shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households`

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over the indicator `survey_progress`, whose format is `percent`, shows the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** the tooltip reads the plotted value as a percentage and the target as `20`, not `2,000%`

### Requirement: Ratio of sums
A widget SHALL be able to plot, instead of one measure, the ratio of the sums of two numeric columns of its indicator over the rows of each group, with no value for a group whose denominator sums to zero.

#### Scenario: Rate over several rows
- **WHEN** the indicator `piped_water_rate` has three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50, and a widget plots the ratio of `numerator` to `denominator` grouped by `municipality`
- **THEN** it plots 0.5 for `Cali`

#### Scenario: Zero denominator
- **WHEN** a group's denominators sum to 0
- **THEN** the widget shows no value for that group

### Requirement: Indicators dashboard and its indicators
Every project member SHALL be able to open any dashboard of the project, which SHALL list the project's indicators with their label, unit and format and whether each has published a table. The dashboard's widgets SHALL come first on the page; the list of indicators MAY follow them inside a disclosure that is collapsed once any indicator has published and opens on demand. While no indicator has published, every dashboard SHALL say so, with instructions, SHALL show the list of indicators expanded as part of that message, and SHALL still list the widgets already configured on it. A user who is not a member SHALL get the same answer as for a project that does not exist.

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
Pointing at a shape, tapping it, or reaching it from the keyboard SHALL show its tooltip. The tooltip SHALL show the group value as its heading, then the plotted value with the indicator's label, format and unit, then the widget's tooltip columns in their order, each with its label. Its text, labels included, SHALL be shown as text, never as markup.

#### Scenario: City block
- **WHEN** a member points at block `0042` on a map of the indicator `survey_progress` (label "Survey progress", format `percent`) whose tooltip columns are `address` labelled "Dirección", `sample_target` labelled "Meta muestral" and `achieved` labelled "Encuestas logradas", and the block's row has `Cra 5 # 10-20`, 20, 9 and a value of 0.45
- **THEN** the tooltip reads `0042`, `Survey progress: 45%`, `Dirección: Cra 5 # 10-20`, `Meta muestral: 20` and `Encuestas logradas: 9`

#### Scenario: Markup in a value
- **WHEN** a tooltip column holds the text `<img src=x onerror=alert(1)>`, or a tooltip column's label is that text
- **THEN** the tooltip shows those characters as text, and nothing runs

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
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep its title, chart type, query and the dashboard filters it ignores, and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

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
A widget's series values SHALL be listed in natural order, then a missing series value, shown as "(none)", then "Other" when some were combined (see "Number of series"). The series a widget shows, their order and their colors SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees the same series in the same colors and legend.

#### Scenario: Natural order
- **WHEN** a stacked column chart has the series `round` with the values `10`, `2` and `1`
- **THEN** its legend and segments read `1`, `2`, `10`

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a stacked column chart shows `Piped`, `Well` and `River` in three colors, and a member sets a dashboard filter after which no row has `Well`
- **THEN** `Piped` and `River` keep their colors and the legend still lists `Well`

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
A heat map SHALL draw its group values as rows, in natural order (2 before 10, a missing group last), and its series values as columns, each cell shaded by its value on a sequential scale from the smallest to the largest value shown, with a legend of that range. A cell with no value SHALL be drawn empty and marked as having no value, unlike a cell whose value is 0.

#### Scenario: Cells shaded
- **WHEN** a heat map of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the cell with the largest value is the darkest

#### Scenario: Age bands in natural order
- **WHEN** a heat map is grouped by the text column `age_band`, whose values are `0-4`, `5-9`, `10-14` and `80+`
- **THEN** its rows read `0-4`, `5-9`, `10-14`, `80+` from top to bottom, not in alphabetical order

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
A cross table widget SHALL show its group values as rows, in natural order (2 before 10, a missing group last), and its series values as columns, each cell holding the value of its pair in the indicator's format, with a "Total" column, a "Total" row and the grand total (see "Totals of a widget with a series"). A pair with no rows SHALL read "—". A table wider than its widget SHALL scroll inside it, keeping the group column in view.

#### Scenario: Contingency table
- **WHEN** a cross table of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the last row and column hold the totals

#### Scenario: Groups in natural order
- **WHEN** a cross table is grouped by the text column `round`, whose values are `1`, `2` and `10`
- **THEN** its rows read `1`, `2`, `10`

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
