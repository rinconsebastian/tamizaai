# Working with Tamiza from Claude (MCP)

Tamiza has an MCP server (Model Context Protocol) so an AI client such as Claude can work with a project the way an analyst does: read the Kobo form and summaries of the data, propose indicators, write and test the scripts, run the chain and build the dashboard and its widgets. You ask in a conversation ("describe the access to water by municipality with the last survey") and Claude does the steps with the tools below, as you, with your role.

- **Tamiza calls no model.** The AI client brings the model; your own Claude plan pays for it. The MCP server makes no outbound request.
- **It is off until the operator turns it on, and each project is off until its admin allows it.** Two locks decide how much of a project's data a client can see (see [Allow a project](#allow-a-project-project-admin)).
- **Claude acts as you.** Every call runs with your account and your role in the project, exactly as the interface does, and every change is attributed to you.

## Turn it on (operator)

1. Register the client in Keycloak: [Client for AI clients (MCP)](keycloak.md#client-for-ai-clients-mcp-optional). Without it nobody can sign in.
2. In `.env`, set `TAMIZA_MCP_ENABLED=true` and, only if projects may allow rows, `TAMIZA_MCP_MAX_DATA_ACCESS=rows`. The other variables have defaults; see [`.env.example`](../.env.example) and [docs/operations.md](operations.md#mcp-server).
3. `docker compose up -d api ui` (the `ui` image carries the proxy locations for `/mcp` and `/.well-known/oauth-protected-resource`).
4. Check `https://<your address>/.well-known/oauth-protected-resource/mcp`: it must answer a short JSON document that names `https://<your address>/mcp` and your Keycloak realm.

**Who can connect.** claude.ai, Claude Desktop and the mobile apps connect from Anthropic's servers (the range `160.79.104.0/21`), so `TAMIZA_PUBLIC_URL` and Keycloak must be reachable from the Internet over HTTPS. Claude Code connects from the analyst's own computer, so it works on a private network when that computer reaches Tamiza and Keycloak. If your installation is private and people use claude.ai, Anthropic documents MCP tunnels for that case.

## Allow a project (project admin)

In **Settings > General** the card **AI clients (MCP)** shows the project's level. Every project starts at *Off*, new and existing. The admin chooses:

| Level | What a client can see |
|---|---|
| **Off** | Nothing. Every tool that names the project answers that an admin can allow it. |
| **Summaries only** | The form, the tables and columns with their types and question labels, row counts, *column profiles*, the scripts, the shape of test and run results with masked errors, and what indicators and widgets show. Never rows. |
| **Summaries and rows** | Also rows of the raw, clean, output, indicator and reference tables, previews of tests, the values a script passes to `ctx.show`, and unmasked errors and logs. |

The operator's maximum (`TAMIZA_MCP_MAX_DATA_ACCESS`, summaries by default) limits every project: a project set to *Summaries and rows* under a *summaries* maximum behaves as *Summaries only*, and the card says so. Changing a level takes effect on the next call.

Below the level the card lists the project's 50 most recent calls (when, who, which tool, the outcome), so the team can see what Claude did.

## Connect (every person)

Open **Connect an AI client** in Tamiza's top bar. It shows the address, the client ID and these steps with your values filled in.

**claude.ai and Claude Desktop.** *Customize > Connectors > Add custom connector*, paste the address (`https://<your address>/mcp`), open the advanced settings, choose **Use your own OAuth client** and enter the client ID (`tamiza-mcp` unless the operator changed it). Leave the secret empty: the client is public. Press Connect and sign in with your Tamiza (Keycloak) account.

**Claude Code.**

```text
claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://<your address>/mcp
```

then run `/mcp` inside Claude Code and sign in. Claude Code keeps working while your Keycloak session lasts, and for as long as the connection's refresh token lives when the realm allows offline access.

**Revoke.** Remove the connector in Claude, and revoke the application in your Keycloak account page (*Applications*, the link is on the connect page). An operator can end every connection by removing the Keycloak client or by turning `TAMIZA_MCP_ENABLED` off.

## What the client sees

- The **column profile** (`profile_table`) describes a column over all its rows without returning any: how many are empty or distinct; for numbers the minimum, quartiles, mean and maximum (a minimum of -99 shows a sentinel to clean); for dates the earliest and latest; for yes/no columns the counts; for select questions the count of each choice label (a count under 5 is reported as "fewer than 5"); and for short text columns the values that 5 or more rows share. Free text is never listed.
- **Test and run results** show the shape of every table and, for an indicator, how its table will look on a dashboard. Errors and log lines are masked as the script assistant masks them: only the exception, the lines of the script and the words that come from the schema, the libraries or your own script survive; any other word or number shows as `…`. Printed output and previews are not included unless rows are allowed.
- **What reaches the client at every level, including summaries:** the choice labels of the form (a list of enumerators' names is part of them), the question labels, the code of your scripts, the names and settings of dashboards, the values that indicator queries and widgets show (what a viewer sees on a dashboard), the field metrics of the performance dashboard (including the names of enumerators), the minimum and maximum of numeric columns, and values that 5 or more rows share in short text lists. Do not type secrets or personal data into scripts as constants.
- Choosing a level and a Claude plan is a data-protection decision of your organization (in Colombia, Ley 1581 and the rules of the donors of a study). What Anthropic does with conversations depends on the plan's terms, which your organization checks; this page is not legal advice.

## The tools

Every tool runs the same code as the interface's own action, with the same checks and limits. The role is the one the interface needs for the same action.

| Group | Tool | Role | What it does |
|---|---|---|---|
| Orientation | `list_projects` | any member | Your projects, your role in each and the access AI clients have there |
| | `get_project` | viewer | State of a project: submissions, published tables, indicators, dashboards; for analysts also the last synchronization, scripts and last run |
| The Kobo data | `get_form` | analyst | Questions, choice lists (`list` reads one in full), form versions in use, enumerator question, metrics the form supports |
| | `get_tables` | viewer (`clean`, `output`, `indicators`), analyst (`raw`, `reference`) | Tables of a source with columns, types, question labels, the select question each column answers, row counts |
| | `profile_table` | as `get_tables` | Column profiles of one table, 40 columns per call |
| | `get_fieldwork` | viewer | The performance dashboard's metrics for a range of days |
| | `query_rows` | as `get_tables`, and rows allowed | Up to 100 rows of a table with the clean data dashboard's filters and one sort column |
| Scripts | `get_authoring_guide` | analyst of a project AI clients may use | The script contract, `ctx`, libraries, conventions and templates |
| | `list_scripts`, `get_script`, `list_script_versions` | analyst | Scripts with their current version numbers, any version's code, the history |
| | `save_script`, `remove_script`, `restore_script_version` | analyst | Write a script as a new version; earlier versions stay and can be restored |
| Tests and runs | `test_script`, `get_test` | analyst | Try a draft on a sample without publishing anything, waiting up to two minutes |
| | `start_run`, `get_run` | analyst | Queue a run and read it |
| Indicators | `list_indicators`, `get_indicator_columns` | viewer | Indicators with their publish state, and the columns of a table |
| | `query_indicator` | viewer | What a widget would plot, without saving anything |
| Dashboards | `list_dashboards`, `get_dashboard`, `get_widget_data` | viewer | Dashboards, their filters and widgets, and what the widgets show |
| | `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `remove_dashboard` | analyst | Add, rename, move, set the filters of, copy and remove dashboards |
| | `create_widget`, `update_widget`, `remove_widget` | analyst | Add, change, move to another dashboard and remove widgets |

There is also the prompt `describe_situation` (a project and a situation), which starts the work described below.

## A worked session

> **You:** Use Tamiza to describe the access to drinking water by municipality in my "Water survey" project.

1. Claude calls `list_projects` and `get_project`, then `get_form` and `get_tables` to see the questions (`water_source`, `municipality`) and the tables, and `profile_table` on `main`: "`water_source` has Piped 812, Well 390 and River under 5; `household_size` has -99 in 6 rows".
2. It proposes: "I would build `piped_water_rate` (households whose main source is piped, over all households, by municipality) and count `households`. I will treat -99 as missing. Do you confirm?" You answer.
3. With `get_authoring_guide` it writes script 1 (turn -99 into empty) and `test_script`s it; it reads the shape of the result and the masked error if any, fixes the draft and tests again. Then `save_script`.
4. The same for each indicator, saving `piped_water_rate` with its label, `%` unit and `percent` format.
5. `start_run` and wait; `get_run` shows the steps.
6. `create_dashboard` "Water access", `query_indicator` to check each widget, `create_widget` for a stat tile, a bar by municipality and a map, `update_dashboard` for a Municipality filter, and `get_widget_data` to read it back. It tells you what the dashboard shows and what to look at.

The calls appear in Settings > General. Everything Claude saved is a new version of a script (Versions tab, marked "via MCP"), so you can restore any earlier one.

## Guards

- **Saves need the version you read.** `save_script`, `remove_script` and `restore_script_version` take `baseVersion`, the version number from `list_scripts` or `get_script` (omitted for a script that does not exist yet). If someone saved in the meantime the call is refused with `mcp.scriptChanged` and the current version's author and time; nothing changes.
- **Removals name what they remove.** `remove_dashboard` needs the dashboard's name and `remove_widget` the widget's title, compared ignoring case. A wrong identifier cannot remove the wrong thing: `mcp.nameMismatch`.
- **Code that reaches outside the script is refused.** `save_script`, `restore_script_version` and `test_script` refuse code that imports `os`, `sys`, `subprocess`, `socket`, `shutil`, `ctypes`, `importlib`, `http` or `urllib`, calls `__import__`, `eval`, `exec`, `compile` or `open`, or holds a web address (`mcp.unsafeCode`, with the lines). The script editor still accepts such code from a person, with warnings. This is a line scan, not isolation: scripts run without a strong sandbox, and the text of a form or of the data could mislead a client, so allow rows only for projects whose scripts you can review.
- **Limits.** A person runs at most 4 calls at once and 120 a minute across all their clients (`mcp.rateLimited`, with the seconds to wait). A result longer than 60,000 characters is cut from the end of its longest list and says which list and which argument asks for less. A test started from Claude replaces your own queued test in the project, as a new test in the editor does.
- **Records.** Each call is recorded with who, which project, which tool, the level, the outcome and the size of the result, never its arguments or its result, and kept 180 days.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Claude says it cannot reach the server | The address is not public HTTPS (claude.ai needs it), or `/.well-known/oauth-protected-resource/mcp` does not answer, or Keycloak is not reachable from the Internet |
| Sign-in loops or fails at Keycloak | The client's redirect URIs lack `https://claude.ai/api/mcp/auth_callback` or `http://localhost:<port>/callback`, or the audience mapper is missing: [the Keycloak section](keycloak.md#client-for-ai-clients-mcp-optional) has a check |
| Every tool says `mcp.projectOff` | An admin of the project has not allowed AI clients in Settings > General |
| `query_rows` says `mcp.rowsNotAllowed` | The project is at *Summaries only*, or the operator's maximum is `aggregates` |
| A call says `mcp.timeout` | A table is being replaced by a run, or a profile took too long: name fewer columns, or try again |
