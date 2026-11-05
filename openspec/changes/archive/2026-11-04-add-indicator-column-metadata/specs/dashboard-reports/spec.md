# Spec Delta

## MODIFIED Requirements

### Requirement: Widget tables in a scheduled email
A widget's table in the body and in the workbook SHALL have the same columns and rows as its "Show as table" view, in the same order. The one exception is a map's table, which SHALL NOT include the kind of geometry. A histogram's table SHALL list each range with the number of values in it. A pie chart's table SHALL list every group value with its share. A box plot's table SHALL list each box with its number of values, minimum, first quartile, median, third quartile, maximum, mean and number of outliers. A widget with a series SHALL be shown as its cross table, with its "Total" column and "Total" row; a 100% layout and a Likert widget SHALL show shares in place of values and have the "Total" column but no "Total" row; and a widget that left rows out SHALL say how many in its note. The headers SHALL name the widget's columns as its "Show as table" view does (see `dashboard-indicators` "Column labels in widget texts").

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

#### Scenario: Box plot
- **WHEN** a box plot of "Housing" is grouped by `municipality`, which has 12 values, and is ordered by median descending
- **THEN** its table lists the 12 municipalities from the largest median to the smallest, each with its number of values, minimum, first quartile, median, third quartile, maximum, mean and number of outliers

#### Scenario: Headers with column labels
- **WHEN** a column chart of "Teatro" plots the ratio of `numerator` to `denominator` of `teatro_hoy`, labelled "Teatro hoy", grouped by `comuna`, and the indicator labels the three columns "Hogares que asistieron", "Hogares" and "Comuna"
- **THEN** its table's headers in the body and in the workbook read "Comuna" and "Teatro hoy: Hogares que asistieron / Hogares"

## ADDED Requirements

### Requirement: Column labels in a scheduled email
The caption a scheduled email writes for each widget, in its HTML body and its plain text, SHALL name the widget's columns as the dashboard does. The names SHALL be the widget's own labels, else the labels of the current version of its indicator's script, else the columns' own names, read when the email is built. A dashboard whose widgets and indicators label no column SHALL be sent exactly as before.

#### Scenario: Caption of a stat tile
- **WHEN** the email of "Teatro" holds a stat tile of the ratio of `numerator`, labelled "Hogares que asistieron", to `denominator`, labelled "Hogares", of `teatro_hoy`, labelled "Teatro hoy"
- **THEN** the body and the plain text describe the tile as "Teatro hoy: Hogares que asistieron / Hogares"

#### Scenario: Label changed before a send
- **WHEN** an analyst changes the label of `denominator` of `teatro_hoy` to "Hogares encuestados" on Tuesday, and the email of "Teatro" is sent on Wednesday
- **THEN** that email names the denominator "Hogares encuestados"

#### Scenario: Dashboard without labels
- **WHEN** the email of "Escuelas" is sent, and none of its widgets or of their indicators labels a column
- **THEN** its captions and headers name `numerator`, `denominator` and the group columns as before
