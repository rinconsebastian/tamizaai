# Spec Delta

## MODIFIED Requirements

### Requirement: Pie chart
A pie chart SHALL show its indicator's measure as one slice per group value, sized by its share of the total. Its slices SHALL be drawn from the largest, with "Other" last, unless a declared order applies (see "Declared category order applied"): the slices then follow that order, and "Other" is still last. Its accessible summary SHALL name the largest slice, whatever the order. Its slices' colors and which group values fold into "Other" (past the eighth color, the smallest) SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees each slice in the same color. A declared order SHALL NOT change which group values fold into "Other". Its colors SHALL follow `dashboard-colors`.

#### Scenario: Shares of a total
- **WHEN** a pie chart is configured for the indicator `households_visited` grouped by `municipality`
- **THEN** each municipality is one slice, sized by its share of the summed total

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a pie chart grouped by `water_source` shows `Piped` larger than `Well`, and a member sets a dashboard filter after which `Well` is larger than `Piped`
- **THEN** `Piped` and `Well` keep their colors, and the slices are sized by the filtered rows

#### Scenario: Other kept under a dashboard filter
- **WHEN** a pie chart's own rows hold 10 group values, so the 3 smallest fold into "Other", and a member sets a dashboard filter after which one of those 3 is larger than the others
- **THEN** that value is still part of "Other"

#### Scenario: Largest slice first without a declared order
- **WHEN** a pie chart grouped by `water_source` with no declared order has 60 for `Piped`, 30 for `Well` and 10 for `River`
- **THEN** its slices and its legend read `Piped`, `Well`, `River`, as before declared orders existed

#### Scenario: Slices in a declared order
- **WHEN** the same pie chart has its own order of groups `River`, `Well`, `Piped`
- **THEN** its slices and its legend read `River`, `Well`, `Piped`, whatever their sizes

#### Scenario: Largest slice named in a declared order
- **WHEN** a screen reader reads the summary of that pie chart, in the order `River`, `Well`, `Piped`
- **THEN** the summary names `Piped` as the largest slice

#### Scenario: Declared order does not choose the slices kept
- **WHEN** a pie chart's own rows hold 10 group values, so the 3 smallest fold into "Other", and its order lists one of those 3 first
- **THEN** that value is still part of "Other", and "Other" is the last slice

### Requirement: Series order and colors
A widget's series values SHALL be listed in natural order, then a missing series value, shown as "(none)", then "Other" when some were combined (see "Number of series"). When the widget or its indicator declares an order for the series column, that order SHALL take the place of the natural order (see "Declared category order applied"), and "(none)" and "Other" SHALL still come last; a population pyramid's sides and a Likert widget's scale SHALL keep the order the widget gives them. The series a widget shows, their order and their colors SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees the same series in the same colors and legend. The colors SHALL be the dashboard's palette, with the colors kept by value of `dashboard-colors`.

#### Scenario: Natural order
- **WHEN** a stacked column chart has the series `round` with the values `10`, `2` and `1`
- **THEN** its legend and segments read `1`, `2`, `10`

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a stacked column chart shows `Piped`, `Well` and `River` in three colors, and a member sets a dashboard filter after which no row has `Well`
- **THEN** `Piped` and `River` keep their colors and the legend still lists `Well`

#### Scenario: Color kept by value
- **WHEN** a stacked column chart has the series `water_source` and its dashboard keeps color 6 for `River`
- **THEN** `River`'s segments and its legend entry are in color 6

#### Scenario: Answers in the order of the question
- **WHEN** the indicator `comercio_lugar_compra` declares for `categoria` the order `Centro de Duitama`, `Centro comercial de Duitama`, `Almacenes del barrio`, `Vendedores ambulantes`, and a grouped column chart over it is grouped by `estrato` with the series `categoria`
- **THEN** its legend and the columns of each estrato read in that order, not in alphabetical order

#### Scenario: Value kept last among the series
- **WHEN** a widget's series column holds `Sí`, `No`, `No sabe` and `Tal vez`, and its indicator declares no order but keeps `No sabe` last
- **THEN** its legend reads `No`, `Sí`, `Tal vez`, `No sabe`

#### Scenario: None and Other after the values kept last
- **WHEN** a stacked column chart shows 6 series, "(none)" and "Other", and its indicator keeps `Otra razón` last
- **THEN** its legend ends with `Otra razón`, "(none)" and "Other", in that order

#### Scenario: Pyramid sides keep their places
- **WHEN** the indicator `population` declares for `sex` the order `Male`, `Female`, and a population pyramid over it has `Female` on the left
- **THEN** `Female` is still drawn on the left and `Male` on the right

### Requirement: Bar and column order
An `analyst` or `admin` SHALL be able to give a bar or column widget an order. The order is either by the plotted value (the measure or the ratio) or by one column of the indicator's published table, and either ascending or descending. For a widget with a series, the plotted value of a bar SHALL be its total over every series value (see "Totals of a widget with a series"), in every layout, and the order SHALL never change the order of the series inside a bar.

When the sort column is not the group column, a bar's sort value SHALL be the smallest value of that column over the rows the bar stands for, compared as described in "Sort column compared as text or as a number". The same rule SHALL apply in both directions.

Bars with no sort value SHALL come last in both directions. Bars with equal sort values SHALL keep the default order between them in both directions. The order SHALL be applied before the row limit, so a widget that reaches the limit shows the first bars in its order.

A box plot SHALL have only the order described in "Box plot order". A widget whose chart type is not bar, column or box plot SHALL NOT have an order, and saving one with an order SHALL be rejected. A widget without an order SHALL use the default order, or its declared order of groups when one applies.

The order of this requirement is a sort. A declared order of values (see "Declared category order applied") is not a sort, and SHALL be allowed on the kinds "Declared category order of a widget" lists. A sort SHALL win over the order its indicator declares: with a sort, the sort SHALL decide the order of the bars, except that the values the indicator keeps last, and after them the bar shown as "(none)", SHALL come at the end. A declared order SHALL be applied to the bars the query returns, after the row limit. A widget SHALL NOT have both a sort and its own order of groups.

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

#### Scenario: Declared order without a sort
- **WHEN** the indicator `teatro_hoy` keeps `Sin dato` last for `comuna`, and a column chart over it grouped by `comuna`, with no sort and no filter on `comuna`, shows `COMUNA 1` to `COMUNA 7`, `SIN NOMBRE` and `Sin dato`
- **THEN** its columns end with `SIN NOMBRE` and then `Sin dato`

#### Scenario: Sorted bars keep the values kept last at the end
- **WHEN** the indicator `comercio_comer_fuera` keeps `Otro` last for `categoria`, and the bar chart "4.5. Dónde come la mayoría de las veces cuando sale" is ordered by the plotted value ascending, `Otro` being its smallest bar
- **THEN** `Otro` is its last bar, and the other bars keep their ascending order

#### Scenario: Series of sorted bars in their declared order
- **WHEN** a stacked bar chart ordered by the plotted value descending has the series `barrera_tipo`, whose indicator declares the order `Acceso`, `Interés`, `Otra`
- **THEN** its bars are ordered by their totals, and inside every bar the segments read `Acceso`, `Interés`, `Otra`

#### Scenario: Sort and own order of groups refused
- **WHEN** an `analyst` saves a column chart ordered by the column `orden` that also has its own order of groups
- **THEN** the API responds 400 with an error on the widget's order of groups

### Requirement: Line chart
A line chart SHALL plot the group values of its indicator in their natural order, or in a declared order when one applies (see "Declared category order applied"), against the measure or ratio. Without a series it SHALL draw one line; with a series it SHALL draw one line per series value (see "Series in a widget"), and a series that has no value for a group SHALL have a gap there instead of a point. Natural order compares runs of digits as numbers, so `2` comes before `10`, and puts a missing group value last. A line chart SHALL NOT have an order (see "Bar and column order").

#### Scenario: Trend over a grouped column
- **WHEN** a line chart is configured for the indicator `attendance` grouped by a `round` column with values `1`, `2` and `3`
- **THEN** it draws one line through the three points in that order

#### Scenario: One line per series
- **WHEN** a line chart over `attendance` is grouped by `round` with values `1`, `2` and `3`, with the series `municipality`, whose values are `Cali` and `Buga`
- **THEN** it draws two lines, one for `Cali` and one for `Buga`, each through its three rounds, with a legend naming both

#### Scenario: Gap in a line
- **WHEN** in that line chart `Buga` has no row for round `2`
- **THEN** the `Buga` line has points at rounds `1` and `3` and no segment through round `2`

#### Scenario: Points in a declared order
- **WHEN** the line chart "2.10. Cuánto pagaría por una boleta: curva de demanda" over `teatro_demanda`, grouped by `precio`, has its own order of groups `$50.000`, `$35.000`, `$20.000`, `$10.000`, `$0`
- **THEN** its points read in that order from left to right

### Requirement: Heat map
A heat map SHALL draw its group values as rows, in natural order (2 before 10, a missing group last) or in a declared order when one applies (see "Declared category order applied"), and its series values as columns, each cell shaded by its value on a sequential scale from the smallest to the largest value shown, with a legend of that range. A cell with no value SHALL be drawn empty and marked as having no value, unlike a cell whose value is 0.

#### Scenario: Cells shaded
- **WHEN** a heat map of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the cell with the largest value is the darkest

#### Scenario: Age bands in natural order
- **WHEN** a heat map is grouped by the text column `age_band`, whose values are `0-4`, `5-9`, `10-14` and `80+`
- **THEN** its rows read `0-4`, `5-9`, `10-14`, `80+` from top to bottom, not in alphabetical order

#### Scenario: Empty cell
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell is drawn empty and its tooltip says it has no value

#### Scenario: Rows in a declared order
- **WHEN** the heat map "2.11. Lo que pagó frente a lo que pagaría" over `teatro_coherencia` follows its indicator's order for `rango_pagado`
- **THEN** its rows read in that order from top to bottom

### Requirement: Population pyramid
A population pyramid SHALL name two values of its series column, one drawn to the left and one to the right of a central axis, both required and different. Its group values SHALL be rows in natural order, or in a declared order when one applies (see "Declared category order applied"), the first at the bottom, with bars measured outward on one scale and labelled without signs. Rows with any other series value SHALL be left out, rows with no group or series value SHALL draw no bar, and the widget SHALL say how many rows it left out. Values SHALL follow the indicator's format.

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

#### Scenario: Bands in a declared order
- **WHEN** a population pyramid over `population` grouped by `age_band` has its own order of groups `10-14`, `5-9`, `0-4`
- **THEN** `10-14` is the bottom row and `0-4` the top row

### Requirement: Cross table widget
A cross table widget SHALL show its group values as rows, in natural order (2 before 10, a missing group last) or in a declared order when one applies (see "Declared category order applied"), and its series values as columns, each cell holding the value of its pair in the indicator's format, with a "Total" column, a "Total" row and the grand total (see "Totals of a widget with a series"). A pair with no rows SHALL read "—". A table wider than its widget SHALL scroll inside it, keeping the group column in view.

#### Scenario: Contingency table
- **WHEN** a cross table of `households_by_source` is grouped by `municipality` with the series `water_source`
- **THEN** each municipality is a row, each water source a column, and the last row and column hold the totals

#### Scenario: Groups in natural order
- **WHEN** a cross table is grouped by the text column `round`, whose values are `1`, `2` and `10`
- **THEN** its rows read `1`, `2`, `10`

#### Scenario: Pair with no rows
- **WHEN** `Buga` has no row for `River`
- **THEN** that cell reads "—" and the totals count only the rows there are

#### Scenario: Rows in a declared order
- **WHEN** a cross table over `teatro_hoy` is grouped by `comuna`, whose indicator keeps `Sin dato` last
- **THEN** its rows end with `SIN NOMBRE`, `Sin dato` and then the "Total" row

### Requirement: Box plot order
A box plot without an order SHALL list its boxes in natural order (2 before 10, a missing group last), or in a declared order of groups when one applies (see "Declared category order applied"). An `analyst` or `admin` SHALL be able to order it by median, ascending or descending; boxes with equal medians SHALL keep the natural order between them. An order by median SHALL be applied before the row limit, which counts boxes. A box plot that reaches the limit SHALL say so. Ordering a box plot by a column, or one with no group column, SHALL be rejected with an error on the order.

An order by median SHALL win over the order its indicator declares, except that the values the indicator keeps last, and after them the box shown as "(none)", SHALL come at the end. A box plot SHALL NOT have both an order by median and its own order of boxes.

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

#### Scenario: Boxes in a declared order
- **WHEN** a box plot over `centro_tarifa_hogar` grouped by `vehiculo`, with no order by median, has its own order of groups `Todos los vehículos`, `Carro`, `Moto`
- **THEN** its boxes read `Todos los vehículos`, `Carro`, `Moto`

#### Scenario: Order by median keeps the values kept last at the end
- **WHEN** a box plot over `comercio_gastos_hogar` ordered by median descending is grouped by `estrato`, whose indicator keeps `No sabe` last
- **THEN** the `No sabe` box comes last, and the other boxes stay ordered by median

#### Scenario: Order by median and own order of boxes refused
- **WHEN** an `analyst` saves a box plot ordered by median that also has its own order of groups
- **THEN** the API responds 400 with an error on the widget's order of groups

## ADDED Requirements

### Requirement: Declared category order of a widget
An `analyst` or `admin` SHALL be able to give a widget its own order of group values, on every kind but stat tile, histogram and map, and of series values, on a widget with a series other than a pyramid or a Likert widget: each a list of at most 100 texts of up to 500 characters, none twice; an empty list is no order. It SHALL be saved with the widget, kept by a copy or a move, and changing it SHALL NOT query again. Any other order SHALL be rejected with an error on it.

#### Scenario: Own order of answers
- **WHEN** an `analyst` gives a bar chart over `comercio_lugar_compra` grouped by `categoria` the order `Centro de Duitama`, `Centro comercial de Duitama`, `Almacenes del barrio`
- **THEN** every member sees its bars in that order, the other values after them

#### Scenario: Order of groups on a stat tile
- **WHEN** an `analyst` saves a stat tile, a histogram or a map with an order of groups
- **THEN** the API responds 400 with an error on the order of groups

#### Scenario: Order of groups without a group column
- **WHEN** an `analyst` saves a box plot with no group column and an order of groups
- **THEN** the API responds 400 with an error on the order of groups

#### Scenario: Order of series without a series
- **WHEN** an `analyst` saves a column chart with no series column and an order of series
- **THEN** the API responds 400 with an error on the order of series

#### Scenario: Order of series of a pyramid or a Likert widget
- **WHEN** an `analyst` saves a population pyramid or a Likert widget with an order of series
- **THEN** the API responds 400 with an error on the order of series, which says that the sides or the scale set that order

#### Scenario: Too many values
- **WHEN** an `analyst` saves a widget whose order of groups lists 101 values
- **THEN** the API responds 400 with an error on the order of groups naming the limit of 100

#### Scenario: Value listed twice
- **WHEN** an `analyst` saves a widget whose order of series lists `No sabe` twice
- **THEN** the API responds 400 with an error on the second `No sabe`

#### Scenario: Empty order
- **WHEN** an `analyst` saves a widget whose order of groups is an empty list
- **THEN** the widget is saved with no order of groups, and its stored settings are those of a widget without one

#### Scenario: Order kept by a copy
- **WHEN** an `analyst` duplicates "2. Teatro", which holds a widget with its own order of series
- **THEN** that widget's copy has the same order of series

#### Scenario: Order kept by a move
- **WHEN** an `analyst` moves a widget with its own order of groups from "2. Teatro" to "8. Cruces"
- **THEN** it follows the same order on "8. Cruces"

### Requirement: Declared category order applied
A widget's values SHALL follow its own order, else its indicator's order for the column, else today's order for its kind; a sort or an order by median keeps ordering groups (see "Bar and column order" and "Box plot order"). Listed values SHALL come first, in order, then the others in today's order, then the values kept last, then "(none)" and "Other". Values SHALL match as exact text, a listed value not shown SHALL be skipped, and an order SHALL NOT change which values are shown or combined.

#### Scenario: Indicator order for every widget
- **WHEN** `comercio_lugar_compra` declares an order for `categoria`, and two widgets on two dashboards group by `categoria` without an order of their own
- **THEN** both list its values in that order

#### Scenario: Widget order over the indicator's
- **WHEN** `comercio_lugar_compra` declares an order for `categoria`, and a bar chart over it has its own order of groups
- **THEN** the bar chart follows its own order

#### Scenario: Values not listed
- **WHEN** a cross table's indicator declares for `respuesta` the order `Sí`, `No`, and its series column also holds `Tal vez` and `No sabe`
- **THEN** its columns read `Sí`, `No`, `No sabe`, `Tal vez`, then "Total"

#### Scenario: Missing value after the values kept last
- **WHEN** `teatro_hoy` keeps `Sin dato` last for `comuna`, and a heat map over it grouped by `comuna` also has rows with no comuna
- **THEN** its last rows read `Sin dato` and then "(none)"

#### Scenario: Own order over a value kept last
- **WHEN** an indicator keeps `No sabe` last for `respuesta`, and a widget's own order of series lists `No sabe` second
- **THEN** the widget shows `No sabe` second

#### Scenario: Exact text
- **WHEN** an order lists `sí` and the widget shows `Sí`
- **THEN** `Sí` takes no place from the order and follows the listed values

#### Scenario: Order does not choose the series kept
- **WHEN** a stacked column chart's series column holds 10 values, and its order lists first a value whose total is among the 3 smallest
- **THEN** that value is still combined into "Other"

### Requirement: Declared category order in charts and tables
Wherever a widget lists its values, it SHALL use the order of "Declared category order applied": the axis of bars, columns, lines and box plots, the rows and columns of a heat map, a cross table and Likert bars, the rows of a pyramid, the slices and legend of a pie, a series legend and its marks, the order the arrow keys move through marks, and the rows and columns of "Show as table".

#### Scenario: Legend, segments and table
- **WHEN** a 100% column chart with the series `respuesta` follows the order `Sí`, `Tal vez`, `No`, `No sabe`
- **THEN** its legend, the segments of every column and the columns of its "Show as table" view read in that order

#### Scenario: Keyboard order
- **WHEN** a keyboard user steps with the right arrow through the columns of a heat map whose columns follow a declared order
- **THEN** the focus moves through the cells in that order

#### Scenario: Heat map as a table
- **WHEN** a member shows as a table a heat map whose rows follow its indicator's order for `rango_pagado`
- **THEN** the table's rows read in that order from top to bottom

### Requirement: Numbered category prefixes hidden
When an indicator column has `stripPrefix`, a widget SHALL show its values without a leading number of 1 to 3 digits, then `.`, `)`, `-`, `–` or `:` (spaces allowed before it) and a space ("1. Sí", "2) No", "3 - Tal vez"), unless what follows is empty or starts with a digit. Two values of a widget that would read alike SHALL keep their full text. The order, filters and colors kept by value SHALL still use the full value.

#### Scenario: Numbered answers
- **WHEN** `teatro_conteo` hides the prefixes of `respuesta`, and the 100% bar chart "2.8. Iría al teatro, por comuna" has the series `respuesta`
- **THEN** its legend reads `Sí`, `Tal vez`, `No`, `No sabe`, in the same order as before

#### Scenario: Everywhere the value is shown
- **WHEN** a member points at the `1. Sí` segment of that chart and then shows it as a table
- **THEN** the tooltip and the table read `Sí`

#### Scenario: Filter on the full value
- **WHEN** `teatro_intencion` hides the prefixes of `categoria`, and a stat tile filters `categoria` equal to `1. No pagaría`
- **THEN** the tile shows the same value as before

#### Scenario: Color kept for the full value
- **WHEN** "2. Teatro" keeps color 7 for `4. No sabe` of `respuesta`, and `teatro_conteo` hides the prefixes of `respuesta`
- **THEN** the segments and the legend entry that read `No sabe` are in color 7

#### Scenario: Ranges are not prefixes
- **WHEN** a column that hides prefixes holds `10 - 14` and `1.5 horas`
- **THEN** they read `10 - 14` and `1.5 horas`

#### Scenario: Values that would read alike
- **WHEN** a column that hides prefixes holds `1. Otro` and `6. Otro`, and a widget shows both
- **THEN** both read with their full text

#### Scenario: Filters offer the full values
- **WHEN** an `analyst` adds a widget filter on `respuesta` of `teatro_conteo`, which hides its prefixes
- **THEN** the values offered read `1. Sí`, `2. Tal vez`, `3. No` and `4. No sabe`

### Requirement: Declared category order in the widget form
The widget form SHALL offer, for the group column and for a series column, the default order (the indicator's, else the chart's own) or an order of the widget's own, set by moving the preview's values up and down with buttons that work from the keyboard. The order of groups SHALL be off, with the reason, while the widget has a sort or an order by median. Changing the kind SHALL drop an order the new kind does not take.

#### Scenario: Own order from the preview
- **WHEN** an `analyst` chooses an order of their own for the series `respuesta` in the widget form
- **THEN** the form lists the values the preview shows, in its current order, each with a button to move it up and one to move it down

#### Scenario: No new query
- **WHEN** that `analyst` moves `No sabe` to the end
- **THEN** the preview is redrawn in the new order without querying the indicator again

#### Scenario: Back to the default
- **WHEN** an `analyst` chooses the default order again and saves the widget
- **THEN** the widget is saved with no order of its own

#### Scenario: Saved unchanged
- **WHEN** an `analyst` opens a widget that has no order of its own and saves it without a change
- **THEN** its saved settings are the same as before, and no version is made

#### Scenario: Order of groups off while sorted
- **WHEN** a column chart is ordered by the plotted value
- **THEN** the form shows its order of groups off, saying that the sort decides it, and still offers its order of series

#### Scenario: Order dropped by a kind change
- **WHEN** an `analyst` changes a column chart that has its own order of series into a pie chart
- **THEN** the saved pie chart has no order of series

#### Scenario: Listed value no longer shown
- **WHEN** a widget's own order lists `No sabe`, which its preview no longer shows
- **THEN** the form lists `No sabe` apart, as not shown, with a button to remove it

#### Scenario: Colors follow the order
- **WHEN** an `analyst` sets an order of series whose values have no color kept by value
- **THEN** the form says that those values take the palette's colors in that order, and that colors kept by value fix a value's color

### Requirement: Widgets without a declared category order
A widget that has no order of its own, and whose indicator declares no order, no values kept last and no hidden prefixes for the columns it groups or splits by, SHALL show its values in the same order, with the same text and colors, as before declared orders existed: on screen, in images, on paper, in scheduled emails and workbooks, and in MCP results.

#### Scenario: Sorted by an order column
- **WHEN** a column chart is ordered by `orden` compared as text, whose values are `01` to `21`, and declares nothing
- **THEN** its columns read `01` to `21` from left to right, as before

#### Scenario: Demand curve
- **WHEN** the line chart "2.10. Cuánto pagaría por una boleta: curva de demanda" over `teatro_demanda` is grouped by `precio` and declares nothing
- **THEN** its points read `$0`, `$10.000`, `$20.000`, `$35.000`, `$50.000`, as before

#### Scenario: Numbered labels kept
- **WHEN** `teatro_conteo` declares nothing for `respuesta`
- **THEN** the legend of "2.8. Iría al teatro, por comuna" reads `1. Sí`, `2. Tal vez`, `3. No`, `4. No sabe`, as before

#### Scenario: Column names alone
- **WHEN** an indicator's column metadata gives `comuna` only a name to show
- **THEN** the widgets grouped by `comuna` keep their order and their colors
