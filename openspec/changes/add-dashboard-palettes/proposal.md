# Proposal

## Why

Every dashboard looks the same: a widget with one series is drawn in the first categorical color (blue), and every map and heat map uses the one blue sequential scale. Consultants deliver these dashboards to clients and print them for reports, and they cannot give a dashboard its own look, set a widget apart, or make the same category read in the same color across widgets.

Color also does not always follow the category. A widget's series take their colors from their place in the widget's own rows, so "Female" can be blue in one widget and orange in the next. A pie colors its slices by size after the dashboard filters, so changing a filter can swap two slices' colors.

## What Changes

- **A palette per dashboard.** An `analyst` or `admin` picks one of a fixed set of palettes for each dashboard: *Tamiza* (today's colors, the default), *Institutional*, *Earth* and *Vivid*. A palette is eight categorical colors and a default sequential scale, each with its own light and dark values. Every palette passes the same checks as today's colors, for color-vision deficiencies and for contrast, in both themes. A dashboard uses one palette. Widgets do not mix palettes.
- **A widget's color is a place in the palette, not a color code.** A bar, column or line chart with no series, and a histogram, can use any of the palette's eight colors (color 1 by default, as today). A map or a heat map can use any sequential scale in a fixed set: blue, teal, green, orange, red, purple or gray. By default it uses its palette's scale. A widget keeps its place in the palette when the dashboard's palette changes and when it moves to another dashboard, and is then drawn in that dashboard's colors.
- **Colors kept by value.** A dashboard can assign one of its palette's colors to a value of a column, for example `sex` = `Female` to color 5 or `answer` = `No` to color 8. The value then has that color in every widget of the dashboard that colors by that column:
  - a series value in a bar, column or line chart with a series;
  - a side of a population pyramid;
  - a slice of a pie chart;
  - a bar or column of a chart with no series, so one value can be highlighted.

  Values with no color assigned take the palette's other colors in order.
- **Pie colors follow the slice.** A pie's colors, and which slices fold into "Other", come from the widget's own rows, as a series' colors already do. Changing a dashboard filter no longer swaps two slices' colors.
- **Where it shows.** The dashboard's actions menu gains **Colors**: the palette, with a preview of its colors, and the list of colors kept by value. The widget form gains a color or scale choice for the kinds above, shown in the preview. Printing prints the dashboard's colors. Palettes and colors kept by value are part of the dashboard's versions: changing them makes a version, a restore brings them back, viewing a version shows its colors, and duplicating a dashboard copies them. The MCP tools read and set them.
- **Migration.** `0019_add_dashboard_palettes.sql` adds `dashboards.palette` (`tamiza` by default) and `dashboards.value_colors` (empty by default). Existing dashboards and widgets look exactly as they do today. The only exception is a pie under a dashboard filter, which now keeps its colors.

Not in this change:
- Color codes typed by hand, or a project's own brand palette. Both would need their own light and dark values and their own validation, and are left for a later change.
- Data labels, legend position, subtitles, reference lines and the other display options of a widget. These come in the next change, `add-widget-display-options`.
- Colors for stat tiles, colors by threshold (good/warning/critical), and per-palette diverging scales. The Likert widget keeps today's red and blue arms on every palette.
- Colors in the scheduled dashboard emails, which show tables.

## Capabilities

### New Capabilities

- `dashboard-colors`: the palette catalog and its checks, a dashboard's palette, colors kept by value, a widget's color and sequential scale, and how a widget's colors are chosen from them.

### Modified Capabilities

- `dashboard-indicators`:
  - the pie chart's colors and its "Other" slice come from its own rows;
  - series colors follow the colors kept by value and the dashboard's palette.
- `dashboard-versions`:
  - changing the colors makes a version;
  - a version holds the palette and the colors kept by value;
  - viewing and restoring a version show and bring them back.
- `custom-dashboards`: duplicating a dashboard copies its palette and its colors kept by value.
- `dashboard-printing`: a printed dashboard uses its palette.
- `mcp-server`:
  - `update_dashboard` sets the palette and the colors kept by value, and `get_dashboard` and `get_dashboard_version` return them;
  - `create_widget` and `update_widget` take a widget's color and scale.

## Impact

- **api** (`tamiza-api`):
  - `Data/Dashboard.cs` and `TamizaDbContext.cs`: `Palette` and `ValueColors`.
  - `Indicators/IndicatorModels.cs`:
    - `WidgetConfigInput` and `WidgetConfig` gain `display` (`color`, `ramp`);
    - `DashboardView` gains `palette` and `valueColors`;
    - `IndicatorQueryResponse` gains a pie's `domain`.
  - New `Indicators/Palettes.cs`: palette and scale ids, limits.
  - `Indicators/IndicatorValidation.cs` and `DashboardValidation`: rules for `display`, the palette and the colors kept by value.
  - `Indicators/IndicatorQueryBuilder.cs`, `IndicatorSeries.cs` and `SeriesQueryBuilder.cs`: a pie's domain over its own rows.
  - `Indicators/DashboardEndpoints.cs`: `PUT /dashboards/{id}/colors`, and duplicate copies the colors.
  - `Indicators/DashboardVersions.cs` and `DashboardVersionEndpoints.cs`: the version content and the version details hold the colors, and the new change kind `colorsChanged`.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpDashboardVersionTools.cs`, `Mcp/Instructions.md` and `McpGuide.cs`: the new settings.
  - OpenAPI, unit and integration tests.
- **ui** (`tamiza-ui`):
  - `styles.css`: one token set per palette and per sequential scale, light and dark, and `chart-tokens.spec.ts` checks every set.
  - New `indicators/chart-colors.ts`: the colors of one widget, from the palette, the colors kept by value and the widget's own setting. `chart-scales.ts` loses `seriesColor` and `sequentialColor`; `axis-chart.ts`, `series-legend.ts`, `pie-widget.ts`, `pyramid-chart.ts`, `heatmap-chart.ts` and `map-widget.ts` take their colors from it, and `script-editor/indicator-view.ts` gives its preview chart the default palette's.
  - New `indicators/dashboard-colors-dialog.ts`, and the **Colors** action in `project-indicators.ts`.
  - `indicators/widget-dialog.ts`: the color or scale field and its preview.
  - `indicators/widget-view.ts`: `display` kept by `savedConfig` and left out of `queryFor`.
  - `core/projects/indicators.service.ts`: the new endpoint, fields and change kind.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0019_add_dashboard_palettes.sql` adds the two columns with their defaults and a CHECK on the palette id. `TamizaDbContext` mirrors them, and the rule in `dbUp/README.md` about scripts that rewrite `dashboard_versions.content` names the palette.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: palettes, widget colors, colors kept by value, and how to pick them.
  - `docs/mcp.md`: the new settings.
  - `docs/tamiza-brief.md`: data model and roadmap.
