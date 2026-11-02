# Spec Delta

## MODIFIED Requirements

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

### Requirement: Indicator formatting
A widget SHALL show its plotted values with the format and unit of its indicator: `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value. The same SHALL apply to a stat tile, axes, legends, tooltips and "Show as table"; a map's tooltip columns use their own format. A box plot over a `count` indicator SHALL show its figures as `decimal` does, with two decimals and the unit, because its quartiles, medians and means can fall between whole numbers; a box plot's numbers of values and of outliers SHALL be whole numbers whatever the indicator's format.

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

### Requirement: Ratio of sums
A widget SHALL be able to plot, instead of one measure, the ratio of the sums of two numeric columns of its indicator over the rows of each group, with no value for a group whose denominator sums to zero. A box plot SHALL instead divide each row's numerator by that row's own denominator (see "Box plot").

#### Scenario: Rate over several rows
- **WHEN** the indicator `piped_water_rate` has three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50, and a widget plots the ratio of `numerator` to `denominator` grouped by `municipality`
- **THEN** it plots 0.5 for `Cali`

#### Scenario: Zero denominator
- **WHEN** a group's denominators sum to 0
- **THEN** the widget shows no value for that group

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie, map, heat map, population pyramid, diverging Likert and cross table widgets), an optional series column (see "Series in a widget"), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value, and the same series value when there is a series, SHALL be summed, or combined as a ratio of sums, before being plotted, except in a box plot, where each row gives one value of its group's box (see "Box plot"). The row limit SHALL count what the widget plots: its groups, its pairs of group and series value when it has a series, the rows of a histogram with no group column, or the boxes of a box plot. On a dashboard, the rows SHALL also match the dashboard filters that narrow the widget (see `dashboard-filters`); those filters do not count toward the widget's 50.

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

### Requirement: Bar and column order
An `analyst` or `admin` SHALL be able to give a bar or column widget an order. The order is either by the plotted value (the measure or the ratio) or by one column of the indicator's published table, and either ascending or descending. For a widget with a series, the plotted value of a bar SHALL be its total over every series value (see "Totals of a widget with a series"), in every layout, and the order SHALL never change the order of the series inside a bar.

When the sort column is not the group column, a bar's sort value SHALL be the smallest value of that column over the rows the bar stands for, compared as described in "Sort column compared as text or as a number". The same rule SHALL apply in both directions.

Bars with no sort value SHALL come last in both directions. Bars with equal sort values SHALL keep the default order between them in both directions. The order SHALL be applied before the row limit, so a widget that reaches the limit shows the first bars in its order.

A box plot SHALL have only the order described in "Box plot order". A widget whose chart type is not bar, column or box plot SHALL NOT have an order, and saving one with an order SHALL be rejected. A widget without an order SHALL use the default order.

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

### Requirement: Moving a widget to another dashboard
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep its title, chart type, query, orientation and the dashboard filters it ignores, and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

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

## ADDED Requirements

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
A box plot without an order SHALL list its boxes in natural order (2 before 10, a missing group last). An `analyst` or `admin` SHALL be able to order it by median, ascending or descending; boxes with equal medians SHALL keep the natural order between them. An order by median SHALL be applied before the row limit, which counts boxes. A box plot that reaches the limit SHALL say so. Ordering a box plot by a column, or one with no group column, SHALL be rejected with an error on the order.

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
