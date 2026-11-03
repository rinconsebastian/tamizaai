# Proposal

## Why

Consultants and analysts put Tamiza's charts into Word and PowerPoint reports, and their clients ask for the numbers behind each chart. Today a chart cannot be saved as an image, and the only export is an indicator's whole published table, without the widget's grouping or the dashboard's filters (`docs/indicators-dashboard.md` "Export"). So members take screenshots and copy "Show as table" by hand, which is the manual flow Tamiza replaces.

## What Changes

- **An Export button on every widget.** Every member, viewers included, gets an **Export** button with a download icon in each widget's header, after the "Show as table" toggle and before the editors' actions menu. Its menu offers **PNG image**, **SVG image** and **Excel workbook (XLSX)**. It is offered at every width from 360 pixels, and while a version of the dashboard is shown. It is never printed. It is not offered in the widget form's preview or in the script editor.
- **Available only when there is something to export.** While a widget is loading, has failed, has an unpublished indicator, lacks a column or matches no rows, its formats stay in the menu, dimmed, each with the reason on a second line. A map that is still being drawn, or that has no shape to draw, offers its workbook but not its images.
- **Images of what the member sees.** PNG and SVG show the widget as it is on screen at that moment: the same answer, the filter values in force, chart or table as the member shows it, the version's widgets and colors while a version is shown, and the member's map view. The image is made in the browser. It sends no request and saves nothing. It holds, top to bottom:
  - the full title;
  - the widget's notes;
  - the chart, or the table;
  - the full legend, key or scale;
  - a footer naming the project, the dashboard (and the version), the dashboard's filters in force, when the indicator was published and when the image was exported, in the project's time zone.
- **Images that stand alone.**
  - Always on a white background in the light colors of the dashboard's palette.
  - The SVG is a self-contained file: text stays text, and it has no script, no style sheet and no external reference.
  - The PNG is that SVG drawn at twice its on-screen size, marked 192 dpi so Word and PowerPoint place it at its on-screen size, within 16 million pixels and 8,192 pixels per side. A widget too large for that is refused with a suggestion to use the SVG.
- **Tables in images are capped.** An image of a table (a cross table, or any widget shown as a table) holds at most 100 rows and the columns that fit in 2,400 pixels, and says what it left out. The workbook has every row.
- **Maps without their basemap.** A map's image holds its shapes, clipped to the member's view, and its legend, never basemap tiles, the Google logo or map credits. When the member shows a basemap, the menu and the image say it is not included. **Print** keeps the basemap.
- **A workbook of the widget's data.** The XLSX is prepared on the server from the same query the screen sent, with the dashboard's filters in force, and downloaded through the single-use link of the other exports.
  - Its first sheet is the widget's table, with the same columns, totals, "(none)" and "Other" as the widget's table in a scheduled email's workbook, numbers as numbers in the indicator's format, and text never as a formula. A stat tile gives one number.
  - Its second sheet, "Summary", names the project, the dashboard (and the version), the widget, the indicator, what is plotted, each dashboard filter with its value, the filters that do not narrow the widget, when the data was published, when it was exported, the number of rows and the widget's notes.
  - Its own words follow the member's interface language and regional format, as the email's workbook does. Names, labels and values stay as they were created.
  - When the data was republished after the page was loaded, the file holds the new data and the success message says so.
  - A filter set that changed since the page was opened is refused with a message to reload.
- **File names** made of the project, the dashboard, the widget, the version when one is shown, and the project's day: `census-2026_housing_coverage-by-municipality_2026-11-02.png`.
- **Feedback.** While a widget's export is prepared, only that widget's Export button is busy, and one message ends it.

Not in this change:
- Basemap tiles, the Google logo or map credits in an image.
- Image options: a chosen or fixed size, a resolution, a transparent or dark background, an image without its footer.
- Exporting a whole dashboard at once (a zip or one workbook), PDF (Print covers it), PowerPoint, or copying to the clipboard.
- A widget CSV. The indicator's CSV and XLSX export stays as it is.
- Data beyond the email's widget table: numeric histogram bounds, whisker or outlier values, map geometry, rows beyond the widget row limit.
- Exports from the widget form's preview or the script editor, an MCP tool, scheduled exports, and an audit record of exports.
- Changes to the scheduled email's workbook.

## Capabilities

### New Capabilities

- `widget-export`: exporting one widget of a project's own dashboard as a PNG image, an SVG image or an Excel workbook. It covers:
  - the Export button, who gets it and where;
  - when each format is available, and why not;
  - the feedback while an export runs;
  - what an image holds, its tables, its maps, its colors, its formats and size;
  - the workbook's sheets, rows, filters and numbers, newer data, download and limits;
  - file names and roles.

### Modified Capabilities

- `interface-language`: "Content shown as it was created": a widget's workbook joins the scheduled email's workbook as an exception, and its images write their own words in the interface language.
- `dashboard-versions`:
  - "Viewing a version": a widget's export stays offered while a version is shown, and its files name the version.
  - "Changes that are not dashboard changes": exporting a widget makes no version.

## Impact

- **ui** (`tamiza-ui`):
  - `shared/ui/dropdown-menu.ts`: a download icon and a busy state.
  - `core/download.service.ts`: saving a file made in the browser.
  - `core/projects/indicators.service.ts`: the workbook request.
  - `indicators/widget-grid.ts`: the Export button on every tile and its busy state.
  - `pages/projects/project-indicators.ts`: what the files name (project, dashboard, version, time zone, filters) and the messages.
  - `indicators/widget-view.ts`: when each format is available, and the widget's parts for an image.
  - Each chart and tile (`axis-chart.ts`, `pie-widget.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `boxplot-chart.ts`, `stat-widget.ts`, `crosstab-widget.ts`, `map-widget.ts`, `widget-table.ts`) offers its parts for an image. `map-engine.ts` projects the map's shapes.
  - A lazily loaded `indicators/widget-export/` folder makes the images.
  - `styles.css` keeps that folder out of the generated CSS.
  - `en.json` and `es.json` strings, and the specs.
- **api** (`tamiza-api`):
  - A new widget export endpoint on the dashboards group.
  - `Reports/ReportDataBuilder.cs` builds one widget's table for it.
  - A widget workbook, sharing its sheet writing with `Reports/ReportWorkbook.cs`.
  - `Reports/ReportText.cs` strings.
  - `CleanData/ExportSweeper.cs` (`ExportFileName`) and `CleanData/ExportRegistry.cs` (the single-use link, shared with the sampling-frame files).
  - OpenAPI, unit and integration tests.
- **Database, analytics, nginx and the Content-Security-Policy:** none.
- **Dependencies:** none. The initial bundle does not grow; the image code is a new lazy chunk.
- **Docs:**
  - `docs/indicators-dashboard.md` gains "Exporting a widget" and drops "a chart cannot be saved as an image".
  - `docs/operations.md` (the workbook uses a query slot and the export file size limit).
  - `docs/tamiza-brief.md` (roadmap item 7n).
  - `README.md`.
