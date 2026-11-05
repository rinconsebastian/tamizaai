# Tasks

**Order.**
- Groups 1 to 5 (api) come first, in order. Each ends with its tests green.
- Groups 6 to 11 (ui) follow, in order; 8 and 9 can run in any order after 7.
- Group 12 is last.

This change is implemented after `add-indicator-column-metadata` and `add-widget-context`, both implemented and archived (2026-11-04). `WidgetDisplay` already ends with `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription` (this change appends after them), the tool texts name them, the widget workbook request already sends `savedConfig`, and the production-shapes fixture (20 shapes) and its two runners exist. Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks that they match). Spec scenario titles are in backticks, with "(API part)" and "(UI part)" when a scenario is split between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~ReferenceLinesTests"`.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`.

## 1. Display fields and validation (api)

- [x] 1.1 Add the settings of design D1 to `Indicators/IndicatorModels.cs`:
  - the record `WidgetReferenceLine(Kind, Value, Label)`, with `Value` and `Label` `WhenWritingNull`;
  - the constants `ReferenceLineKinds` and `LineInterpolations`;
  - `WidgetDisplay.MaxReferenceLines = 4` and `WidgetDisplay.MaxReferenceLineLabel = 40`, beside the text limits;
  - `WidgetDisplay.ReferenceLines`, `Interpolation` and `DataLabels`, `WhenWritingNull`, each with a `[Description]`, appended after `ShowDescription`.

  `WidgetDisplay.Normalize()`:
  - turns an empty list into null, trims labels and drops blank ones, and keeps null lines and empty kinds for validation;
  - keeps `"linear"` and `false` as given;
  - returns null only when every member is unset.

  Verify `WidgetDisplayTests`:
  - `MinimalBefore`, `RichBefore` and `The_texts_of_a_widget_round_trip_unchanged_and_in_the_order_of_the_record` still pass, so the earlier members keep their place;
  - a display with a color, labels, texts and the three new members writes them in the record's order, the new ones last, and `The_limits_are_the_ones_the_design_gives` gains 4 and 40;
  - `{"display":{"referenceLines":[{"kind":"mean"},{"kind":"value","value":0.5,"label":"Meta"}],"interpolation":"step","dataLabels":true}}` round-trips unchanged;
  - `"linear"` and `false` are kept, and a display holding only `{"dataLabels":false}` is kept;
  - `[]` gives no `display`;
  - a label of spaces is none.
- [x] 1.2 Add the rules of design D2 to `IndicatorValidation.CheckDisplay`, with their keys and messages, skipping the per-kind rules when the chart type is null. Verify:
  - `IndicatorValidationTests` has one case per row of D2's table, with its key, the control-character row with a NUL, a line break and a tab;
  - `dataLabels: false` on a stacked bar is accepted;
  - a config with no chart type is checked only for its shapes;
  - `Five lines`, `Line on a pie chart`, `Mean line with a series`, `Overall line over a measure`, `Value line without its number`, `Share past the whole bar`, `Line break in a label`, `Steps on a column chart` and `Stacked bars` are refused, each on its key (API parts).
- [x] 1.3 Check the fields through the widget endpoints. Verify `DashboardWidgetTests`:
  - a column chart with two lines, and a line chart with steps and data labels, are saved and read back as sent;
  - the refusals of 1.2 answer 400 on create and on update, keyed `config.display.…`;
  - `Widget saved before reference lines` (API part): a widget stored before the fields reads without them and keeps its stored text when renamed;
  - `Form saved unchanged` (API part): a PATCH with the config the widget has writes nothing and makes no version.

  Verify also an OpenAPI test after `IndicatorOverviewTests.The_openapi_document_describes_the_texts_of_a_widget_and_its_caption`, describing `referenceLines`, `interpolation` and `dataLabels`.
- [x] 1.4 Extend the production-shapes fixture (`indicators/testing/production-shapes.json`, 20 shapes after `add-widget-context`) with five more, built with the fixture's invented names and values (`encuesta_hoy`, `zona`, "Zona 1"…), never production data:
  - a column chart of a ratio by a subgroup column with a `mean` and an `overall` line;
  - a bar chart by a category column with data labels;
  - a line by a price column with steps;
  - a 100 % bar chart of a count with a `value` line of 0.5;
  - a grouped column chart with an `overall` line.

  Write their goldens from the code of this task (`plan` is the plan without the overall flag, which `ProductionShapesTests` does not pass; the overall statement is pinned by 2.2). Raise the counts the runners pin from 20 to 25: `ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` (the count and the list of chart types, with a comment naming this change) and the spec's `toHaveLength(20)` and `new Set(…)` assertions, and list the new shapes in the spec as it lists `contextShapes`, so the form is checked to send them back. Run `ProductionShapesTests`. Verify:
  - every existing shape keeps its goldens untouched: it normalizes to its stored bytes, `WidgetQuery.For` gives its golden `query`, its plan's SQL is unchanged, and its `formSaved` reads back to `stored`;
  - each new shape round-trips byte for byte.
- [x] 1.5 Check versions, duplicates and moves. Verify:
  - `DashboardVersionTests`: `Line restored away`;
  - `DashboardTests`: `Steps duplicated` (API part);
  - `DashboardWidgetTests`: `Data labels moved` (API part). The moved widget's stored config is byte for byte the one it had.

## 2. The overall value from the engine (api)

- [x] 2.1 Add `IndicatorQueryRequest.Overall` (`bool?`, `WhenWritingNull`, appended last, with a description saying that the page and the server derive it and that no widget stores it). Add `WidgetQuery.Overall(chartType, config)` (design D4). Verify `WidgetQueryTests`:
  - true for a bar, column or line with no series, a ratio and an `overall` line;
  - false for a measure, a series, a pie, a stat tile, a mean line only, and no display;
  - `For` of every config in the existing cases is unchanged;
  - `ProductionShapesTests`: `WidgetQuery.Overall` is false for every existing shape, and true among the shapes of 1.4 only for the ratio column chart with an `overall` line.

  Verify also, in `DashboardWidgetTests`, that a widget created with `overall: true` inside its config stores no `overall` (an unknown key, dropped as today).
- [x] 2.2 Give `IndicatorQueryBuilder.Plan` an `overall` argument, and plan `IndicatorQueryPlan.Overall` (a new last member of the record, after `Boxes`) through the stat tile's code path, only on the plain grouped path. Verify `IndicatorQueryBuilderTests`:
  - its SQL text and parameters equal those of the plan of a `stat` widget over the same config and dashboard filters;
  - there is none without the flag, for a series, for a box plot, for a histogram per row or for a stat tile;
  - `A_grouped_query_that_is_not_a_map_has_no_extra_outputs` still passes.
- [x] 2.3 Give `IndicatorDashboard.QueryAsync` an optional `overall` argument, run the overall statement after the rows, and return `IndicatorQueryResponse.Overall` (`WhenWritingNull`, appended last), null with no row or a non-finite value. Make `IndicatorEndpoints.QueryAsync` pass `request.Overall == true`. Verify `IndicatorQueryTests`:
  - `Same figure as a stat tile` (API part): the overall of a column chart grouped by `comuna` equals the stat tile's value over the same ratio and filters, with and without a dashboard filter;
  - `Rows past the row limit`: with a limit of 2 and 3 groups, the overall still covers every row;
  - denominators that sum to 0 give no `overall`;
  - `Every_other_kind_answers_what_it_answered_before_with_no_domain` still pins every other answer, a bar without the flag included;
  - the OpenAPI document describes `overall` on the request and on the response.

## 3. The values of the lines (api)

- [x] 3.1 Write `Indicators/ReferenceLines.cs` (design D3): `Of(chartType, config, response)` returns the drawable lines in the widget's order, each with its kind, its own label or null, and its value:
  - a mean summed in the response's order;
  - pandas' median;
  - the overall from `Overall`, or from `Totals.All` with a series;
  - a value line's own number;
  - no line with no value.

  Create the shared fixture `tamiza-ui/src/app/pages/projects/indicators/testing/reference-lines.json` with the cases of D3. Verify `ReferenceLinesTests` reads it (as `SeriesTableTests` reads `series-tables.json`) and passes every case, `Four bars`, `Three bars`, `Missing group value counted` and the values of `Overall unlike the mean` among them.

## 4. Emails and workbooks (api)

- [x] 4.1 Add `ReportReferenceLine` and `ReportWidget.ReferenceLines` (empty by default, appended after `CaptionHidden`). In `ReportDataBuilder.ReadWidgetAsync` (the method that queries; `BuildWidgetAsync` adds the texts afterwards), pass `WidgetQuery.Overall(widget.ChartType, widget.Config)` to `QueryAsync` and fill the lines where it builds the table of a bar, column or line that is shown, with a series or without (design D10). Add to `ReportText`, in English and Spanish, `referenceLines`, `referenceMean`, `referenceMedian`, `referenceOverall` and `referenceValue`. Verify:
  - a `ReportDataTests` case: a column chart with a `mean` line, an `overall` line and a `value` line labelled "Meta" has three lines with their values and texts, the texts with the indicator's unit, and a 100 % bar's value line is written as a share;
  - `Every_chart_type_builds_a_table_or_a_value` passes;
  - `ReportTextTests.Both_languages_have_the_same_keys` passes, and a new case pins "Mean", "Median", "Overall", "Reference" and "Promedio", "Mediana", "Valor general", "Referencia".
- [x] 4.2 Write the lines:
  - in `ReportHtml.Block` and the plain text, after the table and its "more rows" and "more columns" lines and before the footnote;
  - in the email workbook's "Summary", one item per widget, after its subtitle and footnote items and before its notes;
  - in the widget workbook's "Summary", after "Plotted".

  Verify:
  - `ReportHtmlTests`: `Lines under the table` ("Mean: 34.17% de los hogares"), `Lines before the footnote` (HTML and plain text), and `Default labels in Spanish` ("Promedio", "Valor general");
  - `ReportWorkbookTests`: `Lines in the workbook`;
  - `WidgetWorkbookTests` and `WidgetExportTests` (with the saved config the page now sends): `Lines in the summary`;
  - `Widget without reference lines`, in both the email and the widget workbook: the block, the sheets and the summary are byte for byte as before.

## 5. MCP (api)

- [x] 5.1 Return `referenceLines: [{ kind, label, value, formatted }]` from `query_indicator` and `get_widget_data` (in the shown result, beside `labels`, `subtitle`, `footnote` and `caption`), from `ReferenceLines.Of`, with English default labels and formatted as the result's own values are, null when there are none. In `WidgetDataAsync`, pass `WidgetQuery.Overall` to `QueryAsync`; in `query_indicator`, set the request's `Overall` from `WidgetQuery.Overall` over `config.Normalize()` when a chart type is given (design D11). Verify `McpDashboardToolsTests`:
  - `Lines in widget data`;
  - `Overall line previewed`: the value equals a stat tile's over the same filters, and nothing is saved;
  - `Widget without reference lines`: the result has no `referenceLines` key, and its other keys are as before.
- [x] 5.2 Update the tool texts of design D11:
  - a new `AnnotationsHelp` constant after `ContextHelp` in `create_widget` and `update_widget`, and `ChartTypeChangeHelp`; `DisplayHelp`'s pinned sentence stays as it is;
  - the `create_widget` template's `display` and the `query_indicator` template's `display: { labels }`;
  - the `Description` attributes of the new `WidgetDisplay` and `WidgetReferenceLine` members, and the description of `WidgetConfigInput.Display`.

  Update in the same step the sentences pinned by `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` and the template assertions of `The_tools_name_the_column_labels_they_take_and_return`. Verify:
  - `McpToolCatalogTests` passes: those tests, `The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` extended to the new fields (described, no enum) and to the words of the `display` description, and every tool documented;
  - a new sibling test checks the words of `AnnotationsHelp` for the three fields, the `overall` caution included, and that no tool's input schema has an `overall` property.
- [x] 5.3 Add MCP write tests to `McpDashboardToolsTests`: `Lines through MCP` (`get_dashboard` returns the lines), `Overall line over a measure through MCP` and `Chart type changed alone` (refused on `config.display.referenceLines`, widget unchanged). Verify the tests pass.
- [x] 5.4 Write the guidance:
  - `Mcp/Instructions.md`: the "Choosing a widget" bullet of D11 (the "Colors" bullet and "Words on a widget" stay as they are);
  - `docs/mcp.md`: the display keys (also in the `create_widget` row of the tools table), their refusal keys, `referenceLines` in widget data, and the chart type sentence.

  Update in the same step the `docs/mcp.md` sentences pinned by `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `…gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do`. Verify:
  - `McpGuideTests` passes, with a new case for the bullet: `referenceLines`, `overall` and `step` in backticks;
  - `IndicatorDocsTests` passes;
  - `AssistantPrompts.Version` is unchanged (D11).

## 6. Types, query flag and the saved config (ui)

- [x] 6.1 In `core/projects/indicators.service.ts`, add the types and constants of design D1, `QueryResult.overall?: number`, and an optional last argument `overall` to `query`, sent as `overall: true` only when true. Verify:
  - `npx ng build` type-checks;
  - `indicators.service.spec.ts`: a query without the argument posts the same body as today, and one with it adds only `overall: true`;
  - a case beside `ColumnMetadataTests.The_page_holds_the_same_widget_text_and_source_limits_as_the_api` reads `maxReferenceLines` and `maxReferenceLineLabel` from the page's copy and finds 4 and 40.
- [x] 6.2 In `widget-view.ts`:
  - `overallFor(type, config)`, twin of `WidgetQuery.Overall`;
  - the view passes it to `query`, and its key adds `overall: true` only when it is true;
  - `displayFor` keeps the three fields only where design D2 allows them, dropping lines of kinds the widget does not take;
  - `queryFor` and `savedConfig` do not change.

  Verify:
  - `query-for.spec.ts` has the cases of `WidgetQueryTests` (2.1) for `overallFor`, and `queryFor` of every existing case is unchanged;
  - `widget-view.spec.ts`: `Label edited` (switching a line from mean to median, or editing its label, sends no query), `Overall line added` (UI part: one query, with `overall: true`), and a widget without an overall line posts a body with no `overall` key.
- [x] 6.3 Run the production-shapes fixture's UI spec (`production-shapes.spec.ts`) with the shapes of 1.4, listed in the spec as `contextShapes` is. Verify:
  - every existing shape's `savedConfig`, `queryFor` and unchanged form save equal its goldens `saved`, `uiQuery` and `formSaved`, and `overallFor` is false for it;
  - the new shapes round-trip through `savedConfig` and the form.

## 7. Reference lines on the chart (ui)

- [x] 7.1 Write `indicators/reference-lines.ts` with `referenceLineValues(type, config, answer)` (design D3). Verify `reference-lines.spec.ts` runs every case of `testing/reference-lines.json` with the results the API test asserts.
- [x] 7.2 Draw the lines (design D5):
  - `plain()` and `seriesPlot` take `references` and widen the value axis to them;
  - draw the dashed lines with their dash patterns after the marks and before the hit areas, with a `<title>` on a chart with no series;
  - draw the labels at their ends, with the halo, the overlap rule and the plot-width rule, and grow the top margin;
  - use no SVG `transform` attribute and no new token.

  Verify `chart-widgets.spec.ts` and `series-widgets.spec.ts` (jsdom):
  - `Mean line on horizontal bars` (a vertical line at the mean's x);
  - `Axis widened`;
  - `Lines close together` (one label drawn);
  - `Text too wide for the plot`: no text at the line, never a cut one (the key's part is in 7.3);
  - `Overall line on grouped columns` (at `totals.all.value`);
  - `Half of every bar`;
  - `Widget saved before reference lines` (UI part): the same ticks, marks and plot height as before, and no key element.
- [x] 7.3 Add the `ReferenceKey` under the plot, the accessible description's sentence and the default labels (`indicators.referenceLines.*`). Verify specs:
  - `Own label`, `Count with a unit` and `Target on a percent indicator` read as the scenarios say, at the line and in the key, with the indicator's unit (design D3);
  - `Default label in Spanish` reads "Promedio: 34,17 % de los hogares" with the locale `es-CO`, and a spec pins the four words of each language in `en.json` and `es.json`;
  - `Text too wide for the plot` (key part): the key lists the line's whole text with its unit;
  - `Screen reader`: the `aria-label` ends with the lines;
  - `translations.spec.ts` passes.
- [x] 7.4 List the lines under "Show as table" (`ReferenceList`), under a `TableModel` and under a cross table. Verify `Column chart as a table`: the table's rows are the same as without lines, and the lines read with the indicator's unit.
- [x] 7.5 Write `### Reference lines, steps and data labels` in `docs/indicators-dashboard.md` for the reference lines, as design D13 says, with D12's caution and the Teatro example, and the two rows in "Limits", a sentence in "Show as table" and one in "Words around a widget" ("Room": the key takes room from the plot as the texts do). Verify an `IndicatorDocsTests` case finds the section, the four kinds and the caution sentence.

## 8. Step lines (ui)

- [x] 8.1 Add `stepPath` to `chart-scales.ts` and use it in `plain()` and `linePlot` when `display.interpolation` is `step` (design D6). Verify:
  - `chart-scales.spec.ts`: three points give `M… H… V… H… V…`, ending at the last point;
  - `Demand curve with steps`, `Steps per series` (runs keep their gaps) and `Straight segments by default` in `chart-widgets.spec.ts` and `series-widgets.spec.ts`.
- [x] 8.2 Add step lines to the docs section of 7.5: demand curves, and why the value holds until the next price. Verify the section names `step` and links from the line chart's row of the widget types table.

## 9. Data labels (ui)

- [x] 9.1 Draw data labels on bars and columns, with no series or grouped (design D7): placement, the room kept past the longest bar, and the rules for hiding a label. Verify `chart-widgets.spec.ts` and `series-widgets.spec.ts`:
  - `Value at the end of a bar` ("24.22% de los hogares" at the end of "Centro cultural o casa de la cultura");
  - `Columns too narrow`;
  - `Off by default`: no label elements, and the same margins as before;
  - the labels are `aria-hidden`.
- [x] 9.2 Draw data labels on lines, with or without a series and with steps, with the overlap rule. Verify a spec in which two series' points at the same value draw one label.
- [x] 9.3 Draw data labels on pie slices. Verify `series-widgets.spec.ts` (where the pie's specs live): `Thin slice`, and a single slice labelled at the center.
- [x] 9.4 Add data labels to the docs section of 7.5: the kinds, where labels sit, when they are hidden, and why horizontal bars suit them better than grouped columns. Verify the section names the four kinds and the stacked-layout refusal.

## 10. The widget form (ui)

- [x] 10.1 Add the reference lines fieldset to `widget-dialog.ts` (design D8):
  - rows with kind, value and label, add and remove, at most 4;
  - the kinds offered, and the help for each, with the `overall` caution;
  - the value in the indicator's units, converted by moving the decimal point, a saved number kept until it is edited;
  - `fieldFor` for `display.referenceLines[i].*`, in its block of `display.` checks, ahead of `seriesby`, `layout` and `title`;
  - the label through `singleLine`, as the texts.

  Add the strings. Verify `widget-dialog.spec.ts`:
  - `Kinds offered with a series`, and the kinds offered for a ratio and a measure, with and without a series, and for each layout;
  - `Percent typed in the form`, and 33.3 typed on a percent indicator saves 0.333;
  - `Overall help`;
  - a server error on `display.referenceLines[1].label` shows on that row;
  - a fifth line cannot be added.
- [x] 10.2 Add the "Straight" and "Steps" radios for a line widget and the data labels checkbox, with `setInterpolation` and `setDataLabels` storing a default only when the widget had the field saved, and `displayDraft()` carrying the three fields. Verify `Form saved unchanged` (UI part): a widget saved without the fields, and one saved with `"linear"`, `false` and two lines, each send the config they were opened with.
- [x] 10.3 Drop what the widget no longer takes when its kind, series or layout changes, with the note under the fieldset. Verify:
  - `Column chart turned into a pie chart`: the saved pie keeps its data labels and has no lines;
  - a column chart that gains a series saves no mean line and shows the note;
  - switching back before saving brings the line back.
- [x] 10.4 Describe the form's fields in the docs section of 7.5, and say in "Moving a widget to another dashboard" and "Version history" that a moved widget, a duplicate and a restored version keep its lines, steps and labels. Verify the sentences match `Chart annotations in the widget form` and `Chart annotations kept with the widget`.

## 11. Images, print and build (ui)

- [x] 11.1 Add the `'dashed'` swatch with its `dash` to `export-parts.ts` and `widget-export/image-layout.ts`, and register the lines in `AxisChart`'s parts (design D9). Verify `export-parts.spec.ts` and `widget-export-flow.spec.ts`:
  - `Mean line in an image`: legend entry "Mean: 34.17% de los hogares" with a dashed swatch;
  - `Lines above the footnote`: a widget with a footnote draws the legend of the lines, then the footnote, then the footer;
  - `Data labels in an image`: the plot's labels are in the image, and the hidden ones are not;
  - `Shown as a table`: the lines in the notes;
  - a chart without lines registers the same parts as before.
- [x] 11.2 Add print cases to `dashboard-print.spec.ts`. Verify:
  - `Reference lines printed`: the lines and the key in paper mode;
  - `Data labels on paper`: labels laid out again at the paper size, none overlapping;
  - `Steps printed`.
- [x] 11.3 Run `npx ng build` and `npm run check:safety` in `tamiza-ui/`. Verify:
  - the initial bundle stays under its budget and the new code is in the lazy dashboard chunk, with the sizes recorded in design.md;
  - the safety script (no `innerHTML`, number pipes or stray `.toFixed(`) is clean.
- [x] 11.4 Add to `docs/indicators-dashboard.md` "Images" and "Workbook" (under "Exporting a widget") and "Printing a dashboard" a sentence each on lines and labels. In `docs/operations.md` "What leaves the deployment by email", add the labels of reference lines. In `docs/tamiza-brief.md`, add the three display fields to section 6 and mark `add-chart-annotations` done in section 8's program list. Verify the links resolve and the brief names this change in both sections.

## 12. Integration checks

- [x] 12.1 Run the whole API suite in the background, unit then integration, with the output saved to a file (about 25 to 35 minutes). Verify:
  - everything passes, apart from the failures known on clean HEAD (`IndicatorSeriesQueryTests.A_ratio_*`, from the machine's decimal comma);
  - `ProductionShapesTests` passes with the goldens of its existing shapes untouched;
  - the counts are recorded in design.md.
- [x] 12.2 Run the UI suite (`TZ=UTC npm test -- --watch=false`, as `add-widget-context` ran it). Verify:
  - everything passes, apart from the known failures on clean HEAD (the two "stops polling when the page is destroyed" tests and the time-zone title test of `project-indicators.spec.ts`);
  - `production-shapes.spec.ts` passes;
  - the counts are recorded in design.md.
- [x] 12.3 Confirm that the change touches nothing under `tamiza-analytics/` (`git status -- tamiza-analytics`), so its suite does not need to run. Verify the command lists no change, and say so in design.md.
- [x] 12.4 Check the real stack in a separate Compose project (`-p tamizaverify`, its own env file and port, fake OIDC and Kobo, as in earlier changes). Seed long-format tables with invented values and the Duitama shapes (`comuna`, `categoria`, `precio`). Check:
  - a column chart with mean, median and overall lines next to a stat tile with the same filters, which must show the same number;
  - a bar chart with data labels, and a 100 % bar with a value line of 0.5;
  - a stepped line with a series, and a pie with data labels;
  - a widget saved before the change, opened and saved unchanged, makes no version and posts the same query body;
  - a column chart in a 6 × 4 tile with a two-line subtitle, a two-line footnote and four lines: the plot area and the rows of key against design D5's measures (135 px with one row of key, 115 px with two, where the chart is cut at its box), and the same in a 4 × 3 tile, where the chart is cut at its box and the key stays whole, on screen and on paper;
  - each at 1,280 and 390 px, in the light and dark themes, with no horizontal page scroll, no label outside its box and the key readable;
  - "Show as table", PNG and SVG images, printing to PDF on A4 in both orientations;
  - the form: kinds offered, the overall help, steps, labels, errors, and no query when a label changes.

  Verify findings and fixes are recorded in design.md.
- [x] 12.5 Send the dashboard's scheduled email to Mailpit, in English and in Spanish. Verify:
  - the lines are under their tables, with the Spanish default labels;
  - the workbook's "Summary" lists them;
  - the widget workbook exported from the page lists them too.
- [x] 12.6 Run `openspec validate add-chart-annotations --strict`. Verify it passes.

## Workflow follow-up

- Sync the delta specs and archive the change once the user has reviewed the verification results.
- Then rebase the later P1 changes of the program with `/opsx:update`: `add-declared-category-order`, `add-category-highlight`, `add-small-sample-warnings`, `improve-stat-cards` and `add-locale-formatting`.
  - Each sees this change's requirements and tool texts.
  - `add-declared-category-order`, which also modifies "Line chart", copies this change's sentence on steps and its scenario `Straight segments by default` into its block.
  - `add-small-sample-warnings` carries design D12's forward rule (its D16 and "Answer columns without an overall line") and makes its `missingFooter` exclusion reach the overall statement (D4).
  - `improve-stat-cards` makes its per-answer base rule for declared `single` columns reach the overall statement (D4; its D4a and task 3.4 already do), and carries its `unfiltered` flag on `IndicatorQueryRequest` like this change's `overall` (decided with the user on 2026-11-04; design D4).
  - `add-locale-formatting` takes the formatted values of the lines and data labels into its regional formats.
- P2 `add-reference-lines-from-indicators` builds on `WidgetReferenceLine` when its design is written, and P2 `add-normalization-and-totals` reads the response's `overall` for its Total bar.
