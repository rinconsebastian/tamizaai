## MODIFIED Requirements

### Requirement: Dashboards and widgets through MCP
`create_dashboard`, `update_dashboard`, `duplicate_dashboard` and `remove_dashboard` SHALL add, rename, move to a position, change the filters of, change the colors of (palette and colors kept by value), duplicate and remove dashboards with the rules of the UI. `get_dashboard` SHALL return the dashboard's palette and colors kept by value. `create_widget` SHALL add a widget with a title, a chart type, a query and an optional position and size on the 12-column grid, placed under the dashboard's widgets when none is given; `update_widget` SHALL change any of them or move the widget to another dashboard. Both SHALL take every chart type and setting the widget form offers, including the series column, the layout of bars and columns, the sides of a population pyramid, the scale of a Likert widget, and a widget's color and sequential scale, with the same validation. A dashboard's members SHALL see the change the next time their page reads the dashboard.

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

#### Scenario: Colors through MCP
- **WHEN** Claude calls `update_dashboard` on "Water access" with the palette `earth` and color 5 kept for `Female` of `sex`, then `create_widget` for a map with the green scale
- **THEN** a member who opens "Water access" sees its charts in the colors of *Earth*, `Female` in color 5, and the map on the green scale, and `get_dashboard` returns the palette and the color kept

#### Scenario: Unknown palette through MCP
- **WHEN** Claude calls `update_dashboard` with the palette `neon`
- **THEN** the call is refused with `mcp.validation` and the error on the palette, and nothing is saved
