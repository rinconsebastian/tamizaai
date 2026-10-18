# API deviations and notes

Written by the API half of the implementation (tasks 3, 5 and 6). None of these changes the JSON contract of D7, D8, D13 and D15; the UI codes against the names in the design as they are.

## Decisions where the design left room

- **Missing-tile status (task 6.6).** Google's pages document the error reasons but no HTTP status for a tile that does not exist. The relay decides by the reason in `error.errors[0].reason` first (`notFound`, `invalid` without a mention of the session or the key, or HTTP 404 means a missing tile). The rule and the other mappings are in `design.md`, "Google documentation check (6.6)". The fakes use the documented reasons with 404. Task 8.2 should confirm the real status with the operator's key.
- **Tiles are buffered, not streamed.** The relay reads a tile into memory (the `maps-google` client caps a response at 2 MB) and then writes it, so an oversized or broken answer is a clean 502 instead of a half-written 200. Nothing is written to disk or kept. `Content-Type`, `Cache-Control` and `ETag` are passed on exactly as Google wrote them (read from the raw headers, because the parsed form reorders directives).
- **Tile-link memo and the clock.** `IMemoryCache` keeps its own clock, so the entry's lifetime is set relative to now as the injected `TimeProvider` sees it (`AbsoluteExpirationRelativeToNow`), not as an absolute time. The key includes the window, so a window change always gives a new link.
- **Credits query values are read as text.** Letting minimal APIs bind `double`/`int` made a malformed value (`zoom=abc`) a 500 here, because the app's exception handler turns the framework's bad-request exception into one. The route reads the five values itself, so a bad value is a 400 naming the field, like every other bad value. `west > east` is accepted (a box over the antimeridian, per Google's viewport documentation).
- **Google key shape.** `MapOptionsValidator` also refuses a `TAMIZA_MAP_GOOGLE_API_KEY` that is not letters, digits, `-` and `_` (up to 256), without repeating it in the message: the key goes in a URL, and this keeps a pasted value with a space, `&` or newline inside from altering the request. The provider name is lower-cased, so `Google` works.
- **Unbound options.** `TileLinkWindow`, `TileLinkGrace` and `CreditsPerMinute` are plain properties of `MapOptions` with no `[ConfigurationKeyName]`, as D12 says. `configuration.Bind` would still read a root key of the same name, but no `TAMIZA_*` variable maps to them and Compose does not pass one.
- **Counters.** The per-day counter counts the tiles `api` agreed to fetch, including ones Google then refused or did not have, and 304s. The minute window is asked first, so a refused minute spends no day.
- **Credits access.** As D15 says, any signed-in user can read credits (not only members), within their own 120-a-minute budget. Viewport calls are not billed and carry no link or tile. This is the one basemap route open to a realm user who is in no project.
- **`MapStartupCheck` is public** (not internal) so the unit tests can run it.
- **Tests use `ManualTimeProvider`** (a clock that stands still and is set to five minutes into a link window) where the design names `AdjustableTimeProvider`, which follows the real clock and could cross a window boundary in the middle of a test. `AdjustableTimeProvider` is untouched.
- **Prompt kit version** went from 2 to 3 with the corrected geometry line and the map note (`AssistantPrompts.Version`).

## Observations (no change made)

- `IndicatorQueryBuilder` selects `count(*)` and the tooltip outputs for any grouped query that selects geometry, which in practice is a map, as D8 says. A query sent without `chartType` but with a geometry column also gets them; the UI always sends `chartType` for a map.
- A tooltip column that is the group column is allowed (the group column is a legal tooltip column, aggregated with `min`/`sum` like any other).
- `GET /basemaps` for `osm` is also gated by the members-or-superadmin rule, as D13 says for every provider, although no link is issued there.
