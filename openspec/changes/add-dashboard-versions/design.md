# Design

## Context

See proposal.md for the motivation. The pieces this change works on:

- **What a dashboard is in the database.**
  - `tamiza.dashboards` holds the name, the position and the filters as `jsonb` (`0012_add_multiple_dashboards.sql:4-19`, `0014_add_dashboard_filters.sql:3`).
  - `tamiza.dashboard_widgets` holds the title, the chart type, `config` (the query, ignored filters, series, pyramid, scale, basemap) and `layout` (`x, y, w, h`), both `jsonb` (`0007`, `0012:31-37`, `0017:1-6`).
  - The scheduled email lives in `tamiza.dashboard_reports` with its recipients and sending state (`0015:6-29`). Printing stores nothing.
  - No table has a row version. Widget writes never touch `dashboards.updated_at`, and reorder, removal and widget deletion set no `updated_by` (`Indicators/DashboardEndpoints.cs:232-269, 565-570`). So nothing today says "this dashboard changed".
- **Who writes it.** Every dashboard write is an `internal static` handler in `DashboardEndpoints` (`:41-56`).
  - Some handlers take the per-project advisory lock in a transaction: add, reorder, remove, duplicate, widget create, and widget move (`LockProjectAsync`, `:578-582`).
  - The others commit by themselves with no transaction: rename and filters with `ExecuteUpdate` (`:142-147, 209-221`), widget edit with `SaveChanges` (`:549`), and widget delete with `ExecuteDelete` (`:568-570`).
  - No other code writes these tables, except project creation, which inserts the first dashboard (`Projects/ProjectEndpoints.cs:137-139`). Runs and `tamiza-analytics` never write them.
- **MCP calls the same handlers** (`Mcp/McpDashboardTools.cs:17`).
  - `update_dashboard` runs rename, filters and move as up to three separate commits (`:196-209`). `create_dashboard` with a position runs two (`:169-173`).
  - The transport is stateless. There is no session id, no request or turn id, and `McpCallScope` lives for one tool call (`Mcp/McpRegistration.cs:33-39`, `Mcp/McpCallScope.cs:5-31`).
  - So nothing in the protocol ties together the five `create_widget` calls of one request.
- **The page writes at once.** There is no edit mode and no Save for the dashboard: each dialog's confirm sends its request.
  - A drag or resize sends one `PATCH .../widgets/{id}` per widget it moved, after a 400 ms debounce per widget (`pages/projects/project-indicators.ts:28-29, 650-691`; `indicators/widget-grid.ts:284-341`).
  - The page reads a dashboard once and does not refresh it (`project-indicators.ts:489-525`).
  - Widgets draw from their config through `POST .../indicators/query` (`core/projects/indicators.service.ts:375`, `indicators/widget-view.ts:281-303`). So a widget that is not saved can be drawn, as the widget form's preview is.
- **The precedent is script versions.**
  - They are append-only rows with `origin` `app|mcp` (`0016_add_mcp_server.sql:8-9`). A restore saves a new version (`Pipeline/ScriptEndpoints.cs:318-336`).
  - MCP writes are guarded by `baseVersion` and refused with `mcp.scriptChanged` (`:356-360`; `Mcp/McpScriptTools.cs:166-183`).
  - The editor has a Versions list marked "via MCP" with read-only viewing (`script-editor/script-side-panel.ts:55-78`, `project-scripts.ts:154-257`).
  - Script versions are not capped.
- **Widgets name their indicator by name** in `config.indicator`, with no foreign key (`Indicators/IndicatorModels.cs:155`). A missing indicator or column shows a message rather than failing (`Indicators/IndicatorDashboard.cs:132-149`).

## Goals / Non-Goals

**Goals:**
- One mechanism, in the API, that records versions for the page, the REST API and MCP alike.
- One version per action of a person and per request of an AI client.
- A restore that never loses the state it replaces.
- Bounded storage per dashboard.
- Additive REST API; the handlers keep their locking and their transactions.

**Non-Goals:**
- Writing the version in the same transaction as the change (D3 explains the trade-off).
- Refreshing open pages when a dashboard changes elsewhere.
- Recording audit events. `add-audit-log` stays planned and may read the same changes later.
- Diffs between versions. A version is a full copy, and the history lists what changed as recorded at the time.

## Decisions

### D1. Data model (DbUp `0018_add_dashboard_versions.sql`)

```sql
-- Dashboard versions. Every change saved to a dashboard records the dashboard as it is after the change: its name, its filters and its
-- widgets (content), with what changed (changes). A dashboard keeps its 10 newest versions; older ones are deleted when a new one is
-- recorded, and all of them go with the dashboard. Origin says whether the change was made in Tamiza or by an AI client, and note holds
-- the request the AI client gave. created_at is the first write of a version and saved_at its last, since one request of an AI
-- client makes one version. Version 1 of every existing dashboard holds its state when version history began.

CREATE TABLE tamiza.dashboard_versions (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    dashboard_id uuid NOT NULL,
    number integer NOT NULL,
    content jsonb NOT NULL,
    changes jsonb NOT NULL DEFAULT '[]',
    more_changes integer NOT NULL DEFAULT 0,
    restored_from integer,
    origin character varying(8) NOT NULL DEFAULT 'app',
    note character varying(120),
    author_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    saved_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_dashboard_versions PRIMARY KEY (id),
    CONSTRAINT fk_dashboard_versions_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_versions_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_versions_users_author_id FOREIGN KEY (author_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_dashboard_versions_number CHECK (number >= 1),
    CONSTRAINT ck_dashboard_versions_more_changes CHECK (more_changes >= 0),
    CONSTRAINT ck_dashboard_versions_origin CHECK (origin IN ('app', 'mcp')),
    CONSTRAINT ck_dashboard_versions_note CHECK (note IS NULL OR origin = 'mcp')
);

CREATE UNIQUE INDEX ix_dashboard_versions_dashboard_id_number ON tamiza.dashboard_versions (dashboard_id, number);
CREATE INDEX ix_dashboard_versions_project_id ON tamiza.dashboard_versions (project_id);
CREATE INDEX ix_dashboard_versions_author_id ON tamiza.dashboard_versions (author_id);

INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, origin, author_id, created_at, saved_at)
SELECT gen_random_uuid(), d.project_id, d.id, 1,
       jsonb_build_object(
           'name', d.name,
           'filters', d.filters,
           'widgets', COALESCE((
               SELECT jsonb_agg(jsonb_build_object(
                          'id', w.id, 'title', w.title, 'chartType', w.chart_type, 'config', w.config, 'layout', w.layout)
                      ORDER BY (w.layout ->> 'y')::integer, (w.layout ->> 'x')::integer, w.created_at, w.id)
               FROM tamiza.dashboard_widgets w
               WHERE w.dashboard_id = d.id), '[]'::jsonb)),
       '[{"kind": "initial"}]'::jsonb,
       'app',
       last_change.updated_by, last_change.updated_at, last_change.updated_at
FROM tamiza.dashboards d
CROSS JOIN LATERAL (
    SELECT edits.updated_by, edits.updated_at
    FROM (
        SELECT d.updated_by, d.updated_at
        UNION ALL
        SELECT w.updated_by, w.updated_at FROM tamiza.dashboard_widgets w WHERE w.dashboard_id = d.id
    ) AS edits
    ORDER BY edits.updated_at DESC
    LIMIT 1
) AS last_change;
```

- `project_id` scopes every read the way `dashboard_widgets` does: `WHERE project_id = @p AND dashboard_id = @d`. It also cascades if a project is ever deleted.
- The unique index serves both "newest first" and pruning. The indexes on `project_id` and `author_id` are the ones EF adds for those foreign keys, as the earlier scripts mirror them (`0003:27`, `0012:24-26`), so `SchemaDriftTests` matches.
- *Alternative:* a `version` column on `dashboards` plus a history table of diffs. Rejected. A full copy per version makes a restore a plain write-back, and keeping only 10 bounds the size (D6).

### D2. What a version holds, and how two are compared (api)

`content` is one JSON object:

```json
{
  "name": "Housing",
  "filters": [ /* dashboards.filters as stored */ ],
  "widgets": [
    { "id": "…", "title": "Water source", "chartType": "bar", "config": { /* as stored */ }, "layout": { "x": 0, "y": 0, "w": 6, "h": 4 } }
  ]
}
```

- **Reading order.** Widgets are kept in reading order: `layout.y`, then `layout.x`, then `created_at`, then `id`. This is the order of `GetDashboardAsync` (`DashboardEndpoints.cs:77-83`) plus a tiebreak, so the same dashboard always gives the same array.
- **Stored JSON as is.** `filters`, `config` and `layout` are embedded exactly as stored, as raw `jsonb` text parsed into `JsonNode`. They are not read and written again through `WidgetConfig`. This means:
  - a version is exactly what was saved;
  - a restore writes back the same JSON;
  - the migration's version 1 equals what the API builds (D5).
- **Comparison.** Two versions are compared in PostgreSQL with `content = @content::jsonb`. `jsonb` equality ignores key order and whitespace and keeps array order.
- **Handlers leave unchanged JSON alone.** Comparing stored JSON works only if a save that changes nothing leaves the stored text as it is. Today the widget edit writes `config` and `layout` again on every `PATCH` (`DashboardEndpoints.cs:461, 543-544`). A widget saved before later config fields existed would then gain keys such as `"seriesBy": null` on a save that changes nothing, which 0017 did not add (`0017:1-2`). So the widget edit and the filters save compare the normalized value they would write with the normalized stored one, and write the column only when the two differ (D12).
- **Left out of `content`:**
  - the position, which is the project's order (`custom-dashboards` "Dashboard order");
  - the scheduled email;
  - the widgets' `created_by/at`, which a restore keeps for widgets that still exist (D7).

`changes` lists what a version holds: `{ "kind", "widget"?, "dashboard"?, "version"? }`.

| Kind | Recorded by | Fields |
|---|---|---|
| `initial` | migration (D5) | none |
| `created` | add dashboard, project creation | none |
| `duplicated` | duplicate, on the copy | `dashboard`: the original's name |
| `renamed` | rename | `dashboard`: the new name |
| `filtersChanged` | filters | none |
| `widgetAdded`, `widgetChanged`, `widgetRemoved` | widget create, edit, delete | `widget`: the title, as it is after the change (before, for a removal) |
| `widgetMovedOut`, `widgetMovedIn` | widget move, one on each dashboard | `widget`, and `dashboard`: the other dashboard's name |
| `layoutChanged` | layout (D8), or a widget edit that changes only the layout | none |
| `restored` | restore (D7) | `version` |
| `unrecorded` | restore (D7), for a state no version held | none |

- Names and titles are kept as they were, so the history says what things were called at the time. They are member text: the page shows them as text, never as markup.
- A version keeps at most 20 entries. Further entries only increase `more_changes`. An entry equal to the version's last entry is not added again, so a request that moves widgets several times in a row lists one `layoutChanged`.
- *Alternative:* compute what changed by diffing consecutive versions when reading. Rejected: the oldest kept version has nothing before it, and a diff cannot tell a widget move from a removal.

### D3. One version per request: the change log and the flush (api)

**The service.** A scoped `DashboardVersions` service collects what a request changed:
- The handlers call `versions.Note(dashboardId, change)` after their write succeeds.
- `versions.FlushAsync(origin, author, note)` then records one version per dashboard noted.

**Two callers.**
- REST: an endpoint filter on the dashboards route group (`MapDashboardEndpoints`, `.AddEndpointFilter<RecordDashboardVersions>()`) flushes with origin `app` after the handler. Each REST request runs one handler, which notes only after its own commit, so a refused change has noted nothing and its flush does nothing. Reads note nothing either.
- MCP: each dashboard write tool flushes with origin `mcp` and its `change` once its handler calls end, in a `finally`. So when a later call is refused or throws, what an earlier call committed is still recorded: `update_dashboard` renames and then refuses the filters (`McpDashboardTools.cs:196-209`), and `create_dashboard` adds and then has its move refused with `dashboards.orderChanged` (`:169-173`). `change` is checked before any handler runs, so a call refused for it notes nothing. On success the result reads the numbers back (D10).

**`FlushAsync`.** It handles each noted dashboard in id order, in its own transaction:
1. `LockProjectAsync`, the same lock the structural handlers take.
2. Build `content` (D2). If the dashboard is gone, skip it.
3. Read the newest version.
4. If its `content` is equal: when D4 says the newest version grows, append the noted changes and set `saved_at`; otherwise record nothing. Return its number. This covers a save that changed nothing, and a change a concurrent flush has already recorded. Two calls of one request running at once (an MCP client may run 4 at a time, `McpOptions.cs:66`) both commit before either flush, so the second flush finds the content recorded and only adds its entry.
5. If D4 says the newest version grows, update its `content` and `saved_at`, and append the changes.
6. Otherwise insert number newest + 1, or 1 when there is none.
7. Prune (D6), then commit.

**Failures.** `FlushAsync` runs with `CancellationToken.None`: the write it records has already committed, and a page closed right after a drag, or a client that cancels its call, must not drop the version. A flush that throws is logged and never changes the handler's answer, so a committed `create_widget` never comes back as a failure that a client would retry. The MCP result then leaves that dashboard out of `dashboardVersions`. The next change records the dashboard as it then is, and a restore records first a state no version holds (D7).

**Why after the write rather than in its transaction.**
- The handlers commit by themselves, several of them with no transaction, and one MCP call runs up to three handlers. A version per handler commit would split one call into several.
- Flushing once at the end of the request makes the request the unit, and leaves the handlers' locking as it is.
- *Alternative:* one transaction per request that wraps every handler and writes the version. Rejected. It would put rename, filters and widget edits under the project lock, which they do not take today. It would also nest transactions across the handlers that MCP chains.
- The cost: a crash between a write and its flush, or a flush that fails, loses that version's granularity, not the state. The next change records the dashboard as it then is, and the version before still holds the state before. A restore, which would overwrite the state, first records it (D7). See Risks.

**Project creation.** The project, its first dashboard and that dashboard's version 1 (`created`, by the creator) are added in the same `SaveChanges` (`ProjectEndpoints.cs:137-139`).

### D4. When the newest version grows instead of a new one (api)

The rows are checked in order; the first that matches decides.

| Newest version | New write | Outcome |
|---|---|---|
| Holds `initial`, `unrecorded` or `restored` | any | new version |
| Same author and origin `mcp`, same `note` (trimmed, ignoring case), `saved_at` less than 30 minutes ago | origin `mcp` | grows |
| Anything else | any | new version |

- Changes made in Tamiza never grow a version: every action of a person is a version of its own, however small, as asked. One drag that pushes other widgets is one action, because the page saves it in one request (D8).
- A restore never grows and is never grown, so it can always be undone on its own.
- Growing keeps the number, `created_at`, the author and the note. It replaces `content` and `saved_at`, and appends the changes.
- The window is a constant of `DashboardVersions` (`RequestWindow`), like the 7-dashboard cap: a product rule, not a setting.
- *Alternative:* also fold one person's moves and resizes made less than a minute apart into one version, so that rearranging a dashboard does not push older versions out of the 10. Rejected: it goes against "a new version for every change, however small", and it would let a version grow while keeping its number, which the `baseVersion` guard compares.

**Why a `change` text and not a time window alone.**
- The stateless server sees five `create_widget` calls as five unrelated POSTs (Context).
- A window alone merges two requests a person makes a minute apart, or splits one request that waits several minutes for a run between two writes.
- The text is also what the person needs to recognize the version in the history.

*Alternatives:*
- A begin/finish tool pair. Rejected: a client that forgets to finish leaves a group open, so a window is needed anyway, and every request costs two more calls.
- One batch tool that takes a list of operations. Rejected: Claude reads between writes (`query_indicator` before each `create_widget`, as `Instructions.md` asks), and the batch would copy every write tool's schema.
- The MCP session id. Not available: the transport is stateless.

**Why `change` is required.** If it were optional, a client that left it out would silently get one version per call. Required makes the client state the request each time, and the refusal says what to pass.

### D5. Version 1 of the dashboards that already exist (migration)

- The migration records every dashboard's current state as version 1 (D1).
  - Its changes are `[{"kind": "initial"}]`.
  - It is attributed to the last `updated_by` among the dashboard row and its widgets, at that time. Today that is the closest record of who last changed it.
- The JSON is built in SQL in D2's shape. A `SchemaMigratorTests` upgrade test checks that, for a seeded dashboard, it equals what `DashboardVersions` builds. Otherwise the first save after the upgrade that changes nothing would still record a version.
- *Alternative:* record the state lazily, before the first change of each dashboard. Rejected: once a handler commits, the state before is gone, so every handler would need a check before its write. The migration does it once.
- A dashboard can have no versions only when the flush after its creation did not run or failed (D3). Its next change then records version 1.

### D6. Ten versions kept (api)

- After inserting version *n*, the same transaction deletes the dashboard's versions numbered *n* − 10 or lower. Numbers keep counting up and are never reused.
- `DashboardVersions.Kept = 10` is a product rule, as the user asked.
- Asking for a deleted or unknown number answers 404 with `dashboards.versionNotFound`.
- **Size.** Typical: a widget's `config` is under 2 KB, so a dashboard of 20 widgets keeps about 400 KB at most. Worst case, at the widget limit's ceiling of 500 widgets, about 10 MB.
- *Alternative:* keep 10 versions besides the current one. Rejected: "the last 10 versions" reads as 10 entries in the list, and the current one is among them.

### D7. Restoring a version (api)

`POST .../versions/{number}/restore` runs in one transaction under the project lock:

1. **Checks.**
   - The dashboard must exist in the project, or the answer is 404 `dashboards.notFound`.
   - The version must be kept, or the answer is 404 `dashboards.versionNotFound`.
   - The version's widgets must fit `MaxWidgetsPerDashboard`, or the answer is `Problems.Validation("widgets", WidgetLimitMessage(max))`, as for a duplicate (`DashboardEndpoints.cs:334-338`).
2. **Unrecorded state.** Build the current content (D2). If it differs from the newest version's, insert it first as version newest + 1, with changes `[{"kind": "unrecorded"}]` and origin `app`, attributed as D5 attributes version 1 (the latest `updated_by` among the dashboard and its widgets).
   - This keeps a state that no version holds: a lock-free rename or widget edit that committed just before the restore took the lock, with its own flush still to come, or a flush that failed (D3). That writer's flush then finds the restored content and records nothing, so without this step the restore would erase the change from the history.
3. **Guard, through MCP only.** `baseVersion` must be the newest number, and step 2 must have found nothing to record. Otherwise the call is refused with `mcp.dashboardChanged`, naming the newest version (D10). A version step 2 recorded is committed all the same.
4. **Nothing to do.** If what the restore would write equals the current content, answer 200 with the current version and write nothing. "What it would write" means the version's content with the current name when step 5 keeps it, so restoring again a version whose name was kept changes nothing.
5. **Name.** When the version's name differs and `NameTakenAsync(except: dashboardId)` finds another dashboard with it, keep the current name and set `nameKept`. Otherwise set `name` and `normalized_name`.
   - Renames take no lock (`DashboardEndpoints.cs:209`), so another dashboard can still take the name before the commit. When the commit hits `ix_dashboards_project_id_normalized_name` (`IsNameTaken`, `:668`), roll back and run the restore again keeping the current name, as `CommitNamedAsync` does for an add (`:646-665`).
6. **Filters.** Set them to the version's.
7. **Widgets.** For each widget of the version:
   - its id is on this dashboard: update its fields where they differ, keeping `created_by/at`;
   - its id is on another dashboard of the project: insert a copy with a new v7 id, and record `{ from, to }` in `copies`;
   - its id is nowhere: insert it with that id, created by the restorer now.

   Then delete this dashboard's widgets that the version does not hold.

   There is no check against the published tables. The config was valid when saved, and a stale widget shows its message, as after a move (`dashboard-indicators` "Moving a widget to another dashboard", "Stale widget configuration").
8. Set `dashboards.updated_by/at`.
9. **The new version.** Insert number newest + 1:
   - `content` is built again after the writes, since copies have new ids;
   - changes are `[{"kind": "restored", "version": N}]`, and `restored_from` is N;
   - origin, note and author come from the caller.

   Then prune (D6).
10. **Answer.** 201 with `Location: .../versions/{new}` and `DashboardRestore(Version, NameKept, Copies, Dashboard)`. `Dashboard` has the shape `GetDashboardAsync` returns. `Copies` lists, for each widget restored as a copy, its title, its new id and the dashboard that still holds the original.

- The restore writes its version itself instead of noting a change, because it must never grow (D4) and must name the version restored. The endpoint filter's flush then has nothing to do.
- A restore from the page is not guarded: the last change wins, as everywhere on the page, and the state it replaces is a version. A restore through MCP is guarded, because an AI client acts on what it read possibly minutes earlier.
- The scheduled email and the position are untouched. The email's next send reads the widgets as they then are (`dashboard-reports` "Email content follows the dashboard").

### D8. Several widgets' layout in one request (api, ui)

**API.** `PUT .../dashboards/{dashboardId}/layout` takes `{ "widgets": [{ "id", "layout" }] }`:
- 1 to `MaxWidgetsPerDashboard` entries with distinct ids, each layout validated as the widget `PATCH` validates it;
- one transaction and one `layoutChanged` note;
- an id that is not a widget of this dashboard answers as the `PATCH` of such a widget answers today, and nothing is saved.

**Page.** One debounce per dashboard replaces the debounce per widget. Every layout change restarts one 400 ms timer, and the save sends all pending layouts in one request.
- The early sends stay: opening another dashboard, leaving the page, editing a widget and duplicating (`project-indicators.ts:468-470, 557, 736-748, 975`).
- New early sends, awaited: opening the version history, viewing a version and restoring.
- A failed save reads the dashboard again, as today (`:714-716`).

`PATCH` with a `layout` stays, for MCP `update_widget` and other clients.

*Alternative:* keep one `PATCH` per widget and fold the layout requests of one drag into one version on the server, by time. Rejected: the server cannot tell the requests of one drag from those of the next drag, a failure halfway would leave a half-saved arrangement, and it would need a growth rule for changes made in Tamiza (D4 rejects one). The batch makes a drag one write.

### D9. REST API (api)

All routes require `analyst` (`RequireProjectRole(ProjectRole.Analyst)`) and live in `MapDashboardEndpoints`.

| Route | Answer |
|---|---|
| `GET .../dashboards/{dashboardId}/versions` | 200 `DashboardVersionList(int Current, IReadOnlyList<DashboardVersionSummary> Versions)`, newest first |
| `GET .../dashboards/{dashboardId}/versions/{number:int}` | 200 `DashboardVersionDetails` |
| `POST .../dashboards/{dashboardId}/versions/{number:int}/restore` | 201 `DashboardRestore` (D7) |
| `PUT .../dashboards/{dashboardId}/layout` | 200 the widgets saved, as `Widget` |

- `DashboardVersionSummary(Number, Current, Name, Widgets, Filters, Changes, MoreChanges, RestoredFrom, Origin, Note, Author, CreatedAt, SavedAt)`:
  - `Widgets` and `Filters` are counts;
  - `Changes` is a list of `DashboardChange(Kind, Widget?, Dashboard?, Version?)`;
  - `Author` is the existing `Person(Id, Name)` from `Pipeline/PipelineModels.cs`.
- `DashboardVersionDetails` adds the filters as `GET .../dashboards/{id}` returns them, and the widgets as `VersionWidget(Id, Title, ChartType, Config, Layout)`. `Config` is read with the same reader as `GetDashboardAsync`, so the page's widget view and MCP use it unchanged.
- `Dashboard`, the answer of `GET .../dashboards/{id}`, gains `Version`: the newest number, null when there is none.
- `dashboards.versionNotFound` joins the codes at `DashboardEndpoints.cs:23-26`.

### D10. MCP (api)

**The `change` argument.** It goes on `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `create_widget`, `update_widget`, `remove_widget` and `restore_dashboard_version`. Its description: "What the person asked for, in a few words (1 to 120 characters), the same on every write you make for that request, for example "Add five water indicators". Writes with the same change on a dashboard make one version of it."
- It is checked before anything runs. A missing, blank or too long value is refused with `mcp.validation` on `change`.
- `remove_dashboard` takes no `change`: its versions are removed with it.

**Results.**
- Each write result gains `dashboardVersions: [{ dashboardId, version }]`, naming both dashboards for a move.
- `get_dashboard` gains `version`, and each entry of `list_dashboards` gains `version`.

**New tools,** in a new class `McpDashboardVersionTools` registered with `.WithTools<>()`. Each gates on `ProjectRole.Analyst` and `McpNeeds.Summaries`. Versions hold no rows, so the *Summaries only* level is enough.

| Tool | Annotations | Arguments | Result |
|---|---|---|---|
| `list_dashboard_versions` | ReadOnly, Idempotent | `projectId, dashboardId` | `{ dashboardId, current, versions: [summary] }`; truncation hint "Older versions are left out." |
| `get_dashboard_version` | ReadOnly, Idempotent | `projectId, dashboardId, version` | the details, widgets in `get_dashboard`'s shape |
| `restore_dashboard_version` | Idempotent=false, Destructive=false | `projectId, dashboardId, version, baseVersion, change` | `{ restored, dashboardId, version, restoredFrom, name, nameKept, copies, dashboardVersions }` |

**The guard.** `mcp.dashboardChanged` joins `McpRefusal.cs` with `current: { version, author, savedAt, origin }`, as `mcp.scriptChanged` does (`McpScriptTools.cs:166-183`).

**Descriptions.**
- `remove_widget` drops "This cannot be undone" and says: "The dashboard's kept versions still hold it. To bring back only this widget, read it with get_dashboard_version and add it again with create_widget; restore_dashboard_version brings back the whole dashboard and also undoes every later change."
- `remove_dashboard` keeps "This cannot be undone" and adds "and its versions are removed with it".

**`Instructions.md`.** The Rules gain this bullet:

> Every dashboard write needs `change`: a few words saying what the person asked for, the same on every write you make for that request and a new one for their next request. One request makes one version of each dashboard it changes. Results give each dashboard's current `version` in `dashboardVersions`. To undo a request, take every dashboard its results named. On each, call `list_dashboard_versions`, check with `get_dashboard_version`, and call `restore_dashboard_version` for the version before the request's, with `baseVersion` the current version. When the request moved a widget, restore the dashboard it moved to first, so the widget comes back with its identifier. A dashboard the request added or duplicated has no earlier version: ask the person before removing it with `remove_dashboard`. To bring back one widget only, read it from a version and add it again. A restore is itself a version, so it can be undone too.

`McpGuideTests` checks for `change`, `restore_dashboard_version`, `baseVersion` and "every dashboard".

**Call records** are unchanged. They hold no argument, so the `change` text is kept only in the version.

### D11. Version history in the page (ui)

**Entry point.** A `versions` item joins the dashboard's menu (`project-indicators.ts:351-382`, dispatched at `:836-860`), for `analyst` and above.

**`dashboard-versions-dialog.ts`.** A CDK dialog built like `run-details-dialog.ts`: a header and a scrolling body, `ariaLabelledBy`, `restoreFocus`.
- It loads the list when it opens.
- Each version in an `<ol>` shows:
  - "Version 12", with a "Current" badge on the newest;
  - up to three change texts, then "and N more";
  - for MCP, the note in quotes and "via MCP";
  - "author · date and time", with the shared `dateTime` pipe (`web-ui`: one absolute format, to the minute). The time is `saved_at`, the last change the version holds, everywhere the page shows a version's time.
- Every version but the current one has **View** and **Restore**.
- It closes with `{ view: n } | { restore: n } | { gone: true }`, and the page handles each.

**Change texts.** One key per kind under `dashboards.versions.changes.*`, with `{{ widget }}`, `{{ dashboard }}` and `{{ number }}`. Names and titles are shown as written (`interface-language`).

**Viewing.** A `viewing` signal holds the `DashboardVersionDetails` on view.
- The grid gets the version's widgets with `editable` false.
- The filter bar gets the version's filters, starting from their defaults. Their values are not written to the address.
- The header's actions are hidden, Print included, since a printout leaves notices out and would not say which version it shows.
- The Add widget buttons are hidden too: in the widgets toolbar, in the no-widgets card and in the card shown before any indicator publishes (`project-indicators.ts:224, 268-272, 290-292`), along with the widget count beside them. The Show tables switch stays, since it saves nothing.
- A notice, announced to screen readers, reads "Viewing version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM. It is read-only." It offers **Back to the current version** and **Restore version 9**.
- Opening another dashboard leaves the view.
- No new API is needed: `widget-view` draws from config.

**Restoring.**
1. `ConfirmDialog` asks first: "Restore version 9?", with "The name, filters and widgets of “Housing” go back to how they were on Oct 31, 2026, 10:32 AM. The current state stays in the history." and the confirm label "Restore".
2. The page sends its pending layouts (D8) and calls the API.
3. On success it:
   - leaves the view;
   - reads the dashboard again (`loads.next(id)`);
   - updates `context.setDashboards` when the name changed;
   - shows one toast (`web-ui`: one message per action). It reads "Restored version 9 as version 13.", plus a sentence when the name was kept because another dashboard is named so, and a sentence naming each widget that came back as a copy and the dashboard that still holds it. A 200 answer instead shows "“Housing” already is as in version 9." and changes nothing on the page.
4. On failure:
   - `dashboards.versionNotFound`: an error toast, and the history lists the versions again;
   - `dashboards.notFound`: the existing "gone" flow (`refused()` and `gone()`, `project-indicators.ts:1127-1165`);
   - a validation error: the API's message.

**Focus.** Starting a view moves focus to the notice. Going back moves it to the heading, and so does a restore.

**Translations.** Every string goes into both `en.json` and `es.json`, with `errors.codes.dashboards.versionNotFound` and its entry in `error-codes.spec.ts`.

### D12. The other write paths (api)

- **Duplicate.** It notes `duplicated` on the copy only. The copy has no versions, so its flush inserts version 1. The page already sends the original's pending moves first (`project-indicators.ts:975`), so those are the original's own version.
- **Remove dashboard.** It notes nothing. The versions go through `ON DELETE CASCADE`.
- **Widget move.** It notes `widgetMovedOut` on the source and `widgetMovedIn` on the target. One flush records both, in id order, each under the project lock.
- **Widget delete.** It reads the title with `DELETE … RETURNING title` and notes `widgetRemoved`.
- **Widget edit.** It notes `widgetChanged`, or `layoutChanged` when only the layout changed. It writes `config` only when the normalized config it would save differs from the normalized stored one (`Serialize(config)` against `Serialize(ReadConfig(widget))`), and `layout` likewise, so a save that changes nothing leaves the stored JSON, and so the content, as it was (D2). It notes nothing when nothing differs.
- **Filters save.** It applies the same comparison to the filter list before `ExecuteUpdate`.
- **Not noted.** Reorder and the scheduled email routes note nothing (`dashboard-versions` "Changes that are not dashboard changes").

## Risks / Trade-offs

- **[Many small changes push useful versions out of the 10]** → Every drag is a version, as asked, so rearranging a dashboard with ten drags fills the history. Several things limit it:
  - one drag, with the widgets it pushes, is one request and one version (D8);
  - the filters dialog and the widget form save once per confirm;
  - a save that changes nothing records nothing (D2, D12);
  - `Kept` is a constant a later change can raise, and D4's rejected alternative can be taken up if the users ask for it.
- **[A client gives a different `change` on each call]** → Each call is a version, as with no grouping. Nothing is lost; the history is finer. The parameter description and the instructions say what to pass, and `McpGuideTests` keeps the instructions saying it.
- **[A client reuses one `change` for a new request within 30 minutes]** → The two requests are one version, and an undo restores the state before both. Accepted. The instructions ask for a new text per request, and any other change in between separates them.
- **[A crash between a write and its flush, or a flush that fails]** → That version is missing (D3). The next change records the dashboard as it then is, and the previous version still holds the state before. A restore first records a state no version holds (D7 step 2).
- **[Two requests on one dashboard at the same moment]** → The flushes are serialized by the lock. A write that commits between another write and that write's flush lands in the version the first flush records. So the second request may find its change already recorded and add nothing, or, when the first is an AI client's request, undoing it also undoes the other write. Accepted: both are rare, and nothing leaves the history.
- **[A restore from the page replaces a change made meanwhile]** → That change is a version of its own. Restoring it brings it back (spec "Restoring while others change").
- **[A later migration rewrites widget configs, filter lists or the chart-type check but not the versions]** → A restore would write an old shape back, or fail the check. `tamiza-api/src/dbUp/README.md` gains the rule that such a migration rewrites `dashboard_versions.content` too. Configs read through `ReadConfig`/`Normalize()`, which already accepts configs saved before later fields existed.
- **[Open pages do not see a restore made elsewhere]** → This is as for every change today. A stale page's next write is itself recorded, and a write to a widget the restore removed is refused as not found, which makes the page read the dashboard again.
- **[Versions keep titles and names after the widget is gone]** → Only the analysts and admins who could read the dashboard can read them. They hold no rows, and they are removed with the dashboard and with the project.

## Migration Plan

1. Deploy the new images. At `api` start, `0018_add_dashboard_versions.sql` creates the table and version 1 of every dashboard, in one transaction: one `INSERT … SELECT` over the dashboards.
2. There is no new configuration.
3. MCP clients get the new schemas when they list the tools. A client that still calls a dashboard write without `change` is refused with `mcp.validation` naming it.
4. Rollback: run the previous images, then:

   ```sql
   DROP TABLE tamiza.dashboard_versions;
   DELETE FROM tamiza.schema_versions WHERE scriptname = '0018_add_dashboard_versions.sql';
   ```

   The versions are lost; dashboards and widgets are unchanged. DbUp never runs scripts backwards, and without the `DELETE` a later upgrade would take the script as applied.

## Open Questions

None that change the specs, the approach or the task breakdown.
