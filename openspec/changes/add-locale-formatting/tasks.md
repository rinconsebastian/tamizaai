# Tasks

**Order.** Group 1 comes first, on the code as it is before this change: it writes the goldens that prove the rest moved nothing. Groups 2 to 7 (api) follow in order. Group 8 (ui types, formatter and locale) can start after group 3; groups 9 and 10 follow group 8. Group 11 is last. `add-indicator-column-metadata` and `add-widget-context` were archived on 2026-11-04, and `add-chart-annotations`, `add-declared-category-order`, `add-category-highlight`, `add-small-sample-warnings` and `improve-stat-cards` are implemented and archived before this change starts. This change has been rebased with `/opsx:update` on `main` as it is after the first two: "Indicator formatting" is main's text, the widget's own unit included, with this change's clauses; the other six MODIFIED blocks differ from main only by this change's edits. Code is cited by function and constant, not by line, since the lines move. When the other five are archived, run `/opsx:update` once more (Workflow follow-up).

Every UI string goes into both `tamiza-ui/public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks.

**How to run the tests** (see the memory note on the development environment): the API tests with `dotnet test` in `tamiza-api/` (integration tests with Testcontainers; filter by class while working, for example `--filter "FullyQualifiedName~NumberLocalesTests"`); the UI tests with `npm test -- --watch=false` in `tamiza-ui/`, or one spec with `npx ng test --watch=false --include='src/app/<path>.spec.ts'`, with `TZ=UTC`. The machine's culture uses a decimal comma: every new API test that writes numbers sets its culture explicitly.

## 1. Production-shapes fixture (before any other change)

- [ ] 1.1 Add a `formats` section to `tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json` (design D12): invented indicators with the study's shapes, each with `format`, `unit` and values, and no study name or label:
  - a percent with a word unit (`de las familias`) and a value with two decimals;
  - a percent whose value has a trailing zero at two decimals (0.157);
  - a percent whose unit is `%` (not repeated after the number), and the first percent given a widget's own unit through `withWidgetUnit` / `MetaOf` (`de quienes asistieron`);
  - a count of `pesos` with a fractional mean, and a whole-number box median of that count;
  - a decimal with no unit;
  - a count with no unit over 1,000,000.

  For each value, write the golden texts from the code at the change's starting commit: the UI's `valueFormatter` and `boxIndicator` in `en-US` and `es-CO`, `ReportFormat` with the `en-US` and `es-CO` cultures (`Value` and `Plain`, with `ReportDataBuilder.ExcelFormat` and `BoxFormat`), and MCP's formatter (`en-US`). Extend `ProductionShapesTests.cs` and `production-shapes.spec.ts` (from `add-indicator-column-metadata`) to check them, with a one-off run that prints the goldens and no generator kept in the tests.

  Verify: both tests pass on the starting commit, changing one character of a golden makes each fail, and a search for `teatro_`, `comercio_`, `Duitama` and `de los hogares` in the file finds nothing.

## 2. Database (api)

- [ ] 2.1 Write `tamiza-api/src/dbUp/Scripts/0024_add_locale_formatting.sql` (design D1, D5), after the pattern of `0016_add_mcp_server.sql`. `0021` and `0022` exist, and `0023_add_small_sample_warnings.sql` comes from `add-small-sample-warnings`, implemented first; check `tamiza-api/src/dbUp/Scripts` when the task starts and take the next free number if that is no longer so:
  - `projects.number_locale character varying(16) NULL` with `ck_projects_number_locale` on the seven formats in `NumberLocales.All` order;
  - `script_versions.decimals integer NULL` with `ck_script_versions_decimals` (`decimals IS NULL OR decimals BETWEEN 0 AND 4`);
  - no default, no index, no row rewritten.

  Add `Project.NumberLocale` and `ScriptVersion.Decimals`, mirror the columns and both CHECKs in `Data/TamizaDbContext.cs` (the `Project` and `ScriptVersion` mappings), and add a `ScriptConventionTests` case that pins the two `ADD COLUMN`s, the two `CHECK`s with the literal list and no `UPDATE`.

  Verify: `ScriptConventionTests` and `SchemaDriftTests` pass.
- [ ] 2.2 Add a `SchemaMigratorTests` upgrade case: apply the scripts through 0023, seed a project and an indicator version, migrate, and check that both rows read `number_locale` and `decimals` as null, that `es-CO` and 4 insert, and that `fr-FR` fails `ck_projects_number_locale` and 5 fails `ck_script_versions_decimals` (`Existing project upgraded`, `Indicator saved before decimals`, database parts).

  Verify: the test passes.
- [ ] 2.3 In `docs/tamiza-brief.md` section 6 (data model), add `projects.number_locale` to the `projects` entry and `script_versions.decimals` to the `script_versions` entry (next to the format and the columns), and `display.decimals` and `display.compact` among the settings of a widget's config; the sentence that says what an indicator has ("An indicator has a name…") gains its decimals.

  Verify: the four names are there with their limits, as the migration and design D7 have them.

## 3. Number formats in the API

- [ ] 3.1 Write `tamiza-ui/src/app/pages/projects/indicators/testing/number-formats.json` (design D3): cases `{ locale, format, unit, decimals, box, value, text }`, each in a project format, the texts written as Node 26's `Intl` writes them, no-break spaces included, with the no-break space of design D2 before `%` in the four Spanish formats (decided with the user on 2026-11-04). Cover each of the seven formats with a count, a decimal, a percent with no decimals and with 1, a negative percent, a percent with a word unit, a box plot over a count with and without decimals, the six currencies of the table and a code outside it that browsers write as its code (`PEN`; not `CAD`, which `en-US` writes `CA$`), a symbol unit (`$`) and a word unit (`pesos`), 4- and 5-digit numbers, halves (0.4685 at 1 decimal, 1.005 at 2), a negative, and zero. Add UI-only cases with `compact: true` (D6): `1 M pesos`, `$1,2 M`, `1,3 M pesos`, `60,5 mil pesos`, 4.41 and a percent written in full. Add `numberLocales` (the seven, in order) and the `NumberLocale` type to `core/projects/projects.service.ts`.

  Verify: the file parses, holds every case above, and its texts include `46,9 %` (`es-CO`), `46.9 %` (`es-MX` and `es-419`), `46,9 %` with a single space (`es-ES`), `46.9%` (`en-US`), `46,9%` (`pt-BR`), `4500` (`es-ES`), `$ 60.514` (`es-CO`, `COP`, 0 decimals) and `$60.514` (`es-CO`, `$`).
- [ ] 3.2 Write `Reports/NumberLocales.cs` (design D3): `All`, `Find(name)` and one `NumberLocale` per format with its separators, minimum grouping digits, percent spacing (`SpacedPercent`, true for `es-CO`, `es-MX`, `es-ES` and `es-419`, design D2), currency pattern and the symbol table of D3. Write the digits without any culture: convert to `decimal` (falling back to invariant `double` formatting outside its range), round with `MidpointRounding.AwayFromZero`, then group and separate.

  Add `NumberLocalesTests.cs` (API unit): every case of `number-formats.json` without `compact` writes its `text`, reading the file from the repository as `SeriesTableTests` does; `All` equals the UI's `numberLocales`, read as `PalettesTests` reads the UI's copies; `SpacedPercent` is true exactly for the formats whose language is `es` (the rule of the UI's `numberStyleOf`); the cases pass with the current culture set to `es-CO` and to the invariant culture.

  Verify: the tests pass.
- [ ] 3.3 Change `Reports/ReportFormat.cs` (design D3, D5): an optional `NumberLocale` in the constructor; `Value(value, format, unit, decimals = null)`, `Plain(value, format, decimals = null)`, `Whole`, `Share`, `TooltipNumber` and `Figure` (from `add-small-sample-warnings`) write with the table when it is given and with today's code when it is not; `Day`, `Moment`, `DateText` and `TimestampText` always use the culture. Change `ReportDataBuilder.BoxFormat(format, decimals)` and `ExcelFormat(format, decimals)`. Update the class comment, which no longer accepts differences for the seven formats. `Value` keeps its `%` rule with a table too (a unit of `%` on a percent indicator is not repeated).

  Verify: `ReportFormatTests` passes unchanged; new cases: `Value` with decimals 0 to 4 for each format with and without a table, `46,9 %` for 0.4685 with 1 decimal with the `es-CO` table and `46,9%` with the `es-CO` culture and no table, `Share` and `TooltipNumber` of a percentage with the space in `es-MX` and without it in `en-GB`, a table's `Day` still written in the culture, `Value` of a percent with the unit `%` and 1 decimal gives `46,9 %` with the `es-CO` table and `46.9%` with the `en-US` culture, `BoxFormat("count", null)` is `decimal` and `BoxFormat("count", 0)` is `count`, and `ExcelFormat` gives `#,##0.0`, `0.0%`, `0%` and today's three formats; the production-shapes `formats` goldens of task 1.1 still pass.

## 4. Project regional format and indicator decimals (api)

- [ ] 4.1 Add `UpdateProjectRequest.NumberLocale` and `ProjectDetails.NumberLocale` (design D1). In `ProjectEndpoints.UpdateAsync`, `""` clears, a value from `NumberLocales.All` is stored, anything else is refused with key `numberLocale` and the message of D1; `ToDetails` gives it to every member.

  Verify: `ProjectsApiTests` cases `Admin sets the regional format` (a viewer reads it), `Unknown regional format`, `Regional format by a non-admin` (403), `Regional format cleared`, and a PATCH without the field leaves it unchanged.
- [ ] 4.2 Indicator decimals (design D5): `SaveIndicatorRequest.Decimals` (appended), its check in `ValidateMetadata`, `Metadata`, `AddVersionAsync`, `RestoreCoreAsync`, `ToDetails`, `ScriptVersionDetails.Decimals` (appended) and `IndicatorEntry.Decimals` (appended, `WhenWritingNull`) built in `IndicatorEndpoints`.

  Verify: `ScriptsApiTests` cases `Decimals saved`, `Decimals restored with a version`, `Decimals out of range` (no version added) and a removal storing none; `IndicatorOverviewTests` checks that the overview of indicators without decimals has no `decimals` key.
- [ ] 4.3 Describe the new fields in OpenAPI. Verify: a test after the pattern of `IndicatorOverviewTests.The_openapi_document_describes_the_sort_of_a_widget` finds `numberLocale` in the project PATCH body and details, and `decimals` in the indicator save body, the version details and `IndicatorEntry`.
- [ ] 4.4 In `docs/scripts.md`, add a **Decimals** row to the metadata table (after **Format**), with the format's defaults and the box plot rule, and say in the sentence under the table that restoring a version brings the decimals back with the label, unit, format and column names.

  Verify: `IndicatorDocsTests` pass, and the row matches `pipeline` "Indicator decimals".

## 5. Widget decimals and abbreviation (api)

- [ ] 5.1 Add `WidgetDisplay.Decimals` (`int?`, `Range(0, 4)`) and `WidgetDisplay.Compact` (`bool?`), both `WhenWritingNull` with a `Description` in lower case and no enum, appended after the members the earlier P1 changes add (today `ShowDescription` ends it; the other changes append theirs first) (design D7). `Normalize()` keeps `0` and `false` as given, and `WidgetDisplay.Normalize()` returns null only when every member is unset.

  Verify: `WidgetDisplayTests` still pins `MinimalBefore` and `RichBefore` byte for byte, `IndicatorQueryTests`' pinned response texts pass unchanged, and new cases round-trip `{"display":{"decimals":0}}`, `{"display":{"compact":false}}` and a display holding only one of them (`Widget saved before decimals`, API part); the OpenAPI document's `WidgetDisplay` schema lists `decimals` and `compact` with their descriptions (the test of task 4.3).
- [ ] 5.2 In `CheckDisplay`, the two rules of the D7 table (`compact`, `true` or `false`, refused on the kinds that do not take it, skipped when the chart type is null).

  Verify: `IndicatorValidationTests` cover `Decimals out of range` (5 and -1), `Abbreviation refused on a heat map` (with `true` and with `false`), `compact` accepted on each of the five kinds, and `decimals` accepted on each of the twelve kinds.
- [ ] 5.3 Add to the production-shapes fixture (API side) the shapes of design D12: a stat tile with `display.decimals: 1`, a box plot with `display.decimals: 0`, an abbreviated stat tile, and a column chart with data labels and `compact`, each checked for its own round trip. The runners pin the number of shapes (`ProductionShapesTests.The_fixture_holds_each_shape_the_design_lists_once` and the spec's `toHaveLength` and `new Set(…)` assertions) and the list of chart types: raise each pinned count by four from what it is when this task starts (24 on today's fixture of 20; 29 after `add-chart-annotations` alone and 31 with `improve-stat-cards` as well, more if the other P1 changes add shapes first) and record the total in design.md.

  Verify: `ProductionShapesTests` passes: every existing shape still normalizes to byte-identical stored JSON with the same `WidgetQuery.For` and plan, and `WidgetQuery.For` of the new shapes equals that of the same shapes without the two fields.
- [ ] 5.4 Check that the two fields travel with the widget, as the generic lists of `add-indicator-column-metadata` promise: a dashboard version holds and restores them, a duplicated dashboard copies them, a moved widget keeps them, and a widget without them makes no version when its form is saved unchanged. Saving an indicator with decimals, and setting the project's format, make no version.

  Verify: new cases in `DashboardVersionTests`, in `DashboardTests` (next to `A_duplicate_holds_a_copy_of_each_widget_and_comes_right_after_the_original`) and in `DashboardWidgetTests` (a widget moved to another dashboard): `Widget decimals restored`, `Regional format set` and `Indicator decimals saved` (API parts).

## 6. Scheduled emails and the widget workbook (api)

- [ ] 6.1 Read the project's `NumberLocale` with its name and time zone in `ReportDataBuilder.BuildAsync` and in `WidgetExportEndpoints.ExportAsync`, and build `ReportFormat` with `NumberLocales.Find` of it (design D10).

  Verify: `ReportDataTests` cases `Project format in an English email` (words and date in English, `46,85 % de los hogares` from an invented `teatro_hoy`-shaped indicator), `Percentage with a space for a Spanish project format` (`45,5 %` in the body, the plain text and the "Summary", where the same email of a project with no format writes `45,5%`), `Format set after the settings were saved`, the n line of `add-small-sample-warnings` and the "Without filters" line of `improve-stat-cards` written in `es-CO` in that email, and the four existing scenarios of "Email language and regional format" unchanged (`Percentage in Spanish of Colombia` still `45,5%` for a project with no format).
- [ ] 6.2 Decimals in the email: `IndicatorMeta.Decimals`, `MetaOf` (the widget's, else the indicator's, taken where it takes the widget's unit, also for an indicator that is gone), `Value`, `ValueColumn`, `BoxplotTable` and the histogram's ranges (design D5, D10); the `%` rule of `Value` and `ValueColumn` holds.

  Verify: `ReportDataTests` cases `Decimals in the workbook` (`0.0%`), `Box plot with its indicator's decimals in the workbook` (`#,##0` on the figure columns), `One decimal in the email`, `Widget without decimals`, `Removed indicator with decimals of its own` (a widget over a removed indicator with its own decimals and unit keeps both), and the existing `Median of a count in the workbook` still `#,##0.00`.
- [ ] 6.3 The stat tile is written in full in the body, the plain text and the workbook's "Summary" whatever `display.compact` says.

  Verify: `ReportHtmlTests`, its plain-text cases and `ReportWorkbookTests` cover `Abbreviated tile written in full`, and a `ReportSenderTests` send of an email with a project format, a tile with decimals and an abbreviated tile passes.
- [ ] 6.4 The widget workbook (design D10): the page's request already carries the saved config, display included (`exportWorkbook` sends `savedConfig(widget.chartType, widget.config)`, and `displayFor` keeps `decimals` on every kind), so no change to the request is planned; check it. Its data sheet and "Summary" take the decimals and the project's format, and the Summary's line of the number of rows, written with `text.Culture` and `N0` in `WidgetWorkbook`, is written by `format.Whole`.

  Verify: `WidgetWorkbookTests` and `WidgetExportTests`: `Widget decimals in its workbook`, `Abbreviated tile in its workbook`, `Workbook ranges in the project's format`, `Rows in the Summary` (`12.345` in a project in `es-CO`, the line's name in English), and the existing `Spanish member` unchanged; a `DataExportTests` case checks `Table export of a project with a regional format` (a CSV of a project in `es-CO` still writes decimals with a dot).
- [ ] 6.5 Document the emails and workbooks: in `docs/indicators-dashboard.md`, the "Workbook" bullets (numbers in the widget's decimals, the Summary's rows, "Words and formats follow your interface language and region") and the "Images" bullet on words and numbers; in `docs/operations.md`, "Email (optional)": the email's numbers follow the project's format with the space before `%` of a Spanish one, the saver's words and dates, decimals, tiles in full.

  Verify: `IndicatorDocsTests` pass, and the sentences match the `dashboard-reports` and `widget-export` deltas.

## 7. MCP (api)

- [ ] 7.1 Replace the three static `English` formatters (in `McpDashboardTools`, `SeriesPayload` and `BoxPayload`) with one `ReportFormat` per call from the project's `NumberLocale`, else `en-US` (design D11). `Row`, `SeriesPayload.From`, `BoxPayload.From`, the reference lines of `add-chart-annotations`, the `noData` of `add-small-sample-warnings` and the `unfiltered` of `improve-stat-cards` take it. `IndicatorInfo` gains `Decimals`; `get_widget_data` merges the widget's in `WidgetDataAsync`, where it merges the widget's unit (also for a removed indicator), and `query_indicator` keeps the indicator's; nothing is abbreviated.

  Verify: `McpDashboardToolsTests` cases `Widget data in the project's format` (`46,85 % de los hogares`, the space a U+00A0), `Project without a regional format` (the same payload text as before), `Widget decimals in widget data`, `Abbreviated tile in widget data`, a box plot of a count with 0 decimals, and `query_indicator` with a widget display of 1 decimal still using the indicator's digits.
- [ ] 7.2 Record the argument names of each call in `McpCallScope.Arguments` from `McpCallFilter.Create`, never written to the call record. Give `save_script` a `decimals` argument: left out, a keep flag that makes `AddVersionAsync` copy the decimals of the version it numbers after, inside its retry loop (design D11, as `add-indicator-column-metadata` keeps `columns`); `null`, none; a value, checked by `ValidateMetadata`. Add `decimals` to `McpScriptSummary` (`list_scripts`), `get_script` and the result of `save_script`, and `numberLocale` and each indicator's `decimals` to `get_project`.

  Verify: `McpScriptToolsTests` cases `Decimals saved through MCP`, `Decimals kept when left out`, `Decimals cleared through MCP`, `Decimals out of range through MCP`; `McpProjectToolsTests` `Format in the project` (absent for a project without one); a `McpCallRecordTests` case that a call's argument names are seen by the tool and neither they nor the values reach the record.
- [ ] 7.3 Tool texts (design D11): the `Description`s of `WidgetDisplay.Decimals` and `Compact` and of `save_script`'s `decimals`, the `save_script` description's keep sentence, a new `NumbersHelp` constant after `ContextHelp` (and the constants the earlier changes add after it) in `create_widget` and `update_widget`, with `DisplayHelp`'s pinned first sentence unchanged, `ChartTypeChangeHelp` (adds `config.display.compact`) and `create_widget`'s `display: { … }` template. Update, in the same step, the sentences pinned by `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale`, as the earlier P1 changes left them, and add a sibling test for the words of `NumbersHelp`.

  Verify: `McpToolCatalogTests` pass (that test, no enums, every tool documented), and MCP write tests: `Widget decimals through MCP` (read back with `get_dashboard`), `Abbreviation refused through MCP` and `Chart type changed alone through MCP` are refused with `mcp.validation` and the key, and nothing is saved.
- [ ] 7.4 Guidance: the Rules bullet of D11 in `Mcp/Instructions.md`; `docs/mcp.md` (`formatted` and `numberLocale`, `save_script`'s `decimals` and its keep rule, `display.decimals`, `display.compact`, and the chart-type sentence at the place the earlier changes left it), with the `docs/mcp.md` sentences pinned by `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `…gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do` updated with those tests. `Assistant/Prompts/system.md` is not changed, so `AssistantPrompts.Version` stays at 5.

  Verify: `McpGuideTests` and those `IndicatorDocsTests` pass, and `AssistantPrompts.Version` is 5.

## 8. UI: types, formatter and the number locale

- [ ] 8.1 Types: `ProjectDetails.numberLocale` and the update body's `numberLocale` (`'' | NumberLocale`) in `projects.service.ts`; `IndicatorMetadata.decimals` and `ScriptVersionDetails.decimals` in `pipeline.service.ts`; `IndicatorEntry.decimals?`, `WidgetDisplay.decimals?` and `compact?` (after `showDescription`, and the members the earlier changes add) in `indicators.service.ts`; `IndicatorUnit.decimals?` in `format.ts`.

  Verify: `npx ng build` type-checks, and `indicators.service.spec.ts` and `projects.service.spec.ts` pass.
- [ ] 8.2 In `indicators/format.ts` (design D3, D4, D5, D6): `valueFormatter(indicator, style)` takes a `NumberStyle` or a locale string (a browser locale, no space), writes its percent values and ticks with `percentFormat`, and writes exactly `decimals` when set, in every format and in `plain`; `abbreviated(value)`; `boxFormat(format, decimals)` and `boxIndicator` with the box rule; `withWidgetUnit` (which merges the widget's unit, `add-widget-context` D4) also merges `decimals`, the widget's before the indicator's, and keeps returning the indicator itself when the widget sets neither; the `%` rule of `valueFormatter` stays after the merge.

  Verify: `format.spec.ts` runs every case of `number-formats.json` with `numberStyleOf` of its `locale` (`compact` cases through `abbreviated`), and the production-shapes `formats` goldens, called with a locale string, still pass (`es-CO` `46,85%` with no space); cases for `Trailing zero kept`, `Widget before indicator`, `Histogram ranges with decimals`, `Widget's own unit with decimals`, `Unit of % not repeated` (indicator's unit and widget's own unit, with and without decimals), and a percent tick of 0.5 that reads `50 %` with the `es-CO` style.
- [ ] 8.3 The number locale (design D4): `core/i18n/number-locale.ts` with `NumberStyle`, `numberStyleOf`, `percentFormat` (design D3) and `NUMBER_LOCALE` (`Signal<NumberStyle>`, root default `LOCALE_ID` with no space), `ProjectContext.numberLocale`, the provider in `project-page.ts`, `WidgetDialogData.numberLocale` passed by `openDialog` in `project-indicators.ts`, and the provider in `WidgetDialog`.

  Verify: `number-locale.spec.ts`: `numberStyleOf` gives the space for `es-CO`, `es-MX`, `es-ES` and `es-419`, none for `en-US`, `en-GB` and `pt-BR`, and none for a browser locale with no project format, `es-CO` included; `percentFormat` adds a U+00A0 before `%` in `es-CO`, keeps `es-ES`'s single one, and adds none without the space. Specs also check that the token defaults to `LOCALE_ID` with no space, that a component under the project page reads the project's format and follows a change of the details, that a dashboard version shown in the page reads the project's current format (`Version viewed after the format changed`, UI part), and that the widget form's preview reads the format it was given.

## 9. UI: charts, the tile and the data labels

- [ ] 9.1 Read `NUMBER_LOCALE` instead of `LOCALE_ID` for numbers in `axis-chart.ts`, `pie-widget.ts` (its share formatter in place of the `pct` pipe and of the shared `percentText`), `likert-chart.ts`, `crosstab-widget.ts`, `boxplot-chart.ts`, `map-widget.ts` and `widget-view.ts`, in computeds, with every share written by `percentFormat` (`seriesMarks` in `axis-chart.ts`, `crossTableModel` in `crosstab-widget.ts`, `likert-chart.ts`, the four shares of `pie-widget.ts`), and in the numbers the earlier P1 changes added (design D4): `add-small-sample-warnings`' n texts (`figureFormatter` and `base-figures.ts` are given the number locale) and `improve-stat-cards`' "Without filters" line. `map-tooltip.ts` takes a number style for numbers and keeps the member's locale for dates and timestamps.

  Verify: chart specs with `NUMBER_LOCALE` from `numberStyleOf('es-CO', …)` and `LOCALE_ID` `en-US`: `Colombian format in an English browser`, `Colombian percentage` (`46,85 %`), `Percentage with a space for a Spanish project format`, `Share in a Mexican project format`, `One space in Spanish of Spain`, `Portuguese project format`, `Colombian browser without a project format` (the root default with `LOCALE_ID` `es-CO` writes `46,85%`), a map tooltip column formatted as a percentage with the space, `Spanish of Spain`, `Dates keep the member's format` (the tooltip's timestamp in English, its numbers in `es-CO`), `Project without a regional format`, `Project regional format in an English browser` (in one test bed, a widget's `12.345,50` and a run step's duration `8.2 s` written by `pipeline-runs.util.ts` `durationText` with `LOCALE_ID`), a pie's, a Likert widget's, a cross table's and a 100% bar's shares with the space, an n line over 1,000 and a tile's "Without filters" line in `es-CO`; `scripts/check-ui-safety.sh` is clean.
- [ ] 9.2 In `widget-view.ts`, build `formatter` and `boxFormatter` from `unitOf`, the result of the merged helper (`withWidgetUnit`) with the decimals, and keep `display` out of the query key.

  Verify: `widget-view.spec.ts` cases: changing `display.decimals` or `display.compact` redraws and sends no query (`Decimals previewed`, view part), `One decimal on a rate`, `Shares keep their digits`, and a box plot over a count with and without decimals.
- [ ] 9.3 The stat tile (design D6), on the markup `improve-stat-cards` leaves (size container, `title` with the whole value, secondary lines): with `display.compact`, the short text `aria-hidden`, a visually hidden span with the full text and the full text in `title`; `value` stays the text shown, so the export parts keep the short text, and a second computed holds the full text. The data labels of `add-chart-annotations` use `abbreviated` when `display.compact` is set; tooltips and tables keep the full text.

  Verify: `chart-widgets.spec.ts` and the data label specs: `Tile abbreviated`, `Data labels abbreviated`, `Tile that does not abbreviate`, `Currency abbreviated`, `Thousands in Spanish of Spain`, `Small value not abbreviated`, `Percentage not abbreviated`, `Full value of a tile`, `Tooltip of an abbreviated label`.
- [ ] 9.4 The script editor's preview (`script-editor/indicator-view.ts`): numbers in `NUMBER_LOCALE`, and the formatter built with the edited metadata's decimals.

  Verify: `indicator-view.spec.ts` cases: the totals in the project's format, and `Decimals offered next to the format` (preview part: choosing 1 decimal redraws with one decimal and starts no test).
- [ ] 9.5 Images and printing.

  Verify: `image-layout.spec.ts` and `export-parts.spec.ts` cases `Image of an abbreviated tile` and `Image in the project's format` (UI part: the value as the screen writes it, `46,85 % de los hogares` with its U+00A0 kept in the SVG text); `dashboard-print.spec.ts` cases `Printed by an English member` and `Abbreviated tile printed`.

## 10. UI: the forms and the documentation

- [ ] 10.1 Settings > General (design D9): the **Regional format** select in the admin's settings form with "Each member's browser" and the seven formats named with their examples, its hint, its error next to it, and the value shown to analysts (`overview.numberLocale`, `overview.numberLocaleHint`, `overview.numberLocaleBrowser`, `overview.numberLocales.*`).

  Verify: `project-overview.spec.ts` cases `Formats offered with an example` (`12.345,6` and `46,9 %` for Spanish (Colombia), `46,9%` for Portuguese (Brazil)), `Analyst sees the value`, a save that sends `numberLocale` with the name and the time zone, `""` sent for "Each member's browser", and a server error on `numberLocale` shown on the select.
- [ ] 10.2 The indicator's details (design D9): the **Decimals** select in `indicator-metadata-fields.ts` with "Format's default ({digits})" and 0 to 4, in the add dialog and above the editor; `project-scripts.ts`' `metadataOf` and the empty metadata carry `decimals`; the save sends it; the read-only details write it (`scripts.fields.decimals`, `scripts.decimals.*`).

  Verify: `project-scripts.spec.ts` cases `Decimals offered next to the format` (UI part), a save sends `decimals` (and `null` for the format's default), an older version's details show its decimals, and a server error on `decimals` shows on the select.
- [ ] 10.3 The widget form (design D8): the **Decimals** select for every kind with the default naming its digits, the **Abbreviate large numbers** checkbox for the five kinds (disabled while a chart's data labels are off), `setCompact`, `displayDraft()`, `displayFor` (decimals on every kind, `compact` on the five) and `fieldFor` (`indicators.dialog.decimals.*`, `indicators.dialog.compact.*`).

  Verify: `widget-dialog.spec.ts` cases `Indicator's default named`, `Decimals previewed`, `Chart type change drops the abbreviation`, `Form saved unchanged` (a widget with none, and one saved with `compact: false`, send the same config), and server errors on `display.decimals` and `display.compact` shown on their fields.
- [ ] 10.4 Add the shapes of task 5.3 to the UI side of the production-shapes fixture, and raise the spec's pinned count (`toHaveLength`, `new Set(…)`) by the same four.

  Verify: `production-shapes.spec.ts` passes: the existing shapes give the same `savedConfig`, `queryFor` and unchanged form save as before, and the new shapes save the two fields as given.
- [ ] 10.5 Document the feature in `docs/indicators-dashboard.md` (design D13): "What a value means: format and unit" with the regional format (the space before `%` in its Spanish formats, never in a browser alone), the decimals and how to get `$60.514`; a `### Abbreviated numbers` section; the "Unit" section (a widget's unit and decimals together, the `%` that is not repeated); "The fields in the widget form"; the box plot's "Numbers" bullet; printing.

  Verify: `IndicatorDocsTests` pass, and every behavior of the `dashboard-indicators`, `interface-language`, `projects`, `pipeline`, `dashboard-versions` and `dashboard-printing` deltas is described.

## 11. Integration checks

- [ ] 11.1 Run the whole API suite in the background (unit, then integration, about 25 to 35 minutes), with the output saved to a file in the scratchpad. Verify: everything passes apart from the failures known on a clean `main` (the memory note lists them), and the counts are recorded in design.md.
- [ ] 11.2 Run the UI suite (`TZ=UTC`), `npx ng build` and `scripts/check-ui-safety.sh`. Verify: everything passes apart from the two known polling failures, the initial bundle stays under its budget (the size is recorded in design.md), and the safety script is clean. The analytics suite is not run: this change does not touch `tamiza-analytics`.
- [ ] 11.3 Check the real stack in a separate Compose project (the `tamizaverify` recipe), seeded with invented tables shaped like `teatro_hoy`, `teatro_precio_hogar` (one row per household) and `comercio_gastos_media`, a dashboard with stat tiles, a column chart with data labels, a box plot, a histogram and a map with a timestamp tooltip column. Check, in an `en-US` browser and then an `es-ES` one:
  - before any setting, every number as before;
  - after an admin sets `es-CO` in Settings > General: the dashboard, the widget form's preview and the script editor's preview in `es-CO`, every percentage and share with a no-break space before `%` that never wraps onto its own line, dates and words in the browser's language;
  - decimals on the indicator and on a widget, an abbreviated tile and abbreviated data labels, with their full values in the title, the tooltip and "Show as table";
  - 1,280 and 390 px, light and dark, with no horizontal page scroll;
  - an SVG and a PNG image and a workbook of the tile and of the histogram;
  - printing to PDF on A4.

  Verify: findings and fixes are recorded in design.md.
- [ ] 11.4 In Chrome on that stack, run every case of `number-formats.json` through the page's `Intl` with the space rule of design D2 (`percentFormat` with `numberStyleOf` of the case's format; a short script over the DevTools protocol). Verify: Chrome writes every `text`, the Spanish percentages with one no-break space; any difference is recorded in design.md and either fixed in the table or listed as a known difference.
- [ ] 11.5 Send a scheduled email of that dashboard to Mailpit, saved by an `en-US` member, with the project in `es-CO`. Verify: the words and the date are in English, the numbers in `es-CO` with the decimals and `46,9 %` with its no-break space in the body, the plain text and the "Summary", the abbreviated tile in full, and the workbook's cells are numbers in the formats of the `dashboard-reports` delta.
- [ ] 11.6 From an MCP client: `get_project` gives `numberLocale`; `get_widget_data` gives `es-CO` texts with the widget's decimals (`46,9 % de los hogares`); `save_script` without `decimals` keeps them and with `null` clears them; a bare chart type change of an abbreviated tile is refused. Verify: the results match the `mcp-server` delta.
- [ ] 11.7 Run `openspec validate add-locale-formatting --strict`. Verify: it passes with no warning.

## Workflow follow-up

- Write "Deviations from the plan" and "Verification Results" in design.md, update `docs/tamiza-brief.md` section 8 (roadmap: this change done, the P1 program complete), and have the user review them.
- Sync the delta specs and archive the change with `/opsx:archive` once the user has reviewed the results.
- When the P2 and P3 changes are written with `/opsx:update`, rebase them on this one: `improve-widget-export` (the n, numerator, denominator and weight columns take the decimals and the project's format), `improve-mcp-ergonomics` (descriptions of `numberLocale`, `decimals` and `compact`), `add-weighting` (weighted values keep the decimals), `add-normalization-and-totals` (engine shares keep their digits) and `add-reference-lines-from-indicators` (lines written with the decimals of their own indicator).
