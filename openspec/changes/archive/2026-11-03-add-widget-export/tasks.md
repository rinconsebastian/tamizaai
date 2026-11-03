# Tasks

## 1. Shared API pieces

- [x] 1.1 Lift the private `Register` helper of `Projects/SamplingFrameEndpoints.cs:147-169` into `CleanData/ExportRegistry.cs` (design D15). Make the two sampling-frame endpoints call it.

  Verify: the sampling-frame integration tests pass unchanged.
- [x] 1.2 Add `ExportFileName.ForWidget(project, dashboard, title, version, day, extension)` in `CleanData/ExportSweeper.cs`. It uses `Slug` with the fallbacks `dashboard` and `widget`, and `_v{n}` before the day (design D16). Add the shared fixture `tamiza-ui/src/app/pages/projects/indicators/testing/export-file-names.json`, with:
  - the `census-2026_housing_coverage-by-municipality_2026-11-02` case and its `_v9` twin;
  - accents and `/` ("Cobertura / Población rural");
  - an emoji-only title;
  - a 60-character title;
  - a bidirectional control character.

  Verify: a unit test reads the fixture the way `SeriesTableTests` reads `series-tables.json`, and every case passes.
- [x] 1.3 Add the `ReportText` keys `item`, `indicator`, `plotted`, `exported`, `version`, `versionLine`, `rows`, `notFilteredBy` and `allValues`, in en and es (`widget` and `noFilters` already exist) (design D13, D16). Replace the hard-coded `"Elemento"`/`"Item"` of `ReportWorkbook.cs:29` with `item`.

  Verify: the `ReportText` parity test covers the new keys, and `ReportWorkbookTests` still pins the email's Summary header text.
- [x] 1.4 Add the overload `WidgetQuery.NotNarrowedBy(inForce, notFiltered)` over the filters in force, ignored ones included: the twin of `widget-view.ts:312-317` (design D12).

  Verify: `WidgetQueryTests` cover an ignored filter, a filter whose column the table lacks, and no filter.

## 2. Widget workbook in the API

- [x] 2.1 Make `ReportDataBuilder.BuildWidgetAsync` (`Reports/ReportDataBuilder.cs:91-178`) take the applied filters and the not-narrowed labels as parameters. The email keeps passing `WidgetQuery.AppliedDefaults` and its `NotNarrowedBy`. Add a public `BuildWidgetExportAsync(projectId, widget, appliedFilters, notNarrowed, text, format, ct)` (design D12).

  Verify: `ReportDataTests` and `ReportSenderTests` pass unchanged.
- [x] 2.2 Extract the sheet writing of `Reports/ReportWorkbook.cs` (headers, number formats keyed by header, the empty-table header row, `CutToCell`) into a shared writer. Add `Reports/WidgetWorkbook.cs` (design D13):
  - sheet 1 named by `SheetName` after the title, unique against the Summary's name;
  - a stat tile as one numeric cell in the indicator's format;
  - sheet 2 "Summary"/"Resumen" with the items in D13's order.

  Verify: `ReportWorkbookTests` pass unchanged. New `WidgetWorkbookTests` open the file and check:
  - the sheet order;
  - typed cells and formats for each of the twelve kinds;
  - the stat as a number;
  - "Resumen" for `es-CO`;
  - a 40,000-character cell cut to 32,767;
  - text starting with `=`, `+`, `-` or `@`, and a URL-like value, in a title, a group value, a series header and a tooltip value, written as plain strings with no `<f>` element and no hyperlink.

## 3. Widget export endpoint

- [x] 3.1 Add `Indicators/WidgetExportEndpoints.cs` with `POST /dashboards/{dashboardId}/widget-exports`, named `ExportDashboardWidget`, `RequireProjectRole(ProjectRole.Viewer)`, mapped on the dashboards group. Add the models `WidgetExportRequest`, `WidgetExportFilter` and `WidgetExportResult`, with OpenAPI descriptions (design D12, D14).

  Verify: `DashboardTests`' OpenAPI path test lists the route, and `IndicatorRoleMatrixTests` gives 200 to `viewer`, `analyst` and `admin`, and 404 to a non-member.
- [x] 3.2 Validate the request (design D12, D15):
  - `format` must be `xlsx`;
  - the title follows the widget title rules;
  - the config and kind are checked as `/query` checks them, with the same 400;
  - at most 8 filters, each a filter column of the dashboard (or of version *n*) with the same kind, else 409 `widgetExports.filtersChanged`;
  - the locale must pass `ReportText.IsSupported`;
  - `version` needs `analyst`, else 403; an unknown version gives 404 `dashboards.versionNotFound`.

  Filter labels come from the stored dashboard or version.

  Verify: integration tests in a new `WidgetExportTests` cover each refusal, and that a refused request issues no link and leaves no file.
- [x] 3.3 Build and register the workbook (design D13, D15):
  - read the publication through `GetPublicationsAsync`;
  - hold one query slot and no export slot;
  - write through `LimitedStream` at `DataDashboard__MaxExportFileSize`;
  - name the file with `ExportFileName.ForWidget` and the project's day;
  - issue the link through `ExportRegistry.Register`.

  Map a missing column to 422 `widgetExports.missingColumns` naming it, an unpublished indicator to 404 `indicators.notPublished`, a size overflow to 422 `data.exportTooLarge`, and a timeout to 503 `data.timeout`.

  Verify: `WidgetExportTests` check that, for every kind, the data sheet's rows and order equal `/query`'s answer and the email's table for the same config (the pattern of `ReportDataTests`). They also cover:
  - a filter in force that narrows the rows;
  - an ignored filter listed under "Not filtered by";
  - a cleared default that is not applied;
  - the row-limit note;
  - a widget of version 9 that was since removed;
  - the link working once;
  - a newer `publishedAt` returned after a republish;
  - no dashboard version recorded;
  - the 409, 422, 404 and 503 answers.
- [x] 3.4 Document in `docs/operations.md` that a widget workbook uses one query slot, the export file size limit and the single-use link, and takes no export slot.

  Verify: the section names `DataDashboard__MaxConcurrentQueries`, `DataDashboard__MaxExportFileSize` and `IndicatorDashboard__MaxWidgetQueryRows`, as the code reads them.

## 4. Shared UI pieces

- [x] 4.1 Add the `icon` (`'more' | 'download'`) and `busy` inputs to `shared/ui/dropdown-menu.ts` (design D1). Busy sets `aria-busy` and `aria-disabled`, pulses the icon, keeps the trigger focusable and gives the menu trigger null.

  Verify: `dropdown-menu.spec.ts` covers both icons, and a busy trigger that is focusable and does not open.
- [x] 4.2 Add `Downloader.save(blob, fileName)` to `core/download.service.ts`: an object URL, a temporary `<a download>`, and revocation after 30 s (design D11).

  Verify: a spec with a stubbed `URL.createObjectURL` sees the file name, the click, the removed anchor and the revoke. `nginx-config.spec.ts` stays unchanged and green.
- [x] 4.3 Add `exportWidget(projectId, dashboardId, body)` and the `WidgetExportRequest` and `WidgetExportResult` types to `core/projects/indicators.service.ts`.

  Verify: an `HttpTestingController` spec posts to `/api/v1/projects/{id}/dashboards/{dashboardId}/widget-exports` with the body unchanged.
- [x] 4.4 Export `isoDay` from `indicators/dashboard-print.ts`, and add `widget-export/file-name.ts`, the TypeScript twin of `ExportFileName.ForWidget` (design D16).

  Verify: a spec reads `testing/export-file-names.json` and every case gives the API's name.

## 5. Export button and readiness

- [x] 5.1 Add `exportState()` to `indicators/widget-view.ts`, following design D3's table, including:
  - the map's states: engine not made, between `motion()` and `settled()`, nothing drawn;
  - the basemap description on the image items.

  `map-engine.ts` exposes whether the map is moving.

  Verify: a spec covers every row of D3's table, for a chart, a stat tile, a cross table and a map.
- [x] 5.2 Add the Export trigger to every tile header of `indicators/widget-grid.ts`, after the table toggle and before the editors' menu (design D1):
  - `icon="download"`, `triggerTestId="widget-export"`, `paper:hidden`;
  - the three items with their test ids, disabled with their reasons from `#view.exportState()`;
  - the `exporting` map driving `busy` on one trigger.

  The grid reports the choice through an `exportWidget` output and shows a busy trigger from an `exporting` input; `project-indicators.ts`, which knows the project, dashboard, version, time zone, filters and locale, makes the file.

  Verify:
  - a `viewer` sees `widget-export` and no `menu-trigger`;
  - `project-indicators.spec.ts:520-527` and `:1278-1287` pass unchanged;
  - at 360 px every trigger is reachable without sideways scrolling;
  - while a version is shown, the trigger is offered;
  - the widget form's preview has no trigger;
  - a print-mode spec hides it.
- [x] 5.3 Add the `indicators.widgetExport` strings to `public/i18n/en.json` and `es.json`:
  - the button, the busy label and the three formats;
  - `withoutBasemap`;
  - the reasons;
  - the results (`imageReady`, `workbookReady` and its singular, `newerData`, `pngFailed`, `pngTooLarge`);
  - the image texts (`footerExported`, `basemapLeftOut`, the singular and plural "rows/columns not shown in the image", `inWorkbook`);
  - the error code `widgetExports.filtersChanged`; `widgetExports.missingColumns` is left untranslated so the API's own message, in the member's language and naming the column, is shown.

  Verify: `translations.spec.ts` parity passes.
- [x] 5.4 In `docs/indicators-dashboard.md`:
  - add an "Exporting a widget" section with the button, who gets it and when a format is unavailable;
  - say under `## Building the dashboard` and `### Viewing a version` that viewers and version views get it;
  - in `## Export`, replace "a widget does not export on its own; a chart cannot be saved as an image" with a pointer to the new section.

  Verify: the doc's statements match the specs `widget-export` "Export button" and "Formats that cannot be exported yet".

## 6. Widget images

- [x] 6.1 Add `indicators/export-parts.ts` with the `WIDGET_EXPORT_PARTS` token and its types (`ready`, `settle`, `plot`, `parts`), provided by `WidgetView` (design D5). Register the parts in:
  - `axis-chart.ts`, `pie-widget.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `boxplot-chart.ts`;
  - `stat-widget.ts`, `crosstab-widget.ts`, `map-widget.ts` and `widget-table.ts`.

  `WidgetView` adds its own notes.

  Verify: a spec per kind gets that kind's caption, legend, key or scale, and notes from its parts. The composer's `switch` over `ChartType` has no default branch, so a new kind fails to compile. `indicator-view.ts` specs pass unchanged.
- [x] 6.2 Add the lazy folder `indicators/widget-export/` with `svg-clean.ts` (design D5, D6):
  - the probe outside `<app-root>`, with the host's inline style and `tmz-map`;
  - settle and leftover removal;
  - inlining from the attribute allowlist;
  - the element and attribute allowlist;
  - id prefixing with `url(#…)` rewriting;
  - the white root rect, the font stack, `role="img"`, `<title>` and `<desc>`.

  Verify: jsdom specs with an injected `styleOf` give only allowlisted nodes and attributes for input holding classes, `data-testid`, hit shapes, the guide, comments, `onload` and a `<script>` text in a label, with the text escaped. Ids are prefixed and every `url(#…)` points at an existing id.
- [x] 6.3 Add `image-layout.ts` and `svg-table.ts` (design D8, D9): title wrapping to 3 lines, notes above and below, legend wrapping, the footer lines, and the table drawing capped at 100 rows and 2,400 px.

  Verify: specs with a 6.5 px-per-character `measure` cover:
  - a 120-character title;
  - all 12 rows of a pie legend;
  - the footer in `America/Bogota` with a version line, with "All", and with no filter line;
  - 128 rows giving "28 rows not shown in the image.";
  - 101 rows giving the singular;
  - a 30-column cross table cut with its note.
- [x] 6.4 Add `MapEngine.picture()` in `indicators/map-engine.ts` (design D10): the shapes in the view, projected with `latLngToContainerPoint`, rings and points with their step, vertices under 0.5 px dropped, and a clip to the box.

  Verify: a spec with `src/testing/leaflet.ts` checks a zoomed view, a point, a multipolygon with a hole, and that no tile layer is touched.
- [x] 6.5 Add `rasterize.ts` (design D7, D11): the scale budget, the `data:` image, `decode()`, the canvas, `toBlob`, the `pHYs` chunk with its CRC32, the canvas release, and the two failure messages.

  Verify: specs check:
  - `pngScale(600, 340) = 2`;
  - `pngScale(1232, 3600)` below 2 and within 16 Mpx;
  - a refusal when the scale is below 1 or the SVG passes 12 MB;
  - `pHYs` of 7,559 px/m;
  - the CRC of `"123456789"` is `CBF43926`;
  - a stubbed decode failure giving "The PNG image could not be made. Try the SVG image."
- [x] 6.6 Add `widget-exporter.ts`, loaded with `import()` through a module-level promise cleared on failure. Wire PNG and SVG from the grid's menu through `Downloader.save`, with one toast each (design D17). Add `@source not "./app/pages/projects/indicators/widget-export"` to `src/styles.css`.

  Verify:
  - a spec exports an SVG and a PNG with a stubbed rasterizer, and gets one toast each and the D16 file name;
  - a tooltip open before the export leaves no tooltip, guide or lifted mark in the file;
  - `npx ng build` keeps the initial bundle at its current size, and the exporter lands in its own lazy chunk;
  - `scripts/check-ui-safety.sh` passes.
- [x] 6.7 In `docs/indicators-dashboard.md`, describe the images: what they hold, the footer, the white background, the size and 192 dpi, the capped tables, and maps without their basemap (also under `## Maps`). Drop "It does not save a chart or a map as an image." from `## What it does not do`.

  Verify: the doc's statements match the specs `widget-export` "Parts of a widget image", "Tables in widget images", "Map images" and "PNG image format".

## 7. Workbook from the UI

- [x] 7.1 Build the workbook request from the pieces of the widget's query key: `queryFor(config)` and the grid's filters for that widget, ignored ones marked, `locale` from `LOCALE_ID`, and `version` only while a version is shown (design D12). Call `exportWidget` and then `Downloader.start(downloadUrl)`.

  Verify: a spec checks that the body equals the screen's query key for a filtered widget, an ignored filter and a version, and that `version` is absent on the current dashboard.
- [x] 7.2 End the workbook export with one toast giving its row count (with the singular form), plus the newer-data sentence when `publishedAt` is later than the page's (design D14). Failures go through `ApiErrors.report`, with the `widgetExports.*` messages.

  Verify: specs cover 32 rows, 1 row, a newer `publishedAt`, a 409 telling the member to reload, and a 422 naming the column. Only the exporting widget's trigger is busy meanwhile.
- [x] 7.3 In `docs/indicators-dashboard.md`, describe the workbook: its two sheets, its rows and filters, the language of its own words, newer data, the file names, and that `## Printing a dashboard` points to it for every row. Add roadmap item 7n `add-widget-export` to `docs/tamiza-brief.md` after 7m, plus the §5.7 sentence. Update the widget export line of `README.md`.

  Verify: the doc's statements match the specs `widget-export` "Sheets of a widget workbook", "Summary of a widget workbook", "Language of widget files" and "Widget export file names".

## 8. Integration checks

- [x] 8.1 In Chrome and Edge, and checked in Firefox and Safari, export PNG and SVG of all twelve kinds, as a chart and as a table.

  Verify:
  - sampled pixels show no black marks and white gaps;
  - each SVG keeps its colors and text in another browser, Inkscape and PowerPoint;
  - a PNG placed in Word takes its on-screen size;
  - the largest tile stays within the PNG budget.
- [x] 8.2 Export a map under OSM and under Google.

  Verify: the file holds no tile, logo or credit; the browser reports no CSP violation; and the network panel shows no tile request during the export.
- [x] 8.3 Open a widget workbook of each kind in Excel, LibreOffice and Google Sheets, in English and in Spanish with `es-CO`.

  Verify: the data sheet comes first, numbers are numbers in their formats, and "Summary"/"Resumen" holds the items.
- [x] 8.4 Run the close checks.

  Verify: `npx ng build` within budgets, `npm test -- --watch=false`, `scripts/check-ui-safety.sh`, the API unit and integration suites, and `openspec validate add-widget-export --strict` all pass.

## Workflow follow-up

- Archive the change after review, which syncs `widget-export`, `interface-language` and `dashboard-versions` into `openspec/specs/`.
