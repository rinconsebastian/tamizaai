# Spec Delta

## MODIFIED Requirements

### Requirement: Indicator queries and widget data
`query_indicator` SHALL run a widget query (an indicator, a measure or a ratio, a column to group by, an optional series column, filters, and for histograms the ranges) without saving anything, and return what that widget would plot, formatted with the indicator's format and unit. `get_widget_data` SHALL return what one widget, or every widget of a dashboard, shows with the dashboard filters' default values or the values the call gives, including the message a widget shows when its indicator is not published or a column is gone. For a widget with a series, both SHALL return one value per pair of group and series value, the totals, the series in their order with "Other" when some were combined, and how many rows were left out. Both SHALL apply the row limit of widgets and say when it is reached.

#### Scenario: Checking a widget before saving it
- **WHEN** Claude calls `query_indicator` for the ratio of `numerator` to `denominator` of `piped_water_rate` grouped by `municipality`
- **THEN** the result holds one value per municipality, formatted as a percent, and no widget is created

#### Scenario: Stale widget
- **WHEN** a widget groups by a column that its indicator no longer publishes
- **THEN** `get_widget_data` returns for it the message naming the missing column, and the other widgets' values

#### Scenario: Checking a stacked chart
- **WHEN** Claude calls `query_indicator` for `households_by_source` grouped by `municipality` with the series `water_source`
- **THEN** the result holds one value per municipality and water source, each municipality's and each water source's total, and the grand total

### Requirement: Dashboards and widgets through MCP
`create_dashboard`, `update_dashboard`, `duplicate_dashboard` and `remove_dashboard` SHALL add, rename, move to a position, change the filters of, duplicate and remove dashboards with the rules of the UI. `create_widget` SHALL add a widget with a title, a chart type, a query and an optional position and size on the 12-column grid, placed under the dashboard's widgets when none is given; `update_widget` SHALL change any of them or move the widget to another dashboard. Both SHALL take every chart type and setting the widget form offers, including the series column, the layout of bars and columns, the sides of a population pyramid and the scale of a Likert widget, with the same validation. A dashboard's members SHALL see the change the next time their page reads the dashboard.

#### Scenario: Dashboard for a situation
- **WHEN** Claude creates the dashboard "Water access", adds a stat tile and a map of `piped_water_rate` without positions, and a dashboard filter on `municipality`
- **THEN** the dashboard has the filter and the two widgets one under the other, and a viewer who opens it sees them

#### Scenario: Move to a position
- **WHEN** Claude calls `update_dashboard` to move "Water access" to the first position while another member moves another dashboard
- **THEN** only one of the two moves is applied, and the other is refused as in the UI with `dashboards.orderChanged`

#### Scenario: Pyramid through MCP
- **WHEN** Claude calls `create_widget` for a population pyramid of `population` grouped by `age_band` with the series `sex`, `Female` on the left and `Male` on the right
- **THEN** the widget is saved and a member who opens the dashboard sees the pyramid

#### Scenario: Invalid Likert scale through MCP
- **WHEN** Claude calls `create_widget` for a Likert widget whose scale puts a positive value before a negative one
- **THEN** the call is refused with `mcp.validation` and the error on the scale, and nothing is saved
