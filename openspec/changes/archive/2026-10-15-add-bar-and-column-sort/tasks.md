# Tasks

Groups 1 and 2 (api) come before group 3 (ui). Group 4 can be written at any point. Group 5 comes last. API tests run with `dotnet test` from `tamiza-api/`; the integration tests need Docker. UI tests run with `npm test -- --watch=false` from `tamiza-ui/`.

## 1. Widget config and validation (api)

- [x] 1.1 Add `WidgetSort(By, Column, CompareAs, Direction)` with string constants (`value`/`column`, `text`/`number`, `asc`/`desc`) and `Sort` to `WidgetConfigInput` and `WidgetConfig`, with `Normalize()` turning an empty `column` or `compareAs` into null (design D1).
  Verify:
  - `IndicatorValidationTests.A_config_without_anything_takes_the_defaults` lists `Sort` as null.
  - A new `IndicatorWidgetTests` case, next to `The_whole_configuration_is_saved_and_returned_as_sent` (a histogram, which cannot carry an order), saves a column widget with `sort: { by: "column", column: "municipality", compareAs: "text", direction: "desc" }` and gets the same `sort` back from the create response and from the overview.
  - A config stored without `sort` (inserted as raw `jsonb`) reads as `sort: null`.
  - The OpenAPI document describes the `sort` object.
- [x] 1.2 Add the `sort` checks to `IndicatorValidation.CheckConfig` and `CheckAgainstColumns`, and make `DataQueryBuilder.Describe` `internal` for the shared type words (design D6).
  Verify unit tests in `IndicatorValidationTests`, one per message, pinning the exact text:
  - a chart type other than bar or column;
  - no group column;
  - a bad `by`;
  - a missing or bad direction;
  - a column required, and a column given with `by: value`;
  - a bad `compareAs`, and a `compareAs` given with `by: value`;
  - an unknown column;
  - a `compareAs` on a date, a timestamp and a true/false column, each with its own type words.

  Verify integration tests for:
  - the scenarios `Only bar and column widgets have an order` and `Date column compared as a number`;
  - a PATCH of only `chartType` to `line` on a sorted widget, rejected on `config.sort`;
  - a PATCH that only moves a sorted widget, accepted with its `sort` kept.

## 2. Sorted query (api)

- [x] 2.1 In `IndicatorQueryBuilder.Plan`, for the grouped path (design D2 to D6):
  - qualify the group column in `ORDER BY` with the table in `FROM`;
  - build the sort key and `ORDER BY <key> <dir> NULLS LAST, <qualified group> NULLS LAST`;
  - add `Need(sort.Column)` after the filter columns;
  - when the sort column exists, is a date, timestamp or true/false column and `compareAs` is set, return the D6 error under `sort.column` with no SQL. When the column is missing, skip the check so it is reported in `missing.columns`.

  Verify `IndicatorQueryBuilderTests` assert:
  - the `ORDER BY` of a widget without an order uses the qualified group column;
  - the key for the plotted value, and for every row of D2's table (including `NULLIF(C, 'NaN')` for decimal, `bool_and` for true/false and `NULLIF(…, '')` for text);
  - the guarded cast sits inside `min()`, with the bracket-class pattern and `pg_input_is_valid`;
  - the qualified group tie-breaker;
  - the missing-columns list ends with the sort column;
  - the type error for each of the three types.
- [x] 2.2 Add integration tests against PostgreSQL to `IndicatorQueryTests` (design D2 to D6). Cover the scenarios:
  - `Default order`, with an integer and a decimal group column (today's text order would fail both)
  - `Missing group value last`
  - `Widget without an order` (a raw-`jsonb` widget without `sort` queried through its saved config)
  - `Largest value first`
  - `Ordered by another column`
  - `Several values in one bar`
  - `Several values in one bar, descending`
  - `Bars without a sort value come last`
  - `Equal sort values keep the default order`, plus a case where integer groups 2 and 10 tie on the key and come out 2, 10
  - `Row limit follows the order` (with `MaxWidgetQueryRows` lowered through settings)
  - `Text column compared as a number`
  - `Text column compared as text`
  - `Signs and decimals`
  - `Text that is not a number`, with a sort column also holding `''`, `NaN`, `Infinity`, `1,5`, `1_000`, `1e1000000` and a value padded with U+2003, all last and none answering 500
  - `Number column compared as text`
  - `Date column in date order`
  - `Sort column removed by a script change`, with the column in `missing.columns`

  Also cover:
  - a true/false sort column, false first when ascending;
  - a decimal (`double precision`) sort column whose group holds only `NaN`, last when descending;
  - an empty-text group value, first in the default order and before "(none)";
  - a `compareAs: number` order whose column has become a date column, answering 400 under `sort.column`.

## 3. Widget dialog (ui)

- [x] 3.1 Add `WidgetSort` and `WidgetConfig.sort` to `indicators.service.ts` and `sort: null` to `emptyConfig`. Add the `sort` rules to `queryFor` in `widget-view.ts`, setting `sort` to null rather than omitting it. Add `sort: null` to the `WidgetConfig` literal in `script-editor/indicator-view.ts` (design D7).
  Verify:
  - The build passes.
  - The literal expectations in `indicators.service.spec.ts` (the `emptyConfig` case) and `widget-dialog.spec.ts` (the line-chart create body) gain `sort: null`. Bodies built with `config()` from `indicators-testing.ts` pick it up through `emptyConfig`.
  - New cases show `queryFor` keeps `sort` for bar and column.
  - It sets `sort` to null for line, pie, stat, histogram and map.
  - It sets `column` and `compareAs` to null when `by` is `value`.
- [x] 3.2 Add the "Order bars by", "Direction" and "Compare as" controls to `widget-dialog.ts`, with:
  - signals started from `start.sort` and the separate `sortColumns` list;
  - the direction and compare-as defaults, and the compare-as draft rules;
  - the reset in `pick()`;
  - the `sort` problem, `Problems.sort` and the `fieldFor` mapping;
  - the `indicators.dialog.sort*` strings in `en.json` (design D7).

  Verify `widget-dialog.spec.ts`:
  - The controls are shown only for bar and column.
  - "Order bars by" offers "Default order", "Plotted value" and every column of the table (scenario `Extra column sorted on`).
  - A saved sort column the table no longer has is kept in front of the sort list only, not in group-by or the filter columns.
  - Editing a saved widget shows its order in all three controls: `by: value`, a column whose `compareAs` is null (shows the type's default), and a stale column (selected).
  - The direction moves to descending for the plotted value and to ascending from there to a column, and is kept when moving between columns.
  - "Compare as" appears only for text, integer and decimal columns and is preselected from the type. Moving from a text column to a date column hides it and sends `compareAs: null`. A saved `compareAs: number` survives a save made before the columns load. Use a `catalog({ columns: [...] })` override with date and true/false columns.
  - The preview stays idle until an order by column has a column.
  - The save body carries the chosen `sort`.
  - The preview is re-queried with the order.
  - Picking another indicator resets the order.
  - A server error on `config.sort.column` is shown under "Order bars by" (`#widgetSortBy-error`), and the control is marked invalid.
- [x] 3.3 Confirm bar and column rendering keeps the server's order (design D2).
  Verify `chart-widgets.spec.ts`:
  - A sorted bar chart and a sorted column chart draw their rows in the order the query returned them: first bar at the top, first column on the left (scenario `Largest value first`).
  - Their "Show as table" view lists the same order (requirement "Bar and column charts of one indicator", last paragraph).
  - An unsorted column chart's table lists the server's order (scenario `Default order`).

## 4. Docs

- [x] 4.1 Update `docs/indicators-dashboard.md` (design D8): the bar and column rows of the widget types table, the default order, and a "Bar and column order" section. Verify that the examples match the spec scenarios (`1`, `2`, `10`; `n/a` last) and that the links resolve.

## 5. Stack verification

- [x] 5.1 Before deploying, save a column chart grouped by an integer column. Then, in a separate Compose project (`tamizaverify`), publish an indicator whose table has:
  - a text `round` column holding `1`, `2`, `10`, `n/a` and an empty text;
  - an integer `sort_key` column with two values for one group;
  - a date column.

  Add column charts:
  - ordered by the plotted value, descending;
  - ordered by `round` as a number and as text;
  - ordered by `sort_key`.

  Then remove `sort_key` in a script change and run again.

  Record in the design's Verification Results:
  - the orders drawn;
  - that the older widget now draws its integer groups in numeric order;
  - that no request answered 500;
  - the stale message naming `sort_key`.
