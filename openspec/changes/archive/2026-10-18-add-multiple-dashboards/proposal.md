# Proposal

## Why

A project has one indicators dashboard: one canvas that holds every widget, up to 50. A study with several themes (housing, health, education, field progress) ends up on one long page that every member must scroll, and there is no way to give each audience its own view. Analysts want several dashboards per project, each with its own name and its own widgets, shown as tabs next to Performance and Clean data.

The indicators dashboard change (archived `2026-10-14-add-indicators-dashboard`) listed "More than one indicators dashboard per project" under "Not in this change" and as a design non-goal. This change lifts that non-goal. The rest of that design (one query per widget, the 12-column grid, last write wins) still holds.

## What Changes

- **Up to 7 dashboards per project.** The fixed Indicators tab becomes the project's own dashboards: at least 1 and at most 7, each a canvas of widgets like today's.
  - The tab row reads Performance, Clean data, the project's dashboards in their order, then Settings.
  - Performance and Clean data stay first. They cannot be renamed, moved or removed, and no dashboard can take their place.
- **Analysts and admins manage the dashboards.** From a toolbar on each dashboard they can:
  - add a dashboard, named when it is created, placed last;
  - rename it;
  - move it left or right among the project's dashboards;
  - duplicate it with all its widgets, under a new name, placed right after it;
  - remove it with all its widgets, after a confirmation that says how many widgets go with it. The last dashboard cannot be removed.

  Viewers see the dashboards and their widgets, and none of these actions.
- **Dashboard names.** 1 to 40 characters after trimming, unique within the project whatever their case, and never "Performance", "Clean data" or "Settings". A name is shown as typed, never translated.
- **A widget belongs to one dashboard.**
  - A new widget is added to the dashboard it is created on.
  - The widget form gains a Dashboard field that moves the widget to another dashboard of the project. A move that changes nothing else works even when the widget's query has gone stale.
  - The widget limit (50 by default) now counts the widgets of one dashboard, not of the whole project. The operator variable becomes `IndicatorDashboard__MaxWidgetsPerDashboard`. The old `IndicatorDashboard__MaxWidgetsPerProject` is still read when the new one is not set.
- **Each dashboard has its own address**, `/projects/<id>/dashboards/<dashboard id>`, which stays the same when the dashboard is renamed or moved. The old address `/projects/<id>/indicators` opens the project's first dashboard. The address of a dashboard the project does not have, such as a removed one, opens the first dashboard in its place, with a notice saying the dashboard asked for does not exist.
- **Every dashboard shows the project's indicators.** The list of indicators, its exports and the "no indicator has published yet" message appear on every dashboard, as on today's Indicators tab.
- **The tab row fits up to 10 tabs.** From 768 pixels wide, tabs that do not fit scroll sideways inside the row (never the page), with a control on each side that hides tabs. Settings stays visible at its end, and long names are cut with an ellipsis and shown in full on hover and keyboard focus. Below 768 pixels the select lists the project's dashboards after Performance and Clean data.
- **Changes by two members at the same time** are caught: a change to a dashboard someone else removed, or a move based on an order someone else changed, is refused, and the member's page catches up.
- **New projects start with one dashboard**, "Indicators". The project details list the project's dashboards in their order.
- **Migration.** DbUp script `0012_add_multiple_dashboards.sql` gives every existing project one dashboard, "Indicators", and moves all its widgets there unchanged.

Not in this change:
- Dashboards visible to some members only, or per-member dashboards.
- Copying a dashboard or a widget to another project.
- A dashboard-wide filter, or filters shared between widgets.
- Dragging tabs to reorder them. Moving left and right covers the same need from the keyboard and on a phone.
- Recording dashboard changes in an audit log. That belongs to `add-audit-log`.

## Capabilities

### New Capabilities

- `custom-dashboards`: the project's own dashboards. It covers:
  - how many a project has (1 to 7) and the one every project starts with;
  - names;
  - adding, renaming, reordering, duplicating and removing them, and who may do each;
  - their addresses, the old indicators address, and a removed dashboard's address;
  - changes made by two members at the same time.

### Modified Capabilities

- `dashboard-indicators`:
  - "Widgets": a widget belongs to one dashboard, and the widget limit counts one dashboard's widgets.
  - "Widget layout": each dashboard has its own grid.
  - "Indicators dashboard and its indicators": every dashboard lists the project's indicators and shows the "no indicator has published yet" message.
  - A new requirement for moving a widget to another dashboard.
- `web-ui`:
  - "Project navigation": the tab row lists Performance, Clean data, the project's dashboards and Settings, and tabs that do not fit scroll inside the row.
  - "Project navigation on narrow screens": the select lists the project's dashboards.
- `projects`: "Project list and details": the details list the project's dashboards. That a new project starts with one dashboard is stated in `custom-dashboards`.

## Impact

- **api** (`tamiza-api`):
  - New `Dashboard` entity and a `/api/v1/projects/{projectId}/dashboards` endpoint group: read one dashboard with its widgets, add, rename, remove, reorder, duplicate. The widget endpoints move under it: create, change (including a move to another dashboard) and remove.
  - `GET /api/v1/projects/{projectId}/indicators` keeps the indicators and the limits and no longer returns widgets.
  - `IndicatorDashboardOptions`: `MaxWidgetsPerDashboard`, with the old name read as a fallback.
  - Project creation adds the first dashboard. Project details carry the dashboards.
  - New problem codes `dashboards.notFound`, `dashboards.limit`, `dashboards.lastOne` and `dashboards.orderChanged`.
  - OpenAPI, unit and integration tests.
- **ui** (`tamiza-ui`):
  - `app.routes.ts`: the `dashboards/:dashboardId` route, and `indicators` sends to the first dashboard.
  - `project-page.ts` and `project-context.ts`: the tab row is built from the project's dashboards.
  - `shared/ui/tabs-nav.ts`: a row that scrolls inside itself, Settings kept visible, long names cut.
  - `project-indicators.ts`: one dashboard at a time, a dashboard toolbar, and a reload when another dashboard opens.
  - A new name dialog. `widget-dialog.ts` gains the Dashboard field. `shared/ui/dropdown-menu.ts` gains disabled items.
  - `core/projects/indicators.service.ts` and `projects.service.ts`: the new endpoints and types.
  - `en.json` strings and problem codes, and the specs.
- **Database:** `0012_add_multiple_dashboards.sql` adds `tamiza.dashboards` and `dashboard_widgets.dashboard_id`, and moves every existing widget to its project's first dashboard. `TamizaDbContext` mirrors it.
- **Analytics:** none. The worker never reads widgets.
- **Configuration:** `IndicatorDashboard__MaxWidgetsPerDashboard` replaces `IndicatorDashboard__MaxWidgetsPerProject`, which keeps working.
- **Docs:**
  - `docs/indicators-dashboard.md`: dashboards, their limits and addresses.
  - `docs/operations.md`: the renamed variable and a rollback note.
  - `docs/kobo.md`: the roles table.
  - `docs/tamiza-brief.md` and `openspec/config.yaml`: "three dashboards", the data model and the roadmap.
  - `README.md`: the status line.
