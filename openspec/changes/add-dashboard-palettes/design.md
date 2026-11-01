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
| `earth` | terracotta, ochre, olive, clay, moss | `orange` |
| `vivid` | saturated, high-energy hues | `purple` |

The scales are `blue`, `teal`, `green`, `orange`, `red`, `purple` and `gray`. Ids live in `Indicators/Palettes.cs` (`Palettes.All`, `Palettes.Scales`, `Palettes.DefaultScale(palette)`) and in their UI twin `chart-colors.ts`.

- **Choosing values.** The three new palettes are built as the reference one was. Candidate hues are ordered by enumerating their orders and running `validate_palette.js` on each, keeping only orders that pass every hard gate on adjacent pairs:
  - lightness band;
  - chroma floor;
  - CVD ΔE ≥ 8;
  - normal-vision ΔE ≥ 15.

  The check runs in light mode against `#ffffff` and in dark mode against `#101828`, the surfaces of `chart-tokens.spec.ts`. A contrast WARN is accepted with the relief Tamiza already provides: legends, tooltips and "Show as table". The new scales are built as `blue` was, with `--ordinal` checks on the steps. The commands and their results go in a **Verification Results** section of this design, as `add-two-dimension-widgets` did.
- **Keeping values.** `chart-tokens.spec.ts` gains:
  - for each palette and theme: 8 distinct colors, each inside the lightness band (OKLab L 0.43-0.77) and above the chroma floor (0.10), and adjacent pairs at least 15 apart in OKLab ×100 (the normal-vision floor, computable with the existing helper);
  - for each scale and theme: the existing sequential checks (one hue within 40°, lightness in one direction with visible steps, ink at 4.5:1 and the better of the two inks).

  The CVD check stays with the validator and its recorded results: simulating deficiencies in the test would duplicate the script.
- **Hue names.** Each palette slot has a hue name, translated in `en.json` and `es.json` (`colors.hues.{palette}.{n}`). It names the color in the pickers (D9), never alone: the number is always shown too.

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
| A value that is null or longer than `DataQueryBuilder.MaxTextLength` | `valueColors[i].value` | |
| A color outside 1-8 | `valueColors[i].color` | |
| A value or a color repeated within one column | `valueColors[i]` | "{column} already keeps a color for {value}." / "{column} already uses color {n}." |
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

- `DashboardChangeKinds.ColorsChanged = "colorsChanged"` joins `All`, the UI list and the history's strings ("Colors changed" / "Colores cambiados").
- `Content(name, filters, palette, valueColors, widgets)` writes `"palette"` only when it is not `tamiza`, and `"valueColors"` only when it is not empty. A dashboard that never changed its colors keeps the content it has today, byte for byte. Its first save after the deploy then makes no version, and a restore's step 2 does not record an "unrecorded" state just because the content shape grew.
- `ReadContent` reads a missing `palette` as `tamiza` and missing `valueColors` as `[]`. `VersionContent` gains both.
- A restore writes both back with the name and filters, so a version from before palettes restores *Tamiza* with no colors kept.
- `GET …/versions/{n}` returns them, so viewing a version draws its widgets in that version's colors (spec "Viewing a version"). `get_dashboard_version` returns them too.

### D7. Resolving colors (`chart-colors.ts`)

```ts
interface DashboardColors { palette: PaletteId; valueColors: readonly ValueColor[] }

/** The colors of one widget. */
function chartColors(colors: DashboardColors, type: ChartType, config: WidgetConfig): {
  accent: string;                                         // var(--tmz-pal-{p}-{display.color ?? 1})
  forValues(column: string, values: readonly SeriesKey[]): string[];  // one per value, in the order given
  ramp: ScaleId;                                          // display.ramp ?? DefaultScale(palette)
  rampStyle: Record<string, string>;                      // the --tmz-seq-* overrides of D1
}
```

`forValues` takes the widget's domain in display order (series, pyramid sides, or a pie's domain from D8) and works in three steps:
1. Each value with a color kept for `(column, value)` takes it. `"(none)"` and "Other" never match.
2. The colors taken in step 1 are set aside.
3. The other values take the remaining colors 1-8 in order, and "Other" takes `--tmz-series-other`.

The domain holds at most 8 entries, "Other" included, and a column keeps each color at most once, so two values of a widget never share a color. A chart with no series calls `forValues(groupBy, …)` for its bars, but only uses kept colors: a value with no color kept is drawn in `accent`, not given the next free color (spec "One bar highlighted").

- **Threading.** `project-indicators.ts` holds the `DashboardColors` of the dashboard or version shown and passes them through `widget-grid` to each `widget-view`, as a new `colors` input. `widget-view` passes `chartColors(...)` to the chart it draws. `WidgetDialogData` gains `colors`, so the preview uses the dashboard's palette and colors kept by value.
- **Readers.**
  - `axis-chart` and `series-plot` take `accent` or `forValues` instead of `seriesColor`, and `series-legend` takes the resolved list.
  - `pie-widget` colors slices by `forValues(groupBy, domain)` and folds into "Other" every slice outside the domain (D8).
  - `pyramid-chart` takes `forValues(seriesBy, [left, right])`.
  - `heatmap-chart` and `map-widget` bind `rampStyle` on their root element.
  - `seriesColor` and `sequentialColor` are removed from `chart-scales.ts`.

### D8. A pie's domain from its own rows

`Plan` for a pie gains the domain statement of `SeriesQueryBuilder.cs:64-80`, built over the group column and the widget's own filters, capped at 9: one more than the pie's 8 colors, so a fold can be detected. `IndicatorDashboard` reads it with `ReadDomainAsync` and `SeriesDomain.Fold(values, hasNone, 8)` before the rows. `IndicatorQueryResponse` gains `Domain`: pie only, `[{ value, kind }]` as `SeriesInfo`, largest total first, `none` last, and `other` when values were folded. It is null and left out of the JSON for every other kind.

- The UI sizes the slices from the filtered rows as before, draws them largest first, colors them from the domain (D7), and sums every row whose group is not in the domain into "Other".
- With no dashboard filter, the domain's order is today's size order, so an existing pie keeps its colors. Under a filter, the colors and "Other" stay as they were.
- `get_widget_data` and `query_indicator` keep their row shape. The domain is a drawing aid, so MCP does not return it.

*Alternative:* color slices by the natural order of their values in the UI. Rejected: a filter that removes one value would still shift the colors of every value after it. "Color follows the entity, never its rank" (dataviz skill).

### D9. Forms

- **Colors** (`dashboard-colors-dialog.ts`, CDK dialog like `dashboard-filters-dialog.ts`). The action `colors` is added to the dashboard's menu (`project-indicators.ts:410-440`), for analysts and admins, and hidden while a version is viewed, like the other changing actions. The form holds:
  - **Palette**: a radio group of the four palettes. Each card shows its name, its eight swatches in order and its scale as a gradient, and the current one is selected.
  - **Colors kept by value**: a list of rows, each with a column (the text columns the filters form offers, from the same source), a value (a combobox fed by `GET …/values?column=&search=&indicator=`, which also accepts typed text), and a color (a radio group of the palette's eight swatches). Each row can be removed, and a row can be added up to 64.
  - Saving calls `PUT …/colors` and replaces the page's `DashboardColors`. Server errors land on their rows, keyed as in D4.
- **Widget form** (`widget-dialog.ts`). It gains a **Color** radio group, with the dashboard palette's eight swatches, for the kinds of `display.color`, and a **Scale** radio group for map and heatmap: "Palette's scale" first, then each scale as a gradient. A swatch is a native `<input type="radio">`, as the form's other choices are (layout, sort), styled as a swatch with its number visible and an `aria-label` with the number and the hue name ("Color 3, aqua"), so the arrow keys move through the group with no code of ours. The preview redraws on change without a new query (D3).

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
- **[Contrast below 3:1 on some light slots]**, as today for aqua, yellow and magenta. → Accepted with the existing relief: legends for 2 or more series, tooltips, and "Show as table".
- **[More tokens in `styles.css`]**: about 270 custom properties. → Negligible at runtime, and the tests list them.
- **[Version content compared as strings]**: a content shape that always wrote the new keys would make every dashboard's next save look like a change. → D6 writes them only when they differ from the defaults.

## Migration Plan

1. Deploy `0019_add_dashboard_palettes.sql`. It adds the columns with defaults and rewrites no row.
2. Existing dashboards read as `tamiza` with no colors kept, and existing widgets have no `display`. They render as before, and their version content is unchanged (D6).
3. **Rollback.** The previous `api` ignores the new columns and drops `display` when it reads a config (`ReadConfig`). The previous `ui` ignores the new response fields. The columns can stay in place. Dropping them is a manual `ALTER TABLE` if ever needed.

## Open Questions

- The exact hues and hex values of `institutional`, `earth` and `vivid` and of the six new scales, chosen with the validator during implementation (D2). They change no spec or task.

## Verification Results

Palettes and sequential scales (tasks 7.1 to 7.3). The values of `institutional`, `earth` and `vivid` and of the six new scales were designed by five independent runs of the dataviz method, each against the same gates; the lead then re-ran every command below on the values as they went into `styles.css` and recomputed every number with its own OKLab code (the helpers of `chart-tokens.spec.ts`). **One hex value was changed after this validation: slot 8 of `institutional`, in the fix pass that followed the first review (see Repairs). Every other palette color and every scale shipped as it was designed, and no palette or scale was dropped from the catalog.** The `institutional` commands and numbers below are those of the values that ship.

Commands run in the development sandbox (Node 22) with the validator of the dataviz skill, `scripts/validate_palette.js`, written below as `validate_palette.js`. The chart surface is `#ffffff` in the light theme and `#101828` (Flowbite `neutral-primary-soft`, gray-900) in the dark theme, the surfaces of `chart-tokens.spec.ts`. A palette is validated as a categorical palette on adjacent pairs, which is what a stacked chart, a legend and a bar chart with a series show side by side; a scale is validated as an ordinal ramp (`--ordinal`).

### The palettes

The gates, all hard: OKLab L inside the band (0.43 to 0.77 light, 0.48 to 0.67 dark), OKLab chroma at least 0.10, adjacent CVD ΔE at least 8 (protan and deutan; tritan is reported), adjacent normal-vision ΔE at least 15. Contrast against the surface below 3:1 is a WARN, accepted with the relief Tamiza already has: legends for two or more series, tooltips and "Show as table".

| Palette | Theme | L range | Lowest chroma | Worst adjacent CVD ΔE | Worst adjacent normal ΔE | Lowest contrast | Slots under 3:1 |
|---|---|---|---|---|---|---|---|
| `tamiza` | light | 0.527 to 0.764 | 0.141 | 9.1 (tritan 5.8) | 19.6 | 2.17:1 | 3 (2.82:1), 4 (2.17:1), 5 (2.69:1) |
| `tamiza` | dark | 0.529 to 0.670 | 0.128 | 8.4 (tritan 8.7) | 19.3 | 3.59:1 | none |
| `institutional` | light | 0.446 to 0.745 | 0.104 | 15.5 (tritan 20.6) | 24.1 | 2.36:1 | 2 (2.67:1), 4 (2.49:1), 5 (2.70:1), 7 (2.36:1) |
| `institutional` | dark | 0.500 to 0.662 | 0.113 | 11.0 (tritan 13.9) | 16.5 | 3.07:1 | none |
| `earth` | light | 0.441 to 0.660 | 0.104 | 16.6 (tritan 7.3) | 19.2 | 3.15:1 | none |
| `earth` | dark | 0.513 to 0.666 | 0.104 | 14.8 (tritan 9.3) | 20.9 | 3.05:1 | none |
| `vivid` | light | 0.529 to 0.761 | 0.137 | 17.4 (tritan 7.2) | 24.3 | 2.05:1 | 2 (2.06:1), 3 (2.05:1), 8 (2.48:1) |
| `vivid` | dark | 0.540 to 0.665 | 0.118 | 13.1 (tritan 9.1) | 19.3 | 3.11:1 | none |

The numbers in the table are the lead's own, recomputed from the hex values; the validator's lines below give the same worst pairs. Each command is followed by what the validator printed, with the PASS, WARN and FAIL lines only.

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
$ node validate_palette.js "#af4a1b,#345895,#b88a01,#82344e,#669929,#7e4701,#934791,#676e02" --mode light --surface "#ffffff"
  [PASS] Lightness band         all 8 inside L 0.43–0.77
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #676e02↔#934791 ΔE 16.6 (deutan) · tritan 7.3
  [PASS] Normal-vision floor    worst adjacent #934791↔#7e4701 ΔE 19.2 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)

$ node validate_palette.js "#b34027,#506cad,#988904,#984e6b,#65a84b,#8e5a01,#a860b0,#677401" --mode dark --surface "#101828"
  [PASS] Lightness band         all 8 inside L 0.48–0.67
  [PASS] Chroma floor           all 8 >= 0.1
  [PASS] CVD separation         worst adjacent #8e5a01↔#65a84b ΔE 14.8 (deutan) · tritan 9.3
  [PASS] Normal-vision floor    worst adjacent #8e5a01↔#65a84b ΔE 20.9 (normal)
  [PASS] Contrast vs surface    all 8 >= 3:1
  → ALL CHECKS PASS   (exit 0)
```

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

Contrast, in the light theme only, is what the WARNs name: `institutional` slots 2 turquoise, 4 sky blue, 5 mustard and 7 salmon (2.67, 2.49, 2.70, 2.36:1) and `vivid` slots 2 cyan, 3 lime and 8 orange (2.06, 2.05, 2.48:1), like Tamiza's aqua, yellow and magenta (2.82, 2.17, 2.69:1). `earth` has no WARN in either theme (lowest 3.15:1 light, 3.05:1 dark), and no palette has one in the dark theme. Where a label is drawn on a fill of `institutional` slot 2, 4, 5 or 7 in the light theme, near-black reads better than white; this matters for the data labels of `add-widget-display-options`.

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

**Against the neutral of "Other"** (`#898781`, the same in every palette): the lowest normal-vision ΔE (OKLab ×100) of any palette color is 14.5 (tamiza), 13.7 (institutional), 13.1 (earth) and 19.1 (vivid) in the light theme, and 12.6, 11.8, 12.1 and 13.0 in the dark one. Under protan and deutan (the smaller of the two) the lowest is 5.1 (tamiza), 9.0 (institutional), 9.8 (earth) and 8.0 (vivid) in the light theme, and 1.1, 5.2, 9.0 and 8.6 in the dark one; the weakest new color is `institutional` salmon in the dark theme, and Tamiza's own colors were already closer. `chart-tokens.spec.ts` keeps every palette color at least 10 from "Other" for full-color readers.

**Against Tamiza** (ΔE ×100 to the nearest Tamiza color of the same theme): the first four colors of each new palette are at least 10.8 (institutional), 11.1 (earth) and 11.2 (vivid) away in the light theme and at least 8.8, 8.2 and 11.1 in the dark one, so none is a near-duplicate of a Tamiza color. Over all eight, the nearest are 7.9 (institutional, slot 8, olive green, beside Tamiza's green), 9.0 (earth) and 7.5 (vivid, slot 8, orange, in the crowded warm zone) in the light theme and 5.5, 7.6 and 5.7 in the dark one (institutional slot 5, mustard, and vivid slot 8). These are looked at, not gated.

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

The spec has a `Checks on every palette` block (spec "Palette checks", scenario "Checks on every palette"):

- the tokens are exactly the 4 palettes of 8 colors and the 7 scales of 7 steps and 7 labels, in both themes, and the `--tmz-series-1..8` are gone;
- every palette and scale holds the hex values above, pinned literally, so `tamiza` and `blue` equal the values validated before this change;
- per palette and theme: 8 distinct colors, each inside the band and above the chroma floor, neighbors at least 15 apart (OKLab ×100, normal vision) and at least 8 apart under protan and under deutan (the validator's simulation and its CVD target, see below), each at least 10 from "Other";
- per scale and theme: lightness in one direction with steps of at least 0.06 (`blue`: 0.04), one hue within 40° (`gray` instead stays under a chroma of 0.02, its measured maximum being 0.017), step 1 a tint (at least 1.25:1 on white, measured 1.30 to 1.35; at least 2:1 on the dark surface, measured 2.14 to 2.41), and a label of white or near-black at 4.5:1 and at least as good as the other;
- the scales are at least 3 apart at every step;
- the `--tmz-seq-n` and `--tmz-seq-ink-n` that a mark reads are the blue scale in both themes;
- for every palette and theme, the color-vision simulation gives back the worst adjacent distances the validator printed, within 0.1 (the validator prints one decimal): the smaller of protan and deutan, and tritan;
- the checks themselves fail on a missing or repeated color, a color off the band, dull, too close to its neighbor or to "Other", a neighbor that full-color readers tell apart and a reader with protan does not, steps out of order, a second hue, a gray with a hue, a lost first step and a faint or wrong label.

### The color-vision check in the spec

Spec "Palette checks" asks that adjacent colors stay apart for readers with each common color-vision deficiency, and its scenario fails the checks when any color breaks one. `chart-tokens.spec.ts` first left that check to the validator run recorded here, so a developer who edited a palette in `styles.css` and in the spec's pinned lists passed every check with no color-vision check at all. It now runs the check itself, the way the validator does (`validate_palette.js`: `MACHADO`, `simulate`, `deltaE`, `CVD_TARGET`):

- `simulate` and `deltaE(x, y, deficiency)` sit next to the OKLab helpers. A color goes through the Machado, Oliveira and Fernandes (2009) matrix of the deficiency at severity 1.0, on linear RGB, clamped to 0-1, then into OKLab, and the distance is ×100. The validator's thresholds are calibrated to that model, so it is copied, not swapped for another.
- For every palette and theme, `paletteProblems` fails a color that is under 8 from its neighbor under protan or under deutan, the two deficiencies the validator gates on. 8 is the validator's `CVD_TARGET` and the gate of design D2. The validator's floor of 6 is only a WARN that labels, gaps or texture must excuse, which a palette's colors do not bring. The validator reports tritan without gating on it, and the spec does not gate on it either: its 5.8 for Tamiza in the light theme would fail an 8.
- The block that tests the checks themselves has a negative case: a dark olive green, `#4c6118`, in place of the indigo of `earth` next to terracotta `#af4a1b` is 18.0 apart with full color vision and 0.38 under protan (8.8 under deutan). The checks return exactly one problem for it, the protan one.

**Cross-check against the validator.** The worst adjacent distance of each palette from the simulation of the spec, written at two decimals, against what the validator printed (the smaller of protan and deutan, and tritan). They agree within 0.05, the rounding of one decimal, and the spec asserts 0.1. The cross-check ran first against the recorded values of the first validation (Tamiza 9.1 / 8.4, institutional 23.8 / 14.6, earth 16.6 / 14.8, vivid 17.4 / 13.1) and passed at once, so the gate was asserted on a simulation known to match; the `institutional` rows below also give the values after the repair.

| Palette | Theme | Protan and deutan: validator | Spec | Tritan: validator | Spec |
|---|---|---|---|---|---|
| `tamiza` | light | 9.1 | 9.12 | 5.8 | 5.83 |
| `tamiza` | dark | 8.4 | 8.36 | 8.7 | 8.65 |
| `institutional` | light | 15.5 (before the repair 23.8) | 15.46 (23.77) | 20.6 | 20.59 |
| `institutional` | dark | 11.0 (before the repair 14.6) | 11.01 (14.64) | 13.9 | 13.87 |
| `earth` | light | 16.6 | 16.62 | 7.3 | 7.31 |
| `earth` | dark | 14.8 | 14.81 | 9.3 | 9.31 |
| `vivid` | light | 17.4 | 17.35 | 7.2 | 7.16 |
| `vivid` | dark | 13.1 | 13.09 | 9.1 | 9.14 |

The simulation is held to the validator by that test: changing one coefficient of the deutan matrix by 0.1 fails four of the eight palette-and-theme rows by 0.16 to 0.66, and a CVD target of 0 fails the negative case.

### Repairs

One, found after the verification above, in the fix pass that followed the first review: the eighth color of `institutional`.

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

### UI checks after the tokens (tasks 7.2 and 7.3)

Run in the same sandbox, with the other groups of this change still in progress in the working tree, so the totals are those of the moment, not final. The rows for the token specs, the build and the safety check were run again in the fix pass (the color-vision check in the spec, the new slot 8 of `institutional`, the raised budget).

| Check | Result |
|---|---|
| `chart-tokens.spec.ts` | 86 tests pass: the Likert tests as they were, and the `Checks on every palette` block (the tokens, each of the 4 palettes and 7 scales in both themes, the color-vision simulation against the validator's numbers, the scales together, and the checks failing on a broken color, a pair confusable under protan included) |
| `translations.spec.ts` | passes with the new `colors.palettes`, `colors.scales` and `colors.hues` keys in `en.json` and `es.json` (4 palette names, 7 scale names, 32 hue names each; hue 8 of `institutional` is "olive green" and "verde oliva") |
| `map-styles.spec.ts`, `print-styles.spec.ts` | pass: the `.tmz-map` rules are untouched and still read `--tmz-seq-n`. Together with `chart-tokens`, `translations`, `chart-widgets`, `chart-colors`, `map-widget` and `dashboard-colors-dialog`: 8 files, 422 tests pass |
| UI, whole suite (`npm test -- --watch=false`) | 101 files, 2,392 tests passed. One unhandled error, `Cannot read properties of undefined (reading 'total')` in `ProjectPerformance`, raised during `project-indicators.spec.ts`: it was there before this change (baseline: 99 files, 2,256 tests, the same error) and still makes the exit code 1 |
| `ng build` | succeeds. The initial bundle is 660.21 kB; before this change it was 649.71 kB. The 10.5 kB are `styles.css` (68.30 to 77.27 kB: the palette and scale tokens, 244 more custom properties, 3.7 kB of them the labels on the scale steps) and 1.5 kB of shared JavaScript. The warning budget of the initial bundle in `angular.json` goes from 650 kB to 675 kB, as it went from 500 kB to 650 kB in the commit "maps ok", and the error budget stays at 1 MB. The build prints no budget warning, only the NG8113 warning about `Card` in `ProjectReference` that was already there; the tokens were not shrunk |
| `npm run check:safety` | clean |

`--tmz-series-1..8` no longer exist in `styles.css`, and nothing reads them: group 10 moved `chart-scales.ts` (`seriesColor`) and `series-legend.ts` to `chart-colors.ts`, which draws a series in `var(--tmz-pal-{palette}-{n})`, and the only series token left is `--tmz-series-other`. `chart-tokens.spec.ts` checks that no `--tmz-series-<n>` is defined, in either theme.
