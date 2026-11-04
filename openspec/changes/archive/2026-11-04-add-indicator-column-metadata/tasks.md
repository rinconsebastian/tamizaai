# Tasks

**Order.**
- Group 1 comes first, on the code as it is before this change: it writes the production-shapes fixture and its goldens, so every later group proves it moved nothing.
- Groups 2 to 6 (api) follow in order; each ends with its tests green. Group 6 can start after group 4.
- Groups 7 and 8 (ui) follow. Group 7 keeps the UI build and suite green from its first type change.
- Group 9 is last.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks, with "(API part)" and "(UI part)" when a scenario is split between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter while working, for example `--filter "FullyQualifiedName~ColumnMetadata"`. The whole suite runs in the background with its output saved to a file.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`; one spec with `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`. Two "stops polling when the page is destroyed" tests fail on unchanged code on the developer's machine.
- **Analytics:** the `tamiza-analytics-test:dev` image, from the repository root with `MSYS_NO_PATHCONV=1`.

## 1. Production-shapes fixture (before any other change)

- [x] 1.1 Write `tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json` (design D13): one entry per shape, with `name`, `chartType`, `columns` (names and types of an invented table) and `stored` (the config text as the API stores it), for:
  - a ratio column chart sorted by `orden` compared as text, with an `equals` filter and `display.color`;
  - a measure column chart with a series in the `percent` layout, over counts;
  - a stacked bar over counts with a series, sorted by value, with an `in` filter on its series column;
  - a bar with an `in` filter on its own group column;
  - a bar with neither measure nor ratio (the implicit `value`);
  - a bar sorted by `orden` compared as a number;
  - a stat tile with `equals` filters on numbered labels ("1. Opción A") and on `orden`;
  - a stat tile with `ignoredDashboardFilters`;
  - a line over text prices with a series and no sort;
  - a box plot grouped by a column that has colors kept by value;
  - a map with four tooltip columns (two with labels, one with a format) and a basemap;
  - a heat map with a ramp;
  - a Likert widget with its scale;
  - a cross table;
  - a pie.

  Add one `dashboard` entry: the *Earth* palette, six text filters without defaults, and colors kept for numbered values of two columns. Every name and value is invented and keeps only the structure (`encuesta_hoy`, `zona`, "Zona 1" to "Zona 7", `orden` "01" to "21"); nothing comes from the study.

  Verify: the file holds each shape above once, and a search for the study's indicator names and category labels (`teatro_`, `escuelas_`, `Duitama`, `COMUNA`) finds nothing.
- [x] 1.2 Add `ProductionShapesTests.cs` (API unit tests) reading the fixture from the repository, as `SeriesTableTests.cs:14` does. `stored` is the text the API writes, not PostgreSQL's `jsonb` output (design D13). For each shape it checks that `stored` reads through `ReadConfig` and serializes back byte for byte, that `WidgetQuery.For` serializes to the golden `query`, and that `IndicatorQueryBuilder.Plan` over `columns` gives the golden `plan` (its SQL texts and parameter names). For the dashboard entry it checks `DashboardVersions.Content` against the golden `content`. Write the goldens from the code at the change's starting commit, with a one-off run that prints them, and keep no generator in the tests.

  Verify: the test passes on the starting commit, and changing one character of a golden makes it fail.
- [x] 1.3 Add `production-shapes.spec.ts` (UI) reading the same fixture: for each shape, `savedConfig` and `queryFor` of `JSON.parse(stored)` equal the goldens `saved` and `uiQuery`, and the widget form opened on the shape and saved without a change sends the golden `formSaved`. Write the goldens from the code at the starting commit. Add to `ProductionShapesTests` a check that each `formSaved` reads through `ReadConfig` to `stored`, so an unchanged form makes no version; a shape for which that does not hold today is an existing defect: record it in design.md and skip the check for it, by name, with that note.

  Verify: the spec and the added check pass on the starting commit.

## 2. Database

- [x] 2.1 Write `tamiza-api/src/dbUp/Scripts/0021_add_indicator_column_metadata.sql` exactly as design D1, with no upper-case `CHECK` in its comment. Add `ScriptVersion.Columns` (`string?`) to `Data/Pipeline.cs`, and map it in `TamizaDbContext` as `jsonb` with the check `ck_script_versions_columns`. Add a `ScriptConventionTests` case: one `ADD COLUMN`, exactly one `CHECK`, no index and no `NOT NULL`.

  Verify: `ScriptConventionTests` and `SchemaDriftTests` pass, and the analytics integration tests still build their template database.
- [x] 2.2 Add a `SchemaMigratorTests` upgrade case after the pattern of the 0020 one: apply the scripts through 0020, seed a script 1 version, an indicator version with metadata and a removal; migrate. Check that the rows are unchanged with `columns` null, that an indicator version takes `{"numerator":{"label":"Hogares"}}`, and that a script 1 version with columns, or an indicator version with a JSON array, fails on `ck_script_versions_columns`.

  Verify: the test passes.

## 3. Column labels of an indicator (api)

- [x] 3.1 Add `Indicators/ColumnMetadata.cs` (design D2): the record `IndicatorColumnMetadata(Label)` with `WhenWritingNull` and a `[Description]`; the static class `ColumnMetadata` with `MaxColumns = 100` and `Normalize`, `Check`, `Read` (stored text to map) and `Write` (map to stored text, null when empty); the static class `ColumnLabels` with `MaxWidgetLabels = 10`, `MaxLength = 80` and the label check. Verify `ColumnMetadataTests` (unit):
  - a label is trimmed, spaces or null mean none, a null entry or one with no property is dropped, and an empty map writes null;
  - keys are written as given (no case change), and an unknown property (`colour`) is dropped;
  - each row of design D3's table gives its key and message: 100 and 101 columns, 80 and 81 characters, a NUL, a line break and a tab, a key `Hogar Menores`, a key `2020_total` (a leading digit), and a key of 200 characters cut to 63 in the message.
- [x] 3.2 Save, keep, restore and remove (design D3). `SaveIndicatorRequest` gains `Columns`; `ValidateMetadata` checks it with the other fields; `AddVersionAsync` copies the stored text of the version it is numbered after when the request leaves `columns` out; `RestoreCoreAsync` copies the source's text; `RemoveCoreAsync` writes null. Verify `ScriptsApiTests` cases:
  - `Labels of a ratio`, `Label trimmed`, `Label cleared` and `Label before the first run` (API part);
  - `Label too long`, `Not a column's name`, `Line break in a label`, `Too many labels` and `Unknown property`, with no version created, and every problem of one save reported at once with the label and format errors;
  - `Code saved without labels`, `Labels replaced`, `Labels cleared` and `Removed and created again`;
  - `Column labels restored with a version` (API part), and a restored version keeps a property this release does not know, written into the stored text by the test.
- [x] 3.3 Read the labels (design D4): `ScriptVersionDetails.Columns` and `IndicatorScript.Columns` through `ToDetails`, `IndicatorEntry.Columns` in `GetOverviewAsync`, and each indicator table column's `label` in `GetColumnsAsync`. Verify:
  - `IndicatorOverviewTests`: an indicator with labels lists them, one without has `columns` null, and an OpenAPI test after the pattern of the box plot's describes `columns` and `IndicatorColumnMetadata.label`;
  - `ScriptsApiTests`: the list, a version's details, a save's answer and the current indicators give the labels (every `ToDetails` path of design D4);
  - the columns endpoint gives `numerator` its label and `orden` none (`Labels of a table's columns`, API part).
- [x] 3.4 Verify `No dashboard version` in `DashboardVersionTests`: saving new labels for an indicator that the widgets of a dashboard plot makes no version of it.
- [x] 3.5 Document the indicator's labels: a **Column names** row in the table of `docs/scripts.md` "The indicator's name and metadata" (1 to 80 characters, at most 100 columns, keyed by the column's name, kept by a save that does not send them), the restore sentence under it, and `docs/tamiza-brief.md:180`. Verify: the limits written are those of `ColumnMetadata` and `ColumnLabels`, and `tests/integration/test_docs_examples.py` still passes in `tamiza-analytics-test:dev` (no code block changes).

## 4. A widget's labels (api)

- [x] 4.1 Add `WidgetDisplay.Labels` (`WhenWritingNull`, a `[Description]`) and extend `WidgetDisplay.Normalize()` (design D5): labels trimmed, null and empty ones dropped, an empty map null, and null only when color, ramp, orientation and labels are all unset. Verify `WidgetDisplayTests`:
  - the pinned texts of the configs without a display are unchanged;
  - `{"display":{"labels":{"numerator":"Hogares que asistieron"}}}` reads and writes back unchanged, keys as given;
  - a display holding only empty or null labels reads as no display.

  Also verify, in the OpenAPI case of `IndicatorOverviewTests` that task 3.3 adds, that `WidgetDisplay.labels` is described.
- [x] 4.2 Add the rules of design D5's table to `CheckDisplay`, with no rule per kind. Verify `IndicatorValidationTests`: one case per row with its key and message, labels accepted on every kind of `ChartTypes.All`, and a chart type change of a widget with labels accepted (`Chart type changed`, API part).
- [x] 4.3 Add a `WidgetQueryTests` case: a config with labels gives the same query as without them (its UI twin comes with the UI types, in 7.3). Verify: the case passes, and the pinned response texts of `IndicatorQueryTests` pass unchanged.
- [x] 4.4 Add integration cases to `DashboardWidgetTests`, `DashboardTests` and `DashboardVersionTests`:
  - `Labels on a stat tile`, and `Labelled column removed from the table` (drawn with no missing column);
  - `Too many labels`, `Label too long` and `Not a column's name` (API part), each a 400 on its key with nothing saved;
  - `Stored settings kept`: a widget stored before labels, sent back unchanged with PATCH, writes nothing and makes no version;
  - `Column labels kept by a move`, `Column labels duplicated`, `Column labels of a copy are independent` and `Widget's column labels restored` (API parts).

  Verify: the tests pass.

## 5. The name shown, emails and workbooks (api)

- [x] 5.1 Add `ColumnLabels.For(indicatorColumns, widgetLabels)` and change `ReportDataBuilder.Plotted` to take the namer (design D6). Write `tamiza-ui/src/app/pages/projects/indicators/testing/column-labels.json` with cases for: a ratio with both columns labelled, a measure, a widget label over an indicator label (`Widget label over the indicator's`), an indicator label alone (`Indicator label used`), a column with neither (`Column's own name`), an empty widget label, and a widget that names no measure (`Default measure`, API part). Verify: a new `ColumnLabelsTests` (unit) reads the fixture and passes every case.
- [x] 5.2 Name the columns in the emails (design D10): `IndicatorMeta.Columns` from `MetaOf`, one namer per widget passed to `Plotted` and to every table's headers. Verify `ReportDataTests` and `ReportHtmlTests`:
  - `Headers with column labels` (body and workbook), `Caption of a stat tile` and `Label changed before a send`;
  - a box plot's group header, a map's tooltip header without its own label, and a histogram's "Rows of" header;
  - `Dashboard without labels`: the existing `ReportDataTests`, `ReportHtmlTests` and `ReportSenderTests` cases, which pin today's captions and headers, pass unchanged;
  - a header "=Hogares" reads back from the workbook as text.
- [x] 5.3 Verify the widget workbook with `WidgetExportTests`: a request whose config carries `display.labels` writes them in the data sheet's headers and the "Summary" line of what it plots (`Widget's own label in the workbook` and `Widget of a version`, API parts), and a request without a display gives the workbook it gives today.
- [x] 5.4 In `docs/operations.md` "What leaves the deployment by email", name the column labels among what an email carries. Verify: the sentence is there and matches `dashboard-reports` "Column labels in a scheduled email".

## 6. MCP (api)

- [x] 6.1 Add `columns` to `save_script` and to its result (design D11). Verify `McpScriptToolsTests`: `Labels saved through MCP`, `Client that leaves the labels out`, `Labels cleared through MCP` and `Label refused through MCP` (`mcp.validation` with the key `columns.numerator.label`, no version created), and `columns` sent for script 1 is ignored.
- [x] 6.2 Verify the reads in `McpDashboardToolsTests` and `McpScriptToolsTests`: `list_indicators` and `get_script` return `columns`, and `Labels of a table's columns` (MCP part).
- [x] 6.3 Add the indicator's columns to `IndicatorInfo` (filled by `MetaAsync`) and `labels`, built with `ColumnLabels.For`, to the widget results of `get_widget_data` and `query_indicator` (design D11). Verify `McpDashboardToolsTests`:
  - `Widget labels through MCP`, `Config replaced without labels` and `Too many widget labels through MCP`;
  - `Labels in widget data`, with no `labels` key on a widget whose columns have none;
  - `query_indicator` with `config.display.labels` returns them;
  - on a dashboard whose widgets and indicators have no labels, no result of `get_widget_data` holds `labels`, and the existing `McpDashboardToolsTests` cases pass unchanged.
- [x] 6.4 Update the tool texts of design D11: `save_script`'s description and its `columns` argument; the descriptions of `get_script`, `list_indicators`, `get_indicator_columns` and `get_widget_data`; `DisplayHelp`; the config templates of `create_widget` and `query_indicator`; the `config` arguments of `create_widget` and `update_widget`; `WidgetConfigInput.Display`'s, `WidgetDisplay.Labels`' and `IndicatorColumnMetadata.Label`'s descriptions. Update the sentences `McpToolCatalogTests` pins (`:88, 98, 158`) in the same step. Verify `McpToolCatalogTests` passes (every argument described, no enums, the pinned sentences), with a case that checks `columns` and `labels` are named.
- [x] 6.5 Write the guidance: `Mcp/Instructions.md:20` and the `display` bullet at `:43`, and `docs/mcp.md` (the `save_script` keep rule, `display.labels` in the tools row at `:77` and the `display` bullet at `:89`, `labels` in widget data, `get_indicator_columns`' `label`, the `widgetChanged` row at `:113`, and an unknown key ignored, as `colour`). Verify `McpGuideTests` and `IndicatorDocsTests` pass, with the pinned `docs/mcp.md` sentences updated in this step.

## 7. UI: types, the name shown and the charts

- [x] 7.1 Add the types: `ColumnMetadata`, `ColumnMetadataMap`, `ColumnDraft` (`{ label: string }`), `IndicatorMetadata.columns` (`Record<string, ColumnDraft>`, design D8), `ScriptVersionDetails.columns` and `IndicatorScript.columns` in `core/projects/pipeline.service.ts`; `IndicatorEntry.columns` (with the map imported from `pipeline.service.ts`, as `IndicatorFormat` is), `WidgetDisplay.labels`, and `maxColumnLabel = 80`, `maxWidgetLabels = 10`, `maxIndicatorColumns = 100` in `core/projects/indicators.service.ts`, whose `WidgetExportRequest.config` doc names `display.labels`. Add to `ColumnMetadataTests` a guard that reads `indicators.service.ts` and fails when its three limits differ from `ColumnLabels.MaxLength`, `ColumnLabels.MaxWidgetLabels` and `ColumnMetadata.MaxColumns`, as `PalettesTests` guards the palettes. Verify: `npx ng build` type-checks and the guard passes.
- [x] 7.2 Add `ColumnNamer`, `rawNames`, `columnLabels` and the `names` parameter of `plotted` to `indicators/format.ts` (design D6). Verify `format.spec.ts` reads `testing/column-labels.json` and passes every case, the same cases `ColumnLabelsTests` passes (`Widget label over the indicator's`, `Indicator label used` and `Column's own name`, UI part).
- [x] 7.3 In `widget-view.ts`, compute `names` and keep `display.labels` in `displayFor` for every kind (design D5, D7). Verify `widget-view.spec.ts` and `query-for.spec.ts`: a label change sends no query, `queryFor` drops the labels (the twin of the `WidgetQueryTests` case of 4.3), `savedConfig` keeps them on every kind and across a change of kind, and a widget without labels saves what it saved before.
- [x] 7.4 Give every chart a `names` input, `rawNames` by default, and use it in its captions, summaries, tooltips and tables, and in the captions its export `parts()` write (`heatmap-chart.ts:318`, `pie-widget.ts:196`, `map-widget.ts:342`, `crosstab-widget.ts:92`; design D7): `axis-chart.ts`, `series-plot.ts`, `pie-widget.ts`, `map-widget.ts` with `map-tooltip.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `crosstab-widget.ts` (`CrossTableContext`), `boxplot-chart.ts` (the header given to `boxTableModel`) and `stat-widget.ts`; pass `names` from `widget-view.ts`. Verify specs:
  - `Caption of a ratio`, `Stat tile description`, `Measure named`, `Table headers` and `Cross table headers` in `chart-widgets.spec.ts` and `series-widgets.spec.ts`;
  - `Map tooltip column without a label` in `map-tooltip.spec.ts` and `map-widget.spec.ts`, a box plot's group header in `boxplot-chart.spec.ts`;
  - `Colors kept by value` in `chart-colors.spec.ts` or `series-widgets.spec.ts`, and `Default measure` (UI part);
  - `Indicator's labels in a version` in `project-indicators.spec.ts`: a version being viewed names its columns with the indicator's current labels;
  - `Dashboard before labels`: every existing chart spec passes unchanged with `rawNames`.
- [x] 7.5 Verify images and print: `image-layout.spec.ts` or `widget-exporter.spec.ts` cases for `Image caption` on a column chart and on each kind whose `parts()` write their own caption (heat map, pie, map, cross table), and `dashboard-print.spec.ts` cases for `Caption printed` and `Table printed`.
- [x] 7.6 Send `display.labels` with the widget workbook request (design D10) in `project-indicators.ts`. Verify `project-indicators.spec.ts`: a widget with labels sends them and no other display field, a widget of a version sends that version's (`Widget of a version`, UI part), and a widget without labels sends today's request.

## 8. UI: the forms and the documentation

- [x] 8.1 Add the **Column names** section to `indicator-metadata-fields.ts`, fed by `project-scripts.ts` and `add-indicator-dialog.ts` (design D8), with its strings (heading, hint, "Not in the table", "The columns are listed after a test.", the read-only list). Verify `project-scripts.spec.ts` and a new `indicator-metadata-fields.spec.ts`:
  - `Columns of the published table`, `Columns of the latest test`, `Column no longer in the table`, `New indicator` and `Older version viewed`;
  - typing a label marks the details unsaved, opening a version with empty fields marks nothing unsaved, and the save sends `columns` as `{ column: { label } }` with the trimmed, non-empty labels;
  - a server error on `columns.numerator.label` shows on its field and one on `columns` under the heading;
  - `Labels of a ratio`, `Label cleared` and `Column labels restored with a version` (UI parts).
- [x] 8.2 Pass the labels being typed to the test's dashboard view (`script-editor/indicator-view.ts`, design D7). Verify `Test view uses the labels being typed` in `indicator-view.spec.ts` or `script-test-panel.spec.ts`.
- [x] 8.3 Add the "Column names" group to `widget-dialog.ts` (design D9), with its strings, the placeholders of the map's tooltip label fields, and `fieldFor` for `display.labels`. Verify `widget-dialog.spec.ts`:
  - `Labels offered`, `Preview without a query`, `Saved unchanged`, `Column replaced` and `Tooltip column placeholder`;
  - `Chart type changed` (UI part): a column chart turned into a bar or a stat tile keeps its labels in the saved config;
  - `Filters keep the column's name`: a widget with a label for `categoria` still names `categoria` in its filter rows and column selects;
  - `Too many labels` and `Label too long` (UI part): a server error shows on the group and its field;
  - `production-shapes.spec.ts` still passes.
- [x] 8.4 Document the feature (design D12, D14) in `docs/indicators-dashboard.md`: the "Column names" section, the sentences in "Show as table", "Tooltip and tooltip columns", "Exporting a widget" and "Printing a dashboard", the generic lists of settings in "Dashboards" (Duplicate), "Moving a widget to another dashboard" and "What makes a version", and "Limits". Update the sentence pinned by `IndicatorDocsTests.The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them` in the same step. In `docs/tamiza-brief.md` section 8, replace item 7l with the program's list of changes. Verify: `IndicatorDocsTests` pass, the links resolve, and every behavior of the deltas of `dashboard-indicators`, `dashboard-versions` and `custom-dashboards` is described.
- [x] 8.5 Run `npx ng build` and `scripts/check-ui-safety.sh`. Verify: the initial bundle stays under its budget (the new code is in the lazy chunks of the dashboard and the scripts), no component style passes 4 kB, and the safety script is clean.

## 9. Integration checks

- [x] 9.1 Run the whole API suite in the background (unit, then integration, about 25 to 35 minutes), with the output saved to a file. Verify: everything passes, `ProductionShapesTests` included with its goldens untouched since group 1, and the counts are recorded in design.md.
- [x] 9.2 Run the UI suite. Verify: everything passes but the two known polling tests, `production-shapes.spec.ts` included, and the counts are recorded in design.md.
- [x] 9.3 Run the analytics suite in `tamiza-analytics-test:dev`, since `docs/scripts.md` changed. Verify: it passes, `test_docs_examples.py` included.
- [x] 9.4 Check the real stack in a separate Compose project (`-p tamizaverify`, its own env file and port, a fake sign-in and a fake Kobo; the developer's own stack and Keycloak untouched). Seed an invented long-format indicator with `numerator`, `denominator` and six dimensions, and a dashboard with a ratio column chart, a stat tile, a stacked chart, a box plot and a map. Then:
  - give the indicator labels in **Column names** after a test, and one widget labels of its own;
  - check captions, tooltips, "Show as table" and accessible names at 1,280 and 390 px, in the light and the dark theme, with no horizontal page scroll;
  - export a PNG, an SVG and a workbook, and print to PDF on A4;
  - send the scheduled email to Mailpit and read its body, plain text and workbook;
  - from an MCP client, call `save_script` without `columns` and check the labels stay, then `get_widget_data`;
  - restore a dashboard version and a script version and check the labels each brings back.

  Verify: findings and fixes are recorded in design.md.
- [x] 9.5 Run `openspec validate add-indicator-column-metadata --strict`. Verify: it passes.

## Workflow follow-up

- Sync the delta specs and archive the change once the user has reviewed the verification results.
- Rebase the later P1 changes of the program with /opsx:update, in order (`add-widget-context`, `add-chart-annotations`, `add-declared-category-order`, `add-category-highlight`, `add-small-sample-warnings`, `improve-stat-cards`, `add-locale-formatting`): their deltas meet the generic lists of "Dashboard versions", "Duplicating a dashboard" and "Moving a widget to another dashboard", refer to "Column labels of an indicator" and "Column labels in widget texts" by name, and extend and run the production-shapes fixture.
