# Tasks

**Order.**
- `add-indicator-column-metadata` and `add-widget-context` are implemented and archived (2026-11-04):
  - groups 2, 3 and 9 build on the column map (`IndicatorColumnMetadata` and `ColumnMetadata` in `Indicators/ColumnMetadata.cs`; `ColumnMetadata`, `ColumnDraft` and `column-names.ts` in the UI), the save form section, the name resolver (`ColumnLabels.For`, `columnLabels`) and the production-shapes fixture;
  - `WidgetDisplay` already ends with `ShowDescription`, the widget workbook request already sends `savedConfig`, and images, emails and workbooks already have places for the subtitle, the footnote and the source.
- `add-chart-annotations` comes earlier in the program too and is not implemented yet: tasks 4.2, 6.5, 7.1, 8.1 and 11.7 build on its overall statement, its reference-line twins (`ReferenceLines.Of`, `referenceLineValues`) and the production shapes it adds.
- Groups 1 to 8 (api) come first, in order; each ends with its tests green.
- Groups 9 to 12 (ui) follow, in order. Group 9 keeps the UI build and suite green from the first type change.
- Group 13 is last.
- **Release.** This change ships in the same release as `improve-stat-cards` (design D17, decided with the user on 2026-11-04), which is implemented right after it. Do not deploy this change alone: the answer columns must never be declarable while a rate over several answers is still added up as separate bases.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks. "(API part)" and "(UI part)" split a scenario between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~IndicatorBaseQueryTests"`.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/pages/projects/<path>.spec.ts'`.

## 1. Database (api)

- [x] 1.1 Write `tamiza-api/src/dbUp/Scripts/0023_add_small_sample_warnings.sql` exactly as design D8: `min_n` with `ck_dashboards_min_n`, and `show_n` with its default. If another change took 0023 first, use the next free number and say so in design.md. Add a `ScriptConventionTests` case in the style of `The_dashboard_palettes_script_adds_two_columns_and_one_named_check`: two columns, one named CHECK, no key, index or table.
  Verify: `ScriptConventionTests` passes, the new case included.
- [x] 1.2 Add `MinN` and `ShowN` to `Data/Dashboard.cs`, and mirror the default and the CHECK in `TamizaDbContext`.
  Verify: `SchemaDriftTests` passes.
- [x] 1.3 Add a `SchemaMigratorTests` upgrade case after the pattern of `Widget_context_script_gives_every_dashboard_no_source_and_changes_nothing_else`: apply the scripts through 0022, seed a dashboard with filters, a palette, a source, two widgets and its version 1, then migrate.
  Verify: the dashboard reads `min_n` null and `show_n` false; its filters, `updated_at`, widgets and version content are unchanged; a `min_n` of 0 or 10001 is refused by `ck_dashboards_min_n`.

## 2. Base metadata of an indicator (api)

- [x] 2.1 Add `Base`, `Missing` and `Categories` to `IndicatorColumnMetadata` (`Indicators/ColumnMetadata.cs`, design D1), appended after `Label` and after the properties that earlier changes append: `WhenWritingNull` and a `Description` for each. Make `ColumnMetadata.Normalize` map `false`, an empty list and an empty kind to null and keep an entry that has any property left (today it builds an entry from its label alone), and make `ColumnMetadata.Check` count and validate every column that has any property. Add `ColumnMetadata.MaxMissingValues = 20`, `ColumnMetadata.MaxMissingLength = 500` and `AnswerKinds` (`single`, `multiple`).
  Verify: `ColumnMetadataTests`: an entry with only a label round-trips to the JSON `add-indicator-column-metadata` pins, one with the three properties unchanged, one with `base: false` and nothing else to no entry, and an entry with `base: true` and no label is kept and counted against the limit of 100 columns.
- [x] 2.2 Add the rules of design D1's table to `ColumnMetadata.Check`, with their keys and messages.
  Verify: unit tests, one per row of the table: `Two base columns`, `Too many values`, `Unknown kind of answers` (API parts), a value listed twice, an empty value, a NUL character, 501 characters, `base` with `missing` on one column, and `Too many columns with facts` (60 labels and 41 columns with facts are refused with the existing message, 100 in all are accepted).
- [x] 2.3 Save and restore the properties with the version, as the column names are saved and restored.
  Verify: integration tests through the save endpoint:
  - `Base declared` (API part);
  - `Base restored with a version`;
  - a save that omits `columns` keeps them, and `{}` clears them, as `add-indicator-column-metadata` rules.
- [x] 2.4 Update `docs/scripts.md`: the **Column names** row of the table in "The indicator's name and metadata" gains `base`, `missing` and `categories` (and its sentence on the keep rule says that a save with column names replaces all of them, properties included), with which columns to declare as answers (design D3, D4): the question, code and answer columns of a long % table (`variable`, `orden`, `categoria`), never a household dimension; the base of a count table (`value`) and its question column when a widget spans several questions; the denominator as the base of a wide table; `multiple` when any question of the column is multiple-choice; and not for per-answer bases such as `centro_motivo_hora`.
  Verify: the table matches design D1, every case of design D4 has its Duitama example, and the analytics docs-examples test still runs the file's code blocks unchanged.

## 3. Base n, numerator and denominator in the query (api)

- [x] 3.1 Read the indicator's base metadata in `QueryAsync` (design D5): one statement in the query's read-only transaction, for the version `CurrentIndicatorsAsync` takes (newest by number, code not null), read with `ColumnMetadata.Read`, as `IndicatorEndpoints.CurrentColumnsAsync` reads it. Resolve it against the described table into `IndicatorBases` (a base only as a number; answers and no-data values only as text or true/false), and pass it to `Plan` as a new optional parameter.
  Verify: integration tests:
  - `Base column no longer published`;
  - a base held as text is ignored;
  - an indicator whose newest version is a removal has no metadata;
  - an indicator without metadata plans as before.
- [x] 3.2 Append the outputs of design D5 to the plain and the stat statements, after every output they have today, with `IndicatorSql.Figures`; the histogram's per-row statement and the box plot's take none. Read the figures in `ReadRowsAsync` by those ordinals, the map's count and tooltips staying at 3 and 4. Add `Numerator`, `Denominator` and `N` to `IndicatorRow`, and `N` to `IndicatorQueryResponse` under design D4's rule.
  Verify: `IndicatorQueryBuilderTests`:
  - `A_grouped_query_that_is_not_a_map_has_no_extra_outputs` passes unchanged;
  - a ratio's statement is today's text with the two sums appended to its select list, a base column adds its sum, and the widget n window appears only under D4's rule;
  - a histogram's and a box plot's statements are today's text;
  - a map's count and tooltip outputs keep their ordinals and still read right (`IndicatorTooltipTests`).
- [x] 3.3 Append the same outputs to every branch of the series cells statement (NULLs in the "left out" branch), with `Numerator`, `Denominator` and `N` on `GroupTotal`, `SeriesTotal` and `GrandTotal`, and read them in `ReadSeriesAsync`.
  Verify:
  - `SeriesQueryBuilderTests`: `The_series_domain_statement_is_the_text_it_was_before_a_pie_shared_it` passes unchanged;
  - every branch has the same column count;
  - `IndicatorSeriesQueryTests` cases for a ratio's cell and totals figures.
- [x] 3.4 Add `BaseQueryBuilder.Counted` (design D5): the largest per-answer sum for each mark, total and the widget, an answer being one combination of every declared column (design D3), run only when an answer column is declared, matched in C# by part and keys, timestamps through `DataValues.IsoTimestamp`.
  Verify: unit tests pin the parts' SQL (no `IS NOT DISTINCT FROM`, the same `WHERE` and parameters as the plotted statement, every declared column in the inner `GROUP BY`). Integration tests:
  - `Total over the answers`;
  - `Two questions in one widget` (476, not 952, with `variable` and `categoria` declared);
  - `Answers of one question`;
  - `Subgroups added up`;
  - `Values unchanged by the declaration`;
  - declaring `orden` with `categoria` gives the same n as `categoria` alone;
  - a missing group's n;
  - a timestamp group.
- [x] 3.5 Write the integration tests of the n (a new `IndicatorBaseQueryTests`) over a seeded long table shaped like teatro_hoy, with invented values:
  - `Numerator and denominator returned`;
  - `Denominator as the base`;
  - `Declared base column`;
  - `Measure without a base` and `Measure without a base answers as before` (byte for byte, after the pattern of `IndicatorQueryTests.Every_other_kind_answers_what_it_answered_before_with_no_domain`);
  - `Nothing declared` (no widget `n`), also for a stat tile;
  - `Dashboard filter before the n` and `Widget n after the filters`;
  - the widget n past the row limit;
  - a box plot's rows unchanged.

  Verify: the tests pass, and a new pinned test holds a ratio column chart's whole response JSON.
- [x] 3.6 Carry each cell's figures in `SeriesTable.cs` (`Figures` next to `Cells`), with a share cell taking its row's n (design D2). Add cases to `indicators/testing/series-tables.json`: a ratio grid with figures, a 100% layout, and a Likert grid whose cells take their group's n.
  Verify: `SeriesTableTests` passes over the new cases (the UI twin follows in 9.3).
- [x] 3.7 Update OpenAPI for the new response fields.
  Verify: an OpenAPI test after the pattern of `IndicatorOverviewTests.The_openapi_document_describes_the_series_of_a_widget_and_its_four_new_kinds` describes `numerator`, `denominator`, `n` on rows and totals, and the response's `n`.

## 4. No-data values (api)

- [x] 4.1 Add `missingFooter` to `WidgetConfigInput` and `WidgetConfig` (`WhenWritingNull`, kept as given), and the kind rule of design D6 to `CheckConfig`. `WidgetQuery.For` sends it only when it is set and the kind takes it.
  Verify:
  - `WidgetDisplayTests` still pins the stored text of configs without it;
  - `IndicatorValidationTests`: `Stat tile refused` (API part), every kind that takes it is accepted, and the message names the kinds;
  - `WidgetQueryTests` cases shared with `query-for.spec.ts` (9.2), among them a config without it giving today's query;
  - the OpenAPI test of 3.7 also finds `missingFooter` on `WidgetConfig` and `WidgetConfigInput`.
- [x] 4.2 Plan the exclusion (design D6): `AND NOT ({left})` on the plotted statements, the series domain, the pie domain and the overall statement of `add-chart-annotations`, with `@ng` and `@ns`, only with the option and declared values. Keep "(none)" rows.
  Verify: `IndicatorQueryBuilderTests` and `SeriesQueryBuilderTests`:
  - without the option, every statement's text is unchanged;
  - with it, the condition keeps a null group;
  - the domain statements change only with the option;
  - the overall statement carries the same condition as the plotted one.
- [x] 4.3 Add the no-data statement and `IndicatorQueryResponse.NoData` (design D6).
  Verify: integration tests:
  - `Comuna with no block`;
  - `Answer left out of a series` (the legend and the shares);
  - `Widget without the option`;
  - `Nothing left out` (no `noData`);
  - `Rows with no value kept`;
  - `Matched as exact text` (`SIN NOMBRE` stays);
  - a measure's `share`;
  - the n of the left-out rows with answers declared;
  - a pyramid's "left out" count without them.

## 5. Base settings of widgets and dashboards (api)

- [x] 5.1 Add `MinN` and `ShowN` to `WidgetDisplay` (design D7), appended after `ShowDescription` and the members that earlier changes append, with `Normalize()` keeping `0` and `false` and returning null only when every field is null, accepted on every kind, and the rule of design D7 in `CheckDisplay`.
  Verify:
  - `WidgetDisplayTests`: the configs without them serialize as before, and `{"display":{"minN":0}}` and `{"display":{"showN":false}}` round-trip;
  - `IndicatorValidationTests`: `Threshold out of range` (widget part), and a chart-type change with both set is accepted;
  - the OpenAPI test of 3.7 also finds `minN` and `showN` on `WidgetDisplay`.
- [x] 5.2 Add `DashboardValidation.Bases` and `PUT …/bases` (`SaveDashboardBases`, role `analyst`) as design D8, with OpenAPI; add `minN` (`WhenWritingNull`) and `showN` (written only when true) to `DashboardView`, after `Source`; copy both in `DuplicateAsync`.
  Verify: a new `DashboardBasesTests`:
  - `Analyst sets the threshold` (API part);
  - `Viewer cannot change base settings` (403);
  - `Base settings saved unchanged` (`updated_at` unchanged, no version);
  - `Threshold out of range` (dashboard part);
  - the view of a dashboard without base settings is byte for byte today's;
  - the OpenAPI document describes the request, the response and the view's fields.

  And in `DashboardTests` (where the duplicate tests live): `Base settings copied` and `Base settings of copies independent`.
- [x] 5.3 Add `DashboardChangeKinds.BasesChanged`, and change `Content` (it gains `minN` and `showN` between `source` and `widgets`: update its callers, among them the helper of `ProductionShapesTests`), `ReadContent` and `VersionContent`, the restore and the version details as design D8.
  Verify: `DashboardVersionsTests`, unit:
  - the content of a dashboard without base settings, with or without a source, is byte for byte the content today's code writes;
  - a content with `minN` 30 and `showN` true reads back the same.

  `DashboardVersionTests`, integration:
  - `Threshold changed`;
  - `Base settings restored`;
  - `Version recorded before base settings`;
  - `Dashboard without base settings`;
  - the version details return the settings (`Version viewed with its threshold`, API part).
- [x] 5.4 Add the C# `BaseFigures` helper (new `Indicators/BaseFigures.cs`, design D7, D9): the settings in force (the widget's over the dashboard's), the small-base rule, the tooltip figures, the n line, the no-data line and the small-base sentences, and `ReportFormat.Figure`. Write the shared fixture `tamiza-ui/src/app/pages/projects/indicators/testing/base-figures.json` with their cases and expected English and Spanish texts.
  Verify: a new `BaseFiguresTests` reads the fixture as `SeriesTableTests` reads `series-tables.json` and passes:
  - `Comunas below 30`, `Turned off for a widget`, `Own threshold` and `Value with no n`;
  - the lines' texts in English and Spanish;
  - `Figures of a percent indicator` (API part): 56 and 121 as counts.
- [x] 5.5 Update `docs/tamiza-brief.md`: the data model (`dashboards.min_n`, `show_n`, the three column properties, `display.minN`, `display.showN`, `missingFooter`), and this change's entry in section 8's program list.
  Verify: the section names every field as design D1, D6, D7 and D8 define it.

## 6. Scheduled emails and the widget workbook (api)

- [x] 6.1 Read the dashboard's `MinN` and `ShowN` in `ReportDataBuilder.BuildAsync`. In `BuildWidgetAsync`, add the small-base sentence, the n line and the no-data line to the widget's notes; add the n column to `AxisTable`, `PieTable` and `MapTable` while a setting is in force and the values have an n; and mark small-base cells' text with " *", also in `SeriesReportTable` (design D13). Add the `ReportText` keys in English and Spanish.
  Verify: `ReportDataBuilderTests`:
  - `Base n line in the email`;
  - `Lines before the footnote` (in the body and the plain text the lines are among the notes above the table, and the footnote stays last);
  - `n column in the email` (`#,##0`, 16 for `COMUNA 7`);
  - `No-data line in the email`;
  - `Marked value`;
  - `Email of a dashboard without base settings` (byte-equal model to the one built before);
  - a measure widget with no base column and a threshold in force keeps its columns;
  - a stat tile's n line.

  And `ReportTextTests.Both_languages_have_the_same_keys` passes.
- [x] 6.2 Write the workbook side.
  Verify:
  - `ReportWorkbookTests`: `Workbook cell of a small base` (the cell holds 0.5, the "Summary" the note) and the n column's format;
  - `ReportHtmlTests` and the plain-text tests: the mark and the notes in the body, within the 80 KB budget.
- [x] 6.3 Add optional `minN` and `showN` to `WidgetExportRequest` (design D13) and apply them in `BuildWidgetExportAsync`, over any `minN` or `showN` in the request's `config.display`.
  Verify:
  - `WidgetExportTests`: `Threshold in force on screen` (API part), `Request without base settings` (byte-equal workbook model to today's), `missingFooter` in the sent config leaves the rows out, a config whose `display.minN` is 10 sent with a `minN` of 30 uses 30, and a `minN` of 10001 is refused with its key;
  - `WidgetWorkbookTests`: the data sheet's n column and the "Summary" notes of a widget under a threshold;
  - the OpenAPI document describes the two request fields.
- [x] 6.4 Send a scheduled email from `ReportSenderTests` with a widget under a threshold, the n line and the no-data option.
  Verify: the `Reports` unit and integration tests pass.
- [x] 6.5 Pass the indicator's declared answer columns to `ReferenceLines.Of` (design D16) from `ReportDataBuilder` and the widget workbook, leave out an `overall` line over them, and add the note ("No overall line: the bars are the answers of one question.") to `ReportText` in English and Spanish. Add the case to `reference-lines.json`.
  Verify: `ReferenceLinesTests` passes over the new case; `ReportDataBuilderTests`: `Overall line over places` (email part: no overall line, the note) and `Overall line over comunas` (the line as before).

## 7. MCP (api)

- [x] 7.1 Add the payload fields of design D14 to `Row()`, `SeriesPayload`, `BoxPayload`, `query_indicator` and `WidgetDataAsync`, reading the dashboard's settings and each widget's in `get_widget_data`, and pass the declared answer columns to `ReferenceLines.Of` (design D16).
  Verify: `McpDashboardToolsTests`:
  - `Base figures of a widget`;
  - `Measure without a base through MCP`;
  - `noData` for a widget with the option;
  - the widget `n` only under design D4's rule;
  - no `overall` line, and its note, for a bar chart grouped by a declared answer column;
  - a long result cut in its rows with the existing hint.
- [x] 7.2 Add `minN` (0 turns it off) and `showN` to `update_dashboard`, its "give at least one" rule and the outputs of `get_dashboard` and `get_dashboard_version`. Take `display.minN`, `display.showN` and `missingFooter` in the widget tools.
  Verify: `McpDashboardToolsTests`:
  - `Threshold through MCP` (one version per request);
  - `No-data option on a stat tile through MCP`;
  - `get_dashboard` of a dashboard without base settings answers byte for byte as before.

  `McpDashboardVersionToolsTests`: a version's settings are returned.
- [x] 7.3 Describe `base`, `missing` and `categories` in `save_script`'s `columns` (`ColumnsHelp`, which says today that a property other than `label` is ignored) and return them from the read tools: `list_indicators`, `get_script` and `save_script`'s result return the map, and `get_indicator_columns` returns `IndicatorColumns.Declared`.
  Verify: `McpScriptToolsTests`: `Answers declared by Claude`, `Two base columns through MCP`, and the declarations in the answer of `get_indicator_columns`.
- [x] 7.4 Update the tool texts of design D14:
  - the `query_indicator`, `get_widget_data`, `get_dashboard` and `update_dashboard` descriptions;
  - `create_widget`'s config template;
  - a new `BasesHelp` constant after `ContextHelp` (and `AnnotationsHelp`) in `create_widget` and `update_widget`, and `ChartTypeChangeHelp`; `DisplayHelp`'s pinned sentence stays as it is;
  - `WidgetConfigInput.Display`'s and the new fields' `Description`s.

  Update in the same step the sentences `McpToolCatalogTests` pins, such as `Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale`.
  Verify: `McpToolCatalogTests` passes (that test, no enums for nullable fields, every tool documented), and a new case checks the base words.
- [x] 7.5 Write the "Bases" section of `Mcp/Instructions.md` (design D14, with which columns to declare as answers) and the new settings and result fields in `docs/mcp.md`. Update the `docs/mcp.md` sentences that `IndicatorDocsTests` pins in the same step, among them the table of the kinds of change, which gains a `basesChanged` row (the test checks every kind of `DashboardChangeKinds.All`), and the sentences on the lengths and keys of `columns`.
  Verify: `McpGuideTests`, with new required phrases (`categories`, `missing`, `minN`, `missingFooter`), and `IndicatorDocsTests` pass. `AssistantPrompts.Version` is unchanged (5), and `PromptKitTests` pass.

## 8. Production shapes (api)

- [x] 8.1 Extend the production shapes fixture (`indicators/testing/production-shapes.json`, run by `ProductionShapesTests.cs` and `production-shapes.spec.ts`) as design D15:
  - `stored`, `query`, `saved`, `uiQuery`, `formSaved` and the dashboard's `content` goldens of every existing shape hold unchanged;
  - each measure, box plot and histogram shape's `plan` equals its golden;
  - each ratio shape's `plan`, with the outputs `IndicatorSql.Figures` lists removed from each select list, equals its unchanged golden byte for byte;
  - a new top-level `declared` entry holds invented column maps, and the shapes are planned again with `base`, `missing` and `categories` declared and no setting on;
  - two new shapes: a ratio column chart with `display.minN` and `display.showN`, and one with `missingFooter`. The runners pin the number of shapes (20 after `add-widget-context`: `ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` and the spec's `toHaveLength` and `new Set(…)` assertions) and the list of chart types. `add-chart-annotations` raises the count to 25 before this change starts, so raise each pinned count by two from what it is when this task starts (27 after `add-chart-annotations` alone, more if `add-declared-category-order` or `add-category-highlight` add shapes first), list the two shapes in the spec as it lists `contextShapes`, and record the total in design.md; `improve-stat-cards` adds two after this change.

  Invented values only: no production data enters the repository, and no golden of an existing shape is regenerated; the goldens of the two new shapes are written from the code of this change.
  Verify: `ProductionShapesTests` passes. Every shape normalizes to byte-identical stored JSON; ratio shapes keep every clause and parameter of their plan; measure shapes keep their whole plan; the dashboard keeps its version content, with and without a source; the two new shapes query what the same shapes without the settings query, apart from the exclusion of `missingFooter`.

## 9. UI: types and twins

- [x] 9.1 In `core/projects/indicators.service.ts`, add:
  - `numerator`, `denominator` and `n` on `QueryRow` and `QueryTotals`, and `n` and `noData` on `QueryResult`;
  - `minN` and `showN` on `WidgetDisplay`, and `missingFooter` on `WidgetConfig`;
  - `DashboardBases`, and optional `minN` and `showN` on `Dashboard` and `DashboardVersionDetails` (a missing `showN` reads as false);
  - `basesChanged` in `dashboardChangeKinds`, and `saveBases(projectId, dashboardId, bases)`;
  - `minN` and `showN` on `WidgetExportRequest`.

  Add the column map properties to the types `add-indicator-column-metadata` defined: `ColumnMetadata` and `ColumnDraft` (`core/projects/pipeline.service.ts`), and `draftsOf`, `columnsOf` and `sameColumns` in `pages/projects/column-names.ts`, which today keep only a row with a label. Add `declared` to the type of `get_indicator_columns`' answer.
  Verify: `npx ng build` type-checks, `indicators.service.spec.ts` covers `saveBases`, and `column-names` keeps a row with only a base, values that mean no data or a kind of answers.
- [x] 9.2 In `widget-view.ts`, `queryFor` sends `missingFooter` only when it is set and the kind takes it (so `savedConfig` drops it on another kind), and `displayFor` keeps `minN` and `showN` for every kind.
  Verify:
  - `query-for.spec.ts` shares the cases of 4.1;
  - `widget-view.spec.ts`: changing `display.minN` sends no query, changing `missingFooter` sends one, and `Form saved unchanged` holds for a widget saved before base settings (UI part).
- [x] 9.3 Carry each cell's figures in `series-table.ts`, with the share rule of design D2.
  Verify: `series-table.spec.ts` passes over the cases added to `series-tables.json` in 3.6.
- [x] 9.4 Add `indicators/base-figures.ts` and `figureFormatter` in `format.ts` (design D7, D9, D11): the settings in force, the small-base rule, the tooltip lines, the n line, the no-data line and the small-base note, twins of `BaseFigures`. Add their strings.
  Verify: `base-figures.spec.ts` reads `testing/base-figures.json` (5.4) and gives the same English and Spanish texts, and `translations.spec.ts` passes.

## 10. UI: tooltips of charts without a series

- [x] 10.1 Give the plain layout of `axis-chart.ts` hit areas and a one-series `MarkGrid`, with `role="application"`, the key help and the live region of design D9. Remove the marks' native titles, and keep the truncated labels' titles. Move the specs that read the marks' titles (the `titles` helper of `chart-widgets.spec.ts` and the test "plots the ratio of each group when asked…" of `script-editor/indicator-view.spec.ts`) to a helper that reads each mark's tooltip, keeping their expected texts.
  Verify: `chart-widgets.spec.ts`:
  - the role assertion of "describes itself to assistive technology and offers the same numbers as a table" now expects `application`;
  - the tests that read titles through the `titles` helper pass with the same expected texts;
  - `Column tooltip without a series`;
  - `Stepping through columns`;
  - `Bars run down`;
  - a line's points and a histogram's `Histogram bar`;
  - Home, End and Escape;
  - the announced text.

  And `script-editor/indicator-view.spec.ts` passes with its expected bars.
- [x] 10.2 Do the same for `pie-widget.ts`.
  Verify: the pie assertions of `chart-widgets.spec.ts` that read slice titles (order, "Other", shares) pass from the tooltips with the same texts, and specs for `Tapping a slice` (the tooltip stays until a tap elsewhere), the arrows, and the "Other" slice with no n.
- [x] 10.3 Show the figures of `base-figures.ts` in these tooltips.
  Verify: specs for `Rate of a comuna`, `Figures of a percent indicator` (UI part) and `Declared base in a tooltip`, with a stub name resolver.

## 11. UI: figures and small bases in every chart

- [x] 11.1 Add the figures and the small-base line to the tooltips of `series-plot.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts` and `boxplot-chart.ts` (a series line: each series' n only).
  Verify:
  - `series-widgets.spec.ts`: `Cell tooltip with its base`, `Segment tooltip` and `Keyboard` still pass;
  - `boxplot-chart.spec.ts`: a box under the threshold says so.
- [x] 11.2 Add the figures to `map-tooltip.ts` and the dashed outline to `map-engine.ts` (design D9, D10).
  Verify:
  - `map-tooltip.spec.ts`: `City block`, `Markup in a value` and `Own caption text and unit in a map tooltip` unchanged, `Ratio columns already in the tooltip`, `Ratio of a block`;
  - `map-engine.spec.ts`: a small-base shape keeps its class and gets `dashArray`.
- [x] 11.3 Draw small bases as design D10: opacity and dashed outline on bars, columns, segments, slices and boxes; hollow dashed points; the heat map's hatch pattern with an id per chart, drawn from top-left to bottom-right (the empty-cell pattern runs the other way) and with no `transform` or `patternTransform`; the dimmed stat figure; the summary's count.
  Verify: specs:
  - `Columns dimmed` (same height);
  - `Heat map cell` (same fill, a hatch rect, a pattern id unique to two charts on one page, a pattern other than the empty cell's and not rotated);
  - a dimmed stat tile;
  - no `transform` attribute in any chart's SVG.
- [x] 11.4 Draw the lines under the chart in `widget-view.ts` (design D11), between the chart's box and the footnote, with the box cut at its own box while a line shows, and add `exportNotesBelow`, passed by `project-indicators.ts` through `widget-exporter.ts` (`ImageJob`, `ImageInput.notesBelow`) to `composeImage`, with `ExportParts.stat.dimmed` for a tile. Mark the tables' cells and add the n column in `widget-table.ts`, the axis, pie and map table models and `crosstab-widget.ts`.
  Verify: `widget-view.spec.ts`:
  - `Line shown`, `Hidden by a widget` and `Shown by a widget`;
  - `Widget without a base n` (no line);
  - `Lines above the footnote`, and a line clamped like the footnote, with its whole text in its title;
  - `Rate of the left-out rows`, `Share of a count` and `Nothing left out`;
  - `Nothing dimmed`.

  Table specs: `Small base in a table`, `Table without base settings`, and a measure with no base column keeps its columns. `export-parts.spec.ts` and `image-layout.spec.ts`: `Dimmed columns in an image` and `Lines in an image` (UI parts), the lines after the chart's notes, and a dimmed stat figure.
- [x] 11.5 Add print cases to `dashboard-print.spec.ts`.
  Verify: `Printed in black and white` (UI part): the dashes and the lines are in the printed DOM, and the hatch pattern is inside the chart's SVG.
- [x] 11.6 Run the production shapes fixture in the UI (design D15): every shape draws as before with no setting, and the only differences are the tooltips' new lines.
  Verify: `production-shapes.spec.ts` passes with its goldens unchanged.
- [x] 11.7 Pass the indicator's declared answer columns (from `IndicatorEntry.columns`) to `referenceLineValues` (design D16): no `overall` line over them, the note under the chart and in the image's notes, and the widget form no longer offering `overall` for such a widget. Add the string in English and Spanish.
  Verify: `reference-lines.spec.ts` passes over the case added in 6.5; `widget-view.spec.ts`: `Overall line over places` and `Overall line over comunas` (UI parts); `widget-dialog.spec.ts`: the overall kind is not offered for a bar chart grouped by a declared column, and a saved one is kept on an unchanged save.

## 12. UI: forms, dashboard settings and docs

- [x] 12.1 Add the "Base" group to `widget-dialog.ts` (design D12), with its signals, `displayDraft()`, `formConfig`, the hints and `fieldFor`, and pass the dashboard's settings in `WidgetDialogData`.
  Verify: `widget-dialog.spec.ts`:
  - `Threshold previewed` (no query);
  - `No-data values named` and `Nothing declared for the columns`;
  - the D4 hints;
  - a chart-type change to a stat tile drops `missingFooter` and keeps `minN`;
  - a server error on `display.minN` shows on its field;
  - `Form saved unchanged` sends the same config.
- [x] 12.2 Add `dashboard-bases-dialog.ts` and the **Base sizes** action after **Source** (design D8). Pass the settings of the dashboard or the shown version through `widget-grid` to each `widget-view`, and send `minN` and `showN` with a widget's workbook request.
  Verify:
  - `dashboard-bases-dialog.spec.ts`: keyboard access, empty means off, 0 refused;
  - page specs: `Analyst sets the threshold` (UI part), saving unchanged sends no request, the action hidden for a viewer and while a version is shown, `Version viewed with its threshold` (UI part);
  - `Threshold in force on screen` and `Version without a threshold` (UI parts: the request's settings);
  - `Moved to a dashboard with a threshold` and `Own setting kept by a move`.
- [x] 12.3 Name `basesChanged` in `dashboard-versions-dialog.ts`, in English ("Base sizes changed") and Spanish.
  Verify: `dashboard-versions-dialog.spec.ts` shows "Base sizes changed".
- [x] 12.4 Add the base, the no-data values and the answer kind, with its help on which columns to declare, to the "Column names" section of `indicator-metadata-fields.ts` and `add-indicator-dialog.ts`, through the `ColumnDraft` rows and `column-names.ts` (design D1).
  Verify: specs for `Save form`, `Older version` (as text), at most 20 chips, and the API's errors shown on their rows.
- [x] 12.5 Write `docs/indicators-dashboard.md`:
  - a section "Bases and small samples": what n is, the base column, answer columns with the teatro_hoy and Comercio 0d62 examples (the question, code and answer columns; a widget over two questions), when not to declare them, the widget's n and why it needs a declaration, the threshold, the n line, the no-data line, tooltips and keyboard;
  - in "Reference lines, steps and data labels" (from `add-chart-annotations`): no overall line over declared answers;
  - the new **Base sizes** action in the sentence that lists the **Dashboard actions** menu (in the page's order: **Colors**, **Source**, **Base sizes**), which `IndicatorDocsTests` pins, and a subsection next to "Dashboard source";
  - a sentence in "Words around a widget" on the room the lines take under the chart, as the texts do;
  - a sentence each in "Show as table", "Export", "Printing a dashboard" and "Version history";
  - the limits (threshold 1 to 10,000, 20 values that mean no data).

  Also add to `docs/operations.md` "What leaves the deployment by email" that the email carries the base n figures and the values an indicator declares as no data, which members write as free text.

  Verify: `IndicatorDocsTests` pass, and every behavior in the deltas of `dashboard-indicators`, `dashboard-versions` and `custom-dashboards` is described.

## 13. Integration checks

- [x] 13.1 Run every suite:
  - the whole API suite (unit, then integration) in the background, with the output saved to a file (about 25 to 35 minutes);
  - the UI suite;
  - `npx ng build` and `scripts/check-ui-safety.sh`.

  The analytics suite is not touched by this change.
  Verify: everything passes apart from the failures known on clean HEAD (the two UI polling tests, the time-zone title test, `IndicatorSeriesQueryTests.A_ratio_*` under a decimal-comma culture); the initial bundle stays under its budget. The counts are recorded in design.md.
- [x] 13.2 Measure through the API, on indicator tables of 100,000 and 1,000,000 rows shaped like teatro_hoy, a ratio column chart and a series chart:
  - without metadata;
  - with answer columns declared;
  - with `missingFooter`.

  Verify: each stays well under the 25-second statement limit, and the times are recorded in design.md.
- [x] 13.3 Check the real stack in a separate Compose project (`-p tamizaverify`, fake sign-in and fake Kobo), with invented data shaped like teatro_hoy (comunas with n 16, 20 and 121, a "Sin dato" comuna, a frequency question with "No sabe", two questions with answers of the same name). Check:
  - the threshold 30 and the n line set from **Base sizes**, and turned off per widget;
  - dimmed columns, a hatched heat map cell next to an empty one, and a dashed map shape at 1,280 and 390 px, in the light and the dark theme, with no horizontal page scroll;
  - tooltips by pointer, tap and keyboard on a column chart, a line, a pie and a series chart, with a screen reader pass on one of them;
  - the n and no-data lines, and "Show as table" with its n column and marks;
  - a bar chart over declared answers with a mean and an overall line;
  - the room (design D11): a 3 × 2 stat tile with a subtitle, a footnote and the n line, and a 6 × 4 column chart with a subtitle, a two-line footnote and the three lines;
  - a PNG and an SVG image, and a widget workbook;
  - printing to PDF on A4;
  - a version restored with its settings.

  Verify: findings and fixes are recorded in design.md.
- [x] 13.4 Send a scheduled email of that dashboard to Mailpit.
  Verify: the body's marks, n column and notes, and the workbook's numbers and "Summary", match "Show as table".
- [x] 13.5 From an MCP client:
  - set a threshold with `update_dashboard`;
  - declare `variable`, `orden` and `categoria` as answers with `save_script`;
  - read `get_widget_data`.

  Verify: the figures, `smallBase` and the widget `n` come back as design D14 says.
- [x] 13.6 Run `openspec validate add-small-sample-warnings --strict`.
  Verify: it passes.

## Workflow follow-up

- Write "Deviations from the plan" and "Verification Results" in design.md.
- Sync the delta specs and archive the change once the user has reviewed the verification results. Archiving does not release it: hold the deploy until `improve-stat-cards` is implemented, and ship both in one release (design D17); roll them back together.
- Rebase the later P1 changes of the program with /opsx:update:
  - `improve-stat-cards` (already rebased on the archived changes; check it again against this change's archived text): it reads the `categories` declaration, the response's `n` and `BaseQueryBuilder`'s answer grouping, and already uses this change's largest per-answer sum (design D3) for its corrected denominator. Its rebase copies "Widget query over one indicator" again from this change's text, narrows "Base n counted once across answers" and `pipeline` "Answer columns of an indicator" to values that hold one answer, and gives "Base n of a mark" and "Base n of a widget" the n of a tile with a statistic (its design "Spec ownership");
  - `add-locale-formatting`: the figure formatter and the lines' numbers are new formatter twins (`figureFormatter`, `ReportFormat.Figure`).
