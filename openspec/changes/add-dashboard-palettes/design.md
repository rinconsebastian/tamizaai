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
| `institutional` | sober: blues, teals, slate, with muted warm accents | `teal` |
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
