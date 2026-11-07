# Proposal

## Why

A widget tile stacks up to seven texts of almost the same size, and the reader cannot tell the title from the notes. In the code as it is on 2026-11-06:

- **Title.** `text-sm font-medium` (14 px, weight 500) with `truncate`, so a long title is one line ending with "…" (`tamiza-ui/src/app/pages/projects/indicators/widget-grid.ts:97`).
- **Subtitle.** `text-xs text-body` (12 px, the body gray), one line on a tile two rows high or less and two on taller ones (`widget-grid.ts:69-76`).
- **Under the chart.** The small-base note, the base n line, the no-data line and the footnote are four more `text-xs text-body` paragraphs (`widget-view.ts:441`, `:444`, `:447` and `:455-461`).
- **Caption.** Each chart draws its own `text-xs` caption line above the plot, "% de los hogares por Comuna" (`axis-chart.ts:56`, `pie-widget.ts:63`, `map-widget.ts:79`, `heatmap-chart.ts:153`, `crosstab-widget.ts:75`), which nearly always repeats the subtitle.
- **Reference line.** Its text is written at the right end of the line with only a halo (`axis-chart.ts:157`, `chart-annotations.ts:126`), so it sits on the columns. A key under the plot then lists every line again (`series-legend.ts:55-60`, drawn at `axis-chart.ts:182-184`).
- **Stat tile.** A figure with a small base is drawn at opacity 0.35 (`stat-widget.ts:25`, `small-base-style.ts:5`). That puts the heading gray at about 2.3:1 on white, below the 3:1 WCAG asks even of large text. Its small-base note is one more line inside the tile (`stat-widget.ts:37-39`).

The study of Duitama shows what this costs. A read-only survey through MCP on 2026-11-06 covered the project "Instrumento 1 · Residentes Duitama": 9 dashboards and 187 widgets.

- **Long titles.** 78 titles pass 40 characters, 41 of them on tiles 4 columns wide or less, and are cut to one line.
- **Every widget has a subtitle and its own caption text.** In 84 of them the caption appears word for word in the subtitle (32) or nearly (52). Among the 47 bar, column and line charts with no series, 34 repeat it.
- **Short stat tiles.** 48 of the 67 stat tiles are 2 rows high, and they cut their last lines.
- **Notes moved by hand.** 70 widgets have a footnote, and authors moved notes into the subtitle so that they would fit.
- **Small bases.** Seven dashboards set the small-base threshold 30 and show the base n, so the dimmed figure and the four lines under the chart are what readers of Teatro, Escuelas and Comercio see today.

## What Changes

- **A type scale for the tile.**
  - The title is 15 px at weight 600 and takes up to two lines before it is cut. Its whole text stays in the page for screen readers and shows on pointing.
  - The subtitle is 13 px in a gray darker than today's body gray (about 10:1 on white instead of 7.6:1). It takes up to two lines, or one on a tile two rows high, as today.
  - Printing lifts both clamps.
- **The caption line leaves the top of the chart under a subtitle.**
  - On a widget with a subtitle, the line above a bar, column or line chart with no series, or above a pie, is out of sight. This holds for the default line and for an own caption text alike, since in Duitama every widget has both.
  - An own text is not lost: it moves to the widget's foot, as its first chip, with a ruler icon. On paper and in images it is the foot's first line. The default line has no chip, since the subtitle says what it said.
  - Maps, heat maps and cross tables keep their line: it alone says what their shades or cells measure.
  - Tooltips, "Show as table", the accessible name, both workbooks and the email keep naming what is plotted.
  - A bar, column or line chart with no series then writes its unit ("% de los hogares") as the title of its value axis.
  - A new setting, `caption.hidden: false`, keeps the line in sight, with the default line or with the own text. In the form it is the caption's "Keep in sight under the subtitle" checkbox, offered for bar, column, line and pie, whose help says where the text goes.
  - The form warns in amber, beside the caption and without blocking the save, while an own text is going to the foot. Through MCP, `create_widget` and `update_widget` say the same in a `notes` entry of their result, since an AI client never sees the page.
  - Unchanged: a widget without a subtitle, a hidden caption, a stat tile's label (stat tiles keep their line under the figure), a histogram and a series legend.
- **One foot of chips in place of the four lines under the chart.**
  - The chips are, in this order:
    - **caption**: the caption's own text, when the subtitle took it out of sight.
    - **n**: "n = 475 Hogares".
    - **small base**, in amber with a warning icon: "2 with a small base". Opened, it names the dimmed marks and their n.
    - **no data**: "No data 0.4%".
    - **reference lines**, only for lines whose text could not be drawn at the line.
    - **detail**: the subtitle of a compact stat tile.
    - **note**: the footnote, labelled "Note" ("Nota").
  - Each chip opens its whole text on click, on keyboard focus or on pointing. Escape closes it, and only one is open at a time.
  - Each chip is a button with `aria-expanded`, described by its whole text through `aria-describedby`, so screen readers hear it whether or not it is open.
  - The chips are Tamiza components on the Angular CDK overlay, the base of the description button (`description-popover.ts`), with which they share "one open at a time".
- **Paper, PDF and images keep every word.**
  - On paper and in the PNG and SVG image, each chip is printed as a line of its whole text through the existing `paper:` variant, as the four lines are today.
  - The scheduled email and both workbooks keep their texts as they are, the caption included: they show tables, which have no axis to carry it.
- **Stat tiles.**
  - A small base no longer dims the figure: it keeps full contrast.
  - The n always sits in the same place, the foot, on every tile and chart. The n chip is gray while the base is enough. Below the threshold in force it turns amber with a warning icon ("n = 26"), whether or not the tile shows its n, and opened it explains the base.
  - Nothing is added next to the figure.
  - On a compact tile, 2 rows high or less at any width, the subtitle moves into a "Detail" chip. A taller tile has room for two lines of subtitle and keeps it under the title.
  - What `improve-stat-cards` added stays: the statistic, `secondary` (its `n` now draws the foot's n chip) and the "Without filters" line under the label.
- **Readable labels.**
  - A reference line's text gets a box in the tile's color and goes to the free stretch of the plot along its line.
  - Data labels are drawn over the reference lines with a halo in the tile's color.
  - The key of reference lines under the chart is removed. A line whose text cannot be drawn at all goes to the reference-lines chip, and its accessible description is unchanged.
- **Compatibility.**
  - No data, query, indicator or migration changes, and every widget keeps byte-identical stored settings.
  - The one new stored value is `hidden: false` in `display.caption`, kept only when set. It goes into `WidgetCaption`, its MCP description, `create_widget` and `update_widget`, `Mcp/Instructions.md`, `docs/mcp.md` and `docs/indicators-dashboard.md`.
  - The look changes for every widget: the type sizes, the foot, the reference labels, and the caption under a subtitle.
- **Interface text.** The chips' words and the new caption option are in both `en.json` and `es.json` with Transloco. No web font is added (the CSP blocks them): the tile keeps the system font stack.

Not in this change:
- Number formats, such as "0,4 %" (`add-locale-formatting`).
- Dashboard sections (`add-dashboard-sections`).
- Highlighting values and graying the others (`add-category-highlight`).
- A reading mode apart from the editing mode. The tile still shows its handle and menu to editors.
- Folding the other notes into the foot: "Not filtered by", the row limit, series notes, and the note that a line has no overall value.
- The stat tile's label, which in 64 of the 67 stat tiles of Duitama restates the title.
- The email's layout.

## Depends on

- `add-widget-context`, `add-chart-annotations`, `add-small-sample-warnings` and `improve-stat-cards`, archived on 2026-11-04 and 2026-11-05 and committed on `main` (`3ede1a2`, `fcd8882`). This change rewrites the texts and lines they placed: the subtitle, footnote and caption, the reference line key and labels, the small-base note, the n and no-data lines, the stat tile's small-base note and dimming, and its secondary `n` line.

## Capabilities

### New Capabilities

None. The tile and its texts belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - Modified:
    - "Widget subtitle", "Widget footnote", "Widget context in a tile", "Widget context in the widget form" (the keep-in-sight checkbox) and "Widget context left unset";
    - "Reference lines drawn";
    - "Base n line under a widget", "Base lines under a widget in their place", "Small bases drawn dimmed" and "No-data line under a widget";
    - "Stat tile secondary lines".
  - New:
    - "Widget title", "Widget foot", "Widget foot chips" and "Small-base chip";
    - "Caption under a subtitle", "Caption chip", "Caption kept in sight" and "Unit on the value axis";
    - "Reference line text placement" and "Data labels over reference lines";
    - "Base n chip below the threshold" and "Subtitle of a compact stat tile";
    - "Widgets saved before the text hierarchy".
- `dashboard-printing`:
  - Modified: "Widget context on paper", "Chart annotations on paper", "Small bases on paper" and "Stat tile on paper".
  - New: "Widget foot on paper".
- `widget-export`:
  - Modified: "Parts of a widget image", "Chart annotations in widget images", "Base n and small bases in widget images" and "Stat tile in a widget image".
- `dashboard-reports`:
  - New: "Widget texts in a scheduled email kept whole".
- `mcp-server`:
  - New: "Caption kept in sight through MCP" and "Caption out of sight told through MCP".

`custom-dashboards` has no delta: duplicating a dashboard copies "every setting the widget is saved with", which holds `hidden: false`, and nothing it shows changes (design "Spec ownership").

## Impact

- **ui** (`tamiza-ui`):
  - `indicators/widget-grid.ts`: the header and its type scale, the subtitle of a compact stat tile, and the tile's columns passed to the view.
  - `indicators/widget-view.ts`: the foot, the chips' texts and the export notes.
  - A new `indicators/widget-foot.ts` (the chips and their panels) next to `description-popover.ts`, whose one-open service becomes shared.
  - `stat-widget.ts`: no dimming, no small-base note inside, and the n moved to the foot.
  - `axis-chart.ts`, `series-plot.ts` and `chart-annotations.ts`: the value-axis title, the reference text placement with its box, and no key.
  - The caption line in `axis-chart.ts` and `pie-widget.ts`, behind one rule in `format.ts`, and its own text as the foot's caption chip. `map-widget.ts`, `heatmap-chart.ts` and `crosstab-widget.ts` keep their line.
  - Every chart's `dimmed` output, which tells which marks are dimmed and not only how many.
  - `series-legend.ts` (`ReferenceKey`), `export-parts.ts` and `widget-export/image-layout.ts`.
  - `widget-dialog.ts`: the caption's keep-in-sight checkbox.
  - `styles.css`: the darker subtitle token.
  - The `en.json` and `es.json` strings, and the specs.
- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`: `WidgetCaption.Normalize()` keeps `hidden: false` instead of dropping it. `IndicatorValidation.cs` keeps its one caption rule: own text with `hidden: true` is refused, and own text with `hidden: false` is valid.
  - `Indicators/IndicatorModels.cs` also gains `WidgetCaption.InFoot`, the rule the UI uses for the caption chip, both tested against one shared case file.
  - `Mcp/McpDashboardTools.cs` (`ContextHelp`, and a `notes` entry in the `create_widget` and `update_widget` results) and `Mcp/Instructions.md`.
  - The unit tests, the production-shapes fixture and the docs test.
  - `Reports/` is unchanged and is pinned by tests.
- **Database and analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: words around a widget, room, the caption, the key of reference lines, the lines under a widget, stat tiles, printing and images.
  - `docs/mcp.md`.
