# Design

## Context

See proposal.md for the motivation and the specs for the behavior. The pieces this change works on:

- **Chart colors are CSS custom properties** in `tamiza-ui/src/styles.css`:
  - `:root` (lines 63-97) and `.dark` (100-134) hold `--tmz-series-1..8` and `--tmz-series-other`;
  - `--tmz-seq-1..7` with `--tmz-seq-ink-1..7`, the label color on each step;
  - the Likert arms `--tmz-div-neg-1..5`, `--tmz-div-pos-1..5` and `--tmz-div-neutral`.

  The values are the dataviz skill's reference palette, validated with its `validate_palette.js`. `chart-tokens.spec.ts` imports `styles.css` as text and checks the sequential and diverging tokens with an OKLab helper. Nothing in the app sets `.dark` today, so the dark values are ready but not shown.
- **Who picks a color:**
  - `chart-scales.ts:91-113`: `seriesColor(i)` returns `var(--tmz-series-{i+1})`, or `--tmz-series-other` past the eighth, and `sequentialColor(step)` returns `var(--tmz-seq-{step})`.
  - `axis-chart.ts:247` draws a chart with no series in `seriesColor(0)`.
  - `series-legend.ts:19`, through `seriesFill`, colors a series by its index in the domain, so color follows the series' place, not its value.
  - `pyramid-chart.ts:259` uses colors 1 and 2 for the left and right sides.
  - `pie-widget.ts:95-117` sorts the filtered slices by size and colors them by rank.
  - `heatmap-chart.ts:305-311` and `map-widget.ts:504` read the sequential tokens. The map's shapes are colored by the global rules `.tmz-map path.tmz-seq-n` (`styles.css:143-149`), because Leaflet builds its own DOM.
- **Tooltips live in the CDK overlay.** `ChartTooltips` (`shared/ui/chart-tooltip.ts:104`) is attached to the body, outside the widget, and its lines carry the series color as a CSS value. The widget form, with its preview, is a CDK dialog, also in the overlay.
- **A widget's config** is `jsonb` read through `WidgetConfigInput.Normalize()` (`IndicatorModels.cs:154`). `ReadConfig` drops properties the record does not have. In the UI, `savedConfig` (`widget-view.ts:95`) keeps the fields each kind saves, and `queryFor` (`widget-view.ts:47`) keeps the fields that change the query, so a field outside `queryFor` changes nothing that is fetched. `WidgetQuery.For` is the C# twin used by the emails and MCP.
- **A dashboard** (`Data/Dashboard.cs`) has `Name`, `Position` and `Filters` (`jsonb`). Filters are saved by `PUT /dashboards/{id}/filters` (`DashboardEndpoints.cs:177-217`):
  - validation by `DashboardValidation.Filters`;
  - a save equal to what is stored returns without writing;
  - `versions.Note(..., FiltersChanged)` records the change.

  Duplicate (`DashboardEndpoints.cs:374`) copies the filters and the widgets. `DashboardView` (`IndicatorModels.cs:267`) is what `GET /dashboards/{id}` returns.
- **Versions** (`DashboardVersions.cs`):
  - `Content(name, filters, widgets)` (line 328) builds the JSON of a version, embedding the stored JSON as it is.
  - Equality with the newest version is a string comparison of that content (lines 192 and 548), both for deciding whether a save makes a version and for a restore's step 2.
  - `ReadContent` (line 428) reads a missing part as empty, and a restore writes back name, filters and widgets (line 599).
  - The change kinds are listed in `DashboardChangeKinds` (lines 15-55) and mirrored in `core/projects/indicators.service.ts:240-255`.
- **A series' domain** is a statement over the widget's own filters only (`SeriesQueryBuilder.cs:64-80`): values by total, largest first, capped at one more than the widget shows, plus one null row for the missing value. `SeriesDomain.Fold` and `SeriesDomain.Order` (`IndicatorDashboard.cs:299-302`) decide which values are kept and in what order. A pie has no domain: `Plan` (`IndicatorQueryBuilder.cs:111`) returns the filtered rows only.
- **The dashboard's columns and values** for the filters form come from the widgets' published indicator tables (`dashboard-filters-dialog.ts:55-58`) and from `GET /dashboards/{id}/values` (`DashboardEndpoints.cs:113`, `indicators.service.ts:576`).
- **MCP** (`Mcp/McpDashboardTools.cs:212`): `update_dashboard` takes a name, a position or filters, each optional. `create_widget` and `update_widget` take the config the REST handlers take.

## Goals / Non-Goals

**Goals:**
- Every color a chart draws is resolved in one place (`chart-colors.ts`), from the palette, the colors kept by value and the widget's own setting, so every chart, its legend, its tooltips and the form's preview agree.
- Dashboards and widgets saved before this change render exactly as today, and their version content stays byte-for-byte the same.
- Every palette and scale is validated before it ships, and the tests keep it at the validated values.

**Non-Goals:**
- A dark theme switch. The palettes get dark values so the theme can come later, but this change does not show it.
- Colors in the scheduled emails and their workbooks, which show tables.
- Per-palette diverging arms, status colors and texture fills.
- A per-widget switch to ignore the dashboard's colors kept by value.

## Decisions

### D1. Tokens: named categorical colors, scoped sequential scales

`styles.css` gains, in `:root` and in `.dark`:

| Token | Count | Notes |
|---|---|---|
| `--tmz-pal-{palette}-{1..8}` | 4 palettes × 8 | `tamiza` holds today's `--tmz-series-1..8` values, unchanged |
| `--tmz-series-other` | 1 | kept as is, shared by every palette |
| `--tmz-ramp-{scale}-{1..7}` and `--tmz-ramp-{scale}-ink-{1..7}` | 7 scales × 14 | `blue` holds today's `--tmz-seq-*` values, unchanged |

`--tmz-series-1..8` are removed: their two readers (`chart-scales.ts`, `series-legend.ts`) move to `chart-colors.ts`. `--tmz-seq-n` and `--tmz-seq-ink-n` stay as the variables a mark reads. `:root` defines them as `var(--tmz-ramp-blue-n)`, and a map or heat map sets them on its own container to the steps of its scale, for example `[style.--tmz-seq-3]="'var(--tmz-ramp-green-3)'"`. The Likert tokens do not change.

- **Why categorical colors are named.** A series color leaves the widget: it is copied into a tooltip line in the CDK overlay, and the form's preview is in a dialog. `var(--tmz-pal-earth-3)` resolves to the same color wherever it is used. A palette scoped by a class on the dashboard would resolve to the default palette in the overlay.
- **Why sequential scales are scoped.** The map's shapes can only be colored by the global `.tmz-map path.tmz-seq-n` rules: Leaflet writes presentation attributes, which cannot hold `var()`. A custom property set on the map's container reaches those shapes, and its `var(--tmz-ramp-…)` is resolved on the element, so the dark values still apply. Neither the map nor the heat map puts a step's color in a tooltip.

*Alternatives:*
- A `data-palette` attribute on `<html>` while a dashboard is shown, like `tmz-printing`. Rejected: it is global state, and a viewed version or a widget moved to another dashboard would have to change it.
- Per-scale classes for the map (`tmz-ramp-green-3`, 49 rules). Rejected for the same result with more CSS.

### D2. The catalog and its validation

| Palette | Mood | Default scale |
|---|---|---|
| `tamiza` | today's colors | `blue` |
| `institutional` | sober: blues, teals, slate, with muted warm accents and an olive green | `teal` |
| `earth` | terracotta, ochre, wine, moss and brown, with indigo, plum and teal as the cool counterpoints | `orange` |
| `vivid` | saturated, high-energy hues | `purple` |

The scales are `blue`, `teal`, `green`, `orange`, `red`, `purple` and `gray`. Ids live in `Indicators/Palettes.cs` (`Palettes.All`, `Palettes.Scales`, `Palettes.DefaultScale(palette)`) and in their UI twin `chart-colors.ts`.

- **Choosing values.** The three new palettes are built as the reference one was. Candidate hues are ordered by enumerating their orders and running `validate_palette.js` on each, keeping only orders that pass every hard gate on adjacent pairs, the eighth color counting as the first one's neighbor (a pie of eight wraps; see Repairs):
  - lightness band;
  - chroma floor;
  - CVD ΔE ≥ 8;
  - normal-vision ΔE ≥ 15.

  The check runs in light mode against `#ffffff` and in dark mode against `#101828`, the surfaces of `chart-tokens.spec.ts`. A contrast WARN is accepted with the relief Tamiza already provides: legends, tooltips and "Show as table". The new scales are built as `blue` was, with `--ordinal` checks on the steps. The commands and their results go in a **Verification Results** section of this design, as `add-two-dimension-widgets` did.
- **Keeping values.** `chart-tokens.spec.ts` gains:
  - for each palette and theme: 8 distinct colors, each inside the lightness band (OKLab L 0.43-0.77 in the light theme, 0.48-0.67 in the dark one) and above the chroma floor (0.10), adjacent pairs at least 15 apart in OKLab ×100 (the normal-vision floor, computable with the existing helper) and at least 8 apart under protan and under deutan, the eighth color held to the same two floors against the first (a pie of eight wraps), and each color at least 10 from the neutral of "Other";
  - for each scale and theme: the existing sequential checks (one hue within 40°, lightness in one direction with visible steps, ink at 4.5:1 and the better of the two inks), with the floors the validated scales meet (see "Where the validator and the design disagree").

  The color-vision check is the spec's own, not a record kept apart from it. It simulates protan and deutan the way the validator does (Machado, Oliveira and Fernandes, severity 1.0), gates at the validator's CVD target of 8, and is checked against the numbers the validator printed, within 0.1 (see "The color-vision check in the spec"). The first draft of this design left it with the validator and its recorded results, which let a palette edited in `styles.css` and in the spec's pinned lists pass every check with no color-vision check at all.
- **Hue names.** Each palette slot has a hue name, translated in `en.json` and `es.json` (`colors.hues.{palette}.{n}`). It names the color in the pickers (D9), never alone: the number is always shown too. A name has to fit the hue of both the light and the dark value of its color, to within about 25° of what the word means (measured in OKLCH); the three Tamiza names that did not were changed (see Deviations from the plan).

### D3. Data model (DbUp `0019_add_dashboard_palettes.sql`)

```sql
ALTER TABLE tamiza.dashboards
  ADD COLUMN palette text NOT NULL DEFAULT 'tamiza',
  ADD COLUMN value_colors jsonb NOT NULL DEFAULT '[]',
  ADD CONSTRAINT ck_dashboards_palette CHECK (palette IN ('tamiza', 'institutional', 'earth', 'vivid'));
```

`TamizaDbContext` mirrors the defaults and the CHECK, as it does for `ck_dashboard_widgets_chart_type`.

- `value_colors` is `[{ "column": "sex", "value": "Female", "color": 5 }]`, camelCase, in the order the member saved them.
- A widget's config gains one optional object, `display`, so that the display options of the next change (`add-widget-display-options`) sit next to it:

  | Field | Type | Kinds | Default |
  |---|---|---|---|
  | `display.color` | 1-8 | bar, column and line with no series; histogram | 1 |
  | `display.ramp` | a scale id | map, heatmap | the palette's scale |

  `savedConfig` keeps `display` for those kinds, with only the fields the kind takes. `queryFor` leaves it out, so changing a color never runs a query. `WidgetQuery.For` ignores it.

*Alternatives:*
- One `style jsonb` column on the dashboard. Rejected: the palette is an enum the database can check, as `chart_type` is.
- Flat `color` and `ramp` config fields. Rejected: the next change adds several display fields, and one object keeps them apart from the query.

### D4. Validation

`DashboardValidation.Colors` (new) and `IndicatorValidation.CheckConfig` add, with errors on these keys:

| Rule | Key | Message |
|---|---|---|
| Unknown palette | `palette` | "Choose one of the palettes: tamiza, institutional, earth, vivid." |
| More than 64 entries | `valueColors` | "A dashboard keeps at most 64 colors by value." |
| A column that is not a valid column name (`ColumnNames.IsValidColumnName`) | `valueColors[i].column` | |
| A value that is null, longer than `DataQueryBuilder.MaxTextLength` or holding a NUL character (`jsonb` cannot store one) | `valueColors[i].value` | "The value cannot contain a NUL character." for the last |
| A color outside 1-8 | `valueColors[i].color` | |
| A value or a color repeated within one column | `valueColors[i]` | "{column} already keeps a color for {value}." ("…for an empty text." when the value is empty) / "{column} already uses color {n}." |
| `display.color` on another kind, or with a series | `config.display.color` | "A widget with a series takes its colors from its series." / "A {kind} widget has no color of its own." |
| `display.color` outside 1-8 | `config.display.color` | |
| `display.ramp` on a kind other than map and heatmap, or unknown | `config.display.ramp` | |

Entries are not checked against the published tables, because a script can change its values later. An empty value is allowed, since an empty text is a group value (`dashboard-indicators` "Bar and column charts of one indicator").

### D5. Saving and reading the colors

- `PUT /api/v1/projects/{projectId}/dashboards/{dashboardId}/colors`, role `analyst`, takes `{ palette, valueColors }` and returns the same.
- It is built like `SaveFiltersAsync`: validate, compare the serialized result with what is stored and return without writing when equal, `ExecuteUpdateAsync` with `UpdatedBy` and `UpdatedAt`, then `versions.Note(..., ColorsChanged)`.
- `DashboardView` gains `palette` and `valueColors`.
- `DuplicateAsync` copies both, and the copied widgets keep their config, `display` included.
- Moving a widget does nothing extra: its `display.color` is a number, read against the palette of the dashboard it lands on.

### D6. Versions

- `DashboardChangeKinds.ColorsChanged = "colorsChanged"` joins `All`, the UI list and the history's strings ("Colors changed" / "Se cambiaron los colores").
- `Content(name, filters, palette, valueColors, widgets)` writes `"palette"` only when it is not `tamiza`, and `"valueColors"` only when it is not empty. A dashboard that never changed its colors keeps the content it has today, byte for byte. Its first save after the deploy then makes no version, and a restore's step 2 does not record an "unrecorded" state just because the content shape grew.
- `ReadContent` reads a missing `palette` as `tamiza` and missing `valueColors` as `[]`, and a palette the catalog no longer has as `tamiza` too, since the CHECK would refuse it on a restore. `VersionContent` gains both.
- A restore writes both back with the name and filters, so a version from before palettes restores *Tamiza* with no colors kept.
- `GET …/versions/{n}` returns them, so viewing a version draws its widgets in that version's colors (spec "Viewing a version"). `get_dashboard_version` returns them too.

### D7. Resolving colors (`chart-colors.ts`)

```ts
interface DashboardColors { palette: PaletteId; valueColors: readonly ValueColor[] }

/** The colors of one widget. */
function chartColors(colors: DashboardColors, type: ChartType, config: WidgetConfig): {
  accent: string;                                         // var(--tmz-pal-{p}-{display.color ?? 1})
  forValues(column: string, values: readonly SeriesKey[]): string[];  // one per value, in the order given
  kept(column: string, value: string | null): string | null;          // the color kept for a value, for a bar or column with no series
  ramp: ScaleId;                                          // display.ramp ?? DefaultScale(palette)
  rampStyle: Record<string, string>;                      // the --tmz-seq-* overrides of D1
}
```

`forValues` takes the widget's domain in display order (series, pyramid sides, or a pie's domain from D8) and works in three steps:
1. Each value with a color kept for `(column, value)` takes it. `"(none)"` and "Other" never match.
2. The colors taken in step 1 are set aside.
3. The other values take the remaining colors 1-8 in order, and "Other" takes `--tmz-series-other`.

The domain holds at most 8 entries, "Other" included, and a column keeps each color at most once, so two values of a widget never share a color. A bar or column chart with no series asks `kept(groupBy, value)` for each mark instead, which answers a color kept for the value or nothing: a mark with no color kept is drawn in `accent`, not given the next free color (spec "One bar highlighted"). A line with no series and a histogram are always in `accent`.

- **Threading.** `project-indicators.ts` holds the `DashboardColors` of the dashboard or version shown and passes them through `widget-grid` to each `widget-view`, as a new `colors` input. `widget-view` passes `chartColors(...)` to the chart it draws. `WidgetDialogData` gains `colors`, so the preview uses the dashboard's palette and colors kept by value.
- **Readers.**
  - `axis-chart` takes `accent`, `kept` or `forValues` instead of `seriesColor`, and `series-legend` takes the resolved list.
  - `pie-widget` colors slices by `forValues(groupBy, domain)` and folds into "Other" every slice outside the domain (D8).
  - `pyramid-chart` takes `forValues(seriesBy, [left, right])`.
  - `heatmap-chart` and `map-widget` bind `rampStyle` on their root element.
  - `seriesColor` and `sequentialColor` are removed from `chart-scales.ts`.

### D8. A pie's domain from its own rows

`Plan` for a pie gains a second statement, `IndicatorQueryBuilder.PieDomain`, built over the group column and the widget's own filters only, so a dashboard filter never reaches it. It ranks every group whose total is above zero, the missing group (a null group) among them and in its place, and reads at most 9: one more than the pie's 8 colors, so a fold can be told.

- **The statement.** One `GROUP BY` of the group column, `HAVING total > 0 AND total < 'Infinity'` (a pie draws no slice for a total at or below zero, and a NaN would sort above every number), `ORDER BY` total descending and then the group column's own order with `NULLS LAST` (the order of the pie's own rows, which the page breaks ties by), `LIMIT 9`.
- **The fold.** `IndicatorDashboard` reads it before the rows (`ReadPieDomainAsync`), and `SeriesDomain.BySize(groups, 8)` makes the domain: all the groups when there are at most 8, otherwise the 7 largest followed by `other`. The missing group has no special place: it is ranked, and folds into "Other" when it is small, like any value. The page has always drawn a pie's missing group as one slice among the others, by size.
- **The response.** `IndicatorQueryResponse` gains `Domain`: pie only, `[{ value, kind }]` as `SeriesInfo`, largest total first, with `none` where the missing group ranks, and `other` last when groups were folded. It is null and left out of the JSON for every other kind.
- **The page** sizes the slices from the filtered rows as before, draws them largest first, colors them from the domain (D7), and sums into "Other" every row whose group is not in the domain. That includes a group with no share in the widget's own rows (its total there is at or below zero) that has one under a dashboard filter, as when the filter leaves out the rows that held its total down. Without a `domain` in the response (an API from before this change) it ranks the filtered rows by size and folds past the eighth color, as it always did.
- With no dashboard filter, the domain's order is today's size order, so an existing pie keeps its colors. Under a filter, the colors and "Other" stay as they were.
- `get_widget_data` and `query_indicator` keep their row shape. The domain is a drawing aid, so MCP does not return it.

The first version of this change read the series' domain statement (`SeriesQueryBuilder.Domain`) with `ReadDomainAsync` and `SeriesDomain.Fold(values, hasNone, 8)`, which lists the missing group last and never folds it. That would have recolored a pie whose missing group is not its smallest slice, and the audit that followed replaced it with the statement above (see Deviations from the plan).

*Alternative:* color slices by the natural order of their values in the UI. Rejected: a filter that removes one value would still shift the colors of every value after it. "Color follows the entity, never its rank" (dataviz skill).

### D9. Forms

- **Colors** (`dashboard-colors-dialog.ts`, CDK dialog like `dashboard-filters-dialog.ts`). The action `colors` is added to the dashboard's menu (`project-indicators.ts:410-440`), for analysts and admins, and hidden while a version is viewed, like the other changing actions. The form holds:
  - **Palette**: a radio group of the four palettes. Each card shows its name, its eight swatches in order and its scale as a gradient, and the current one is selected.
  - **Colors kept by value**: a list of rows, each with a column (the text columns the filters form offers, from the same source), a value (a native combobox, an `<input list>` with a `<datalist>`, fed by `GET …/values?column=&search=`, which also accepts typed text), and a color (a radio group of the palette's eight swatches). Each row can be removed, and a row can be added up to 64.
  - Saving calls `PUT …/colors` and replaces the page's `DashboardColors`. Server errors land on their rows, keyed as in D4.
- **Widget form** (`widget-dialog.ts`). It gains a **Color** radio group, with the dashboard palette's eight swatches, for the kinds of `display.color`, and a **Scale** radio group for map and heatmap: "Palette's scale" first, then each scale as a gradient. A swatch is a native `<input type="radio">`, as the form's other choices are (layout, sort), styled as a swatch with its number visible and an `aria-label` with the number and the hue name ("Color 3, jade"), so the arrow keys move through the group with no code of ours. The preview redraws on change without a new query (D3).

### D10. MCP

- `update_dashboard` gains `palette` (string) and `valueColors` (`[{ column, value, color }]`, the whole list, replacing the current one). Giving none of name, position, filters, palette or valueColors is still refused. The colors are one more commit inside the same `RecordAsync`, so the request remains one version (`mcp-server` "One version per request").
- `get_dashboard` and `get_dashboard_version` return `palette` and `valueColors`. `create_widget` and `update_widget` take `display` within the config.
- `Instructions.md` and `McpGuide.cs` add a short paragraph:
  - keep the default palette unless the person asks for another;
  - keep colors by value for categories that recur across widgets (sex, yes/no);
  - keep color 1 for a widget unless it should stand out.

## Risks / Trade-offs

- **[Two indicators share a column name with different meanings]**: a color kept for `type` = `A` colors both. → Documented in `docs/indicators-dashboard.md`. Members can use distinct column names in their indicator scripts.
- **[A pie's "Other" changes under a dashboard filter]**: a value that is large inside the filter but small overall now folds into "Other". → It is the series' rule already, and the cost of stable colors. Documented, and "Show as table" still lists every group.
- **[Palettes look alike, or a palette fails the gates]**: the moods in D2 are a target, not fixed hues. → The validator decides. A palette that cannot pass with distinct moods is dropped from the catalog before release, and the specs name four.
- **[Contrast below 3:1 on some light slots]**, as today for Tamiza's jade, amber and pink. → Accepted with the existing relief: legends for 2 or more series, tooltips, and "Show as table".
- **[More tokens in `styles.css`]**: about 270 custom properties. → Negligible at runtime, and the tests list them.
- **[Version content compared as strings]**: a content shape that always wrote the new keys would make every dashboard's next save look like a change. → D6 writes them only when they differ from the defaults.

## Migration Plan

1. Deploy `0019_add_dashboard_palettes.sql`. It adds the columns with defaults and rewrites no row.
2. Existing dashboards read as `tamiza` with no colors kept, and existing widgets have no `display`. They render as before, and their version content is unchanged (D6).
3. **Rollback.** The previous `api` ignores the new columns and drops `display` when it reads a config (`ReadConfig`). The previous `ui` ignores the new response fields. The columns can stay in place. Dropping them is a manual `ALTER TABLE` if ever needed.

## Open Questions

None that change the specs, the approach or the task breakdown.

## Deviations from the plan

Found while implementing, or fixed after what followed it: an audit of the implementation, a first pass on the real stack (browser, print and an MCP client), and a second pass of the browser and MCP checks with three independent reviews, each of whose findings a skeptic tried to refute (see Verification Results). Each item says what the plan said, what was built and why.

**Config, validation and versions (D3, D4, D6)**
- *An empty display is none, and where it sits in the JSON.* The plan added `display` to the config and did not say what an empty one is. `WidgetDisplay.Normalize()` turns a display that sets nothing (`{}`, null fields) and an empty `ramp` into none, so a widget form saved with nothing chosen writes the config it had and makes no version (`Widget form saved unchanged`). In the config JSON `display` is written before `tooltip`, not last as it is declared; nothing reads the order, since version contents are compared as `jsonb`, and a config without a display serializes byte for byte as it did (`WidgetDisplayTests`).
- *The message for a repeated empty value.* D4 words it "{column} already keeps a color for {value}.", which for an empty value would end "for .". The API says "… for an empty text." (`DashboardValidation.Colors`).
- *A NUL character in a kept value is refused.* D4 refused a value only when it is null or too long. One with a NUL character passed, then failed with a 500 over REST and `mcp.failed` over MCP, because `jsonb` cannot hold it (found by the second round of reviews). It is now refused with an error on `valueColors[i].value`: 400 over REST, `mcp.validation` over MCP. Any other control character is a value as it is.
- *`DashboardVersions.ReadContent` and `VersionContent` are public.* D6 changed them and left them `internal`. The unit tests of the content (`DashboardVersionsTests`) read a stored content through them, and the unit test project cannot see the API's internals. `VersionContentWidget` went public with them.
- *A new chart type alone keeps the old `display`.* The plan did not say what a `PATCH` that changes only the chart type does with a widget's color or scale. The stored config is validated against the new type, so a widget with a color or a scale is refused on `config.display.color` or `config.display.ramp` when the new type does not take it. The page's `savedConfig` drops the fields a kind does not take; an MCP client has to send a config without them, which the description of `update_widget` and `docs/mcp.md` say.
- *The Spanish label of `colorsChanged`.* D6 quoted "Colores cambiados". The history says "Se cambiaron los colores", as it says "Se cambiaron los filtros".
- *The dbUp README.* The plan did not touch it. Its rule about scripts that rewrite `dashboard_versions.content` now also names a dashboard's palette and colors kept by value and the check on `palette`, since a version holds them.

**A pie's domain (D8)**
- *The domain ranks every group.* The plan read the series' domain statement with `ReadDomainAsync` and `SeriesDomain.Fold(values, hasNone, 8)`, the missing group last, and the first version of this change did so. A pie ranks differently: the page has always drawn the missing group as one slice among the others, by size, and folded it like any other. Built: `IndicatorQueryBuilder.PieDomain` ranks every group with a total above zero, the missing one in its place, by total and then by the group column's own order. `SeriesDomain.BySize(groups, 8)`, whose signature changed from `(kept, hasNone, hasOther)`, keeps all the groups when there are at most 8, and otherwise the first 7 and "Other". A small missing group therefore folds into "Other", and a pie with no dashboard filter keeps the colors it had.
- *The pie's statement is no longer the series' statement.* `SeriesQueryBuilder.Domain` remains the series' statement (the non-missing values by total, then one row for the missing value); the pie has its own, and `ReadDomainAsync` now answers the values in the statement's order with null for the missing one, which the series' reading splits again.
- *`HAVING` moved into the SQL.* A group with no share (a total at zero, negative, NaN or infinite) was dropped in C# as the rows were read, after the statement's cap of 9. The statement now leaves it out (`HAVING total > 0 AND total < 'Infinity'`), so the cap counts only groups with a share and such a group cannot push a slice into "Other".
- *The page ranks the same way without a domain.* Its fallback, for an API that sends none, ranks the filtered rows by size, the missing group included, and folds past the eighth color (7 and "Other"), as it did before palettes. With a domain, a group outside it joins "Other": the groups folded in the widget's own rows, and a group that has no share there but one under a dashboard filter.

**Colors in the page (D7, D9)**
- *`chart-colors.ts` has more than D7's four members.* It also exports `defaultColors`, `colorsOf` (a dashboard or a version without palette fields, or with a palette the page has no tokens for, reads as Tamiza with no color kept), `defaultScale`, `paletteColor`, `rampGradient` (the swatch of a scale in the pickers), `SeriesKey`, and `kept(column, value)` on the colors of a widget: a bar or column chart with no series needs "the color kept for this value, or nothing" to draw every other mark in `accent`, which `forValues`, handing out the free colors, cannot say.
- *The chart components take their colors as a required input.* D7 said `widget-view` passes `chartColors(…)` to the chart. `axis-chart`, `heatmap-chart`, `map-widget`, `pie-widget` and `pyramid-chart` declare `colors` as a required input, so that no chart is drawn in colors nobody chose. `widget-grid` and `widget-view` default to Tamiza for a caller without a dashboard, and `script-editor/indicator-view.ts` gives its preview chart the default palette's colors.
- *`pivot()` returns the groups.* `chart-data.ts` `pivot()` also returns the group value of each category (null for the missing one), because a bar's category is its label and a color kept by value is looked up by the group value. A histogram has none: a range is no group value.
- *The value field of the Colors form is a native combobox.* D9 said "a combobox fed by `GET …/values?column=&search=&indicator=`". Built: an `<input list>` with a `<datalist>`, fed by `GET …/values` with the column and the search. The `indicator` parameter is not used: the form is not offered while a version is viewed, so the dashboard's own widgets give the tables. The column's most frequent values are read once, when the field is first focused, then only the last search once typing stops, and an answer for a column the row no longer has is dropped. The form refuses to send a row with an empty value ("Pick or type a value.") and saves as it is an entry that was saved with the empty text, since an empty text is a group value (D4).
- *Keyboard and screen reader details of the two forms.* Found by the browser passes and by the reviews:
  - the Colors form opens with focus on the palette in use (`cdkFocusInitial` on the checked radio), and the palettes are one tab stop (a roving `tabindex`): the focus trap wraps to the first tabbable element in the DOM order and knows nothing of radio groups, so with every palette tabbable a wrap landed on the first one, unchecked;
  - each entry's column and value are named with the entry ("Column, entry 2 of 3"), since "Column" and "Value" alone read the same in every entry;
  - the scale caption of a palette card sits above its gradient, so a long caption (in Spanish) no longer leaves the gradient no width;
  - in the widget form the scale cards pack their rows from the top, so their gradients line up;
  - **Add** starts a new entry on a column the form offers that still has a color left, in the first color the column does not use, and not on a column no table offers or one that has used all eight;
  - choosing color 1 on a widget saved without a color leaves it without one, so trying a color and coming back saves what was saved.

**Tokens and checks (D1, D2)**
- *The validator's ordinal gates are not all met, and the spec's floors for scales are relaxed to match.* The plan validated each scale with `--ordinal`. The light end of all seven scales is 1.30 to 1.35:1 on white against the validator's floor of 2:1, and the dark ladder of `blue` has two steps of 0.046 and 0.049 against its 0.06; that `blue` is the one that has shipped all along, which the plan keeps unchanged. The design binds, so step 1 was not darkened and `blue` was not re-stepped. `chart-tokens.spec.ts` asks step 1 to be a tint (1.25:1 on white, 2:1 on the dark surface) and the steps to be 0.06 apart (`blue`: 0.04). See "Where the validator and the design disagree".
- *The color-vision check moved into the spec, and two palettes were repaired.* The plan left the check with the validator. `chart-tokens.spec.ts` simulates protan and deutan as the validator does (Machado, severity 1.0), gates at 8, the validator's target and not its floor of 6, and cross-checks its numbers against the printed ones within 0.1. Two repairs followed from the reviews: slot 8 of `institutional` (a steel blue 4.9 from slot 1) became an olive green, and slot 8 of `earth` (an olive 5.1 and 2.8 from slot 1 under protan and deutan) became a teal. The spec now holds the eighth color of every palette to the neighbor floors against the first (a pie of eight wraps). See Repairs.
- *Hue names changed where they did not describe the color.* The names are read to a screen reader as "Color 3, jade", and the review measured the hue of each color (OKLCH, both themes) against what its name means. Tamiza's 3, 4 and 5 were 29° to 37° from theirs: "aqua" became "jade" ("aguamarina" to "jade"), "yellow" "amber" ("amarillo" to "ámbar") and "magenta" "pink" ("magenta" to "rosa"). `earth` 8 was renamed with its new color ("olive" to "teal", "oliva" to "verde azulado"). The other 28 are within about 25° and were kept, among them the ones at the edge (`institutional` 2 turquoise, `institutional` 6 slate, `vivid` 2 cyan, `earth` 2 indigo, and `earth` 3 ochre in the dark theme). The skeptic of that review could not show the old names wrong and judged them cosmetic.
- *The bundle budget.* The plan did not touch `angular.json`. The warning budget of the initial bundle goes from 650 kB to 675 kB, as it went from 500 kB to 650 kB in the commit "projects": the initial total grew from 649.71 kB to 660.25 kB, of which `styles.css` is 9.01 kB (68.30 to 77.31 kB, the palette and scale tokens and the labels on the scale steps) and the shared JavaScript 1.5 kB. The error budget stays at 1 MB, and the tokens were not shrunk.

**MCP (D10) and tests**
- *The input schema carries descriptions and ranges.* `WidgetDisplay` and `ValueColorInput` have `[Description]` and `[Range]` attributes, which the MCP tools' schema shows and the REST OpenAPI document shows too. They validate nothing: `IndicatorValidation` and `DashboardValidation.Colors` are the rule, and the catalog test keeps the attributes with `Palettes`. The palette and scale names are in the descriptions and not in an enum, because the fields are nullable and an enum without null would refuse the null that the server accepts for "not set". The tool descriptions also say that names are matched exactly, in lower case, and that a value is matched as exact text.
- *`update_dashboard` with only a palette, or only colors, keeps the other.* D10 described `valueColors` as the whole list. The REST endpoint takes both together, so a call that names one reads the other from the dashboard and sends both.
- *`get_widget_data` answers `dashboards.notFound` with its status.* The tools that name a dashboard answer the page's code with the HTTP `status` 404, as `docs/mcp.md` says every such refusal does; `get_widget_data` answered it without (found by the docs review).
- *A guard against the page's copy of the catalog drifting.* The plan's test for `Palettes` compared it with the CHECK of 0019. `PalettesTests` also reads three files of the page (`indicators.service.ts`, `chart-colors.ts` and `dashboard-colors-dialog.ts`) and fails when the palette ids, the scale ids, the default scale of each palette or the limits (8 colors, 64 kept by value, 500 characters) differ from the API's, since the page keeps its own copy of them.

**Left as is.** What the reviews and the verification passes found and was not changed, because it is cosmetic, older than this change or by design. Nothing was compared with a build from before the change, which is not available, so "older than this change" rests on the code that causes it.
- The pie's legend and tile layout: at the default tile the legend names wrap inside words ("Comm erce") and the pie is drawn over the legend's value text, and an 8-slice pie in a 6×5 tile prints as a 5 px dot because the legend takes the tile's height. The pie's template is unchanged since `b7c9727`; this change touches its script.
- The print defects: in portrait on A4 and Letter the right border of the right-hand column is clipped (landscape is unaffected), and legend labels break inside words. Neither is about color, and this change adds tokens and one input to the grid and no print rule.
- The filter dialog keeps the label of the first column when a filter row's column is changed (`dashboard-filters-dialog.ts` last changed in an earlier change), and the Spanish version banner ends its time with a double period ("9:36 p. m.. Es de solo lectura."), from `add-dashboard-versions`.
- An unknown key in an MCP argument is ignored: `display: { colour: 3 }` makes a successful call that sets nothing, and an extra key in a `valueColors` entry is dropped. `docs/mcp.md` does not say that unknown keys are refused. The second MCP round also noted, and nothing was changed: a fractional color (3.5) is refused on `arguments` with a .NET type name in the message, an empty `ramp` is read as not set, the string `"3"` is read as the number, and a well-formed column that no table publishes is kept (an entry is never checked against the tables, D4).
- The geometry column is offered for a kept color: the list is the filters dialog's text columns, and a kept value for a WKT column colors nothing useful.
- **Add** in the Colors form, when every column offered has used all eight colors, starts a row on the column before it (or the first column offered) in color 1, and the API says "{column} already uses color 1." on save. Reaching it takes 8 entries in each text column the dashboard offers.
- The first step of each dark scale is 2.14 to 2.41:1 against the dark surface, so the lowest class of a map looks faint there. The design asks of the first step of a scale a tint (at least 2:1 on the dark surface, which all seven meet), not the 3:1 that a palette slot is warned about, and the app has no dark theme switch yet.
- The row error of a repeated color is the API's English text inside the Spanish form, as the app does for every text the API writes (`lang="en"`).

## Verification Results

Palettes and sequential scales (tasks 7.1 to 7.3). The values of `institutional`, `earth` and `vivid` and of the six new scales were designed by five independent runs of the dataviz method, each against the same gates; every command below was then run again on the values as they went into `styles.css` and recomputed every number with its own OKLab code (the helpers of `chart-tokens.spec.ts`). **Two hex values were changed after this validation, each in the fix pass that followed a review: slot 8 of `institutional` (first review) and slot 8 of `earth` (second round of reviews); see Repairs. Every other palette color and every scale shipped as it was designed, and no palette or scale was dropped from the catalog.** The `institutional` and `earth` commands and numbers below are those of the values that ship; the `earth` ones were run again after its repair, on the values read from `styles.css`.

Commands run in the development sandbox (Node 22) with the validator of the dataviz skill, `scripts/validate_palette.js`, written below as `validate_palette.js`. The chart surface is `#ffffff` in the light theme and `#101828` (Flowbite `neutral-primary-soft`, gray-900) in the dark theme, the surfaces of `chart-tokens.spec.ts`. A palette is validated as a categorical palette on adjacent pairs, which is what a stacked chart, a legend and a bar chart with a series show side by side; a scale is validated as an ordinal ramp (`--ordinal`).

### The palettes

The gates, all hard: OKLab L inside the band (0.43 to 0.77 light, 0.48 to 0.67 dark), OKLab chroma at least 0.10, adjacent CVD ΔE at least 8 (protan and deutan; tritan is reported), adjacent normal-vision ΔE at least 15. Contrast against the surface below 3:1 is a WARN, accepted with the relief Tamiza already has: legends for two or more series, tooltips and "Show as table".

| Palette | Theme | L range | Lowest chroma | Worst adjacent CVD ΔE | Worst adjacent normal ΔE | Lowest contrast | Slots under 3:1 |
|---|---|---|---|---|---|---|---|
| `tamiza` | light | 0.527 to 0.764 | 0.141 | 9.1 (tritan 5.8) | 19.6 | 2.17:1 | 3 (2.82:1), 4 (2.17:1), 5 (2.69:1) |
| `tamiza` | dark | 0.529 to 0.670 | 0.128 | 8.4 (tritan 8.7) | 19.3 | 3.59:1 | none |
| `institutional` | light | 0.446 to 0.745 | 0.104 | 15.5 (tritan 20.6) | 24.1 | 2.36:1 | 2 (2.67:1), 4 (2.49:1), 5 (2.70:1), 7 (2.36:1) |
| `institutional` | dark | 0.500 to 0.662 | 0.113 | 11.0 (tritan 13.9) | 16.5 | 3.07:1 | none |
| `earth` | light | 0.441 to 0.660 | 0.104 | 12.8 (tritan 7.3) | 19.2 | 3.11:1 | none |
| `earth` | dark | 0.513 to 0.667 | 0.104 | 8.6 (tritan 9.3) | 20.9 | 3.05:1 | none |
| `vivid` | light | 0.529 to 0.761 | 0.137 | 17.4 (tritan 7.2) | 24.3 | 2.05:1 | 2 (2.06:1), 3 (2.05:1), 8 (2.48:1) |
| `vivid` | dark | 0.540 to 0.665 | 0.118 | 13.1 (tritan 9.1) | 19.3 | 3.11:1 | none |

The numbers in the table are recomputed from the hex values (for `earth`, after its repair); the validator's lines below give the same worst pairs. Each command is followed by what the validator printed, with the PASS, WARN and FAIL lines only.

**`tamiza`** (the colors the charts had before palettes, unchanged; its dark set was already in `styles.css`)

```
$ node validate_palette.js "#2a78d6,#eb6834,#1baf7a,#eda100,#e87ba4,#008300,#6250d6,#e34948" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 8 inside L 0.43–0.77
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #eda100↔#1baf7a ΔE 9.1 (protan) · tritan 5.8
  [PASS] Normal-vision floor    worst adjacent #e87ba4↔#eda100 ΔE 19.6 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#1baf7a",2.82],["#eda100",2.17],["#e87ba4",2.69]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3987e5,#d95926,#199e70,#c98500,#d55181,#008300,#9085e9,#e66767" --mode dark --surface "#101828"
  [PASS] Lightness band         all 8 inside L 0.48–0.67
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #c98500↔#199e70 ΔE 8.4 (protan) · tritan 8.7
  [PASS] Normal-vision floor    worst adjacent #d55181↔#c98500 ΔE 19.3 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

**`institutional`**

```
$ node validate_palette.js "#1f5398,#18aec2,#893747,#52ade3,#be9922,#4a5e9f,#e5957b,#657d19" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 8 inside L 0.43–0.77
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #657d19↔#e5957b ΔE 15.5 (protan) · tritan 20.6
  [PASS] Normal-vision floor    worst adjacent #be9922↔#52ade3 ΔE 24.1 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#18aec2",2.67],["#52ade3",2.49],["#be9922",2.7],["#e5957b",2.36]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3f6cbb,#00a5b6,#a94d58,#26a0ca,#a98900,#6473bd,#cf785c,#516f0a" --mode dark --surface "#101828"
  [PASS] Lightness band         all 8 inside L 0.48–0.67
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #516f0a↔#cf785c ΔE 11.0 (protan) · tritan 13.9
  [PASS] Normal-vision floor    worst adjacent #00a5b6↔#3f6cbb ΔE 16.5 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

**`earth`**

```
$ node validate_palette.js "#af4a1b,#345895,#b88a01,#82344e,#669929,#7e4701,#934791,#09a3a0" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 8 inside L 0.43–0.77
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #09a3a0↔#934791 ΔE 12.8 (deutan) · tritan 7.3
  [PASS] Normal-vision floor    worst adjacent #934791↔#7e4701 ΔE 19.2 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b34027,#506cad,#988904,#984e6b,#65a84b,#8e5a01,#a860b0,#2da9a0" --mode dark --surface "#101828"
  [PASS] Lightness band         all 8 inside L 0.48–0.67
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #2da9a0↔#a860b0 ΔE 8.6 (deutan) · tritan 9.3
  [PASS] Normal-vision floor    worst adjacent #8e5a01↔#65a84b ΔE 20.9 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)

**`vivid`**

```
$ node validate_palette.js "#9d32e5,#00c5ec,#95c500,#f22dbd,#009d1f,#5485ff,#c4134d,#ff8210" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 8 inside L 0.43–0.77
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #009d1f↔#f22dbd ΔE 17.4 (deutan) · tritan 7.2
  [PASS] Normal-vision floor    worst adjacent #95c500↔#00c5ec ΔE 24.3 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#00c5ec",2.06],["#95c500",2.05],["#ff8210",2.48]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b054ff,#00a5c1,#7ca400,#c500a7,#009f20,#485edf,#cc004f,#df7100" --mode dark --surface "#101828"
  [PASS] Lightness band         all 8 inside L 0.48–0.67
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #00a5c1↔#b054ff ΔE 13.1 (deutan) · tritan 9.1
  [PASS] Normal-vision floor    worst adjacent #df7100↔#cc004f ΔE 19.3 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

Contrast, in the light theme only, is what the WARNs name: `institutional` slots 2 turquoise, 4 sky blue, 5 mustard and 7 salmon (2.67, 2.49, 2.70, 2.36:1) and `vivid` slots 2 cyan, 3 lime and 8 orange (2.06, 2.05, 2.48:1), like Tamiza's jade, amber and pink (2.82, 2.17, 2.69:1). `earth` has no WARN in either theme (lowest 3.11:1 light, 3.05:1 dark), and no palette has one in the dark theme. Where a label is drawn on a fill of `institutional` slot 2, 4, 5 or 7 in the light theme, near-black reads better than white; this matters for the data labels of `add-widget-display-options`.

**Beyond adjacent pairs (information, not a gate).** Forms that put marks side by side in any order (scatter, bubble, small multiples) need every pair apart, which no eight colors can do (the reference palette has the same limit). The first three slots of every palette pass `--pairs all` in both themes, and `vivid` passes with its first four. The first four slots of `institutional` (turquoise and sky blue are 6.2 apart in the light theme and 3.9 in the dark one) and of `earth` (terracotta and wine, 13.3 and 9.6) do not, so a form that needs every pair apart should carry at most three series in those palettes:

```
$ node validate_palette.js "#2a78d6,#eb6834,#1baf7a" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 3 inside L 0.43–0.77
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #1baf7a↔#eb6834 ΔE 9.2 (deutan) · tritan 9.6
  [PASS] Normal-vision floor    worst all-pairs #1baf7a↔#2a78d6 ΔE 24.0 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#1baf7a",2.82]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3987e5,#d95926,#199e70" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 3 inside L 0.48–0.67
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #199e70↔#d95926 ΔE 9.4 (deutan) · tritan 4.0
  [PASS] Normal-vision floor    worst all-pairs #199e70↔#3987e5 ΔE 20.9 (normal)
  [PASS] Contrast vs surface    all 3 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#1f5398,#18aec2,#893747" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 3 inside L 0.43–0.77
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #893747↔#1f5398 ΔE 13.5 (protan) · tritan 21.7
  [PASS] Normal-vision floor    worst all-pairs #893747↔#1f5398 ΔE 20.0 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#18aec2",2.67]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3f6cbb,#00a5b6,#a94d58" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 3 inside L 0.48–0.67
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #a94d58↔#00a5b6 ΔE 14.6 (deutan) · tritan 13.9
  [PASS] Normal-vision floor    worst all-pairs #00a5b6↔#3f6cbb ΔE 16.5 (normal)
  [PASS] Contrast vs surface    all 3 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#af4a1b,#345895,#b88a01" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 3 inside L 0.43–0.77
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #b88a01↔#af4a1b ΔE 12.6 (deutan) · tritan 14.8
  [PASS] Normal-vision floor    worst all-pairs #b88a01↔#af4a1b ΔE 16.1 (normal)
  [PASS] Contrast vs surface    all 3 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b34027,#506cad,#988904" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 3 inside L 0.48–0.67
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #988904↔#b34027 ΔE 9.4 (deutan) · tritan 16.2
  [PASS] Normal-vision floor    worst all-pairs #988904↔#b34027 ΔE 18.6 (normal)
  [PASS] Contrast vs surface    all 3 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#9d32e5,#00c5ec,#95c500" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 3 inside L 0.43–0.77
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #00c5ec↔#9d32e5 ΔE 22.2 (deutan) · tritan 10.4
  [PASS] Normal-vision floor    worst all-pairs #95c500↔#00c5ec ΔE 24.3 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#00c5ec",2.06],["#95c500",2.05]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b054ff,#00a5c1,#7ca400" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 3 inside L 0.48–0.67
  [PASS] Chroma floor           all 3 >= 0.1
  [PASS] CVD separation         worst all-pairs #00a5c1↔#b054ff ΔE 13.1 (deutan) · tritan 9.1
  [PASS] Normal-vision floor    worst all-pairs #7ca400↔#00a5c1 ΔE 20.5 (normal)
  [PASS] Contrast vs surface    all 3 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#9d32e5,#00c5ec,#95c500,#f22dbd" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 4 inside L 0.43–0.77
  [PASS] Chroma floor           all 4 >= 0.1
  [PASS] CVD separation         worst all-pairs #f22dbd↔#9d32e5 ΔE 8.1 (protan) · tritan 10.4
  [PASS] Normal-vision floor    worst all-pairs #f22dbd↔#9d32e5 ΔE 18.2 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#00c5ec",2.06],["#95c500",2.05]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b054ff,#00a5c1,#7ca400,#c500a7" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 4 inside L 0.48–0.67
  [PASS] Chroma floor           all 4 >= 0.1
  [PASS] CVD separation         worst all-pairs #c500a7↔#00a5c1 ΔE 9.4 (deutan) · tritan 9.1
  [PASS] Normal-vision floor    worst all-pairs #c500a7↔#b054ff ΔE 15.7 (normal)
  [PASS] Contrast vs surface    all 4 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#1f5398,#18aec2,#893747,#52ade3" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 4 inside L 0.43–0.77
  [PASS] Chroma floor           all 4 >= 0.1
  [FAIL] CVD separation         worst all-pairs #52ade3↔#18aec2 ΔE 4.6 (deutan) · tritan 1.8
  [FAIL] Normal-vision floor    worst all-pairs #52ade3↔#18aec2 ΔE 6.2 (normal) — below 15, hard to tell apart even with full color vision
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#18aec2",2.67],["#52ade3",2.49]]
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#3f6cbb,#00a5b6,#a94d58,#26a0ca" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 4 inside L 0.48–0.67
  [PASS] Chroma floor           all 4 >= 0.1
  [FAIL] CVD separation         worst all-pairs #26a0ca↔#00a5b6 ΔE 3.4 (deutan) · tritan 0.8
  [FAIL] Normal-vision floor    worst all-pairs #26a0ca↔#00a5b6 ΔE 3.9 (normal) — below 15, hard to tell apart even with full color vision
  [PASS] Contrast vs surface    all 4 >= 3:1
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#af4a1b,#345895,#b88a01,#82344e" --mode light --surface "#ffffff" --pairs all
  [PASS] Lightness band         all 4 inside L 0.43–0.77
  [PASS] Chroma floor           all 4 >= 0.1
  [PASS] CVD separation         worst all-pairs #82344e↔#345895 ΔE 12.5 (protan) · tritan 11.0
  [FAIL] Normal-vision floor    worst all-pairs #82344e↔#af4a1b ΔE 13.3 (normal) — below 15, hard to tell apart even with full color vision
  [PASS] Contrast vs surface    all 4 >= 3:1
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#b34027,#506cad,#988904,#984e6b" --mode dark --surface "#101828" --pairs all
  [PASS] Lightness band         all 4 inside L 0.48–0.67
  [PASS] Chroma floor           all 4 >= 0.1
  [PASS] CVD separation         worst all-pairs #988904↔#b34027 ΔE 9.4 (deutan) · tritan 8.2
  [FAIL] Normal-vision floor    worst all-pairs #984e6b↔#b34027 ΔE 9.6 (normal) — below 15, hard to tell apart even with full color vision
  [PASS] Contrast vs surface    all 4 >= 3:1
  → FAILED — fix the marked checks   (exit 1)
```

**Against the neutral of "Other"** (`#898781`, the same in every palette): the lowest normal-vision ΔE (OKLab ×100) of any palette color is 14.5 (tamiza), 13.7 (institutional), 11.4 (earth) and 19.1 (vivid) in the light theme, and 12.6, 11.8, 11.5 and 13.0 in the dark one. Under protan and deutan (the smaller of the two) the lowest is 5.1 (tamiza), 9.0 (institutional), 4.5 (earth) and 8.0 (vivid) in the light theme, and 1.1, 5.2, 5.0 and 8.6 in the dark one. The closest color of any palette to "Other" is `earth`'s teal, in both themes, for full-color readers (11.4 and 11.5), and the weakest new color under protan and deutan in both (4.5 and 5.0); Tamiza's own colors are closer still in the dark theme (1.1). `chart-tokens.spec.ts` keeps every palette color at least 10 from "Other" for full-color readers.

**Against Tamiza** (ΔE ×100 to the nearest Tamiza color of the same theme): the first four colors of each new palette are at least 10.8 (institutional), 11.1 (earth) and 11.2 (vivid) away in the light theme and at least 8.8, 8.2 and 11.1 in the dark one, so none is a near-duplicate of a Tamiza color. Over all eight, the nearest are 7.9 (institutional, slot 8, olive green, beside Tamiza's green), 7.5 (earth, slot 8, teal, beside Tamiza's jade) and 7.5 (vivid, slot 8, orange, in the crowded warm zone) in the light theme and 5.5, 7.1 and 5.7 in the dark one (institutional slot 5, mustard; earth slot 8, teal, beside jade again; and vivid slot 8). These are looked at, not gated. A dashboard has one palette, so a Tamiza color and a color of another palette are never drawn together.

### The sequential scales

Each scale has seven steps and the label on each step. The light ladder is the lightness ladder of `blue` (OKLab L 0.905 to 0.338 in steps of 0.092 to 0.096) with one hue per scale (red leans rosier at its pale end so that it keeps off orange; gray is a cool tint) and chroma set per step, fitted into sRGB. The dark ladder is its own, from L 0.43 or 0.45 to 0.905, not an inversion of the light one. The scales are validated with `--ordinal`, whose checks are: lightness monotone, adjacent ΔL at least 0.06, the lightest step at least 2:1 against the surface, and one hue within 40°.

| Scale | Theme | L first to last | Smallest step | Hue spread | Chroma | Lowest label contrast | Labels | Step 1 on the surface |
|---|---|---|---|---|---|---|---|---|
| `blue` | light | 0.905 to 0.338 | 0.093 | 4.1° | 0.041 to 0.161 | 5.39:1 | BBBBWWW | 1.32:1 |
| `blue` | dark | 0.433 to 0.905 | 0.046 | 3.7° | 0.041 to 0.161 | 5.39:1 | WWBBBBB | 2.19:1 |
| `teal` | light | 0.904 to 0.337 | 0.092 | 1.6° | 0.055 to 0.106 | 5.11:1 | BBBBWWW | 1.30:1 |
| `teal` | dark | 0.433 to 0.906 | 0.075 | 1.0° | 0.068 to 0.108 | 5.25:1 | WWBBBBB | 2.30:1 |
| `green` | light | 0.904 to 0.338 | 0.093 | 0.4° | 0.080 to 0.164 | 5.02:1 | BBBBWWW | 1.30:1 |
| `green` | dark | 0.432 to 0.904 | 0.073 | 0.2° | 0.085 to 0.185 | 5.31:1 | WWBBBBB | 2.33:1 |
| `orange` | light | 0.904 to 0.340 | 0.093 | 1.6° | 0.052 to 0.152 | 5.19:1 | BBBBWWW | 1.34:1 |
| `orange` | dark | 0.433 to 0.906 | 0.075 | 0.5° | 0.050 to 0.160 | 4.75:1 | WWBBBBB | 2.14:1 |
| `red` | light | 0.905 to 0.338 | 0.093 | 12.5° | 0.046 to 0.189 | 4.97:1 | BBBBWWW | 1.35:1 |
| `red` | dark | 0.452 to 0.905 | 0.068 | 12.5° | 0.046 to 0.193 | 4.93:1 | WWBBBBB | 2.23:1 |
| `purple` | light | 0.905 to 0.338 | 0.093 | 9.7° | 0.052 to 0.204 | 5.00:1 | BBBBWWW | 1.35:1 |
| `purple` | dark | 0.452 to 0.905 | 0.068 | 9.2° | 0.048 to 0.197 | 4.97:1 | WWBBBBB | 2.21:1 |
| `gray` | light | 0.905 to 0.338 | 0.093 | 12.8° | 0.005 to 0.017 | 5.33:1 | BBBBWWW | 1.33:1 |
| `gray` | dark | 0.452 to 0.905 | 0.068 | 14.4° | 0.005 to 0.016 | 5.37:1 | WWBBBBB | 2.41:1 |

Labels: `B` is near-black `#0b0b0b` and `W` is white, per step 1 to 7; each is the better of the two on its fill, and the pattern is the same on every scale, light `BBBBWWW` and dark `WWBBBBB`, so the existing "flips the label colors" expectation holds for all seven. `gray` is a near-neutral (chroma 0.005 to 0.017), whose hue angle is rounding noise; the one-hue check does not apply to it, and it is held below a chroma of 0.02 instead.

**The validator's ordinal run, as printed.** Light theme first, then dark, for each scale:

```
$ node validate_palette.js "#cde2fb,#9ec5f4,#6da7ec,#3987e5,#256abf,#184f95,#0d366b" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #cde2fb at 1.32:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 4°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#184f95,#256abf,#3987e5,#5598e7,#6da7ec,#9ec5f4,#cde2fb" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [FAIL] Adjacent ΔL            steps too close: [["#3987e5","#5598e7",0.049],["#5598e7","#6da7ec",0.046]]
  [PASS] Light-end contrast     #184f95 at 2.19:1 vs surface
  [PASS] Single hue             hue spread 4°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#adeee9,#75d5cf,#3fb9b2,#149a95,#0a7b76,#065d59,#05403e" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #adeee9 at 1.30:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 2°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#095d59,#117772,#1b938d,#2bada7,#4bc5bf,#78dbd5,#abefea" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #095d59 at 2.30:1 vs surface
  [PASS] Single hue             hue spread 1°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#bfeebf,#92d593,#61bb67,#359f41,#1e802c,#12611e,#074411" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #bfeebf at 1.30:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 0°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#12611e,#197c28,#279936,#38b447,#55cd5f,#82e186,#bdefbd" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #12611e at 2.33:1 vs surface
  [PASS] Single hue             hue spread 0°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#fcd7c0,#f2b185,#e68942,#ca6907,#a15306,#7b3e06,#572a02" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #fcd7c0 at 1.34:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 2°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#793f0d,#9a5111,#be6517,#e0791f,#fb903c,#fab688,#fbd8c1" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #793f0d at 2.14:1 vs surface
  [PASS] Single hue             hue spread 1°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#fdd4d6,#f7aaaa,#ef7d79,#e24a48,#b93432,#8f2420,#651714" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #fdd4d6 at 1.35:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 12°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#982622,#bc2e2e,#e34746,#f16c6a,#f49190,#f7b4b4,#fdd4d6" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #982622 at 2.23:1 vs surface
  [PASS] Single hue             hue spread 12°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#e9d7fb,#d0b2f4,#b68bf0,#9d61ea,#8041cc,#602fa0,#421f72" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #e9d7fb at 1.35:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 10°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#6436a6,#7d45c8,#9a62e9,#ae80f1,#c09ef0,#d4bbf3,#e8d8f9" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #6436a6 at 2.21:1 vs surface
  [PASS] Single hue             hue spread 9°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#dde0e3,#bdc2c8,#9ea4ad,#818791,#646c74,#4b515a,#32383f" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [FAIL] Light-end contrast     #dde0e3 at 1.33:1 vs surface — below 2:1 floor
  [PASS] Single hue             hue spread 13°
  → FAILED — fix the marked checks   (exit 1)

$ node validate_palette.js "#51565f,#656b74,#80878f,#959da5,#abb2b9,#c5c8cd,#dde0e3" --mode dark --surface "#101828" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #51565f at 2.41:1 vs surface
  [PASS] Single hue             hue spread 14°
  → ALL CHECKS PASS   (exit 0)
```

**Where the validator and the design disagree, and how it was settled.**

1. *Light-end contrast.* `--ordinal` wants the lightest step at 2:1 against the surface. On the light theme step 1 is 1.30:1 to 1.35:1 for every scale, and the `blue` scale that has shipped all along is 1.32:1: all seven fail this one check and pass the other three. Design D2 and the spec ask for one hue, one direction of lightness with visible steps and labels at 4.5:1, and the sequential tests of `chart-tokens.spec.ts` have always had the low end recede into the surface in both themes: the pale end is meant to recede, as the dataviz palette reference allows for a full-range sequential scale. The design binds, so step 1 was not darkened. To show that nothing else is wrong, steps 3 to 7 of each scale, the way a Likert arm uses a ramp, pass every ordinal check, with the light end at 2.38:1 to 2.67:1:

```
$ node validate_palette.js "#6da7ec,#3987e5,#256abf,#184f95,#0d366b" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #6da7ec at 2.50:1 vs surface
  [PASS] Single hue             hue spread 3°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3fb9b2,#149a95,#0a7b76,#065d59,#05403e" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #3fb9b2 at 2.39:1 vs surface
  [PASS] Single hue             hue spread 2°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#61bb67,#359f41,#1e802c,#12611e,#074411" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #61bb67 at 2.38:1 vs surface
  [PASS] Single hue             hue spread 0°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#e68942,#ca6907,#a15306,#7b3e06,#572a02" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #e68942 at 2.62:1 vs surface
  [PASS] Single hue             hue spread 1°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#ef7d79,#e24a48,#b93432,#8f2420,#651714" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #ef7d79 at 2.67:1 vs surface
  [PASS] Single hue             hue spread 4°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b68bf0,#9d61ea,#8041cc,#602fa0,#421f72" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #b68bf0 at 2.64:1 vs surface
  [PASS] Single hue             hue spread 4°
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#9ea4ad,#818791,#646c74,#4b515a,#32383f" --mode light --surface "#ffffff" --ordinal
  [PASS] Lightness monotone     steps read light→dark
  [PASS] Adjacent ΔL            all gaps >= 0.06
  [PASS] Light-end contrast     #9ea4ad at 2.51:1 vs surface
  [PASS] Single hue             hue spread 13°
  → ALL CHECKS PASS   (exit 0)
```

   On the dark theme the six new scales pass all four checks (step 1 at 2.14:1 to 2.41:1).
2. *Smallest step.* The validator's ΔL of 0.06 is stricter than the `blue` that ships: its dark ladder reuses light steps and has two gaps of 0.046 and 0.049 (`#3987e5` to `#5598e7` to `#6da7ec`). `blue` is claimed to stay unchanged, so it was not re-stepped. The six new scales keep 0.068 or more in both themes. `chart-tokens.spec.ts` asks 0.06 of the new scales and 0.04 of `blue`.
3. *Categorical checks do not apply to a ramp.* They fail a correct ramp by design (the ramp spans the lightness band and its pale steps are under the chroma floor), so they were not run on a scale.
4. *Labels.* No fill with a luminance between 0.1833 and 0.1900 reaches 4.5:1 with either label, so the designers nudged the lightness ladders to leave that window empty (dark step 3 of `red` and `purple` is L 0.620, where 0.600 sits at the crossover); no step of any scale falls in it. The lowest label contrast is 4.75:1 (`orange`, dark, step 3).

**Telling the scales apart.** At the same step, the nearest pair of scales is 3.3 apart in OKLab ×100 (red and orange at step 1 of the light theme and step 7 of the dark one, where every scale converges on the surface); the nearest pair per step is 3.3, 5.8, 7.8, 9.5, 8.2, 6.9 and 5.0 in the light theme and 7.2, 8.2, 9.4, 9.0, 9.0, 5.9 and 3.3 in the dark one. `chart-tokens.spec.ts` asks 3. Under protan and deutan, at their worst step, the closest pairs fall to 0.2 to 1.2 in the light theme (blue and purple 0.2, teal and gray 0.2, green and orange 0.5, green and red 1.1, orange and red 1.2) and 0.4 to 1.3 in the dark one (green and orange 0.4, blue and purple 0.5, teal and gray 1.3): one ladder of lightness with another hue is what a reader who cannot see the hue is left with. A scale is therefore always chosen by its name and its gradient in text (design D9), a widget draws only one scale, and its legend carries the values, so a scale never relies on hue alone.

### What `chart-tokens.spec.ts` keeps

The spec has a `Checks on every palette` block (spec "Palette checks" and "Scale checks", scenario "Checks on every palette"):

- the tokens are exactly the 4 palettes of 8 colors and the 7 scales of 7 steps and 7 labels, in both themes, and the `--tmz-series-1..8` are gone;
- every palette and scale holds the hex values above, pinned literally, so `tamiza` and `blue` equal the values validated before this change;
- per palette and theme: 8 distinct colors, each inside the band and above the chroma floor, neighbors at least 15 apart (OKLab ×100, normal vision) and at least 8 apart under protan and under deutan (the validator's simulation and its CVD target, see below), the eighth color held to the same two floors against the first (a pie of eight wraps, see Repairs), each at least 10 from "Other";
- per scale and theme: lightness in one direction with steps of at least 0.06 (`blue`: 0.04), one hue within 40° (`gray` instead stays under a chroma of 0.02, its measured maximum being 0.017), step 1 a tint (at least 1.25:1 on white, measured 1.30 to 1.35; at least 2:1 on the dark surface, measured 2.14 to 2.41), and a label of white or near-black at 4.5:1 and at least as good as the other;
- the scales are at least 3 apart at every step;
- the `--tmz-seq-n` and `--tmz-seq-ink-n` that a mark reads are the blue scale in both themes;
- for every palette and theme, the color-vision simulation gives back the worst adjacent distances the validator printed, within 0.1 (the validator prints one decimal): the smaller of protan and deutan, and tritan;
- the checks themselves fail on a missing or repeated color, a color off the band, dull, too close to its neighbor or to "Other", a neighbor that full-color readers tell apart and a reader with protan does not, an eighth color too close to the first (for full-color readers, or under protan or deutan), steps out of order, a second hue, a gray with a hue, a lost first step and a faint or wrong label.

### The color-vision check in the spec

Spec "Palette checks" asks that adjacent colors stay apart for readers with each common color-vision deficiency (protan and deutan), and that the eighth color stay as far from the first, and its scenarios fail the checks when any color breaks one. `chart-tokens.spec.ts` first left that check to the validator run recorded here, so a developer who edited a palette in `styles.css` and in the spec's pinned lists passed every check with no color-vision check at all. It now runs the check itself, the way the validator does (`validate_palette.js`: `MACHADO`, `simulate`, `deltaE`, `CVD_TARGET`):

- `simulate` and `deltaE(x, y, deficiency)` sit next to the OKLab helpers. A color goes through the Machado, Oliveira and Fernandes (2009) matrix of the deficiency at severity 1.0, on linear RGB, clamped to 0-1, then into OKLab, and the distance is ×100. The validator's thresholds are calibrated to that model, so it is copied, not swapped for another.
- For every palette and theme, `paletteProblems` fails a color that is under 8 from its neighbor under protan or under deutan, the two deficiencies the validator gates on. 8 is the validator's `CVD_TARGET` and the gate of design D2, not its floor of 6: the validator fails below 6 and accepts 6 to 8 only with a second encoding (direct labels, gaps or texture), which a palette's colors do not bring. The validator reports tritan without gating on it, and the spec does not gate on it either: its 5.8 for Tamiza in the light theme would fail an 8.
- The block that tests the checks themselves has a negative case: a dark olive green, `#4c6118`, in place of the indigo of `earth` next to terracotta `#af4a1b` is 18.0 apart with full color vision and 0.38 under protan (8.8 under deutan). The checks return exactly one problem for it, the protan one.
- Two more negative cases cover the wrap (see Repairs). The olive `#676e02` that `earth` once had as its eighth color, put in its place, is 15.5 from terracotta with full color vision and 24 from plum, its neighbor, but 5.1 under protan and 2.8 under deutan from terracotta: the checks return exactly two problems, the protan one and the deutan one. `#b74e20`, a twin of terracotta, gets the full-color problem.

**Cross-check against the validator.** The worst adjacent distance of each palette from the simulation of the spec, written at two decimals, against what the validator printed (the smaller of protan and deutan, and tritan). They agree within 0.05, the rounding of one decimal, and the spec asserts 0.1. The cross-check ran first against the recorded values of the first validation (Tamiza 9.1 / 8.4, institutional 23.8 / 14.6, earth 16.6 / 14.8, vivid 17.4 / 13.1) and passed at once, so the gate was asserted on a simulation known to match; the `institutional` and `earth` rows below also give the values before their repair. The `earth` values of the first validation are those of its first eighth color, olive.

| Palette | Theme | Protan and deutan: validator | Spec | Tritan: validator | Spec |
|---|---|---|---|---|---|
| `tamiza` | light | 9.1 | 9.12 | 5.8 | 5.83 |
| `tamiza` | dark | 8.4 | 8.36 | 8.7 | 8.65 |
| `institutional` | light | 15.5 (before the repair 23.8) | 15.46 (23.77) | 20.6 | 20.59 |
| `institutional` | dark | 11.0 (before the repair 14.6) | 11.01 (14.64) | 13.9 | 13.87 |
| `earth` | light | 12.8 (before the repair 16.6) | 12.80 (16.62) | 7.3 | 7.31 |
| `earth` | dark | 8.6 (before the repair 14.8) | 8.62 (14.81) | 9.3 | 9.31 |
| `vivid` | light | 17.4 | 17.35 | 7.2 | 7.16 |
| `vivid` | dark | 13.1 | 13.09 | 9.1 | 9.14 |

The simulation is held to the validator by that test: changing any one coefficient of the deutan matrix by 0.1, up or down, moves the worst distance of 2 to 5 of the eight palette-and-theme rows by more than the 0.1 the test allows, and a CVD target of 0 fails the negative case.

### Repairs

Two, found after the verification above, each in the fix pass that followed a review: the eighth color of `institutional` (first review) and the eighth color of `earth` (second round of reviews). They are one defect: a pie of exactly eight values puts slice 8 beside slice 1, and no gate looked at that pair.

#### The eighth color of `institutional`

*The finding.* The palette is blue-heavy and its slot 8, steel blue (`#076392` light, `#357cb1` dark), sat next to slot 1, navy. Only adjacent pairs are gated and slots 7 and 8 were 24 apart, so every gate passed; but a pie of exactly eight values puts slice 8 beside slice 1 at 12 o'clock, and a line chart with several series needs every pair apart. The validator, run on that pair alone, fails it:

```
$ node validate_palette.js "#076392,#1f5398" --mode light --surface "#ffffff"
  [FAIL] CVD separation         worst adjacent #1f5398↔#076392 ΔE 4.8 (deutan) · tritan 3.6
  [FAIL] Normal-vision floor    worst adjacent #1f5398↔#076392 ΔE 4.9 (normal) — below 15, hard to tell apart even with full color vision

$ node validate_palette.js "#357cb1,#3f6cbb" --mode dark --surface "#101828"
  [FAIL] CVD separation         worst adjacent #3f6cbb↔#357cb1 ΔE 4.8 (deutan) · tritan 3.2
  [FAIL] Normal-vision floor    worst adjacent #3f6cbb↔#357cb1 ΔE 5.1 (normal) — below 15, hard to tell apart even with full color vision
```

Over all pairs, the closest were slots 1 and 8 (4.9 in the light theme) and slots 2 and 4 (3.9 in the dark one), and under protan slots 6 and 8 were 2.3 apart in the light theme (1.8 under deutan in the dark one).

*The search.* Slot 8 alone was replaced; slot 6 did not have to change. Every OKLCH color with chroma 0.10 to 0.13 was tried (hue in steps of 1°, chroma of 0.005 and lightness of 0.004, across each theme's band), and kept only when, in its theme, it passed the hard gates against slot 7, its one neighbor (lightness band, chroma floor, CVD ΔE at least 8 for protan and deutan, normal-vision ΔE at least 15), had contrast of 3:1 or more on the surface (so the dark theme keeps no WARN), stayed at least 10 from "Other" and was at least 15 from slot 1. The best a family of hues could do for its distance to the nearest of slots 1 to 7 (normal vision, OKLab ×100), light and dark: greens 21.9 and 19.9, yellow-olives 18.1 and 14.6, purples 17.9 and 13.4, red-pinks 17.7 and 11.4, teal-cyans 16.7 and 6.8, orange-browns 15.2 and 11.7, blues 12.2 and 2.4. Only the greens stay 15 or more from every other slot in the dark theme. Within them, hue 123° to 126° gave the best color-vision margin against slot 3, burgundy, the slot a green is hardest to tell apart from for a reader with protan or deutan (7.0 in the dark theme at that hue with 3.07:1 on the surface, 5.8 to 6.4 at hue 140° to 148°).

*The choice.* An olive green, hue name `colors.hues.institutional.8` ("olive green" / "verde oliva", it was "steel blue" / "azul acero"): `#657d19` in the light theme (OKLCH 0.552, 0.124, 123°) and `#516f0a` in the dark theme (0.500, 0.124, 126°). Against slot 1: 25.3 and 24.0 for full-color readers, 24.6 and 23.2 under protan and deutan. Against every other slot, 16.4 and 16.0 at the nearest for full-color readers (slot 5, mustard), 11.8 and 7.0 at the nearest under protan and deutan (slot 3 and slot 5 in the light theme, slot 3 in the dark one); against slot 6, 22.8 and 24.2. Contrast on the surface is 4.67:1 and 3.07:1, so no new WARN, and the distance to the neutral of "Other" is 13.7 and 17.0. The validator, on the whole palette and on slots 8 and 1 alone, all exit 0:

```
$ node validate_palette.js "#1f5398,#18aec2,#893747,#52ade3,#be9922,#4a5e9f,#e5957b,#657d19" --mode light --surface "#ffffff"
  [PASS] CVD separation         worst adjacent #657d19↔#e5957b ΔE 15.5 (protan) · tritan 20.6
  [PASS] Normal-vision floor    worst adjacent #be9922↔#52ade3 ΔE 24.1 (normal)
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#3f6cbb,#00a5b6,#a94d58,#26a0ca,#a98900,#6473bd,#cf785c,#516f0a" --mode dark --surface "#101828"
  [PASS] CVD separation         worst adjacent #516f0a↔#cf785c ΔE 11.0 (protan) · tritan 13.9
  [PASS] Normal-vision floor    worst adjacent #00a5b6↔#3f6cbb ΔE 16.5 (normal)
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#657d19,#1f5398" --mode light --surface "#ffffff"
  [PASS] CVD separation         worst adjacent #1f5398↔#657d19 ΔE 24.6 (protan) · tritan 11.9
  [PASS] Normal-vision floor    worst adjacent #1f5398↔#657d19 ΔE 25.3 (normal)
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#516f0a,#3f6cbb" --mode dark --surface "#101828"
  [PASS] CVD separation         worst adjacent #3f6cbb↔#516f0a ΔE 23.2 (deutan) · tritan 8.6
  [PASS] Normal-vision floor    worst adjacent #3f6cbb↔#516f0a ΔE 24.0 (normal)
  → ALL CHECKS PASS   (exit 0)
```

The worst adjacent color-vision distance of the palette falls, from 23.8 to 15.5 in the light theme and from 14.6 to 11.0 in the dark one, because slots 7 and 8 (salmon and a green) are now neighbors that red-green deficiencies bring closer; both stay above the target of 8. The worst adjacent distance for full-color readers (24.1 and 16.5), the contrast WARNs of slots 2, 4, 5 and 7 and the `--pairs all` runs of the first three and four slots (above) are unchanged.

*What stays.* All eight slots still do not pass `--pairs all`, and this repair does not try to. The closest pairs are now slots 1 and 6 (navy and slate) in the light theme and slots 2 and 4 (turquoise and sky blue) in the dark one, pairs the repair did not touch:

```
$ node validate_palette.js "#1f5398,#18aec2,#893747,#52ade3,#be9922,#4a5e9f,#e5957b,#657d19" --mode light --surface "#ffffff" --pairs all
  [FAIL] CVD separation         worst all-pairs #4a5e9f↔#1f5398 ΔE 4.3 (protan) · tritan 1.8
  [FAIL] Normal-vision floor    worst all-pairs #4a5e9f↔#1f5398 ΔE 6.0 (normal) — below 15, hard to tell apart even with full color vision

$ node validate_palette.js "#3f6cbb,#00a5b6,#a94d58,#26a0ca,#a98900,#6473bd,#cf785c,#516f0a" --mode dark --surface "#101828" --pairs all
  [FAIL] CVD separation         worst all-pairs #6473bd↔#3f6cbb ΔE 3.0 (protan) · tritan 0.8
  [FAIL] Normal-vision floor    worst all-pairs #26a0ca↔#00a5b6 ΔE 3.9 (normal) — below 15, hard to tell apart even with full color vision
```

So the advice under "Beyond adjacent pairs" stands: a form that needs every pair apart (a line chart with several series, a scatter) should carry at most three series in `institutional`. What the repair settles is the pie of eight values, whose slice 8 and slice 1 were 4.9 apart and are now 25.3 apart.

#### The eighth color of `earth`

*The finding.* The second round of reviews put the same question to the other palettes, and `earth` failed it. The validator gates the neighbors in the order (1-2 to 7-8) and does not see 8 against 1, and neither did the spec. The eighth color of `earth` was an olive (`#676e02` light, `#677401` dark). Against slot 1, terracotta, it was far enough for full-color readers and confusable for a reader with protan or deutan (ΔE ×100):

| Theme | Pair | Normal | Protan | Deutan |
|---|---|---|---|---|
| light | `#676e02` / `#af4a1b` | 15.5 | 5.1 | 2.8 |
| dark | `#677401` / `#b34027` | 18.6 | 8.9 | 1.1 |

The wrap pairs of the other palettes pass (see "The gate").

*The search.* Slot 8 alone was replaced, and slot 1 did not move. Every OKLCH color was tried (hue in steps of 1° over the whole circle, chroma 0.10 to 0.20 in steps of 0.005, lightness in steps of 0.004 across the theme's band) and kept, in its theme, when it was inside the band, had chroma of 0.10 or more, was at least 15 from slot 7 and from slot 1 for full-color readers and at least 8 under protan and under deutan, had contrast of 3:1 or more on the surface (`earth` has no contrast WARN in either theme, and keeps none) and was at least 10 from "Other": 52,127 candidates in the light theme and 19,887 in the dark one. The distance to slots 2 to 6 was computed too, since a new color must not become a near-duplicate of moss, indigo or any other slot. What each family of hues could do (the best candidate per 10° of hue):

| Family (OKLCH hue) | Verdict |
|---|---|
| red, pink (0 to 30) | the hue of terracotta and wine, and only as a hot pink at chroma 0.14 to 0.20: not earth |
| orange, ochre, olive, moss (40 to 150) | near-duplicates of terracotta, ochre, moss or brown: any color here that clears slots 1 and 7 sits on top of ochre, moss or brown, which is why the olive could not be re-pitched |
| jade green (150 to 175) | passes, but weaker under protan and deutan against wine, onto which a dark jade collapses |
| teal (180 to 200) | passes, with the best color-vision margin of any earthy family: a teal reads as a blue-gray under protan and deutan, apart from every brown, green and red of the palette |
| azure, blue (210 to 260) | the largest margins, but at chroma 0.13 to 0.18 and a second blue next to indigo: vivid, not earth (`institutional` was already repaired for being blue-heavy) |
| violet, magenta (270 to 350) | the hue of plum and wine, and far more saturated than earth |

*The choice.* A teal, the cool counterpoint the palette lacked, neither a second blue nor a second green: `#09a3a0` in the light theme (OKLCH 0.647, 0.110, 192°) and `#2da9a0` in the dark theme (0.667, 0.105, 188°). Its hue name, `colors.hues.earth.8`, is "teal" / "verde azulado" (it was "olive" / "oliva"; "verde azulado" is the name the app already gives the teal scale in Spanish). Its numbers (ΔE ×100):

| | light `#09a3a0` | dark `#2da9a0` |
|---|---|---|
| Contrast on the surface | 3.11:1 | 6.16:1 |
| Against slot 1, the wrap: normal / protan / deutan / tritan | 27.0 / 23.9 / 16.7 / 31.5 | 28.9 / 26.2 / 17.0 / 34.2 |
| Against slot 7, its neighbor: normal / protan / deutan / tritan | 26.4 / 22.0 / 12.8 / 25.5 | 23.8 / 17.1 / 8.6 / 21.7 |
| Against slot 5, moss, the nearest in normal vision: normal / protan / deutan / tritan | 13.9 / 14.4 / 13.9 / 7.6 | 11.1 / 11.3 / 11.2 / 5.0 |
| Against slot 2, indigo: normal / protan / deutan | 21.9 / 22.3 / 20.7 | 18.5 / 18.1 / 16.2 |
| From "Other": normal / protan / deutan | 11.4 / 6.7 / 4.5 | 11.5 / 8.3 / 5.0 |
| Nearest Tamiza color of the same theme | 7.5 (Tamiza 3, jade) | 7.1 (Tamiza 3, jade) |

The validator, on the whole palette (above) and on slot 8 against slot 1 and against slot 7 alone, exits 0 every time:

```
$ node validate_palette.js "#09a3a0,#af4a1b" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 2 inside L 0.43–0.77
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #af4a1b↔#09a3a0 ΔE 16.7 (deutan) · tritan 31.5
  [PASS] Normal-vision floor    worst adjacent #af4a1b↔#09a3a0 ΔE 27.0 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#2da9a0,#b34027" --mode dark --surface "#101828"
  [PASS] Lightness band         all 2 inside L 0.48–0.67
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #b34027↔#2da9a0 ΔE 17.0 (deutan) · tritan 34.2
  [PASS] Normal-vision floor    worst adjacent #b34027↔#2da9a0 ΔE 28.9 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#934791,#09a3a0" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 2 inside L 0.43–0.77
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #09a3a0↔#934791 ΔE 12.8 (deutan) · tritan 25.5
  [PASS] Normal-vision floor    worst adjacent #09a3a0↔#934791 ΔE 26.4 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#a860b0,#2da9a0" --mode dark --surface "#101828"
  [PASS] Lightness band         all 2 inside L 0.48–0.67
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #2da9a0↔#a860b0 ΔE 8.6 (deutan) · tritan 21.7
  [PASS] Normal-vision floor    worst adjacent #2da9a0↔#a860b0 ΔE 23.8 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

*What it costs.* No gate was relaxed; these are the figures that moved.

- The worst adjacent color-vision distance of `earth` falls from 16.6 to 12.8 in the light theme and from 14.8 to 8.6 in the dark one. The pair is slots 7 and 8, plum and teal, under deutan. Dark 8.6 is 0.6 above the target of 8, and among the palettes the weakest adjacent margin after Tamiza's. A dark candidate further from plum under deutan has to move toward green and onto moss or wine: a dark jade 9.1 from plum is 6.0 from wine under protan and deutan and 11.0 from moss, and a candidate 10.0 from plum gives 9.2 from moss, 11.0 gives 8.0 and 12.0 gives 6.8. So 8.6 is the most that a teal 11 or more from moss gives. The tritan figure (7.3 light, 9.3 dark) and the worst adjacent normal-vision distance (19.2, 20.9) did not change.
- Against the neutral of "Other" the teal is the closest color of any palette in both themes: 11.4 and 11.5 for full-color readers, where `earth` was at 13.1 and 12.1, and the lowest of any palette had been 13.1 (`earth`'s ochre, light) and 11.8 (`institutional`'s salmon, dark). Under protan and deutan it is 4.5 and 5.0 from "Other", where `earth` was at 9.8 and 9.0. The tripwire in the spec (10, normal vision) holds. `tamiza` was already at 5.1 and 1.1 and `institutional` at 9.0 and 5.2 under protan and deutan, so `earth` moves into the range the other palettes already had; it is measured, not gated. A teal at this lightness cannot be much further: in the light theme no candidate of hue 186° to 196° with chroma 0.115 or more reaches 3.05:1 and the gates (the gamut ends first), and in the dark theme chroma 0.115 gives 12.1 to 12.4 from "Other" but takes 0.5 off the distance to moss and 0.2 off the margin against plum. The teal is the eighth color, and "Other" is the neutral of a value past the eighth, so a chart with nine or more values draws "Other" next to the teal.
- The nearest Tamiza color to an `earth` color is now 7.5 (light, slot 8 and Tamiza 3) and 7.1 (dark, the same pair), where it was 9.0 and 7.6; the first four slots are unchanged (11.1 and 8.2). A dashboard has one palette, so the pair is never drawn together.
- Over all 28 pairs the worst color-vision pairs are still among slots 1 to 7, exactly as before: 2-7, indigo and plum, 0.8 light and 1.2 dark; 3-5, ochre and moss, 1.0 and 5.1; 1-6, terracotta and brown, 5.1 and 2.3. The old olive made the worst pair of the dark theme (6-8, brown and olive, 9.4 normal; 1-8, 1.1 under deutan); the teal is in none of the worst pairs, its worst pair with any slot being 13.9 (normal, against moss) and 12.8 (protan and deutan, against plum) in the light theme and 11.1 and 8.6 in the dark one. In the dark theme the worst normal-vision pair of the palette moves from 9.4 to 9.5 (slots 1 and 6) and the worst color-vision pair from 1.1 to 1.2 (slots 2 and 7).

*The gate.* `paletteProblems` in `chart-tokens.spec.ts` now checks color 8 against color 1 for every palette in both themes: at least 15 for full-color readers (`neighborFloor`) and at least 8 under protan and under deutan (`cvdFloor`), and reports `color 8 (#hex): only X from color 1 (a pie of eight wraps)`, with `under protan` or `under deutan` for the others. The wrap pair of every palette (slot 8 against slot 1), ΔE ×100, normal / protan / deutan / tritan:

| Palette | Light | Dark |
|---|---|---|
| `tamiza` | 32.3 / 21.6 / 27.3 / 34.5 | 29.0 / 19.2 / 24.9 / 31.4 |
| `institutional` | 25.3 / 24.6 / 25.9 / 11.9 | 24.0 / 23.4 / 23.2 / 8.6 |
| `earth` | 27.0 / 23.9 / 16.7 / 31.5 | 28.9 / 26.2 / 17.0 / 34.2 |
| `vivid` | 38.9 / 36.0 / 39.6 / 20.8 | 33.8 / 32.8 / 33.9 / 16.7 |

The smallest of protan and deutan, over both themes, is 19.2 for `tamiza`, 23.2 for `institutional`, 16.7 for `earth` and 32.8 for `vivid`. The validator on the wrap pair of `tamiza` and `vivid` on its own (the one of `institutional` is above, the one of `earth` is in "The choice"):

```
$ node validate_palette.js "#e34948,#2a78d6" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 2 inside L 0.43–0.77
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #2a78d6↔#e34948 ΔE 21.6 (protan) · tritan 34.5
  [PASS] Normal-vision floor    worst adjacent #2a78d6↔#e34948 ΔE 32.3 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#e66767,#3987e5" --mode dark --surface "#101828"
  [PASS] Lightness band         all 2 inside L 0.48–0.67
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #3987e5↔#e66767 ΔE 19.2 (protan) · tritan 31.4
  [PASS] Normal-vision floor    worst adjacent #3987e5↔#e66767 ΔE 29.0 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#ff8210,#9d32e5" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 2 inside L 0.43–0.77
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #9d32e5↔#ff8210 ΔE 36.0 (protan) · tritan 20.8
  [PASS] Normal-vision floor    worst adjacent #9d32e5↔#ff8210 ΔE 38.9 (normal)
  [WARN] Contrast vs surface    below 3:1 — relief required (visible labels or table view): [["#ff8210",2.48]]
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#df7100,#b054ff" --mode dark --surface "#101828"
  [PASS] Lightness band         all 2 inside L 0.48–0.67
  [PASS] Chroma floor           all 2 >= 0.1
  [PASS] CVD separation         worst adjacent #b054ff↔#df7100 ΔE 32.8 (protan) · tritan 16.7
  [PASS] Normal-vision floor    worst adjacent #b054ff↔#df7100 ΔE 33.8 (normal)
  [PASS] Contrast vs surface    all 2 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

The tests fail without the repair. With the old `earth` values restored in `styles.css`, the new test fails for `earth` in both themes (light: 5.1 under protan and 2.8 under deutan; dark: 1.1 under deutan), and so do the case that checks the validated palettes pass and the pins and the validator-printed distances (6 failures). With the wrap check removed from `paletteProblems`, the two negative cases fail. With the old hue names restored in `en.json` and `es.json`, four tests of `widget-dialog.spec.ts` and `dashboard-colors-dialog.spec.ts` fail. Each file was restored and compared identical with `cmp`.

### UI checks after the tokens (tasks 7.2 and 7.3)

Run in the same sandbox. The rows for the token specs were run in the fix passes (the color-vision check in the spec, the new slot 8 of `institutional` and of `earth`, the wrap check, the raised budget). The rows of the whole suites, the build and the safety check are the final run, on the final tree (the sources of `tamiza-api` and `tamiza-ui` are those of the commit before the one that records these numbers): task 14.4.

| Check | Result |
|---|---|
| `chart-tokens.spec.ts` | 88 tests pass (86 before the `earth` repair, which added the wrap check and its two negative cases): the Likert tests as they were, and the `Checks on every palette` block (the tokens, each of the 4 palettes and 7 scales in both themes, the color-vision simulation against the validator's numbers, the eighth color against the first, the scales together, and the checks failing on a broken color, a pair confusable under protan included) |
| `translations.spec.ts` | passes with the new `colors.palettes`, `colors.scales` and `colors.hues` keys in `en.json` and `es.json` (4 palette names, 7 scale names, 32 hue names each; hue 8 of `institutional` is "olive green" and "verde oliva") |
| `map-styles.spec.ts`, `print-styles.spec.ts` | pass: the `.tmz-map` rules are untouched and still read `--tmz-seq-n`. Together with `chart-tokens`, `translations`, `chart-widgets`, `chart-colors`, `map-widget` and `dashboard-colors-dialog`: 8 files, 422 tests pass |
| UI, whole suite (`npm test -- --watch=false`) | 102 files, 2,574 tests passed. One unhandled error, `Cannot read properties of undefined (reading 'total')` in `ProjectPerformance`, raised during `project-indicators.spec.ts`: it was there before this change (baseline: 99 files, 2,256 tests, the same error) and still makes the exit code 1 |
| `ng build` | succeeds. The initial bundle is 660.25 kB; before this change it was 649.71 kB. The 10.5 kB are `styles.css` (68.30 to 77.31 kB: the palette and scale tokens, 244 more custom properties, 3.7 kB of them the labels on the scale steps) and 1.5 kB of shared JavaScript. The warning budget of the initial bundle in `angular.json` goes from 650 kB to 675 kB, as it went from 500 kB to 650 kB in the commit "projects", and the error budget stays at 1 MB. The build prints no budget warning, only the NG8113 warning about `Card` in `ProjectReference` that was already there; the tokens were not shrunk |
| `npm run check:safety` | clean |
| API, unit tests (`dotnet test tests/Tamiza.Api.UnitTests`) | 1,733 tests: 1,730 passed and 3 failed. The 3 are the ones that fail on the baseline too (`ReportFormatTests.Dates_and_timestamps_of_tooltip_columns`, `ReportHtmlTests.Header`, `ReportHtmlTests.Text_reader`, about reports and the ICU data of the sandbox; baseline: 1,582 tests, the same 3 failing) |
| API, integration tests (`dotnet test tests/Tamiza.Api.IntegrationTests`, Testcontainers with `postgis/postgis:17-3.5`) | 1,841 tests passed, none failed, in 46 min 38 s (baseline: 1,729 passed) |

`--tmz-series-1..8` no longer exist in `styles.css`, and nothing reads them: group 10 moved `chart-scales.ts` (`seriesColor`) and `series-legend.ts` to `chart-colors.ts`, which draws a series in `var(--tmz-pal-{palette}-{n})`, and the only series token left is `--tmz-series-other`. `chart-tokens.spec.ts` checks that no `--tmz-series-<n>` is defined, in either theme.

### Browser, first round (task 14.1)

**Task 14.1, real stack (real API, PostGIS, production UI build, mock OIDC), Chromium driven by Playwright, light theme.** The dashboard "14.1 Housing" was built through the UI: New dashboard, and the widget form for all ten widgets (a stacked column of `water_source` by municipality, a pie and a pyramid by `sex`, a bar by `municipality`, a map, a heat map, a stacked column by `sex`, a stacked column by `municipality`, a pie of `activity` that folds into "Other", and a column of `water_source` with no series in color 3). Palettes were switched in the Colors dialog and every chart was looked at in a 1,280 px screenshot, and the colors it draws were read from computed styles (35 stacked segments, 24 pyramid bars, pie slices, 7 bars, 7 map shapes, 35 heat cells, every legend swatch). Under Tamiza, Institutional, Earth and Vivid every mark and swatch resolved to that palette's own `--tmz-pal-*` token (Earth 1 to 6: 175,74,27 / 52,88,149 / 184,138,1 / 130,52,78 / 102,153,41 / 126,71,1), legends matched marks, and the map and heat map took the palette's scale (blue, teal, orange, purple); Tamiza equals the colors of the baseline build. With Female 5, Male 6 and Cali 8 kept (Tamiza) the pie, the pyramid sides, the stacked chart by sex, the stacked chart by municipality (Cali 8, the six others set aside to 1 to 6) and the Cali bar (8, the other six in color 1) followed, and the map and heat map were identical to before. An entry for a value no widget shows, a lower-case "river or stream" and "(none)" changed nothing, and keeping "Piped network" as 2 moved the others to 1, 3, 4, 5. With the green scale the map's 7 shapes and 7 legend swatches resolved to `--tmz-ramp-green-*` and the form's preview showed it before saving; the heat map on red did the same; the label ink reads at least 4.97:1 on all 49 steps of the 7 scales (red lowest). The column in color 3 drew vivid-3 (149,197,0), then institutional-3 (137,55,71) after the palette change, then earth-3 (184,138,1) after the widget form moved it to a dashboard with Earth (named "Color 3, ochre"), and back; its preview showed the color before saving with 0 queries. The pie of `activity` kept its size-order colors with no filter (7 slices 1 to 7, Other 493 in the neutral 137,135,129; the old build drew Other in color 8), and under a Municipality filter (Buga: Agriculture 99 first but still color 2, Other 48 neutral; Yumbo) each name kept its color and Other stayed neutral.

**People, history, small screens, keyboard.** The viewer's page has no actions menu at all (only Print) and no Colors; the viewer's token got 403 on `PUT …/colors` (also with the stored values), the analyst got 200 (a save equal to the stored colors made no version: 24 to 24), the admin 200, and `#ff0000` got 400 on `palette`; a second entry with color 5 in `sex` showed "sex already uses color 5." on its row. The history lists "Colors changed" (versions 15, 18, 19; it keeps only the 10 newest, so version 15 was the oldest to view); version 15 (Institutional, no kept colors) was drawn in its own palette with a read-only banner and no actions, restoring it gave version 25 with its palette, no kept colors, no filter and no widget scale, and restoring 24 gave version 26 with everything back. At 390 px the dashboard, the Colors dialog, and the widget form's Color and Scale groups have no horizontal scroll (scrollWidth 390), all 51 dialog controls are reachable, and the color choices are 49 x 40 px. The keyboard pass (Tab, Shift+Tab, arrows through palettes, the 8 colors and the 8 scales, Escape, Cancel and both Save paths) found a visible focus ring on every stop, a focus trap that wraps, and focus back on "Dashboard actions" after the dialog closes 5 times out of 5 (and on the widget's actions button after the widget form); each color choice reads "Color n, hue" (32 distinct names over the 4 palettes) and the scales read by name. One finding: when the saved palette is not the first one, the dialog opens with focus on the unchecked first palette radio. About 35 watched browser sessions showed no console warning and no page error; the only events were the deliberate 400 with its console error and one cancelled value lookup (`ERR_ABORTED`). Not checked: the dark theme (the app has no switch or `prefers-color-scheme` rule), the native suggestion popup of the value field (it cannot be seen or driven in headless Chromium, so only its options and filtering were read from the DOM), a real screen reader, other browsers, widths other than 1,280 and 390, a version recorded before palettes, and the Spanish text.

*Afterwards.* The one finding (the form opened with focus on the unchecked first palette) was fixed and checked again in the second round. A version recorded before palettes and the Spanish text, not covered here, were covered there.

### Print (task 14.2)

**14.2, colors on paper (real stack, final tree).** A dashboard with the palette *Earth*, color 5 kept for `Female` and color 6 for `Cali`, stacked columns by `water_source` and by `sex`, a pie, a pyramid, a bar chart in color 3, a heat map on the palette's scale, a map on the green scale and a second pie was printed on A4 through Chromium's PDF engine (`printBackground` false is Background graphics off; the `@page` margin of 10 mm applies). It went through the app's own **Print** action (the dialog replaced by the PDF) and through the browser path (`beforeprint`, as Ctrl+P), in portrait and landscape, with Background graphics off and on. Each sheet was rasterized at 100 dpi, and each of 140 SVG marks and 42 legend swatches (CSS backgrounds) was read back at 3 interior points against the color the browser computed for it on screen. With Background graphics off, 119 marks and 42 of 42 swatches printed in their color and none lost it; the other 21 marks are too small to sample alone (13 slivers of the stacked columns, 8 slices of the pie described below). All 22 distinct colors are on the sheet. `Female` (color 5, #669929) covers 17,401 px in the pie, 14,352 in the pyramid and 4,920 in the stacked columns, `Male` color 1 the other side; `Cali` (color 6) gives 5,447 px against 18,538 px of color 3 in the bars. The map's shapes use green steps 1, 3, 4, 5 and 7 and its legend all seven: steps 2 and 6 exist on paper only as legend swatches (128 and 112 px), and so does step 6 of the heat map's orange (120 px). Colors 2, 4 and 8 (a bar chart, a histogram and a bar chart in each), a Likert widget and the hatch swatch of empty heat map cells also printed. With Background graphics on, 9,964 of 2.9 million pixels differ, by 1 or 2 levels out of 255, all light-gray anti-aliased border edges; the Ctrl+P path matches (1,162 map-edge pixels shift by 1 px). *Vivid* (heat map on the purple scale, the map keeping its own green) and a dashboard saved without colors (palette *Tamiza*, blue scale, tokens equal to the chart colors before palettes) printed the same checks. Nothing loses its color because bars, slices, cells and map shapes are SVG fills, and the series, pie, heat map and map swatches are CSS backgrounds kept by `@media print { .tmz-grid { -webkit-print-color-adjust: exact; print-color-adjust: exact; } }` in `widget-grid.ts`, which they inherit (computed `exact` on all 51 elements that paint a CSS background; `html`, `body` and `main` compute `economy` and paint only `html { background: #fff }`; no `forced-color-adjust` rule). With that rule neutralized in a test page and Background graphics off, all 42 swatches printed blank (green steps 2 and 6 and orange step 6 left the sheet) while the SVG marks stayed. The sheet is white (the outer 5 mm of every sheet is pure white), and widgets have a 1 px border (#e5e7eb) and no shadow (none, and no off-white pixel within 2 mm outside any card).

Three findings are not about color and predate this change. In portrait on A4 and Letter the right border of the right-hand column is clipped (the 980 px canvas fits the printable width exactly); landscape is unaffected. A pie with 8 slices in a 6x5 tile prints as a 5 px dot, because `@md:flex-row` sits on the `@container` element itself, so the legend takes the tile's height (a 6x9 tile prints the whole pie). Legend labels break inside words. Not checked: the real print dialog and its checkbox, Firefox and Safari, the basemap tiles (this stack has none), the dark theme, Windows forced colors, line charts and stat tiles, and a rendering of a build from before palettes (only its token values, from git history, were compared).

*Afterwards.* The three findings that are not about color are under "Left as is" in Deviations from the plan. No print check was repeated in the second round.

### MCP client, first round (task 14.3)

**14.3 (MCP).** A script speaking streamable-HTTP JSON-RPC to the real `/mcp` endpoint (initialize, notifications/initialized, tools/list, tools/call; 34 tools, 175 tool calls, every request and a shortened answer recorded) checked the palette work as the analyst on the local stack. `tools/list` shows `palette` and `valueColors` on `update_dashboard`, and `create_widget` and `update_widget` describe `config.display` (color, ramp). The server instructions have a Colors section and the `describe_situation` prompt repeats it; `get_authoring_guide` is the script contract and says nothing about colors, which is by design. On a new dashboard, `update_dashboard` set `earth` and `[{sex, Female, 5}]`. `create_widget` then added a map of `water_coverage` with `display.ramp` green, a column chart with `display.color` 3, a stacked column by `water_source`, a pie by `sex` and a stacked column by `sex`, and `get_dashboard` returned the palette, the kept color and each widget's `display`. In the real UI (Chromium, 1440 px, computed fills read and screenshots viewed) the column bars were all #b88a01 (Earth 3), the water-source series were Earth 1 to 6 in order with matching legend swatches, Female was #669929 (Earth 5) in the pie and in 12 of 12 columns of the sex series with Male #af4a1b (Earth 1), and the map used the green steps with a matching 7-step legend (37.72% to 79.95%). A viewer saw the same dashboard in Vivid with Female and Male in Vivid 5 and 6. Refusals all answered `mcp.validation` on the right key with version, palette, kept colors and widgets unchanged afterwards: palette `neon` (also `Vivid`, which is case-sensitive, and empty), color 9 and 0 (`valueColors[n].color`), a color on a series chart, a pie, a map or a stat (`config.display.color`), a ramp on a pie, column or histogram (`config.display.ramp`), a color kept twice in a column (`valueColors[1]`), 65 entries (64 accepted), and `update_widget` with only a new `chartType` on a widget with a color or a ramp. A viewer token reads but gets `mcp.forbidden` (analyst role) on `update_dashboard`, `create_widget` and the version tools, and nothing changed. One `update_dashboard` with a new name and colors made one version (5 to 6, changes `renamed` and `colorsChanged`); setting the same colors again made none, and a rename plus an invalid palette saved the rename and refused the palette. `list_dashboard_versions` showed the `colorsChanged` versions, `get_dashboard_version` returned the palette and kept colors of versions 1, 2 and 6, and `restore_dashboard_version` brought back earth with Female 5 and then vivid with Female 5 and Male 6, each as a new version; a stale `baseVersion` gave `mcp.dashboardChanged`.

**Not checked.** `duplicate_dashboard` copying palette and colors was not run: the project was at its cap of 7 dashboards for the whole session, so the call was refused with `dashboards.limit` (409) and changed nothing. The copy code (`DuplicateAsync`) was only read, not run. A version recorded before palettes (expected to read as `tamiza` with no colors) was not available on the stack. Only the light theme was looked at, and the MCP client does not exercise the OAuth login (a minted token was used). Small gaps found in the MCP guide: `changes[].kind` values such as `colorsChanged` are undocumented (the doc says 'Colors changed'); palette and ramp names are case-sensitive without saying so; the JSON schema leaves `palette`, `display.color` and `display.ramp` unconstrained; `dashboards.limit` and `dashboards.versionNotFound` are not in `docs/mcp.md`.

*Afterwards.* The gaps in the guide were closed by the commit that followed: the tool descriptions and the schema say that names are matched exactly, in lower case, and give the range of a color, and `docs/mcp.md` lists the kinds of change of a version and the `dashboards.limit` and `dashboards.versionNotFound` refusals. `duplicate_dashboard` and a version recorded before palettes, not covered here, were covered in the second round.

### Browser, second round

Real Chromium (Playwright) against the real stack on the final tree (HEAD b77b3e5, UI built after it): real API, PostGIS, mock OIDC. Two dashboards of mine (6 and 3 widgets), both deleted at the end. 28 screenshots, 21 of them looked at; 0 console warnings, page errors or HTTP errors in 4 watched sessions (en light, en dark, es, 390 px).

- **Initial focus and tab stops (B1).** For each of the 4 saved palettes, by keyboard and by mouse (8 opens), `document.activeElement` was the checked palette radio 8 of 8; the old finding (focus on the unchecked first radio) is fixed. From the checked radio Tab went to Add, Cancel, Save and Shift+Tab to Save; the arrows moved from the checked radio and wrapped (earth: Right gave vivid, tamiza; Left gave vivid, earth, institutional). With 3 entries Tab went radio, Column 1, Value 1, color 5, Remove, Column 2... Escape returned focus to "Dashboard actions" 8 of 8. One low defect: Tab from Save wraps to the first radio, unchecked, when the saved palette is not Tamiza; a second Tab reaches the checked one and ArrowRight from the unchecked one selects the next palette from there (CDK focus-trap wrap, not compared with baseline).
- **Entry names (B2).** With Female 5, Male 6, Cali 8: 6 comboboxes named "Column, entry N of 3" and "Value, entry N of 3"; removing entry 2 gave "of 2", re-adding "of 3", removing entry 1 renumbered. Datalist: sex gave Female, Male ("Fe": Female); municipality gave 7 values ("Ca": Cali, Cartago). With the values request delayed 3 s and 4 s (column changed while pending, inside the 300 ms debounce, slow then fast), a 30 ms poll over 20 snapshots found 0 values of the old column; no lookup went out for the old column inside the debounce.
- **Scale alignment (B3).** Map and heat map, Tamiza and Earth, 1280 and 390 px (24 rows of cards, 2x4 and 4x2): gradient tops differ by 0 px in every row (limit 1 px), 9 px below each card top; cards are 74 px in the row with the palette's own card, 54 px in the others. Screenshots looked at.
- **Spanish (B4).** Language comes from `navigator.languages`, so the context was made with locale es-CO (html lang es, Accept-Language es-CO). Scanned the dashboard menu, Colors dialog (161 lines, 4 palettes), error state, toast, version history and banner, and the widget form Color and Scale groups under each palette: 0 raw keys, 0 untranslated lines, 32 hue names and 7 scale names read ("Tierra", "Vívido", "Verde azulado", "Escala de la paleta", "Se cambiaron los colores"). Two leftovers: the repeated-color row error is the API's English text (lang="en", by the app's convention), and the long caption "Escala de mapas y mapas de calor: Verde azulado" squeezes the Institutional card's gradient to 2.3 px at 1280 px (English 84.7 px; Earth and Vivid 40 px).
- **Dark through `.dark` (B5).** 4 palettes, 6 chart types, 146 marks and swatches each: 584 of 584 resolved to dark tokens (0 light-only, 0 unknown), by name `--tmz-pal-<p>-1..6` for series, `<p>-3` for the color-3 bar, `--tmz-series-other` for Other, blue/teal/orange/purple ramps for map and heat map; strokes = dark surface. Axis, legend and map legend text 6.82:1, heat labels at least 4.75:1, pyramid labels 17.75:1, tooltips 5.64:1 (heading 14.67:1). Screenshots legible. Observation: step 1 of each dark scale is 2.14 to 2.41:1 against the surface, so the lowest map shapes look faint (no stated gate for scale steps).
- **Pie on real data (B6).** The API domain (7 activities and Other; the missing group, rank 11, folds into Other: 493) matched the slices: order, tamiza-1..7, Other neutral, legend, shares, and the water pie's (none) at rank 5 in color 5. Kept Health 8 changed 1 slice; Agriculture 5 plus Mining 3 (inside Other, no effect) gave Commerce 1, Agriculture 5, Construction 2... (set aside, as the spec says). Filter municipality (pill: Buga; address: Yumbo, Cali, Cartago, Buga+Yumbo): colors followed the value (Buga: Agriculture 99 first, color 2), Manufacturing 21 stayed in Other (48). 17 comparisons against psql sums, 0 problems.
- **Version before palettes (B7).** Inserted version 12 = version 11 (earth, 2 kept) with `palette` and `valueColors` removed, `changes` `[{"kind":"filtersChanged"}]`, origin app, same author; `dashboards` has no version column (current = highest number). API: palette `tamiza`, `valueColors` `[]`. After a UI Colors change (13) the history viewed 12 read-only in Tamiza colors; Restore gave "Restored version 12 as version 14", GET dashboard palette `tamiza`, no colors, version 14 `restored` of 12.
- **Not checked.** Native datalist popup (headless), real screen reader, other browsers, widths other than 1280 and 390, printing, the plain bar and pie native `<title>` tooltips, baseline comparison. Seen and not part of this change: the pie legend layout.

*Afterwards.* The two low defects were fixed in the code: the palette cards are one tab stop (a roving `tabindex`) and the scale caption sits above its gradient. Each has a spec; neither had been looked at again in a browser when this was written. The other findings are under "Left as is" in Deviations from the plan.

### MCP client, second round

A Node client speaking streamable HTTP to the real `/mcp` of the local stack (HEAD `b77b3e5`, clean tree, API built after the commit) made 476 tool calls as the analyst. Every request and response is in `t3b-mcp/transcript.jsonl`; 7 calls were answered `mcp.rateLimited` (`retryAfter` in seconds) and the client waited and retried.

- **Copy (`duplicate_dashboard`).** An original with `earth`, `[{sex, Female, 5}]`, a municipality filter, a bar with `display.color` 3 and a map with `display.ramp` green was copied. The copy read the same palette, kept colors, filters and widget `display`/layout (new widget ids). Setting the copy to `vivid` and its bar to color 6 while replacing the original's colors left the other side untouched. The copy's history started with `{kind: duplicated, dashboard: <original's name>}`. Both dashboards were removed.
- **Schema.** `palette` lists its four values in lower case with no enum; `valueColors[].color` and `config.display.color` have minimum 1 and maximum 8; `config.display.ramp` names the seven scales; every one of those fields has a description (the `display` object itself has none). `palette: null` and `display.ramp: null` were accepted as "not set" (nothing stored, existing colors kept); `palette: null` alone is refused as "nothing to change".
- **Exact names.** `Vivid`, ` vivid`, `VIVID`, `neon` and an empty palette: `mcp.validation` on `palette`. `Green`, ` green`, `GREEN`, `magenta`: on `config.display.ramp`. Column `Sex`: on `valueColors[0].column`. `Female` and `female` (also `Female ` and an empty value) each kept a color; a repeated value or color answered on `valueColors[1]`. Color 0, 9, -1, null: on `valueColors[n].color` / `config.display.color`. Color 3.5 answered on `arguments` with a .NET type name in the message. Unknown keys (`display.colour`, extra `label`, top-level `colors`) were silently ignored; an empty `ramp` was accepted as not set.
- **Kinds of change.** All 14 kinds of the docs table were produced and read back, each with exactly the fields the table gives, and `list_dashboard_versions` and `get_dashboard_version` agreed on all 14 versions compared; the tool description lists the same 14. Twelve came from real calls (including one call that renamed and set colors: one version, and 22 widgets in one request: 20 entries and `moreChanges` 2). `initial` and `unrecorded` were produced on the test database with psql (a one-row history written as migration 0018 does, and a direct `UPDATE` of the palette followed by a restore, which answered `mcp.dashboardChanged` and left a `{kind: unrecorded}` version). A legacy version read `tamiza` with no colors and restoring it removed earth and the kept colors.
- **Errors.** `get_dashboard_version` and `restore_dashboard_version` for 9999, 0, -1 and for versions 1 and 5 (older than the 10 kept) all answered exactly `{code: dashboards.versionNotFound, message, status: 404}` and changed nothing. An unknown dashboard id answered `dashboards.notFound` (404), which the docs do not list.
- **Chart type.** `update_widget` with only `chartType` pie, stat or map on a bar with `display.color` 4 was refused on `config.display.color`; a map with a ramp turned into column or pie was refused on `config.display.ramp`; the same call with a `config` without `display` worked.
- **Guide texts.** The server instructions have a Colors section and the `describe_situation` prompt repeats the palette and kept-colors advice. `get_authoring_guide` has no color text (script contract, by design). The three tool descriptions carry the lower-case wording and the `chartType` sentence of `docs/mcp.md`.
- **Also confirmed.** Undo of a move in the documented order returned the widget with its own id (the opposite order gave a copy); a position-only update makes no version; `dashboards.limit` (409) was not repeated because the project is shared.
- **Not done.** No baseline comparison, no UI check, no viewer token, no OAuth sign-in.

*Afterwards.* The low findings of this round are under "Left as is" in Deviations from the plan. The docs review of the same round found that `get_widget_data` answered `dashboards.notFound` without its status; it answers it with the status now.

### Independent reviews

Three reviewers read the change as it stood at `b77b3e5` through three lenses (the API and SQL, the page and the tokens, the docs and the specs), each on the real stack where it could, and a skeptic then tried to refute every finding. None found a high or medium defect.

| Lens | What it checked | What it found | The skeptic |
|---|---|---|---|
| API and SQL | The whole delta of the API, its tests and the migration. The pie's domain against the rows of the same widget on the seeded tables (84 comparisons with every non-numeric column as the group and random own filters, 40 random dashboard filters that never changed a domain), the SQL by hand, the MCP schema attributes (REST and MCP with colors 9, 0, null, 2.5 and "3"), `PUT …/colors`, the version content rules, a restore to a version before palettes and back, duplicate, and a widget moved | Three low: a NUL character in a kept value ended in a 500 over REST and `mcp.failed` over MCP; D8 and task 5.2 still described the first version of the pie's domain; the description of `ValueColorInput.Column` was incomplete. One info: a pie matches a row to its domain entry by the text of the group | All three real and low; the first reproduced on the stack |
| Page and tokens | The validator on the 8 palette-and-theme sets and its `--ordinal` runs on the 7 scales; every number of Verification Results recomputed, and the hue of each color; the i18n keys; the Colors form's lookup race with the values endpoint delayed; the pie's fallback against the API's domain; kept colors, the map and heat map scales, the dark values and the widget form in the real page | Four low: `earth`'s slot 8 beside slot 1; hue names that did not describe their color; two Spanish words for purple in one card; D6's Spanish string. Four infos | Two real and low (`earth`, D6). Two not defects: the names could not be shown wrong, and "Morado" and "púrpura" are two things (the scale and a hue of Vivid) |
| Docs and specs | `docs/mcp.md` and `docs/indicators-dashboard.md` against the code and the stack (the 14 kinds of change, "names are matched exactly", the Troubleshooting rows), the hue names against `styles.css`, the design's numbers, a test for every scenario of the six spec files, and `openspec validate --strict` on a scratch copy | Seven low: a false sentence about `status` in `docs/mcp.md`; **Colors** missing from the actions menu list; D8 and task 5.2; D2 "Keeping values" and task 7.3; "each common color-vision deficiency" where only protan and deutan are gated; four base statements the delta left incomplete after an archive; Open Questions and D6. Five infos | Six real and low. One not: tritan is not a common deficiency, and the repo says so in four places |

What was done about each finding:

| Lens | Finding | Done |
|---|---|---|
| API | A NUL character in a kept value gives a 500 / `mcp.failed` | Refused with an error on `valueColors[i].value` (400, `mcp.validation`); a unit test, an integration test over REST and one over MCP |
| API | D8 and task 5.2 describe the pie's domain as `Fold`, "none last" (and a comment of the page's `QueryResult.domain` still does) | D8 and 5.2 rewritten for `PieDomain` and `BySize`. The comment above `domain` in `indicators.service.ts` still says "then `none`" and is the code's to change |
| API | `ValueColorInput.Column` does not say "not starting with a digit, at most 63 characters" | The description says it |
| Page | `earth` slot 8 (olive) beside slot 1: confusable under deutan (2.8) and protan, and nothing gates 8 against 1 | Slot 8 re-picked (teal); the spec gates the eighth color of every palette against the first (Repairs) |
| Page | Hue names that do not describe their hex (Tamiza 3 "aqua", 4 "yellow", 5 "magenta"; `earth` 2 "indigo") | Tamiza 3, 4 and 5 renamed "jade", "amber" and "pink" (Deviations); `earth` 2 kept, 17° and 12° from a Tailwind-style indigo |
| Page | "Morado" (the scale) and "púrpura" (a Vivid hue) | Left, as the skeptic judged |
| Page, docs | D6 quotes "Colores cambiados" | Corrected to "Se cambiaron los colores" |
| Docs | `docs/mcp.md` says every page-coded refusal adds `status`, which `get_widget_data` on a missing dashboard did not | `get_widget_data` answers `dashboards.notFound` with the status 404 |
| Docs | The actions menu list of `docs/indicators-dashboard.md` (and its lines on Duplicate and on who can act) omits **Colors** | Corrected in the same round: the menu list names **Colors** |
| Docs | D2 "Keeping values" says the color-vision check stays with the validator; the list and task 7.3 are incomplete | Rewritten, with task 7.3 |
| Docs | The spec says "each common color-vision deficiency" | Now "(protan and deutan)", and the eighth color's check is in the spec; the skeptic judged the old wording acceptable |
| Docs | Four base statements stay incomplete after an archive: `mcp-server` "Tools and their roles" and "Dashboard versions through MCP", `dashboard-versions` "Dashboard versions" | `MODIFIED` requirements added to the delta, with every base scenario carried over |
| Docs | Open Questions still lists the hex values | "None that change the specs, the approach or the task breakdown." |
| Docs | D8 and task 5.2 | As above |

Also noted, with nothing to do or left as is (see Deviations from the plan, "Left as is"): the hue names at the edge of the 25° rule; **Add** in the Colors form when every column offered has used its eight colors; the design's numbers all reproduce from `styles.css`; the pie's legend layout; the spec said "on the entry" for the limit of 64 (it now says "or on the list for the limit"); an empty `ramp` is read as not set while `docs/mcp.md` says nothing is corrected; the comment of `WidgetChanged` lacked the display (it names it now); `series-plot` was listed among the files that moved to `accent` and `forValues` and was not (removed from the three lists); the docs tests are prose matches that pass silently when the docs folder is not found; and a pie matching a row to its domain entry by the text of the group could, for a numeric group column holding equal values written with different scales (1.0 and 1.00), read one text in each statement (not reproduced).

### Final spot checks of this round

After the fixes of the second round (the palette tab stop and the Spanish palette card of the Colors form, slot 8 of `earth`, the NUL character in a kept value, `get_widget_data`, the descriptions and the docs) the real stack was rebuilt from the sources of the commit "Checkpoint: round three of fixes for the dashboard palettes" and two verifiers ran again, each in its own words below. The sources of `tamiza-api` and `tamiza-ui` have not changed since, except a doc comment of `indicators.service.ts`; the commits that followed hold the docs, the specs and this design.

Browser: the final spot checks ran in real Chromium (Playwright) against the real local stack built from the committed tree (HEAD e475280, UI sources clean), signed in as the analyst, on throw-away dashboards that were deleted afterwards. Every screenshot cited below was looked at.

- **Colors dialog tab stops.** For earth, vivid and institutional (each saved with no kept color and with two) the dialog was opened by keyboard. Focus opened on the checked palette radio, the only radio with tabindex 0. A full Tab cycle was 4 stops (radio, Add, Cancel, Save) and 12 with two kept entries, with the same count backward, and the radio group appeared once in 6 of 6 runs. Tab from Save landed on the checked radio and Shift+Tab from it landed on Save. ArrowRight and ArrowLeft moved focus and the choice from the checked radio, and after an arrow the group was still one stop on the new radio. Escape returned focus to "Dashboard actions" and sent no PUT.
- **Palette cards.** The scale gradient was 295 px wide at 1280 px and 332 px at 390 px in both es-CO and en-US (the Institutional card in Spanish had been 2.3 px at 1280). None was under 40 px.
- **Earth after the repair.** A dashboard with palette earth held two pies of exactly 8 slices (livelihoods on 8 activities, population by 8 age bands). In light and in dark, with normal vision and with Chromium's protanopia and deuteranopia emulation, the OKLab dE x100 between slice 1 (terracotta) and slice 8 (teal), measured on screenshot pixels beside 12 o'clock, was 27.0, 23.8 and 16.8 in light and 28.9, 26.3 and 16.9 in dark. The lowest was 16.8 against the 8 required.
- **Earth tokens.** Every slice and legend swatch resolved to --tmz-pal-earth-1..8 of the :root block (#af4a1b to #09a3a0) and the .dark block (#b34027 to #2da9a0) of styles.css. The old olive values appeared nowhere, and the rendered pixels matched the tokens exactly in normal vision.
- **Hue names.** In the widget form's Color group (bar chart), the 8 aria-labels, the swatch titles and the visible "Color n, name" text under the swatches matched colors.hues.* of en.json and es.json under all four palettes in en-US and es-CO (64 radios), with no raw key. Tamiza 3, 4 and 5 read jade, amber and pink (jade, ámbar, rosa), and Earth 8 read teal (verde azulado).
- **Sanity through the UI.** The Colors dialog saved Vivid plus a kept color (municipality Cali, color 4): PUT 200, and the Cali bar drew in --tmz-pal-vivid-4. The version history showed "Version 26 Current: Colors changed". The widget form saved a bar with color 3 (PATCH 200) and a new map with the Orange scale (POST 201, drawn in orange).
- **Console.** 0 console errors, 0 page errors and no HTTP status of 400 or more across the session. The only network events were two cancelled suggestion lookups (net::ERR_ABORTED) when Escape closed the dialog mid-request.
- **Seen, not part of this change.** At the default 6x4 tile a pie of 8 slices was clipped by its legend (the flex container has "@container" and "@md:flex-row" on one element, identical on main), so the sampling used 6x10 tiles. Under tritanopia and achromatopsia some adjacent earth slices fall below dE 8 (lowest 2.4); the criterion covered only protanopia and deuteranopia.

MCP: the last round was checked as the analyst over the real `/mcp` endpoint of the local stack (API built from the committed sources of HEAD e475280, 288 tool calls plus 3 REST calls, all recorded in a JSONL transcript).
- A NUL character in a kept value (`Fe\u0000male`, also alone or at the end) answered `mcp.validation` on `valueColors[0].value` (or `valueColors[1].value` in a pair) with no `mcp.failed`, and `get_dashboard` read the same before and after. `PUT /colors` answered 400 on the same keys; no 500 and no warning or error in the API log. A normal Female/Male pair still saved over both routes.
- `get_widget_data` with an unknown dashboard id answered `{ code: "dashboards.notFound", status: 404 }`, as `get_dashboard` does; an unknown widget kept `mcp.notFound`.
- `tools/list`: the `create_widget` description no longer holds "To change the chart type of a widget" and `update_widget`'s does; `valueColors[].column` has the complete description, `config.display` has its own, `color` is 1 to 8, and `palette` and `ramp` have no enum; explicit nulls for both are read as "not set".
- Every new sentence of `docs/mcp.md` held: an empty `display.ramp` is "not set" and an empty `palette` is refused, a well-formed unpublished column is saved (and `get_indicator_columns` gives the real names), a fraction 3.5 is refused on `arguments`, unknown keys are ignored.
- A position-only `update_dashboard` moved the dashboard and made no version; with a rename it made one. `get_dashboard_version` widgets carry exactly `id`, `title`, `chartType`, `config` and `layout`.
- A new `chartType` alone on a bar with a color or a map with a scale was refused on `config.display.color` or `config.display.ramp`; with a config without `display` it was accepted.
- `dashboards.notFound` with status 404 came from all eleven tools of the Troubleshooting row (and from `update_dashboard` with a name, filters, palette or colors).
- The full flow (create, palette earth plus colors, bar color 3, map ramp green, duplicate, read both, remove both) worked and the copy matched the original.
- Open note, low: `update_dashboard` with only a `position` for an unknown dashboard answers success with no versions instead of `dashboards.notFound`; the docs row does not claim it. Not tested: the `dashboards.limit` row (needs 7 dashboards) and a version from before palettes.

### What was not verified

What the checks above could not reach, collected from their own lists of limits:

- **Browsers and readers.** Chromium only (Playwright, headless) at 1,280 and 390 px (the first MCP check read the page at 1,440 px). No Firefox or Safari, no touch emulation, no real screen reader: names were read from `aria-label`, `ariaSnapshot` and `getByRole`.
- **The native suggestion popup of the value field** is not visible or drivable in headless Chromium. Only its options in the DOM were read, and filtered; picking a suggestion with the mouse or the keyboard was not tried.
- **The dark theme** was seen only through the `.dark` class on the page's root, which the harness set: the app has no theme switch and nothing sets the class, so a member cannot reach it yet. The tooltips of plain bars and pie slices are the browser's own `<title>` and were not themed or checked.
- **Spanish** came from the browser's language (the context's locale `es-CO`), since the app has no language setting. The API's own field messages stay English by design.
- **Sign-in** was a mock OpenID Connect provider in place of Keycloak, and MCP calls used minted tokens (analyst, and viewer in the first round): the real OAuth sign-in, the Origin check and the project's access setting were not exercised.
- **The stack** had no analytics worker (a run queues and nothing processes it) and no basemap provider, so maps were drawn on a blank background and the basemap credit and tiles were not checked. The stack's migration ran from an empty database only; the upgrade from `0018` is covered by `SchemaMigratorTests`, not by a real stack, and the `ck_dashboards_palette` check was not tried with a bad insert on it (the API and `SchemaMigratorTests` refuse the palettes `neon`, `Earth`, `#ff0000` and an empty one).
- **Print** went through Chromium's PDF engine (`printBackground` false for Background graphics off), not the real print dialog, its checkbox or its preview; Firefox, Safari, Windows forced colors, Letter beyond the right border and A5 were not tried. The map has no basemap in this stack. Stat tiles, line charts, cross tables, a phone-width sheet, the viewer role, a dashboard with dashboard filters and a version shown were not printed. A dashboard saved before palettes was not printed from a build of that time: the claim that it looks as before rests on the token values (`tamiza` 1 to 8 and `blue` 1 to 7 equal the old series and sequential tokens in the history) and on a visual read.
- **Versions recorded before palettes** were not available through the real application: the second round inserted one with SQL, as the migration of `0018` leaves it, and `initial` and `unrecorded` versions for the MCP check were produced with `psql` on the test database. They show how the API reads and writes such data and are not data a real migration produced.
- **The MCP troubleshooting statements** on `mcp.projectOff`, `mcp.rowsNotAllowed`, `mcp.timeout` and `mcp.nameMismatch` were not exercised; `dashboards.limit` was seen once, in the first round, when the project was at its cap, and not repeated in the second, since the project is shared; `remove_dashboard` refusing the only dashboard was not tried. Some checks of the first round made entries through the API or a Node client rather than the page, and the rate limit of 120 calls a minute interrupted the second MCP round seven times.
- **Cases that cannot be reached**: a group with no share in the pie's own rows but one under a dashboard filter was not reached with the seeded data, where a filter only removes rows from groups that already have a share, so a group outside the domain by its size (one that folds into "Other") was tested instead; the missing group at its rank was tested with a second pie. Colors per chart were seen on the seeded Housing data only (7 municipalities, 2 sexes, 6 water sources).
- **Nothing was compared with a build from before the change**, which is not available. Every defect noted as older than this change rests on reading the code that causes it.
