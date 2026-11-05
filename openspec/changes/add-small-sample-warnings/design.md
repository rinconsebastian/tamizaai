# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change builds on `add-indicator-column-metadata` and `add-widget-context`, both implemented and archived on 2026-11-04. From the first it uses the per-column metadata map (`script_versions.columns`, migration 0021; the record `IndicatorColumnMetadata` and the static classes `ColumnMetadata` and `ColumnLabels`, all in `Indicators/ColumnMetadata.cs`), the "Column names" section of the indicator's details, `save_script`'s `columns` argument, the names shown for columns and the production-shapes fixture. From the second it uses the widget's subtitle, footnote, own caption text and unit, and the dashboard's source (migration 0022). The pieces it works on:

- **A query returns only the plotted value.**
  - `IndicatorQueryBuilder.Plan` builds `aggregate` (`sum(m)`, or `sum(num) / nullif(sum(den), 0)`) and `plotted` once for every path.
  - The plain statement selects `group, value, geometry` and, for a map only, `count(*)` and the tooltip outputs (`extra` in `Plan`).
  - The series cells statement (`SeriesQueryBuilder.Build`) is one `UNION ALL` of group totals, cells, series totals, the grand total and the rows left out (`SeriesParts`), each branch with the same seven columns.
  - The readers use fixed ordinals: `ReadRowsAsync` reads the map's count at 3 and its tooltip columns from 4; `ReadSeriesAsync` reads columns 0 to 6 of every branch (both in `IndicatorDashboard.cs`).
- **No response carries a base.** `IndicatorRow` has `Rows` (a count of table rows) only for maps, series cells and boxes; the series totals (`GroupTotal`, `SeriesTotal` and `GrandTotal` in `IndicatorSeries.cs`) carry `Rows` too. No row carries the numerator or the denominator.
- **The query knows nothing of the indicator's metadata.** `IndicatorDashboard.QueryAsync` validates, locks and describes the table (`DataDashboard.LockAndDescribeAsync`) and plans, in one read-only transaction on the application database (`InTransactionAsync`). `GetPublicationsAsync` already reads `tamiza.run_steps` through the same helper, so a query can read `tamiza.script_versions` too. The current version of an indicator is the newest by number, when its code is not null (`ScriptEndpoints.CurrentIndicatorsAsync`; `IndicatorEndpoints.CurrentColumnsAsync` reads its column map with `ColumnMetadata.Read`): a removed indicator has none.
- **Configs.**
  - `WidgetDisplay` has nine members, `Color`, `Ramp`, `Orientation`, `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription`, each written only when set; its `Normalize()` returns null when all nine are unset. Its UI mirror is `WidgetDisplay` in `indicators.service.ts`.
  - `WidgetConfigInput` → `Normalize()` → `WidgetConfig`.
  - `CheckDisplay` (`IndicatorValidation.cs`) refuses a field on a kind that does not take it.
  - The query twins are `WidgetQuery.For` and `queryFor` (`widget-view.ts`). `displayFor` and `savedConfig` keep the display fields per kind (`displayFor` reads the nine members by name). The form rebuilds the config from its signals (`formConfig` and `displayDraft` in `widget-dialog.ts`).
- **Dashboards.**
  - The entity has `Filters`, `Palette`, `ValueColors` and `Source` (`Data/Dashboard.cs`), mirrored in `TamizaDbContext`, which also names the check `ck_dashboards_source`.
  - Colors are saved by `PUT …/colors` (`DashboardEndpoints.SaveColorsAsync`) and the source by `PUT …/source` (`SaveSourceAsync`). Both are returned in `DashboardView` (`Source`, its last member, is `WhenWritingNull`) and copied by `DuplicateAsync`.
  - A version's content is `DashboardVersions.Content(name, filters, palette, valueColors, source, widgets)`: `palette` and `valueColors` are written only when they are not the defaults, and `source` only when there is one. `ReadContent` reads a missing key as its default into `VersionContent`, and the restore writes them back. The change kinds are `DashboardChangeKinds` (`SourceChanged` was the last added) and `dashboardChangeKinds` in `indicators.service.ts`.
- **Tooltips.**
  - A chart with a series uses `markTooltips` (`mark-tooltips.ts`): one Tab stop, `role="application"`, arrows, Home, End, Escape and a live region. Its content is built by the `content` function of the plot in `series-plot.ts`.
  - The plain layout of `axis-chart.ts` has no hit areas (it is the layout with `hits: []`): its marks carry a native `<title>` (bars, columns, lines and dots), and its SVG is `role="img"` unless there is a series. The test "describes itself to assistive technology and offers the same numbers as a table" of `chart-widgets.spec.ts` pins the `img` role.
  - A pie's slices also carry a `<title>` under `role="img"` (`pie-widget.ts`).
  - Specs read those native titles to check values and order: the `titles` helper of `chart-widgets.spec.ts` (about 20 tests, on bars, dots, histogram bars and pie slices) and the test "plots the ratio of each group when asked…" of `script-editor/indicator-view.spec.ts`, whose test view also draws with `AxisChart` (`script-editor/indicator-view.ts`).
  - A heat map already hatches its **empty** cells: a pattern of diagonal hairlines over the surface color, defined in the chart's `<defs>` with an id per component (`hatchId`) and used by the empty cell's `<rect>`. The pattern is a vertical line with `patternTransform="rotate(45)"`, so its hairlines run from bottom-left to top-right.
  - `buildSeriesTable` drops each cell's own counts. Its C# twin `SeriesTable.cs` shares the fixture `indicators/testing/series-tables.json`.
  - The map's tooltip is the group, the plotted value (with the widget's own caption text and unit when it has them, since `add-widget-context`), the tooltip columns and "Combines N rows" (`tooltipContent` in `map-tooltip.ts`); its shapes are styled by `polygonStyle`, `markerStyle` and a class per step in `map-engine.ts`.
- **Notes and texts.**
  - widget-view draws "Not filtered by" and the row limit above its chart. The chart's box follows, cut at its own box while the widget has a footnote. The footnote closes the tile (`footnoteLine`: one line on a tile two rows high or less, else two, whole on paper). `exportNotesAbove` gives an image the same notes above.
  - An image is composed by `composeImage` (`widget-export/image-layout.ts`) from an `ImageInput`: the title, the subtitle, the notes above (`notesAbove`, filled by `exportImage` in `project-indicators.ts`), the caption, the plot or table, the legend, the notes below (the table's, `parts.notes` and `notesExtra`), the footnote and the footer. It draws a stat tile's figure itself, as text at 36 px (`improve-stat-cards` sizes it later), not from the tile's DOM.
  - The email's notes are built in `ReportDataBuilder.ReadWidgetAsync` and shown in the body above the widget's table (`ReportHtml.Block`), with the footnote last; `BuildWidgetAsync` adds the subtitle, the footnote and whether the caption is hidden. The workbook's "Summary" joins the notes on its "Notes" line, after its "Footnote" line (`ReportWorkbook`, `WidgetWorkbook`).
  - The widget workbook request sends `savedConfig(widget.chartType, widget.config)` since `add-widget-context` (`exportWorkbook` in `project-indicators.ts`), and `WidgetExportEndpoints.ExportAsync` validates that config as a save does. The endpoint reads the dashboard's, or the shown version's, name, filters and source itself.
- **Reference lines** (`add-chart-annotations`, earlier in the program, not implemented yet): the twins `referenceLineValues(type, config, answer)` (`indicators/reference-lines.ts`) and `ReferenceLines.Of(chartType, config, response)` (`Indicators/ReferenceLines.cs`) give the lines to draw, with a shared fixture `indicators/testing/reference-lines.json`. Its overall value is asked for by a flag of the query request, `IndicatorQueryRequest.Overall`, derived from the saved config by `WidgetQuery.Overall` and `overallFor` and never stored (its D4), and its statement is the stat tile's (`IndicatorQueryPlan.Overall`). Its D12 leaves to this change the rule for an `overall` line over the answers of one question, and its D4 asks that `missingFooter` reach that statement.
- **MCP.** `query_indicator` and `get_widget_data` write rows with `Row()`, series with `SeriesPayload` and widgets with `WidgetDataAsync`, in `McpDashboardTools.cs`. A widget's header there already carries `labels`, `subtitle`, `footnote` and `caption`, and `MetaAsync` gives it each indicator's `IndicatorInfo` (label, unit, format and column map). The help texts are consts at the top of that file (`DisplayHelp`, `ContextHelp`, `SourceHelp` and `ChartTypeChangeHelp`, with `AnnotationsHelp` and `StatHelp` to come), pinned by `McpToolCatalogTests` and `IndicatorDocsTests`. `ColumnsHelp` in `McpScriptTools.cs` describes `save_script`'s `columns`. `update_dashboard` takes `name`, `position`, `filters`, `palette`, `valueColors` and `source`.
- **Production** (Duitama, read-only survey on 2026-11-03): see "Production impact".

## Goals / Non-Goals

**Goals:**
- One definition of n, computed in SQL and returned with every value, so the screen, images, print, emails, workbooks and MCP read the same figures.
- Nothing changes for a widget or a dashboard until someone sets a base setting, apart from richer tooltips and the three figures a rate's rows gain in the API and MCP answers. A measure widget with no base column sends the same SQL and gets the same JSON as today.
- n is never silently wrong: where the engine cannot count a widget's units once, it shows no widget n and says why.

**Non-Goals:**
- Changing any plotted value. The `in` guard over declared answers is `improve-stat-cards`' work.
- Weights, and a weighted n (`add-weighting`).
- An n for a histogram's bars, a box plot's widget n, a pie's folded "Other" slice.
- Leaving out no-data values on a map, a box plot, a histogram or a stat tile.
- A per-viewer switch for dimming: it is a saved dashboard or widget setting.

## Decisions

### D1. Base metadata in the indicator's column map

`add-indicator-column-metadata` stores a map keyed by column name on each indicator version (`script_versions.columns`, applied by migration 0021), which today holds a `label` for each column. This change adds three optional properties to an entry:

```json
{
  "variable":  { "categories": "multiple" },
  "orden":     { "categories": "multiple" },
  "categoria": { "label": "Respuesta", "categories": "multiple" },
  "comuna":    { "label": "Comuna", "missing": ["Sin dato"] }
}
```

(`teatro_hoy`: its `variable` column holds both single-choice questions, such as E1, and multiple-choice ones, such as E4, so `multiple` is the safe kind; a table with a base column would add `"hogares": { "label": "Hogares", "base": true }`.)

- **C#.** The map's entry record, `IndicatorColumnMetadata` (`Indicators/ColumnMetadata.cs`), gains `Base` (`bool?`), `Missing` (`IReadOnlyList<string>?`) and `Categories` (`string?`), each `[JsonIgnore(WhenWritingNull)]` with a `Description` for the MCP schema, appended after `Label` and after the properties that earlier changes of the program append (`Order`, `Last` and `StripPrefix` of `add-declared-category-order`). `ColumnMetadata.Normalize` maps `base: false`, an empty list and an empty kind to null, and keeps an entry that has any property left: today it keeps only an entry with a label and builds it from the label alone, so it must now keep the whole entry. An entry with only a label keeps the JSON `add-indicator-column-metadata` writes, and an entry with nothing left is dropped as before. Constants `ColumnMetadata.MaxMissingValues = 20` and `ColumnMetadata.MaxMissingLength = 500`, and the kinds in a new `AnswerKinds` class (`Single = "single"`, `Multiple = "multiple"`, `All`) in the same file.
- **Validation** (in `ColumnMetadata.Check`, the column validator that `ScriptEndpoints.ValidateMetadata` calls, keyed as it keys a column's label, `columns.<name>.<property>`):

  | Rule | Key | Message |
  |---|---|---|
  | `base` on two columns | `columns.<second>.base` | "Only one column can be the base: {first} already is." |
  | `missing` with more than 20 values | `columns.<c>.missing` | "A column lists at most 20 values that mean no data." |
  | a `missing` value empty, longer than 500 characters or holding a NUL character | `columns.<c>.missing[i]` | "A value that means no data has 1 to 500 characters and no NUL character." |
  | a `missing` value listed twice | `columns.<c>.missing[i]` | "{value} is listed twice." |
  | `categories` not `single` or `multiple` | `columns.<c>.categories` | "Answers are 'single' or 'multiple'." |
  | `base` with `missing` or `categories` on one column | `columns.<c>` | "A base column counts units: it holds no answers and no values that mean no data." |

  The API checks shape only, because metadata can be saved before the table is published. The engine applies `base` only to a column the published table holds as a number, and `missing` and `categories` only to a text or true/false column; anything else is ignored (pipeline "Base column no longer published").

  `ColumnMetadata.Check` today counts and validates only the columns that have a label (its `kept` list) against `MaxColumns` (100); it now counts and validates every column that has any property (pipeline "Limit of columns with base metadata", so the table of facts cannot grow past the limit of labels), and its message ("An indicator labels at most 100 columns.") is unchanged. The rule for two bases looks at the entries in the order they were sent, since the stored map has no order.
- **Save form.** The "Column names" section of `indicator-metadata-fields.ts` (and `add-indicator-dialog.ts`) lists the table's columns from the published table or the last test, as `add-indicator-column-metadata` does. Its rows are `ColumnDraft` (`pipeline.service.ts`), which gains `base`, `missing` and `categories`, and `column-names.ts` (`draftsOf`, `columnsOf`, `sameColumns`), which today keep only a row with a label, keeps a row with any of them. The section gains a "Base" radio group over the numeric columns, with "None" first. Each other column gets a "Values that mean no data" field (a list of chips, typed and confirmed with Enter, at most 20) and an "Answers of one question" select (none, single choice, multiple choice), whose help says to choose it for the question, code and answer columns of a long table (`variable`, `orden`, `categoria`) and never for a column that splits the households (D3). An older version shows them as text, in the page's list of column names (`indicator-meta-columns`).
- **MCP.** `save_script`'s `columns` argument takes the three properties, with its omitted-means-kept rule: `ColumnsHelp` (`McpScriptTools.cs`) stops saying that a property other than `label` is ignored (an unknown key still is). Given, the map replaces the whole stored one, so a client sends back the properties it read. `list_indicators`, `get_script` and `save_script`'s result return the map, the properties included. `get_indicator_columns` gives each column its `label` as before, and `IndicatorColumns` gains `Declared` (`WhenWritingNull`, appended last): the entries that hold `base`, `missing` or `categories`, by column name, read by `IndicatorEndpoints.GetColumnsAsync` with the labels.
- **The UI reads them** from the column map that `IndicatorEntry` carries (`columns`, since `add-indicator-column-metadata`): the form's hints and the line's names need them; the n itself comes from the API.

*Alternatives:*
- *Columns of their own on `script_versions`.* Rejected: the map exists to hold per-column properties, and it is versioned and restored with the script already.
- *Declared by the script (`df.attrs`).* Rejected, as `add-indicator-column-metadata` rejected it for labels: metadata is entered in the save form and through MCP.
- *A `unit` id column and `count(DISTINCT unit)`.* Rejected: the % tables are pre-aggregated (teatro_hoy, 5,706 rows for 476 households, no household id); only the 3 box plot tables have one.

### D2. The n of a mark

| Mark | n |
|---|---|
| A value of a measure or a ratio, when the indicator declares a base column the table holds as a number | `sum(base)` over the value's rows |
| A value of a ratio, otherwise | `sum(denominator)` over its rows |
| A value of a measure, otherwise | none |
| A segment of a 100% layout, a segment of a Likert bar | its group's n (the base of the share) |
| A total (group, series, all) | the n over the total's rows |
| A box | its number of values |
| A histogram's bar, a pie's "Other" slice (folded by the page) | none |

Rows are counted after the widget's filters and the dashboard's, and the no-data rows a widget leaves out (D6) count in no n. Weights do not exist yet; every n is unweighted by definition.

The share rule is applied by the twins `SeriesTable.cs` and `series-table.ts`: a cell of a table of shares takes its row's n. It is pinned by new cases in `series-tables.json`.

**Decided with the user on 2026-11-04:** without a declaration, a mark's n (its tooltip, its dimming) stays the sum of its denominator; only the widget's n, and so its n line, needs a declaration (D4).

`improve-stat-cards`, released with this change (D17), adds one row to the table: a stat tile with a statistic has the n of a box, the number of values it covers, or the sum of the base column over them when the indicator declares one (its D10, decided with the user on 2026-11-04).

*Alternatives:*
- *`count(*)` of the rows.* Rejected: the rows are cells already summed over households.
- *No n without a declaration.* Rejected: the user decided that a ratio's n is the sum of its denominator, which is right for a mark that holds one answer, the usual case (Teatro 4e00: `COMUNA 7` = 16).

### D3. The base counted once across answers

With columns declared `categories`, an **answer** is one combination of the values of every declared column of the indicator. An n over rows of several answers is the **largest per-answer sum**: group the rows by all the declared columns together, sum the base in each group, and take the maximum. A mark takes it over its own rows, so a column chart by `comuna` takes it per comuna.

**Decided with the user on 2026-11-04:** the base is counted once across answers by the largest per-answer sum, not by the program's first rule (once per cell), and `improve-stat-cards` uses the same rule now for the denominator of a rate over several answers (see "Agreement with `improve-stat-cards`" below).

- In a select-one question, block() puts every household in the denominator of every answer, so each answer's sum is the same n. The maximum is that n even when an answer is missing from some cells, as long as one answer has a row in every cell (block() writes a row for every answer of every household).
- A multiple-choice question repeats the base in the same way, so its n is counted the same way. The kind (`single` or `multiple`) does not change the n: it matters to `improve-stat-cards`, which counts a single choice's denominator once and refuses a multiple choice's `in`.
- **Which columns to declare.** A column qualifies when every unit of the base is counted under each of its values. In a long % table those are the column of the answers (`categoria`) and the columns that name the question or code the answer (`variable`, `orden`, and `intencion` or `barrera_tipo` where they move with the answer). Declaring `orden` with `categoria` changes nothing, since it moves with the answer. Declaring `variable` matters when a widget spans several questions: Comercio 0d62 splits by `variable` in ["Ropa y calzado", "Tecnología"], two questions of where households buy, whose answers (the places) share names, so grouping by `categoria` alone would add both questions' households (952 for 476). A household dimension (`comuna`, `estrato`, `sexo`…) never qualifies: it splits the households.
- **Nested bases.** When the answers grouped have different bases (E1 asked of 476 households, E2 of the 223 who attended), the maximum is the larger, which is right when the smaller base is inside the larger, as in the study's filtered questions.
- The values, totals and orders do not change (dashboard-indicators "Base n counted once across answers").

*Alternatives:*
- *The program's first idea: group by every other column, take each cell's denominator once, sum.* Rejected. Every long-format % indicator publishes `orden` next to `categoria`, and `orden` takes a different value for each answer (27 widgets sort by it). So each answer would be its own cell again, and the n would be k × n as soon as the author forgot to declare `orden` (or `barrera_tipo`), a silent error. The largest per-answer sum is right whether or not those columns are declared; only a widget that spans several questions needs `variable` too.
- *`max(denominator)` per cell.* Rejected for the same reason, and because a cell can hold several rows of one answer.

**Agreement with `improve-stat-cards`.** That change (P1-7, implemented right after this one and released with it, D17) corrects the value of a ratio over several answers of declared `single` columns with this rule: each value or total that spans several answers divides the sum of its numerator by the largest of its per-answer denominator sums, taken over its own rows (per group, series value or cell), and refuses a sum over several answers of a `multiple` column (its D4a and D4b). It groups the answers with the same helper as `BaseQueryBuilder` (every declared column, grouped together), so a value's denominator and its n are counted over the same answers and never disagree. Its advice on which columns to declare is this change's (D3 above): the question, code and answer columns of a long table.

### D4. When a widget has an n

A widget has an n (for its line, MCP's `n` and the email) only when its marks have one and **its indicator declares a base column or an answer column**. The n is then counted over every row it plots, as D2 and D3 count a mark's. A histogram and a box plot have none. A stat tile is one mark, but its row can span several answers (no filter, or an `in` filter, on `categoria`), so it follows the same rule: its row carries its mark's n (D2), which decides its dimming, and the response's `n`, which its n line reads, needs a declaration. `improve-stat-cards`' `n` line inside the tile reads that same response `n` (its D3), so without a declaration a tile that sums its rows shows no n, and that change's form hint gives this rule's reason. Who draws the line: this change shows a tile's n line under the tile like any widget's (D11). `improve-stat-cards` (its D3) then draws that one line inside the tile, once: it shows when the tile's `display.secondary` holds `n` or when the line is in force for the tile (`display.showN`, else the dashboard's `showN`), and widget-view draws no second n line under a stat tile. The small-base note stays widget-view's. A tile with a statistic, which that change adds, is the exception: its values are rows, as a box's are, so its n is their number, or the sum of the base column over them, with or without a declaration (its D10).

**Decided with the user on 2026-11-04:** the n line ("n = …") is shown only when the indicator declares a base column or its question and answer columns; without them a mark keeps its n (D2), and the widget has none.

Without a declaration the widget n would be the sum of its marks' n, and that is wrong exactly when the group column holds the answers of one question: a bar chart of the 5 answers of a question of 476 households would read n = 2,380. 57 production widgets group by `categoria`. The engine cannot tell `categoria` from `comuna` without the declaration. So:
- **Long % table** (teatro_hoy): declare the answer column and the columns that name the question or code the answer (`variable`, `orden`, `categoria`), as D3 says; never a household dimension.
- **Count table** (teatro_conteo: `pregunta`, `respuesta`, households in `value`): declare `value` as the base. Each household is under one answer of a single-choice question, so the answers add up; a widget over two questions (Comercio Likert e278, 952 for 476) also needs `pregunta` declared, since every household is under each question.
- **Wide table** (one row per municipality): declare the denominator itself as the base. That says the rows count units once.

The widget form says which applies when the n line is asked for and the widget has none (spec "Widget without a base n").

The box plot's rows are units, but production copies every household of `centro_tarifa_hogar` into a "Todos los vehículos" box (176 rows for 88 households), so a summed widget n would be double. Its boxes still have their own n (D2).

*Alternatives:*
- *The program's suggested rule: no widget n when a ratio widget spans several values of a column that is neither its group nor its series, and no answer column is declared.* Rejected:
  - the household dimensions (comuna, estrato, sexo…) always span several values, so no widget would ever have an n;
  - the rule misses the common wrong case, where the group column itself holds the answers.
- *Always show the sum of the marks.* Rejected: a wrong n is worse than none.

### D5. The engine

**Metadata read in the query's transaction.** `QueryAsync` reads the `columns` of the widget's indicator's current version, the one `CurrentIndicatorsAsync` takes (the newest by number, when its code is not null; a removed indicator has none), with one statement in its read-only transaction before planning, read with `ColumnMetadata.Read` as `IndicatorEndpoints.CurrentColumnsAsync` reads it for the columns endpoint. It resolves the map against the described table into `IndicatorBases(BaseColumn?, Answers[], Missing{column → values})`, which `Plan` takes as a new optional parameter, after the `overall` argument that `add-chart-annotations` adds. Every consumer (the page's query endpoint, the email, the widget workbook, MCP) goes through `QueryAsync`, so all of them count the same n. The declarations apply as soon as a version is saved, over the table the last run published, as the column names do.

**Outputs.** `IndicatorSql` gains `Figures`, the ordinals of the outputs below. They are **appended after every output the statement has today** (after a map's count and tooltip outputs), so today's select list is a prefix of the new one: the readers keep their ordinals (`ReadRowsAsync` reads the count at 3 and the tooltips from 4), and a plan differs from today's only by the outputs at the end of each select list. Absent outputs have no ordinal.
- For a ratio: `sum(num)::double precision`, `sum(den)::double precision`.
- With a base column: `sum(base)::double precision`.
- For the widget n when D4 allows it and no answer column is declared: `sum(sum(<n expression>)) OVER ()`. The window runs after `HAVING` and before `LIMIT`, so the widget n covers every plotted group, past the row limit too.
- **A measure with no base column adds no output.** Its SQL text, and so its response JSON, are those of today. `IndicatorQueryBuilderTests.A_grouped_query_that_is_not_a_map_has_no_extra_outputs` and `IndicatorQueryTests.Every_other_kind_answers_what_it_answered_before_with_no_domain` keep passing unchanged, since both use measures.
- **A histogram's per-row statement and a box plot's statement take no output.** A histogram's bar has no n, and a box's n is the number of values it already returns.

The stat tile's ungrouped statement takes the same outputs: its one row carries its mark's n (D2), which decides its dimming, and the response's `n` follows D4 like any widget's.

Every branch of the series cells statement appends the same outputs, and the "left out" branch NULLs. The series domain and the pie domain statements do not change, except for D6.

**Answers declared** (`BaseQueryBuilder.Counted`). A second statement in the same transaction computes the n of every mark, total and the widget as D3 says. It uses the same `FROM`, row filter, filters and parameters as the plotted statement, and one `UNION ALL` part per kind of mark. A plain widget:

```sql
SELECT 1 AS part, g, max(s) FROM (SELECT {g}::text AS g, {a1}, {a2}, sum({b}) AS s FROM {from} WHERE {rowFilter}{filters} GROUP BY 1, 2, 3) x GROUP BY g
UNION ALL
SELECT 2, NULL, max(s) FROM (SELECT {a1}, {a2}, sum({b}) AS s FROM {from} WHERE {rowFilter}{filters} GROUP BY 1, 2) y
```

A series widget has the parts group, cell (g, kind, series), series and all. `{b}` is the base column, else the denominator. The C# reader matches each part by its keys (the group text, timestamps through `DataValues.IsoTimestamp` as `ReadRowsAsync` writes them, and the series kind and value). It replaces the plain sums of the plotted statement, so no SQL join uses `IS NOT DISTINCT FROM` (box plot D3's lesson). Indicators without a declared answer column never run it. The answer grouping (every declared column, grouped together) is one helper of `BaseQueryBuilder`, which `improve-stat-cards`' corrected ratio uses too (D3).

**No derived request flag.** `add-chart-annotations` (its D4, decided with the user on 2026-11-04) sends a flag derived from a saved widget as a flag of the query request, never in a config. This change has none to send: `missingFooter` is a stored setting that changes the rows (program rule 2), the figures are returned with every query, and the dashboard's settings never reach the query. So the answers statement runs for every widget query over an indicator that declares answer columns, whatever the settings are, because tooltips and MCP always return the n (task 13.2 measures its cost).

**Response.**

```json
{
  "published": true, "rowLimitReached": false, "missing": { "indicator": false, "columns": [], "any": false },
  "rows": [ { "group": "COMUNA 7", "value": 0.5, "geometry": null, "numerator": 8, "denominator": 16, "n": 16 } ],
  "n": 476,
  "noData": { "values": ["Sin dato"], "value": 1, "numerator": 1, "denominator": 1, "n": 1, "share": null }
}
```

- `IndicatorRow` gains `Numerator`, `Denominator` and `N` (`double?`, `WhenWritingNull`, appended after `SeriesKind` so every constructor call stays valid). `GroupTotal`, `SeriesTotal` and `GrandTotal` gain the same three.
- `IndicatorQueryResponse` gains `N` (the widget's n under D4) and `NoData` (D6), both `WhenWritingNull`, appended after `Boxes` (and after the `Overall` that `add-chart-annotations` appends last).
- A box plot's rows keep `rows` = values; `n` is not repeated on them.
- The TS types in `indicators.service.ts` mirror them (`QueryRow`, `QueryTotals`, `QueryResult`).

**Cost.** The extra sums are read in the same scan. The answers statement scans the table once more, only for declared indicators (teatro_hoy: 5,706 rows). The no-data statement (D6) runs only for widgets that opt in. Both run under the same slot, statement timeout and share lock; task 13.2 measures them.

*Alternatives:*
- *Compute n in the browser and in C# from returned rows.* Rejected: the rows are grouped already; the answer columns are not in them.
- *Always emit `NULL::double precision` outputs.* Rejected: it would change the SQL and the JSON of every measure widget for nothing.
- *One statement with a join back to the n per group.* Rejected: a join on a nullable group needs `IS NOT DISTINCT FROM` or a second branch for the missing group, and the sort-by-column key needs the raw rows; a separate statement matched in C# is simpler and only runs when declared.

### D6. No-data values left out (`missingFooter`)

- **Config.** A top-level `missingFooter` (`bool?`, `WhenWritingNull`) on `WidgetConfigInput` and `WidgetConfig`. `Normalize()` keeps it as given, `false` included (box plot D1 rule). The UI stores `true` when the box is checked, and `false` only when the widget already had the field saved.
- **Kinds.** It is taken by bar, column, line, pie, heatmap, pyramid, likert and crosstab. Elsewhere it is refused with `missingFooter`: "Only bar, column, line, pie, heat map, population pyramid, Likert and cross table widgets can leave out values that mean no data." It is not checked against the metadata: with nothing declared for the group and series columns it does nothing, and the form says so.
- **Query twins.** `WidgetQuery.For` and `queryFor` send it only when it is set and the kind takes it, so the query of every widget without it is byte for byte today's (the production-shapes goldens `query` and `uiQuery` hold), it is in the preview's key, and changing it queries again. `savedConfig` starts from `queryFor`, so a chart-type change to a kind that does not take it drops it. Shared cases go in `query-for.spec.ts` and `WidgetQueryTests.cs`.
- **Planning.** With the option and declared values for the group column (`@ng`) or the series column (`@ns`), every plotted statement adds `AND NOT ({left})`, where `{left}` is `({g} IS NOT NULL AND {g}::text = ANY(@ng)) OR ({s} IS NOT NULL AND {s}::text = ANY(@ns))`.
  - The `IS NOT NULL` keeps the missing group, "(none)", where it was: it is not a declared value.
  - The series domain and the pie domain add the same condition, so a left-out series takes no slot and no color. Their SQL changes only for widgets with the option.
  - A pyramid's and a Likert widget's "left out" count does not count these rows.
  - The overall statement of `add-chart-annotations` (`IndicatorQueryPlan.Overall`) is the stat tile's statement over the rows the widget plots, so it adds the same condition, as that change's D4 asks: an `overall` line never counts the rows the widget leaves out.
- **The no-data statement.** One more statement over the same `WHERE` without `NOT ({left})`: `sum(num) FILTER (WHERE {left})`, `sum(den) FILTER (…)`, `sum(m) FILTER (…)`, `sum(m)` over every row, the n of the left-out rows (D3's per-answer maximum over them when answers are declared), and `array_agg(DISTINCT …)` of the left-out values that have rows.
  - `noData` holds the values in natural order, the value (a ratio's num/den; a measure's sum), the numerator and denominator, n, and for a measure `share` = left-out sum / total sum.
  - It is left out of the response when nothing was left out.
- **The line.** `{values}: {value}`, with ` ({share})` for a measure and ` · n = {n}` while the base n line shows: "Sin dato: 4.2%" or "4. No sabe: 11 hogares (2.3%)". At most 3 values are named, then "…". The value is in the widget's formatter, with the widget's own unit when it has one (`add-widget-context`), and the share is a percent with at most one decimal, as shares are. The twins are `noDataLine` in a new `base-figures.ts` and `ReportText`/`ReportDataBuilder` in C#.
- **Stat tiles** do not take it: a tile has no group or series column. The share of "Sin dato" stays a tile filtered to it, as the 7 production cards are.

*Alternatives:*
- *Hide the values in the browser only.* Rejected: the totals, the shares of a 100% layout, the n and the row limit would still count them, and the email and MCP would disagree with the screen.
- *A dashboard-wide exclusion.* That is `add-filter-exclusions` (P2), which also handles the domains under dashboard filters.

### D7. The base settings of a widget and a dashboard

| Field | Where | Values | Absent |
|---|---|---|---|
| `display.minN` | widget config | 0 (off for the widget) or 1 to 10,000 | follows the dashboard |
| `display.showN` | widget config | `true` or `false` | follows the dashboard |
| `minN` | `dashboards.min_n` | 1 to 10,000, or null | off |
| `showN` | `dashboards.show_n` | `true` or `false` | `false` |

- **In force.** The widget's own value when it has one, else the dashboard's (or the shown version's).
- **C#.** `WidgetDisplay` gains `MinN` (`int?`) and `ShowN` (`bool?`), `WhenWritingNull`, with descriptions for the MCP schema. They are appended after `ShowDescription` and after the members that earlier changes of the program append (the reference lines of `add-chart-annotations`, the order of `add-declared-category-order`), so the existing members keep their place in the JSON. `Normalize()` returns null only when every field of the display, these two included, is null (program rule 3; today it tests the nine members by name), and keeps `0` and `false` as given, so a form saved unchanged keeps its stored text (dashboard-versions "Widget form saved unchanged"). `WidgetDisplayTests` keeps pinning the stored text of configs without them, and new cases round-trip `{"display":{"minN":0}}` and `{"display":{"showN":false}}`.
- **Universal.** Both are accepted on every kind, so a chart-type change never fails on them, and `displayFor` (which reads the display members by name) keeps them for every kind. The UI's `WidgetDisplay` gains `minN?: number` and `showN?: boolean`. A histogram has no n, so they do nothing there, and the form does not offer them for it.
- **Validation.**
  - `display.minN` outside 0 to 10,000: "The small-base threshold must be a whole number from 0 (off) to 10,000."
  - Dashboard `minN` outside 1 to 10,000: "The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off."
- **Small base** = a mark with an n below the threshold in force. A mark with no n never has one. The UI decides it from the response, in `base-figures.ts`, and the C# twin `BaseFigures.Small` (new `Indicators/BaseFigures.cs`) serves the email and MCP.
- **One fixture for the twins.** `base-figures.ts` and `BaseFigures` (the settings in force, the small-base rule, the tooltip figures, the n line, the no-data line and the small-base sentences) are tested against one shared file, `indicators/testing/base-figures.json`, read by `base-figures.spec.ts` and by `BaseFiguresTests.cs` as `SeriesTableTests` reads `series-tables.json`, with the expected English and Spanish texts.

*Alternatives:*
- *`minN` as a top-level config field.* Rejected: it never changes the rows (program rule 2), and in `display` changing it redraws the preview without a query.
- *Dimming refused on the kinds with no n.* Rejected: a universal field never blocks a chart-type change; the form simply does not offer it there.

### D8. The dashboard's settings: migration, endpoint, versions, copies

```sql
-- 0023_add_small_sample_warnings.sql
ALTER TABLE tamiza.dashboards
  ADD COLUMN min_n integer,
  ADD COLUMN show_n boolean NOT NULL DEFAULT false,
  ADD CONSTRAINT ck_dashboards_min_n CHECK (min_n IS NULL OR min_n BETWEEN 1 AND 10000);
```

- **Entity.** `Data/Dashboard.cs` gains `MinN` and `ShowN`, and `TamizaDbContext` mirrors the defaults and the CHECK. The number is 0023: 0021 (column metadata) and 0022 (widget context) are applied, and of the active changes only `add-locale-formatting` plans a script (0024), while `add-chart-annotations`, `add-declared-category-order`, `add-category-highlight` and `improve-stat-cards` plan none. If the order changes, it takes the next free number.
- **Endpoint.** `PUT /api/v1/projects/{projectId}/dashboards/{dashboardId}/bases`, role `analyst`, takes and returns `{ minN, showN }`. It is built like `SaveColorsAsync` and `SaveSourceAsync`:
  1. validate with `DashboardValidation.Bases`;
  2. return without writing when the values equal the stored ones;
  3. write with `ExecuteUpdateAsync`, with `UpdatedBy` and `UpdatedAt`;
  4. record the change with `versions.Note(…, BasesChanged)`.

  `DashboardView` gains `minN` (`WhenWritingNull`) and `showN` (`[JsonIgnore(Condition = WhenWritingDefault)]`, written only when true), appended after `Source`, so the view of a dashboard without base settings, which `get_dashboard` returns as it is, is byte for byte today's. The UI reads a missing `showN` as false. `DashboardVersionDetails` follows the same rule.
- **Versions.** `DashboardVersions.Content` gains the parameters `minN` and `showN`, between `source` and `widgets`, and its callers pass them (`ContentAsync`, the first version of a dashboard, and the helper of `ProductionShapesTests`). It writes `"minN"` when it is not null and `"showN": true` when it is true, after `valueColors` and `source` and before `widgets`. A dashboard without them keeps its content byte for byte, so the first save after the deploy makes no version (palettes D6, and the same for the source).
  - `ReadContent` reads a missing key as off, and a value out of range as null, into `VersionContent`, which gains the two members.
  - A restore writes both back.
  - `GET …/versions/{n}` and `get_dashboard_version` return them, so viewing a version draws with its settings.
- **Change kind** `basesChanged`, in C# `DashboardChangeKinds.All`, the TS list and the history's strings ("Base sizes changed", in English and Spanish). It is a new value, as program rule 6 asks for every dashboard-level setting (palettes added `colorsChanged` the same way). After a rollback, an older history dialog builds the key `dashboards.versions.changes.basesChanged` (it builds `dashboards.versions.changes.{kind}` for every change), which it lacks, so that one line shows the key's text; nothing is refused, and the older API stores and lists versions without checking their kinds.
- **Duplicate.** `DuplicateAsync` copies `MinN` and `ShowN` with the filters, colors and source. A moved widget needs nothing: its `display` travels, and the new dashboard's settings apply.
- **UI.** A new `dashboard-bases-dialog.ts`, a CDK dialog like `dashboard-colors-dialog.ts` and `dashboard-source-dialog.ts`, holds:
  - a checkbox "Show each widget's base n under it";
  - a number field "Dim marks with a base below", empty for off, with the hint "Common choices are 30 or 50";
  - a short text saying where n comes from.

  The **Base sizes** action sits after **Source** in the dashboard's menu (**Colors**, **Source**, then **Base sizes**: the entries are built in `project-indicators.ts` with the keys `dashboards.menu.*`), for analysts and admins, and is hidden while a version is shown. `IndicatorDocsTests` pins the order of the menu in `docs/indicators-dashboard.md` (task 12.5). Saving replaces the page's settings, which it keeps in a signal as it keeps `dashboardSource`, without querying the widgets again. `project-indicators.ts` passes the settings of the dashboard or the shown version through `widget-grid` to each `widget-view` (a `bases` input, as `colors` is) and into `WidgetDialogData`.

### D9. Tooltips and keyboard on charts without a series

**On by default, decided with the user on 2026-11-04.** Bar, column, line and histogram charts without a series, and pies, get rich tooltips and keyboard access on every dashboard, with no setting: the value, the numerator and the denominator under their names, and the n (a histogram's bar: its range and count). This is one of the program's intended default changes, with `improve-stat-cards`' smaller stat tile figure. Nothing drawn, printed, exported or emailed changes with it: it shows only on pointing, tapping or focusing.

- **Axis charts.** The plain layout of `axis-chart.ts` gains hit areas and a `MarkGrid` of one series (`s = 0`), one mark per bar, column, point or histogram bar, so the existing `markTooltips` serves it.
  - The SVG becomes `role="application"` with `tabindex="0"`, `aria-roledescription` ("chart"), `aria-describedby` on the existing key help that names no series (`indicators.series.keyHelpGroupsDown` for bars, `keyHelpGroups` for columns, lines and histograms) and the live region, as a chart with a series has. The summary stays as its `aria-label`.
  - The marks' native `<title>` goes, and the truncated axis labels keep theirs. The hit areas carry the `chart-hit` test id, which `svg-clean.ts` already leaves out of images.
  - The script editor's test view (`script-editor/indicator-view.ts`) draws with `AxisChart`, so it gets the same tooltips; its rows come from a test, with no figures, so its tooltips show the value only.
  - A line's hit areas are bands around its points. Its dots are drawn up to 40 points as today, and always for a small base.
- **Pie.** `pie-widget.ts` gets the same: the slices as marks in the legend's order (largest first, "Other" last), `role="application"`, with the left and right arrows stepping slices and `keyHelpGroups` as its key help.
- **Content.** One builder in `base-figures.ts` makes every tooltip's lines after its value: the numerator and denominator under their names, and the n under its name when it is not the denominator. A histogram's bar has its range as heading and "Rows: 12". A series line, whose tooltip lists every series of a group, adds each series' n only. The small-base line is "Small base: n = 16 (below 30)".
  - The names come from `add-indicator-column-metadata`'s resolver (`columnLabels` in `format.ts`, `ColumnLabels.For` in C#): the widget's own name, else the indicator column's, else the column itself.
  - The value's own line is the tooltip line of the plotted value as `dashboard-indicators` "Widget caption text" and "Widget unit" write it (the widget's own caption text and unit when it has them), as it is in a map's tooltip.
  - The figures use a new `figureFormatter(locale)` in `format.ts`: whole numbers with thousands separators, up to two decimals when not whole (a decimal numerator, such as teatro_intencion's 0.5), never the indicator's format or unit. Its C# twin is `ReportFormat.Figure`.
- **Other kinds.** `series-plot.ts` (the plot's `content` function), `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts` and `boxplot-chart.ts` call the same builder with their cell's figures. `buildSeriesTable` keeps each cell's figures next to `cells` (`figures[i]`), with its C# twin and fixture.
- **Map.** `map-tooltip.ts` adds the figures after the value, leaving out a numerator or a denominator that is one of the widget's tooltip columns, and the n when it equals the denominator. The production map (`registros`/`meta`, both tooltip columns) keeps its tooltip.
- **Tests.** The role assertion of the `chart-widgets.spec.ts` test "describes itself to assistive technology and offers the same numbers as a table" changes from `img` to `application`. The specs that read the marks' native titles (the `titles` helper of `chart-widgets.spec.ts`, about 20 tests on values, order, ranges and pie slices, and the test "plots the ratio of each group when asked…" of `script-editor/indicator-view.spec.ts`) read the same texts from the tooltip each mark shows instead, through a helper that steps the marks with the keyboard; the expected texts do not change, so the values and orders they pin are still checked. New jsdom cases cover the arrows, Home, End, Escape, a tap that keeps the tooltip and the announced text for a column, a bar, a line, a histogram and a pie. The real-stack check includes a screen reader pass, which the last changes could not run (task 13.3).

*Alternatives:*
- *Keep the native `<title>` and put the figures in it.* Rejected: a native title has no keyboard access, no lines and no touch behavior, and it is not shown on a tap.
- *Rich tooltips only when a base setting is on.* Rejected: the figures are what a reader of a rate needs, and two tooltip systems would be kept in step.

### D10. Drawing small bases

- **Bars, columns, segments, slices, boxes.** A small base sets `fill-opacity="0.35"` and a 1.5 px outline in the mark's own color with `stroke-dasharray="3 2"`. The mark keeps its size, so nothing reads as a different value.
- **Points of a line.** The dot is hollow with a dashed ring; the line through it is unchanged.
- **Heat map cells.** The shade is kept, since lower opacity would read as a lower value on the scale.
  - A hatch overlay is a `<rect fill="url(#{id})">` over the cell, with a dashed outline drawn with the existing `stroke-heading` utility, the ink the box plot's median uses.
  - The pattern is defined once in the chart's own SVG `<defs>`, next to the empty-cell pattern, with an id unique to the chart (as `hatchId` is per component), so an image's clone carries it (`svg-clean.ts` keeps patterns and prefixes their ids). It is a 6 × 6 tile with one diagonal path from its top-left to its bottom-right corner in `stroke-heading`, drawn as a path that needs no rotation, so it uses neither the SVG `transform` attribute (two-dimension defect 4) nor a `patternTransform`.
  - **Not the empty cell's mark.** An empty cell is today a neutral surface with hairlines running from bottom-left to top-right (its pattern is rotated 45°) and no shade. A small-base cell keeps its shade, its hatch runs the other way, from top-left to bottom-right, in the heading ink, and its outline is dashed, so the two read apart; the note under the chart says what the hatching means.
- **Map shapes.** The class-based shade is kept; the Leaflet layer gets `dashArray: '4 3'` and `weight: 2` (where `map-engine.ts` creates its layers from `polygonStyle` and `markerStyle`). Hatching a Leaflet path would need patterns inside Leaflet's own SVG, which its renderer owns.
- **Stat tile.** The figure is drawn at reduced opacity; the small-base note and the n line sit under the tile with the other lines (D11). An image draws a tile's figure itself (in `composeImage`), so `ExportParts.stat` gains `dimmed` and `composeImage` writes the figure with the same `fill-opacity`; the figure's size is `improve-stat-cards`'.
- **Notes.** While a mark is dimmed, the chart's summary adds ", 2 with a small base", and widget-view shows "Marks with a base below 30 are dimmed." under the chart (D11).
- **No new tokens or utilities.** The colors are those the marks already have and `stroke-heading`. Opacity and dashes are SVG attributes, so the initial bundle does not grow.
- **Print and images.** They use the same SVG, so the dashes and the hatching print, and `svg-clean.ts` writes `fill-opacity` and `stroke-dasharray` from the computed style. An SVG fill is not a "background graphic".

### D11. The lines under a widget

- **The lines.** `widget-view.ts` draws up to three lines under the chart, after the chart's box (which holds a chart's own key, such as the reference lines' key of `add-chart-annotations`) and before the widget's footnote (`footnoteLine`), each `shrink-0 text-xs text-body`, wrapping:
  1. the small-base note;
  2. the base n line: "n = 223 Hogares que asistieron", the n with `figureFormatter` and the name of the base column, else of the denominator;
  3. the no-data line.

  widget-view has the response, the settings in force and the names, so the chart components do not change for them. The lines show where the widget shows its chart, its table or its "no rows" message, as the footnote does, and not while it loads, fails or says its indicator is not published. A stat tile draws its lines the same way (it takes no no-data line); `improve-stat-cards` then draws the tile's n line inside the tile, once, and widget-view draws no second one (D4).
- **Room.** The lines take their room from the plot as the texts of `add-widget-context` do (its D5, "Widget context in a tile"): the tile never grows, the chart's box takes what is left and is cut at its own box, never over a line, so it gets `overflow-hidden` while the widget has a footnote or any line. A line is clamped as the footnote is, to two lines, or one on a tile two rows high or less, ending with an ellipsis, with the whole text in its `title` and in the DOM for screen readers; on paper it is whole. A line costs about 16 px plus a 4 px gap, as a text does. On a 1,280 px page:
  - a 6 × 4 tile has a plot area of about 233 px under its caption, 199 with a subtitle and a one-line footnote and 159 with a two-line footnote. One line leaves 213, 179 and 139 px, and three lines leave 173, 139 and 99 px. The axis charts draw at no less than 120 px high, so only the last case cuts the chart at its box;
  - a 3 × 2 stat tile has a body of 81 px, and its figure and label take 64 px. A one-line footnote (20 px) leaves its figure whole (40 px) and its label cut, as `add-widget-context` fixes. One n line more leaves 41 px, which keeps the figure whole. A subtitle, a footnote and an n line leave about 25 px, and then the tile's box cuts the figure. `improve-stat-cards`' smaller figure (18 to 30 px), which ships in the same release, gives the room back, and the documentation suggests h 3 for a stat tile that shows its lines. Task 13.3 measures both tiles;
  - a chart's key (`add-chart-annotations`) takes its room before the plot does, above these lines, so a chart with a key, texts and lines is cut sooner by the same rule.

  The form's warning for crowded tiles (`textsCrowd`) stays about the subtitle and the footnote.
- **Footnote.** `add-widget-context`'s footnote closes the tile: data notes first, the author's note last. The other surfaces keep the same order: an image draws the lines among its notes below, before the footnote; the email shows its notes above the table and the footnote after it (`dashboard-reports` "Widget footnote in a scheduled email"); the workbook's "Summary" holds them on its "Notes" line, which follows its "Footnote" line.
- **Images.** widget-view gains `exportNotesBelow`, the three lines, next to `exportNotesAbove`. `exportImage` in `project-indicators.ts` passes it with the notes above to `makeImage` (`widget-exporter.ts`), whose `ImageJob` and `ImageInput` gain `notesBelow`, and `composeImage` draws it after the chart's own notes (the table's, `parts.notes` and `notesExtra`) and before the footnote (`ImageInput.footnote`). Print uses the same DOM, with the lines whole.
- **Tables.** While the n line shows or a threshold is in force, "Show as table" of a widget with no series whose values have an n gets an "n" column after the value, and a small-base value is followed by "*". The table has a note "* Base below 30." and a visually hidden "small base" for screen readers. A cross table marks its cells the same way. `widget-table.ts` takes the marks. A widget whose values have no n (a measure with no base column) keeps its columns. With no setting, the tables are as before.

### D12. The widget form

- **Fields.** `widget-dialog.ts` gains a "Base" group after the color fields and the **Text on the widget** group of `add-widget-context`:
  - "Base n under the widget": a select of "As the dashboard says (shown)" or "(hidden)", "Show" and "Hide";
  - "Small bases": a select of "As the dashboard says (below 30)" or "(off)", "Off" and "Below", with a number field for "Below";
  - for the kinds of D6, a checkbox "Leave out values that mean no data, and say what they held under the chart". It names the declared values ("comuna: Sin dato"), or says that the indicator declares none for its group and series columns.
- **Hints.** When the n line is on and the widget has no n, the form gives the reason of D4: a measure with no base column; no base and no answer column declared; a histogram or a box plot.
- **Signals.** The fields are signals seeded from `start.display` and `start.missingFooter`, stored as D7 says. `displayDraft()` carries `minN` and `showN` with the display fields it already carries, so a widget that sets none of them still saves no `display`. `formConfig` carries `missingFooter`, and `savedConfig` keeps it only for its kinds (D6).
- **Preview.** `minN` and `showN` stay out of the preview's key, so changing them redraws without a request; `missingFooter` is in it.
- **Errors.** `fieldFor` maps `display.minN`, `display.showN` and `missingFooter`.

### D13. Emails and workbooks

- **Email.** `ReportDataBuilder.BuildAsync` reads the dashboard's `MinN` and `ShowN` with its filters and source (the select at the top of `BuildAsync`), and passes each widget's settings in force to `BuildWidgetAsync`.
  - The widget's notes (built in `ReadWidgetAsync`) gain the small-base note ("2 values have a base below 30, marked *."), the n line and the no-data line, in the email's language (`ReportText`, English and Spanish). They show above the table, so the footnote that `BuildWidgetAsync` adds stays last in the body and the plain text; on the workbook's "Summary" they are part of the "Notes" line. The no-data line's value is written with the widget's own unit when it has one (`MetaOf`).
  - `AxisTable`, `PieTable` and `MapTable` add an "n" column (`#,##0`) after the value while the n line or a threshold is in force and the values have an n. A small-base value's `ReportCell.Text` ends with " *", and its `Value` stays the number, so the workbook holds the number alone. `SeriesReportTable` marks its cells the same way.
  - The names come from `add-indicator-column-metadata`'s C# resolver (`ColumnLabels.For`); the n figure uses `ReportFormat.Figure`.
- **Widget workbook.** `WidgetExportRequest` gains optional `minN` (0 to 10,000, 0 off) and `showN`, after `Version`: the settings the page applies to the widget, its own over the dashboard's or the shown version's. `exportWorkbook` (`project-indicators.ts`) sends them from widget-view's settings in force. Without them the workbook is today's: an older page, or a dashboard with no setting. Since `add-widget-context` the request's `config` is the widget's saved config (`savedConfig`), validated as a save, so `missingFooter` travels in it and so do the widget's own `display.minN` and `display.showN`. The workbook leaves those two aside and applies the request's `minN` and `showN`, which already resolve the widget's own over the dashboard's or the version's, so there is one source.

  *Alternative:* resolve the dashboard's (or the shown version's) settings in the endpoint, which already reads their name, filters and source. Rejected: the workbook is the widget as the member's screen shows it, and an older page whose screen shows no marks must get the workbook that release gave ("Request without base settings").
- **Size.** The body's 80 KB budget, the 10 rows per block and the 6 series columns do not change. The n column is one more column only while a setting is in force.

### D14. MCP

**Always returned, settled on 2026-11-04** (the program's decision on the review's recommendation, with the decisions taken with the user that day): `query_indicator` and `get_widget_data` return a ratio's `numerator`, `denominator` and `n` with its rows whatever the settings, not only when a base setting is on, so a client reads the same figures the tooltips show. Paging, or a summary mode with a per-widget truncation note, is P3 `improve-mcp-ergonomics`' work.

- **Rows.** `Row()` adds `numerator`, `denominator` and `n` when present. `get_widget_data` also adds `smallBase: true` to a row, a cell or a box below the threshold in force.
- **Series and boxes.** `SeriesPayload`'s cells add `n` and `smallBase`, and its totals add the three figures. `BoxPayload` adds `smallBase` only.
- **Widget header.** `get_widget_data`'s widget gains:
  - `n`, the widget's n under D4;
  - `minN` and `showN`, the settings in force, when set;
  - `noData`: `{ values, value, formatted, share, n }`;
  - the small-base sentence among its `notes`.

  `query_indicator` gains `n` and `noData`; it has no dashboard, so no threshold.
- **Size.** A ratio row grows by about 40 characters, a cell by about 8. The 60,000-character budget and its cutting do not change. On Teatro (44 widgets, 142 ratio widgets in the project) a whole-dashboard `get_widget_data` is cut sooner, and the hint (`widgetId`) already says how to ask for less; paging comes with `improve-mcp-ergonomics`.
- **Tools.**
  - The `query_indicator` and `get_widget_data` descriptions name the new fields.
  - `create_widget`'s config template gains `missingFooter`, and its `display` gains `minN` and `showN` after the fields it lists when this change is implemented.
  - A new `BasesHelp` constant, after `ContextHelp` (and `AnnotationsHelp`) in the descriptions of `create_widget` and `update_widget`, describes `display.minN` and `display.showN`, accepted on every kind, and `missingFooter`. `DisplayHelp`'s pinned sentence stays as it is.
  - `ChartTypeChangeHelp` names `missingFooter`, which another kind refuses.
  - `update_dashboard` gains `minN` (0 turns it off) and `showN`, and joins them to the "give at least one" rule, whose message names them; its description says that a base-size change is one more commit of the same request, as a color or source change is.
  - `get_dashboard`'s description names them.
  - `ColumnsHelp` (`save_script`'s `columns`) names `base`, `missing` and `categories`, says that given it replaces the whole map, and stops saying that a property other than `label` is ignored.

  The sentences that `McpToolCatalogTests` and `IndicatorDocsTests` pin change in the same task.
- **`Instructions.md`.** A "Bases" section, after "Words on a widget" and before "Rules":
  - a rate's n is its denominator unless the indicator declares a base column;
  - declare `categories` on the columns of a long table that name the question and its answers (`variable`, `orden`, `categoria`), never on a household dimension, and `missing` on the values that mean no data, with `save_script` `columns`;
  - a widget's n needs one of those declarations;
  - set a dashboard's `minN` and `showN` only when the person asks or their study's specification does;
  - `missingFooter` replaces hand-written `in` lists that only drop "Sin dato";
  - `save_script`'s `columns` replaces the whole map when given, so read it with `get_script` and send the properties back with the labels.

  `McpGuideTests` pins the new phrases. `AssistantPrompts.Version` does not change: `system.md` is not edited, since the script contract is the same.

### D15. The production shapes fixture

The shared fixture `add-indicator-column-metadata` created (program rule 10, its design D13: `indicators/testing/production-shapes.json`, run by `ProductionShapesTests.cs` and `production-shapes.spec.ts`, and extended by `add-widget-context`) is extended again and run in the API and the UI. Its goldens are written before that change and never regenerated.
- **Stored text, queries and the form.** Every existing shape still reads and writes back byte for byte (`stored`), and its `query` (`WidgetQuery.For`), `saved`, `uiQuery` and `formSaved` goldens hold unchanged: no existing shape has `missingFooter`, `minN` or `showN`, and both twins send `missingFooter` only when set.
- **Plans.** A measure shape's `plan` golden holds unchanged: no output is added (D5). A ratio shape's plan differs from its golden only by the figure outputs appended at the end of each select list (D5). The test removes the outputs `IndicatorSql.Figures` lists (and, in each series branch, the same appended outputs) and compares the rest with the unchanged golden, byte for byte, so the `WHERE`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT` and parameters are proved unchanged. A box plot shape and a histogram plan equal their goldens.
- **Two new shapes.** The fixture holds 20 shapes after `add-widget-context`; `add-chart-annotations` raises it to 25 before this change starts, and this change adds two more, so each count the runners pin goes up by two from what it is when the task starts (27 after `add-chart-annotations` alone, more if `add-declared-category-order` or `add-category-highlight` add shapes first), and the total is recorded in design.md; `improve-stat-cards` adds two after it. They are a ratio column chart by `comuna` with `display.minN` 30 and `display.showN` true, whose `query` and `plan` equal those of the same shape without them, and a ratio column chart with `missingFooter` true, whose `query` carries it and whose `plan` holds the exclusion with no declared value (so the same SQL until an indicator declares one). Their stored text, `saved`, `uiQuery` and `formSaved` goldens are written from the code of this change, with invented names, and both are listed in the UI spec as `contextShapes` is, so the form is checked to send them back unchanged.
- **Declared metadata, no opt-in.** A new top-level `declared` entry of the fixture holds invented column maps (`base`, `missing` and `categories` on the shapes' own columns). The existing shapes are planned again with them and no setting on: the plotted statement after the same removal still equals its golden, and only the answers statement (D5) is added; `missing` adds nothing without `missingFooter`.
- **Responses.** `IndicatorBaseQueryTests` (integration) runs a ratio column chart, a stat tile with `equals` filters and a series over counts built like the fixture's shapes, and compares each response without the new fields to the response before the change, byte for byte.
- **A dashboard row without base settings.** Its version `content` golden holds unchanged, and so does the golden of `dashboardWithSource`.

### D16. No overall line over declared answers

`add-chart-annotations` (its D12) leaves this rule to this change, which brings the declaration: an `overall` line over the answers of one question is only the mean of the bars (Teatro 9b11 "En qué lugares"), which reads like a total.

- **Rule.** A widget whose group column or series column is declared `categories` (D1) draws no `overall` reference line, and says so in a note under the chart, in images, in the email and in MCP: "No overall line: the bars are the answers of one question." The other kinds of line are drawn as before.
- **Where.** The twins `referenceLineValues` (`reference-lines.ts`) and `ReferenceLines.Of` (`ReferenceLines.cs`) take one more argument, the indicator's declared answer columns, and leave the `overall` line out, with the note, when the widget groups or splits by one of them. The page passes them from `IndicatorEntry.columns`, the email and the widget workbook from the indicator metadata they read, MCP from the same lookup. A case goes into `reference-lines.json`.
- **The query is untouched.** `queryFor` and `WidgetQuery.For` never hold the overall flag, which travels on the query request (`IndicatorQueryRequest.Overall`), and `WidgetQuery.Overall` and `overallFor` still derive it from the saved config alone (its D4), so the twins stay pure and the preview's key never depends on metadata; the engine still computes the figure, which is not drawn.
- **The form** stops offering `overall` for such a widget, and a saved one keeps its line in the config: nothing is refused, so a widget saved before its indicator gained the declaration never becomes invalid.
- **Production.** No production widget has a reference line yet, so nothing changes until an author declares answers on an indicator whose widgets have an `overall` line.

*Alternative:* refuse an `overall` line on a declared column when saving. Rejected: a script's metadata would then make saved widgets invalid, which `add-chart-annotations` D12 rules out.

### D17. Released with `improve-stat-cards`

**Decided with the user on 2026-11-04:** this change and `improve-stat-cards` ship in the same release. This change is implemented and archived first, then `improve-stat-cards`, and neither is deployed alone.

- **Why.** An author who declares answer columns here is told that only the n changes. On its own, this change would let a rate over several answers of a declared column keep dividing by k × n (the 23.79 % card) while its n is right, so a tile's value and its n would disagree. Shipped together, the declaration never exists without the correction.
- **No window to manage.** No indicator can hold a declaration before the correction applies, so `improve-stat-cards` needs no list of declared indicators before its deploy.
- **Rollback.** Both go back together to the release before them (Migration Plan).

*Alternative:* ship this change first and `improve-stat-cards` in a later release, listing the indicators declared in between. Rejected: it leaves a window where declared values and their n disagree, and a step on the deploy to tell their authors.

## Spec ownership

- **Modified, owned by this change** (program section 3): `dashboard-indicators` "Widget query over one indicator" (the figures returned and the rows left out), "Bar and column charts of one indicator" (tooltips and keyboard without a series), "Marks of a widget with a series" (the figures and small bases in tooltips), "Map tooltip" (the figures the tooltip columns do not show); `mcp-server` "Indicator queries and widget data". Every scenario is kept with its body. Each block was compared with the main spec after the changes archived on 2026-11-04: only "Map tooltip" had moved (it gained the widget's own caption text and unit and the scenario "Own caption text and unit in a map tooltip"), and the block now holds that text with this change's clause added.
- **Not modified, only referred to:**
  - `dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard" and `dashboard-indicators` "Moving a widget to another dashboard" are `add-indicator-column-metadata`'s. Its generic wording ("its other display settings") covers the new fields, and this change adds "Base settings in dashboard versions", "Base settings of a duplicated dashboard" and "Base settings of a moved widget" for the dashboard-level ones.
  - `widget-export` "Parts of a widget image" (its notes below come before the footnote), "Summary of a widget workbook" and "Widget context in a widget workbook", `dashboard-reports` "Widget notes in a scheduled email" and "Widget footnote in a scheduled email" (the footnote follows the table, apart from the widget's notes) and `dashboard-printing` "Charts on paper" and "Widget context on paper" are `add-widget-context`'s or nobody's, and stay true with the lines in the notes; the lines are added by "Base n and small bases in widget images", "Base n and no-data lines in a scheduled email" and "Small bases on paper". `dashboard-indicators` "Widget context in a tile" (a stat tile short of room keeps its figure whole) holds for a 3 × 2 tile with a footnote and one n line. With a subtitle too, the n line is a third text and leaves the figure about 25 px (D11), so its scenario "Stat tile two rows high" holds again only with `improve-stat-cards`' smaller figure, which ships in the same release (D17).
  - `pipeline` "Indicator metadata" is `add-indicator-column-metadata`'s; the three properties are new requirements.
  - "Line chart" (`add-chart-annotations`) and "Pie chart" (`add-declared-category-order`) are not modified: "Mark tooltips of charts without a series" covers their tooltips.
  - `dashboard-versions` "Changes that make a version" lists examples ("however small: …"), so a new requirement says that saving the base settings makes one.
  - `add-chart-annotations`' "Overall reference line" is not in the main specs yet (that change is not implemented), so it is referred to by name: "Answer columns without an overall line" is added for D16. Its scenario "Answers of one question" (mean and overall at the same value over `categoria`) stays true for an indicator that declares no answer columns; if the rebase finds it in the main specs without that condition, the rebase adds the condition to its WHEN and nothing else.
  - `improve-stat-cards` modifies "Widget query over one indicator" too, after this change; it copies this change's text when it is rebased (its design says so). It also narrows, when rebased, this change's sentences "The declaration SHALL change no plotted value, total or order" ("Base n counted once across answers") and "The declaration SHALL change no plotted value" (`pipeline` "Answer columns of an indicator") to values that hold one answer, and gives "Base n of a mark" and "Base n of a widget" the n of a tile with a statistic. Both changes ship in one release (D17), so those statements and their exceptions reach production together.
  - The mcp-server requirements on saving scripts ("Indicator column labels through MCP") are `add-indicator-column-metadata`'s; "Base metadata through MCP" is added instead, and "Widget context through MCP" and "Dashboard source through MCP" are left as they are.
- **Default changes.** Section 0 of the program says nothing in production may look different until someone opts in, while section 4 asks for rich tooltips on every kind. The user settled it on 2026-11-04 (D9): the tooltips and their keyboard access are on by default, one of the program's intended default changes with the stat tile figure, and everything else stays unchanged: what is drawn, printed, exported and emailed. "Widgets and dashboards without base settings" says so. The data the API and MCP return follows D14: a ratio's rows gain three numbers whatever the settings.

## Production impact

Of the facts in program section 5:
- **190 widgets, 9 dashboards.** No dashboard has `min_n` or `show_n`, and no widget has `minN`, `showN` or `missingFooter`. So nothing is dimmed, no line appears, and no row is left out. No stored config is rewritten, and no version is made.
- **142 ratio widgets.** Their values, orders and colors are unchanged. Their query answers gain `numerator`, `denominator` and `n`, which only tooltips, MCP and the opted-in surfaces read.
- **38 measure widgets** (29 series over count indicators, 6 box plots, 3 median tiles). With no base column declared, their SQL and responses are byte for byte today's.
- **Tooltips.** The bars, columns and lines without a series (most of the 95 bars, columns and lines in production) and the Escuelas pie b296 move from the native title to rich tooltips with keyboard access, the one visible change by default (D9, decided with the user on 2026-11-04). Every ratio tooltip gains its numerator and denominator.
- **The only map** (`registros`/`meta`, both tooltip columns) keeps its tooltip.
- **The 49 `in` filters**, all on a widget's own group or series column, keep their values: no value changes here.
- **The 27 sorts by `orden`, the numbered labels in filters and colors kept, `ignoredDashboardFilters` and the median cards** are untouched: the change reads no filter, sort or label.
- **The 22 `comuna in [...]` lists** keep working. Declaring "Sin dato" plus `missingFooter` can replace them, but only on purpose. "SIN NOMBRE" is a real comuna and is never matched by "Sin dato": the match is exact text.
- **Long-format indicators** (teatro_hoy and its kind) get a per-bar n right away: `COMUNA 7` = 16, `SIN NOMBRE` = 20, acceptance case 8. A widget n needs their question and answer columns (`variable`, `orden`, `categoria`) declared as answers (D3, D4).
- **Widgets over several questions** (Comercio 0d62, `variable` in ["Ropa y calzado", "Tecnología"] split by `variable`; Likert e278 over two `pregunta` of a count table) get the right widget n (476, not 952) only when the question column is declared too; the docs give both examples.
- **Production pages and MCP results.** The 142 ratio widgets' query answers grow by three numbers per row; the dashboards' own views (`get_dashboard`) are byte for byte the same, since `showN` is written only when true and `minN` only when set.
- **teatro_coherencia and centro_motivo_hora** repeat households per column; declaring answers there would be wrong. The docs say not to, since their bases differ per answer.
- **Escuelas indicators over persons** count persons, and the line names the denominator ("Personas"), so it says what it counts.
- **Tiles.** The lines show only when a setting is on, so Teatro's 44 tiles keep their room; the form says when a widget has no n to show. Its 16 stat tiles, 9 of them 2 rows high, are the ones that feel an n line first (D11).
- **Scheduled emails** of the 9 dashboards are identical until a dashboard sets a threshold or the n line.
- **MCP clients** see three more fields per ratio row and `n` per cell.

## Risks / Trade-offs

- **[An author declares answers on a column whose answers have different bases]** (cross_block in Escuelas, centro_motivo_hora) → the n would be the largest answer's base, not a total. The docs and the form's help say when to declare (every unit in every answer's denominator), with the Duitama examples. Values never change, so a wrong declaration misleads only the n.
- **[An author declares only `categoria` on a table that holds several questions]** (teatro_hoy holds 9) → a widget that spans several questions whose answers share a name ("Sí") adds their households. The form's help, the docs and `Instructions.md` say to declare the question and code columns with the answers (D3), and a widget over one question, the usual case, is right either way.
- **[One answer column mixes single- and multiple-choice questions]** (teatro_hoy: E1 and E2 single, E3 and E4 multiple) → the kind is per column, so it cannot differ per question. It does not change this change's n. The docs say to declare `multiple` when any question of the column is multiple-choice, which `improve-stat-cards` turns into a refusal of a sum over several answers, never into a wrong figure.
- **[A small-base hatch read as an empty heat map cell]** → the two marks differ in shade, direction, ink and outline (D10), and the note under the chart says what the hatching means.
- **[n of a ratio whose denominator is not a count of units]** (a mean as `sum(pesos) / sum(households)` has households as its denominator, which is right; a target as denominator, as the map's `meta`, is not a count of respondents) → the line and the tooltip name the column the n comes from, and the author can declare the real base column.
- **[Tooltips change by default]** → hover-only content. The drawn chart, print, images and emails are unchanged, and the keyboard access is gained. The role change is tested and checked with a screen reader in the real-stack pass.
- **[Lines take height from fixed-size tiles]** → only when switched on. One line each, `text-xs`, clamped and cut at the chart's own box like the footnote (D11 has the measures). The docs suggest h 3 for stat tiles that show their n.
- **[A 3 × 2 stat tile with a subtitle, a footnote and an n line cuts its figure]** (about 25 px left for a 40 px figure) → the figure shrinks with `improve-stat-cards`, which ships in the same release (D17); task 13.3 measures the tile, and the docs suggest h 3.
- **[A client sends `columns` with the labels only and erases the declarations]** → `columns` given replaces the whole map, as it does for labels. The tool text, `Instructions.md` and `docs/mcp.md` say to read it with `get_script` and send it back whole, and the page always sends the whole map.
- **[A second statement per query for declared indicators]** → indicator tables are small (thousands of rows). It runs only with a declared answer column or the no-data option, under the same timeout and slot. Task 13.2 measures it on 100,000 and 1,000,000 rows.
- **[MCP results grow]** → a few numbers per row, cut by the same budget with the same hints.
- **[`missingFooter` changes a 100% layout's shares]** (shares over the known answers) → that is its purpose, it is opt-in, and the line says what was left out.
- **[Twins drift: n rules, small-base rule, line texts, figures]** → one builder per side (`base-figures.ts`, `BaseFigures`), with shared cases in `base-figures.json`, `series-tables.json`, `reference-lines.json` and the fixture of D15.

## Migration Plan

1. **Release.** This change ships in the same release as `improve-stat-cards` (D17, decided with the user on 2026-11-04): it is never deployed without it, so the answer columns can never be declared while a rate over several answers is still added up as separate bases.
2. **Deploy.** `0023_add_small_sample_warnings.sql` adds the two columns with their defaults and the CHECK, and rewrites no row. Existing dashboards read as off; existing widgets have no new field; the version contents are unchanged (D8). The column metadata properties live in `add-indicator-column-metadata`'s column, which needs no migration here. The UI and the API deploy together, as always: an older UI's form would drop `display.minN`, `display.showN` and `missingFooter` on its next save.
3. **Rollback** (program rule 7). This change and `improve-stat-cards` are rolled back together, to the release before both.
   - An older API ignores `min_n` and `show_n`; the columns and the CHECK can stay.
   - It drops `display.minN`, `display.showN` and `missingFooter` from a widget on that widget's next save, and never rejects them, since its reader skips unknown fields. Until then the stored JSON keeps them, and a version that holds them keeps them.
   - An older `add-indicator-column-metadata` API drops `base`, `missing` and `categories` from an indicator's next version saved with `columns` (every save from the page, which always sends them); an MCP save that leaves `columns` out copies the stored text and keeps them. The versions saved before keep them.
   - An older page ignores the new response fields and draws today's tooltips.
   - No value of an existing setting and no operator is added. `missingFooter` is a new field, not a new value of an old one, so an older API never refuses a widget for it. The one new value is the change kind `basesChanged`, which an older history shows by its key (D8).
   - Removing the columns is a manual `ALTER TABLE`, never needed.

## Open Questions

- Whether a box plot should get a widget n once its tables stop copying households into "Todos" boxes. This change gives each box its n and no widget n; a later change can add one without changing the response shape.
- The hatch tile's density at 2× PNG and on paper. The design fixes a 6 × 6 tile; the real-stack check may tune its stroke width without changing the spec.
