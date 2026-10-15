# Design

## Context

- **Widget config and storage.**
  - `WidgetConfigInput` and `WidgetConfig` (`Indicators/IndicatorModels.cs`) hold `indicator`, `measure`, `ratio`, `groupBy`, `filters`, `geometryColumn` and `buckets`.
  - They are stored as opaque `jsonb` in `dashboard_widgets.config`.
  - The endpoints serialize them with `JsonSerializerDefaults.Web`, which writes camelCase, ignores unknown properties and has no enum converter (`IndicatorEndpoints.cs`).
  - Reads go through `ReadConfig` and `Normalize()`.
  - Every PATCH re-serializes the config. A PATCH with only a layout re-serializes the stored one; a PATCH with a config replaces it whole.
- **Query.**
  - `IndicatorQueryBuilder.Plan` quotes every column through the table catalog.
  - For bar, column, line, pie and map widgets it builds a grouped query: `SELECT "<group>"::text, <aggregate> … FROM "<schema>"."<table>" … GROUP BY "<group>" HAVING <aggregate> IS NOT NULL ORDER BY "<group>" NULLS LAST LIMIT @limit`.
  - In `ORDER BY`, PostgreSQL matches a bare name against output columns first. The bare `"<group>"` therefore resolves to the `"<group>"::text` output column, not to the table column. So today every group column sorts by its text, and an integer group of 10, 2, 1, -5 and -10 reads `1, -10, 10, 2, -5` (reproduced on PostgreSQL 17).
  - The order runs before the `MaxWidgetQueryRows + 1` limit. `ReadRowsAsync` stops at the limit and reports it.
  - `Need()` collects missing columns in this order: group, geometry, measure, ratio, filters.
- **Validation.**
  - `IndicatorValidation.CheckConfig` checks a config's shape. `CheckAgainstColumns` checks it against the catalog.
  - On the widget endpoints both run, with paths prefixed `config.` (`config.groupBy`, …), and errors are returned as a 400.
  - The query endpoint runs only `CheckConfig`, with no prefix (`groupBy`, …), and `Plan` adds its own type checks, such as a text measure under `measure`. Those are a 400 too. The widget view then shows the first field error in its error alert.
  - A column the table no longer has is not an error at query time. It is listed in `missing.columns` with a 200.
- **Failure mode to avoid.** `DataDashboard.InTransactionAsync` maps only timeouts. Any other PostgreSQL error, such as `22P02` from `'n/a'::numeric`, becomes a 500.
- **Engine.**
  - PostgreSQL 17 (`postgis/postgis:17-3.5` in `compose.yaml` and in the integration tests' `PostgresFixture`), with the `en_US.utf8` collation.
  - `PerformanceSql` already uses `pg_input_is_valid`.
  - SQL regex precedent: `PerformanceSql.DateShape` uses bracket classes, not backslash escapes.
- **UI.**
  - The widget dialog starts one signal per field from `start` (the saved config or `emptyConfig`). It uses native `<select tmzInput>` with `[selected]` options inside `tmz-form-field`.
  - `allColumns()` lists the catalog's columns with a stale saved `groupBy` kept in front. It feeds the group-by and filter-column selects.
  - `queryFor` (`widget-view.ts`) whitelists which config fields each chart type sends. It sets the fields a type does not use to null, and it feeds the preview, the save body and the re-query key.
  - `AxisChart` keeps the server's row order for bar and column (`pivot(rows, …, 'listed')`), and its "Show as table" view follows it.
  - The line chart re-sorts with `naturalCompare`, an `Intl.Collator` with `numeric: true`. That collator is not a true numeric sort: it orders `-5` before `-10` and `1.5` before `1.10`.
- **Precedent.** The clean data sort (`CleanData/DataQueryBuilder.cs`) validates `{column, direction}` with the messages "The table has no column 'x'." and "The direction must be 'asc' or 'desc'.". It sorts `NULLS LAST` in both directions. Its `ctid` tie-breaker cannot be used in a grouped query.

Requirements are in `specs/dashboard-indicators`. The user did not specify the following; this proposal decided them, and they can be revised:
- The plotted value is offered as a sort key next to the columns, because sorting by the measure column itself would compare its smallest row value, not the bar's height.
- A bar's sort value is the smallest value of the sort column over its rows, in both directions.
- A text value that cannot be read as a number counts as no value, so its bar goes last. It does not fail the widget.
- Date, timestamp and true/false columns are compared in their own order only.
- The default order is fixed to follow the group column's type (D2). Widgets grouped by an integer or decimal column change order.

## Goals / Non-Goals

**Goals:**
- No data in a sort column can turn the query into a server error.
- One SQL path for every sort key and column type, with the default order as the tie-breaker.
- No migration. A widget saved without an order uses the default order.
- The query response stays `{group, value, geometry}` rows. The order is carried by the row order.

**Non-Goals:**
- A natural default order for text group columns (`2` before `10` in a text column). That is what "compare as a number" is for.
- Sending the sort value to the browser, or sorting in the browser.
- Telling the user how many values could not be read as numbers.

## Decisions

### D1. The `sort` field of a widget config (api, ui)

```json
"sort": null
"sort": { "by": "value",  "column": null,    "compareAs": null,     "direction": "desc" }
"sort": { "by": "column", "column": "round", "compareAs": "number", "direction": "asc" }
```

- `WidgetConfigInput` and `WidgetConfig` gain `Sort` (`WidgetSort(string? By, string? Column, string? CompareAs, string? Direction)`).
- The values are string constants, as `ChartTypes` is: `value`/`column`, `text`/`number` and `asc`/`desc`. They are not C# enums, because the endpoints' JSON options have no enum converter. An enum would be stored as a number while the API exposes a string.
- `Normalize()` turns an empty `column` or `compareAs` into null. A missing `sort` stays null.
- A stored config without the property reads as `sort: null`, which means the default order. No DbUp script is needed.
- `compareAs: null` means "the column's own way": text as text, integer and decimal as numbers, and date, timestamp and true/false in their own order.

*Alternative:* a column name with a reserved value, such as `"@value"`, for the plotted value. Rejected: a ratio is not a column, and a published column may have any name.

*Alternative:* reuse `DataSort {column, direction}` from the clean data module. Rejected: it has no plotted value and no comparison. Its direction words and message are reused instead.

*Alternative:* a type override on the indicator's metadata, so that a column would always be compared as a number. Rejected: the request is per widget, and a column-type layer edges toward the semantic layer that is out of scope for the MVP.

### D2. The order is computed in SQL, before the limit (api)

`Plan` changes only the grouped path's `ORDER BY`. The group column in `ORDER BY` is qualified with the table already named in `FROM` (`"<schema>"."<table>"."<group>"`), so it is the table column and not the `::text` output.

- Without an order: `ORDER BY "<schema>"."<table>"."<group>" NULLS LAST`. This is the group column's own type order.
- With an order: `ORDER BY <key> ASC|DESC NULLS LAST, "<schema>"."<table>"."<group>" NULLS LAST`.

The tie-breaker is the default order, ascending in both directions. Group values are unique per output row, so the order is total and stable. `<key>` depends on the sort:

| Sort | Column type | Compare as | Key |
|---|---|---|---|
| plotted value | — | — | the aggregate already in the SELECT: `sum(M)::double precision` or the ratio |
| column | integer | number (or null) | `min(C)` |
| column | decimal | number (or null) | `min(NULLIF(C, 'NaN'))` |
| column | integer, decimal | text | `min(NULLIF(C::text, ''))` |
| column | text | text (or null) | `min(NULLIF(C::text, ''))` |
| column | text | number | `min(CASE WHEN <readable> THEN C::text::numeric END)` (D4) |
| column | date, timestamp | null | `min(C)` |
| column | true/false | null | `bool_and(C)` (PostgreSQL has no `min(boolean)`; `bool_and` is false when any value is false) |

- Inside an aggregate, `C` always means the table column, so the key needs no qualification.
- The same aggregate is used when the sort column is the group column. One value per group makes it the value itself, so there is no special case.
- The ratio is never NULL in an output row, because `HAVING … IS NOT NULL` already removes it.
- The per-row path (a histogram without a group) and the stat tile are untouched. They never carry a sort (D6).
- `AxisChart` needs no change: bar and column already draw in row order, and so does their table.
- Line and pie re-sort in the browser and are unaffected. Map draws regions with no order.

*Alternative:* keep the bare `"<group>"` and write today's text order into the spec. Rejected: it would make `1, -10, 10, 2, -5` a requirement for integer groups.

*Alternative:* alias the select's output (`AS "group"`). Rejected: a group column literally named `group` would bind to the output again. Qualifying with the table cannot collide.

*Alternative:* sort in the browser. Rejected: a query row carries only the group, the value and the geometry, so the browser cannot see the sort column. Also, the server's `LIMIT` would already have kept the wrong groups.

*Alternative:* return the sort value in each row. Rejected: the row order already says everything the chart needs.

### D3. One sort value per bar: the smallest (api)

A bar stands for every row of its group, so a sort column other than the group column has several values per bar. Using the smallest value in both directions keeps two properties:
- Switching the direction reverses the order of bars whose values differ.
- A script's own order column (`sort_key`), which normally holds one value per group, behaves the same under any aggregate.

*Alternatives:*
- `max` for descending. Rejected: switching the direction would then no longer reverse the bars, which is surprising. The scenario "Several values in one bar, descending" tells the two rules apart.
- Requiring one value per group and reporting otherwise. Rejected: it needs a `count(DISTINCT …)` per group and a new error state, for a setting that is only about presentation.
- `sum`. Rejected: it is meaningless for codes and dates, and the measure is already the plotted value.

### D4. Reading text as a number never fails (api)

`<readable>` requires two conditions:

```
C::text ~ '^[[:space:]]*[-+]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][-+]?[0-9]+)?[[:space:]]*$'
AND pg_input_is_valid(C::text, 'numeric')
```

- **The pattern** is the spec's shape rule.
  - It rejects `NaN`, `Infinity`, `1_000`, `0x1F` and `1,5`, even though PostgreSQL 16 and later would accept some of them as `numeric`.
  - It uses bracket classes, as `PerformanceSql.DateShape` does, so its digits are ASCII under any locale provider. It also reads the same whatever `standard_conforming_strings` is set to: with backslash escapes and that setting off, `\d` silently becomes `d`.
- **`pg_input_is_valid`** is the spec's "fits the database's numbers" rule. It catches what a pattern cannot:
  - an exponent or a digit count outside `numeric`'s range (`1e1000000`, `1e-16384`);
  - a non-ASCII space that `[[:space:]]` matches under the database locale but `numeric` rejects (U+2003).
- **The cast.** Only then is the value cast with `::numeric`. `double precision` is not used, because it overflows on `1e400`.

The `CASE` sits inside `min()`. The cast therefore runs only on guarded rows, and the aggregate ignores the NULLs. The pattern is a constant in the SQL, so no user text is spliced in.

A value that fails the guard is NULL, so it goes last through `NULLS LAST`. A decimal column's `NaN` is turned into NULL by `NULLIF` for the same result. Otherwise it would sort above every number and come first when descending.

*Alternatives:*
- A plain `::numeric` cast. Rejected: the first `n/a` raises `22P02`, which becomes a 500.
- The browser's numeric collator. Rejected: it gets signs and decimals wrong (spec "Signs and decimals"), and it runs in the browser (D2).
- A pattern alone. Rejected: `1e1000000` matches and then raises `22003`.
- A PL/pgSQL function with an exception block. Rejected: it adds a database object and a migration, and it is slower per row.

### D5. Comparing as text (api)

- Text order uses the database's default collation, the same as the default order and the clean data sort, so the three agree.
- `NULLIF(…, '')` makes an empty text count as no value, as the clean data filters already treat it, so it goes last.
- An integer or decimal column compared as text uses PostgreSQL's text output, so `10` comes before `9`. A `numeric(…, 2)` column prints `2.50`, and a decimal `NaN` prints `NaN` and is compared as that text.

*Alternative:* `COLLATE "C"`. Rejected: byte order would put `Álamo` after `Zarzal` and uppercase before lowercase, unlike every other text order in the dashboards.

### D6. Validation and stale columns (api)

`CheckConfig` runs without the table. Its paths are under `config.sort` on the widget endpoints and under `sort` on the query endpoint:
- `sort`: "Only bar and column widgets can be sorted." when the chart type is known and is neither bar nor column. "Sorting needs a column to group by." when `groupBy` is null; this case is reachable from the query endpoint, whose `chartType` is optional.
- `sort.by`: "Sort by 'value' or by 'column'."
- `sort.direction`: "The direction must be 'asc' or 'desc'." It is required.
- `sort.column`:
  - "Choose a column to sort by." when `by` is `column` and no column is given.
  - "Sort by the plotted value or by a column, not both." when `by` is `value` and a column is given.
  - The 500-character name limit, as for every column name.
- `sort.compareAs`:
  - "Compare as 'text' or as 'number'." for any other value.
  - "The plotted value is always compared as a number." when `by` is `value` and `compareAs` is given.

The type check is the same on both paths. It runs in `CheckAgainstColumns` on create and on an update that changes the config, and in `Plan` at query time:
- When the column is missing:
  - On create and update, `config.sort.column` gets "The table has no column 'x'.".
  - At query time, `Need(sort.Column)` lists it in `missing.columns` after the filter columns, so the existing order of that list is unchanged. A missing sort column makes the widget stale, as a missing group column does.
- When the column is a date, timestamp or true/false column and `compareAs` is set: "Column 'x' is {a date column | a timestamp column | a true/false column}, so it is sorted in its own order and cannot be compared as text or as a number." It goes under `config.sort.column` on the widget endpoints and under `sort.column` at query time, where `Plan` returns no SQL, as it does for a text measure.

The type words come from `DataQueryBuilder.Describe`, which becomes `internal` so both modules share them.

A PATCH that changes only `chartType` re-runs `CheckConfig` on the stored config. Turning a sorted bar chart into a line chart through the API alone is therefore rejected with the `config.sort` error. The dialog never hits this, because `queryFor` sets `sort` to null for other chart types.

*Alternative:* ignore a missing sort column and fall back to the default order. Rejected: every other missing column is named, and a silent reorder would mislead in the same way a silent row cut would.

### D7. Widget dialog (ui)

**Types (`indicators.service.ts`).**
- Add `WidgetSort { by: 'value' | 'column'; column: string | null; compareAs: 'text' | 'number' | null; direction: 'asc' | 'desc' }` and `WidgetConfig.sort: WidgetSort | null`.
- `emptyConfig.sort` is `null`.
- `script-editor/indicator-view.ts` builds a `WidgetConfig` literal and gains `sort: null`.

**Query (`widget-view.ts`).**
- `queryFor` keeps `sort` for bar and column and sets it to `null` for every other chart type. It sets the key to null rather than omitting it, as it does for `groupBy` on a stat tile, so test bodies built from `emptyConfig` still match.
- When `by` is `value`, `queryFor` sets `column` and `compareAs` to null.
- The preview, the save body and the widget's re-query key therefore all carry the order.

**Controls.** Three controls go in the dialog's existing `grid gap-4 sm:grid-cols-2` block, next to the group column, for bar and column only. Each is a `<select tmzInput>` in a `tmz-form-field`, like the other fields:
- **Order bars by** (`#widgetSortBy`, `dialog-sort-by`).
  - Options: "Default order", "Plotted value", then every column.
  - The columns come from a separate `sortColumns = computed(() => columnsWith(<catalog names>, [sortColumn()]))`. A saved sort column the table no longer has is therefore kept in front of this list only, and never added to the group-by or filter selects. `allColumns()` is not reused, because it would offer a stale group column instead.
  - Option values distinguish the plotted value from a column of any name: `value`, and `column:` followed by the name, parsed at the first colon.
- **Direction** (`#widgetSortDirection`, `dialog-sort-direction`).
  - Options: "Ascending" and "Descending". Shown once an order is chosen.
  - Moving to "Plotted value" sets it to descending (largest first). Moving from "Default order" or "Plotted value" to a column sets it to ascending. Moving between columns keeps it.
- **Compare as** (`#widgetSortCompareAs`, `dialog-sort-compare-as`).
  - Options: "Text (10 before 2)" and "Number (2 before 10)". Shown only when the sort column's known type is text, integer or decimal.
  - The hint reads "Values that are not numbers go last." when Number is chosen.

**State.**
- The signals start from `start.sort`:
  - `sortKey` is `''` without an order, `'value'`, or `'column:' + column`.
  - `sortDirection` defaults to `'asc'`.
  - `sortCompareAs` is the saved value.
- **Compare as in the draft.** The value sent depends on what is known about the sort column's type:
  - The type is known and is date, timestamp or true/false: the draft sends `null`. This clears a value left over from a previous column, and lets a saved widget whose column has since become a date be saved again.
  - The type is known and is text, integer or decimal: the draft sends the signal's value. When that is null (saved through the API), the select shows and the draft sends the type's default.
  - The type is unknown (the catalog is loading or failed to load, or the column is stale): the draft sends the saved value unchanged.
- **Picking a column** sets the signal to `number` for integer and decimal, `text` for text, and `null` otherwise.
- **Resets.**
  - `pick()` (a new indicator) resets the order, because the columns change.
  - Changing the chart type keeps the order in the signals, and `queryFor` nulls it for other types.
- **Validation.** `problems` reports `sort` when an order by column has no column chosen, so `previewConfig` stays null until it is complete.
- **Server errors.** `Problems` gains `sort`. `fieldFor` maps every server path containing `sort` to it. None of the substrings it checks earlier (`title`, `ratio`, `measure`, `indicator`, `groupby`, `geometrycolumn`, `buckets`) occur in a `config.sort.*` path. The "Order bars by" field shows the error (`#widgetSortBy-error`), because it is always visible while the others may be hidden.
- Strings go under `indicators.dialog.sort*` in `en.json`.
- At phone width the grid is one column.

*Alternative:* segmented buttons for the direction and the comparison. Rejected: there is no shared segmented control, and selects match the dialog's other fields. The direction is a separate select, unlike the performance page's single "Sort by", because folding it into the column list would list every column twice.

### D8. Docs

`docs/indicators-dashboard.md`:
- The bar and column rows of the widget types table say they can be ordered, and that by default they follow the group column's type.
- A new section "Bar and column order" covers:
  - the plotted value or a column, and the direction;
  - compare as text or as a number, with the `1`, `2`, `10` example;
  - the smallest value per bar;
  - bars without a value last, which includes text that is not a number and `NaN`;
  - ties in the default order;
  - a script that needs a custom order, such as age ranges, publishing a `sort_key` column.

## Risks / Trade-offs

- **[Existing widgets grouped by an integer or decimal column reorder]** → This is the intended fix of a text order nobody chose. The proposal and docs say so. Text, date, timestamp and true/false groups keep their order.
- **[Text order depends on the database locale]** → The default order and the clean data sort have the same dependency today. Tests assert only orders that do not depend on the locale (digits, lowercase ASCII).
- **[`pg_input_is_valid` needs PostgreSQL 16 or later]** → The API already depends on it in `PerformanceSql`, and the stack and the integration tests pin PostgreSQL 17. This change adds no new version requirement.
- **[Non-numbers go last silently]** → The dialog hint and the docs say so. Counting them in the widget, as the map counts unparseable geometry, is left for later if analysts ask.
- **[Pattern and cast per row]** → Indicator tables are shaped per indicator and are small. The existing query timeout still applies.
- **[NaN in the plotted value]** → A `double precision` measure holding `NaN` sorts first when descending, takes one place under the limit, and is then dropped by `ReadRowsAsync`, as happens today. Accepted.
- **[API-only chart type change is rejected on a sorted widget]** → The error names `config.sort` and says why. The UI always sends a config with `sort` nulled for the new type.

## Migration Plan

- No database migration. Deploy `tamiza-api` and `tamiza-ui` together, as Compose does.
- Widgets saved before the change have no `sort` and use the default order. That order changes only for integer and decimal group columns.
- An older UI against the new API never sends `sort`. Editing a sorted widget in an older UI replaces its config and removes the order.
- **Rollback:** the previous images.
  - Integer and decimal groups return to text order.
  - The older API ignores `sort` when it reads a widget, so sorted widgets draw in its default order. Any write it makes to a widget re-serializes the config without `sort` (a move or resize from the grid, a rename, a type change or an edit). That deletes the stored order, and the widget stays unsorted after a redeploy until it is edited again.
  - Widgets left untouched during the rollback sort again after a redeploy.

## Verification Results

Measured on 2026-10-15 in a separate Compose project (`tamizaverify`: PostgreSQL 17, API, worker and UI built from this change as `:verify` images, a Keycloak sign-in as a project admin), with a seeded indicator `ranked` of six rows: five municipalities (`Cali` with two rows whose `sort_key` is 5 and 1, and values 40 and 35; `Palmira` 120; `Buga` 40; `Tulua` 20; `Yumbo` 10 with no `sort_key`), a text `round` column holding `1`, `2`, `10`, `n/a` and an empty text, and a date column.

- **Default order.** The new widget dialog drew a column chart with no order alphabetically: Buga, Cali, Palmira, Tulua, Yumbo.
- **Plotted value.** Ordering by the plotted value, which the dialog set to descending on its own, drew and saved Palmira (120), Cali (75), Buga (40), Tulua (20), Yumbo (10). The saved config was `{"by": "value", "column": null, "compareAs": null, "direction": "desc"}`.
- **Another column.** Ordering by `sort_key` preselected Ascending and Number (with the hint that values that are not numbers go last) and drew Cali, Buga, Palmira, Tulua, Yumbo: Cali by its smallest value (1) and Yumbo, with none, last. The saved config was `{"by": "column", "column": "sort_key", "compareAs": "number", "direction": "asc"}`.
- **Stale column.** After `sort_key` was dropped from the table, the widget showed "The published table no longer has the columns: sort_key." instead of a chart.
- **Errors.** No request answered 500; the API log held only the unrelated Kobo synchronization warning of the seeded project's fake token.

Not measured here, covered by the integration tests against PostgreSQL 17 instead: comparing the text `round` column as text and as a number, the values that are not numbers (`n/a`, `''`, `NaN`, `1e1000000`), and widgets saved before the change drawing integer groups in numeric order.
