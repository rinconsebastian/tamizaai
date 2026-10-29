# Proposal

## Why

Members print the project's dashboards, or save them as PDF, to take them to meetings and attach them to field reports. Today the browser prints the page as it is on screen, and the result is poor:

- The top bar, the project's tabs, the dashboard's actions, the filter pills, every widget's buttons and the map controls are all printed.
- The widgets lose their columns. A printed page is narrower than the 768 pixels at which the grid switches to its phone layout, so every widget prints full width, one under the other.
- Nothing on the page says which project, dashboard, filters or date the sheet shows.

Charts are drawn at the pixel size of their box, so a widget printed at another size can also be cut off or left with empty space.

The dashboard email reports (archived `2026-10-28-add-dashboard-email-reports`) send figures and tables. A printout that looks like the dashboard itself is still missing.

## What Changes

- **A Print action on each of the project's own dashboards.** Every member can use it, viewers included. It sits next to the dashboard's name. It gets the dashboard ready for paper, waits for the widgets and map tiles to finish loading, and then opens the browser's print dialog. From there the member can print or save as PDF. Printing from the browser's own menu (Ctrl+P) gives the same sheets, except that it cannot wait for widgets or map tiles that are still loading.
- **Only the content is printed.** Paper never shows controls:
  - the app's top bar, the back link, the role badge and the project's tabs;
  - the dashboard's actions and the widget toolbar;
  - each widget's move, table and menu buttons, and its resize handle;
  - the map controls;
  - the filter bar's clear and reset buttons;
  - notices, toasts, tooltips and open menus;
  - the list of indicators.
- **The desktop columns are kept.** On paper the grid always has its 12 columns, with every widget in its saved position, width and height in rows. The phone layout applies only to screens.
- **Either orientation.** The page asks for narrow margins and leaves the orientation to the browser's dialog. On a landscape A4 or Letter sheet the dashboard prints at full size. On a portrait sheet the whole dashboard is scaled down to the sheet's width, upright and with its 12 columns, never turned sideways.
- **A print header.** Above the widgets, the sheet shows:
  - the project and the dashboard;
  - when it was printed, in the project's time zone;
  - the latest time one of the widgets' indicators was published;
  - the filters in force, as plain text.

  The email report's header gives the same facts.
- **Widgets are not split across pages.** A widget that fits on a sheet is moved whole to the next one. Only a widget taller than a sheet continues on the next one.
- **Tables say what they leave out.** A widget shown as a table may have more rows than fit in its box. Its printout then says how many rows were left out, instead of cutting them off without saying so.
- **A useful PDF name.** While printing, the page's title is the project, the dashboard and the date. The browser offers this title as the PDF's file name and prints it in its own header.
- **Print-friendly look.** The sheet has a white background, no shadows and thin borders. Chart, legend and map colors print even when the browser's "background graphics" option is off.

Not in this change:
- A PDF generated on the server, or a PDF attached to the scheduled email.
- A print layout for the Performance and Clean data dashboards. On every page, the general rules only hide the app's top bar, the project's tabs, toasts and open menus.
- A print preview inside Tamiza, or a choice of paper size, orientation or margins in Tamiza. The browser's dialog keeps its paper size, orientation and scale options.
- Fitting the whole dashboard on one sheet. It takes as many sheets as its rows need.
- Printing every row of a widget's table. The table export already gives the full table.

## Capabilities

### New Capabilities

- `dashboard-printing`: printing one of the project's own dashboards. It covers:
  - the Print action and how it gets the dashboard ready;
  - what paper shows and hides;
  - the print header;
  - the 12-column print layout, in either orientation;
  - page breaks between widgets;
  - charts, maps and tables on paper;
  - the document title while printing;
  - printing from the browser's own menu.

### Modified Capabilities

- `custom-dashboards`:
  - "Dashboard header": every member gets the Print action after the dashboard's name, viewers included. Analysts and admins keep their own actions after it.
- `web-ui`:
  - "Feedback while an action runs": an action that ends by opening the browser's own dialog, such as printing, shows that dialog instead of a success or failure message.
  - A new requirement: printing any screen leaves out the app's navigation, toasts and open menus.

## Impact

- **ui** (`tamiza-ui`):
  - `project-indicators.ts`:
    - the Print action in the dashboard header;
    - getting the dashboard ready and waiting for its widgets;
    - the print header;
    - the document title while printing.
  - `widget-grid.ts`:
    - the phone layout is limited to screens;
    - print rules for the 12 columns and page breaks;
    - controls hidden on paper.
  - `widget-view.ts`, `axis-chart.ts`, `pie-widget.ts`, `map-widget.ts`, `map-controls.ts`, `widget-table.ts`:
    - each widget reports when it is ready to print;
    - charts are scaled to fit on paper, maps keep their colors and attribution, and tables count the rows left out.
  - `dashboard-filter-bar.ts`: on paper, the filters in force as plain text, without the buttons.
  - `app.html`, `project-page.ts`, `project-settings-layout.ts`, `toast.ts`: the top bar, the project header's back link and role badge, the tabs, the secondary navigation and the toasts are hidden on paper.
  - `styles.css`:
    - the `@page` rule;
    - the white background;
    - the CDK overlay hidden on paper;
    - the print colors.
  - `en.json` and `es.json` strings, and the specs.
- **api**, **analytics**, **database**: none. Printing uses the data the page already reads.
- **Docs:** `docs/indicators-dashboard.md` gains a "Printing a dashboard" section.
