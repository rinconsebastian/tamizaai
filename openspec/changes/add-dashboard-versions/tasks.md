# Tasks

**Order.**
- Groups 1 to 5 (api) come first, in order; each ends with its tests green.
- Groups 6 to 10 (ui) follow, in order; group 6 keeps the UI build and suite green from the first type change. Group 7 can be done any time after group 6.
- Group 11 is last.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks; "(API part)" and "(UI part)" split a scenario between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~DashboardVersionTests"`.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.

## 1. Schema (api)

- [ ] 1.1 Add `tamiza-api/src/dbUp/Scripts/0018_add_dashboard_versions.sql` exactly as design D1: the table, its index and version 1 of every dashboard. Keep upper-case `CHECK`, `FOREIGN KEY` and `PRIMARY KEY` out of its comment. Verify the analytics integration tests still build their template database.
- [ ] 1.2 Add the entity `DashboardVersion` and its mapping to `TamizaDbContext`. Map `content` and `changes` as `jsonb` strings, as `dashboards.filters` is, and mirror the checks and the unique index. Verify `SchemaDriftTests` passes.
- [ ] 1.3 Add a `ScriptConventionTests` case for the 0018 script, in the style of the 0016 test. Verify it passes.
- [ ] 1.4 Add a `SchemaMigratorTests` upgrade test from `0017`. Seed:
  - a dashboard with three widgets, last edited by two users at different times;
  - an empty dashboard.

  Then run `MigrateAsync`. Verify:
  - each dashboard has one version, number 1, with changes `[{"kind":"initial"}]`;
  - the author and time are those of the latest edit;
  - the widgets are in reading order with their `config` and `layout` unchanged;
  - the empty dashboard's version has `"widgets": []`.
- [ ] 1.5 Update the docs for the schema:
  - `docs/tamiza-brief.md` section 6: `dashboard_versions`;
  - `docs/operations.md`: the rollback of design's Migration Plan, in the style of the 0015 rollback (`:681-685`);
  - `tamiza-api/src/dbUp/README.md`: a migration that rewrites widget configs, filter lists or the chart-type check must also rewrite `dashboard_versions.content`.

  Verify the links resolve.

## 2. Recording versions (api)

- [ ] 2.1 Add `Indicators/DashboardVersions.cs` with the content builder of design D2: widgets in reading order with the `id` tiebreak, and `filters`, `config` and `layout` embedded from their stored JSON. Verify:
  - integration tests: one dashboard gives the same content twice;
  - two contents that differ only in key order compare equal in PostgreSQL;
  - for a dashboard seeded as in 1.4, the content equals what the migration recorded (`Version 1 equals the API's content`).
- [ ] 2.2 Add `Note` and `FlushAsync` (design D3): under the project lock, compare, insert, and prune to 10 (D6). Also add the `DashboardChange` kinds, with the cap of 20 entries, `more_changes`, and no repeat of the last entry (D2). Verify:
  - `DashboardVersionTests`: `Numbers never reused` (API part) and `Eleventh version`;
  - a flush that finds equal content records nothing and returns the current number;
  - unit tests for the cap and the repeat rule.
- [ ] 2.3 Add the growth rules of design D4, with `TimeProvider`, and the constants `LayoutWindow`, `RequestWindow` and `Kept`. Verify:
  - integration tests with a fake clock: `Rearranging a dashboard` (five layout notes within 40 s), `Pause between moves` and `Other change in between` (API parts);
  - an `initial` version never grows;
  - unit tests of the MCP rule: same note, a note differing in case and spaces, another note, another person, and 31 minutes later.
- [ ] 2.4 Add the endpoint filter on the dashboards route group, and make each handler note its change (design D12):
  - `created`, `renamed`, `filtersChanged` and `duplicated` (on the copy only);
  - `widgetAdded`;
  - `widgetChanged`, or `layoutChanged` when only the layout changed;
  - `widgetMovedOut` and `widgetMovedIn`;
  - `widgetRemoved`, with `DELETE … RETURNING title`.

  Verify `DashboardVersionTests`:
  - `Widget added`, `One character`, `Filters saved unchanged`, `Widget moved to another dashboard` and `New dashboard`;
  - `Dashboard moved`, `Scheduled email saved` and `Copy`;
  - `Removed dashboard` (API part: the rows are gone);
  - a refused change, such as a name that is taken, records nothing.
- [ ] 2.5 Record version 1 (`created`, by the creator) of a new project's first dashboard in the project's `SaveChanges`. Verify `ProjectsApiTests`: a new project's dashboard "Indicators" has version 1 by its creator.
- [ ] 2.6 Show that runs and reads make no version. Verify integration tests:
  - `Run publishes new data`: publish tables with `PublishedTables`, then check the versions are unchanged;
  - `Filter values`: `GET .../values` and `POST .../indicators/query` with dashboard filters record nothing.

## 3. Layout of several widgets (api)

- [ ] 3.1 Add `PUT .../dashboards/{dashboardId}/layout` (design D8), with its validation, one transaction and one `layoutChanged` note. Verify `DashboardWidgetTests`:
  - `Drag that pushes other widgets` (API part): four layouts give one version;
  - a widget that is not on the dashboard answers as the `PATCH` does, and saves nothing;
  - duplicate ids, and more entries than the widget limit, answer 400;
  - a `viewer` gets 403.

## 4. History and restore (api)

- [ ] 4.1 Add `GET .../versions` and `GET .../versions/{number}` (design D9), the code `dashboards.versionNotFound`, and `Version` on `GET .../dashboards/{id}`. Verify:
  - `History listed` (API part);
  - `Version no longer kept` for a read;
  - the details' widgets read back with `GetDashboardAsync`'s config reader;
  - `IndicatorRoleMatrixTests` rows for the new routes: `Viewer refused` and `Not a member`.
- [ ] 4.2 Add `POST .../versions/{number}/restore` (design D7). Verify `DashboardVersionTests`:
  - `Removed widget brought back` and `Restore undone` (API parts);
  - `Numbers never reused`;
  - `Scheduled email kept` (the report row is unchanged);
  - `Name taken`, `Widget since moved to another dashboard` (the copy has a new id; "Field" is unchanged), `Indicator gone` and `Limit lowered`;
  - `Changed meanwhile`, and `Version deleted meanwhile` and `Dashboard removed meanwhile` (API parts);
  - restoring the current version writes nothing and answers 200;
  - a restore never grows: a layout note right after it makes a new version;
  - `First change after the upgrade`: a dashboard seeded with an `initial` version 1, as the migration records it.
- [ ] 4.3 Check the OpenAPI document. Verify it lists the four new routes with their request and answer schemas, as `ReportEndpointTests` checks the report routes.

## 5. MCP (api)

- [ ] 5.1 Add the required `change` to `create_dashboard`, `update_dashboard`, `duplicate_dashboard`, `create_widget`, `update_widget` and `remove_widget` (design D10). Validate it first, flush with origin `mcp` after the handlers, and add `dashboardVersions` to their results and `version` to `get_dashboard` and `list_dashboards`. Pass `change` in every existing call of `McpDashboardToolsTests`. Verify:
  - `Request missing`, `Version returned` and `Only the order changed`;
  - `Five widgets, one version`, `New dashboard with its widgets`, `Two dashboards in one request` and `Next request`;
  - `Member changes in between` (the analyst's layout call through REST between Claude's calls);
  - `Long pause` (the harness clock advanced 40 minutes);
  - the version is attributed to the analyst with origin `mcp` and the note.
- [ ] 5.2 Add `McpDashboardVersionTools` (`list_dashboard_versions`, `get_dashboard_version`, `restore_dashboard_version`) and `mcp.dashboardChanged` in `McpRefusal.cs`. Register the class in `McpRegistration.cs`. Verify `McpDashboardVersionToolsTests`:
  - `Undoing a request`, `Comparing a version`, `Viewer` and `Version no longer kept`;
  - `Member changed meanwhile`, where `current` names the version, the author, the time and the origin;
  - `Widget removal undone`;
  - the restore's `copies` for a widget since moved.
- [ ] 5.3 Update the catalog and the role matrix:
  - add the three tools to `McpToolCatalogTests.Expected` with their read-only and destructive flags;
  - add their rows to `McpRoleMatrixTests`;
  - describe every new parameter.

  Verify `Tool list`, `Removal marked` and `Restore marked`, and that every parameter of every tool has a description.
- [ ] 5.4 Update the `remove_widget` and `remove_dashboard` descriptions, and add design D10's bullet to `Mcp/Instructions.md`. Verify `McpGuideTests` checks for `change`, `restore_dashboard_version` and `baseVersion` (`Versions explained`).
- [ ] 5.5 Update the MCP docs:
  - `docs/mcp.md`: the three tools, `change` and the versions in results;
  - `docs/tamiza-brief.md`: "31 tools" becomes 34.

  Verify the catalog test that every tool is named in `docs/mcp.md`.

## 6. Types and service (ui)

- [ ] 6.1 Add to `core/projects/indicators.service.ts`:
  - the types `DashboardVersionSummary`, `DashboardVersionDetails`, `DashboardChange` and `DashboardRestore`, and `version` on `Dashboard`;
  - `listVersions`, `getVersion`, `restoreVersion` and `saveLayout`.

  Verify `ng build`, and a service spec that each method calls its route.

## 7. One layout save per drag (ui)

- [ ] 7.1 Replace the debounce per widget in `project-indicators.ts` with one per dashboard that sends `saveLayout` (design D8), keeping the early sends. Verify `widget-grid.spec.ts` and `project-indicators.spec.ts`:
  - three arrow presses send one `PUT`;
  - a move that pushes another widget sends one `PUT` with both;
  - opening another dashboard, leaving the page, editing a widget and duplicating still send what is pending;
  - a failed save reads the dashboard again.

## 8. Version history (ui)

- [ ] 8.1 Add the **Version history** menu item for `analyst` and `admin`. Verify `Version history offered`, and `No actions for a viewer` (UI part).
- [ ] 8.2 Add `indicators/dashboard-versions-dialog.ts` (design D11), with the change texts per kind, "Current", "via MCP" with the note, "and N more", the author and time, a loading state and a failure state. Verify `dashboard-versions-dialog.spec.ts`:
  - `History listed`, `Version made by Claude` and `Several changes in one version` (UI parts);
  - a widget title holding `<b>` is shown as text;
  - `History at phone width`: every action reachable with the dialog's narrow layout.
- [ ] 8.3 Send pending layouts before the dialog opens, and wait for them. Verify `Move saved before the history opens`.

## 9. Viewing a version (ui)

- [ ] 9.1 Add the read-only view (design D11): the version's widgets and filters, the notice, **Back to the current version**, and **Restore version N** for an older version. Verify `project-indicators.spec.ts`:
  - `Viewing an older version`: no edit, move or remove action, and the widgets query by their configs;
  - `Back to the current version`;
  - the filter values are not written to the address;
  - opening another dashboard leaves the view;
  - focus moves to the notice, and back to the heading.

## 10. Restoring (ui)

- [ ] 10.1 Add the restore, from the dialog and from the view: the confirmation, then send pending layouts, call the API, read the dashboard again, update the tab name, and show one toast. Verify:
  - `Removed widget brought back` and `Restore undone` (UI parts);
  - `Name taken` (UI part: the toast says the name was kept and why);
  - `Filter values in the address`;
  - focus moves to the heading.
- [ ] 10.2 Handle the failures:
  - `dashboards.versionNotFound`: a toast, then the history lists the versions again;
  - `dashboards.notFound`: the existing "gone" flow;
  - a validation error: its message.

  Add `errors.codes.dashboards.versionNotFound` to both files and to `error-codes.spec.ts`. Verify `Version deleted meanwhile`, `Dashboard removed meanwhile` and `Limit lowered` (UI parts).
- [ ] 10.3 Update the user docs:
  - `docs/indicators-dashboard.md`: a "Version history" section covering what is versioned and what is not, moves in a row, requests from AI clients, the 10 kept, viewing, restoring, names, copies and the limit. Change the sentence that says removing cannot be undone (`:21`): a dashboard still cannot be brought back, a widget can.
  - `docs/operations.md`: change the line saying dashboards are not versioned (`:461`).
  - `docs/tamiza-brief.md`: the roadmap entry.
  - `README.md`: the status line.

  Verify the links resolve.

## 11. Verification

- [ ] 11.1 On a real stack in a separate Compose project (`tamizaverify`), with the user's stack untouched:
  - build a dashboard with ten edits, including five drags in a row;
  - open the history at 360, 768 and 1,280 pixels, light and dark;
  - view a version, restore it, and undo the restore;
  - restore a version whose name another dashboard took, and one holding a widget since moved;
  - check that the scheduled email is unchanged and its next send shows the restored widgets (Mailpit);
  - do a keyboard pass.
- [ ] 11.2 From Claude connected to that stack:
  - ask for five widgets on one dashboard and check that the history shows one version "via MCP" with the request;
  - ask Claude to undo it;
  - change the dashboard in the UI, then ask Claude to restore with its old base, and check the refusal with `mcp.dashboardChanged`.

  If no Claude client can reach the stack, run the same steps through an MCP client in tests, and say so in Verification Results.
- [ ] 11.3 Upgrade a copy of a database at `0017` with dashboards to this version. Check that every dashboard lists version 1, "State before version history", and that the first change after the upgrade can be undone.
- [ ] 11.4 Screen reader pass on the history dialog, the view notice and the restore confirmation (NVDA or VoiceOver). Left for the user.
- [ ] 11.5 Write "Deviations from the plan" and "Verification Results" in the design, and run `openspec validate add-dashboard-versions --strict`.

## Workflow follow-up

- Archive the change once it has been reviewed and verified.
- The release notes say that the MCP dashboard write tools now require `change`.
