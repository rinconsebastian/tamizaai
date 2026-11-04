# Spec Delta

## ADDED Requirements

### Requirement: Chart annotations through MCP
`create_widget` and `update_widget` SHALL take a widget's reference lines, its choice of straight segments or steps and its data labels in its display, with the validation of the widget form, and `get_dashboard` and `get_dashboard_version` SHALL return them as saved. A new chart type sent alone SHALL be refused, naming the display setting, when the kept display holds a setting the new type does not take.

#### Scenario: Lines through MCP
- **WHEN** Claude calls `create_widget` for a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy` grouped by `comuna`, with a `mean` line and an `overall` line
- **THEN** the widget is saved, a member who opens the dashboard sees both lines, and `get_dashboard` returns them in the widget's display

#### Scenario: Overall line over a measure through MCP
- **WHEN** Claude calls `create_widget` for a bar chart of the measure `value` of `teatro_conteo` with an `overall` line
- **THEN** the call is refused with `mcp.validation` and the error on that line's kind, and nothing is saved

#### Scenario: Chart type changed alone
- **WHEN** Claude calls `update_widget` with only the chart type pie for a column chart that has a `mean` line
- **THEN** the call is refused with `mcp.validation` and the error on the reference lines, and the widget is unchanged

### Requirement: Reference lines in MCP widget data
For a widget whose display has reference lines, `query_indicator` and `get_widget_data` SHALL return each line drawn with its kind, its label (its own, or the English default), its value and that value formatted as the result's other values are. A widget without reference lines SHALL return what it returned before.

#### Scenario: Lines in widget data
- **WHEN** Claude calls `get_widget_data` for a column chart with a `mean` line and an `overall` line
- **THEN** the result lists both lines with their kinds, the labels "Mean" and "Overall", their values and their formatted values

#### Scenario: Overall line previewed
- **WHEN** Claude calls `query_indicator` for a column chart grouped by `comuna` whose display has an `overall` line
- **THEN** the result holds the columns and the overall line, at the same value a stat tile with the same ratio and filters would show, and nothing is saved

#### Scenario: Widget without reference lines
- **WHEN** Claude calls `get_widget_data` for a widget with no reference lines
- **THEN** the result holds no reference lines, as before
