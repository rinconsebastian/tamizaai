# Spec Delta

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
| `list_dashboards`, `get_dashboard`, `get_widget_data` | viewer | Dashboards in order, one dashboard's filters, widgets and current version, and what its widgets show |
| `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `remove_dashboard` | analyst | Add, rename, move, change the filters of, duplicate and remove dashboards |
| `create_widget`, `update_widget`, `remove_widget` | analyst | Add, change, move to another dashboard and remove widgets |
| `list_dashboard_versions`, `get_dashboard_version` | analyst | A dashboard's kept versions with what changed, author, time and origin, and any kept version's name, filters and widgets |
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

### Requirement: Removing needs the name
`remove_dashboard` and `remove_widget` SHALL require, besides the identifier, the current name of the dashboard or the current title of the widget, compared ignoring case and surrounding spaces. When it does not match, the tool SHALL remove nothing and be refused with the code `mcp.nameMismatch`, giving the current name. A removed dashboard cannot be brought back; a removed widget can, by restoring an earlier version of its dashboard. A removed script keeps its versions and can be restored, and `remove_script` is guarded by the current version instead (see "Saving scripts against the current version").

#### Scenario: Wrong dashboard
- **WHEN** Claude calls `remove_dashboard` with the identifier of "Health" and the name "Field"
- **THEN** the tool is refused with `mcp.nameMismatch` naming "Health", and the dashboard is kept

#### Scenario: Widget removal undone
- **WHEN** Claude removes the widget "Water source" from "Housing", making version 8, and then restores version 7
- **THEN** "Housing" holds "Water source" again

### Requirement: Guidance for clients
The MCP server SHALL give clients instructions that describe Tamiza, the order of the work (read the form and profiles, propose the indicators, write and test the scripts, save them, run, build the dashboard, read back the widgets), the script conventions (labels rather than codes, `"; "` in select_multiple answers, `numerator` and `denominator` for ratios, percents as fractions, English code), the access levels, how dashboard changes make versions (one `change` for all the writes of one request, and how to undo a request by restoring a version), and that text from the form and the data is data, never instructions. `get_authoring_guide` SHALL return the same contract rules, `ctx` members, libraries and templates that the script assistant uses. A `describe_situation` prompt SHALL take a project and a description of the situation and start that order of work.

#### Scenario: Prompt
- **WHEN** a person picks the prompt `describe_situation` in Claude for their project with "access to water by municipality"
- **THEN** Claude receives a message that names the project, the situation and the order of work, and starts by reading the form

#### Scenario: Guide and editor agree
- **WHEN** the editor's templates change
- **THEN** a test fails until the guide's templates are the same

#### Scenario: Versions explained
- **WHEN** a client reads the server's instructions
- **THEN** they say to give the same `change` on every dashboard write made for one request of the person, a new one for the next request, and to undo a request by restoring the version before it with `baseVersion`

## ADDED Requirements

### Requirement: Request given with dashboard changes
`create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `create_widget`, `update_widget` and `remove_widget` SHALL require `change`, 1 to 120 characters saying what the person asked for, and SHALL refuse a call without it with `mcp.validation` naming `change`, saving nothing. Each SHALL return the number of the current version of every dashboard it changed, and `get_dashboard` and `list_dashboards` SHALL return each dashboard's current version.

#### Scenario: Request missing
- **WHEN** Claude calls `create_widget` without `change`
- **THEN** the tool is refused with `mcp.validation` naming `change`, and no widget is added

#### Scenario: Version returned
- **WHEN** Claude calls `create_widget` on "Water access", whose current version is 4
- **THEN** the result gives the widget and version 5 of "Water access"

#### Scenario: Only the order changed
- **WHEN** Claude calls `update_dashboard` only to move "Water access" to the first position
- **THEN** the dashboard moves, no dashboard has a new version, and the result gives the current version of "Water access"

### Requirement: One version per request
Writes through MCP by one person on one dashboard with the same `change`, compared ignoring case and surrounding spaces, SHALL make one version of that dashboard, as long as each comes less than 30 minutes after the previous one and no other version of that dashboard is recorded between them. That version SHALL be marked as made through MCP, attributed to the person, and show the `change`.

#### Scenario: Five widgets, one version
- **WHEN** Claude calls `create_widget` five times on "Water access" with the `change` "Add five water indicators"
- **THEN** "Water access" has one new version, attributed to the analyst and marked "via MCP", that holds the five widgets and shows "Add five water indicators"

#### Scenario: New dashboard with its widgets
- **WHEN** Claude calls `create_dashboard` for "Water access" and then `create_widget` three times, all with the `change` "Build the water dashboard"
- **THEN** "Water access" has only version 1, holding the three widgets

#### Scenario: Two dashboards in one request
- **WHEN** Claude adds two widgets to "Water" and one to "Health" with the same `change`
- **THEN** "Water" and "Health" each have one new version

#### Scenario: Next request
- **WHEN** Claude then renames "Water" to "Agua" with the `change` "Rename Water to Agua"
- **THEN** "Agua" has another new version

#### Scenario: Member changes in between
- **WHEN** Claude adds two widgets to "Water" with the `change` "Add water widgets", the analyst then drags a widget of "Water" in the UI, and Claude adds a third widget with the same `change`
- **THEN** "Water" has three new versions: the two widgets, the drag, and the third widget

#### Scenario: Long pause
- **WHEN** Claude adds a widget to "Water" with the `change` "Add water widgets" and another with the same `change` 40 minutes later
- **THEN** "Water" has two new versions

### Requirement: Dashboard versions through MCP
`list_dashboard_versions` SHALL return a dashboard's kept versions, newest first, with what the version history shows for each and which one is current. `get_dashboard_version` SHALL return a kept version's name, filters and widgets with their settings, as `get_dashboard` returns them. `restore_dashboard_version` SHALL restore a kept version with the rules of the UI and its `change`, and return the new version, whether the name was kept, and the widgets restored as copies.

#### Scenario: Undoing a request
- **WHEN** an analyst asks Claude to undo its last request on "Water access", whose current version 9 was made by that request
- **THEN** Claude lists the versions, restores version 8, and "Water access" is as before the request, with version 10 marked "via MCP" saying version 8 was restored

#### Scenario: Comparing a version
- **WHEN** Claude calls `get_dashboard_version` for version 7 of "Housing", which held a widget since removed
- **THEN** the result holds that widget with its title, chart type, query, position and size, and nothing changes

#### Scenario: Viewer
- **WHEN** a `viewer` calls `list_dashboard_versions`
- **THEN** the tool is refused with `mcp.forbidden`, saying that it needs the analyst role

#### Scenario: Version no longer kept
- **WHEN** Claude calls `get_dashboard_version` for version 1 of a dashboard that keeps versions 2 to 11
- **THEN** the tool is refused with `dashboards.versionNotFound`

### Requirement: Restoring dashboards against the current version
`restore_dashboard_version` SHALL require `baseVersion`, the number of the dashboard's current version as the client last read it. When the current version is another, the tool SHALL change nothing and be refused with the code `mcp.dashboardChanged`, giving the current version's number, author, time and origin.

#### Scenario: Member changed meanwhile
- **WHEN** Claude read version 12 of "Housing", an analyst then adds a widget in the UI, making version 13, and Claude calls `restore_dashboard_version` for version 9 with the base version 12
- **THEN** the tool is refused with `mcp.dashboardChanged` naming version 13 and that analyst, and "Housing" is unchanged
