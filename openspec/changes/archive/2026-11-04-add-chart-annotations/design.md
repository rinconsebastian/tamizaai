# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This is the third change of the widget and dashboard program and the first one about drawing. It follows the box plot's patterns for display settings (`add-boxplot-widget` D1, D7). It is written against the main specs and the code as they are after `add-indicator-column-metadata` and `add-widget-context` were implemented and archived (2026-11-04), so what they added is stated in the past tense. The pieces it works on:

- **`display` is how a widget is drawn, apart from its query.**
  - `WidgetDisplay` (`Indicators/IndicatorModels.cs`) is the record `(Color, Ramp, Orientation, Labels, Subtitle, Footnote, Caption, Unit, ShowDescription)`, in that order: `add-indicator-column-metadata` appended `Labels` and `add-widget-context` the five texts. Each member is `WhenWritingNull` and has a `[Description]` that the MCP schema shows, and `Normalize()` returns null when nothing is set. The text limits are constants of the record (`WidgetDisplay.MaxSubtitleLength`, `MaxFootnoteLength`, `MaxCaptionLength`). `WidgetConfig.Display` is the only `WhenWritingNull` member of the stored config.
  - `CheckDisplay` (`Indicators/IndicatorValidation.cs`) gives the per-kind rules, keyed `display.color`, `display.ramp` and `display.orientation` after the caller's prefix (`config.` on the widget endpoints), and skips them when the chart type is null (the query endpoint). It then calls `CheckLabels` and `CheckContext`, which have no rule per kind. `CheckText` counts a text's length once trimmed and looks for control characters in the text as sent; a reference line's label follows it.
  - The stored text is pinned by `WidgetDisplayTests`: the goldens `MinimalBefore` and `RichBefore`, and the test of the texts, which pins their order in the record. Versions embed the stored config as it is (`DashboardVersions`), a duplicate copies it (`DashboardEndpoints`), and an update writes nothing when the normalized JSON is unchanged.
  - Unknown properties are dropped, never refused: `ReadConfig` reads with Web defaults, and so does the query endpoint's body.
- **The two query twins.**
  - The UI's `queryFor` (`indicators/widget-view.ts`) and the API's `WidgetQuery.For` (`Indicators/WidgetQuery.cs`) build the query a chart type uses. Neither sends `display`.
  - `WidgetQuery.For` feeds `IndicatorDashboard.QueryAsync` (`Indicators/IndicatorDashboard.cs`) for the scheduled email (`ReportDataBuilder.ReadWidgetAsync`), MCP `get_widget_data` (`McpDashboardTools.WidgetDataAsync`) and the widget workbook, which goes through the same builder. The page's query endpoint passes its body's config and chart type (`IndicatorEndpoints.QueryAsync`), and `query_indicator` builds an `IndicatorQueryRequest(config, chartType)`.
  - The preview's re-query key is `queryFor` plus the dashboard filters (the view's `key` in `widget-view.ts`), and the service posts `{ config, chartType?, dashboardFilters? }`, leaving out what is empty (`IndicatorsService.query` in `core/projects/indicators.service.ts`).
  - `savedConfig` spreads `queryFor` and adds `displayFor`, which lists what a widget is saved with, per kind: a color, a scale or an orientation only for the kinds that take one, and on every kind the labels and the five texts (a change of chart type keeps them).
- **The plain grouped query** is one statement: `SELECT group, (aggregate), geometry … GROUP BY … HAVING (aggregate) IS NOT NULL ORDER BY … LIMIT` (`IndicatorQueryBuilder.Plan`).
  - The aggregate is `sum(m)` or `sum(num) / nullif(sum(den), 0)`.
  - A stat tile is the same statement with no group (`queryFor` drops `groupBy`): no `GROUP BY` and no `ORDER BY`, so its `HAVING` leaves no row when the denominators sum to zero.
  - A pie adds a second statement, `IndicatorQueryPlan.Domain`, which the plan holds beside `Sql` and the dashboard reads before the rows (`ReadPieDomainAsync`). The overall statement of D4 follows that pattern.
  - The reader (`ReadRowsAsync`) skips a non-finite value and stops at the row limit.
  - `IndicatorQueryBuilderTests.A_grouped_query_that_is_not_a_map_has_no_extra_outputs` pins that a grouped query that is not a map starts with an exact `SELECT` and has no `count(*)`.
  - `IndicatorQueryTests.Every_other_kind_answers_what_it_answered_before_with_no_domain` pins the response JSON of bar, stat and column widgets byte for byte.
- **A series response already has the grand total.** The cells statement's last branch (`SeriesParts.Grand` in `SeriesQueryBuilder`) is the aggregate over every row the widget and dashboard filters keep. It reaches the UI as `totals.all` (`QueryResult.totals` in `core/projects/indicators.service.ts`; `series-table.ts`) and the API as `IndicatorQueryResponse.Totals.All` (`SeriesTable.GrandTotal`).
- **Charts are hand-drawn SVG.**
  - `AxisChart` draws bar, column, line and histogram (`indicators/axis-chart.ts`). Without a series its `plain()` layout builds the value axis from `min(0, values)` and `max(0, values)` and draws bars, a line through `linePath` and columns. Its marks have only a native `<title>` and no hit areas (the layout gives `plain()` an empty `hits`). A line draws its dots only up to 40 points.
  - With a series, `seriesPlot` (`indicators/series-plot.ts`) builds its own axis (a 100 % layout fixed at 0 to 1) and `linePlot` its lines through `linePath`. The marks there are keyboard stops (`markTooltips`).
  - `linePath` (`indicators/chart-scales.ts`) writes straight segments only.
  - `PieWidget` draws slices with a `<title>` (`indicators/pie-widget.ts`) and a legend with value and share.
  - The chart is a flex column: the caption, or a series legend, then the plot box (`chart-box`, `min-h-0 flex-1`) that holds the SVG (`axis-chart.ts`). A caption the widget hides stays for screen readers (`sr-only`). The SVG is drawn at no less than 160 × 120 px. Text in it is 11 px (`font-size="11"`), measured with `charWidth = 6.5` (`chart-scales.ts`).
- **Values are written by one formatter per side.** The UI's `valueFormatter` (`indicators/format.ts`) writes a value with its unit ("46.85% de los hogares", "100 hogares"), and `ReportFormat.Value` (`Reports/ReportFormat.cs`) does the same in the email and the workbooks. The widget's own unit replaces the indicator's before both: `withWidgetUnit` gives the view's `formatter` its unit (`unitOf` in `widget-view.ts`), and `ReportDataBuilder.MetaOf` gives the email, both workbooks and MCP's `get_widget_data` theirs.
- **Images and print.**
  - An image is the live plot SVG plus the `ExportParts` a chart registers: caption, legend, notes, table (`indicators/export-parts.ts`). `widget-export/image-layout.ts` lays them out top to bottom under the title: the subtitle, the notes above, the caption, the body, the legend, the notes below, the footnote and the footer, the last three filled by `widget-exporter.ts` from the widget's display and the dashboard's source.
  - A legend entry's swatch is `square`, `line` or `outlined` (`ExportSwatch`; drawn by `swatch` in `image-layout.ts`).
  - Print is the same DOM in paper mode, where every chart measures itself again (`indicators/dashboard-print.ts`). The subtitle and the footnote print whole and take their room from the plot, and the tile never grows.
- **The email and the workbooks** are tables only. `ReportDataBuilder.BuildWidgetAsync` builds one `ReportWidget` (`Reports/ReportModel.cs`) per widget: `ReadWidgetAsync` queries and builds the caption, columns, rows and notes, and `BuildWidgetAsync` then adds the subtitle, the footnote and whether the caption is hidden, whatever became of the widget. `ReportHtml.Block` and the plain text write a block in this order: title, subtitle, caption (unless hidden), notes, the table, the lines that say how many more rows or columns, and the footnote (`dashboard-reports` "Widget footnote in a scheduled email"). The email workbook's "Summary" (`ReportWorkbook`) lists each widget's subtitle, footnote and notes, in that order, and the widget workbook's "Summary" (`WidgetWorkbook`) the source, the subtitle, the indicator, the plotted line, the filters, the dates, the rows, the footnote and the notes.
- **The widget workbook request** sends `savedConfig(widget.chartType, widget.config)` (`exportWorkbook` in `pages/projects/project-indicators.ts`), since `add-widget-context`, so every display setting reaches the server, the new ones included, and the page needs no change for it. `WidgetExportEndpoints` normalizes and validates it as a saved config (prefix `config.`), and the builder re-queries through `WidgetQuery.For`.
- **MCP.**
  - The widget tools take `WidgetConfigInput` as their schema, `display` included (`McpToolCatalogTests` reads it from `tools/list`). The `display` property's own description (on `WidgetConfigInput.Display`) lists what it holds: its color, scale, orientation, the names of its columns, and its subtitle, footnote, caption, unit and description button.
  - The display rules live in `DisplayHelp`, in `ContextHelp` (the texts, said once after `DisplayHelp`, whose pinned sentence stays) and in `ChartTypeChangeHelp` (`Mcp/McpDashboardTools.cs`), in the `create_widget` and `query_indicator` templates, and in `Mcp/Instructions.md` ("Colors" and "Words on a widget"). `get_widget_data` returns, beside the rows, `labels`, `subtitle`, `footnote` and `caption` when a widget has them.
  - Pinned sentences, in `McpToolCatalogTests`: `Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` (the chart type warning, and "config.display = { color, ramp, orientation, labels } sets how the widget is drawn"), `The_widget_tools_name_the_texts_of_a_widget_and_the_dashboard_tools_name_its_source`, `The_tools_name_the_column_labels_they_take_and_return` (the `create_widget` and `query_indicator` templates) and `The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` (display fields described, no enums). In `IndicatorDocsTests`: the chart type sentences of `docs/mcp.md`.
  - The result serializer leaves nulls out (`Mcp/McpResultBudget`).
- **The widget form** rebuilds the config from its signals (`formConfig` in `indicators/widget-dialog.ts`), with `displayDraft()` for the display, which carries the labels and the five texts (a text goes through `singleLine`, which turns a line break into a space). `setOrientation` stores the default only when the widget already had one saved, so a form saved unchanged keeps the stored text. `fieldFor` maps the API's error keys to fields by substring, in order, with the `display.` checks first (`config.display.subtitle` holds `title`).
- **Shared fixtures.** `indicators/testing/series-tables.json` is read by `series-table.spec.ts` and by `SeriesTableTests`, and `indicators/testing/column-labels.json` by the tests of `plotted` and `Plotted`. `add-indicator-column-metadata` created the production-shapes fixture (`indicators/testing/production-shapes.json`, run by `ProductionShapesTests` and `production-shapes.spec.ts`, its D13) and `add-widget-context` extended it: it holds 20 shapes (15 of the study's structure, then 5 with texts) and the entries `dashboard` and `dashboardWithSource`. Its goldens are written from the code before the program and no later change regenerates one. Both runners pin the number of shapes, and the API one also the list of their chart types (`ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once`, and `toHaveLength(20)` in the spec).
- **A sibling flag.** `improve-stat-cards` (later in the program) also derives a query flag, `unfiltered`, for its "without filters" figure. Its design D3 puts that flag on `WidgetConfigInput`, set by `queryFor` and `WidgetQuery.For`, where D4 below puts this change's `overall` on `IndicatorQueryRequest` and rejects that form. The user settled on 2026-11-04 that both travel as request flags, so `improve-stat-cards` is rebased to put `unfiltered` on `IndicatorQueryRequest` like `overall`.

## Goals / Non-Goals

**Goals:**
- Three optional display fields that change nothing for a widget without them: stored text, query, request body, response, email, workbook and MCP output all stay byte for byte.
- One rule for each line's value, computed the same way on screen, in the email, in the workbooks and in MCP.
- The overall value from the engine with the stat tile's own statement, so the line and a tile with the same filters always show the same number.

**Non-Goals:**
- Lines from another indicator or another statistic, bands, and vertical lines on a line chart's category axis (P2 `add-reference-lines-from-indicators`). The record of a line is designed so that P2 can add kinds and fields to it.
- Making single-series marks keyboard stops. That is `add-small-sample-warnings`' tooltips work. A reference line is never a keyboard stop.
- Changing any order, color or number format.

## Decisions

### D1. Three display fields, and how they are stored

- **`display.referenceLines`**: a list of `WidgetReferenceLine(Kind, Value, Label)`, in the order the author gave.
  - `Value` and `Label` are `WhenWritingNull`, so a mean line is stored as `{"kind":"mean"}`.
  - The kinds are string constants, `ReferenceLineKinds` (`mean`, `median`, `overall`, `value`).
  - The limits are `WidgetDisplay.MaxReferenceLines = 4` and `WidgetDisplay.MaxReferenceLineLabel = 40`, beside the text limits of `add-widget-context`.
  - `Value` is a `double?` in the indicator's units. For a `percent` indicator that is the fraction the engine plots: 0.5 is 50 %. In a 100 % layout it is a share from 0 to 1.
- **`display.interpolation`**: `"linear"` or `"step"` (`LineInterpolations`). Left out, it is linear.
- **`display.dataLabels`**: a `bool?`. Left out, data labels are off.
- **Normalize.**
  - An empty list of lines is no list (null). A line's label is trimmed, and a blank one is none. A null line, or a line with an empty kind, is kept, so validation can name it, as `WidgetConfigInput.Normalize` keeps a null tooltip item.
  - An empty interpolation is none.
  - A saved default is kept as given: `"linear"` and `false` are never rewritten to null, so a form saved unchanged keeps its stored text (box plot D1, program rule 3). Unlike the texts and `showDescription` of `add-widget-context`, whose default is to be absent, these two fields have a default a form can store, so they follow the box plot's rule.
  - `WidgetDisplay.Normalize()` returns null only when every field is unset: color, ramp, orientation, labels, the five texts of `add-widget-context`, and these three.
- **Order of the fields.** The new members are appended after `ShowDescription`, the last member of the record, so `Color`, `Ramp`, `Orientation`, `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription` keep their place in the JSON. Both sides of every comparison serialize the same record, so the order never makes a version.
- **TypeScript.** The mirror types are `ReferenceLineKind`, `WidgetReferenceLine { kind; value?; label? }`, `LineInterpolation`, `WidgetDisplay.referenceLines?`, `interpolation?` and `dataLabels?`, and the constants `maxReferenceLines` and `maxReferenceLineLabel`, next to `maxSubtitleLength` and the other limits of `indicators.service.ts`, which a unit test reads from the page's copy as `ColumnMetadataTests` reads the limits of the labels and the texts. The new members come after `showDescription` in `WidgetDisplay`.

*Alternatives:*
- *Top-level config fields.* Rejected: they would enter `queryFor`, `WidgetQuery.For` and the preview's key, so every label edit would query again. `display` is the documented place for what never changes the rows. The one value that needs the engine, `overall`, gets a request flag of its own (D4).
- *A separate `annotations` object next to `display`.* Rejected: it would need its own `WhenWritingNull` member, its own normalization, its own tool texts and its own `displayFor`, and buy nothing.
- *The value of a percent indicator as the number typed (50).* Rejected: every other number of a percent widget is a fraction (the rows, the filters' ranges, MCP `value`). The form converts what is typed (D8).

### D2. Validation (`IndicatorValidation.CheckDisplay`)

The per-kind rules are skipped when the chart type is null, as the existing ones are. `i` is the line's index, and every key takes the caller's prefix (`config.` on the widget endpoints).

| Rule | Key | Message |
|---|---|---|
| More than 4 lines | `display.referenceLines` | "A widget has at most 4 reference lines." |
| A null line | `display.referenceLines[i]` | "Each reference line needs a kind." |
| A kind that is not one of the four | `display.referenceLines[i].kind` | "The kind must be 'mean', 'median', 'overall' or 'value'." |
| A second `mean`, `median` or `overall` line | `display.referenceLines[i].kind` | "The {kind} line is listed twice." |
| A `value` line with no value | `display.referenceLines[i].value` | "A 'value' line needs its value." |
| A value on another kind | `display.referenceLines[i].value` | "Only a 'value' line takes a value." |
| A label longer than 40 characters (UTF-16 units after trimming, as `CheckText` counts a widget's texts) | `display.referenceLines[i].label` | "A reference line's label has at most 40 characters." |
| A control character in a label (`char.IsControl`: a NUL, which `jsonb` cannot hold, a line break, a tab) | `display.referenceLines[i].label` | "A reference line's label cannot contain line breaks or other control characters." |
| Lines on a kind that is not bar, column or line | `display.referenceLines` | "Only bar, column and line widgets have reference lines." |
| `mean` or `median` with a series | `display.referenceLines[i].kind` | "A mean or median line needs a chart with no series." |
| `overall` with a measure (no ratio) | `display.referenceLines[i].kind` | "An overall line needs a ratio: over a measure it would be the sum of every bar." |
| A `value` outside 0 to 1 in a 100 % layout | `display.referenceLines[i].value` | "In a 100% layout a value is a share from 0 to 1." |
| An interpolation that is not `linear` or `step` | `display.interpolation` | "The interpolation must be 'linear' or 'step'." |
| An interpolation on a kind that is not line | `display.interpolation` | "Only a line chart has an interpolation." |
| Data labels on a kind that is not bar, column, line or pie | `display.dataLabels` | "Only bar, column, line and pie widgets have data labels." |
| `dataLabels: true` with a stacked or 100 % layout | `display.dataLabels` | "Stacked bars have no data labels." |

- The control-character rule is the one `add-indicator-column-metadata` and `add-widget-context` give their texts (`CheckText`): a label is drawn on one line in SVG, an HTML email and an Excel cell. The form turns a line break into a space, as it does for the texts, so only an API or MCP client is refused.
- Stacked and 100 % layouts need a measure (the series rules of `IndicatorValidation` refuse a ratio there: "Stacked bars add their values, so they need a measure, not a ratio."), so an `overall` line on them gets the ratio message.
- `dataLabels: false` stays accepted on a stacked bar, so a saved default never blocks a layout change.
- **The overall line over a measure is refused**, decided with the user on 2026-11-04, although the program's sketch said "measure or ratio":
  - a measure is a `count` or a sum with no numerator and denominator, and a stat tile over it is the sum of every row, so on a bar chart of sums the line would sit above every bar and read as nothing a reader compares a bar with;
  - the figure a reader wants there is a Total bar next to the bars, which P2 `add-normalization-and-totals` adds (`includeTotal`) for measures and ratios alike;
  - the form never offers `overall` over a measure (D8), and the API refuses it with the ratio message of the table above.

  *Alternative:* *drawing the sum of every bar as the overall line over a measure.* Rejected with the user: it is off the scale of the bars and is not a value any bar is read against.
- `CheckAgainstColumns` needs nothing new: a line names no column.

*Alternatives:* *ignoring a line the widget cannot take instead of refusing it.* Rejected: every display setting is refused where it does not belong (program rule 5), so an MCP client learns of a mistake at once. The form never sends one (D8).

### D3. The value of each line, and its twins

- **`mean`** is the sum of the values drawn, added in the response's order, divided by their count.
- **`median`** sorts a copy of the values. With an odd count it is the middle value, and with an even count the mean of the two middle values (pandas' `median`).
- **The values drawn** are the response's `rows` of a widget with no series, in the response's order (a line chart draws them in natural order, which does not change the result): one per bar or point. The reader has already left out non-finite values, and when the row limit is reached the rows are the bars drawn. The "(none)" group is a row like any other. "Other" never appears without a series, and mean and median lines are refused with one.
- **`overall`** is the response's `overall` for a widget with no series (D4), and `totals.all.value` for one with a series.
- **`value`** is the number as saved.
- **A line with no value** (no rows, or an overall whose denominators sum to 0) is left out everywhere: the plot, the key, "Show as table", the email and MCP.
- **Twins.**
  - UI: `referenceLineValues(type, config, answer)` in a new `indicators/reference-lines.ts`.
  - API: `ReferenceLines.Of(chartType, config, response)` in a new `Indicators/ReferenceLines.cs`.
  - Both return the drawable lines in the widget's order: `{ kind, label (null for the default), value }`.
  - Both are tested against one fixture, `indicators/testing/reference-lines.json`, read by `reference-lines.spec.ts` and by `ReferenceLinesTests.cs` as `SeriesTableTests` reads `series-tables.json`. Its cases: four values (0.40, 0.50, 0.60, 0.20 give 0.425 and 0.45), three values, one value, a "(none)" row, an overall from `overall` and from `totals.all`, an overall of null, a value line, the widget's order, and values whose sum depends on the order of addition.
- **Default labels.**
  - UI: `indicators.referenceLines.kinds.mean|median|overall|value`, "Mean", "Median", "Overall" and "Reference" in English, and "Promedio", "Mediana", "Valor general" and "Referencia" in Spanish.
  - API: the `ReportText` keys `referenceMean`, `referenceMedian`, `referenceOverall` and `referenceValue`, with the same words.
  - MCP: the English words.
- **Text, with the unit**, decided with the user on 2026-11-04. A line reads `{label}: {value}`. The value is written by the widget's formatter, the one its tooltips and table use, so it carries the indicator's unit as they do: "Mean: 34.17% de los hogares" over `teatro_hoy` in an American English browser, "Promedio: 34,17 % de los hogares" in a Colombian Spanish one (`es-CO`), "Reference: 100 hogares" over `teatro_conteo`. That is the widget's own unit when it has one (`withWidgetUnit`, `MetaOf`). A `value` line of a 100 % layout is written as a share, as that layout's axis and cells are ("Reference: 50%"). The same text is used at the line, in the key, under "Show as table", in images, in the email and in the workbooks. When the text does not fit at its line, it is left out there and the key still lists it (D5).

*Alternatives:*
- *Mean and median in SQL, over the table's rows.* Rejected: the lines summarize what the chart draws, which the response already holds. SQL would add a statement to every such widget, and would summarize table rows, not bars. A median of unit values is the box plot's job.
- *A mean weighted by each bar's base.* Rejected: that is the overall line. The user asked for the mean of the bars and for the overall value as separate kinds.
- *The value without its unit at the line, as an axis tick writes it.* Rejected with the user: it would be a third way of writing a value next to the tooltip's and the axis tick's, and "34,17 %" alone does not say of whom. A text that does not fit is left out at the line, and the key always lists it (D5).
- *Shortening a text that does not fit, or moving it away from its line.* Rejected with the user: a cut text can read as another value, and a text away from its line can be read against the wrong line. The key is the one place that always holds every line whole.

### D4. The overall value from the engine

- **The flag travels on the query request, never in a config.** `IndicatorQueryRequest` gains `bool? Overall` (`WhenWritingNull`, appended last, with a `Description`). `WidgetConfigInput` and `WidgetConfig` do not change, so no widget's stored config, no widget tool's schema and no `queryFor` output can hold it.
- **Who sets it.** Two twins decide it from the saved config:
  - API: `WidgetQuery.Overall(chartType, config)` is true for a bar, column or line widget with no series and a ratio, whose display has an `overall` line;
  - UI: `overallFor(type, config)` in `widget-view.ts`, under the same rule;
  - their shared cases go in `query-for.spec.ts` and `WidgetQueryTests.cs`, as program rule 2 asks of the query twins.
- **Who sends it.**
  - The page: `IndicatorsService.query` gains an optional last argument and sends `overall: true` only when it is true, so every other request body is byte for byte today's. The view's key adds `overall: true` only then, so the key of every widget without such a line is unchanged.
  - The email, the widget workbook and `get_widget_data`: `QueryAsync` gains an optional `overall` argument, and `ReportDataBuilder.ReadWidgetAsync` and `McpDashboardTools.WidgetDataAsync` pass `WidgetQuery.Overall(widget.ChartType, widget.Config)`. The widget workbook gets the saved config, display included, through its request and builds its block with the same `ReadWidgetAsync`, so the same call covers it.
  - The query endpoint passes `request.Overall == true` (`IndicatorEndpoints.QueryAsync`).
  - MCP `query_indicator` previews a config given whole, display included. When a chart type is given, it sets `Overall` from `WidgetQuery.Overall` over `config.Normalize()`. It takes no `overall` argument of its own.
- **The statement.**
  - `QueryAsync` passes `overall` to `Plan` as a new argument.
  - On the plain grouped path only (not a box plot, not a series, not per-row, not a stat tile), `Plan` adds `IndicatorQueryPlan.Overall`, a new last member of the record after `Boxes` and a second statement beside `Sql` as a pie's `Domain` is: the plan of the same config as a stat tile would be planned (no group, no order), that is `SELECT NULL::text, ({aggregate}), NULL::text FROM {from} WHERE {rowFilter}{filters} HAVING ({aggregate}) IS NOT NULL LIMIT …`, with the widget's and the dashboard's parameters. It is built by the stat tile's own code path, not written again, so a test can compare the two texts.
  - It runs after the rows, in the same read-only transaction and slot, under the same 25-second statement timeout. Its first row's value is the overall, and no row, or a non-finite value, is null.
  - The main statement stays exactly as it is, so the pinned SQL (`IndicatorQueryBuilderTests.A_grouped_query_that_is_not_a_map_has_no_extra_outputs`) holds.
- **The response.** `IndicatorQueryResponse` gains `double? Overall` (`WhenWritingNull`), appended last. Only a query that asked for it carries it, so every other response keeps its bytes (`IndicatorQueryTests.Every_other_kind_answers_what_it_answered_before_with_no_domain`).
- **With a series** nothing is new: the `overall` line reads `totals.all.value`, and the flag is not sent.
- **Forward rules for later changes of the program.** Because the overall statement is the stat tile's statement, whatever a later change does to how a stat tile adds up its rows applies to the line too: `add-small-sample-warnings`' `missingFooter` leaves the same "Sin dato" rows out of it, `improve-stat-cards`' per-answer base rule for a declared `single` column (its D4a) applies to it, and `add-weighting` weights it. `improve-stat-cards` already names the overall statement in its design and tasks; the others name it in theirs when they are rebased.

*Alternatives:*
- *A stored `overall: true` next to the display.* Rejected: two sources of truth. MCP could set one without the other, and validation would have to pair them.
- *`overall` inside `WidgetConfigInput`, added by `queryFor` and `WidgetQuery.For`.* Rejected: `WidgetConfigInput` is the schema of `create_widget`, `update_widget` and `query_indicator` and of the widget endpoints in OpenAPI, so the flag would show there as a setting that saving silently drops; `savedConfig`, which spreads `queryFor`, would have to strip it again. `improve-stat-cards` first planned that form for its `unfiltered` flag (its design D3); the user decided on 2026-11-04 that both flags travel on the request, and that change follows this one.
- *Computing it in the browser from the rows.* Rejected: the rows are already ratios per group. A ratio of sums needs the sums of the numerator and of the denominator, which the response does not carry, and the row limit can cut bars.
- *A grand-total row added to the plain statement with `UNION ALL`.* Rejected: it changes the pinned statement and every reader that reads it by position, for every bar chart, to serve the few that ask.

### D5. Drawing reference lines

- **The input.** `plain()` and `seriesPlot` take `references: { value, text, dash }[]`, built in `AxisChart` from D3's values and texts. `dash` is the line's place in the widget's list.
- **The axis.** The value axis includes every line's value: `low` and `high` take the minimum and the maximum with them, before `niceAxis`, in `plain()` (`axis-chart.ts`) and in `seriesPlot` (`series-plot.ts`). A 100 % layout keeps its 0-to-1 axis, which D2 keeps every value inside.
- **The line.**
  - It crosses the plot from edge to edge: vertical at `x(value)` on a bar chart, and horizontal at `y(value)` on a column or line chart.
  - It is 1.5 px, in the existing `stroke-heading` utility (the box plot's ink), with a dash pattern by place: `6 4`, `2 3`, `8 3 2 3`, `12 4`.
  - It is drawn after the bars, lines and dots and before the hit areas, so pointing and the keyboard work as before.
  - On a chart with no series it carries a `<title>` with its text, as the marks do.
- **The label at the line.**
  - It reads `{label}: {value}` in 11 px `fill-heading`, with a 3 px halo in the surface color (`stroke-neutral-primary-soft`, the SVG attribute `paint-order="stroke"`), so it reads over bars.
  - On columns and lines it is end-anchored at the plot's right edge, 4 px above the line. On bars it sits at the top, just right of the line, and flips to the left when it would pass the plot's edge.
  - A label wider than the plot, or whose box would overlap a label already placed, is not drawn: it is never cut or moved away from its line (D3, decided with the user on 2026-11-04). The key still lists its line with the whole text.
  - While a chart has reference lines, its top margin grows by one 12 px text line, so a label near the top stays inside the box.
- **The key.**
  - A `ReferenceKey` component next to `SeriesLegend` (`indicators/series-legend.ts`) lists every line drawn under the plot: a 16 × 4 px inline SVG swatch with the line's dash, then its text.
  - Entries wrap on narrow widgets. The key is a child of the chart's column, which has an 8 px gap between its children, so the first row takes 24 px from the plot (8 px of gap and 16 px of row), and each further row 20 px (4 px between rows and 16 px of row).
  - It is a sibling of the plot box in the chart's column, like the caption (`shrink-0`), so it takes its room before the plot does. The plot box takes what is left, and a chart that does not fit it is cut at its own box (`add-widget-context` D5), never the key. The widget's footnote sits at the foot of the tile, after the chart, so the key is above it.
  - The room is what `add-widget-context` measured at 1,280 px: a 6 × 4 tile has a plot area of 233 px, 199 with a subtitle and a one-line footnote and 159 with a two-line footnote. One row of key leaves 135 px of the last and two rows 115 px, so a single row is above the chart's least height of 120 px and a second row is below it: the chart is then cut at its box (a plot of 199 px keeps 175 px with one row and 155 with two). A 4 × 3 tile with texts already keeps 105 px of plot without a key (`add-widget-context`'s record), so there the chart is cut and the key stays whole. The cut is made at the chart's own box (`overflow: clip` as an inline style on the box while a key follows, so the stylesheet gains no utility, with a 4 px `overflow-clip-margin` that keeps the focus ring and stays inside the 8 px gap), so the plot's labels and grid never paint over the key. Task 12.4 measures it, and the form's warning for crowded texts (`textsCrowd`) does not count the key.
  - A series legend stays above the plot.
  - A chart without lines renders no key element, so its plot keeps its height and its marks their places.
- **For screen readers.** The chart's accessible description (`summary` in `axis-chart.ts`) gains `indicators.referenceLines.summary`: "Reference lines: Mean 34.17%, Overall 45.99%." The key is visible text.
- **No new tokens or utilities.** The initial bundle does not change, and the code lands in the lazy dashboard chunk.

*Alternatives:*
- *A palette color per line.* Rejected: palette colors mean series and values, so a colored line could read as a series or a kept value. Ink with a dash never collides with a kept color.
- *Labels only in the key.* Rejected: the user asked to see "Promedio: 46,9 %" at the line, as in printed reports. The key is the fallback when labels collide.
- *Lines as keyboard stops among the marks.* Rejected: it would change the keyboard model of every series chart. The values are in the key and the accessible description.

### D6. Step lines

- **The path.** `stepPath(points)` in `chart-scales.ts` writes `M x0 y0 H x1 V y1 H x2 V y2 …`: step-after, so a value holds from its point to the next point's x, then moves straight to the next value. The last point ends the line.
- **Where.** `plain()` (`axis-chart.ts`) and `linePlot` (`series-plot.ts`) call it in place of `linePath` when `display.interpolation` is `step`. The runs keep their gaps, and dots and hit areas do not move.
- **No query.** It is a display setting, so switching it redraws without querying.

*Alternatives:*
- *Step-before or step-middle.* Rejected: a demand curve gives the share that would pay at least that price, which holds from one price to the next. That is step-after.
- *A numeric x axis, so steps have the prices' real widths.* Not in this change: the line chart's category axis stays, as P2 `add-reference-lines-from-indicators` scopes axis work.

### D7. Data labels

- **The text.** Each label is the widget's formatter applied to the mark's value, the same text as its tooltip and its table cell. It is 11 px `fill-heading` with D5's halo and `aria-hidden="true"`, since the table and the tooltips carry the values for assistive technology.
- **Plain bars.**
  - The label sits 4 px past the end of the bar, `start`-anchored, or `end`-anchored left of a negative bar.
  - The plot's right edge moves in by the widest label (`characters × charWidth + 4`), so the longest bar's label fits.
  - Labels are not drawn when the bars are thinner than 11 px.
- **Plain columns.**
  - The label sits 4 px above the column's end, or below a negative column. The top margin grows by 14 px.
  - A label wider than the band's step is not drawn.
- **Grouped bars and columns.** One label per bar, under the same rules, measured against its slot in `seriesPlot`. Grouped columns are at most 24 px wide, so their labels often do not fit, and the docs say horizontal bars suit them better.
- **Lines.**
  - The label sits 8 px above each point. The labels are placed series by series and point by point, left to right, and one whose box would overlap a placed label is not drawn.
  - The same holds with steps, and on a line of more than 40 points, which draws no dots.
- **Pie slices.**
  - The label sits at the slice's middle angle, at 0.62 of the radius.
  - It is not drawn when the chord across the slice at that radius is shorter than the text plus 4 px.
  - A pie of a single slice puts its label in the center.
- **Print.** Paper mode measures again, so the rules run again for the paper size.
- **Nothing changes without them.** The margins grow only while data labels are on, so a chart saved without them keeps its layout.

*Alternatives:*
- *The share on a pie slice.* Rejected for now: the program asks for the value, and the legend beside the pie already gives each slice's share. An option for it can come later (Open Questions).
- *Labels inside the bars in a contrasting ink.* Rejected: it needs a contrast rule for each palette color in both themes. A label on the surface reads in either theme.
- *Compact numbers ("$1,2 M").* Left to `add-locale-formatting`, which adds `display.compact` for data labels.

### D8. The widget form (`widget-dialog.ts`)

- **Reference lines.**
  - A fieldset for bar, column and line widgets. Each line is a row: a kind select, a value input (only for `value`), a label input whose placeholder is the default label, and a remove button. "Add a reference line" is disabled at 4.
  - The kinds offered follow D2. With a series there is no mean or median. With a measure there is no overall. Stacked and 100 % bars get only `value`.
  - The rows live in a `referenceLines` signal seeded from `start.display?.referenceLines`.
  - A label goes through `singleLine`, as the texts do, so a pasted line break is saved as a space.
- **The value input** shows the indicator's units.
  - For a `percent` indicator, and for the share of a 100 % layout, it takes a percent with a "%" suffix (50) and stores the fraction (0.5). The conversion moves the decimal point in the typed text instead of dividing, so 33.3 is stored as 0.333 and not as 0.33299999999999996.
  - The form keeps a saved number as it is until the input is edited.
- **The help under the kind select** says:
  - what each kind is: the mean or median of the bars drawn, the indicator's value over all the widget's rows as a stat tile with the same filters shows it, a fixed value;
  - for `overall`: "When the bars are the answers of one question, such as places or reasons, they share one base, and this line is only their mean, not a total. Use it when the bars are groups of households, such as comunas or estratos."
- **Steps.** A radio pair, "Straight" or "Steps", for line widgets. `setInterpolation(value)` stores `'linear'` only when the widget already had an interpolation saved, as `setOrientation` does.
- **Data labels.** A checkbox, "Show the values on the chart", where D2 allows them. `setDataLabels(false)` stores nothing unless the widget had `dataLabels` saved.
- **`displayDraft()`** carries the three fields next to those it carries (color, ramp, orientation, labels and the texts). A widget that sets none of them still saves no `display`.
- **`displayFor(type, config)`** keeps only what the kind, the series and the layout take, and keeps the labels and the five texts on every kind as it does.
  - It drops lines of a kind that is not offered, and data labels on stacked and 100 % layouts.
  - The form state keeps them while the member changes the kind or the series back and forth, as it keeps a color today.
  - A note under the fieldset says which lines will not be saved and why.
- **Preview.** Display edits redraw the preview without a request. Only `overallFor` enters the key (D4), so adding or removing an `overall` line on a widget with no series queries once. With a series the line reads `totals.all.value` and nothing is queried.
- **Errors.** `fieldFor` gains, in its block of `display.` checks ahead of `seriesby`, `layout` and `title`, `display.referencelines[i].kind|value|label` mapped to that row's inputs (the row from the index, as `stepFor` reads a scale step), and `display.referencelines`, `display.interpolation` and `display.datalabels` mapped to their fieldsets.
- **Strings.** New `indicators.dialog.referenceLines.*`, `indicators.dialog.interpolation.*` and `indicators.dialog.dataLabels.*` in `en.json` and `es.json`, with `indicators.referenceLines.*` for the chart.

### D9. "Show as table", images and print

- **"Show as table"** lists the lines under the table, one per line in the widget's order (`ReferenceList`, beside `ReferenceKey`), both under a `TableModel` and under a cross table, above the widget's footnote, which the tile keeps at its foot. The table itself is unchanged.
- **Images.**
  - `ExportSwatch` gains `'dashed'`, with an optional `dash` index on the legend entry. `image-layout.ts`'s `swatch` draws a 14 px line with that dash in the heading color.
  - `AxisChart`'s parts list the series entries, if any, then one entry per line drawn. A chart with no series keeps its caption and gets a legend of its lines. A chart without lines registers the parts it registers today.
  - While the chart is shown as a table, the lines' texts go in its notes, which `composeImage` draws after the table's own notes and before the footnote and the footer. With the chart, the legend of the lines is under the plot, above the notes, the footnote and the footer.
  - The plot SVG already holds the lines, the labels, the steps and the data labels, so the picture needs nothing else.
- **Print** is the same DOM. Paper mode measures again, so the labels and data labels are laid out for the paper size and the key prints whole, taking its room before the plot does (D5). The tile never grows: what the key and the texts need beyond the tile is cut by the tile (`dashboard-printing` "Widget context on paper").

### D10. Emails and workbooks (`Reports/`)

- **The model.** `ReportReferenceLine(Kind, Label, Value, Text)`, and `ReportWidget.ReferenceLines`, empty by default, appended after `CaptionHidden`, the last member today, so every existing construction stays valid.
- **Building it.** `ReadWidgetAsync`, the method that queries, passes `WidgetQuery.Overall` to the query (D4) and fills the lines where it builds the table of a bar, column or line widget that is shown, with a series (the cross table's branch) or without (`AxisTable`). `BuildWidgetAsync` goes on adding the texts, and a widget that is not shown has no lines:
  - the values come from `ReferenceLines.Of(...)`;
  - the label is the widget's own, or the `ReportText` default;
  - the text is `format.Value(value, meta.Format, meta.Unit)` (`meta.Unit` is the widget's own unit when it has one, from `MetaOf`), or the share format for a 100 % layout.
- **HTML body.** After the table and its "more rows" or "more columns" line and before the widget's footnote, which stays last, one muted paragraph per line, "Mean: 34.17% de los hogares". The plain text writes one line per reference line in the same place.
- **The email workbook.** Its "Summary", after the widget's subtitle and footnote items and before its notes, gains one item per widget with lines: `"{referenceLines}: {title}"` and the texts joined by " · ". Its widget sheets do not change.
- **The widget workbook.** Its "Summary" gains a `referenceLines` item after "Plotted", with the same text. Its data sheet does not change.
- **Strings.** `ReportText` gains `referenceLines` ("Reference lines" / "Líneas de referencia") and the four default labels, in both languages. `ReportTextTests.Both_languages_have_the_same_keys` holds them to parity.
- **Size.** A few short lines per widget. The 80 KB budget and the 10 rows per block are unchanged.

### D11. MCP (`Mcp/`)

- **A new constant, `AnnotationsHelp`,** follows `ContextHelp` in the descriptions of `create_widget` and `update_widget`, as `ContextHelp` follows `DisplayHelp`; the pinned sentence of `DisplayHelp` stays as it is. It says:
  - "referenceLines is up to 4 lines on a bar, column or line: [{ kind, value, label }] with kind mean or median (of the bars drawn; not with a seriesBy), overall (the ratio's value over all the widget's rows, as a stat tile with the same filters shows; needs a ratio; over the answers of one question it is only the mean of the bars) or value (a number in the indicator's units, 0.5 for 50%; the only kind with a value; in a percent layout a share from 0 to 1); label is optional, 1 to 40 characters.";
  - "interpolation is linear (the default) or step, for a line only.";
  - "dataLabels true writes each value on a bar, column (no seriesBy or layout grouped), line or pie.".
- **`ChartTypeChangeHelp`** names "a color, a scale, an orientation, reference lines, steps or data labels" and the keys `config.display.referenceLines`, `config.display.interpolation` and `config.display.dataLabels`.
- **The templates.** The `create_widget` template's `display` lists the new keys after `showDescription`: `display: { color, ramp, orientation, labels, subtitle, footnote, caption, unit, showDescription, referenceLines, interpolation, dataLabels }`. The `query_indicator` template's `display: { labels }` gains `referenceLines`, since a preview needs the lines to return them.
- **Pinned sentences.** `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale`, `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `…gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do` change in the same task as `ChartTypeChangeHelp`. The template assertions of `The_tools_name_the_column_labels_they_take_and_return` (the `create_widget` and `query_indicator` templates) change with the templates. `DisplayHelp`'s own sentence, "config.display = { color, ramp, orientation, labels } sets how the widget is drawn", does not change.
- **Schema.** `Description` attributes on `WidgetDisplay.ReferenceLines`, `Interpolation` and `DataLabels`, and on `WidgetReferenceLine`'s fields, name the values in lower case with no enum, as `McpToolCatalogTests.The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` requires. The description of `WidgetConfigInput.Display` ("How the widget is drawn and what it says"…, whose words that test pins) names the new settings too. No tool gains an argument: `IndicatorQueryRequest.Overall` is not part of any tool's schema.
- **The result.** `query_indicator` and `get_widget_data` add `referenceLines: [{ kind, label, value, formatted }]` from `ReferenceLines.Of`, with the English default labels, formatted as the result's own values are (the indicator's unit in `query_indicator`, the widget's own in `get_widget_data` as `mcp-server` "Widget context through MCP" says, a share for a 100 % layout). In `get_widget_data` it goes in the shown result, beside `labels`, `subtitle`, `footnote` and `caption`. It is null, so left out, for a widget without lines. It is a short list of fixed-size items, so the result budget needs no change.
- **`Instructions.md`.**
  - The "Colors" bullet on `display` and the section "Words on a widget" stay as `add-widget-context` left them (the first names `{ color, ramp, orientation, labels }`, the second the texts).
  - A new "Choosing a widget" bullet: "A line to compare the bars with: `display.referenceLines` with `mean`, `median`, `overall` or `value`. Use `overall` when the bars are subgroups (comuna, estrato); over the answers of one question it is only their mean. A demand curve: a `line` with `display.interpolation` `step`. `update_widget` replaces the whole `config`, so send `referenceLines`, `interpolation` and `dataLabels` again when you change the query."
  - `McpGuideTests` gains a case for the bullet.
  - `AssistantPrompts.Version` does not change: it versions the script assistant's prompt kit (`Prompts/system.md`, the message words and `Examples/**`), which this change does not touch.

### D12. `overall` over the answers of one question

The production study shows the trap: a bar chart grouped by `categoria` of a long-format table (Teatro 9b11 "En qué lugares") has categories that share one denominator in every cell. Its overall value, `sum(num) / sum(den)` over every category, is then the mean of its bars, which reads like a total but is not.

- **In this change** the form's help (D8), `docs/indicators-dashboard.md` and the MCP texts (D11) say so, and the spec scenario "Answers of one question" pins the behavior. Nothing is refused, because nothing yet declares which columns are the answers of one question.
- **Forward rule for `add-small-sample-warnings`**, which adds the column metadata `categories: "single" | "multiple"`:
  - a widget whose group column (or series column) is declared that way draws no `overall` line, and says so in a note on screen, in images, in the email and in MCP: "No overall line: the bars are the answers of one question";
  - the form stops offering `overall` for it;
  - nothing is refused, so a widget saved before its indicator gains the declaration never becomes invalid when a script's metadata changes.

  `add-small-sample-warnings` carries it (its D16 and its requirement "Answer columns without an overall line", with one more argument for the twins `referenceLineValues` and `ReferenceLines.Of`) and rebases on this change with `/opsx:update`. The Workflow follow-up of this change names it too.

### D13. Documentation

- **`docs/indicators-dashboard.md`.**
  - A section `### Reference lines, steps and data labels` under "Widget types": the four kinds and how each is computed, with the series and layout rules, the caution of D12 with the Teatro example, the dash patterns and the key, step lines for demand curves, data labels and when they are hidden.
  - The widget types paragraph names the three settings.
  - "Show as table", "Images" and "Workbook" (under "Exporting a widget") and "Printing a dashboard" each get a sentence, and "Words around a widget" ("Room") says that the key takes room from the plot as the texts do.
  - "Limits" gains "Reference lines in a widget: 4" and "Length of a reference line's label: 40 characters".
  - "What it does not do" gains: no line from another indicator, no bands, no line colors.
- **`docs/mcp.md`.** The display keys, the refusals with their keys, `referenceLines` in widget data, and the chart type sentence (D11).
- **`docs/operations.md`** "What leaves the deployment by email" adds the labels of reference lines to the free text that members write and that leaves with the email.
- **`docs/tamiza-brief.md`** section 6 adds `referenceLines`, `interpolation` and `dataLabels` to the `display` fields of `dashboard_widgets`, and section 8 marks this change done in the program's list (the list itself is written by `add-indicator-column-metadata`).

## Spec ownership

- **MODIFIED: `dashboard-indicators` "Line chart".** The program's ownership table gives it to this change. The edit adds one sentence (straight segments or steps) and one scenario. Every existing sentence and scenario keeps its text and name, and the block copies today's main text, which neither archived change touched. `add-declared-category-order` also modifies it, because its declared order contradicts "in their natural order"; it lands after this change and copies this block's sentence and scenario when it is rebased (its Spec ownership says so).
- **Everything else is ADDED**, under names that start with this feature's nouns ("Reference lines …", "Step lines", "Data labels on …", "Chart annotations …"), so no other change of the program collides with them:
  - "Dashboard versions", "Duplicating a dashboard" and "Moving a widget to another dashboard" belong to `add-indicator-column-metadata`, which rewrote their lists of settings generically (every setting a widget is saved with). "Chart annotations kept with the widget" states the behavior for these settings without touching them.
  - "Parts of a widget image" and "Summary of a widget workbook" were modified by `add-widget-context`, which also added "Widget context in a widget workbook", "Widget context in a scheduled email" and "Widget footnote in a scheduled email", and left "Widget notes in a scheduled email" as it was. This change adds "Chart annotations in widget images", "Reference lines in a widget workbook" and "Reference lines in a scheduled email" instead, and says in them where the lines sit relative to the subtitle, the footnote and the source: the legend or the notes of an image are above the footnote and the footer; the email's lines come after the table and its "more" lines and before the footnote; the email workbook's item comes after the subtitle and footnote items and before the notes; the widget workbook's item comes after "Plotted".
  - "Widget context in a tile" and "Widget context on paper" (`add-widget-context`) and "Charts on paper" (owned by nobody) already say that the texts take their room from the plot and that a chart which does not fit is cut at its own box. The key takes its room the same way, so none is modified: "Reference lines drawn" and "Chart annotations on paper" say where the key sits.
  - "Widget tables in a scheduled email" (`add-indicator-column-metadata`) is not changed: the tables keep their columns and rows, and the lines go under them.
  - "Bar and column charts of one indicator", "Marks of a widget with a series", "Widget query over one indicator" and "Indicator queries and widget data" belong to `add-small-sample-warnings`. "Pie chart" belongs to `add-declared-category-order`. "Indicator formatting" belongs to `add-locale-formatting`. None is contradicted: the lines and labels follow the indicator's formatting as written (the widget's own unit replacing the indicator's, which `add-widget-context` wrote into "Indicator formatting"), a series legend still lists the series, the overall value is a separate figure that does not change what a widget plots, and `query_indicator` still returns what the widget would plot.
  - "Dashboards and widgets through MCP" belongs to nobody, and the program says to ADD "<Feature> through MCP": "Chart annotations through MCP".
  - `dashboard-printing` "Charts on paper" already asks for "all its marks, labels and legend". It is not modified, to keep rebases trivial, and "Chart annotations on paper" adds the paper layout rule.
- **Requirements of earlier P1 changes** are referred to by name only (`add-widget-context`'s widget unit, its subtitle, footnote and source in images, emails and workbooks, and its workbook request; `add-indicator-column-metadata`'s production-shapes fixture), never modified.

## Production impact

From the production survey (program section 5):
- **No widget has the new fields.** None of the 190 widgets (58 column, 32 bar, 5 line, 1 pie) has them, so:
  - `queryFor` and `WidgetQuery.For` do not change at all, `overallFor` and `WidgetQuery.Overall` are false, and the request bodies and preview keys are the same;
  - the engine runs the same statements and answers the same JSON;
  - their stored configs keep their bytes, so a form saved unchanged makes no version;
  - their emails, workbooks and MCP results are the same.
- **The demand curves** (Teatro 233d and 3229; parqueadero 77ea and de78) stay straight until someone picks steps. Their group order is not touched by this change.
- **What this change does not touch:** the 27 sorts by `orden`, the 49 `in` filters, the colors kept by value, the palettes, the 2 `ignoredDashboardFilters`, the map's tooltip and basemap, the 13 ramps, the Likert scales and the 67 stat tiles.
- **The category bar charts** (9b11, be40, 3138, 84aa) have no lines today. The `overall` caution (D12) is for the moment someone adds one.
- **The count indicators** that feed stacked and 100 % bars (22 widgets) can take only `value` lines, and those are opt-in.
- **The production-shapes fixture** of `add-indicator-column-metadata`, extended by `add-widget-context` to 20 shapes, is extended again to 25 and run (tasks 1.4, 6.3). Every shape still normalizes to its stored bytes, builds the same query, plan and UI query, and saves unchanged from the form. New shapes with lines, steps and labels round-trip.

## Risks / Trade-offs

- **[An overall line over the answers of one question reads like a total]** → The form's help, the docs, the MCP texts and a spec scenario say what it is. D12 sets the rule `add-small-sample-warnings` adopts once the declaration exists.
- **[A later change alters how a stat tile adds up its rows]** → The overall statement is built by the stat tile's code path (D4), so it follows; a test compares the two statements, and D4's forward rules name the changes that must keep it so.
- **[The key and the data labels take room from a tile that already gives some to its texts]** → The key takes its room before the plot does and the chart is cut at its own box (D5); the measures of D5 say how many rows of key a 6 × 4 tile with two-line texts still holds. A tile 3 rows high or less, with texts and several lines, cuts the chart: task 12.4 records it, and the docs say that a taller tile gives the room back.
- **[Labels collide on small tiles]** → A colliding label, or one wider than the plot, is not drawn, and the key lists every line with its value while the tile has room for it (on paper a footnote that fills the tile clips the key too, as it clips the chart: see "Widget context on paper"). Data labels that do not fit are hidden, never drawn over each other.
- **[A value far from the data flattens the bars]** → It is the author's choice and shows in the preview. In a 100 % layout the value is held to 0 to 1.
- **[The two twins drift]** → Both value twins are tested against one fixture and add in the response's order, so even a floating-point sum is the same. `overallFor` and `WidgetQuery.Overall` share their cases.
- **[An older UI tab or an MCP client drops the fields when it saves the config]** → The same holds for every display field. The tool descriptions say to send `display` again, and the Migration Plan states the rollback rule.
- **[One more statement for each widget with an overall line]** → Only those widgets run it. It is a stat tile's statement, in the same slot and transaction, under the same timeout. No production widget has such a line today.
- **[The pinned MCP sentences are also edited by later changes of the program]** → Each change rebases with `/opsx:update` after the one before is archived (tasks, Workflow follow-up).
- **[Two default labels per language to keep in step, UI and email]** → `translations.spec.ts` and `ReportTextTests` check key parity, and a test on each side pins the four words in both languages.

## Migration Plan

1. **Deploy.** No migration and no data change. Every existing widget is drawn, queried, stored, emailed and returned to MCP as before.
2. **Rollback.**
   - An older API reads the three display fields as unknown properties of a config, and the `overall` flag as an unknown property of a query request: it ignores them and never refuses them.
   - Widgets that have them draw without lines, steps and labels. On that API's next save of such a widget, the fields are dropped from its stored config.
   - Versions recorded with the fields keep them in their content. Restoring one later, on a release that knows the fields, brings them back.
   - A newer UI against an older API gets no `overall` in the response and draws no overall line. Nothing needs cleaning up.
   - No enum value or operator is added, so nothing that an older release would refuse is ever stored.

## Open Questions

- Whether a pie's data labels should be able to show the slice's share instead of its value. The legend gives the share today, and an option can be added to `display` later without changing these rules.
- Whether a reference line should be able to take a color of the palette. They are ink today (D5). A color can be an optional field of the line record later.

## Verification record

**UI build (task 11.3), 2026-11-04** (`npx ng build`, `npm run check:safety` in `tamiza-ui/`):
- The initial total is 674.85 kB raw (158.46 kB estimated transfer): `main` 304.28 kB, the shared chunk 291.78 kB, `styles` 78.12 kB and a 675-byte chunk. The budget is a warning at 675 kB and an error at 1 MB, and the build raised neither. The margin to the warning is 0.15 kB, so any later growth of the initial chunk crosses it.
- None of this change's code is in an initial chunk (read from the build's `browser-stats.json`):
  - `widget-view.ts` and `widget-dialog.ts` are in the lazy `project-indicators` chunk, 367.90 kB raw (74.48 kB transfer);
  - `axis-chart.ts`, `chart-annotations.ts`, `reference-lines.ts`, `export-parts.ts` and `widget-table.ts` are in a shared lazy chunk of 61.23 kB (17.98 kB);
  - `image-layout.ts` and `svg-clean.ts` are in the lazy `widget-exporter` chunk, 16.00 kB (6.13 kB).
- `check:safety` is clean: no `innerHTML`, no number pipe, no stray `.toFixed(`. The form's percent conversion moves the decimal point in the text and does not round.

**Known limits, recorded after the adversarial review of 2026-11-04** (nothing here is changed by this change):
- **The halo of a label in an exported SVG depends on `paint-order`.** The labels at the lines and the data labels get their halo from a 3 px stroke in the surface color drawn under the glyphs with the SVG 2 attribute `paint-order="stroke"`. Browsers and the PNG export honor it. Some viewers do not (some Office and Illustrator import paths), and there the stroke would be painted over the glyphs. If that is ever reported, the fallback is a stroke-only copy of the text followed by a fill-only copy.
- **Two shared-fixture tests return silently when the UI folder is not found.** `ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` and the tests of `ReferenceLinesTests` find the fixture through the repository layout, as the other shared-fixture tests do, and pass without checking anything when the folder is not there (a build that has only the API). The UI specs read the same files, so a run of the whole repository checks both.
- **The initial bundle has 0.15 kB of margin under the 675 kB warning** (674.85 kB, unchanged by the review fixes, which add no stylesheet utility: the clip of the plot box is an inline style). Any later growth of an initial chunk crosses the warning (the 1 MB error budget is far away).

**Suites and real stack (tasks 12.1 to 12.6, 2026-11-04).**
- Suites: API 2417 unit tests pass and 2127 of 2129 integration tests pass (the two failures are `IndicatorSeriesQueryTests.A_ratio_*`, which fail on a clean tree too: decimal comma). UI (`TZ=UTC`) 3420 of 3422 (the two known polling tests of `project-data.spec` and `project-scripts.spec`; the unhandled `project-performance.ts` error of a clean tree also appears). Analytics 620 pass and `git status -- tamiza-analytics` lists nothing. `openspec validate add-chart-annotations --strict` is valid.
- Real stack (compose project `tamizaverify`, fake sign-in and Kobo, Mailpit, headless Chrome). Baseline: the verification record of `add-widget-context` (its Control dashboard emails and workbook), the same app with the settings added and removed again, and a Control dashboard no change touches; no HEAD build was made because the tree is mixed with other changes. All eleven checks pass:
  - The overall line equals a stat tile over the same ratio and filters (0.52 against a mean of the bars of 0.5152; 0.4978 with a dashboard filter) and no request without the flag answers with `overall`; adding or removing an overall line sends one query, editing any label sends none; nothing stores `overall`.
  - Room: a key row costs 24 px (8 px gap plus 16) and each further row 20 px. In a 6x4 tile (336 px) with a two-line subtitle and a two-line footnote the plot box is 159, 135, 115, 95 and 75 px with 0, 1, 2, 3 and 4 key rows; in a 4x3 tile (248 px) with a 500-character footnote it is 105, 81, 61 and 21 px with 0, 1, 2 and 4 rows. The chart is cut at its box (`overflow: clip` with a 4 px clip margin, inside the 8 px gap): a pixel test found nothing painted between the box and the key, the focus ring of a series chart shows, and a widget with no line has no style attribute on its box.
  - 1,280 and 390 px, light and dark: no horizontal scroll, no label outside its plot, halo readable, distinct dashes. Steps end at the last point and keep the gaps of a series. Data labels are dropped on thin bars, too-narrow columns, stacked and 100% layouts and where a negative bar or column would cover the axis text; pie labels are inside the circle; every data label is `aria-hidden`. The key is a list named "Reference lines" and the chart's description carries the same sentence.
  - The form offers the kinds its rules allow; a percent value is typed as a percentage (33.3 saves 0.333); `1.500` on a count is refused with the separator message; a form saved unchanged leaves the stored row and `updated_at` identical and makes no version (also for a series with a stored null layout); focus goes to the new row after an add.
  - Images keep the halo (`paint-order="stroke"` is retained in the exported SVG; computed `paint-order` is `stroke` in Chrome), list the lines in the dashed legend (as notes under "Show as table") above the footnote, and the widget workbook's Summary lists them right after "Plotted". The SVG and the workbook of a widget with its settings removed are byte-identical to what it exported before (export time and random ids masked).
  - MCP: every refusal names its key; `query_indicator` with a chart type returns the lines and none for a query with no rows; `get_widget_data` returns them; a widget without lines has no `referenceLines` key.
  - Emails in English and Spanish: the lines come after the table and its "more rows" line and before the footnote, with the default labels and the fixed value's own label, in the widget's unit; the email workbook's Summary item sits after subtitle and footnote. A Control dashboard sends the same HTML, text and workbook (random relationship ids aside) as before.
  - Compatibility: Control widgets and versions hold none of the new keys, the dashboard JSON is unchanged, the latest migration is still 0022, duplicates carry the fields and an old version restores without them.
- Findings from the real stack, none fixed here:
  - **On paper, a 4x3 tile with a 499-character footnote loses its plot and its key.** The footnote prints whole and takes the room; the same tile with no line already has no plot on paper (the case of `add-widget-context`). With a short footnote the key prints. The delta spec "Chart annotations on paper" said the key always lists every reference line; the user decided on 2026-11-04 to narrow the spec, as `add-widget-context` did for its texts: when the texts alone fill the tile, the tile clips the key and the chart with them (scenario "Key in a tile the texts fill").
  - **A fixed value of 0.5 on a count indicator reads "Reference: 1 visitas" in the chart and the key but "0 visitas" in the email and both workbooks** (the page rounds half up, `N0` rounds to even). Numbers that are not whole are rare for a count, and the difference is the one between the two formatters that every number of the product already has.
  - At 390 px a data label can touch a reference label (the overlap rule covers data against data and reference against reference only).
  - A pie in a 4x4 tile can drop every label (the reach rule) with no hint to the member.
  - After a server error the focus lands on the first field of the form, not on the invalid control.
  - Four long reference labels in a 6x4 tile leave a 75 px plot and no category axis; this is the accepted cost of D5.
- Not checked: a real screen reader, a real device (390 px is emulation), the dialog of Chrome's print, and a HEAD build for the baseline.
