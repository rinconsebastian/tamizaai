# Design

## Context

See proposal.md for the motivation and the specs for the behavior. The pieces this change works on:

- **One query, one group.** `IndicatorDashboard.QueryAsync` (`IndicatorDashboard.cs:112-153`) normalizes the config, checks it, takes a query slot, opens a read-only transaction, describes the table and calls `IndicatorQueryBuilder.Plan` (`IndicatorQueryBuilder.cs:109-255`). The statement is:
  ```
  SELECT {group}::text, ({aggregate}), {geometry}{extra}
  FROM {from} WHERE {rowFilter}{filters}
  GROUP BY {group} HAVING ({aggregate}) IS NOT NULL
  {orderBy} LIMIT @limit
  ```
  - The aggregate is `sum(m)::double precision` with the row filter `m IS NOT NULL`, or `sum(num)::double precision / nullif(sum(den), 0)` with `(num IS NOT NULL OR den IS NOT NULL)`.
  - The group goes out as text. The order uses the table-qualified column, so it sorts in the column's own type, `NULLS LAST`. A sort key is the plotted value or `ColumnSortKey` (the smallest value per group), and is applied before the limit.
  - Widget filters (`f0`…) and dashboard filters (`d0`…, `PlanDashboardFilters`) are ANDed. A missing column is collected into `missing` and no SQL runs.
  - `@limit` is `MaxWidgetQueryRows + 1` (5,000 by default); the reader stops at the limit and sets `RowLimitReached`.
  - `ReadRowsAsync` (`IndicatorDashboard.cs:268-317`) reads by position: 0 group, 1 value, 2 geometry, 3 count (map), 4 onward tooltips, into `IndicatorRow(Group, Value, Geometry, Rows?, Tooltip?)`.
  - Statement timeout 25 s, queue timeout 25 s, 4 concurrent queries (`DataDashboardOptions.cs`); a timeout is `data.timeout` (503).
- **Validation** is `IndicatorValidation.CheckConfig` (no table; keys prefixed `config.` inside a widget) and `CheckAgainstColumns` (published table, on create and update). `ChartTypes.NeedsGroup` covers bar, column, line, pie and map. Sorting is refused outside bar and column. `DefaultSize` gives stat 3x2, map 6x6 and the rest 6x4 (`grid-layout.ts:13-22` is the UI twin).
- **`chart_type` is listed in four places:** the CHECK of `0007_add_indicators_dashboard.sql:16`, `TamizaDbContext.cs:430-431`, `ChartTypes.All`, and the UI `chartTypes` (`indicators.service.ts:14`) with `indicators.chartTypes.*` strings.
- **Unknown kinds fall somewhere quietly:** `widget-view.ts:211` draws them as a column chart, `ReportDataBuilder.cs:154` makes them an `AxisTable`, `DefaultSize` and `grid-layout.ts` give them 6x4, and `IndicatorDashboard.cs:117` drops an unknown `chartType` on the query endpoint.
- **The config** is `jsonb`, read and written with the web JSON options and no enum converter (string constants), through `WidgetConfigInput.Normalize()`. `ReadConfig` drops properties the record does not have. The UI's `queryFor` and `savedConfig` (`widget-view.ts:29-61`) whitelist the fields per kind, and `WidgetQuery.For` is their C# twin for emails and MCP.
- **Charts are hand-drawn SVG** in `computed()` signals, with no chart library (`chart-scales.ts`). `axis-chart.ts` draws one series in `seriesColor(0)`, with no legend; `pivot` gives `{categories, values}` in listed or natural order. The pie folds slices past the eighth into "Other" (`pie-widget.ts:95-117`). The map uses the sequential ramp `--tmz-seq-1..7` and a min-to-max gradient legend.
- **Palette** (`styles.css:51-91`): `--tmz-series-1..8` and `--tmz-series-other` (the documented categorical palette, light and dark), `--tmz-seq-1..7` (reversed in dark). There is no diverging palette.
- **Accessibility today:** axis and pie charts are `role="img"` with a translated summary and an SVG `<title>` per mark; bars and slices take no focus. The map is `role="application"` with arrow keys through shapes, an `aria-live` announcement and the CDK overlay `ChartTooltips` (`shared/ui/chart-tooltip.ts`).
- **"Show as table"** is `TableModel {headers, rows, align?}` (`widget-table.ts:8-14`), built per kind; it counts rows left out on paper. Natural order is `naturalCompare` (an `Intl.Collator` with numeric order) and its C# twin `NaturalGroupOrder` (`ReportDataBuilder.cs:328-341`).
- **Emails** (`Reports/`): `ReportDataBuilder.BuildWidgetAsync` runs `WidgetQuery.For` with the dashboard filters' defaults and builds one table per kind (`AxisTable`, `HistogramTable`, `PieTable`, `MapTable`). `ReportHtml` shows 10 rows per block within an 80 KB body; the plain text writes rows with `|`; `ReportWorkbook` writes one sheet per widget, with number formats keyed by header across the workbook.
- **MCP** (`Mcp/McpDashboardTools.cs`): `query_indicator` sends the config as given and returns `rows: [{group, value, formatted, rows}]`; `get_widget_data` uses `WidgetQuery.For` and the same row shape; `create_widget` and `update_widget` call the REST handlers. `Instructions.md` lists the kinds.
- **There is no per-widget export.** The indicator export is the whole published table, and stays so.

## Goals / Non-Goals

**Goals:**
- One query shape for every widget with a series: cells, totals and the series domain, computed in SQL from rows.
- One table model per side (API and UI) for a widget with a series, so the chart's tooltips, "Show as table", printing, the emails and MCP read the same numbers.
- No kind that falls into a default branch: every place that switches on the kind lists all of them.

**Non-Goals:**
- Keyboard access to the marks of the existing single-series charts. The new marks get it (D8); the old charts keep "Show as table" as their keyboard path.
- Direct labels on series. The legend and the tooltips carry identity.
- Prefilling a Likert scale or a pyramid's sides from the Kobo form's choice order. The editor offers the values the indicator table holds (D7).

## Decisions

### D1. Config and data model (DbUp `0017_add_two_dimension_widgets.sql`)

The widget config gains four optional fields. They are stored in `dashboard_widgets.config` as camelCase JSON, with string constants for the enum-like values, as every other config field:

| Field | Type | Kinds | Default |
|---|---|---|---|
| `seriesBy` | column name | bar, column, line (optional); heatmap, pyramid, likert, crosstab (required) | none |
| `layout` | `grouped`, `stacked` or `percent` (`SeriesLayouts`) | bar and column with a series | `grouped` |
| `pyramid` | `{ left, right }`, two series values as text | pyramid | none |
| `scale` | `[{ value, side }]`, side `negative`, `neutral` or `positive` (`LikertSides`) | likert | none |

- **Kinds.** `ChartTypes` gains `Heatmap = "heatmap"`, `Pyramid = "pyramid"`, `Likert = "likert"` and `Crosstab = "crosstab"`. `NeedsGroup` adds the four. New `TakesSeries` (bar, column, line) and `NeedsSeries` (the four new kinds).
- **Values are text.** A pyramid's sides and a scale's values are compared with the series column's text output (`"col"::text`), so a true/false or numeric series works with `true` or `2`. They are not checked against the data: a dashboard filter can hide a side, and a script can change its values later (spec "Stale widget configuration").
- **Migration.** `0017` drops `ck_dashboard_widgets_chart_type` and adds it again with the eleven kinds. No config is rewritten: a config without `seriesBy` has no series. `TamizaDbContext` mirrors the CHECK.
- **Default sizes.** Heat map, pyramid and cross table 6x5, Likert 6x4, on both sides (`DefaultSize`, `grid-layout.ts`).

*Alternatives:* new kinds for stacked, grouped and 100% bars. Rejected: they are layouts of the bar and column kinds, keep their orientation, order and settings, and a widget switches between them without losing its query. The line chart's series is the same field.

### D2. Validation (`IndicatorValidation`)

`CheckConfig` adds, with errors on these keys:

| Rule | Key | Message (English, as the API's others) |
|---|---|---|
| A kind outside `TakesSeries` and `NeedsSeries` with a series | `seriesBy` | "A {kind} widget cannot have a series." |
| A kind in `NeedsSeries` without one | `seriesBy` | "A {kind} widget needs a series column." |
| Series equal to the group column | `seriesBy` | "Choose a series column other than the group column." |
| `layout` without a series, on another kind, or unknown | `layout` | as the sort's messages |
| `stacked` or `percent` with a ratio | `layout` | "Stacked bars add their values, so they need a measure, not a ratio." |
| `pyramid` on another kind, a side missing, or both equal | `pyramid.left`, `pyramid.right` | |
| `scale` on another kind; fewer than 2 or more than 11 values; a value repeated or empty; an unknown side; a side out of order; more than one neutral; more than 5 on one side | `scale`, `scale[i]` | |
| Likert with a ratio | `measure` | "A Likert widget shows shares of a measure, not a ratio." |

`CheckAgainstColumns` checks that the series column exists. A series column that a script later removes is collected into `missing.columns`, so the widget is stale (no SQL runs), as for the group column. Sorting stays bar and column only; a bar or column with a series may be sorted (D3).

### D3. Query with a series (`IndicatorQueryBuilder.PlanSeries`)

A widget with `seriesBy` is planned by a new `PlanSeries`, with its own reader, so the positions `ReadRowsAsync` relies on do not move. Without `seriesBy`, `Plan` is untouched. One statement per query, as today, in three parts:

1. **The domain** (CTE), with the row filter and the widget's own filters only, no dashboard filters: `SELECT "s"::text AS s, {aggregate} AS total FROM t WHERE … GROUP BY 1`. It fixes which series exist, their order and their colors (spec "Series order and colors").
   - Bar, column and line keep at most 8 values, heat map and cross table 30. When the domain holds more non-missing values than the limit allows, the `limit - 1` with the largest totals are kept (ties by value) and the rest fold into "Other". The missing value is never folded.
   - A pyramid's domain is its two sides and a Likert's its scale, in the config's order. Nothing folds.
2. **The cells and totals**, with every filter (widget and dashboard):
   ```
   SELECT g::text, kind, key, {aggregate}, count(*), {sortKey}, GROUPING(g), GROUPING(kind, key)
   FROM t WHERE {rowFilter}{filters}{included}
   GROUP BY GROUPING SETS ((g, kind, key), (g), (kind, key), ())
   HAVING ({aggregate}) IS NOT NULL
   ```
   - `kind` is `value`, `none` (missing series value) or `other`, and `key` the series text when `kind = 'value'`: `CASE WHEN s IS NULL THEN 'none' WHEN s::text = ANY(kept) THEN 'value' ELSE 'other' END`.
   - Totals come from the same rows (spec "Totals of a widget with a series"): a ratio's total is `sum(num) / nullif(sum(den), 0)` over the group, the series or all rows, never a sum of cells.
   - For a pyramid and a Likert widget, `{included}` keeps rows with a group value and a series value in the domain, and a scalar subquery with the same filters counts the rows it leaves out (`leftOut`).
3. **Order and limit.** Groups are ranked in SQL from their group-level row: by the widget's order (the group total, or `ColumnSortKey` over the group's rows) and then the group column in its own type, `NULLS LAST`; by the group column alone for every other kind. The limit counts cell rows in that order (`MaxWidgetQueryRows + 1`), and the totals of the groups that are kept are always returned. A pyramid orders its groups in natural order instead (D5).

The response (`IndicatorQueryResponse`) gains, only for a widget with a series (null and left out of the JSON otherwise):
- `series`: the domain in display order, `[{ value, kind }]` (D4);
- rows with `series` and `seriesKind` next to `group`, `value` and `rows` (the cell's row count);
- `totals`: `{ groups: [{ group, value, rows }], series: [{ series, kind, value, rows }], all: { value, rows } }`;
- `leftOut`: rows a pyramid or a Likert widget left out.

*Alternatives:* computing totals in the browser from the cells, which is wrong for a ratio and would have to be done again in the emails and MCP; a second round trip for the domain, which the CTE makes unnecessary; a "Top N" setting, left for later.

### D4. Series order and the table model (API and UI twins)

- **Order.** Series values in natural order, then `none` shown as "(none)", then `other` as "Other" (spec). `NaturalGroupOrder` moves from `ReportDataBuilder` to `Indicators/NaturalOrder.cs` for every use; `naturalCompare` is the UI twin.
- **Colors.** A series' color is its index in `series`: `seriesColor(i)` for values and "(none)", `--tmz-series-other` for "Other". Because the domain ignores dashboard filters, a filter never repaints or reorders the survivors, and the legend keeps a series that has no rows after a filter.
- **`SeriesTable`** (`Indicators/SeriesTable.cs`) and `series-table.ts` turn a response into the grid every consumer reads:
  - rows = groups in the response's order (a pyramid's in natural order), columns = `series`, one cell per pair, `null` when the pair has no value;
  - totals per row, per column and overall, from `totals`;
  - for `percent` and Likert, each cell's share of its row: over the row's cells of zero or more for `percent` (negative cells left out and counted), over the scale's cells for Likert;
  - for a pyramid, the left and right columns and the row total;
  - the notes: rows left out, negative values left out, the row limit.
- **Shared fixtures.** The cases (ratio totals, "Other", "(none)", percent with a negative cell, pyramid, Likert) live in one JSON file in the UI's tests and are read by the API's unit tests through `FindUp`, as `PromptKitTests` reads the editor's templates; the API test is skipped when the UI is not next to it.

### D5. Drawing

Every new mark follows the method already used for the palette: color by the job it does, legends for two or more series, marks that never rely on color alone, a 2-pixel surface gap between adjacent fills.

- **Bars and columns by series** (`axis-chart.ts`).
  - *Grouped:* each group's band is split into one sub-band per series, in series order.
  - *Stacked:* segments from the axis in series order, positive values up or right and negative values down or left.
  - *100%:* each bar's segments are its shares, on a 0 to 100% axis whatever the indicator's format; the tooltip gives the share and the value.
  - A legend above the plot replaces the caption line. Order and limit as D3.
- **Lines by series:** one path per series, broken where a group has no value, dots up to 40 points per series as today.
- **Heat map** (`heatmap-chart.ts`): a grid of rows and columns with labels, each cell shaded with `sequentialColor` from the smallest to the largest value shown (the map's ramp and its min-to-max legend). A cell with no value is drawn in the surface color with a hatch and an outline, so it differs from the lightest step. Cells too narrow for a label show none; the tooltip carries the value.
- **Population pyramid** (`pyramid-chart.ts`): two sets of horizontal bars on one linear scale measured outward from a central axis, the left side in series slot 1 and the right in slot 2, axis labels without signs, the first group at the bottom. The note "N rows left out" sits under the plot.
- **Likert** (`likert-chart.ts`): horizontal bars centered on a vertical line at 0%, negatives to the left nearest-first, the neutral split across the line, positives to the right; percent axis from the largest left extent to the largest right extent. Colors from new diverging tokens: `--tmz-div-neg-1..5` (red arm), `--tmz-div-neutral` (`#f0efec` light, `#383835` dark) and `--tmz-div-pos-1..5` (the blue arm of `--tmz-seq`), each arm stepped by distance from the neutral. Both arms are validated as ordinal ramps against the light and the dark surface before they ship (task 13.1).
- **Cross table** (`crosstab-widget.ts`): the `SeriesTable` as an HTML table, the group column sticky, totals in bold, scrolling horizontally inside the widget. It is the "Show as table" view of the other kinds too.
- **Tooltips and keyboard.** The new marks use the map's `ChartTooltips` overlay on hover and on focus. Each chart with a series is one Tab stop (`role="application"` with a key help, as the map): arrow keys move through groups and series, Home and End go to the ends, Escape leaves, and an `aria-live` region reads the focused mark's group, series and formatted value.
- **"Show as table"** of every widget with a series is its cross table (spec). `TableModel` gains the "columns left out" count used on paper (D9).

*Alternatives:* a chart library. Rejected: the charts are hand-drawn SVG with tokens, print rules and table views already; a library would bring its own palette, print and accessibility behavior for half the kinds.

### D6. Kinds handled everywhere

The four quiet fallbacks become exhaustive switches over `ChartTypes.All`: the widget view's chart choice, `ReportDataBuilder`'s table choice, both `DefaultSize`s, and the query endpoint, which rejects an unknown `chartType` with an error on it instead of dropping it. A unit test on each side iterates every kind.

### D7. The widget form (`widget-dialog.ts`)

- The kind `<select>` lists the eleven kinds.
- **Series:** a column select (every column but the group column), optional for bar, column and line, required for the four new kinds. Changing the indicator resets it, as the other columns.
- **Layout:** for a bar or column with a series, a radio group Grouped / Stacked / 100%. Stacked and 100% are disabled while the widget plots a ratio, with the reason next to them.
- **Pyramid sides** and **Likert scale:** the values offered are the preview's `series` (the domain of the widget's own rows), with a typed value allowed for one the preview does not show. The scale editor lists the values in natural order, each with a side (negative, neutral, positive, or left out), up and down buttons and a remove button, all reachable by keyboard; the form shows the API's rules before saving.
- `queryFor` and `savedConfig` (and `WidgetQuery.For`) keep `seriesBy` for the kinds that take one, `layout` for bar and column, `pyramid` and `scale` for their kinds.
- The preview works as today; its legend and notes are the widget's.

### D8. Emails (`Reports/`)

- `ReportDataBuilder` builds a widget with a series from `SeriesTable`: columns `[group, series values…, Total]` and one row per group, then a "Total" row of the series totals. A 100% layout and a Likert widget show shares (formatted `0.0%`, numbers in the workbook) in the series columns and have no "Total" row, as their "Show as table" view; a pyramid shows its left, right and total. The notes say the rows left out and the negative cells set aside.
- `ReportHtml` and the plain text show the first 6 series columns and the "Total" column, and "N more columns" when there are more (spec "Email body"); the workbook sheet holds the whole table. The 80 KB budget is unchanged.
- `ReportText` gains the English and Spanish texts: "Total", "Other", "N more columns", "N rows left out", "N negative values left out".

### D9. Printing

The print rules of `widget-grid.ts` (`print-color-adjust: exact`) apply to the new SVGs. A heat map and a Likert widget print their legends. A cross table, or any widget shown as a table, prints the rows that fit and the group column with the columns that fit, and says how many of each were left out (spec "Tables on paper").

### D10. MCP (`Mcp/`)

- `query_indicator`, `create_widget` and `update_widget` describe `seriesBy`, `layout`, `pyramid` and `scale` in their config, and the four kinds, with the same validation (refusals are `mcp.validation`).
- For a widget with a series, `query_indicator` and `get_widget_data` return the `SeriesTable`: `series`, `cells: [{ group, series, value, formatted, share? }]`, `totals` and `notes`, formatted as the emails format them. The result budget cuts `cells` first (its hint: name fewer groups with a filter).
- `Instructions.md` names the kinds and when each fits: composition of a category (100% bars), a gap between two groups (pyramid, grouped bars), an agreement scale (Likert), a contingency table (cross table), a magnitude over two dimensions (heat map), trends by group (lines by series). It says that a widget with a series needs an indicator table with two text dimensions.

### D11. Documentation

- `docs/indicators-dashboard.md`: "Widgets with a series" (the series, order, colors, "Other", totals), one section per kind, and "Preparing an indicator with two dimensions" (a `groupby` on both columns; age bands as text that sorts naturally, such as `0-4`, `5-9`, `10-14`; a Likert answer as its choice label). "What it does not do" says a widget plots one indicator with at most two dimensions.
- `docs/mcp.md`: the widget tools' new settings. `docs/operations.md`: the row limit counts pairs of group and series. `docs/tamiza-brief.md`: the chart kinds and config in section 6, and the roadmap item. `README.md`: the widget kinds in the status.

### D12. Wire format

Fixed before either side is built, so the API and the UI can be written apart. All JSON is camelCase; fields marked *omitted* are left out of the JSON when null (as `IndicatorRow.Rows` is today).

**Widget and query config** (`config` of a widget, of `POST .../indicators/query` and of the MCP tools):

| Field | JSON | Saved config when none |
|---|---|---|
| `seriesBy` | string | `null` |
| `layout` | `"grouped"`, `"stacked"` or `"percent"` | `null` |
| `pyramid` | `{ "left": string, "right": string }` | `null` |
| `scale` | `[{ "value": string, "side": "negative" \| "neutral" \| "positive" }]` | `[]` |

Chart types: `stat bar column line histogram pie map heatmap pyramid likert crosstab`. Validation errors use the keys of D2 prefixed with `config.` inside a widget (`config.seriesBy`, `config.layout`, `config.pyramid.left`, `config.pyramid.right`, `config.scale`, `config.scale[2]`). The query endpoint's body carries the `chartType`; with `seriesBy` and no `chartType` the query is a bar's.

**Query response**, for a widget with `seriesBy` only (all *omitted* otherwise):

```json
{
  "published": true, "rowLimitReached": false, "missing": { "indicator": false, "columns": [] },
  "series": [ { "value": "Piped", "kind": "value" }, { "value": null, "kind": "none" }, { "value": null, "kind": "other" } ],
  "rows":   [ { "group": "Cali", "value": 35, "series": "Piped", "seriesKind": "value", "rows": 3 } ],
  "totals": {
    "groups": [ { "group": "Cali", "value": 50, "rows": 5 } ],
    "series": [ { "series": "Piped", "kind": "value", "value": 55, "rows": 4 } ],
    "all":    { "value": 70, "rows": 6 }
  },
  "leftOut": 15
}
```

- `series` is the domain in display order (natural order, then `none`, then `other`). A pyramid's is `[left, right]` and a Likert's the scale in order, whether or not rows exist for them. `kind` is `value`, `none` (the rows with no series value; `value` null) or `other` (the folded series; `value` null).
- `rows` lists the cells that have a value, group by group in the order of `totals.groups`, and inside a group in the order of `series`. `rows` of a cell is the number of table rows it sums. A missing group value is `group: null`. A cell's `series` is **left out of the JSON** (absent, not `null`) when its `seriesKind` is `none` or `other`, as a plain row leaves out the members it has no value for: a reader treats absent and `null` alike and matches such a cell to its series by `seriesKind`.
- `totals.series` has one entry per `series` entry, in the same order; `value` is null and `rows` 0 for a series with no rows (for example after a dashboard filter). `totals.groups` is the authoritative order of the groups. A total is computed from the rows: for a ratio it is the ratio of the sums, never a sum or mean of cells (`value` is null when the denominator sums to zero).
- `leftOut` is only sent for `pyramid` and `likert`.

**`SeriesTable`**, the grid every consumer reads, derived from the response with the chart type and the layout (the API's `SeriesTable.cs` and the UI's `series-table.ts` must agree on the cases of `tamiza-ui/src/app/pages/projects/indicators/testing/series-tables.json`):

```
columns: [ { series, kind, total, rows } ]            // one per response.series, same order
rows:    [ { group, total, rows, cells, shares } ]    // one per totals.groups, same order
grandTotal, grandRows, negativeLeftOut, leftOut, rowLimitReached
```

- `cells[i]` aligns with `columns[i]`: the row's value, or null when the pair has no value.
- `shares` is null except for `likert`, and for `bar` and `column` with `layout: "percent"`. Then `shares[i]` is `cells[i]` over the sum of the row's non-negative cells; it is null for a cell with no value and for a negative cell (each negative cell counts once in `negativeLeftOut`), and all null when that sum is zero.
- `total`, `rows`, `grandTotal` and `grandRows` are copied from `totals`.

## Risks / Trade-offs

- [The `GROUPING SETS` statement scans the table twice (domain and cells)] → Indicator tables are small by contract (one row per combination of dimensions); the 25-second statement limit, the query slots and the row limit still apply. Task 17.3 measures a 100,000-row indicator table.
- [Colors follow the widget's own filters, so editing a widget's filters can repaint its series] → That is an edit by the analyst, seen in the preview; dashboard filters, which every member changes, never repaint.
- [Pyramid sides and scale values are typed text that can stop matching the data] → The widget counts and says the rows it left out, so a mismatch is visible; the editor offers the values that exist.
- [A `percent` layout over a measure with negative values] → They are left out of the shares and counted in a note rather than drawn as shares that do not add up to 100%.
- [The API and UI table models drift] → One set of JSON fixtures tests both.
- [Wide tables in emails and on paper] → 6 series columns in the body and a "N more columns" note; the workbook holds everything; paper says how many columns it left out.
- [Keyboard navigation adds a pattern to four charts] → It is the map's pattern, reused through one helper, and the cross table is plain HTML.

## Migration Plan

1. Deploy: `0017` widens the CHECK; existing widgets have no `seriesBy` and draw as before.
2. Rollback: an earlier release reads a config with `seriesBy`, `layout`, `pyramid` or `scale` by dropping those fields, so a bar, column or line with a series falls back to one series. Widgets of the four new kinds must be removed or changed first, or the earlier release draws them as column charts: `DELETE FROM tamiza.dashboard_widgets WHERE chart_type IN ('heatmap', 'pyramid', 'likert', 'crosstab');`. DbUp never runs scripts backwards; the wider CHECK stays and is harmless.

## Open Questions

- Whether ordering series by their total, or Likert bars by their net agreement, is worth a setting; the order rules of this change do not depend on it.

## Deviations from the plan

- **Two statements, not one (D3).** The series domain (the non-missing values with the largest totals, plus whether some rows have none) is read by its own statement, and the cells statement then takes the values to keep as a `text[]` parameter. The fold into "Other" is a plain function (`SeriesDomain.Fold`), tested without a database, and the cells statement has no condition on the domain. Both run in the same read-only transaction, under the share lock that describing the table takes. A pyramid and a Likert widget have a fixed domain and run only the cells statement.
- **The row limit leaves a group out whole (D3).** Cells come in the order of their group's rank, up to the limit plus one. When the limit falls inside a group, that group and the groups after it are dropped from the cells and from `totals.groups`, so a group is never drawn partly. The totals of the series and of all rows are not cut: they cover every row the widget matches, and the response says the limit was reached.
- **Every kind an analyst cannot order lists its groups in natural order (D3).** The line chart, the heat map, the population pyramid, Likert bars and the cross table have no order setting, and the real-browser check showed that age bands in the column's own order read `0-4, 10-14, 15-19, …, 5-9`. Only bars and columns, which an analyst can order, keep the column's own order. PostgreSQL has no natural-order collation here, so the SQL ranks groups in the column's own order and the API re-sorts the groups it got; the UI's `chartOrder` is the twin. Only a table with more than 5,000 cells could notice, and then the response says the limit was reached.
- **A cell's `series` is absent for "(none)" and "Other" (D12).** A first version marked cells with a derived record so that `series: null` was written; it made OpenAPI describe `IndicatorRow` as a union and broke the document's tooltip test. `IndicatorRow` has two more members, `series` and `seriesKind`, written only for a widget with a series, and a reader matches a cell with no `series` by its `seriesKind`.
- **The folded series lose their value inside the SQL (D3).** The source rows carry `NULL` as series text when their value is not kept, so their rows add up to one "Other" cell and one "Other" total, with a ratio's total computed over them.
- **`leftOut` counts rows, not values (D3).** The pyramid and Likert examples of the spec count rows ("12 rows have the sex Other and 3 have no age band: 15 rows").
- **Shares tables have no "Total" row (D8, D5).** The UI's "Show as table" for a 100% layout and Likert bars has the group's total as its own column and no row, and the emails must list the same rows as that view, so the "Total" row of the first plan, with each series' share of the whole, was dropped from the emails.
- **A series needs a group (D2).** A series on a query with no group column is refused (`config.seriesBy`): the plan had no rule for it and the query would have ignored the series silently.
- **An unknown chart type is refused everywhere (D6).** `DefaultSize` throws for it and the query endpoint answers 400 with an error on `chartType`; the first design only listed the four places that fell back to a default.
- **The pyramid and Likert editors read their values from a second query (D7).** The preview of a pyramid or a Likert widget without its sides or scale is refused, so it has no series to offer. The form sends one silent query for a heat map over the same indicator, columns and widget filters and offers its `series`, which is the domain the finished widget will have.
- **`NaturalGroupOrder` became `NaturalOrder`** (`ByGroup` for rows, `ByText` for series values) in `Indicators/`, used by emails and series alike.

## Verification Results

Checked in the development sandbox (4 cores; PostgreSQL 17 with PostGIS in a container; Keycloak 26.4; .NET 10; Node 22.22.3; headless Chromium through Playwright). The stack of the browser check was a published API, nginx serving the `ng build` output and the containers, started by hand rather than as a Compose project.

**Tests**

| Suite | Result |
|---|---|
| API integration, whole suite, after the API and the emails were done | 1,619 passed, 0 failed, 38 minutes |
| API integration, `Indicators`, `Reports`, `Mcp`, `SchemaMigrator` and `SchemaDrift`, after the last API changes (natural order of every kind that cannot be ordered, the nullable fix) | 677 passed, 0 failed, 14 minutes |
| API unit | 1,560 passed, 3 failed: `ReportFormatTests.Dates_and_timestamps_of_tooltip_columns`, `ReportHtmlTests.Header` and `Text_reader`. The sandbox's ICU writes a narrow no-break space before `AM`; they fail the same way on the commit before this change |
| UI (`ng test`, Node 22.22.3) | 98 files, 2,190 tests passed (baseline 93 files, 1,985). The one unhandled error `Cannot read properties of undefined (reading 'total')` in `ProjectPerformance`, present before this change, still makes the exit code 1 |
| UI `ng build` | clean; initial bundle 649.56 kB, under the 650 kB budget. `scripts/check-ui-safety.sh` clean |

**Query time over an indicator table of 100,000 rows** (500 groups, 30 water sources, 2 sexes, 12 rounds; one request through the API with PostgreSQL in a container, in milliseconds as the HTTP client saw them):

| Widget | Cells | Time |
|---|---|---|
| Plain bar chart (warm-up) | 500 | 129 |
| Stacked column, 8 series (the other 22 folded into Other) | 850 | 332 |
| Heat map, 30 series | 1,500 | 277 |
| Heat map as a ratio, 30 series | 1,500 | 359 |
| Column, 2 series | 500 | 193 |
| Line, 12 rounds × 2 series | 12 | 167 |
| Stacked bar ordered by its total | 850 | 236 |

The statement limit of 25 seconds leaves two orders of magnitude of room on a table of this size.

**The Likert palette (task 13.1).** The widget surface is `#ffffff` in light and `#101828` in dark (Flowbite `neutral-primary-soft` and gray-900). Each arm passes as an ordinal ramp with the validator of the dataviz method (`validate_palette.js --ordinal`):

- `"#ea9a93,#dd716a,#c74845,#9e3432,#762221" --mode light --surface "#ffffff"`: monotone, light end 2.20:1, hue spread 0°.
- `"#86b6ef,#5598e7,#2a78d6,#1c5cab,#104281" --mode light --surface "#ffffff"`: light end 2.11:1, spread 3°.
- `"#892b2a,#b13f3c,#d75853,#e4857e,#f1aea8" --mode dark --surface "#101828"`: light end 2.06:1, spread 0°.
- `"#184f95,#256abf,#3987e5,#6da7ec,#9ec5f4" --mode dark --surface "#101828"`: light end 2.19:1, spread 3°.

The neutral step (`#f0efec` light, `#383835` dark) is as light as the surface by design, so its segment and its legend swatch get a hairline. `chart-tokens.spec.ts` pins the hexes and re-checks the ordinal properties.

**In a real browser** (a published API behind Keycloak 26.4 sign-in, 30 widgets over 7 indicator tables with 12 municipalities, 17 age bands, long place names, a 10-source table, rows with no series value and a ratio indicator; 4 dashboards with 3 dashboard filters):

- Every kind and layout was looked at at 1,280 and 390 pixels, in the light and the dark theme (the dark tokens sit under `.dark` on `<html>` and the app has no toggle, so the class was set by script): 16 of 16 full pages without horizontal scroll, legends in the right colors, "Other" and "(none)" last, bars from the axis, a mirrored pyramid with the first band at the bottom, Likert bars centered with numbers that match the database. A scan of 3,820 texts for contrast found one real failure (below).
- Tooltips and keyboard on 12 charts: one Tab stop, arrows, Home, End and Escape, the `aria-live` region reading the focused mark ("Cali. Agree: 18.2%. … 96 answers. Positive") and a visible focus ring.
- "Show as table" and "Show all as tables": totals and dashes right; a 100% layout and Likert bars have the group total as a column and no "Total" row.
- The form: the side and scale editors offer the values of the series column, Stacked and 100% are disabled with their reason while the widget plots a ratio, changing the indicator resets the series, and a server error shows on the right field (a second neutral value on the scale, an empty value on the third).
- A dashboard filter that empties a series keeps it in the legend, with the other series in their colors; a pyramid side that a filter hides is drawn empty.
- Printing through `page.pdf` on A4 in both orientations: every chart whole in its box, legends and swatch colors printed without background graphics, "3 columns not printed." under a cross table that does not fit.
- The scheduled email to Mailpit: a body with six series columns, "Total" and "4 more columns", and a workbook with a sheet per widget holding the whole table, shares stored as numbers.

Six defects of the UI were found, fixed and looked at again:

1. The heat map's cell values were white on the blue of step 4 (3.64:1 at 11 px); they use near-black ink on the middle steps (5.41:1), and `chart-tokens.spec.ts` asks for 4.5:1.
2. The sticky group-column cells of a cross table painted over its sticky header while it scrolled (`z-20` on the header).
3. In the scale editor the focus stayed on the row after Move up or Move down, so Enter then Space undid the move, and Remove lost the focus; the focus now follows the value.
4. A heat map of 10 series dropped half its column labels at 1,280 px; every column is now labelled, vertically when it does not fit across, and values are written in every cell or in none. The rotation uses `writing-mode`: the SVG `transform` attribute makes Tailwind emit its `.transform` utility, which took the initial bundle to 650.05 kB.
5. A pyramid at 390 px labelled only its short bars; the label format is now the same for every bar of the widget.
6. The tooltip of 100% and Likert bars broke its figures across lines ("18.\n2%"); its label column wraps and numbers do not.

Left as they are, by judgment: grouped bars of twelve groups by five series are 1 px wide at the default 6×4 size (the documentation says to give them the grid's width or to stack them); a cross table repeats the unit in every cell, as the indicator's format says; the Likert neutral swatch is 1.15:1 against white, by design, with its hairline; the 288 px tooltip overflows the left edge of a 390 px screen by up to 11 px; the map tooltip editor has the same focus gap as defect 3 (not part of this change).

The check also found two things on the API side. Groups of text read alphabetically in the heat map, the cross table and Likert bars (`0-4, 10-14, …, 5-9`), so every kind an analyst cannot order now lists its groups in natural order (see Deviations from the plan). And a build warning (a possibly null `plan.Sql`) was fixed. The workbook of a dashboard writes `Piped (6)` for a series column whose number format differs from that of another widget's column of the same name: the format is kept per header in the workbook, a rule of the earlier email change; with series it shows more often, and it stays.

**The MCP tools.** Task 17.5 is covered by `McpDashboardToolsTests` through an MCP client: a pyramid is created and read back with `get_dashboard`, a Likert widget's shares and the rows it left out come from `get_widget_data`, a stacked chart is previewed with `query_indicator` (series, cells and totals, no rows, nothing saved), an invalid scale and a stacked ratio are refused with `mcp.validation` and the key, and a result too long is cut from its cells with a hint. It was not run from Claude.

**Not verified.** Touch taps on a phone (the 390 px runs are a desktop browser with a phone viewport), Firefox and Safari, the browser's own print dialog (`page.pdf` and a hand-fired `beforeprint` stand in for it) and a real screen reader (the `aria-live` text was read from the DOM). Negative values in stacked and 100% charts were checked only in jsdom: the seed data had none. A Compose project with the real images, and the 4 GB VPS the design targets, were not used.
