# Proposal

## Why

Indicator scripts name their columns for code: `numerator`, `denominator`, `comuna`, `grupo_edad`, `hogar_menores`. Every widget then shows those names to the people who read the dashboard. In the market study of the Duitama Bicentennial Center, 142 of the 190 widgets plot the ratio of `numerator` to `denominator`, so their caption, the description under each stat tile, the value header of "Show as table", the exported image, the scheduled email and the workbook all read "<label>: numerator / denominator". A chart grouped by `hogar_menores` says "by hogar_menores". Authors moved the real names into 80-character titles instead. The user asked for display names for `numerator` and `denominator`.

Later changes of this program need more facts about an indicator's columns: the order of a column's answers, the column that holds the base n, the values that mean "no data", a weight. An indicator's details hold only a label, a description, a unit and a format, so these facts have no place to live.

## What Changes

- **Column names of an indicator.** An indicator's details gain a name to show for each column of its table, 1 to 80 characters, such as `numerator` → "Hogares que asistieron", `denominator` → "Hogares" and `comuna` → "Comuna". The names are saved with every version of the script, like the label and the unit, so restoring a version brings its names back. Changing a name needs no run. The same place will later hold the other facts about a column (the order of its answers, answers kept last, number prefixes hidden, the base n, the values that mean "no data", the kind of answers, a weight, mandatory filters), and it is built so they can be added.
- **Entered with the indicator's details.** The details above the script editor gain a **Column names** section: one optional name per column of the indicator's table, taken from its latest test or, without one, from its published table. A column that has a name but is no longer in the table is listed apart, so its name can be kept or cleared. A save that sends no column names, such as one from an AI client that does not know them, keeps the current ones.
- **A widget's own names.** One widget can give its own names to up to 10 columns (1 to 80 characters each), on every kind of widget. A widget's name wins over the indicator's, which wins over the column's own name. The widget form offers a name for each column the widget shows, with the indicator's name as a hint.
- **Shown wherever a column's name is shown today**:
  - the caption and the accessible name of every chart ("Teatro hoy: Hogares que asistieron / Hogares by Comuna"), and a stat tile's description;
  - tooltip lines that name what is plotted, and a map's tooltip columns that have no label of their own;
  - the headers of "Show as table" and of cross tables;
  - images and printed dashboards, which show the same captions and tables;
  - the scheduled email's body, plain text and workbook, and the widget workbook's data sheet and Summary.

  The columns' own names stay where a column is chosen or matched: widget and dashboard filters, colors kept by value, the column lists of the widget form, messages about a column the table no longer has, what the API and AI clients send, and the export of an indicator's table. Colors never change with a name.
- **AI clients (MCP).**
  - `save_script` takes the names in an optional `columns`: left out, the current names are kept; `{}` clears them; given, it replaces them all.
  - `list_indicators`, `get_script` and `save_script`'s answer return the names, and `get_indicator_columns` gives each column its `label`.
  - The widget tools take a widget's own names in `display.labels`.
  - `get_widget_data` and `query_indicator` return `labels`: the name shown for each of the widget's columns that has one.
- **The widget workbook gets the widget's own names.** Today the workbook request sends only the widget's query; it now also sends the widget's own names.
- **Settings listed once.** The requirements on dashboard versions, duplicating a dashboard and moving a widget stop naming each widget and dashboard setting one by one, so the later changes of the program do not each rewrite them.
- **A check of the widget shapes the study uses.** A test holds one invented widget of each shape found in the study's dashboards (ratio and measure widgets, sorts by `orden`, numbered answers in filters, `in` filters, ignored dashboard filters, series, box plots, the map, heat maps, Likert scales, stat tiles), with made-up names and values. It proves each one is saved and queried exactly as before. Every later change of the program adds its shapes and runs it.
- **Compatibility.** No widget and no indicator has column names today, so every dashboard, image, printout, scheduled email, workbook and AI client answer stays the same until someone adds one. Saved widgets keep exactly the stored text they have, so no dashboard version appears by itself. As today, `update_widget` replaces a widget's whole settings, so an AI client that sends them without `display.labels` removes the widget's own names; the tool descriptions say so.
- **Migration.** The database gains one empty column for the indicators' names (migration 0021). Nothing already saved is rewritten.

Not in this change:
- The other facts about a column: the order of its answers, answers kept last and number prefixes hidden (`add-declared-category-order`), the base n, values that mean "no data" and answer columns (`add-small-sample-warnings`), weights (`add-weighting`), and mandatory filters (`add-authoring-guards`).
- Names declared by the script itself, in its code or its table. Rejected: the names live in the details form and in MCP, as the label, unit and format do, so a name change needs no run.
- Names for the values of a column (category labels).
- Labels in the column pickers of the widget form, the dashboard filters form or the colors form, and in the export of an indicator's table.
- The script assistant reading or proposing column labels.

Depends on: none. It is the first change of the program. `add-widget-context`, `add-declared-category-order` and `add-small-sample-warnings` (and, through it, `improve-stat-cards`) build on its column names and on the place that holds them, and every later P1 change extends and runs its check of the production widget shapes.

## Capabilities

### New Capabilities

None. Indicator metadata belongs to `pipeline`, and widgets to `dashboard-indicators`.

### Modified Capabilities

- `pipeline`:
  - "Indicator metadata" names the column labels;
  - new requirements "Column labels of an indicator", "Column labels that are refused", "Column labels kept by a save that leaves them out" and "Column names in the indicator's details".
- `dashboard-indicators`:
  - "Moving a widget to another dashboard" lists the widget's settings generically;
  - new requirements "Column labels of a widget", "Column labels of a widget that are refused", "Column labels in widget texts", "Column labels in the widget form" and "Widgets and indicators without column labels".
- `dashboard-reports`:
  - "Widget tables in a scheduled email": the headers use the column labels;
  - new requirement "Column labels in a scheduled email".
- `widget-export`: new requirement "Column labels in widget files".
- `dashboard-printing`: new requirement "Column labels on paper".
- `dashboard-versions`: "Dashboard versions" lists the dashboard's and the widgets' settings generically.
- `custom-dashboards`: "Duplicating a dashboard" lists the copied settings generically.
- `mcp-server`: new requirements "Indicator column labels through MCP" and "Widget column labels through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Data/Pipeline.cs` (`ScriptVersion.Columns`) and `Data/TamizaDbContext.cs`.
  - `Pipeline/PipelineModels.cs`: `SaveIndicatorRequest`, `ScriptVersionDetails` and `IndicatorScript` gain `columns`.
  - `Pipeline/ScriptEndpoints.cs`: validation, the keep rule, restore and removal.
  - New `Indicators/ColumnMetadata.cs`: the record `IndicatorColumnMetadata`, `ColumnMetadata` (the map's limits, normalization and checks, which later changes extend) and `ColumnLabels` (the label rules and the name shown for a column, C# twin of `columnLabels`).
  - `Indicators/IndicatorModels.cs`: `WidgetDisplay.Labels` and `IndicatorEntry.Columns`.
  - `Indicators/IndicatorValidation.cs` (`display.labels`) and `Indicators/IndicatorEndpoints.cs` (the overview and the columns of a table).
  - `Reports/ReportDataBuilder.cs`: `Plotted` and every table header.
  - `Mcp/McpScriptTools.cs`, `Mcp/McpDashboardTools.cs` (`IndicatorInfo`, `labels`, the tool texts), `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, the pinned-text tests (`McpToolCatalogTests`, `McpGuideTests`, `IndicatorDocsTests`), and the production-shapes fixture test. `AssistantPrompts.Version` does not change: the script guide is untouched.
- **ui** (`tamiza-ui`):
  - `core/projects/pipeline.service.ts` and `core/projects/indicators.service.ts`: the new fields.
  - `pages/projects/indicator-metadata-fields.ts`, `add-indicator-dialog.ts` and `project-scripts.ts`: the **Column names** section.
  - `indicators/format.ts` (`columnLabels`, `plotted`), `widget-view.ts` (`displayFor`), `widget-dialog.ts`, and every chart that names a column: `axis-chart.ts`, `series-plot.ts`, `pie-widget.ts`, `map-widget.ts`, `map-tooltip.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `crosstab-widget.ts`, `boxplot-chart.ts`, `stat-widget.ts`, and `script-editor/indicator-view.ts`.
  - `project-indicators.ts`: the widget workbook request.
  - `indicators/testing/column-labels.json` and `indicators/testing/production-shapes.json`, shared with the API tests.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0021_add_indicator_column_metadata.sql` adds `script_versions.columns` (`jsonb`, null) and a check that only an indicator version holds an object there. `TamizaDbContext` mirrors both.
- **Analytics:** none.
- **Docs:**
  - `docs/scripts.md`: the metadata table.
  - `docs/indicators-dashboard.md`: column labels, where they show, and the generic lists of settings.
  - `docs/mcp.md`: `save_script`'s `columns`, `display.labels` and `labels`.
  - `docs/operations.md`: column labels among what a scheduled email carries.
  - `docs/tamiza-brief.md`: the data model, and section 8 lists this program in place of "7l `add-widget-display-options` (next)".
