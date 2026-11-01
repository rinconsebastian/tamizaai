# Tasks

**Order.**
- Groups 1 to 6 (api) come first, in order; each ends with its tests green. Group 6 can run any time after group 3.
- Groups 7 to 13 (ui) follow, in order. Group 7 can start any time, since it only touches `styles.css` and its spec. Group 8 keeps the UI build and suite green from the first type change. Groups 11 to 13 can run in any order after group 10.
- Group 14 is last.

Every UI string goes into both `public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks they match). Spec scenario titles are named in backticks. "(API part)" and "(UI part)" split a scenario between groups.

**How to run the tests:**
- **API:** `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers. Filter by class while working, for example `--filter "FullyQualifiedName~DashboardColorsTests"`.
- **UI:** `npm test -- --watch=false` in `tamiza-ui/`. A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.
- **Palettes:** `node <dataviz skill>/scripts/validate_palette.js "<hex,…>" --mode light --surface "#ffffff"`, and `--mode dark --surface "#101828"`. Add `--ordinal` for the steps of a scale.

## 1. Schema (api)

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0019_add_dashboard_palettes.sql` exactly as design D3: `palette` with its default and `ck_dashboards_palette`, and `value_colors` with its default. Keep upper-case `CHECK` out of its comment. Verify the analytics integration tests still build their template database.
- [x] 1.2 Add `Palette` and `ValueColors` to `Data/Dashboard.cs` and mirror the defaults and the CHECK in `TamizaDbContext`. Map `value_colors` as a `jsonb` string, as `filters` is. Verify `SchemaDriftTests` passes.
- [x] 1.3 Add a `ScriptConventionTests` case for the 0019 script, in the style of the 0014 test. Verify it passes.
- [x] 1.4 Add a `SchemaMigratorTests` upgrade test from `0018`. Seed a dashboard with filters, two widgets and its version 1, then `MigrateAsync`. Verify:
  - the dashboard reads `tamiza` and `[]`;
  - its `filters`, `updated_at`, widgets and version content are unchanged;
  - a `palette` of `neon` is refused.

## 2. Palettes, config and validation (api)

- [x] 2.1 Add `Indicators/Palettes.cs` (design D2): `All`, `Scales`, `DefaultScale(palette)`, `MaxValueColors = 64`, and the record `ValueColor(Column, Value, Color)`. Verify a unit test that every palette has a default scale in `Scales`, and that `All` matches the CHECK of 0019.
- [x] 2.2 Add `WidgetDisplay(Color, Ramp)` and `Display` to `WidgetConfigInput`, `Normalize()` and `WidgetConfig` (design D3). Verify unit tests:
  - a config saved before has no `display`;
  - `display` survives the JSON round trip that `ReadConfig` and the PATCH use;
  - `WidgetQuery.For` leaves it out.
- [x] 2.3 Add `DashboardValidation.Colors` and the `display` rules of `CheckConfig`, as in design D4. Verify `DashboardValidationTests` and `IndicatorValidationTests`: one case per row of D4's table, each naming its key (`palette`, `valueColors[1]`, `valueColors[0].color`, `config.display.color`, `config.display.ramp`…), `No color codes`, `Color used twice in a column`, `Same color in two columns`, `Color with a series` and `Scale on a pie chart`.

## 3. Saving and reading colors (api)

- [x] 3.1 Add `PUT /dashboards/{dashboardId}/colors` (`SaveDashboardColors`, role `analyst`) as design D5, with OpenAPI. Add `palette` and `valueColors` to `DashboardView`. Verify `DashboardColorsTests`:
  - `Palette chosen` (API part);
  - `Viewer cannot change the palette` (403);
  - `Dashboard saved before palettes` (API part);
  - `Color kept for a value` (API part);
  - a save equal to the stored colors writes nothing (`updated_at` unchanged).
- [x] 3.2 Copy the palette and the colors kept by value in `DuplicateAsync`. Verify `Colors duplicated` and `Colors of a copy are independent` (API parts) in `DashboardDuplicateTests`.
- [x] 3.3 Verify that moving a widget keeps its `display` and that a widget with `display.color` saved and read back through PATCH and GET is unchanged (`Widget moved`, API part).

## 4. Versions (api)

- [x] 4.1 Add `DashboardChangeKinds.ColorsChanged` and note it from the colors endpoint. Change `Content` and `ReadContent` as design D6: the keys are written only when they are not the defaults, and missing keys read as `tamiza` and `[]`. Verify unit tests:
  - the content of a dashboard with the defaults is byte-for-byte the content built before this change (a fixed fixture from 0018's version 1);
  - a content with `earth` and two colors kept reads back the same.
- [x] 4.2 Write the palette and the colors kept by value back in a restore, and return them from `GET …/versions/{n}`. Verify `DashboardVersionTests`:
  - `Palette changed`, `Colors saved unchanged` and `Widget color changed`;
  - `Dashboard saved before palettes` (no version);
  - `Colors restored`, `Version recorded before palettes`;
  - a restore's step 2 records no "unrecorded" version on a dashboard migrated from 0018.

## 5. A pie's domain (api)

- [x] 5.1 Add `IndicatorQueryBuilder.PieDomain` to `Plan` for a pie (design D8): one statement that ranks every group with a total above zero, the missing group in its place, over the group column and the widget's own filters only, capped at 9. Verify `IndicatorQueryBuilderTests`: the pie's domain has no dashboard filter parameter, and every other kind's SQL is unchanged.
- [x] 5.2 Read the domain before the rows (`ReadPieDomainAsync`) and make it with `SeriesDomain.BySize(…, 8)`: all the groups when there are at most 8, otherwise the 7 largest and "Other", the missing group ranked and foldable like any group. Return `domain` on a pie's response only. Verify `IndicatorQueryTests`:
  - `Colors kept under a dashboard filter` and `Other kept under a dashboard filter` (API parts: the same `domain` with and without the filter);
  - a pie with no filter has its domain in today's size order, the missing group in its place;
  - a group whose total is zero, negative or not a finite number (a ratio over a zero denominator) is not in the domain;
  - every other kind's response is byte-for-byte as before.

## 6. MCP (api)

- [x] 6.1 Add `palette` and `valueColors` to `update_dashboard` and to the outputs of `get_dashboard` and `get_dashboard_version`, and `display` to the widget tools' config (design D10). Verify `McpDashboardToolsTests`:
  - `Colors through MCP`;
  - `Unknown palette through MCP`;
  - one `update_dashboard` call with a rename and colors makes one version (`mcp-server` "One version per request").
- [x] 6.2 Add the guidance of design D10 to `Mcp/Instructions.md` and `McpGuide.cs`, and the new settings to `docs/mcp.md`. Verify the MCP tools' docs test passes and names `palette`, `valueColors` and `display`.

## 7. Palette and scale tokens (ui)

- [x] 7.1 Choose the values of `institutional`, `earth` and `vivid` (light and dark) and of `teal`, `green`, `orange`, `red`, `purple` and `gray` (seven steps and inks, light and dark) with the palette validator, as design D2 says. Record each command and its result in a **Verification Results** section of `design.md`, and drop any palette that cannot pass from the catalog before going on.
- [x] 7.2 Add `--tmz-pal-{palette}-{1..8}` and `--tmz-ramp-{scale}-{1..7}`, `--tmz-ramp-{scale}-ink-{1..7}` to `:root` and `.dark` in `styles.css` (design D1). `tamiza` and `blue` hold today's values. Remove `--tmz-series-1..8`, keep `--tmz-series-other`, and define `--tmz-seq-n` and `--tmz-seq-ink-n` as `var(--tmz-ramp-blue-n)`. Update the comment above the tokens.
- [x] 7.3 Extend `chart-tokens.spec.ts` (design D2) to cover every palette and scale in both themes. Verify `Checks on every palette`:
  - the validated values are pinned;
  - the band, the chroma floor and the adjacent normal-vision floor hold for each palette, and so does the adjacent color-vision floor of 8 under protan and under deutan, simulated as the validator does and cross-checked against the numbers it printed;
  - the eighth color is held to the same floors against the first (a pie of eight wraps), and every color stays 10 from the neutral of "Other";
  - the sequential checks hold for each scale;
  - `tamiza` and `blue` equal the values validated before;
  - the checks fail on a missing or repeated color, a color off the band or dull, neighbors too close for full-color readers or under protan or deutan, and an eighth color too close to the first.

## 8. Types and service (ui)

- [x] 8.1 Add `PaletteId`, `ScaleId`, `ValueColor`, `DashboardColors`, `WidgetDisplay`, the pie's `domain`, `palette` and `valueColors` on the dashboard and version views, and `colorsChanged` to `core/projects/indicators.service.ts`, with `saveColors(projectId, dashboardId, colors)`. Verify the build and the service spec for the new call.
- [x] 8.2 Keep `display` in `savedConfig` for the kinds of design D3, with only the fields each takes, and leave it out of `queryFor`. Verify `widget-view.spec.ts`:
  - changing `display.color` sends no query;
  - `savedConfig` drops `display.color` from a column chart that gains a series;
  - `Widget form saved unchanged` still holds for a widget saved before `display`.

## 9. Resolving colors (ui)

- [x] 9.1 Add `indicators/chart-colors.ts` (design D7): `chartColors`, `accent`, `forValues`, `kept`, `ramp` and `rampStyle`. Verify `chart-colors.spec.ts`:
  - `Colors set aside`;
  - `Other stays neutral`;
  - `Same value, same color`;
  - `Value not in the data`;
  - `Case differs`;
  - `"(none)"` never takes a kept color;
  - a chart with no series uses only kept colors (`kept`) and `accent` (`One bar highlighted`);
  - the palette's scale is used when `display.ramp` is unset (`Palette's scale`).
- [x] 9.2 Pass `DashboardColors` from `project-indicators.ts` through `widget-grid` to each `widget-view` (`colors` input), and into `WidgetDialogData`. The current dashboard or the version shown gives them. Verify a `widget-grid.spec.ts` case that a dashboard with `earth` draws a widget with `var(--tmz-pal-earth-1)`.

## 10. Charts read their colors (ui)

- [x] 10.1 Move `axis-chart` to `accent`, `kept` and `forValues`, give `series-legend` the colors it is handed, and remove `seriesColor` from `chart-scales.ts`. Verify `chart-widgets.spec.ts` and `series-widgets.spec.ts`:
  - `Column chart in color 3`;
  - `Widget saved before colors`;
  - `One bar highlighted`;
  - `Color kept by value`;
  - `Palette changed`;
  - tooltips carry the same color as the mark.
- [x] 10.2 Color the pyramid's sides with `forValues(seriesBy, [left, right])`. Verify `Pyramid sides`.
- [x] 10.3 Color the pie's slices from its `domain`, and fold every slice outside it into "Other" (design D8). Verify `Colors kept under a dashboard filter` and `Other kept under a dashboard filter` (UI parts), and that a pie without a domain in the response (an older API) still draws.
- [x] 10.4 Bind `rampStyle` on the heat map's and the map's root elements, and remove `sequentialColor` (design D1). Verify:
  - `Map on the green scale` and `Palette's scale` in `map-widget.spec.ts` and the heat map's spec;
  - the map's shapes read the green steps through `.tmz-map path.tmz-seq-n`;
  - `Likert unchanged`.
- [x] 10.5 Verify `Palette printed` in `dashboard-print.spec.ts`: a dashboard with `earth` and a green map keeps those tokens while it prepares the print.

## 11. Colors form (ui)

- [x] 11.1 Add `dashboard-colors-dialog.ts` (design D9): the palette radio group with swatches and scale, and the colors kept by value with column, value (combobox on `GET …/values`) and color. Map the API's errors onto their rows. Verify `dashboard-colors-dialog.spec.ts`:
  - `Palette previewed`;
  - `Palettes offered`;
  - `Value picked from the data`;
  - a refused second entry shown on its row (`Color used twice in a column`, UI part);
  - keyboard access to every field.
- [x] 11.2 Add the **Colors** action to the dashboard's menu for analysts and admins, hidden while a version is viewed. Saving updates the page's colors without reloading the widgets' data. Verify `Viewer has no Colors action`, `Palette chosen` (UI part), and that saving unchanged sends no request.
- [x] 11.3 Write the palettes, the colors kept by value and the shared-column caveat of design "Risks" in `docs/indicators-dashboard.md`, and replace its "let you choose their colors" line (§ limits). Verify the links.

## 12. Widget form (ui)

- [x] 12.1 Add the **Color** and **Scale** radio groups to `widget-dialog.ts` for the kinds of design D3, with numbered, named swatches from the dashboard's palette, and the preview drawn with the dashboard's colors. Verify `widget-dialog.spec.ts`:
  - `Color picked`;
  - `Named choices`;
  - the fields of each chart type (no color for a widget with a series, scale only for map and heat map);
  - picking a color sends no query.
- [x] 12.2 Add the widget color and scale to `docs/indicators-dashboard.md`. Verify the links.

## 13. Versions (ui)

- [x] 13.1 Name `colorsChanged` in the version history, and draw a viewed version with its own palette and colors kept by value. Verify `dashboard-versions-dialog.spec.ts` for the label, and `Colors of a version` and `Colors restored` (UI parts) in the page's spec.

## 14. Verification

- [x] 14.1 Real stack: a dashboard with a stacked column chart by `water_source`, a pie by `sex`, a pyramid by `sex`, a bar chart by `municipality`, a map and a heat map. Then:
  - switch through the four palettes;
  - keep colors for `Female`, `Male` and one municipality;
  - give the map the green scale;
  - set a dashboard filter that reorders the pie.

  Check it at 390 and 1,280 pixels with a keyboard pass, and record the results in the design.
- [x] 14.2 Print the dashboard on A4 with background graphics off: every palette color, kept color and scale on paper.
- [x] 14.3 From an MCP client, set a palette and a kept color with `update_dashboard`, create a map with a scale, and read them back with `get_dashboard`.
- [x] 14.4 Run `npm run check:safety` in `tamiza-ui/` and both full suites.
- [x] 14.5 Write "Deviations from the plan" and "Verification Results" in the design, update `docs/tamiza-brief.md` (data model and roadmap: `add-widget-display-options` next), and run `openspec validate add-dashboard-palettes --strict`.
