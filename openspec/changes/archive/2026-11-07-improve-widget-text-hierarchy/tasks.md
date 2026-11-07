# Tasks

**Order.** Group 1 (api) can run first or in parallel with the UI. In the UI:

- group 2 (type scale) comes first;
- group 3 (caption) follows it;
- group 4 (foot) comes before groups 5 to 8, which use the foot;
- group 9 is last.

The visual reference is [`mockup.html`](mockup.html) (design Context lists where the design departs from it). Code is cited by function, constant and `data-testid`, not by line.

**Strings.** Every UI string goes into both `tamiza-ui/public/i18n/en.json` and `es.json` in the task that adds it (`translations.spec.ts` checks that they match). Spec scenario titles are named in backticks.

**How to run the tests** (see the memory note on the development environment):

- API tests run with `dotnet test` in `tamiza-api/` (the solution is `Tamiza.sln`; integration tests use Testcontainers). Filter by class while working, for example `--filter "FullyQualifiedName~WidgetDisplayTests"`.
- UI tests run with `npm test -- --watch=false` in `tamiza-ui/`, or one spec with `npx ng test --watch=false --include='src/app/pages/projects/indicators/<file>.spec.ts'`, with `TZ=UTC`.

## 1. Stored setting, MCP and email pins (api)

- [x] 1.1 In `WidgetCaption.Normalize()`, keep `Hidden` as given when it is a bool, `false` included (design D2), and update the record's and `Hidden`'s `Description` with the wording of design D11. `CheckContext` is unchanged.
  Verify: new `WidgetDisplayTests` cases round-trip `{"hidden":false}` and `{"text":"…","hidden":false}`; `{}` and `{"text":"  "}` are still no caption; `MinimalBefore` and `RichBefore` stay byte for byte. `IndicatorValidationTests` still refuse `{ text, hidden: true }` on `config.display.caption` and accept `{ text, hidden: false }` (`Own text kept`, `Caption with a text and hidden`).
- [x] 1.2 Add two shapes to the production-shapes fixture (`tamiza-ui/src/app/pages/projects/indicators/testing/production-shapes.json`, run by `ProductionShapesTests.cs` and `production-shapes.spec.ts`), with invented names and values only: a column chart with a subtitle and `{ "hidden": false }`, and a bar chart with a subtitle and `{ "text": "% de las viviendas", "hidden": false }`. Raise every pinned count by two and record the new total in design.md.
  Verify: `ProductionShapesTests` and `production-shapes.spec.ts` pass. Every existing shape normalizes to byte-identical stored JSON and gives the same `WidgetQuery.For` and plan SQL, and the two new shapes round-trip.
- [x] 1.3 Check that `hidden: false` travels with the widget (design "Spec ownership"): a dashboard version holds and restores it, a duplicated dashboard copies it, a moved widget keeps it, and saving its form unchanged makes no version.
  Verify: new cases pass in `DashboardVersionTests`, in `DashboardTests` next to `A_duplicate_holds_a_copy_of_each_widget_and_comes_right_after_the_original`, and in the moved-widget tests.
- [x] 1.4 MCP (design D11): add the sentence to `ContextHelp` in `Mcp/McpDashboardTools.cs` (`DisplayHelp`'s pinned sentence untouched), update `Mcp/Instructions.md` (the `caption` bullet), the `display.caption` bullet of `docs/mcp.md`, and whatever `IndicatorDocsTests` pins.
  Verify: `McpDashboardToolsTests` cases pass:
  - the `create_widget` and `update_widget` descriptions hold the sentence (`Descriptions say it`);
  - `update_widget` with `{ text, hidden: false }` saves it and `get_dashboard` returns it as given (`Kept in sight through MCP`);
  - `create_widget` with `{ text }` stores no `hidden` (`Caption left as before through MCP`).
  `IndicatorDocsTests` passes.
- [x] 1.6 The caption rule on the API (design D2, D11): add `WidgetCaption.InFoot(chartType, config)` in `IndicatorModels.cs`, and the shared case file `tamiza-ui/src/app/pages/projects/indicators/testing/caption-in-foot.json` (kinds, series, blank and set subtitles, default, own, hidden and kept captions), read by a new `WidgetCaptionTests` as `ReferenceLinesTests` reads `reference-lines.json`. In `McpDashboardTools.WithVersions`, add `notes` with the sentence of design D11 when `InFoot` holds for the saved widget, left out otherwise.
  Verify: `WidgetCaptionTests` passes over the shared file; `McpDashboardToolsTests` cases pass: `Note on a caption out of sight` (`update_widget` adding a subtitle), `No note when nothing moves` (default line, heat map, `hidden: false`), and a stat tile and a column chart with a series give no note. The rest of each result is unchanged.
- [x] 1.5 Pin the email (design D10; `dashboard-reports` "Widget texts in a scheduled email kept whole"). Nothing in `Reports/` changes.
  Verify: new `ReportHtmlTests` and `ReportDataTests` cases pass:
  - a column chart with a subtitle and an own caption text writes its caption line in the HTML body and the plain text, with or without `hidden: false` (`Caption under a subtitle in the email`);
  - a dashboard with threshold 30 and `showN` writes the small-base note, the n line, the no-data line and the footnote in today's words, and no `indicators.foot.*` text (`Notes worded as before`);
  - a stat tile with n 26 writes its value with no opacity style (`Stat tile with a small base`).

## 2. Type scale (ui)

- [x] 2.1 Add `--color-body-strong` to `styles.css`: gray-700 in `@theme`, gray-300 under `.dark` (design D1), so `text-body-strong` exists.
  Verify: a `chart-tokens.spec.ts` case reads the token in both themes and checks its contrast against the tile, `bg-neutral-primary-soft`: at least 9:1 in light and 9:1 in dark (`Subtitle size and color`).
- [x] 2.2 In `widget-grid.ts`, give the header `items-start` and `pt-2 pb-1.5`, and give the title `text-[15px] leading-[1.35] font-semibold line-clamp-2 break-words paper:line-clamp-none`, keeping `title`. Give the subtitle `text-[13px] leading-[1.45] text-body-strong`, keeping its clamp rule (design D1). In `docs/indicators-dashboard.md`, write the type scale into "Words around a widget" and the arithmetic into "Room" and "How much fits".
  Verify: `widget-grid.spec.ts` cases pass:
  - the title has the classes and its whole text in `title` and in the `<h3>` (`Title of two lines`, `Title longer than two lines`);
  - the subtitle has the size, the token and one line on a 2-row tile, two on a taller one (`Long subtitle on a column chart`).
- [x] 2.3 Compact stat tiles (design D6): `compactStat(widget)` in the grid (stat and `layout.h <= 2`), the subtitle's `hidden paper:block` on a compact tile, and `compact` passed to the view for its Detail chip (group 4). The article keeps `aria-describedby` on the subtitle.
  Verify: `widget-grid.spec.ts` cases pass: a 3×3 and a 3×6 stat tile keep the subtitle under the title (`Tile 3 columns wide`), a 4×3 too (`Tile 4 columns wide and 3 rows high`), a 4×2 is compact (`Tile 4 columns wide and 2 rows high`), a 4×2 column chart is not compact, and a 4×2 stat tile on the phone layout is still compact (`Phone`).
- [x] 2.4 Measure on a dev server (`ng serve` against the user's stack or the verify stack of task 9.3) at 1,280 and 390 px, in light and dark, and record the heights and findings in design.md "Implementation notes". Tiles: stat 3×2, 3×3 (subtitle of two lines under the title) and 4×2 with a two-line title, a subtitle, a footnote, `secondary` `["n","unfiltered"]` and threshold 30; a column chart 4×5 with a subtitle, an own caption text of 60 characters out of sight, a footnote, small bases, the n and a no-data value (all seven kinds of chip but `detail`).
  If the 3×2 tile does not show its figure and its label whole, tune the header padding, then the title's line height, within the spec and record it. If the 3×3 tile cannot hold its subtitle, its figure and its label whole, record it and ask the user before widening the compact rule.
  Verify: the record shows the figure and label whole on the 3×2 and 3×3 tiles (`Stat tile two rows high`, `Tile with texts and the n line`, `Tile 3 columns wide`), and the 4×5 column chart's foot at no more than three chip rows with all six chips at 1,280 px (four at 390 px; two with the usual four), the chip label cut at 10 rem.

## 3. Caption under a subtitle and the axis title (ui)

- [x] 3.1 Add `captionInSight(config)` to `format.ts` (design D2) and use it for the caption line and for `parts.caption` in `AxisChart` (bar, column and line only; a histogram keeps `captionShown`) and `PieWidget`. `MapWidget`, `HeatmapChart` and `CrosstabWidget` keep `captionShown`. A default line out of sight stays `sr-only`; an own text out of sight is not rendered by the chart, since the caption chip carries it (task 4.1).
  Verify:
  - `format.spec.ts` covers the four cases of the rule;
  - `chart-widgets.spec.ts` and `series-widgets.spec.ts` cases pass: `Own text under a subtitle`, `Pie under a subtitle`, `Heat map keeps its line`, `Without a subtitle`, `Stat tile keeps its label`, `Histogram keeps its line`, `Series legend kept` and `Table header`;
  - a map and a cross table with a subtitle still draw their caption line.
- [x] 3.2 `displayFor` in `widget-view.ts` keeps `{ hidden: false }` and `{ text, hidden: false }`, and still drops `{ hidden: false }` together with a blank text, as today.
  Verify:
  - `query-for.spec.ts` cases pass: `savedConfig` keeps both, `queryFor` and the query key are unchanged by them, so the preview does not re-query;
  - the fixture shapes of task 1.2 round-trip in `production-shapes.spec.ts`.
- [x] 3.3 In the widget form (`widget-dialog.ts`), add the "Keep in sight under the subtitle" checkbox under the caption's three options, for bar, column, line and pie only. It is unavailable with "Hidden", its help says the line is out of sight while the widget has a subtitle and its text shows in the foot (design D2), and `formConfig` and `displayDraft` carry it. Add `indicators.dialog.context.caption.keep` and `keepHelp` in both languages.
  Add the amber warning under the caption's text field (design D2): shown while `captionInFoot` holds for the draft, `role="status"`, tied to the field by `aria-describedby`, never blocking the save. Add `indicators.dialog.context.caption.inFoot` in both languages. Add `captionInFoot` to `format.ts` and run it over the shared `testing/caption-in-foot.json` of task 1.6.
  Verify: `format.spec.ts` passes over the shared file; `widget-dialog.spec.ts` cases pass: `Caption kept in sight in the form` (preview, no query, no caption chip), `Keep in sight with a hidden caption`, `Help of the keep-in-sight option` (the help and the preview's caption chip), `Warning for an own text out of sight` (typed caption first and subtitle first; save still enabled), `No warning when nothing moves`, `Option on a heat map` (no option), and a widget saved unchanged sends the same config (`Form saved unchanged`).
- [x] 3.4 The value-axis title in `AxisChart` (design D3): the unit with `%` for `percent`, never the own caption text, cut to the plot with `<title>`, top-left on columns and lines, bottom-right on bars, `aria-hidden`, only with no series and the caption out of sight. In `docs/indicators-dashboard.md`, write "Caption" (the default under a subtitle, the caption chip, the checkbox, the axis title, and the kinds that keep their line).
  Verify: `chart-widgets.spec.ts` cases pass: `Own text in the foot, unit on the axis`, `Unit of a percent indicator`, `Caption in sight`, `Percent with no unit`, a bar chart's title under its ticks, and a long title cut with its whole text in `<title>`.

## 4. The foot (ui)

- [x] 4.1 Add `FootItem` and `footItems` to `WidgetView` (design D4) from the sources of today: the own caption text out of sight (`ownCaption` and `captionInSight`, bar, column, line and pie only), `nLineText`, the small marks, `noDataText`, the unplaced reference lines (empty until group 6), the subtitle for `compact`, and the footnote. The order is fixed, and an item with nothing to say is left out. Shown only with rows or the "no rows" message.
  Verify: `widget-view.spec.ts` cases pass: `Foot of a column chart` (n, small and note in order), `Caption chip first`, `Chip opened` (the whole line), `Default line under a subtitle` and `Hidden caption` (no caption chip), `Nothing to say`, `Widget that failed`, and a widget shown as a table without its small chip (`Shown as a table`).
- [x] 4.2 Add `widget-foot.ts`: the chips, their icons, the panels on `CdkConnectedOverlay`, and the paper `<ul>` with one `<li [id]>` per item (none for `detail`). Each chip is `aria-expanded`, with `aria-describedby` on its `<li>`, and its panel is `aria-hidden`. Implement the closed, transient and pinned states, Escape, the outside click, and the shared `TilePopovers` (the renamed `DescriptionPopovers`, with an alias). Hook `WidgetGrid.tileMoving()` to close it. Add `indicators.foot.*` in both languages (design D5, D6, D9 and D12).
  Verify: a new `widget-foot.spec.ts` passes:
  - `Opened by pointing` (mouse enter and leave);
  - `Opened with the keyboard` (focus-visible, then Escape, with focus kept);
  - `Opened by a click` (pinned, a second click closes it, no double toggle on the focus a click gives);
  - `One at a time` (with an open `DescriptionPopover`), and a tap with `pointerType` touch;
  - `Screen reader`: the accessible name, `aria-expanded` and the description;
  - `Screen reader on the caption chip`: the own text as the name, the whole line as the description, and no `sr-only` caption line left in the chart;
  - `Not by color alone`: the icon of a warning chip.
  `description-popover.spec.ts` still passes.
- [x] 4.3 Replace the `#baseLines` and `#footnoteLine` templates with `<app-widget-foot>`. Clip the plot box while the foot exists, and give the grid body `pb-2`. Move the `data-testid`s to `widget-foot`, `foot-chip-{kind}` and `foot-line-{kind}`.
  Verify: `base-lines.spec.ts`, `small-bases.spec.ts`, `widget-view.spec.ts`, `chart-widgets.spec.ts` and `widget-dialog.spec.ts` are updated to the chips and pass: `Lines above the footnote`, `Chart cut above the lines`, `Line shown`, `Hidden by a widget`, `Shown by a widget`, `Widget without a base n`, `Footnote under the chart`, `Footnote under the table`, `No footnote`, `Rate of the left-out rows`, `Share of a count` and `Nothing left out`.
- [x] 4.4 Every chart's `dimmed` output becomes `SmallMark[]` (design D5): `AxisChart` with `seriesPlot` cells, `PieWidget`, `MapWidget`, `HeatmapChart`, `PyramidChart`, `LikertChart` and `BoxplotChart`, named as shown, in drawing order. The view builds the small chip's text with up to five names and "and N more".
  Verify: `small-bases.spec.ts` and `series-widgets.spec.ts` cases pass: `Two comunas` (the whole text), `Cells of a series` ("COMUNA 7 · 1. Sí"), `More than five`, `Columns dimmed` and `Nothing dimmed`.
- [x] 4.5 In `docs/indicators-dashboard.md`, rewrite "The lines under a widget" as "The foot of a widget": the chips, their order, how they open, what screen readers hear, and paper.
  Verify: the section names the seven chips in the spec's order, and `IndicatorDocsTests` (if it reads the section) passes.

## 5. Stat tiles (ui)

- [x] 5.1 In `StatWidget`, drop the figure's opacity (keep `data-small`), the small-base note and the n line, so that `lines` holds only "Without filters". Stop emitting `dimmed`. Remove `dimmed` from `ExportParts.stat` and keep `secondary` as that one line (design D6).
  Verify: a new `stat-widget.spec.ts` passes:
  - `Stat tile not dimmed`: no opacity style, the heading color, `data-small` set;
  - `Value without the dashboard filters` and `Median without the dashboard filters`: the line under the label;
  - no small-base note or n line inside the tile;
  - the export parts hold the value, the description and the one line.
  `chart-widgets.spec.ts`'s stat cases are updated.
- [x] 5.2 Move the n rule of `StatWidget.nText` to `statNLine` in `base-figures.ts`, and build the stat tile's n chip in `WidgetView` from it: shown when the tile or the dashboard shows the n, or when it is small; `warning` below the threshold, with the label "n = {{n}}" and `indicators.foot.smallStat` as its text. Do the same for a chart's n chip below the threshold, with `indicators.foot.smallChart`.
  Verify:
  - `base-figures.spec.ts` covers `statNLine` with and without a statistic and a base column;
  - `widget-view.spec.ts` cases pass: `Base enough`, `Small base of a stat tile`, `Small base with the n not shown`, `Threshold off`, `Base under the value`, `Base n line of the dashboard`, `Nothing declared`, `Lines of a tile that hides its caption`, `Tile short of room` and `Measure with no base`;
  - the Detail chip of task 2.3 shows on compact tiles.
- [x] 5.3 In `docs/indicators-dashboard.md`, update the stat tile's "Lines under the figure" (the n in the foot, amber below the threshold), "Room" (Detail on compact tiles) and "Small bases" (the figure is no longer dimmed).
  Verify: no sentence of the docs says a stat figure is dimmed or that its n is under the figure.

## 6. Reference line texts and data labels (ui)

- [x] 6.1 In `placeReferences`, add `obstacles` and the right-to-left search for a horizontal line's box, keep the bar-chart position, and grow `referenceMargin` to 16 (design D7). `AxisChart` and `seriesPlot` pass the marks' boxes.
  Verify: `chart-annotations.spec.ts` cases pass:
  - `Free stretch on the left`: the 3.1 columns, with only Estrato 4 above the line;
  - `No free stretch`: the text at the right end;
  - `Bar chart`;
  - `Lines close together`;
  - `Text too wide for the plot`;
  - a text never placed over a data label already placed.
  `reference-lines.spec.ts` and the shared case file `testing/reference-lines.json` are unchanged.
- [x] 6.2 Draw the box (`<rect>` with `fill-neutral-primary-soft stroke-default paper:fill-white`) under each placed text. Stop drawing `ReferenceKey` in `AxisChart` and `seriesPlot` (keep `ReferenceList` under tables). Expose the unplaced lines to the view for the `lines` chip, and remove the `dashed` legend entries of the export parts. Remove `ReferenceKey` and the `dashed` swatch once nothing uses them. In `docs/indicators-dashboard.md`, rewrite "The key" as "Texts at the lines".
  Verify: `chart-widgets.spec.ts`, `series-widgets.spec.ts` and `series-legend.spec.ts` cases pass: `Key above the footnote` (no `reference-key`, the text at the line, the note chip), `Mean line on horizontal bars`, `Text left out` (the `lines` chip opens the text), `Screen reader` and `Column chart as a table` (the list under the table).
- [x] 6.3 Data labels (design D8): add `paper:stroke-white` to the halo of data labels and of reference texts, and make the reference box part of `taken`.
  Verify: `chart-widgets.spec.ts` cases pass: `Line crossing a label` (the label drawn after the line, with its halo), and `Label under a line's text` (left out, tooltip kept).

## 7. Paper (ui)

- [x] 7.1 Check the foot on paper: chips hidden, lines shown whole in order, the `detail` line of a compact stat tile under its figure, titles and subtitles unclamped. Fix what the check finds in the `paper:` classes. In `docs/indicators-dashboard.md`, update "Printing a dashboard".
  Verify: `dashboard-print.spec.ts` cases pass:
  - `Foot printed as lines` and `Printed in black and white`;
  - `Amber n of a stat tile on paper` and `Line chip on paper`;
  - `Whole texts on paper` (title, subtitle and footnote), `Hidden caption on paper` (under a subtitle too), `Own caption text on paper` (the foot's first line);
  - `Reference lines printed` (texts at the lines, no key), `Key in a tile the texts fill`;
  - `Tiles of a long dashboard` and `Secondary lines on paper`.

## 8. Images (ui)

- [x] 8.1 Feed the image from the foot (design D10):
  - `exportNotesBelow` gives the caption, n, small, noData and lines texts (no `detail`, no `note`);
  - a stat tile's n goes first among them;
  - `parts.caption` follows `captionInSight`;
  - `composeImage` no longer dims a figure or lists reference lines in the legend.
  In `docs/indicators-dashboard.md`, update "Images".
  Verify: `image-layout.spec.ts`, `export-parts.spec.ts` and `widget-export-flow.spec.ts` cases pass:
  - `Dimmed columns in an image` and `Lines in an image` (whole texts);
  - `Lines before the footnote in an image`, `Subtitle and footnote` and `Caption hidden` (under a subtitle);
  - `Own caption text` (no subtitle), `Own caption text under a subtitle` (the foot's first line), `Chip open` (no chip in the picture);
  - `Mean line in an image` (no legend entry), the two `Lines above the footnote` scenarios;
  - `Secondary lines in the image` (the n as the first foot line), `Small base in the image`, `Smaller value` and `Long figure`.

## 9. Integration checks

- [x] 9.1 Run the whole API suite in the background (unit, then integration), with the output saved to a file in the scratchpad.
  Verify: everything passes apart from the failures known on a clean `main` (the memory note lists them), and the counts are recorded in design.md.
- [x] 9.2 Run the UI suite (`TZ=UTC`), `npx ng build` and `scripts/check-ui-safety.sh`.
  Verify: everything passes apart from the two known polling failures, and the safety script is clean. Record the initial bundle size against the 675 kB warning in design.md; if it passes the warning, the user decides before release (design Risks). The analytics suite is not run: this change does not touch `tamiza-analytics`.
- [x] 9.3 Check the real stack in a separate Compose project (the `tamizaverify` recipe). Read the 42 widgets of "2. Teatro" from Duitama with `get_dashboard` (read-only MCP, never a write), and rebuild them with the same chart types, sizes, places, subtitles, captions, footnotes, reference lines, data labels, `secondary` and `minN` over invented tables shaped like `teatro_hoy`, `teatro_conteo` and `teatro_precio_hogar`, with no production data. Give the dashboard threshold 30 and `showN`. Then check, at 1,280 and 390 px, in light and dark:
  - every title on at most two lines, with its whole text on pointing;
  - the subtitles at 13 px and darker;
  - no caption line under the subtitles of bar, column, line and pie charts, their own text as the foot's first chip with its ruler icon, the unit as the value axis title on 2.1 por comuna, por estrato and por grupo de edad, and the heat maps' caption lines still in sight;
  - the 16 stat tiles whole, "Detail" on the nine 4×2 ones, the subtitle under the title on the 3×3 and 3×6 ones, no dimmed figure, and the amber n on a small base;
  - the foot of 2.1 por comuna: the caption chip, n, "2 with a small base" naming COMUNA 7 and SIN NOMBRE, and "Note";
  - the chips by pointer, keyboard and touch (Chrome's device mode), one at a time with a description open;
  - the "Total Duitama" texts on their boxes at the free stretch, and no key;
  - the data labels over the lines;
  - no horizontal page scroll.
  Then print to PDF on A4 in both orientations and export a PNG and an SVG of 2.1 por comuna and of one amber stat tile.
  Verify: findings, screenshots' notes and fixes are recorded in design.md "Verification Results", and every text the foot opens on screen is in the PDF and the images.
- [x] 9.4 Screen readers on the stack of task 9.3: NVDA with Chrome, and VoiceOver with Safari if a Mac is at hand. Tab through the foot of 2.1 por comuna and of an amber stat tile, and read a compact tile's description.
  Verify: each chip is read once with its name, its state and its whole text. If a reader misses the `aria-describedby` text (design Risks), move the description to a visually hidden span and record it.
- [x] 9.5 Send a scheduled email of the rebuilt Teatro to Mailpit.
  Verify: the body, the plain text and the workbook carry the captions, notes and footnotes they carried before this change, with no chip text (`dashboard-reports` delta).
- [x] 9.6 From an MCP client, give one rebuilt column chart an own caption text with `update_widget` and read the result's `notes`, then set `caption: { hidden: false }` on it and read it back with `get_dashboard`.
  Verify: the results match the `mcp-server` delta: the first call returns the note, the second none, and the page shows the caption line again with no caption chip.
- [x] 9.7 Run `openspec validate improve-widget-text-hierarchy --strict`.
  Verify: it passes.

## Workflow follow-up

- Write "Implementation notes", "Deviations from the plan" and "Verification Results" in design.md. Add this change to the roadmap in `docs/tamiza-brief.md` (item 7l), and have the user review them.
- Archive the change with `/opsx:archive` once the user has reviewed the results.
- After deploying, have the user open the real "2. Teatro" of Duitama (they sign in themselves) and compare it with the mockup's three cases; record the findings.
- Rebase the active changes on this one with `/opsx:update` when their turn comes:
  - `add-locale-formatting`: the chips' numbers and the axis title;
  - `add-category-highlight`: the `dimmed` list, the halo and its neutral gray;
  - `add-dashboard-sections`: the grid header;
  - `add-normalization-and-totals`: its notes go to the foot.
