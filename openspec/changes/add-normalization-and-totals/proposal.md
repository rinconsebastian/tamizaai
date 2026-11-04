# Proposal

## Why

The Duitama study publishes most of its questions as percentages, a ratio of `numerator` over `denominator`, but the charts that compare compositions cannot read them. Stacked and 100 % bars refuse a ratio ("Stacked bars add their values, so they need a measure, not a ratio.", `IndicatorValidation.cs:340-342`), and the MCP instructions tell clients to publish counts for them (`Mcp/Instructions.md:34`). So the study keeps 7 count indicators that repeat about 20 questions already published as %, only to feed 22 stacked or 100 % widgets (and 6 Likert widgets). Teatro's "2.8. Iría al teatro, por comuna" (8c4b) reads `teatro_conteo`, although `teatro_intencion` already holds the same shares (74.58 / 10.71 / 12.39 / 2.31 %).

Heat maps and cross tables only show each cell's value, so scripts fake a row %. `teatro_coherencia` (Teatro 5b7a, "lo que pagó frente a lo que pagaría") and `centro_motivo_hora` (Centro ee25) copy every household into every column of its row; their totals then read a meaningless 16.67 % and 20 %. Other heat maps precompute a column % in the script (parqueadero addb, Segmentos 7732, Cruces 6c16).

Totals are duplicated rows. To draw a "Total" line, box or bar, scripts repeat every household under `vehiculo` "Todos los vehículos" (parqueadero 77ea, Centro a137, parqueadero 8492) or a "5. Cualquier componente" category (Cruces 57ee, 64cb). Teatro's demand curve by comuna (3229) has no Total line at all, so the total lives in another widget (233d). The engine already computes each group's total for a widget with a series (`SeriesQueryBuilder.cs:95-98`), but nothing draws it.

Heat maps keep only their largest rows through hand-written lists: Comercio 8849 and 9af8 filter `categoria in [...]` with 10 and 7 values, and Centro 5fdd with 6.

## What Changes

- **Normalize a widget with a series** (`config.normalize`, a field that changes the query, sent by both query twins):
  - bar and column with a series: `none` (the default, today's look), `group` (each bar sums to 100 %), `series` (each series value sums to 100 % across the groups) or `total` (each cell is a share of all the rows);
  - heat map and cross table: `none`, `row`, `column` or `total`;
  - shares are written as percentages without the indicator's unit, with the value in the tooltip and in "Show as table", as the 100 % layout does today. Cells below zero are left out and counted, as today;
  - a cross table normalized by row has a "Total" column at 100 % and a "Total" row that gives each column's share of all rows. A normalized heat map shades its cells by their share;
  - `layout: "percent"` keeps its meaning (stacked, normalized by group), so a `normalize` next to it is refused.
- **A ratio in stacked and 100 % layouts, through its numerator.** A stacked or 100 % widget over a ratio, and any normalized widget over a ratio, sums the ratio's numerator in each cell:
  - stacked segments are counts of the numerator, named after the numerator column (its name from `add-indicator-column-metadata`), and written as whole counts, never in the indicator's percent format;
  - the 100 % layout and `normalize` give the shares of those counts. Teatro 8c4b can read `teatro_intencion` (variable "Iría al teatro (I1)", series `categoria`) and show 74.58 / 10.71 / 12.39 / 2.31 % in each comuna, with no count indicator;
  - when the series column is declared `categories: "multiple"` (`add-small-sample-warnings`), shares of a ratio are refused, because a household is counted once per answer it gave.
- **A Total next to the groups** (`config.includeTotal: true`, a field that changes the query):
  - a line, or grouped bars or columns, with a series gain one more series, "Total": at each group, the group's total over every series value (the sum, or the ratio of sums), which the engine already returns. Teatro 3229 (`teatro_demanda`, grouped by `precio`, series `comuna`) gains a Total line;
  - stacked and 100 % bars gain one "Total" bar after their groups, split by the series totals the engine already returns (the composition of every row);
  - a bar or column with no series gains one "Total" bar after its groups, with the widget's overall value: the figure of the `overall` reference line of `add-chart-annotations`;
  - a heat map gains a "Total" row and a "Total" column. A cross table already has them;
  - the Total comes last, after "Other", in its own color. It is listed in the legend, tooltips, tables, the email and MCP, and it does not count toward the 8 series;
  - over a ratio, it is refused when the group column (with no series) or the series column is declared `categories`: the total of a question's answers is not a figure of the base.
- **Top groups** (`config.top`, a field that changes the query): `{ "count": 1–50, "from": "largest" | "smallest", "other": true | false }`. `from` is `largest` and `other` is `true` by default.
  - Bar and column charts (with or without a series, ranked by each group's total), heat map rows and cross table rows can take it.
  - The kept groups follow the widget's order. The engine combines the rest into one "Other" group, last and in the neutral color, as the sum or the ratio of sums. Over a column declared `single`, a cell's denominator is counted once, by the rule of `improve-stat-cards`. Over a column declared `multiple`, "Other" is refused.
  - With `other: false`, the rest is left out and the widget says how many groups it left out. The row limit counts the kept groups and "Other".
- **Everywhere a widget goes**:
  - the widget form, with help text saying two things: normalizing a table that already holds percentages normalizes it twice, and a Total over rows such as "Todos los vehículos" counts every unit twice;
  - "Show as table", printing and widget images;
  - the scheduled email (body and workbook) and the widget workbook, which show shares in place of values and list the Total and Other rows;
  - the MCP tools: `create_widget` and `update_widget` take the fields, and `query_indicator` and `get_widget_data` return the shares;
  - the guides: `Mcp/Instructions.md` and `docs/indicators-dashboard.md` no longer tell authors to publish count indicators for stacked bars.

Not in this change:
- Normalizing a widget with no series (each bar as a share of all bars), a line chart or a pie (a pie already shows shares).
- Likert bars over a ratio (a decision, see Next step).
- Cumulative shares or running totals, such as a demand curve built by the engine.
- Changing how a series' "Other" is computed today, or fixing it for declared category columns.
- Weighted shares (`add-weighting`).
- Removing the count indicators or the "Todos" rows. Authors switch their widgets when they choose; nothing is migrated.

Depends on:
- `add-indicator-column-metadata`: the numerator column's name for stacked counts.
- `add-chart-annotations`: the `overall` value in the query response, for the Total bar of a widget with no series.
- `add-declared-category-order`: Total and Other come after the declared order and its `last` values.
- `add-category-highlight`: the neutral color for Other and Total.
- `add-small-sample-warnings`: the `categories` declaration, and the n of the Total and Other marks.
- `improve-stat-cards`: the denominator of a `single` category column counted once per cell.

## Capabilities

### New Capabilities

None. Widget queries and drawing belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Bars and columns by series": a stacked or 100 % layout may plot a ratio through its numerator, so the refusal and its scenario "Stacked ratio refused" are rewritten;
  - "Cross table widget": a normalized cross table holds shares, not values in the indicator's format, in its cells and totals;
  - "Number of series": the Total is drawn next to the 8 series and is not one of them;
  - new requirements: normalized bars and columns, stacked bars of a ratio, normalized heat maps and cross tables, the Total series, the Total bar of a widget with no series, top groups and their Other group, normalized values in "Show as table".
- `dashboard-colors`: a new requirement for the colors of a Total and of the Other group.
- `dashboard-reports`: a new requirement for normalized widgets, Totals and top groups in a scheduled email. "Widget tables in a scheduled email" stays as it is.
- `mcp-server`: a new requirement, "Normalization and totals through MCP".

## Impact

- **api** (`tamiza-api`), in `Indicators/`:
  - `IndicatorModels.cs`: `WidgetConfigInput`, `Normalize()` and `WidgetConfig` gain `normalize`, `includeTotal` and `top`, each `[JsonIgnore(Condition = WhenWritingNull)]`;
  - `IndicatorValidation.cs`: the ratio refusal (lines 340-342) lifted, the per-kind rules and the `categories` rules;
  - `IndicatorQueryBuilder.cs` (the aggregate, lines 234-248) and `SeriesQueryBuilder.cs`: `sum(numerator)` for the shares of a ratio, ranking for top groups, the Other group in every UNION ALL branch, and the Total bar from the overall value;
  - `SeriesTable.cs` (`ShowsShares` and `SharesOf`, lines 29-30 and 84-102): shares by group, series or total;
  - `WidgetQuery.cs`; and elsewhere `Reports/ReportDataBuilder.cs`;
  - OpenAPI, and the tests: `WidgetQueryTests`, `SeriesTableTests`, `IndicatorSeriesQueryTests`, `ReportDataBuilderTests`, and the production shapes fixture of `add-indicator-column-metadata`, extended with a normalized, a top-groups and a Total widget.
- **ui** (`tamiza-ui`): `core/projects/indicators.service.ts` (the types), and in `indicators/`:
  - `widget-view.ts` (`queryFor`, line 102, and `savedConfig`, line 151) and `widget-dialog.ts` (the form);
  - `series-table.ts` (the shares, lines 72-76) with the shared fixture `testing/series-tables.json`;
  - `series-plot.ts` (the 100 % axis, lines 131-157, and the Total series), `axis-chart.ts` (the Total and Other bars), `heatmap-chart.ts`, `crosstab-widget.ts` (`crossTableModel`, line 28) and `chart-colors.ts`;
  - `en.json` and `es.json`, and the specs, `query-for.spec.ts` and `series-table.spec.ts` among them.
- **Database:** none. The fields live in `dashboard_widgets.config`.
- **Analytics:** none.
- **MCP:** `Mcp/McpDashboardTools.cs` (`SeriesHelp`, `ChartTypeChangeHelp`, the series payload), `Mcp/Instructions.md` (line 34) and `docs/mcp.md`, with `McpToolCatalogTests` and `McpGuideTests`.
- **Docs:** `docs/indicators-dashboard.md`: the layouts and the rule "plot a measure" (line 213), the cross table (line 236), and how to keep one % indicator in place of a count twin, with `IndicatorDocsTests`.

**Compatibility.** Every field is optional, and absent means today's widget. The 18 percent and 4 stacked widgets over count indicators keep their shares and the `share` of each cell in MCP output byte for byte, and the existing cases of `series-tables.json` do not change. Existing heat maps whose script precomputes a % (Teatro 5b7a, Centro ee25, parqueadero addb, Segmentos 7732, 85ee and 049e, Cruces 6c16 and 798b, Escuelas 7718) are not normalized unless an author turns it on. Widgets that read a duplicated total ("Todos los vehículos", "Todos los estratos", "5. Cualquier componente": 77ea, de78, 6086, a137, 8492, 57ee, 64cb) draw no second total, because `includeTotal` is off by default. No widget is added, so the 50-widget limit is untouched. Dashboard filters that a table lacks are still skipped silently. The new fields are written only when set, so untouched widgets keep byte-identical stored JSON and make no spurious version; versions, duplicates and moved widgets carry the fields with the rest of the config. Scheduled emails and the MCP tools show existing widgets as before, and an MCP client that does not send the fields gets today's widget (`update_widget` still replaces the config whole). After a rollback, an older API ignores the fields and drops them on the next save, but its query check refuses a stacked or 100 % widget over a ratio. Such a widget shows that error until it is changed back to grouped bars. The design states this in its Migration Plan.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next in line, against the main specs as they are then (after the P1 changes are archived). Decisions to take with the user at that time:
- One set of `normalize` values for every kind (`group` and `series`, shown as "row" and "column" in a heat map), or the kind-specific `row` and `column`.
- Stacked bars over a ratio: stack the numerator's counts (proposed) or stack the ratios themselves.
- Whether Likert bars also take a ratio through its numerator in this change (`Diverging Likert bars` would then be modified).
- Whether the guards on `categories` refuse the save (proposed) or only warn, and what the form says when a column is not declared.
- The Total's color and place (proposed: last, after Other, in a dark neutral color), and whether a stacked layout, whose Total bar dwarfs the others, takes one at all.
- Whether `top` ranks by plotted value even when the widget is ordered by a column, and whether a declared `last` value can be folded into Other.
