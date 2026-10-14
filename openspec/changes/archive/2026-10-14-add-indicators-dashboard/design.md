# Design

## Context

- **Indicator table.** Script 3's output publishes as the single table `indicators` in schema `p_<id>_indicators` (`names.schema_name`), atomically swapped by `publish.publish` exactly like the other three steps. It is required to have the columns `indicator`, `disaggregation`, `value`, `numerator` and `denominator`; other columns, including a `geometry` column, are allowed (`runner.py`'s `_validated`, the pipeline spec's Output validation requirement).
- **Geometry already publishes as WKT.** Any step's output, script 3's included, that returns a `geometry`-dtype column (a GeoPandas `GeoSeries`) is converted to WKT text before publishing (`runner.py: _plain`). The CRS is not preserved and nothing else about it is geospatial today: PostGIS ships in the `db` image but is never enabled (no `CREATE EXTENSION postgis` anywhere), and nothing in `tamiza-api` or `tamiza-analytics` references NetTopologySuite, a `geometry`/`geography` column, or spatial SQL.
- **Published-schema reading.** `CleanData`'s `TableCatalog` (a `pg_attribute`/`pg_class` query) and `DataQueryBuilder` (typed filters validated against that catalog, `contains`/`equals`/`in`/`range`/`empty`/`notEmpty` by column type, parameters always typed, identifiers always catalog-quoted) are the precedent for reading any published schema generically. `CleanData` itself deliberately excludes `raw` and `indicators` from the tables it offers, so this change adds its own module rather than extending `CleanData`'s.
- **Export.** `ExportRegistry`, `CsvExport`, `XlsxExport`, `LimitedStream` and `ExportSweeper` already prepare a CSV/XLSX file behind a single-use, 5-minute, 32-random-byte link, source/table-agnostic; `auth`'s single-use download link requirement already covers any such file generically.
- **UI dashboards so far are either fully computed (performance) or pure URL-state (clean data); neither persists anything.** The project's tabs are a fixed array built in `ProjectPage`, each a lazily loaded standalone component. The one precedent for wrapping a non-Angular library is `CodeEditor` (CodeMirror): imported outside the shared barrel, initialized in `afterNextRender()`, themed with CSS variables. `@angular/cdk` 22.2.1 is already a dependency; its `drag-drop` module is unused. No chart or map library exists; the performance dashboard's one chart is inline SVG with a "Show as table" fallback, chosen explicitly over a library to keep charts canvas-free and jsdom-testable.
- **Durable per-project configuration so far is whole-row replace** (`sampling_frames`, one row per project, overwritten whole) **or one row per independently addressable entity** (`reference_tables`, `script_versions`), both via a small `HasJsonColumn<T>()` EF Core convention for list-shaped `jsonb` columns. Nothing in the schema has a user-reorderable position column yet.
- **The brief's own scope for this dashboard is narrower than this change.** `docs/tamiza-brief.md` section 5.7 currently specifies one fixed bar chart by disaggregation and an XLSX export; section 8's plan item 7 names this change with no further description, the only plan item left that way. `add-performance-dashboard` and `add-clean-data-dashboard` both already defer to `add-indicators-dashboard` by name in their own "Not in this change" lists.

Requirements are in `specs/dashboard-indicators`. User decisions for this change:
- Widgets are generic over any column of the published table, not hardcoded to `disaggregation`.
- Charts and the map are hand-rolled inline SVG; no chart or map library, no PostGIS, no NetTopologySuite.
- A map's geometry comes only from a `geometry`-shaped column a script publishes (already possible today); no bundled administrative boundaries.
- Widgets persist as one row per widget, not one JSON blob per project.
- Viewing is open to every member; adding, editing, arranging and removing widgets needs `analyst` or `admin`.

## Goals / Non-Goals

**Goals:**
- A widget is generic over any column the published table has, the five required ones and anything a script adds.
- No new runtime dependency, in the UI or in `tamiza-analytics`; no new CSP directive.
- The dashboard degrades like Clean data does: a widget whose column, indicator or geometry disappeared shows a message, not a broken page.
- Reads stay cheap without caching: indicator tables hold one row per indicator and disaggregation value, not per submission.

**Non-Goals:**
- A pannable, zoomable basemap with street tiles.
- Built-in administrative boundaries, or any new way to upload geospatial reference data.
- More than one indicators dashboard per project, cross-widget filtering, or a dashboard-wide filter.
- Exporting a widget's rendered chart or map as an image.
- Changing the indicator contract, its validation, or how geometry columns publish.

## Decisions

### D1. Data model (DbUp `0007_add_indicators_dashboard.sql`)

- `CREATE TABLE tamiza.dashboard_widgets (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), project_id uuid NOT NULL REFERENCES tamiza.projects(id) ON DELETE CASCADE, chart_type text NOT NULL CONSTRAINT ck_dashboard_widgets_chart_type CHECK (chart_type IN ('stat','bar','column','line','histogram','pie','map')), title text NOT NULL, config jsonb NOT NULL DEFAULT '{}', layout jsonb NOT NULL DEFAULT '{}', created_by uuid NOT NULL REFERENCES tamiza.users(id), created_at timestamptz NOT NULL DEFAULT now(), updated_by uuid NOT NULL REFERENCES tamiza.users(id), updated_at timestamptz NOT NULL DEFAULT now())`.
- `CREATE INDEX ix_dashboard_widgets_project_id ON tamiza.dashboard_widgets (project_id)`.
- `config` and `layout` are opaque `jsonb` validated by the API (the widget-config and layout DTOs), not by a database constraint beyond `chart_type` — the same division of labor as `sampling_frames.dimensions`/`targets`.
- Mirrored in `TamizaDbContext` (a `DashboardWidget` entity) for `SchemaDriftTests`.

*Alternative:* one `jsonb` column on `projects` holding the whole widget list, like `sampling_frames`. Rejected: every small edit (move one widget, rename one) would read-modify-write the whole list, and nothing else about a widget needs that — it is created, edited and removed on its own, like a `reference_table`.

### D2. Indicators catalog and query endpoint (api)

`GET /api/v1/projects/{id}/indicators/columns` reuses `TableCatalog.FindAsync` against `p_<id>_indicators.indicators` (an existence check first: a project without a published run answers `published: false` with no columns, not 404).

`POST /api/v1/projects/{id}/indicators/query` takes `{indicators: string[], groupBy: string, valueField: "value"|"numerator"|"denominator", filters: DataFilter[], geometryColumn?: string}` — the same `DataFilter` shape and the same 50-filter, 100-value, 500-character limits as `CleanData`'s `DataQueryBuilder` — and returns one row per distinct `groupBy` value (and per indicator, when more than one is requested), with `value`, `numerator` and `denominator` summed over matching rows (D9) and, when `geometryColumn` is named, that column's raw WKT text from an arbitrary matching row (D9 does not define summing geometries). It is used both to render a saved widget and, with an unsaved draft, to preview one being edited. Built with the same validate-against-catalog-then-parameterize approach as `CleanData.DataQueryBuilder`: a request naming a column the catalog does not have is a validation error, not a 500. Results are capped at `IndicatorDashboardOptions.MaxWidgetQueryRows` (default 5,000, far above any realistic indicator-by-group count); past the cap the response says so instead of truncating silently.

*Alternative:* let the UI fetch every row and aggregate client-side. Rejected: it would ship every column of every row to the browser just to group and sum a handful of them, and duplicate the typed-filter validation `CleanData` already centralizes server-side.

### D3. Widget CRUD and roles (api)

`POST`/`PATCH`/`DELETE .../indicators/widgets[/{widgetId}]`, each `.RequireProjectRole(ProjectRole.Analyst)`; the overview and query endpoints are `ProjectRole.Viewer`, matching every other dashboard's read side. `PATCH` accepts any subset of `title`, `chartType`, `config`, `layout` (a partial update, not a full replace) so moving a widget does not need to resend its query configuration. Creating or updating validates `config` against the chart type (D9's one-indicator-only constraints) and against the catalog, the same way the query endpoint does. At most `IndicatorDashboardOptions.MaxWidgetsPerProject` widgets per project (default 50); past it, creation fails with a message naming the limit.

### D4. Export (api)

`POST .../indicators/exports` is a thin wrapper that points the existing `CsvExport`/`XlsxExport`/`ExportRegistry`/`LimitedStream` machinery at `p_<id>_indicators.indicators` with no filters (the whole published table, as the brief's original export requirement says), reusing `DataDashboardOptions`'s existing limits (`MaxConcurrentExports`, `ExportLinkLifetime`, `MaxExportFileSize`, the 1,048,575-row XLSX cap) unchanged. The download endpoint is the existing shared `GET /api/v1/data-exports/{token}`; nothing about it changes.

*Alternative:* let each widget export its own filtered rows. Not in this change; the brief only ever asked for the whole table, and a widget's own rows are already visible in its chart's "Show as table" view (D5).

### D5. Chart rendering: inline SVG, no library (ui)

Every non-map widget (stat, bar, column, line, histogram, pie) renders as plain template-bound SVG `<rect>`/`<path>`/`<circle>` elements from a `computed()` layout, the same technique and the same reasons the performance dashboard's bar chart already uses: no canvas (so it stays jsdom-testable without headless Chrome), no bundle-size or license-notice cost, full control over the light/dark CSS-variable tokens. A shared `chart-scales.ts` (a linear scale, a category scale, a handful of SVG path helpers) factors out what the chart types have in common, since there are now six of them instead of one. Every chart keeps the performance dashboard's accessible fallback: `role="img"`, a computed `aria-label` summary, and a "Show as table" toggle rendering the same rows as a `<table>`.

*Alternatives:*
- Chart.js or ECharts, as the performance dashboard's own design already considered and rejected for the same reasons, now sharper with six chart types instead of one: a dependency and its license notice, canvas rendering untestable under jsdom, and a far larger API surface than these chart types need.
- D3.js for the SVG generation. It would still need a canvas-free, jsdom-testable integration written by hand for each chart type, for little over writing the (simpler) scale math directly — a new dependency for no capability the project does not already have a hand-rolled precedent for.

### D6. Map rendering: WKT, a simple projection, no map library (ui)

A map widget's rows carry a `geometry` column of WKT text (D2); the UI parses the subset the pipeline can actually produce — `POINT`, `POLYGON` and `MULTIPOLYGON` — with a small hand-written parser (`wkt.ts`, no dependency: the grammar is a handful of nested coordinate lists). Parsed coordinates are projected with a single linear (equirectangular) transform fitted to the bounding box of the widget's own rows, then drawn as inline SVG: `<path>` fills for polygons, colored by the plotted field on a sequential scale; `<circle>`s for points, sized or colored the same way. There is no basemap, no tiles and no pan/zoom: the map shows the rows' shapes in their relative positions, which is what a choropleth of a project's own disaggregation regions needs, and keeps the map on the same inline-SVG, jsdom-testable, dependency-free footing as every other chart (D5). A row whose geometry is missing or fails to parse is left out of the map and counted, shown next to it, the same pattern as the performance dashboard's unusable completion times.

*Alternatives:*
- MapLibre GL JS (or Leaflet) with a tile basemap, for real pan/zoom and street-level context. The CSP already allows it with one addition (`worker-src 'self' blob:`, since MapLibre's workers use `blob:` URLs the current fallback to `script-src 'self'` would block; `connect-src` already allows the `fetch()` calls a vector-tile source makes, so no origin needs adding there). Rejected for this change on cost, not feasibility: a new dependency, canvas/WebGL rendering that cannot be asserted on under jsdom, and a basemap that free-text indicator data (disaggregation values like `"Cali"` or `"18-29"`) has no street address to put on. Worth revisiting if a future change wants real-world context rather than a choropleth of the project's own regions.
- Enabling PostGIS and `ST_AsGeoJSON` server-side, with `Npgsql.NetTopologySuite`. Rejected: indicator tables are small (D2), so there is no performance reason to move the geometry parsing server-side, and it would add a database extension and a package for a problem the client-side parser already solves at this scale.
- A bundled reference dataset of administrative boundaries (so a project with no geometry column still gets a map by matching `disaggregation` text to known region names). Out of scope (Non-Goals); a script that wants a map already has geopandas to produce its own geometry, and a shared boundary catalog is its own change with its own licensing and matching questions.

### D7. Widget layout (ui)

A 12-column grid, fixed row height; each widget's `layout` is `{x, y, w, h}` in grid units, with defaults chosen by chart type when a widget is created (a stat tile defaults smaller than a map). Dragging and resizing use `@angular/cdk/drag-drop` (already a dependency, unused until now — no new package); a drop reflows overlapping widgets downward, the same collision behavior most grid dashboards use. A move or resize persists through `PATCH .../widgets/{id}` debounced 400 ms after the drag ends, not per pointer-move event. Reading order for keyboard and screen-reader navigation, and the order widgets list in when several "Show as table" views are open at once, follows `(y, x)` — top-to-bottom, left-to-right — not a separately stored position.

### D8. Degrading gracefully (ui + api)

A widget's `config` can name an indicator, a `groupBy` column or a `geometryColumn` the published table no longer has (a script changed, or has not published yet). The query endpoint (D2) answers with which of the three, if any, are missing rather than failing; the widget renders an `Alert` naming them instead of its chart, the same choice Clean data makes for a stale filter column. The dashboard overview itself answers `published: false` with no error when script 3 has never published (the brief's existing "empty state with instructions" requirement), listing any already-configured widgets so an admin can see what they will show once it has.

### D9. Aggregation and chart-type constraints (api)

Rows sharing the same indicator and the same `groupBy` value are summed (`value`, `numerator` and `denominator` each) before a widget plots them, so a script that emits more than one row per combination (for instance, one per form version) still charts sensibly; this is done in the query's SQL (`GROUP BY`, `sum(...)`), not after the fact in the UI. A stat tile and a map widget require exactly one indicator (a stat tile shows one number; a map needs one value per region); bar, column, line and histogram charts accept one or more, each indicator becoming its own series over the same `groupBy` values; a pie chart accepts exactly one indicator, its slices being the `groupBy` values.

### D10. Docs

- **`docs/indicators-dashboard.md` (new)** covers: the widget types and what each needs from script 3 (plainly, the five required columns plus, for a map, a `geometry` column); how duplicate rows are summed; the roles that can edit widgets; the WKT/projection approach and its no-basemap trade-off, so an analyst knows why their map has no street names.
- **`docs/tamiza-brief.md`:** section 5.7 superseded by this design (noted, not deleted, the way earlier sections note later changes); section 8's plan item 7 filled in; the data model gains `dashboard_widgets`.
- **README status.**

## Risks / Trade-offs

- **[A choropleth with no basemap can be read as "wrong" by someone expecting a real map]** → `docs/indicators-dashboard.md` says plainly that shapes are shown in relative position only, and the widget's own title and legend carry the units; D6's alternatives give the upgrade path if this turns out to matter.
- **[A script's `geometry` column mixes point and polygon rows, or assumes a CRS other than degrees]** → the parser renders what WKT it can and counts what it can't (D6); CRS is already silently dropped at publish time (today's behavior, unchanged by this proposal), and `docs/indicators-dashboard.md` tells an analyst to project to degrees before returning geometry.
- **[Widgets referencing many indicators or a wide `groupBy` column make a heavy page]** → `MaxWidgetsPerProject` and `MaxWidgetQueryRows` (D2, D3) bound both axes; past either the user sees why, not a slow or broken page.
- **[One member's drag updates race another's]** → last write wins per widget (D7's debounced `PATCH` is scoped to one widget's row); two members actively dragging the same widget at once is accepted as a rare, low-stakes collision, not engineered around.
- **[A hand-written WKT parser misses a geometry type a future script produces, e.g. `MULTIPOINT` or `LINESTRING`]** → unsupported types are treated like unparseable geometry (D6, counted apart); `docs/indicators-dashboard.md` lists exactly the three types supported, and extending the parser is a small, self-contained change if needed.

## Migration Plan

- `0007_add_indicators_dashboard.sql` adds the table and index only; no existing data changes. It runs in the API's startup migration like every prior script.
- After deployment, every project's Indicators tab shows the empty state until an admin or analyst adds a widget, whether or not script 3 has ever published — adding a widget does not require indicators to exist yet (D8).
- **Rollback:** older images ignore the new table. To remove it by hand, also delete its row from `tamiza.schema_versions`.

## Verification Results

Measured on the development machine in a separate Compose project (`tamizaverify`, port 8189), through nginx, with a fake OIDC provider and a fake Kobo, on a seeded `p_<id>_indicators.indicators` of 6,648 rows across 7 indicators (extra `year`/`round` columns, 300 polygons, 200 points, and rows with NULL or unparseable geometry):

| Check | Result |
|---|---|
| Heaviest widget (map, 300 polygons, 138 KB response) | 119 ms cold, 11–13 ms warm |
| Multi-indicator and group-by-year queries | 7–8 ms |
| A 6,000-group query | 5,000 rows, `rowLimitReached: true`, 28 ms; the widget shows an alert |
| 51st widget | 400 with a message naming the limit of 50 |
| Export (CSV and XLSX) | each link downloads once, then 404 |
| Viewer | add, edit, delete and move give 403; the UI shows no edit controls; export is allowed |
| Memory (`docker stats`) | api 108 MiB, db 115 MiB, ui 15 MiB |

- Dragging a widget with free space around it sent one `PATCH`, and the layout survived a reload. A resize that pushes other widgets down sends one `PATCH` per moved widget (5–7), all at once.
- With 50 widgets (about 19,000 DOM nodes) a map drag showed no long tasks, and frames matched the idle baseline of headless Chrome's 30 fps cap.
- At 360 px and 1280 px, in light and dark, there was no horizontal page scroll and the dialog fit.

Two bugs found by this run were fixed, each with a regression test:
- Eight widgets created at the same moment at the limit let four through. Creation now runs in a transaction with a per-project advisory lock (`Widgets_added_at_the_same_moment_cannot_pass_the_limit`).
- Bar and column charts drew hundreds of overlapping labels with many groups. Labels are now thinned (two specs in `chart-widgets.spec.ts`).

**Not measured:** frame rate on a real GPU, the empty state before publishing, and the dialog's invalid-draft error path when switching chart types (the dialog trims to one indicator on its own).

**Observed, not changed:** new widgets default to the left half of the grid; the pie widget is small at its default height; a stat tile sums its plotted field over every matching row, so a rate indicator over several groups shows a sum, which an analyst avoids by filtering to one row or by emitting a total row.
