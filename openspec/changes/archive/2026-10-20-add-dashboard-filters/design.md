# Design

## Context

See proposal.md for the motivation. The pieces this change works on:

- **A widget query is stateless.** `POST /indicators/query` takes the whole widget config from the browser (`IndicatorEndpoints.cs:73-86`). `IndicatorDashboard.QueryAsync` runs it inside a query slot and a read-only transaction: `WithSlotAsync` waits up to 25 s for one of 4 slots shared by every project, then answers 503 `data.timeout` (`DataDashboard.cs:346-361`). The same endpoint draws saved widgets and previews drafts in the widget form.
- **A filter on a missing column blanks the widget.** `IndicatorQueryBuilder.Plan` adds every filter's column to `missing` (`IndicatorQueryBuilder.cs:129-135`). With anything missing it runs nothing, and the widget shows the "missing columns" alert (`widget-view.ts:78-82`).
- **One `DataQueryBuilder.Build` call refuses two filters on one column** ("Column 'x' has more than one filter.", `DataQueryBuilder.cs:64-78`).
  - Its parameters are named `f{i}`, `f{i}min` and `f{i}max`, counted from 0 in each call (`:143,266,274`).
  - A timestamp range adds `tz` once per call (`:280-283`).
  - Error keys are `filters[i]` and `filters`.
  - `BuildRange` reads an integer column's bounds as `long`, so `2.5` is refused on an integer column (`:226`).
- **Column types** come from PostgreSQL's catalog at every use: `TableCatalog.FindAsync`, and `LockAndDescribeAsync` takes a share lock. The API stores nothing about indicator columns.
  - `ColumnTypes.FromPostgres` maps every integer type to `Integer`; `numeric`, `double precision` and `real` to `Decimal`; `timestamp` with or without time zone to `Timestamp`; and anything unknown to `Text` (`DataModels.cs:20-48`).
  - pandas writes an integer column that has empty values as `double precision`, so the same dimension can be `Integer` in one indicator and `Decimal` in another.
- **Indicator tables cannot list their values.**
  - `DataDashboard.ValuesAsync` accepts only the `output` and `clean` sources (`DataSources.cs:15`), so the widget form asks for typed values (`widget-dialog.ts:825-827`).
  - Its building blocks (`WithSlotAsync`, `InTransactionAsync`, `LockAndDescribeAsync`) are `internal` and take a schema, like `ExportFromSchemaAsync` (`DataDashboard.Export.cs:65`).
- **Dashboards and widgets.**
  - `tamiza.dashboards` has no `jsonb` column today.
  - `dashboard_widgets.config` is `jsonb`, read and written as a string with `JsonSerializerOptions(JsonSerializerDefaults.Web)`, which has no enum converter (`DashboardEndpoints.cs:37`). Enum-like config values are stored as string constants for that reason.
  - `ReadConfig` deserializes `WidgetConfigInput`, so a property the record does not have is dropped on read (`:645-646`).
  - A widget PATCH with `config` replaces the whole config (`:387`).
  - Duplicating copies each widget's config string verbatim (`:281-282`), and `NewDashboard` copies nothing of the dashboard row besides its name (`:540-551`).
  - Rename uses `ExecuteUpdateAsync` without the project lock (`:143-149`).
- **The dashboard page.**
  - `ProjectIndicators` reads no query parameters today. `withComponentInputBinding()` (`app.config.ts:28`) binds both route and query parameters to inputs of the same name.
  - The router reuses the component between dashboards, and `open()` resets the page's state (`project-indicators.ts:403-431`).
  - Dashboard tabs link without a query string (`project-page.ts:87`).
  - The page has a persistent live region, `dashboard-status`, and an `announce()` helper (`:82,443-447`).
  - The grid passes each widget its config (`widget-grid.ts:79-90`).
  - `widget-view` re-queries when `JSON.stringify({type, config: queryFor(...)})` changes, and `switchMap` cancels the previous request (`widget-view.ts:178-208`).
  - `queryFor` and `savedConfig` whitelist the config's fields (`:24-48`), so a new field is dropped unless it is added there.
- **Clean data keeps its view in the address.** `encodeFilters` and `decodeFilters` (`data-view.ts:81-104`) are a base64url JSON list of `DataFilter`. A part that no longer applies is dropped, with a `tmz-alert` that has `announce` (`project-clean-data.ts:137-145,443-462`).
- **`DataFilterDialog`** (`data-filter-dialog.ts`) is reused by clean data and the widget form.
  - It takes a column, a filter, `timeZone`, `loadValues`, `check` and `allowTypedValues`.
  - Its title is fixed to the column name (`:42`), and it offers every operation of the column's type (`operationsFor`).
  - It is full screen below `md`.
- **The widget form** takes `WidgetDialogData {projectId, dashboardId, dashboards, widget, indicators, timeZone, refused}` (`widget-dialog.ts:37-50`).
  - Any input or change inside its container marks it edited (`:673-678`).
  - Server field errors reach fields through `fieldFor` (`:930-944`).
- **Translations.** `translations.spec.ts` requires the same keys and parameters in `en.json` and `es.json`. Every API problem code must be in `error-codes.spec.ts`. This change adds no problem code.
- **Migrations.** The next script is `0014`.
  - `ScriptConventionTests` has one test per script.
  - `SchemaDriftTests` compares the scripts' catalog with the EF model (types, defaults, constraints, indexes).
  - The analytics tests run every script as plain SQL.
- **Templates.**
  - The indicator templates in `tamiza-ui/.../script-templates/indicator/` have byte-equal copies in `tamiza-api/src/Tamiza.Api/Assistant/Examples/indicator/`, which a unit test checks.
  - Changing them requires bumping `AssistantPrompts.Version` (`AssistantPrompts.cs:15-18`).
  - `tamiza-analytics/tests/integration/test_templates.py` runs every template unchanged on a sample whose main table has `_version`, `age` and `town`.

**Decided with the user (2026-10-20):**
- Filters are defined per dashboard by an `analyst` or `admin`. Members change their values without saving them, and the values live in the address.
- A widget's own filter and a dashboard filter on the same column must both hold.
- A filter narrows a widget when the indicator's table has a column with the same name and type; there is no per-indicator column mapping.
- A widget can ignore chosen dashboard filters.
- The indicator export stays the whole published table.
- Indicator scripts must publish the columns to filter on, with measures that can be summed; the templates and docs say so.

**Decided by this design** (open to revision):
- A dashboard holds at most 8 filters, one per column. Labels are 1 to 40 characters and unique within the dashboard, ignoring case.
- There are four kinds, text, true/false, number and date, matched to column types by family (D2).
- Text and true/false filters choose values; number and date filters take a range. No *contains*, *equals* or *empty* on the dashboard.
- A widget's ignored filters are kept by column name.
- The value lists show no counts. A row of an indicator table is a group, not a submission, so a count would mislead.

The requirements are in `specs/dashboard-filters`, `specs/dashboard-indicators` and `specs/custom-dashboards`.

## Goals / Non-Goals

**Goals:**
- The query path stays stateless: a widget query never reads the dashboard row, so 50 widgets cost 50 queries, as today.
- A dashboard filter can never blank a widget, refuse its query or make it look stale. The worst case is a widget that is "not filtered by" it.
- No new problem code, and no change to the clean data dashboard's behaviour.
- Saved widgets and dashboards keep working unchanged, and no filter applies until one is defined.

**Non-Goals:**
- Live updates when another analyst changes a dashboard's filters. The member's page catches up when the dashboard is opened again.
- Remembering a member's values between visits, except through the address.
- Caching table catalogs in the API or the browser.

## Decisions

### D1. Data model (DbUp `0014_add_dashboard_filters.sql`)

```sql
-- Dashboard filters: each dashboard's list of filters, in order. Existing dashboards get none.
ALTER TABLE tamiza.dashboards ADD COLUMN filters jsonb NOT NULL DEFAULT '[]'::jsonb;
```

- **The stored shape** is a JSON array, serialized with the same Web options as widget configs:

  ```json
  [{ "column": "municipality", "label": "Municipality", "kind": "text", "default": { "values": ["Cali"] } },
   { "column": "round", "label": "Round", "kind": "number", "default": { "min": "3", "max": "3" } }]
  ```

  - `kind` is one of the string constants `text`, `boolean`, `number`, `date` (`DashboardFilterKinds`), not an enum, because the stored-config serializer has no enum converter.
  - `default` is null or a `DashboardFilterValue(Values, Min, Max)`.
- **EF:** `Dashboard.Filters` is a `string` mapped `HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")`, exactly as `DashboardWidget.Config`. `SchemaDriftTests` then matches the script.
- *Alternative:* a `dashboard_filters` table, one row per filter. Rejected: the list is always read and written whole, its order and its unique columns and labels are rules of the list, and duplicating a dashboard becomes a copy of one value.
- *Alternative:* the typed `HasJsonColumn<T>` helper. Rejected so that the dashboard's JSON is handled like the widget's, by the same serializer and the same code path.
- No check constraint: the API validates the list (D4). A constraint on JSON shape is not something EF models for the drift test.

### D2. Kinds and column types

A filter applies to a table column when the column has the filter's name (ordinal comparison, as everywhere else) and a type of the filter's kind:

| Kind | Column types | Value | Built as |
|---|---|---|---|
| `text` | Text | `values`, 1 to 100, each at most 500 characters | `in` |
| `boolean` | Boolean | `values` ⊆ {`true`, `false`} | `in` |
| `number` | Integer, Decimal | `min` and/or `max`, invariant decimals | `range`, always as a decimal (see below) |
| `date` | Date, Timestamp | `min` and/or `max`, `YYYY-MM-DD` | `range` on the column's own type: days in the project's zone for a timestamp |

- **Number filters on integer columns.** A number filter is built against the column as if it were `Decimal`, so its bounds are bound as `numeric`. PostgreSQL compares an integer column with a numeric bound exactly (`round >= 2.5` keeps 3 and more). Without this, `BuildRange` would refuse `2.5` on an integer column, although it is a valid number.
- **Why families and not the exact type:** the user's rule is "same name and type". pandas turns an integer column with empty values into a decimal one, and a script may publish one dimension as `date` and another as `timestamp`. Exact types would leave such widgets unfiltered with no visible reason. Value formats are the same within a family, so one value works on every column of the family.
- `DashboardFilterKinds.Matches(kind, DataColumnType)` and `ToDataFilter(column, kind, value)` live in the indicators module. The same value checker (`CheckValue(kind, value, field, errors)`) validates defaults (D4) and applied values (D3), so a default can never be refused when it is applied.

### D3. Query: dashboard filters next to the widget's (api)

`IndicatorQueryRequest` gains `DashboardFilters: IReadOnlyList<AppliedDashboardFilter>?`. Each item is `{ column, kind, values?, min?, max? }`: the values in force for the filters that are not ignored, sent by the page.

- **Before the database is touched,** `IndicatorValidation.CheckDashboardFilters` checks the list:
  - at most 8 items;
  - each column a published-column name (`^[a-z_][a-z0-9_]{0,62}$`, the analytics rule in `tamiza/names.py:14`);
  - a known kind, distinct columns, and values valid for the kind (D2).

  Errors are keyed `dashboardFilters[i]` and name the column. A malformed value is the request's fault, so it stays a 400, as in clean data.
- **`IndicatorQueryBuilder.Plan`** gains a `dashboardFilters` parameter:
  - Each item whose column is absent from the table, or whose type does not match its kind, goes to `notFiltered`. It is never added to `missing`.
  - The others become `DataFilter`s, built by a second `DataQueryBuilder.Build` call.
  - The two `WHERE` fragments are joined with `AND`. A widget filter and a dashboard filter on the same column therefore both hold, and the "more than one filter" rule still applies inside each set.
  - The dashboard filters do not count toward the widget's 50.
- **`DataQueryBuilder.Build`** gains two optional arguments:
  - `parameterPrefix` (default `"f"`) names the parameters `d0`, `d0min`, and so on for the second set.
  - `fieldName` (default `"filters"`) keys errors as `dashboardFilters[i]`. `Plan` maps the index in the list it built back to the request's index.

  The time-zone parameter keeps its name `tz`. `Plan` merges the two parameter lists and keeps one `tz`, since both calls use the project's zone. The defaults keep every existing SQL text and test unchanged.
- **The response** gains `NotFiltered: IReadOnlyList<string>?`, the columns of the request's dashboard filters that did not apply. It is left out when the request had none, and on the not-published and missing-column answers, which draw nothing.
- *Alternative:* the request names the dashboard, and the API reads its filters and the widget's ignored list. Rejected:
  - it adds a dashboard read to every one of up to 50 queries;
  - the widget form's preview queries a draft config that is not saved;
  - the page already holds the definitions.

  Sending the values gives the member nothing new: any member may already query any indicator with any filter.
- *Alternative:* the page appends the dashboard filters to `config.filters`. Rejected for the two reasons in Context: a missing column blanks the widget, and a column the widget already filters is refused.

### D4. Saving a dashboard's filters (api)

`PUT /api/v1/projects/{projectId}/dashboards/{dashboardId}/filters`, analyst, body `{ filters: [ { column, label, kind, default } ] }`.

- **Validation** (`DashboardValidation.Filters`):
  - at most 8 (`filters`);
  - `filters[i].label`: trimmed, 1 to 40 characters, no control characters, unique within the list ignoring case;
  - `filters[i].column`: the column-name rule, unique within the list;
  - `filters[i].kind`: one of the four;
  - `filters[i].default`: null or valid for the kind (D2).

  Nothing is checked against the tables: like a widget, a filter may be defined before its indicator publishes, and tables change with every run.
- **Saving.** One `ExecuteUpdateAsync` filtered by project and dashboard id sets `filters`, `updated_by` and `updated_at`, with no lock, as rename does. Zero rows answers 404 `dashboards.notFound`. The answer is 200 `{ filters }`, as stored, labels trimmed. The last save wins.
- **Reading.** `GET /dashboards/{dashboardId}` returns `DashboardView(Id, Name, Filters, Widgets)`. The first query reads `Name` and `Filters` together.
- **Duplicating.** `DuplicateAsync` re-reads the source row's `Filters` under the lock it already takes, and `NewDashboard` takes them, so the copy holds the list as it was at that moment. Widgets' configs, including their ignored filters, are already copied verbatim.
- *Alternative:* add, change and remove filters one by one. Rejected: the form edits the list as a whole (order, unique labels and columns), and one PUT keeps those rules in one place, as `PUT /order` does for dashboards.

### D5. Values across the dashboard's tables (api)

`GET /api/v1/projects/{projectId}/dashboards/{dashboardId}/values?column=municipality&search=pal`, viewer.

1. The dashboard must exist in the project (404 `dashboards.notFound`), `column` must follow the column-name rule (400 on `column`), and `search` must be at most 500 characters (400 on `search`, as clean data).
2. The distinct indicators of the dashboard's widgets are read from their stored configs.
3. In one slot and one read-only transaction (`IndicatorDashboard.ValuesAsync`, built from `DataDashboard`'s internal pieces as `ExportFromSchemaAsync` is):
   - `LockAndDescribeAsync` runs for each indicator, keeping the tables that exist and have the column as `Text`;
   - then one statement runs:

   ```sql
   SELECT v, sum(n) FROM (
     SELECT "municipality"::text AS v, count(*) AS n FROM "p_…_indicators"."coverage"
      WHERE "municipality" IS NOT NULL AND char_length("municipality"::text) <= 500 [AND … ILIKE '%' || @search || '%' ESCAPE '\']
      GROUP BY 1
     UNION ALL …
   ) s GROUP BY v ORDER BY sum(n) DESC, v LIMIT 50
   ```

   Every identifier is quoted and comes from the catalog. The indicator names in configs were validated when the widgets were saved, and a missing table is skipped.
4. The answer is `DataValueList` (`{ values: [{ value, count }] }`), empty when no table has the column as text. A true/false filter never asks: the page offers true and false itself.

- *Alternative:* a values endpoint per indicator, merged by the page. Rejected: one request and one slot per search instead of one per indicator, and an exact "most frequent across tables" instead of a union of top-50 lists. The widget form keeps its typed values; offering a list there is a separate change.

### D6. A widget's ignored filters (api + ui)

`WidgetConfigInput` and `WidgetConfig` gain `IgnoredDashboardFilters: IReadOnlyList<string>` (default `[]`). They are the column names of the dashboard filters the widget ignores.

- **Validation** in `CheckConfig`: at most 8, each a column name, distinct. Errors are keyed `config.ignoredDashboardFilters[i]`. They are not checked against the dashboard's filters, so a move, a redefinition or a duplicate keeps working.
- **Storage.** The field lives in `config` `jsonb`, so moves and duplicates keep it with no extra code. It is not a query setting: the UI's `queryFor` leaves it out and `savedConfig` keeps it. The API ignores it in `POST /indicators/query`.
- **The form** shows a checkbox per filter of the dashboard ("Apply «Municipality»", checked unless ignored).
  - It saves the unchecked ones together with the entries for columns the dashboard has no filter on now. Those are kept so that "Filter defined again" holds after an edit.
  - When the total would pass 8, the kept entries go first, oldest first.
- *Alternative:* a flag "ignore every dashboard filter". Rejected: the user chose per filter.
- *Alternative:* filter ids. Rejected: ids would not survive removing and re-adding a filter, and a column name is already unique within a dashboard.

### D7. The filter bar and the address (ui)

- **`ProjectIndicators`** keeps:
  - `filters`, the definitions from the dashboard's GET;
  - a new input `f`, bound from the `f` query parameter by `withComponentInputBinding`;
  - a computed `values`, the values in force, one per filter or none.
- **The address.**
  - With `f` absent, every filter is at its default.
  - With `f` present, the values are exactly what it holds, decoded with `decodeFilters`: `in` for text and true/false, `range` for number and date. `encodeFilters([])` (`W10`) therefore means "every filter cleared" (spec "Cleared filter in a link").
  - A change navigates with `router.navigate([], { relativeTo, queryParams: { f } })`, pushing history as clean data does. When the values equal the defaults, `f` is removed, so the plain address and a link at the defaults are the same.
  - Tabs carry no query string, so another dashboard opens at its defaults.
- **Sanitizing.** After the dashboard loads, entries of `f` whose column is not a filter of the dashboard, whose operation does not match the filter's kind, or whose values fail the kind's client check are dropped:
  - the address is replaced (`replaceUrl`);
  - a `tmz-alert` with `announce` lists them (`dashboardFilters.dropped.*`), as clean data does.

  The notice clears when the member changes a value or another dashboard opens. `open()` resets everything.
- **`DashboardFilterBar`** (new, `indicators/dashboard-filter-bar.ts`) sits between the notice and the page body.
  - Each filter is a pill button `«Label»: «value»` ("All" when none), truncated, with its full text in its accessible name and `title`. Clicking it opens `DataFilterDialog`.
  - It has a "Clear filters" button when any value is set, and a "Default filters" button when the values differ from the defaults.
  - It wraps (`flex flex-wrap`) and never scrolls the page sideways.
  - After a change the page announces through `dashboard-status` ("Dashboard filtered by Municipality, Round" or "Dashboard filters cleared"). The dialog's `restoreFocus` returns focus to the pill.
- **`DataFilterDialog`** gains two optional inputs: `title` (the filter's label) and `operations` (only `in`, or only `range`).
  - Its "Remove filter" button reads "Clear" through a `removeLabel` key.
  - The column it is given is synthetic: `{ name: column, type: 'text' | 'boolean' | 'decimal' | 'date', label }`, so number filters take any decimal and date filters take days.
  - `loadValues` calls the values endpoint (D5) with `count: null`, `allowTypedValues: true` (a value outside the list can still be typed), and `check` is local (`of(null)`). The query validates again on the server.
- **The grid** gets `dashboardFilters` (the values in force with their kinds and labels).
  - Each widget gets the subset it does not ignore. `widget-view` adds it to its query key and to `IndicatorsService.query(projectId, config, type, dashboardFilters)`. A change re-queries every widget, and `switchMap` cancels requests still in flight.
  - The view shows a `shrink-0 text-xs` line at the top of its ready state, next to the row-limit line: "Not filtered by: Municipality". It lists the labels of the active filters the widget ignores plus those whose columns come back in `notFiltered`, in the bar's order.
- *Alternative:* keep the values in a signal only, like the basemap switch. Rejected: the user asked for a shareable view, and clean data already keeps its view in the address.
- *Alternative:* the bar as part of the dashboard header. Rejected: viewers have no header actions, and the bar must wrap at 360 px independently.

### D8. Editing the filters (ui)

- **The action.** A "Filters" item in the dashboard's actions menu (analyst, admin) opens `DashboardFiltersDialog` (new, `indicators/dashboard-filters-dialog.ts`). It is full screen below `md`, like `DataFilterDialog`. Each row has:
  - the label input;
  - the column select;
  - the kind (shown, set by the column);
  - the default, set with `DataFilterDialog` as above;
  - move up and down;
  - remove.

  "Add filter" is unavailable at 8, with a hint, reachable from the keyboard as "New dashboard" is.
- **The column choices.** On opening, the dialog asks for `columns(projectId, indicator)` of each distinct published indicator of the dashboard's widgets, in parallel.
  - The choices are grouped by name and kind (D2), each with "narrows N of M widgets", counting the widgets whose table has it and that do not ignore that column.
  - A saved filter whose column no table has now stays, marked "in no table".
  - An error while loading columns shows inside the dialog and still lets the member save the list as it is.
- **Saving** sends the whole list (D4).
  - Field errors land next to their row through a `filters[i].*` mapping.
  - `dashboards.notFound` closes the dialog with `{ gone: true }` into the page's existing removed-dashboard flow.
  - On success the page takes the new definitions and removes `f`, so the dashboard shows the saved defaults. Focus returns to the actions menu button through `restoreFocus`, after the menu has closed, as for rename.

### D9. The widget form (ui)

- `WidgetDialogData` gains `filters` (the dashboard's definitions) and `values` (the values in force on the member's screen).
- When the dashboard has filters, the form shows a "Dashboard filters" fieldset with a checkbox per filter, after the widget's own filters (D6). The existing `touched` handler marks the form edited.
- The preview passes the values minus the unchecked filters, and a line under it says "Filtered by: Municipality" when any applies. `fieldFor` maps `config.ignoredDashboardFilters[i]` to the fieldset.

### D10. Templates and docs

- **`count-by-group.py` and `rate-by-group.py`** take `GROUPS = ["_version"]` instead of `GROUP = "_version"`. The comment explains:
  - list every column the dashboard groups or filters by (`["municipality", "sex", "round"]`);
  - widgets add the rows back up;
  - so the template keeps counts, and numerator and denominator for rates.

  `groupby(GROUPS, dropna=False)` keeps empty values as their own group. `monthly-series.py` is unchanged.
- **The copies** under `Assistant/Examples/indicator/` stay byte-equal, and `AssistantPrompts.Version` goes from 3 to 4. The template names and descriptions in `en.json` and `es.json` are unchanged.
- **`test_templates.py`** runs them unchanged on its sample: `_version` exists there, and a list of one column behaves as before.
- **`docs/indicators-dashboard.md`** gains:
  - a "Dashboard filters" section (defining, the bar, kinds, which widgets a filter narrows, "Not filtered by", ignoring, the address, limits);
  - a "Preparing indicators for dashboard filters" subsection under "What an indicator script must provide": group by every filter column; use counts and sums; give rates as numerator and denominator; avoid rows that count one submission twice; name and type a dimension the same in every indicator.

  `docs/scripts.md` links to it from its indicator rules.

## Risks / Trade-offs

- [One filter change re-runs every widget's query (up to 50, through 4 slots shared by all projects)] → Changes are discrete: a dialog's Apply, not keystrokes. `switchMap` aborts the browser requests, and the API passes `RequestAborted` to Npgsql, so a superseded query is cancelled. This is the same load as opening the dashboard.
- [Two indicators use the same column name for different things, so one filter narrows both] → Columns are matched by name, as decided. The docs ask for consistent names, the "narrows N of M widgets" count shows the reach, and a widget can ignore the filter.
- [Disaggregated tables with non-additive measures (rates in `value`, medians, distinct counts) give wrong totals] → The templates and the docs show the additive shape. The widget form already plots rates as ratios of sums. Tamiza cannot detect this in general.
- [A text column with many values makes the values statement slow across many tables] → It runs in a slot with the 25 s statement limit. A timeout shows in the dialog, and typed values still work.
- [A long `f` (8 filters × 100 values) exceeds what a proxy accepts in a URL when the page is reloaded] → This is the same limit clean data already has with 50 filters. It is noted in the docs.
- [Ignored filters are kept by column name, so changing a filter's column loses the widgets' "ignore"] → Accepted: it is a different filter. The form shows the checkboxes again.
- [An older `api` after a rollback drops `ignoredDashboardFilters` when it reads a widget, and on its next PATCH] → It is only a preference. See the Migration Plan.

## Migration Plan

1. Deploy as usual. DbUp runs `0014` (one `ALTER TABLE` with a constant default, which is instant on PostgreSQL 11 and later). Every dashboard gets `[]`, so nothing changes until an analyst defines a filter.
2. **Rollback.** The previous `api` never selects `filters` and inserts dashboards with the column's default, so it runs against the new schema.
   - Widgets saved with `ignoredDashboardFilters` keep the property in `jsonb`. The older `api` drops it when it reads and rewrites a widget, which loses only the preference.
   - The column can stay. To remove it: `ALTER TABLE tamiza.dashboards DROP COLUMN filters;` and delete the `0014` row from the journal.

## Deviations from the plan

- **The "Filters" action sits after "Move right", before "Remove"** in the dashboard's actions menu, not next to "Rename". Placing it second would have moved every item the existing keyboard tests reach from "Rename"; "Remove" stays last as the destructive action.
- **A column name is checked with the analytics identifier rule** (`^[a-z_][a-z0-9_]{0,62}$`, `ColumnNames.IsValidColumnName`), anchored with `\z` so a trailing line break is refused. It allows Kobo's own columns such as `_version`.
- **The dropped-filters notice is announced through the dashboard's existing live region**, as the dashboard notice is, rather than being a live region itself (`tmz-alert` with `announce`). A region inserted with its text already in it is not reliably read.
- **`DataFilterDialog` hides the condition select when it offers a single condition**, so a dashboard filter's form opens straight on its values or range.
- **The fake browser login of the stack check** forces Chrome to English (`--lang=en-US`); the first run, in Spanish from the machine's locale, showed the Spanish strings correctly.

## Verification Results

**Suites (2026-10-20):**
- **API** (`dotnet test`, after the last change): 1184 unit and 1314 integration tests pass, none failing.
- **UI** (`npm test -- --watch=false`): 1778 of 1780 pass. The 2 failures are the known "stops polling when the page is destroyed" tests in `project-data.spec.ts` and `project-scripts.spec.ts`, which fail on unchanged code in this environment. `npm run check:safety` passes, and `ng build` succeeds (its one warning, an unused `Card` in `project-reference.ts`, predates this change).
- **Analytics** (`tamiza-analytics-test:dev`): 618 passed, including `test_templates.py` running the changed `count-by-group` and `rate-by-group` templates whole and cell by cell. `ruff check` and `ruff format --check` pass on the indicator templates.

**Stack check (task 13.2)**, in a separate Compose project (`tamizaverify`, port 8189, its own env file, a fake OIDC provider on 8190; the user's stack and Keycloak untouched; torn down with `down -v` and the `:verify` images removed):
- **Seed:**
  - three indicators: `coverage` (`municipality` text, `round` integer), `attendance` (`municipality` text, `round` double precision, numerator and denominator), and `households` (`zone` only);
  - four widgets on "Health": a column chart, a ratio bar chart, a stat over `households`, and a stat over `coverage` ignoring `municipality`.
- **21 checks passed in headless Chrome:**
  - **The form at 1280 px:**
    - the form offers `municipality · text · narrows 2 of 4 widgets` and `round · number · narrows 3 of 4 widgets` (integer and double precision as one number column);
    - 3 filters defined with a default on Round (2 to 2) through the clean data filter form;
    - after saving, the bar reads `Municipality: All | Round: 2 | Zone: All` and focus is on the actions button;
    - the `households` stat says "Not filtered by: Round".
  - **The viewer:**
    - no dashboard actions;
    - the Municipality values list Cali, Palmira and Buga once each, with no counts;
    - choosing Cali writes `f` to the address, the charts show Cali only, and the ratio chart reads 60%;
    - "Not filtered by: Municipality, Round" on `households` and "Not filtered by: Municipality" on the ignoring stat;
    - "Dashboard filtered by Municipality, Round." is announced.
  - **The address:**
    - Shared link;
    - Filter removed since (the notice names "district", the address keeps Municipality only).
  - **Keyboard only:**
    - Enter on a pill opens its form, and Escape returns focus to the pill;
    - the actions menu reaches Filters with the arrow keys, Enter opens the form, and Escape returns focus to the menu button.
  - **Widths:** 8 filters with 40-character labels and a long value fit at 360, 768 and 1280 px with no horizontal scroll, and the filter form is full screen at 360 px.
  - **Filters duplicated:** the copy holds the same 3 filters and its stat still ignores `municipality`, and its bar reads the same.
- **Screenshots reviewed:** the define form at 1280 px, the viewer's filtered dashboard at 1280 px, and 8 filters at 360 px.
