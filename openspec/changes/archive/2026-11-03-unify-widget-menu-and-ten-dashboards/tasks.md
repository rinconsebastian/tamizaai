# Tasks

Paths: `UI/` = `tamiza-ui/src/app/`, `IND/` = `UI/pages/projects/indicators/`, `API/` = `tamiza-api/src/Tamiza.Api/`, `UT/` and `IT/` = the API unit and integration test projects, `I18N/` = `tamiza-ui/public/i18n/`.

## 1. Ten dashboards in the API and MCP

- [x] 1.1 Set `IndicatorDashboardOptions.MaxDashboards` to 10 (`API/Indicators/IndicatorDashboardOptions.cs:15`). Update the comments that say 7 or count to it: `DashboardEndpoints.cs:281` ("two adds at 9 dashboards give one 10th") and `Data/Dashboard.cs:4` ("1 to 10") (design D1).

  Verify: `dotnet build` passes and `git grep -n "1 to 7\|7th" -- tamiza-api/src ':!tamiza-api/src/Tamiza.Api/Mcp'` finds nothing about dashboards.
- [x] 1.2 Change the three MCP tool descriptions in `API/Mcp/McpDashboardTools.cs`: `list_dashboards` (:131, "1 to 10"), `create_dashboard` (:207, "at most 10") and `duplicate_dashboard` (:300, "Fails at 10 dashboards."). Add a unit test in `UT/Indicators/IndicatorDocsTests.cs` that reads the three descriptions with its `Description(tool)` helper (:356-364) and asserts that each contains `IndicatorDashboardOptions.MaxDashboards` (design D1, `mcp-server` "Dashboard limit described").

  Verify: the new test passes, and fails when one description is set back to 7. `git grep -n "1 to 7\|at most 7\|at 7" -- tamiza-api/src/Tamiza.Api/Mcp` finds nothing.
- [x] 1.3 Update the limit tests:
  - `UT/Indicators/IndicatorDashboardOptionsTests.cs:45-51`: rename to `..._10_dashboards` and assert 10.
  - `IT/Indicators/DashboardTests.cs`:
    - `LimitMessage` (:19) says 10.
    - The eighth-dashboard test (:156-168) becomes an eleventh with `FillAsync(h, 10)`.
    - Add a test where adding to a project of 7 succeeds (`custom-dashboards` "Eighth dashboard").
    - The concurrent test (:174-197) becomes "…to a project with nine…" with `FillAsync(h, 9)` and positions `Range(0, 10)`.
    - The duplicate-at-limit test (:902-910) uses ten.
    - Add a test where duplicating a dashboard of a project of 7 succeeds and leaves 8 (`custom-dashboards` "Duplicate of a seventh dashboard").
  - `IT/Mcp/McpDashboardToolsTests.cs`: `maxDashboards` 10 (:71), the loop `<= 10` (:194), the variable and name "Eleventh" (:199-207), and "at most 10 dashboards" (:211).
  - `IT/Indicators/IndicatorOverviewTests.cs:51` and `IT/Indicators/IndicatorWidgetTests.cs:295-296`: the limit triples end in 10.
  - `UT/Mcp/McpResultsTests.cs:65-72`: the sample data says 10, for consistency.

  Verify: these test classes pass with `dotnet test --filter`.
- [x] 1.4 Update `docs/mcp.md:156` to "The project already has 10 dashboards, the most it can have (`status` 409)…".

  Verify: `IndicatorDocsTests` passes (it asserts this line through the constant).

## 2. Ten dashboards in the UI and docs

- [x] 2.1 Set the fallback `maxDashboards` to 10 (`UI/core/projects/indicators.service.ts:85`). Set `errors.codes.dashboards.limit` in `I18N/en.json:164` and `es.json:164` to "A project can have at most 10 dashboards. Remove one before adding another." and "Un proyecto puede tener como máximo 10 tableros. Elimina uno antes de agregar otro." Add a UI test that asserts both texts contain `String(maxDashboards)` (design D1).

  Verify: the new test passes, and `translations.spec.ts` passes.
- [x] 2.2 Update the UI specs and fixtures:
  - `IND/indicators-testing.ts:110` default limits `maxDashboards: 10`.
  - `UI/pages/projects/project-indicators.spec.ts`:
    - :935 and :1224 use `maxDashboards: 10`.
    - `seven` (:1336) becomes `ten` (`length: 10`), and the `(10)` hint at :1400-1410.
    - The 10 in the messages at :1421, :1926 and :2091.
    - `ten.slice(0, 9)` at :1904 and :2080 (rename `six` to `nine`), plain `ten` at :2088 and :2093, and `ten.slice(1)` with `d11` / "Dashboard 11" at :1914.
    - A new case: at 8 dashboards, **New dashboard** and **Duplicate** are available (`custom-dashboards` "Add action below the limit").
  - `IND/dashboard-name-dialog.spec.ts:143` and `UI/shared/ui/dropdown-menu.spec.ts:97,108` say 10.
  - `UI/shared/ui/tabs-nav.spec.ts`: keep the seven-tab case at 1024 pixels (`web-ui` "Seven dashboards at tablet width", still true). Add a ten-tab case at 1024 pixels with names of at least 20 characters ("Ten dashboards at tablet width"). Add an open-tab case on the tenth of 10 ("Open tab brought into view"); the existing one on the seventh stays. The `d7` fixtures and assertions are at :13, :156-221, :237-239, :289, :308 and :314.

  Verify: `npx ng test --watch=false --include=` passes for each of these spec files.
- [x] 2.3 Update the docs that say 7:
  - `docs/indicators-dashboard.md` :3, :25 (every 7, including both quoted messages), :42 and the limits table at :619.
  - `docs/operations.md` :204 ("up to 10 dashboards … 10 times as many widgets") and :209.
  - `README.md:7` ("from one to ten").
  - `docs/tamiza-brief.md` :12, :259 and :277. Leave the history entry 7f as it is.
  - `openspec/config.yaml:16` ("1 to 10 per project").

  Verify: `git grep -n -i "1 to 7\|one to seven\|at most 7\|up to 7\|(7)\|| 7," -- docs README.md openspec/config.yaml` finds nothing about dashboards (except the history entry 7f), the limits table at `docs/indicators-dashboard.md:619` reads "10, and at least 1", and `IndicatorDocsTests` passes.

## 3. Groups, check items and an open busy state in `DropdownMenu`

- [x] 3.1 Extend `UI/shared/ui/dropdown-menu.ts` (design D2):
  - `MenuGroup { id; label?; items }` and a `groups` input. `items` becomes optional. With no `groups`, the items render as today: flat, with no group wrapper.
  - Each shown group is a `cdkMenuGroup`. A labelled group gets an `aria-label` and a visible heading marked `aria-hidden="true"`.
  - Empty groups are left out, with a `role="separator"` between two shown groups. The heading and separators prevent `mousedown`.
  - `MenuItem.checked`. An item that has it renders as `cdkMenuItemCheckbox`, with `[cdkMenuItemChecked]` and a check mark shown when checked.
  - Every item's triggered handler closes the menu through `menuStack.closeAll({ focusParentTrigger: true })`, never `CdkMenuTrigger.close()`. The choice is still emitted after focus is back on the trigger.
  - `busy` keeps the pulse and `aria-busy`, no longer sets `aria-disabled`, and lets the menu open.

  Verify: `dropdown-menu.spec.ts` covers:
  - a flat `items` menu with the same markup as before (no `role="group"`);
  - groups in order with their `aria-label`, an `aria-hidden` heading the arrow keys skip, separators only between shown groups, and no empty group;
  - a check item with `role="menuitemcheckbox"` and `aria-checked` true or false;
  - a click, Enter and a synthetic Space on it each close the menu, and `document.activeElement` is the trigger when the choice is emitted;
  - a mousedown on the heading leaves the menu open;
  - busy pulses, sets `aria-busy`, has no `aria-disabled`, and opens.

  The busy tests at :154-188 (assertions at :161 and :179) are rewritten for the new meaning.
- [x] 3.2 Check the other menus still behave: the dashboard menu, members, reference data and scripts.

  Verify: `project-indicators.spec.ts` (dashboard menu cases), `project-members.spec.ts`, `project-reference.spec.ts` and `project-scripts.spec.ts` pass unchanged.

## 4. One widget menu

- [x] 4.1 Replace the table toggle and the two menus in `IND/widget-grid.ts` with one `tmz-dropdown-menu` (design D3):
  - `icon="more"`, `triggerTestId="widget-menu"` and `paper:hidden`;
  - the label `indicators.grid.menuFor`, or `menuForPreparing` while the widget exports;
  - `busy` while exporting;
  - groups from a `menuGroups(widget, view)` method: `view` (the `widget-show-table` check item, only while drawable), `export` (labelled, today's format items, all unavailable with `widgetExport.reason.preparing` while that widget exports) and `edit` (only while editable: `widget-edit`, and `widget-remove` as danger);
  - one `selected` handler that routes the choice. `table` toggles and sets the polite `status` to `shownAsTable` or `shownAsChart`.

  Remove `actions()` and the template comment at :87. Keep `tableChoice`, the `allTables` reset, `drawable` and `exporting` as they are.

  Verify: `widget-grid.spec.ts` covers:
  - the header holds only the move handle, the title and `widget-menu`;
  - the menu's groups and order for an analyst, for a viewer and with `editable` false;
  - no `widget-show-table` for a stat tile, a cross table or a widget with no rows;
  - choosing it shows the table, sets `aria-checked`, and announces `shownAsTable` in `grid-status`; choosing it again shows the chart, clears `aria-checked` and announces `shownAsChart`;
  - the keyboard: ArrowDown on `widget-menu` opens it, the arrow keys move through its items, and Escape closes it with focus back on `widget-menu`;
  - **Show all as tables** resets single choices;
  - `paper:hidden` on the menu's host, and no breakpoint class.
- [x] 4.2 Add the strings of design D4 in `I18N/en.json` and `es.json`, and reuse `indicators.widget.showTable` for the check item. Remove `indicators.grid.showTable`, `actionsFor`, `edit` and `remove`, and `indicators.widgetExport.button` and `preparing`.

  Verify: `translations.spec.ts` passes, and `git grep -n "grid\.showTable'\|grid\.actionsFor\|widgetExport\.button\|widgetExport\.preparing'" -- tamiza-ui/src` finds nothing.
- [x] 4.3 Rewrite `IND/widget-export-menu.spec.ts` for the Export group (`widget-export` "Export in the widget menu", "Formats that cannot be exported yet", "Feedback while a widget export runs"):
  - the trigger is `widget-menu` named "Options for “…”" for every role;
  - "Export" is a labelled group of three `menuitem`s, with no submenu;
  - the unavailable reasons stay as today;
  - while one widget exports:
    - its trigger is busy and named `menuForPreparing`;
    - its menu opens with the three formats unavailable, reading "Preparing the export…";
    - Show as table still works;
    - the other widget's formats can be chosen;
  - after the export ends, everything is available again.

  Update the `menuItems()` helper to count `menuitem` and `menuitemcheckbox`.

  Verify: the spec passes.
- [x] 4.4 Move the other UI specs to the menu.
  - `widget-table-toggle` becomes a step that opens `widget-menu` and chooses `widget-show-table`:
    - `boxplot-chart.spec.ts:438,452`
    - `chart-widgets.spec.ts:35,65,174`, where `aria-pressed` becomes `aria-checked`
    - `dashboard-print.spec.ts:446`
    - `map-widget.spec.ts:243,256,424,650,653,925,1766`
    - `series-widgets.spec.ts:46,742`
    - `widget-grid.spec.ts:433-434`
    - `project-indicators.spec.ts:1239-1246`
  - `widget-export` becomes `widget-menu`:
    - `widget-export-flow.spec.ts` `choose()` at :14-19, and :59, :65, :113 and :165
    - `project-indicators.spec.ts:3262-3282`
    - `widget-dialog.spec.ts:520`
  - The widget's `menu-trigger` becomes `widget-menu`:
    - `project-indicators.spec.ts:824,965,1788,1825,2761`
    - `map-widget.spec.ts:1164`
    - `widget-dialog.spec.ts:69`
  - Two tests need a rewrite, not only a test-id swap:
    - `project-indicators.spec.ts:1255-1276`: ArrowDown now lands on `widget-show-table`, the first item. Move to `widget-edit` before Enter. The focus-return check at :1275 stays (`dashboard-indicators` "Form cancelled").
    - `project-indicators.spec.ts:1278-1287`: the label becomes `Options for “Coverage by municipality”`, and the items are the full grouped order (Show as table as `menuitemcheckbox`, the three formats, Edit widget, Remove widget). Rename the test (`dashboard-indicators` "Analyst's widget menu").
  - The role checks look for the `widget-edit` item, not a trigger:
    - `project-indicators.spec.ts:524`
    - `widget-export-menu.spec.ts:52`
    - `project-indicators.spec.ts:3264`, where a version view's menu has no `widget-edit` or `widget-remove` (`dashboard-versions` "Widget menu of a version")
  - Reword the test names and comments that describe three controls.

  Verify: each of these spec files passes with `npx ng test --watch=false --include=`.
- [x] 4.5 Update the docs for the widget menu:
  - `docs/indicators-dashboard.md`:
    - :461, the widget menu with its parts, who sees which, and how it differs from the dashboard's own menu;
    - :493, Show as table as a checked menu item, and Show all as tables resets it;
    - :501-509, the Export group in the widget menu, and the busy menu that still opens;
    - :520, the basemap note in the Export group;
    - :587, the version view's menu without Edit or Remove.
  - `docs/operations.md:207`: "A widget's menu offers…".
  - `docs/tamiza-brief.md` :261 and :327, and a new roadmap item 7o `unify-widget-menu-and-ten-dashboards`.

  Add an `IndicatorDocsTests` check, modelled on the dashboard-menu check (`UT/Indicators/IndicatorDocsTests.cs:275-298`). It asserts that `docs/indicators-dashboard.md` lists the widget menu's items in the order `widget-grid.ts` builds them (design D5).

  Verify: `IndicatorDocsTests` passes, and fails when two items of the doc's list are swapped.

## 5. Integration checks

- [x] 5.1 Run every suite: the API unit and integration tests (in the background, about 22 minutes), the full UI suite and `scripts/check-ui-safety.sh`.

  Verify: everything passes except the two known UI polling failures (`project-data.spec.ts`, `project-scripts.spec.ts`) and the known flaky `ExportRegistryTests` sweeper test. Record the counts in design.md under "Verification Results".
- [x] 5.2 On a separate verification stack (`-p tamizaverify`), check with a real browser:
  - a project grows from 7 to 10 dashboards, then **New dashboard** and **Duplicate** are unavailable with the "(10)" reason;
  - an eleventh add through the API answers 409 with 10;
  - the tab row with 10 long names at 768 and 1024 pixels;
  - every widget at 360 pixels has one menu button and an untruncated title area;
  - the menu's parts for an analyst, a viewer and a version;
  - an export in progress leaves Show as table usable;
  - a printed sheet shows no widget menu.

  Verify: screenshots or a recording of each case saved in the session scratchpad. Each result is listed in "Verification Results".
- [x] 5.3 Have the user check the widget menu with a screen reader:
  - the trigger's name;
  - the "Export" group's name, read once;
  - Show as table read as a checkbox, with its state;
  - the announcement after switching;
  - the busy name while exporting.

  Verify: the user confirms it, recorded in "Verification Results".

## Workflow follow-up

- When archiving, change the Purpose of `openspec/specs/custom-dashboards/spec.md` from "up to 7 dashboards" to "up to 10 dashboards". A delta cannot carry it.
- Archive the change after the user's review, then commit on `dev-dashboard-improvements`.
