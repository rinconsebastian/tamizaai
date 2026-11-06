# Spec Delta

## MODIFIED Requirements

### Requirement: Indicator queries and widget data
`query_indicator` SHALL run a widget query (an indicator, a measure or a ratio, a column to group by, an optional series column, filters, and for histograms the ranges) without saving anything, and return what that widget would plot, formatted with the indicator's format and unit. `get_widget_data` SHALL return what one widget, or every widget of a dashboard, shows with the dashboard filters' default values or the values the call gives, including the message a widget shows when its indicator is not published or a column is gone. For a widget with a series, both SHALL return one value per pair of group and series value, the totals, the series in their order with "Other" when some were combined, and how many rows were left out. For a box plot, both SHALL return each box's group, number of values, minimum, first quartile, median, third quartile, maximum, mean, whisker ends and number of outliers, formatted, and SHALL NOT return a list of the outliers' values (a box's minimum and maximum are single figures, as a column profile's are). Both SHALL apply the row limit of widgets and say when it is reached.

Both SHALL also return, for each value of a widget with no series and for each total, its base n and, for a ratio, the sums of its numerator and denominator; for each cell of a widget with a series, its base n; the widget's base n when it has one; and, for a widget that leaves out no-data values, those values and what they held (see `dashboard-indicators`). `get_widget_data` SHALL also say which values have a small base under the threshold in force.

#### Scenario: Checking a widget before saving it
- **WHEN** Claude calls `query_indicator` for the ratio of `numerator` to `denominator` of `piped_water_rate` grouped by `municipality`
- **THEN** the result holds one value per municipality, formatted as a percent, and no widget is created

#### Scenario: Stale widget
- **WHEN** a widget groups by a column that its indicator no longer publishes
- **THEN** `get_widget_data` returns for it the message naming the missing column, and the other widgets' values

#### Scenario: Checking a stacked chart
- **WHEN** Claude calls `query_indicator` for `households_by_source` grouped by `municipality` with the series `water_source`
- **THEN** the result holds one value per municipality and water source, each municipality's and each water source's total, and the grand total

#### Scenario: Checking a box plot
- **WHEN** Claude calls `query_indicator` with the chart type box plot for `household_income` grouped by `municipality`
- **THEN** the result holds one box per municipality with its number of values, its five figures from minimum to maximum, its mean, its whisker ends and its number of outliers, and no list of its outliers

#### Scenario: Base figures of a widget
- **WHEN** Claude calls `get_widget_data` for "2.1. Asistió, por comuna" on "Teatro", whose small-base threshold is 30
- **THEN** each comuna has its value, numerator, denominator and n, and `COMUNA 7` and `SIN NOMBRE` are marked as small bases

#### Scenario: Measure without a base through MCP
- **WHEN** Claude calls `query_indicator` for the sum of `value` of `teatro_conteo`, which declares no base column
- **THEN** its rows hold no numerator, denominator or n

## ADDED Requirements

### Requirement: Base settings through MCP
`update_dashboard` SHALL set whether a dashboard shows its widgets' base n lines and its small-base threshold, 0 turning the threshold off, and `get_dashboard` and `get_dashboard_version` SHALL return both. `create_widget` and `update_widget` SHALL take a widget's own base n line and threshold and its option to leave out no-data values, with the rules of the UI.

#### Scenario: Threshold through MCP
- **WHEN** Claude calls `update_dashboard` on "Teatro" with the small-base threshold 30
- **THEN** its members see its small bases dimmed, `get_dashboard` returns the threshold 30, and one version is made

#### Scenario: No-data option on a stat tile through MCP
- **WHEN** Claude calls `create_widget` for a stat tile that leaves out no-data values
- **THEN** the call is refused with `mcp.validation` and the error on that option, and nothing is saved

### Requirement: Base metadata through MCP
`save_script` SHALL take an indicator's base column, values that mean no data and answer columns in `columns` with its column names, with the rules of the save form and the keep, replace and clear rules of `columns`. `list_indicators`, `get_script`, `get_indicator_columns` and `save_script`'s result SHALL return them.

#### Scenario: Answers declared by Claude
- **WHEN** Claude calls `save_script` for `comercio_lugar_compra` declaring `variable`, `orden` and `categoria` as the answers of single-choice questions and "Sin dato" as no data for `comuna`
- **THEN** the new version holds both, and `get_script` returns them

#### Scenario: Declarations replaced by labels alone
- **WHEN** Claude calls `save_script` for `teatro_hoy`, whose current version declares `categoria` as answers, with `columns` = `{ "numerator": { "label": "Hogares que asistieron" } }`
- **THEN** the new version declares no answers, since a given `columns` replaces the whole map

#### Scenario: Two base columns through MCP
- **WHEN** Claude calls `save_script` declaring both `hogares` and `personas` as the base
- **THEN** the call is refused with `mcp.validation` and the error on `personas`, and no version is created
