# Proposal

## Why

Consultants paste Tamiza's charts into the study's Word report. Today a widget's PNG is the widget at its on-screen width, drawn at twice that size and marked 192 dpi (`widget-export` "PNG image format"). Its size therefore depends on the tile and on the member's screen. Teatro's 44 widgets range from 3 grid columns wide (the stat tiles) to 12 (the 2.5 chart by comuna), and the same chart exported from a laptop and from a wide monitor comes out with different sizes and line breaks. In the report each figure is then stretched by hand to the page, so the text of one figure ends up twice as large as the text of the next. A report needs every figure at the page's text width, 16 cm, which is 1,890 pixels at 300 dpi, with text of the same size in every figure.

The workbook has the opposite gap: it holds only the plotted values. The study's specification asks for the base n of every figure, and authors typed it into 19 titles ("Comuna 7 y Sin nombre con n < 30", "n = 34"). A reader of the workbook cannot see that the 46.85 % of Teatro 1b71 is 223 of 476 households, cannot recompute a percentage from its numerator and denominator, and, once weights exist, cannot tell the weighted sums from the sample's base.

## What Changes

- **Image size.** The widget menu's Export group gains **Image size…**. It opens a small dialog, and the choice is kept in this browser and applies to the PNG and SVG images:
  - *As on screen*, the default: today's file, at the widget's on-screen width, drawn at twice its size and marked 192 dpi;
  - *Report page*: 1,890 pixels wide, marked 300 dpi, so Word and PowerPoint place it 16 cm wide, the text width of an A4 or letter page with usual margins;
  - *Custom*: a width from 600 to 4,000 pixels and a resolution from 96 to 600 dpi.
  - The height keeps the widget's proportions on screen.
  - The pixel budget stays: at most 16,000,000 pixels and 8,192 on a side. A widget that does not fit at the chosen size is refused with today's message, which suggests the SVG image.
- **Laid out at that size, not scaled.** Before the image is made, the widget is drawn again for the width it will have on the page (its pixels × 96 / its dpi; about 605 CSS pixels for *Report page*), so its text keeps the size it has on screen: 11-pixel labels read as about 8 points.
  - It is drawn from the answer the widget already has, with no second query, as printing already redraws each widget for its box on paper (`dashboard-print.ts:94`). The export design rejected an off-screen redraw for its first version; this change adds it.
  - Axis labels, ticks, bar thickness, the stat tile's value, and the wrapped title, subtitle, notes and footer are fitted to the new width.
  - A map keeps the area the member sees, fitted to the new box. A table keeps its first 100 rows and the columns that fit the new width, and says what it left out.
  - The screen does not change while the image is made.
- **Bases in the widget workbook.** The workbook gains a third sheet, "Bases", after "Summary", with one row per plotted mark: a bar or point, a pair of group and series value, a slice, a cell, a box, or the stat tile's figure.
  - Each row gives the group and series values, the value, n (the unweighted base: the `base` column, else the denominator), the numerator and the denominator, each headed with its column's name ("Hogares que asistieron", "Hogares"). Teatro 1b71 gives 46.85 %, n 476, numerator 223, denominator 476.
  - With a weight (`add-weighting`), the numerator and the denominator are the weighted sums the value comes from, n stays unweighted, and one more column gives the sum of weights, headed with the weight column's name.
  - A mark under the dashboard's small base (`add-small-sample-warnings`) is marked as on screen.
  - A widget with no n (a measure with no `base` column) gets no "Bases" sheet.
  - Numbers are numbers in the workbook's formats. The data sheet and "Summary" keep their names, columns and order, so a sheet read by Power Query or `read_excel` stays the same.
- **Docs.** `docs/indicators-dashboard.md` "Exporting a widget" explains the sizes, what "laid out at that size" changes, and the "Bases" sheet.

Not in this change:
- A saved default size per dashboard or project, a transparent or dark background, and an image without its footer.
- The "Bases" sheet in the scheduled email's workbook, unless the user chooses it at design time.
- Exporting a whole dashboard, PowerPoint files, copying to the clipboard, a widget CSV, an MCP export tool and scheduled exports.
- The unit-level rows behind a widget (one row per household), and data beyond the widget's table: histogram bounds, whisker and outlier values, map geometry.

## Depends on

- `add-widget-context`: the workbook request sends the saved config, and images carry the subtitle, footnote and dashboard source that are laid out again here.
- `add-indicator-column-metadata`: the column names in the "Bases" headers.
- `add-small-sample-warnings`: n, numerator and denominator in every query response, and the small-base mark.
- `add-weighting`: the weighted sums and the sum of weights.
- `improve-stat-cards`: the stat tile's container-relative value size, which the new layout uses.
- `add-locale-formatting`: the number formats of the workbook.

## Capabilities

### New Capabilities

None. Widget files belong to `widget-export`.

### Modified Capabilities

- `widget-export`:
  - "Export in the widget menu": the Export group gains **Image size…**;
  - "Widget image as shown": an image keeps the member's rows, filters and view, laid out at the chosen size;
  - "SVG image format" and "PNG image format": the width, scale and resolution follow the chosen size; *As on screen* keeps today's rule;
  - "Tables in widget images" and "Map images": a table fits its columns to the chosen width, and a map keeps the member's area;
  - "Sheets of a widget workbook": the third sheet, "Bases";
  - new requirements: "Image size", "Image laid out at its size", "Bases of a widget workbook".
- `dashboard-indicators`: "Widget menu", whose scenario lists the items of the Export group.

## Impact

- **ui** (`tamiza-ui`):
  - `indicators/widget-export/widget-exporter.ts` (the width, today `max(352, bodyWidth + 32)` at line 128), `rasterize.ts` (`pngScale` at line 13 and the `pHYs` resolution), `image-layout.ts`, `map-picture.ts` and `svg-table.ts`.
  - `indicators/widget-view.ts` and each renderer: a layout pass at a given width from the widget's answer, with the box measuring that printing uses (`dashboard-print.ts`, `chart-data.ts` `observeSize`).
  - `indicators/widget-grid.ts` (the menu item), a new image size dialog, and `project-indicators.ts`.
  - `en.json` and `es.json`, and the specs (`widget-export-menu.spec.ts`, `widget-exporter.spec.ts`, `rasterize.spec.ts`).
- **api** (`tamiza-api`):
  - `Reports/WidgetWorkbook.cs` (the "Bases" sheet), `Reports/WorkbookSheets.cs`, `Reports/ReportText.cs` ("Bases" and its headers in English and Spanish).
  - `Reports/ReportDataBuilder.cs`: `BuildWidgetExportAsync` reads n, numerator, denominator and the sum of weights from the response.
  - `Indicators/WidgetExportEndpoints.cs`.
  - Unit and integration tests, and the production shapes fixture.
- **Database, analytics and MCP:** none. MCP `get_widget_data` already returns n, numerator and denominator after `add-small-sample-warnings`.
- **Docs:** `docs/indicators-dashboard.md` ("Exporting a widget", "Limits"), `docs/tamiza-brief.md`.

**Compatibility.** The size starts at *As on screen*, which makes today's files: the on-screen width, twice the size, 192 dpi. The spec scenario "Usual widget" (600 by 340 pixels on screen gives 1,200 by 680, marked 192 dpi) stays true. The choice lives in the member's browser, never in a widget or a dashboard: no config field, no version, nothing in scheduled emails, and a browser that cannot keep it falls back to *As on screen*. The workbook's first two sheets keep their names, columns and order, so existing readers of the file are not affected, and the scheduled email's workbook does not change. MCP clients see nothing new. Long titles (up to 153 characters in Teatro) are still shown whole, wrapped to the new width. On rollback, an older page ignores the kept choice and makes on-screen images, and an older API writes the workbook with two sheets.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, on the main specs as they are then. Decisions to take with the user at that time:
- Where the size is chosen: an **Image size…** item in the Export group (recommended), a second PNG item for reports, or a dialog on every image export.
- The presets: *As on screen* and *Report page* at 16 cm (recommended), and whether to add a half page (8 cm) or a 16:9 slide.
- The text size at a chosen size: laid out at the placed width so labels read about 8 points (recommended), or a wider layout with smaller text.
- The height: the widget's proportions on screen (recommended), or a chosen height or aspect ratio.
- The bases: a third sheet (recommended) or columns added to the data sheet; and whether the scheduled email's workbook gets it too.
