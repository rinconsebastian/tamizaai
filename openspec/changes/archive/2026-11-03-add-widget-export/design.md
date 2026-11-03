# Design

## Context

See proposal.md for the motivation and the specs for the behavior. Paths: `IND/` = `tamiza-ui/src/app/pages/projects/indicators/`, `UI/` = `tamiza-ui/src/app/`, `API/` = `tamiza-api/src/Tamiza.Api/`, `UT/` and `IT/` = the API unit and integration test projects.

- **The tile header** is in `IND/widget-grid.ts:51-81`, not in `widget-view.ts`. It holds:
  - the drag handle;
  - the title, truncated on screen (`:65`);
  - the "Show as table" toggle, only while `drawable()` (`:66-78`);
  - `tmz-dropdown-menu` with exactly Edit and Remove, **only `@if (editable())`** (`:79-81`, items `:232-236`).

  Viewers have no tile menu today. `DropdownMenu` (`UI/shared/ui/dropdown-menu.ts`) is a CDK menu with disabled items that carry a second-line `description` (`:4-14`), a hard-coded kebab icon (`:35-37`), and a choice emitted after focus returns to its trigger. Pinned tests: `project-indicators.spec.ts:520-527` (the default `menu-trigger` test id exists only for editors) and `:1278-1287` (that menu is exactly Edit/Remove).
- **Widget states** live in `IND/widget-view.ts`: `state` (`:295`), `blocked()` (`:322-323`), the published/missing branches, and the notes "Not filtered by" (`:312-317`) and row limit (`:189-191`). `drawable` leaves out stat and crosstab (`:296-300`).
- **Charts are hand-drawn SVG** with no chart library. Each of the six SVG charts (`axis-chart.ts`, `pie-widget.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `boxplot-chart.ts`) renders one `<svg class="tmz-chart-svg …">`. Around it, these are HTML:
  - captions and the series legend (`series-legend.ts`), the pie legend (which scrolls inside its box), the heat-map scale, the box-plot key;
  - notes;
  - the stat tile (`stat-widget.ts`), the cross table (`crosstab-widget.ts`) and every "Show as table" view (`widget-table.ts`, whose `TableModel` holds the cells as formatted text).

  The marks take their colors from Tailwind classes and CSS custom properties (`var(--tmz-seq-*)` set inline on the host, `heatmap-chart.ts:250`, `map-widget.ts:157`). So a serialized live `<svg>` renders black fills and loses class-only strokes.
- **Maps** are Leaflet, loaded lazily (`IND/map-engine.ts:31-36`), the only file that calls Leaflet. The engine reports `motion()` and `settled()` (`:70-72`).
  - OSM tiles load cross-origin without `crossOrigin` (`:292-296`, pinned by `map-widget.spec.ts`), so drawing them would taint a canvas.
  - Google tiles come through the API relay with expiring links and "Relayed tiles are not stored" (`map-basemaps`).
- **The CSP** is `img-src 'self' data: https://tile.openstreetmap.org`, and `blob:` appears nowhere (`UI/core/nginx-config.spec.ts:58-68`). Adding `blob:` was rejected twice (`archive/2026-10-17-improve-map-widget/design.md`). A download through an `<a download href="blob:…">` is a navigation, not an image load. The font stack is the system one, because the CSP blocks web fonts (`tamiza-ui/src/styles.css:25-28`).
- **Theme.** Nothing sets `.dark` today. `.dark` swaps tokens on any element it is set on (`styles.css:387`).
- **Printing** (`IND/dashboard-print.ts`) cannot tell whether one widget is ready. Its pending set is anonymous and page-wide (`:102-109`), and it loops only while printing. It exports `printTitle` and keeps `isoDay` private (`:18-22`).
- **Downloads.** `Downloader.start(url)` navigates to a single-use link (`UI/core/download.service.ts`). No helper saves a Blob made in the browser.
- **The version view** plots `version.widgets` from their configs (`UI/pages/projects/project-indicators.ts:528-532`). `canPrint` is off while viewing (`:541`).
- **The API.**
  - `POST …/indicators/query` lets any viewer query any widget config (`API/Indicators/IndicatorEndpoints.cs:26`).
  - The indicator table export (`:27`) and the clean data exports prepare files through `ExportRegistry.Add` (`API/CleanData/ExportRegistry.cs:36`) and `LimitedStream` at `DataDashboard__MaxExportFileSize` (`API/CleanData/DataDashboard.Export.cs:170`).
  - Query slots (`MaxConcurrentQueries`, 4) and export slots (`MaxConcurrentExports`, 2) are separate semaphores (`API/CleanData/DataDashboard.cs:86-87`).
  - The sampling-frame endpoints wrap `Add` in a private `Register` helper (`API/Projects/SamplingFrameEndpoints.cs:147-169`).
- **The email workbook already builds every kind's table.**
  - `ReportDataBuilder.BuildWidgetAsync` (`API/Reports/ReportDataBuilder.cs:91-178`) takes a widget's title, kind and config, queries through `WidgetQuery.For` with `WidgetQuery.AppliedDefaults` (`:103`), computes `WidgetQuery.NotNarrowedBy` (`:127`), and builds `AxisTable`, `SeriesReportTable`, `BoxplotTable`, `HistogramTable`, `PieTable` and `MapTable` (`:196-337`).
  - `ReportWorkbook` writes with MiniExcel and names sheets with `SheetName` (`API/Reports/ReportWorkbook.cs:136-152`). It hard-codes the Summary header `"Elemento"`/`"Item"` (`:29`).
  - `ReportText.Resolve` picks en or es regional formats from a locale (`API/Reports/ReportText.cs:163-190`).
  - `dashboard-reports` "Widget tables in a scheduled email" already makes these tables equal "Show as table", except the map's shape kind.
- **File names.** `ExportFileName.Slug` gives ASCII, lower case, dashes, at most 40 characters, with a fallback (`API/CleanData/ExportSweeper.cs:52-89`).
- **Bundle.** The initial bundle is 660.25 kB against a 675 kB warning (`archive/2026-11-02-add-boxplot-widget/design.md`). The dashboard page is lazy, and Tailwind scans every source file for class names. `innerHTML` is banned by `scripts/check-ui-safety.sh:27-29`.

## Goals / Non-Goals

**Goals:**
- An image is what the member sees, made in the browser with no request. The workbook is built on the server by the email's code, so it, the email and "Show as table" read the same tables.
- No new dependency, no CSP change, no migration, and no growth of the initial bundle.
- Every place that switches on the widget kind names all twelve kinds, with no default branch.

**Non-Goals:**
- A device-independent rendering at a fixed size (an offscreen second render).
- Embedding fonts or converting text to outlines.
- Changing what the email's workbook holds.

## Decisions

### D1. A separate Export menu in every tile header
- A second `tmz-dropdown-menu` sits after the table toggle and before the editors' menu. It has `triggerTestId="widget-export"`, the label "Export “{title}”" and `class="paper:hidden"`. Its items are `widget-export-png`, `widget-export-svg` and `widget-export-xlsx`.
- `DropdownMenu` gains two inputs:
  - `icon: 'more' | 'download'`, default `'more'`;
  - `busy`, default false. Busy sets `aria-busy` and `aria-disabled`, pulses the icon and gives `cdkMenuTriggerFor` null, so the menu does not open. The trigger stays focusable, as **Print** does.
- At 360 px, an editor's title keeps about 176 px: a 328 px tile, less padding, the 36 px toggle, two 40 px buttons and the gaps.
- *Alternatives:*
  - *One role-built menu for everyone, holding Export, Edit and Remove.* Rejected: a busy export would lock Edit and Remove, and it breaks both pinned tests.
  - *A hand-written `cdkMenu` like the indicator export's* (`project-indicators.ts:243-260, 295-304`). Rejected: it duplicates the disabled-with-reason markup.

### D2. Where Export is offered
- On every widget of a project's own dashboards, for every role, at every width, including while a version is shown.
- Not in the widget form's preview (`IND/widget-dialog.ts`) or the script editor's `indicator-view.ts`. Those do not render the grid header, so nothing changes there.
- `dashboard-versions` "Viewing a version" forbids only actions that change or print the dashboard, so its delta says Export stays.
- *Alternative:* hiding Export in the version view. Rejected: a removed widget seen in a version is exactly what a member may want to file.

### D3. Readiness per widget
`WidgetView` exposes `exportState(): { image: string | null; workbook: string | null }`, where `null` means available and a string is the translated reason. It is computed from `state`, `blocked()`, `result()` and, for maps, the engine's motion. The grid reads it through `#view` on `<app-widget-view>`, so the trigger never shifts the header.

| Widget state | Images | Workbook | Reason |
|---|---|---|---|
| blocked, or ready with `!published` / `missing.indicator` | off | off | "{label} has not published yet." |
| `loading` | off | off | "The widget is still loading." |
| `error` | off | off | "The widget could not be loaded." |
| ready, `missing.columns` | off | off | "Its table no longer has {columns}." |
| ready, 0 rows | off | off | "No rows match this widget's query." |
| map as chart: engine not made, or between `motion()` and `settled()` | off | on | "The map is still being drawn." |
| map as chart: every row left out | off | on | "The map has no shape to draw." |
| ready with rows (row limit or not) | on | on | — |

While a basemap is shown, the image items carry the description "Shapes and legend, without the basemap".

*Alternative:* waiting through `DashboardPrint.whenReady`. Rejected: it is page-wide and only loops while printing.

### D4. Images come from the screen
An image uses the widget's current answer, chart or table mode, the version's widgets and colors, and the member's map view. It sends no request and saves nothing, following `dashboard-printing` "Charts on paper" ("as the member shows it at that moment").

*Alternatives:*
- *A paper-style pass* (switching the page to `printing`). Rejected: it re-measures every widget and refits every Leaflet map, which requests tiles.
- *An offscreen re-render at a fixed size.* Rejected: a second query, a second Leaflet map, and labels fitted to another box.

### D5. One pipeline: a composed, standalone SVG; the PNG is that SVG rasterized
- **Parts.** A new `IND/export-parts.ts` (in the dashboard chunk) defines the token `WIDGET_EXPORT_PARTS`, which `WidgetView` provides. Each chart or tile injects it optionally and registers `{ ready(), settle(), plot(), parts() }`, so `indicator-view.ts` is unaffected. `WidgetView` adds its own notes. The composer switches over `ChartType` with no default branch.
- **Plot.** It is the live `.tmz-chart-svg`, cloned. Every HTML part (title, caption, legends, scales, key, notes, stat body, tables) is redrawn as SVG from the same signals its template reads. The DOM is never scraped.
- **Steps:**
  1. **Settle.** `ChartTooltips.closeAll()` and `MarkTooltips.reset()`, then one render. Leftovers are removed anyway: `chart-guide`, `heat-active`, `likert-active`, transparent hit shapes (`chart-hit`, `outlier-hit`), the map's `tmz-active`, and lifted opacity.
  2. **Probe.** The clone goes into a probe appended to `document.body`, outside `<app-root>`: `aria-hidden`, `inert`, `visibility:hidden`, at `left:-100000px`. It copies the host's inline `style` (the `--tmz-seq-*` ramp) and, for maps, the class `tmz-map`. No chart declares component styles, so global tokens resolve the same there, and a clone is never hovered or focused.
  3. **Inline** the computed values as presentation attributes, from an allowlist: `fill`, `fill-opacity`, `fill-rule`, `stroke`, `stroke-width`, `stroke-opacity`, `stroke-dasharray`, `stroke-linecap`, `stroke-linejoin`, `opacity`, `font-family`, `font-size`, `font-weight`, `text-anchor`, `dominant-baseline`, `writing-mode`. A computed `display:none` drops the element. A computed `url(...)` is rewritten to the local `#id`.
  4. **Keep only allowlisted elements:** `svg g rect path line polyline polygon circle text tspan title desc defs pattern clipPath`, with only their geometry and presentation attributes. Drop:
     - `class`, `style`, `data-*`, Angular's `_ngcontent-*` and `_nghost-*`;
     - `tabindex`, `role`, `aria-*`, every `on*`;
     - every `href` that is not `#id`;
     - each mark's `<title>`, and comments.
  5. **Prefix ids** (such as the heat map's hatch patterns) with a per-file prefix, and rewrite every `url(#…)` to match.
  6. **Root:**
     - `xmlns`; `width`, `height` and `viewBox` in CSS px;
     - a white `<rect>` first;
     - the computed `--font-sans` stack as `font-family`;
     - `role="img"`, `<title>` = the widget's title, `<desc>` = the chart's own summary.
- **Code rules.**
  - Nodes are built with `createElementNS` and `textContent`, and serialized with `XMLSerializer`. There is no `innerHTML`, no `<style>`, no `<foreignObject>` and no external reference.
  - Numbers are the screen's own formatted text. Coordinates are rounded.
- *Alternatives:*
  - *Serializing the live `<svg>` as is.* Rejected: black fills and lost strokes.
  - *Inlining styles from the live tree.* Rejected: it reads a hovered mark's opacity.
  - *`<foreignObject>` or an html-to-image library.* Rejected: canvas taint, Office does not draw it, and bundle size.
  - *A generic HTML→SVG walker.* Rejected: line boxes, sticky cells and borders are a fidelity risk for every kind.

### D6. Light theme on white, always
Every image has a white background, and its colors are the light tokens resolved in the probe outside `<app-root>`. No hex value is written in TypeScript: text and line colors come from the computed `text-heading`, `text-body` and `stroke-default`. The gaps between stacked segments and heat-map cells are surface-colored holes, so they come out white, as on paper ("Colors on paper").

*Alternative:* following the screen's theme, or a transparent background. Rejected: files would differ by viewer, and the gaps would become holes.

### D7. Size and resolution
- **SVG.** 1 unit = 1 CSS px. The plot keeps its measured box, so labels are fitted as on screen. The width W is the tile body's content width plus 32 px of padding, at least 352. The height H is the sum of the parts (D8).
- **PNG scale.** `s = min(2, √(16,000,000 / (W·H)), 8,192 / max(W, H))`. The PNG is refused when `s < 1` or the SVG text passes 12 MB. After `toBlob`, the canvas is released (`width = 0`).
- **Resolution.** A `pHYs` chunk after `IHDR` gives `round(96·s / 0.0254)` px/m: 7,559 at 2×, which is 192 dpi. Its CRC32 comes from a 256-entry table, tested with `"123456789"` → `CBF43926`.
- **Checked.** The widest tile is about 1,232 px. The tallest is 40 rows (`API/Indicators/IndicatorValidation.cs:15`), which is 40·72 + 39·16 = 3,504 px. With its parts, it passes 16 Mpx at 2×, so the budget is needed.
- *Alternative:* `devicePixelRatio`. Rejected: files would differ by device.

### D8. What an image holds, top to bottom
1. The full title: 14 px, weight 500, heading color, wrapped to at most 3 lines, then cut with "…".
2. Notes shown above the chart: "Not filtered by: …" and the row limit, with the screen's strings.
3. The caption, when the kind has one.
4. The body: the chart, or the table drawing (D9).
5. The legend, key or scale, with every entry, wrapped to W − 32.
6. Notes shown below the chart:
   - rows left out, negative values left out;
   - outliers or groups not drawn, map rows left out;
   - when a basemap is shown, "The basemap is not included in the image."
7. The footer, 11 px, in the body color:
   - "Census 2026 · Housing", plus `dashboards.print.version` while a version is shown;
   - one line of every dashboard filter as the filter bar describes it, with "All" for a filter without a value, only when the dashboard has filters;
   - `dashboards.print.publishedAt` for this widget's `IndicatorEntry.publishedAt`, then "Exported {time} ({zone})", with `dateText(…, 'dateTime', timeZone)`.

No logo and no watermark. Wrapping is measured in the probe with `getComputedTextLength()` at 92 % of the width. Tests inject a `measure` stub of 6.5 px per character.

### D9. Tables in images are capped
A table drawing holds the shaded header and at most 100 body rows of 22 px, and the columns that fit in 2,400 px. The first column is always kept, each column is at most 240 px, and a cut cell ends in "…". Notes follow, with singular forms through `countKey`: "{n} rows not shown in the image.", "{n} columns not shown in the image." and "The Excel workbook has them all." The cells are the `TableModel`'s formatted strings. Paper's "N rows not printed" is the precedent.

### D10. Map images: the view's shapes and the legend, never the basemap
- A new `MapEngine.picture()` in `IND/map-engine.ts` projects every shape whose bounds meet the view with `latLngToContainerPoint`, into the engine's size. It returns rings (`fill-rule="evenodd"`) and points with their `tmz-seq-n` step, and drops vertices closer than 0.5 px.
- The exporter resolves the fills from `var(--tmz-seq-n)` in the probe, with fill opacity 1 as without a basemap, and clips everything to the box with a `<clipPath>`.
- Tiles, the Google logo and credits are never drawn. No tile is requested. This keeps `map-basemaps` "No basemap, no credit" true, and **Print** keeps the basemap.
- *Alternatives:*
  - *Cloning Leaflet's overlay `<svg>`.* Rejected: it uses padding, a layer-pixel `viewBox` and `translate3d` placement.
  - *OSM imagery with CORS.* Rejected by the product owner.

### D11. Saving files under the current CSP
- `Downloader.save(blob, fileName)` creates an object URL, clicks a temporary `<a download>` and revokes the URL after 30 s. SVG files are `image/svg+xml;charset=utf-8`.
- The PNG is drawn from `new Image()` with a `data:image/svg+xml;charset=utf-8,` URL, then `decode()`, a `W·s × H·s` canvas, `drawImage`, `toBlob('image/png')` and the `pHYs` insertion.
- A `SecurityError`, a null blob or a decode failure gives "The PNG image could not be made. Try the SVG image."
- The CSP and `nginx-config.spec.ts` stay unchanged.

### D12. The workbook is made on the server, by the email's builder, from the screen's query
- **Route.** `POST /api/v1/projects/{projectId}/dashboards/{dashboardId}/widget-exports`, operation `ExportDashboardWidget`, `RequireProjectRole(ProjectRole.Viewer)`, mapped on the dashboards group. `RecordDashboardVersions` records nothing, since the handler notes no change (`API/Indicators/DashboardVersions.cs:879-900`).
- **Body.** `WidgetExportRequest(Format, Title, ChartType, Config, Filters, Locale, Version?)`:
  - `format` must be `"xlsx"`;
  - `title` follows the widget title rules (at most 200 characters, `IndicatorValidation.cs:13`);
  - `config` is a `WidgetConfigInput`;
  - `filters` lists `WidgetExportFilter(Column, Kind, Values?, Min?, Max?, Ignored)`, one per dashboard filter with a value in force, at most 8;
  - `locale` is the UI's `LOCALE_ID`.

  The UI builds it from the pieces of the screen's query key: `queryFor(config)` and the grid's filters for that widget.
- **Server.**
  - It validates the config and kind as `/query` does, and returns the same 400.
  - It applies `WidgetQuery.For` and the filters that are not ignored.
  - Each filter's column must be a filter of the dashboard, or of version *n*, with the same kind. Otherwise it answers 409 `widgetExports.filtersChanged`.
  - Filter labels come from the stored dashboard or version, never from the client.
  - `version` requires `analyst` (otherwise 403). An unknown version answers 404 `dashboards.versionNotFound`.
- **Builder.** `BuildWidgetAsync` takes the applied filters and the not-narrowed labels as parameters. The email keeps passing `AppliedDefaults` and its `NotNarrowedBy`. A public `BuildWidgetExportAsync` wraps it. `WidgetQuery.NotNarrowedBy` gains an overload over the filters in force, the twin of `widget-view.ts:312-317`.
- **Why the config, not the widget id.** `BuildWidgetAsync` already takes title, kind and config. The version view plots configs that may no longer be saved. `/query` already lets a viewer query any config, so no new access is granted.
- *Alternatives:*
  - *Keyed by widget id.* Rejected: it cannot export a version's removed widget.
  - *A client-side XLSX.* Rejected: a third implementation of the per-kind tables, plus a zip writer or a 0.3–1 MB library, against the rule that the API makes every XLSX.

### D13. What the workbook holds
- **Sheet 1** is the widget's table, named after the title by `SheetName`'s rules (31 characters, no `[]:*?/\`, unique against the Summary's name).
  - Its columns and rows come from the email's per-kind builders (see the table below).
  - A stat tile gets one header ("{caption} ({unit})") and one numeric cell in the indicator's format. The email's text-only stat is unchanged.
- **Sheet 2, "Summary"** ("Resumen" for Spanish), with `item` and `value` headers from `ReportText`. This fixes the hard-coded header at `ReportWorkbook.cs:29`. Its items, in order:
  - project, dashboard, version (when sent), widget;
  - indicator ("{label} ({name})"), plotted;
  - one "Filters: {label}" line per dashboard filter with its value or "All", or "No filter applies";
  - not filtered by (when any);
  - data published (through `GetPublicationsAsync`, read in the request);
  - exported (`Format.Moment`, which names the zone);
  - rows;
  - notes (row limit, left out, negatives set aside, "No rows match" in a race).
- MiniExcel writes strings as strings, and `CutToCell` cuts long cells. New tests cover formula-like text in report sheets, which today are tested only for `XlsxExport`.
- *Alternative:* Summary first, as in the email. Rejected: `read_excel`, Power Query and a person opening the file all land on sheet 1.

| Kind | Image body | Workbook sheet 1 |
|---|---|---|
| stat | value (36 px) and description, from `StatWidget`'s signals | one column "{caption} ({unit})", one numeric row |
| bar, column | clone; caption, or the series legend with 2+ series | `AxisTable`, or with a series `SeriesReportTable` (series columns, "Total" column and row; 100 % gives shares and no "Total" row) |
| line | as bar | as bar, in natural group order |
| histogram | clone; caption | `HistogramTable`: range text and the count of values |
| pie | clone; full legend with "Other" | `PieTable`: every group, value and share |
| map | `MapEngine.picture()`; scale legend; basemap note | `MapTable`: group, value, tooltip columns, no shape kind |
| heatmap | clone with prefixed hatch ids; scale legend | `SeriesReportTable` |
| pyramid | clone; two-side legend | `SeriesReportTable` (left, right, "Total") |
| likert | clone; scale legend in order | `SeriesReportTable` with shares, no "Total" row |
| crosstab | table drawing (D9) | `SeriesReportTable` |
| boxplot | clone; key; "N outliers not drawn" | `BoxplotTable`, no outlier values |

### D14. Data republished between the screen and the file
Images never re-read. The workbook answers `{ downloadUrl, fileName, rowCount, publishedAt, expiresAt }`. When `publishedAt` is later than the page's `IndicatorEntry.publishedAt`, the success message adds that the file holds data published after the page was loaded.

*Alternative:* a 409 on newer data. Rejected: it would refuse members after every scheduled run.

### D15. Download, slots and errors
- The single-use link goes through a `Register` helper lifted from `SamplingFrameEndpoints.cs:147-169` into `ExportRegistry`, and the sampling frames use it too. Expiry and deletion are as `auth` "Single-use download links" says. The UI calls `Downloader.start(downloadUrl)`.
- The export holds one query slot (`WithSlotAsync`) and no export slot. It is one widget query, like a widget refresh. The file is written through `LimitedStream` at `DataDashboard__MaxExportFileSize`, and rows are bounded by `IndicatorDashboard__MaxWidgetQueryRows`.

| Status | Cause |
|---|---|
| 400 | format, title, config, filter values or locale |
| 403 | `version` sent by a `viewer` |
| 404 | not a member, a dashboard outside the project, `dashboards.versionNotFound`, `indicators.notPublished` |
| 409 `widgetExports.filtersChanged` | a filter column that is not the dashboard's or the version's, or has another kind |
| 422 `widgetExports.missingColumns` | a column the widget needs is gone, named in the message |
| 422 `data.exportTooLarge` | the file passes the size limit |
| 503 `data.timeout` | the read took too long |

No rows (a race between the page and the file) still gives a file: the data sheet holds the reason in a "Notes" column, as a scheduled email's sheet does, and the Summary says 0 rows.

### D16. Language and file names
- **Language.** An image's own words are in the interface language, and its numbers are the screen's text. The workbook's own words, range texts and Summary dates follow `locale` through `ReportText.Resolve`, as the email's do. Its number cells carry Excel format codes. Names and values stay as created.
- **File names.** `{slug(project)}_{slug(dashboard,"dashboard")}_{slug(title,"widget")}[_v{n}]_{yyyy-MM-dd}.{png|svg|xlsx}`, with `ExportFileName.Slug`'s rule and the project's local day, as `isoDay` (now exported from `dashboard-print.ts`) computes it.
  - The API gets `ExportFileName.ForWidget` and the UI a TypeScript twin.
  - Both read one fixture, `IND/testing/export-file-names.json`, as `series-tables.json` is shared today.
- *Alternative:* print-style Unicode names. Rejected: a slug also defuses `/`, `:`, emoji and bidirectional control characters.

### D17. Feedback, lazy loading and what is not recorded
- **Feedback.** `exporting: Record<widgetId, format>` in the grid drives `busy` on one trigger. Its label becomes "Preparing the export of “{title}”…". Each export ends with one toast (`UI/shared/ui/toast.ts`). Failures go through `ApiErrors.report` with the API's message, or with the D7 and D11 messages.
- **Lazy code.** The composer, probe, sanitizer, layout, table drawing and rasterizer live in `IND/widget-export/`. They are loaded with `import()` on first use, through a module-level promise that is cleared on failure, as Leaflet is.
- **CSS.** `styles.css` gains `@source not "./app/pages/projects/indicators/widget-export"`, so the exporter's string literals add no utilities.
- **Not recorded.** Existing exports record nothing, and `get_widget_data` already gives MCP clients what a widget shows. So there is no audit record and no `mcp-server` delta.

## Risks / Trade-offs

- [Images and the workbook come from two paths; histogram bins, a line's natural order and pie shares are twins] → The same query key and the email's builder. Integration tests compare the workbook with `/query` and with the email table for all twelve kinds. D14 says when the data is newer.
- [The light-only guarantee depends on `.dark` never being put on `<html>`] → It is recorded here and in the docs, so a future theme switch keeps a light scope for the probe.
- [Computed-style fidelity: `url(#…)` serialization, `system-ui` inside an SVG image in Safari, `writing-mode` and `dominant-baseline` in Illustrator, Inkscape or Office] → A real-browser matrix of every kind in both modes; other programs are checked by hand.
- [Safari canvas caps and memory, and whether Safari taints a canvas drawn from a `data:` SVG] → The pixel budget, `width = 0` after use, and the SVG suggestion in the failure message.
- [A `blob:` download under the CSP, and iOS opening a preview instead of saving] → Checked in Chrome, Edge, Firefox and Safari. A `data:` link is the fallback for small files.
- [Maps without a basemap lose context] → The menu and the image say so, and Print keeps the basemap.
- [Performance: about 6,000 `getComputedStyle` reads for a 30×100 heat map, and large polygon maps] → Tens of milliseconds; vertices under 0.5 px are dropped; the code is lazy.
- [Header crowding at 360 px] → About 176 px of title for editors, tested at phone width.
- [The `ReportDataBuilder` refactor touches the email] → `ReportDataTests` and `ReportWorkbookTests` stay green, unchanged.
- [Query-slot contention: workbooks share the 4 query slots with widgets and emails] → Each export is one query; it holds no export slot.
- [A 409 after a concurrent filter edit] → The message tells the member to reload.
- [The footer's publication time is as old as the page] → Accepted and documented; the workbook's Summary reads it fresh.

## Migration Plan

No migration and no configuration. Deploy the API and the UI together. A UI without the Export button simply never calls the new route. Rollback is redeploying the previous images; no data is written.
