# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change adds an eleventh chart kind after `add-two-dimension-widgets` added four, so it follows that change's patterns. The pieces it works on:

- **Kinds are listed in many places.**
  - API:
    - `ChartTypes.All` and its predicates (`Indicators/IndicatorModels.cs:9-38`).
    - The DB CHECK (`0017_add_two_dimension_widgets.sql:4-6`) and its mirror `TamizaDbContext.cs:438-440`.
    - `IndicatorValidation.DefaultSize` (`:20-27`).
    - `WidgetQuery.For` (`:14-30`).
    - The report table switch `ReportDataBuilder.cs:166-174`.
    - The MCP help texts (`McpDashboardTools.cs:25-52, 83-91, 334-365`) and `Mcp/Instructions.md:10,28,42`.
  - UI:
    - `indicators.service.ts` (`ChartType`, `chartTypes`, `needsGroup`, `takesSeries`, `needsSeries`).
    - `widget-view.ts`: `queryFor`, `savedConfig`, `displayFor`, `axisKindOf` and the template `@switch`.
    - `grid-layout.ts` `defaultSize`.
    - `chart-colors.ts` `keepsByValue`.
    - `widget-dialog.ts`.
  - Tests that pin the list: `IndicatorValidationTests` (`Assert.Equal(11, ChartTypes.All.Count)`), `WidgetDisplayTests`, `IndicatorDocsTests` (each kind's bold name in `docs/indicators-dashboard.md`), `McpGuideTests` (each kind in backticks in `Instructions.md`), `ReportDataTests.Every_chart_type_builds_a_table_or_a_value`, `ScriptConventionTests`, the UI specs typed `Record<ChartType, …>`, and `series-widgets.spec.ts` (the order of `indicators.chartTypes.*`).
- **Only the histogram reads rows one by one, and only when it has no group column.**
  - `IndicatorDashboard.cs:139` passes `perRow: chartType == ChartTypes.Histogram`.
  - `IndicatorQueryBuilder.cs:229` then plans `rows = perRow && !grouped`.
  - The per-row value is `m::double precision`, or `num::double precision / nullif(den, 0)` (`:236-247`).
  - The per-row statement has no `ORDER BY` and stops at `MaxWidgetQueryRows + 1` (5,000 by default) with `rowLimitReached`.
  - Binning happens twice: in the browser (`chart-data.ts:36`) and in C# for the emails (`ReportDataBuilder.cs:340`).
  - No path today returns a value per row *and* a group.
- **Quartiles already exist in SQL.**
  - `CleanData/ColumnProfiler.cs:160` uses `percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY …)`.
  - `Performance/PerformanceSql.cs:56-68` computes Tukey's 1.5×IQR fences and counts outliers. Its rule is written in `dashboard-performance` "Completion time".
  - `percentile_cont` is linear interpolation (Hyndman–Fan type 7), the default of pandas' `quantile` and numpy's `quantile`.
  - PostgreSQL is `postgis/postgis:17-3.5`.
- **NaN.** NaN sorts above every number in PostgreSQL. `PieDomain` guards it with `< 'Infinity'::double precision` (`IndicatorQueryBuilder.cs:89-95`), and `ReadRowsAsync` skips non-finite values (`IndicatorDashboard.cs:467-471`).
- **`display` is how a widget is drawn, apart from its query.**
  - `WidgetDisplay(Color, Ramp)` (`IndicatorModels.cs:134-157`) has each member `WhenWritingNull`, and `Normalize()` returns null when nothing is set.
  - `CheckDisplay` (`IndicatorValidation.cs:164-198`) gives the per-kind rules with keys `display.color` and `display.ramp`.
  - `WidgetQuery.For` and the UI's `queryFor` leave `display` out, so changing it never queries again.
  - Roadmap item 7l (`add-widget-display-options`, `docs/tamiza-brief.md:326`) plans more display options on the same object.
- **The stored config text is pinned** (`WidgetDisplayTests.cs:16-38`). Dashboard versions and duplication copy configs verbatim (`DashboardVersions.cs:339-370`, `DashboardEndpoints.cs:481-494`). A new field must be left out of the JSON while it is unset.
- **Charts are hand-drawn SVG.**
  - The newer kinds (`pyramid-chart.ts`, `heatmap-chart.ts`, `likert-chart.ts`) export a pure plot function tested in jsdom.
  - Each wraps its plot in a component with `observeSize`, `tmz-chart-svg`, `role="application"` and `markTooltips` (one Tab stop, arrows, Home, End, Escape, `aria-live`).
  - The CDK overlay `ChartTooltips` shows the tooltips.
  - The bar and column kinds share `axis-chart.ts` and choose their orientation by kind; there is no orientation flag.
- **The bundle.** The initial bundle budget is 675 kB (660.25 kB measured after palettes). The dashboard page is lazy, so a new chart component lands in a lazy chunk; only new Tailwind utilities and new tokens in `styles.css` reach the initial bundle. Avoid the SVG `transform` attribute (two-dimension defect 4).
- **The authoring guide** (`Assistant/Prompts/system.md`, served by the script assistant and `get_authoring_guide`) says "Rows must be unique by their dimensions … one row per combination". Any edit bumps `AssistantPrompts.Version` (4 today).

## Goals / Non-Goals

**Goals:**
- Compute the statistics of every box in one SQL statement over every matching row. There are no browser or C# twins of the statistics, so the chart, "Show as table", the emails and MCP read the same numbers.
- One kind, `boxplot`, whose orientation is a display setting.
- Every place that switches on the kind names `boxplot`, with no default branch that would draw it as something else.

**Non-Goals:**
- Changing the histogram. Its per-row path and its 5,000-row cap stay as they are.
- Keyboard access to single outliers. Each box's tooltip counts them, and pointing shows each one.
- Aggregating rows by a unit column before the statistics (the histogram's grouped mode). The script decides what a row is.

## Decisions

### D1. Kind, config and migration (DbUp `0020_add_boxplot_widget.sql`)

- **Kind.**
  - `ChartTypes.Boxplot = "boxplot"` is appended to `All`, after `Crosstab`. The UI `chartTypes`, the `indicators.chartTypes.*` strings and the specs that pin the order append it the same way.
  - It is not in `NeedsGroup`, `TakesSeries` or `NeedsSeries`: the group is optional, as for the histogram, and a series is refused.
- **Orientation** is `display.orientation`, `"vertical"` or `"horizontal"`, with string constants `BoxOrientations`. It is left out of the JSON while unset, and unset reads as vertical.
  - `WidgetDisplay` gains `[JsonIgnore(WhenWritingNull), Description(...)] string? Orientation = null`.
  - `Normalize()` returns null only when color, ramp and orientation are all unset.
  - The API never rewrites `"vertical"` to null. A form saved unchanged must keep its stored text, or it would make a dashboard version (`dashboard-versions` "Widget form saved unchanged"). As for color 1, the UI stores `"horizontal"` when it is chosen and stores `"vertical"` only when the widget already had an orientation saved.
- **Order** reuses `sort`: `{ "by": "value", "direction": "asc" | "desc" }`. For a box plot the plotted value is the median. `by: "column"` and `compareAs` are refused on a box plot (D2).
- **Color** is `display.color`, as for the histogram. Boxes also take the colors kept for their group value, as bars with no series do (D6).
- **Constants.** `WidgetConfig.MaxBoxOutliers = 100` (outlier values returned per box) and `WhiskerRange = 1.5`.
- **Migration.** `0020` drops `ck_dashboard_widgets_chart_type` and adds it again with the twelve kinds, in `ChartTypes.All` order, as `0017` did. It rewrites no config. `TamizaDbContext` mirrors the CHECK. `Data/DashboardWidget.cs:16`, a stale comment that lists 7 kinds, names all twelve.
- **Default size** 6x4, on both sides (`DefaultSize`, `grid-layout.ts`).

*Alternatives:*
- *Two kinds* (a vertical `boxplot` and a horizontal one), as bar and column are. Rejected:
  - the orientation does not change the rows, and every kind list, string, test and doc would grow by two;
  - an analyst could not switch orientation without the form treating it as a kind change;
  - the roadmap already puts per-widget drawing options on `display`.
- *A top-level `orientation` in the config.* Rejected: it would enter the query key and `WidgetQuery.For`. `display` is the documented place for settings that never change the rows.

### D2. Validation (`IndicatorValidation`)

Existing rules apply unchanged:
- group optional;
- `seriesBy` refused ("A boxplot widget cannot have a series.");
- `layout`, `pyramid`, `scale`, `tooltip`, `basemap` and `display.ramp` refused, as for any kind that is not theirs;
- `buckets` dropped by `WidgetQuery.For`.

New rules:

| Rule | Key | Message |
|---|---|---|
| `display.orientation` not `vertical` or `horizontal` | `display.orientation` | "The orientation must be 'vertical' or 'horizontal'." |
| `display.orientation` on another kind (skipped when the chart type is null) | `display.orientation` | "Only a box plot has an orientation." |
| `display.color` on a box plot | — | accepted (`CheckDisplay` adds `Boxplot` next to `Histogram`) |
| `sort` on a box plot with `by: "column"` | `sort.by` | "A box plot can only be ordered by its median ('value')." |
| `sort` on a box plot with no group column | `sort` | existing "Sorting needs a column to group by." |
| `sort` on any other kind but bar and column | `sort` | existing message, now "Only bar, column and box plot widgets can be sorted." |

`CheckAgainstColumns` needs nothing new: a box plot names no column the other kinds do not.

The spec scenario "Only bar and column widgets have an order" keeps its title, because `openspec validate` refuses a renamed scenario inside a MODIFIED requirement. Its case, a line chart refused an order, is still true. Renaming it is left to a later change made for that purpose.

### D3. Query (`BoxQueryBuilder`, planned from `IndicatorQueryBuilder.Plan`)

`IndicatorDashboard.QueryAsync` plans a box plot when `chartType == ChartTypes.Boxplot`. It uses the shared parts of `Plan`:
- the measure or ratio and its row filter;
- the widget filters (`f0…`) and the dashboard filters (`d0…`);
- the missing-column checks: a missing column still means no SQL and a stale widget.

`IndicatorQueryPlan` gains a `Boxes` member, next to `Series` and `Domain`, and the "missing" test becomes `plan.Sql is null && plan.Series is null && plan.Boxes is null`. Without the box chart type, nothing changes, the histogram included.

One statement, in one read-only transaction under the existing slot, timeout and share lock:

```sql
WITH src AS (                        -- one row per value; materialized, as it is read more than once
  SELECT {g} AS g, ({plotted}) AS x
  FROM {from}
  WHERE {rowFilter} AND ({plotted}) > '-Infinity'::double precision
        AND ({plotted}) < 'Infinity'::double precision{filters}      -- also drops NULL and NaN
),
box AS (                             -- grouped by the typed column, as every other kind groups
  SELECT g, row_number() OVER () AS b,                         -- a box number, for hashable joins
         count(*) AS n, min(x) AS lo, max(x) AS hi, sum(x) / count(*) AS mean,
         percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY x) AS q
  FROM src GROUP BY g
),
fence AS (SELECT box.*, q[1] - 1.5 * (q[3] - q[1]) AS lf, q[3] + 1.5 * (q[3] - q[1]) AS uf FROM box),
marked AS (                          -- each value next to its box's fences
  SELECT f.b, s.x, f.q[2] AS med, (s.x < f.lf OR s.x > f.uf) AS out
  FROM src s JOIN fence f ON s.g = f.g                         -- equality, so a hash join
  UNION ALL
  SELECT f.b, s.x, f.q[2], (s.x < f.lf OR s.x > f.uf)
  FROM src s JOIN fence f ON f.g IS NULL WHERE s.g IS NULL      -- the rows with no group value
),
ends AS (
  SELECT b, min(x) FILTER (WHERE NOT out) AS lw, max(x) FILTER (WHERE NOT out) AS uw,
         count(*) FILTER (WHERE out) AS outliers
  FROM marked GROUP BY b
),
shown AS (
  SELECT b, array_agg(x ORDER BY x) AS xs
  FROM (SELECT b, x, row_number() OVER (PARTITION BY b ORDER BY abs(x - med) DESC, x) AS r
        FROM marked WHERE out) far
  WHERE r <= @maxOutliers GROUP BY b
)
SELECT f.g::text, f.n, f.lo, f.q, f.hi, f.mean, e.lw, e.uw, e.outliers, s.xs
FROM fence f JOIN ends e USING (b) LEFT JOIN shown s USING (b)   -- box numbers: hash joins
ORDER BY {order} LIMIT @limit
```

- **No group column.** `{g}` is `NULL::text`, so there is one box.
- **Group.** Values are grouped by the typed group column, as the other kinds group them. A true/false column, or numbers written `1.0` and `1.00`, give the same boxes as bars over the same table. No aggregate is taken over the group value, so a true/false group works (PostgreSQL has no `min(boolean)`, which is why `ColumnSortKey` uses `bool_and`).
- **Joins.** No join uses `IS NOT DISTINCT FROM`, which PostgreSQL cannot hash or merge: on many boxes such a join is a nested loop that grows with the square of their number.
  - Each value meets its box's fences through an equality join on the group. The rows with no group value join the missing box in a second branch.
  - Every later part is keyed by the box's number `b` (`row_number()` runs after the `GROUP BY`). So the final joins, over one row per box and not yet cut by `@limit`, are hash joins too.
  - A box plot grouped by a column of 100,000 values, such as an id by mistake, then returns its first 5,000 boxes and says the limit was reached, instead of timing out (task 10.2).
- **Whiskers always exist.** `lf ≤ q1 ≤ q3 ≤ uf`. With 3 or more values, a value lies between the quartiles. With 2, both lie within the fences. With equal values the IQR is 0 and those values are inside.
- **Overflow.** The mean is `sum(x) / count(*)`, not `avg(x)`. PostgreSQL's `avg` of `double precision` also accumulates squares and raises "value out of range" long before a sum does. With the sum, a box plot fails on the same data a bar chart fails on, with the same error.
- **Order and limit.**
  - Natural order: `ORDER BY f.g NULLS LAST`, the column's own order. The API then re-sorts the boxes it read with `NaturalOrder.ByGroup`, as the kinds that cannot be ordered do since `add-two-dimension-widgets` (its deviation "natural order of every kind that cannot be ordered"). When the limit is reached, the boxes kept are therefore the first in the column's own order, listed in natural order. The spec promises only that the limit is said.
  - By median: `ORDER BY f.q[2] {ASC|DESC}, f.g NULLS LAST`, then a stable re-sort by median with natural order between equal medians. Equal medians that straddle the limit are cut in the column's own order.
  - `@limit` is `MaxWidgetQueryRows + 1` and counts boxes. Reaching it sets `rowLimitReached`.
- **Reader.** `ReadBoxesAsync` reads `IndicatorBox(Group, Values, Min, Q1, Median, Q3, Max, Mean, LowerWhisker, UpperWhisker, OutlierCount, Outliers)`. It takes the group column's type as `ReadRowsAsync` does, so a timestamp group goes through `DataValues.IsoTimestamp` and reads like every other widget's, and a color kept for it matches. As a guard it skips a box with a non-finite figure; with finite values only `percentile_cont`'s interpolation could produce one.

*Alternatives:*
- *Return each row's value with its group and compute in the browser and in C#* (the histogram's way). Rejected:
  - the 5,000-row cap would truncate the distribution arbitrarily, because the per-row statement has no order;
  - it would need quantile twins in TypeScript and C# with shared fixtures;
  - MCP would return single rows.
- *`percentile_disc`.* Rejected: it does not match pandas or the two existing uses of `percentile_cont`.
- *Several statements.* Rejected: the CTE reads the table once and is materialized.

### D4. Response and wire format

`IndicatorQueryResponse` gains `boxes` (`WhenWritingNull`), sent only for a box plot. The JSON of every other kind stays byte for byte (`IndicatorQueryTests.cs:1055-1100`).

```json
{
  "published": true, "rowLimitReached": false, "missing": { "indicator": false, "columns": [], "any": false },
  "rows":  [ { "group": "Cali", "value": 610, "geometry": null, "rows": 120 } ],
  "boxes": [ { "group": "Cali", "values": 120, "min": 80, "q1": 420, "median": 610, "q3": 880, "max": 4200,
               "mean": 702.5, "lowerWhisker": 80, "upperWhisker": 1560, "outlierCount": 6,
               "outliers": [1720, 2100, 2650, 3000, 3900, 4200] } ]
}
```

- `boxes` is in display order (D3). `rows[i]` stands for `boxes[i]`: `value` is the median and `rows` the number of values.
  - These rows keep the generic code right: the empty state, the "no rows" outcome of emails (`ReportDataBuilder.cs:143`), MCP's `noRows` and the row-limit notes that count `Rows`.
  - The new types are records of their own; `IndicatorRow` is not derived, because a derived row type broke OpenAPI in the two-dimension change.
- `group` is null for the missing group value and for the single box of a widget with no group column. The UI tells them apart by the config: "All rows" when `groupBy` is null, "(none)" otherwise.
- `outliers` holds at most `MaxBoxOutliers` values, the farthest from the median, in ascending order. `outlierCount` counts them all. The UI's note "N outliers not drawn" is the sum over the boxes of `outlierCount - outliers.length`.
- The query request needs `chartType: "boxplot"`. Without it the query is a bar's, as today.

### D5. Drawing (`boxplot-chart.ts`)

The component follows the pyramid's pattern:
- an exported pure `boxplotPlot(boxes, orientation, size, format, colors, labels)` that returns the geometry, tested in jsdom;
- a component around it with `observeSize`, an `<svg class="tmz-chart-svg" role="application" tabindex="0">`, `markTooltips` with one mark per box (`s = 0`) and `groupsAcross: () => orientation === 'vertical'`, a `helpId` key help and an `aria-live` region.
  - The key help is `indicators.series.keyHelpGroups` ("Left and right arrows move between the groups…") when vertical, and a new `indicators.series.keyHelpGroupsDown` ("Up and down arrows move between the groups…") when horizontal.
  - As everywhere `markTooltips` is used, the first arrow key enters the marks at the first box.
- **Outliers are not marks.** `markTooltips` shows a tooltip only for a mark of its grid, and every mark of the grid is a keyboard stop. So outliers get a second tooltip of their own, created with `inject(ChartTooltips).create()`, which opens on pointer enter and closes on leave. It also closes when `markTooltips` resets or the component is destroyed (`DestroyRef`).
- **Hit areas.** Each box's transparent hit area covers its box and whisker span across the box's width, not the whole band. Each outlier has a transparent hit circle of radius 6. The outlier hits are drawn after the box hits, so they take the pointer.

`widget-view.ts` gains `@case ('boxplot')`. `axisKindOf` returns null for it.

- **Scale.** One linear scale (`linearScale`, `niceAxis`) over the smallest and the largest of the whisker ends, the means and the outliers drawn. A mean can lie beyond every drawn mark when outliers on its side were not drawn.
  - It does not force zero, and ticks use `format.tick`.
  - A flat domain (every figure equal) is padded in proportion to its value before `niceAxis`: `pad = |v| × 0.05`, or, when `v` is 0, 0.01 for a `percent` indicator and 1 otherwise. When every value is 0 or more, the low end is clamped at 0, so a rate never gets a negative tick. `niceAxis` pads a flat range by a fixed ±1, which gives ticks that read alike at 1,000,000, an axis from 0% to 200% for a percent at 1.0, and from -100% to 100% for a percent at 0.
- **Groups.**
  - Vertical: a `bandScale` along x, first box on the left, labels under the boxes as the column chart writes them (truncated to the band).
  - Horizontal: groups along y, first box at the top, a label column on the left as the bar chart and the pyramid have.
  - The box width is the band's width (70% of its step), at most 56 px.
- **Marks.**
  - The box (Q1 to Q3): fill in the box's color at 20% opacity and an outline in the color at 1.5 px.
  - The median: a 2 px line across the box, with the existing `stroke-heading` utility (the ink the pyramid and the heat map already draw marks in).
  - The mean: a 7 px cross, also `stroke-heading`.
  - The whiskers: 1.5 px lines in the box's color, with end caps half the box's width.
  - The outliers: hollow circles of radius 3 in the box's color.
  - A box of one value draws only its median line.
  - A box plot has no adjacent fills, so the palette method's 2-pixel gap between fills does not apply. The whiskers and the median join the box's outline with no gap. Nothing relies on color alone: the tooltip and the table carry every figure.
- **Key.** One line under the plot, wrapping on narrow widgets, worded as the spec's "Key" scenario:
  - a small box: "Middle half (Q1 to Q3)";
  - a line: "Median";
  - a cross: "Mean";
  - a whisker: "Whiskers: up to 1.5 × the box beyond it";
  - a dot: "Outliers".

  When outliers were not drawn, the note "N outliers not drawn" (with a `…One` variant) follows. Both print.
- **Tooltips.**
  - A box's `ChartTooltipContent`:
    - heading: the group, "All rows" or "(none)";
    - lines, from top to bottom: Values, Maximum, Upper whisker, Q3, Median, Mean, Q1, Lower whisker, Minimum and Outliers;
    - footer: the outliers this box did not draw, if any, with a `…One` variant.
  - An outlier's tooltip (pointer only): the group and the value.
  - The `aria-live` text is `tooltipSpeech` of the box's content.
- **Summary.** `aria-roledescription` "box plot". The accessible name reads "Box plot of {what}: {n} boxes, medians from {a} to {b}" (with a `…One` variant).
- **No new tokens or utilities.** The box's color is the widget's `accent` or `kept(groupBy, group)`, set with `[style.fill]` and `[style.stroke]`, and the opacity is a `fill-opacity` attribute. The median and the mean use `stroke-heading`, which the app already has. This keeps the initial bundle where it is.

### D6. Formatting, colors and "Show as table"

- **Figures.** Box figures use `valueFormatter` with the indicator's format and unit, except that a `count` indicator is formatted as `decimal` (spec "Indicator formatting"). Numbers of values and of outliers use the whole-number formatter, as the histogram's counts do.
  - A `ValueFormatter` does not expose its format, so the chart cannot change it. `widget-view.ts` builds the box plot its own formatter, `valueFormatter({ ...indicator, format: boxFormat(indicator.format) }, locale)`, and passes it to `BoxplotChart` and to `boxTableModel` instead of the shared one.
  - The API twins: `ReportFormat.Value(v, BoxFormat(meta.Format), unit)`, `ExcelFormat(BoxFormat(meta.Format))` (`"#,##0.00"` for a count), and MCP's English formatter.
  - `boxFormat`/`BoxFormat` is one function per side (`format == "count" ? "decimal" : format`).
- **Colors.** `chartColors` adds `boxplot` to `keepsByValue`, so `kept(groupBy, group)` gives a box its kept color and `accent` is the default. `displayFor` and `offersColor` add `boxplot` next to `histogram`. On the API side the `WidgetDisplay.Color` description, `DisplayHelp`, `Instructions.md` "Colors" and `docs/mcp.md` name it.
- **"Show as table".** `boxTableModel(…)`:
  - headers: the group column, or a new "Group" string (`indicators.boxplot.group`) when there is none, then "Values", "Minimum", "Q1", "Median", "Q3", "Maximum", "Mean", "Outliers";
  - numbers aligned to the end, one row per box in the chart's order;
  - the existing `TableModel` handles printing and the rows left out on paper.

### D7. The widget form (`widget-dialog.ts`)

- The kind select lists "Box plot" last. The group field stays optional, with the hint "Optional. Without it, a box plot draws one box over every row."
- **Ratio hint.** The ratio's hint today says the widget plots the sum of the numerator over the sum of the denominator. For a box plot it says each row's numerator is divided by its own denominator (`indicators.dialog.ratioHelpBoxplot`).
- **Order.**
  - For a box plot the order field is labelled "Order boxes by" (`indicators.dialog.sortByBoxes`). Its select offers the default (natural order) and "Median" only, then the direction select. "Compare as" is never shown.
  - Choosing "Median" saves `{ by: "value", direction }`.
  - A `column:` key left from a bar or column chart reads as the default for a box plot, so neither the direction select nor "Compare as" shows for it.
  - Without a group column the order field is disabled, with the reason next to it, as the API refuses it.
- **Orientation.** A radio group, Vertical / Horizontal, shown only for a box plot.
  - An `orientation` signal is seeded from `start.display?.orientation`. `setOrientation(o)` stores `o === 'vertical' && start.display?.orientation == null ? null : o`, as `setColor` does for color 1.
  - `displayDraft()` carries it next to color and ramp, so a widget with none of the three still saves no `display`.
  - It is kept out of the preview's query key, so switching it redraws the preview without a request.
- **Color.** The eight color radios, as for a histogram.
- **Twins.**
  - `queryFor` sends a box plot's `sort` only when it orders by value and there is a group column: `type === 'boxplot' && config.groupBy && config.sort?.by === 'value' ? sortFor(config.sort) : null`. So an analyst who picks "Median" and then clears the group gets a preview and a save with no order, not a 400 on a disabled field. The form keeps the choice for when a group comes back.
  - `queryFor` drops `seriesBy` for a box plot.
  - `savedConfig` and `displayFor` keep `display.orientation` and `display.color` for `boxplot` only.
  - `WidgetQuery.For` keeps `Sort` for `Boxplot` under the same rule.
  - Changing the kind away from box plot drops the orientation, as a map's tooltip is dropped.
- **Errors.** `fieldFor` maps `display.orientation` to the orientation field.

### D8. Emails (`Reports/`)

- `ReportDataBuilder`'s kind switch gains `ChartTypes.Boxplot => BoxplotTable(response.Boxes, …)`. Columns:
  - the group: `config.GroupBy ?? text["group"]` ("Group" / "Grupo"), so a box plot with no group column has the same header as "Show as table";
  - Values (`#,##0`);
  - Minimum, Q1, Median, Q3, Maximum and Mean (`ValueColumn` with `ExcelFormat(BoxFormat(meta.Format))` and the unit);
  - Outliers (`#,##0`).

  The rows are one per box in the response's order. A missing group reads "(none)" and the single box "All rows".
- `ReportWorkbook` has no per-kind code: it writes the formats `BoxplotTable` sets, so the formats are built and tested there (`ReportDataTests`).
- The table has 9 columns, the widest table in the body, since the body caps only series tables. The 10 rows per block and the 80 KB budget are unchanged. The workbook holds every box.
- The workbook keys number formats by header across sheets, so two box plots with different formats write "Median (5)" on the later sheet. That is the existing rule (`ReportWorkbook.cs:102-105`), and it stays.
- The row-limit note today reads "Only the first {0} rows are shown". For a box plot it uses a new `rowLimitBoxes` text, "Only the first {0} boxes are shown: the query matches more.", worded as the UI's new `indicators.widget.rowLimitBoxes`.
- `ReportText` gains, in English and Spanish, worded as the UI's strings: `group`, `values`, `minimum`, `q1`, `median`, `q3`, `maximum`, `mean`, `outliers`, `allRows`, `rowLimitBoxes`.

### D9. MCP (`Mcp/`)

- **Help texts.**
  - `ChartTypeHelp` gains "boxplot (the spread of each row's value per group: quartiles, whiskers and outliers)".
  - The `groupBy` sentences of `query_indicator` and `create_widget` add the box plot to the kinds that do not need one.
  - The `query_indicator` description says that `chartType: "boxplot"` returns boxes. The `get_widget_data` description says that a box plot returns `boxes` in place of rows.
  - The `create_widget` description:
    - its list of kinds gains "box plot";
    - its config template becomes `display: { color, ramp, orientation }`;
    - it says `sort` is for bar, column and boxplot, a boxplot by `value` (its median) only.
  - The two `config` parameter descriptions, and `WidgetConfigInput.Display`'s `Description`, name the orientation. The prefix "How the widget is drawn", which `McpToolCatalogTests` pins, stays.
  - `DisplayHelp` describes `orientation`, and `ChartTypeChangeHelp` adds `display.orientation` to the settings a bare kind change is refused on. Their pinned sentences change in the same task, with the tests that pin them:
    - `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` ("a color, a scale or an orientation", "config.display.color, config.display.ramp or config.display.orientation", "config.display = { color, ramp, orientation }");
    - `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `IndicatorDocsTests.The_mcp_documentation_gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do`, for `docs/mcp.md`.
- **Result.** For a box plot, `query_indicator` and `get_widget_data` return, in place of `rows`:
  ```
  boxes: [{ group, values, min, q1, median, q3, max, mean, lowerWhisker, upperWhisker, outlierCount, formatted: { min, q1, median, q3, max, mean, lowerWhisker, upperWhisker } }]
  ```
  - `outlierCount` is named as in the REST response.
  - The outliers' values are not sent, so each box stays a fixed-size summary: up to 100 values for each of up to 5,000 boxes would read like a listing of rows. The minimum, the maximum and the whisker ends are single figures per box, as `profile_table` already gives a column's minimum and maximum at *Summaries only* ("What summaries hold" allows the values widgets return).
  - The items hold no arrays, so `boxes` is a plain list: the result budget cuts it as it cuts `rows`, with each tool's existing hint. No budget change is needed.
  - The row-limit note says "The row limit was reached ({n} boxes)." for a box plot.
- **`Instructions.md`.**
  - The list of kinds on line 10.
  - A "Choosing a widget" bullet, in task 2.5 because `McpGuideTests` needs the kind in backticks: "The spread of a value across units in each group (income by municipality): `boxplot`, over a table with one row per unit and its id as text; `sort` by `value` orders the boxes by median."
  - The sentence on line 34 (what replaces `rows`) names `boxes` for a box plot.
  - The "Colors" line.
  - Line 9's "one small table" becomes "one table".
- **Schema.** `WidgetDisplay.Orientation`'s `Description` names the two values, in lower case. There is no enum, as for the other nullable fields (`McpToolCatalogTests.cs:134-186`).

### D10. Guide for scripts

`system.md` "The indicator contract" changes in two places, and `AssistantPrompts.Version` goes from 4 to 5:
- The rule on line 52 becomes "… so that there is one row per combination (one row per unit when a unit's id is among the dimensions)".
- A new bullet: "A box plot shows how a value is spread over units: return one row per unit (household, person) with its value, the dimensions to group and filter by first, and the unit's id as a text column after them (`household_id`). The id keeps the rows unique, and putting it last keeps new widgets from starting grouped by it. Do not group the units: the box plot computes the statistics."

New widgets start grouped by the table's first text column (`IndicatorQueryBuilder.DefaultGroupBy`, the form's `fillDefaults`, `get_indicator_columns` defaults), so the column order matters.

The dashboard view's warning "rows that share every dimension" does not fire on such a table, because of the id column, so the warning stays as it is. The id column, with more than 50 values, is listed and not charted, which is the view's existing behavior. No template is added.

### D11. Documentation

- **`docs/indicators-dashboard.md`.**
  - A row in the kinds table, "**Box plot**".
  - A section `### Box plot`: what a box is, the method (the same as pandas `quantile`), the whiskers and outliers, the 100 outliers drawn, order, orientation, colors, count figures with two decimals.
  - A section `### Preparing an indicator for a box plot`, with a pandas example of one row per household, its columns in the order `municipality`, `household_id` (as text), `value`, and why the id goes after the dimensions. It notes that such a table also feeds bars that count or sum, and dashboard filters.
  - The box plot exception in `### What to plot: a measure or a ratio` (a box plot divides each row's numerator by its own denominator) and in `### Duplicate rows are summed` (a box plot never sums).
  - Sentences in "Show as table", "Widget color and scale", "Colors kept by value", "Bar and column order" (box plots order by median) and "Widgets with a series" (not allowed).
  - A separate sentence saying that a duplicated or moved widget, and a restored version, keep a box plot's orientation. The duplication sentence that `IndicatorDocsTests` pins stays as it is.
  - The grid sentence, fixed while there: stat 3×2, map 6×6, heat map, pyramid and cross table 6×5, the others 6×4.
  - "Limits": the row limit counts a box plot's boxes, and 100 outliers drawn per box.
- **`docs/mcp.md`**: the kinds list, `display.orientation`, `display.color` for a box plot, and the `boxes` result.
- **`docs/operations.md`**:
  - `IndicatorDashboard__MaxWidgetQueryRows`: a box plot counts boxes.
  - "What leaves the deployment by email": a box plot's minimum and maximum are single rows' values.
- **`docs/tamiza-brief.md`**: section 6 (the `chart_type` list and `display.orientation`), section 5.7's stale kind list, and a roadmap item 7m for this change, done before 7l.
- **`README.md`**: the widget kinds.

## Risks / Trade-offs

- **[Per-unit tables are larger than "one row per combination" tables]**
  - The statement reads the table once; the 25-second limit, the query slots and the share lock still apply.
  - Task 10.2 measures 100,000 and 1,000,000 rows.
  - The documentation says to publish only the units needed.
- **[Minimum, maximum and outliers are single households' values]**
  - Dashboards, "Show as table" and emails show them, as a histogram without a group already shows single values.
  - The analyst decides what an indicator publishes.
  - MCP returns no outlier values.
  - `docs/operations.md` says so.
- **[A box draws at most 100 outliers]** → It draws the farthest from its median, which set the scale and are the most telling, and the widget says how many it left out. The table and the tooltip count them all.
- **[Two decimals on a count read "4.50 people"]** → One rule on every surface (dashboard, emails, workbook, MCP), documented. The alternative, rounding interpolated quartiles to whole numbers, would show figures that disagree with the drawn box and with pandas.
- **[Natural order is applied after the limit]** → It only shows when a box plot has more than 5,000 boxes, and the widget then says the limit was reached. This is the same trade-off as the kinds that cannot be ordered.
- **[`percentile_cont` disagrees with an analyst's own calculation]** → It is pandas' default. Integration tests pin the figures of known value sets against numpy's results (D3, task 2.7), and the documentation names the method.
- **[A config field that older releases do not know]** → `display.orientation` is dropped by an older release's reader, so a box plot would be drawn vertical. But an older release does not know the kind at all (see Migration Plan).

## Migration Plan

1. **Deploy.** `0020` widens the CHECK. Existing widgets have no box plot settings and draw as before.
2. **Rollback.** An earlier release refuses the kind in its query, so its emails and MCP report the widget as no longer fitting, and its UI's exhaustive switch draws nothing for it.
   - Remove box plots before rolling back: `DELETE FROM tamiza.dashboard_widgets WHERE chart_type = 'boxplot';`.
   - Dashboard versions that hold box plots stay. Restoring one later, on a release that knows the kind, brings them back.
   - DbUp never runs scripts backwards. The wider CHECK stays and is harmless.

## Open Questions

- Whether a later change should let a box plot take a series (boxes side by side by a second dimension). This change refuses a series, and the response shape (`boxes` with a `group`) could carry a series member later without breaking readers.

## Deviations from the plan

- **The box number `b` joins every box-level part** (D3). The first draft joined `ends` and `shown` to the boxes with `IS NOT DISTINCT FROM`, which PostgreSQL cannot hash; a review found it would be quadratic in the number of boxes. Each box gets `row_number() OVER ()` after the `GROUP BY`, `src`, `fence` and `marked` are `MATERIALIZED` so the number is the same everywhere, and every join is an equality. A table of 100,000 one-value groups returns its first 5,000 boxes with the limit said, in under a second.
- **A box of one value draws no mean cross** (D5). Its mean is its median, and the spec says it is drawn as one line.
- **The hit area of a box stays inside its band and reaches the mean** (D5). A hit area wider than the band covered its neighbours when there were many boxes.
- **An outlier's tooltip also opens on a tap** (D5). A touch screen has no hover: the tooltip stays until the next touch elsewhere or a scroll, as a box's does.
- **The median row of a box's tooltip has no color swatch** (D5). The median line is drawn in ink, and the box's color would say it belongs to the box.
- **The summary says the medians once when they are all equal** (`indicators.boxplot.summarySame`).
- **The orientation field is called "Orientation"** in the form, not "Direction": the order has a "Direction" select of its own.
- **The direction select is off with the order** when a box plot has no group column.
- **The MCP result has no list of outliers, not "no outlier value"** (spec "Indicator queries and widget data"): a box's minimum and maximum are single rows' values and are an outlier when the box has one on that side. The spec, `docs/operations.md` and the test say so.
- **Tasks 3.1 and 2.6 were done in new tests** (`ReportDataTests`, `ReportSenderTests`) instead of in `Shapes_each_widget_like_its_show_as_table_view_in_reading_order`, and 1,200 values are checked in their own test.
- **The scenario "Only bar and column widgets have an order" keeps its title** (see D2).
- **Not bounded: the total number of outliers of a response.** Up to 100 per box and 5,000 boxes can be 500,000 values on the page's path. The SQL ranks outliers before the limit cuts the boxes. It is acceptable at the sizes measured below, and the MCP result carries none.

## Verification Results

Checked on the developer's Windows machine (Docker Desktop, .NET 10, Node 26, PostgreSQL 17 with PostGIS in containers, Chrome headless).

**Tests**

| Suite | Result |
|---|---|
| API unit | 1,783 passed |
| API integration, whole suite (before the last small changes) | 1,877 passed, 2 failed: `IndicatorSeriesQueryTests.A_ratio_total_is_the_ratio_of_the_sums…` and `…A_ratio_cell_whose_denominator_sums_to_zero…` write numbers with the machine's decimal comma (`0,5`). They pass with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` and do not touch the box plot |
| API integration, `Mcp`, `Reports`, `IndicatorBox*` and `IndicatorOverview`, after the last changes | 365 passed |
| UI (`ng test`, `TZ=UTC`) | 2,629 passed, 2 failed: the known `project-data` and `project-scripts` "stops polling when the page is destroyed" tests. Without `TZ=UTC` a third fails (`project-indicators.spec.ts`, the Spanish title of a version: the machine is 5 hours from UTC); it passes in UTC |
| Analytics (`tamiza-analytics-test:dev`) | 620 passed |
| `ng build` | Initial bundle 660.25 kB, as before the change (budget 675 kB); the chart is in the lazy `project-indicators` chunk (316 kB). `scripts/check-ui-safety.sh` clean |

**Query time through the API** (PostgreSQL in a container, one request after a warm-up, in milliseconds as the HTTP client saw them; `value` with a few far outliers, `ratio` per row, the dashboard filter keeps half the rows):

| Rows | Groups | Natural | Median desc | Ratio | Dashboard filter | No group |
|---|---|---|---|---|---|---|
| 100,000 | 40 | 180 | 168 | 222 | 138 | 147 |
| 100,000 | 500 | 188 | 201 | 252 | 852 (first call of its kind) | 130 |
| 1,000,000 | 40 | 1,235 | 1,184 | 1,371 | 643 | 909 |
| 1,000,000 | 500 | 1,408 | 1,409 | 1,567 | 795 | 934 |

100,000 groups of one value each: 5,000 boxes with `rowLimitReached` in 870 ms (natural) and 839 ms (median desc). The statement limit is 25,000 ms.

**In a real browser** (a Compose project of the real api and ui images behind a fake sign-in and fake Kobo, Mailpit for the email; 4,246 rows of a count indicator over 12 municipalities with long names, a box with 140 outliers, one of a single row, rows with no municipality, and a per-row percent ratio with rows whose denominator is 0; 7 box plots on a dashboard with the *Earth* palette, a color kept for one municipality, and filters on `sex` and `round`):

- Every box plot was captured at 1,280 and 390 px in the light and the dark theme: no horizontal page scroll, no cut text, the key readable, "40 outliers not drawn" for the box with 140, natural order of age bands, the kept color and the widget color right, a single-row box drawn as its median line.
- Keyboard: one Tab stop, a visible focus ring, arrows (across when vertical, down when horizontal), Home, End and Escape; the live text reads the box's figures. Pointing at a box and at an outlier opens the right tooltips.
- "Show as table" and "Show all as tables": nine columns, two decimals for the count indicator, whole numbers for the counts, "Group" and "All rows" without a group column, "(none)" for the missing group.
- The form: group hint, "Order boxes by" with the default and Median only (off without a group), orientation and color radios, the ratio hint; changing the orientation or the color sent no query, changing the group sent one; a bad orientation answered 400 and showed on the field.
- Printing to PDF on A4, portrait (3 pages) and landscape (5 pages): every chart whole with its key and note, none split across pages.
- A dashboard filter narrowed the boxes; dropping a column made the widget say which column is missing while the others kept drawing.
- The scheduled email in Mailpit: nine columns and ten rows then "N more rows" in the body and the text, and one sheet per widget in the workbook with every box, counts as `#,##0`, a count indicator's figures as `#,##0.00` and the percent ratio as `0.00%`.
- All 14 boxes of one widget and six more figures matched an independent calculation with `percentile_cont` in psql and with pandas' `quantile`.

Left as they are, by judgment: with 13 or more boxes in the default 6 × 4 widget only some group labels are written (as for every chart; the tooltip and the table carry the rest); a box with 100 far outliers flattens the others on its one scale, by design; the nine-column table wraps units inside a 6 × 4 card and scrolls inside it.

**Not verified.** The MCP tools from Claude itself (the real-browser stack ran with the MCP server off; the tools are covered by `McpDashboardToolsTests` through an MCP client), the Spanish interface and email in a browser (strings are checked for parity and one plural in the specs), a real screen reader (the live text was read from the DOM), the browser's own print dialog (`Page.printToPDF` stands in for it), opening the workbook in Excel (not installed), touch taps on a phone (tested in jsdom), Firefox and Safari.
