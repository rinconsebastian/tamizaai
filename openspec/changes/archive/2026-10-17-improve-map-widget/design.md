# Design

## Context

- **The map today** (`tamiza-ui/src/app/pages/projects/indicators/map-widget.ts`):
  - It is a custom SVG renderer. `parseWkt` (`wkt.ts`) reads `POINT`, `POLYGON` and `MULTIPOLYGON` and drops any `SRID=`.
  - `projection()` (`:130-148`) fits one equirectangular transform to the rows' bounding box. It guesses "degrees" from the value ranges (`:137`), and a lone point falls back to a scale of 1 (`:143-144`).
  - Polygons are `<path>` and points `<circle r=6>`, filled through `[style.fill]` bindings on template elements, with `var(--tmz-seq-N)` (`chart-scales.ts:102-113`; the tokens are global in `src/styles.css:52-76`).
  - The only tooltip is an SVG `<title>` reading "group: value" (`:45`, `:49`). The SVG is `role="img"` with a summary label (`:41`).
  - There is no view state. `@if (asTable())` lives inside the component's own template (`:37`), so the component instance survives "Show as table".
- **Styles.**
  - Every component uses Angular's default Emulated encapsulation; nothing in `src/app` sets `encapsulation` or uses `::ng-deep`. A component rule therefore matches only elements the template created.
  - Tailwind 4's utilities sit in a cascade layer (`src/styles.css:5`), so any unlayered stylesheet, such as `leaflet.css`, beats them.
- **Dialog and query.**
  - `queryFor` (`widget-view.ts:24-35`) whitelists the fields each chart type sends.
  - The dialog builds its `draft` through `queryFor` (`widget-dialog.ts:366-378`). That draft is both the save body and the preview's `[config]` (`:250`, `:428-431`), which `WidgetView` hands to `MapWidget` (`widget-view.ts:93`).
  - `WidgetView`'s re-query key is `JSON.stringify({type, config: queryFor(...)})` (`:165-168`). A changed key swaps the chart for the loading skeleton at once (`:174-176`).
  - `DropdownMenu` (`shared/ui/dropdown-menu.ts`) has plain items only: no radio items, no checked state, and a fixed three-dot trigger.
- **API query.**
  - The grouped map query is `SELECT "g"::text, <aggregate>, min("geom"::text) … GROUP BY "g"` (`IndicatorQueryBuilder.cs:172-208`). The same branch serves bar, column, line, pie and stat queries.
  - `Plan` knows no chart type beyond `perRow` (`:84-85`).
  - Its grouped `WHERE` keeps rows with a non-null measure, or with a non-null numerator or denominator for a ratio (`:163`, `:169`), plus the filters.
  - `Need()` lists missing columns in this order: group, geometry, measure, ratio, filters, sort (`:91-119`). Any missing name means no SQL and a `missing.columns` answer.
  - The text sort key already treats an empty text as missing: `min(NULLIF(c::text, ''))` (`:73`).
- **Values and types.**
  - `DataValues.Number` and `DataValues.IsoTimestamp` parse the text PostgreSQL returns. Timestamps are selected as text so PostgreSQL applies the session time zone, which is the project's (`DataValues.cs:8-9, :26`; `DataDashboard.cs:457`).
  - Column types are `integer`, `decimal`, `boolean`, `date`, `timestamp` and `text`. Geometry is a text column of WKT.
  - Indicator columns have no labels, and their names are reduced to lowercase ASCII, so `Dirección` arrives as `direccion`.
- **Config storage.** `dashboard_widgets.config` is versionless `jsonb`, and an absent property reads as null (`IndicatorEndpoints.cs:288-289`). A PATCH re-serializes the stored config (`:200`).
- **Grid** (`widget-grid.ts`):
  - CDK drags start only from the tile's handle, and grid arrow keys are bound on the handle buttons only.
  - The tile body is `overflow-hidden` (`:78`), and the resize handle is bottom-end (`:95`).
  - Rows are 72 px, and a widget may be 1×1 (`grid-layout.ts:4, :24-28`).
- **Security headers and nginx.**
  - The CSP has `img-src 'self' data:` and `connect-src 'self' https: …` (`nginx-security-headers.conf:3`), plus `Referrer-Policy: no-referrer` (`:5`), which the export links rely on.
  - The file is static (`Dockerfile:11-12`); setup-foundation D4 rejected templating nginx from the environment.
  - `location ~* \.(?:js|css|woff2?|ico|png|svg|json)$` caches as `immutable` for a year (`nginx.conf:85-87`).
  - The export-link location `^~ /api/v1/data-exports/` turns proxy buffering off (`:64-76`).
  - `ng serve` sends no CSP.
- **Auth and settings.**
  - Endpoints require a signed-in user through the fallback policy (`AuthenticationRegistration.cs:36-38`) unless they opt out with `AllowAnonymous()`: system config, health, OpenAPI, the webhook, and the export link `/data-exports/{token}`, which is the precedent for a link that is its own credential.
  - Any user of the realm gets a local record on first request, member of a project or not.
  - The assistant's settings use flat `TAMIZA_*` variables with `[ConfigurationKeyName]`, `IValidateOptions` with `ValidateOnStart`, and a named `HttpClient` with no redirects, no proxy and redacted headers.
  - Compose gives numeric variables a default (`compose.yaml:42-44`), because binding an empty string to an `int` throws.
  - Data Protection keys live on an api-only volume.
- **Others.**
  - `indicators-testing.ts:132` uses `innerHTML` in a test helper. It is the only `innerHTML` under `src/app` outside spec files.
  - The assistant's prompt claims geometry is published "in degrees" (`Assistant/Prompts/system.md:41`). The SDK never reprojects.
- **External terms**, read on 2026-10-17:
  - **OpenStreetMap's tile policy:**
    - use exactly `https://tile.openstreetmap.org/{z}/{x}/{y}.png`;
    - visible attribution, never "behind toggles or off-screen";
    - a valid `Referer`; referrer-stripping traffic may be blocked;
    - no prefetching or offline use;
    - no tunnelling of all clients behind one identity.
  - **Google Maps Platform:**
    - only the Map Tiles API: `createSession`, then `2dtiles/{z}/{x}/{y}?session&key`, plus `tile/v1/viewport` for credits and `maxZoomRects`;
    - the key is a web-service key, restricted by IP only, "not expected to be publicly exposed";
    - an unmodified outlined logo on map backgrounds, 16–19 dp, with clear space and the label "Google Maps";
    - no caching beyond `Cache-Control`;
    - no use "with or near a non-Google map" (3.2.3(e));
    - billing per tile after 100,000 a month, and a default quota of 15,000 2D tiles a day.

**Decided with the user (2026-10-17):**
- Providers are set by the operator, with no basemap and no requests by default.
- A basemap is chosen per widget, and any member can switch it for themselves without saving.
- Tooltip numbers are summed and other columns show one value.
- Google is in this change, through the `api`, so the key never reaches the browser.
- Satellite comes from Google only.
- One provider per deployment.

**Decided by this design** (open to revision):
- Leaflet;
- the view is not saved;
- the limits: 5 tooltip columns, labels of 80 characters, text cut at 200, 600 tiles per minute and 3,000 per day per user;
- the smallest value as "one value", with an empty text counting as no value;
- per-column formats;
- staleness when a tooltip column is missing;
- the narrow CSP;
- links only for project members.

The requirements are in `specs/dashboard-indicators` and `specs/map-basemaps`.

## Goals / Non-Goals

**Goals:**
- With no basemap configured, the map makes no request outside the deployment, as today, and zoom, pan and tooltips still work.
- No HTML sink: tooltip, attribution, labels and controls go through Angular text bindings.
- The Google key never leaves the `api`. Tamiza stores no tile.
- Panning, zooming, changing the basemap and editing tooltip labels or formats never re-query.
- No migration: widgets saved before this change draw as before.
- Leaflet code stays in one adapter file, so a later Leaflet 2 migration touches only it.

**Non-Goals:**
- A canvas or WebGL renderer, vector tiles, or more than one basemap layer at a time.
- Operator-defined XYZ providers or self-hosted tiles. The configuration shape leaves room for them.
- Moving the other charts to the new tooltip. They keep their SVG `<title>`.
- Dark-mode basemaps. No code adds the `.dark` class today.
- Usage metrics for tiles. Google Cloud's console reports usage and quota.

## Decisions

### D1. Engine: Leaflet 1.9.4, pinned and lazy, behind one adapter (ui)

- **Packages.** Add `leaflet@1.9.4` and an exact `@types/leaflet` 1.9.x pin (development only).
- **One import site.** `indicators/map-engine.ts` is the only file that imports Leaflet, through `await import('leaflet/dist/leaflet-src.esm.js')`, with a `declare module` shim in `src/types/leaflet-esm.d.ts`.
  - That puts Leaflet (about 43 KB gzipped) in a lazy chunk. Lazy chunks have no budget, and the initial bundle has about 21 KB of headroom.
  - If esbuild does not split the ESM file, fall back to `import('leaflet')` plus `allowedCommonJsDependencies` (task 1.1).
- **Why Leaflet.**
  - It keeps an SVG renderer, so focusable shapes, CSS-token colours and jsdom tests keep working. That was the real constraint behind D5/D6 of the indicators dashboard.
  - It hands the field-fragile parts to a frozen, well-tested engine: the tile grid, wheel and trackpad quirks, pinch, inertia, zoom animation and per-zoom simplification.
  - Three judges scored the options independently, and all three picked Leaflet.
- **Alternatives.**
  - *MapLibre GL 6.14:* about 425 KB gzipped with its worker. It requires WebGL2, its constructor throws in jsdom, and it needs `worker-src` and `img-src blob:`. Its shapes cannot take focus.
  - *OpenLayers 10.11:* about 99 KB gzipped. It draws on Canvas, which cannot be tested in jsdom.
  - *Extending the custom SVG renderer:* about 600–1,000 lines of tile, gesture and Web Mercator code that jsdom cannot check. It would be a rebuild of Leaflet's GridLayer.

### D2. Leaflet is the engine only; everything visible is Angular (ui)

- **Created bare.** The map is created with `zoomControl: false` and `attributionControl: false`.
- **Never used.** `bindTooltip`, `bindPopup`, `setContent`, `L.control.*` and attribution strings are never used, because in 1.9.4 they write `innerHTML`.
- **Angular renders the rest.** Zoom buttons, "Show all", the basemap menu, attribution, hints and notices are Angular/Tailwind templates. They sit as siblings of the Leaflet host inside `#box`.
- **Stacking.**
  - The Leaflet host gets `isolate`. Its panes (z-index 200–800) then stack inside it, and the overlays, with `z-10`, paint above it.
  - `#box` also gets `isolate`, so nothing in it competes with the dragged tile (`z-30`) or the resize handle (`z-20`).
- **Guard test.**
  - It fails on `innerHTML`, `bindTooltip(`, `bindPopup(`, `setContent(` or `L.control` in `src/app/**/*.ts` outside `*.spec.ts` files.
  - The test helper's `clearOverlay` (`indicators-testing.ts:132`) changes to `c.replaceChildren()`, so the guard needs no other exception.

### D3. Projection, shapes, styles and zoom limits (ui)

- **CRS by kind of coordinates.**
  - Degrees (today's test at `map-widget.ts:137`, moved to a pure `map-geometry.ts`) use `L.CRS.EPSG3857`.
  - Other coordinates use `L.CRS.Simple`, with no basemap. A basemap, saved or switched on, then gives the note "The basemap cannot be shown: the coordinates are not degrees".
- **Shapes.** `L.polygon` (holes and multipolygons included) and `L.circleMarker` are built from `wkt.ts`, on one `L.svg({ padding: 0.5 })` renderer.
  - Base options: polygons `weight: 1, fillOpacity: 1`; markers `radius: 6, weight: 2, fillOpacity: 1`.
  - Over a basemap, `fillOpacity` is 0.7, and the legend swatches get the same opacity.
- **Styles must be global.**
  - Leaflet creates its own DOM, which never carries the component's `_ngcontent` attribute, so component styles cannot reach it.
  - An **unlayered block in `src/styles.css`**, next to the `--tmz-seq-*` tokens, holds:
    - `.tmz-map path.tmz-seq-N { fill: var(--tmz-seq-N) }` for N = 1–7;
    - `.tmz-map path { stroke: var(<neutral token>) }`, and the `.tmz-active` highlight stroke;
    - `.tmz-map.leaflet-container { background: transparent; font: inherit }`, which beats Leaflet's grey `#ddd` and its Helvetica;
    - the `touch-action` override (D5), written as `.tmz-map.leaflet-container, .tmz-map.leaflet-container.leaflet-touch-drag, .tmz-map.leaflet-container.leaflet-touch-drag.leaflet-touch-zoom { touch-action: pan-x pan-y }`. The last selector, (0,4,0), outranks Leaflet's `.leaflet-container.leaflet-touch-drag.leaflet-touch-zoom { touch-action: none }`, (0,3,0), whatever the load order.
  - Leaflet writes `fill` and `stroke` as presentation attributes, which author CSS overrides, so the token and dark-mode swap keep working.
  - A spec reads `styles.css` through `node:fs` and checks that the seven fill rules exist. The 8.2 manual check confirms that the colours match the legend.
- **Fit and limits.**
  1. Create the map with provisional limits (`minZoom: -30` for Simple, `0` for EPSG3857; `maxZoom: 20`) and `zoomSnap: 0.25`.
  2. Compute `fitZoom = getBoundsZoom(data, false, [10, 10])`, capped at 16 for EPSG3857.
  3. Set `minZoom = fitZoom − 3` (at least 0 for EPSG3857), and `maxZoom` 20, or `fitZoom + 10` for Simple.
  4. Then `setView(data.getCenter(), fitZoom)`. "Show all" uses the same call. `fitBounds` is not used, because it doubles the padding and ignores the cap of 16 unless it is passed, so a lone point would open at 20.

  Computing the fit first matters: `getBoundsZoom` clamps to the current minimum, so a planar map would otherwise fit at 0 instead of about −9. A lone point opens at zoom 16.
- **Bounds.** After the fit, `maxBounds = data.pad(1)` extended by `map.getBounds().pad(1)`, with `maxBoundsViscosity: 0.8`. This leaves panning room around a lone point or a row of points, which would otherwise give a zero-size box, while keeping tiles near the data.
- **Tile zoom.** Each tile layer has `maxZoom: 20`. Its `maxNativeZoom` is 19 for OSM. For Google it is the depth Google reports for the view (D15), so Leaflet enlarges the deepest real tiles.
- **Nothing to draw.** With zero drawable shapes, no Leaflet map is created: no controls and no tiles, only the "nothing to draw" message. `fitBounds` would throw on empty bounds.
- **Alternative rejected:** keep equirectangular. It is incompatible with Web Mercator tiles.

### D4. The view lives in the component and is never saved (ui)

- **Signals.** `view { center, zoom, touched }` and `viewerBasemap` (D11) are signals on `MapWidget`.
- **"Show as table".** It destroys `#box` and with it the Leaflet map. Coming back rebuilds the map from the signals.
- **Rows and resize.** New rows reset the view and the member's switch and re-fit. A resize calls `invalidateSize({ pan: false })` and re-fits only while `!touched`.
- **Never stored.** The view is not written to `WidgetConfig`, `queryFor` or browser storage. The dialog preview has its own instance.
- **Alternative:** a saved starting view. That is later work, kept possible because view state stays out of the query.

### D5. Gestures, keyboard and controls (ui)

- **Wheel.**
  - A capture-phase `wheel` listener on `#box`, the Leaflet host's parent and therefore always an ancestor of the event target, calls `stopPropagation()` (never `preventDefault()`) unless Ctrl or ⌘ is held.
  - The page then scrolls, and a 1.5 s `aria-hidden` hint shows "Hold Ctrl and scroll to zoom" ("⌘" on macOS).
  - With Ctrl or ⌘ the event reaches Leaflet's `scrollWheelZoom`, as the Chrome and Firefox trackpad pinch does.
- **Touch: a gesture-time toggle.**
  - Leaflet 1.9.4 uses native touch events, and its drag handler calls `preventDefault()` on `touchmove`, which no `touch-action` can override.
  - So a capture-phase `touchstart` listener on `#box` calls `map.dragging.disable()` when one finger touches, and enables it when two do. A `touchend` that leaves no finger on the screen disables it again.
  - A mouse `pointerdown` re-enables dragging, so mouse drag keeps working on touchscreen laptops.
  - The browser fixes `touch-action` when a touch starts, before any listener runs. So the global override (D3) keeps `pan-x pan-y` on the container even while Leaflet's `leaflet-touch-drag` class is present, and the browser can scroll the page under one finger.
  - Two fingers pinch and pan through Leaflet's touch-zoom handler.
  - A passive one-finger `touchmove` shows "Use two fingers to move the map".
- **Mouse.** Drag, double-click and shift-drag box zoom are Leaflet's defaults. A click pins the tooltip, and Leaflet suppresses the click that ends a drag.
- **Reduced motion.** `zoomAnimation`, `fadeAnimation` and `inertia` are off under `prefers-reduced-motion: reduce`.
- **Keyboard.** The container is Leaflet's `tabIndex=0` element. Leaflet keeps its arrows and `+`/`-`.

  | Focus on | Key | Effect |
  |---|---|---|
  | The map | `0`, Home | Show all |
  | The map | Enter | Into the shapes; the first is highlighted |
  | A shape | Arrows | Next or previous shape in "Show as table" order |
  | A shape | Home, End | First or last shape |
  | A shape | Escape | Back to the map |

  - **Escape** is consumed (`stopPropagation()`) only when it leaves the shapes or closes a pinned tooltip. A hover tooltip never consumes it, so a second Escape, or one with only a hover tooltip open, reaches the CDK Dialog of the widget form.
  - Leaflet's document keydown is attached only while its container has focus, so arrows inside the shapes do not pan.
- **Controls** (`map-controls.ts`).
  - One horizontal row, top-end: zoom in, zoom out, "Show all" and the basemap menu (None plus the offered kinds).
  - The menu is built on the CDK menu directly, with `cdkMenuItemRadio` and `cdkMenuItemChecked`, in the visual style of `DropdownMenu`, which has no radio items.
  - **Compact mode.** When the box is smaller than 160×140, the row collapses into one "Map options" menu button that holds the same actions, so zoom and the basemap switch stay available at every size.
  - The controls are kept clear of the bottom-end resize handle.
- **Grid.** A tile drag closes the tooltip (task 2.2).

### D6. Tooltip and accessible shapes (ui)

- **Component.** A new `shared/ui/chart-tooltip.ts`:
  - It uses `Overlay` with `flexibleConnectedTo({ x, y })`, four fallback positions and a 12 px offset.
  - Its content is `{ heading, lines: { label, value, align }[], footer }`, rendered through text bindings only.
  - It escapes the tile's `overflow-hidden` and Leaflet's panes, and works inside the dialog.
  - It never subscribes to `overlayRef.keydownEvents()`. CDK's keyboard dispatcher would otherwise give it the dialog's Escape. Its Escape close is driven by the map's key handler.
  - It is detached, not hidden, when closed.
- **Anchor.** The Leaflet layer event's `containerPoint`, plus `#box.getBoundingClientRect()`, throttled with `requestAnimationFrame` through `setOrigin()` and `updatePosition()`.
- **Hover.** The tooltip follows the pointer, has `pointer-events: none`, and closes when the pointer leaves the shape or a drag or zoom starts.
- **Pinned** (tap, click or keyboard). It closes on an outside pointer, Escape, another shape, page scroll, focus leaving the map, or new rows. It re-anchors on `moveend`.
- **Content** (`map-tooltip.ts`, pure):
  - heading: the group, or "(none)";
  - `<indicator label>: <plotted value>`;
  - each tooltip column as `<label or column name>: <value>`;
  - footer: "Combines N rows" when N > 1.
- **Accessibility.**
  - **Container.** `role="application"` with `aria-roledescription="map"`, the existing summary as `aria-label`, and `aria-describedby` pointing at screen-reader-only key help.
    - `application` makes NVDA and JAWS switch to focus mode, so Enter and the arrows reach the page. In browse mode they would keep those keys.
    - "Show as table" stays the full text alternative.
    - The SVG loses `role="img"`.
  - **Roving focus.** Only the active path has `tabindex="-1"`, `role="img"` and an `aria-label` holding the full tooltip text. It is focusable from script only, so the container stays the map's single Tab stop.
  - **Leaving the shapes** (Escape, or focus leaving) removes the attributes.
  - **Activating a shape** follows this order, because `bringToFront()` re-inserts the node and a moved node loses focus:
    1. `bringToFront()`;
    2. add `tmz-active`;
    3. `focus({ preventScroll: true })`;
    4. pan by the minimum amount, in pixels: compare `map.project()` of the shape's bounds with `getPixelBounds()` shrunk by the padding. Use `panTo(center)` when the shape is larger than the view, otherwise `panBy([dx, dy])` when either offset is non-zero.

    Leaflet's `panInside` takes a point, not bounds.
  - **Announcements.** The overlay is `aria-hidden`, because the focused shape's name carries its text. One polite live region announces basemap switches and "Basemap unavailable". The SVG `<title>` elements are removed.

### D7. Tooltip columns in the widget config (api, ui)

```json
"tooltip": [
  { "column": "address",       "label": "Dirección",          "format": null },
  { "column": "sample_target", "label": "Meta muestral",      "format": "count" },
  { "column": "achieved",      "label": "Encuestas logradas", "format": null }
],
"basemap": "satellite"
```

- **Models.**
  - `WidgetTooltipItem(string Column, string? Label, string? Format)`.
  - `WidgetConfigInput` and `WidgetConfig` gain `Tooltip` and `Basemap` as their last optional parameters, after `Sort`.
  - `Normalize()` turns a null list into `[]`, keeps null items as null, trims labels, and turns a blank label or format into null.
- **Constants**, next to `MaxBuckets`: `MaxTooltipItems = 5`, `MaxTooltipLabel = 80`, `MaxTooltipText = 200`.
- **String constants, not enums.** `format` is `count`, `decimal`, `percent` or null, and `basemap` is `streets`, `satellite` or null. They are not enums, because the endpoints' JSON options have no enum converter.
- **Validation.** Field paths follow `config.filters[i]`.
  - `config.tooltip`:
    - "A map has at most 5 tooltip columns."
    - "Only map widgets can have tooltip columns.", checked when `chartType` is known.
  - `config.tooltip[i]` null: "A tooltip column needs a column."
  - `config.tooltip[i].column`:
    - empty, over 500 characters, a duplicate, or the geometry column;
    - "The table has no column 'x'." against the catalog, on create and update while published.
  - `config.tooltip[i].label`: at most 80 characters.
  - `config.tooltip[i].format`:
    - one of the three words;
    - "Only a number column can have a format." against the catalog.
  - `config.basemap`:
    - "The basemap must be 'streets' or 'satellite'."
    - "Only map widgets can have a basemap."
    - Never checked against what the deployment offers.
- **PATCH of `chartType` only.** It re-checks the stored config (`IndicatorEndpoints.cs:182-186`), so a map with tooltip columns PATCHed to `bar` is rejected, as with sort. The UI never does this, because `savedConfig` clears both fields for other types.
- **UI types.**
  - `WidgetTooltipItem` and the required `tooltip` and `basemap` fields in `WidgetConfig`, set in `emptyConfig()`.
  - `QueryRow` gains `rows?` and `tooltip?: DataValue[]`. `QueryResult` gains `tooltipTypes?`.

### D8. Tooltip values in the map query (api)

- **What makes a query a map.** `config.GeometryColumn is not null`. `queryFor` already nulls it for other types, and only a map selects geometry. Only then does the grouped branch add `count(*)` and the tooltip outputs and fill `Rows`, `Tooltip` and `TooltipTypes`.
- **Missing columns.** `foreach (var t in config.Tooltip) Need(t.Column)` goes after the sort column, so missing tooltip columns are listed last.
- **Rows used.** Values are taken over the rows the grouped `WHERE` keeps: a non-null measure, or a non-null numerator or denominator for a ratio, plus the widget's filters. "Combines N rows" counts the same rows.

  | Column type | Expression | Read as |
  |---|---|---|
  | integer | `sum(c)::text` | `DataValues.Number` |
  | decimal | `sum(c)::text` | `DataValues.Number`; `NaN` and `±Infinity` → null |
  | text | `left(min(NULLIF(c::text, '')), 201)` | a new `DataValues.Display(fetched, max)` overload with `max` 200 (the existing one calls it with 500), with "…" added when cut; all-empty → null |
  | date | `min(c)::text` | ISO date text |
  | timestamp | `min(c)::text` | `DataValues.IsoTimestamp`, project time zone |
  | boolean | `bool_and(c)` | bool (false is the smallest) |

  - `NULLIF` follows the text sort-key precedent.
  - `min()` on text uses the column's collation (`en_US.utf8`).
- **Response.**
  - `IndicatorRow(…, long? Rows = null, IReadOnlyList<object?>? Tooltip = null)`.
  - `IndicatorQueryResponse(…, IReadOnlyList<DataColumnType>? TooltipTypes = null)`, so the UI formats without the catalog.
  - The new fields are `JsonIgnore(WhenWritingNull)`, so other widgets' payloads do not change.
  - `ReadRowsAsync` reads through a per-ordinal reader list carried by the plan.
- **Payload.** The worst case is about 5 MB: 5,000 groups × 5 items × 200 characters. That is why the caps exist.
- **Alternative:** an `aggregate` per item (min, max, avg, ratio). It is left for later; the item object has room for it.

### D9. Tooltip formatting (ui)

- **The plotted value** uses the indicator's formatter (`format.ts:40-93`), unit included.
- **Tooltip columns** never use the indicator's format.
  - A chosen format (`count`, `decimal` or `percent`) applies only when `tooltipTypes[i]` is integer or decimal. A format saved on a column that has since become text, a date or a boolean is ignored.
  - Without a format, the type decides:
    - integer → grouped digits;
    - decimal → up to 2 decimals;
    - boolean → Yes or No;
    - date → `mediumDate`;
    - timestamp → `dateTimeFormat`, formatted with the value's own offset as the time zone, so it reads as the project's wall time to the minute whatever the browser's zone (`2026-09-01T21:30:05-05:00` → `Sep 1, 2026, 9:30 PM`);
    - null → "—".
  - `currency` is not offered, because it needs a unit.
- **Shared helper.** A shared `timestampText()` goes into `shared/ui/date-format.ts`. The clean-data cell rule (`project-clean-data.ts:642-650`) is left as it is.
- **"Show as table"** uses the same strings. `TableModel` gains `align?: ('start' | 'end')[]`, so text columns align to the start.

### D10. A missing tooltip column makes the widget stale (api, ui)

- A tooltip column the table no longer has is reported in `missing.columns`, like every other named column. The widget shows "This widget cannot be drawn" and names it.
- **Alternative:** draw the map without that column. Rejected for consistency: the sort change rejected silent fallbacks, and a new response field would be needed.
- **Edit path.** The edit form keeps a missing saved column selected and marked, through `columnsWith`.

### D11. Saved config, preview, and the member's switch (ui, api)

- **`config.basemap` is a kind** (`streets`, `satellite` or null), not a provider. A widget keeps its meaning when the operator changes provider.
- **Three views of one form.**
  - The dialog keeps a raw `formConfig` computed from every field, including the basemap and the full tooltip items.
  - `queryFor(type, config)` sends `basemap: null`, and for maps `tooltip` with **column names only** (`label: null, format: null`), or `[]` otherwise. The query key therefore ignores the basemap, labels and formats.
  - `savedConfig(type, config)` returns `queryFor` plus, for maps, the full tooltip items and the basemap. For other types it returns `tooltip: []` and `basemap: null`.
  - An item whose column the catalog knows as a type other than integer or decimal is saved with `format: null`, following the `compareAs` precedent (`widget-dialog.ts:354-360`). A format left over from before a script changed the column's type then never blocks saving.
  - The save body and the preview's `[config]` are both `savedConfig(type, formConfig)`. `WidgetView` derives its key through `queryFor` itself, so editing a label or format, or picking a basemap, never re-queries or blanks the preview, while the preview still shows them.
- **Member's switch.** `viewerBasemap: 'none' | 'streets' | 'satellite' | undefined`, where undefined means no switch was made.
  - The effective basemap is `viewerBasemap === undefined ? config.basemap : viewerBasemap === 'none' ? null : viewerBasemap`. It is then filtered by the offered kinds, by degrees data and by the box size (D17).
  - A kind that is not offered draws nothing and raises no error. The dialog shows it as "(no longer offered)" and keeps it when saved unchanged.
  - The switch is announced in the live region, and never saved or stored.
- **New widgets** start with null. A basemap is the analyst's explicit choice.

### D12. Operator settings (api)

| Variable | Compose default | Meaning |
|---|---|---|
| `TAMIZA_MAP_BASEMAP_PROVIDER` | empty (`none`) | `none`, `osm` or `google` |
| `TAMIZA_MAP_GOOGLE_API_KEY` | empty | Required with `google`. A Map Tiles API key restricted to the `api`'s egress address. |
| `TAMIZA_MAP_GOOGLE_LANGUAGE` | empty (`en-US`) | IETF tag for Google's labels, e.g. `es-419` |
| `TAMIZA_MAP_GOOGLE_REGION` | empty (`US`) | CLDR region, e.g. `CO`, uppercased |
| `TAMIZA_MAP_TILES_PER_MINUTE` | `600` | Google tiles per user per minute, 60–10,000 |
| `TAMIZA_MAP_TILES_PER_USER_PER_DAY` | `3000` | Google tiles per user per UTC day, 100–1,000,000 |

- **Code.** `Maps/MapOptions.cs` (`[ConfigurationKeyName]`), `MapOptionsValidator.cs` (`ValidateOnStart`; each message names the variable) and `MapRegistration.cs`, following the assistant.
- **Compose.** The string variables are passed as `${VAR:-}` and the two integers with their defaults (`${TAMIZA_MAP_TILES_PER_MINUTE:-600}`), as the assistant does. Binding an empty string to an `int` would stop the `api`.
- **Validation.**
  - Text is trimmed, and blank means unset.
  - The provider must be one of the three words.
  - The key, language and region are checked only when the provider is `google`. Otherwise, setting them gives one warning: "ignored".
- **Startup log.** One info line names the provider and the hosts that will be contacted: viewers' browsers to `tile.openstreetmap.org`, or the `api` to `tile.googleapis.com`.
- **Not variables.** The tile-link window (45 minutes) and the credits budget (120 per user per minute) are `MapOptions` properties not bound to any variable. Tests set them through the host.
- **One provider per deployment** makes Google's "not with or near a non-Google map" term hold by construction. This is a reading of the terms, not legal advice; `operations.md` says so.
- **Alternatives.**
  - A list of ids plus custom JSON entries: rejected now, because satellite is Google-only and custom hosts would reopen the CSP question (D14).
  - An operator default basemap: rejected, because a basemap is the analyst's explicit choice. It is not a privacy control: once a provider is set, any member can switch a basemap on for any map.

### D13. Basemap list (api, ui)

`GET /api/v1/basemaps`, with operation id `ListBasemaps` and tag `Maps`, requires a signed-in user.

```json
{ "provider": "google",
  "basemaps": [
    { "kind": "streets", "minZoom": 0, "maxZoom": 22,
      "tiles": "/api/v1/basemap-tiles/<link>/{z}/{x}/{y}",
      "linksExpireAt": "2026-10-17T15:15:00Z", "linksRefreshInSeconds": 1980,
      "attribution": { "source": "google", "text": null, "url": null } },
    { "kind": "satellite", "…": "…" } ] }
```

- **Who gets entries.** A user who is a member of at least one project, or `superadmin`. Anyone else gets `{ provider, basemaps: [] }` and no link is issued. Dashboards are reachable only to members anyway, and this stops a realm user without a project from minting Google links.
- **With `osm`.** `tiles` is the OSM template, `maxZoom` is 19 and the link fields are null. The attribution is `{ source: "osm", text: "© OpenStreetMap contributors", url: "https://www.openstreetmap.org/copyright" }`.
- **With no provider:** `{ provider: "none", basemaps: [] }`.
- **Unchanged and translated.** `/system/config` is unchanged. Labels come from the UI's translations.
- **UI.** `basemaps.service.ts` loads the list once per page (`shareReplay`) and answers `[]` on error. It is imported only from the lazy chunk.
- **The dialog's select** appears when the list is not empty or the widget already has a basemap.

### D14. OpenStreetMap tiles and the CSP (ui)

- **Layer.** A Leaflet `TileLayer` with the fixed preset URL, `referrerPolicy: 'strict-origin'` and `maxNativeZoom: 19`.
  - For a third-party host, `strict-origin` sends the origin only, as `strict-origin-when-cross-origin` would. That meets OSM's Referer rule while the site-wide `no-referrer` stays.
  - `crossOrigin` is not set: `<img>` tiles need no CORS.
- **CSP.** `nginx-security-headers.conf:3` gets `img-src 'self' data: https://tile.openstreetmap.org`.
  - Google tiles are same-origin through the `api`, so this single fixed origin is all that is added. No templating is needed, and setup-foundation D4 holds.
  - Every deployment allows that origin, even with no provider: a deliberate, narrow cost.
- **Alternatives.**
  - `img-src https:`: needed only for operator-defined hosts, a non-goal. With `style-src 'unsafe-inline'`, it would allow scriptless exfiltration.
  - A templated CSP: conflicts with setup-foundation D4.
  - Proxying OSM: OSM forbids tunnelling all clients behind one identity.

### D15. Google tiles relayed by the `api` (api, ui nginx)

**Sessions** (`Maps/GoogleTileSessions.cs`, a singleton):
- One session per kind: streets → `roadmap`, satellite → `satellite`.
- Created with `POST https://tile.googleapis.com/v1/createSession?key=…` and `{ mapType, language, region }`.
- Kept in memory. Renewed under an hour before `expiry`, or once after Google rejects the session. A semaphore per kind prevents a stampede.
- Created lazily, on the first tile or credits request.

**Tile links**, stable within a window:
- **Window.** Time is cut into clock-aligned windows of 45 minutes. For each (user, kind, window) the `api` issues one link.
- **Format.** A plain `IDataProtector` (purpose `Tamiza.Maps.TileLink.v1`) protects `userId|kind|windowStart`, and the result is base64url.
  - The `api` checks expiry itself with the injected `TimeProvider`, as `ExportRegistry` does (`ExportRegistry.cs:39, :55`): a link is valid until its window's end plus 15 minutes, so no link lives more than an hour.
  - `ITimeLimitedDataProtector` is not used, because it checks the real clock, which tests cannot move.
- **Memoized** in `IMemoryCache` under (userId, kind, windowStart). Every list in a window then returns the same string, and the browser's HTTP cache, keyed by URL, can reuse tiles under Google's `Cache-Control`. Data Protection output is randomized, so it would otherwise change on every call.
- **Response fields.** `linksExpireAt` is the link's expiry, and `linksRefreshInSeconds` is the time until the window ends, relative so client clocks do not matter.
- **Alternative:** a bearer-authenticated fetch per tile with blob URLs. It adds `img-src blob:` and replaces Leaflet's tile loading.

**Tile endpoint:** `GET /api/v1/basemap-tiles/{link}/{z}/{x}/{y}`, `AllowAnonymous()`, operation id `GetBasemapTile`. It is always mapped, and checked in this order before any outbound call:
0. The provider is `google`. Otherwise 404, with no session created and no outbound call.
1. `z` is an integer from 0 to 22, and `x` and `y` are from 0 to 2^z − 1. Otherwise 404.
2. The link unprotects, its kind is offered, and `TimeProvider.GetUtcNow()` is before its window's end plus 15 minutes. Otherwise 404.
3. The per-user minute window and the per-user UTC-day counter both grant a permit. Otherwise 429 `basemaps.rateLimited` with `Retry-After`.
   - The minute window uses `PartitionedRateLimiter<Guid>`; the day counter lives in memory.
   - One `api` instance is assumed, as everywhere in the MVP. A restart resets the counters.

Then:
- **Fetch.** `GET https://tile.googleapis.com/v1/2dtiles/{z}/{x}/{y}?session=…&key=…`, forwarding `If-None-Match`.
- **Pass-through.**
  - On 200 or 304, the body streams with `Content-Type`, `Cache-Control` and `ETag`, plus `Cross-Origin-Resource-Policy: same-origin`, so other sites cannot embed live links.
  - Nothing is written to disk or a cache.
- **Failures.**
  - A Google 4xx naming the session renews it and retries once.
  - A missing tile becomes 404. Task 6.6 confirms Google's status for it.
  - Quota or billing refusals become 503 `basemaps.unavailable`.
  - Anything else becomes 502 `basemaps.upstream`.
  - Google's body is never passed on.

**nginx.** `location ^~ /api/v1/basemap-tiles/` has the `/api/` proxy settings plus `proxy_buffering off;` and `access_log off;`, and the security-headers include.
- Nothing is spooled to disk, following the export location's `proxy_buffering off`, and live links stay out of the `ui` access log.
- nginx's error log still holds the request line when the upstream fails, for example while `api` restarts. The error log is kept for diagnosis, and `operations.md` says so.
- The `^~` prefix also keeps the static-asset regex (`nginx.conf:85`) away from tile paths.
- `operations.md` notes that an outer proxy logs tile URLs, which hold links valid for up to an hour and tile coordinates.

**HttpClient** `maps-google`, following `AssistantHttp`:
- no redirects, no proxy, a 10 s timeout, 2 MB at most;
- `RedactLoggedHeaders(_ => true)`;
- **`RemoveAllLoggers()`**, because the default `IHttpClientFactory` logging writes the request URI, which holds the key;
- failures logged at most once a minute per kind, without the URL.

**Credits and depth:** `GET /api/v1/basemaps/{kind}/credits?zoom&north&south&east&west`, for signed-in users, operation id `GetBasemapCredits`.
- Answers 404 unless the provider is `google` and the kind is offered.
- Validates the ranges.
- Calls `https://tile.googleapis.com/tile/v1/viewport` with the session and key.
- Returns `{ text, maxZoom }`: the `copyright` text, and the largest `maxZoom` of the `maxZoomRects` that contain the centre of the box (22 when none does). Google's rectangles add up: every answer starts with world-wide ones at 19 and 9, so a minimum would be 9 everywhere on Earth. Over land the largest value is 19 or 20, which is as deep as the map zoom goes.
- Has its own per-user budget (120 a minute), separate from tiles, so fast panning never costs the credits. Viewport calls are not billed.

**UI for Google layers:**
- **Order.** A Google layer is added only after its first credits answer. Its `maxNativeZoom` is `min(22, maxZoom)`, so tiles above the local imagery are never requested and Leaflet enlarges the deepest ones.
- **Updates.** On `moveend`, debounced 500 ms and cached per rounded viewport, credits are fetched again. A new `maxZoom` updates the layer and calls `redraw()`.
- **Failures.** A failed later call keeps the last credits. If the first call fails, the layer is not shown and the map says "Basemap unavailable".

**Logo.** An outlined Google Maps logo from Google's attribution assets (`GoogleMaps_Logo_WithLightOutline` or `_WithDarkOutline`, chosen for contrast), unmodified:
- 16–19 px high, with clear space of 10 px on the left, right and top and 5 px below;
- `alt="Google Maps"`;
- bottom-start, never overlapping the credits, legend or controls;
- never replaced by text: below the D17 minimum box no Google imagery is drawn at all, and that minimum leaves room for the logo with its clear space and one line of credits;
- served from a versioned path (`public/vendor/google/<yyyy-mm>/`), because nginx caches images as immutable.

Task 7.4 records the asset's source.

**Operator duties**, documented in `operations.md`:
- accept Google's terms and set up billing;
- restrict the key to the Map Tiles API and to the `api`'s egress address;
- set the daily quota in the Google Cloud console as the cost cap, with billing alerts;
- reference Google's terms and privacy policy in the deployment's end-user terms (3.2.2(a)).

### D16. Fresh links in the UI (ui)

- **Refresh timer.** `basemaps.service.ts` schedules a refresh `linksRefreshInSeconds` after the response arrived, at least 60 s, while a Google layer is mounted. It also re-checks on `visibilitychange` and on the first tile error after the links are due.
- **Applying new links.** When the refreshed list has the same provider, `MapWidget` calls `layer.setUrl(template, true)`. Shown tiles stay, and new ones use the fresh link.
- **Provider changed.** When the refreshed list names a different provider, the effective-basemap rule runs again (D11). The tile layer and attribution are rebuilt from the new entry, or removed.
- **Margin.** A link stays valid 15 minutes past its window, which covers a late timer after sleep.
- **Before giving up** (D17): a Google layer whose links are due re-fetches the list once and retries. A basemap switch re-fetches the list when the links are due.

### D17. Tile economy, small boxes and failure (ui)

- **Lazy creation.** The Leaflet map is created on the widget's first intersection with the viewport (IntersectionObserver, 200 px margin). Without it, as in jsdom, it is created at once.
- **Small boxes.** A tile layer is drawn only when the box is at least 200×120. Below that there is no tile layer and no tile or credits request, and the map shows "Enlarge the map to show the basemap", because the attribution would not fit. Attribution wraps and is never cut.
- **No prefetching.** Leaflet does not prefetch, and `maxBounds` keeps tiles near the data.
- **Failure.** The layer counts `tileload` and `tileerror`. It gives up when the first `load` has zero successful tiles, or when more than half of a settled batch failed, after D16's one retry for a due Google link. It then removes the layer for that instance and announces "Basemap unavailable" once.
  - An `<img>` error carries no status, which is why D15 avoids requests that can fail for ordinary reasons: imagery depth comes from `maxNativeZoom`, and credits have their own budget.
- **What keeps working.** Shapes, zoom, tooltips and the table never wait for tiles.
- **Retry.** Switching the basemap again tries again.

### D18. Leaflet's CSS and licence (ui)

- **CSS.**
  - `leaflet.css` (about 14.8 KB) is over the 8 kB component-style budget, and as a global style it would eat the initial headroom.
  - An `angular.json` asset glob copies it to `vendor/leaflet/1.9.4/leaflet.css`. `map-engine.ts` adds a `<link>` once and waits for `onload`. The versioned path makes nginx's year-long cache safe.
  - The app's overrides (D3) sit unlayered in `styles.css`, each with a selector more specific than the Leaflet rule it overrides, so the later `<link>` cannot win a tie. A spec pins each selector.
- **Licence.** Leaflet is BSD-2-Clause. The `Dockerfile` copies the build's `3rdpartylicenses.txt` next to `index.html`. `THIRD-PARTY-NOTICES.md` keeps covering adapted source only.

### D19. API codes, strings and the assistant's prompt (api, ui)

- **Problem codes.** `basemaps.unavailable` (503), `basemaps.upstream` (502) and `basemaps.rateLimited` (429) are added to `error-codes.spec.ts` and to `en.json` `errors.codes.basemaps`.
- **Strings.** Every new string goes into `en.json` under `indicators.map.*` and `indicators.dialog.*`: hints, controls, kinds, "(no longer offered)", notes, "Basemap unavailable", key help, "Combines N rows", Yes/No, and the editor.
- **The assistant's prompt** (`Assistant/Prompts/system.md:41`) is corrected. Geometry is published as WKT text and coordinates are not converted, so project to degrees first (`to_crs(4326)`). The map bullet adds "one row per mapped shape; tooltip numbers are summed when shapes share a group". `PromptKitTests` must keep passing.

## Risks / Trade-offs

- **[Reverses archived goals: a runtime dependency and a CSP change]** → One adapter file, a pinned 1.9.4, and one fixed tile origin instead of `https:`.
- **[Imperative Leaflet DOM inside template-bound charts; styles must be global]** → Signals update on settle only. `data-*` hooks serve tests. The global block is small and scoped by `.tmz-map`, and a spec pins its rules.
- **[The touch toggle relies on Leaflet's `dragging` handler semantics]** → Covered by jsdom tests that dispatch touch events, and by real devices (task 8.3).
- **[SVG performance at 5,000 detailed polygons]** → Leaflet's `smoothFactor` and a single renderer. Pass criterion in task 8.3; if it fails, raise `smoothFactor` for large sets. Canvas was rejected because it loses CSS colours, focus and jsdom tests.
- **[Projection change]** → Country-scale maps get slightly taller in the north. Non-degree numbers within ±180/±90 would be misplaced, because the SRID is lost upstream. The docs and the assistant's prompt say to project to EPSG:4326.
- **[Summing every number]** → Averages or rates added as tooltip columns are summed over several rows. The "Combines N rows" footer and the docs, which say to plot rates as a ratio, mitigate this. A per-item aggregate is the follow-up.
- **[One missing tooltip column blanks the whole map]** → It is consistent with every other column. The message names the column.
- **[Google quota and cost]**:
  - The per-minute limit bounds bursts.
  - The per-user daily cap keeps one user, or a script with their token, from using up the project's default 15,000 tiles a day.
  - Links go to project members only.
  - The console quota is the operator's cost cap.
  - Stable links let the browser cache work.
  - Quota exhaustion degrades to "Basemap unavailable".
- **[The `api` carries tile traffic]** → Tens of tiles per view, nothing stored, and nginx streams without buffering.
- **[Links in outer proxy logs]** → Valid for at most an hour, for one user and kind, and bounded by that user's limits. Documented.
- **[Reading of Google's terms: relay, "near a non-Google map"]**:
  - no storage;
  - cache headers passed through;
  - CORP same-origin;
  - one provider per deployment;
  - the outlined logo and credits always visible;
  - imagery hidden when credits are missing.

  `operations.md` says the operator accepts Google's terms with their own key, and that this is not legal advice.
- **[OSM may block heavy or commercial use]** → The provider is a setting and off by default. Google is the documented alternative.
- **[Privacy]** → With OSM, viewers' browsers reveal their IP address and the blocks they look at. With Google only the `api` contacts the provider. With a provider set, any member can turn a basemap on for any map. The data-protection paragraph says so, and no indicator value is ever sent.
- **[Rollback: an older API drops `tooltip` and `basemap` on any PATCH, a move included]** → Release notes.
- **[Not verifiable in jsdom: real gestures, tile drawing, z-order, CSP, Referer, colours]** → Group 8 checks them on the built `ui` container in a separate Compose project and on real devices. The repo has no CI.

## Migration Plan

1. **Deploy as one release.** The `api` accepts and returns the new fields, and the `ui` sends them only for maps. No DbUp script is needed: a stored config without `tooltip` or `basemap` reads as `[]` and null.
2. **With no basemap variables,** maps gain zoom, pan and the new tooltip, and make no outside request.
3. **To offer basemaps,** set `TAMIZA_MAP_BASEMAP_PROVIDER`, plus the Google variables for `google`, and run `docker compose up -d api`. The new `ui` image carries the CSP and the tile location.
4. **Rollback.** Restore the previous images. Widgets keep working, but any widget edited or moved under the old `api` loses its tooltip columns and basemap.

## Verification Results

### Google documentation check (6.6)

Read on 2026-10-17 from the Map Tiles API pages (2D tiles overview, session tokens, handling errors, policies).

- **Session.** `POST https://tile.googleapis.com/v1/createSession?key=...` takes `mapType` (`roadmap`, `satellite`, ...), `language` (IETF tag) and `region` (two-letter CLDR region). **All three are required**, which is why D12 defaults the language to `en-US` and the region to `US`. The answer holds `session` and `expiry`, a string of seconds since the epoch; a session lasts two weeks "but this policy might change without notice", so the `api` reads `expiry` and never assumes the span.
- **Tile.** `https://tile.googleapis.com/v1/2dtiles/{z}/{x}/{y}?session=...&key=...`, as D15 says. Zoom runs from 0 to 22.
- **Viewport.** `https://tile.googleapis.com/tile/v1/viewport?session=...&key=...&zoom=...&north=...&south=...&east=...&west=...`, as D15 and tasks 6.5 say. All parameters are required. North and south are in (-90, 90) and east and west in (-180, 180). A box that crosses the antimeridian has a positive `west` and a negative `east`, so the `api` accepts `west > east`.
- **Viewport answer.** `copyright` is the attribution string to display with roadmap and satellite tiles. `maxZoomRects` is an array of rectangles that overlap the viewport, each with `maxZoom`, `north`, `south`, `east` and `west`. Google offers the call "to avoid requesting imagery at zoom levels that don't exist". D15 originally took the smallest `maxZoom` of the rectangles that intersect the box. Stack verification showed that this is 9 everywhere; D15 now takes the largest of the rectangles that contain the centre.
- **Missing tile: not documented.** The pages give error reasons but no HTTP status for a tile that does not exist. `notFound` and `invalid` mean "the x, y, or z value is out of range"; `expired` means the session lapsed; `forbidden` is 403 (key missing, not allowed, or billing/permission refusal); `quotaExceeded` and `rateLimitExceeded` are quota refusals (429 in Google's usual convention); an expired or mismatched session is a 400 on the 3D side of the API. So the relay decides by the `error.errors[].reason` and `error.status` in the body, and by the status only as a fallback:
  - HTTP 404, or reason `notFound`, or reason `invalid` with no mention of the session or the key: missing tile, answered 404;
  - reason `expired`, or a 400/401/403 naming the session: renew the session and retry once;
  - HTTP 429, reason `quotaExceeded`/`rateLimitExceeded`, or HTTP 403: `basemaps.unavailable` (503);
  - anything else: `basemaps.upstream` (502).

  The fakes of tasks 6.4 and 6.5 use these documented reasons, with HTTP 404 for a missing tile and 400 for an expired session. Task 8.2 should confirm the real status with the operator's key.
- **Policies.** Content must not be pre-fetched, stored or cached beyond the HTTP cache headers (`max-age`, `stale-while-revalidate`, `must-revalidate`, `private`, and `ETag` for revalidation); the logo is shown at 16-19 dp with 10 dp of clear space left, right and top and 5 dp below, outlined on busy backgrounds, with the accessibility label "Google Maps"; the full `copyright` text is shown, normally bottom-right. All of this matches D15.

### Google logo asset (7.4)

- **Source.** Google's official "Google Maps attribution assets", the zip linked from the Map Tiles API policies page ("Display Google Maps attribution" > "Download the Google Maps attribution assets"): `https://developers.google.com/static/maps/documentation/images/Google_Maps_Attribution_Assets.zip` (91,462 bytes, SHA-256 `899e2ada2969330debb94adf6ec44290ee528fcec55ac18683485259e1bf82e8`), downloaded 2026-10-17. The files inside are dated 2024-09 to 2025-07.
- **Files used, unmodified and under their original names**, in `tamiza-ui/public/vendor/google/2026-10/` (served at `vendor/google/2026-10/`; the month in the path is the download month, so a newer logo is a new path that nginx's immutable cache cannot hide):
  - `GoogleMaps_Logo_WithLightOutline.svg` (SHA-256 `16751302519e08d2cdfe3b693c78e1a288a7dc85cc17218bdc971bbdcf427cbe`), 105 x 22 px, for Streets;
  - `GoogleMaps_Logo_WithDarkOutline.svg` (SHA-256 `1974830500ca480c8c81daf95b049441927f18061fff040be607467c6b42bc39`), 105 x 22 px, for Satellite.

  The SVG variant of each was taken for sharpness at any pixel density; the zip also holds 1x, 2x and 4x PNGs and the non-outlined (plain background) logos, which are not used.
- **Permitted use**, from the same policies page, read 2026-10-17: use the official logos; do not modify them; keep the aspect ratio; use the **outlined** logo on a busy background such as a map or imagery; height from 16 dp to 19 dp (the UI draws 18 px); clear space of at least 10 dp on the left, right and top and 5 dp below; keep an accessible contrast with the background; give it the accessibility label "Google Maps"; do not overlap or obscure it or the data attribution with any other logo. The UI picks the outline by basemap for contrast: the light outline over the lighter Streets map, the dark outline over Satellite imagery.
- **Terms.** The zip carries no licence file: the logos are Google's, used for attribution under the policies above and the Google Maps Platform terms the operator accepts with their own key, and are not covered by Tamiza's licence. `THIRD-PARTY-NOTICES.md` covers adapted source only and is left as it is.

### Stack verification (8.2)

Run on 2026-10-17 in the separate Compose project `tamizaverify` (images `tamiza-api:verify` and `tamiza-ui:verify` built from the working tree, port 8189), with a fake OIDC provider and a fake Kobo on the host, headless Chrome 155 driven over the DevTools protocol, and the operator's Google key (never written into the repo). The block indicator `survey_progress` was published by a real script run: six polygons in degrees around Cali (3.45, -76.53), block `B02` on two rows, columns `block_code`, `address`, `sample_target`, `achieved`, `value` (percent) and `geometry`. The map widget has the three tooltip columns "Dirección", "Meta muestral" and "Encuestas logradas".

**Result: task 8.2 is NOT complete.** One real bug (below) makes Google basemaps unusable. Everything else passed.

**none (provider unset)**
- `curl -sI` on the built `ui` (`/`, `/api/v1/system/config`, `/vendor/leaflet/1.9.4/leaflet.css`, `/api/v1/basemap-tiles/...`) shows `img-src 'self' data: https://tile.openstreetmap.org` and `Referrer-Policy: no-referrer`. The `api` logged "Map basemaps are off" and, with the key set, one warning "TAMIZA_MAP_GOOGLE_API_KEY is set but ignored".
- `GET /api/v1/basemaps` answered `{"provider":"none","basemaps":[]}`; the tile and credits routes answered 404.
- Zero third-party requests, and zero requests of any kind while zooming (ctrl+wheel in and out, the buttons, a plain wheel that shows the hint and leaves the zoom unchanged). No basemap button, no `map-tile`.
- Colours: each region's computed fill equals the legend swatch of its class (`tmz-seq-1` rgb(205,226,251), `tmz-seq-3` rgb(109,167,236), `tmz-seq-7` rgb(13,54,107)); the 20% to 100% legend maps B04 to step 1, B01/B02/B05 to step 3, B03/B06 to step 7. Fill opacity is 1.
- Background: the map container's computed background is `rgba(0, 0, 0, 0)` and the font is inherited (screenshot: plain card).
- Stacking: with the tooltip's `pointer-events` switched on for the test, a hit test at its centre returns the tooltip (overlay z-index 1000, above the Leaflet panes at 200 to 700); each control button hits itself.
- The tooltip for B02 reads `Survey progress: 45%`, `Meta muestral: 20`, `Encuestas logradas: 7` and "Combines 2 rows".

**osm**
- 12 tiles painted at once and 78 requests after zooming, all `https://tile.openstreetmap.org/{z}/{x}/{y}.png` with status 200, the only third-party host. Every request carried `Referer: http://localhost:8189/` (origin only) and the tile elements have `referrerPolicy="strict-origin"`.
- The export link request (`/api/v1/data-exports/<token>`) has no `Referer` header at all.
- The credit "© OpenStreetMap contributors" is visible and topmost, with `rel="noopener noreferrer"`. Shapes and legend swatches have opacity 0.7.

**google**
- Browser: no request to any Google host and no third-party request at all, for Satellite and Streets. Tiles arrive from `/api/v1/basemap-tiles/<link>/z/x/y` with `image/jpeg` (satellite) or `image/png` (streets), `Cache-Control: private, max-age=86400, must-revalidate, no-transform` and `Cross-Origin-Resource-Policy: same-origin`.
- The key is in no request URL or header, no response header, no page HTML, no storage, no `/api/v1/basemaps` or credits body, no static file of the `ui` image and no `docker logs` of `api`, `ui`, `analytics` or `db`. A repeat with a deliberately wrong key (502 `basemaps.upstream` from a failed session) logged "the session for satellite failed: Google answered 400" without any key.
- The `ui` access log holds 0 lines with `basemap-tiles` (hundreds of tile requests were made) and does log the list and credits calls.
- The Google Maps logo is 18 px high with a 10 px left margin, `alt="Google Maps"`, dark outline for Satellite and light outline for Streets.
- Credits change on pan: with a second map of three polygons 0.15 degrees apart at zoom 14.5, panning east without changing the zoom changed the text from "Imagery ©2026 Airbus, CNES / Airbus, Maxar Technologies" to "Imagery ©2026 CNES / Airbus, Maxar Technologies", and each pan made a new credits request. In the 0.01 degree block map a pan changes nothing, and Google answers the same text there.
- Reloading reuses cached tiles: 20 of 20 tile requests were served from the browser cache, and the tile link was identical after the reload.
- Missing tile through the relay: Google answers HTTP 404, reason `notFound`, for a tile it does not have (checked directly at z14 to z22 over the open ocean, polar and Siberian points); the relay answered 404 for the ocean tile z16/27306/32768, and 200 `image/jpeg` for the Cali control tile. This confirms D15's 404.
- Removing a tooltip column: the script was saved without `achieved` and run again. The widget shows "This widget cannot be drawn" and "The published table no longer has the columns: achieved."

**BUG (blocks the satellite and streets bullets): `GoogleTiles.GetCreditsAsync` takes the smallest `maxZoom`.**
- `tamiza-api/src/Tamiza.Api/Maps/GoogleTiles.cs:97`, `maxZoom = Math.Min(maxZoom, rectZoom)`, and the summary at `:70-71`, follow D15 ("the smallest maxZoom of the rects that intersect the box"). Real `maxZoomRects` always begin with two world-wide rectangles with `maxZoom` 19 and 9, then 13 and 14 for latitudes -57 to 83.7, then local ones, so the minimum is 9 everywhere on Earth.
- Observed: `GET /api/v1/basemaps/satellite/credits?zoom=16&north=3.456&south=3.444&east=-76.52&west=-76.537` answers `{"text":"Imagery ©2026 Airbus, Maxar Technologies","maxZoom":9}`. The layer therefore sets `maxNativeZoom` 9, requests a single z9 tile (147/251, Streets and Satellite alike) and Leaflet enlarges it 128 times: the screenshot shows a blurred brown smear under the blocks, at every view zoom from 13 to 20.
- Real depth, probed directly (deepest zoom that returns a tile, and the minimum and maximum `maxZoom` of the rectangles containing the point): Cali 22 (9, 20); Bogotá 22 (9, 20); Nairobi 22 (9, 20); Amazon 20 (9, 20); Sahara 21 (9, 19); Siberia 14 (9, 19); mid-Atlantic 11 (9, 19); Pacific 13 (9, 19). The maximum over the intersecting rectangles is right where land imagery exists (19 or 20) and too deep over ocean and polar areas, where the tiles are missing (404) and the tile-health rule of D17 takes over. Ignoring the world-wide rectangles and taking the maximum of the rest fits better still (Siberia 14, Pacific 14).
- The fakes of tasks 6.5 used non-overlapping rectangles, so the unit and integration tests could not see it. To see the mechanism work with the right depth, the browser was made to rewrite the credits answer's `maxZoom` (the `api` untouched), with real Google tiles: with 17, view zoom 16 requested z16, and zooming to 20 requested only z16 and z17, 200 for all, the layer kept (`data-basemap="satellite"`, no notice); with 20, z16, z18, z19 and z20 were requested and the imagery was sharp. So the "zooming past the imagery keeps the layer" mechanism is sound, but with the real answer it can't be shown at a true depth.
- Needs a design decision and a fix (D15 text, `GetCreditsAsync`, and a test with real overlapping rectangles).

**Observation (no change made).** Google's `copyright` is an empty string for views at zoom 12 and below (and the `api` passes it on as `""`). A map that fits at zoom 12 or lower (the 0.3 degree-wide test map fits at 11.5) shows the Google logo and 4 tiles but no credits text, which sits oddly with "Google imagery is not shown without its credits".

### Depth rule fixed after stack verification (8.2)
- `GetCreditsAsync` now returns the largest `maxZoom` of the `maxZoomRects` that contain the centre of the view, as D15 says. The unit and integration tests use answers shaped like Google's (world-wide 19 and 9 first), so the old rule cannot pass them again. A box that crosses the antimeridian has its centre on the right side, and a rectangle that crosses it is read correctly.
- Checked against Google's real answers with the operator's key: the centre of Cali gives 20 (58 rectangles contain it; the minimum would be 9). Over ocean and polar areas the largest value is 19, deeper than Google's imagery there. Tiles that do not exist answer 404, which D17 handles.
- Not re-run end to end after the fix: the browser check of sharp imagery at zoom 16 to 20 over the real stack. The mechanism (the layer's `maxNativeZoom` taken from the credits answer, enlargement above it, the layer kept) was verified with a rewritten depth before the fix, and the new value for Cali is 20.
- Google's `copyright` is an empty string for views at zoom 12 and below. The UI treats it as credits without text: the Google logo is shown and the imagery stays.

### Full suites (8.1)
- API integration: 971 passed, 0 failed, in one uninterrupted run of 12 min 30 s (on a clean disk, after an earlier run was cut short when Docker Desktop crashed on a full C: drive).
- API unit: 1,016 tests. One test, `ExportRegistryTests.The_sweeper_clears_leftover_files_when_the_api_starts`, fails in some full runs (about 1 in 3). It fails the same way on a clean `HEAD` checkout (3 of 8 runs), passes alone and within `CleanData`, and is not touched by this change. After the depth fix, the 115 `Maps` unit tests and the 88 `Maps` integration tests pass.
- UI: 66 files, 1,403 tests, 1,401 passed. The 2 failures are the known polling tests (`project-data.spec.ts`, `project-scripts.spec.ts`).
- `npm run build`: passes; initial total 635.70 kB (warning budget 650 kB); Leaflet is a lazy chunk of 149.42 kB raw, 37.47 kB gzipped. `npm run check:safety` passes.

