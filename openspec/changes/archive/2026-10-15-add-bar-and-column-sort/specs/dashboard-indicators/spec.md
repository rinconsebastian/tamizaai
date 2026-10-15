# Spec Delta

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Columns available to a widget
A widget's query SHALL be able to name any column of its indicator's published table, discovered the same way the clean data dashboard discovers a table's columns. It SHALL be offered the table's numeric columns as measures, every column as a group column, and, for a bar or column widget, every column as a sort column.

#### Scenario: Extra column grouped on
- **WHEN** the indicator `attendance` publishes a table with a `round` column and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `round`

#### Scenario: Extra column sorted on
- **WHEN** the indicator `coverage` publishes a table with a `sort_key` column and an analyst configures the order of a column chart over it
- **THEN** `sort_key` is offered as a sort column

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, against the measure or ratio, as one line. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order").

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

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
