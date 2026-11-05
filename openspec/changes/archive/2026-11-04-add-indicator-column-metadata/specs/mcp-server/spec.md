# Spec Delta

## ADDED Requirements

### Requirement: Indicator column labels through MCP
`save_script` SHALL take for an indicator an optional `columns`: an object from a column's name to its properties, here its `label`. Left out, the current version's labels SHALL be kept; given, it SHALL replace them all, and `{}` SHALL clear them, with the rules and errors of the indicator's details. `list_indicators`, `get_script` and `save_script`'s result SHALL return an indicator's column labels, and `get_indicator_columns` SHALL give each column the label its indicator gives it.

#### Scenario: Labels saved through MCP
- **WHEN** Claude calls `save_script` for `teatro_hoy` with `columns` = `{ "numerator": { "label": "Hogares que asistieron" }, "denominator": { "label": "Hogares" } }`
- **THEN** the new version has those labels, `get_script` returns them, and a member who opens "Teatro" sees them in its widgets

#### Scenario: Client that leaves the labels out
- **WHEN** an AI client that knows nothing of column labels calls `save_script` for `teatro_hoy` with new code, its label, unit and format
- **THEN** the new version keeps the column labels of the version before it

#### Scenario: Labels cleared through MCP
- **WHEN** Claude calls `save_script` for `teatro_hoy` with `columns` = `{}`
- **THEN** the new version labels no column

#### Scenario: Label refused through MCP
- **WHEN** Claude calls `save_script` with a label of 81 characters for `numerator`
- **THEN** the call is refused with `mcp.validation` and the error on that label, and no version is created

#### Scenario: Labels of a table's columns
- **WHEN** Claude calls `get_indicator_columns` for `teatro_hoy`, which labels `numerator` "Hogares que asistieron" and not `orden`
- **THEN** the result gives `numerator` the label "Hogares que asistieron" and `orden` no label

### Requirement: Widget column labels through MCP
`create_widget`, `update_widget` and `query_indicator` SHALL take a widget's own column labels in `config.display.labels`, with the rules of the widget form. `get_widget_data` and `query_indicator` SHALL return for each widget `labels`: the name shown for each of its columns that has a label, its own or its indicator's, and no `labels` when none has. A new `config` SHALL replace the old one whole, its labels included.

#### Scenario: Widget labels through MCP
- **WHEN** Claude calls `create_widget` for a column chart of `teatro_hoy` with `config.display.labels` = `{ "denominator": "Hogares que asistieron" }`
- **THEN** a member who opens the dashboard sees the chart name its denominator "Hogares que asistieron", and `get_dashboard` returns the label

#### Scenario: Labels in widget data
- **WHEN** Claude calls `get_widget_data` for "Teatro", whose column chart grouped by `comuna` plots the ratio of `teatro_hoy`, which labels `numerator`, `denominator` and `comuna`
- **THEN** that widget's result gives the three labels under `labels`, and a widget whose columns have no label gives no `labels`

#### Scenario: Config replaced without labels
- **WHEN** Claude calls `update_widget` with a new `config` that has no `display.labels` for a widget that had its own labels
- **THEN** the widget has no labels of its own and names its columns with its indicator's labels

#### Scenario: Too many widget labels through MCP
- **WHEN** Claude calls `create_widget` with labels for 11 columns
- **THEN** the call is refused with `mcp.validation` and the error on `config.display.labels`, and nothing is saved
