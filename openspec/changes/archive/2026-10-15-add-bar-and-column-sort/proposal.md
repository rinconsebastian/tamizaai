# Proposal

## Why

Bar and column widgets order their bars alphabetically by the text of the group values, whatever the group column's type, and an analyst cannot change it. A group column holding `1`, `2` and `10` draws as `1`, `10`, `2`, whether the column is text (indicator scripts are told to return number-like dimensions as text) or integer. Ranges such as `0-4`, `5-9` and `10-14` come out of order the same way.

A chart cannot show "largest first", and a script cannot hand the chart an order column such as `sort_key`. The order the server picks also decides which groups survive the 5,000-row limit. So reordering in the browser cannot fix it; the order must be part of the widget's query.

## What Changes

- **An order for bar and column widgets.** A bar or column widget can keep the default order or be sorted by the **plotted value** (the measure or the ratio) or by **any column** of its indicator's table, ascending or descending. The widget, its "Show as table" view and its row limit all follow that order.
- **Text or number.** A text, integer or decimal sort column can be compared **as text** (`10` before `2`) or **as a number** (`2` before `10`). By default a text column is compared as text and a numeric one as a number. A value that cannot be read as a number counts as empty, and so does `NaN`. Date, timestamp and true/false columns are always compared in their own order.
- **One sort value per bar.** A bar can stand for several rows. When the sort column is not the group column, the bar sorts by the smallest of its rows' values, compared the chosen way. The same rule applies in both directions, so switching the direction reverses the order of bars whose sort values differ.
- **Predictable edges.** Bars with no sort value come last in both directions. Bars with equal sort values keep the default order between them.
- **The default order follows the column's type.** Without an order, bars follow the group column's own type, so integer and decimal group columns sort as numbers. Text, date, timestamp and true/false groups keep their current order. **Existing widgets grouped by an integer or decimal column change order** (`1`, `10`, `2` becomes `1`, `2`, `10`).
- **Stale and invalid orders.** A sort column the table no longer has makes the widget name it, as a removed group column does today. Saving an order on a chart type other than bar or column is rejected. So is comparing a date, timestamp or true/false column as text or as a number.
- **Line charts.** The line chart's requirement drops its unbuilt "or chosen order" wording. A line keeps its natural order and has no order setting.
- **Widget dialog.** For bar and column charts, the dialog gains "Order bars by", "Direction" and "Compare as" controls, and the live preview shows the result.
- **No migration.** A widget saved without an order uses the default order.

Not in this change:
- An order setting for line, pie, histogram or map widgets. Pie slices keep "largest first", and histogram ranges stay in range order.
- A hand-made order (dragging bars, or listing group values one by one). A script that needs a custom order, such as age ranges, publishes a column for it, such as `sort_key`, and the widget sorts by that column.
- Showing only the top or bottom N bars.
- Sorting by an aggregate other than the smallest value, such as the largest value or a sum.
- Reading numbers written with a decimal comma or thousands separators.
- Telling the user how many sort values could not be read as numbers.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `dashboard-indicators`:
  - Bar and column widgets gain an order: the plotted value or a column, ascending or descending, with the column compared as text or as a number.
  - The default order becomes the group column's own type order.
  - The sort column becomes one of the columns a widget can name, and it is checked by the stale-configuration rules.
  - The line chart is pinned to its natural order.

## Impact

- **api:** the `Indicators` module:
  - `IndicatorModels` gains a `sort` object in the widget config (`by`, `column`, `compareAs`, `direction`).
  - `IndicatorValidation` checks the order's shape, chart type and column type.
  - `IndicatorQueryBuilder` qualifies the group column in `ORDER BY`, adds the sort key, a guarded numeric read and a tie-breaker, checks the column's type at query time, and reports a missing sort column.
  - `CleanData/DataQueryBuilder.Describe` becomes shared.
  - Unit and integration tests are added.
  - The OpenAPI document shows the new config field.
- **Database:** no migration. `dashboard_widgets.config` is opaque `jsonb`, so a saved widget without `sort` reads as the default order.
- **UI:**
  - `indicators.service.ts`: the `WidgetSort` and `WidgetConfig` types and `emptyConfig`.
  - `widget-view.ts`: `queryFor`, which keeps `sort` for bar and column only and sets it to null otherwise.
  - `widget-dialog.ts`: the three controls, how a saved order is loaded into them, their defaults, the reset on a new indicator, and server error mapping.
  - `script-editor/indicator-view.ts`: its `WidgetConfig` literal gains `sort: null`.
  - `en.json` strings.
  - Dialog, service and chart specs.
- **Docs:** `docs/indicators-dashboard.md`: the widget types table, a section on bar order, and the default order.
- **Configuration and dependencies:** none new. The guarded numeric read uses `pg_input_is_valid` (PostgreSQL 16 or later), which the performance dashboard already relies on.
