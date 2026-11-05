# Spec Delta

## ADDED Requirements

### Requirement: Highlighted values and the neutral color through MCP
`create_widget` and `update_widget` SHALL take a widget's highlighted values with the same validation as the page, and `update_dashboard` the neutral color, number 0, in the colors kept by value; `get_dashboard` SHALL return both as saved. The tool descriptions and the server's instructions SHALL say where colors kept by value apply, the bars of a chart with no series and the boxes of a box plot included, and that a highlight sets values apart in one widget only.

#### Scenario: Highlight through MCP
- **WHEN** Claude calls `create_widget` for a bar chart of `teatro_hoy` grouped by `categoria` in color 1 that highlights `Centro cultural o casa de la cultura`
- **THEN** a member who opens the dashboard sees that bar in color 1 and the others in the neutral color, and `get_dashboard` returns the highlight

#### Scenario: Neutral through MCP
- **WHEN** Claude calls `update_dashboard` on "2. Teatro" with the neutral color, 0, kept for `4. No sabe` of `respuesta`
- **THEN** a member who opens "2. Teatro" sees `4. No sabe` in the neutral color, and `get_dashboard` returns the entry with the color 0

#### Scenario: Highlight on a heat map through MCP
- **WHEN** Claude calls `create_widget` for a heat map that highlights a value
- **THEN** the call is refused with `mcp.validation` and the error on the highlight, and nothing is saved

#### Scenario: Where kept colors apply, in the descriptions
- **WHEN** a client reads the description of `update_dashboard`
- **THEN** it says that a color kept by value also colors the bars of a bar or column chart with no series and the boxes of a box plot by their group value, and that the neutral color, 0, can be kept for any number of values of a column
