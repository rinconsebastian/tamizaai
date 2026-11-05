# Proposal

## Why

A bar chart of a rate by comuna shows each bar, but not what to compare it with. In the Duitama study (Centro Bicentenario, 478 surveys), "Asistió, por comuna" (Teatro 4e00) draws one column per comuna, while the figure each column is read against, 46.85 % of the households, sits on a separate stat tile (1b71). The user asked for four optional lines on vertical and horizontal bars: the mean of the bars, their median, the indicator's overall value and a fixed value with a label.

Demand curves have the same gap. "Cuánto pagaría por una boleta" (Teatro 233d) joins its prices with straight segments, although a share holds until the next price, and the median paid lives in a separate box plot (3e1c); Escuelas 2d22 even asks the reader to "compare it with the current median, above". On paper, where there are no tooltips, a bar's value can only be read off the axis.

## What Changes

- **Reference lines** on bar, column and line charts: up to 4 per widget, each optional, each with an optional label of up to 40 characters:
  - **mean** and **median** of the bars (or points) the chart shows, after every filter. A "(none)" bar counts like any other. With an even number of bars the median is the mean of the two middle ones, as pandas computes it;
  - **overall**: the indicator's value over all the widget's rows with the same filters, the figure a stat tile with the same indicator and filters shows. It needs a rate (a numerator over a denominator): over a measure, a count or a sum, it would be the sum of every bar, so it is refused there;
  - **fixed value**: a number in the indicator's units, such as a target of 50 % labelled "Meta".
- **Reference lines on a chart with a series**: side-by-side bars and lines can have an overall line and fixed values; stacked bars only fixed values; 100 % bars only fixed shares (50 % crosses every bar at its middle). Mean and median lines need a chart with no series. Any other combination is refused with a message that says why.
- **How a reference line looks**: a dashed line across the chart, vertical on horizontal bars and horizontal on columns and lines, with its label and value at its end, written with the indicator's unit as the widget's tooltips and tables write values ("Mean: 34.17% de los hogares"; "Promedio: 34,17 % de los hogares" in Spanish). The axis stretches to include it. Each line has its own dash pattern, and a key under the chart lists every line. The key belongs to the chart, so it sits above the widget's footnote and takes its room from the plot, as the subtitle and the footnote do. A text that does not fit at its line is left out there and still listed in the key. The lines also appear under "Show as table", in images, on paper, in the scheduled email and its workbook, in the widget workbook and in what the MCP tools return.
- **Step lines** on line charts, with or without a series: each value holds until the next point, as a demand curve reads. Straight segments stay the default.
- **Data labels** on bar and column charts (no series, or bars side by side), line charts and pie charts: the value written at the end of each bar, above each point and on each slice. A label that does not fit is left out. Off by default.
- **The widget form** has a reference lines editor, a straight-or-steps choice and a data labels checkbox, each shown only for the charts that take it. A percent target is typed as a percent (50). The help for the overall line says plainly that over the answers of one question, such as places or reasons, it is only the mean of the bars and not a total; it is meant for groups of households such as comunas or estratos. Editing a label or a line redraws the preview at once; only adding or removing an overall line asks the database once more.
- **Compatibility**:
  - every setting is optional, and a widget without them is drawn, queried, saved and exported exactly as today;
  - none of the 190 production widgets has them, so their charts, numbers, versions, scheduled emails, workbooks and MCP answers do not change;
  - saving a widget's form without touching anything changes nothing and makes no new version;
  - no database change.

Not in this change:
- A line from another indicator with its own filters, a vertical line on a line chart's category axis, and shaded bands (P2 `add-reference-lines-from-indicators`).
- A Total bar next to the bars, the figure a chart of a measure would compare with in place of an overall line (P2 `add-normalization-and-totals`).
- Reference lines on stat tiles, histograms, pies, maps, box plots, heat maps, pyramids, Likert charts and cross tables.
- Data labels on stacked and 100 % bars, heat maps, pyramids and Likert bars, which already show their values or shares their own way.
- Colors for reference lines: they are drawn in the text's ink and told apart by their dash and label.
- Short numbers in data labels ("$1,2 M") and decimals per widget (`add-locale-formatting`).
- Hiding the overall line when the bars are the answers of one question. An indicator can only declare that with `add-small-sample-warnings`; design D12 gives the rule that change follows.

Builds on two changes implemented and archived on 2026-11-04:
- `add-indicator-column-metadata`: its production-shapes fixture (`production-shapes.json`), which this change extends, and `display.labels`, which comes before the settings of this change in the stored display.
- `add-widget-context`: the stored `display` ends with `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription`, after which this change appends its three fields. The widget workbook's request already carries the widget's saved display, which this change reads. The widget's own unit is the unit the lines and labels are written with. The subtitle, the footnote and the dashboard's source already have their places in images, emails and workbooks, and the lines are placed relative to them.

## Capabilities

### New Capabilities

None. Widget settings and drawing belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Line chart" (modified): its points are joined by straight segments, or by steps when the widget asks for them;
  - new requirements: "Reference lines on bars, columns and lines", "Mean and median reference lines", "Overall reference line", "Fixed-value reference line", "Reference lines with a series", "Reference line settings refused", "Reference lines drawn", "Reference lines in "Show as table"", "Step lines", "Data labels on bars, columns, lines and pies", "Chart annotations in the widget form" and "Chart annotations kept with the widget".
- `widget-export`: new requirements "Chart annotations in widget images" and "Reference lines in a widget workbook".
- `dashboard-printing`: new requirement "Chart annotations on paper".
- `dashboard-reports`: new requirement "Reference lines in a scheduled email".
- `mcp-server`: new requirements "Chart annotations through MCP" and "Reference lines in MCP widget data".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`:
    - `WidgetDisplay` gains `ReferenceLines`, `Interpolation` and `DataLabels`;
    - new types: the `WidgetReferenceLine` record and the constants `ReferenceLineKinds` and `LineInterpolations`;
    - new limits `WidgetDisplay.MaxReferenceLines` (4) and `WidgetDisplay.MaxReferenceLineLabel` (40), beside the text limits of `add-widget-context`;
    - `IndicatorQueryRequest.Overall`, a flag of the query request that no widget stores;
    - `IndicatorQueryResponse.Overall`.
  - `Indicators/IndicatorValidation.cs`: the rules for the three new display fields.
  - `Indicators/WidgetQuery.cs`: `Overall(chartType, config)`, which decides when a saved widget asks for the overall value.
  - `Indicators/IndicatorQueryBuilder.cs`, `IndicatorDashboard.cs` and `IndicatorEndpoints.cs`: the overall statement (`IndicatorQueryPlan.Overall`) and the request flag.
  - A new `Indicators/ReferenceLines.cs`, the C# twin of the values of the lines.
  - `Reports/`: `ReportModel`, `ReportDataBuilder`, `ReportHtml`, `ReportWorkbook`, `WidgetWorkbook` and `ReportText`.
  - `Mcp/McpDashboardTools.cs` and `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, and the production-shapes fixture.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the new types, the `overall` argument of `query` and the `overall` of its result.
  - `indicators/widget-view.ts`: `overallFor`, the query key and `displayFor`.
  - A new `indicators/reference-lines.ts`, with its fixture `indicators/testing/reference-lines.json` shared with the API.
  - Drawing: `axis-chart.ts`, `series-plot.ts`, `chart-scales.ts` (a step path), `pie-widget.ts` and `series-legend.ts` (the key).
  - Images: `export-parts.ts` and `widget-export/image-layout.ts` (a dashed swatch).
  - `indicators/widget-dialog.ts`.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** none. No migration, and no stored config is rewritten.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: reference lines, the caution about the overall line, step lines, data labels, and the widget types table;
  - `docs/mcp.md`: the display fields and `referenceLines` in widget data;
  - `docs/operations.md` "What leaves the deployment by email": the labels of reference lines, which members write as free text;
  - `docs/tamiza-brief.md`: the three `display` fields in section 6, and the program's roadmap entry in section 8.
