# Proposal

## Why

Analysts who already write a `compute_indicators` script get a table of computed indicators but, today, no way to look at it beyond what the brief originally fixed: one bar chart by disaggregation and an XLSX export. An indicator table rarely fits one chart — a coverage indicator wants a map by municipality, a trend wants a line by round, a distribution across many disaggregation values wants a histogram, and a single headline figure wants a stat tile, often several of these side by side on the same screen. The two dashboards that exist, Performance and Clean data, are fixed pages; this is the third and last of the brief's three dashboards, and the brief leaves its shape for this change to decide (`docs/tamiza-brief.md` section 5.7, section 8 plan item 7 — the only plan item with no one-line description).

## What Changes

- **Indicators dashboard.** A new **Indicators** section, the third project tab, open to every member. It reads the project's published indicator table (`p_<id>_indicators.indicators`, script 3's output) the same way the other dashboards read their own published schemas, and shows an empty state with instructions while script 3 has never published.
- **Widgets, not a fixed chart.** A member with the `analyst` or `admin` role can add, configure, move, resize and remove widgets on the dashboard; a `viewer` sees the result but cannot change it. Each widget is one of: a stat tile, a bar chart, a column chart, a line chart, a histogram, a pie chart, or a map, each querying the indicator table on its own — one or more indicators, a field to plot (value, numerator or denominator), a column to group by (disaggregation by default, or any other column a script added), and up to 50 filters built the same way as the clean data dashboard's.
- **Any column, not just the five required ones.** Script 3's output can carry columns beyond `indicator`, `disaggregation`, `value`, `numerator` and `denominator`; a widget can filter, group or plot by any of them, discovered from the published table the same way the clean data dashboard discovers its columns.
- **Maps without a new geospatial stack.** A map widget plots the rows of a chosen geometry column (WKT text, as the pipeline already publishes geometry columns for every step) as filled regions when the geometry is a polygon or as markers when it is a point, colored or sized by the plotted field. It needs nothing from script 3 beyond that one column — no PostGIS extension, no boundary dataset and no new Python dependency, since geopandas already produces WKT text today.
- **Charts and the map as inline SVG.** Every chart and the map are drawn the same way the performance dashboard's bar chart is: inline SVG, with a "Show as table" fallback, no canvas and no new UI dependency.
- **Duplicate rows.** When several rows of the published table share the same indicator and group value, their value, numerator and denominator are summed before plotting.
- **Export.** Every member can export the full published indicator table to CSV and XLSX, through the same single-use download link as the clean data dashboard.
- **Stale configuration.** A widget whose indicator, group column or geometry column the published table no longer has is shown with a message instead of failing the dashboard, the same way the clean data dashboard handles a removed column.

Not in this change:
- Built-in administrative boundaries; a map needs the geometry column script 3 provides, as `add-clean-data-dashboard` already deferred this capability to this change.
- A pannable, zoomable basemap with street tiles; the map is a relative-position plot of the rows' own geometry, not a slippy map.
- Exporting a widget's chart or map as an image.
- More than one indicators dashboard per project, cross-widget filtering, or a dashboard-wide filter; each widget is configured and filtered on its own.
- Any change to the pipeline's indicator contract, its validation, or how geometry columns are published; this change only reads what already exists.

## Capabilities

### New Capabilities

- `dashboard-indicators`: the indicators dashboard as a canvas of widgets a member adds, arranges and configures, each a chart or map over the project's published indicator table. This is the name the brief gives it (section 5.7), whose single fixed bar chart this change replaces with a configurable widget canvas.

### Modified Capabilities

None. The export reuses `auth`'s existing single-use download link requirement unchanged, and the dashboard reads the indicator schema and its geometry columns exactly as `pipeline` already publishes them.

## Impact

- **api:**
  - New endpoints under `/api/v1/projects/{id}/indicators`: an overview (publish state, widgets and their layout), a column catalog, a query used both to render a saved widget and to preview one being edited, widget CRUD (create/update/delete), and an export — mirroring `CleanData`'s endpoints and validation style.
  - A new `DashboardWidget` entity and EF Core mapping.
  - The export reuses `ExportRegistry`, `CsvExport`, `XlsxExport`, `LimitedStream` and `ExportSweeper` unchanged.
- **Database:** DbUp script `0007_add_indicators_dashboard.sql` adding `tamiza.dashboard_widgets`.
- **UI:**
  - A new **Indicators** tab, third after Performance and Clean data.
  - A widget canvas built on `@angular/cdk/drag-drop` (already a dependency, unused until now) for moving and resizing widgets.
  - Seven widget renderers (stat, bar, column, line, histogram, pie, map), all inline SVG, sharing scale/axis helpers; a small WKT parser and a simple projection for the map.
  - An add/edit widget dialog built on the existing `Dialog` pattern.
- **Docs:** a new `docs/indicators-dashboard.md`; the README status; `docs/tamiza-brief.md` (section 5.7 superseded, section 8 plan item 7 filled in).
- **Analytics:** none; the existing WKT-publishing step already covers geometry columns for any step's output, indicators included.
- **Configuration and dependencies:** none new. Charts and the map need no library; map geometry is parsed from the existing WKT text column with a small hand-written parser.
