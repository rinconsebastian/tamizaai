## MODIFIED Requirements

### Requirement: Tools and their roles
The MCP server SHALL offer these tools, each requiring the role given and acting on one project unless it says otherwise:

| Tool | Role | What it does |
|---|---|---|
| `list_projects` | any member | The person's projects, with their role and the effective access level of each |
| `get_project` | viewer | Name, form, time zone, submissions stored and last received, last synchronization, published tables, scripts and indicators (analyst), dashboards, last run |
| `get_form` | analyst | Questions, groups, repeats, choice lists, form versions in use, enumerator question, field check |
| `get_tables` | viewer (clean, output, indicators), analyst (raw, reference) | Tables of a source with columns, types, question labels, select lists and row counts |
| `profile_table` | as `get_tables` | Column profiles of one table |
| `get_fieldwork` | viewer | The performance dashboard's metrics for an optional range of days |
| `query_rows` | as `get_tables`; rows level | Rows of one table with the clean data dashboard's filters and sort, at most 100 per call |
| `get_authoring_guide` | analyst of a project whose access is not off | The script contract, the `ctx` members, the libraries and the templates of a kind |
| `list_scripts`, `get_script`, `list_script_versions` | analyst | Scripts, their current versions with author, time and origin, and any version's code |
| `save_script`, `remove_script`, `restore_script_version` | analyst | Save, remove or restore a script as a new version |
| `test_script`, `get_test` | analyst | Start a sample test of a draft and read it |
| `start_run`, `get_run` | analyst | Queue a run and read the latest or a given run |
| `list_indicators`, `get_indicator_columns`, `query_indicator` | viewer | Indicators with their metadata and publish state, their columns and roles, and what a widget query returns |
| `list_dashboards`, `get_dashboard`, `get_widget_data` | viewer | Dashboards in order, one dashboard's filters, palette, colors kept by value, widgets and current version, and what its widgets show |
| `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `remove_dashboard` | analyst | Add, rename, move, change the filters and the colors (palette and colors kept by value) of, duplicate and remove dashboards |
| `create_widget`, `update_widget`, `remove_widget` | analyst | Add, change (a widget's color or scale included), move to another dashboard and remove widgets |
| `list_dashboard_versions`, `get_dashboard_version` | analyst | A dashboard's kept versions with what changed, author, time and origin, and any kept version's name, filters, palette, colors kept by value and widgets |
| `restore_dashboard_version` | analyst | Restore a kept version of a dashboard as a new version |

Each tool SHALL describe its arguments with a JSON schema and SHALL be marked as read-only, or as changing or removing data, so clients can ask the person before running it.

#### Scenario: Tool list
- **WHEN** a signed-in client lists the tools
- **THEN** it receives the tools of the table, `query_rows` among them, each with its schema and annotations

#### Scenario: Removal marked
- **WHEN** a client reads the annotations of `remove_dashboard`
- **THEN** the tool is marked as destructive, and `get_dashboard` as read-only

#### Scenario: Restore marked
- **WHEN** a client reads the annotations of `restore_dashboard_version`
- **THEN** the tool is marked as changing data and not as destructive, and `list_dashboard_versions` and `get_dashboard_version` as read-only

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

### Requirement: Dashboard versions through MCP
`list_dashboard_versions` SHALL return a dashboard's kept versions, newest first, with what the version history shows for each and which one is current. `get_dashboard_version` SHALL return a kept version's name, filters, palette, colors kept by value and widgets with their settings, as `get_dashboard` returns them. `restore_dashboard_version` SHALL restore a kept version with the rules of the UI and its `change`, and return the new version, whether the name was kept, and the widgets restored as copies.

#### Scenario: Undoing a request
- **WHEN** an analyst asks Claude to undo its last request on "Water access", whose current version 9 was made by that request
- **THEN** Claude lists the versions, restores version 8, and "Water access" is as before the request, with version 10 marked "via MCP" saying version 8 was restored

#### Scenario: Undoing a request that moved a widget
- **WHEN** a request moved the widget "Coverage" from "Health" to "Field", and Claude then restores the version before it on "Field" and after that on "Health"
- **THEN** "Coverage" is back on "Health" with its identifier, and "Field" no longer holds it

#### Scenario: Comparing a version
- **WHEN** Claude calls `get_dashboard_version` for version 7 of "Housing", which held a widget since removed
- **THEN** the result holds that widget with its title, chart type, query, position and size, and nothing changes

#### Scenario: Viewer
- **WHEN** a `viewer` calls `list_dashboard_versions`
- **THEN** the tool is refused with `mcp.forbidden`, saying that it needs the analyst role

#### Scenario: Version no longer kept
- **WHEN** Claude calls `get_dashboard_version` for version 1 of a dashboard that keeps versions 2 to 11
- **THEN** the tool is refused with `dashboards.versionNotFound`

#### Scenario: Colors of a version
- **WHEN** Claude calls `update_dashboard` on "Water access" with the palette `earth` and color 5 kept for `Female` of `sex`, and then calls `get_dashboard_version` for the version before that change and for the version it made
- **THEN** the first holds the palette `tamiza` and no colors kept by value, and the second holds the palette `earth` and the color kept
