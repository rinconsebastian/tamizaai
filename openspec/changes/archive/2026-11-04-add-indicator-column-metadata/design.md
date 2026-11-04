# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change is the first of the widget and dashboard program: it gives indicator metadata a home for facts about columns, and uses it for the first fact, the name shown for a column. The pieces it works on:

- **Indicator metadata is four columns of `script_versions`.**
  - `label`, `description`, `unit` and `format` were added by `0008_split_indicator_scripts.sql:39-46`, with a check on `format`. The entity is `ScriptVersion` (`Data/Pipeline.cs:4-38`, metadata at `:20-28`), mapped in `TamizaDbContext.cs:201-224`.
  - `ScriptEndpoints.SaveIndicatorCoreAsync` (`Pipeline/ScriptEndpoints.cs:186-208`) runs `ValidateMetadata` (`:264-291`) and `AddVersionAsync` (`:349-391`), which numbers the version and retries a unique-key race. Every save stores exactly what it is sent: an omitted unit or description is cleared.
  - `RestoreCoreAsync` (`:319-336`) copies the source version's code and metadata; `RemoveCoreAsync` (`:306-316`) writes a version with no code and no metadata.
  - `ToDetails` (`:46-48`) projects a version into `ScriptVersionDetails` (`Pipeline/PipelineModels.cs:14-27`); `CurrentIndicatorsAsync` (`:51-58`) is the current version of every indicator, read by the overview (`IndicatorEndpoints.cs:32-56`), the emails (`ReportDataBuilder.cs:41-43`) and MCP (`McpDashboardTools.cs:576-579`).
  - The request is `SaveIndicatorRequest(Code, Label, Description, Unit, Format)` (`PipelineModels.cs:40`). MCP `save_script` builds the same request (`McpScriptTools.cs:86-117`) and answers with the saved version without its code (`:186-204`).
  - The UI holds the metadata as `IndicatorMetadata` (`core/projects/pipeline.service.ts:45-50`), edited by `indicator-metadata-fields.ts` in the add dialog (`add-indicator-dialog.ts:43`) and above the editor (`project-scripts.ts:181-187`), compared by `sameMetadata` (`project-scripts.ts:62-64`) and sent by `save()` (`:597-622`).
- **Indicator tables publish no column labels.** `TableColumn(Name, Type, Label)` (`CleanData/DataModels.cs:15`) carries question labels for raw and clean tables; for an indicator table `Label` is always null, because the indicator step publishes its table with no column labels (`tamiza-analytics/src/tamiza/pipeline.py:402`, read back by `col_description` in `CleanData/TableCatalog.cs:13`). `get_indicator_columns` and `GET …/indicators/{name}/columns` return those `TableColumn`s (`IndicatorDashboard.cs:76-87`) through `IndicatorEndpoints.GetColumnsAsync` (`IndicatorEndpoints.cs:59-69`), which takes no `TamizaDbContext` today. The UI reads that endpoint in the widget form (`widget-dialog.ts:1076`), the dashboard filters form (`dashboard-filters-dialog.ts:230`) and the colors form (`dashboard-colors-dialog.ts:359`); none of them reads `label`.
- **A widget's display.** `WidgetDisplay(Color, Ramp, Orientation)` (`Indicators/IndicatorModels.cs:157-173`) has each member `WhenWritingNull`, and `Normalize()` returns null when all three are unset. `WidgetConfigInput.Display` (`:214-215`) and `WidgetConfig.Display` (`:255`) are the only `WhenWritingNull` members of the config. `CheckDisplay` (`IndicatorValidation.cs:164-210`) refuses a setting on a kind that does not take it. The stored text of configs without a display is pinned by `WidgetDisplayTests.cs:16-38`.
- **The stored config is read and compared as the API reads it.** `ReadConfig` (`DashboardEndpoints.cs:1015-1019`) deserializes into `WidgetConfigInput` with Web defaults and normalizes; an update writes nothing when the normalized config equals `ReadConfig` of the stored one (`:685-694`). Unknown members are dropped, never refused. Versions embed the stored text (`DashboardVersions.cs:339-369`), a duplicate copies it (`DashboardEndpoints.cs:474-495`), and a move keeps it and the size (`:681`).
- **The UI keeps only what it lists.** `queryFor` (`widget-view.ts:102-121`), `savedConfig` (`:151-159`) and `displayFor` (`:75-83`); the form rebuilds the config from its signals (`widget-dialog.ts:904-924`) and its display from `displayDraft` (`:931-938`). `WidgetQuery.For` is the C# twin of `queryFor`.
- **Every column name a reader sees comes from a few places.**
  - "What is plotted" is `plotted(config, label)` (`format.ts:108-114`): the indicator's label, plus `: measure` when the widget names a measure, or `: numerator / denominator`. Its C# twin is `ReportDataBuilder.Plotted` (`ReportDataBuilder.cs:427-431`).
  - Callers: `axis-chart.ts:210, 383, 396, 425, 443`, `boxplot-chart.ts:482`, `crosstab-widget.ts:79`, `heatmap-chart.ts:271`, `likert-chart.ts:284`, `map-widget.ts:197`, `pie-widget.ts:109`, `pyramid-chart.ts:241`, `stat-widget.ts:30`. The `what` reaches the tooltips of a 100% layout (`series-plot.ts:174`) and of a Likert mark (`likert-chart.ts:158`).
  - The raw group and series columns are written in captions (`axis-chart.ts:386`, `pie-widget.ts:50`, `map-widget.ts:77`, `heatmap-chart.ts:146`, `crosstab-widget.ts:60`), summaries (`axis-chart.ts:408-409, 433`, `heatmap-chart.ts:361-362`, `pyramid-chart.ts:320`, `likert-chart.ts:349`, `boxplot-chart.ts:631, 638`, `pie-widget.ts:234`) and table headers (`axis-chart.ts:446`, `pie-widget.ts:244`, `map-widget.ts:611`, `crosstab-widget.ts:43`, `boxplot-chart.ts:619`).
  - The caption of an image is not read from the page: each chart registers its own `parts()` with `WidgetExportHost` (`export-parts.ts:1-6, 31-41`), built from the same signals as its template. The axis chart reuses its `caption()` (`axis-chart.ts:381-387`), but four kinds write the caption again there with the raw columns: `heatmap-chart.ts:318`, `pie-widget.ts:196`, `map-widget.ts:342` and `crosstab-widget.ts:92`. A stat tile's part reuses its `description` (`stat-widget.ts:30`).
  - No chart draws an axis title or a legend title today: axes show values, and a series legend lists the series' values, so neither names a column.
  - A map tooltip column without a label reads its column (`tooltipLabel`, `map-tooltip.ts:26-29`, used by the tooltip at `:69` and the table at `map-widget.ts:611`), as does its placeholder in the form (`widget-dialog.ts:357`).
  - The emails and the workbooks write the caption (`ReportDataBuilder.cs:116`, shown at `ReportHtml.cs:118, 137, 216, 234` and `WidgetWorkbook.cs:71`) and the headers (`ReportDataBuilder.cs:221, 242, 291, 314, 327, 341, 345`).
  - Print is the browser's print of the page itself (`dashboard-print.ts:174-189`), so it follows the screen.
- **The widget workbook request** sends `queryFor(widget.chartType, widget.config)` (`project-indicators.ts:816`), so no display setting reaches the workbook today (map tooltip labels are lost there).
- **MCP results** are serialized `WhenWritingNull` (`McpResultBudget.cs:18-22`) and cut at 60,000 characters (`McpOptions.cs:64`). A widget's result has `indicator` and `label` (its indicator's) and its rows (`McpDashboardTools.cs:592-660`), and names none of its columns; the indicator facts it uses are `IndicatorInfo(Label, Unit, Format)`, built by `MetaAsync` from `CurrentIndicatorsAsync` (`:574-579`). The display help texts are `DisplayHelp` and `ChartTypeChangeHelp` (`:50-60`), pinned by `McpToolCatalogTests` (`McpToolCatalogTests.cs:88, 98, 158`). The `config` arguments of `create_widget` and `update_widget` describe the display as "(color, scale or orientation)" (`:354, 376`).
- **Shared fixtures between the twins** live in `tamiza-ui/src/app/pages/projects/indicators/testing/` and are read by the API tests from the repository (`SeriesTableTests.cs:14`).

## Goals / Non-Goals

**Goals:**
- One map of column facts per indicator version, which later changes extend with properties and never with columns or tables of their own.
- One rule for the name shown for a column (widget, then indicator, then the column), with one function per side and shared cases, so the screen, the images, the emails, the workbooks and MCP agree.
- Nothing changes for a widget or an indicator with no labels: stored text, queries, responses, versions, emails, workbooks and MCP results stay as they are.
- A fixture of the production shapes that this change and every later one run before and after their work.

**Non-Goals:**
- Any change to queries or query responses. Labels never reach `queryFor`, `WidgetQuery.For` or the SQL.
- Labels in the column pickers of the widget form, the dashboard filters form and the colors form, in the "Indicators (N)" list, and in the indicator table export.
- Column labels in the script assistant's context or in the authoring guide (`Assistant/Prompts/system.md`), so `AssistantPrompts.Version` stays as it is.
- Fixing the loss of the map's tooltip labels in the widget workbook (`add-widget-context` makes the request carry the saved config).

## Decisions

### D1. Where column labels live (DbUp `0021_add_indicator_column_metadata.sql`)

```sql
ALTER TABLE tamiza.script_versions
    ADD COLUMN columns jsonb,
    ADD CONSTRAINT ck_script_versions_columns
        CHECK (columns IS NULL OR (kind = 'indicator' AND jsonb_typeof(columns) = 'object'));
```

- The column is null for script 1 and script 2, for a removal, and for an indicator version that labels no column. It rewrites no row, so existing versions read as "no labels".
- `ScriptVersion.Columns` is the stored text (`string?`, `HasColumnType("jsonb")`), as `Dashboard.Filters` is (`TamizaDbContext.cs:424`), so a copy (the keep rule of D3, a restore) moves the stored text as it is. The analytics worker reads `script_versions` by named columns only (`pipeline.py:89, 524`), so it never sees the new one. `TamizaDbContext` mirrors the check. `SchemaDriftTests` and a `ScriptConventionTests` case keep script and model in step.
- The metadata stays with the script version, so it is versioned, restored and attributed as the label, unit and format are (`pipeline` "Indicator metadata").

*Alternatives:*
- *Declared by the script* (a dict returned with the DataFrame, or `df.attrs`). Rejected: the label, unit and format are entered in the details form and MCP, not in code; a label change would need a run; the contract, the SDK's local check, the local dev kit and the assistant would all change.
- *`COMMENT ON COLUMN` in the published table*, which `TableColumn.Label` already reads. Rejected: written by runs, so a label would wait for a run and be lost by a failed one, and it would not be versioned with the script.
- *One `details jsonb` for all metadata.* Rejected: `label` and `format` are columns with rules and readers today; moving them rewrites every version for no reader's gain.
- *A table `indicator_columns`.* Rejected: versioning with the script would need one row set per version anyway.

### D2. The map and its properties (new `Indicators/ColumnMetadata.cs`)

```json
{ "numerator": { "label": "Hogares que asistieron" }, "denominator": { "label": "Hogares" }, "comuna": { "label": "Comuna" } }
```

- **One file, two static classes.** `ColumnMetadata` holds the map's rules (`MaxColumns`, `Normalize`, `Check`, `Read`, `Write`): it is the column validator that `ScriptEndpoints.ValidateMetadata` calls, and the one later changes extend. `ColumnLabels` holds what is only about names (`MaxLength`, `MaxWidgetLabels`, the label check shared with `display.labels`, and the namer `For` of D6). The record `IndicatorColumnMetadata` lives in the same file.
- **Keys** are the column's name as the indicator's table publishes it, checked with `ColumnNames.IsValidColumnName` (`Pipeline/ColumnNames.cs:85, 97-98`: `^[a-z_][a-z0-9_]{0,62}\z`, the rule of `tamiza.names`, so every published column passes it). They are not checked against the published table: the script may not have run, and a column may come back. JSON keeps them as written (Web defaults set no dictionary key policy).
- **Values** are `IndicatorColumnMetadata(string? Label = null)`, each property `WhenWritingNull` with a `[Description]` for the MCP schema. The record is the place later changes add their properties (`order`, `last`, `stripPrefix`, `base`, `missing`, `categories`, `weight`, `required`), each optional, nullable and `WhenWritingNull`, with its own checks in `ColumnMetadata.Check` and its own field in the form's row (D8). A column's row in the form, its errors (`columns.<column>.<property>`) and its MCP description are built per property for that reason.
- **Limits:** at most 100 columns (`ColumnMetadata.MaxColumns`); a label is trimmed and holds 1 to 80 characters (`ColumnLabels.MaxLength`, the length of a map tooltip label), counted as the title is (UTF-16 code units, the same in C# and TypeScript); no control character (a NUL cannot be stored in `jsonb`, and a line break would break a one-line caption).
- **Normalization** (`ColumnMetadata.Normalize`): a null or blank label is none; a null entry, or one with no property left, is dropped; an empty map is null, stored as SQL NULL.
- **Order carries no meaning.** `jsonb` stores an object's keys in its own order (shorter keys first) and gives them back that way, so the API, the UI and MCP never rely on the order of the map; the form lists the columns in the table's order (D8).
- **Unknown property keys are dropped**, as everywhere in the config: the typed reader ignores them when a save gives `columns`. A save that leaves `columns` out and a restore copy the stored text (D1, D3), so they never drop anything. `docs/mcp.md` says that an unknown key is ignored, with the example of `colour`.

*Alternatives:*
- *A flat map of strings* (`{ "numerator": "Hogares" }`). Rejected: every later property would need a new map, a new argument and a new column, which is the point of the program's shared map.
- *Keeping unknown properties on every save.* Rejected: the API would have to round-trip JSON it cannot validate, and the UI rebuilds the map from its form anyway; an older release dropping newer properties is the program's rollback rule.

### D3. Saving, keeping and restoring

- `SaveIndicatorRequest` gains `Columns` (`IReadOnlyDictionary<string, IndicatorColumnMetadata?>?`), and `Metadata` (`ScriptEndpoints.cs:346`) gains the columns' text and a `KeepColumns` flag.
- **Left out** (`null`): keep. `AddVersionAsync` reads, inside its retry loop, the `columns` text of the version numbered `latest` when that version has code, and writes it on the new one. Reading it with the number the new version follows means a save that races another keeps the labels of the version it is numbered after, and the MCP guard (`baseVersion`) means a client always keeps the labels of the version it read. With no current version (a new or removed indicator), there are none.
- **Given:** validated, normalized and stored; `{}` stores NULL (clears).
- **Restore** copies the source version's text (`RestoreCoreAsync` selects `v.Columns` with the rest). **Removal** writes NULL.
- The UI always sends `columns`, built from its form; MCP sends what the client gave. Both reach `SaveIndicatorCoreAsync`, so the rules are one.
- Errors, collected with the other metadata errors and returned at once:

| Rule | Key | Message |
|---|---|---|
| More than 100 columns with a property | `columns` | "An indicator labels at most 100 columns." |
| A key that is not a column's name | `columns` | "'{key}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores." (the key cut to 63 characters) |
| A label longer than 80 characters once trimmed | `columns.{column}.label` | "A column label can have up to 80 characters." |
| A label with a control character | `columns.{column}.label` | "A column label cannot hold a line break or another control character." |

  An invalid key goes to `columns`, never into a field path, so a key of any length or character never makes an odd key.
- Saving labels changes no dashboard, so it makes no dashboard version (`dashboard-versions` versions dashboards only).

*Alternatives:*
- *Replace on every save, as label, unit and description are.* Rejected: an MCP client that does not know `columns` would wipe the names a member typed, on every `save_script` it makes (46 of the study's indicators were written through MCP).
- *Merge per column.* Rejected: a client could never remove a label without a second mechanism; "the whole map or nothing" is the rule of `valueColors` too.

### D4. Reading the labels

- `ScriptVersionDetails` and `IndicatorScript` gain `Columns` (the typed map, null when none). `ToDetails` projects the stored text and reads it with `ColumnMetadata.Read` in the final projection, which EF evaluates on the client. Every use of `ToDetails` is a final projection (`ScriptEndpoints.cs:55, 66, 138, 223, 383`, `McpScriptTools.cs:67`); an integration test reads labels through each of them.
- `IndicatorEntry` (`IndicatorModels.cs:311-312`) gains `Columns`, filled by `GetOverviewAsync`. The UI's `IndicatorEntry` (`indicators.service.ts:293-303`) gains `columns?: ColumnMetadataMap | null`; a response without it reads as none.
- `GET …/indicators/{name}/columns` (and `get_indicator_columns`) sets each indicator table column's `label` to its indicator's label. `IndicatorEndpoints.GetColumnsAsync` gains the `TamizaDbContext` parameter (as `GetOverviewAsync` has, and the MCP tool passes its own) and reads the indicator's current version; the data layer (`IndicatorDashboard.GetColumnsAsync`) does not change. The widget form, the dashboard filters form and the colors form do not read `DataColumn.label` today (Context), so nothing on the page changes with it.
- `save_script`'s result (`McpScriptTools.cs:204`) adds `columns`; `list_scripts` does not.

### D5. A widget's labels (`display.labels`)

- `WidgetDisplay` gains `[JsonIgnore(WhenWritingNull), Description(…)] IReadOnlyDictionary<string, string>? Labels = null`. `Normalize()` trims each label, drops the null and empty ones, turns an empty map into null, and returns null only when color, ramp, orientation and labels are all unset. A display that holds only labels is kept. As for the indicator's map, the keys' order means nothing (`dashboard_widgets.config` is `jsonb`).
- The API never compares a widget's label with its indicator's: a saved label equal to the indicator's is kept as given, so an unchanged save stays unchanged.
- **Universal:** every kind takes labels, so `CheckDisplay` has no per-kind rule for them, `displayFor` keeps them on every kind, and a chart type change keeps them. `ChartTypeChangeHelp` does not name them.
- **Not checked against the table** (`CheckAgainstColumns` unchanged): a label for a column the table lost never makes a widget stale.
- **Never queried:** `queryFor` and `WidgetQuery.For` leave `display` out, as today, so a label change redraws without a request.
- Errors (`CheckDisplay`):

| Rule | Key | Message |
|---|---|---|
| More than 10 labels | `config.display.labels` | "A widget labels at most 10 columns." |
| A key that is not a column's name | `config.display.labels` | "'{key}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores." |
| A label longer than 80 characters once trimmed | `config.display.labels.{column}` | "A column label can have up to 80 characters." |
| A label with a control character | `config.display.labels.{column}` | "A column label cannot hold a line break or another control character." |

  The widget export endpoint keys its config errors with the prefix `config` without a dot (`WidgetExportEndpoints.cs:74`), an existing defect this change leaves as it is; the form never sends invalid labels there.

*Alternatives:*
- *Labels inside `ratio` (`{ numerator, denominator, numeratorLabel }`).* Rejected: `ratio` is part of the query, so a label change would query again, and only the ratio's columns would get names.
- *Top-level `labels` in the config.* Rejected: `display` is the documented place for what never changes the rows, and it is already `WhenWritingNull`.
- *A per-kind rule (labels only where a column is named).* Rejected: every kind names a column somewhere (a stat tile its ratio, a map its tooltip columns), and a per-kind rule would make chart type changes fail.

### D6. The name shown, on both sides (`columnLabels` / `ColumnLabels.For`)

```ts
export type ColumnNamer = (column: string) => string;
export const rawNames: ColumnNamer = (column) => column;
/** The widget's label, else the indicator's, else the column. Twin of ColumnLabels.For in tamiza-api. */
export function columnLabels(indicator: { columns?: ColumnMetadataMap | null } | null, display: WidgetDisplay | null | undefined): ColumnNamer;
export function plotted(config: Pick<WidgetConfig, 'measure' | 'ratio'>, label: string, names: ColumnNamer = rawNames): string;
```

- `plotted` keeps its shape and only names the columns: `label`, `label: names(measure)` when the widget names a measure, `label: names(numerator) / names(denominator)`. A widget that names no measure still reads as the label alone. `ReportDataBuilder.Plotted(config, label, names)` is its twin.
- A shared fixture `indicators/testing/column-labels.json` (cases: an indicator's map, a widget's labels, a config, a label, and the expected `plotted` and the name of each column) is read by `format.spec.ts` and by a new `ColumnLabelsTests.cs`, as `series-tables.json` is (`SeriesTableTests.cs:14`).
- `rawNames` is the default everywhere a namer is taken, so the script editor's charts, the specs and any caller that passes none behave as today.

### D7. Where the page shows them

- `WidgetView` computes `names = computed(() => columnLabels(this.indicator(), this.config()?.display))` next to `label` (`widget-view.ts:342`) and passes `[names]` to every chart component, which takes `names = input<ColumnNamer>(rawNames)`.
- Each component replaces `plotted(config, label)` with `plotted(config, label, names)`, and every raw `config.groupBy` and `config.seriesBy` it writes in a caption, a summary or a header with `names(column)`. The list is the one in Context. `crossTableModel`'s context (`CrossTableContext`, `crosstab-widget.ts:15`) carries the namer, and `boxTableModel` is given the named group header by its caller (`boxplot-chart.ts:619`).
- **Images.** The caption an image draws comes from each chart's `parts()`, not from the page, so the four kinds that write it there (`heatmap-chart.ts:318`, `pie-widget.ts:196`, `map-widget.ts:342`, `crosstab-widget.ts:92`) take `names` too; the axis chart and the stat tile reuse the caption and description they show. `image-layout.spec.ts` or `widget-exporter.spec.ts` checks one caption per kind that has one.
- **Print** needs no code of its own: it prints the page. `dashboard-print.spec.ts` gains a caption and a table case.
- `tooltipLabel(item, names)` (`map-tooltip.ts:26-29`) returns the item's own label, else `names(item.column)`; `TooltipContext` carries the namer.
- The single-series line's name (`axis-chart.ts:361`) is the indicator's label alone and stays so.
- Raw names stay in the filter forms and the filter bar (filters have their own labels), the colors form, the widget form's column selects, the messages that name a column the table no longer has, and the indicator table export.
- Colors kept by value and series colors stay keyed by the raw column (`axis-chart.ts:185-188, 299-300`, `pie-widget.ts:160`, `pyramid-chart.ts:265-268`, `boxplot-chart.ts:513`), so a label never recolors a mark.
- `script-editor/indicator-view.ts` passes `columnLabels({ columns: <the labels being typed> }, null)` to its stat tiles and chart (`:122, 127, 150`).

### D8. The **Column names** section of the indicator's details

- `IndicatorMetadata` gains `columns: Record<string, ColumnDraft>`, where `ColumnDraft` is `{ label: string }`, the row being typed for one column; later changes add their properties' fields to `ColumnDraft`. `metadataOf`, `sameMetadata` and `emptyMetadata` (`project-scripts.ts:49-64`) include it, so typing a label makes the details unsaved. `sameMetadata` compares the rows with a non-empty label, trimmed, so a listed column with an empty field equals no entry and opening a version marks nothing unsaved.
- `IndicatorMetadataFields` takes `columns: string[]` (the columns to offer) and lists one row per column, labelled by the column's name in `<code>`, after the format field, under a heading "Column names" with a hint ("The name shown for the column in captions, tables, emails and workbooks. Empty: the column's own name."). This change puts one text field in each row, `maxlength` 80; a row is a group of fields, so later properties add fields to it.
- **Which columns:** `project-scripts.ts` gives the columns of the latest test of the selected indicator's draft (the table of its last step), or, without one, of the published table (`IndicatorsService.columns`, already used by the widget form); then each column with a saved label that list lacks, marked "Not in the table", whose field can be emptied. A new indicator (the add dialog) has none: the section says "The columns are listed after a test." and the dialog sends `columns: {}`.
- **Saving:** `save()` (`project-scripts.ts:597-622`, which spreads the metadata into the body today) sends `columns` as `{ column: { label } }` for every row with a non-empty label, trimmed. Server errors on `columns.{column}.label` go to that field and an error on `columns` under the heading (`IndicatorMetadataFields.errors` is keyed by field, `indicator-metadata-fields.ts:72`).
- **Read-only:** a version viewed lists its labels as text, after the format.
- The test's dashboard view (D7) uses the labels as typed.

*Alternative:* a separate dialog for column names. Rejected: the details are where the label, unit and format are typed, saved and versioned together.

### D9. The widget form

- A "Column names" group after the color and scale fields, collapsed into a disclosure while the widget has no labels of its own, lists the columns the widget shows: the measure when one is named, or the numerator and denominator; the group column (not for a stat tile); the series column (for the kinds that take one). Each field's placeholder is `columnLabels(indicator, null)(column)`, so a member sees the indicator's label and can override it. The map's tooltip label fields take the same placeholder.
- A `labels` signal is seeded from `start.display?.labels`. `displayDraft()` carries it with color, ramp and orientation, so a widget with none of the four still saves no `display`.
- **Unchanged save:** while the analyst has not typed in a label field and has not changed the measure, ratio, group or series column, the draft holds the stored labels as they were, so a widget saved by an AI client with labels for columns it does not show saves its stored text again (`dashboard-versions` "Widget form saved unchanged"). From the first such change, the draft holds only the labels of the columns the widget shows, so a replaced column's label is dropped and the 10-label limit cannot be passed through the form. A change of chart type alone is not such a change: the labels stay, as `dashboard-indicators` "Column labels of a widget" requires (a column chart turned into a stat tile keeps its group column's label, unused, until the group is changed).
- The preview's query key leaves `display` out (as for color), so typing a label redraws with no request. `fieldFor` maps `display.labels` and `display.labels.{column}` to the group and its field.

### D10. Emails and workbooks (`Reports/`)

- `IndicatorMeta` (`ReportDataBuilder.cs:70`) gains `Columns`, filled by `MetaOf` from the current version (`:76-77`); a removed indicator has none.
- `BuildWidgetAsync` builds `names = ColumnLabels.For(meta.Columns, widget.Config.Display?.Labels)` once and passes it to `Plotted` and to every table: the group header of `AxisTable`, `SeriesReportTable`, `PieTable` and `MapTable` (`names(config.GroupBy)`), the box plot's (`config.GroupBy is null ? text["group"] : names(config.GroupBy)`), the map's tooltip headers (own label, else `names(column)`), and the histogram's "Rows of {caption}". The workbook writes the same `ReportColumn` headers, so it follows.
- **Widget workbook request:** `project-indicators.ts` sends `config: { ...queryFor(type, config), ...(labels ? { display: { labels } } : {}) }`, and `WidgetExportRequest.config`'s doc (`indicators.service.ts:136`) says so. Without labels the request is byte for byte today's. `CheckConfig` accepts labels on every kind, so the export validates them as a save does. A version's widget sends that version's labels. This is the smallest step that carries labels; `add-widget-context` later replaces it with the saved config, which holds the labels too.

### D11. MCP (`Mcp/`)

- **`save_script`:** a new argument `columns` (`IReadOnlyDictionary<string, IndicatorColumnMetadata?>?`), described as "Optional, for an indicator: { column: { label } } names columns of its table for people, 1 to 80 characters, at most 100 columns, keyed by the column's name in lower case. Left out, the current version's labels are kept; {} clears them; given, it replaces them all." The tool's description adds that `columns` is kept when left out, unlike the label, description and unit. Ignored for script 1 and script 2, as `label` is.
- **Reads:** `get_script` and `save_script`'s result return `columns`; `list_indicators` returns each indicator's `columns`; `get_indicator_columns` each column's `label` (D4). The descriptions of `get_script`, `list_indicators` and `get_indicator_columns` name what they now return.
- **Widgets:** `WidgetDisplay.Labels`' `Description` gives the rule; `DisplayHelp` opens with `config.display = { color, ramp, orientation, labels }` (the sentence `McpToolCatalogTests.cs:98` pins) and gains "labels = { column: name } gives up to 10 of the widget's columns a name of 1 to 80 characters, on every kind; left out, a column takes its indicator's label (save_script columns), else its own name."; the `create_widget` template becomes `display: { color, ramp, orientation, labels }`, and `query_indicator`'s names `display.labels`; the `config` arguments of `create_widget` and `update_widget` (`McpDashboardTools.cs:354, 376`) say "(color, scale, orientation or column labels)"; `WidgetConfigInput.Display`'s description names the column labels, keeping the pinned prefix "How the widget is drawn".
- **Widget data:** `get_widget_data` and `query_indicator` add `labels` to each widget's result: an object from each column the widget names (the measure when named, the numerator and denominator, the group column of the query, its series column, and the map's tooltip columns without a label of their own) to the name shown, for the columns whose name shown differs from the column. `IndicatorInfo` (`McpDashboardTools.cs:574`) gains the indicator's `Columns`, filled by `MetaAsync`, and `labels` is built with `ColumnLabels.For`, as the email's names are. It is null, so left out, when no column has a label. A dashboard with no labels returns exactly what it returns today, so the 60,000-character budget is not touched; with labels it costs a few dozen characters per widget. A widget's result names no column next to its rows, so one map per widget is where the name of each column goes, at the smallest cost (see the alternative below). `get_widget_data`'s description names `labels`.
- **Guide:** `Instructions.md:20` becomes "… saved with a label, a unit, a format (…) and, in `columns`, labels for the columns people read, such as `numerator` and `denominator`."; the "Colors" bullet on `display` (`:43`) names `labels` and that `update_widget` replaces them with the config. `system.md` does not change, so `AssistantPrompts.Version` stays.
- Pinned texts change in the task that changes them: `McpToolCatalogTests` (the `DisplayHelp` and template sentences), `McpGuideTests` (Instructions), `IndicatorDocsTests` (`docs/mcp.md`).

*Alternative:* `columns: [{ role, column, label }]` for every column of every widget in `get_widget_data`. Rejected: about 220 characters per widget, so about 9,700 for the 44 widgets of Teatro even with no label set, out of a 60,000-character budget that large dashboards already reach.

### D12. Settings listed generically

`dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard" and `dashboard-indicators` "Moving a widget to another dashboard" named each widget setting (color or scale, orientation…), so every new display setting had to modify all three. They now say "every setting the widget is saved with (title, chart type, query, display settings, the dashboard filters it ignores, position and size)" and "the dashboard's settings (name, filters with their defaults, palette, colors kept by value and its other display settings)", and keep every scenario. This matches the code: versions, duplicates and moves copy the stored config whole (Context). `docs/indicators-dashboard.md` says the same, and the sentence `IndicatorDocsTests.The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them` pins (`IndicatorDocsTests.cs:394`) changes with it.

### D13. The production-shapes fixture

- `tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json` holds one entry per shape the study's dashboards use (proposal), each with invented indicator names, columns and values that keep the structure (`encuesta_hoy`, `zona`, "Zona 1"…"Zona 7", "1. Opción A", `orden` "01"…"21"): `name`, `chartType`, `columns` (the table's columns and types), `stored` (the config text as the API stores it), and the goldens `query` (`WidgetQuery.For` as JSON), `plan` (the SQL texts and parameter names of `IndicatorQueryBuilder`'s plan), `saved` and `uiQuery` (the UI's `savedConfig` and `queryFor`), and `formSaved` (what the widget form sends when it is saved unchanged). One `dashboard` entry holds a dashboard's settings (*Earth*, six text filters without defaults, colors kept for numbered values) and its golden version `content`.
- **Shapes:** a ratio column chart sorted by `orden` as text with an `equals` filter and a color; a measure column chart with a series in `percent` over counts; a stacked bar over counts with a series, sorted by value, with an `in` filter on its series column; a bar with an `in` filter on its own group column; a widget with neither measure nor ratio; a bar sorted by `orden` compared as a number; a stat tile with `equals` filters on numbered labels and on `orden`; a stat tile with `ignoredDashboardFilters`; a line over text prices with a series and no sort; a box plot grouped by a column that has kept colors; the map with four tooltip columns (two labelled, one with a format) and a basemap; a heat map with a ramp; a Likert widget with its scale; a cross table; a pie.
- **`stored` is the text the API writes** (the `WidgetConfig` serialization), not the text PostgreSQL gives back: `jsonb` reorders keys and adds spaces, and the API compares configs as it reads them (Context), so the test runs without a database.
- `ProductionShapesTests.cs` (API unit): `stored` reads and writes back byte for byte, `WidgetQuery.For` and the plan equal their goldens, `formSaved` reads back to `stored` (an unchanged form makes no version), and the version content equals its golden. `production-shapes.spec.ts` (UI): `savedConfig`, `queryFor` and the form's unchanged save equal their goldens.
- The goldens are written from the code before this change (group 1 of the tasks), so the test shows the change moved nothing. Every later change of the program adds its shapes, regenerates no golden, and runs the test.

### D14. Documentation

- `docs/scripts.md` "The indicator's name and metadata": a **Column names** row in the table (rules, limits, the keep rule), and that restoring a version brings the labels back.
- `docs/indicators-dashboard.md`: a section "Column names" under "What a value means: format and unit" (indicator labels, a widget's labels, precedence, where they show and where raw names stay, the example of `numerator` and `denominator`); a sentence in "Show as table", "Tooltip and tooltip columns", "Exporting a widget" and "Printing a dashboard"; the generic lists of D12 in "Dashboards" (Duplicate), "Moving a widget to another dashboard" and "What makes a version"; "Limits": 100 columns per indicator, 10 per widget, 80 characters.
- `docs/mcp.md`: `save_script`'s `columns` and its keep rule, `display.labels` (the tools row at `docs/mcp.md:77` and the `display` bullet at `:89`), `labels` in widget data, `get_indicator_columns`' `label`, and the `widgetChanged` row (`:113`) naming column labels.
- `docs/operations.md` "What leaves the deployment by email" (`:678`): the column labels, which members write, are among what an email carries.
- `docs/tamiza-brief.md`: the data model sentence on indicator metadata (`:180`), and section 8 lists the program's changes in place of item 7l (`:329`).

## Spec ownership

This change MODIFIES only requirements the program assigns to it:
- `pipeline` "Indicator metadata": it names the column labels and gains a scenario.
- `dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard", `dashboard-indicators` "Moving a widget to another dashboard": rewritten generically (D12), every scenario kept, one scenario added each.
- `dashboard-reports` "Widget tables in a scheduled email": one sentence on the headers and one scenario.

The mcp-server requirements on saving scripts are also assigned to it, but it leaves them as they are and ADDS "Indicator column labels through MCP" and "Widget column labels through MCP", so later changes that touch those requirements rebase nothing. Every other requirement is ADDED under a name that holds "column labels" or "Column names", which no other change of the program uses. No requirement it adds is one another change of the program owns, and it contradicts none: `dashboard-indicators` "Map tooltip columns" still holds (a column with no label of its own showed its name, and still does while its indicator labels it not). Later changes that add properties to the map refer to "Column labels of an indicator" and "Column names in the indicator's details" by name and ADD their own requirements; in the code they extend `IndicatorColumnMetadata` and `ColumnMetadata.Check`, which `ScriptEndpoints.ValidateMetadata` calls (the place `add-declared-category-order` and `add-small-sample-warnings` name).

## Production impact

Of the production behaviors the program lists:
- **Captions of the 142 ratio and 38 measure widgets** keep `numerator / denominator` and `value` until someone labels a column; no indicator or widget has labels after the deploy.
- **Stored configs and versions:** no config gains a key, so `WidgetDisplayTests` and the production-shapes test hold, no version is made, and restores and duplicates are unchanged.
- **Numbered labels in filters and colors kept by value** (`"1. No pagaría"`, `"1. Sí"`, `"2. Maneja moto o carro"`) are values, not columns; labels name columns, and filters and colors keep matching raw names.
- **Sorts by `orden`, `in` filters on group columns, `ignoredDashboardFilters`, the implicit `value` of the two legacy dashboards:** no query changes (D5, D6).
- **The map's tooltip columns** keep their own labels; the ones without a label show their column until the indicator labels it.
- **Scheduled emails and workbooks** of the 9 dashboards read the same until labels exist. The widget workbook request is the same bytes for a widget with no labels.
- **Images and print** draw the same captions and tables until labels exist: with no label, every namer is `rawNames`.
- **REST responses** gain `columns: null` on script versions and indicator entries; the UI reads a missing or null map as none, and no stored text holds it.
- **MCP:** the 46 indicators written through `save_script` keep the labels members type, because a call without `columns` keeps them. Results gain `columns` and `labels` only where labels exist.

## Risks / Trade-offs

- **[A long caption]** An indicator label of 120 characters and two labels of 80 make a caption that wraps over several lines and takes height from the plot. → Captions wrap as they do today, with nothing clamped, so existing widgets look the same. A widget's own labels give a short name, and `add-widget-context` adds a caption of the author's own.
- **[Two columns with the same label]** ("Hogares / Hogares") → Allowed, as two widgets may share a title. The form's hint and the docs show the ratio example.
- **[A widget's label hides a later change of the indicator's]** → The form shows the indicator's label as each field's placeholder, so a member sees what the widget overrides. The docs give the precedence.
- **[`update_widget` and older clients drop `display.labels`]** → As every display setting today: the descriptions and `Instructions.md` say to send `display` again; the UI and the API are deployed together.
- **[Keep on `save_script` differs from label, unit and description]** → Said in the tool description and `docs/mcp.md`; the alternative wipes members' names (D3).
- **[A label that looks like a formula in a workbook header]** ("=Hogares") → MiniExcel writes headers as text, as it writes text cells (`widget-export` "Numbers in a widget workbook" says text is never a formula); a test writes the header "=Hogares" and reads it back as text.
- **[Headers that collide]** A label can make two headers equal where two column names were not, in one sheet or across the email workbook. → `WorkbookSheets.Headers` (`WorkbookSheets.cs:17-38`) already makes a header unique in its sheet ("(2)") and, where two sheets need it with different number formats, adds the widget's position, so nothing new is needed.
- **[A restore of an older script version brings back older labels]** → Intended: the labels are metadata of the version. The docs say so.
- **[The final projection of `ToDetails` reads JSON on the client]** → It is the top-level `Select`, which EF evaluates on the client; an integration test reads labels through every `ToDetails` path.

## Migration Plan

1. **Deploy.** `0021_add_indicator_column_metadata.sql` adds the nullable column and its check. It rewrites no script version, no widget config and no `dashboard_versions.content`, so the `dbUp/README.md` rule on rewriting version content does not apply. Every indicator reads as labelling no column, and every widget draws as before.
2. **Rollback.** An older API:
   - ignores `script_versions.columns`; its saves write NULL there, so an indicator saved by it has no labels from then on, while its earlier versions keep theirs (restoring one on a newer release brings them back);
   - reads `display.labels` through its `WidgetConfigInput` and drops it, never refusing it; a display with only labels reads as no display, and the next save of that widget writes the config without them. A widget saved unchanged writes nothing, because both sides of the comparison lack them;
   - keeps the stored text inside versions, so a restore on a newer release brings the labels back.

   The column and its check stay and are harmless to older code: DbUp never runs scripts backwards.

## Open Questions

- Whether the "Indicators (N)" list under the widgets and the widget form's column selects should show the column labels next to the names. Neither changes the specs of this change, and either can be a later change.

## Implementation Results

### Suites (2026-11-04)

- API, unit: 1992 passed, 0 failed.
- API, integration: 1992 of 1993 passed and 2 known clean-tree failures (`IndicatorSeriesQueryTests.A_ratio_*`, decimal comma; they pass with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`). One test of this change failed on its own wording (`The_openapi_document_describes_the_column_labels_of_an_indicator` looked for "10 columns" while the description says "Up to 10 of the widget's columns"); the assertion was fixed and the test passes. Do not run the suite with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: it turns 107 culture-dependent unit tests red.
- UI (`TZ=UTC npx ng test --watch=false`): 2936 passed, 2 failed, both known polling tests (`project-data.spec.ts` "stops polling when the page is destroyed while a run is active", `project-scripts.spec.ts` "polls while a run is active and stops when the page is destroyed"). The unhandled `project-performance.ts` error is pre-existing. `production-shapes.spec.ts` passes with its goldens untouched.
- Analytics (`tamiza-analytics-test:dev`): 620 passed, `test_docs_examples.py` included.

### Deviations reported by the implementation

- ProductionShapesTests repeats `DashboardEndpoints.ReadConfig` step by step in a private helper, because that method is internal and the unit-test project has no `InternalsVisibleTo`.
- The Column names rows use `<tmz-form-field>`, so a field is labelled by the column's name as plain text (not `<code>`); "Not in the table" is the field's hint.
- A read-only view of a version lists the names in the page's `<dl>` ("Column names", `data-testid indicator-meta-columns`).
- `TooltipContext.names` and the `names` parameter of `tooltipLabel` are optional (default: the raw names), so existing map-tooltip specs are unchanged.
- The widget draft keeps the stored labels whole until a label is typed or the measure, ratio, group or series column changes; `savedConfig` and `displayFor` drop blank labels.
- Likert and pyramid renamed their private `names` computed to `seriesNames` / `sideNames`.
- `IndicatorSave` is a new type for the save body; the assistant request omits column names.
- `docs/indicators-dashboard.md` links `dashboard-reports.md`, which does not exist (pre-existing, not touched).

### Real-stack check (task 9.4, 2026-11-04)

Run in the Compose project `tamizaverify` (own env file, ports 8189 to 8192, fake OIDC, fake Kobo with 40 invented submissions, Mailpit, headless Chrome over CDP); the developer's own stack was untouched and everything was removed afterwards. No defect caused by this change was found and no source or test was changed.

- Migration 0021 applied on a database holding data (schema 0020, a project with a run; the existing row kept `columns` NULL) and on a fresh one.
- The Column names section lists the table's columns before and after a test, keeps its labels after a run and a reload, always sends `columns` on save, and `{}` clears them.
- The widget form shows the indicator's labels as placeholders and stores only what is typed under `display.labels`.
- At 1,280 and 390 px, light and dark, there is no horizontal page scroll. Widget labels win over the indicator's, which win over the raw name, in captions, the stat tile description, aria summaries, "Show as table" headers and the map's tooltip columns.
- Exports: PNG and SVG captions, workbook data and Summary sheets, and A4 print emulation (2 pages) carry the labels. The scheduled email (plain text, HTML and workbook) carries them too; a widget whose columns have no label keeps the raw names.
- MCP: `save_script` without `columns` keeps the labels; with `columns` it replaces them; `{}` clears them. `get_widget_data` returns a `labels` map and, with it removed, equals the baseline JSON. Errors name the right key.
- Restoring a script version brings back that version's labels (as a new version); restoring a dashboard version brings back or removes the widget's own labels.

Findings:
- Pre-existing, low: the widget workbook request drops a map's tooltip item labels and formats (`queryFor` sets `label: null, format: null`), so the map's XLSX header reads "Total de hogares" where the page and the email read the item's own label. D10 only promises `display.labels`; consider it in `add-widget-context`.

Not checked: the map's PNG and SVG export (the sandbox has no internet, so no tiles), the app's own Print button (`window.print()` is not usable headless), a screen reader, the add-indicator dialog (covered by unit specs) and the real Keycloak.
