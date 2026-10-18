# Design

## Context

See proposal.md for the motivation. The pieces this change works on:

- **Widgets are project-scoped.** `tamiza.dashboard_widgets` (`0007_add_indicators_dashboard.sql:4-27`) has `project_id` (cascade from `projects`), `config` and `layout` as `jsonb`, and no tab, dashboard or position column. Every API read and write filters by `ProjectId` (`IndicatorEndpoints.cs:52,125,158,211`). The analytics worker never reads the table.
- **One endpoint group.** `/api/v1/projects/{projectId:guid}/indicators` (`IndicatorEndpoints.cs:24-31`) serves the overview (indicators, *all* widgets ordered by `layout.y`, `layout.x`, `created_at`, and `limits`), `/{name}/columns`, `/query`, `/{name}/exports`, and `POST /widgets`, `PATCH|DELETE /widgets/{widgetId:guid}`. Reads need `viewer`, widget writes `analyst`, through `RequireProjectRole` (404 for non-members, `ProjectAccess.cs:37-63`). PATCH and DELETE load the widget, then save it (`:158,198-205,211-218`).
- **The widget limit is per project.** `IndicatorDashboardOptions.MaxWidgetsPerProject` (default 50, `[Range(1, 500)]`, `ValidateOnStart`, env `IndicatorDashboard__MaxWidgetsPerProject`). Create takes `pg_advisory_xact_lock(hashtextextended(projectId::text, 0))`, counts the project's widgets and answers a 400 on `widgets` past the limit (`IndicatorEndpoints.cs:121-130`). `PlaceBelow` puts a widget without a layout under *all* the project's widgets (`:132,272-276`). There is no other lock, no ETag, and last write wins.
- **Lost rows are not handled.** The API catches only `UniqueViolation` (8 places). Nothing catches a foreign-key violation or a `DbUpdateConcurrencyException`, so a write that meets a row removed a moment before ends in a 500. Race-free deletes elsewhere use `ExecuteDeleteAsync` and map 0 rows to 404 (`MemberEndpoints.cs:199`, `ReferenceTableEndpoints.cs:163`).
- **Project details** (`ProjectModels.cs:18-31`) are built by `ToDetails` (POST) and `DetailsAsync` (GET, PATCH, kobo-token, kobo-check). Creation adds the form version and the creator's membership in one `SaveChanges` (`ProjectEndpoints.cs:117-125`). Tests seed projects with raw SQL (`TestData.InsertProjectAsync`). `ProjectContext.refresh()` re-reads the details fire-and-forget and swallows errors (`project-context.ts:61-66`); the sampling-frame page relies on that.
- **The tab row is hard-coded.** `ProjectPage.tabs` (`project-page.ts:74-85`) lists Performance, Clean data, Indicators (`/projects/<id>/indicators`) and a Settings group, every label through Transloco. The row is rendered inside `@if (project())`, so it appears only after the details arrive, after the first `NavigationEnd`. `TabsNav` (`shared/ui/tabs-nav.ts`) renders a native select below `md` and, from `md`, a `flex flex-wrap` row: ten tabs would wrap onto 2 or 3 rows, misalign the underline (`-mb-px`) and push Settings (`ms-auto`) to the end of whichever row it lands on. Its links have no focus style of their own.
- **Navigations that only change the query string.** Clean data writes its page, sort and filters into the query string (`project-clean-data.ts:487,513`). Nothing scrolls on navigation today (`app.config.ts:22` enables anchor scrolling only).
- **The indicators page loads once.** `ProjectIndicators` (`project-indicators.ts:225-243`) reads the project id from `ProjectContext`, loads the overview in its constructor with no cancellation, and flushes pending layout saves (400 ms debounce, one PATCH per widget) only on destroy. `save()` removes a widget from `pending` before its PATCH is answered and returns nothing (`:258-270`). Its empty-state link to the scripts is relative (`['..', 'scripts']`, `:107`); under `dashboards/:dashboardId` it would resolve to `/projects/<id>/dashboards/scripts`.
- **The dialogs send their own requests.** `WidgetDialog.save()` creates and updates the widget itself and keeps every error inside the dialog (`widget-dialog.ts:791-830`). Its draft is a normalized recomputation of the saved config, not the config itself (`:452-463,490-502`).
- **UI building blocks.** The app is zoneless (Angular 22). `DropdownMenu` items have no disabled state or styling (`dropdown-menu.ts:4-51`). A CDK Dialog opened from a menu item records that item as the element to restore focus to, and the menu's overlay then removes it (`menu.mjs:911-924`, `dialog.mjs:206-245`). `ConfirmDialog` takes no input; no dialog in `shared/ui` takes text. `tmz-alert` has no dismiss control. `map-widget.ts:119,564-567` keeps a persistent sr-only status and alternates a zero-width space so repeated messages are announced.
- **Error codes.** `dashboard.timeout` is already the performance dashboard's code. Every new code must be in `apiErrorCodes` (`error-codes.spec.ts`) and `en.json` `errors.codes`. `errors.report` toasts any error without field errors (`api-error.ts:59-65`).
- **Migration conventions.** Next script is `0012`. `ScriptConventionTests` checks names (`pk_`, `fk_`, `ck_`, `ix_<table>_`) per script, `SchemaDriftTests` compares the scripts' catalog with the EF model (so no expression index, no deferrable constraint), and the analytics tests run every script as plain SQL on an empty database.

**Decided with the user (2026-10-17):**
- A project has 1 to 7 dashboards of its own, after Performance and Clean data, which stay permanent and unmodifiable. Settings stays last.
- `analyst` and `admin` add, rename, reorder, duplicate and remove dashboards. `viewer` only sees them.
- The widget limit (50 by default) counts one dashboard's widgets.
- Removing a dashboard removes its widgets, after a confirmation that says how many. The last dashboard cannot be removed.
- In scope: moving dashboards left and right, duplicating a dashboard with its widgets, and moving a widget to another dashboard from the widget form.

**Decided by this design** (open to revision):
- Names: 1 to 40 characters after trimming, no control characters, unique within the project ignoring case, and "Performance", "Clean data" and "Settings" reserved in any case.
- Every project starts with, or is migrated to, one dashboard named "Indicators".
- Addresses use the dashboard's id: `/projects/<id>/dashboards/<dashboard id>`. `/projects/<id>/indicators` and the address of a dashboard that does not exist open the first dashboard.
- The management actions live in a header on each dashboard, not in the tab row.
- Every dashboard shows the project's indicator list, its exports and the "nothing published" message.
- A duplicate is named "<name> (copy)" by default, placed right after the original.

The requirements are in `specs/custom-dashboards`, `specs/dashboard-indicators`, `specs/web-ui` and `specs/projects`.

## Goals / Non-Goals

**Goals:**
- Widgets keep their config, layout and every widget behaviour; only their owner changes from the project to a dashboard.
- One lock key for every change to a project's set or order of dashboards and to a dashboard's widget count, so the limits, the "at least one" rule and stale reorders hold under concurrent requests, and an insert can never meet a removed dashboard.
- Every write that loses its row to a concurrent removal answers 404 with `dashboards.notFound`, never 500.
- No extra request to draw the project page: the dashboards come with the project details.
- Existing bookmarks to `/projects/<id>/indicators` keep working.

**Non-Goals:**
- Live updates of another member's dashboard changes. The tab row catches up on the member's own actions and on every `dashboards.*` refusal; otherwise on reload, as today for widgets.
- An undo for a removed dashboard.
- Dragging tabs to reorder them.

## Decisions

### D1. Data model (DbUp `0012_add_multiple_dashboards.sql`)

```sql
-- Multiple dashboards: a project's own indicator dashboards, each with its widgets. Every existing project gets one, Indicators, holding all its widgets.
CREATE TABLE tamiza.dashboards (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL,
    name text NOT NULL,
    normalized_name text NOT NULL,
    position integer NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_dashboards PRIMARY KEY (id),
    CONSTRAINT ck_dashboards_position CHECK (position >= 0),
    CONSTRAINT fk_dashboards_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboards_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_dashboards_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);
-- One name per project whatever its case; also serves the lookups by project.
CREATE UNIQUE INDEX ix_dashboards_project_id_normalized_name ON tamiza.dashboards (project_id, normalized_name);
CREATE INDEX ix_dashboards_created_by ON tamiza.dashboards (created_by);
CREATE INDEX ix_dashboards_updated_by ON tamiza.dashboards (updated_by);

INSERT INTO tamiza.dashboards (project_id, name, normalized_name, position, created_by, updated_by)
SELECT id, 'Indicators', 'indicators', 0, created_by, created_by FROM tamiza.projects;

ALTER TABLE tamiza.dashboard_widgets ADD COLUMN dashboard_id uuid;
UPDATE tamiza.dashboard_widgets AS w SET dashboard_id = d.id FROM tamiza.dashboards AS d WHERE d.project_id = w.project_id;
ALTER TABLE tamiza.dashboard_widgets
    ALTER COLUMN dashboard_id SET NOT NULL,
    ADD CONSTRAINT fk_dashboard_widgets_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE;
CREATE INDEX ix_dashboard_widgets_dashboard_id ON tamiza.dashboard_widgets (dashboard_id);
```

- Every project gets a dashboard, with or without widgets, so "at least one" holds from the start. `projects.created_by` is `NOT NULL` with a `RESTRICT` FK, so it is always a valid owner. Each project has exactly one dashboard at that point, so the `UPDATE … FROM` is one to one. A widget left without a dashboard would fail `SET NOT NULL` and roll the whole script back (`WithTransactionPerScript`).
- `dashboard_widgets.project_id` stays: the per-project 404s and the project cascade rely on it. The API checks that a widget's dashboard belongs to the same project (D4). *Alternative:* a composite FK `(dashboard_id, project_id)` to an alternate key `(id, project_id)`. Rejected: the repo has no alternate-key precedent and the check sits under the lock anyway.
- The FK from widgets to dashboards cascades: removing a dashboard removes its widgets, as decided. `RESTRICT` would break project deletion, which cascades to both tables in one statement (the rule noted in `0003:48`).
- `normalized_name` is the trimmed name in lower case, computed by the API (`ToLowerInvariant`), following `project_invitations.normalized_email`. The migration's literal `'indicators'` matches it. *Alternative:* a unique index on `lower(name)`. Rejected: EF cannot model an expression index, so `SchemaDriftTests` would fail.
- No `ix_dashboards_project_id`: the unique index starts with `project_id`, and EF does not add an FK index when another index starts with the FK column.
- `TamizaDbContext` gets a `Dashboard` entity and `DbSet<Dashboard> Dashboards`, plus `DashboardWidget.DashboardId` with a cascading FK, named so EF produces exactly the script's constraint and index names. The services keep their names (`IndicatorDashboard`, `DataDashboard`, `PerformanceDashboard`).
- *Alternative:* a dashboard key inside `layout` `jsonb`. Rejected: no FK, no cascade, no index, and nothing to hold the name and order. *Alternative names* `dashboard_tabs`/`tab_id`: rejected, a tab is how the UI shows a dashboard; the API, the specs and the problem codes say "dashboard".

### D2. Order (api)

`position` holds 0 to n-1 and the API renumbers it under the lock (D3) on every add, duplicate, removal and reorder. Lists are ordered by `position`, then `created_at`, then `id`, so a gap or a tie can never hide a dashboard.

*Alternative:* a unique index on `(project_id, position)`. Rejected: a swap collides row by row, and a `DEFERRABLE` constraint is not something EF can model.

### D3. One lock per project, and lost rows (api)

Every operation that changes a project's set or order of dashboards, or the number of widgets of one of them, runs in a transaction that first takes the existing key `pg_advisory_xact_lock(hashtextextended(projectId::text, 0))`: add, duplicate, remove and reorder dashboards, add a widget, and move a widget to another dashboard. Inside the lock each operation re-reads what it checks (the dashboards and their order, the target dashboard, its widget count, and for a move the widget itself), so:
- two adds at 6 dashboards give one 7th and one 409;
- two removals at 2 dashboards leave one;
- a reorder from a page that missed another member's move is refused (D6);
- a widget added or moved to a dashboard removed a moment before gets a 404 or a 400, never an FK violation.

Rename, a widget change that stays on its dashboard (title, config, layout) and widget removal take no lock. A row they lose to a concurrent dashboard removal is handled, never a 500:
- Rename and widget removal use `ExecuteUpdateAsync` and `ExecuteDeleteAsync` filtered by project, dashboard and widget id, and treat 0 rows as not found.
- The tracked widget PATCH catches `DbUpdateConcurrencyException`.
- Every such "not found" re-checks the dashboard in the path: gone → 404 `dashboards.notFound`, else 404 "Widget not found.".

Because rename runs outside the lock, add, duplicate and rename all catch `UniqueViolation` on `ix_dashboards_project_id_normalized_name` and answer the 400 on `name` (D5), as `ReferenceTableEndpoints` does after its own pre-check.

*Alternative:* `SELECT … FOR UPDATE` on the project's dashboard rows, as `MemberEndpoints.LockAdminsAsync` does for admins. Rejected: it does not serialize adds, which create rows, and a second lock order next to the widget lock invites deadlocks.

### D4. Endpoints (api)

A new group `/api/v1/projects/{projectId:guid}/dashboards`, in the indicators module (`DashboardEndpoints.cs`), tagged "Dashboards":

| Verb | Path | Role | Body | Answers |
|------|------|------|------|---------|
| GET | `/{dashboardId:guid}` | viewer | | 200 `{ id, name, widgets }`; 404 |
| POST | `` | analyst | `{ name }` | 201 `{ id, name }`; 400 `name`; 409 `dashboards.limit` |
| PATCH | `/{dashboardId:guid}` | analyst | `{ name }` | 200 `{ id, name }`; 400 `name`; 404 |
| DELETE | `/{dashboardId:guid}` | analyst | | 204; 404; 409 `dashboards.lastOne` |
| PUT | `/order` | analyst | `{ expectedIds: [...], dashboardIds: [...] }` | 200 `[{ id, name }]` in the new order; 409 `dashboards.orderChanged` |
| POST | `/{dashboardId:guid}/copies` | analyst | `{ name }` | 201 `{ id, name }`; 400 `name` or `widgets`; 404; 409 `dashboards.limit` |
| POST | `/{dashboardId:guid}/widgets` | analyst | `{ title, chartType, config, layout? }` | 201 widget; 400; 404 |
| PATCH | `/{dashboardId:guid}/widgets/{widgetId:guid}` | analyst | `{ title?, chartType?, config?, layout?, dashboardId? }` | 200 widget; 400; 404 |
| DELETE | `/{dashboardId:guid}/widgets/{widgetId:guid}` | analyst | | 204; 404 |

- Every route with `{dashboardId}` first looks the dashboard up in the project: missing → 404 with `code: dashboards.notFound`. Only then does a widget route look the widget up on that dashboard: missing → 404 "Widget not found." (no code). The UI tells "removed by someone else" from any other failure by the code alone.
- The widget endpoints move here from `/indicators/widgets` and keep their bodies and rules. The widget DTO gains `dashboardId`. The 201's `Location` is `/api/v1/projects/{projectId}/dashboards/{dashboardId}/widgets/{id}`.
- `GET /indicators` keeps `indicators` and `limits`, drops `widgets`, and `limits` gains `maxDashboards` (7). `/indicators/{name}/columns`, `/query` and `/{name}/exports` do not change.
- There is no list endpoint: the project details carry the dashboards (D7), and `PUT /order` answers the new list.
- `order` cannot clash with a dashboard id: the id segments carry `:guid`. The group is separate from `/indicators/{name}`, so no indicator name can shadow it.
- Problem codes, under `errors.codes.dashboards` in en.json: `dashboards.notFound` (404), `dashboards.limit` ("A project can have at most 7 dashboards. Remove one before adding another."), `dashboards.lastOne` ("A project needs at least one dashboard."), `dashboards.orderChanged` ("The project's dashboards have changed. Reload and try again."), the last three 409. The plural namespace keeps them apart from the performance dashboard's `dashboard.timeout`.

*Alternative:* keep `POST /indicators/widgets` with a `dashboardId` in the body and flat PATCH and DELETE routes. Fewer test edits, but the target of a create hides in the body, and a widget call on a removed dashboard could not answer `dashboards.notFound` without a lookup by widget. Rejected.

### D5. Names (api)

`DashboardValidation.Name(string?)` returns the trimmed name or a field error on `name`:
- required and at most 40 characters after trimming (`MaxNameLength = 40`): "The name is required." / "The name can have at most 40 characters.";
- no control characters (`char.IsControl`): "The name cannot contain control characters.";
- not "performance", "clean data" or "settings" once normalized: "\"Clean data\" is the name of a permanent tab. Choose another name.";
- not the `normalized_name` of another dashboard of the project: "Another dashboard of this project is named \"Salud\"." The check runs in the request; the unique index and the caught `UniqueViolation` (D3) cover a race.

Add, rename and duplicate all go through it. Names are stored as typed (trimmed) and rendered only through text bindings, never `innerHTML` (`npm run check:safety` already enforces this).

### D6. Adding, duplicating, removing and reordering (api)

All run under the lock (D3) in one transaction.
- **Add:** at 7 dashboards (`MaxDashboards = 7`, a constant like `ScriptEndpoints.MaxIndicators`, not an option: the number is a product rule) → 409 `dashboards.limit`. Otherwise insert at `position = count`.
- **Duplicate:** source not in the project → 404; 7 dashboards → 409; source holding more widgets than `MaxWidgetsPerDashboard` (possible only after an operator lowered it) → 400 on `widgets`. Otherwise shift `position > source.position` by one, insert the copy at `source.position + 1`, and copy each widget's title, chart type, config and layout with a new id, `created_by` and `updated_by` set to the caller.
- **Remove:** source not in the project → 404; the project's only dashboard → 409 `dashboards.lastOne`. Otherwise delete it (its widgets cascade) and shift `position > removed.position` down by one.
- **Reorder:** `expectedIds` is the order the member's page shows and `dashboardIds` the order it asks for. `expectedIds` must equal the stored order exactly and `dashboardIds` must list the same ids once each; otherwise 409 `dashboards.orderChanged` and nothing changes. So a page that missed another member's add, removal *or move* is refused. Otherwise `position` = index in `dashboardIds`. The UI sends the list with two neighbours swapped; the API does not know about "left" and "right".

*Alternative for reorder:* `PATCH /{id}` with a `position`. Rejected: a stale page would move a different dashboard than the one the member saw. *Alternative:* a version number on the project's dashboards. Rejected: `expectedIds` says the same with no new column.

### D7. Project creation and details (api)

- `ProjectEndpoints.CreateAsync` adds the "Indicators" dashboard (`position` 0) in the same `SaveChanges` as the form version and the creator's membership.
- `ProjectDetails` gains `Dashboards: DashboardEntry[]` (`{ id, name }`), ordered as in D2, in both `ToDetails` and `DetailsAsync`. One query per details request, on the unique index.
- `TestData.InsertProjectAsync` also inserts the "Indicators" dashboard, so SQL-seeded projects look like real ones and every test that seeds a project keeps a dashboard to put widgets on.

*Alternative:* a separate `GET /dashboards` loaded by the project page. Rejected: the tab row would appear in two steps, a failure of the second request would hide every dashboard, and every UI spec that opens a project (`openProjectSection`, two of them with `http.verify()`) would have to answer one more request.

### D8. Widget limit per dashboard (api, configuration)

- `IndicatorDashboardOptions.MaxWidgetsPerDashboard` (`[Range(1, 500)]`, default 50) replaces `MaxWidgetsPerProject`. When `IndicatorDashboard:MaxWidgetsPerDashboard` is not configured and `IndicatorDashboard:MaxWidgetsPerProject` is, the old value is used, inside the options pipeline so `[Range]` and `ValidateOnStart` still check it, and a warning naming both variables is logged once at startup. An operator's `compose.override.yaml` keeps working.
- Add and move count the target dashboard's widgets under the lock. The message on `widgets` (add) or `dashboardId` (move) becomes "A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first."
- `limits.maxWidgets` means per dashboard. `PlaceBelow` uses only the target dashboard's widgets, on add and on move.

### D9. Moving a widget (api, ui)

- **api:** a PATCH whose `dashboardId` differs from the widget's takes the lock, re-reads the widget, checks the target belongs to the project (else 400 on `dashboardId`: "The dashboard does not exist."), checks its count (D8), sets the widget's dashboard and places it at the left edge under the target's lowest widget, keeping the width and height it had. A `layout` sent with a move is ignored, so a moved widget always lands below the target's widgets. The config is re-validated only when `config` or `chartType` is sent, as today, so a PATCH of `{ dashboardId }` alone moves a stale widget.
- **ui form:** `WidgetDialogData` gains `dashboards` and `dashboardId`. When editing, and when the project has more than one dashboard, the form shows a "Dashboard" select after the title, defaulting to the widget's own. The form keeps an `edited` flag that every control except the Dashboard select sets. On save it sends `{ dashboardId }` alone while `edited` is unset, and the whole body plus `dashboardId` otherwise. It never compares the draft with the stored config, because the draft is a normalized recomputation that can differ for exactly the stale widgets the rule exists for. `fieldFor` maps `dashboardId` errors to the select.
- **ui page:** opening the form waits for that widget's pending or in-flight layout save (D12), so a late `{ layout }` cannot land on the new dashboard. When the saved widget comes back with another `dashboardId`, the page removes it from the grid, toasts "Moved to Field", and moves focus to the dashboard's heading, because the widget's menu button is gone. A 400 on `dashboardId` keeps the form open with its error and reloads the project (D12), so a removed target leaves the select and the tab row.

### D10. Routes, addresses and the notice (ui)

- `app.routes.ts`: `dashboards/:dashboardId` loads `ProjectIndicators` (file and class keep their names to limit churn). `indicators` loads a tiny `FirstDashboard` component that, once rendered inside the project page's outlet (so the project is loaded), navigates with `replaceUrl` to the first dashboard.
- `ProjectIndicators` checks each new `dashboardId` against `context.dashboards()` once, before requesting it. An id the project does not have is never requested: the page sets the notice and navigates with `replaceUrl` to the first dashboard.
- **Notice.** `context.notice` holds `{ dashboardId, text }`, where `dashboardId` is the dashboard the member is sent to and `text` names it: "The dashboard you opened does not exist. Showing Health instead." (bookmark) or "\"Housing\" no longer exists. Showing Health instead." (refusal, D12). It is shown only while that dashboard is open, so it survives the redirect itself, and it is cleared when another dashboard opens or the member dismisses it. The text is written into the header's persistent live region (D12) after render, so it is announced once without moving focus. A visible `tmz-alert` (warning, not itself a live region) shows the same text with a "Dismiss" button; dismissing moves focus to the dashboard's heading.
- The empty-state link to the scripts becomes absolute (`['/projects', id, 'scripts']`), like `widget-view.ts:124`.
- *Alternative:* an async `RedirectFunction` on `indicators`. Rejected: it runs before the project page loads, so it would fetch the project a second time, and its snapshot may not carry the parent `:id`. *Alternative:* name slugs in the address. Rejected: a rename breaks bookmarks, user text lands in logs, and a name ending in `.json` or `.svg` would hit nginx's static-file rule (`nginx.conf:100-104`) on reload.

### D11. Tab row (ui)

- `ProjectContext` gains `dashboards = computed(() => this.project()?.dashboards ?? [])`, `setDashboards(list)` (writes into the project signal, so there is one source), `reload(): Observable<ProjectDetails>` (reads, applies and emits the details; `refresh()` stays fire-and-forget for its current caller) and the `notice` signal (D10). `ProjectPage.tabs` builds Performance and Clean data (translated), one item per dashboard (`label` = the name, never passed through Transloco, `link` = `/projects/<id>/dashboards/<dashboard id>`), then Settings.
- `TabsNav` from `md`:
  - the plain items sit in a `flex flex-nowrap overflow-x-auto` list whose native scrollbar is hidden (`[scrollbar-width:none]`, `[&::-webkit-scrollbar]:hidden`), so the dashboards' underlines stay on the nav's border on every platform. Settings sits outside the list, at the end of the nav, so it is always visible;
  - when the list overflows, each side that hides tabs shows a fade and a chevron button. The buttons are `aria-hidden="true"` and `tabindex="-1"`, because Tab already brings a focused tab into view; a click scrolls the list by its width. They update on scroll and through a `ResizeObserver`;
  - every tab is `whitespace-nowrap` and draws an inset focus ring (`focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand`), which the list's overflow cannot clip; Settings uses the same;
  - dashboard tabs add `max-w-48 truncate`, a `title` with the full name for hover, and `focus-visible:max-w-none`, so a focused tab shows its whole name;
  - the current tab is brought into view horizontally only: the list's `scrollLeft` is set so the current link's `offsetLeft` to `offsetLeft + offsetWidth` lies inside the list. This runs in `afterNextRender` after the first render with items (a bookmark opens before the row exists), after the items change (add, move, rename), and after a `NavigationEnd` whose path differs from the previous one. Query-only and fragment-only navigations are ignored, and `scrollIntoView` is not used, so the page never scrolls vertically;
  - a tab that receives focus (Tab, Shift+Tab) is brought into view the same way, clear of the fades, from a `focusin` handler on the list. The browser scrolls a focused element only when it is wholly hidden, and it measures a cut tab before `focus-visible:max-w-none` widens it, so without the handler a focused tab could stay under a fade (found by the stack verification, 13.2).
- Below `md` the select lists the dashboards as plain options after Clean data, as it does today for the fixed tabs.
- Dashboard tabs carry `data-testid="dashboard-tab-<id>"`. The deprecated `router.isActive(url, options)` (`tabs-nav.ts:90`) moves to the `isActive()` function while the file is open.
- *Alternative:* a "More" menu for the tabs that do not fit. Rejected: a menu hides which dashboards exist, and measuring widths to decide what goes in it is fragile. *Alternative:* wrapping onto several rows. Rejected: the underline breaks and Settings drifts. *Alternative:* a visible thin scrollbar. Rejected: classic scrollbars (Chrome on Windows) push the dashboards' underlines above Settings', and overlay scrollbars show nothing.

### D12. Dashboard page (ui)

- **One dashboard at a time.** `ProjectIndicators` gets `dashboardId = input.required<string>()`. The router reuses the component between two dashboards. One effect tracks only `dashboardId()`; its body runs in `untracked(...)` (the `projectId` getter reads the project signal, and so does `context.dashboards()`), so `setDashboards`, `reload` and `refresh` never reset or reload the open dashboard. On each id it:
  1. waits for the previous dashboard's pending and in-flight layout saves (below), without blocking the new load;
  2. resets `widgets`, `allTables`, `exporting`, `error`, and sets `loading`;
  3. checks the id (D10), then loads `GET /indicators` and `GET /dashboards/<id>` together, with `switchMap` on the id, so an answer for an earlier id is dropped.
- **Loading.** While `loading`, the existing skeleton shows with `aria-busy`, and neither the "No widgets yet" card nor "Add widget" is shown. The header is outside the loading and overview branches, so its heading exists from the first render.
- **Layout saves.** `save()` returns a promise and the page keeps an `inFlight` map next to `pending`. "Waiting for the saves" means flushing every pending timer and awaiting every in-flight PATCH. A failed save reports the error and reloads only when the widget's dashboard is still the open one. A `dashboards.notFound` from a save of another dashboard only refreshes the project.
- **Header.** `flex flex-wrap items-center gap-2`:
  - an `h2` with the name (`min-w-0 flex-1 break-words [overflow-wrap:anywhere]`, `tabindex="-1"`), the focus target after add, duplicate, remove, a refusal and a widget moved out. Focus moves to it in `afterNextRender` after the navigation, once it shows the new name;
  - for `analyst` and `admin`, an `ms-auto` group, which wraps under the name below `sm`, with "New dashboard" (secondary, sm) and the "Dashboard actions" menu: Rename, Duplicate, Move left, Move right, Remove. "Add widget" in the widget toolbar below stays the only primary button;
  - one sr-only `role="status"` element rendered empty from the first render. The move announcement and the notice are written into it after render, alternating a zero-width space as `map-widget.ts` does, so repeats are announced.
- **Unavailable actions.** At 7 dashboards, "New dashboard" stays focusable: `aria-disabled="true"`, the click is ignored, and `aria-describedby` points to a visible hint ("The project has the most dashboards it can hold (7)."), shown as a full-width line under the header. `DropdownMenu`'s `MenuItem` gains `disabled?: boolean` (bound to `cdkMenuItemDisabled`) and `description?: string` (a second line, referenced by `aria-describedby`). Disabled items show `aria-disabled:opacity-50 aria-disabled:cursor-not-allowed` and no hover colour. Duplicate at the limit says "A project can have at most 7 dashboards."; the first dashboard's Move left and the last one's Move right are disabled; Remove on the only dashboard says "A project needs at least one dashboard.". While a move is being saved, Move left and Move right are disabled.
- **Name dialog.** A new `DashboardNameDialog` (CDK Dialog, one input) serves add, rename and duplicate. It shows `name` errors next to the input, and problem codes (`dashboards.limit`) and any other field error (such as `widgets` on a duplicate) as the dialog's error, with the API's message. On `dashboards.notFound` it closes with `{ gone: true }` and the page runs the refusal flow. Opened from the actions menu, it restores focus to the menu's trigger, not to the menu item the overlay removes: `DropdownMenu` emits `selected` after `cdkMenuClosed`, when the trigger has focus again. That also fixes the same latent defect of the widget menu's Edit. Duplicate proposes the original name cut to 33 characters (trailing spaces trimmed) followed by " (copy)"; when that name is taken in `context.dashboards()`, " (copy 2)", " (copy 3)" and so on, with the base shortened the same way.
- **Add:** dialog, `POST`, `setDashboards`, navigate to the new dashboard, focus its heading.
- **Rename:** dialog, `PATCH`, `setDashboards`, toast "Renamed to \"Coverage\".", focus back on the actions button.
- **Move left/right:** `PUT /order` with `expectedIds` = the shown order and the two neighbours swapped, `setDashboards` with the answer, then the live region says "Health moved to position 3 of 4". Focus returns to the actions button (CDK Menu's default), so the move can be repeated.
- **Duplicate:** wait for the layout saves, dialog, `POST copies`, `setDashboards`, open the copy, focus its heading.
- **Remove:** reload the dashboard so the count is the server's, then `ConfirmDialog` (danger): "Remove Health and its 5 widgets? This cannot be undone." After the DELETE: navigate to the dashboard now in its place (or the new last one), then `setDashboards`, toast "\"Health\" and its 5 widgets were removed.", focus the heading. Because the load effect ignores list changes, the page never mistakes its own removal for a missing dashboard.
- **Refusals.** Every `dashboards.*` refusal (`notFound`, `limit`, `lastOne`, `orderChanged`), from the page or from a dialog, is handled with `errors.parse`, not `errors.report`, so it shows once:
  - `notFound`: remove the refused id from `context.dashboards()` (setDashboards), then `context.reload()`. When it answers, navigate with `replaceUrl` to the first dashboard of the fresh list, set the refusal notice naming the removed dashboard and the one shown, and focus the heading. If the reload fails, open the first dashboard of the locally corrected list and report the reload's error.
  - `limit`, `lastOne`: keep the dialog or confirmation's message, and `context.reload()` so the header's disabled states and the tab row follow.
  - `orderChanged`: show the message and `context.reload()`.
  - A `WidgetDialog` save answered `dashboards.notFound` closes the dialog with `{ gone: true }`, and the page runs the `notFound` flow.

### D13. Strings and problem codes (ui)

- `en.json`: a `dashboards.*` block (header, actions and their descriptions, the name dialog and its hints, the remove confirmation and toast, the rename toast, the move announcement, the two notices with the shown dashboard's name as a parameter, the limit hint, the chevron labels) and `errors.codes.dashboards.{notFound,limit,lastOne,orderChanged}`. The four codes go into `apiErrorCodes`.
- `indicators.limit`, `indicators.count`, `indicators.noWidgets.*` and `indicators.remove.message` are reworded per dashboard. `project.tabs.indicators` is removed: "Indicators" is now data, the default dashboard's name.

### D14. Docs

- `docs/indicators-dashboard.md`: "the third tab" becomes "the project's own dashboards", a new "Dashboards" section (add, rename, move, duplicate, remove, the limits, who may do what, addresses), the limits table per dashboard, and "A project has one indicators dashboard" leaves "What it does not do". The existing anchors stay, because `scripts.md` and `operations.md` link to them.
- `docs/operations.md`: the variable, its fallback, and a rollback note (Migration Plan).
- `docs/kobo.md`: the roles table.
- `docs/tamiza-brief.md` and `openspec/config.yaml`: "three dashboards", the data model, the roadmap (and a note for `add-audit-log` to record dashboard changes).
- `README.md`: the status line. `docs/scripts.md` is left alone: `test_docs_examples.py` pins its code blocks.

## Risks / Trade-offs

- **[Rollback after `0012` is unsafe for writes]** → An older `api` has no `dashboard_id` in its model: its widget inserts fail on `NOT NULL`, and its overview shows every dashboard's widgets on one canvas, overlapping. The Migration Plan gives the manual SQL and recommends restoring the pre-upgrade backup instead.
- **[Operators who set `IndicatorDashboard__MaxWidgetsPerProject`]** → It is still read when the new variable is not set, with a startup warning, and the docs name both.
- **[The project ceiling grows to 350 widgets by default]** → Only the open dashboard's widgets are loaded and queried. The overview no longer returns widgets. Duplicating copies at most `MaxWidgetsPerDashboard` rows in one transaction.
- **[Other members' tab rows go stale]** → Every refusal reloads the project details, and a removed dashboard's refusal also opens the first dashboard with a notice. A rename by another member shows on reload.
- **[A removal confirmation could understate the widgets]** → The page reloads the dashboard before asking. A widget added between that reload and the click is still removed with the dashboard, as the message warns it cannot be undone.
- **[Two members acting on the same widget at once]** → A layout save read before another member's move committed can write the old coordinates on the new dashboard. That is the last change winning, which the spec accepts, and the widget is not lost.
- **[Ten tabs on a tablet]** → One scrolling row with Settings pinned, chevrons on the hidden sides and the open tab in view. Below 768 pixels the select lists them all.
- **[Map tiles requested again when switching dashboards]** → Maps start only when visible, and Google tile links stay the same for up to an hour, so the browser cache serves most of them. Cache misses still count toward the per-user tile limits, as reopening the page does today.
- **["Indicators" is stored in English]** → The UI is English-only. Analysts can rename it.
- **[`GET /indicators` changes shape and widget routes move]** → The API is the UI's own and ships with it. OpenAPI and the release notes say so.
- **[Test churn: about 30 widget URLs and 20 overview widget reads in the API tests, 5 UI specs on `indicators-testing.ts`]** → The harnesses gain `DashboardId`, `WidgetsUrl`, `WidgetUrl(id)` and `GetDashboardAsync()` (API) and `dashboardId`, `dashboardUrl` and an `open()` that answers both requests (UI), so most tests change one helper call. The tasks order the work so every group ends green.
- **[The role matrix grows]** → The dashboard actions and the widget move join `IndicatorRoleMatrixTests` (about 28 new cases, each with its own database). Accepted: it is how every endpoint's roles are pinned.

## Migration Plan

1. **Deploy as one release.** `0012_add_multiple_dashboards.sql` runs at `api` startup, in one transaction: every project gets "Indicators", every widget moves to it with its layout and config untouched. No downtime beyond the usual restart.
2. **Configuration.** Nothing to change. Operators may rename `IndicatorDashboard__MaxWidgetsPerProject` to `IndicatorDashboard__MaxWidgetsPerDashboard`; the old name keeps working.
3. **Rollback.** Restore the database backup taken before the upgrade together with the previous images. Without a backup, after stopping the new `api`:
   ```sql
   BEGIN;
   ALTER TABLE tamiza.dashboard_widgets DROP COLUMN dashboard_id;
   DROP TABLE tamiza.dashboards;
   DELETE FROM tamiza.schema_versions WHERE scriptname = '0012_add_multiple_dashboards.sql';
   COMMIT;
   ```
   The widgets of every dashboard then share one canvas and may overlap, a duplicated dashboard's widgets appear twice, and the older `api` counts them all against its per-project limit, so a project above 50 widgets refuses new ones. The dashboards' names and order are lost. `docs/operations.md` carries this note.

## Open Questions

None that change the specs, the approach or the task breakdown. Two preferences can be settled during implementation: whether a "+" button at the end of the tab row (from 768 pixels) should also add a dashboard, beside the header's "New dashboard"; and the exact wording of the header's hints and toasts.

## Verification Results

### Stack verification (13.2)

Run on 2026-10-18 in the separate Compose project `tamizaverify` (own env file, port 8189, services `db`, `api` and `ui`; the analytics worker, which this change does not touch, was not started), with a fake OIDC provider (:8190) and a fake Kobo (:8191) on the host and headless Chrome driven over the DevTools protocol at 360, 768, 1024 and 1280 px. The user's `tamiza` stack and Keycloak were not touched.

**Result: one defect found and fixed (keyboard focus in the tab row, below). Everything else passed.**

**Existing project upgraded**
- Pre-change images (`:verifyold`) built from a clean worktree of `HEAD` (3ff0bce). Through the API: two projects (one without widgets); an analyst, a second analyst and a viewer invited; two indicator scripts saved (`cobertura`, `hogares`), their tables seeded with SQL in `p_<id>_indicators`; 12 widgets (stat, bar, column, line, histogram, pie, one with a filter) added through the old route `POST /indicators/widgets`, laid out on five rows. Schema at `0011`.
- `api` and `ui` stopped, the working tree's images (`:verify`) started on the same volume: the log shows `0012_add_multiple_dashboards.sql` executed and "Upgrade successful".
- Database: each project has exactly one dashboard, "Indicators" (`indicators`), at position 0, `created_by` and `updated_by` the project's creator. The 12 widget rows (id, title, chart type, config, layout, `updated_at`, `created_by`) are identical to before the upgrade, all on their project's dashboard; `dashboard_id` is `NOT NULL`.
- API: the details list one "Indicators" for the owner, the analyst and the viewer, and for the project without widgets. `GET /dashboards/{id}` returns the 12 widgets top to bottom, then left to right, each with `dashboardId`. `GET /indicators` has no `widgets` and limits 50, 5000 and 7. The old widget route answers 404 and adds nothing. All 12 widget queries return rows.
- UI (1280): the 12 widgets sit in their former grid cells (`grid-column` and `grid-row` from the old layouts). The tab row reads Performance, Clean data, Indicators, Settings.

**Addresses**
- Old bookmark: `/projects/<id>/indicators` becomes the first dashboard's address, with one dashboard and again with six. The `/indicators` entry is replaced in the history.
- Bookmark to a removed dashboard (viewer): the first dashboard opens, and its tab is the only `aria-current="page"`. The notice reads "The dashboard you opened does not exist. Showing … instead.", and the live region holds the same text. The removed id is never requested, and its address is replaced. Dismiss removes the notice and puts focus on the heading. After another dashboard is opened and the first one again, no notice is shown.
- Bookmark after a rename: the address of a dashboard renamed and moved twice opens it under its new name.

**Managing dashboards** (analyst, 1280)
- Rename "Indicators" from the actions menu: toast "Renamed to …", focus back on the actions button, address and 12 widgets kept.
- Six dashboards added from "New dashboard", named with 27 to 40 characters (one is "Cobertura de vacunación por departamento"). Each opens empty with focus on its heading; the details list the 7 in order.
- At 7: "New dashboard" is `aria-disabled`, still focusable (tabIndex 0) and described by "The project has the most dashboards it can hold (7)."; a click opens nothing. Duplicate is disabled with "A project can have at most 7 dashboards.". An 8th through the API is 409 `dashboards.limit`.
- Move right (pointer): the API order changes, the live region says "Salud y vacunación infantil moved to position 4 of 7", the tab row follows, focus is on the actions button. On the first dashboard, Move left is disabled with "It is already the first dashboard.".
- Name errors show under the dialog's input, with the dialog kept open: another dashboard's name in upper case, "  clean data  ", and 41 characters.
- Remove a dashboard without widgets: the confirmation reads "Remove …? It has no widgets. This cannot be undone.". The dashboard now in its place opens with no notice, only its tab current, a toast, and focus on its heading.
- Duplicate "Indicadores generales del estudio" (33 characters, 11 widgets): the proposed name is "Indicadores generales del estudio (copy)" (40 characters). The copy is placed right after the original, with the same titles, chart types, configs and layouts under new ids, and opens with focus on its heading. Removing a widget from the copy leaves the original's 11.
- Remove the copy: "… and its 10 widgets? This cannot be undone."; its widget rows are gone and the next dashboard opens with the toast. Remove the last dashboard (50 widgets): the confirmation says 50 widgets and the new last dashboard opens.
- Races against the API: 8 parallel adds at 6 dashboards gave one 201 and seven 409 `dashboards.limit`, positions 0 to 6. Two parallel removals of a project's two dashboards gave one 204 and one 409 `dashboards.lastOne`. A reorder with a stale `expectedIds` after another member's move was 409 `dashboards.orderChanged`, and the first move stayed. A viewer's add, rename, remove, reorder and duplicate were each 403 and changed nothing; a non-member got 404.
- Refusals in the page:
  - A rename of a dashboard removed meanwhile opens the first dashboard with "\"Educación y asistencia escolar\" no longer exists. Showing … instead.", focus on its heading, and no toast.
  - A move after another member added a dashboard shows "The project’s dashboards have changed. Reload and try again.". Nothing moves, and the tab row gains the new dashboard.
  - A new widget saved on a dashboard removed while its form was open: the form closes, the first dashboard opens with the notice after one GET of it, and nothing is saved.
  - A keyboard resize saved on a removed dashboard is refused, and the first dashboard opens with the notice.
- Name shown as typed: "<b>Avance</b>" shows as text in the tab, its `title`, the heading and the select, with no `<b>` element.

**Keyboard only**
- Tab from the heading reaches "New dashboard", then the actions button. Enter opens the menu, and the arrow keys reach every item, disabled ones included.
- Move left twice in a row: "position 3 of 7", then "position 2 of 7", with focus back on the actions button each time.
- Rename: the dialog's input has focus; Ctrl+A, typing and Enter save it, with focus back on the actions button and the toast.
- Escape on the rename and on the duplicate dialog: focus back on the actions button, nothing changed.
- Duplicate with Enter: the copy opens with focus on its heading.
- Remove: Tab reaches "Remove dashboard" in the confirmation, Enter removes, and focus is on the next dashboard's heading.
- Enter on "New dashboard", a name and Enter: the new dashboard opens last with focus on its heading.

**Moving a widget**
- The Edit form shows a "Dashboard" select after the title, set to the widget's own dashboard and listing the 7. A new widget's form has no such select.
- Moving "Vacunados por zona" without other edits sends exactly `{"dashboardId": …}` to its current dashboard's URL. The widget leaves the grid, the toast reads "Widget Vacunados por zona moved to Salud y vacunación infantil.", and focus goes to the heading. On the empty target it lands at x 0, y 0, keeping 6×4; a second widget moved there lands at y 4.
- Stale widget moved: with `zona` dropped from the published table, the widget shows "The published table no longer has the columns: zona.". Moved back through the form, the PATCH body is exactly `{ dashboardId }` and the form closes. The widget sits under the lowest widget (y 18) and still names the missing column.
- Full dashboard: a dashboard filled to 50 through the API refuses a 51st (400 on `widgets`). Moving a widget to it through the form shows "A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first." next to the select (`aria-invalid`, described by the error); the form stays open and the widget stays.

**Viewer**
- No "New dashboard", actions menu, "Add widget", widget menus or drag handles. The tab row lists Performance, Clean data, every dashboard in order, and Settings.

**Tab row with 7 dashboards** (names of 27 to 40 characters)
- At 768, 1024 and 1280:
  - The row is one line (the select is hidden) and scrolls inside itself (590, 846 and 1102 of 1628 px). Settings is whole and visible at its end, and the page does not scroll horizontally.
  - On the first dashboard only the end chevron shows (`aria-hidden="true"`, `tabindex="-1"`). It scrolls the row by its width and the start chevron appears; at the far end only the start chevron shows, and the start chevron brings the row back.
  - Every tab's bottom edge, its underline, is at the same y as Settings' and as the nav's border (255 px), before and after scrolling.
- Open tab brought into view: the 7th dashboard's address, opened at 768, shows its tab whole and clear of the fades (`scrollLeft` 1023), with page `scrollY` 0.
- Dashboard opened from its tab, by pointer and by Enter: that tab is the only current one, focus stays on it, and the page does not scroll.
- Page stays in place: on Clean data at 1024, scrolled to the pager (`scrollY` 277), Next changes the query string to `page=2`. `scrollY` stays 277 and the row's scroll stays 0.
- Long name: "Cobertura de vacunación por departamento" is cut at 192 px with `text-overflow: ellipsis`. Its `title` and link text are the whole name, and the accessibility tree names the link with the whole name. On keyboard focus its `max-width` is `none` and the whole name shows (311 px).
- 360: the select lists Performance, Clean data, the 7 dashboards in order and the Settings group, and choosing a dashboard opens it. The page does not scroll horizontally.

**Dashboard header at phone width**
- A 40-character name without spaces ("Coberturadevacunaciónpordepartamento2026"), seen by the project's admin.
- At 360 the name wraps onto two lines inside the viewport (text at 16–338 px). "New dashboard" (170–296 px) and the actions button (304–344 px) are visible, and the page does not scroll horizontally.
- At 768, 1024 and 1280 the name fits on one line with the actions at the right. Its tab is cut, with the whole name in its `title`.

**DEFECT (fixed): a tab reached with Tab could stay hidden under the end's fade**
- Measured at 768 before the fix, Tab through the row: Chrome scrolls a focused element only when it is wholly hidden, and then centres it. A partly visible tab is not scrolled, and a cut tab is measured before `focus-visible:max-w-none` widens it.
  - "Vivienda y servicios públicos" took focus at 452–666 px and "Cobertura de vacunación por departamento" at 408–720 px, in a row ending at 614 px; their ends were under the fade and chevron.
  - The last dashboard's tab took focus at 608–836 px: 6 px of it showed, under the chevron.
  - D11's premise that Tab brings a focused tab into view did not hold, and the long name was not whole on keyboard focus.
- Fix: `TabsNav` handles `focusin` on the list and brings the focused tab into view by the open tab's rule, sharing `bringIntoView`. It reads the tab's size first, which applies the focus style. D11 now says so.
  - New unit test in `tabs-nav.spec.ts`: "brings a tab reached with the keyboard into view, clear of the fades, moving only the list". It fails without the handler and passes with it.
- After rebuilding `ui`, Tab and Shift+Tab through all 10 tabs at 768, 1024 and 1280:
  - every focused tab is whole inside the row, with its inset `ring-2` drawn and the tab itself at the pixel just inside each edge;
  - the page does not move, and the 40-character name shows whole (311 px);
  - the largest overlap with a fade is 1.03 px, from `offsetLeft` and `offsetWidth` being whole pixels, and falls on the transparent start of the gradient.
  - Screenshots of the first tab, a middle one, the long-named one, the last one and Settings at 768 agree.
- After the fix:
  - full UI suite: 68 files, 1,552 tests, 1,550 passed; the 2 failures are the known polling tests;
  - `npm run check:safety` passes;
  - the `ui` image builds (`npm run build`);
  - the `api` log of the whole run holds no warning or error.

**Observation (no change made)**
- At 768 px the 3-column stat tiles wrap their figures ("135 hog / ares"). The widget grid is not part of this change.

**Not checked**
- The native tooltip on hover: headless Chrome draws none, so only the `title` attribute was checked.
- What a screen reader announces (task 13.3). Only the live region's text and the accessibility tree's names were read.
- "Recent move included" and "Original above the widget limit" on the stack. The first needs a drag followed at once by a duplicate; the second needs an operator limit below an existing dashboard's widget count. The UI and API tests cover both.
- The startup warning for `IndicatorDashboard__MaxWidgetsPerProject`. Compose does not pass that variable, so it needs a `compose.override.yaml`; `IndicatorDashboardOptionsTests` covers it.
- Publishing by a pipeline run: the indicator and clean data tables were seeded with SQL.

### Suites (13.1)

Run on 2026-10-18 on the working tree, after the review fixes and the stack check, one suite at a time.

**Result: green apart from the known failures. Nothing needed fixing.**

| Suite | Command | Result |
|---|---|---|
| API unit | `dotnet test` in `tamiza-api/` | 1,055 tests, 1,054 passed, 1 failed (`ExportRegistryTests.The_sweeper_clears_leftover_files_when_the_api_starts`, known and intermittent, see below) |
| API integration | same run | 1,075 tests, all passed (14 m 52 s) |
| UI | `npm test -- --watch=false` in `tamiza-ui/` | 68 files, 1,552 tests, 1,550 passed. The 2 failures are the known polling tests in `project-data.spec.ts` and `project-scripts.spec.ts` |
| UI build | `npx ng build` | Passes. Its one warning, NG8113 (`Card` unused in `ProjectReference`), is in a file this change does not touch |
| UI safety | `npm run check:safety` | Passes ("No markup sinks in src/app.") |
| Analytics | `python -m pytest tests -q` in the `tamiza-analytics-test:dev` image | 618 passed, none skipped (3 m 15 s). The integration tests build their schema from every script, `0012_add_multiple_dashboards.sql` included |

**The sweeper test fails on `HEAD` too (not caused by this change)**
- It failed in the full run and again when the unit project was run alone. Run by itself with a filter, it passes.
- The change touches nothing under `CleanData`. On a clean worktree of `HEAD` (3ff0bce), the unit project failed this test in 1 of 6 runs. On the working tree it failed in 3 of 6 runs, and every other unit test passed in each run.
- Likely cause: the test calls `StopAsync` right after `StartAsync`. On .NET 10, `BackgroundService` starts `ExecuteAsync` as a separate task, so the stop can cancel it before `ClearDirectory` runs. The fix belongs to the test, outside this change.
