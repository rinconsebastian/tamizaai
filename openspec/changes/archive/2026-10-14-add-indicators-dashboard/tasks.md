# Tasks

## 1. Schema (api)

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0007_add_indicators_dashboard.sql` creating `tamiza.dashboard_widgets` (design D1): `id`, `project_id` (FK, cascade delete), `chart_type` (checked against the 7 allowed values), `title`, `config` and `layout` (`jsonb`), `created_by`/`created_at`, `updated_by`/`updated_at`, and the index on `project_id`; mirror in `TamizaDbContext` (a `DashboardWidget` entity); verify `SchemaDriftTests`, `ScriptConventionTests` and a `SchemaMigratorTests` upgrade from `0006`

## 2. Indicators catalog and query (api)

- [x] 2.1 Add `GET /api/v1/projects/{id}/indicators/columns` reusing `TableCatalog` against `p_<id>_indicators.indicators`, answering `published: false` with no columns when the schema does not exist (design D2); verify the `Script 3 has not published` scenario's catalog part and that every published column, not only the five required ones, is listed
- [x] 2.2 Add `POST /api/v1/projects/{id}/indicators/query` (design D2, D9): validate `indicators`/`groupBy`/`valueField`/`filters`/`geometryColumn` against the catalog the same way `CleanData.DataQueryBuilder` does, `GROUP BY` the chosen column with `sum()` on the plotted field, one row per indicator when more than one is requested, the geometry column's raw text taken from an arbitrary matching row when requested, capped at `MaxWidgetQueryRows` with the limit reported when reached; verify scenarios `Default grouping`, `Duplicate rows summed`, `Row limit`, `Extra column grouped on`, and that an unknown column is a validation error, not a 500

## 3. Widget CRUD and roles (api)

- [x] 3.1 Add `POST`/`PATCH`/`DELETE .../indicators/widgets[/{widgetId}]` (design D3): `RequireProjectRole(Analyst)`; `PATCH` as a partial update of `title`/`chartType`/`config`/`layout`; validate `config` against the chart type's constraints (design D9: `stat` and `map` need exactly one indicator, `pie` exactly one indicator, others one or more) and against the catalog (task 2.1); enforce `MaxWidgetsPerProject`; verify scenarios `Analyst adds a widget`, `Viewer cannot edit` (403 for each of add/edit/remove/move), `Widget limit`, and that a chart-type constraint violation is a validation error naming it
- [x] 3.2 Add `GET /api/v1/projects/{id}/indicators` (the dashboard overview: published state, `publishedAt`/`runId` the way `CleanData.LatestStepAsync` resolves them, and every widget with its layout) at `RequireProjectRole(Viewer)`; verify scenarios `Viewer opens a published dashboard`, `Script 3 has not published` (overview part, including that configured widgets still list), and `Not a member`

## 4. Export (api)

- [x] 4.1 Add `POST /api/v1/projects/{id}/indicators/exports` pointing the existing `CsvExport`/`XlsxExport`/`ExportRegistry`/`LimitedStream` machinery at `p_<id>_indicators.indicators` with no filters, reusing `DataDashboardOptions`'s limits unchanged (design D4); verify scenario `Viewer exports` (both formats) and that the download uses the existing shared `GET /api/v1/data-exports/{token}` endpoint unchanged

## 5. UI: dashboard shell, layout and widget CRUD

- [x] 5.1 Add `IndicatorsService`, the `indicators` route and tab (third, after Performance and Clean data, open to every role), and the empty-state view when nothing is published yet, still listing configured widgets; add the `indicators.*` and `project.tabs.indicators` i18n keys; verify specs that the tab is present for viewer, analyst and admin, and the empty-state scenario
- [x] 5.2 Build the widget grid (design D7): a 12-column `@angular/cdk/drag-drop` layout, move and resize persisted through a debounced `PATCH` 400 ms after the drag ends, collision reflow, and `(y, x)` reading order for keyboard navigation and for a "show every widget's table" view; verify a spec for move, resize, and keyboard tab order
- [x] 5.3 Build the add/edit widget dialog (on the existing `Dialog` pattern): chart type, title, one or more indicators (constrained per design D9), group-by column and value field (from task 2.1's catalog), up to 50 filters (reusing the clean data dashboard's filter dialog pieces), and, for a map widget, the geometry column; live-preview the chosen configuration through task 2.2's query endpoint before saving; verify specs per chart type's constraint (stat/map/pie reject a second indicator) and that an invalid draft previews its own error instead of the old chart

## 6. UI: chart widgets

- [x] 6.1 Add `chart-scales.ts` (linear and category scales, SVG path helpers) and the stat, bar, column, line and histogram widgets as inline SVG with a "Show as table" toggle and `role="img"` summary, following the performance dashboard's chart exactly (design D5); verify specs `Single figure`, `One indicator, several groups`, `Several indicators compared`, `Trend over a grouped column`, and `Distribution across many groups`
- [x] 6.2 Add the pie chart; verify scenario `Shares of a total`

## 7. UI: map widgets

- [x] 7.1 Add `wkt.ts`, a hand-written parser for `POINT`, `POLYGON` and `MULTIPOLYGON` text, with unsupported or malformed text treated as unparseable (design D6); verify unit tests for all three types, nested polygon rings, and malformed text
- [x] 7.2 Add the map widget: a bounding-box-fitted linear projection, filled regions for polygons and markers for points, both colored by the plotted field on a sequential scale, and the left-out-and-counted handling for unparseable or missing geometry; verify scenarios `Choropleth by region`, `Points` and `Unparseable geometry`

## 8. Degrading gracefully (ui + api)

- [x] 8.1 Have the query endpoint (task 2.2) report which of a widget's indicator, group column or geometry column are missing from the current catalog instead of failing, and have the widget render that as an `Alert` naming them instead of its chart (design D8); verify scenario `Column removed by a script change` and the equivalent for a removed indicator and a removed geometry column

## 9. Docs

- [x] 9.1 Write `docs/indicators-dashboard.md` (design D10): the widget types and what script 3 must provide for each, duplicate-row summing, editing roles, and the WKT/projection approach's no-basemap trade-off; update `docs/tamiza-brief.md` (section 5.7 superseded, section 8 plan item 7, the data model with `dashboard_widgets`) and the README status; verify links resolve

## 10. Stack verification and release

- [x] 10.1 In a separate Compose project, seed a project's `p_<id>_indicators` schema with a few hundred disaggregation values across several indicators, one with polygon geometry and one with point geometry, and a dashboard at the `MaxWidgetsPerProject` limit; sign in with a token minted by a fake OIDC provider. Record: the query endpoint's time for the heaviest widget, that dragging a widget with a few hundred polygons on the map stays responsive, and that the row-count and widget-count limits are reached and reported rather than silently truncating
- [x] 10.2 Update the README status and confirm `docs/tamiza-brief.md` section 8's plan item 7 now has the one-line description every other item has

## Workflow follow-up

- Archive this change after review, then check `openspec/specs/dashboard-indicators/spec.md` with `openspec validate --specs`.
