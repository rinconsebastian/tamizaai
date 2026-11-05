# Proposal

## Why

A survey estimate is only as good as the number of people behind it, and a dashboard today never says that number. In the market study of the Duitama Bicentennial Center (476 households), Teatro's column chart "2.1. Asistió, por comuna" draws COMUNA 7 at 50 % with the same weight as COMUNA 1, although COMUNA 7 rests on 16 households and COMUNA 1 on 121. The study's specification asks for the base n under each chart and for values with n < 30 shown dimmed. Authors typed n into 19 titles instead ("Comuna 7 y Sin nombre con n < 30", "Estrato 4 n = 7 < 30"), and those numbers are wrong as soon as a reader sets a dashboard filter.

The numbers exist. A rate is published as a numerator and a denominator, and the denominator counts households. But a chart only receives the rate, and a bar, column, line or pie chart without a series shows only "category: value" in a small browser tooltip. Nobody can see that 46.85 % is 223 of 476 households.

A chart's n cannot simply add up its denominators either. In the study's "long" tables every household is counted under every answer of a question, so a bar chart of the 5 answers of one question would add up to 5 × 476. Authors also keep "Sin dato" and "No sabe" out of charts by hand: 7 separate stat tiles show the "Sin dato" share, and 22 widgets repeat a filter listing every comuna only to drop one household with no block.

## What Changes

- **Facts about an indicator's columns.** Next to the column names of an indicator (from `add-indicator-column-metadata`), an analyst can say three more things, in the indicator's details or through MCP `save_script`. Each is optional and saved with the script version:
  - **the base column**: the column that counts the people each row stands for, such as `hogares` (at most one);
  - **values that mean no data** for a column, such as "Sin dato" or "No sabe" (up to 20);
  - **answer columns**: the columns of a long table that name a question and its answers (`variable`, `orden`, `categoria`), single or multiple choice, where every household of the base is counted under every answer.
- **The n of every value.** Each bar, point, slice, cell and total knows its n: the sum of the base column, or else of the denominator of a rate. A sum with neither has no n. A rate also brings its numerator and denominator. Over several answers of one question, each household is counted once. No plotted value changes.
- **The n of a whole chart.** A chart has an n, and so its n line, only when its indicator declares a base column or its answer columns, because without them Tamiza cannot tell answers (each household counted once per answer) from groups such as comunas (each household counted once). Without a declaration each value keeps its own n (the sum of its denominator) for its tooltip and its dimming.
- **Richer tooltips everywhere.** Bar, column, line and histogram charts without a series, and pies, get the tooltips of the charts with a series, and can be read with the keyboard (one Tab stop, then the arrow keys). Every tooltip of a rate adds its numerator and denominator under their names, and the n when it is not the denominator. A histogram's bar gives its range and count only.
- **Small bases dimmed.** A dashboard can set a small-base threshold (1 to 10,000, off by default), and each widget can follow it, turn it off or set its own. A value whose n is below it is drawn lighter with a dashed outline (hatched on a heat map), never hidden. Its tooltip says "Small base: n = 16 (below 30)", a note under the chart explains the dimming, and tables mark the value.
- **The n line.** A dashboard can show each chart's n under it ("n = 223 Hogares que asistieron"), off by default, and each widget can show or hide it either way.
- **The no-data line.** A widget can leave out the values its indicator declares as no data, and say under the chart what they held ("Sin dato: 4.2%", and for a count its share of the total). Offered on bar, column, line, pie, heat map, pyramid, Likert and cross table widgets.
- **No "overall" line over answers.** A reference line at the overall value (from `add-chart-annotations`) is only the mean of the bars when the bars are the answers of one question. Once the indicator declares its answer columns, such a widget draws no overall line and says why.
- **Everywhere a widget goes.** Images, print, the scheduled email (body and workbook), the widget workbook and the MCP tools show the same n figures, marks and lines. The lines sit under the chart and before the widget's own footnote, which stays last, and they take their room from the plot as the texts of `add-widget-context` do. The dashboard's base settings are part of its versions (a new kind of change, "Base sizes changed"), are restored and duplicated with it, and can be read and set through MCP.
- **Compatibility.** Every new setting is optional, and nothing is on after the upgrade:
  - no saved widget or dashboard changes what is stored, and none gets a new version;
  - nothing is dimmed, no line appears and no row is left out until someone turns a setting on;
  - nothing drawn, printed, exported or emailed changes: charts, images, paper, scheduled emails and widget workbooks are as today until a setting is on.

  The richer tooltips are on by default, decided with the user on 2026-11-04: bar, column, line, histogram and pie charts without a series get the tooltips and keyboard access of the charts with a series, and every rate's tooltip gains its numerator and denominator. They are one of the program's intended default changes, with the smaller stat tile figure of `improve-stat-cards`, and they show only when a member points at, taps or focuses a mark. The rows of a rate that the API and the MCP tools `query_indicator` and `get_widget_data` return always gain the numerator, the denominator and the n too.
- **Migration.** `0023_add_small_sample_warnings.sql`, the next free number after the applied 0021 and 0022, adds the dashboard's threshold (empty, off) and its n line switch (off). The column facts live in the column metadata that `add-indicator-column-metadata` added (`script_versions.columns`); this change adds no column for them.

Not in this change:
- Weights and a weighted n (P3 `add-weighting`); every n here is unweighted.
- The stat tile's own lines inside the tile, the n of a tile with a statistic, and fixing or refusing a rate that adds up several answers (`improve-stat-cards`, which uses the answer columns declared here, counts the base across answers by the same rule, and ships in the same release). A stat tile here shows its n line under it, like any widget; `improve-stat-cards` then draws that one line inside the tile, once, and lets a tile choose it with its own setting.
- Paging or a summary mode for long MCP results (P3 `improve-mcp-ergonomics`).
- Hiding "Sin dato" through dashboard filters, and a "not in" filter (P2 `add-filter-exclusions`).
- n columns in every workbook whatever the settings (P3 `improve-widget-export`).
- A chart n for a box plot, and leaving no-data values out of a box plot, a map, a histogram or a stat tile.
- Treating the empty group "(none)" as no data: it keeps its bar.
- Column facts written by the script itself: they are entered in the indicator's details and through MCP, as its label and unit are.

## Depends on

- `add-indicator-column-metadata` and `add-widget-context`, implemented and archived on 2026-11-04:
  - the per-column metadata map (`script_versions.columns`, migration 0021, `IndicatorColumnMetadata` and `ColumnMetadata` in `Indicators/ColumnMetadata.cs`), the "Column names" section of the indicator's details, `save_script`'s `columns` argument and the names shown for columns;
  - the production-shapes fixture (`production-shapes.json`), which this change extends;
  - the stored `display` ends with `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription`, after which this change appends `minN` and `showN`;
  - the widget workbook's request already carries the widget's saved config, display included, and the dashboard's source (migration 0022);
  - the subtitle, the footnote and the source have their places in images, emails and workbooks, and the lines of this change are placed relative to them: before the footnote, which stays last.
- `add-chart-annotations`, which comes earlier in the program: its overall reference line is the one this change leaves out over declared answers, and its overall statement takes the exclusion of no-data values. It plans no migration, and neither does `improve-stat-cards`, so 0023 is free; `add-locale-formatting` plans 0024.
- It ships in the same release as `improve-stat-cards` (P1-7, implemented right after it), decided with the user on 2026-11-04, so an indicator can never declare answer columns while a rate over several of its answers is still added up as separate bases.

## Capabilities

### New Capabilities

None. The behavior belongs to the existing capabilities below.

### Modified Capabilities

- `dashboard-indicators`:
  - modified: "Widget query over one indicator" (the n, numerator and denominator returned, and the rows left out), "Bar and column charts of one indicator" (tooltips and keyboard without a series), "Marks of a widget with a series" (the base figures and small bases in tooltips), "Map tooltip" (the base figures the tooltip columns do not already show; the own caption text and unit that `add-widget-context` added stay);
  - added: "Base n of a mark", "Base n counted once across answers", "Base n of a widget", "Base n line under a widget", "Base lines under a widget in their place", "Small-base threshold", "Small bases drawn dimmed", "Small bases in tooltips and tables", "Mark tooltips of charts without a series", "Base figures in tooltips", "No-data values left out of a widget", "No-data line under a widget", "Answer columns without an overall line", "Base settings in the widget form", "Base settings of a dashboard", "Widgets and dashboards without base settings", "Base settings of a moved widget".
- `pipeline`: added "Base column of an indicator", "Values that mean no data in an indicator column", "Answer columns of an indicator", "Base metadata entered with the column names", "Limit of columns with base metadata".
- `dashboard-versions`: added "Base settings in dashboard versions".
- `custom-dashboards`: added "Base settings of a duplicated dashboard".
- `dashboard-reports`: added "Base n and no-data lines in a scheduled email", "Small bases in a scheduled email".
- `widget-export`: added "Base n and small bases in widget images", "Base n and small bases in a widget workbook".
- `dashboard-printing`: added "Small bases on paper".
- `mcp-server`: modified "Indicator queries and widget data" (n, numerator, denominator, small bases and the no-data values in the results); added "Base settings through MCP", "Base metadata through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/ColumnMetadata.cs` (from `add-indicator-column-metadata`): `IndicatorColumnMetadata` gains `base`, `missing` and `categories`, with their checks in `ColumnMetadata.Check` and `ColumnMetadata.Normalize`, which today drop and count only the columns that have a label.
  - `Indicators/IndicatorModels.cs`:
    - `WidgetDisplay` gains `minN` and `showN`, after `ShowDescription`;
    - `WidgetConfigInput` and `WidgetConfig` gain `missingFooter`;
    - `IndicatorRow` and the series totals gain `numerator`, `denominator` and `n`;
    - `IndicatorQueryResponse` gains the widget's `n` and `noData`;
    - `DashboardView` gains `minN` and `showN`, after `Source`;
    - `IndicatorColumns` (the answer of `get_indicator_columns`) gains the indicator's declarations for its columns.
  - `Indicators/IndicatorQueryBuilder.cs`, `SeriesQueryBuilder.cs` and a new `BaseQueryBuilder.cs`: the sums, the n counted once across answers, and the no-data rows.
  - `Indicators/IndicatorDashboard.cs`: the indicator's base metadata read in the query's transaction, and the readers.
  - `Indicators/IndicatorValidation.cs` and `DashboardValidation.cs`: the new settings.
  - `Indicators/WidgetQuery.cs`: `missingFooter`.
  - `Indicators/SeriesTable.cs`: the figures of each cell, with the shared fixture.
  - New `Indicators/BaseFigures.cs`: the settings in force, the small-base rule and the texts of the lines.
  - `Indicators/ReferenceLines.cs` (from `add-chart-annotations`): no overall line over declared answers.
  - `Indicators/DashboardEndpoints.cs`: `PUT /dashboards/{id}/bases`, the view and the duplicate.
  - `Indicators/DashboardVersions.cs`: the content, the restore and the change kind `basesChanged`.
  - `Indicators/WidgetExportEndpoints.cs`: `WidgetExportRequest` gains `minN` and `showN`.
  - `Indicators/IndicatorEndpoints.cs`: `GetColumnsAsync` returns the declarations.
  - `Reports/`: `ReportDataBuilder`, `ReportFormat` (`Figure`), `ReportText`, `ReportHtml`, `ReportWorkbook` and `WidgetWorkbook`.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpScriptTools.cs` and `Mcp/Instructions.md`.
  - `Data/Dashboard.cs` and `TamizaDbContext.cs`.
  - OpenAPI, unit and integration tests, and the production-shapes fixture test.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the types, `saveBases` and the change kind; `core/projects/pipeline.service.ts`: `ColumnMetadata` and `ColumnDraft` gain the three properties.
  - `indicators/widget-view.ts`: `queryFor`, `savedConfig`, `displayFor`, the lines under the chart and the image's notes.
  - `indicators/axis-chart.ts`, `series-plot.ts`, `series-table.ts`, `pie-widget.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `crosstab-widget.ts`, `boxplot-chart.ts`, `stat-widget.ts`, `map-widget.ts`, `map-engine.ts` and `map-tooltip.ts`: tooltips, figures and dimming.
  - New `indicators/base-figures.ts`, and `format.ts` (`figureFormatter`).
  - `indicators/reference-lines.ts` (from `add-chart-annotations`): no overall line over declared answers.
  - `indicators/widget-table.ts`, `export-parts.ts`, `widget-export/widget-exporter.ts` and `widget-export/image-layout.ts`: the table marks, and the lines and the dimmed figure in images.
  - `indicators/widget-dialog.ts`, `widget-grid.ts`, `dashboard-versions-dialog.ts` and a new `indicators/dashboard-bases-dialog.ts`.
  - `pages/projects/project-indicators.ts` (the **Base sizes** action, after **Source**), `column-names.ts`, `indicator-metadata-fields.ts` and `add-indicator-dialog.ts`.
  - `indicators/testing/series-tables.json`, a new `indicators/testing/base-figures.json` and `indicators/testing/production-shapes.json`, shared with the API tests.
  - `en.json` and `es.json` strings, and the specs (among them the specs that read the native tooltips today: `chart-widgets.spec.ts` and `script-editor/indicator-view.spec.ts`).
- **Database:** `0023_add_small_sample_warnings.sql` adds `dashboards.min_n` (with `ck_dashboards_min_n`, 1 to 10,000) and `dashboards.show_n`, and rewrites no row. `TamizaDbContext` mirrors them.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: a section on bases, small bases and no-data values, and which columns of a long table to declare.
  - `docs/scripts.md`: the column metadata table gains the three properties.
  - `docs/mcp.md`: the settings and the new result fields;
  - `docs/operations.md` "What leaves the deployment by email": the base n figures and the values an indicator declares as no data, which members write as free text;
  - `docs/tamiza-brief.md`: the data model, and this change in the program's list.
