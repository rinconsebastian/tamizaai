# Proposal

## Why

Survey dimensions hold values that mean "no data". In the Duitama study, comuna has "Sin dato" (1 household whose census block is unknown), estrato has "No sabe" (2 households), and grupo_edad and the hogar_* columns have "Sin dato". A filter can only keep a list of values, never leave one out, so authors list every other value by hand:
- `comuna in ["COMUNA 1" … "COMUNA 7", "SIN NOMBRE"]` appears in 22 widgets across 7 dashboards;
- an estrato list appears in 12 widgets, a grupo_edad list in 5, and `hogar_menores in ["Sí", "No"]` in 4.

Each list exists only to drop "Sin dato". It silently drops any value added later, and two Escuelas widgets list "Estrato 5" and "Estrato 6", which do not exist. "SIN NOMBRE" is a real comuna of 20 households and must stay.

Dashboard filters cannot do it for the whole dashboard either. They only keep values, and none of the 51 in production has a default. So widgets without a list, such as the stat tiles, keep "Sin dato" in their base, and two widgets on one dashboard count different households. A value that a dashboard filter does leave out also stays in the legend as an empty entry, because series and pie domains ignore dashboard filters on purpose.

## What Changes

- **`notIn` filter operator** (*is not one of*):
  - it keeps the rows whose column holds none of 1 to 100 values, on text and true/false columns, as `in` does;
  - it is built once in the shared `DataQueryBuilder`, so widget filters, MCP `query_indicator`, the clean data dashboard and MCP `query_rows` all take it;
  - rows whose column has no value are kept: "not one of these" leaves out only the values listed (to be confirmed);
  - it is offered in the filter dialog that the widget form and the clean data dashboard share, and described as text wherever filters are listed (`describeFilter` and its C# twin);
  - for example, Teatro 4e00 could use `comuna notIn ["Sin dato"]` instead of listing eight comunas.
- **Values a dashboard filter hides.** A text filter can name up to 20 values it hides by default, for example `hidden: ["Sin dato"]` on the Teatro filter "Comuna (área, desde A1)":
  - while the filter is at "All", every widget it narrows leaves those values out, and the filter bar reads "Comuna: All except Sin dato";
  - picking values works as today, and a member may pick "Sin dato" itself;
  - **Show hidden values** brings them back on that member's screen only, and the address keeps that choice;
  - "Clear all" and "Reset" go back to "All except …";
  - a widget that ignores the filter, or whose table lacks its column, is not narrowed by the hidden values and gets no "Not filtered by" note for them, so the median cards on escuelas_pago_mediana and comercio_gastos_resumen stay as they are;
  - the scheduled email, printing, widget images and MCP `get_widget_data` apply hidden values as they apply defaults, and list them with the filter;
  - they are stored with the filter, next to its default, so a version holds them, a restore brings them back and a duplicated dashboard copies them.
- **Prefilled from declared missing values.** For a text column, the filters form offers the values the dashboard's indicators declare as `missing` in their column metadata (add-small-sample-warnings), such as "Sin dato" for comuna. The analyst confirms them, and they are saved as plain values, so a later change to the metadata does not change the dashboard.
- **Series and slices without left-out values.** A value left out by the widget's own `notIn`, or hidden by a dashboard filter, is also left out of the widget's series and pie slices. It gets no empty legend entry and does not fold into "Other". Values a member picks still never change the series, so colors do not shift while a member filters.
- **Through MCP:**
  - `create_widget`, `update_widget`, `query_indicator` and `query_rows` take `notIn`;
  - `update_dashboard` and `get_dashboard` take and return a filter's `hidden` values, and `get_widget_data` can show them;
  - the tool descriptions, `Mcp/Instructions.md` and `docs/mcp.md` say how to replace the long `in` lists.

Not in this change:
- Rewriting the existing `in` lists. They keep working, and the docs show how to replace them.
- `notIn` on number and date columns. Sentinels such as 98 in a measure are left to the script, to a range filter, or to improve-histogram-bins.
- Hidden values on number, date and true/false dashboard filters, or for a whole project.
- Hiding the values declared `missing` without the analyst confirming them.
- The "Sin dato" footer (`missingFooter`, add-small-sample-warnings), and any change to how `in` adds up several categories (improve-stat-cards).

## Depends on

- `add-small-sample-warnings`: the column metadata `missing` used to prefill hidden values. Without it, hidden values are typed or picked. The design states how hidden values and `missingFooter` combine: rows a dashboard filter hides are left out before the footer is computed.
- `add-indicator-column-metadata`: the column metadata map, and the column names shown in the filter dialog.
- `add-declared-category-order`: it owns "Series order and colors" and "Pie chart", which this change edits minimally after rebasing (see below).
- `add-dashboard-sections`, if it lands first: both edit "Widgets a filter does not narrow".

## Capabilities

### New Capabilities

None. Operators belong to `dashboard-data`, and dashboard filters to `dashboard-filters`.

### Modified Capabilities

- `dashboard-data`:
  - "Text filters" and "True/false and empty filters" gain *is not one of*;
  - new requirement "Rows with no value under is not one of".
- `dashboard-filters`:
  - "Filter kinds": a text filter can also hide values;
  - "Widgets a filter does not narrow": hidden values alone add no note (if confirmed);
  - new requirements "Values hidden by a dashboard filter", "Hidden values in the filter bar and the address", "Hidden values offered from declared missing values" and "Hidden values wherever filters are listed" (print header, image footer, MCP).
- `dashboard-indicators`: "Series order and colors" and "Pie chart" say that series and slices come from the widget's own filters, "not the dashboard filters". The edit adds "except the values a dashboard filter hides" and keeps every scenario.
- `dashboard-reports`: "Filters in a scheduled email" says a filter without a default narrows nothing. A filter with hidden values now narrows, and the email lists it.
- `mcp-server`: new requirement "Filter exclusions through MCP".

## Impact

- **api** (`tamiza-api`):
  - `CleanData/DataQueryBuilder.cs`: `notIn` in `BuildFilter` (lines 150-184), whose `default` branch refuses unknown operators today; `CleanData/DataQueryModels.cs` doc.
  - `Indicators/IndicatorValidation.cs`: `FilterOps` (line 17) and its message at line 573.
  - `Indicators/DashboardFilters.cs`: `DashboardFilter` (line 170) gains `hidden`; `AppliedDashboardFilter` (185) and `ToDataFilter` (52) carry them; `CheckValue` (68).
  - `Indicators/DashboardValidation.cs`: at most 20 hidden values, on text filters only.
  - The twins that copy filter values: `WidgetQuery.AppliedDefaults` (`WidgetQuery.cs:50-56`), `McpDashboardTools.Applied` (line 663) and the UI's `appliedOf`.
  - `Indicators/SeriesQueryBuilder.cs` (the domain reads only the widget's own filters, lines 43 and 65) and the pie domain in `IndicatorQueryBuilder.cs` (lines 74-84): the domain honors hidden values.
  - `Indicators/DashboardVersions.cs`: version content holds `hidden` only when set. Duplicating copies filters raw.
  - `Reports/ReportDataBuilder.cs`: `Describe` and the filter list.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpDataTools.cs` (the `query_rows` operator list, line 134) and `Mcp/Instructions.md`.
  - OpenAPI and tests: the pinned SQL of `SeriesQueryBuilderTests`, `McpToolCatalogTests`, and the production shapes fixture (the 22 comuna `in` lists unchanged).
- **ui** (`tamiza-ui`):
  - `core/projects/data.service.ts`: `FilterOp` (line 54) gains `notIn`.
  - `pages/projects/data-view.ts` (`operationsFor`, `isFilter` and `describeFilter`) and `data-filter-dialog.ts`, shared by the widget form.
  - `indicators/dashboard-filters.ts` (`appliedOf`, `describeValue`, the address `f`), `dashboard-filter-bar.ts` and `dashboard-filters-dialog.ts` (hidden values and their prefill).
  - `core/projects/indicators.service.ts`: the filter types.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** none. Hidden values live in the existing `dashboards.filters` jsonb, and `notIn` in the existing widget config.
- **Analytics:** none.
- **Docs:** `docs/indicators-dashboard.md` (widget filters, "Preparing indicators for dashboard filters", and dashboard filters), `docs/clean-data-dashboard.md` (the text filter table) and `docs/mcp.md`.

**Compatibility.** Nothing in production changes until an analyst opts in:
- The 22 comuna lists and the 21 other dimension `in` lists keep their exact rows.
- The 51 dashboard filters have no hidden values, so every widget, scheduled email and MCP `get_widget_data` result is as today, and series and pies keep their colors.
- Selecting comuna "Sin dato" on a dashboard filter still empties the 22 widgets that list comunas.
- Dashboard filters whose column a table lacks are still skipped silently.
- Turning hidden values on is visible on purpose. Every widget the filter narrows leaves the value out, stat tiles included, so the 46.85 % card on teatro_hoy would no longer count the household without a comuna. Values without a kept color may also take other colors once a hidden value leaves the series.
- MCP clients that never send `notIn` or `hidden` see the same tools and results.

On rollback, an older API refuses a saved `notIn` filter: those widgets show "the query was refused", and the email lists them as no longer fitting. An older API drops `hidden` on the next save of the filters and, until then, reads such a filter as "All". The design keeps `hidden` out of the default for that reason, and states which widgets to fix before a rollback.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, against the main specs as they are then, after the P1 changes and add-dashboard-sections are archived. These decisions are to be taken with the user at that time:
- Rows with no value under `notIn`: kept (suggested) or left out, and whether an empty text counts as no value.
- Whether `notIn` also takes number columns, for sentinels such as 98 and 99.
- Whether the clean data dashboard offers *is not one of* (it shares the builder and the dialog; suggested yes).
- What "Clear all" does with hidden values, and how **Show hidden values** appears in the filter bar and the address.
- Whether hidden values alone add a "Not filtered by" note to widgets the filter cannot narrow (suggested no).
- Whether the prefill from `missing` values is copied once (suggested) or follows the metadata.
- The limit of 20 hidden values per filter.
