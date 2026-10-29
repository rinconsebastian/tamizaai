# Tasks

**Order.**
- Groups 1 to 5 build the feature, in order. Each group ends with its specs green.
- Group 6 runs the full suites.
- Group 7 checks the printout in real browsers. jsdom has no layout and no print, so only group 7 can show the sheets.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks that they match). Count messages use `countKey` (`<key>` and `<key>One`). The Spanish UI says "tablero" for dashboard and "Imprimir" for Print.

**How to run the tests (UI only; this change touches no api or analytics code):**
- Full suite: `npm test -- --watch=false` in `tamiza-ui/`.
  - The two "stops polling when the page is destroyed" tests (`project-data.spec.ts`, `project-scripts.spec.ts`) are known failures.
  - The test worker sometimes dies at start (exit code 3221225477). Run it again.
- A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.
- Build: `npx ng build` in `tamiza-ui/`.

## 1. Print rules for every page (`web-ui`)

- [x] 1.1 In `src/styles.css`, add an unlayered `@media print` block with `html { background: #fff }` and `.cdk-overlay-container { display: none }` (design D12). Add a `print-styles.spec.ts` that reads `styles.css` as text, in the style of `map-styles.spec.ts`. Verify that the spec finds both rules inside `@media print`.
- [x] 1.2 Add `print:hidden` to:
  - the top bar (`app.html`);
  - the back link, the role badge and `tmz-tabs-nav` (`project-page.ts`);
  - the settings navigation (`project-settings-layout.ts`);
  - `tmz-toast-region` (`app.html`).

  Add `print:max-w-none print:p-0` to `main`. The project's name and form name stay printable. Verify with assertions on these classes in `app.spec.ts`, `project-page.spec.ts` and `project-settings-layout.spec.ts`.

## 2. Paper mode (design D1, D3, D9, D10)

- [x] 2.1 Declare the `@custom-variant paper` in `styles.css` (design D3). Run `npx ng build` and look in the emitted CSS:
  - Find where a `paper:hidden` rule sits relative to `md:inline-flex` and `sm:w-auto`.
  - If a responsive utility comes later, write a note in the variant's comment that `paper:` utilities competing with responsive ones take the important modifier (`paper:hidden!`).

  Verify that a `print-styles.spec.ts` case finds the variant's `@media print` and `.tmz-printing` selectors in the built CSS. If the build's CSS cannot be read from a spec, read `styles.css` and record the build check in the task.

  Build check (2026-10-28): the spec reads `styles.css` (the `.css` loader gives the source, not the built CSS). The project's Tailwind 4.3.3, compiling the variant with probe utilities, emitted `.paper:*` after `.md:inline-flex`, `.md:block`, `.sm:w-auto` and `.print:hidden`, each as an `@media print` rule plus a `:where(.tmz-printing, .tmz-printing *)` rule. So no important modifier is needed, and the variant's comment says so. `npx ng build` succeeds with the variant.
- [x] 2.2 Add `pages/projects/indicators/dashboard-print.ts` with `DashboardPrint` (design D1, D5, D6):
  - the `printing` signal and the print time stamp;
  - `registerMeasure(callback)` and `registerPending(pending)` (a function the wait asks again and again), each returning an unregister function;
  - `prepare(title)`:
    - adds the `tmz-printing` class to `<html>` and the `<style id="tmz-print-page">` element (D9);
    - saves `document.title` and sets the print title;
    - ticks, forces layout, runs the measure callbacks and ticks again;
  - `restore()`, which undoes every step of `prepare()`;
  - `whenReady(deadlineMs = 20000)`, which resolves when nothing is pending, plus two animation frames, or at the deadline;
  - the `beforeprint` and `afterprint` listeners;
  - the backstop restore: the first `pointerdown` or `keydown` after `window.print()` returned while still printing, or the `print` media query turning false;
  - a restore when it is destroyed.

  Verify unit tests:
  - `prepare` adds the class, the style element and the title, and runs the callbacks in order between two ticks;
  - `restore` puts the title back and removes the class and the style element;
  - `whenReady` resolves once the pending reports clear, and at 20 s with fake timers;
  - `beforeprint` prepares only when not already printing, and `afterprint` restores;
  - the backstop restores;
  - destroying the service restores.
- [x] 2.3 Add `printTitle(project, dashboard, now, timeZone)`, which returns `<project> – <dashboard> – <yyyy-mm-dd>` with the date in the project's time zone (D10). Verify unit tests, including `2026-10-29T03:00:00Z` in `America/Bogota` giving `2026-10-28`, and names with `/` or `<b>` passed through as text.

## 3. The grid, charts and tables on paper (design D2, D4, D5, D8)

- [x] 3.1 `widget-grid.ts`:
  - Limit the one-column rule to `@media screen and (max-width: 47.99rem)`, with selectors that exclude `.tmz-paper`.
  - Put `tmz-paper` on the `<ul>` while `DashboardPrint.printing()` is on. The service is injected optionally, so the grid works without it.
  - Add `break-inside: avoid` on `.tmz-item` for paper, and `print-color-adjust: exact` on the grid.
  - Give tiles `paper:bg-white paper:shadow-none`.
  - Make the move button, the table toggle, the widget menu and the resize handle `paper:hidden`.

  Verify in `widget-grid.spec.ts`:
  - `tmz-paper` follows `printing`;
  - the four controls carry `paper:hidden`;
  - without the service the grid renders as before.
- [x] 3.2 Have `observeSize` (`chart-data.ts`) register a measure callback with an optionally injected `DashboardPrint`. The callback reads `getBoundingClientRect()` and sets the size. `AxisChart` and `PieWidget` keep using it unchanged. Add the paper safety net: `width: 100%; height: 100%` on the chart SVGs, by the class `tmz-chart-svg` (D2). Verify a spec:
  - the callback is registered when the service is present, sets the size from a stubbed rectangle, and is unregistered on destroy;
  - nothing is registered without the service.
- [x] 3.3 `widget-table.ts`:
  - Register a measure callback that counts the rows whose bottom fits in the container, leaving one line for the note, and stores `fit`.
  - On paper, rows from `fit` on are `paper:hidden`, and a `hidden paper:block` note reads `dashboards.print.rowsLeftOut` or `…One` with the count left out.
  - With no `DashboardPrint`, or when every row fits, there is no note.

  Add the strings. Verify a spec with stubbed row rectangles:
  - 40 rows of which 12 fit gives `fit = 12` and "28 rows not printed";
  - 1 row left out uses the singular;
  - 5 rows that all fit give no note.

## 4. Maps and widget states on paper (design D5, D6, D8)

- [x] 4.1 `map-widget.ts`:
  - Register a measure callback that marks the map visible (a map below the screen is made for the paper), measures, calls `engine.resize()` and re-fits only when the map is untouched.
  - Report pending while the map is being made, its basemap set up, or its tiles loading (`MapEngine.tilesLoading()`, Leaflet's `isLoading()`). A map without a basemap is ready once made. Until it is made, its box says on paper that it had not finished loading.
  - Make `app-map-controls` and the map hint `paper:hidden`. Keep the legend, the notes and the attribution printable.

  Verify in `map-widget.spec.ts` with the Leaflet test double:
  - the callback resizes and re-fits an untouched map, and only resizes a zoomed one;
  - a map that never came near the screen is made when the print is prepared;
  - the print dialog waits until the basemap tiles have loaded;
  - the controls and hints carry `paper:hidden`.
- [x] 4.2 `widget-view.ts`:
  - Report pending to `DashboardPrint` while the state is `loading`.
  - Make the skeleton `paper:hidden` and add a `hidden paper:block` note, `dashboards.print.notLoaded` ("This widget had not finished loading when it was printed.").
  - Make the scripts link of a widget whose indicator has not published `paper:hidden`.

  Add the strings. Verify in `chart-widgets.spec.ts`:
  - the pending report is on while the query is in flight and off once it answers or fails;
  - the note is in the DOM while loading;
  - the scripts link carries `paper:hidden`.

## 5. The dashboard page (design D7, D11; `custom-dashboards`, `dashboard-printing`)

- [x] 5.1 In `project-indicators.ts`:
  - Provide `DashboardPrint`.
  - Make the page's content the paper canvas (D2): `paper:` 980 px wide, left-aligned with the project header, with nothing capping it on paper (the browser scales it down to a narrower sheet, task 8.2). Add `.tmz-printing main { overflow-x: clip }` to `styles.css`, inside `@media screen`, so a phone screen clips the canvas while it prepares instead of scrolling.
  - Render the `dashboard-actions` container for every member, with the Print action first. "New dashboard" and the actions menu stay inside the analyst `@if`.
  - Show Print when `!loading() && !error() && widgets().length > 0`.
  - On click, run `prepare(printTitle(...))`, then `whenReady()`, then `window.print()`.
  - While preparing, the action has `aria-disabled`/`aria-busy`, ignores clicks, and `announce()` says `dashboards.print.preparing`.
  - After `restore()`, focus returns to the action. No toast is shown.
  - The Print action and the analyst actions are `print:hidden`.

  Add the strings `dashboards.print.action` ("Print") and `…preparing`. Verify in `project-indicators.spec.ts`, with `window.print` stubbed:
  - a viewer sees only Print;
  - an analyst sees Print, "New dashboard" and the menu, in that order;
  - a dashboard with no widgets offers no Print;
  - Print waits for a widget still loading before calling `window.print`;
  - the busy state and the announcement;
  - focus returns after `afterprint`, and no toast appears;
  - the address and the filter values are unchanged afterwards;
  - the content carries the canvas's `paper:` width.
- [x] 5.2 Add the print header: a `hidden paper:block` section after the heading (D7). It gives:
  - the print time, with `dateText(..., 'dateTime', timeZone)` and the zone's name;
  - the latest `publishedAt` among the indicators the widgets name, or `dashboards.print.notPublished`.

  Add the strings `dashboards.print.printedAt` and `…publishedAt`. Verify with a fixed clock and the `America/Bogota` time zone, in English and Spanish:
  - the two times and the zone are shown;
  - an indicator that no widget names is ignored;
  - the not-published text appears when no widget's indicator has published.
- [x] 5.3 In `dashboard-filter-bar.ts`, add a `hidden paper:block` plain-text line with each filter's "Label: value" in order, or "All", built from the same `value()` the pills show. Make the pills list and the clear and reset buttons `paper:hidden`. A dashboard without filters still renders nothing. Verify in `dashboard-filter-bar.spec.ts`: "Municipality: Cali" and "Round: All" in order, and no line without filters.
- [x] 5.4 Make the dashboard notice, the dropped-filters alert, the empty-state card, the toolbar and the inventory disclosure `paper:hidden`. Verify in `project-indicators.spec.ts` that each carries `paper:hidden` and that the heading and the grid do not.
- [x] 5.5 Leaving the page during the wait: a destroyed page drops the wait, does not call `window.print`, and restores. Verify in `project-indicators.spec.ts` by navigating to another tab while a widget is still loading.
- [x] 5.6 Add a "Printing a dashboard" section to `docs/indicators-dashboard.md`. It covers:
  - the Print action and who has it;
  - what the sheets show and leave out;
  - the orientation: full size in landscape, scaled down in portrait;
  - widgets kept whole;
  - the table note, with a pointer to the export for every row;
  - printing with Ctrl+P (no wait);
  - the browser's own options (scale, headers and footers; background graphics is not needed);
  - the browsers checked.

  Add one line under "What it does not do" for a PDF generated on the server. Verify that the section's terms match the spec and the English UI labels.

## 6. Full suites

- [x] 6.1 Run `npx ng build` and `npm test -- --watch=false` in `tamiza-ui/`, and `npm run check:safety`. Verify that the build has no new warnings and that the only failures are the two known polling tests.

  Result (2026-10-28): the build succeeds with only the earlier NG8113 warning (`ProjectReference`); `check:safety` passes; 1893 of 1894 tests pass, the one failure being the known "polls while a run is active and stops when the page is destroyed" in `project-scripts.spec.ts`.

## 7. The printout in real browsers

- [x] 7.1 Build the UI into the verification stack, following the recipe in the dev-environment memory: `-p tamizaverify`, only the UI rebuilt, a fake OIDC and a fake Kobo. Seed a project whose dashboard "Housing" has:
  - two stat tiles side by side;
  - a column chart 9 columns wide;
  - a pie chart;
  - a map with the OpenStreetMap basemap;
  - a widget shown as a table with 40 rows;
  - a bar chart 14 rows high;
  - the filters "Municipality" and "Round".

  In headless Chrome over the DevTools protocol, as an analyst, set "Municipality" to a value and click Print with `window.print` replaced by a recorder. Once it is called, run `Page.printToPDF` with `preferCSSPageSize: true` and `printBackground: false`. Read the PDF and verify:
  - landscape pages;
  - no control, tab or top bar;
  - the header with the times and the filters;
  - 12 columns;
  - no widget split except the 14-row chart;
  - the table note;
  - the map with its tiles, legend and attribution;
  - colors present;
  - the page title.

  Result (2026-10-28, Chrome headless, Letter and A4): every point holds. The map sat below the screen and had never been made; Print made it and waited for its 6 OSM tiles (about 0.5–0.8 s in all) before the dialog. On screen while preparing, every control computed `display: none`, the canvas was 980 px with 12 columns, and the title was "Census 2026 – Housing – 2026-10-28". The table printed 7 rows and "33 rows not printed."; the busy state and "Preparing the print…" showed. After the dialog the page, the title and the focus (on Print) were back. One defect found and fixed: the pie legend kept its scroll height on paper and printed 6 of 9 entries; it now grows to its full height on paper (`paper:max-h-none paper:overflow-visible`, with a spec), and the pie is drawn in the room left. Seen, not changed: the 5-row widgets after the header start on sheet 2, leaving sheet 1 half empty (widgets kept whole, as specified); a stat tile 3 columns wide breaks "918 households" mid-word on paper, as it does on a narrow screen.
- [x] 7.2 Repeat with `Page.printToPDF` and no Print click (the browser's own command; `printToPDF` fires `beforeprint`, as seen while planning). Do it once with every widget loaded, and once with a delayed widget query. Do it again at a 390 px viewport. Verify the same sheets, the "not finished loading" note for the delayed widget, the 12 columns from the phone viewport, and that the page is back to its screen layout afterwards.

  Result (2026-10-28): the same sheets each time, the charts drawn for their paper boxes. The held query's widget read "This widget had not finished loading when it was printed." The 390 px phone printed 12 columns; its map, re-fitted to the larger paper box, printed its shapes, legend and credit without the basemap, whose new tiles could not be waited for (as specified for the browser's own command). Afterwards the class, the page rule and the title were gone and the phone was back to one column.
- [x] 7.3 Ask the user to print "Housing" in Firefox: from the Print action, and with Ctrl+P, in landscape and in portrait. Verify with them that no widget is split, the header and filters show, the colors print, and a portrait sheet shows the dashboard upright, scaled to the sheet's width. If Firefox splits widgets, apply the band fallback from the design's risks before closing this task.
- [x] 7.4 Ask the user to check in Edge or Chrome:
  - the dialog offers both orientations: portrait shows the dashboard upright and scaled to the sheet's width, landscape at full size;
  - "Save as PDF" offers `<project> – Housing – <date>` as the file name;
  - cancelling restores the dashboard with focus on Print;
  - optionally, a screen reader announces "Preparing the print…".
- [x] 7.5 Ask the user to print once on A4 and once on Letter, in landscape and in portrait. Confirm that the 980 px canvas fills a landscape sheet well and that the scaled-down portrait sheets stay readable. If it does not, adjust the constant (design, Open Questions).

  Result for 7.3–7.5 (2026-10-28): the user checked the prints on the verification stack and confirmed that they print well. Nothing was changed; the band fallback and a new canvas width were not needed.

## 8. Either orientation (revision, 2026-10-28)

The user printed in portrait and asked that the charts not be turned sideways: the dashboard should be zoomed out until its width fits the sheet's (design, "Decided with the user"; D2, D9).

- [x] 8.1 Drop `size: landscape` from the page rule in `dashboard-print.ts`, keeping the 10 mm margins. Verify in `dashboard-print.spec.ts` that the style element holds `@page { margin: 10mm; }`.
- [x] 8.2 Let the canvas overflow a narrower sheet, so the browser scales the page down: remove `print:max-w-full` from the canvas in `project-indicators.ts`, and put `.tmz-printing main { overflow-x: clip }` inside `@media screen`. Verify in `project-indicators.spec.ts` that nothing caps the canvas, and in `print-styles.spec.ts` that the clip is screen-only.
- [x] 8.3 Start each tile 2 px into its grid area on paper (`.tmz-grid.tmz-paper > .tmz-item { padding-top: 2px }`), so a scaled-down sheet shows no line of the next sheet's tiles. Verify in `widget-grid.spec.ts`.
- [x] 8.4 Update "Printing a dashboard" in `docs/indicators-dashboard.md`: the orientation, the scaling, sheets smaller than A4 or Letter, and the browser's options.
- [x] 8.5 Rebuild the verification UI and print "Housing" with `Page.printToPDF`: from the Print action in portrait and landscape on A4 and Letter, with no click in portrait, and from a 390 px phone in portrait. Verify that portrait sheets are upright, scaled to the width with 12 columns and the right edge on the sheet; that landscape still prints at full size; that no line shows at the foot of a sheet; and that the page is restored afterwards.

  Result (2026-10-28): portrait A4 and Letter print the whole dashboard upright, scaled to the width, with 12 columns (2 and 3 sheets; landscape takes 5). Landscape Letter and A4 are unchanged at full size. Before 8.3, portrait Letter showed a thin line at the foot of sheets 1 and 2, the top border of the tiles moved to the next sheet; with the 2 px it is gone. Phone and Ctrl+P print the same columns, and the class, page rule and title are gone afterwards. A static 980 px grid confirmed the limits: a clip on `main` cuts the canvas off instead of scaling it, and Chrome scales at most 1.5 times (a sheet 1.6 times narrower is cut off).
- [x] 8.6 Run `npx ng build`, `npm test -- --watch=false` and `npm run check:safety` in `tamiza-ui/` again. Verify that the only failure is a known polling test.

  Result (2026-10-28): the build succeeds with only the earlier NG8113 warning; `check:safety` passes; 1896 of 1897 tests pass, the one failure being the known "polls while a run is active and stops when the page is destroyed" in `project-scripts.spec.ts`.

## Workflow follow-up

- Archive the change once group 7 is done and the user has reviewed the printouts.
- Update the project memory with the outcome and any browser quirk found in group 7.
