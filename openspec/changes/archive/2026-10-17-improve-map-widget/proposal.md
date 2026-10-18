# Proposal

## Why

The map widget draws an indicator's shapes on a blank background, with no zoom or pan, and its tooltip shows only the group value and the plotted number. That works for a choropleth of municipalities. It fails for the field work Tamiza is used for: following survey progress block by block (manzanas). To read that map, a coordinator needs to see the streets or the imagery under the blocks, zoom into one neighbourhood, and point at a block to read its address, its sample target and how far it has got.

The indicators dashboard design (archived `2026-10-14-add-indicators-dashboard`, D6) deliberately left out a basemap, pan and zoom, and a map library, and named this as the upgrade path. This change takes that path. It supersedes three things in that design:
- D6's "no basemap";
- the goal "no new runtime dependency and no new CSP directive";
- the non-goal "a pannable, zoomable basemap".

D5 still holds for the other charts.

## What Changes

- **Zoom and pan on every map.** A map can be zoomed and moved:
  - with zoom-in, zoom-out and "Show all" buttons;
  - by mouse drag and double-click;
  - with Ctrl or ⌘ and the wheel;
  - with two fingers on a touch screen;
  - from the keyboard.

  A plain wheel or a one-finger swipe still scrolls the page and shows a hint. The view is not saved: each viewer's zoom stays on their screen, and the map opens fitted to its shapes. Zoom and pan need no internet connection and work without a basemap.
- **Maps in their real position.** Coordinates in degrees are drawn on a Web Mercator map so a basemap lines up with them. Other coordinates keep today's relative drawing, with no basemap.
- **A tooltip with chosen columns.** Pointing at a shape, tapping it, or reaching it from the keyboard shows a tooltip:
  - the group value as its heading;
  - the plotted value, with the indicator's format;
  - up to 5 **tooltip columns** of the indicator's table, chosen in the widget form, each with an optional label and format. For example "Dirección", "Meta muestral" and "Encuestas logradas" for a city block.
  - Rows that share a group are combined: numbers are summed, other columns show one value (the smallest), and the tooltip says how many rows it combines.
  - "Show as table" lists the tooltip columns.
  - A tooltip column the table no longer has makes the widget stale, as for the other columns.
- **A basemap per widget, chosen from what the deployment offers.**
  - The analyst picks None, Streets or Satellite in the widget form. Any member can switch it on their own screen without saving.
  - The operator chooses one provider with `TAMIZA_MAP_BASEMAP_PROVIDER`:
    - `none`, the default: no basemap and no request to anyone, as today;
    - `osm`: OpenStreetMap streets, loaded by the browser;
    - `google`: Google streets and satellite.
  - **Google's API key never reaches the browser.** The `api` creates the Map Tiles API session and relays each tile, without storing it, through tile links. It issues the links only to project members. They stay the same for up to an hour, so the browser cache works, and then expire.
  - Tiles are limited per user and minute (600) and per user and day (3,000), so one user cannot use up the project's Google quota.
  - Google tiles never go deeper than the imagery Google has for the area. Google imagery is not shown without its data credits.
  - Google's terms forbid showing its maps next to another provider's, so a deployment offers one provider only.
  - Attribution is always visible: "© OpenStreetMap contributors", or the Google logo and Google's data credits for the area on screen.
  - When tiles cannot load, the shapes, zoom, tooltip and table keep working, and a notice says so.
  - A map too small to show the attribution in full draws no basemap and says so. On small maps the zoom and basemap buttons collapse into one menu.
- **Security headers.**
  - The UI's CSP allows images from `https://tile.openstreetmap.org`, beside `'self'` and `data:`. Google tiles come from the UI's own origin through the `api`.
  - Each OpenStreetMap tile request sends only the deployment's origin as the referrer, as OpenStreetMap's policy requires. The site-wide `no-referrer` policy stays.
  - nginx gets its own location for relayed tiles, which keeps tile links out of its access log and never buffers tiles to disk.
- **The assistant's prompt** stops claiming that geometry is published in degrees. It tells the model to project to EPSG:4326, because a map in metres cannot show a basemap.
- **No migration.** Widgets saved without the new fields keep no tooltip columns and no basemap.

Not in this change:
- Satellite imagery from anyone but Google. No keyless source shows city blocks, and Esri needs a key.
- Operator-defined tile servers, self-hosted tiles, vector tiles, and labels drawn over the satellite imagery.
- Saving a zoomed view as the widget's starting view.
- Choosing a tooltip column's aggregation (minimum, maximum, average, ratio).
- Tooltips on the other chart types.
- Exposing the sampling frame to scripts (`ctx.sampling_frame`). Block targets still come from a reference table.

## Capabilities

### New Capabilities

- `map-basemaps`: the background maps a deployment offers to the dashboard's maps. It covers:
  - the operator's choice of provider and its startup checks;
  - the basemap list for signed-in users;
  - how OpenStreetMap tiles are loaded;
  - Google tiles relayed by the `api` with the key kept on the server, behind expiring tile links, unstored, with a per-user rate;
  - attribution;
  - what leaves the deployment;
  - the image sources the UI allows.

### Modified Capabilities

- `dashboard-indicators`:
  - A map's shapes are drawn at their geographic position when the coordinates are degrees.
  - Maps gain zoom and pan, gestures that do not trap the page, keyboard access, and a view that is not saved.
  - Maps gain a tooltip with up to 5 tooltip columns, with rules for combining rows, formats, and a "Show as table" view that lists them.
  - Map widgets gain a basemap, a member's own switch, and fallbacks when the basemap is not offered or cannot load.
  - Tooltip columns join the columns a widget can name and the stale-configuration rules.
  - "Indicator formatting" now applies to the plotted value, and tooltip columns use their own format.

## Impact

- **ui** (`tamiza-ui`):
  - New dependency `leaflet` 1.9.4, pinned exactly, loaded lazily with the map, and `@types/leaflet` for development.
  - `pages/projects/indicators/`: the map widget is rewritten on a Leaflet adapter (`map-engine.ts`). New map controls, a tooltip model and geometry helpers.
  - A shared CDK-overlay tooltip in `shared/ui/`.
  - `widget-view.ts`: `queryFor` and a new `savedConfig`.
  - `widget-dialog.ts`: the tooltip-column editor and the basemap select.
  - `widget-table.ts`: per-column alignment.
  - `core/projects/indicators.service.ts`: types.
  - A basemaps service.
  - `en.json` strings and problem codes, and the specs.
  - `src/styles.css`: an unlayered block for the shapes Leaflet draws, which component styles cannot reach.
  - `angular.json`: Leaflet's CSS copied as a versioned asset.
  - `nginx-security-headers.conf`: `img-src`.
  - `nginx.conf`: the tile location.
  - `Dockerfile`: the build's third-party licence file.
  - The official Google Maps logo asset.
- **api** (`tamiza-api`):
  - `Indicators`:
    - `WidgetConfig` gains `tooltip` and `basemap`;
    - validation;
    - the grouped map query returns a row count and the tooltip values per group;
    - missing tooltip columns are reported.
  - A new `Maps` module:
    - options and their validator;
    - `GET /api/v1/basemaps`;
    - a Google session service;
    - the public tile endpoint behind expiring links;
    - the attribution endpoint;
    - a rate limiter;
    - a named `HttpClient` with no redirects and no proxy.
  - OpenAPI.
  - `Assistant/Prompts/system.md`: the geometry and map lines.
  - Unit and integration tests.
- **Configuration:**
  - `TAMIZA_MAP_BASEMAP_PROVIDER`;
  - `TAMIZA_MAP_GOOGLE_API_KEY`, `TAMIZA_MAP_GOOGLE_LANGUAGE` and `TAMIZA_MAP_GOOGLE_REGION`;
  - `TAMIZA_MAP_TILES_PER_MINUTE` and `TAMIZA_MAP_TILES_PER_USER_PER_DAY`.

  These go in `compose.yaml` (passed to `api` only, with the integers defaulted) and `.env.example`. With `google`, `api` needs outbound HTTPS to `tile.googleapis.com`.
- **Database:** no migration. `dashboard_widgets.config` is versionless `jsonb`.
- **Docs:**
  - `docs/indicators-dashboard.md`: the map section, a city-block example, stale columns, and "what it does not do".
  - `docs/operations.md`: a basemaps section, the outbound list and the data-protection note.
  - `docs/tamiza-brief.md`: the "no basemap" and "no map library" lines.
- **External services and terms:**
  - OpenStreetMap's tile usage policy.
  - Google Maps Platform terms, billing and quotas, which the operator accepts with their own key.
