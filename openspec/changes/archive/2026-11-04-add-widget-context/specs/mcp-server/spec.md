# Spec Delta

## ADDED Requirements

### Requirement: Widget context through MCP
`create_widget` and `update_widget` SHALL take a widget's subtitle, footnote, caption, unit and description option in its display, on every chart type, refused and kept as a saved widget's are (see `dashboard-indicators` "Widget context validation"). `get_widget_data` SHALL format a widget's values with its own unit and return its subtitle, footnote and own caption text when it has them. A new config given to `update_widget` SHALL replace them with what it holds.

#### Scenario: Widget context through MCP
- **WHEN** Claude calls `create_widget` for a bar chart of `teatro_hoy` with the subtitle "¿En qué lugares ha visto teatro en el último año? (E4)", the caption hidden and the unit "de quienes asistieron"
- **THEN** the widget is saved, a member who opens the dashboard sees the subtitle and no caption line, and `get_widget_data` returns the subtitle and values formatted with "de quienes asistieron"

#### Scenario: Caption refused through MCP
- **WHEN** Claude calls `update_widget` with a caption that has its own text and is also hidden
- **THEN** the call is refused with `mcp.validation` and the error on `config.display.caption`, and nothing is saved

#### Scenario: Config replaced through MCP
- **WHEN** Claude calls `update_widget` on a widget that has a subtitle, with a new config that has none
- **THEN** the widget has no subtitle

#### Scenario: Widget data without these settings
- **WHEN** Claude calls `get_widget_data` on "Escuelas", none of whose widgets has these settings
- **THEN** each widget's result is as before these settings existed, with no subtitle, footnote or caption in it

### Requirement: Dashboard source through MCP
`update_dashboard` SHALL set or remove a dashboard's source with the rules of the page, as one more commit of the same request, and `get_dashboard` and `get_dashboard_version` SHALL return the source when the dashboard or the version has one. `list_dashboard_versions` SHALL name the kind of change a source change records.

#### Scenario: Source through MCP
- **WHEN** Claude calls `update_dashboard` on "Teatro" with the source "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas"
- **THEN** a member who opens "Teatro" sees that source under its widgets, and `get_dashboard` returns it

#### Scenario: Rename and source in one version
- **WHEN** Claude calls `update_dashboard` on "Teatro" with a new name and a source in one call
- **THEN** "Teatro" has one new version holding both

#### Scenario: Source removed through MCP
- **WHEN** Claude calls `update_dashboard` on "Teatro" with an empty source
- **THEN** "Teatro" has no source, and `get_dashboard` returns none

#### Scenario: Long source through MCP
- **WHEN** Claude calls `update_dashboard` with a source of 301 characters
- **THEN** the call is refused with `mcp.validation` and the error on `source`, and nothing is saved
