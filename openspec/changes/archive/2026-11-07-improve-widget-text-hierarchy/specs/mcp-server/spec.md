## ADDED Requirements

### Requirement: Caption kept in sight through MCP
`create_widget` and `update_widget` SHALL take `caption: { hidden: false }`, alone or with an own `text`, to keep the caption line in sight under the widget's subtitle (see `dashboard-indicators` "Caption kept in sight"). `get_dashboard` and `get_dashboard_version` SHALL return it as saved. The tools' descriptions and the client instructions SHALL say that a subtitle takes the caption line of a bar, column, line or pie chart out of sight, its own text to the foot, unless it is set.

#### Scenario: Kept in sight through MCP
- **WHEN** Claude calls `update_widget` on "2.1. Asistió a espectáculos, por estrato" with the caption `{ "text": "% de los hogares que asistieron", "hidden": false }` and its subtitle
- **THEN** the widget is saved, a member who opens the dashboard sees the caption line above the columns and no caption chip, and `get_dashboard` returns the caption as given

#### Scenario: Caption left as before through MCP
- **WHEN** Claude calls `create_widget` for a column chart with a subtitle and the caption `{ "text": "% de los hogares" }`
- **THEN** the widget is saved with that caption, a member who opens the dashboard sees no caption line above its columns and "% de los hogares" as the first chip of its foot, and the result has the note of "Caption out of sight told through MCP"

#### Scenario: Descriptions say it
- **WHEN** a client lists the tools
- **THEN** the description of `create_widget` and `update_widget` says that a bar, column, line or pie widget with a subtitle shows its caption's own text in its foot, not above the chart, unless `caption.hidden` is `false`

### Requirement: Caption out of sight told through MCP
When a widget saved by `create_widget` or `update_widget` has its caption's own text out of sight under its subtitle (see `dashboard-indicators` "Caption under a subtitle"), the result SHALL hold a `notes` entry saying so: that the text shows in the widget's foot, not above the chart, and that `caption.hidden: false` keeps it above. A result with nothing to tell SHALL have no `notes`, as before. The note SHALL NOT block the save.

#### Scenario: Note on a caption out of sight
- **WHEN** Claude calls `update_widget` to give the column chart "2.1. Asistió a espectáculos, por estrato", whose caption text is "% de los hogares que asistieron", the subtitle "E1, % de los hogares de cada estrato que asistieron en el último año"
- **THEN** the widget is saved, and the result's `notes` says that "% de los hogares que asistieron" shows in the widget's foot and that `caption.hidden: false` keeps it above the chart

#### Scenario: No note when nothing moves
- **WHEN** Claude creates a column chart with a subtitle and no own caption text, a heat map with a subtitle and an own caption text, or a column chart whose caption is `{ "text": "% de los hogares", "hidden": false }`
- **THEN** the result has no `notes`

#### Scenario: Same rule as the page
- **WHEN** the shared cases of the caption rule are run against the API and the UI
- **THEN** the API notes a caption out of sight for exactly the cases where the UI draws a caption chip
