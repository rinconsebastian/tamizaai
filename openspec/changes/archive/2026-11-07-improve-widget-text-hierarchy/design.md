# Design

## Context

See proposal.md for the motivation and the specs for the behavior. **Visual reference:** [`mockup.html`](mockup.html), a copy of `docs/mockups/propuesta_jerarquia_widgets.html`. It draws three cases from Duitama, today and proposed, on screen and printed:

1. the bar chart by comuna with a total line and small bases;
2. the column chart by estrato;
3. two stat tiles.

It ends with the type scale. Its figures keep today's number format, so that only the hierarchy changes.

Where the mockup and this design differ, the design wins:

- **Subtitle color today.** The mockup gives today's subtitle the gray `#6b7280` (gray-500). The code already uses `text-body`, gray-600 `#4b5563`, about 7.6:1 on white. "A darker gray than today" is therefore gray-700 (D1).
- **Reference lines.** The mockup folds the reference line into the n chip's text. This design keeps the line's text at the line and uses a reference-lines chip only for a text that does not fit (D7).

The change was written against `main` at `f0d0786`, after `add-widget-context`, `add-chart-annotations`, `add-small-sample-warnings` and `improve-stat-cards` were archived and committed. The pieces it works on:

**The tile.**

- `WidgetGrid` (`widget-grid.ts`) draws the `<article>`, its header and a body `min-h-0 flex-1 overflow-hidden p-3` that holds the `WidgetView`.
- The header row holds:
  - the drag handle (`size-9`, editors from `md` up);
  - the title `<h3>` (`min-w-0 flex-1 truncate text-sm font-medium text-heading`, with `title` set);
  - the description button (`DescriptionPopover`, `paper:hidden`);
  - the menu (`tmz-dropdown-menu`, `paper:hidden`).
- A widget with a subtitle gets a two-row header with `<p id="widget-subtitle-…" class="text-xs text-body paper:line-clamp-none">`. It is clamped to one line when `spanOf(widget) <= 2` and to two otherwise, and the article's `aria-describedby` points at it.
- The grid is 12 columns of 72 px rows with a 16 px gap from `md` up, one column below `47.99rem` (each tile keeps its height in rows), and 12 columns on paper (`tmz-paper`). The default stat tile is 3×2: 160 px high.

**The view.** `WidgetView` (`widget-view.ts`) draws:

- above the chart, the not-filtered note and the row limit;
- the chart in a `min-h-0 flex-1` box, clipped while there is a footnote or a base line;
- under it, the `#baseLines` template: the small-base note (`smallMinBelow`), the n line (`nLineText`, none for a stat tile) and the no-data line (`noDataText`);
- then the `#footnoteLine` template.

Each line is a `shrink-0 text-xs text-body paper:line-clamp-none` paragraph, clamped to one line (`compact()`, a tile 2 rows high or less) or two. `exportNotesAbove` and `exportNotesBelow` hand the same texts to the image.

**The charts' caption line.** `<p class="shrink-0 text-xs text-body" [class.sr-only]="!captionShown(config())" data-testid="chart-caption">` sits:

- in `AxisChart` when it has no series legend (bar, column, line and histogram);
- in `PieWidget`, `MapWidget`, `HeatmapChart` and `CrosstabWidget`.

`captionShown` (`format.ts`) is false only for `caption.hidden === true`. A series legend takes the caption's place in `AxisChart`. Box plot, pyramid and Likert draw no caption line. Each chart's export parts give `caption`: null when it has a legend, or a chart has none. `plotted()` names what is plotted, from the own text, else the label and the columns, for tooltips, tables and the accessible name.

**Reference lines.**

- `placeReferences` (`chart-annotations.ts`) puts a horizontal line's text at the plot's right edge, 4 px above the line, end-anchored. A vertical line's text goes in the top margin beside the line, flipped when needed.
- A text wider than the plot, or one over a text already placed (`taken`), is left out.
- `AxisChart` and `seriesPlot` draw the strokes, then the data labels, then the reference texts, each `<text>` with a halo: `stroke-neutral-primary-soft`, `stroke-width 3`, `paint-order stroke`. The halo is the tile's own color, white in light and gray-900 in dark.
- References are placed before data labels with the same `taken`, so a data label never covers a reference text.
- `ReferenceKey` (`series-legend.ts`) lists every line under the plot, and `ReferenceList` lists them under a table.
- The image's legend gets a `dashed` entry per line (`ExportSwatch`).

**Stat tile.**

- `StatWidget` dims its figure with `opacity: 0.35` (`smallBaseOpacity`) and `data-small` when its n is under the threshold.
- It draws, inside the tile, the small-base note and its lines (`nText`, from `display.secondary` holding `n` or `bases().showN`, then "Without filters").
- It registers `stat: { value, description, size, secondary, dimmed? }`.
- The widget does not draw the n line or the small-base note under a stat tile (`nLineText`, `smallMinBelow`).

**Small marks.**

- Every chart emits `dimmed` as a number: the count of dimmed marks. The view keeps it in `dimmedMarks` to decide the note.
- The marks themselves (`isSmallBase` over `figures.n`) are known only inside each chart.

**Popovers.**

- `DescriptionPopover` is a `CdkConnectedOverlay` panel, `role="dialog"`, that takes focus. Only one is open at a time, through the root `DescriptionPopovers.open` signal.
- `ChartTooltips` (`shared/ui/chart-tooltip.ts`) are CDK overlays, `aria-hidden`, closed together by `closeAll()` when a tile moves (`tileMoving`).

**Tokens.**

- The theme is Flowbite's `default.css` with Tamiza's overrides in `styles.css`, and dark mode through `.dark`.
- `text-body` is gray-600 in light and gray-400 in dark, and `text-heading` is gray-900 in light and white in dark.
- `bg-neutral-primary-soft`, the tile, is white in light and gray-900 in dark.
- The warning tokens are `bg-warning-soft`, `text-fg-warning` and `border-warning-subtle`: orange-50, orange-900 and orange-200 in light; orange-950, orange-300 and orange-900 in dark.
- The system font stack only: the CSP blocks web fonts.

**Elsewhere.**

- **Paper** is the page's own DOM under the `paper:` variant (`@media print` or `.tmz-printing`).
- **The email** (`Reports/ReportHtml.cs`, `ReportText`, `ReportWorkbook`) writes tables, the caption line unless `CaptionHidden` (`display.Caption?.Hidden is true`), notes in amber and the footnote. It draws no chart.
- **The API** keeps `WidgetCaption(Text, Hidden)`. `Normalize()` stores only `hidden: true` (`Hidden is true ? true : null`), and `CheckContext` refuses a text that is also hidden. The UI's `displayFor` keeps only `{ hidden: true }` or `{ text }`, so no stored widget holds `hidden: false` today.

**Duitama (read-only MCP survey, 2026-11-06).** 187 widgets on 9 dashboards; "2. Teatro" now has 42 widgets.

- **Subtitles and captions.**
  - All 187 have a subtitle and their own caption text; none hides it.
  - Of the 47 bar, column and line charts with no series, 34 repeat the subtitle in their caption.
  - Of the 26 pies, maps, heat maps and cross tables, 23 do.
- **Stat tiles.** 67 stat tiles: 48 are 2 rows high, 30 are 3 columns wide or less, and their labels restate their titles in 64.
- **Footnotes and titles.** 70 footnotes; 78 titles over 40 characters.
- **Annotations.** 13 widgets with reference lines (8 `overall`, 5 `value`) and 66 with data labels.
- **Base settings.**
  - Dashboards 2 to 8 set `minN` 30 and `showN`.
  - 9 stat tiles show `secondary` `n` (8 with `unfiltered`).
  - 3 widgets set their own `minN` to 0.

## Goals / Non-Goals

**Goals:**
- **Levels.** Three visible levels in a tile: the title, then the subtitle, then the plot and its figure, with one foot that never competes with the chart.
- **Nothing lost.** Every text the four lines say today is still read:
  - on screen, by opening a chip;
  - by screen readers, from the chip's description;
  - on paper and in images, as whole lines.
  The email is not touched.
- **Byte-identical saves.** No stored setting is rewritten and no query changes. The production-shapes fixture proves it.
- **One rule per text,** shared by the page, the preview in the form, paper and the image.

**Non-Goals:**
- Removing the other notes from above or under the chart: "Not filtered by", the row limit, series notes and the "no overall" note. They stay where they are (proposal).
- A reading mode, collapsing the header, or hiding the menu.
- Changing what the email or the workbooks say.
- Per-widget font sizes or chip settings.
- Detecting that an own caption text repeats the subtitle (D2, alternative b).

## Decisions

### D1. The type scale and its tokens

**Title** (`widget-grid.ts`):

- Classes: `text-[15px] leading-[1.35] font-semibold text-heading line-clamp-2 break-words paper:line-clamp-none`, keeping `title` with the whole text. `line-clamp` cuts only the drawing, so screen readers read the whole `<h3>`.
- The header row becomes `items-start`, so the handle, the description button and the menu stay aligned with the title's first line.
- **Padding.** The header's padding goes from `py-1.5` to `pt-2 pb-1.5`. A two-line title (two lines of about 20 px) is then a header of about 56 px, against today's measured 53 px. A one-line title is the same height as today, since the 36 px buttons set the row.

**Subtitle:**

- Classes: `text-[13px] leading-[1.45] text-body-strong`, with the one- or two-line clamp unchanged.
- `text-body-strong` is a new token in `styles.css`: `--color-body-strong` is gray-700 `#374151` in `@theme`, and gray-300 under `.dark`. On the tile it is about 10.3:1 in light (gray-600 is 7.6:1) and well over 10:1 in dark.
- The token is defined like Flowbite's own text tokens, so it swaps with `.dark` wherever it is used.

**Room for a 3×2 stat tile:**

- The tile is 160 px. The 2 px border, a two-line header (56 px) and a foot of one chip row (35 px: chips 22 px, `py-1.5`, a 1 px rule) leave a body of 67 px.
- With the body's padding reduced to `px-3 py-2` on a stat tile, 51 px remain. The figure is clamped by its container to 18 px (about 21 px of line), the label is 20 px, and 10 px are left.
- So the tile keeps figure and label whole, with the subtitle in the Detail chip (D6). The "Without filters" line, when there is one, is the part cut, as `improve-stat-cards` D1 allows.
- Task 2.4 measures it on the real stack.

*Alternatives:*
- *Title at 16 px (`text-base`).* Rejected: a 3-column tile holds about 30 characters a line at 16 px semibold, and Duitama's titles are numbered and long (78 over 40 characters).
- *Title kept on one line, at 15 px.* Rejected: the study's main complaint is the cut title.
- *Subtitle at `text-sm` (14 px).* Rejected: it would be as large as today's title, and the two would no longer read as two levels.
- *A new gray outside the tokens (an arbitrary hex).* Rejected: dark mode would need its own rule, and every other text color here is a token.

### D2. The caption under a subtitle

- **One rule** in `format.ts`: `captionInSight(config)`.
  - `caption.hidden === true` gives false.
  - `caption.hidden === false` gives true.
  - A subtitle (trimmed, not empty) gives false.
  - Anything else gives true.
- **Where it applies** (the user's decision, 2026-11-06: only where the line is not needed). `captionInSight` replaces `captionShown` in `AxisChart` for bar, column and line, and in `PieWidget`. There the drawing already names the groups, the value axis carries the unit (D3) and the pie's legend gives each value and share.
- **Where it does not apply.** These keep `captionShown`, so a subtitle changes nothing there:
  - `MapWidget`, `HeatmapChart` and `CrosstabWidget`. Their line is the only text that says what the shades or the cells measure, and on a heat map and a cross table it also names the two dimensions ("… by estrato and comuna"), which their row and column labels do not name.
  - A histogram, whose line says what its ranges are of and that the bars count rows.
  - `StatWidget`: a tile's label stays.
- **The own text goes to the foot** (the user's decision, 2026-11-06). When the line is out of sight and the caption has an own text (`ownCaption(config)`), the view adds a `caption` item to the foot, first, with a ruler icon (D4, D9). Its label is the own text and its text is the whole line as the chart would write it (`indicators.chart.caption` with the group). The default line has no chip: it is built from the indicator's label and the group, which the subtitle and the axis already give.
- **Screen readers.** While the caption chip exists, the chart does not render its `sr-only` line, so the text is heard once, from the chip's name and description. A default line out of sight stays `sr-only`, as a hidden caption does today. Tooltips, tables and the accessible name use `plotted()`, which does not change.
- **Image.** The chart's export parts give `caption: null` when the line is out of sight, and the caption item is the first of the foot's lines (D10).
- **Paper** is the same DOM: the line is out of sight above the chart and printed as the foot's first line.
- **The email** keeps its own rule (`CaptionHidden` from `hidden: true` only), so it still writes the caption over its tables (D10).
- **Stored value.** `hidden` becomes three-state.
  - **API:** `WidgetCaption.Normalize()` keeps `false`: `Hidden is bool h ? h : null`. A caption with neither field is still null. `CheckContext` still refuses only `{ text, hidden: true }`.
  - **UI:** `displayFor` keeps `{ hidden: false }` and `{ text, hidden: false }`.
  - **Form:** under the caption's three options (default, own text, hidden), a checkbox "Keep in sight under the subtitle" ("Mantener visible con subtítulo"), offered only for bar, column, line and pie. It is unavailable with "Hidden". Its help reads "With a subtitle, this line is out of sight and its text shows in the widget's foot." ("Con subtítulo, esta línea no se muestra y su texto aparece en el pie del widget.").
  - **Form warning** (the user's decision, 2026-11-06). While `captionInFoot` is true for the draft (below), an amber note sits under the caption's text field: "This text will not show above the chart, since the widget has a subtitle: it shows in the widget's foot. Check "Keep in sight under the subtitle" to show it above." ("Este texto no se mostrará encima del gráfico porque el widget tiene subtítulo: aparecerá en el pie del widget. Marque «Mantener visible con subtítulo» para mostrarlo arriba."). It uses the warning tokens and a triangle icon, is `role="status"` so a screen reader hears it once when it appears, is tied to the text field by `aria-describedby`, and never blocks the save. It follows the draft, so it appears whether the subtitle or the caption text was typed last.
- **One predicate for the chip, the warning and MCP.** `captionInFoot(chartType, config)` in `format.ts` is true when the kind is bar, column or line with no `seriesBy`, or pie, the subtitle is not blank, the caption has an own text, and `hidden` is not set. The API has the same rule as `WidgetCaption.InFoot(chartType, config)` in `IndicatorModels.cs` for the MCP note (D11). Both read one shared case file, `testing/caption-in-foot.json`, as `reference-lines.json` is shared today, so the two cannot drift.
  - **Safe for stored widgets:** no stored widget holds `hidden: false` today (Context), so none changes meaning. A map, heat map or cross table saved with `hidden: false` through MCP looks as it does without it.
  - **Old MCP clients.** A client that sent `hidden: false` before was told "Only true is stored". From now on it keeps the line in sight, which is what the words say.
- **Duitama.** The rule takes the line out of sight on 55 widgets: the 47 bar, column and line charts with no series and the 8 pies. All 55 have an own text, so each gets a caption chip, and none loses its text, including the ones that say something the subtitle does not. The 16 heat maps, the map and the cross table keep their line.

*Alternatives:*
- *(a) Hide only the default line, never an own text.* It is the safer reading of "the internal legend", but it changes none of Duitama's 187 widgets, since every one has an own text.
- *(b) Hide an own text only when the subtitle contains it.* Rejected: 32 are word for word but 52 are paraphrases ("% de los hogares que asistieron" against "E1, % de los hogares de cada comuna que asistieron…") that no string test catches without false positives.
- *(c) Hide the own text with no chip, on the value axis only.* Rejected by the user: the text must stay one click away in the foot, and a pie has no axis.
- *(d) Apply the rule to maps, heat maps and cross tables too.* Rejected by the user ("only where it is needed"): their line says what the shades and cells measure.
- *(e) A new field `caption.shown: true`.* Rejected: it adds a member whose only valid value is `true`, and a fourth combination (`shown` with `hidden`) to refuse. Three-state `hidden` reads naturally ("not hidden") and needs no validation rule.

### D3. The value axis title

- **When.** `AxisChart` for bar, column and line with no series, when `captionInSight` is false.
- **What it writes:** the widget's unit (`withWidgetUnit`) as values write it: `%` and the unit for a `percent` indicator ("% de los hogares"), the unit alone for the others ("hogares", "pesos", "COP"). A `percent` indicator with no unit, or a unit of `%`, writes nothing. The own caption text is not written there: it is in the caption chip (D2), and writing it on the axis too would say it twice.
- **Where.**
  - On a column or line chart, an 11 px `fill-body` text above the value axis, start-anchored at the plot's left. It reserves 14 px of top margin, added to `referenceMargin` when both are present.
  - On a bar chart, end-anchored under the value ticks at the plot's right, using the existing bottom margin plus 14 px.
- **Long texts** are cut to the plot's width with the chart's `charWidth` measure and an ellipsis, with a `<title>` holding the whole text. The text is `aria-hidden`, since the accessible summary already names what is plotted.
- **In the image** it comes with the SVG.
- **Series charts.** A chart with a series has a legend in place of the caption, so it gets no axis title.
- **`add-locale-formatting`** (abbreviated ticks such as "12K") does not touch it.

*Alternatives:*
- *Write the unit on every tick ("40 hogares").* Rejected: the ticks are deliberately short (`format.tick`), and abbreviated ticks would get longer still.
- *An axis title on every chart.* Rejected: it changes widgets that keep their caption line. The title exists only to carry the unit the hidden line said.
- *The own caption text as the axis title.* It was this design's first draft. Rejected once the own text went to the foot: one text in two places, and own texts are often longer than the plot.

### D4. The foot: model and component

- **The model.** `WidgetView` computes `footItems: FootItem[]`. Each item is `{ kind, label, text, tone, id }`:
  - `kind` is one of `caption`, `n`, `small`, `noData`, `lines`, `detail` and `note`, in that order;
  - `label` is the chip's short text, `text` its whole text and `tone` is `neutral` or `warning`.
- **What each chip says:**

  | kind | label | text | shown when |
  |---|---|---|---|
  | `caption` | the own caption text (cut by CSS) | the whole caption line, as the chart would write it | an own text out of sight under the subtitle (D2) |
  | `n` | `nLine(...)` ("n = 475 Hogares", cut by CSS) | the same line, whole | `nLineText` today; for a stat tile, D6 |
  | `small` | `indicators.foot.small` ("{{count}} with a small base") | `indicators.foot.smallText` with up to five marks (D5) | `smallMin` today, chart view only |
  | `noData` | `indicators.foot.noData` ("No data {{figure}}"), the figure being the share when there is one, else the value | `noDataLine(...)`, as today | `noDataText` today |
  | `lines` | `indicators.foot.lines` ("Reference lines") | the texts of the lines not drawn at the line, joined by "; " | some line's text was left out (D7) |
  | `detail` | `indicators.foot.detail` ("Detail") | the subtitle | compact stat tile (D6) |
  | `note` | `indicators.foot.note` ("Note") | the footnote | footnote set |

- **The component.** A new `WidgetFoot` in `indicators/widget-foot.ts` takes `items`. It replaces the `#baseLines` and `#footnoteLine` templates and sits last in the view's column (`shrink-0`), under the chart, the table or the "no rows" message, never in the other states.
  - It draws `<div class="flex flex-wrap gap-1.5 border-t border-default pt-1.5 paper:hidden">` with one chip button per item.
  - It also draws `<ul class="hidden paper:block">` with one `<li [id]>` of whole text per item, `detail` included.
- **The grid** gives the view's body less bottom padding (`pb-2`), so the foot sits at the tile's foot. The view's plot box stays clipped while the foot exists (`overflow-hidden` when `footItems().length > 0`), the rule `add-widget-context` D5 gave the footnote.
- **The preview and the script editor.** The form's preview (`widget-dialog.ts`) uses the same view and therefore the same foot. The script editor's total tiles use `StatWidget` without a view, so they never had these lines and keep none.
- **`hasBaseLines`, `nLineText`, `noDataText` and `exportNotesBelow`** keep their meaning, fed from `footItems`. Their `data-testid`s move to the chips: `widget-foot`, `foot-chip-{kind}` and `foot-line-{kind}`.

*Alternatives:*
- *Keep the lines and only make them smaller or lighter.* Rejected: four lines of 11 px are still four lines, and lighter text fails contrast.
- *One "i" button that opens all the notes.* Rejected: it hides the small-base warning, the one note that must be seen without a click.
- *Put the chips in the header.* Rejected: the header holds the title and controls, and the notes are about the chart, which they should follow.

### D5. Which marks are small

- **Charts report the marks.** Every chart's `dimmed` output becomes `SmallMark[]` (`{ name: string; n: number }`), in the chart's drawing order, instead of a count.
  - `name` is the group as shown (declared prefixes hidden, "(none)" for null), joined to the series with " · " for a cell of a chart with a series.
  - The charts concerned are `AxisChart` (plain marks and `seriesPlot` cells), `PieWidget`, `MapWidget`, `HeatmapChart`, `PyramidChart`, `LikertChart` and `BoxplotChart`.
- **The view** stores the list (`dimmedMarks`), uses its length where it used the count, and builds the small chip's text.
- **The text.** `indicators.foot.smallText`: "Small base (below {{min}}): {{marks}}. Their marks are drawn dimmed." / "Base pequeña (menos de {{min}}): {{marks}}. Sus marcas se dibujan atenuadas."
  - `{{marks}}` is "COMUNA 7 (n = 16), SIN NOMBRE (n = 20)". After five names it ends with `indicators.foot.more` ("and {{count}} more" / "y {{count}} más").
  - n is written with `figureFormatter`.
- **Why five, against three for no-data values (`maxNoDataNamed`).** Small marks are what the reader must not misread, and five names still fit a panel of `max-w-72` in four lines.
- **`StatWidget`** reports nothing: its n is the n chip's job (D6).
- **The table view** keeps its own mark and note (`smallMin` is null while `asTable()`), so the chip is chart-only, as the note is today.

*Alternative:* keep the count and ask each chart for names only when the chip opens. Rejected: the paper line and the screen reader's description need the names before anything is opened.

### D6. Stat tiles

- **No dimming.** `StatWidget` drops `[style.opacity]` and keeps `data-small` as a hook for tests and for the image.
  - It no longer draws the small-base note, and its `lines` hold only "Without filters".
  - `ExportParts.stat.dimmed` is removed, `secondary` holds only that line, and `composeImage` no longer sets `fill-opacity` on the figure.
- **The n chip.** The view builds a stat tile's n chip from the same rules `StatWidget.nText` uses today, moved to `base-figures.ts` as `statNLine(config, names, bases, n)`.
  - Its `tone` is `warning` when `isSmallBase(n, minN)`.
  - It is shown when the tile shows its n (`secondary` holds `n`, or `showN` is in force) or when it is `warning`.
  - A `warning` chip's `text` is `indicators.foot.smallStat`: "Small base: the figure rests on n = {{n}}, below the {{min}} this dashboard asks for. Read it with caution." / "Base pequeña: la cifra sale de n = {{n}}, menos de los {{min}} que pide el tablero. Léala con cautela." Its `label` is "n = {{n}}" without the base name, since it is a warning and short. The text says "this dashboard" even when the threshold is the widget's own, because the wording is about the base, not who set it.
  - A chart's n chip below the threshold uses the same tone and the same text, with "figures" in place of "the figure" (`indicators.foot.smallChart`).
- **Compact tiles** (the user left the threshold to this design, 2026-11-06). The grid computes `compactStat(widget)` = stat and `layout.h <= 2`, and passes the view `compact: boolean` for its Detail chip.
  - **Why height only.** What a stat tile lacks is height: the subtitle is clamped to two lines at any width, so a narrow tile only cuts it sooner, while a tile 2 rows high (160 px) has no room for it at all (D1). A 3×3 tile is 232 px: a two-line title and a two-line subtitle (about 94 px of header), the foot (35 px) and the padding leave about 85 px, enough for a 30 px figure, its label and the "Without filters" line (about 74 px). Task 2.4 measures it.
  - The rule is the same at every width, phones included: a tile keeps its height in rows on the one-column layout, so a 2-row tile is still short there.
  - The subtitle in the header gets `hidden paper:block`. No breakpoint class is needed.
  - The article's `aria-describedby` still points at the subtitle element, which screen readers compute even while it is `display: none`.
  - The detail item prints as a line of the foot on paper, under the figure. The first draft printed the subtitle under the title; on the stack (task 9.3) 8 of Teatro's 9 tiles 4×2 then had no room left for their figure on paper (the title and a three-line subtitle fill 160 px), so the header of a compact tile keeps the title alone on screen and on paper.
- **Teatro.** 9 of its 16 tiles are compact, the nine 4×2 ones. The six 3×3 tiles and the 3×6 one keep their subtitle under the title. In all of Duitama, 48 of the 67 stat tiles are compact.
- **Unchanged:** the statistic, `unfiltered`, the figure's size and `secondary`'s stored meaning.

*Alternatives:*
- *Keep dimming at a higher opacity (0.6).* Rejected: that is still under 4.5:1 for the label-sized line and reads as "disabled".
- *A badge next to the figure.* Rejected by the brief: nothing goes beside the figure.
- *Compact also when 3 columns wide or less.* It was this design's first draft, from the brief's "narrow tiles". Rejected: it took the subtitle off all 16 Teatro tiles, including the 3×3 ones that have room for it, and needed a breakpoint rule so that phones counted only the height.
- *A compact rule by measured room (a container query or a `ResizeObserver`).* Rejected: the same tile would change with the window, and paper would differ from screen. The grid's rows are what the author sets.

### D7. Reference line texts

- **Columns and lines.** `placeReferences` gains an `obstacles: Box[]` argument: the marks' rectangles, the points' discs and the labels already placed.
  - For a horizontal line, the text's box (text plus 3 px each side and 2 px above and below) is tried from the plot's right end leftward, in steps of 8 px, until it covers no obstacle and no `taken` box.
  - If no position is free, it goes at the right end as today.
  - `referenceMargin` grows from 12 to 16 for the box.
- **Bars.** For a vertical line the position does not change (the top margin beside the line), and only the box is added.
- **What is drawn.** Each placed text gets a `<rect rx="3" class="fill-neutral-primary-soft stroke-default paper:fill-white">` under it. The halo stays.
- **The key goes.** `AxisChart` and `seriesPlot` stop drawing `ReferenceKey`, and the image's legend loses its `dashed` entries.
  - `ExportSwatch` keeps `dashed` until no caller is left; then it is removed in the same task.
  - `ReferenceList` under a table stays.
- **Lines left out.** A line whose text was left out (wider than the plot, or over another reference text) is reported. `AxisChart` registers `unplacedReferences` with its export parts and exposes it to the view, which builds the `lines` chip.
  - The line's `<title>` and the accessible summary still name it.
- **Duitama's 13 lines.** All of them have short texts ("Total Duitama: 46.74%", "Mitad: 50%", "Meta: 480 encuestas"), so the chip should rarely appear.

*Alternatives:*
- *Keep the key and drop the label at the line.* Rejected: the reader would match dashes to a legend.
- *Move every line's text into the n chip, as the mockup does.* Rejected: a widget with lines and no n (no declaration) would lose them, and a line is about the chart, not the base.

### D8. Data labels and halos

- **Already true.** `AxisChart`, `seriesPlot` and `PieWidget` draw data labels after the reference strokes, with the tile-colored halo, and place reference texts first, sharing `taken`. So "Data labels over reference lines" is true today.
- **This change:**
  - pins it with tests;
  - adds `paper:stroke-white` on halos, since on paper the tile is `bg-white` while the halo is the screen's tile color;
  - makes the reference text's box (D7) part of `taken`, so a data label under it is left out as a covered label is today.

### D9. Chips: behavior and accessibility

- **Element.** Each chip is a `<button type="button">`.
  - Its icon is an inline SVG, `aria-hidden`: a ruler for `caption`, people for `n`, a triangle for `small` and for `n` in `warning`, a slashed circle for `noData`, a dashed line for `lines`, and "i" for `detail` and `note`.
  - Its name is its `label`. It has `aria-expanded`, and `aria-describedby` pointing at its paper `<li>`, which holds the whole text. A `display: none` element referenced by `aria-describedby` still gives its text (accname 1.2), so screen readers hear the whole text without it being read twice in browse mode.
  - Labels longer than the chip are cut with `truncate max-w-40` (10 rem; it was 16 rem until the stack check, see "Third decisions").
- **Panel.** A `CdkConnectedOverlay` above the chip (then below), with `max-w-72` and the `ChartTooltip` look (`rounded-base border border-default-medium bg-neutral-primary-medium px-3 py-2 text-sm text-body shadow-lg`). It is `aria-hidden="true"` and not focusable: its text is already the chip's description. It sits in the overlay container, so a tile's `overflow-hidden` never clips it.
- **States.** Each chip is closed, open (transient) or open and pinned.
  - Pointer enter (`pointerType` mouse or pen) and keyboard focus (`:focus-visible`, read from `matches(':focus-visible')`) open it, unpinned. Leaving or blur closes an unpinned chip.
  - A click or tap pins a chip that is not pinned and closes one that is, so the focus a click gives does not toggle it twice.
  - Escape closes the open chip and leaves focus on it. A click outside closes it. `tileMoving()` closes it with the tooltips.
- **One at a time.** `DescriptionPopovers` becomes the shared `TilePopovers`, keeping its file and adding an alias. Its `open` signal holds the id of whatever is open, so opening a chip closes an open description and the reverse.
- **Escape** is listened for on `document` only while a chip is open.
- **Look.**
  - Neutral chips are `bg-neutral-secondary-medium text-body`.
  - Warning chips are `bg-warning-soft text-fg-warning border border-warning-subtle`, the warning tokens the row-limit note already uses. They are orange in Flowbite and read as the mockup's amber.
  - Chips are 12 px (`text-xs`), weight 500 and `rounded-full`, with a focus ring `focus-visible:ring-4 ring-neutral-tertiary`.
  - Every state is told apart without color: icon, words, `aria-expanded`.

*Alternatives:*
- *Reuse `DescriptionPopover` as is.* Rejected: it is a dialog that takes focus, right for a long description and wrong for a one-line note read on hover.
- *Native `title` tooltips.* Rejected: no keyboard, no touch, and the study already found native tooltips poor.
- *The `popover` attribute with CSS anchor positioning.* Rejected: the overlay is what the rest of the app uses and tests, and anchor positioning is not yet in every browser the app supports.

### D10. Paper, images and email

- **Paper.** The chips are `paper:hidden` and the `<ul>` is `hidden paper:block`. Lines are 12 px `text-body`, whole, in the foot's order, with the footnote last.
  - The title and the subtitle lift their clamps.
  - `DashboardPrint` measures boxes after `tmz-printing` is set, so the foot's paper height is what the charts are laid out against.
- **Image.**
  - `exportNotesBelow` becomes the texts of `footItems` without `detail` and `note`: caption, n, small, noData and lines, in that order. The footnote stays its own parameter.
  - For a stat tile, the n text comes first among those notes, and `stat.secondary` holds only "Without filters".
  - `parts.caption` follows `captionInSight`.
  - No chip or panel is ever in the picture: chips are DOM outside the plot, and the image is composed from parts.
- **Email.** Nothing changes in `Reports/`. It already writes full notes, the caption unless `hidden: true`, and a stat value without dimming. `ReportHtmlTests` and `ReportDataTests` gain cases that pin it: a widget with a subtitle and an own caption text, one with `hidden: false`, and a stat tile with a small base.

### D11. MCP and docs

- **`WidgetCaption`'s `Hidden` description:** "true takes the caption line out of sight, false keeps it in sight under the widget's subtitle; left out, a bar, column, line or pie widget with a subtitle shows its own caption text in its foot instead of above the chart. Tooltips and tables still name what is plotted. true is not taken together with text."
- **`ContextHelp`** gains one sentence: "A bar, column, line or pie widget with a subtitle shows its caption's own text in its foot, not above the chart, unless caption.hidden is false." `DisplayHelp`'s pinned sentence is not touched.
- **`Mcp/Instructions.md` line 52** says the same and advises to set `hidden: false` only when the caption must be read without opening the foot.
- **A note in the write result** (the user's question, 2026-11-06: should MCP warn too? Yes). An MCP client never sees the page, so a description read once is not enough: it must learn, call by call, that the text it just wrote is not above the chart. `create_widget` and `update_widget` add `notes` to their result (`WithVersions`) when `WidgetCaption.InFoot` holds for the saved widget: "The caption text "{text}" is out of sight under the subtitle: it shows in the widget's foot, not above the chart. Set caption.hidden to false to keep it above." It is English, as every MCP text is, left out when empty, as `get_widget_data`'s `notes` is, and never refuses the call. `get_dashboard` is not changed: the descriptions explain what it returns.
  - *Alternative:* refuse the save until the client chooses. Rejected: moving the text to the foot is the intended default, not an error, and the form does not block either.
- **`docs/mcp.md`** says it in the `display.caption` bullet.
- **`docs/indicators-dashboard.md`:**
  - "Words around a widget" (the type scale, the caption under a subtitle, the axis title, the keep-in-sight box);
  - "Room" and "How much fits" (the new arithmetic);
  - the reference lines' "The key" bullet, which becomes "Texts at the lines";
  - "The lines under a widget", which becomes "The foot of a widget";
  - the stat tile's "Lines under the figure" and "Room";
  - "Small bases" (the stat figure is no longer dimmed);
  - printing and images.
- **`IndicatorDocsTests`** keeps the docs and the descriptions in step. It is updated with them.

### D12. Translations and fonts

- Every new string goes into `public/i18n/en.json` and `es.json` under `indicators.foot.*`, `indicators.dialog.context.caption.keep`, `keepHelp` and `inFoot`, in the task that adds it. `translations.spec.ts` checks that they match.
- The Spanish words are those of the mockup: "Base pequeña", "Sin dato", "Nota", "Detalle", "Líneas de referencia".
- Numbers in the chips use the formatters of today (`figureFormatter`, `valueFormatter`), so `add-locale-formatting` changes them later in one place.
- No font is loaded. The scale uses sizes and weights of the system stack, as the mockup does.

## Spec ownership

**MODIFIED.** Each block was copied whole from the main spec at `f0d0786`, every scenario name kept, and edited:

- `dashboard-indicators`:
  - "Widget subtitle", "Widget footnote", "Widget context in a tile", "Widget context in the widget form" and "Widget context left unset" (`add-widget-context`);
  - "Reference lines drawn" (`add-chart-annotations`). Its scenario "Key above the footnote" now states that no key is drawn;
  - "Base n line under a widget", "Base lines under a widget in their place", "Small bases drawn dimmed" and "No-data line under a widget" (`add-small-sample-warnings`). "Lines above the footnote" and "Chart cut above the lines" keep their names and now speak of chips;
  - "Stat tile secondary lines" (`improve-stat-cards`).
- `dashboard-printing`: "Widget context on paper", "Chart annotations on paper", "Small bases on paper" and "Stat tile on paper".
- `widget-export`: "Parts of a widget image", "Chart annotations in widget images", "Base n and small bases in widget images" and "Stat tile in a widget image".

**Not modified, still true:**

- `dashboard-indicators`:
  - "Stat tile": its sentence on what the tile shows under the number refers to "Stat tile secondary lines", which now puts the n in the foot.
  - "Widget caption text" and "Widget caption hidden": they say what a text or `hidden: true` does, and "Caption under a subtitle" adds the default.
  - "Widget context validation": no new refusal.
  - "Reference lines in "Show as table"": the list under the table stays.
  - "Data labels on bars, columns, lines and pies".
  - "Small bases in tooltips and tables".
  - "Stat tile value size": its "Tile with texts" scenario still holds with the subtitle in the Detail chip.
- `dashboard-reports`: "Widget context in a scheduled email", "Base n and no-data lines in a scheduled email" and "Small bases in a scheduled email". They already describe what the email keeps; "Widget texts in a scheduled email kept whole" pins that this change leaves it so.
- `mcp-server`: "Widget context through MCP". Its scenario's "no caption line" holds with `hidden: true`.

**No delta, covered generically:**

- `custom-dashboards` "Duplicating a dashboard", `dashboard-versions` and `dashboard-indicators` "Moving a widget to another dashboard" copy, hold and keep "every setting the widget is saved with", which includes `hidden: false`. Task 1.3 checks them.
- `custom-dashboards` shows no widget text of its own.

**Changes in flight that rebase on this one,** at their turn with `/opsx:update`:

- `add-category-highlight`: its neutral gray and `markTooltips` meet the `dimmed` list of D5 and the halo.
- `add-locale-formatting`: the chips' numbers and the axis title's "%".
- `add-dashboard-sections`: the grid header.
- `add-normalization-and-totals`: its totals' notes, if it adds any, go into the foot.

## Production impact

From the survey (Context):

- **Every widget's look changes:** the title and subtitle sizes and the foot. No stored JSON, query, value or version changes. Saving a form unchanged still makes no version, and the production-shapes fixture pins it (task 1.2).
- **55 widgets move their caption line from above the chart to the foot:** the 47 bar, column and line charts with no series, whose value axis then carries the unit, and the 8 pies. Each shows its own text as the foot's first chip. The 16 heat maps, the map and the cross table keep their line.
- **67 stat tiles.** None is dimmed any more. The 9 with `secondary` `n` and those on dashboards 2 to 8 (`showN`) show their n as a chip, amber below 30. The 48 compact tiles (2 rows high) show their subtitle in "Detail"; the 19 taller ones keep it under the title.
- **13 reference lines.** Their text sits on a box at the free stretch, and the key under 13 plots goes.
- **70 footnotes** move from a line to the "Note" chip on screen and stay whole on paper and in images.
- **The email is unchanged** for all 9 dashboards.

## Risks / Trade-offs

- **[A note behind a chip is read less]** "No suman 100 %" now needs a hover. → Mitigations:
  - the note is still there, whole, on paper, in images, in the email and for screen readers;
  - the chip says "Note" ("Nota"), the user's choice over showing the note's first words (2026-11-06);
  - the small-base chip, the one that warns, is amber.
- **[Hover panels flicker as the pointer crosses a foot]** → Opening is immediate but closing waits 100 ms. Moving to another chip closes the first at once, and the pointer only opens chips, never tooltips of the chart.
- **[Own captions that said something new are read less]** (some of Duitama's 55) → The text is the foot's first chip, labelled with the text itself, so its first words stay in sight; it is whole on paper, in images and for screen readers, tooltips and tables keep it, and the checkbox brings the line back.
- **[A long own text makes a wide caption chip]** → The label is cut at `max-w-40` (10 rem) and the chips wrap (`Chips on a narrow tile`), so the chart gives up one more chip row at most. Task 2.4 measures a 4-wide column chart with all chips.
- **[Initial bundle]** It was at 674.96 kB of the 675 kB warning before `improve-stat-cards`. → `WidgetFoot` lives in the lazy indicators chunk, and only the token and the arbitrary sizes reach `styles.css`. Task 9.2 records the size, and if it passes the warning the user decides before release.
- **[The panel's `aria-hidden` and `aria-describedby` on a hidden element]** Behavior differs between screen readers. → Task 9.4 checks NVDA with Chrome and VoiceOver with Safari. If one fails, the description moves to a visually hidden span.
- **[The 3×2 stat tile arithmetic]** It is computed, not measured. → Task 2.4 measures on the real stack. The fallback is the header's padding, then the title's line height, recorded in this design.
- **[`hidden: false` from old clients now means something]** → It means what its words say, and no stored widget holds it (Context).

## Migration Plan

- **No database migration and no stored setting is rewritten.** The UI and the API ship together in one release. The API's `Normalize()` change only keeps a value that a new UI or MCP client may send.
- **Rollback** is a revert of both.
  - A widget saved with `hidden: false` during the release keeps it in the database. The old UI and API read it as "not hidden", which is what they did with no setting, and drop it on the next save.
  - Nothing else is stored.

## Open Questions

**Settled with the user on 2026-11-06:**

- An own caption text goes out of sight under a subtitle too, and moves to the foot as the caption chip, with an icon (D2, D4).
- The rule applies only where the line is not needed: bar, column and line charts with no series, and pies. Maps, heat maps and cross tables keep their line (D2).
- The compact threshold was left to this design: 2 rows high or less, at every width (D6).
- The note chip says "Note" ("Nota") (D4).
- The form warns, beside the caption, when an own text goes to the foot, and MCP tells it in the write result's `notes` (D2, D11).
- The small-base text names up to five marks (D5).

**Still open:** none.

## Implementation notes

Applied on 2026-11-06 on `DEV-TEXT-HERARCHYY`, not committed.

- **API.** `WidgetCaption.Normalize()` keeps `hidden` as given. `WidgetCaption.TextInFoot` and `InFoot` are the one rule for "the own text is in the foot", tested over `testing/caption-in-foot.json`, which the UI's `captionInFoot` reads too. `create_widget` and `update_widget` return `notes` when the rule holds. `Reports/` is untouched and pinned by tests.
- **The foot** is `widget-foot.ts` (`FootItem`, `WidgetFoot`). `WidgetView.footItems` builds the chips from the sources the four lines had. `DescriptionPopovers` is now `TilePopovers` with the old name as an alias. Each chart reports its dimmed marks as `SmallMark[]` (`seriesPlot` gives `smallAt`, which `AxisChart` names).
- **The tile.** `WidgetGrid` has `compactStat` (stat tile, `layout.h <= 2`) and passes it to the view. `WidgetView.tileRows` and `compact` went away with the clamps they served.
- **Stat tiles.** `StatWidget` draws no n, no small note and no opacity; `statNLine` moved to `base-figures.ts`. `ExportParts.stat.dimmed` and `composeImage`'s `fill-opacity` are gone.
- **Reference lines.** `placeReferences` takes `obstacles` and returns `labels` with a `box` and `unplaced`. `seriesPlot` and `linePlot` write their data labels after the reference texts (the loops queue them), so the texts keep the room and the marks are known. `ReferenceKey`, the `dashed` swatch and the clip of the plot box are removed.
- **Documentation.** `docs/indicators-dashboard.md` and `docs/mcp.md`, with `IndicatorDocsTests` updated for the sentences it pins.

## Deviations from the plan

- **Email scenario.** The delta's "Stat tile with a small base" said the email's notes say the base is below 30. The email never wrote that for a tile (its `StatLines` hold the n line only), and "Reports/ is untouched", so the scenario now says the value is written plainly with its n line and no chip text.
- **Header row.** The title sits in a `min-h-9` box that centers it on the buttons, and the row is `items-start`. `items-start` alone would have put a one-line title at the top of a row 36 px high.
- **`tileRows`.** The design did not say that the view's `tileRows` input and `compact` computed go. They only served the clamps of the lines the foot replaced.
- **Hidden caption and the axis title.** `captionInSight` is false for `hidden: true` too, so a bar, column or line chart with no series that hides its caption writes its unit as the value axis title (design D3 says so; no widget of Duitama hides its caption).
- **Close lines.** Two close lines whose texts cannot both fit leave the second to the foot's chip, since a text that would cover another goes there (spec "Reference line text placement"); it moves left only where a stretch is free.
- **Dash of the lines in the image.** jsdom has no style sheet, so the unit tests cannot read the dash of a reference line in the exported SVG, and the test that did read it through the legend swatch now checks the texts' boxes. The dash is for the stack verification (task 9.3).
- **Order of tasks 2.2 and 2.4.** The arithmetic of "Room" and the measurement of the 3×2 tile need the foot and the stat tiles (groups 4 and 5), so they came after them.

## Verification Results

2026-11-06. The suites ran on the working tree; the stack check ran on `tamizaverify` images built from it (then removed), before the three fixes listed under "After the stack check".

**Suites.**
- API: 3,197 unit tests pass. Integration: 2,412 of 2,415 pass. The two `A_ratio_*` tests of `IndicatorSeriesQueryTests` fail on a clean `main` as well. `McpPipelineToolsTests.A_run_is_queued_waited_for_and_read_with_its_steps_and_the_scripts_it_used` failed under the full run and passes alone (17 of 17).
- UI: 4,308 tests, 2 fail, the two known polling failures (`project-data.spec.ts` and `project-scripts.spec.ts`).
- `scripts/check-ui-safety.sh` is clean. `openspec validate improve-widget-text-hierarchy --strict` is valid.
- **Bundle.** The initial total is 675.82 kB against the 675 kB warning (818 bytes over, the error budget is 1 MB). Before this change it was 674.83 kB. It is a warning only; the user decides before release whether to trim it.

**Stack check** (a separate Compose project, invented tables shaped like `teatro_hoy`, `teatro_conteo` and `teatro_precio_hogar`, the 42 widgets of "2. Teatro" rebuilt from the tally of Duitama, threshold 30 and base n on; headless Chrome at 1,280 and 390 px, light and dark).
- **Passed:**
  - no horizontal scroll in any of the 8 runs;
  - titles of at most two lines at 15 px and weight 600 with their whole text in `title` (42 tiles);
  - subtitles at 13 px, contrast 10.3:1 in light and 12.05:1 in dark;
  - no caption line above the bar, column, line and pie charts with a subtitle, their own text as the foot's first chip, the unit as the value axis title, heat maps and histograms keeping their line;
  - the 16 stat tiles of Teatro: "Detalle" on the nine 4×2 ones, the subtitle under the title on the 3×3 and 3×6 ones, no dimmed figure, the amber n chip only on the small base, figure and label whole;
  - the foot of "2.1 por comuna" with the caption chip, the n, "2 con base pequeña" naming SIN NOMBRE (n = 20) and COMUNA 7 (n = 16), and the note;
  - chips by pointer, keyboard (focus opens, Escape closes and keeps the focus), click (pinned, closed by a second click or a click elsewhere) and touch, one panel at a time, a widget's description included;
  - reference texts on a box whose fill is the tile's color in both themes, with the halo, no key under any chart, and the long text in the "Líneas de referencia" chip;
  - data labels over the lines with their halo;
  - PNG and SVG of the por comuna chart and of the amber tiles: the foot's lines in order, the dashed lines, the texts on rounded boxes, the dimmed columns, the stat figure with no `fill-opacity`;
  - **9.5 (Mailpit):** the body, the plain text and both workbooks hold the captions (own text lines included, under subtitles too), the notes, the n lines and the footnotes in their old words, with no chip text and no opacity;
  - **9.6 (MCP over HTTP):** the first `update_widget` returned the note, the second (`hidden: false`) returned none and `get_dashboard` read the caption as given; the page showed the caption line again with no chip.
- **Measured (1,280 px, light; 390 px and dark are the same).** Stat 3×2 (160 px): header 55.5 with a two-line title, body 102.5, foot one row of 29, stat box 41.5, figure 18 plus label 20 = 38 whole. With the "Sin filtros" line (16 px) it needed 54, 12.5 short. Stat 3×3 (248 px): header 95, box 89.8, figure 24 + label 20 + line 16 = 60 whole. Stat 3×6: whole. Column 4×5 with all six chips but Detail: chips of 292, 127, 143, 190, 144 and 63 px over 369 px, four rows (113 px).

**Found by the stack check.**
- **D1, print of a compact stat tile (high).** 8 of Teatro's 9 tiles 4×2 printed their title, a three-line subtitle and the n line, and nothing else: on a 160 px tile the subtitle under the title left the stat box 0 px high, so "17,000" and "22.07%" were not on the sheet.
- **D2, label of the 3×2 and 4×2 tile with the "Without filters" line (medium).** The flex column shrank the label before the line.
- **D3, six chips (medium, kept).** A 4×5 chart with six chips takes four rows; three is the best packing of those widths. Teatro's worst foot is two rows at 1,280 px and three at 390 px.
- **D4, reference text box (medium).** It was about 30 % wider than the text (292 px for a text of 223), so a stretch that was free was not found and the box covered bars.
- **D5 (low, not this change's).** A bar chart 3 columns wide with data labels has a plot about 50 px wide: its ticks touch and its names are cut. The new axis title adds to the crowding.

**After the stack check** (fixed and unit-tested, not seen again on the stack).
- **D1.** The subtitle of a compact stat tile prints as the Detail line of the foot, under the figure, never under the title (specs `dashboard-indicators` "Subtitle of a compact stat tile" and `dashboard-printing` "Widget context on paper" and "Widget foot on paper", design D4 and D6, and the docs, were changed to say so).
- **D2.** The figure and the label are `shrink-0`; only the line without filters shrinks and is cut.
- **D4.** A reference text is measured at 5.5 px a character (`referenceCharWidth`) for its box and its fit, not at 6.5.

**Second stack check** (the same stack, rebuilt from the tree with the fixes).
- **D2 passes.** Stat 3×2 and 4×2 with the "Sin filtros" line: figure 18 px whole, label 20 px whole, only the line cut (3.5 of 16 px). Stat 3×3: figure 24, label 20 and line 16, whole. No difference in 208 screen measurements between the two runs.
- **D4 passes.** Every reference box is at most 1.12 times its text's width (248 px for a text of 222; before, 292 for 223); on "2.1 por comuna" at 1,280 px the text now stands at x = 250 and covers no bar. Where every stretch is taken (2.1 por estrato, the 4-wide copy, 390 px) the text stays at the right end over the bars, the spec's "No free stretch".
- **D1 needed a second fix.** `paper:line-clamp-none` set `display: block` after `hidden`, so the subtitle of a compact tile printed under its title as well; it now has `paper:hidden` and not that class. And the Detail line, as a line of the foot, took the room of the figure, so the stat box of a tile has `paper:min-h-[3.75rem]` and the foot is what a short tile cuts. Third run: 9 of Teatro's 9 tiles 4×2 print a stat box of 60 px with the figure and the label ("17,000", "22.07%" and "21,012" are in the PDF text, in both orientations); the subtitle no longer prints under the title or twice.

**Still open.**
- **Foot lines on a 2-row tile on paper.** A tile 160 px high holds, on paper, its title, the figure and the label (60 px), and what is left of the foot is cut at the tile's edge, as the spec's "Texts taller than the tile" says: on the 9 Teatro 4×2 tiles the Detail line is cut (0 of 9 have the subtitle in the PDF text) and, on "2.6 Sin dato", the note too. The amber sentence of a tile with a small base and a long footnote is cut on the 3×3 tile 2.5 of Teatro and on the three small-base tiles of Pruebas, so on those sheets the figure prints without its warning. The screen shows the chip and opens the sentence. Options: give those tiles a third row; print the amber sentence and then the figure's n in place of the subtitle (the n first, the Detail line last, which is the order now, with less room for it); print a stat tile's foot lines smaller; or accept it, since a sheet is a copy and the tile's size is the author's.
- **Tasks 2.4 and 9.3** stay unchecked for that and for D3.
- **D3.** A chart with six chips needs a taller tile or fewer chips; the caption chip's label could be capped lower than 16 rem if the user prefers.
- **Task 9.4** (NVDA with Chrome, VoiceOver with Safari) is the user's.
- The screenshots and measurements are in the verification session's scratchpad (`verify\`), outside the repository.

**Third decisions** (the user, after the second stack check).
- **Paper, stat tiles.** The foot lines of a stat tile print at 10 px with a line height of 12 px (`WidgetFoot` `compact`, set for `stat`), and the stat box keeps room on paper for its figure and its label: `paper:min-h-[2.6rem]` (41.6 px, one label line) when the label the tile draws (its statistic included, `statDescription`) has at most 10.5 characters per grid column of the tile, `paper:min-h-[3.7rem]` (59.2 px, two lines) when it is longer or the columns are not known (`WidgetView.columns`, `statBoxClass`). A single 2.6 rem for all showed the foot whole but cut the second line of "2.10 Pagaría en promedio" ("boleta" lost), and a single 3.7 rem would cut the Detail line of every 4×2 tile, so the room follows the label. The 10.5 characters per column, read from the stack (a 41-character label fits one line of 4 columns and a 46-character one takes two), is a guess of the line a label of 14 px holds, as `textsCrowd`'s 15 for the 12 px texts. A first try at 9 px and 60 px, run on the stack, still cut the Detail line (11 to 15 of 22 px seen) and printed 4.96 pt in portrait, which is why the room was taken from the box and the size kept at 10 px. The order is unchanged: the n chip's amber sentence comes before the Detail line and the footnote, so what a short tile still cuts is the end of the foot, the Detail line first. Not seen on the stack yet.
- **Chips, D3.** The label of a chip is cut at 10 rem (`max-w-40`), not 16. Six chips at 369 px cannot fit in two rows (their widths add up to about 830 px against 738), so task 2.4 now asks for no more than three rows with all six, and two with the usual four.

**Fourth to seventh stack passes** (print of the stat tiles, A4, both orientations give the same result).
- **Pass 4** (9 px lines, box 60 px): the Detail line was cut on the 9 tiles 4×2, the amber sentence of the three Pruebas tiles was cut or missing, the foot printed at 4.96 pt in portrait.
- **Pass 5** (10 px lines, box 41.6 px): the 9 tiles 4×2 of Teatro printed figure, label, n and Detail whole; the second line of a two-line label was lost ("2.10 Pagaría en promedio", "boleta").
- **Pass 6:** the box rule measured the indicator's label, not the text the tile draws, so every tile kept 41.6 px. Fixed: `statDescription` is the text both measure.
- **Pass 7** (box 41.6 px for a label that fits one line of the tile's columns at 10.5 characters per column, 59.2 px otherwise): "2.10 Pagaría en promedio" prints its two label lines, "2.9 Sin dato" (41 characters) keeps one line, n and Detail. The cost of the taller box is 18 px of foot on the tiles with a two-line label.
- **Cut on paper after pass 7**, as the dashboard-printing scenario "Texts taller than the tile" allows: the Detail line of 2.10 Pagaría en promedio (15 of 24 px), the note of 2.6 Sin dato (8 of 24) and of 2.2 (21 of 24), the last 3 px of the amber sentence of 3×3 tile 2.5, and the amber sentence of the three Pruebas tiles with a small base (2, 13 and 20 of 36 px; Pruebas 4×2 shows 20 of 24). Figure and label are whole on every tile.
- **Measured at 1,280 px, light and dark:** the 4×5 column chart with all six chips takes three rows (chip widths 196, 127, 143, 190, 144 and 63 px); at 390 px it takes four, since the small-base chip and the no-data chip do not fit together in 332 px.
- **Decided by the user (option A):** the amber n chip of a stat tile prints a short line, "Small base: n = 26, below 30." (`FootItem.printText`, key `indicators.foot.smallStatShort`), in place of the whole sentence, which the page keeps (`foot-line-whole`) for screen readers and the panel, and an image and the email keep too. Specs `dashboard-printing` "Widget foot on paper" and "Amber n of a stat tile on paper" and the docs say so. The short span is `aria-hidden`, so the chip's description is the whole sentence only.
- **Pass 8 (print, A4, both orientations):** the short line prints whole on 3×3 "2.5" of Teatro and on the 4×2 and 3×3 tiles of Pruebas; the long sentence is in none of the four PDFs. On screen the chip's panel gives the whole sentence by pointer and focus. The images of the amber tiles have the whole sentence and no short line; the email has its old words and no short line.
- **Residual cuts on paper (task 9.3 closed with them, the "Texts taller than the tile" scenario allows them):** the amber line of the Pruebas 3×2 tile (a two-line title and a two-line label leave it 2 of 12 px), the Detail line of "2.10 Pagaría en promedio" (15 of 24 px) and of Pruebas 4×2 (8 of 36), the notes of "2.6 Sin dato" (8 of 24), "2.2" (21 of 24) and the small-base tiles of Pruebas. The remedy is a taller tile.
- **Task 9.4 (screen readers):** marked done by the user's decision at archive time (2026-11-07); the NVDA and VoiceOver run was not done in this session, and the chip's description, the short print line's `aria-hidden` and the panel's `aria-hidden` rest on the unit specs and on the DOM read of the stack passes.
