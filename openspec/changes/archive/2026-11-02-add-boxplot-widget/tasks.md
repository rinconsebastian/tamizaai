# Tasks

## 1. Database

- [x] 1.1 Write `tamiza-api/src/dbUp/Scripts/0020_add_boxplot_widget.sql` (design D1):
  - it drops `ck_dashboard_widgets_chart_type` and adds it again with the twelve kinds, in `ChartTypes.All` order with `boxplot` last;
  - mirror it in `Data/TamizaDbContext.cs`, and list the twelve kinds in the stale comment of `Data/DashboardWidget.cs:16`;
  - add a `ScriptConventionTests` case like `The_two_dimension_widgets_script_widens_one_check`, with a literal list of the twelve kinds, exactly one `CHECK`, and no key or index.

  Verify: `ScriptConventionTests` and `SchemaDriftTests` pass.
- [x] 1.2 Add a `SchemaMigratorTests` upgrade case, after the pattern at `:767-820`:
  - apply the scripts through 0019 and seed one widget of each existing kind;
  - migrate;
  - check the rows are unchanged, a `boxplot` widget inserts, and `radar` still fails on `ck_dashboard_widgets_chart_type`.

  Verify: the test passes.

## 2. Box plot kind and query in the API

- [x] 2.1 Config (design D1). Add:
  - the constant `ChartTypes.Boxplot = "boxplot"`, not yet in `All`;
  - `BoxOrientations` (`vertical`, `horizontal`);
  - `WidgetDisplay.Orientation`, `WhenWritingNull`, with a `Description` that names both values in lower case and no enum;
  - `WidgetConfig.MaxBoxOutliers = 100` and `WhiskerRange = 1.5`.

  `WidgetDisplay.Normalize()` returns null only when color, ramp and orientation are all unset, and keeps `"vertical"` as given.

  Verify: `WidgetDisplayTests` still pins the stored text of a config with no orientation, and a new case round-trips `{"display":{"orientation":"horizontal"}}` and `"vertical"` unchanged.
- [x] 2.2 Validation (design D2):
  - `CheckDisplay` refuses an unknown orientation, refuses an orientation on any other kind (skipped when the chart type is null), and accepts a color on a box plot.
  - `CheckSort` accepts a box plot ordered by `value`, refuses `by: "column"` with the key `sort.by`, and keeps "Sorting needs a column to group by.".
  - The message for the other kinds names bar, column and box plot.

  Verify: `IndicatorValidationTests` cover each row of the D2 table, including a box plot with a series refused on `seriesBy` and a ramp refused on `display.ramp`.
- [x] 2.3 Plan the box plot statement (design D3):
  - add a `BoxQueryBuilder` that reuses `Plan`'s measure or ratio, row filter, widget and dashboard filters and missing-column checks;
  - add `IndicatorQueryPlan.Boxes`, and change the "missing" test in `QueryAsync`.

  The SQL:
  - keeps only finite values;
  - groups by the typed group column, with no aggregate over it;
  - joins values to their box by equality, plus a branch for the missing group, and joins the box-level parts by a box number, never with `IS NOT DISTINCT FROM`;
  - computes the mean as `sum / count`, `percentile_cont(ARRAY[0.25, 0.5, 0.75])`, the fences, the whisker ends, the outlier count, and at most `@maxOutliers` outliers farthest from the median;
  - orders by the group column or by the median, then limits to `MaxWidgetQueryRows + 1`.

  Verify: unit tests in `IndicatorQueryBuilderTests` pin the statement's parts for no group, a text group, a true/false group (no `min` over it), a ratio per row, a dashboard filter, both orders and a stale column (no SQL).
- [x] 2.4 Reader and response (design D3, D4):
  - `IndicatorBox` and `ReadBoxesAsync`. It takes the group column's type, so a timestamp group goes through `DataValues.IsoTimestamp`, and it skips a box with a non-finite figure.
  - `IndicatorQueryResponse.Boxes` (`WhenWritingNull`), plus one `IndicatorRow` per box (value = median, rows = number of values), in the same order.
  - The natural re-sort with `NaturalOrder.ByGroup`, or the stable re-sort by median with natural order between equal medians.
  - `rowLimitReached` when more boxes than the limit come back.

  Verify: `IndicatorQueryTests` still pins the JSON of the other kinds byte for byte, and an OpenAPI test after the pattern of `IndicatorOverviewTests.cs:403-430` describes `boxes` and `display.orientation`.
- [x] 2.5 Register the kind and name it where the API tests require. In one step:
  - append `ChartTypes.Boxplot` to `All`, in none of `NeedsGroup`, `TakesSeries` or `NeedsSeries`;
  - `DefaultSize` 6x4;
  - `QueryAsync` plans boxes for `chartType == ChartTypes.Boxplot`;
  - `WidgetQuery.For` keeps `Sort` for a box plot with a group column that orders by value;
  - `IndicatorDocsTests.Names` gains `"Box plot"`, and `docs/indicators-dashboard.md` a "**Box plot**" row in the kinds table of `## Widget types`;
  - `Mcp/Instructions.md` lists `boxplot` on line 10 and gets the "Choosing a widget" bullet of D9 with `boxplot` in backticks;
  - `McpDashboardTools.ChartTypeHelp` names it.

  Update the unit tests that pin the kinds:
  - `IndicatorValidationTests`: twelve kinds, the default sizes, the kinds that cannot be sorted or have a series, the color and ramp lists;
  - `WidgetQueryTests`: sort kept for a box plot only with a group column and by value.

  Verify: the API unit tests pass, `IndicatorDocsTests` and `McpGuideTests` included. The integration test `IndicatorWidgetTests` (default sizes) passes with its box plot row added.
- [x] 2.6 Add `ChartTypes.Boxplot => BoxplotTable(...)` to `ReportDataBuilder`'s kind switch (design D8). Columns, with these formats:
  - `config.GroupBy ?? text["group"]`;
  - Values (`#,##0`);
  - Minimum, Q1, Median, Q3, Maximum and Mean, with `ExcelFormat(BoxFormat(meta.Format))` and the unit;
  - Outliers (`#,##0`).

  A missing group reads "(none)" and the single box "All rows". For a box plot, the row-limit note uses `rowLimitBoxes`. `ReportText` gains, in English and Spanish, `group`, `values`, `minimum`, `q1`, `median`, `q3`, `maximum`, `mean`, `outliers`, `allRows` and `rowLimitBoxes`.

  Add a box plot config to `ReportDataTests.Every_chart_type_builds_a_table_or_a_value`, and a `ReportDataTests` case over a `count` indicator with a unit. Verify:
  - the box case asserts `Columns[2..7].ExcelFormat == "#,##0.00"` with the unit, `Columns[1]` and `Columns[8]` as `"#,##0"`, and the cell values 4.5 and 1200;
  - `ReportTextTests.Both_languages_have_the_same_keys` passes.
- [x] 2.7 Add integration tests over PostgreSQL (for example `IndicatorBoxQueryTests`) through the query endpoint with `chartType: "boxplot"`:
  - the values 1, 2, 3, 4 give 1.75, 2.5 and 3.25;
  - 1, 2, 3, 4, 20 give whiskers at 1 and 4 and one outlier;
  - a fixed set of 50 values matches figures computed beforehand with numpy's default `quantile`, written into the test;
  - a single value gives 1 value, equal figures, equal whisker ends and no outliers;
  - groups by a text, an integer, a true/false (`false` before `true`), a date and a timestamptz column. The timestamp groups must equal a column chart's ISO groups over the same table;
  - a missing group gives a box with `group: null`, and no group column gives one box;
  - a ratio is taken per row, and a zero denominator, a null, NaN and ±Infinity are left out;
  - widget and dashboard filters apply before the statistics;
  - 130 outliers give the 100 farthest and `outlierCount` 130;
  - the default order is natural (`0-4, 5-9, 10-14, 80+`), and ordering by median, ascending and descending, keeps equal medians in natural order;
  - the row limit counts boxes: 40 boxes over 200,000 rows do not reach it, and 8 boxes ordered by median with a limit of 5 keep the 5 largest medians;
  - a removed column makes the widget stale;
  - the 400 errors of D2 come on create and update.

  Verify: the tests pass.
- [x] 2.8 Document the API side:
  - `docs/operations.md`, the `IndicatorDashboard__MaxWidgetQueryRows` row: a box plot counts boxes and reads every row;
  - `docs/indicators-dashboard.md` "Limits": rows a box plot plots (boxes), and 100 outliers drawn per box;
  - `docs/tamiza-brief.md` section 6: `chart_type` with `boxplot`, and `display.orientation` for a box plot;
  - section 5.7's stale list of kinds.

  Verify: `IndicatorDocsTests` still pass, and the documents name the limits as the code has them.

## 3. Scheduled emails

- [x] 3.1 Extend the email tests:
  - `ReportDataTests.Shapes_each_widget_like_its_show_as_table_view_in_reading_order` gets a box plot ordered by median descending, with a "(none)" box, and one with no group column, whose header is "Group" and whose row is "All rows";
  - `ReportHtmlTests` and its plain-text tests get a 9-column box table in the body, with 10 rows per block and "more rows";
  - a box plot that reaches the limit gets the `rowLimitBoxes` note;
  - a `ReportSenderTests` send includes a box plot.

  Verify: the `Reports` unit and integration tests pass.
- [x] 3.2 In `docs/operations.md` "What leaves the deployment by email", say that a box plot's minimum and maximum are single rows' values. Verify: the sentence is there and matches the spec.

## 4. MCP

- [x] 4.1 Add a box payload to `query_indicator` and `get_widget_data` (design D9):
  - `boxes` with the figures, their `formatted` English texts (a `count` as `decimal`) and `outlierCount`, in place of `rows`;
  - no outlier values, and no arrays in the items;
  - the row-limit note says boxes.

  Verify: `McpDashboardToolsTests` cases:
  - preview a box plot, and nothing is saved;
  - read a saved one with `get_widget_data`;
  - cut a long `query_indicator` result in `boxes`, after the pattern of `A_series_result_longer_than_the_size_is_cut_in_its_cells_and_names_the_filter_that_asks_for_less`.
- [x] 4.2 Update the tool texts of design D9:
  - the `query_indicator` and `get_widget_data` descriptions;
  - `create_widget`'s kinds list, its `display` template and its `sort` sentence;
  - the two `config` parameter descriptions;
  - `WidgetConfigInput.Display`'s and `WidgetDisplay.Color`'s `Description`;
  - `DisplayHelp` and `ChartTypeChangeHelp`.

  Update the sentences pinned by `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` in the same step. Verify:
  - `McpToolCatalogTests` passes (that test, no enums, every tool documented);
  - `The_tool_descriptions_name_the_series_settings_and_the_new_kinds`, or a new sibling, checks the box plot words.
- [x] 4.3 Add MCP write tests:
  - `create_widget` for a horizontal box plot ordered by median descending is saved and read back with `get_dashboard`;
  - a box plot with a series, and an orientation on a column chart, are refused with `mcp.validation` and the key, and nothing is saved.

  Verify: the tests pass.
- [x] 4.4 Write the guidance:
  - `Mcp/Instructions.md`: the line-34 sentence names `boxes`, "Colors" names `boxplot`, and line 9 says "one table";
  - `docs/mcp.md`: `boxplot` among the widget kinds (line 81), `display.orientation`, `display.color` for a box plot, and the `boxes` result;
  - update the `docs/mcp.md` sentences pinned by `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config` and `…gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do`, with those tests.

  Verify: `McpGuideTests` and `IndicatorDocsTests` pass.

## 5. Script guide

- [x] 5.1 Make the two changes of design D10 to `Assistant/Prompts/system.md` "The indicator contract", and bump `AssistantPrompts.Version` to 5. Verify: `PromptKitTests` (size bound), the assistant prompt tests and `McpGuideTests` (the guide equals `AssistantPrompts`) pass.
- [x] 5.2 Write `### Preparing an indicator for a box plot` in `docs/indicators-dashboard.md`:
  - a pandas example whose columns are `municipality`, `household_id` as text, and `value`;
  - why the id keeps the rows unique and goes after the dimensions;
  - that the same table also feeds counts, sums and dashboard filters.

  Add a test in `tamiza-analytics/tests` that runs the example through `tz.check(compute, "indicator", "household_income", tables=..., folder=download())`, as `tests/test_sdk_check.py` does. Verify, in the `tamiza-analytics-test:dev` image:
  - `result.dashboard["duplicateRows"] == 0`;
  - `"household_id"` is among the dimensions and not among the measures.

## 6. UI: types and drawing

- [x] 6.1 In `core/projects/indicators.service.ts`, add `QueryBox` and `QueryResult.boxes`, `BoxOrientation` and `WidgetDisplay.orientation`, and `maxBoxOutliers = 100`. Leave the `ChartType` union for group 7. Verify: `npx ng build` type-checks and `indicators.service.spec.ts` passes.
- [x] 6.2 Write the pure `boxplotPlot(...)` in `indicators/boxplot-chart.ts` (design D5):
  - one linear scale over the whisker ends, the means and the outliers drawn, not forced to zero, with a flat domain padded in proportion to its value;
  - bands with the first box on the left (vertical) or at the top (horizontal), and labels truncated as the column and bar charts truncate them;
  - box, median, mean, whiskers with caps, and outliers; a one-value box as a single line;
  - hit areas over each box's box and whisker span, and outlier hit circles;
  - no SVG `transform` attribute.

  Verify: a `boxplot-chart.spec.ts` covers:
  - both orientations;
  - a scale from 40 to 90;
  - a mean below the lower whisker with only high outliers drawn;
  - a single box of the one value 1,000,000, with distinct tick labels;
  - a percent box plot whose only value is 0, with no negative tick;
  - outliers;
  - a long label.
- [x] 6.3 Write the `BoxplotChart` component (design D5). It takes a `ChartColors`, and the formatter from its parent:
  - `observeSize`, `tmz-chart-svg`, and `role="application"` with `aria-roledescription` and the summary;
  - `markTooltips` with one mark per box, arrows across for vertical and down for horizontal, and `keyHelpGroups` or the new `keyHelpGroupsDown`;
  - the box tooltip, and the outliers' own pointer-only tooltip from `ChartTooltips.create()`;
  - the `aria-live` text, the key line, and the "N outliers not drawn" note with its `…One` variant.

  Add the strings it reads, in `en.json` and `es.json`: `indicators.boxplot.*` (figures, key, notes, summary with `…One`, "All rows", "Group") and `indicators.series.keyHelpGroupsDown`.

  Verify with jsdom specs:
  - the tooltip lines in the given formatter;
  - three right arrows in a vertical box plot reach the third box, and two down arrows in a horizontal one reach the second;
  - Home, End and Escape;
  - no key ever lands on an outlier, and pointing at one shows its group and value;
  - the announced text;
  - a box drawn in the color a stub `ChartColors.kept` returns;
  - the note for 130 outliers and for 101 (one not drawn), in English and Spanish;
  - `translations.spec.ts` passes.
- [x] 6.4 Write `boxTableModel` for "Show as table":
  - the group column ("Group" without one) and the 8 figure columns of D6, aligned to the end;
  - "(none)" and "All rows";
  - figures in the formatter it is given, numbers of values and of outliers as whole numbers.

  Add `boxFormat` in `format.ts`. Verify: a spec checks headers, order, and that with `valueFormatter({ format: boxFormat('count'), unit: 'people' })` a median of 4.5 reads `4.50 people` while 1,200 values read `1,200`.

## 7. UI: the kind everywhere

- [x] 7.1 Add `'boxplot'` to the `ChartType` union and to `chartTypes` (last), with `needsGroup` excluding it as it excludes the histogram, and `indicators.chartTypes.boxplot` ("Box plot" / "Diagrama de caja"). In the same step, wire everything the union makes the compiler and the specs ask for:
  - `widget-view.ts`:
    - `@case ('boxplot')` drawing `BoxplotChart` with the box formatter of D6, and `axisKindOf` returning null;
    - `queryFor` with the box plot's sort rule of D7, and `savedConfig`/`displayFor` keeping `display.orientation` and `display.color`;
    - "Show as table" with `boxTableModel`, and the row-limit message `indicators.widget.rowLimitBoxes`;
  - `grid-layout.ts`: 6x4;
  - `chart-colors.ts`: `keepsByValue` for `boxplot`;
  - the specs typed `Record<ChartType, …>` or pinning the list:
    - `indicators.service.spec.ts:305-339`;
    - `query-for.spec.ts`, `grid-layout.spec.ts` and `chart-colors.spec.ts`;
    - the `drawnBy` table and the `plain` predicate of `series-widgets.spec.ts`;
    - `widget-dialog.spec.ts`: `expected`, the option texts and `takesColor`, with the form's fields as they are before group 8.

  Verify: the whole UI suite compiles and passes, apart from the two known polling failures, including `series-widgets.spec.ts:826-833` (the order of the kind strings) and a new `widget-view.spec.ts` case: switching the orientation sends no new query.

## 8. UI: the widget form

- [x] 8.1 In `widget-dialog.ts` (design D7), for a box plot:
  - the group hint and the box plot's ratio hint;
  - the order field "Order boxes by", with the default and "Median" only and the direction select, no "Compare as", and a `column:` key read as the default;
  - the order disabled with its reason while there is no group column.

  Add the strings in `en.json` and `es.json`. Verify with `widget-dialog.spec.ts` cases:
  - Median descending saves `{ by: "value", direction: "desc" }`;
  - a bar with a column order switched to box plot shows the default and no direction;
  - clearing the group after choosing Median previews and saves with no sort, and with no 400.
- [x] 8.2 Orientation and color in the form:
  - the Vertical/Horizontal radios, with the `orientation` signal, `setOrientation` and `displayDraft()` of D7, and the eight color radios;
  - changing the kind away from box plot drops the orientation;
  - `fieldFor` maps `display.orientation`;
  - the spec's `expected` table now holds the box plot's own fields.

  Verify with specs:
  - a box plot form saved unchanged, with or without an orientation, sends the same config;
  - a server error on `display.orientation` shows on the radios;
  - the preview redraws with no query when the orientation changes.
- [x] 8.3 Document the feature:
  - `docs/indicators-dashboard.md`:
    - a `### Box plot` section: what a box is, the method as pandas `quantile`, whiskers and outliers, order, orientation, colors, count figures with two decimals;
    - the exceptions in "What to plot" and "Duplicate rows are summed";
    - sentences in "Show as table", "Widget color and scale", "Colors kept by value", "Bar and column order" and the series table ("Not allowed");
    - the separate sentence on orientation kept by a duplicate, a move and a restored version;
    - the corrected grid sizes;
  - `README.md`: the widget kinds;
  - `docs/tamiza-brief.md`: roadmap item 7m for this change.

  Verify: `IndicatorDocsTests` pass, and every behavior in the deltas of `dashboard-indicators`, `dashboard-colors`, `dashboard-versions` and `custom-dashboards` is described.

## 9. UI: printing and build

- [x] 9.1 Add box plot cases to `dashboard-print.spec.ts`:
  - a box plot prints whole, with its key and note;
  - shown as a table, it prints the rows that fit and says how many were left out.

  Verify: the print specs pass.
- [x] 9.2 Run `npx ng build` and `scripts/check-ui-safety.sh`. Verify:
  - the initial bundle stays under the 675 kB budget, no component style passes 4 kB, and the measured size is recorded in design.md;
  - the safety script, which forbids `innerHTML`, number pipes and stray `.toFixed(`, is clean.

## 10. Integration checks

- [x] 10.1 Run every suite:
  - the whole API suite in the background (unit, then integration, about 40 minutes), with the output saved to a file;
  - the UI suite;
  - the analytics suite in `tamiza-analytics-test:dev`.

  Verify: everything passes, apart from the two known UI polling failures, and the counts are recorded in design.md.
- [x] 10.2 Measure box plot queries through the API over indicator tables of 100,000 and 1,000,000 rows (40 groups, and 500 groups), in both orders, with a ratio and with a dashboard filter, and one over 100,000 one-value groups. Verify: each stays well under the 25-second statement limit, the 100,000-group case returns 5,000 boxes with `rowLimitReached`, and the times are recorded in design.md.
- [x] 10.3 Check the real stack, following the `tamizaverify` recipe in a separate Compose project. Seed a per-household table with long municipality names, a `count` indicator, a ratio, and a true/false and a timestamp dimension. Check:
  - each orientation at 1,280 and 390 px, in the light and the dark theme, with no horizontal page scroll;
  - tooltips, outlier tooltips, the keyboard (one Tab stop, arrows, Home, End, Escape) and the `aria-live` text;
  - "Show as table";
  - a palette, a widget color and a kept color;
  - printing to PDF on A4 in both orientations;
  - the form: order, orientation and errors.

  Verify: findings and fixes are recorded in design.md.
- [x] 10.4 Send a scheduled email with a box plot to Mailpit. Verify: the body's 9-column table and the workbook's numbers and formats match "Show as table".
- [x] 10.5 Run `openspec validate add-boxplot-widget --strict`. Verify: it passes.

## Workflow follow-up

- Sync the delta specs and archive the change once the user has reviewed the verification results.
