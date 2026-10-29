# Design

## Context

See proposal.md for the motivation. The pieces this change works on:

- **Charts are drawn in script, at the pixel size of their box.**
  - `observeSize` (`chart-data.ts:61-79`) measures the box with a `ResizeObserver`.
  - `AxisChart` (`axis-chart.ts:68`) and `PieWidget` (`pie-widget.ts:39`) draw an SVG whose `width`, `height` and `viewBox` are that size, so text keeps one size.
  - A browser does not run script while it lays the page out for paper, and a `ResizeObserver` does not fire then. A chart therefore prints at the size it had on screen, whatever the size of its box on paper.
- **Maps are Leaflet.** `MapWidget` measures its box (`map-widget.ts:298-320,582-587`). On a resize it calls `engine.resize()` (`invalidateSize`, `map-engine.ts:246`) and re-fits the shapes unless the member has zoomed or moved the map (`touched`). The engine reports when a batch of basemap tiles has settled (`onSettled`, `map-engine.ts:94,309-311`). The controls are an Angular component, `MapControls`, placed over the map. Tooltips are CDK overlays (`chart-tooltip.ts:70-76`).
- **The grid** (`widget-grid.ts:126-161`) has 12 columns, 72 px rows and a 16 px gap. Below `47.99rem` it switches to one column, with `grid-column: auto !important`, and that rule is not limited to screens.
- **Print layout, checked in headless Chrome while planning (scratchpad, not committed).**
  - On a landscape Letter page, `(max-width: 47.99rem)` matched, so the phone layout applies on paper in either orientation.
  - A grid wider than the page was cut off at the right edge rather than shrunk.
  - With `break-inside: avoid` on the grid items, Chrome moved a widget that crossed the end of a page, with its row, whole to the next page. No widget overlapped another. A widget taller than a page started at the top of a page and continued on the next.
- **A widget's table** (`widget-table.ts:17`) is a `tmz-table-container` with `max-h-full overflow-y-auto`. On paper it would cut rows off silently.
- **The dashboard page** (`project-indicators.ts`):
  - Its header row has the heading, then `dashboard-actions` ("New dashboard" and the actions menu) for analysts only (`:63-89`).
  - Then come the notices, the filter bar, the empty-state card, the toolbar ("Show tables", the widget count, "Add widget"), the grid and the indicator inventory disclosure (`:91-266`).
  - It has a persistent live region and `announce()` (`:88,523-527`).
  - `overview.indicators[].publishedAt` gives each indicator's publication time (`indicators.service.ts:152`).
  - The project's time zone is `context.project().timeZone`, and `dateText(value, locale, style, timeZone)` (`formats.ts:41`) writes the shared format in a given zone.
- **The shell.** `app.html` has a sticky top bar and `main.max-w-screen-xl px-4 py-6`. `project-page.ts` has the back link, the project name, the role badge, the form name and `tmz-tabs-nav`. `project-settings-layout.ts:23` has the secondary navigation. `tmz-toast-region` holds the toasts.
- **Styles.** `index.html` has `<title>Tamiza</title>`. Tailwind CSS 4 (`styles.css`) has the built-in `print:` variant. `html` gets `bg-neutral-secondary-soft`. Chrome does not print CSS backgrounds unless the member turns on "Background graphics", and the map legend swatches are CSS backgrounds (`map-widget.ts:131`).
- **Angular 22 without zone.js.** `ApplicationRef.tick()` renders synchronously.
- **Tests.** `translations.spec.ts` checks that `en.json` and `es.json` have the same keys. Counts use `countKey` (`<key>` and `<key>One`). jsdom has no layout and no `ResizeObserver`, so the print layout itself is checked in a real browser (group 7 of the tasks).

**Decided with the user (2026-10-28):**
- Only the project's own dashboards get the print layout and the Print action. On every page, global rules leave out the navigation, toasts and menus.
- Orientation: first fixed to landscape by Tamiza. After printing with it, the user asked (2026-10-28) to allow portrait too, without turning the charts sideways, the dashboard scaled down so its width fits the sheet's. The member now picks the orientation in the dialog (D2, D9).
- Every extra was asked for:
  - a print header;
  - widgets kept whole across pages;
  - a count of the table rows left out;
  - the document title as the PDF's name.

**Decided by this design** (open to revision):
- The paper canvas is 980 px wide, which fits A4 and Letter landscape with 10 mm margins; the browser scales it down to a narrower sheet (D2).
- The page waits at most 20 seconds for widgets and tiles.
- The title reads `<project> – <dashboard> – <yyyy-mm-dd>`.

The requirements are in `specs/dashboard-printing`, `specs/custom-dashboards` and `specs/web-ui`.

## Goals / Non-Goals

**Goals:**
- What the member sees while the print is prepared and what the paper shows come from one set of rules. Every box measured on screen then has the size it has on paper.
- The Print action and the browser's own print command share the same preparation. The only difference is that the browser's command cannot wait.
- No API call, data change or address change.

**Non-Goals:**
- Pixel-identical output across browsers. Chrome and Edge are the reference, and Firefox is checked. Safari works on a best-effort basis.
- Fitting a dashboard to one page, or letting the member choose the scale, paper or margins in Tamiza.

## Decisions

### D1. A page-scoped "paper mode", entered before the browser prints

`DashboardPrint` is an injectable provided by `ProjectIndicators`, so it exists only on the project's own dashboards. It holds a `printing` signal and a registry, and has three operations:

- `prepare()` is synchronous:
  - it sets `printing`, adds the class `tmz-printing` to `<html>` and adds the `@page` style element (D9);
  - it saves `document.title` and sets the print title, and stamps the print time;
  - it runs `ApplicationRef.tick()`, so the paper-mode DOM is rendered;
  - it reads the layout to force it, then runs every registered measure callback (D5): charts and maps re-measure, and tables count the rows that fit;
  - it ticks again.
- The Print action runs `prepare()`, waits for readiness (D6), then calls `window.print()`.
- A `beforeprint` listener runs `prepare()` when the member prints from the browser's menu and paper mode is not on yet. `afterprint` runs `restore()`. `restore()` undoes every step of `prepare()` and puts focus back on the Print action when the print started from it.

Changes made synchronously in `beforeprint` are in the printout. That is the event's purpose, and Chrome and Firefox honour it. So both paths print the same sheets. The browser's command does not wait for loading widgets or tiles.

*Alternatives:*
- **CSS-only `@media print`.** Charts would print at their on-screen size, which on a phone is a different shape. Maps would be cropped, and tables could not be counted. Rejected.
- **A separate print route.** It would re-read every widget and lose the member's view: the tables shown, the map zoom and any unsaved state. Rejected.
- **A PDF made on the server with a headless browser.** That would mean a new container. Out of scope.

### D2. A fixed paper canvas of 980 px; rows keep their size

In paper mode the dashboard's content is 980 px wide, starting at the left edge like the project's header above it (centring only the canvas would set it off from that header on A4). That is the printable width of Letter landscape with 10 mm margins (11 in − 20 mm ≈ 980 px). A4 landscape (≈ 1047 px) has room to spare.

The grid keeps 72 px rows and a 16 px gap. A landscape sheet holds about 8 rows, after the header on the first sheet; a portrait sheet, scaled down, about 15.

The width is fixed on screen during preparation too, so every box measured there has its paper size whatever the member's screen. On a phone the canvas is wider than the screen, and the page clips it (`overflow-x: clip` on `main`, screen only) rather than scrolling.

Nothing caps the canvas on paper. On a sheet narrower than 980 px (portrait A4 ≈ 718 px, portrait Letter ≈ 740 px), the page overflows the sheet and the browser scales the whole page down to the sheet's width, as zooming out would. Every box keeps the size it was measured at, so charts, maps and table counts stay right; the sheet only shows them smaller. Checked in headless Chrome: a 980 px grid on portrait A4 and Letter shrinks to the sheet; Chrome shrinks a page at most 1.5 times, so a sheet narrower than about 650 px (A5 portrait) cuts the right-hand columns off. Firefox fits the page to the sheet's width by default ("Fit to page width").

The shrinking needs the overflow to reach the document. A clip on an ancestor stops it, and Chrome then cuts the canvas off at the sheet's edge (checked). So `main`'s `overflow-x: clip` is screen-only.

On paper each tile starts 2 px into its grid area (`padding-top` on the grid item, also while preparing, so the measured boxes include it). When Chrome scales the page down, it can paint the first pixel of the next sheet at the foot of the previous one; the top border of a tile moved to the next sheet then showed there as a thin line (checked on portrait Letter, gone with the 2 px).

Each chart's SVG gets `width: 100%; height: 100%` on paper as a safety net: its `viewBox` scales it into a box that came out a different size.

*Alternatives:*
- **A fluid width.** The paper size is only known inside the browser's dialog, after the page has prepared, so the charts would be drawn for the wrong width. Rejected.
- **Scaling the canvas ourselves to the sheet's width** (CSS `zoom` or `transform` from the page width). The width is only known inside the dialog, Firefox cannot yet divide a length by a length in `calc()`, and width media queries see another width than layout while printing (Context). Rejected: the browser's own shrink-to-fit does it.
- **The desktop width of 1232 px shrunk with CSS `zoom`.** This looks more like the screen and fits more rows per sheet. But `zoom` changes the coordinates that Leaflet and `ResizeObserver` report, and makes the text smaller. Rejected. The browser's own Scale option still lets a member fit more on a sheet.

### D3. One `paper:` variant for preparation and paper

`styles.css` declares a custom variant:

```css
@custom-variant paper {
  @media print { @slot; }
  &:where(.tmz-printing, .tmz-printing *) { @slot; }
}
```

Content inside the canvas is styled with `paper:` (for example `paper:hidden` and `paper:shadow-none`). What the member sees during preparation is then laid out like the paper, which makes the measurements in D5 true.

Only what must stay usable on screen while the page prepares uses plain `print:`: the Print action with its busy state, the header's other actions, the top bar, the tabs and the toasts.

`paper:` rules may lose to an earlier responsive utility (`md:inline-flex` on the move handle), depending on the order Tailwind emits variants in. Where they do, they get Tailwind's important modifier (`paper:hidden!`). Task 2.1 checks the emitted order.

### D4. The phone layout is for screens only

The grid's one-column rule becomes `@media screen and (max-width: 47.99rem)`. Its selectors also exclude `.tmz-paper`, a class `WidgetGrid` puts on its `<ul>` while `printing` is on. In paper mode, and on paper, every widget keeps its `grid-column` and `grid-row` from its layout.

### D5. Measuring synchronously

`observeSize` and `MapWidget.measure()` keep their `ResizeObserver`. They also register a callback with `DashboardPrint` when one is injected (an optional injection, so the widget form's preview is unchanged):

- For a chart, the callback reads `getBoundingClientRect()` and sets the size signal.
- For a map, it measures, calls `engine.resize()` and re-fits when the map is untouched. A zoomed map keeps its centre and zoom, as on any resize.
- `WidgetTable` registers a callback that finds the last row whose bottom fits in the container, leaving one line for the note. It stores how many rows fit.

`prepare()` calls them after forcing layout, then ticks. That is how the `beforeprint` path, which cannot wait for a `ResizeObserver`, still prints charts drawn for their paper box. Leaving paper mode needs no callback: the observers fire on the screen layout again.

### D6. Readiness for the Print action

Widgets report to the registry with a function the wait asks every 50 ms, so it can read state that is not a signal:

- `WidgetView` reports pending while its state is `loading`.
- `MapWidget` reports pending while the map is still being made, while its basemap is being set up, or while Leaflet's tile layer `isLoading()` (exposed as `MapEngine.tilesLoading()`). A map without a basemap is ready once it is made.
- A map is only made when its widget comes near the screen (`IntersectionObserver`). So the map's measure callback also marks it visible, and a map below the screen is made for the paper. Until it is, its box shows the same "not finished loading" note on paper.

The Print action resolves when nothing is pending, then waits two animation frames so the last redraw is painted. A deadline of 20 seconds cuts the wait short. A widget still loading on paper shows the note "This widget had not finished loading when it was printed." in place of its skeleton, which is `paper:hidden`. The `beforeprint` path shows the same note.

A dashboard the router opens in place of this one while the wait runs ends paper mode too (`open()` restores), so nothing is printed for the dashboard left.

If the page is destroyed while it waits, for example when the member opens another tab, the wait is dropped and `restore()` runs.

### D7. The print header and the filters on paper

The page header gets a `hidden paper:block` section after the dashboard's heading. It gives:

- the print time: the stamp taken in `prepare()`, written with `dateText(..., 'dateTime', timeZone)` and the zone's name;
- the data's publication time: the latest `publishedAt` among the indicators that the dashboard's widgets name, or "The data has not been published yet.";
- the filters, as plain text. `DashboardFilterBar` renders `paper:` plain text, "Label: value" joined in order, with the same `value()` it gives the pills. The pills and the clear and reset buttons are `paper:hidden`.

The project's name and form come from the project header, which stays. Its back link and role badge are `print:hidden`. The empty-state card, the notices, the toolbar and the inventory disclosure are `paper:hidden`.

### D8. Widgets on paper

Tiles get `paper:bg-white paper:shadow-none` with their border. `break-inside: avoid` on `.tmz-item` keeps a widget whole (see Context).

The tile header keeps its title. Its buttons, the resize handle, `MapControls`, map hints and the scripts link are `paper:hidden`.

`print-color-adjust: exact` is set on the grid. The property is inherited, so legend swatches, table header shading and map attribution backgrounds print without "Background graphics".

A table that does not fit shows its first `fit` rows and the note "{{count}} rows not printed" (`…One` for 1). The rows after them are `paper:hidden`. This keeps a half-cut row off the paper and keeps the count true.

### D9. `@page` only while a dashboard prints

`prepare()` appends `<style id="tmz-print-page">@page { margin: 10mm; }</style>` to `<head>`, and `restore()` removes it. The UI's CSP already allows inline styles (`style-src 'self' 'unsafe-inline'`, `nginx-security-headers.conf:5`), as Angular's own component styles need. A global rule in `styles.css` would change the margins of every printout of the app. A rule in the component's styles depends on how Angular scopes at-rules. A named `page:` would force a break between the project header and the dashboard.

The rule sets no `size`. With `size: landscape`, as first planned, Chrome and Edge locked the orientation, and a portrait sheet got the dashboard turned sideways. The member now picks the orientation in the dialog (decided with the user); D2 covers portrait.

### D10. Document title

`prepare()` sets `document.title` to `<project> – <dashboard> – <yyyy-mm-dd>`, with the date in the project's time zone. `restore()` puts back the title it saved. Chrome and Edge offer the title as the PDF file name and print it in their own header. The browser replaces any character a file name cannot hold.

### D11. The Print action

It goes in the header row, in front of "New dashboard" and the actions menu. The `dashboard-actions` container is now rendered for every member, and the analyst-only actions stay inside an `@if`. The action is shown when `!loading() && !error() && widgets().length > 0`.

It is a `tmzButton variant="secondary" size="sm"` with a printer icon and the text "Print". While preparing it has `aria-disabled="true"` and `aria-busy="true"`, keeps focus, and ignores further clicks. The page's live region says "Preparing the print…". No toast follows (`web-ui`, as modified).

### D12. The rest of the app on paper

These rules apply to every page (`web-ui`, "Navigation left out of printouts"):

- `print:hidden` on the top bar (`app.html`), the tabs (`project-page.ts`), the settings navigation and the toast region;
- `@media print { .cdk-overlay-container { display: none } }` for menus, dialogs and tooltips;
- `print:max-w-none print:p-0` on `main`;
- `@media print { html { background: #fff } }`.

None of these depends on `DashboardPrint`.

## Risks / Trade-offs

- **A browser may not honour changes made in `beforeprint`.** Printing from the browser's menu would then show charts scaled from their screen size by the SVG safety net (D2), and tables cut without a count. → The Print action is the path the docs describe. Task 7 checks Ctrl+P in Chrome, Edge and Firefox.
- **`window.print()` after an asynchronous wait.** Some browsers, mainly Safari, may refuse to print outside the user's click, or may not block until the dialog closes. Then `afterprint` may never come and the page would stay in paper mode. → `restore()` also runs on the first `pointerdown` or `keydown` after `window.print()` returns once paper mode is still on, and on the `print` media query turning false.
- **Grid fragmentation in Firefox.** It is only checked in Chrome so far. → Task 7.3 checks Firefox. If Firefox splits widgets, the fallback is to render the grid as bands of rows that no widget crosses, each with `break-inside: avoid`. Only this decision and the grid would change.
- **Sheets narrower than two thirds of the canvas.** Chrome and Edge shrink a page at most 1.5 times, so on A5 portrait or smaller the right-hand columns are cut off. → A4 and Letter fit in either orientation; the docs say to pick landscape or a smaller scale on a smaller sheet.
- **Firefox's own scaling.** Firefox fits the page to the sheet while its "Fit to page width" scale is chosen, which is its default. → Task 7.3 checks portrait in Firefox.
- **Slow basemap tiles.** On paper they come out blank or partly drawn after 20 seconds. → The shapes and legend still print, and the attribution stays.
- **Tall widgets.** A widget can be up to 40 rows high, about five sheets. Such a widget continues across sheets, as the spec allows.
- **Screen flicker while preparing.** The canvas narrows and the controls disappear for the time of the wait. → It reads as a preview, and the busy action and the announcement say what is happening.
- **Tailwind variant order** (D3). → It is checked in task 2.1, with the important modifier where needed.

## Migration Plan

UI only. It ships with the `tamiza-ui` image and needs no migration, setting or API change. Rolling back means deploying the previous UI image.

## Open Questions

- Whether 980 px should become 1000 px or so once printed on real A4 and Letter printers (task 7.5). This only changes a constant in D2.
