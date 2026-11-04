# Spec Delta

## ADDED Requirements

### Requirement: Number formats through MCP
The `formatted` texts of `query_indicator` and `get_widget_data` SHALL be written in the project's regional format when the project has one, with a no-break space before `%` in a Spanish format, and in American English otherwise. They SHALL take the widget's decimals in `get_widget_data` and the indicator's in `query_indicator`, and SHALL never be abbreviated. The numbers themselves SHALL be returned as before. `get_project` SHALL return the project's regional format when it has one.

#### Scenario: Widget data in the project's format
- **WHEN** the Duitama project's regional format is `es-CO` and Claude calls `get_widget_data` for the stat tile over `teatro_hoy` with the value 0.4685
- **THEN** the result holds the value 0.4685 and the formatted text `46,85 % de los hogares`, with a no-break space before `%`

#### Scenario: Project without a regional format
- **WHEN** the project has no regional format and Claude calls `get_widget_data` for that tile
- **THEN** the formatted text is `46.85% de los hogares`, as before this change

#### Scenario: Widget decimals in widget data
- **WHEN** a project with no regional format has a stat tile over `teatro_hoy` that sets 1 decimal, and Claude calls `get_widget_data` for it
- **THEN** its formatted text is `46.9% de los hogares`

#### Scenario: Abbreviated tile in widget data
- **WHEN** Claude calls `get_widget_data` for a stat tile over `comercio_gastos_media` that reads `1 M pesos` on screen, in a project with no regional format
- **THEN** its formatted text is `1,016,279 pesos`

#### Scenario: Format in the project
- **WHEN** Claude calls `get_project` for the Duitama project, whose regional format is `es-CO`
- **THEN** the result gives `numberLocale` `es-CO`, and for a project with no regional format it gives none

### Requirement: Decimals through MCP
`save_script` SHALL take an indicator's decimals, 0 to 4: left out, the current version's decimals SHALL be kept, and `null` SHALL clear them. `list_indicators`, `get_project`, `list_scripts`, `get_script` and the result of `save_script` SHALL give an indicator's decimals when it sets them.

#### Scenario: Decimals saved through MCP
- **WHEN** Claude calls `save_script` for `teatro_precio_hogar` with the format `count`, the unit `pesos` and 0 decimals
- **THEN** a new version sets 0 decimals, `list_indicators` gives them, and its box plots write whole numbers

#### Scenario: Decimals kept when left out
- **WHEN** an analyst set 1 decimal on `teatro_hoy` in the editor and Claude saves a new version of it with `save_script` without `decimals`
- **THEN** the new version still sets 1 decimal

#### Scenario: Decimals cleared through MCP
- **WHEN** Claude calls `save_script` for `teatro_hoy` with `decimals` set to `null`
- **THEN** the new version sets no decimals, and its widgets show the digits of the format

#### Scenario: Decimals out of range through MCP
- **WHEN** Claude calls `save_script` with 7 decimals
- **THEN** the call is refused with `mcp.validation` and the error on `decimals`, and no version is added

### Requirement: Widget decimals and abbreviations through MCP
`create_widget` and `update_widget` SHALL take a widget's decimals and abbreviation in its display, with the validation of the widget form, and `get_dashboard` and `get_dashboard_version` SHALL return them as saved. A new chart type sent alone SHALL be refused, naming the abbreviation, when the kept display holds the abbreviation setting and the new type does not take it.

#### Scenario: Widget decimals through MCP
- **WHEN** Claude calls `create_widget` for a stat tile over `teatro_hoy` with 1 decimal in its display
- **THEN** the widget is saved, a member who opens the dashboard sees `46,9 % de los hogares` in a project whose regional format is `es-CO`, and `get_dashboard` returns the decimals

#### Scenario: Abbreviation refused through MCP
- **WHEN** Claude calls `create_widget` for a heat map that abbreviates its numbers
- **THEN** the call is refused with `mcp.validation` and the error on `config.display.compact`, and nothing is saved

#### Scenario: Chart type changed alone through MCP
- **WHEN** Claude calls `update_widget` with only the chart type `heatmap` for a stat tile that abbreviates its value
- **THEN** the call is refused with `mcp.validation` and the error on `config.display.compact`, and the widget is unchanged
