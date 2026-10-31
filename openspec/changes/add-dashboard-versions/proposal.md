# Proposal

## Why

A dashboard is built over many small edits: widgets added and tuned, filters defined, widgets moved and resized. Since the MCP server (archived `2026-10-31-add-mcp-server`), an AI client can also make a dozen changes to a dashboard in one go. Today none of these edits can be undone:
- a widget removed by mistake must be rebuilt by hand;
- a filter list saved wrongly must be typed again;
- a layout spoiled by a careless drag must be arranged again;
- if a request to Claude goes wrong, every widget it touched must be checked one by one.

The MCP design says it plainly in its risks: "A dashboard or widget changed wrongly is not versioned and must be fixed by hand." Scripts already keep their versions and can be restored. Dashboards are the last thing people edit in Tamiza that has no history.

The multiple dashboards change (archived `2026-10-18-add-multiple-dashboards`) listed "An undo for a removed dashboard" under its non-goals. This change does not lift that one. It gives each dashboard that still exists a history of its own.

## What Changes

- **Automatic versions of each dashboard.** Every change a person saves to a dashboard records a new version, however small. That covers adding the dashboard, renaming it, duplicating it, saving its filters, and adding, editing, removing, moving or resizing a widget, or moving one to or from another dashboard.
  - A version holds the dashboard as it is after the change: its name, its filters with their defaults, and its widgets with their title, chart type, query, ignored filters, position and size.
  - It records its number, who made it, when, whether it was made in Tamiza or through an AI client, and a short list of what changed ("Widget “Water source” added").
  - One action makes one version of each dashboard it changes. A drag that pushes three other widgets down is one version. A widget moved to another dashboard makes one version on each of the two.
  - A save that leaves the dashboard as it was makes no version.
- **Not dashboard changes.** These never make a version, and a restore never changes them:
  - runs and the data they publish, which refresh what widgets show;
  - synchronizations;
  - the filter values a member picks;
  - a member's basemap or map view;
  - showing widgets as tables;
  - printing;
  - the order of the project's dashboards;
  - the dashboard's scheduled email, which has its own recipients and sending state.
- **Moves and resizes in a row are one version.** When one person moves or resizes widgets on one dashboard less than a minute apart, with nothing else changed in between, those moves are one version that keeps the last arrangement. Rearranging a dashboard with ten drags would otherwise push every other change out of the history.
- **One request of an AI client is one version.** The MCP tools that change a dashboard take a new required argument, `change`: a few words saying what the person asked for, the same on every call made for that request. Writes by the same person on a dashboard with the same `change`, less than 30 minutes apart, with no other change in between, are one version.
  - So "add five indicators to Water access" is one version of "Water access", however many `create_widget` calls it takes.
  - The version shows that text in the history, marked "via MCP".
- **The last 10 versions are kept.** Each dashboard keeps its 10 newest versions, the current one included, and older ones are deleted as new ones arrive. A dashboard's versions go with it when it is removed. A copy made by duplicating starts its own history.
- **Version history in the dashboard.** Analysts and admins find **Version history** among the dashboard's actions. It lists the kept versions, newest first, with what changed, who, when and "via MCP". From there they can:
  - view a version on the dashboard page, read-only, with today's data;
  - restore it after confirming.
- **Restoring a version.** The dashboard's name, filters and widgets become those of the version, recorded as a new version, so the state it replaced stays in the history and the restore itself can be undone.
  - The dashboard keeps its address, its place among the dashboards and its scheduled email.
  - When another dashboard now has the version's name, the restore keeps the current name and says so.
  - A widget of the version that now sits on another dashboard comes back as a copy.
  - A version with more widgets than the current limit is refused, as a duplicate is.
- **Claude manages versions through MCP.**
  - New tools: `list_dashboard_versions`, `get_dashboard_version` (a version's name, filters and widgets, so it can be compared with the current one) and `restore_dashboard_version`.
  - The restore is guarded by `baseVersion`, the current version the client last read, as script writes are. It is refused with `mcp.dashboardChanged` when someone changed the dashboard meanwhile.
  - `get_dashboard` and every dashboard write return the current version number.
  - The server instructions explain `change` and how to undo a request.
- **One save per drag.** The dashboard page sends the layout of every widget a drag moved in one request to a new layout endpoint, instead of one request per widget.
- **Migration.** The DbUp script `0018_add_dashboard_versions.sql` adds the table of versions and records version 1 of every existing dashboard: its current state, marked as the state before version history.
- **BREAKING (MCP only):** the dashboard write tools refuse a call without `change` with `mcp.validation`. Clients read the new schemas when they list the tools. The REST API keeps every route and body it has, and adds new ones.

Not in this change:
- Bringing back a removed dashboard. Its versions are removed with it, and `remove_dashboard` still cannot be undone.
- Versions of the Performance and Clean data dashboards, of the dashboards' order, or of scheduled emails.
- Scripts, which keep their own unlimited versions.
- Comparing two versions side by side, naming or pinning a version, or changing how many are kept.
- Showing an open page the changes another member or an AI client makes meanwhile.
- The audit log (`add-audit-log`). Versions are pruned and go with their dashboard, so they are not a record of who did what.

## Capabilities

### New Capabilities

- `dashboard-versions`: the version history of a project's dashboards. It covers:
  - what a version holds;
  - which changes make one, which do not, and when several changes are one version;
  - the state before history began;
  - the 10 kept;
  - the history, viewing and restoring in the dashboard page;
  - what a restore does with names, widgets and limits;
  - duplicated and removed dashboards;
  - restores racing other changes.

### Modified Capabilities

- `custom-dashboards`:
  - "Dashboard header": analysts and admins also get the action that opens the version history.
  - "Roles for dashboards": reading and restoring versions is an `analyst` or `admin` action.
- `mcp-server`:
  - "Tools and their roles": the three version tools.
  - "Removing needs the name": a widget removed through MCP can be restored with an earlier version; a dashboard still cannot.
  - "Guidance for clients": the instructions cover `change` and undoing a request.
  - New requirements for:
    - grouping one request's writes into one version;
    - the version tools;
    - restoring against the current version.

## Impact

- **api** (`tamiza-api`):
  - New table and EF entity `DashboardVersion`.
  - A `DashboardVersions` service records, extends and prunes versions once per request. It is called by an endpoint filter on the dashboards route group and by the MCP dashboard tools. The dashboard and widget handlers report what they changed.
  - `GET /api/v1/projects/{projectId}/dashboards/{dashboardId}/versions`, `GET .../versions/{number}` and `POST .../versions/{number}/restore` (analyst).
  - `PUT .../dashboards/{dashboardId}/layout` saves several widgets' positions and sizes at once.
  - `GET .../dashboards/{dashboardId}` returns the current version number.
  - Project creation records version 1 of the first dashboard.
  - New problem code `dashboards.versionNotFound`.
  - OpenAPI, unit and integration tests.
- **MCP** (`tamiza-api/src/Tamiza.Api/Mcp`):
  - The three tools, `change` on `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `create_widget`, `update_widget` and `remove_widget`, and versions in their results and in `get_dashboard` and `list_dashboards`.
  - The refusal `mcp.dashboardChanged`.
  - `Instructions.md`, the tool descriptions, and the catalog and role matrix tests.
- **ui** (`tamiza-ui`):
  - `project-indicators.ts`: the Version history action, the read-only view of a version, the restore, and one layout request per drag.
  - A new dialog `dashboard-versions-dialog.ts`.
  - `indicators.service.ts`: the new endpoints and types.
  - `en.json` and `es.json` strings, `error-codes.spec.ts`, and the specs.
- **Database:** `0018_add_dashboard_versions.sql` adds `tamiza.dashboard_versions` and version 1 of every existing dashboard. `TamizaDbContext` mirrors it.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: version history, restoring, what is and is not versioned.
  - `docs/mcp.md`: the tools and `change`.
  - `docs/operations.md`: rollback, and the line saying dashboards are not versioned.
  - `docs/tamiza-brief.md`: the data model, the tool count and the roadmap.
  - `tamiza-api/src/dbUp/README.md`: a migration that rewrites widget configs must rewrite versions too.
  - `README.md`: the status line.
