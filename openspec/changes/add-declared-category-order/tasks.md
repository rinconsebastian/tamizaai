# Tasks

**Order.**
- This change was rebased with `/opsx:update` on 2026-11-04 on the archived `add-indicator-column-metadata` and `add-widget-context`. Before group 1 it is rebased once more on `add-chart-annotations`, which is implemented first (see Workflow follow-up).
- Group 1 comes first, on the code as it is before this change: it adds this change's shapes to the production-shapes fixture with goldens from that code, so every later group proves it moved nothing.
- Groups 2 and 3 (api, then the shared rule on both sides) follow in order. Groups 4 and 5 (ui) need group 3. Groups 6 to 8 (emails, MCP, guide) need groups 2 and 3. Group 9 is last.

The code this change starts from: `WidgetDisplay` ends with `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription` (`add-chart-annotations` appends `ReferenceLines`, `Interpolation` and `DataLabels` before this change, which then appends `Order`); `IndicatorColumnMetadata` holds only `Label`; the widget workbook request already sends `savedConfig`; the production-shapes fixture holds 20 shapes (25 after `add-chart-annotations`); the migrations stop at 0022, and this change adds none.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match).

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`, filtered while working (`--filter "FullyQualifiedName~CategoryOrder"`); the whole suite runs in the background with its output saved to a file.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/` with `TZ=UTC`; one spec with `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`.

## 1. Production-shapes fixture (before any other change)

- [ ] 1.1 Add to `tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json` (design D13 of `add-indicator-column-metadata`) three shapes this change touches that it does not hold yet, with invented names and values that keep the structure (the fixture's `encuesta_hoy`, `pregunta`, `respuesta`…):
  - a column chart sorted by `orden` compared as text and grouped by a numbered question column (`pregunta`: "1. Opción A" … "4. Opción D", whose colors the `dashboard` entry already keeps);
  - a 100% bar over a count indicator with a numbered series column (`respuesta`);
  - a heat map whose rows and columns are numbered columns (`pregunta` by `respuesta`).

  Give each of these, and each of the existing line over text prices, pie, Likert, box plot grouped by a column with kept colors and cross table, a new optional member `indicatorColumns` (a `ColumnMetadataMap`) that gives the group and series columns only a `label`. The fixture has no such member today and the runners ignore it until 3.2 and 3.4 assert that no order applies to those shapes. Leave the `dashboard` entries alone: their goldens stay.

  Write the new goldens from the code at this change's starting commit, as the fixture's other goldens were. The runners pin the number of shapes (20 after `add-widget-context`; `add-chart-annotations` raises it to 25 before this change starts) and the list of chart types: raise each pinned count by three from what it is when this task starts (28 after `add-chart-annotations` alone, 23 if this change landed first, more if other P1 changes add shapes first) in `ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` (with a comment naming this change) and in the spec's `toHaveLength` and `new Set(…)` assertions, list the new shapes in the spec as it lists `contextShapes`, and record the total in design.md. Verify: `ProductionShapesTests` and `production-shapes.spec.ts` pass on the starting commit with the new shapes and every existing shape keeps its goldens; a search for the study's indicator names and category labels (`teatro_`, `escuelas_`, `Duitama`, `COMUNA`, `No sabe`) finds nothing in the file.

## 2. API: the widget's order and the column metadata

- [ ] 2.1 Add `WidgetCategoryOrder` and `WidgetDisplay.Order` (design D7): `WhenWritingNull`, declared last (after `ShowDescription`, or after `DataLabels` once `add-chart-annotations` is in), with a `Description` on each member. `WidgetCategoryOrder.Normalize()` turns an empty list into none and keeps null items, and `WidgetDisplay.Normalize()` returns null only when every member, the order included, is unset. Verify `WidgetDisplayTests`:
  - every pinned stored text of a config without an order is unchanged (`MinimalBefore`, `RichBefore` and the texts of the earlier changes);
  - `{"display":{"order":{"series":["Acceso","Interés","Otra"]}}}` round-trips unchanged;
  - a display that holds only an order is kept, and `{"order":{"group":[]}}` is no display;
  - `A_query_never_carries_the_display` covers the order, and a `WidgetQueryTests` case says `WidgetQuery.For` of a config with an order equals the one without (its UI twin comes in 3.3).
- [ ] 2.2 Add the rules of the first D6 table to `IndicatorValidation.CheckDisplay`, skipped per kind when the chart type is null. Verify `IndicatorValidationTests`: each row with its key and message, including a sorted bar with an order of groups, a box plot ordered by median with an order of groups, a box plot with no group column, an order of series on a pyramid and on a Likert widget, 101 values, a NUL, a value listed twice, and a chart type change from a column chart with an order of series to a pie (refused on `config.display.order.series`).
- [ ] 2.3 Add `Order`, `Last` and `StripPrefix` to the record `IndicatorColumnMetadata` in `tamiza-api/src/Tamiza.Api/Indicators/ColumnMetadata.cs` (the record holds only `Label` today), `WhenWritingNull`, each with a `[Description]`; make `ColumnMetadata.Normalize` keep an entry while any property is set and turn empty lists and `false` into none; add the rules of the second D6 table to `ColumnMetadata.Check`, which `ScriptEndpoints.ValidateMetadata` already calls (`Pipeline/ScriptEndpoints.cs` does not change), and make it count and check every entry that has any property toward `MaxColumns` (design D6); add `CategoryOrder.MaxValues` and `MaxLast` (100 and 20). Verify:
  - `ColumnMetadataTests`: each rule with its key and message, an entry holding only `stripPrefix` kept, an entry with only empty lists and `false` dropped, a 101st column that holds only an order refused with the existing message, and the existing cases passing unchanged;
  - `ScriptsApiTests`: a version saved with an order, values kept last and `stripPrefix` reads back the same from the script and from the indicators overview (`Order saved`, API part); a value in both lists and 101 values are refused with no version made (`Value in both lists`, `Too many values`); restoring a version without an order gives a new version without one (`Restored with a version`); a save that leaves `columns` out keeps the order.
- [ ] 2.4 Describe `display.order` and the three column properties in OpenAPI. Verify: an OpenAPI test after the pattern of `IndicatorOverviewTests.The_openapi_document_describes_the_series_of_a_widget_and_its_four_new_kinds` finds `WidgetDisplay.order`, `WidgetCategoryOrder.group`/`series` and `IndicatorColumnMetadata.order`/`last`/`stripPrefix`.
- [ ] 2.5 Integration tests through the widget endpoints:
  - create and update with an order (`Own order of answers`, API part), and an empty order saved as none (`Empty order`);
  - each 400 of 2.2 with its key (`Order of groups on a stat tile`, `Order of groups without a group column`, `Order of series without a series`, `Order of series of a pyramid or a Likert widget`, `Too many values`, `Value listed twice`, `Sort and own order of groups refused`, `Order by median and own order of boxes refused`);
  - a `PATCH` of an unchanged widget with an order makes no version;
  - a duplicated dashboard and a moved widget keep the order (`Order kept by a copy`, `Order kept by a move`);
  - restoring a version brings back a widget's own order (`Own order restored`), and saving a script with a new order makes no dashboard version (`Indicator order makes no version`).

  Verify: the tests pass in `DashboardWidgetTests`, `DashboardTests` (duplicate) and `DashboardVersionTests`.

## 3. Shared ordering rule

- [ ] 3.1 Write the shared fixture `tamiza-ui/src/app/pages/projects/indicators/testing/category-orders.json` (design D2, D3, D5), worked out by hand. Cases for `arrange`:
  - no declaration, and a declaration with nothing that applies (`sorted` with only an `order`), each give back the same list;
  - an indicator `order`, a widget order that replaces it, and values not listed that keep the given order (listed and natural);
  - `last`, a widget order that lists a value kept last, and `last` with `sorted`;
  - "(none)" and "Other" after the values kept last, from a list where "(none)" was in the middle (a pie ranked by size);
  - a listed value the entries do not hold, exact text (`sí` against `Sí`, `No sabe` against `No  sabe`), and the empty text as a value.

  Cases for the shown text: each separator (`.`, `)`, `:`, `-`, `–`), spaces before the separator, `10 - 14`, `0 - 4`, `1.5 horas`, `1.Sí`, `100.000`, `$10.000`, `1234. x`, a prefix with nothing after it, and `1. Otro` with `6. Otro` (alike values keep their text).

  Verify: `CategoryOrderTests.The_fixture_has_the_cases_the_design_lists` counts them, as `SeriesTableTests` does.
- [ ] 3.2 Write `tamiza-api/src/Tamiza.Api/Indicators/CategoryOrder.cs` (design D3, D5):
  - `Arrange`, `Shown`/`ShownValues` and `For(chartType, config, columns)`, with the resolution table of D3;
  - `ArrangeTable(SeriesTable, orders)`, which moves each column with its cells, shares and total, and the rows.

  Verify:
  - `CategoryOrderTests` runs every fixture case through `PromptKitChecks.FindUp`;
  - unit cases of `For` for every kind of the D4 table (pyramid and Likert series never arranged; stat, histogram and map groups never arranged; a box plot sorted only by median with a group column; null when the indicator gives the group and series columns only labels, which `ProductionShapesTests` also asserts for every shape of task 1.1 with its `indicatorColumns`);
  - `ArrangeTable` keeps every cell with its column.
- [ ] 3.3 In `core/projects/indicators.service.ts`, add `WidgetDisplay.order` and the limits `maxOrderValues = 100` and `maxKeptLast = 20`; in `core/projects/pipeline.service.ts`, add `order`, `last` and `stripPrefix` to `ColumnMetadata`. Verify: `npx ng build` type-checks, `indicators.service.spec.ts` passes, a `query-for.spec.ts` case (twin of the 2.1 `WidgetQueryTests` case) says `queryFor` leaves the order out, and a guard in `CategoryOrderTests` fails when the TS limits differ from `CategoryOrder`'s.
- [ ] 3.4 Write `tamiza-ui/src/app/pages/projects/indicators/category-order.ts`: `arrange`, `shownValues`, `ordersFor` and `arrangeColumns`, the twins of 3.2. Verify: `category-order.spec.ts` runs every fixture case, plus the same `ordersFor` and `arrangeColumns` cases as 3.2, and `production-shapes.spec.ts` asserts that `ordersFor` is null for every shape of task 1.1 with its `indicatorColumns`.

## 4. UI: the order and the text in every chart

- [ ] 4.1 Thread the orders (design D4, D8):
  - `widget-view.ts` computes `ordersFor(chartType(), config(), indicator()?.columns)` and passes it to the chart as a new input `orders`, default null;
  - `pivot`, `chartOrder` and `arrangeColumns` take it at the call sites of D4 (`axis-chart.ts`, `series-plot.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `crosstab-widget.ts`), and so do the pie's parts, table and color domain and the box plot's boxes;
  - the pie's summary names the largest slice by value, not `slices[0]`;
  - `displayFor` keeps `order.group` and `order.series` per D7.

  Verify:
  - specs for each kind with an indicator order and with a widget order: a bar with no series, a sorted bar with a value kept last, a line (`Points in a declared order`), a heat map's rows and columns (`Rows in a declared order`), a cross table (`Values not listed`, `Rows in a declared order`), a pyramid's rows with its sides unchanged (`Bands in a declared order`, `Pyramid sides keep their places`), Likert rows with the scale unchanged, a box plot by group and by median (`Boxes in a declared order`, `Order by median keeps the values kept last at the end`), a pie's slices, legend, table and summary (`Slices in a declared order`, `Largest slice named in a declared order`);
  - the same specs with `orders` null give today's output (`Largest slice first without a declared order`, `Demand curve`, `Sorted by an order column`);
  - `widget-view.spec.ts`: `savedConfig` drops an order the kind does not take, the order of groups under a sort or an order by median, and the order of groups without a group column;
  - `series-table.spec.ts` passes on the unchanged `series-tables.json`.
- [ ] 4.2 Show values with their prefixes hidden (design D5) in every label helper: the `groupLabel` callbacks, `seriesName`, `pivot`'s categories (sorted by the raw value, labelled with the shown text), `crossTableModel`, the map tooltip's heading and the map's table, the accessible summaries and the export parts' legends and captions. Verify: specs that the legend of a 100% bar over `respuesta` reads `Sí` … `No sabe` in the same order (`Numbered answers`), that a tooltip and "Show as table" read `Sí` (`Everywhere the value is shown`), that `10 - 14` and `1.5 horas` keep their text (`Ranges are not prefixes`), that `1. Otro` and `6. Otro` keep their text (`Values that would read alike`), and that the widget filter editor and the Colors form still offer `1. Sí` (`Filters offer the full values`).
- [ ] 4.3 Colors (design D10): the legend passes the arranged columns to `forValues`, and the pie arranges its color domain. Verify specs for the scenarios of the dashboard-colors delta (`Colors follow the order`, `Color kept whatever the order`, `Slices colored in their declared order`, `Bars keep their color`) and `Color kept for the full value`.
- [ ] 4.4 Keyboard: check that `markTooltips` steps through the marks in the arranged order. Verify: a heat map spec with a declared order of columns steps right through its cells in that order (`Keyboard order`), and a series bar spec steps through its segments in the declared order.
- [ ] 4.5 Images: the export parts of a series chart, a pie and a heat map carry the arranged legend with the shown text. Verify: `export-parts.spec.ts` and `image-layout.spec.ts` cases for `Image in the declared order`.
- [ ] 4.6 Print: `dashboard-print.spec.ts` cases for a heat map in a declared order whose column labels hide their prefixes (`Heat map in a declared order`) and for a column chart in a declared order shown as a table (`Table in a declared order`). Verify: the print specs pass.

## 5. UI: the forms and the documentation

- [ ] 5.1 Write `indicators/value-order-editor.ts` (design D9), made like the Likert scale editor:
  - one row per value with Move up, Move down and Remove, focus kept on the moved value, and `aria-label`s that name the value;
  - an Add a value field with a `<datalist>` of the values offered.

  Add its strings in both languages. Verify: its spec covers moving with the keyboard, removing, adding an offered and a typed value, and the labels; `translations.spec.ts` passes.
- [ ] 5.2 In `widget-view.ts`, add the output of the values the widget shows (design D8). In `widget-dialog.ts`, add the **Order of values** fieldset (design D9):
  - the Default/Own pairs for groups and series, with the source of the default worded;
  - seeding from the preview's new output;
  - values listed but not shown, kept apart;
  - the group pair off with its reason while a sort or an order by median is chosen;
  - the colors note;
  - `groupOrder`/`seriesOrder` in `displayDraft`, and `fieldFor` for both keys.

  Add the strings in both languages. Verify `widget-dialog.spec.ts`:
  - `Own order from the preview`, `No new query`, `Back to the default`, `Listed value no longer shown` and `Colors follow the order`;
  - `Saved unchanged`: a widget with no order, saved unchanged, sends the same config, and `production-shapes.spec.ts` still gives every `formSaved` golden;
  - a server error on `display.order.group` shows on its field;
  - `Order dropped by a kind change` and `Order of groups off while sorted`.
- [ ] 5.3 In the "Column names" section (design D9):
  - `ColumnDraft` in `core/projects/pipeline.service.ts`, the row `IndicatorMetadata.columns` holds per column, gains `order`, `last` and `stripPrefix` next to `label`;
  - `draftsOf`, `columnsOf` and `sameColumns` in `pages/projects/column-names.ts` carry them (`metadataOf`, `sameMetadata`, `emptyMetadata` and `save()` in `project-scripts.ts` already go through them);
  - `project-scripts.ts` passes the component the values of the latest test's dashboard view, and its `shownColumns` lists a viewed version's properties;
  - `indicator-metadata-fields.ts` gives each column its **Order** disclosure (summary, the order editor, **Keep at the end**, **Hide numbered prefixes**, the values offered, the note on widgets and colors) and lists apart a column with any saved property that the table lacks.

  Add the strings in both languages. Verify `project-scripts.spec.ts` and `indicator-metadata-fields.spec.ts` (which also tests `column-names.ts`):
  - a save sends `columns` with `order`, `last` and `stripPrefix` next to the names, and a column with only an order is sent (`Order saved`, `Prefixes hidden without a test`, UI parts);
  - moving a value marks the details unsaved, and a row with only empty lists and an unticked box equals no row;
  - `Values of the latest test`, and `Order of a column the latest test lacks`;
  - the test's dashboard view in `indicator-view.ts` still names the columns with the labels being typed;
  - a refused value shows on its row (an error on `columns.{column}.order[i]` reaches that row), a reopened form shows what was saved, and a column with only a name sends no order.
- [ ] 5.4 Document the feature in `docs/indicators-dashboard.md` and `docs/scripts.md` (design D14), and mark this change in the program's list of `docs/tamiza-brief.md` section 8. Verify: `IndicatorDocsTests` pass, every behavior of the dashboard-indicators, dashboard-colors, pipeline and dashboard-versions deltas is described, every sentence D14 lists no longer says a kind has no order, and the limits table shows 100 values per order and 20 kept last.

## 6. Scheduled emails and the widget workbook

- [ ] 6.1 In `ReportDataBuilder` (design D11), compute the orders in `ReadWidgetAsync`, beside its `names`, from `meta.Columns`, arrange `AxisTable`, `PieTable`, `SeriesReportTable` and `BoxplotTable`, and write group and series values with `CategoryOrder.Shown`. Verify `ReportDataTests`:
  - `Answers in order without their numbers` (a 100% bar over `respuesta` with prefixes hidden);
  - `Value kept last` (`comuna` with `Sin dato` kept last);
  - `Widget's own order` (a pie with its own order);
  - a box plot by median with a value kept last;
  - `Nothing declared`: `Shapes_each_widget_like_its_show_as_table_view_in_reading_order` and `Every_chart_type_builds_a_table_or_a_value` pass unchanged.
- [ ] 6.2 Check the body and plain text with the new order. Verify: a `ReportHtmlTests` case and its plain-text twin show the series headers `Sí` … `No sabe` in order, with 10 rows per block as before.
- [ ] 6.3 Check that the widget workbook request, which already sends `savedConfig(widget.chartType, widget.config)` (`exportWorkbook` in `project-indicators.ts`), carries `display.order` to `BuildWidgetExportAsync` (design D11). Verify: a `WidgetExportTests` case lists a widget's series in its own order (`Workbook in the widget's order`), a `project-indicators.spec.ts` case checks that the request carries `display.order`, and the workbook of a version's widget follows the order that version had (`Workbook of a version`).

## 7. MCP

- [ ] 7.1 Arrange the results of `query_indicator` and `get_widget_data` (design D12): the `Row` lists, `SeriesPayload.From` through `ArrangeTable`, and `BoxPayload.From`, with full values and no step at all when nothing is declared. Verify `McpDashboardToolsTests`:
  - `Full values in results`: `get_widget_data` of a widget whose indicator declares an order and hides the prefixes of `respuesta` lists the series in order with `1. Sí`;
  - `Results of a widget that declares nothing`: a `query_indicator` of a line that declares nothing gives the same result text as before the change, and the existing cases pass unchanged.
- [ ] 7.2 Update the tool texts of D12:
  - `WidgetDisplay.Order`'s and `WidgetCategoryOrder`'s `Description`s, and `WidgetConfigInput.Display`'s;
  - a new `OrderHelp` constant after `ContextHelp` in `create_widget` and `update_widget` (`DisplayHelp`'s pinned sentence stays as it is), and `ChartTypeChangeHelp`;
  - the `create_widget` and `query_indicator` templates, which name `order`;
  - `ColumnsHelp` and the description of `save_script` (the sentence "A property other than label is ignored." goes; the pinned sentences stay, and the order words follow them).

  Update in the same step the sentence pinned by `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` and the template assertions of `The_tools_name_the_column_labels_they_take_and_return`. Verify: `McpToolCatalogTests` passes (those tests, no enums, every tool documented), and a new sibling checks the order words of `OrderHelp` and of `save_script`.
- [ ] 7.3 Add MCP write tests:
  - `Widget order through MCP`: `create_widget` for a stacked column chart with an order of series is saved and read back by `get_dashboard`;
  - `Order of groups with a sort refused`: `mcp.validation` with the key, and nothing is saved;
  - `Order through save_script`: `columns` holding an order, a value kept last and `stripPrefix` is read back by `get_script`, `list_indicators` and `get_indicator_columns`.

  Verify: the tests pass in `McpDashboardToolsTests` and `McpScriptToolsTests`.
- [ ] 7.4 Write the guidance:
  - the `Mcp/Instructions.md` bullet of D12;
  - `docs/mcp.md`: `display.order`, the `columns` properties, full values in results and the chart type sentence;
  - the `docs/mcp.md` sentences pinned by `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `…gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do`, with those tests; `The_mcp_documentation_explains_the_column_labels_the_tools_take_and_return` pins sentences that stay as they are (`{}` clears them, and given, it replaces them all; a column given an unknown property is saved with its label only).

  Verify: `McpGuideTests` (with a new case for the order bullet) and `IndicatorDocsTests` pass.

## 8. Script guide

- [ ] 8.1 Add the bullet of design D13 to `Assistant/Prompts/system.md` "The indicator contract" and raise `AssistantPrompts.Version` by one. Verify: `PromptKitTests` (size bound), the assistant prompt tests and `McpGuideTests` (the guide equals `AssistantPrompts`) pass.

## 9. Integration checks

- [ ] 9.1 Run `npx ng build` and `scripts/check-ui-safety.sh`. Verify: the initial bundle stays under the 675 kB budget (the new code sits in the lazy dashboard and scripts chunks), no component style passes 4 kB, the sizes are recorded in design.md, and the safety script is clean.
- [ ] 9.2 Run every suite:
  - the whole API suite, unit then integration, in the background (about 25 to 35 minutes), with its output saved to a file;
  - the UI suite with `TZ=UTC`.

  The analytics suite is not touched by this change: run it only if a task changed `tamiza-analytics`. Verify: everything passes, `ProductionShapesTests` and `production-shapes.spec.ts` included with their goldens untouched since group 1, apart from the failures that a run of the same suites on a clean checkout of the base commit also has, and the counts are recorded in design.md.
- [ ] 9.3 Check the real stack on a separate Compose project (the `tamizaverify` recipe). Seed synthetic indicators shaped like:
  - `teatro_conteo` (numbered `respuesta`);
  - `teatro_hoy` (`comuna` with `SIN NOMBRE` and `Sin dato`);
  - `comercio_lugar_compra` (unnumbered `categoria`);
  - a per-household table for a box plot.

  Declare orders, values kept last and hidden prefixes in the details form and in widgets. Then check:
  - each kind at 1,280 and 390 px, in the light and the dark theme, with no horizontal page scroll;
  - the keyboard order of marks, tooltips, "Show as table" and a screen reader's summary of a pie in a declared order;
  - colors kept and free;
  - PNG, SVG and the workbook;
  - printing to PDF on A4;
  - in the browser's network panel, that moving a value in the widget form sends no query;
  - from an MCP client, `save_script` with and without `columns`, then `get_widget_data`.

  Verify: findings and fixes are recorded in design.md.
- [ ] 9.4 Send a scheduled email of that dashboard to Mailpit. Verify: the body's tables, the plain text and the workbook list groups and series in the widget's order with the prefixes hidden, matching "Show as table".
- [ ] 9.5 Run `openspec validate add-declared-category-order --strict`. Verify: it passes.

## Workflow follow-up

- `add-indicator-column-metadata` and `add-widget-context` are archived and this change is rebased on them (2026-11-04). Before implementing, rebase it once more with `/opsx:update` on `add-chart-annotations` once that change is archived: its "Line chart" text and the scenario `Straight segments by default` go into this change's MODIFIED "Line chart" block, and the display members and help texts it adds (`ReferenceLines`, `Interpolation`, `DataLabels`, `AnnotationsHelp`) are the ones D7 and D12 extend.
- Sync the delta specs and archive the change (`/opsx:archive`) once the user has reviewed the verification results.
- Rebase the later P1 changes of the widget and dashboard program, `add-category-highlight`, `add-small-sample-warnings`, `improve-stat-cards` and `add-locale-formatting`, with `/opsx:update` against the archived specs.
