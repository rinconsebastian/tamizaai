# Spec Delta

## ADDED Requirements

### Requirement: Stat tiles through MCP
`create_widget` and `update_widget` SHALL take a stat tile's statistic and secondary lines with the rules of the widget form, and SHALL refuse them on another chart type with `mcp.validation` and the error's key. `query_indicator` and `get_widget_data` SHALL return a stat tile's statistic with its value and n, and `get_widget_data` its value without the dashboard filters, formatted, while a filter in force narrows a tile that shows that line.

#### Scenario: Median card through MCP
- **WHEN** Claude calls `create_widget` for a stat tile with the statistic `median` over `teatro_precio_hogar` filtered to "Pagó por la última boleta (E7)"
- **THEN** the widget is saved, and `get_widget_data` returns its statistic, the value 40000, `40,000 pesos` and the n 37

#### Scenario: Statistic on a bar chart through MCP
- **WHEN** Claude calls `create_widget` for a bar chart with the statistic `median`
- **THEN** the call is refused with `mcp.validation` and the error on `config.statistic`, and nothing is saved

#### Scenario: Value without the filters through MCP
- **WHEN** Claude calls `get_widget_data` with "Comuna" set to `COMUNA 1` for a stat tile that shows the line without filters
- **THEN** the result holds the tile's value for `COMUNA 1` and its value without the dashboard filters, each formatted

#### Scenario: Chart type changed alone through MCP
- **WHEN** Claude calls `update_widget` with only the chart type `bar` on a stat tile with a statistic
- **THEN** the call is refused with `mcp.validation` and the error on `config.statistic`, and nothing is saved

### Requirement: Ratio over declared categories through MCP
`query_indicator` and `get_widget_data` SHALL return the figures the dashboard shows for a ratio over declared answers: the base counted once across the answers of a single-choice column in each value, total and cell and in the denominator returned with it, and, for a widget that would add up several answers of a multiple-choice column, the message the dashboard shows and no rows.

#### Scenario: Card over several answers through MCP
- **WHEN** Claude calls `query_indicator` for a stat tile over `cruces_intencion`, whose `categoria` is declared as the answers of a single-choice question, keeping `variable` "Componentes con sí o tal vez" and `categoria` in `1`, `2`, `3` and `4`
- **THEN** the result holds its value formatted as `95.17% de los hogares`, not `23.79% de los hogares`

#### Scenario: Refused card through MCP
- **WHEN** Claude calls `get_widget_data` for a dashboard holding a stat tile over two reasons of `escuelas_razones_no`, whose `categoria` is declared as the answers of a multiple-choice question
- **THEN** the result gives that widget no rows and the message naming `categoria`, and the other widgets' rows
