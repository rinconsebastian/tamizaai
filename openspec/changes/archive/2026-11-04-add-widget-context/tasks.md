# Tasks

**Order.**
- Groups 1 to 5 (api) come first, in order. Each ends with its tests green. Group 5 can start after group 3.
- Groups 6 to 11 (ui) follow, in order. Group 6 keeps the UI build and suite green from its first type change. Group 11 comes after group 10, since an image's footer reads the source the page shows.
- Group 12 is last.

This change is implemented after `add-indicator-column-metadata`, which is archived (2026-11-04). Its `display.labels`, the column names in `plotted` and `ReportDataBuilder.Plotted`, migration 0021, its widget workbook request and its production-shapes fixture (`tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json`, `ProductionShapesTests.cs`, `production-shapes.spec.ts`) are already there. Line numbers below are those of the code before that change, so they can be off by a few lines.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks that they match). Spec scenario titles are in backticks, with "(API part)" and "(UI part)" when a scenario is split between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~DashboardSourceTests"`.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`.
- **Analytics:** the `tamiza-analytics-test:dev` image, from the repository root with `MSYS_NO_PATHCONV=1`. Its integration tests build their template database from the dbUp scripts, so migration 0022 reaches them.
- **Known failures on clean HEAD** (not regressions): the two UI "stops polling when the page is destroyed" tests, the time-zone title test of `project-indicators.spec.ts`, and the API `IndicatorSeriesQueryTests.A_ratio_*` tests (the machine's decimal comma).

## 1. Database (api)

- [x] 1.1 Write `tamiza-api/src/dbUp/Scripts/0022_add_widget_context.sql` exactly as design D7, with no upper-case `CHECK` in its comment. Then:
  - add `Dashboard.Source` (`string?`) to `Data/Dashboard.cs`, after `ValueColors` (`:28-29`);
  - mirror the column and `ck_dashboards_source` in `Data/TamizaDbContext.cs` (`:415-426`);
  - add a `ScriptConventionTests` case after `The_dashboard_palettes_script_adds_two_columns_and_one_named_check` (`ScriptConventionTests.cs:327`): one `ADD COLUMN`, exactly one `CHECK`, no index, no `NOT NULL` and no `UPDATE`.

  Verify: `ScriptConventionTests` and `SchemaDriftTests` pass.
- [x] 1.2 Add a `SchemaMigratorTests` upgrade case after the pattern of `Dashboard_palettes_script_gives_every_dashboard_the_palette_tamiza_with_no_colors_and_changes_nothing_else` (`SchemaMigratorTests.cs:1017`):
  - apply the scripts through 0021 and seed a dashboard with filters, the palette `earth`, a color kept by value, two widgets and its version 1;
  - migrate;
  - check that its `source` is null, and that its filters, palette, colors, `updated_at`, widgets and version content are unchanged;
  - check that a source of 300 characters is stored, and that an empty one and one of 301 characters fail on `ck_dashboards_source`.

  Verify: the test passes.
- [x] 1.3 In `tamiza-api/src/dbUp/README.md:24`, name the source in the rule about scripts that rewrite `tamiza.dashboard_versions.content`: a version holds `source` only when the dashboard has one. Verify: the rule names `source` and says when it is written, as design D7 does.

## 2. A widget's texts in its config (api)

- [x] 2.1 Add the settings of design D1 and D2 to `Indicators/IndicatorModels.cs`:
  - the record `WidgetCaption(Text, Hidden)`, both members `WhenWritingNull` with a `[Description]`, and its `Normalize()`;
  - `WidgetDisplay.Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription`, `WhenWritingNull` with a `[Description]` and no enum, appended after `Labels`;
  - the constants `WidgetDisplay.MaxSubtitleLength = 200`, `MaxFootnoteLength = 500` and `MaxCaptionLength = 200`;
  - `WidgetDisplay.Normalize()` (`:167-172`) trims the three texts, reads blanks, `false` and an empty caption as none, and returns null only when every member, old and new, is unset.

  Verify `WidgetDisplayTests`:
  - `MinimalBefore` and `RichBefore` (`WidgetDisplayTests.cs:16-20`) and the cases of `add-indicator-column-metadata` still serialize byte for byte;
  - `{"display":{"subtitle":"¿En qué lugares ha visto teatro en el último año? (E4)","footnote":"No suman 100 %: respuesta múltiple.","caption":{"hidden":true},"unit":"de quienes asistieron","showDescription":true}}` round-trips unchanged;
  - a display with a color, labels and a subtitle writes them in the record's order, the old members first;
  - a display holding only a subtitle is kept;
  - a display holding only `"   "`, `"showDescription":false`, `{"hidden":false}` or `{"text":" "}` reads as no display;
  - the texts are stored trimmed.
- [x] 2.2 Add the rules of design D2's table to `IndicatorValidation.CheckDisplay` (`IndicatorValidation.cs:164-210`), on every kind and with no rule per kind. Verify `IndicatorValidationTests`:
  - one case per row of the table, each with its key and message;
  - the limits at both sides: 200 and 201, 500 and 501, 200 and 201, 32 and 33 characters, counted after trimming;
  - a NUL, a line break, a tab and U+0085 are each refused on the field's key;
  - the five fields are accepted on every kind of `ChartTypes.All`, and with no chart type;
  - `Chart type changed` (API part): a bar chart with a subtitle and a unit turned into a pie chart is accepted.
- [x] 2.3 Add a `WidgetQueryTests` case: a config with every new field gives the query of the same config without them. Its UI twin comes in 6.2. Verify: the case passes, and `IndicatorQueryTests`, which pins the answers byte for byte, passes unchanged.
- [x] 2.4 Check the fields through the widget endpoints. Verify `DashboardWidgetTests`:
  - `Long subtitle`, `Line break in a footnote`, `Caption with a text and hidden` and `Long unit`: 400 on create and on update, keyed `config.display.…`, and nothing saved;
  - `Blank subtitle`, `Every chart type` and `Chart type changed`;
  - `Form saved unchanged before` (API part): a column chart in color 2 stored before the fields, sent back unchanged with PATCH, writes nothing and makes no version.

  Verify also an OpenAPI test after the pattern of `The_openapi_document_describes_the_boxes_of_a_box_plot_and_its_orientation` (`IndicatorOverviewTests.cs:434`), describing the five fields and `WidgetCaption`.
- [x] 2.5 Check versions, copies and moves. Verify:
  - `DashboardVersionTests`: `Subtitle restored`, `Unit changed` (a version that says the widget changed), and `Saved unchanged` (API part: a PATCH with the config the widget has makes no version);
  - `DashboardTests`: `Copied with the dashboard` (API part);
  - `DashboardWidgetTests`: `Moved widget` (API part), with the moved widget's stored config byte for byte the one it had.
- [x] 2.6 Extend the production-shapes fixture (design D13) with shapes built from invented values:
  - a bar of a ratio grouped by `categoria` with a subtitle, a footnote, a hidden caption and a unit;
  - a column chart in color 2 with an own caption text;
  - a stat tile with an own caption text, a footnote and `showDescription`;
  - a map with its tooltip columns and basemap and a unit;
  - a Likert widget with a subtitle, a kind with no caption line.

  The new shapes carry no UI goldens yet: 6.3 and 9.2 add them. Make `production-shapes.spec.ts` skip, by name, a shape that has none, so the UI suite stays green meanwhile. Add to `ProductionShapesTests`:
  - each new shape's `stored` reads and writes back byte for byte;
  - its `query` and `plan` equal those of the same config without the new members, computed in the test;
  - each shape's `saved` golden (what the page sends with a workbook request after design D10) passes `IndicatorValidation.CheckConfig` for its chart type with no error, so no production widget's workbook is refused.

  Verify: `ProductionShapesTests` and `production-shapes.spec.ts` pass, the fixture's diff only adds entries (no golden of an existing shape changes), and a search of the fixture for the study's names (`teatro_`, `escuelas_`, `Duitama`, `COMUNA`, `quienes asistieron`) finds nothing.

## 3. The dashboard source (api)

- [x] 3.1 Add `DashboardValidation.Source(string?, FieldErrors)` and `MaxSourceLength = 300` (design D7). Verify `DashboardValidationTests`:
  - a source is trimmed, and a blank one is null;
  - 300 characters are kept and 301 are refused on `source`, naming 300;
  - a NUL, a line break and a tab are refused on `source`.
- [x] 3.2 Add `PUT /dashboards/{dashboardId}/source` (`SaveDashboardSource`, role `analyst`) next to the colors route (`DashboardEndpoints.cs:56`), built as `SaveColorsAsync` (`:227-268`) is, with OpenAPI. Add `DashboardView.Source` (`WhenWritingNull`, last), read in `ViewAsync` (`:85-100`). Verify a new `DashboardSourceTests` (integration), after the pattern of `DashboardColorsTests`:
  - `Source saved` and `Source removed` (API parts);
  - `Long source`, and `Viewer cannot change the source` (403);
  - a user who is not a member gets 404, and a removed dashboard is not found;
  - saving the stored source writes nothing (`updated_at` unchanged) and makes no version (`Source saved unchanged`, API part);
  - `No source` (API part): the dashboard's JSON has no `source` key.

  Verify also that the OpenAPI document describes the route, its body and its answer, after `The_openapi_document_describes_the_dashboard_endpoints` (`DashboardTests.cs:1016`).
- [x] 3.3 Copy the source in `DuplicateAsync` (`DashboardEndpoints.cs:476-478`). Verify `DashboardTests`: `Source duplicated` and `Source of a copy is independent` (API parts).
- [x] 3.4 Versions (design D7):
  - `DashboardChangeKinds.SourceChanged = "sourceChanged"` joins `All` after `ColorsChanged` (`DashboardVersions.cs:56-58`), and the endpoint notes it;
  - `Content` (`:339-370`) takes the source and writes `"source"` after `valueColors` only when it is set, and `ContentAsync`, `FirstVersion` and the restore pass it;
  - `ReadContent` (`:457-477`) reads a missing or non-string `source` as null, and `VersionContent` gains `Source`;
  - a restore writes it back with the filters and colors (`:632-634`);
  - `DashboardVersionDetails` (`DashboardVersionEndpoints.cs:42-57`) gains `Source`, `WhenWritingNull`.

  Verify unit tests:
  - the content of a dashboard without a source is byte for byte the content built before this change (the production-shapes `dashboard` entry's golden, and a fixed text of a version from 0021);
  - a content with a source reads back the same.

  Verify `DashboardVersionTests`:
  - `Source changed`, `Source saved unchanged` and `Source restored`;
  - `Version recorded before sources` and `Dashboard saved before sources`;
  - `Version without a source` (API part): `GET …/versions/{n}` of a version without one has no `source` key;
  - a restore's step 2 records no "unrecorded" version on a dashboard migrated from 0021.
- [x] 3.5 Add a dashboard with a source to the production-shapes fixture, beside its existing `dashboard` entry, with its golden content, and let `ProductionShapesTests` check both. Verify: the test passes, and the golden content of the existing entry is unchanged.
- [x] 3.6 In `docs/tamiza-brief.md` section 6, add the `display` fields of design D1 and `dashboards.source`. Verify: the limits written are those of `WidgetDisplay` and `DashboardValidation`.

## 4. Emails and workbooks (api)

- [x] 4.1 Own caption text and unit (design D3, D4):
  - `ReportDataBuilder.Plotted` (`ReportDataBuilder.cs:427-431`) returns `display.caption.text` when it is set;
  - `MetaOf` (`:76-77`) takes `widget.Config.Display?.Unit ?? indicator?.Unit`.

  The shared fixture `testing/column-labels.json` gains its caption cases in 7.1, with the UI twin, so the UI suite stays green until then. Verify:
  - `ColumnLabelsTests` has a case with a caption text, which no column name changes, and one with a hidden caption, which keeps the default;
  - `ReportDataTests`: `Own caption text` (API part: the caption line and the value column's header), `Own unit` (API part: "11.21% de quienes asistieron" and the header's unit), a box plot's table with a widget unit, `Removed indicator with a unit of its own` ("40,000.00 pesos por boleta"), a removed indicator's widget with an own caption text keeping it, and `Removed indicator` unchanged;
  - a hidden caption keeps what is plotted in the value column's header;
  - `Every_chart_type_builds_a_table_or_a_value` passes.
- [x] 4.2 Add `ReportModel.Source`, read with the dashboard (`ReportDataBuilder.cs:24-27`), and `ReportWidget.Subtitle`, `Footnote` and `CaptionHidden`, appended last and filled from the widget's display for every outcome, a reason included (design D9). Add `source`, `subtitle` and `footnote` to `ReportText`, in English and Spanish. Verify:
  - a `ReportDataTests` case fills them for a shown widget and for one that timed out;
  - `ReportTextTests.Both_languages_have_the_same_keys` passes.
- [x] 4.3 Write them in the HTML body and its plain text (design D9): the header (`ReportHtml.cs:78-95`), the stat tiles (`:98-127`), the blocks (`:133-176`) and `ReportPlainText` (`:198`). Verify `ReportHtmlTests`, in HTML and in plain text:
  - `Source in the header` (body part) and `Dashboard without a source`;
  - `Subtitle under the title`, `Caption hidden on a stat tile` and `Own caption text` (body part);
  - `Footnote after the table`, `Footnote of a stat tile` and `Footnote of a widget not shown`;
  - `Email of widgets without these settings`: the existing `ReportHtmlTests` cases, which pin today's body, pass unchanged;
  - the body budget (`:37-57`) counts the texts: a block that grows past it with a 500-character footnote is listed as not shown.
- [x] 4.4 Write them in the email's workbook (design D9): "Source" after the filters, and "Subtitle: {title}" and "Footnote: {title}" before a widget's notes line (`ReportWorkbook.cs:27-45`). Verify `ReportWorkbookTests`:
  - `Source in the header` (workbook part) and `Texts in the summary sheet`;
  - `Own unit` (workbook part): the widget sheet's value header names "de quienes asistieron";
  - `Summary_sheet` passes unchanged for a model without them.
- [x] 4.5 The widget workbook (design D7, D9, D10):
  - `WidgetWorkbookModel` (`WidgetWorkbook.cs:10-22`) gains `Source`, and the summary (`:63-90`) writes "Source" after the dashboard and the version, "Subtitle" after the widget, and "Footnote" before the notes;
  - `WidgetExportEndpoints` reads the dashboard's source, or the version's when one is named (`:98-131`);
  - it keys its config errors `config.…`, as a widget's save does: today it passes `"config"` without the dot (`:74`), so a refusal would read `configdisplay.subtitle`.

  Verify `WidgetWorkbookTests`: `Widget texts in the summary` and `No texts in the summary`. Verify `WidgetExportTests`, with requests that carry a saved config:
  - `Own text and unit in the header`, `Map tooltip labels` and `Hidden caption in the workbook`;
  - `Widget texts in the summary` (API part), with the dashboard's source and with a version's source;
  - a request with `queryFor`'s config gives the workbook it gives today;
  - a widget sent with `ignoredDashboardFilters` in its config, and with each filter's `ignored` flag, gives the same rows;
  - a subtitle of 201 characters is refused on `config.display.subtitle`.
- [x] 4.6 In `docs/operations.md` "What leaves the deployment by email", name the dashboard's source and the widgets' subtitles and footnotes, which members write. Verify: the sentence is there and matches `dashboard-reports` "Widget context in a scheduled email".

## 5. MCP (api)

- [x] 5.1 `get_widget_data` (design D4, D12):
  - each widget's result, for every status, gains `subtitle`, `footnote` and `caption` (the own text) when it has them;
  - `WidgetDataAsync` (`McpDashboardTools.cs:592-660`) hands `Row`, `SeriesPayload` and `BoxPayload` an `IndicatorInfo` whose `Unit` is the widget's when it has one.

  Verify `McpDashboardToolsTests`:
  - a widget with a unit has its rows, a series widget its cells and a box plot its figures `formatted` with that unit;
  - `Widget data without these settings`: no result holds `subtitle`, `footnote` or `caption`, and the existing cases pass unchanged;
  - `query_indicator` with `display.unit` in its config keeps the indicator's unit.
- [x] 5.2 `update_dashboard` gains `source` (design D12): left out keeps the source, an empty text removes it, and it is one more commit of the same `RecordAsync`. The result returns `source`. The refusal when nothing is given (`:257-260`) becomes "Give a name, a position, filters, a palette, colors kept by value or a source to change." Update the assertion that pins it (`McpDashboardToolsTests.cs:1526`) in the same step. `list_dashboard_versions` names `sourceChanged` (`McpDashboardVersionTools.cs:28`). Verify:
  - `McpDashboardToolsTests`: `Source through MCP`, `Rename and source in one version`, `Source removed through MCP` and `Long source through MCP`;
  - `McpDashboardVersionToolsTests`: `get_dashboard_version` returns a version's source, and none for a version without one;
  - `McpToolCatalogTests.The_version_list_names_every_kind_of_change_a_version_can_list` (`McpToolCatalogTests.cs:121-131`) passes.
- [x] 5.3 Add MCP write tests to `McpDashboardToolsTests`: `Widget context through MCP` (MCP part: `create_widget` saves the texts and `get_dashboard` returns them), `Caption refused through MCP` and `Config replaced through MCP`. Verify: the tests pass.
- [x] 5.4 Update the tool texts of design D12:
  - add `ContextHelp` after `DisplayHelp` (`McpDashboardTools.cs:50-55`) in the descriptions of `create_widget` and `update_widget`;
  - add the five fields to the `display` of `create_widget`'s config template (`:346`);
  - extend `WidgetConfigInput.Display`'s `Description` (`IndicatorModels.cs:214-215`) to the texts, keeping its opening "How the widget is drawn";
  - name the source in the title, description and new `source` parameter of `update_dashboard`, and in the descriptions of `get_dashboard`, `get_dashboard_version`, `restore_dashboard_version` and `duplicate_dashboard`;
  - say in `get_widget_data`'s description that each widget's result gives its `subtitle`, `footnote` and own `caption` when it has them, and that values are formatted with the widget's own unit.

  Leave `ChartTypeChangeHelp` and the sentence "config.display = { color, ramp, orientation, labels } sets how the widget is drawn" as they are. `get_widget_data` already returns a `labels` map per widget: add `subtitle`, `footnote` and `caption` beside it. Verify:
  - `McpToolCatalogTests` passes: `Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` (`:80-101`) unchanged, every tool and argument described, and no enum;
  - a new sibling test checks that `ContextHelp` names `subtitle`, `footnote`, `caption`, `unit` and `showDescription`, and that `get_widget_data`'s description names `subtitle`, `footnote` and the widget's unit;
  - a new `IndicatorDocsTests` theory, after `The_dashboard_tools_say_the_most_dashboards_a_project_can_have` (`IndicatorDocsTests.cs:256`), checks that the numbers in `ContextHelp` and in the `source` parameter's description are those of `WidgetDisplay`, `ScriptEndpoints.MaxUnitLength` and `DashboardValidation.MaxSourceLength`.
- [x] 5.5 Write the guidance:
  - `Mcp/Instructions.md`: the section "## Words on a widget" of design D12, after "## Colors" (`Instructions.md:37`);
  - `docs/mcp.md`: the five display fields and their refusal keys, `update_dashboard`'s `source` ("any one of the six is enough", `docs/mcp.md:87`), `source` in `get_dashboard` and `get_dashboard_version`, the texts in `get_widget_data`, the row `` | `sourceChanged` | ``, and a sentence for the source after the pinned "Every change to a dashboard's name, filters, colors or widgets is recorded as a version" (`:93`), which stays.

  `Assistant/Prompts/system.md` is not changed, so `AssistantPrompts.Version` stays. Verify:
  - `McpGuideTests` passes, with a new case: the section names `subtitle`, `footnote`, `caption`, `unit` and `source` in backticks, and says that `update_widget` needs the `display` sent again;
  - `IndicatorDocsTests` passes: `The_mcp_documentation_lists_every_kind_of_change_a_version_can_list_and_says_the_page_words_colorsChanged_as_colors_changed` (`:218-232`) with the new row, and `The_mcp_documentation_says_a_dashboard_is_versioned_for_its_content_and_not_for_its_place` (`:202-215`) unchanged.

## 6. Types, the service and the saved config (ui)

- [x] 6.1 In `core/projects/indicators.service.ts` (design D1, D7), add:
  - `WidgetCaption`, and `subtitle`, `footnote`, `caption`, `unit` and `showDescription` on `WidgetDisplay`;
  - `maxSubtitleLength = 200`, `maxFootnoteLength = 500`, `maxCaptionLength = 200` and `maxSourceLength = 300`;
  - `Dashboard.source?` and `DashboardVersionDetails.source?`, and `saveSource(projectId, dashboardId, source)`;
  - `sourceChanged` after `colorsChanged` in `DashboardChangeKind` and `dashboardChangeKinds` (`:330-361`), with the history's words "Source changed" and "Se cambió la fuente";
  - the doc of `WidgetExportRequest.config` (`:136`): "as `savedConfig` builds it".

  Add to the API unit tests a guard that reads `indicators.service.ts` and fails when its four limits differ from `WidgetDisplay`'s and `DashboardValidation`'s, as `PalettesTests` guards the palettes. Verify:
  - `npx ng build` type-checks;
  - `indicators.service.spec.ts` covers `saveSource`: the `PUT` body and the answer;
  - `dashboard-versions-dialog.spec.ts` words `sourceChanged` "Source changed";
  - the guard passes.
- [x] 6.2 In `widget-view.ts`, keep the five fields in `displayFor` (`:75-83`) on every kind and across a change of kind (design D1). `queryFor` (`:102-121`) does not change. Verify:
  - `query-for.spec.ts` has the case of `WidgetQueryTests` (2.3);
  - `widget-view.spec.ts`: editing any of the five fields sends no query (the re-query key at `:429-432` is unchanged), `savedConfig` keeps them on every kind and when a stat tile becomes a bar chart, and a widget without them saves what it saved before.
- [x] 6.3 Give the shapes of 2.6 their `saved` and `uiQuery` goldens, and run `production-shapes.spec.ts`. Verify:
  - every existing shape's `saved` and `uiQuery` are deep-equal to its goldens;
  - each new shape round-trips through `savedConfig`, and its `queryFor` equals that of the shape without the new members;
  - `ProductionShapesTests` passes, the new shapes' `saved` checked by `CheckConfig` too.

## 7. The caption and the unit (ui)

- [x] 7.1 In `indicators/format.ts` (design D3, D4):
  - `plotted` (`:108-114`) returns `display.caption.text` when it is set, and its `Pick` type gains `display`;
  - add `captionShown(config)`;
  - add `withWidgetUnit(indicator, config)`, which returns the indicator with the widget's unit when it has one.

  Move the two caption cases of 4.1 into the shared fixture `testing/column-labels.json` (design D13). Verify `format.spec.ts` and `ColumnLabelsTests`:
  - both run every case of the fixture, the caption cases included, with the same results;
  - `captionShown` is false only for `{ hidden: true }`;
  - `withWidgetUnit` with no display returns the indicator's unit, and a widget unit of `%` on a percent indicator is not repeated after the number.
- [x] 7.2 Build `formatter` and `boxFormatter` from `withWidgetUnit(this.indicator() ?? plainIndicator, config)` in `widget-view.ts` (`:343-345`). Verify `chart-widgets.spec.ts` and `boxplot-chart.spec.ts`:
  - `Base of a subgroup`: "11.21% de quienes asistieron";
  - `Other widgets keep the indicator's unit`;
  - `Unit of a box plot`: "40,000.00 pesos por boleta";
  - `Widget's own unit`: "12,450 households with children";
  - `Stat tile with its own unit`: "24.22% de quienes asistieron";
  - a widget whose indicator the project no longer lists shows two decimals and its own unit;
  - a map's tooltip columns keep their own formats.
- [x] 7.3 Give each caption line `[class.sr-only]="!captionShown(config())"` (design D3), keeping it in the DOM: the `chart-caption` lines of `axis-chart.ts` (`:48`), `pie-widget.ts:50`, `map-widget.ts:77`, `heatmap-chart.ts:146` and `crosstab-widget.ts:60`, and the `stat-label` of `stat-widget.ts:15`. The map's tooltip line of the plotted value takes the own text when it is set (`map-widget.ts:789`, into `map-tooltip.ts:68`). Verify `chart-widgets.spec.ts`, `series-widgets.spec.ts`, `map-widget.spec.ts` and `map-tooltip.spec.ts`:
  - `Own text above the bars` ("% de quienes asistieron by categoria"), a histogram's "Rows per range of …" and a heat map's "… by … and …";
  - `Own text in the table`, `Own text on a stat tile` and `Series legend kept`;
  - `Hidden above the columns`: the line carries `sr-only` and still holds what is plotted, and the chart's `aria-label` still names it; `Hidden on a stat tile`: the label carries `sr-only` and still holds what is plotted; `Table header kept` and `Legend kept`;
  - a cross table, and a pyramid or Likert widget shown as a table, whose caption line carries `sr-only` when hidden;
  - `Own caption text and unit in a map tooltip` ("Avance de la muestra: 45% de la meta"), and `City block` unchanged;
  - a widget without a caption setting has no `sr-only` on its caption line, and every existing chart spec passes unchanged.

## 8. The tile (ui)

- [x] 8.1 Put the subtitle in the tile's header (`widget-grid.ts:59-83`), as design D5 says:
  - for a widget with a subtitle only, the header becomes a column: the existing row, then `<p id="widget-subtitle-{id}">`, and the `article` gains `aria-describedby`;
  - `line-clamp-2`, or `line-clamp-1` on a tile whose `layout.h` is 2 or less, the whole text in a `title` attribute, and `paper:line-clamp-none`.

  Verify `widget-grid.spec.ts`:
  - `Subtitle under the title` and `Subtitle of a stat tile`;
  - `Whole text on pointing`: the `title` attribute holds the whole text;
  - the clamp class for a tile 2 rows high and one 4 rows high;
  - `Widgets saved before`: a widget without a subtitle has no subtitle element and no `aria-describedby`, and its header's element and classes are today's.
- [x] 8.2 Put the footnote at the foot of the widget view (design D5): shown with the chart, the table or the "no rows" message, and not while it loads or fails, or when its indicator is not published or a column is gone. A widget with a footnote gives its chart's box (`widget-view.ts:214`) `overflow-hidden`. A stat tile with a subtitle or a footnote takes `justify-center-safe` in place of `justify-center` (`stat-widget.ts:13`). Verify `widget-view.spec.ts` and `chart-widgets.spec.ts`:
  - `Footnote under the chart` and `Footnote under the table`;
  - no footnote while loading or for a missing column;
  - the clamp classes, and `overflow-hidden` on the chart's box of a widget with a footnote;
  - a widget without a footnote has no footnote element and today's chart box classes, and a stat tile without texts keeps `justify-center`.

  jsdom has no layout: `Long subtitle on a column chart` and `Stat tile two rows high` are checked on the real stack (12.4).
- [x] 8.3 Write `indicators/description-popover.ts` (design D6) and put its button in the tile's header, before the menu, with `paper:hidden`, when `display.showDescription` is on and the indicator's description is not blank. Add its strings ("About {{ label }}" / "Acerca de {{ label }}"). Verify a `description-popover.spec.ts` and `widget-grid.spec.ts`:
  - `Description opened` and `Indicator without a description`;
  - `Closed with Escape`, with focus back on the button, and closed by a click outside or by the button;
  - focus moves into the panel, the panel is a `role="dialog"` named after the indicator, and the button has `aria-expanded` and `aria-controls`;
  - opening one closes another.
- [x] 8.4 Write the section "## Words around a widget" of design D13 in `docs/indicators-dashboard.md`: the subtitle, the footnote, the caption's three modes, the widget's unit, the description button, the room the texts take in a tile (a line of a 6-column tile holds about 90 characters, one of a 3-column tile about 40; a 3×2 stat tile with both texts cuts its label and keeps its figure), the form's warning, and what prints (whole texts, and a chart they leave too little room cut at its box). Add the widget's unit to "What a value means: format and unit". Verify: the links resolve and `IndicatorDocsTests` passes.

## 9. The widget form (ui)

- [x] 9.1 Add the fields of design D11 to `widget-dialog.ts`, with their strings:
  - **Subtitle** under **Title** (`:170-172`);
  - the fieldset **Text on the widget** after the color fields: **Caption** (Default, Own text, Hidden) with its text field and the line it makes as hint, **Unit** with the indicator's unit as hint, **Footnote**, and **Show the indicator's description**;
  - the `maxlength` of each field from the constants of 6.1.

  Verify `widget-dialog.spec.ts`:
  - `Unit hint` ("de los hogares"), and the hint for an indicator with no unit;
  - `Description option unavailable`;
  - the caption's hint reads "{text} by {group}";
  - every field is reached with the keyboard.
- [x] 9.2 Seed the signals from `start.display`, carry the fields in `displayDraft()` (`:931-938`) trimmed and with line breaks turned into spaces, and show the subtitle and the footnote in the preview. Verify `widget-dialog.spec.ts`:
  - `Preview without a query`;
  - `Saved unchanged` (UI part): a widget with every field sends the config it read;
  - `Form saved unchanged before` (UI part): a column chart in color 2 saved before sends what it read, with no new key;
  - a pasted line break is saved as a space, and "Own text" with an empty text saves no caption;
  - `production-shapes.spec.ts`: the new shapes get their `formSaved` goldens and no shape is skipped any more; every shape's form saved unchanged sends its golden, and `ProductionShapesTests` reads each `formSaved` back to its `stored`.
- [x] 9.3 Map `display.subtitle`, `display.footnote`, `display.caption`, `display.caption.text` and `display.unit` in `fieldFor` (`:1605`), placed before its `title` check (`:1619`), since `subtitle` contains `title` (design D11). Add `textsCrowd(layout, subtitle, footnote)` to `format.ts` and the form's warning of design D5. Verify `widget-dialog.spec.ts` and `format.spec.ts`:
  - `Error on its field`, an error on `config.display.subtitle` shown on the subtitle field and not on the title, and a caption error shown on the caption's radios;
  - `Warning in the form`, for a new stat tile at its default 3×2 and for an existing tile 2 rows high;
  - `Warning for long texts` (a 4×3 column chart with a footnote of 500 characters) and `No warning on a tall tile` (a 6×4 column chart with a subtitle of 80 characters);
  - `textsCrowd` cases at both sides of half the tile's height.
- [x] 9.4 Describe the form's fields in "## Words around a widget", and say in "Moving a widget to another dashboard" and "Version history" that a moved widget, a duplicate and a restored version keep its texts, caption, unit and description button. Verify: the sentences match `dashboard-indicators` "Widget context kept with the widget", and `IndicatorDocsTests` passes.

## 10. The dashboard source (ui)

- [x] 10.1 Write `indicators/dashboard-source-dialog.ts` (design D7), built like `dashboard-name-dialog.ts`, with its strings. Verify a `dashboard-source-dialog.spec.ts`:
  - one field of at most 300 characters, and an empty field sends `null`;
  - the server's error shows on the field (`Long source`, UI part);
  - nothing is sent when nothing changed;
  - keyboard access, and focus back on the actions menu's button when the dialog closes, saved or cancelled.
- [x] 10.2 Wire the source into `project-indicators.ts` (design D7):
  - the **Source** action after **Colors** (`:441`), offered where **Colors** is;
  - a `dashboardSource` signal set where `dashboardColors` is: read (`:634`), reset when another dashboard opens (`:682`), after a save (as `:1238`) and after a restore (`:1424`); and a `source` computed from the version shown or the dashboard, as `colors` is (`:508-511`);
  - a save answered 404 calls `gone(dashboard)`, as the colors save does (`:1232`);
  - the line `data-testid="dashboard-source"` under the grid and before the indicators' disclosure (`:333-369`), with `paper:hidden`;
  - the line `data-testid="print-source"` in the print header (`:133-147`).

  Verify `project-indicators.spec.ts`:
  - `Source action offered`, and a viewer is offered no **Source**;
  - `Source saved` and `Source removed` (UI parts): the page shows the new source without reading the widgets' data again;
  - `Under the widgets`, `Source after the widgets`, `No source` and `Version without a source` (UI part);
  - `Source in the print header` ("Fuente: …" in Spanish), `No source printed`, and no source line under the widgets on paper;
  - `Source restored` (UI part);
  - opening "Escuelas" after "Teatro" shows no source line, and a source saved on a dashboard another member removed tells the member and opens the first dashboard.
- [x] 10.3 Document the source in `docs/indicators-dashboard.md`:
  - "### Dashboard source";
  - **Source** in the dashboard actions sentence (`docs/indicators-dashboard.md:13`), after **Colors**;
  - the source in "Version history" and in the duplicate's sentence, keeping the sentences `The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them` (`IndicatorDocsTests.cs:386`) pins.

  Verify: `IndicatorDocsTests` passes, `The_dashboard_documentation_lists_the_actions_of_the_dashboard_menu_in_the_order_the_page_has_them` (`:286-308`) included.

## 11. Images, paper and the widget workbook (ui)

- [x] 11.1 Images (design D8):
  - `ImageJob` (`widget-export/widget-exporter.ts:16-32`) gains `subtitle`, `footnote` and `captionHidden`;
  - `FooterFacts` gains `source`, and `footerLines` (`footer.ts:2-25`) gives it a line after the project, dashboard and version line;
  - `ImageInput` (`image-layout.ts:27-46`) gains `subtitle` and `footnote`, and `composeImage` (`:96-175`) draws them in the order of D8;
  - the exporter passes `parts.caption`, and a stat tile's description, as null when the caption is hidden (`ExportParts.stat.description` becomes `string | null`, `export-parts.ts:38`);
  - `exportImage` (`project-indicators.ts:766-806`) fills them from the widget's display and the source shown.

  Verify:
  - `footer.spec.ts`: `Source in the footer` and `Dashboard without a source` (no source line);
  - `image-layout.spec.ts`: `Subtitle and footnote` (whole texts at 12 px, in their places), `Caption hidden`, and a stat tile with no description;
  - `widget-export-flow.spec.ts`: `Own caption text` ("% de quienes asistieron by categoria") and `Not printed or exported` (the image holds no description);
  - an image of a widget and a dashboard without these settings is the same SVG text as before.
- [x] 11.2 Send `savedConfig(widget.chartType, widget.config)` with the workbook request in place of `{ ...queryFor(…), display: { labels } }` (`project-indicators.ts:811-818`, design D10), and update the doc of `WidgetExportRequest.config` (`indicators.service.ts:142`). Verify:
  - `widget-export-flow.spec.ts` "sends the widget's query…" (`:31`) expects the saved config;
  - `project-indicators.spec.ts`: a map sends its tooltip labels, formats and basemap, and a widget of a version sends that version's display;
  - the case of `add-indicator-column-metadata` that checks the labels are sent now expects the whole saved display.
- [x] 11.3 Add print cases to `dashboard-print.spec.ts`. Verify:
  - `Whole texts on paper`: the subtitle and the footnote carry `paper:line-clamp-none`, and the chart re-measures in paper mode;
  - `Hidden caption on paper`;
  - `Description not printed`: the button is `paper:hidden`;
  - `Texts that leave a chart too little room` (`dashboard-printing` "Charts on paper"): the chart's box of a widget with a footnote carries `overflow-hidden` in paper mode; the cut itself is checked on the real stack (12.4).
- [x] 11.4 Run `npx ng build` and `npm run check:safety` in `tamiza-ui/`. Verify:
  - the initial bundle stays under its budget, the new code is in the lazy dashboard chunk, and no component style passes 4 kB; the sizes are recorded in design.md;
  - the safety script (no `innerHTML`, number pipes or stray `.toFixed(`) is clean.
- [x] 11.5 In `docs/indicators-dashboard.md`, add the texts and the source to "Exporting a widget" (images, and the workbook built from the saved settings, which now heads a map's tooltip columns with their labels) and to "Printing a dashboard". In `docs/tamiza-brief.md` section 8, mark `add-widget-context` done in the program's list. Verify: the links resolve and the brief names this change.

## 12. Integration checks

- [x] 12.1 Run the whole API suite in the background, unit then integration, with the output saved to a file (about 25 to 35 minutes). Verify:
  - everything passes, apart from the failures known on clean HEAD;
  - `ProductionShapesTests` passes with no golden of an existing shape changed since `add-indicator-column-metadata`;
  - the counts are recorded in design.md.
- [x] 12.2 Run the UI suite (`npm test -- --watch=false`). Verify:
  - everything passes, apart from the failures known on clean HEAD;
  - `production-shapes.spec.ts` passes;
  - the counts are recorded in design.md.
- [x] 12.3 Run the analytics suite in `tamiza-analytics-test:dev`, since its template database is built from the dbUp scripts and 0022 is one of them. Verify: it passes, and `git status -- tamiza-analytics` lists no change.
- [x] 12.4 Check the real stack in a separate Compose project (`-p tamizaverify`, its own env file and port, a fake sign-in and a fake Kobo; the developer's own stack and Keycloak untouched). Seed an invented long-format indicator with `numerator`, `denominator`, `categoria` and `zona`, a per-household `count` indicator with a description, and a dashboard with:
  - a bar chart with a subtitle of 180 characters, a footnote, a hidden caption and a unit;
  - a 3×2 stat tile with a subtitle, a footnote and an own caption text;
  - a column chart with an own caption text, a box plot with a unit, and a map with tooltip labels;
  - a source, and the description button on one widget.

  Check:
  - `Long subtitle on a column chart` and `Stat tile two rows high`: tiles keep their height, texts clamp and show whole on pointing, no chart paints over a footnote, and the 3×2 stat tile keeps its figure whole (its label cut, as design D5 computes); the measured heights are recorded in design.md;
  - each at 1,280 and 390 px, in the light and the dark theme, with no horizontal page scroll;
  - the description popover with the pointer, the keyboard and a touch emulation, and the accessible names in the accessibility tree;
  - "Show as table", PNG and SVG images, and the widget workbook (the map's tooltip labels and formats);
  - printing to PDF on A4 in both orientations: whole texts, the source in the header, no button, and `Texts that leave a chart too little room` (a 4×3 column chart with a 500-character footnote cut at its box, never over the footnote);
  - the form: hints, the 2-row warning, errors, and no query while typing;
  - the **Source** action, the version history's "Source changed", a restore and a duplicate;
  - from an MCP client: `update_dashboard` with a source and a rename in one call, `create_widget` with the texts, and `get_widget_data`.

  Verify: findings and fixes are recorded in design.md.
- [x] 12.5 Send the dashboard's scheduled email to Mailpit, in English and in Spanish. Verify:
  - the header gives the source, and the blocks give the subtitles, the own captions, the units and the footnotes in their places, in the HTML body and the plain text;
  - the workbook's "Summary" lists the source, the subtitles and the footnotes;
  - a dashboard without these settings sends the same body and workbook as before the change.
- [x] 12.6 Run `openspec validate add-widget-context --strict`. Verify: it passes.

## Workflow follow-up

- Sync the delta specs and archive the change once the user has reviewed the verification results.
- Then rebase the later P1 changes of the program with `/opsx:update`:
  - `add-chart-annotations`, `add-declared-category-order`, `add-category-highlight` and `improve-stat-cards` find the widget workbook request sending the saved config, and `widget-export` "Parts of a widget image" and "Summary of a widget workbook" with the subtitle, the footnote and the source;
  - `add-small-sample-warnings` puts its lines before the footnote, as `dashboard-reports` "Widget footnote in a scheduled email" orders them, and copies its `dashboard-indicators` "Map tooltip" block again from the text this change leaves (the own caption text and unit), keeping the scenario "Own caption text and unit in a map tooltip";
  - `improve-stat-cards` copies its `dashboard-indicators` "Stat tile" block again from the text this change leaves (the widget's own unit, scenario "Stat tile with its own unit"); its sentence that the tile says what it plots under the number needs "unless the widget hides its caption", and its smaller figure lets a 3×2 tile with texts show its label again;
  - `add-locale-formatting` rebases on `dashboard-indicators` "Indicator formatting", which now carries the widget's own unit.
- P2 `add-dashboard-sections` answers design.md's open question on line breaks and Markdown in footnotes, and P3 `improve-widget-export` lays the texts and the source out at its fixed width.
