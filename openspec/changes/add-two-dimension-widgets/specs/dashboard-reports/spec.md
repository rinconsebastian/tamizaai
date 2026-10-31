# Spec Delta

## MODIFIED Requirements

### Requirement: Email body
After the header, the email body SHALL show the value of each stat tile. Then it SHALL show one block per other widget, in the dashboard's reading order: top to bottom, then left to right. Each block SHALL hold the widget's title and the first 10 rows of its "Show as table" view. When a widget has more rows, the block SHALL say how many more it has. A widget with a series SHALL show at most its first 6 series columns and its "Total" column in the block, and SHALL say how many more columns it has. The body SHALL say once where the full tables are: in the attachment, or, when the attachment was left off, that it was too large.

#### Scenario: Stat tiles first
- **WHEN** "Housing" has a column chart at the top, then two stat tiles below it
- **THEN** the body shows the two stat tile values before the column chart's block

#### Scenario: Long table
- **WHEN** a bar chart of "Housing" has 32 bars
- **THEN** its block shows the first 10 rows and says the widget has 22 more rows

#### Scenario: Wide cross table
- **WHEN** a heat map of "Housing" has 25 series values
- **THEN** its block shows the first 6 series columns and the "Total" column, and says the widget has 19 more columns

#### Scenario: Value formatted
- **WHEN** a stat tile over the indicator `piped_water_rate`, whose format is `percent`, has the value 0.42 in an American English email
- **THEN** the body shows `42%`

### Requirement: Widget tables in a scheduled email
A widget's table in the body and in the workbook SHALL have the same columns and rows as its "Show as table" view, in the same order. The one exception is a map's table, which SHALL NOT include the kind of geometry. A histogram's table SHALL list each range with the number of values in it. A pie chart's table SHALL list every group value with its share. A widget with a series SHALL be shown as its cross table, with its "Total" column and "Total" row; a 100% layout and a Likert widget SHALL show shares, and a widget that left rows out SHALL say how many in its note.

#### Scenario: Histogram
- **WHEN** a histogram of "Housing" has 10 buckets over the values of 80 municipalities
- **THEN** its table lists the 10 ranges, each with the number of municipalities in it

#### Scenario: Pie chart
- **WHEN** a pie chart of "Housing" has 12 group values and draws the smallest ones as one slice
- **THEN** its table lists all 12 group values, each with its value and share

#### Scenario: Map
- **WHEN** a map of blocks with the tooltip columns "Dirección" and "Meta muestral" is in the email
- **THEN** its table lists each block, its plotted value, its address and its sample target, and no shape kind

#### Scenario: Line chart order
- **WHEN** a line chart is grouped by `round` with the values `1`, `2` and `10`
- **THEN** its table lists them as `1`, `2`, `10`

#### Scenario: Stacked columns
- **WHEN** a stacked column chart of "Housing" is grouped by `municipality` with the series `water_source`
- **THEN** its table lists each municipality with one column per water source, a "Total" column and a "Total" row

#### Scenario: Population pyramid
- **WHEN** a population pyramid with `Female` on the left and `Male` on the right left out 15 rows
- **THEN** its table lists each age band with a `Female`, a `Male` and a "Total" column, and its note says that 15 rows were left out

### Requirement: Numbers in the workbook
In each widget's sheet, plotted values, row counts, pie shares, the shares of a 100% layout or a Likert widget, totals and numeric tooltip values SHALL be numbers, not text. Plotted values and totals SHALL follow the indicator's format: `count` a whole number with thousands separators, `decimal` and `currency` two decimals, `percent` a percentage. Row counts SHALL be whole numbers, and shares percentages. A tooltip value SHALL follow its tooltip column's format, as in its tooltip. A unit SHALL be written in the column's header.

#### Scenario: Percentage cell
- **WHEN** a column chart over `piped_water_rate`, whose format is `percent`, has the value 0.42 for `Cali`
- **THEN** the cell holds the number 0.42, shown as a percentage

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over `survey_progress`, whose format is `percent`, has the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** its cell holds the number 20, shown as `20`, not as a percentage

#### Scenario: Unit in the header
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`
- **THEN** its value column's header names `households`, and its cells hold whole numbers

#### Scenario: Shares of a Likert widget
- **WHEN** a Likert widget's `Cali` row has 30% `Agree`
- **THEN** that cell holds the number 0.3, shown as a percentage, whatever the indicator's format
