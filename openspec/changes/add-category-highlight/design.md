# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change adds one display field and one value of an existing field, after `add-dashboard-palettes` and `add-boxplot-widget`, and follows their patterns. It is written against the main specs and the code as they are after `add-indicator-column-metadata` and `add-widget-context` were implemented and archived (2026-11-04), so what they added is stated in the past tense; `add-chart-annotations` and `add-declared-category-order` come before it in the program and are named as planned. The pieces it works on:

- **Colors are resolved in one place**, `tamiza-ui/src/app/pages/projects/indicators/chart-colors.ts`:
  - the neutral of "Other" is `var(--tmz-series-other)` (`:58-59`), `#898781` in both themes and every palette (`styles.css:112-113`);
  - colors kept by value are read into a map by column and exact text, skipping any entry whose color is not a place 1 to 8 (`isPlace`, `:88-91`; the loop `:109-121`);
  - `keepsByValue` is true for a bar or column chart with no series and for a box plot (`:122`); `kept(column, value)` answers the kept color or null for them (`:151-154`);
  - `forValues(column, values)` gives the series, the pyramid's sides and the pie's slices their colors: kept colors first, set aside, then the free colors in order, "Other" neutral (`:128-150`).
- **Who reads them:**
  - `axis-chart.ts`: a chart with no series draws a bar or column in `colors.kept(groupBy, groups[i]) ?? color` (`markColor`, in the plain layout), a line always in `accent`; a chart with a series takes its legend colors from `forValues(seriesBy, …)` (the `legend` computed) and hands them to `seriesPlot`. Its group labels are the `<text>` elements of `layout().labels`, and its accessible name is `summary(t)`, the `aria-label` of the SVG;
  - `series-plot.ts`: `linePlot` pushes one path and its dots per series, in series order (`:284-331`); `Label` has no weight (`:18-26`);
  - `series-legend.ts`: `LegendEntry { name, color, outlined? }` (`:13-19`);
  - `pie-widget.ts`: `partColors` from `forValues(groupBy, [...domain, other])`, its own legend list (`data-testid="chart-legend"`) and its `summary(t)`;
  - `boxplot-chart.ts`: `boxplotPlot({ …, color: (box) => kept(groupBy, box.group) ?? accent })`, group labels as `<text>` from `plot().labels`, and its `summary(t)`;
  - `pyramid-chart.ts`: its sides from `forValues(seriesBy, [left, right])` (`sideColors`).
- **The API stores colors kept by value as numbers.**
  - `ValueColor(Column, Value, int Color)` (`Indicators/Palettes.cs:48-55`), read back with `DashboardEndpoints.ReadValueColors` as a `List<ValueColor>`. `ValueColorInput.Color` carries `[Range(1, 8)]` for the MCP schema (`Palettes.cs:61-64`).
  - `DashboardValidation.Colors` refuses more than 64 entries, a color outside 1 to 8 with "The color must be a number from 1 to 8." (on `valueColors[i].color`), and a value or a color repeated in a column with "{column} already keeps a color for {value}." or "{column} already uses color {n}.". Neither message says that a column holds 8 colors at most. (The widget's own `display.color` has its own message of the same words, in `IndicatorValidation`; it stays.)
  - The `0019` script puts no check on the content of `value_colors` (`jsonb`), and neither `0021` nor `0022` touches it.
  - `PUT …/colors` (`SaveColorsAsync` in `DashboardEndpoints.cs`) validates the whole list, and MCP `update_dashboard` with only a palette resends the stored list through the same validation (`SaveColorsAsync` in `McpDashboardTools.cs`).
- **A widget's display.**
  - `WidgetDisplay(Color, Ramp, Orientation, Labels, Subtitle, Footnote, Caption, Unit, ShowDescription)` (`IndicatorModels.cs`), in that order: `add-indicator-column-metadata` appended `Labels` and `add-widget-context` the five texts. Each member is `WhenWritingNull` with a `[Description]` that the MCP schema shows; `Normalize()` returns null when none is set. The text limits are constants of the record (`WidgetDisplay.MaxSubtitleLength`, `MaxFootnoteLength`, `MaxCaptionLength`).
  - `CheckDisplay` (`IndicatorValidation.cs`) refuses `color`, `ramp` and `orientation` on a kind that does not take them, skipped when the chart type is null (the query endpoint), and then calls `CheckLabels` and `CheckContext`, which have no rule per kind.
  - `ChartTypes.TakesSeries` is bar, column and line. `ChartTypes.NeedsGroup` holds bar, column, line, pie, map, heat map, pyramid, Likert and cross table but not the box plot, whose `groupBy` is optional: without it there is one box over every row.
  - The stored text of a config without a display is pinned (`WidgetDisplayTests`: `MinimalBefore`, `RichBefore` and the test that pins the texts' order in the record). `UpdateWidgetAsync` writes a config only when its normalized JSON differs.
  - The widget workbook endpoint normalizes and validates its request's config as a saved config (`WidgetExportEndpoints`, prefix `config.`), so a highlight in it is checked by the same rules.
- **UI whitelists.** `displayFor` (`widget-view.ts`) keeps `color`, `ramp` and `orientation` per kind and, on every kind, the labels and the five texts; `queryFor` leaves `display` out; `savedConfig` spreads `displayFor`. The form builds `display` in `displayDraft()` (`widget-dialog.ts`), offers a color in `offersColor`, and maps API errors in `fieldFor`, whose `display.` checks come first, before `seriesby`. Its fields run: the color and scale fields (`dialog-color`, `dialog-ramp`), then the fieldset of texts (`dialog-context`), the labels, the pyramid's sides and the scale. The pyramid's sides already offer the series column's values from a debounced query shaped as a heat map's (`seriesDomainKey` and the query that follows it, the `<input list>` fields and their `<datalist id="widgetSeriesValues">`).
- **The Colors form** (`dashboard-colors-dialog.ts`) shows eight radios per entry (`:191-220`, `places` `:51`). **Add** (`:460-476`) starts a new entry on the column before when that column has a color left, else on the first column offered that has one, in the first color the column does not use (`freeColor`, `:479-482`); when every column offered uses all eight, it starts on the column before in color 1, which the API then refuses. A column change keeps the entry's color (`setColumn`, `:432-441`).
- **Pictures, paper and email.**
  - An image clones the plot's SVG; `widget-export/svg-clean.ts` keeps a text's `font-weight`. The legend is drawn from `ExportLegendEntry { label, color, swatch?, detail? }` (`export-parts.ts`; the legend of `widget-export/image-layout.ts`, whose `measureText` measures every legend label at weight 400 and whose legend `text` writes no weight, where `paragraph` writes `font-weight` only when it is not 400). Under the legend come the notes, the footnote and the footer (`widget-export` "Parts of a widget image").
  - Printing uses the same DOM.
  - `Reports/` reads a widget's `display` for its labels, caption, unit, subtitle and footnote (`ReportDataBuilder`) and never for colors; the email, both workbooks and MCP carry the subtitle, the footnote and the source. The widget workbook request sends `savedConfig(widget.chartType, widget.config)` (`exportWorkbook` in `pages/projects/project-indicators.ts`) since `add-widget-context`, so a highlight reaches the endpoint and the builder ignores it.
- **Texts that are pinned.**
  - `ColorsHelp`, `DisplayHelp`, `ContextHelp` (the texts, said once after `DisplayHelp`, whose pinned sentence stays) and `ChartTypeChangeHelp` (`McpDashboardTools.cs`), the `valueColors` argument of `update_dashboard`, and `Mcp/Instructions.md` "Colors" and "Words on a widget". `add-chart-annotations` adds an `AnnotationsHelp` after `ContextHelp`.
  - `McpToolCatalogTests`: `Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` pins the chart type warning and "config.display = { color, ramp, orientation, labels } sets how the widget is drawn"; `The_widget_tools_name_the_texts_of_a_widget_and_the_dashboard_tools_name_its_source` pins the same sentence and the texts; `The_tools_name_the_column_labels_they_take_and_return` pins the `create_widget` template; `The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` expects `minimum` 1 on both color fields (its local `AssertColor`) and the words of the `display` description ("How the widget is drawn", "subtitle", "footnote", "caption", "unit", "description button").
  - `IndicatorDocsTests` pins the `docs/mcp.md` sentences (`The_color_settings_are_explained_in_the_mcp_documentation_…`, `The_mcp_documentation_warns_that_changing_the_chart_type_…` and `…gives_the_chart_type_sentence_to_update_widget_only_…`) and the duplication sentence of `docs/indicators-dashboard.md` (`The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them`); `McpGuideTests.The_instructions_say_when_to_keep_the_palette_keep_colors_by_value_and_give_a_widget_its_own_color` pins the instructions, among them "for categories that recur across widgets, such as sex or yes/no" and "send the `display`, labels included, again when you change the query".
  - The refusal messages of the Colors rules are pinned in `DashboardValidationTests` (`A_color_outside_1_to_8_is_refused_on_the_entry`, `A_color_used_twice_in_a_column_is_refused_on_the_second_entry`), `DashboardColorsTests.A_color_used_twice_in_a_column_is_refused_on_the_second_entry_and_nothing_is_saved` and `McpDashboardToolsTests.Colors_refused_carry_the_keys_and_messages_of_the_page_and_save_nothing`. The same words for a widget's `display.color` (`IndicatorValidationTests`, `DashboardColorsTests`, `McpDashboardToolsTests`) are another rule and do not change.
  - `PalettesTests.cs:150-174` keeps the page's limits equal to the API's.
- **Tokens.** `chart-tokens.spec.ts:380-383` pins the neutral at `#898781`, and `:400-404` checks that each color of every palette, in both themes, stays clear of it.

## Goals / Non-Goals

**Goals:**
- One rule per mark, resolved in `chart-colors.ts`, so a chart, its legend, its tooltips, the form's preview, the printed sheet and the image agree.
- Nothing stored changes: a widget or a dashboard saved before this change reads, draws and serializes exactly as today.
- An older release that reads what this one stores never fails: it draws without the new settings, and refuses only to save a list of colors that holds the neutral (see Migration Plan).

**Non-Goals:**
- A highlight that changes the rows: it never enters `queryFor` or `WidgetQuery.For`.
- Marking highlighted rows in "Show as table", the scheduled email or the workbooks.
- Changing which kinds take colors kept by value.

## Decisions

### D1. The neutral color kept by value is the number 0 (`Palettes.Neutral`)

An entry `{ "column": "respuesta", "value": "4. No sabe", "color": 0 }` keeps the neutral grey. The API gains `Palettes.Neutral = 0` and `Palettes.IsKeptColor(int?) => color == Neutral || IsColor(color)`. `IsColor` stays 1 to 8: a widget's own `display.color` cannot be the neutral. The UI gains `neutralPlace = 0` next to `paletteSize`.

What an older release does with it:
- **Reading** never fails: `ReadValueColors` reads 0 into the `int`.
- **Drawing** ignores it: the older `chartColors` skips an entry that is not a place 1 to 8 (`chart-colors.ts:109-121`), so the value is drawn as if nothing were kept for it.
- **Saving** a list that holds it is refused cleanly, with a 400 on `valueColors[i].color` ("The color must be a number from 1 to 8."), in the Colors form and in `update_dashboard`.

*Alternatives:*
- *`"color": "neutral"`.* Rejected: `ValueColor.Color` is an `int`, so an older API would throw a `JsonException` reading the stored list, and every read of that dashboard would answer 500.
- *A separate `"neutral": true` with no color.* Rejected: two ways to say one color. An older API would read the missing color as 0 anyway.
- *A ninth palette slot.* Rejected: it would change the four validated palettes and their checks, and "Other" already has a neutral that every palette is checked against.

### D2. One neutral: the grey of "Other"

A value kept as 0, a value a highlight leaves out, the missing value under a highlight, and "Other" are all drawn in `var(--tmz-series-other)`. The UI exports it as `neutralColor`. No token is added, so the initial bundle and `chart-tokens.spec.ts` do not change.

- Every palette color is already checked to stay clear of this grey in both themes (`chart-tokens.spec.ts:400-404`), so a kept color, a highlighted value and a series' own color stay apart from the neutral.
- Several values can now share it. That is the point of a neutral, and names, legends, tooltips and "Show as table" tell them apart. Touching fills keep the 2-pixel surface gap between them (`surfaceGap`, `series-plot.ts:93-94`), so two grey segments of a stack still read as two.

*Alternatives:*
- *A second, lighter grey for kept values.* Rejected: two greys next to each other are hard to tell apart, and it would need its own checks in both themes.
- *A neutral per palette.* Rejected: the specs already promise one neutral for every palette ("Palettes").

### D3. Validation of colors kept by value (`DashboardValidation.Colors`)

| Rule | Key | Message |
|---|---|---|
| Color not 0 to 8 | `valueColors[i].color` | "The color must be a number from 1 to 8, or 0 for the neutral grey." |
| A palette color (1 to 8) repeated in a column | `valueColors[i]` | "{column} already uses color {n}: a column keeps each of the palette's 8 colors for one value only. Keep the neutral grey (0) for more values." |
| A value repeated in a column, whatever its colors | `valueColors[i]` | "{column} already keeps a color for {value}." (unchanged; "…for an empty text." for the empty text) |
| More than 64 entries | `valueColors` | "A dashboard keeps at most 64 colors by value." (unchanged: it names its limit) |

- The neutral is never added to a column's set of used colors, so any number of values of a column may keep it.
- It counts toward the 64 entries like any other.
- The REST endpoint, MCP and the page's form share the function, so all three answer the same.

### D4. `display.highlight` (`IndicatorModels.cs`, `IndicatorValidation.CheckDisplay`)

- **Field.** `WidgetDisplay` gains `[JsonIgnore(WhenWritingNull), Description(…)] IReadOnlyList<string?>? Highlight = null`, declared last, after `ShowDescription` and after the members `add-chart-annotations` appends before it, so a display with a color and a highlight is stored as `{"color":1,"highlight":["Centro cultural o casa de la cultura"]}` and the earlier members keep their place in the JSON.
- **Normalize.** An empty list is none. A null item is kept so validation can name it, as tooltip items are. The values are kept as given: not trimmed, not sorted, not deduplicated, since they are matched as exact text and a version compares the stored text. `Normalize()` returns null only when every field of the display, the highlight included, is null (today color, ramp, orientation, labels and the five texts; `add-chart-annotations` adds its three), so a display that holds only a highlight is kept. Unlike the texts, which are trimmed, the values are data and are not.
- **Which column.** `ChartTypes.TakesHighlight(chartType, groupBy, seriesBy)` is true for a bar or column chart (with or without a series), a pie chart, a box plot with a group column, and a line chart with a series. The values are of the series column when the widget has one, else of the group column. A box plot with no group column has one box and nothing to set apart, so it is refused as a line with no series is. The UI twin `takesHighlight(type, config)` lives in `indicators.service.ts`.
- **Limit.** `WidgetDisplay.MaxHighlight = 20`, beside the text limits, and `maxHighlightValues = 20` in the UI (`core/projects/indicators.service.ts`, beside `maxSubtitleLength`).
- **Validation.** `CheckDisplay` calls a `CheckHighlight` after `CheckLabels` and `CheckContext`. Unlike them it has per-kind rules, skipped when the chart type is null.
- **Values.** Values are compared as the group or series value's text, exactly as colors kept by value compare them (spec "Values compared as text"), so a number, a true/false or a timestamp group is written as the chart labels it. They are not checked against the table, as kept values are not.

| Rule | Key | Message |
|---|---|---|
| More than 20 values | `config.display.highlight` | "A widget highlights at most 20 values." |
| A null value | `config.display.highlight[i]` | "A highlighted value is text; an empty text is a value too." |
| Longer than 500 characters | `config.display.highlight[i]` | "The value is longer than 500 characters." |
| A NUL character | `config.display.highlight[i]` | "The value cannot contain a NUL character." |
| A value given twice | `config.display.highlight[i]` | "{value} is already highlighted." ("The empty text is already highlighted.") |
| A kind that takes none (skipped when the chart type is null) | `config.display.highlight` | "Only bar, column, line, pie and box plot widgets highlight values." |
| A line chart with no series | `config.display.highlight` | "A line chart highlights its series: give it a series, or leave out the highlight." |
| A box plot with no group column | `config.display.highlight` | "A box plot highlights its groups: give it a group column, or leave out the highlight." |

The 500 characters are `DataQueryBuilder.MaxTextLength`, the limit of a kept value.

*Alternatives:*
- *`{ "column": …, "values": [...] }`.* Rejected: the column is always the one the widget colors by, and a second copy of it could disagree with `seriesBy` or `groupBy`. The form handles a change of column (D7).
- *A top-level config field.* Rejected: it would enter the query key and `WidgetQuery.For`, and a highlight never changes the rows (program rule 2).
- *Checking the values against the published table.* Rejected: a dashboard filter, a later run or a script edit can make a value come and go, and kept values are not checked either.

### D5. Resolving colors with a highlight (`chart-colors.ts`)

`ChartColors` changes:

```ts
export const neutralColor = 'var(--tmz-series-other)';

interface ChartColors {
  accent: string; ramp: ScaleId; rampStyle: Record<string, string>;
  /** As today, then: with a highlight, every value it leaves out ("(none)" included) is the neutral. "Other" stays the neutral. */
  forValues(column: string, values: readonly SeriesKey[]): string[];
  /** The color kept for a value (the neutral for an entry kept as 0), or null; for a bar or column with no series and a box. */
  kept(column: string, value: string | null): string | null;
  /** The color of a bar or column of a chart with no series, or of a box: the neutral when a highlight leaves it out, else kept ?? accent. */
  mark(column: string, value: string | null): string;
  /** Whether the widget highlights `value`, so its name is written in bold; false for every value of a widget with no highlight. */
  highlighted(value: string | null): boolean;
  /** The values the widget highlights, as saved; empty when it highlights none. */
  highlight: readonly string[];
}
```

- `chartColors` reads `display.highlight` only when `takesHighlight(type, config)`, so a list saved on another kind (which the API refuses) draws nothing.
- A kept entry is read when its color is 0 or a place 1 to 8. A neutral entry sets no palette color aside in `forValues`, so the free colors are handed out as if it were not there.
- `forValues` first computes the colors exactly as today, over the whole domain, then replaces with the neutral every value the highlight leaves out. A highlighted value therefore keeps the very color it has without the highlight, and setting or clearing a highlight never recolors it.
- `markColor` in `axis-chart.ts` (its plain layout) and the `color` callback of `boxplotPlot` in `boxplot-chart.ts` call `mark(groupBy, group)` in place of `kept(…) ?? accent`. A line with no series still draws in `accent`.
- A box plot's key keeps drawing its glyphs in `accent` (the key's glyph SVGs in `boxplot-chart.ts`): it explains the marks' shapes, not their colors.
- The reference lines and data labels of `add-chart-annotations` are ink, not palette colors, and are drawn after the marks. They cross grey and highlighted marks alike and never take the neutral, and a data label writes a value, not a name, so it stays as it is (the bold of D6 is for names).

| The value | No highlight | Highlighted | Left out by a highlight |
|---|---|---|---|
| Kept as a palette color n | n | n | neutral |
| Kept as the neutral (0) | neutral | neutral | neutral |
| No color kept, chart with no series or a box plot | widget color | widget color | neutral |
| No color kept, series or pie slice | next free color | the color it has with no highlight | neutral |
| "(none)" | as today | cannot be highlighted | neutral |
| "Other" | neutral | cannot be highlighted | neutral |

**Lines.** `seriesPlot` takes `highlighted: readonly boolean[]`, one per series, all false with no highlight. `linePlot` paints the paths and dots of the series a highlight leaves out first, then the highlighted ones, so a highlighted line is never hidden under a grey one; with no highlight it paints in series order, as today. `marks` and `hits` keep the series order, so the keyboard, the tooltips and the legend do not change. Bars and slices do not overlap and keep their order. Reference lines, when `add-chart-annotations` is implemented, are drawn after every path and dot, the highlighted ones included.

*Alternatives:*
- *Hand the highlighted values the free colors 1, 2, … among themselves.* Rejected: setting or clearing a highlight would recolor the highlighted series, and a value would read in another color than in the dashboard's other widgets.
- *Draw the values left out in the widget's color, not the neutral.* Rejected for this change: the program's rule is the neutral, and a single bar in another color with the rest in the widget's color is already a color kept by value. See Open Questions.

### D6. Not by color alone

- `Label` (`series-plot.ts:18-26`) gains `strong?: boolean`. The `<text>` of the chart with no series (the loop over `layout().labels` in `axis-chart.ts`) and of the box plot (the loop over `plot().labels` in `boxplot-chart.ts`) writes `font-weight="600"` when it is set. That is a presentation attribute, so no Tailwind utility is added, and `widget-export/svg-clean.ts` (its `font-weight` branch) carries it into images.
- `LegendEntry` gains `strong?: boolean`. `series-legend.ts` writes its name `font-semibold text-heading`, and the pie's legend item does the same.
- `ExportLegendEntry` gains `strong?: boolean`. The `parts()` that `axis-chart.ts` and `pie-widget.ts` register (`registerExportParts`) set it from `highlighted`, and `image-layout.ts` measures and writes that label at weight 600 (today `measureText` in the legend measures every label at 400 and the legend's `text` writes no weight; like `paragraph`, it writes `font-weight` only when the weight is not 400). The box plot's image key explains marks, not values, and its bold group labels come with the cloned plot.
- The accessible name of the axis chart (`summary(t)`, with and without a series), of the pie and of the box plot ends with `indicators.chart.highlighted` ("Highlighted: {{ values }}.", with its Spanish twin in `es.json`), listing the highlighted values the chart draws, in its order, after any sentence an earlier change appends (the reference lines of `add-chart-annotations`). A widget with no highlight keeps its accessible name as it is.
- Label widths are estimated with `charWidth`, and semibold text is a few percent wider. A strong label is therefore truncated as if each character took `charWidth × 1.08`, so it stays inside the label column of a bar chart and the band of a column chart; the other labels keep today's estimate. A bold legend entry is also a little wider, so a legend can wrap a row earlier and take about 16 px more from the plot, which a tile with texts has less of (`add-widget-context` D5 measured a plot area of 233 px in a 6 × 4 tile, 199 px with a subtitle and a one-line footnote). The real-stack check (task 9.4) looks at the longest Teatro labels and at such a tile.

### D7. The widget form (`widget-dialog.ts`)

- **Field.** **Highlight** follows the color and scale fields and comes before the fieldset of texts (`dialog-context`), shown when `takesHighlight(chartType(), formConfig())`. A chart with a series has no Color field, so there it follows the series settings.
  - A help line names the column: "Values of {{ column }} to set apart. They keep their color; every other value is drawn in grey."
  - A list of rows, each a native combobox (`<input list>` with a `<datalist>`, as the pyramid's sides are) and a **Remove** button.
  - **Add a value**, off at 20 with "A widget highlights at most 20 values." next to it.
  - Each row is named "Highlighted value {{ number }} of {{ total }}", as the Colors form names its entries.
- **Values offered.** They are the values the widget plots under its own filters, read by one debounced query in the pattern of `seriesDomainKey`:
  - with a series, the series of `queryFor('heatmap', config)`, as for the pyramid;
  - with no series, the groups of the widget's own query, without the dashboard filters (the boxes' groups for a box plot);
  - in the order the widget draws them, "(none)" left out.

  The query runs only while the field is shown. A value can also be typed: a dashboard filter or a later run can change the values.
- **One list per column.** A signal `highlights: Map<string, string[]>`, seeded with the saved list under the column the saved widget colors by (`seriesBy` when it has one, else `groupBy`). The draft's `highlight` is the list of the column the form now colors by. Changing the group or series column and coming back finds the list again, as a color is kept across kind changes. Saving sends only the current column's list, through `displayFor`, so a kind that takes none saves none.
- **Rows.** A row left empty is refused by the form ("Pick or type a value."), as in the Colors form. Only the API and MCP can highlight the empty text.
- **Draft and preview.** `displayDraft()` carries `highlight` next to color, ramp and orientation, and `displayFor` keeps it for the kinds of D4. The preview redraws from the draft. `queryFor` leaves `display` out, so nothing is asked again.
- **Errors.** `fieldFor` maps `display.highlight` to the field, and `display.highlight[i]` to the row it names, in its block of `display.` checks, before `seriesby`.
- **Color help.** With a highlight on a chart with no series, the color help adds "Highlighted bars are drawn in this color."

*Alternative:* a checkbox per value the widget plots. Rejected: a group column can have hundreds of values, and a saved value that no longer appears in the data still needs a row of its own.

### D8. The Colors form (`dashboard-colors-dialog.ts`)

- After the eight radios of an entry comes a ninth with `[value]="0"` and `data-color="0"`: a swatch in `neutralColor` and the visible word "Neutral", read to a screen reader as "Neutral grey" (`dashboardColors.kept.neutralOption`). The arrow keys reach it as they reach the others: native radios. It is one more `dashboard-colors-color` radio of the entry's group, so the two form tests that count eight choices per entry gain the ninth in task 7.3: "Named choices" (`dashboard-colors-dialog.spec.ts:549`) and the keyboard test (`:710`, radio groups of 4 and 8).
- **Add** starts a new entry on the column before when it is offered; when it is not, on the first column offered that has a palette color left, else the first column offered, as today. The entry starts in the first palette color its column does not use (`freeColor`, unchanged), else in `neutralPlace`. So **Add** never starts an entry the API will refuse.
- **A full column, decided with the user on 2026-11-04** (the recommended option). After an entry of a column that already uses all eight palette colors (`respuesta` in "8. Cruces"), **Add** used to move to another column that had a color left. It now stays on that column and starts the new entry on the neutral grey, since a ninth value of the same column is what the member is most likely adding, and the reason to move (the API's refusal) is gone. Nothing stored changes.
  - The form's test of the old move, "adds an entry on another column when the one before already uses all eight colors, which the API would refuse" (`dashboard-colors-dialog.spec.ts:433`), is pinned to the old behavior and is rewritten in task 7.3.
  - The tests at `:401` (the first free color of the column before), `:413` (an entry after one on a column no table offers lands on the first column offered) and `:446` (the first entry starts in color 1) keep their behavior and pass unchanged.

  *Alternatives:*
  - *Keep moving to another column with a color left, as today.* Rejected with the user: the member then has to change the column back by hand, and only to reach the grey that the column can now keep.
  - *Stay on the column in color 1, as when every column is full today.* Rejected: the API refuses a palette color the column already uses, so the entry would be wrong from the start.
- A column change keeps the entry's color, as today; a palette color the new column already uses is refused on save with the message of D3, which points to the neutral.
- `dashboardColors.kept.intro` names the boxes of a box plot and the neutral.
- The palette cards keep their eight swatches.

### D9. MCP (`Mcp/`)

- **`ColorsHelp`** and the `valueColors` argument of `update_dashboard` ("with color 1 to 8" in both): valueColors' `color` is 1 to 8 "or 0 for the neutral grey". It names where a kept color applies: the series of a bar, column or line chart, the sides of a pyramid, the slices of a pie, and, by their `groupBy` value, the bars of a bar or column chart with no `seriesBy` and the boxes of a boxplot. It says "a column keeps each of the 8 colors for one value; 0 for any number of values; a dashboard keeps at most 64". Its last sentence ("Names are matched exactly, … so female does not color Female."), pinned by `McpToolCatalogTests`, stays.
- **A new constant, `HighlightHelp`,** follows `ContextHelp` (and `AnnotationsHelp`, when `add-chart-annotations` is implemented) in the descriptions of `create_widget` and `update_widget`, as `ContextHelp` follows `DisplayHelp`. It says: "config.display.highlight is up to 20 values, as exact text, that keep their color while every other value is drawn in the neutral grey: values of groupBy for a bar, column or pie, or a boxplot with a groupBy, when there is no seriesBy, and values of seriesBy for a bar, column or line with one; a line with no seriesBy, a boxplot with no groupBy and the other kinds take none. It changes that widget only; to give a value one color in every widget, keep a color by value on the dashboard." `DisplayHelp` stays as it is: its first sentence, "config.display = { color, ramp, orientation, labels } sets how the widget is drawn", is pinned by two `McpToolCatalogTests` cases, and `add-widget-context` and `add-chart-annotations` also keep it.
- **The `create_widget` template** (`display: { color, ramp, orientation, labels, subtitle, footnote, caption, unit, showDescription }`) lists `highlight` last, after the keys the earlier changes add, and the `config` arguments of `create_widget` and `update_widget` ("its display (color, scale, orientation, column labels or texts)") name highlighted values too. The `query_indicator` template (`display: { labels }`) does not change: a preview returns nothing a highlight changes.
- **`ChartTypeChangeHelp`** ("a color, a scale or an orientation" and "config.display.color, config.display.ramp or config.display.orientation" today) names "highlighted values" and the key `config.display.highlight` after the settings and keys the earlier changes add there (`add-chart-annotations`: reference lines, steps and data labels).
- **Schema.**
  - `ValueColorInput.Color` gets `[Range(0, Palettes.ColorCount)]` and the description "A place in the dashboard's palette, 1 to 8, or 0 for the neutral grey; never a color code."
  - `WidgetDisplay.Highlight` gets a description that names 20, the neutral and the column, with no enum.
  - `WidgetConfigInput.Display`'s description (today "How the widget is drawn and what it says: its color, its scale, its orientation, the names of its columns, or its subtitle, footnote, caption, unit and description button. It never changes the rows.") names highlighted values too, for example after the orientation. The words `McpToolCatalogTests` pins stay: "How the widget is drawn", "subtitle", "footnote", "caption", "unit" and "description button".
- **Results.** `get_dashboard` and `get_dashboard_version` return the config and `valueColors` as stored, so the highlight and 0 come back with no code. `get_widget_data` and `query_indicator` do not change: a highlight is a drawing aid, as a pie's domain is.
- **`Instructions.md` "Colors"**:
  - the kept-colors bullet says where they apply, the bars of a `bar` or `column` with no `seriesBy` and the boxes of a `boxplot` included, and that `color` 0 keeps the neutral grey for answers such as "No sabe" for any number of values;
  - a new bullet: "To set one or a few values apart in one widget, give it `display.highlight`; the others turn grey. Keep colors by value for what should read the same in every widget";
  - the `display` sentence names `highlight`.

  The pinned phrases of `McpGuideTests.The_instructions_say_when_to_keep_the_palette_…` stay, among them "for categories that recur across widgets, such as sex or yes/no", "Keep color 1 for a widget unless it should stand out" and "send the `display`, labels included, again when you change the query"; the sentence "A column keeps each value once and each color once." says instead that a column keeps each of the 8 colors once and the neutral grey for any number of values. "Words on a widget" stays as `add-widget-context` left it.
- **Pinned sentences.** `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` (the chart type warning), the `create_widget` template assertion of `The_tools_name_the_column_labels_they_take_and_return`, and `IndicatorDocsTests.The_mcp_documentation_warns_that_changing_the_chart_type_…` and `…gives_the_chart_type_sentence_to_update_widget_only_…` change in the same task as the texts. The display sentence (`config.display = { color, ramp, orientation, labels } sets how the widget is drawn`) is left alone; a new sibling test checks the words of `HighlightHelp` and the neutral and "where kept colors apply" words of `ColorsHelp`, as `add-chart-annotations` and `add-declared-category-order` check theirs. The local `AssertColor` of `The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description` takes the minimum: 0 for a kept color, 1 for `display.color`.

### D10. Paper, images, emails and workbooks

- **Printing** uses the page's DOM, whose marks already print their colors with background graphics off (`dashboard-printing` "Colors on paper"); a font weight prints as it is. `dashboard-print.spec.ts` covers it.
- **Images** clone the plot (fills, strokes and `font-weight` kept by `widget-export/svg-clean.ts`) and draw the legend from `ExportParts`. The neutral resolves through `input.resolve` like any token, and the legend's `strong` comes from D6. The parts of an image and their order are those of `widget-export` "Parts of a widget image" (title, subtitle, notes above, caption, chart, whole legend, notes below, footnote, footer); a highlight adds no part, it changes the legend's weight and the colors.
- **Scheduled emails** build tables from `WidgetQuery.For` and read a widget's `display` only for its labels, caption, unit, subtitle and footnote (`ReportDataBuilder`), never its colors or its highlight, so their body, text and workbook do not change. A `ReportDataTests` case pins that a widget with a highlight, on a dashboard that keeps a neutral, builds the same table as without.
- **Widget workbook.** The request has sent `savedConfig(widget.chartType, widget.config)` since `add-widget-context` (`exportWorkbook` in `project-indicators.ts`), so a highlight is in the request. `WidgetExportEndpoints` normalizes and validates it as a saved config (prefix `config.`), and the builder (`ReportDataBuilder.BuildWidgetExportAsync`) reads from `display` only the labels, caption, unit, subtitle and footnote, so the data sheet and the "Summary" (`widget-export` "Summary of a widget workbook": source, subtitle, indicator, what is plotted, filters, dates, rows, footnote and notes) are the same with a highlight as without. Task 3.1 pins it with the display sent.

### D11. Versions, duplicates, moves and restores

No code:
- a version embeds each widget's stored config and the stored `valueColors` when not empty (`DashboardVersions.Content`), and a restore writes them back (`RestoreOnceAsync`);
- `DuplicateAsync` copies the raw `ValueColors` and each widget's raw config;
- a moved widget keeps its config.

A widget with no highlight serializes as before (D4), so its next save makes no version. Tests pin each of these (tasks 1.4, 2.4 and 7.2).

### D12. The production-shapes fixture

The fixture of program rule 10 (`indicators/testing/production-shapes.json`, created by `add-indicator-column-metadata`, extended by `add-widget-context`, run by `ProductionShapesTests` and `production-shapes.spec.ts`) holds 20 shapes, the entries `dashboard` and `dashboardWithSource`, and invented names (`encuesta_hoy`, `zona`, `canal`, `grupo`, `pregunta`, `respuesta`, `categoria`, `precio`), never the study's. It already has a box plot grouped by `grupo` (whose values the `dashboard` entry keeps colors for), a column chart in color 2, a percent column chart with a series, a line with a series and a pie, and the `dashboard` entry keeps colors on `pregunta` and `grupo`, all 1 to 8. This change adds the shapes it touches that the fixture lacks, with invented values that keep the structure:
- a bar chart with no series grouped by `pregunta`, sorted by value, with `display.color` 2, under the colors the dashboard keeps for `pregunta`, as 9b11;
- a pie grouped by `grupo`, one value of which has a color kept, as b296;
- a line with the series `grupo`, one value of which has a color kept, as parqueadero 77ea;
- a 100 % bar over a count with the series `pregunta`, as 8c4b, unless `add-declared-category-order` has added one of that structure first, which this change then reuses;
- a list of colors kept like "8. Cruces'", as an entry of its own (`eightColors`): one column that keeps all eight palette colors, which still validates and serializes as stored now that the neutral sits outside a column's used colors.

The goldens of the new shapes are written from the code of the task that adds them, as `add-chart-annotations` writes its own; every existing shape keeps its golden. The runners pin the number of shapes (20 after `add-widget-context`) and the list of their chart types; the count each runner pins when this change starts depends on the changes before it (25 after `add-chart-annotations`, more with the shapes `add-declared-category-order` adds), so tasks 2.6 and 6.5 raise it by what they add and record the total.

Checks:
- the API side checks, as it does for every shape, that each config normalizes to byte-identical stored JSON and gives the same `WidgetQuery.For` and plan, and adds that the colors lists (the `dashboard` entries and `eightColors`) pass `DashboardValidation.Colors` and serialize byte for byte as stored;
- the UI side checks the same `queryFor` and `savedConfig`, and pins the colors `chartColors` gives each shape (`accent`, `kept(group) ?? accent` per group, `forValues` per domain) as they are before this change; after it, `mark(group)` must give what `kept(group) ?? accent` gave.

### D13. Documentation

- `docs/indicators-dashboard.md`:
  - "Colors kept by value": the neutral grey and its rule, the new messages, and a plain sentence that a bar or column chart with no series and a box plot take kept colors by their group value. Its bullet "so one value can be highlighted" says "set apart" instead, so the word "highlight" means only `display.highlight`, and its sentence that a bar with no color kept is drawn in the widget's own color adds "unless the widget highlights values";
  - a new `### Highlighted values` under "Colors": what it does, the kinds, the column, the limit, the neutral for the rest, bold names, and when to keep a color by value instead (the same value in every widget) or highlight (one widget only), with the Teatro example;
  - "Widget color and scale": the highlighted bars take the widget's color, and the bars a highlight leaves out are grey;
  - "Images" (the bullet on white and the light colors) and "Printing a dashboard" (the bullet on the browser's options, where colors print without background graphics): a sentence each on the highlight, the grey and the bold names; "Workbook" and the scheduled email need none, since they list every row as before;
  - "Limits": highlighted values per widget, 20;
  - "What it does not do": no highlight on maps, heat maps, Likert bars, cross tables, pyramids, histograms, stat tiles, lines with no series and box plots with no group column, one neutral only;
  - a sentence that a duplicated, moved or restored widget keeps its highlight, apart from the duplication sentence `IndicatorDocsTests.The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them` pins.
- `docs/mcp.md`, the **Colors** paragraph and its bullets: `color` 0, `display.highlight`, the keys `config.display.highlight` and `config.display.highlight[i]`, and the chart type sentence; and the `widgetChanged` row of "The kinds of change", whose display list ("color, scale or column labels") gains highlighted values.
- `docs/tamiza-brief.md`: section 6 says in the `dashboards` row that `value_colors` holds the palette's 8 colors by number, or 0 for the neutral, and in the `dashboard_widgets` row (after the sentence on the texts) that a widget can keep a `display.highlight`; section 8 marks this change done in the program's list.

## Spec ownership

- **Modified, as owner** (program section 3): `dashboard-colors` "Colors kept by value", "Where colors kept by value apply" and "Colors of values with no color kept". Each keeps its whole current text, with the changes below, and every current scenario under its name and with its body unchanged:
  - "Colors kept by value": an entry's color may be the neutral, and "a value and a color SHALL each appear at most once" becomes "a value and a palette color", the one rule the neutral breaks on purpose. The new scenario "Value kept twice with the neutral" shows that the value rule still holds. What the refusals say is its own requirement, "Color limits named in refusals".
  - "Where colors kept by value apply": "unless a highlight leaves it out", and the scenario "Kept color outside a highlight".
  - "Colors of values with no color kept": "other than the neutral" and "unless a highlight leaves it out", and the scenario "Bar left out by a highlight".
  - The scenarios "One bar highlighted" and "One box highlighted" keep their titles, as the program's rule for a MODIFIED block requires; they are about a color kept by value, not `display.highlight`, and the documentation says "set apart" there (D13).
- **Owned and left as it is:** "Color choice in the widget form". The widget's eight colors do not change. The highlight field is its own requirement, "Highlighted values in the widget form".
- **Not modified, because nothing they state is contradicted:**
  - `dashboard-colors` "Palettes": the neutral is the one "Other" has, the same in every palette, and no color code is typed;
  - `dashboard-colors` "Widget color": highlighted bars are in the widget's color, and the exception for the bars a highlight leaves out is written in "Colors of values with no color kept";
  - `dashboard-colors` "Colors of a dashboard": entries still have a column, a value and a color; the neutral choice is "Neutral color in the Colors form";
  - `dashboard-colors` "Values compared as text": the highlight refers to it;
  - `dashboard-indicators` "Series order and colors" and "Pie chart" (owned by `add-declared-category-order`), and "Box plot drawn": all three say their colors follow `dashboard-colors`, where the highlight lives;
  - `dashboard-indicators` "Marks of a widget with a series" (owned by `add-small-sample-warnings`): legends and tooltips stay, and the highlight adds bold names;
  - `dashboard-printing` "Colors on paper" and `widget-export` "Colors of widget images": both are written for any color drawn.
- **Lists of settings** in `dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard" and `dashboard-indicators` "Moving a widget to another dashboard" belonged to `add-indicator-column-metadata`, which rewrote them generically when it was archived ("display settings"), so a highlight is covered. This change does not touch them. It states its own rule in "Highlighted values saved with the widget".
- **MCP**: "Dashboards and widgets through MCP" is owned by nobody, so the change adds "Highlighted values and the neutral color through MCP".
- **Added by earlier changes and only referenced:** `add-declared-category-order`'s "Colors follow a declared category order" (dashboard-colors) reads the order in which "Colors of values with no color kept" hands out free colors; the highlight computes its colors over that same order and only greys values afterwards (D5), so the two hold together.
- **Names.** Every added requirement starts with "Highlighted values", "Neutral color" or "Color limits", which no other change of the program uses (checked against the delta specs of the other P1 changes).

## Production impact

The production facts of the program (section 5) this change touches, and why they stay as they are:
- **47 colors kept by value in 7 dashboards**, all 1 to 8: none is 0, so they validate, serialize, draw and version as today. Only the wording of a refusal changes.
- **Box plots Centro a137 and parqueadero 8492** colored by `vehiculo`, and **the Escuelas pie b296** with `categoria` "Culturama (escuelas de formación)" in color 1 and its other slices in 2, 3 and 4: no widget has a highlight, so `mark` and `forValues` answer what `kept` and `forValues` answer today (pinned by the fixture, D12).
- **50 widgets with `display.color` and 13 with `display.ramp`**: `highlight` is written only when set, so their stored text and version content stay byte for byte.
- **Numbered labels** ("1. Sí", "4. No sabe", "2. Maneja moto o carro") match kept colors and filters as full text. A highlight matches full text too, so "4. No sabe" is highlighted only as "4. No sabe". `add-declared-category-order`'s prefix hiding is display-only and leaves the match alone.
- **Kept colors are dashboard-wide by column name** (`categoria` shared by `teatro_hoy`, `teatro_intencion`, `teatro_barreras`): unchanged. The highlight is the per-widget alternative, opt-in.
- **Likert bars** (6 widgets) keep their own neutral and arms: kept colors, the neutral included, never reach a Likert widget.
- **The stat cards a6df and b219, and the count twins colored by `barrera_tipo`**: unchanged. Authors may opt in to a highlight; coloring by another column stays P2.
- **"8. Cruces" keeps all eight palette colors on `respuesta`**: its list still validates and serializes byte for byte (D12). In **Colors**, **Add** after one of those entries now starts on `respuesta` with the neutral, where it used to move to another column (D8, decided with the user on 2026-11-04); nothing is saved until the member saves.
- **MCP clients** see longer descriptions. Every response is the same until a highlight or a neutral is saved, and then only `get_dashboard` and `get_dashboard_version` echo them. Two refusal messages of the colors kept by value are longer (D3); their keys do not change.

## Risks / Trade-offs

- **[A neutral value and "Other" read alike]** → Legend names, tooltips and "Show as table" tell them apart, and a widget folds into "Other" only past 8 series. The documentation says the neutral is the grey of "Other".
- **[Grey segments side by side in a stacked bar read as one]** → The 2-pixel surface gap between touching fills stays, and tooltips name each segment. The documentation suggests the neutral for few values, such as "No sabe" and "Sin dato".
- **[An older release refuses to save a dashboard's colors that hold a 0]** → The refusal names the entry and is clean (D1). The Migration Plan gives the query that finds such dashboards before a rollback.
- **[An MCP client adds a series, or changes the group column, of a widget with a highlight]** → The API cannot know the column the values came from, so every value may turn grey. That is visible at once, and the tool descriptions say whose values the highlight lists. The page's form keeps one list per column (D7). Removing the series of a highlighted line, or the group column of a highlighted box plot, is refused on `config.display.highlight` (D4).
- **[Several changes of the program extend the same MCP help texts]** → This change puts its words in a constant of its own, `HighlightHelp`, after `ContextHelp` (and `AnnotationsHelp`), and appends one setting to `ChartTypeChangeHelp`, after the earlier changes' words, and checks its own words in a sibling test (D9), so a rebase touches only the chart type sentence that every change extends.
- **[Bold labels are wider than the estimate]** → The label column has slack, and the real-stack check measures the longest Teatro labels, and a legend that wraps a row earlier in a tile with texts (D6).
- **[A highlight saved with values no row holds draws everything grey]** → The form offers the values the widget plots, and the accessible summary names only the highlighted values the chart draws.

## Migration Plan

1. **Deploy** the api and the ui together, as always. No database script runs, and none takes a number (the last applied is `0022`). Stored widgets and dashboards are read and drawn as before (D12 pins it).
2. **Rollback** (program rule 7):
   - **Highlights.** An older API ignores `display.highlight`: System.Text.Json drops the unknown member, and the older `WidgetDisplay.Normalize()` drops a display that held only a highlight. The widget draws with no highlight. A save that changes nothing else still writes nothing, since the comparison reads both sides without the field (`UpdateWidgetAsync`); the first real change of that widget, in the form or through MCP, writes its config without the field, as a version like any change. Nothing is refused.
   - **The neutral color.** An older release reads the entries, draws those values as if no color were kept, and refuses to save a list that holds a 0 (D1). That refusal includes an `update_dashboard` that names only a palette, since it resends the stored list (`SaveColorsAsync` in `McpDashboardTools.cs`).
   - **Before rolling back**, find the dashboards that keep a neutral and change those entries to a palette color or remove them:
     `SELECT id, name FROM tamiza.dashboards WHERE value_colors @> '[{"color": 0}]';`
     Highlighted widgets need nothing.
   - Versions recorded with either restore as they were saved, on either release.
   - Nothing in the database needs undoing.

## Open Questions

- Whether a later change should let a highlight draw the values it leaves out in the widget's color instead of the neutral, as acceptance case 1 words it ("the rest slate"). That case is met today with a color kept by value and `display.color` 2. The field's shape leaves room for an optional `display.highlightRest` without changing what is stored now.
- Whether the scheduled email should mark highlighted rows, for example in bold, once its tables carry any formatting of their own. Nothing here depends on it.
