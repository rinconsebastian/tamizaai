# Design

## Context

See proposal.md for the motivation and the specs for the behavior. Paths: `UI/` = `tamiza-ui/src/app/`, `IND/` = `UI/pages/projects/indicators/`, `API/` = `tamiza-api/src/Tamiza.Api/`, `UT/` and `IT/` = the API unit and integration test projects, `I18N/` = `tamiza-ui/public/i18n/`.

### The dashboard limit

- **Server.** The limit is one constant, `IndicatorDashboardOptions.MaxDashboards = 7` (`API/Indicators/IndicatorDashboardOptions.cs:15`, "a product rule, not a setting").
  - `DashboardEndpoints.cs:285` (add) and `:451` (duplicate) check it, under the project's dashboard lock.
  - `Limit()` (`:968-970`) builds the 409 `dashboards.limit` message from it.
  - The overview sends it to the UI as `limits.maxDashboards` (`API/Indicators/IndicatorEndpoints.cs:50`).
- **Database.** Nothing bounds the count: `ck_dashboards_position` is only `position >= 0` (`tamiza-api/src/dbUp/Scripts/0012_add_multiple_dashboards.sql:15`).
- **UI.** The page reads `overview.limits.maxDashboards` and falls back to `maxDashboards = 7` (`UI/core/projects/indicators.service.ts:85`, `UI/pages/projects/project-indicators.ts:414`). The hint and the unavailable reasons use `{{ max }}`.
  - The refusal text is the one exception. `UI/core/api-error.ts:51` shows the key `errors.codes.dashboards.limit` (`I18N/*.json:164`), a literal "7" with no parameter, instead of the API's message. The other `limit` key, `dashboards.unavailable.limit` (`:1929`), already takes `{{ max }}`.
- **MCP.** The descriptions of `list_dashboards`, `create_dashboard` and `duplicate_dashboard` are attribute strings with a literal 7 (`API/Mcp/McpDashboardTools.cs:131,207,300`).
- **Tab row.** It scrolls inside itself whatever the number of tabs (`UI/shared/ui/tabs-nav.ts`).

### The widget header

The header is in `IND/widget-grid.ts:59-100`. It holds:
- the move handle (editors, from `md`);
- the title;
- the table toggle, an icon button with `aria-pressed`, only while `drawable()[id]`;
- the Export `tmz-dropdown-menu`, with `icon="download"`, `busy` while that widget exports, and items built by `exportItems(view.exportState())`;
- for editors, a second `tmz-dropdown-menu` with Edit and Remove, under the default test id `menu-trigger`.

Every control has `paper:hidden`. `WidgetGrid` is used only by the dashboard page, and also shows a version's widgets (with `editable` false).

### The shared menu

- `DropdownMenu` (`UI/shared/ui/dropdown-menu.ts`) renders a flat list of `cdkMenuItem` buttons, with disabled items that keep a described second line.
- It emits the choice only after the menu closed and focus is back on the trigger (design D12 of add-multiple-dashboards).
- `busy` makes the trigger pulse and refuse to open (asserted at `dropdown-menu.spec.ts:161` and `:179`). The widget Export menu is the only caller that passes `busy`. The other users (the dashboard menu, members, reference data, scripts) pass only `label`, `items` and the test id.
- The CDK already offers `CdkMenuGroup` and `CdkMenuItemCheckbox`, and `IND/map-controls.ts:55-88` uses groups with a `border-t` and checked radio items.

## Goals / Non-Goals

**Goals:**
- One constant on each side. The server's limit stays the single source, and every text that cannot read it is checked by a test.
- One menu component. The widget menu is built from `DropdownMenu`, extended so its other callers do not change.
- The widget's export, table and editing logic stays where it is. Only the controls that start them move.

**Non-Goals:**
- Making the dashboard limit configurable, or passing parameters through `errors.codes.*`.
- Submenus in `DropdownMenu`.
- Touching `WidgetView`'s own chart/table switch in the widget form's preview.

## Decisions

### D1. The limit stays a constant, raised to 10 on both sides
- `MaxDashboards` becomes 10, and so does the UI fallback `maxDashboards`.
- The add and duplicate checks, the 409 message, the overview limit, the MCP `maxDashboards` field and the UI's hint already read the constant. They change by themselves.
- **The refusal text in `en.json` and `es.json` stays a literal**, now "10".
  - The alternative was `{{ max }}`. That needs an extension on the API's problem, plus parameters through `ApiErrors.translated`. It is not worth it for a number that changes once.
  - A UI test asserts that `errors.codes.dashboards.limit` names `maxDashboards`, so the two cannot drift apart.
- **The three MCP descriptions stay literals too.** C# attribute strings cannot interpolate an `int`. A unit test in `UT/Indicators/IndicatorDocsTests.cs` reads the three tools' descriptions through its `Description(tool)` helper (`:356-364`, already used for tool text at `:194-195`) and asserts that each contains `IndicatorDashboardOptions.MaxDashboards`.
- No setting and no migration. The lock and the count check already handle concurrent adds whatever the number.

### D2. `DropdownMenu` learns groups and a check item, and `busy` stops blocking
Groups and check items are optional additions, so the four other callers are unchanged. `busy` changes meaning; only the widget menu passes it.

- **Groups.**
  - A new input `groups: MenuGroup[]`, with `MenuGroup = { id: string; label?: string; items: MenuItem[] }`. `items` becomes optional.
  - **With no `groups`, the items render exactly as today**: a flat list, with no group wrapper and no separator. The other menus keep their markup.
  - With `groups`, each shown group is a `cdkMenuGroup`, whose host already sets `role="group"`. A group with a `label` gets that label as its `aria-label`, and shows it as a small visible heading marked `aria-hidden="true"`. The CDK key manager holds only menu items, so the arrow keys never land on the heading or a separator. A screen reader hears the name once, from the group.
  - Groups that hold no item are left out. A `role="separator"` line goes between two shown groups.
  - The heading and the separators get `(mousedown)="$event.preventDefault()"`. The menu host is not focusable, so a click on them would send focus to `body`, and the CDK would then close the menu without returning focus to the trigger.
  - Rejected: a heading as a disabled item. The arrow keys would stop on it, and it would read as an unavailable action.
- **Check item.**
  - `MenuItem.checked?: boolean`. An item that has it renders as `cdkMenuItemCheckbox`, with `[cdkMenuItemChecked]` and a check mark that shows only when checked. The CDK sets `aria-checked` from it.
  - **Every choice closes the menu and returns focus.**
    - In a browser, click, Enter and Space on a `<button cdkMenuItemCheckbox>` all close the menu already: Space on a button is a native click, which runs `trigger()` and then `closeAll({ focusParentTrigger: true })`.
    - The CDK keeps the menu open only for an untrusted Space keydown (`closeOnSpacebarTrigger = false` on selectable items), which is what the specs' `pressKey` sends, or on a non-button host.
    - To make every path behave the same, the item's triggered handler calls the menu's own stack: `#m="cdkMenu"` and then `m.menuStack.closeAll({ focusParentTrigger: true })`. On the paths where the CDK closes the menu itself, its later `closeAll` finds an empty stack and does nothing.
    - **Not `CdkMenuTrigger.close()`.** It detaches the overlay without focusing the trigger, so focus falls to `body` before the choice is emitted (D12).
  - Rejected: a label that swaps between "Show as table" and "Show as chart". It changes the item under the member's eyes and does not say which view is current. A check item says both.
- **Busy no longer blocks.**
  - `busy` keeps the pulse and `aria-busy="true"`, but the trigger keeps its menu and drops `aria-disabled`.
  - The caller marks the items that cannot run while it is busy. The widget menu is the only caller of `busy`.
  - This matches `web-ui` "Feedback while an action runs": the control that started the action is the format item, which is unavailable until the export ends, and other controls stay usable.

### D3. One widget menu in `WidgetGrid`
- **The header** keeps the move handle and the title, then one `tmz-dropdown-menu`. It has:
  - `icon="more"` and `triggerTestId="widget-menu"`;
  - `class="paper:hidden"`, with no breakpoint class;
  - the label `indicators.grid.menuFor`, "Options for “{{ title }}”". While the widget exports, it uses `indicators.grid.menuForPreparing`, "Options for “{{ title }}”, preparing its export".

  The toggle button and the second menu are removed.
- **Its groups** are built by one `menuGroups(widget, view)` method:
  1. `view`: `{ id: 'table', label: t('indicators.widget.showTable'), checked: shownAsTable(widget), testId: 'widget-show-table' }`, only while `drawable()[widget.id]`.
  2. `export`, labelled "Export": today's `exportItems(view.exportState())`. While `isExporting(widget)`, the three items are disabled, with the description `indicators.widgetExport.reason.preparing`, "Preparing the export…".
  3. `edit`, only while `editable()`: Edit widget and Remove widget (danger), with today's test ids `widget-edit` and `widget-remove`.

  The format items keep `widget-export-png`, `widget-export-svg` and `widget-export-xlsx`.
- **One `selected` handler** routes the choice by id:
  - `table` goes to `toggleTable` and sets the grid's existing polite `status`: "“{{ title }}” is shown as a table" or "… as a chart" (`indicators.grid.shownAsTable` and `shownAsChart`).
  - The formats go to `exportChosen`.
  - `edit` and `remove` go to `act`.
- **The rest stays.** `tableChoice`, `allTables` and the reset effect, `drawable`, `exporting` and the page's one-export-per-widget guard (`project-indicators.ts:730-743`) are unchanged. Focus returns to the menu trigger after a cancelled widget form, through D12.
- **The trigger test id changes on purpose.** `widget-menu` replaces both `widget-export` and the widget's `menu-trigger`. A test that used the trigger's presence to mean "can edit" then fails, instead of passing on the trigger now shown to every member. Those tests move to the `widget-edit` item.
- **Icon and placement.** The trigger keeps the three dots, the same as the dashboard's own menu. The docs tell them apart by place: the dashboard menu is in the dashboard header, the widget menu at the right of a widget's title.

### D4. Strings
- **New, in `en.json` and `es.json`:**
  - `indicators.grid.menuFor`: "Options for “{{ title }}”" / "Opciones de “{{ title }}”"
  - `indicators.grid.menuForPreparing`: "Options for “{{ title }}”, preparing its export" / "Opciones de “{{ title }}”, preparando su exportación"
  - `indicators.grid.exportGroup`: "Export" / "Exportar"
  - `indicators.grid.shownAsTable`: "“{{ title }}” is shown as a table" / "“{{ title }}” se muestra como tabla"
  - `indicators.grid.shownAsChart`: "“{{ title }}” is shown as a chart" / "“{{ title }}” se muestra como gráfico"
  - `indicators.widgetExport.reason.preparing`: "Preparing the export…" / "Preparando la exportación…"
- **Reused:** `indicators.widget.showTable`, "Show as table" / "Mostrar como tabla", which the widget form's own switch already uses (`IND/widget-view.ts:210`). The item and that switch then read the same.
- **Removed, once unused:**
  - `indicators.grid.showTable` and `actionsFor`
  - `indicators.widgetExport.button` and `preparing`
  - `indicators.grid.edit` and `remove`, which were already unused
- `translations.spec.ts` keeps en and es in step.

### D5. Docs checked by tests
- **`docs/mcp.md:156`** is already asserted through the constant by `IndicatorDocsTests`, so it must say 10.
- **A new `IndicatorDocsTests` check for the widget menu**, modelled on the dashboard-menu check (`UT/Indicators/IndicatorDocsTests.cs:275-298`). It asserts that `docs/indicators-dashboard.md` lists the widget menu's items in the order `widget-grid.ts` builds them (Show as table, Export with its three formats, Edit widget, Remove widget), so a later reorder updates the doc.

## Risks / Trade-offs

- **[Risk] Switching one widget to its table takes two clicks instead of one.** → **Show all as tables** stays one click on the toolbar. The check item says which view is shown, which the icon toggle did not.
- **[Risk] Every member, viewers included, now has a three-dots button on each widget, like the dashboard's own menu.** → The two menus are named differently ("Options for “…”" against "Dashboard actions"), and they sit in different places. The docs describe both.
- **[Risk] Tests that read `menu-trigger` or `widget-export` silently mean something else.** → The new test id `widget-menu` makes them fail loudly (D3). There are about 40 such lines across 10 UI spec files (see tasks).
- **[Risk] `busy` changes meaning for every caller of `DropdownMenu`.** → Today only the widget menu passes it. The component's spec is rewritten for the new meaning.
- **[Risk] A longer menu near the bottom of a phone screen.** It holds at most 6 items, a heading and two separators. → The CDK overlay flips above the trigger when there is no room below, as the other menus already do.
- **[Trade-off] The refusal text and the MCP descriptions repeat the number 10 by hand.** → Tests tie each one to the constant (D1).

## Migration Plan

- No database or configuration change. Deploy `api` and `ui` together, as usual.
- **Rollback.** An older `api` brings back the limit of 7.
  - A project that has 8 to 10 dashboards keeps them all: the older code only refuses adds and duplicates while the count is at least 7.
  - Restoring a version never adds a dashboard.
  - An older `ui` against the new `api` reads 10 from the overview. Only its refusal text would say 7.
- **MCP clients** that cached the tool list see the new descriptions the next time they list the tools.

## Verification Results

Run on 2026-11-03 on `dev-dashboard-improvements`, with the changes not yet committed.

- **API unit tests:** 1825 of 1825 passed.
- **API integration tests:** 1919 of 1921 passed. The two that failed are `IndicatorSeriesQueryTests.A_ratio_total_is_the_ratio_of_the_sums_and_not_a_sum_or_a_mean_of_the_cells` and `A_ratio_cell_whose_denominator_sums_to_zero_has_no_value_and_its_total_has_none`. They compare "0.5" with "0,5", a decimal separator that depends on the culture of the machine. They fail the same way on a clean `HEAD`.
- **UI tests:** 2792 of 2795 passed. Three failed, all also failing on a clean `HEAD`:
  - the two known polling tests (`project-data.spec.ts`, `project-scripts.spec.ts`);
  - `project-indicators.spec.ts` "names the version on the sheet and in the page's title in Spanish", which depends on the time zone of the machine (5:09 against 10:09 a. m.).
- **Build and checks:** `ng build` completes, and `scripts/check-ui-safety.sh` passes.
- **Real stack (task 5.2):** a separate Compose project, a fake OIDC provider and a fake Kobo, and a headless Chrome driven over the DevTools protocol with real key and mouse events. All seven checks passed:
  - **Ten dashboards.** `limits.maxDashboards` is 10. At 9, New dashboard is available. At 10, New dashboard and Duplicate are unavailable with "(10)" and "at most 10 dashboards". An eleventh add and a copy both answer 409 `dashboards.limit`. A stale form shows the message with 10.
  - **Tab row.** With 10 dashboards named with 33 characters, there is no horizontal page scroll at 768, 1024 and 1280 pixels. Settings stays visible, and the tenth tab is brought fully into view.
  - **Widget header.** A viewer has one button and an analyst two at 1280 pixels and one at 360. Titles are not truncated (258 of 258 pixels at 360). The menu button is 40 by 40 pixels, and there is no horizontal scroll at 360.
  - **Menu and keyboard.**
    - Contents are right for an analyst, a viewer and a shown version, and a stat tile has no Show as table.
    - The arrow keys never land on the heading or a separator, and Escape returns focus to the menu button.
    - A real Space, a real Enter and a click on Show as table each close the menu, switch the view, leave focus on the menu button and announce the change.
    - A press on the heading or a separator keeps the menu open.
  - **Export in progress.** With the request held, only that widget's button is busy. Its menu opens with the three formats unavailable ("Preparing the export…") and Show as table works. After the release the formats are available, one message is shown, and the XLSX, PNG and SVG files download with no console error.
  - **Print.** With print media and in the app's paper mode, the widget menu, move handle and resize handle are not displayed. The PDFs were written but not viewed, so this rests on computed styles.
  - **Spanish.** "Opciones de “…”", "Mostrar como tabla", "Exportar", "Editar widget", "Eliminar widget" and "máximo 10 tableros".
- **Screen reader check (task 5.3):** not run. The user asked on 2026-11-03 to mark it done and archive; no one has checked the change with a screen reader.
- **Observation:** the Show as table group and the editing group are `role="group"` wrappers with no name; only Export is named. Screen readers may announce them as unnamed groups.
