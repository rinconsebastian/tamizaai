# Tasks

**Order.**
- Groups 1 to 5 (api) come first, in order; each ends with its tests green. Group 4 can run any time after group 2.
- Groups 6 to 8 (ui) follow, in order. Group 6 keeps the UI build and suite green from the first type change.
- Group 9 is last.

This change is implemented after `add-indicator-column-metadata` and `add-widget-context`, both implemented and archived (2026-11-04), and, in the program's order, after `add-chart-annotations` and `add-declared-category-order`. `WidgetDisplay` already ends with `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription` (this change appends after them, and after what `add-chart-annotations` appends), the tool texts name them, the widget workbook request already sends `savedConfig`, and the production-shapes fixture (20 shapes) and its two runners exist.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks; "(API part)" and "(UI part)" split a scenario between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~DashboardColorsTests"`. The whole suite runs in the background, with its output saved to a file.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`.
- Tests that fail on a clean HEAD in this environment are not regressions: the two UI "stops polling when the page is destroyed" tests, the Spanish-title test of `project-indicators.spec.ts` outside UTC, and `IndicatorSeriesQueryTests.A_ratio_*` under a decimal-comma culture.

## 1. The neutral color kept by value (api)

- [ ] 1.1 In `Indicators/Palettes.cs` (design D1):
  - add `Palettes.Neutral = 0` and `Palettes.IsKeptColor(int?)` (0 or 1 to 8), and keep `IsColor` at 1 to 8;
  - give `ValueColorInput.Color` `[Range(0, Palettes.ColorCount)]` and the description of design D9;
  - update the doc comment of `ValueColor`.

  Verify: a new `PalettesTests` theory `A_kept_color_is_0_or_a_number_from_1_to_8` passes, and `A_color_is_a_number_from_1_to_8` still says 0 is no widget color.
- [ ] 1.2 Change `DashboardValidation.Colors` as design D3: the neutral is never added to a column's used colors, and the messages for a color out of range and a palette color used twice name the limits. In the same task, update the messages pinned by `DashboardValidationTests` (`A_color_outside_1_to_8_is_refused_on_the_entry` and `A_color_used_twice_in_a_column_is_refused_on_the_second_entry`), `DashboardColorsTests.A_color_used_twice_in_a_column_is_refused_on_the_second_entry_and_nothing_is_saved` and `McpDashboardToolsTests.Colors_refused_carry_the_keys_and_messages_of_the_page_and_save_nothing`. The same words for a widget's `display.color` (`IndicatorValidationTests`, `DashboardColorsTests`, `McpDashboardToolsTests`) are another rule and stay.

  Verify: `DashboardValidationTests` has one case per row of the D3 table, each naming its key, plus:
  - `Neutral for several values`, `Value kept twice with the neutral` and `Sixty-five entries` (API parts);
  - a column with colors 1 to 8 and three neutral values accepted, and a ninth palette color refused with the new message;
  - `The_page_holds_the_limits_the_api_validates_8_colors_64_kept_by_value_and_500_characters` gains two neutral entries in one column (accepted) and a color of 9 and of -1 (refused).
- [ ] 1.3 Add integration cases to `DashboardColorsTests` through `PUT …/colors` and `GET …/dashboards/{id}`:
  - the neutral kept for `4. No sabe` and `3. No o no sabe` of `respuesta` is saved, read back with the color 0, and makes one `colorsChanged` version;
  - saving the same list again writes nothing (`updated_at` unchanged);
  - `Dashboard saved before the neutral color` (API part): a dashboard whose list holds only colors 1 to 8 reads and serializes byte for byte as stored.

  Verify: the tests pass.
- [ ] 1.4 Add `DashboardVersionTests` and duplicate cases: a version holds a neutral entry, restoring it brings it back, and `duplicate` copies it. Verify: the tests pass.

## 2. Highlighted values (api)

- [ ] 2.1 In `Indicators/IndicatorModels.cs` (design D4):
  - add `WidgetDisplay.Highlight` (`IReadOnlyList<string?>?`, `WhenWritingNull`, declared last, after `ShowDescription` and after what `add-chart-annotations` appends) with its `Description`;
  - make `Normalize()` read an empty list as none, keep null items and the values as given, and return null only when every field of the display, the highlight included, is null;
  - add `WidgetDisplay.MaxHighlight = 20`, beside the text limits, and `ChartTypes.TakesHighlight(chartType, groupBy, seriesBy)` (a box plot needs a group column, a line a series);
  - give `WidgetConfigInput.Display` the description of design D9.

  Verify, in `WidgetDisplayTests`:
  - `MinimalBefore`, `RichBefore` and `The_texts_of_a_widget_round_trip_unchanged_and_in_the_order_of_the_record` still pass, so the earlier members keep their place;
  - a display with a color, labels, texts and a highlight writes the highlight last, and `The_limits_are_the_ones_the_design_gives` gains 20;
  - `{"display":{"highlight":["Centro cultural o casa de la cultura"]}}` and `{"display":{"color":1,"highlight":["COMUNA 7","SIN NOMBRE"]}}` round-trip unchanged, order and spaces kept;
  - `{"display":{"highlight":[]}}` reads as no display.
- [ ] 2.2 Add the highlight rules of design D4 as a `CheckHighlight` that `IndicatorValidation.CheckDisplay` calls after `CheckLabels` and `CheckContext`. Verify `IndicatorValidationTests`:
  - one case per row of the D4 table, with its key and message (`Twenty-one values`, `Value highlighted twice`, `Highlight on a heat map`, `Line chart with no series`, `Box plot with no group column`);
  - accepted on a bar and a column chart with and without a series, a pie, a box plot with a group column and a line with a series;
  - per-kind rules skipped when the chart type is null;
  - `Empty highlight`.
- [ ] 2.3 Check the query twins leave the highlight out. Add a `WidgetQueryTests` case and its shared case in `query-for.spec.ts`: a config with `display.highlight` gives the same `WidgetQuery.For` as the same config without it. Verify: both pass with no code change to the twins.
- [ ] 2.4 Add integration cases (`DashboardWidgetTests`, `DashboardVersionTests`, duplicate and move tests):
  - a widget created with a highlight is read back with it through `GET`;
  - `Highlight changed` (one `widgetChanged` version), `Highlight restored`, `Highlight duplicated` and `Highlight moved`;
  - `Widget form saved unchanged` (API part): a `PATCH` with the stored config of a widget saved before highlights writes nothing and makes no version;
  - the 400s of task 2.2 on create and on update.

  Verify: the tests pass.
- [ ] 2.5 Add an OpenAPI test after `IndicatorOverviewTests.The_openapi_document_describes_the_texts_of_a_widget_and_its_caption`: `WidgetDisplay` has `highlight`, an array of strings whose description names 20 and the neutral, and `ValueColorInput.color` has `minimum` 0. Verify: the test passes.
- [ ] 2.6 Extend the production-shapes fixture (`indicators/testing/production-shapes.json`, API part) with the four shapes and the list of colors of design D12, with the fixture's invented names and values, never production data. The runners pin the number of shapes and the list of their chart types (20 after `add-widget-context`: `ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` and the spec's `toHaveLength(20)` and `new Set(…)` assertions). `add-chart-annotations` raises the count to 25 and `add-declared-category-order` adds its own shapes before this change starts, so these come after those: raise each pinned count by the number of shapes added here from what it is when this task starts (29 after `add-chart-annotations` alone, one fewer if the 100 % bar of `add-declared-category-order` is reused, more if the other P1 changes add shapes first) and record the total in design.md. Write the goldens of the new shapes from the code of this task, as `add-chart-annotations` writes its own.

  Verify: `ProductionShapesTests` passes, every existing shape keeps its goldens untouched, and for every shape and list:
  - the normalized config is byte-identical to the stored text;
  - `WidgetQuery.For` gives the same query;
  - the colors lists (the `dashboard` entries and `eightColors`) pass `DashboardValidation.Colors` and serialize byte for byte.

## 3. Scheduled emails and the widget workbook (api)

- [ ] 3.1 Add `ReportDataTests` cases (design D10):
  - a bar chart that highlights a value, on a dashboard that keeps the neutral for a value of its group column, builds the same table (columns, rows, order, formats) as without either;
  - the widget workbook (`BuildWidgetExportAsync`, in `WidgetWorkbookTests`) of that config builds the same sheets and the same "Summary" lines (source, subtitle, footnote and notes included), both with the config's display sent (as the page's request does since `add-widget-context`: `savedConfig(widget.chartType, widget.config)`) and without it;
  - a `WidgetExportTests` request for a bar chart whose config holds a highlight passes the endpoint's validation of the saved config and gives the file it gives without it.

  Verify: `Highlighted bar chart sent` and `Highlight in a workbook` (API parts) pass, with no change to `Reports/`.

## 4. MCP (api)

- [ ] 4.1 Update the tool texts of design D9 in `Mcp/McpDashboardTools.cs`: `ColorsHelp` and the `valueColors` argument of `update_dashboard`, a new `HighlightHelp` constant said after `ContextHelp` (and `AnnotationsHelp`) in `create_widget` and `update_widget` (`DisplayHelp` unchanged), `ChartTypeChangeHelp`, `display` in the `create_widget` template, and the `config` argument descriptions of `create_widget` and `update_widget`; and the description of `WidgetConfigInput.Display`. In the same task, update `McpToolCatalogTests`:
  - `Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` (the chart type warning);
  - the sentence "config.display = { color, ramp, orientation, labels } sets how the widget is drawn", pinned there and in `The_widget_tools_name_the_texts_of_a_widget_and_the_dashboard_tools_name_its_source`, stays and still passes;
  - the `create_widget` template assertion of `The_tools_name_the_column_labels_they_take_and_return` lists `highlight` after the keys the earlier changes add;
  - the local `AssertColor` of `The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` expects `minimum` 0 for `valueColors[].color` and 1 for `display.color`, and the words of the `display` description it pins stay;
  - a new sibling test checks the words of `HighlightHelp` (20 values, the neutral grey, `groupBy` and `seriesBy`) in `create_widget` and `update_widget`, and `Where kept colors apply, in the descriptions` in `update_dashboard`.

  Also update `IndicatorDocsTests.The_mcp_documentation_gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do` (the `update_widget` description). Verify: `McpToolCatalogTests` passes (no enums, every tool documented, the color schemas described, the new sibling test).
- [ ] 4.2 Write the "Colors" changes of design D9 in `Mcp/Instructions.md`. `McpGuideTests.The_instructions_say_when_to_keep_the_palette_keep_colors_by_value_and_give_a_widget_its_own_color` gains asserts for `` `highlight` ``, "neutral grey" and the bars of a `bar` or `column` with no `seriesBy`; its pinned phrases stay ("for categories that recur across widgets, such as sex or yes/no", "Keep color 1 for a widget unless it should stand out", "send the `display`, labels included, again when you change the query"). `Assistant/Prompts/system.md` is not touched, so `AssistantPrompts.Version` stays. Verify: `McpGuideTests` passes.
- [ ] 4.3 Update the **Colors** paragraph and its bullets in `docs/mcp.md`: `color` 0, `display.highlight` and its column, the keys `config.display.highlight` and `config.display.highlight[i]`, and the chart type sentence; and the `widgetChanged` row of "The kinds of change", whose display list gains highlighted values. In the same task, update `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and add `display.highlight` to the settings that `The_color_settings_are_explained_in_the_mcp_documentation_with_every_palette_and_scale_a_client_can_name` lists. Verify: `IndicatorDocsTests` passes.
- [ ] 4.4 Add `McpDashboardToolsTests` cases:
  - `Highlight through MCP` and `Neutral through MCP`, read back with `get_dashboard`;
  - `Highlight on a heat map through MCP`: refused with `mcp.validation` on `config.display.highlight`, nothing saved;
  - `update_dashboard` with only a palette keeps the neutral entries;
  - `update_widget` that changes a highlighted column chart into a heat map with no new config is refused on `config.display.highlight`, and so is one that removes the series of a highlighted line;
  - `get_widget_data` of a highlighted widget returns the same rows as without.

  Verify: the tests pass.

## 5. API check

- [ ] 5.1 Run the API unit tests and the integration classes touched by groups 1 to 4 (`Dashboard*`, `Indicator*`, `Mcp*`, `Reports*`). Verify: they pass, apart from the known failures above.

## 6. UI: types and colors

- [ ] 6.1 In `core/projects/indicators.service.ts`, add:
  - `WidgetDisplay.highlight`;
  - `takesHighlight(type, config)` (twin of `ChartTypes.TakesHighlight`), `maxHighlightValues = 20` and `neutralPlace = 0`;
  - the 0 in the doc comment of `ValueColor`.

  Extend `PalettesTests.The_page_holds_the_limits_the_api_validates_8_colors_64_kept_by_value_and_500_characters` so the page's `neutralPlace` and `maxHighlightValues` must equal `Palettes.Neutral` and `WidgetDisplay.MaxHighlight`. Verify: `npx ng build` type-checks, `indicators.service.spec.ts` and `PalettesTests` pass.
- [ ] 6.2 Change `indicators/chart-colors.ts` as design D5: `neutralColor`, kept entries of 0, `forValues` with the highlight, `kept`, `mark`, `highlighted` and `highlight`. Verify `chart-colors.spec.ts`:
  - one case per row of the D5 table;
  - `Neutral sets nothing aside`, `Neutral answer`, `Kept color outside a highlight`, `Highlighted value filtered out` and `Missing group left out`;
  - a pyramid side whose value is kept as the neutral is drawn in the neutral, the other side keeping its color;
  - a highlight on a line with no series, on a box plot with no group column or on a heat map is ignored;
  - every existing case passes unchanged (`Widget saved before highlights`).
- [ ] 6.3 Make the charts read it (design D5, D6):
  - `axis-chart.ts` and `boxplot-chart.ts` call `mark(groupBy, group)`;
  - `seriesPlot` takes `highlighted` and paints the highlighted lines last;
  - `Label.strong`, `LegendEntry.strong`, and bold names in `series-legend.ts` and the pie's legend;
  - the accessible names end with `indicators.chart.highlighted`: "Highlighted: {{ values }}." in `en.json` and "Resaltados: {{ values }}." in `es.json`, after the sentence of the reference lines when `add-chart-annotations` is implemented.

  Verify, in `chart-widgets.spec.ts`, `series-widgets.spec.ts` and `boxplot-chart.spec.ts`:
  - `One place highlighted`, `Kept color of a highlighted value`, `Highlighted series` (the highlighted path is the last one, and the keyboard order of the marks is unchanged), `Kept color outside a highlight`;
  - `Bold label`, `Bold legend entry`, `Read to a screen reader`, and no `font-weight` on any label of a widget with no highlight;
  - tooltips carry the neutral for the values left out;
  - `translations.spec.ts` passes.
- [ ] 6.4 Add `ExportLegendEntry.strong`, set it in the `parts()` that `axis-chart.ts` and `pie-widget.ts` register, and measure and write it at weight 600 in `widget-export/image-layout.ts` (design D6). Verify:
  - `export-parts.spec.ts` and `image-layout.spec.ts`: `Highlight in an image` (UI part), with the neutral swatches resolved to `#898781` and the highlighted name in bold;
  - `svg-clean.spec.ts` keeps `font-weight="600"` on a bold axis label.
- [ ] 6.5 Run the production-shapes fixture's UI spec (`production-shapes.spec.ts`) with the shapes of task 2.6, listed in the spec as `contextShapes` is, and raise its pinned count (`toHaveLength` and the `new Set(…)` assertions) by the same number, from what it is when this task starts. Verify: its spec checks the same `queryFor` and `savedConfig` for each shape, and that `chartColors` gives each shape the colors pinned before this change (`kept(group) ?? accent` per group and `forValues` per domain, which `mark(group)` must match).

## 7. UI: forms

- [ ] 7.1 In `widget-view.ts`, make `displayFor` keep `highlight` for the kinds of `takesHighlight` (design D4). Verify `widget-view.spec.ts`:
  - changing the highlight sends no query;
  - `savedConfig` keeps it for a bar, a column, a pie, a box plot with a group column and a line with a series, and drops it for a line with no series, a box plot with no group column and a heat map.
- [ ] 7.2 Add **Highlight** to `widget-dialog.ts` (design D7): the rows, **Add a value** up to 20, **Remove**, the values offered, one list per column, `displayDraft`, `fieldFor`, and the strings in `en.json` and `es.json`. Verify `widget-dialog.spec.ts`:
  - `Highlighted value picked from the data`, `Highlight previewed`, `Series added`, `Column changed back`, `Kind without a highlight`, `Refusal shown on the field`;
  - `Widget form saved unchanged` (UI part) for a widget with and without a highlight;
  - **Add a value** is off at 20 with its reason, an empty row is refused, and each row is named "Highlighted value n of m";
  - the `expected` table of fields per kind (in "the fields of each chart type") holds the highlight.
- [ ] 7.3 Add the neutral choice to `dashboard-colors-dialog.ts` (design D8), with its strings and the new `kept.intro`, and make **Add** start an entry on a full column on the neutral (design D8, decided with the user on 2026-11-04). Verify `dashboard-colors-dialog.spec.ts`:
  - `Neutral offered`: nine radios, the last one "Neutral grey", reached with the arrow keys;
  - `Ninth value of a column`: **Add** after an entry of a column with colors 1 to 8 stays on that column and starts on 0, and saving it with a new value sends the entry with the color 0. The pinned case "adds an entry on another column when the one before already uses all eight colors, which the API would refuse" (`:433`), which expects the entry on `municipality` in color 2, is rewritten to this behavior and renamed after it, since the API no longer refuses it; the cases at `:401` (first free color of the column before), `:413` (a column no table offers) and `:446` (the first entry in color 1) pass unchanged;
  - the pinned cases that count eight choices per entry gain the ninth: "is one of the palette's eight swatches … (Named choices)" (`:549`) lists the eight colors and then "Neutral grey", with its visible word in place of a number, and "reaches every field with Tab …" (`:710`) expects radio groups of 4 and 9 and nine `dashboard-colors-color` stops;
  - `Neutral for several values` (UI part): two neutral entries on `respuesta` are sent;
  - an entry saved with 0 opens checked on the neutral.
- [ ] 7.4 Document the feature (design D13) in `docs/indicators-dashboard.md` (the "Highlighted values" section, "Images" and "Printing a dashboard" included) and `docs/tamiza-brief.md` (sections 6 and 8). Verify: `IndicatorDocsTests` pass (the duplication sentence of `The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them` unchanged), the links resolve, and every behavior in the deltas of `dashboard-colors`, `dashboard-printing`, `widget-export` and `dashboard-reports` is described.

## 8. UI: printing and build

- [ ] 8.1 Add `dashboard-print.spec.ts` cases: `Highlight printed` and `Neutral kept printed`, with the marks and the bold label kept while the print is prepared. Verify: the print specs pass.
- [ ] 8.2 Run `npx ng build` and `npm run check:safety`. Verify: the initial bundle stays under the 1 MB error budget and, as far as the earlier changes leave room, under the 675 kB warning (it was 674.64 kB after `add-widget-context`), with no new token in `styles.css`, the new code in the lazy dashboard chunks apart from the constants of `indicators.service.ts`, the safety script is clean, and the measured size is recorded in design.md.

## 9. Integration checks

- [ ] 9.1 Run the whole API suite (unit, then integration) in the background, about 25 to 35 minutes, with the output saved to a file. Verify: everything passes apart from the known failures, and the counts are recorded in design.md. The analytics suite is not run: this change touches no analytics code.
- [ ] 9.2 Run the whole UI suite (`TZ=UTC npm test -- --watch=false`, as `add-widget-context` ran it). Verify: everything passes apart from the known failures, and the counts are recorded in design.md.
- [ ] 9.3 Run the production-shapes fixture of both sides once more on the final code. Verify: every shape keeps its stored text, its query and its colors.
- [ ] 9.4 Check the real stack in a separate Compose project (the `tamizaverify` recipe; never the user's own stack or Keycloak). Seed a Teatro-like dashboard with invented values: *Earth*, numbered `respuesta` values, a long-format `categoria`, the bar of design D12, a pie, a box plot by `vehiculo` and a line with the series `comuna`. Check:
  - a highlight on each of the four kinds, and the neutral kept for `4. No sabe` in a 100 % bar chart;
  - at 1,280 and 390 px, with no horizontal page scroll and the longest bold labels inside their column, and in a 6 × 4 tile with a subtitle and a footnote, where a bold legend that wraps a row earlier still leaves the plot above its least height (`add-widget-context` D5 measured the plot area at 233 px without texts and 199 px with them);
  - the widget form (offered values, 20 limit, column changed back, errors) and the Colors form (the neutral choice, **Add** on a full column), from the keyboard;
  - printing to PDF on A4 with background graphics off;
  - a PNG and an SVG image;
  - the scheduled email in Mailpit, whose tables are unchanged.

  Verify: findings and fixes are recorded in design.md.
- [ ] 9.5 Run `openspec validate add-category-highlight --strict`. Verify: it passes.

## Workflow follow-up

- Write "Deviations from the plan" and "Verification Results" in design.md, then sync the delta specs (`dashboard-colors`, `dashboard-printing`, `widget-export`, `dashboard-reports`, `mcp-server`) and archive the change with `/opsx:archive` once the user has reviewed the results.
- Rebase the later P1 changes of the program with `/opsx:update`: `add-small-sample-warnings` (it moves single-series tooltips to `markTooltips` and dims marks, over the colors and paint order this change sets), `improve-stat-cards` and `add-locale-formatting` (they extend the same `ChartTypeChangeHelp`, `create_widget` template and `display` description; this change keeps its words in `HighlightHelp`). Refresh the proposal of P2 `add-color-by-and-custom-palettes`, which builds on the neutral color (number 0), `display.highlight` and "Colors kept by value" as this change leaves them.
