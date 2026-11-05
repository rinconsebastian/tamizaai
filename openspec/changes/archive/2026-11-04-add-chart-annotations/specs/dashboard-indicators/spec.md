# Spec Delta

## MODIFIED Requirements

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, against the measure or ratio. Without a series it SHALL draw one line; with a series it SHALL draw one line per series value (see "Series in a widget"), and a series that has no value for a group SHALL have a gap there instead of a point. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order"). Its points SHALL be joined by straight segments, or by steps when the widget says so (see "Step lines").

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

## ADDED Requirements

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
- **WHEN** a bar chart of `teatro_hoy` grouped by `categoria` with the filter `variable` = `Lugar (E4)`, in which every place has the same denominator in every cell of the table, has a `mean` and an `overall` line
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
