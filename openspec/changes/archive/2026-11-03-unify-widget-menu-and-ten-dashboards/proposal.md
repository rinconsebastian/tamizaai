# Proposal

## Why

Two things on a project's own dashboards are now too tight.

- **Seven dashboards is not enough.** Projects that split their indicators by theme, round or region reach seven quickly, and then have to merge dashboards that belong apart. Seven was a product rule, not a technical limit: nothing in the database bounds it, and the tab row already scrolls inside itself.
- **Each widget's header has three controls.** Every widget carries three buttons at the right of its title: a table toggle (an icon with no text), an **Export** menu and, for analysts and admins, an actions menu with **Edit widget** and **Remove widget**. On a phone they take about 124 pixels of a 360-pixel tile, which cuts the title short. Two of them look alike, and the table toggle's icon does not say what it does.

## What Changes

- **Up to 10 dashboards per project.** A project has from 1 to 10 dashboards of its own instead of 1 to 7.
  - The API refuses an eleventh, added or duplicated, with the existing `dashboards.limit` code and a message naming 10.
  - At 10 dashboards, **New dashboard** and **Duplicate** become unavailable and say why, as they do today at 7.
  - The MCP tools `list_dashboards`, `create_dashboard` and `duplicate_dashboard` say 10.
  - The limit stays a product rule, not a setting. No migration is needed.
- **One menu per widget.** The table toggle, the Export menu and the actions menu become a single **widget menu** button (three dots) at the right of the title. It is named after the widget ("Options for “Coverage by municipality”") and holds, in this order:
  1. **Show as table**, a check item that is checked while the widget shows its table. It is only there when the widget has rows to draw as a chart, the same rule as today's toggle.
  2. An **Export** group, under a visible heading, with **PNG image**, **SVG image** and **Excel workbook (XLSX)** as direct items, with no submenu. Formats that cannot be made yet stay in the group, unavailable, with their reason under them, as today.
  3. For analysts and admins, after a separator: **Edit widget** and **Remove widget**.
- **Who sees what.** Every member gets the menu, viewers included. A viewer's menu holds Show as table and Export. While a version of the dashboard is shown, nobody gets Edit widget or Remove widget.
- **An export does not block the menu.** While a widget's file is prepared, its menu button shows that it is busy, but it still opens. Its formats read "Preparing the export…" and are unavailable, and Show as table, Edit widget and Remove widget stay usable. Other widgets' menus are not affected. The export still ends with one message.
- **Unchanged.** These stay where they are: the move handle at the left of the title, the resize handle, **Show all as tables** on the toolbar, the dashboard's own menu, the widget form's preview and the map's own controls. A printed sheet still shows no widget control.

Not in this change:
- The Performance and Clean data dashboards. Their table toggle and Export button stay as they are.
- A setting for the number of dashboards, or a limit above 10.
- New items in the widget menu, such as duplicating a widget or moving it to another dashboard from the menu. Moving stays in the widget form.
- Changes to the formats, files or messages of a widget export.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `custom-dashboards`: "Project dashboards", "Adding a dashboard" and "Duplicating a dashboard" go from at most 7 to at most 10 dashboards.
- `mcp-server`: "Same rules as the UI". The dashboard limit scenario says 10.
- `web-ui`: "Project tab row that does not fit" gains a scenario with 10 dashboards, and the open-tab scenario uses the tenth of 10.
- `dashboard-indicators`: four new requirements:
  - "Widget menu": what it holds, in what order, for whom, and the keyboard;
  - "Show as table in the widget menu": the check item;
  - "Editing actions in the widget menu";
  - "Widget menu at every width and on paper".
- `widget-export`:
  - "Export button" is renamed "Export in the widget menu". The formats become a group of the widget menu.
  - "Formats that cannot be exported yet" refers to that group.
  - "Feedback while a widget export runs": the busy menu still opens, and its formats say the export is being prepared.
- `dashboard-versions`: "Viewing a version". Each widget's menu stays, with Show as table and Export but without Edit widget or Remove widget.

## Impact

- **ui** (`tamiza-ui`):
  - `shared/ui/dropdown-menu.ts`:
    - Two optional additions: groups with an optional heading and separators, and a check item (`menuitemcheckbox`). The other users (the dashboard menu, members, reference data, scripts) do not change.
    - One change of meaning: `busy` no longer stops the menu from opening. Only the widget menu passes it.
  - `pages/projects/indicators/widget-grid.ts`: one menu instead of the toggle and two menus, and a spoken status when a widget switches between chart and table.
  - `pages/projects/project-indicators.ts`: the fallback `maxDashboards` constant in `core/projects/indicators.service.ts` becomes 10. The page already reads the limit from the API.
  - `public/i18n/en.json` and `es.json`: new menu strings and the `dashboards.limit` error text with 10.
  - The UI specs that use the header buttons' test ids.
- **api** (`tamiza-api`):
  - `IndicatorDashboardOptions.MaxDashboards` becomes 10.
  - The three MCP tool descriptions in `Mcp/McpDashboardTools.cs`, and comments.
  - Unit and integration tests of the limit.
- **Database, analytics, nginx and dependencies:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: dashboards, the widget menu, Show as table, Exporting a widget, the version view and the limits table.
  - `docs/operations.md` and `docs/mcp.md`. The `docs/mcp.md` line is checked by `IndicatorDocsTests`.
  - `docs/tamiza-brief.md`, a new roadmap item.
  - `README.md` and `openspec/config.yaml`, which say "1 to 7".
