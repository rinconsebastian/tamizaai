# Spec Delta

## ADDED Requirements

### Requirement: Declared category order through MCP
`save_script` SHALL take a column's `order`, `last` and `stripPrefix` in its `columns`, and `get_script`, `list_indicators` and `get_indicator_columns` SHALL return them. `create_widget` and `update_widget` SHALL take a widget's own order, with the rules of the UI. `query_indicator` and `get_widget_data` SHALL list rows, series, cells and boxes in the widget's order, each value with its full text, and SHALL list them as before for a widget that declares nothing.

#### Scenario: Order through save_script
- **WHEN** Claude calls `save_script` for `teatro_intencion` with `columns` that give `categoria` the order `1. Sí`, `2. Tal vez`, `3. No`, `4. No sabe` kept last and `stripPrefix`
- **THEN** `get_script` returns those settings for `categoria`, and a member sees the widgets over `categoria` in that order, without the prefixes

#### Scenario: Widget order through MCP
- **WHEN** Claude calls `create_widget` for a stacked column chart of `barrera_tipo` series with the order of series `Acceso`, `Interés`, `Otra` in its display settings
- **THEN** the widget is saved, `get_dashboard` returns that order, and a member sees the segments in that order

#### Scenario: Full values in results
- **WHEN** Claude calls `get_widget_data` for the 100% bar chart "2.8. Iría al teatro, por comuna", whose indicator declares an order and hides the prefixes of `respuesta`
- **THEN** the result lists the series in the widget's order with their full values, such as `1. Sí`, so a filter Claude writes from them matches

#### Scenario: Order of groups with a sort refused
- **WHEN** Claude calls `create_widget` for a bar chart ordered by the plotted value that also has its own order of groups
- **THEN** the call is refused with `mcp.validation` and the error on the widget's order of groups, and nothing is saved

#### Scenario: Results of a widget that declares nothing
- **WHEN** Claude calls `query_indicator` for a line chart whose indicator declares nothing for its group column
- **THEN** the rows come in the same order as before declared orders existed
