# Tasks

**Order.**
- Groups 1 and 2 (ui) need no API change and can ship first.
- Group 3 (api) comes before group 4 (ui).
- Group 5 comes before groups 6 and 7.
- Group 8 comes last.

**How to run the tests:**
- API: `dotnet test` from `tamiza-api/`. The integration tests start PostgreSQL with Testcontainers, so Docker must be running.
- UI: `npm test -- --watch=false` from `tamiza-ui/`, or one spec with `npx ng test --watch=false --include=<spec>`.
  - Two known polling tests (`project-data.spec.ts`, `project-scripts.spec.ts`) fail on unchanged code in this environment.

## 1. Leaflet engine, zoom and pan (ui)

- [x] 1.1 Add the Leaflet packages and assets (design D1, D18):
  - `leaflet@1.9.4` and an exact `@types/leaflet` 1.9.x pin;
  - `src/types/leaflet-esm.d.ts`;
  - an `angular.json` asset glob copying `node_modules/leaflet/dist/leaflet.css` to `vendor/leaflet/1.9.4/`;
  - a `Dockerfile` line copying `dist/tamiza-ui/3rdpartylicenses.txt` next to `index.html`.

  Verify with `npm run build`:
  - Leaflet is in a lazy chunk, not `main`, and the initial total stays within the 650 kB warning budget;
  - `dist/tamiza-ui/browser/vendor/leaflet/1.9.4/leaflet.css` exists.

  If esbuild does not split `leaflet-src.esm.js`, switch to `import('leaflet')` plus `allowedCommonJsDependencies` and record it in the design.
- [x] 1.2 Add a pure `map-geometry.ts` with (design D3):
  - the degrees test, moved from `map-widget.ts:137`;
  - the zoom limits from a fit zoom (EPSG3857 and Simple);
  - the lone-point rule;
  - the `maxBounds` rule (data padded, extended by the fitted view).

  Verify `map-geometry.spec.ts`:
  - degrees and non-degrees input;
  - `minZoom = fit − 3`, at least 0 for degrees and negative allowed for Simple;
  - `maxZoom` 20 for degrees, and `fit + 10` for Simple;
  - a lone point at zoom 16;
  - non-zero bounds for a lone point and for collinear points.
- [x] 1.3 Add the unlayered map block to `src/styles.css` (design D3, D5):
  - `.tmz-map path.tmz-seq-1…7` fills;
  - the path stroke token and `.tmz-active`;
  - `.tmz-map.leaflet-container { background: transparent; font: inherit }`;
  - the `touch-action: pan-x pan-y` override, with the four-class selector of D3.

  Verify with a spec that reads `styles.css` through `node:fs` and finds:
  - the seven fill rules;
  - the container rule;
  - the touch-action rule with the selector `.tmz-map.leaflet-container.leaflet-touch-drag.leaflet-touch-zoom`, which outranks Leaflet's. If the unit-test builder refuses `node:fs`, write `scripts/check-ui-safety.sh` next to `scripts/check-env-example.sh` for this check and for those of 1.8 and 7.1.
- [x] 1.4 Build `map-engine.ts`, the only Leaflet import (design D1–D3):
  - the lazy CSS `<link>` loader;
  - `createMap(host, init)`: CRS choice, provisional limits, then `getBoundsZoom`, then the real limits and `setView(center, fitZoom)`;
  - polygons and circle markers with the base options and `tmz-seq-N` classes on one SVG renderer;
  - `maxBounds`;
  - `setData(rows)`, returning layer↔row maps;
  - `destroy()`;
  - no map at all when nothing can be drawn.

  Add `src/testing/leaflet.ts`, which stubs `clientWidth`/`clientHeight`, `matchMedia` and `link.onload`.

  Verify `map-engine.spec.ts`:
  - metre coordinates spanning about 100 km fit at a negative zoom and fill the box;
  - a lone point can be panned;
  - all rows left out creates no `.leaflet-container`;
  - shapes carry their classes and base options.
- [x] 1.5 Rewrite `map-widget.ts` on the engine, keeping the caption, legend, left-out count and "nothing to draw" message. Verify the `map-widget.spec.ts` rewrite:
  - "north up", "fits" and "lone point" are read through `latLngToContainerPoint` and `data-zoom` (scenarios `Municipalities in degrees` and `A single point`);
  - `Unparseable geometry` still counts 3 rows left out;
  - points are circle markers.
- [x] 1.6 Add `map-controls.ts` and the gestures (design D2, D5):
  - one top-end row: zoom in, zoom out, "Show all", and the basemap menu slot as a CDK radio menu;
  - below 160×140, a single "Map options" menu;
  - the capture-phase wheel gate on `#box`;
  - the one-finger and two-finger toggle of `dragging`, re-enabled by a mouse `pointerdown`;
  - both hints;
  - the reduced-motion options;
  - `0`/Home;
  - `isolate` on the Leaflet host and on `#box`, and `z-10` overlays.

  Verify `map-widget.spec.ts` for these scenarios:
  - `Zoom with the buttons`;
  - `Zoom-out limit`;
  - `Wheel without Ctrl`, with a plain `wheel` dispatched on the container and on a path: the hint shows and `data-zoom` is unchanged;
  - `Wheel with Ctrl`;
  - `One finger on a phone`: a one-finger `touchstart` disables dragging and a `touchmove` shows "Use two fingers to move the map";
  - `Two fingers on a phone`: dragging is enabled, and the `touchend` that lifts the last finger disables it again;
  - `Small map on a phone`: a 150×120 box shows only the "Map options" button, holding the same actions.

  Also verify:
  - a mouse `pointerdown` re-enables dragging;
  - `widget-grid.spec.ts` still passes.
- [x] 1.7 Keep the view in `MapWidget` signals only (design D4). New rows re-fit, a resize re-fits only while the view is untouched, and the view survives "Show as table". Verify with `map-widget.spec.ts`:
  - `Table and back`;
  - `No new query` and `Offline zoom`: with no basemap, `HttpTestingController.expectNone` after panning and zooming;
  - `Zoom stays on the viewer's screen`: a second fixture over the same widget opens fitted.
- [x] 1.8 Change `clearOverlay` in `indicators-testing.ts` to `c.replaceChildren()`. Add the guard spec, or the 1.3 script, that reads `src/app/**/*.ts` except `*.spec.ts` and fails on `innerHTML`, `bindTooltip(`, `bindPopup(`, `setContent(` or `L.control` (design D2). Verify that it fails on a planted `innerHTML` line in a component and passes once the line is removed.
- [x] 1.9 Update `docs/indicators-dashboard.md` "Maps" for Web Mercator, zoom and pan, the gestures, the compact menu and the keyboard. Remove "no pan or zoom" there and in "What it does not do". Verify that the documented keys and gestures match `map-widget.spec.ts`.

## 2. Map tooltip and keyboard access (ui)

- [x] 2.1 Add `shared/ui/chart-tooltip.ts`, exported from `shared/ui/index.ts` (design D6):
  - a CDK `Overlay` with `flexibleConnectedTo({x, y})`, four positions and a 12 px offset;
  - text-only content;
  - detached when closed;
  - no subscription to `overlayRef.keydownEvents()`.

  Verify `chart-tooltip.spec.ts`:
  - the positions;
  - text-only rendering;
  - attach and detach;
  - a CDK Dialog opened under an open tooltip still closes on Escape.
- [x] 2.2 Add `map-tooltip.ts` (pure): heading, the plotted-value line, tooltip lines and footer. Wire it up:
  - hover follow, closing when the pointer leaves;
  - pinning on tap or click, closing on an outside pointer, Escape, another shape, scroll, blur and new rows;
  - the `moveend` re-anchor;
  - closing when a tile drag starts.

  Remove the SVG `<title>` elements.

  Verify:
  - `map-tooltip.spec.ts`: the group, "(none)", and the plotted value with the indicator's format and unit;
  - `map-widget.spec.ts`: a bubbling `mouseover` on `[data-testid=map-region]` shows the overlay (`City block` without tooltip columns yet);
  - `Pointer leaves`;
  - `Tap on a phone`, including closing on a page scroll;
  - a group value `<img src=x onerror=alert(1)>` renders as text.
- [x] 2.3 Make the map accessible (design D5, D6):
  - `role="application"` with `aria-roledescription`, the summary `aria-label` and screen-reader-only key help;
  - roving focus over the shapes in table order (Enter, arrows, Home, End, Escape), with the active path `tabindex="-1"` and its attributes removed on leaving;
  - the order `bringToFront`, `tmz-active`, `focus`, then the pixel pan;
  - Escape consumed only when leaving the shapes or closing a pinned tooltip;
  - an `aria-hidden` overlay and the polite live region.

  Verify `map-widget.spec.ts`:
  - `Stepping through blocks`: after Enter and two right-arrow presses, `document.activeElement` is the third path, and its `aria-label` holds the tooltip text;
  - `One Tab stop`: after Enter, an arrow, Escape and Tab, focus is on the next control, and no path ever has `tabIndex >= 0`;
  - `Escape in the widget form`: the second Escape closes the dialog, and Escape with only a hover tooltip open closes it at once;
  - a shape bigger than the view pans to its centre, and a visible shape does not pan.

## 3. Tooltip columns, basemap field and prompt (api)

- [x] 3.1 Add the model changes (design D7, D8):
  - `WidgetTooltipItem`, plus `Tooltip` and `Basemap` as the last optional parameters of `WidgetConfigInput` and `WidgetConfig`;
  - `Normalize()` per D7, with null items kept;
  - the constants;
  - `IndicatorRow.Rows` and `.Tooltip`, and `IndicatorQueryResponse.TooltipTypes`, all `JsonIgnore(WhenWritingNull)`.

  Verify:
  - `IndicatorValidationTests.A_config_without_anything_takes_the_defaults` lists `Tooltip` empty and `Basemap` null;
  - an `IndicatorWidgetTests` case saves a map with three items and `basemap: "satellite"`, and reads them back from the create response and the overview (`Analyst adds tooltip columns`, API part);
  - a raw-`jsonb` config without both fields (`config - 'tooltip' - 'basemap'`) reads as `[]` and null;
  - `IndicatorOverviewTests` asserts the OpenAPI schemas: `WidgetConfig.properties.tooltip` and `basemap`, `WidgetTooltipItem`, `IndicatorRow.properties.rows` and `tooltip`, and `IndicatorQueryResponse.properties.tooltipTypes`.
- [x] 3.2 Add the tooltip and basemap checks to `IndicatorValidation.CheckConfig` and `CheckAgainstColumns` (design D7).

  Verify that `IndicatorValidationTests` pins each message and field path:
  - a null item;
  - more than 5;
  - a non-map type;
  - an empty, over-long, duplicate or geometry column;
  - a label over 80 characters;
  - a bad format;
  - a format on a text column;
  - an unknown column;
  - a bad basemap;
  - a basemap on a non-map.

  Verify integration tests for the scenarios `Too many tooltip columns`, `Tooltip on another chart type`, `Basemap on another chart type` and `Format offered for numbers only` (API part). Also:
  - a PATCH of only `chartType` from a map with tooltip columns to `bar` is rejected on `config.tooltip`;
  - a move-only PATCH keeps both fields.
- [x] 3.3 Extend `IndicatorQueryBuilder.Plan` and `ReadRowsAsync` (design D8):
  - the map marker (`GeometryColumn` not null);
  - `Need()` for tooltip columns, after sort;
  - `count(*)`;
  - the per-type expressions, with `NULLIF` for text and `::text` for timestamps;
  - the new `DataValues.Display(fetched, max)` overload, which the existing `Display` calls with 500;
  - the per-ordinal readers;
  - `TooltipTypes`.

  Verify `IndicatorQueryBuilderTests`: the SQL for each type, quoting through the catalog, missing tooltip columns listed last, and no extra outputs for a grouped bar query.

  Verify `IndicatorQueryTests` against PostgreSQL:
  - the scenarios `One row per block`, `Two rows for one block`, `Filters apply to the tooltip`, `Empty text in one row` and `Tooltip column removed by a script change`;
  - rows of only `''` give null;
  - a ratio widget whose rows have a numerator but no denominator, counted in `rows`;
  - a timestamp with offset in the project's time zone;
  - mixed booleans give false;
  - a 250-character text cut to 200 plus "…";
  - a decimal `NaN` gives null;
  - a grouped bar query whose payload has no `rows`, `tooltip` or `tooltipTypes`.
- [x] 3.4 Correct `Assistant/Prompts/system.md:41`: coordinates are not converted, so project to degrees first. Add the map note on one row per shape and summed tooltip numbers (design D19). Verify that `PromptKitTests` passes and that the prompt version follows the kit's versioning rule.

## 4. Tooltip columns (ui)

- [x] 4.1 Add the UI side of the config (design D7, D11):
  - the types and `emptyConfig()`;
  - `queryFor` sending tooltip column names only, and `basemap: null`;
  - `savedConfig()` restoring the items and the basemap for maps;
  - `script-editor/indicator-view.ts` building from `emptyConfig()`;
  - the `row()` factory in `indicators-testing.ts`.

  Verify:
  - `query-for.spec.ts` covers both functions, the "null, not left out" rule and the other types;
  - the literal at `indicators.service.spec.ts:73` is updated;
  - `project-indicators.spec.ts` and the `map-widget.spec.ts` test `asks for the geometry column along with the indicator` pass without edits.
- [x] 4.2 Format the tooltip columns in `map-tooltip.ts` (design D9):
  - formats only for numeric `tooltipTypes`;
  - the type defaults;
  - `timestampText()` in `shared/ui/date-format.ts`, using the value's own offset;
  - `mediumDate` for dates;
  - Yes/No, "—", and the "Combines N rows" footer.

  Verify `map-tooltip.spec.ts`:
  - the scenarios `City block` (all three columns), `Progress shown as a percentage`, `Missing value`, `Tooltip column keeps its own format` and `Column no longer a number`;
  - `Timestamp`: `2026-09-01T21:30:05-05:00` reads `Sep 1, 2026, 9:30 PM` with the test's time zone set to UTC;
  - item order;
  - `Analyst adds tooltip columns` (UI part): the items `address`/"Dirección" and `sample_target` with no label render `Dirección: …`, then `sample_target: …`;
  - `Markup in a value`: `<img src=x onerror=alert(1)>` as a tooltip value and as a label renders as text in `map-widget.spec.ts`.
- [x] 4.3 Add the tooltip editor to `widget-dialog.ts`, under the geometry select, in the filter-chip pattern:
  - each item: a column (every column except the geometry), a label (the column name as placeholder), a format (numeric columns only), up/down and remove;
  - "Add" disabled at 5;
  - the raw `formConfig`, with the preview and the save body as `savedConfig(type, formConfig)`;
  - `pick()` resetting the items;
  - `columnsWith` keeping missing saved columns, marked;
  - `Problems` and `fieldFor` for `config.tooltip[i].*`.

  Verify `widget-dialog.spec.ts`:
  - add, remove, reorder and the limit of 5;
  - formats for numbers only;
  - `Extra column shown in a tooltip`: the geometry column is not offered;
  - reset on a new indicator;
  - server errors under the right item;
  - the save body;
  - a type change to bar sends `tooltip: []` (`Map turned into another chart type`);
  - editing a label or format sends no query (`expectNone`), and `view-loading` never appears;
  - a widget whose formatted column has since become text, saved after changing only its title, is accepted with that item's `format: null`.

  Update the literal at `:375`.
- [x] 4.4 Add `TableModel.align` to `widget-table.ts`. The map table gets the columns group, plotted value, tooltip labels and geometry kind. Verify the scenario `Table of blocks` in `map-widget.spec.ts`, with text columns aligned to the start.
- [x] 4.5 Update `docs/indicators-dashboard.md`:
  - tooltip columns and their rules after "Maps";
  - the city-block example: one row per block, `value` in `percent`, and the items "Dirección", "Meta muestral" and "Encuestas logradas", with the notes on capped and uncapped progress, a target of 0, and dividing with `where`;
  - tooltip columns in the stale list;
  - the limits table.

  Verify the example by running it through `contract.validated` and `dashboard_view` in the `tamiza-analytics-test:dev` image.

## 5. Basemap settings and list (api)

- [x] 5.1 Add `Maps/MapOptions.cs`, `MapOptionsValidator.cs` and `MapRegistration.cs` (design D12):
  - the six variables;
  - the unbound window and credits-budget properties;
  - `ValidateOnStart`;
  - the startup line and the "ignored" warning;
  - registration in `Program.cs`.

  Verify `MapOptionsTests`:
  - each rule names its variable, including `Unknown provider` and `Google without a key`;
  - blank strings read as unset;
  - the integer defaults;
  - the ranges;
  - a malformed language with provider `none` starts with a warning;
  - the region is uppercased.
- [x] 5.2 Pass the variables to `api` in `compose.yaml`: the strings as `${VAR:-}`, and `TAMIZA_MAP_TILES_PER_MINUTE: ${TAMIZA_MAP_TILES_PER_MINUTE:-600}` and `TAMIZA_MAP_TILES_PER_USER_PER_DAY: ${TAMIZA_MAP_TILES_PER_USER_PER_DAY:-3000}`. Document them in a `# --- Map basemaps (optional) ---` block of `.env.example`, with the integers written out and the data-protection paragraph. Verify:
  - `scripts/check-env-example.sh` passes;
  - `docker compose config` shows them only under `api`;
  - `docker compose up` with no map variables starts `api` in the verification project.
- [x] 5.3 Add `GET /api/v1/basemaps` (`ListBasemaps`, tag `Maps`), with the members-or-superadmin rule, memoized links per window, and `linksRefreshInSeconds` (design D13, D15). Verify integration tests:
  - `Anonymous request` and `Nothing configured`;
  - `User without a project` gives `[]` and no link;
  - `OpenStreetMap` gives the OSM entry, `maxZoom` 19 and its attribution;
  - `Google` gives two entries, with links and no key (`Key not in the browser`, list part);
  - `Dashboard reloaded`: two calls in a window give identical templates, and a call in the next window gives a new one, using a `TimeProvider`;
  - `SystemConfigTests` unchanged;
  - the OpenAPI path and the `Basemap` schema.
- [x] 5.4 Write the operator docs:
  - In `docs/operations.md`, a "Map basemaps (optional)" section:
    - turning it on, and the variable table;
    - OSM's policy in short;
    - Google's setup: terms, billing, a key restricted to the Map Tiles API and the `api`'s egress address, the daily console quota as the cost cap, billing alerts, the per-user limits against the quota, and the end-user terms reference;
    - one provider per deployment;
    - what leaves the deployment, and that any member can turn a basemap on;
    - outer-proxy logs of tile links;
    - the data-protection paragraph;
    - the rollback caveat.
  - Add the outbound bullet after `operations.md:11`.
  - In `docs/tamiza-brief.md`, edit only `:260`: drop "map" from "no chart or map library" and drop "with no basemap". Add an `improve-map-widget` entry to the brief's change list.

  Verify that the names match `MapOptions`.

## 6. Google tile relay (api)

- [x] 6.1 Register the `maps-google` `HttpClient` (design D15): no redirects, no proxy, a 10 s timeout, a 2 MB limit, `RedactLoggedHeaders` and `RemoveAllLoggers()`. Verify with a unit test that captures every log of a relayed tile, a failed tile and a session creation, and finds the key in none.
- [x] 6.2 Add `Maps/GoogleTileSessions.cs`: one lazy session per kind, renewed under an hour from expiry and once after a rejection, with a semaphore per kind. Verify unit tests with a fake handler:
  - the request body;
  - reuse;
  - renewal near expiry;
  - `Session expired at Google`;
  - concurrent callers create one session.
- [x] 6.3 Add tile links: 45-minute clock-aligned windows; a plain `IDataProtector` with purpose `Tamiza.Maps.TileLink.v1` over `userId|kind|windowStart`; expiry at the window's end plus 15 minutes, checked with the injected `TimeProvider`; memoized in `IMemoryCache`. Verify unit tests with a fake clock:
  - a round trip;
  - the same string within a window;
  - a valid link 10 minutes after its window;
  - expiry;
  - a tampered link;
  - another purpose;
  - a kind mismatch.
- [x] 6.4 Add `GET /api/v1/basemap-tiles/{link}/{z}/{x}/{y}` (`GetBasemapTile`, `AllowAnonymous`):
  - checks 0 to 3;
  - the minute limiter and the day counter;
  - the fetch with `If-None-Match`;
  - pass-through with CORP;
  - the 404, 429, 503 and 502 mapping with the `basemaps.*` codes.

  Verify integration tests with a fake Google primary handler:
  - the scenarios `Forged or expired link` (with `AdjustableTimeProvider.Advance(61 min)`), `Coordinates out of range` and `Provider switched off`, with the handler never called;
  - `Session expired at Google`, tile part: the fake rejects the first `2dtiles` call for its session, and the endpoint creates one session, retries once and answers 200. Two rejections in a row give 502 `basemaps.upstream`, with no further retry;
  - `Google basemap`, tile part: the fake receives only `GET /v1/2dtiles/{z}/{x}/{y}` with the query `session` and `key` (plus `If-None-Match`), with no other parameter and no body;
  - `Cache rules passed through`: the same `Cache-Control`, and `Cross-Origin-Resource-Policy: same-origin`;
  - `Too many tiles in a minute` and `Daily allowance used up`: 429 with `Retry-After`, and another user still served;
  - `Google quota exhausted` gives 503;
  - a missing tile gives 404;
  - a 304 is passed through;
  - Google's body never appears in the response;
  - no file is written under the API's data paths.
- [x] 6.5 Add `GET /api/v1/basemaps/{kind}/credits` (`GetBasemapCredits`): 404 unless the provider is `google` and the kind is offered, range checks, the viewport call, `{text, maxZoom}`, and its own budget. Verify integration tests:
  - 401 anonymous;
  - 404 under `osm`;
  - 400 on a bad box;
  - text and the largest `maxZoom` of the rectangles containing the view's centre, from a fake viewport shaped like Google's (world-wide 19 and 9 first);
  - `Google basemap`, credits part: the viewport call carries only session, key, zoom and the four bounds;
  - the budget is separate from tiles: a user at their tile limit still gets credits;
  - no key in the response.
- [x] 6.6 Check against Google's documentation and record in the design:
  - the viewport URL (`https://tile.googleapis.com/tile/v1/viewport`);
  - its `copyright` and `maxZoomRects` fields;
  - the status Google returns for a tile beyond the local imagery.

  Verify that the fakes in 6.4 and 6.5 use the documented URL, fields and status.

## 7. Basemaps in the UI

- [x] 7.1 Change `nginx-security-headers.conf:3` to `img-src 'self' data: https://tile.openstreetmap.org`. Add `location ^~ /api/v1/basemap-tiles/` to `nginx.conf`, with the `/api/` proxy settings, `proxy_buffering off;`, `access_log off;` and the security-headers include (design D14, D15). Verify with a spec, or the 1.3 script, that reads both files and asserts:
  - the `img-src` value and no other image source;
  - `Referrer-Policy "no-referrer"` kept;
  - no `unsafe-eval`;
  - the tile location with `^~`, buffering off, access log off and the include.
- [x] 7.2 Add `basemaps.service.ts` (design D13, D16):
  - one load per page;
  - `[]` on error;
  - a refresh `linksRefreshInSeconds` after arrival, at least 60 s, only while a Google layer is mounted;
  - re-checks on `visibilitychange` and on the first tile error after the links are due;
  - a provider-change signal.

  Add the three `basemaps.*` codes to `error-codes.spec.ts` and `en.json` `errors.codes.basemaps`.

  Verify `basemaps.service.spec.ts` with fake timers:
  - one request per page;
  - `[]` on 503;
  - the refresh time unchanged with `Date.now` skewed by ±1 hour;
  - a late timer (sleep) still refreshes before the links' expiry margin ends;
  - no refresh without a Google layer.

  Verify that `error-codes.spec.ts` passes.
- [x] 7.3 Add the OSM layer and attribution (design D14, D17):
  - `referrerPolicy: 'strict-origin'`, `maxNativeZoom` and `data-testid="map-tile"`;
  - a bottom-start overlay with the OSM text and https link (`rel="noopener noreferrer"`), wrapping.

  Verify `map-widget.spec.ts`:
  - `Referrer of a tile`, browser part: the tile `src` follows the template and `referrerPolicy === 'strict-origin'`;
  - `OpenStreetMap credit` and `No basemap, no credit`;
  - `Offline zoom`, re-run with a provider configured but the widget set to None: after the list request is flushed, no `map-tile` and no tile or credits request while zooming;
  - with a basemap, shapes have `fill-opacity` 0.7 and the legend swatches the same opacity; without one, both are 1.
- [x] 7.4 Add the Google layer (design D15, D16):
  - credits before the layer;
  - `maxNativeZoom` from `maxZoom`, with `redraw()`;
  - credits on `moveend` (debounced, cached), keeping the last on failure;
  - `setUrl(template, true)` on a refresh, and a rebuild on a provider change;
  - the outlined logo from Google's attribution assets under a versioned `public/vendor/google/` path: 16–19 px, the clear space and `alt="Google Maps"`, never replaced by text.

  Record the asset's source in the design.

  Verify `map-widget.spec.ts`:
  - `Google credits follow the view`;
  - `Zooming past the imagery`: with `maxZoom` 18, z20 requests no tile above 18 and the layer stays;
  - `Google credits unavailable` and `Credits request fails after a pan`;
  - a refresh changes the template of new tiles only;
  - a refresh returning `osm` rebuilds the layer and shows the OSM credit;
  - the logo's variant, size and alt;
  - at the D17 minimum box, the logo, its clear space and one line of credits fit without overlapping.
- [x] 7.5 Add the basemap select to `widget-dialog.ts` (None, the offered kinds, and "(no longer offered)"). Fill the basemap menu of the controls and of the compact menu with the member switch (radio items, `aria-checked`), the live-region announcement, and the effective-basemap rule of D11, including `'none'`. Show the not-degrees note. Verify:
  - `widget-dialog.spec.ts`:
    - `Deployment without basemaps`;
    - `Saved basemap kept`;
    - `Analyst picks satellite`: the preview map has `data-basemap="satellite"` and draws `map-tile` elements once the `basemaps/satellite/credits` request is flushed, and `expectNone` holds on the indicator query URL.
  - `map-widget.spec.ts`:
    - `Viewer turns on satellite`: no new indicator query (`expectNone` on the widget's query URL) and no widget POST or PATCH; the one credits request is flushed before `map-tile` appears;
    - `Small map on a phone`, basemap part: at 150×120 the "Map options" menu holds None and the offered kinds as radio items with `aria-checked`;
    - `Viewer turns a basemap on`;
    - `Viewer turns the basemap off`;
    - `Provider changed by the operator`;
    - `Coordinates that are not degrees`;
    - the switch is reset by new rows.
- [x] 7.6 Create the Leaflet map on first intersection (IntersectionObserver, 200 px margin; at once without it). Add the 200×120 minimum for tile layers, the tile-health counting with the due-link retry, and the "Basemap unavailable" notice (design D17). Verify `map-widget.spec.ts`:
  - with an IntersectionObserver stub, no map exists before the widget intersects;
  - `Map resized to one row`: no `map-tile`, no credits request, and the note;
  - `Tile server unreachable`: dispatching `error` on every tile gives one announced notice, and shapes, zoom, tooltip and table keep working;
  - a due Google link re-fetches the list once before the notice;
  - switching the basemap retries.
- [x] 7.7 Update `docs/indicators-dashboard.md` for:
  - the basemap per widget and the member switch;
  - attribution;
  - small maps;
  - "no internet connection" now holding only without a basemap;
  - the not-degrees note;
  - "What it does not do".

  Verify that the wording matches the spec scenarios and that the links resolve.

## 8. Stack and device verification

- [x] 8.1 Run the full suites: `dotnet test` in `tamiza-api/`, and `npm test -- --watch=false` and `npm run build` in `tamiza-ui/`. Verify they pass, apart from the two known polling failures, and record the counts in a `## Verification Results` section of the design.
- [x] 8.2 In a separate Compose project (`tamizaverify`), publish a block indicator: polygons in degrees, three tooltip columns, and duplicate rows for one block. Check and record:
  - **none:**
    - `curl -sI` on the built `ui` shows the D14 CSP and `no-referrer`;
    - zero third-party requests while zooming (`Offline zoom`, `Not configured`);
    - the shape colours match the legend;
    - the background stays plain;
    - the controls and tooltip paint above the map.
  - **`osm`:**
    - tiles load, and each tile request goes only to `tile.openstreetmap.org/{z}/{x}/{y}.png` with the origin as its `Referer` (`OpenStreetMap basemap`);
    - the export link still sends no referrer;
    - the OSM credit is visible.
  - **`google`,** with an operator key the user supplies for the check:
    - satellite and streets load;
    - the browser makes no request to any Google host (`Google basemap`);
    - no response, page or `docker logs` of `api` or `ui` holds the key, and the `ui` access log holds no tile link;
    - the credits change on pan;
    - zooming past the imagery keeps the layer;
    - reloading reuses cached tiles.
  - Then remove a tooltip column in a script and run again: the widget names it.
- [ ] 8.3 Check by hand on real devices and record:
  - desktop Chrome, Firefox and Safari: Ctrl/⌘+wheel, trackpad pinch, drag and double-click;
  - iOS Safari and Android Chrome: one-finger page scroll with the hint, two-finger pan and pinch, and tap to pin;
  - a touchscreen laptop: mouse drag and finger scroll;
  - NVDA (focus mode) and VoiceOver stepping through shapes, with no browse-mode flood;
  - 20 maps on one dashboard: tiles load only for widgets on screen;
  - 5,000 block polygons on a mid-range Android phone: first draw within 2 s, and pan and zoom without visible stalls. If this fails, raise `smoothFactor` for large sets and measure again.

## Workflow follow-up

- Archive the change after review. This syncs the `dashboard-indicators` delta and the new `map-basemaps` spec into `openspec/specs`.
- Release notes: the rollback caveat (design Risks and Migration Plan step 4), the new CSP origin and the new tile location.
