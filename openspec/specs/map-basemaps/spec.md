# map-basemaps Specification

## Purpose

Lets the operator offer a background map, streets or satellite imagery, under the dashboard's maps from OpenStreetMap or Google. Provider keys stay on the server, and the operator knows exactly what leaves the deployment.

## Requirements

### Requirement: Basemap provider chosen by the operator
A deployment SHALL offer basemaps only when the operator sets `TAMIZA_MAP_BASEMAP_PROVIDER` to `osm` or `google`. When it is unset, empty or `none`, no basemap SHALL be offered, and neither the browser nor the server SHALL make any request to a tile provider. Any other value SHALL stop the `api` at startup with a message naming the variable.

#### Scenario: Not configured
- **WHEN** a deployment has no basemap variables and a member opens a map
- **THEN** no basemap is offered, the map is drawn on its plain background, and no request is made to any tile provider

#### Scenario: Unknown provider
- **WHEN** the operator sets `TAMIZA_MAP_BASEMAP_PROVIDER=mapbox` and starts the stack
- **THEN** the `api` does not start, and its log names `TAMIZA_MAP_BASEMAP_PROVIDER` and the accepted values

### Requirement: Basemaps of each provider
With `osm`, a deployment SHALL offer one basemap, Streets, from OpenStreetMap's standard tiles. With `google`, it SHALL offer Streets (Google's roadmap) and Satellite (Google's imagery), and SHALL require `TAMIZA_MAP_GOOGLE_API_KEY`; without the key, the `api` SHALL stop at startup naming it. A deployment SHALL offer one provider only, so a Google map is never shown next to a map from another provider.

#### Scenario: OpenStreetMap
- **WHEN** the operator sets `TAMIZA_MAP_BASEMAP_PROVIDER=osm`
- **THEN** maps offer None and Streets, and Streets shows OpenStreetMap

#### Scenario: Google
- **WHEN** the operator sets `TAMIZA_MAP_BASEMAP_PROVIDER=google` and a Map Tiles API key
- **THEN** maps offer None, Streets and Satellite, both from Google, and OpenStreetMap is not offered

#### Scenario: Google without a key
- **WHEN** the operator sets `TAMIZA_MAP_BASEMAP_PROVIDER=google` without `TAMIZA_MAP_GOOGLE_API_KEY`
- **THEN** the `api` does not start, and its log names `TAMIZA_MAP_GOOGLE_API_KEY`

### Requirement: Basemap list for project members
`GET /api/v1/basemaps` SHALL list the basemaps the deployment offers to a signed-in user who is a member of at least one project or is `superadmin`. Each entry SHALL give its kind (Streets or Satellite), its zoom range, how the browser gets its tiles and its attribution. Any other signed-in user, and every user when the deployment offers none, SHALL get an empty list. Without a valid token the endpoint SHALL answer 401. The public system configuration SHALL say nothing about basemaps.

#### Scenario: Anonymous request
- **WHEN** a request without a token reaches `GET /api/v1/basemaps`
- **THEN** the API responds 401

#### Scenario: Nothing configured
- **WHEN** a signed-in viewer requests the basemap list of a deployment without basemaps
- **THEN** the API responds 200 with an empty list

#### Scenario: User without a project
- **WHEN** a user of the realm who belongs to no project requests the basemap list of a deployment with Google basemaps
- **THEN** the API responds 200 with an empty list and issues no tile link

### Requirement: OpenStreetMap tiles
With `osm`, the browser SHALL load tiles only from `https://tile.openstreetmap.org/{z}/{x}/{y}.png`. Each tile request SHALL send the deployment's origin as its referrer, never the page's path. The browser SHALL load only the tiles of the area on screen, with no prefetching and no offline copy, and SHALL follow the tiles' own cache rules.

#### Scenario: Referrer of a tile
- **WHEN** a member at `https://tamiza.example.org/projects/<id>/indicators` views a map with Streets under OpenStreetMap
- **THEN** each tile request carries the referrer `https://tamiza.example.org/` and nothing of the page's path

#### Scenario: Other requests keep no referrer
- **WHEN** the same member opens an export download link
- **THEN** that request carries no referrer, as before

### Requirement: Google key stays on the server
With `google`, the browser SHALL get Google tiles only from the `api`. The `api` SHALL request them from Google's Map Tiles API with the key and with sessions it creates and renews itself, in the language and region the operator sets (`TAMIZA_MAP_GOOGLE_LANGUAGE`, `TAMIZA_MAP_GOOGLE_REGION`). The key SHALL NOT appear in any response, page, tile address, log line or error message.

#### Scenario: Key not in the browser
- **WHEN** a member views a map with Satellite under Google basemaps
- **THEN** the basemap list, the tile addresses and the tile responses the browser receives do not contain the key

#### Scenario: Session expired at Google
- **WHEN** Google rejects the `api`'s session because it has expired
- **THEN** the `api` creates a new session and the tiles keep loading

### Requirement: Tile links for Google basemaps
The `api` SHALL serve Google tiles only through tile links it issued to a user listed in "Basemap list for project members", for one basemap, valid for at most one hour, and only while the provider is `google`. The tile endpoint SHALL be public, SHALL accept only whole-number zoom levels and tile coordinates within the map's range, and SHALL answer 404 to any other request without contacting Google. A map left open SHALL get fresh links before its links expire.

#### Scenario: Forged or expired link
- **WHEN** a request uses a tile link the `api` did not issue, or one issued more than an hour ago
- **THEN** the API responds 404 and serves no tile

#### Scenario: Coordinates out of range
- **WHEN** a request asks for the tile x 5 at zoom level 2
- **THEN** the API responds 404 without contacting Google

#### Scenario: Provider switched off
- **WHEN** the operator switches the provider from `google` to `none` and restarts the `api`, and a browser still uses a link issued before
- **THEN** the API responds 404 and makes no request to Google

#### Scenario: Map open all morning
- **WHEN** a member keeps a dashboard with a Google map open for three hours and keeps panning, on a device whose clock is half an hour off
- **THEN** the tiles keep loading

### Requirement: Stable tile links
Within one period of at most an hour, every basemap list given to the same user SHALL hold the same tile links, so the browser can reuse the tiles it has cached under Google's cache rules.

#### Scenario: Dashboard reloaded
- **WHEN** a member reloads a dashboard with a Google map twice within a few minutes
- **THEN** both lists give the same tile links, and the browser serves the tiles it already has from its cache

### Requirement: Relayed tiles are not stored
The `api` SHALL pass each Google tile to the browser as it receives it, with Google's content type and cache rules, and SHALL mark it for use by the deployment's own pages only. Neither the `api` nor the UI's web server SHALL store, prefetch or cache tiles, or write them to disk.

#### Scenario: Cache rules passed through
- **WHEN** Google answers a tile with a `Cache-Control` header
- **THEN** the browser receives the same `Cache-Control` and `Cross-Origin-Resource-Policy: same-origin`, and the tile is written neither by the `api` nor by the `ui` container

#### Scenario: Tile embedded by another site
- **WHEN** a page on another site uses a valid tile link as an image
- **THEN** the browser refuses to show it

### Requirement: Google tile limits per user
The `api` SHALL relay at most `TAMIZA_MAP_TILES_PER_MINUTE` Google tiles per user per minute (600 by default) and at most `TAMIZA_MAP_TILES_PER_USER_PER_DAY` per user per UTC day (3,000 by default), and SHALL answer the rest with 429. When Google fails or refuses a tile, the `api` SHALL answer with an error and SHALL NOT pass on Google's message.

#### Scenario: Too many tiles in a minute
- **WHEN** one user's browser requests the 601st Google tile within a minute
- **THEN** the API responds 429 to that request, and other users' tiles keep loading

#### Scenario: Daily allowance used up
- **WHEN** one user has been relayed 3,000 Google tiles today
- **THEN** their next tile request gets 429 until the next UTC day, and another member's tiles keep loading

#### Scenario: Google quota exhausted
- **WHEN** Google refuses tiles because the operator's daily quota is used up
- **THEN** the tile requests fail, and the map says "Basemap unavailable" and keeps working without it

### Requirement: Imagery depth
A Google basemap SHALL NOT request tiles deeper than the deepest zoom Google offers for the area on screen. Zooming further SHALL enlarge the deepest tiles instead of failing.

#### Scenario: Zooming past the imagery
- **WHEN** a member zooms a Google Satellite map of a rural area to level 20, and Google's imagery there stops at level 18
- **THEN** the map keeps showing imagery, enlarged from level 18, and does not say the basemap is unavailable

### Requirement: Attribution on the map
Whenever a basemap is shown, the map SHALL show its attribution inside the map at all times, never behind a control:
- for OpenStreetMap, "© OpenStreetMap contributors", linking to OpenStreetMap's copyright page;
- for Google, the Google Maps logo, unmodified, and the data credits Google gives for the area on screen.

Attribution SHALL wrap rather than be cut, and SHALL be shown as text and links, never as markup from a provider.

#### Scenario: OpenStreetMap credit
- **WHEN** a map shows Streets under OpenStreetMap
- **THEN** "© OpenStreetMap contributors" is visible inside the map, linking to `https://www.openstreetmap.org/copyright`

#### Scenario: Google credits follow the view
- **WHEN** a member pans a Google Satellite map from one city to another
- **THEN** the Google Maps logo stays visible, and the data credits change to those Google gives for the new area

#### Scenario: No basemap, no credit
- **WHEN** a map has no basemap
- **THEN** it shows no basemap attribution

### Requirement: Google imagery only with its credits
A Google basemap whose data credits cannot be obtained SHALL NOT be shown. A map that already shows credits SHALL keep the last credits it obtained while a later request for them fails.

#### Scenario: Google credits unavailable
- **WHEN** the first request for Google's data credits of a map fails
- **THEN** the map shows no Google imagery and says "Basemap unavailable"

#### Scenario: Credits request fails after a pan
- **WHEN** a member pans a Google map that shows credits and the next request for credits fails
- **THEN** the map keeps its imagery and its last credits

### Requirement: What leaves the deployment
Basemap requests SHALL carry only the basemap kind, tile coordinates and, for Google's data credits, the zoom and bounds of the area on screen. No rows, group values, geometry or tooltip values SHALL be sent to a tile provider. With `osm`, each viewer's browser SHALL contact OpenStreetMap directly, sending what a browser sends with any image: its address, its user agent and the deployment's origin. With `google`, only the `api` SHALL contact Google, adding its session, key, language and region.

#### Scenario: Google basemap
- **WHEN** members view Google Satellite maps of city blocks
- **THEN** Google receives requests from the `api` only, holding the session, the key, tile coordinates and the zoom and bounds of each view, and no value of the indicator

#### Scenario: OpenStreetMap basemap
- **WHEN** members view Streets maps under OpenStreetMap
- **THEN** OpenStreetMap receives tile requests from their browsers, holding the tile coordinates, the browser's address and the deployment's origin, and no value of the indicator

### Requirement: Image sources the UI allows
The UI's content security policy SHALL allow images only from its own origin, `data:` URLs and `https://tile.openstreetmap.org`, whatever the provider. The UI SHALL keep its site-wide `no-referrer` referrer policy.

#### Scenario: Headers of the UI
- **WHEN** the response headers of the UI's index page are read from the `ui` container
- **THEN** its `Content-Security-Policy` has `img-src 'self' data: https://tile.openstreetmap.org` and its `Referrer-Policy` is `no-referrer`
