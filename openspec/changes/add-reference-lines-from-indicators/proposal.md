# Proposal

## Why

The study's demand curves compare what households would pay with what they pay today, but the reference figure always sits in another widget:
- Teatro 233d "Cuánto pagaría por una boleta: curva de demanda" (`teatro_demanda`: the % that would pay each price or more) is read against the median of what households paid for their last ticket (E7, 40,000 pesos in `teatro_precio_hogar`). That median is drawn as a separate box plot, 3e1c, whose title explains that "la mediana es la línea de la caja".
- parqueadero 77ea is read against box plot 8492 ("Referencia: …", median 3,000 pesos per hour).
- The title of Escuelas 2d22 says "compárela con la mediana actual, arriba" and points to card cef7 (60,000 pesos).

`add-chart-annotations` draws mean, median, overall and fixed lines from the widget's own rows. But this reference is a statistic of another indicator with its own filters, and it is a price: it belongs on the curve's price axis, not on its % axis. A line chart's category axis is a row of evenly spaced categories (`bandScale` in `axis-chart.ts:339`, points at line 355). The curves are drawn over the text labels "$0", "$10.000", "$20.000", "$35.000", "$50.000", which are not spaced by value, so 40,000 has no place on that axis.

## What Changes

- **References from another indicator** (`config.references`, at most 2 per widget). This field changes the query: the query twins send only its query part, as they send only a map tooltip's columns. Each reference is `{ "indicator", "measure" | "ratio", "filters", "statistic", "to"?, "axis"?, "label"? }`:
  - `indicator` is any indicator of the same project, the widget's own included. `measure` or `ratio` default as a widget's do, and `filters` take up to 10 filters shaped like a widget's;
  - `statistic` is `overall` (the default), `mean`, `median`, `p25` or `p75`. The value is exactly what a stat tile over that indicator, measure or ratio, filters and statistic shows (`improve-stat-cards`): `overall` is the sum or the ratio of sums, and the others take one value per row, computed in the database as a box plot does;
  - a reference is narrowed by the dashboard filters that narrow its widget, minus the ones the widget ignores. A filter whose column the reference's table lacks is skipped silently, as for widgets today. Its own filters apply too;
  - `label` has 1 to 40 characters. By default it is the statistic's name and the indicator's label;
  - example: Teatro 233d, grouped by its price published as a number and drawn on a numeric category axis (below), with `{ "indicator": "teatro_precio_hogar", "measure": "value", "filters": [variable = "Pagó por la última boleta (E7)"], "statistic": "median", "axis": "category", "label": "Mediana de lo que pagó (E7)" }`, draws a vertical line at 40,000. With the dashboard filter Comuna set to "COMUNA 1" the line moves to 35,000, as box plot f21f does.
- **Bands.** A reference with `to` (a second statistic of the same source, for example `p25` to `p75`) is drawn as a shaded band, with its label and both values.
- **Numeric category axis on line charts** (`display.categoryAxis: "numeric"`):
  - a line chart grouped by a number column places its points at their values on a linear axis, so the gaps between prices are in proportion;
  - it is needed for references with `axis: "category"`, and the fixed `value` lines of `add-chart-annotations` can take `axis: "category"` there too;
  - it keeps natural order and works with the step line of `add-chart-annotations`. By default the axis stays as today: evenly spaced categories.
- **Where references apply:**
  - on the value axis (`axis: "value"`, the default), on bar, column and line widgets, with the series rules that `add-chart-annotations` sets for its fixed `value` line;
  - on the category axis, only on a line chart with a numeric category axis;
  - a value-axis reference whose indicator has another format than the widget's indicator (a count on a percent axis) is refused with a message that says why.
- **Drawn** like the lines of `add-chart-annotations`:
  - a dashed line across the plot (vertical on the category axis), with the axis widened to include it;
  - its label and its value in the reference indicator's format and unit ("Mediana de lo que pagó (E7): 40,000 pesos");
  - listed in the chart key, and shown in tooltips, printing and widget images.
- **A reference that cannot be drawn** (its indicator is not published, a column it names is gone, or its filters match no row) leaves the widget drawn, and a note says why, for example "Reference not drawn: teatro_precio_hogar has no rows for these filters".
- **Cost.** Each reference adds one SQL statement to its widget's query. The statement runs in the same read-only transaction, in the same query slot and with the same 25 s timeout. At most 2 per widget.
- **Everywhere a widget goes:**
  - the widget form gains a reference editor (indicator, measure or ratio, filters, statistic, band end, axis and label), reusing the filter editor, with a live preview;
  - "Show as table" lists the references under the table;
  - the scheduled email (body note and workbook) and the widget workbook summary list each reference's label, indicator, statistic, filters and value;
  - in MCP, `create_widget` and `update_widget` take `references`, and `get_widget_data` returns each reference's value, its band end and their formatted text;
  - the docs.

Not in this change:
- References on box plots, histograms, heat maps, pies, maps and stat tiles. Whether a histogram gets a median line is decided in `improve-histogram-bins`.
- One reference per group, such as the median of each comuna drawn on its own bar.
- References to another project's indicators or to clean data tables, and references that choose their own dashboard filters.
- Bands between two fixed values, or between two statistics of the widget's own rows.
- More than 2 references per widget, and text notes attached to a point.
- A numeric axis read from text labels such as "$10.000" (a decision, see Next step).
- Cumulative curves computed by the engine.

Depends on:
- `add-chart-annotations`: the reference line model and its drawing, chart key, labels, fixed `value` lines with their series rules, and step lines.
- `improve-stat-cards`: `statistic` and its SQL (one value per row, `percentile_cont`), and the rule that a reference shows what a stat tile shows.
- `add-indicator-column-metadata`: column names in the form and in default labels.
- `add-small-sample-warnings`: a reference's n in its tooltip, when the base is known.

## Capabilities

### New Capabilities

None. Widget queries and drawing belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`: only new requirements: references from another indicator, reference bands, the numeric category axis of a line chart, vertical reference lines on a line chart, and references that cannot be drawn. "Widgets" ("a query over exactly one indicator") stays true: a reference is a separate statistic and does not change what the widget plots, as the new requirement says. "Line chart" also stays true: the numeric axis changes only the spacing, never the order.
- `dashboard-reports`: a new requirement for references in a scheduled email.
- `widget-export`: a new requirement for references in a widget workbook's summary.
- `mcp-server`: a new requirement, "Reference lines from another indicator through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`: `WidgetConfigInput` and `WidgetConfig` gain `references`, `WidgetDisplay` gains `categoryAxis` (and `WidgetDisplay.Normalize()` keeps a display that holds only it), and `IndicatorQueryResponse` gains `references`. Every new field is `[JsonIgnore(Condition = WhenWritingNull)]`.
  - `Indicators/IndicatorValidation.cs`: the limits, the kinds, the axis and the format rule.
  - `Indicators/IndicatorDashboard.cs`: `QueryAsync` (lines 111-176) describes each referenced table and runs its statistic in the same transaction. `CheckConfigAgainstTableAsync` (line 93) checks a reference's columns against its own table.
  - `Indicators/IndicatorQueryBuilder.cs`: the stat path with the statistic of `improve-stat-cards`.
  - `Indicators/WidgetQuery.cs`, and in `Reports/`: `ReportDataBuilder`, `ReportHtml`, `ReportText`, `ReportWorkbook` and `WidgetWorkbook`.
  - `Mcp/McpDashboardTools.cs` (`DisplayHelp`, `ChartTypeChangeHelp`, the widget data payload) and `Mcp/Instructions.md`.
  - OpenAPI, and the tests: `WidgetQueryTests`, `WidgetDisplayTests`, `IndicatorQueryTests`, `ReportDataBuilderTests`, `McpToolCatalogTests`, `McpGuideTests`, `IndicatorDocsTests`, and the production shapes fixture of `add-indicator-column-metadata`, extended with a demand curve that has a reference.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the types.
  - `indicators/widget-view.ts`: `queryFor` (line 102) and `savedConfig` (line 151).
  - `indicators/widget-dialog.ts`: the reference editor.
  - `axis-chart.ts` (a linear category axis for lines in place of `bandScale` at line 339, vertical lines and bands), `series-plot.ts`, `chart-scales.ts`, and the chart key in the export parts.
  - `en.json` and `es.json`, and the specs, `query-for.spec.ts` among them.
- **Database:** none. The fields live in `dashboard_widgets.config`.
- **Analytics:** none.
- **MCP:** the tool descriptions, `Mcp/Instructions.md` and `docs/mcp.md`.
- **Docs:**
  - `docs/indicators-dashboard.md`: references, the numeric axis, and preparing a demand curve with a number column for its price;
  - `docs/mcp.md`;
  - `docs/operations.md`: each reference is one more statement in the query slots.

**Compatibility.** A widget without `references` runs no extra statement and looks as today. The demand curves 233d, 3229, 77ea and de78 keep their evenly spaced text axes and their group order, because the numeric axis is opt-in. Box plots 3e1c and 8492 and card cef7 stay as they are. No widget is added, so the 50-widget limit is untouched. A reference skips silently the dashboard filters its table lacks, as the median cards rely on for their widgets, and the widget's `ignoredDashboardFilters` (Comercio aea1 and 5901) apply to its references too. The fields are written only when set, so untouched widgets keep byte-identical stored JSON and make no spurious version. Versions, duplicates and moved widgets carry the fields, and a reference always points into the same project. Removing the referenced indicator never removes the widget: the widget shows the "Reference not drawn" note. Scheduled emails and the MCP tools change only for widgets that have references. An MCP client that does not send the field gets today's widget. After a rollback, an older API ignores `references` and `display.categoryAxis` and drops them on the next save, so the lines disappear but no widget is refused.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next in line, against the main specs as they are then (after `add-chart-annotations` and `improve-stat-cards` are archived). Decisions to take with the user at that time:
- Where a reference is saved: a top-level `config.references`, which an older API drops silently (proposed), or a new kind in `display.referenceLines`, which an older API refuses. The choice should follow what `add-chart-annotations` chose for its `overall` line.
- The numeric axis: only for a number group column, so a demand indicator publishes its price as a number (for example `precio_valor`, proposed), or also for text labels that read as numbers in the project's format.
- Whether a reference follows its widget's dashboard filters (proposed) or can ignore them on its own.
- Whether a value-axis reference whose format differs is refused (proposed) or only warned about.
- The limit of 2 references per widget, and whether a band counts as one.
- Whether `query_indicator` also takes references, so an MCP client can check one before saving.
