# Tasks

**Order.**
- Groups 1 to 6 (api) come first, in order; each ends with its tests green.
- Groups 7 to 11 (ui) follow, in order; group 7 keeps the UI build and suite green from the first type change.
- Group 12 (templates and docs) can run any time after group 3.
- Group 13 is last.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match).

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers; run one at a time.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. The two "stops polling when the page is destroyed" tests (`project-data.spec.ts`, `project-scripts.spec.ts`) are known failures. A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.
- **Analytics:** pytest in the `tamiza-analytics-test:dev` Docker image, with the repo mounted at `/repo` and the Docker socket mounted, as in earlier changes.

## 1. Schema (api)

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0014_add_dashboard_filters.sql` exactly as design D1: `tamiza.dashboards.filters jsonb NOT NULL DEFAULT '[]'::jsonb`. Keep upper-case `CHECK`, `FOREIGN KEY` and `PRIMARY KEY` out of its comment. Verify the analytics integration tests still build their template database (they run every script as plain SQL).
- [x] 1.2 Mirror it in EF: `Dashboard.Filters` (`string`), mapped `HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")` as `DashboardWidget.Config` is. Verify `SchemaDriftTests` passes.
- [x] 1.3 Add `The_dashboard_filters_script_adds_one_column` to `ScriptConventionTests`, in the style of the 0012 and 0013 tests, asserting the file name and that it creates no unnamed constraint. Verify it passes.
- [x] 1.4 Add a `SchemaMigratorTests` upgrade test from `0013`. Seed two dashboards, one with 2 widgets, then run `MigrateAsync`. Verify:
  - both dashboards have `filters = '[]'`;
  - the widgets' `config`, `layout` and `updated_at` are unchanged;
  - an insert into `dashboards` without `filters` gets `[]`.

## 2. Filter kinds and a second set of filters (api)

- [x] 2.1 Add `DashboardFilterKinds` (`text`, `boolean`, `number`, `date` as string constants), `DashboardFilterValue(Values, Min, Max)`, `Matches(kind, DataColumnType)`, `ToDataFilter(column, kind, value)` and `CheckValue(kind, value, field, errors)`, as in design D2. Verify unit tests:
  - every kind × every `DataColumnType` for `Matches`;
  - text with 0, 1, 100 and 101 values, and a 501-character value;
  - boolean with `true`, `false` and `yes`;
  - number with `2`, `2.5`, `-1e3` and `second`;
  - date with `2026-09-01` and `2026-13-01`;
  - a range with no end;
  - a reversed range (`Value of the wrong kind` and `Reversed range`, unit part).
- [x] 2.2 Give `DataQueryBuilder.Build` the optional `parameterPrefix` (default `"f"`) and `fieldName` (default `"filters"`) arguments (design D3). Verify:
  - the existing `DataQueryBuilder` and `IndicatorQueryBuilderTests` assertions on `@f0` and `@f1min` pass unchanged;
  - a new test shows prefix `"d"` naming `@d0` and `@d0min`, keeping `tz`, and keying errors as `dashboardFilters[0]`.

## 3. Query with dashboard filters (api)

- [x] 3.1 Add `AppliedDashboardFilter`, `IndicatorQueryRequest.DashboardFilters`, `IndicatorQueryResponse.NotFiltered` (left out when null), `IndicatorValidation.CheckDashboardFilters`, and the `dashboardFilters` parameter of `IndicatorQueryBuilder.Plan` (design D3).
  - Plan builds the matching items as a second set and joins the two `WHERE` fragments with `AND`.
  - It merges the parameters with one `tz`.
  - It sends absent or mismatched columns to `notFiltered`, never to `missing`.
  - It maps error indexes back to the request's.

  Verify `IndicatorQueryBuilderTests`:
  - a widget filter and a dashboard filter on one column both appear in the SQL;
  - a number filter on an integer column binds a `numeric` bound;
  - a date filter on a timestamp column uses the day range in the zone, with one `tz` parameter;
  - a missing column and a text-typed `round` under a number filter are in `notFiltered`, and the plan still has SQL;
  - 50 widget filters plus 8 dashboard filters plan without error;
  - an invalid value is keyed `dashboardFilters[i]` at the request's index.
- [x] 3.2 Extend `IndicatorsHarness.QueryAsync` with optional dashboard filters. Verify in `IndicatorQueryTests`:
  - `Values of a text filter`;
  - `Date range by day` (a timestamp at 23:30 Bogotá kept);
  - `Number range with one end`;
  - `Same column on several widgets` (a column query and a stat query);
  - `Integer and decimal columns`;
  - `Widget filter and dashboard filter on one column`;
  - `Map tooltip narrowed` (the rows count and the tooltip sum);
  - `Dashboard filter before the sum`;
  - `Widget with 50 filters on a filtered dashboard`;
  - `Indicator without the column` and `Column of another kind` (API part: rows unfiltered, `notFiltered` lists the column);
  - `Dashboard filter column removed by a script change` (no `missing.columns`);
  - `Value of the wrong kind` and `Reversed range` (400 naming the column);
  - a request without `dashboardFilters` answering with no `notFiltered` property.

## 4. Saving and reading a dashboard's filters (api)

- [x] 4.1 Add `DashboardValidation.Filters` (design D4). Verify unit tests:
  - 0, 8 and 9 filters;
  - a label null, blank, of 40 and 41 characters, trimmed then measured, or with a tab;
  - `Label taken` in any case;
  - a column `Municipality`, `1round`, `_version`, and one of 64 characters (`Column name not allowed`);
  - `Two filters on one column`;
  - an unknown kind;
  - `Invalid default` and a valid default for each kind.
- [x] 4.2 Add `PUT /dashboards/{dashboardId:guid}/filters` (analyst) with `ExecuteUpdateAsync`, and add `filters` to `DashboardView` from `GET /dashboards/{dashboardId}`. Add `SaveFiltersAsync(filters, client?, dashboardId?)` to `IndicatorsHarness`. Verify a new `DashboardFilterTests.cs`:
  - `Analyst adds a filter` (API part: another dashboard's GET has none);
  - `Ninth filter`;
  - `Two filters on one column`;
  - `Filters reordered`;
  - `New dashboard` (empty `filters`);
  - `Label trimmed`;
  - `Label taken`;
  - `Column name not allowed`;
  - `Invalid default` (nothing saved);
  - `Two saves` (the later wins);
  - `Filters saved on a removed dashboard` (API part: a dashboard removed with SQL answers 404 `dashboards.notFound`, never 500);
  - a filter on a column no table has is accepted.
- [x] 4.3 Make `DuplicateAsync` copy the source's `Filters`, read under its lock (design D4). Verify in `DashboardTests.cs`:
  - `Filters duplicated` (labels, order and defaults, and a widget's `ignoredDashboardFilters` once group 6 lands; until then the filters alone);
  - `Filters of a copy are independent`;
  - the existing duplicate tests unchanged.
- [x] 4.4 Add `filters` (PUT) and `values` (GET, from group 5) rows to `IndicatorRoleMatrixTests`: viewer 403 and 200, outsider 404 and 404, analyst and admin 200 and 200. Extend `A_refused_change_to_the_dashboards_changes_nothing` with a refused filters save. Update `The_openapi_document_describes_the_dashboard_endpoints` with `/dashboards/{dashboardId}/filters put` and `/dashboards/{dashboardId}/values get`, and assert that `DashboardView` has `filters`. Verify `Viewer refused` and `Not a member` (custom-dashboards) and `Viewer cannot edit the filters` (API part) through the matrix.

## 5. Values across the dashboard's tables (api)

- [x] 5.1 Add `IndicatorDashboard.ValuesAsync` and `GET /dashboards/{dashboardId:guid}/values?column=&search=` (viewer), as in design D5, in one slot and one read-only transaction, identifiers only from the catalog. Verify integration tests:
  - `Values of two indicators` (`Cali`, `Buga` and `Palmira` once each, `Cali` first);
  - `Search among values` (more than 50 values, `pal`);
  - a value of 501 characters not listed;
  - an indicator whose `municipality` is an integer column ignored;
  - an unpublished indicator and a dashboard with no widgets answering an empty list;
  - a column name `Municipality` answering 400 on `column`;
  - a search of 501 characters answering 400 on `search`;
  - a removed dashboard answering 404 `dashboards.notFound`;
  - `Not a member` (values) through the matrix row of 4.4.

## 6. Ignored filters in a widget's config (api)

- [x] 6.1 Add `IgnoredDashboardFilters` to `WidgetConfigInput` and `WidgetConfig` (default `[]`), validated in `CheckConfig` (design D6). Verify:
  - unit tests: 8 and 9 entries, a duplicate, and `Municipality`, each keyed `config.ignoredDashboardFilters[i]`;
  - integration tests: a widget saved with `["municipality"]` reads it back; a stored config without the field reads `[]`; `Ignored filter kept by a move` (API part); `Filter defined again` (API part: the entry survives removing and re-adding the filter); `Filters duplicated` (the widget copy keeps it; complete 4.3's assertion); `Viewer cannot change what a widget ignores` (403); `POST /indicators/query` with the field in `config` answering as without it.

## 7. Services and types (ui)

- [x] 7.1 `indicators.service.ts` (design D3 to D6):
  - add `DashboardFilterKind`, `DashboardFilterValue`, `DashboardFilter` and `AppliedDashboardFilter`;
  - add `Dashboard.filters`, `WidgetConfig.ignoredDashboardFilters` (in `emptyConfig()`) and `QueryResult.notFiltered?`;
  - add `saveDashboardFilters(projectId, dashboardId, filters)` and `dashboardValues(projectId, dashboardId, column, search)`;
  - add the optional `dashboardFilters` argument of `query`, sent only when non-empty.

  Make `savedConfig` keep `ignoredDashboardFilters` and `queryFor` leave it out. In `indicators-testing.ts`, `dashboard()` gains `filters: []`, `open()` gains an optional query string, and add an `isDashboardValues` matcher. Verify `indicators.service.spec.ts` covers each URL, verb and body under `http.verify()`, and `query-for.spec.ts` covers the two config functions. The five specs on `indicators-testing.ts` should pass unchanged apart from those helpers.

## 8. The filter dialog (ui)

- [x] 8.1 Give `DataFilterDialog` the optional `title`, `operations` and `removeLabel` inputs (design D7), leaving clean data and the widget form unchanged. Verify `data-filter-dialog` tests (a new spec, or the existing callers' specs):
  - the title shows the label as text;
  - `operations: ['in']` offers only "is one of";
  - `operations: ['range']` on a `decimal` column accepts `2.5`;
  - a `date` column takes days;
  - the remove button reads the given label.

  Verify `project-clean-data.spec.ts` and `widget-dialog.spec.ts` pass unchanged.

## 9. Filter bar, address and widgets (ui)

- [x] 9.1 Add `DashboardFilterBar` (design D7). Verify its spec:
  - `Viewer sees the filter bar` ("Municipality: All", "Round: All");
  - `No filters` (renders nothing);
  - a set value described as in clean data's chips;
  - a 40-character label truncated with its full text as the accessible name and `title`;
  - "Clear filters" only when a value is set;
  - "Default filters" only when the values differ from the defaults;
  - opening a pill opens `DataFilterDialog` with the label as title, the kind's single operation, and values from `dashboardValues` with no counts;
  - a true/false filter offers true and false without a request;
  - `Label shown as typed` (markup shown as text).
- [x] 9.2 `ProjectIndicators` (design D7):
  - the `f` input;
  - `filters` from the dashboard's GET, and `values` from `f` and the defaults;
  - changes navigating with `queryParams: { f }`, and `f` removed when the values equal the defaults;
  - sanitizing with `replaceUrl` and the announced dropped notice;
  - the announcement through `dashboard-status`;
  - everything reset in `open()`.

  Verify `project-indicators.spec.ts`:
  - `Viewer filters a dashboard` (the address carries `f`, no write request is sent);
  - `Filter cleared`;
  - `Back to the defaults`;
  - `Default in force`;
  - `Change announced` (status text set, focus back on the pill);
  - `Shared link` (opening with `f`);
  - `Filter removed since` (notice shown and announced, address replaced);
  - a link with a value of the wrong kind dropped the same way;
  - `Cleared filter in a link` (`f=W10` shows "All" despite a default);
  - `Another dashboard` (switching tabs opens the other dashboard at its defaults);
  - a dashboard reload not triggered by an `f` change.
- [x] 9.3 Pass `dashboardFilters` from the page through `WidgetGrid` to each `WidgetView` minus that widget's ignored columns. Add them to the view's query key and request, and show the "Not filtered by" line (design D7). Verify `widget-grid.spec.ts` and `chart-widgets.spec.ts`:
  - a change re-queries every widget once with the new body and cancels the pending one;
  - `Indicator without the column` and `Column of another kind` (UI part: the line lists the label from `notFiltered`);
  - `Ignored filter` (UI part: the request leaves the filter out and the line lists it);
  - `Filter without a value` (no line);
  - the line is absent on the not-published and missing-column states.
- [x] 9.4 Check `Filter bar at phone width` in a component test at 360 px: 8 filters with 40-character labels, no element wider than the viewport. Add the "Dashboard filters" section of `docs/indicators-dashboard.md` for members: the bar, kinds, clearing, defaults, the address, which widgets a filter narrows, "Not filtered by", and the URL length note from design Risks. Verify the section matches the bar's labels in `en.json` and its links resolve.

## 10. Editing a dashboard's filters (ui)

- [x] 10.1 Add `DashboardFiltersDialog` (design D8). Verify its spec:
  - `Columns of several indicators` ("narrows 4 of 5 widgets");
  - `Integer and decimal together` (one "number" choice);
  - `No published indicator`;
  - adding, editing the label, choosing the column and kind, setting a default through `DataFilterDialog`, moving up and down, removing;
  - "Add filter" unavailable at 8, still focusable, with its hint as the accessible description;
  - a saved filter on a column no table has shown as "in no table";
  - a columns load error shown inside while saving still works;
  - 400 errors on `filters[i].label`, `.column` and `.default` shown on their row;
  - `dashboards.notFound` closing with `{ gone: true }`;
  - Escape cancelling.
- [x] 10.2 Add the "Filters" item to the dashboard's actions menu for analysts and admins, and wire the dialog into the page: new definitions, `f` removed, focus back on the menu button, `{ gone: true }` into the existing removed-dashboard flow. Verify `project-indicators.spec.ts`:
  - `Analyst adds a filter` (UI part);
  - `Filters saved` (bar at the new defaults, focus on `dashboard-menu`);
  - cancelling changes nothing and returns focus there;
  - `Filters saved on a removed dashboard` (UI part: told and taken to the first dashboard);
  - `Viewer cannot edit the filters` and `No actions for a viewer` (no "Filters" item).

  Extend `docs/indicators-dashboard.md` with defining filters (who, limits, labels, columns offered) and verify against the dialog's strings.

## 11. The widget form (ui)

- [x] 11.1 Give `WidgetDialogData` `filters` and `values`. Add the "Dashboard filters" fieldset, keeping the entries for columns without a filter as design D6 says. Give the preview the values minus the unchecked filters, with its "Filtered by" line, and map `config.ignoredDashboardFilters[i]` errors (design D9). Verify `widget-dialog.spec.ts`:
  - `Total that does not change` (UI part: saving sends `ignoredDashboardFilters: ["municipality"]`);
  - `No filters to ignore` (no fieldset);
  - `Preview filtered`;
  - `Ignoring updates the preview` (a new preview query without the filter);
  - an entry for a column the dashboard has no filter on kept on save;
  - unchecking a box marks the form edited;
  - an error on `config.ignoredDashboardFilters[0]` shown at the fieldset.

  Add "Ignoring dashboard filters" to `docs/indicators-dashboard.md` under "Building the dashboard".

## 12. Templates, assistant and docs

- [x] 12.1 Change `count-by-group.py` and `rate-by-group.py` to `GROUPS = ["_version"]` with the comment from design D10, in `tamiza-ui/.../script-templates/indicator/` and byte-equal in `tamiza-api/src/Tamiza.Api/Assistant/Examples/indicator/`, and bump `AssistantPrompts.Version` to 4. Verify:
  - the API unit test that compares the copies passes;
  - `ContextAssemblerTests` pass;
  - `project-scripts.spec.ts` passes ('rate-by-group' still contains `denominator`);
  - `tests/integration/test_templates.py` passes in the analytics image, whole and cell by cell.
- [x] 12.2 Add "Preparing indicators for dashboard filters" to `docs/indicators-dashboard.md` under "What an indicator script must provide" (design D10): group by every filter column; use counts and sums; give rates as numerator and denominator; avoid rows that count one submission twice; name and type a dimension the same in every indicator; integer and decimal, and date and timestamp, count as the same. Link it from `docs/scripts.md`'s indicator rules. In `docs/tamiza-brief.md`, add `dashboards.filters` to the data model and this change to the roadmap. Verify the examples match the templates and the links resolve.

## 13. Verification

- [x] 13.1 Run the full suites: `dotnet test`, the UI suite and `npm run check:safety`, and the analytics suite in the `tamiza-analytics-test:dev` image. Verify they pass apart from the two known UI polling failures, and record the counts in a `## Verification Results` section of design.md.
- [x] 13.2 On a separate Compose project (`tamizaverify`, own env file, port 8189, fake OIDC and fake Kobo, the user's stack untouched), publish two indicators that share `municipality` (text) and `round` (integer in one, decimal in the other), plus one without them. Then check in headless Chrome at 360, 768 and 1280 px:
  - defining 3 filters with a default;
  - `Viewer filters a dashboard`;
  - the "Not filtered by" line;
  - an ignoring widget;
  - `Shared link` and `Filter removed since`;
  - `Filter bar at phone width`;
  - `Filters duplicated`;
  - keyboard-only use of the bar and the filters dialog.

  Record what was checked and any defect fixed in `## Verification Results`.
- [ ] 13.3 Manual check by the user, on a real screen reader: the filter change announcement, the dropped-filter notice, a pill's full label, and the unavailable "Add filter" hint.

## Workflow follow-up

- Archive the change after review. This syncs the new `dashboard-filters` spec and the `dashboard-indicators` and `custom-dashboards` deltas into `openspec/specs`.
- Release notes:
  - dashboard filters, and that indicator scripts must publish the columns to filter on;
  - the updated templates;
  - the rollback note (an older `api` drops widgets' ignored filters when it rewrites them).
