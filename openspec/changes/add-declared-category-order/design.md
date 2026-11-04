# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change builds on two changes of the program, both implemented and archived on 2026-11-04:
- `add-indicator-column-metadata` adds a per-column metadata map to every indicator script version (`script_versions.columns`, migration 0021):
  - in the API, `tamiza-api/src/Tamiza.Api/Indicators/ColumnMetadata.cs` holds the record `IndicatorColumnMetadata(Label)`, the static class `ColumnMetadata` (`MaxColumns` = 100, `Normalize`, `Check`, `Read`, `Write`) and the static class `ColumnLabels` (`CheckKey`, `CheckLabel` and `For`: the rules that are only about names). The map is stored as JSON and `Read` ignores a property it does not know. Today `Normalize` keeps an entry only while it has a label, and `Check` counts and checks only the entries that have one; this change widens both to the new properties (D6, D7);
  - `ScriptEndpoints.ValidateMetadata` (`Pipeline/ScriptEndpoints.cs`) calls `ColumnMetadata.Check` with the other metadata checks of a save, a save that leaves `columns` out copies the stored text of the current version (`KeepColumns`), and `ColumnMetadataTests` guards the limits the page copies (`The_page_holds_the_same_column_label_limits_as_the_api`);
  - the "Column names" section of the indicator's details form (`pages/projects/indicator-metadata-fields.ts`, fed by `project-scripts.ts`), `save_script`'s `columns` (`McpScriptTools`, its `ColumnsHelp`), `IndicatorEntry.columns` and `ScriptVersionDetails.columns` (UI, a `ColumnMetadataMap` of `ColumnMetadata` from `core/projects/pipeline.service.ts`), `ScriptVersionDetails.Columns` and `IndicatorMeta.Columns` in the emails (API);
  - the form holds the row being typed for each column as `IndicatorMetadata.columns: Record<string, ColumnDraft>`, where `ColumnDraft` is `{ label: string }`. Three functions of `pages/projects/column-names.ts` are the places later changes extend: `draftsOf` (the rows of a saved version, one per column with a label), `columnsOf` (what a save sends, only the non-empty labels) and `sameColumns` (whether two sets of rows save the same). The component lists a column the latest test lacks only when that column has a saved label, and `shownColumns` in `project-scripts.ts` lists only the labelled columns of a viewed version.

  Where this change's column properties go was decided with the user on 2026-11-04: in that record, `ColumnMetadata.Normalize` and `ColumnMetadata.Check` (D6, D7), and in `ColumnDraft` and the three functions above (D9). `Pipeline/ScriptEndpoints.cs` does not change.
- `add-widget-context` made the widget workbook request send `savedConfig(widget.chartType, widget.config)` (`exportWorkbook` in `pages/projects/project-indicators.ts`) in place of the query, so the saved display reaches the workbook. `WidgetExportEndpoints` validates the config it receives as a saved config, and `ReportDataBuilder.BuildWidgetExportAsync` reads the indicator's current column metadata. That change also fixed the parts of a widget image (title, subtitle, notes, caption, chart, legend, notes, footnote, footer) and the lines of a workbook's "Summary" (source, subtitle, plotted line, footnote, notes); an order and hidden prefixes change only the values that the chart, the legend and the table write, not those parts or their order.

`WidgetDisplay` already holds `Color`, `Ramp`, `Orientation`, `Labels` (`add-indicator-column-metadata`) and `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription` (`add-widget-context`), in that order, and `core/projects/indicators.service.ts` mirrors it; `add-chart-annotations`, implemented before this change, appends `ReferenceLines`, `Interpolation` and `DataLabels`. The pieces this change works on, as they are today:

- **Today's orders live in several places, after the query and in it.**
  - SQL: a bar or column orders by its sort, then by the group column's own type, the missing group last (`IndicatorQueryBuilder.OrderBy` and `GroupOrder`). With a sort by value, the missing group is placed by its value, since `NULLS LAST` applies to the sort key. A widget with a series ranks its groups with the same `OrderBy` (`SeriesQueryBuilder`, as `rk`). A box plot orders by median or by group (`BoxQueryBuilder`).
  - API, after the query: the kinds that cannot be ordered re-sort their groups naturally (`IndicatorDashboard.ReadSeriesAsync`, under the `NaturalGroups` flag of `SeriesQueryBuilder`'s plan), box plots too (`IndicatorDashboard.ReadBoxesAsync`), and a series is always natural, then `none`, then `other` (`SeriesDomain.Order` in `IndicatorSeries.cs`). `NaturalOrder` is the class of `NaturalOrder.cs`.
  - UI, after the answer: `pivot(rows, empty, 'listed' | 'natural')` (`chart-data.ts`, natural for a line in `axis-chart.ts`), `chartOrder` (`series-table.ts`, natural for every kind but bar and column), and the pie, which draws its `parts` largest first while its table lists the rows as they came (`pie-widget.ts`). The pie's accessible summary names `slices[0]` as the largest slice, which is true only while the slices are ranked by size. `naturalCompare` is `Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })` (`chart-scales.ts`), the twin of `NaturalOrder`.
  - Emails: `ReportDataBuilder.AxisTable` re-sorts a line naturally (its `natural` argument); `PieTable` lists the rows as they came; `SeriesReportTable` reads `SeriesTable.From`; `BoxplotTable` lists the boxes; `MapTable` writes its places with `GroupCell`.
  - MCP: `Row` (`McpDashboardTools`) lists the plain rows as the response has them, `SeriesPayload.From` reads `SeriesTable.From`, and `BoxPayload.From` lists the boxes.
- **Labels are made in a few helpers.** Group labels come from each chart's `groupLabel` callback (`axis-chart.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `boxplot-chart.ts`), from `pivot`'s categories (`chart-data.ts`), from `crossTableModel` (`crosstab-widget.ts`) and the map tooltip's heading (`map-tooltip.ts`). Series names come from `seriesName` (`series-legend.ts`). In C#, `GroupCell` (`ReportDataBuilder`) and the series `Label` of `SeriesReportTable`.
- **Colors follow the display order.** `chartColors(...).forValues(column, values)` hands the free palette colors out in the order it is given, after the kept colors (`chart-colors.ts`). The legend passes the series columns in order (`legend` in `axis-chart.ts`); the pie passes its domain, ranked by size over the widget's own rows (`colorDomain` and `partColors` in `pie-widget.ts`; design D8 of dashboard palettes). Bars with no series and boxes use `kept()` or the widget color, so their place does not change their color. Kept colors are looked up by the raw value: `pivot` keeps `groups` next to the shown `categories` (`chart-data.ts`), and `forValues` reads `column.series`.
- **Display settings never reach the query.** `WidgetDisplay` (`IndicatorModels.cs`) has each member `WhenWritingNull`, and `Normalize()` returns null while every member is unset. `CheckDisplay` (`IndicatorValidation.cs`) skips per-kind rules when the chart type is null. `WidgetQuery.For` and `queryFor` (`widget-view.ts`) leave `display` out, `savedConfig` and `displayFor` keep it per kind, `sortOf` keeps a box plot's sort only by value and with a group column, and the form builds the display in `displayDraft` (`widget-dialog.ts`). The stored text is pinned (`WidgetDisplayTests`, `MinimalBefore` and `RichBefore`), and dashboard versions embed the raw config (`DashboardVersions.Content`). This is also why the program's rule that a query flag derived from a saved setting travels on the request (`add-chart-annotations`, D4) does not apply here: ordering never reaches the query, so this change derives no flag.
- **The form already edits an ordered list of values.** The Likert scale editor (the `dialog-scale` fieldset of `widget-dialog.ts`, with `moveScale` and `removeScale`) lists values with move-up, move-down and remove buttons of 40 px and an "add" field, and reads the series values with a heat map query of the widget's own rows (`seriesValues`).
- **A test knows the values of its table.** The dashboard view of an indicator test (`dashboard_view` in `tamiza-analytics/src/tamiza_sdk/dashboard.py`) lists, for the first `MAX_CHART_DIMENSIONS` (10) dimensions that are not geometries, the values of each one that has at most `MAX_CHART_VALUES` (50), as `IndicatorTestView.charts[column].groups` in the UI (null past 50 values). The details form can offer them, read from the latest test's step as `testedColumns()` in `project-scripts.ts` reads its columns.

## Goals / Non-Goals

**Goals:**
- One ordering rule with a C# and a TypeScript twin, checked against one shared fixture, applied after the query by every consumer: the charts, "Show as table", images and print (same DOM), the emails and workbooks, and MCP.
- With nothing declared, every consumer takes exactly today's code path: same order, same labels, same colors, same JSON.
- No change to the query, its SQL, its request or its response.

**Non-Goals:**
- Choosing which series or slices are kept, or folding small groups (P2 `add-normalization-and-totals`).
- Ordering the values offered by filters, by the Colors form or by the order editors themselves; they keep listing full values in their own order.
- Making the test's dashboard view chart in the script editor follow the order being typed in the details form.
- Prefix rules beyond a leading number.

## Decisions

### D1. Two places to declare, one precedence

- **The indicator's column**, in the map of `add-indicator-column-metadata` (keyed by column name), gains three properties:

  ```json
  "columns": {
    "categoria": { "label": "Respuesta", "order": ["1. Sí", "2. Tal vez", "3. No"], "last": ["4. No sabe"], "stripPrefix": true },
    "comuna": { "last": ["Sin dato"] }
  }
  ```

- **The widget**, under `config.display`, gains `order`: `{ "group"?: [values], "series"?: [values] }`.

  ```json
  "display": { "color": 2, "order": { "series": ["Acceso", "Interés", "Otra"] } }
  ```

- **Precedence**, per place (the groups, the series): the widget's own list replaces the indicator's `order`, never its `last`; then today's order. A value the widget lists is placed by the widget, even when the indicator keeps it last.
- The widget has no `last` and no `stripPrefix` of its own: a widget places any value anywhere by listing it, and what a value's number means is a fact of the indicator, not of one chart.

*Alternatives:*
- *Only a widget order.* Rejected: the 27 widgets that sort by `orden` and every new widget over the same column would each repeat it, and widgets created through MCP would forget it.
- *Only an indicator order.* Rejected: a chart sometimes needs its own order (the boxes of "Todos los vehículos" first), and an order kept per widget costs nothing more than a display setting.
- *Declared by the script* (`df.attrs`, a returned dict). Rejected by `add-indicator-column-metadata`: metadata lives in the details form and MCP, like label, unit and format.
- *A top-level `config.order`.* Rejected: it would enter `queryFor`, `WidgetQuery.For` and the query key, so dragging a value would query again.

### D2. Ordering after the query, in one shared function

New `Indicators/CategoryOrder.cs` and `indicators/category-order.ts`, twins, both tested against `tamiza-ui/src/app/pages/projects/indicators/testing/category-orders.json` (read by `category-order.spec.ts` and by `CategoryOrderTests.cs` through `PromptKitChecks.FindUp`, as `SeriesTableTests` reads `series-tables.json`). The fixture holds input lists, declarations and the expected output, worked out by hand, and the prefix cases of D5. The limits are constants on both sides (`CategoryOrder.MaxValues = 100`, `MaxLast = 20`; `maxOrderValues`, `maxKeptLast` in `indicators.service.ts`), with a guard in `CategoryOrderTests` that reads the TS file, as `ColumnMetadataTests` guards the column limits.

The query endpoint, its SQL and its response do not change, so `IndicatorQueryTests` (JSON byte for byte), `IndicatorQueryBuilderTests` and `SeriesQueryBuilderTests` stay as they are. Each consumer arranges what it received:
- the page: every chart, from one `orders` input computed in `widget-view.ts` (D8);
- the emails and the widget workbook: `ReportDataBuilder` (D11);
- MCP: `McpDashboardTools` (D12).

*Alternatives:*
- *`ORDER BY array_position(@order, g::text)` in SQL.* Rejected:
  - the order would be part of the query, so editing it re-queries and changes the query key;
  - the series and pie domains rank by total, and lines and heat maps re-sort naturally after SQL anyway, so SQL alone could not give the order;
  - the query would have to read the indicator's metadata in its transaction, and every UNION branch and pinned SQL test would change.
  Its one advantage, ordering before the row limit, matters only past 5,000 groups (see Risks).
- *The API reorders the response before sending it.* Rejected: the widget's `display.order` would have to travel with the query, and a page's cached answers would not follow a metadata change without querying again.

### D3. The rule

```
arrange(entries, keyOf, declared, sorted):
  // entries: in the order the place has today; keyOf(e) = { value: text | null, kind: value | none | other }
  if declared is null: return entries
  order = sorted ? [] : (declared.order ?? [])
  if order is empty and declared.last is empty: return entries           // nothing applies: the same list
  listed = value entries whose text is in order, by their index in order
  kept   = value entries not in order whose text is in declared.last, by their index in last
  rest   = the other value entries, in the given order
  return listed + rest + kept + [entries of kind none] + [entries of kind other]
```

- Text is compared ordinally, case and accents included, like colors kept by value. A listed value the widget does not show is skipped.
- Once a declaration applies, "(none)" goes after the values kept last and "Other" at the very end, whatever its place was (a pie's missing group, ranked by size today; a sorted bar chart's missing group, placed by its value today). With nothing declared, they keep today's places.
- `declared` for a place is resolved by `ordersFor(chartType, config, columns)`:

  | Place | `order` | `last` | `sorted` |
  |---|---|---|---|
  | groups | `display.order.group` ?? `columns[groupBy].order` | `columns[groupBy].last` | a bar or column with a sort; a box plot ordered by median with a group column (the rule of `sortOf` in `widget-view.ts`) |
  | series | `display.order.series` ?? `columns[seriesBy].order` | `columns[seriesBy].last` | never |

  `ordersFor` returns null when no place has a declaration and no column it shows hides prefixes, and every caller then runs today's code.
- **A sort wins over the indicator's order.** The first plan of this change listed a bar's sort among today's defaults, below the indicator's order. This design puts it above: a sort is the widget's own, explicit choice, and the 30 production widgets sorted by value would lose that order the day someone declares an order on their group column. With a sort, only `last` applies. A box plot's order by median is the same kind of choice and follows the same rule ("Box plot order"). A widget's own group order and a sort, or an order by median, are refused together, because neither could say which wins.
  *Alternative:* the indicator's order over the sort, as listed. Rejected for the reason above; the 27 widgets sorted by `orden` would also stop following their column.

### D4. Where each kind arranges

| Kind | Groups today | Groups arranged | Series arranged |
|---|---|---|---|
| bar, column, no series | as listed (the group column's own order, or the sort) | yes (`pivot`) | — |
| bar, column, series | as listed (the same `ORDER BY`, as ranks) | yes (`chartOrder`) | yes (columns) |
| line | natural | yes | yes |
| pie | slices largest first; table as listed; colors by its domain | yes (parts, table, domain) | — |
| heat map, cross table | natural | yes | yes |
| population pyramid | natural | yes | no: `pyramid.left`/`right` set the sides |
| Likert bars | natural | yes | no: `scale` sets the answers |
| box plot | natural, or by median | yes (boxes) | — |
| stat tile, histogram, map | — | no | — |

The UI's call sites gain an optional argument and keep today's path without it:
- `pivot(rows, empty, base, orders?)` sorts by the raw group as today, then arranges; its categories carry the shown text (D5) and its `groups` keep the raw values;
- `chartOrder(table, type, orders?)`, and a new `arrangeColumns(table, orders?)` that moves each column with its cells, shares and total, applied in `orderedSeriesTable` and where `buildSeriesTable` is read directly (`seriesTable` in `axis-chart.ts`, `table` in `crosstab-widget.ts`, whose `crossTableModel` takes the orders in its context as it takes `names`). `buildSeriesTable` and its twin `SeriesTable.From` do not change, so `series-tables.json` stays as it is;
- `pie-widget.ts`: `colorDomain`, `parts` and `table`. Its summary takes the largest slice by value, not `slices[0]`, so it stays right in a declared order and reads the same without one;
- `boxplot-chart.ts`: `boxes`.

Keyboard order follows: `markTooltips` steps through its grid of marks by group, then by series (`mark-tooltips.ts`), which is the order the plot lays them out.

### D5. Prefixes hidden

- **Pattern**, in both twins: `^[0-9]{1,3} *[.):\-–] +(?=[^0-9\s])`. One leading number of 1 to 3 ASCII digits, optional spaces, one of `.` `)` `:` `-` `–`, at least one space, and a rest that is not empty and does not start with a digit. So `1. Sí`, `2) No`, `3 - Tal vez`, `05: Mañana` lose their number; `10 - 14`, `0 - 4`, `100.000`, `1.5 horas`, `1.Sí` and `$10.000` do not change.
- **Alike values.** `shownValues(values, strip)` gives the text of every value of one place; two different values that would read alike keep their full text.
- **Where.** The text of group and series values wherever a chart, its legend, its tooltips, its accessible summary, "Show as table", an image, an email table or a workbook writes them, the map tooltip's heading and the map's table included. A pyramid's sides and a Likert scale's answers are series values, so their names lose their prefixes too, though their order is the widget's.
- **Not where.** The filter editors, the dashboard filter bar, the Colors form, the order editors and MCP keep the full value: they are what filters and colors kept by value match. The order and colors are computed on full values; only the label changes.

### D6. Validation

Widget (`CheckDisplay`; per-kind rules skipped when the chart type is null, as for the other display fields). Values are not checked against the table: a script can change them later.

| Rule | Key | Message |
|---|---|---|
| more than 100 values | `config.display.order.group` / `.series` | "An order lists at most 100 values." |
| a value that is null, longer than 500 characters or holds a NUL character | `config.display.order.group[i]` | "Each value is a text of up to 500 characters." / "The value cannot contain a NUL character." |
| a value listed twice | the second's `[i]` | "'{value}' is listed twice." |
| group order on a stat tile, histogram or map | `config.display.order.group` | "A {kind} widget has no groups to order." |
| group order with no group column | `config.display.order.group` | "Ordering groups needs a column to group by." |
| group order on a bar or column with a sort | `config.display.order.group` | "A sorted widget takes the order of its groups from its sort: remove the sort or this order." |
| group order on a box plot ordered by median | `config.display.order.group` | "A box plot ordered by median takes the order of its boxes from their medians: remove that order or this one." |
| series order without a series column, or on a kind that takes none | `config.display.order.series` | "Only a widget with a series has an order of series." |
| series order on a pyramid | `config.display.order.series` | "A population pyramid's sides are its left and right values." |
| series order on a Likert widget | `config.display.order.series` | "A Likert widget's answers follow its scale." |

The 500 characters are `DataQueryBuilder.MaxTextLength`, the limit of a filter value and of a color kept by value; the NUL rule is the one `DashboardValidation.Colors` already applies.

Column metadata, in `ColumnMetadata.Check` of `add-indicator-column-metadata` (`Indicators/ColumnMetadata.cs`, called by `ScriptEndpoints.ValidateMetadata`, so its errors come back with the label and format errors of the same save, and `Pipeline/ScriptEndpoints.cs` does not change), with its key pattern `columns.{column}.…`:

| Rule | Key | Message |
|---|---|---|
| `order` of more than 100 values | `columns.{column}.order` | "An order lists at most 100 values." |
| `last` of more than 20 values | `columns.{column}.last` | "At most 20 values are kept last." |
| a value that is null, too long or holds a NUL | `columns.{column}.order[i]` / `.last[i]` | as for the widget |
| a value twice in one list | the second's `[i]` | "'{value}' is listed twice." |
| a value in both lists | `columns.{column}.last[i]` | "'{value}' is already in the order." |

No type check: a column may be an integer, a date or true/false, and its values are compared as the widget's rows write them (`1`, `2026-01-31`, `true`). A column the table no longer has keeps its entry, as `add-indicator-column-metadata` keeps a name.

`ColumnMetadata.Check` counts toward `MaxColumns` (100) and checks only the entries that have a label; it now counts and checks every entry that has any property, so a column with only an order cannot slip past the limit. Its message ("An indicator labels at most 100 columns.") and `pipeline` "Column labels that are refused" (scenario `Too many labels`, 101 labels) are unchanged, and a save of labels alone is counted as before.

### D7. Stored shape

- **C#.**
  - A new record `WidgetCategoryOrder(IReadOnlyList<string?>? Group, IReadOnlyList<string?>? Series)`, each member `WhenWritingNull` with a `Description`. `Normalize()` turns an empty list into null and returns null when both are null. Null items are kept so validation can name them, as tooltip items are. Values are never trimmed: they are exact text, and an empty text is a group value.
  - `WidgetDisplay` gains `[JsonIgnore(WhenWritingNull), Description(...)] WidgetCategoryOrder? Order = null`, declared last (after `ShowDescription`, and after `DataLabels` once `add-chart-annotations` is implemented), so a display without it writes the same text. `Normalize()` returns null only when every member, the order included, is unset, so a display that holds only an order is kept.
  - `IndicatorColumnMetadata` (`Indicators/ColumnMetadata.cs`) gains `Order` and `Last` (`IReadOnlyList<string?>?`) and `StripPrefix` (`bool?`), all `WhenWritingNull` with a `[Description]` for the MCP schema. `ColumnMetadata.Normalize` turns empty lists and `false` into null, so "off" is never stored, and drops an entry left with no property: it keeps an entry while any property is set, where today it keeps one only while it has a label.
- **TS.** `WidgetDisplay.order?: { group?: string[]; series?: string[] }` in `indicators.service.ts`, and `order?`, `last?`, `stripPrefix?` on `ColumnMetadata` in `pipeline.service.ts`, which `ColumnMetadataMap` uses. The form's `ColumnDraft` gains them too (D9).
- **Twins of the query.** `WidgetQuery.For` and `queryFor` do not change: `display` is not part of either. `WidgetDisplayTests.A_query_never_carries_the_display` gains the order, with a shared case in `WidgetQueryTests` and `query-for.spec.ts`.
- **`displayFor`** (`widget-view.ts`) keeps `order.group` when the kind takes one, the widget has a group column and `sortOf` gives no sort, and `order.series` when the kind takes a series order and the widget has a series column. A change of kind, a cleared column or a new sort drops them, as a change of kind drops an orientation, so the form never saves what `CheckDisplay` refuses.

### D8. The page

- `widget-view.ts` computes `orders = computed(() => ordersFor(chartType(), config(), indicator()?.columns))` and passes it to the chart it draws, as it passes `names` (new input `orders`, default null). A viewed version passes its own widget config with the indicator's current metadata (spec "Declared category order in dashboard versions").
- The form's preview is a `WidgetView` over the draft, so an order edited in the form redraws it with no query: the query key comes from `queryFor`.
- `WidgetView` gains an output with the values it shows, groups and series, in display order, full text. The form seeds its order editors from it.

### D9. The forms

- **One editor, two forms.** A new `indicators/value-order-editor.ts`, made like the Likert scale editor: one row per value with **Move up**, **Move down** and **Remove** buttons (40 px targets, `aria-label` "Move {value} up"), focus kept on the moved value, and an **Add a value** field with a `<datalist>` of the values offered. Values are shown in full.
- **Widget form** (`widget-dialog.ts`). A fieldset **Order of values**, for the kinds that take one:
  - for the group column, and for the series column when the kind takes a series order, a radio pair: **Default order** (worded "the indicator's order" when the indicator declares one, else "the chart's order") or **Own order**;
  - **Own order** shows the editor, seeded with the preview's values in their current order; values the order lists but the preview does not show are listed apart as not shown, with **Remove**;
  - the group pair is off while a sort or an order by median is chosen, with the reason ("The sort decides the order of the bars"), as a box plot's order is off without a group;
  - under a series order, and under a pie's group order, a note: values with no color kept take the palette's colors in this order, and **Colors** keeps a value's color.
  - Signals `groupOrder` and `seriesOrder` (null is the default) go into `displayDraft`, which writes `order` only when one is set, so a widget saved unchanged keeps its stored text and makes no version. `fieldFor` maps `display.order.group` and `display.order.series` to their fields, before the existing checks.
- **Details form** (the "Column names" section of `add-indicator-column-metadata`: `indicator-metadata-fields.ts`, fed by `project-scripts.ts`, which passes it the columns of the latest test or, without one, of the published table; the add dialog shares the component but lists no columns before a test, so it offers no order).
  - `IndicatorMetadata.columns` stays `Record<string, ColumnDraft>`, and `ColumnDraft` (`core/projects/pipeline.service.ts`) gains `order: string[]`, `last: string[]` and `stripPrefix: boolean` next to `label`, the properties being typed. The three functions of `pages/projects/column-names.ts` carry them: `draftsOf` makes a row for every column that has a label or any property, `columnsOf` makes an entry for every row that has one (a trimmed label; empty lists and `false` left out) and `sameColumns` compares them all, so moving a value makes the details unsaved. `metadataOf`, `sameMetadata`, `emptyMetadata` and `save()` in `project-scripts.ts` already go through them. The test's dashboard view in `script-editor/indicator-view.ts` reads only the labels of `columnsOf`, so it does not change.
  - Each column gets an **Order** disclosure, closed by default, whose summary says what is set ("3 values ordered, 1 kept last, prefixes hidden"). It holds the editor for `order`, a second editor **Keep at the end** for `last`, and a checkbox **Hide numbered prefixes ("1. ", "2) ", "3 - ")**. The values offered are those of the latest test's dashboard view for that column (Context), which `project-scripts.ts` passes to the component beside `columns`; for any other column, or before a test, nothing is offered and values are typed.
  - A column with any saved property that the latest test or the published table lacks is listed apart as "Not in the table", with its order, so a save from the form never drops it unseen (the component's `stale` rows are the columns with a saved label today).
  - `save()` sends an entry for every column that has a label or any of the new properties, so a column with only an order is saved.
  - A read-only version lists each column's order, values kept last and hidden prefixes as text after its label (`shownColumns` in `project-scripts.ts` lists only the labelled columns today).
  - A note says the order applies to every widget of the indicator that has no order of its own, on every dashboard, and can recolor values with no color kept.

### D10. Colors

`chart-colors.ts` does not change. The legend passes the arranged columns to `forValues`, and the pie arranges its `colorDomain` with the same rule before it asks for colors. The domain still comes from the widget's own rows, so a pie in a declared order keeps its colors under a dashboard filter. Bars, columns and boxes keep `kept()` or the widget color. A pyramid's sides stay `forValues(seriesBy, [left, right])`.

### D11. Emails and the widget workbook

- `ReadWidgetAsync` already builds the names once for the widget from `meta.Columns`, which `MetaOf` fills (`ColumnLabels.For(meta.Columns, widget.Config.Display?.Labels)`), and computes beside them `CategoryOrder.For(widget.ChartType, widget.Config, meta.Columns)`. Then:
  - `AxisTable` keeps its natural re-sort of a line, then arranges;
  - `PieTable` arranges the rows as listed;
  - `SeriesReportTable` reads `CategoryOrder.ArrangeTable(SeriesTable.From(...), orders)`;
  - `BoxplotTable` arranges the boxes;
  - `GroupCell` and the series `Label` write `CategoryOrder.Shown` texts, `MapTable`'s places included (D5).
- No new email text. The body caps and the 80 KB budget are unchanged.
- The widget workbook reads the same builder (`BuildWidgetExportAsync` builds its block with the same `ReadWidgetAsync`). Its request carries `display.order` because the page already sends `savedConfig(widget.chartType, widget.config)` (`exportWorkbook`, since `add-widget-context`), and `displayFor` keeps the order (D7); task 6.3 checks that it reaches `BuildWidgetExportAsync`. The export endpoint validates the config it is sent with `CheckConfig`, so a bad order is refused there too.

### D12. MCP

- **Results.** `query_indicator` and `get_widget_data` arrange `rows` (a line's rows naturally first, as the page draws them), `series`, `cells` (through `ArrangeTable`) and `boxes`. `query_indicator` reads the orders from the config it is given and the indicator's metadata, for its chart type, or for a bar when it has none, as `SeriesPayload` already reads it. Values stay full text, since a client writes filters from them. With no declaration the tools take no step at all, so their results stay byte for byte (a line's rows keep the response's order, as today). No new result field, so the 60,000-character budget is not touched.
- **Inputs.** `create_widget`, `update_widget` and `query_indicator` take `display.order` in the config, through `WidgetDisplay.Order`'s `Description`; the `create_widget` template (`display: { color, ramp, orientation, labels, subtitle, footnote, caption, unit, showDescription }` today) and the `query_indicator` template (`display: { labels }` today) name `order` after the settings the earlier changes list. `save_script`'s `columns` (`McpScriptTools`; its `ColumnsHelp` ends with "A property other than label is ignored." today) documents `order`, `last` and `stripPrefix` in place of that sentence and says the map is replaced whole, so a client re-sends each column's settings with its name; the sentences that `McpToolCatalogTests.The_tools_name_the_column_labels_they_take_and_return` pins ("{ column: { label } }", "Left out, the current version's labels are kept; {} clears them; given, it replaces them all.") stay, and the new words follow them. `get_script`, `list_indicators` and `get_indicator_columns` return them through the same map.
- **Help texts.**
  - A new constant, `OrderHelp`, follows `ContextHelp` (and `AnnotationsHelp` of `add-chart-annotations` when it is implemented first) in the descriptions of `create_widget` and `update_widget`, as `ContextHelp` follows `DisplayHelp`; `DisplayHelp`'s pinned sentence, "config.display = { color, ramp, orientation, labels } sets how the widget is drawn", stays as it is. It says: "config.display.order = { group, series } lists values, as full text, in the order to draw them (up to 100 each); the others follow in the chart's order. group is refused with a sort (a boxplot's median order included), series without a series column and on a pyramid or likert."
  - `ChartTypeChangeHelp` adds "an order" to the settings it names ("a color, a scale or an orientation" today) and `config.display.order` to the keys it lists, next to the ones the earlier changes add. Its pinned test, `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale`, and the two `IndicatorDocsTests` that pin the `docs/mcp.md` sentences change with it, and so do the template assertions of `McpToolCatalogTests.The_tools_name_the_column_labels_they_take_and_return`.
  - `WidgetConfigInput.Display`'s description ("How the widget is drawn and what it says: …") names the order too and keeps its pinned prefix "How the widget is drawn".
- **`Instructions.md`**, "Choosing a widget": "Answers in the order of the question (Sí, Tal vez, No, No sabe): declare `order`, and `last` for answers such as No sabe, Otro or Sin dato, in `save_script`'s `columns`, instead of numbering labels or publishing an order column; `stripPrefix` hides numbers already in the data, while filters and colors kept by value still use the full text. Declaring an order can recolor series that have no color kept."

### D13. Script guide

`Assistant/Prompts/system.md`, "The indicator contract", gains one bullet: "Keep category labels as the form gives them: do not number them or publish an order column to fix their order on charts; the indicator's column metadata declares the order (`order`, `last`, `stripPrefix`, in the details form or in `save_script`)." `AssistantPrompts.Version` (5 today) goes up by one. `PromptKitTests` (size bound) and `McpGuideTests` (the guide equals `AssistantPrompts`) cover it.

### D14. Documentation

- `docs/indicators-dashboard.md`: a section `### Order of values` (the two places, the precedence, values not listed, values kept last, "(none)" and "Other", sorts and the box plot's median order, prefixes, colors), and the sentences that say "natural order" or "have no order setting": the line chart row of "Widget types", "Bar and column order", "Widgets with a series", "Heat map", "Population pyramid", "Cross table", "Box plot", "Colors kept by value" (whose "palette in order" sentence names the widget's series order) and, for the exclusions the proposal lists, "What it does not do" (no order of filter values, maps, histogram ranges or stat tiles, and no prefix but a leading number); the limits table (100 values per order, 20 kept last).
- `docs/scripts.md`: the **Column names** row of the metadata table in "The indicator's name and metadata", which lists the column properties.
- `docs/mcp.md`: `display.order`, the `columns` properties and full values in results; the sentences about column labels and the chart type that `IndicatorDocsTests` pins keep their words, and the new ones follow them.
- `docs/tamiza-brief.md`: this change's line in the program's list that `add-indicator-column-metadata` writes in section 8.

## Spec ownership

- **MODIFIED, owned by this change** (the program's ownership table gives them to it): dashboard-indicators "Bar and column order" (a sort and a declared order are told apart, the values kept last move with a sort, a declared order is applied after the row limit), "Series order and colors" and "Pie chart" (slices in a declared order; the summary names the largest slice). Every scenario is kept with its name and body; new scenarios are added.
- **MODIFIED because this change contradicts a rule they state**, each by one clause ("or in a declared order when one applies (see 'Declared category order applied')") with every sentence and scenario kept and one scenario added:
  - "Heat map", "Population pyramid", "Cross table widget" and "Box plot order" say their groups SHALL be in natural order. No other change of the program touches them. "Box plot order" also gains the rule that an order by median wins over the indicator's order but keeps its values kept last at the end, and that it is refused with a widget's own order of boxes, as a sort is in "Bar and column order".
  - "Line chart" says the groups SHALL be in natural order. The program's table gives it to `add-chart-annotations`, which lands before this change and adds a sentence on step lines and a scenario. This change still modifies it, as the program allows for a contradicted rule, rather than leave two SHALLs that disagree in the main spec. When `add-chart-annotations` is archived, the `/opsx:update` rebase of this change copies its new text and scenario into this block before this change is implemented (Workflow follow-up of tasks.md).
- **Not modified, though they state today's order:** "Bar and column charts of one indicator" (owner `add-small-sample-warnings`) orders bars "in the widget's order (see 'Bar and column order')" and by the group column "without a chosen order"; the modified "Bar and column order" makes a declared order of groups the widget's order, so its text stays true. "Line chart"'s "SHALL NOT have an order (see 'Bar and column order')" stays true because the modified "Bar and column order" says its order is a sort. "'Show as table' for a widget with a series", "'Show as table' for a box plot", "Marks of a widget with a series" and "Box plot marks" say "in the chart's order" or "in their order", which stays true.
- **Owned by others and only referenced:** dashboard-reports "Widget tables in a scheduled email" and pipeline "Indicator metadata" (both in the main specs since `add-indicator-column-metadata` was archived), dashboard-colors "Colors of values with no color kept" (`add-category-highlight`; its "in the widget's series order" now reads as the declared order, which "Colors follow a declared category order" says), mcp-server "Indicator queries and widget data" (`add-small-sample-warnings`) and "Dashboards and widgets through MCP" (nobody).
- **Versions, duplicates and moves** are covered by the generic lists `add-indicator-column-metadata` wrote in the main specs ("every setting it is saved with"). This change only adds scenarios in its own requirements and "Declared category order in dashboard versions", which states what is not generic: the indicator's metadata is not part of a dashboard version.
- **Names.** Every ADDED requirement names the feature ("Declared category order …", "Numbered category prefixes hidden", "Colors follow a declared category order", "Widgets without a declared category order"), so none collides with the requirements of the other widget and dashboard changes in flight.

## Production impact

The production facts of the Duitama project (a read-only survey of 2026-11-03), and why each stays the same until someone declares an order:

- **27 widgets sort by `orden` as text, and 5 stat tiles filter on it.** The sort decides (D3, `sorted`), the query is untouched, and stat tiles have no groups. Declaring `last` later moves only those values.
- **30 widgets sort by value**, ascending or descending. A sort wins over any order declared later on their group column (D3).
- **Numbered labels are filter and color keys** ("1. No pagaría", "5. Cualquier componente", "3. Mediana", "1. Sí", "2. Maneja moto o carro"). Matching uses full values; `stripPrefix` is opt-in per column and changes only labels (D5).
- **Demand curves** (Teatro 233d and 3229, parqueadero 77ea and de78, Escuelas 2d22) keep natural order, or their sort: nothing is declared on `precio`.
- **The pie** (Escuelas b296) keeps largest first and its domain colors, Culturama's kept color included, and its summary still names the largest slice.
- **Series colors.** On Teatro, every series value of `respuesta` (the four I1 answers), `intencion` and `barrera_tipo` has a color kept by value, so an order declared on them recolors nothing; a column without kept colors would take the free colors in its new order, which the forms and docs say.
- **Box plots colored by `vehiculo`** (Centro a137, parqueadero 8492) keep their kept colors and natural order.
- **Likert scales** (6) keep the order of their scale; a series order is refused on them.
- **`in` filters on group and series columns, `ignoredDashboardFilters`, stacked and percent layouts over counts, heat maps with ramps, the map**: untouched, since no query changes.
- **Stored JSON and versions.** No config is rewritten; a widget without `display.order` serializes as before, and its first save after the deploy makes no version. The production-shapes fixture of `add-indicator-column-metadata` (20 shapes today) is extended and run (task 1.1).
- **Scheduled emails and workbooks** of the 9 dashboards: `CategoryOrder.For` finds nothing declared, so every table is built by today's code.
- **MCP clients** get byte-identical results for widgets that declare nothing.

## Risks / Trade-offs

- **[A declared order is applied after the row limit]** → It shows only past 5,000 groups, and the widget then says the limit was reached. A sort still orders in SQL, before the limit. Documented in "Limits".
- **[An indicator's order changes many widgets at once, colors included, and makes no dashboard version]** → The details form says so, the docs recommend colors kept by value, and the script version records it; a script restore undoes it.
- **[The twins drift]** → The shared fixture covers every rule (sorted, own order over `last`, "(none)" and "Other", skipped values, prefixes, alike values). `NaturalOrder` and `naturalCompare` already differ at locale edges; the fixture avoids those values.
- **[A script renames a value and the order goes stale]** → Unlisted values follow in today's order, nothing fails, and both forms list stale values apart.
- **[A prefix that is data]** ("2 - Tres personas") → `stripPrefix` is opt-in per column, a number followed by a digit is never touched, and the docs give examples.
- **[Alike values depend on what is shown]** → A dashboard filter that hides one of two alike group values lets the other lose its prefix. Each surface decides on the values it shows, the screen and the email alike, so they agree for the same filters; series come from the widget's own rows and do not change with a dashboard filter.
- **["Line chart" is modified by two changes in flight]** → `add-chart-annotations` lands first; this change is rebased on its archived text before it is implemented (Spec ownership).
- **[UI and API deployed apart]** → An older UI drops `display.order` when a member saves the widget form, and the column properties when a member saves the script's details, as with every new field. Deploy them together.
- **[An MCP client re-sends `columns` without the new properties]** → `save_script` replaces the map whole; its description says to re-send them, and `get_script` returns them.
- **[Long orders in a form]** → At most 100 rows, scrolling inside the fieldset, every action a button.

## Migration Plan

1. **Deploy** with `add-indicator-column-metadata` (its migration 0021 added `script_versions.columns`) and `add-widget-context` already in. This change adds no migration (the scripts stop at 0022 today) and rewrites no stored config. Existing widgets and indicators declare nothing, so they render, export, email and answer MCP as before.
2. **Rollback.**
   - An older API that has `add-indicator-column-metadata` reads `script_versions.columns` without `order`, `last` and `stripPrefix`, so their widgets draw in today's order and text. A save of the script's details from the form, or a `save_script` that sends `columns`, writes the map without them; a `save_script` that leaves `columns` out copies the stored text and keeps them. Versions saved before the rollback keep them, and restoring one on a newer release brings them back. A release older still ignores the column.
   - An older API drops `display.order` when it reads a widget config, and never rejects it; a display that held only an order becomes none, and the next save of that widget loses it. A widget saved unchanged writes nothing, because both sides of the comparison lack the order.
   - Dashboard versions keep the raw JSON. A restore on an older API writes it back and its reads drop the order; restoring on a newer release brings it back.
   - An older UI draws today's order and labels. No enum value or operator is added, and there is nothing to undo in the database.

## Open Questions

- Whether a later change should offer dashboard filter values in a column's declared order, with prefixes hidden. Filters keep full values and their own order here, so the specs do not depend on it.
- Whether the script editor's test chart should follow the order being edited in the details form before it is saved. It only previews the table and does not change what is saved.
