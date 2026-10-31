# Tasks

**Order.**
- Groups 1 to 7 (api) come first, in order; each ends with its tests green.
- Groups 8 to 15 (ui) follow, in order; group 8 keeps the UI build and suite green from the first type change. Groups 11 to 14 can be done in any order after group 10.
- Group 16 (docs) can run any time after group 3. Group 17 is last.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Every new email text goes into both languages of `ReportText`.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers; filter by class while working (`--filter "FullyQualifiedName~IndicatorQueryTests"`).
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.

## 1. Schema (api)

- [ ] 1.1 Add `tamiza-api/src/dbUp/Scripts/0017_add_two_dimension_widgets.sql` as design D1: drop `ck_dashboard_widgets_chart_type` and add it again with `stat`, `bar`, `column`, `line`, `histogram`, `pie`, `map`, `heatmap`, `pyramid`, `likert` and `crosstab`. Keep upper-case `CHECK`, `FOREIGN KEY` and `PRIMARY KEY` out of its comment. Verify the analytics integration tests still build their template database.
- [ ] 1.2 Mirror the CHECK in `TamizaDbContext` (`:430-431`). Verify `SchemaDriftTests` passes.
- [ ] 1.3 Add `The_two_dimension_widgets_script_widens_one_check` to `ScriptConventionTests`, in the style of the 0014 test. Verify it passes.
- [ ] 1.4 Add a `SchemaMigratorTests` upgrade test from `0016`: seed a dashboard with a column chart and a map, then `MigrateAsync`. Verify their `chart_type`, `config` and `updated_at` are unchanged, a `heatmap` widget can be inserted, and `radar` is still refused.

## 2. Kinds, config and validation (api)

- [ ] 2.1 Add the four kinds, `TakesSeries` and `NeedsSeries` to `ChartTypes`, and `SeriesLayouts`, `LikertSides`, `WidgetPyramid` and `LikertStep`; add `SeriesBy`, `Layout`, `Pyramid` and `Scale` to `WidgetConfigInput`, `Normalize()` and `WidgetConfig` (design D1). Verify unit tests: a config saved before has no series; each field survives the JSON round trip that `ReadConfig` and the PATCH use; an empty `seriesBy` normalizes to none.
- [ ] 2.2 Add the rules of design D2 to `CheckConfig`. Verify `IndicatorValidationTests` with one case per row of D2's table, each naming its key (`config.seriesBy`, `config.layout`, `config.pyramid.right`, `config.scale[2]`…), and that every existing case still passes.
- [ ] 2.3 Check the series column in `CheckAgainstColumns`, and collect a missing one into `missing.columns` for the query. Give the new kinds their default sizes (D1). Verify `Series column removed by a script change` (the widget is stale and no SQL runs) and the sizes.
- [ ] 2.4 Make the query endpoint reject an unknown `chartType` with an error on it, and `DefaultSize` switch over every kind with no default branch (design D6). Verify a unit test that iterates `ChartTypes.All` and an integration test for the unknown kind (400).

## 3. Query with a series (api)

- [ ] 3.1 Move `NaturalGroupOrder` from `ReportDataBuilder` to `Indicators/NaturalOrder.cs` and use it from both. Verify the existing report tests pass unchanged.
- [ ] 3.2 Add `IndicatorQueryBuilder.PlanSeries` (design D3, parts 1 and 2): the domain over the row filter and the widget's filters only, the fold into "Other" past the kind's limit, `kind` and `key`, `GROUPING SETS`, the same filter parameters as `Plan`. Verify `IndicatorQueryBuilderTests`: the domain has no dashboard filter parameter; nothing folds at the limit and the smallest fold past it; the missing value never folds; a ratio's totals use `sum(num) / nullif(sum(den), 0)`.
- [ ] 3.3 Give the pyramid and the Likert widget their fixed domains, the `included` condition and the `leftOut` count. Verify unit tests for the SQL and integration tests for `Age bands by sex`, `Third value and missing values left out` and `Value outside the scale`.
- [ ] 3.4 Rank groups and apply the limit (design D3, part 3): the widget's order by group total or by `ColumnSortKey`, the group column's own order otherwise, natural order for a pyramid; the limit counts cells; the kept groups' totals are always returned. Verify `Stacked bars ordered by their totals`, a sort column with a series, `Row limit` counting pairs, and that a widget without a series still runs `Plan` with the same SQL as before (`IndicatorQueryBuilderTests` unchanged).
- [ ] 3.5 Add the series reader and the response fields (`series`, rows' `series` and `seriesKind`, `totals`, `leftOut`), left out of the JSON for a widget without a series. Verify `IndicatorQueryTests`: `Rows that share a group and a series summed`, `Ratio totals`, `Ten series in a stacked chart`, `Heat map within its limit`, `Natural order`, `Colors kept under a dashboard filter` (the same `series` with and without the filter), `Pyramid side with no rows`, and that the response of every existing widget is byte-for-byte as before.

## 4. Series table (api)

- [ ] 4.1 Add `Indicators/SeriesTable.cs` (design D4): rows, columns, cells, totals, the shares of `percent` (negative cells left out and counted) and of Likert, a pyramid's columns, and the notes. Write the JSON fixtures at `tamiza-ui/src/app/pages/projects/indicators/testing/series-tables.json` (ratio totals, "Other", "(none)", percent with a negative cell, pyramid, Likert) and read them from the API's unit tests with `FindUp`, skipped when the UI is not next to them. Verify every fixture.

## 5. Emails (api)

- [ ] 5.1 Make `ReportDataBuilder` switch over every kind with no default branch, and build a widget with a series from `SeriesTable` (design D8). Verify `ReportDataBuilderTests`: a test over `ChartTypes.All`, and the tables of a stacked chart, a 100% chart, a heat map, a pyramid (`Population pyramid` of `dashboard-reports`), a Likert widget and a cross table.
- [ ] 5.2 Show at most 6 series columns and the "Total" column in the HTML and plain-text blocks, with "N more columns", and add the new `ReportText` strings. Verify `ReportHtmlTests` (`Wide cross table`), the plain-text test and the 80 KB budget test with 50 wide widgets.
- [ ] 5.3 Write the whole cross table in each widget's sheet, with shares and totals as numbers. Verify `ReportWorkbookTests` (`Shares of a Likert widget`, `Stacked columns`) and a `ReportSenderTests` send of a dashboard holding one widget of each new kind and layout.

## 6. MCP (api)

- [ ] 6.1 Describe `seriesBy`, `layout`, `pyramid`, `scale` and the four kinds in `query_indicator`, `create_widget` and `update_widget` (design D10). Verify `McpDashboardToolsTests`: `Pyramid through MCP`, `Invalid Likert scale through MCP` (`mcp.validation` with the key) and `McpToolCatalogTests`.
- [ ] 6.2 Return the `SeriesTable` from `query_indicator` and `get_widget_data` for a widget with a series, with `cells` cut first by the result budget. Verify `Checking a stacked chart`, a Likert widget's shares, and a cut result naming its hint.
- [ ] 6.3 Update `Mcp/Instructions.md` and `docs/mcp.md`. Verify `McpGuideTests` (the instructions name the new kinds) and that every tool is still named in `docs/mcp.md`.

## 7. Roles and OpenAPI (api)

- [ ] 7.1 Extend `IndicatorRoleMatrixTests` with a widget of each new kind (a viewer reads it and cannot create, change or remove it) and the OpenAPI test with the new config and response fields. Verify the full `dotnet test` run is green.

## 8. Types and kinds (ui)

- [ ] 8.1 Extend `indicators.service.ts` (the eleven `chartTypes`, `needsGroup`, `takesSeries`, `needsSeries`, the config fields and the response fields), `queryFor` and `savedConfig` (design D7), `grid-layout.ts` default sizes, `indicators.chartTypes.*` strings, and make the widget view's chart choice switch over every kind (design D6). Verify `query-for.spec.ts` per kind, a spec that iterates `chartTypes`, and `ng build`.

## 9. Series table, legend and keyboard (ui)

- [ ] 9.1 Add `series-table.ts`, the twin of `SeriesTable`. Verify a spec that reads the same `series-tables.json` and gives the same rows, totals, shares and notes.
- [ ] 9.2 Add a series legend component and one helper for tooltips and keyboard on marks (design D5): `ChartTooltips` on hover and focus, one Tab stop, arrows, Home, End and Escape, and the `aria-live` announcement. Verify specs for the legend (swatch next to the name in text colors) and the helper (moves, announcement, Escape returns focus).

## 10. Bars, columns and lines by series (ui)

- [ ] 10.1 Draw grouped, stacked and 100% bars and columns in `axis-chart.ts`, with negatives below the axis when stacked, the 2-pixel gaps, the legend and the helper of 9.2. Verify `chart-widgets.spec.ts`: `Grouped columns`, `Stacked to 100%`, `Stacked bars ordered by their totals`, a negative value stacked and a negative value left out of a 100% bar; and that `indicator-view.spec.ts` (the script editor's reuse of `AxisChart`) passes unchanged.
- [ ] 10.2 Draw one line per series with gaps. Verify `One line per series` and `Gap in a line`.

## 11. Heat map (ui)

- [ ] 11.1 Add `heatmap-chart.ts` (design D5): cells on `sequentialColor`, the min-to-max legend, empty cells hatched and outlined, labels only where they fit, and the helper of 9.2. Verify `Cells shaded`, `Empty cell`, keyboard focus through the cells, and dark mode's reversed ramp.

## 12. Population pyramid (ui)

- [ ] 12.1 Add `pyramid-chart.ts`: one scale outward from the center, slots 1 and 2, unsigned labels, the first group at the bottom, the "rows left out" note, the indicator's format. Verify `Age bands by sex`, `Third value and missing values left out`, `Percent indicator` and `Pyramid side with no rows`.

## 13. Diverging Likert bars (ui)

- [ ] 13.1 Add `--tmz-div-neg-1..5`, `--tmz-div-neutral` and `--tmz-div-pos-1..5` to `styles.css`, light and dark (design D5). Validate each arm as an ordinal ramp against the light and the dark chart surface with the palette validator, fix any step that fails, and record the commands and results in the design's Verification Results.
- [ ] 13.2 Add `likert-chart.ts`: bars centered on 0%, negatives nearest-first to the left, the neutral split, positives to the right, shares with values in the tooltips, the "rows left out" note. Verify `Composition around the center` and `Value outside the scale`.

## 14. Cross table, "Show as table" and paper (ui)

- [ ] 14.1 Add `crosstab-widget.ts` from `series-table.ts` (sticky group column, totals, horizontal scroll inside the widget) and use it as the "Show as table" view of every widget with a series. Verify `Contingency table`, `Pair with no rows`, `Stacked chart as a table` and `Likert as a table`.
- [ ] 14.2 Count the columns left out on paper in `TableModel` and print the new kinds with their legends. Verify `dashboard-print.spec.ts`: `Heat map`, `Stacked chart` and `Wide cross table`.

## 15. Widget form (ui)

- [ ] 15.1 Add the series field and the layout radio group to `widget-dialog.ts`, with Stacked and 100% disabled while plotting a ratio and the reason shown. Verify `widget-dialog.spec.ts` ("the fields of each chart type" for the eleven kinds) and that changing the indicator resets the series.
- [ ] 15.2 Add the pyramid's sides and the Likert scale editor, offering the preview's `series` and accepting a typed value, keyboard reachable, with the form's checks mapped to the API's keys. Verify specs for both editors, a refused scale shown on its field, and the preview with a series.

## 16. Documentation

- [ ] 16.1 Write design D11's sections in `docs/indicators-dashboard.md`, `docs/operations.md`, `docs/tamiza-brief.md` (section 6 and the roadmap) and `README.md`. Verify the links and that every kind of `ChartTypes.All` is named in `docs/indicators-dashboard.md` (a unit test, as the MCP tools' docs test).

## 17. Verification

- [ ] 17.1 Real stack in a separate Compose project: indicators `households_by_source` (municipality × water source), `population` (age band × sex, count and percent versions) and an agreement scale by municipality. Build a dashboard with one widget of each new kind and layout (grouped, stacked and 100% columns, lines by series, heat map, pyramid, Likert, cross table) and a dashboard filter. Check them at 390 and 1,280 pixels, light and dark, with a keyboard pass and with a dashboard filter that empties one series.
- [ ] 17.2 Print the dashboard on A4, portrait and landscape: every mark and legend inside its box, the cross table's columns note.
- [ ] 17.3 Measure the query of a stacked column chart with 8 series and of a heat map with 30 over an indicator table of 100,000 rows, and record the times in the design.
- [ ] 17.4 Send the dashboard's scheduled email to Mailpit: the body, the plain text and the workbook, with one sheet per widget and shares as numbers.
- [ ] 17.5 From an MCP client, create a pyramid and a Likert widget and read `get_widget_data`; refuse an invalid scale.
- [ ] 17.6 Write "Deviations from the plan" and "Verification Results" in the design, and run `openspec validate add-two-dimension-widgets`.
