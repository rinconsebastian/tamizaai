# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This change follows the pattern `add-dashboard-palettes` set for draw-only settings (`config.display`, design D3 there) and for a dashboard-level setting (its own `PUT`, a change kind, version content written only when not the default, design D5 and D6 there). It is planned to land after `add-indicator-column-metadata`, which adds `display.labels`, the column names in the "what" of a caption, migration 0021 and the production-shapes fixture.

- **A widget's display.**
  - `WidgetDisplay(Color, Ramp, Orientation)` (`tamiza-api/src/Tamiza.Api/Indicators/IndicatorModels.cs:157-173`) writes each member only when set, and `Normalize()` (`:167-172`) returns null when all three are unset.
  - `WidgetConfigInput.Display` and `WidgetConfig.Display` are the only `WhenWritingNull` members of a config (`:214-215`, `:255`).
  - The stored text of configs without a display is pinned by `WidgetDisplayTests` (`tests/Tamiza.Api.UnitTests/Indicators/WidgetDisplayTests.cs:15-20`).
  - `CheckDisplay` (`Indicators/IndicatorValidation.cs:164-210`) gives the per-kind rules, with keys `config.display.*`; `CheckTitle` (`:29-39`) counts a trimmed title against `MaxTitleLength` 200 (`:13`).
- **The twins that leave `display` out.**
  - `queryFor` (`tamiza-ui/src/app/pages/projects/indicators/widget-view.ts:102-121`) and `WidgetQuery.For` (`Indicators/WidgetQuery.cs:14-30`) never send `display`, and the widget's re-query key is built from `queryFor` (`widget-view.ts:429-432`).
  - `savedConfig` (`widget-view.ts:151-159`) adds `displayFor` (`:75-83`), which keeps only the display fields each kind takes.
  - The form rebuilds the config in `formConfig` and `displayDraft` (`widget-dialog.ts:904-924`, `:931-938`).
- **What "what" is.** `plotted(config, label)` (`indicators/format.ts:108-114`) and its twin `ReportDataBuilder.Plotted` (`Reports/ReportDataBuilder.cs:427-431`) write the indicator's label, with the measure or the ratio's two columns. They feed:
  - the caption line of a chart with no series (`axis-chart.ts:44-49`, `:381-387`);
  - the caption lines of a pie, a map, a heat map and a cross table (`pie-widget.ts:50`, `map-widget.ts:77`, `heatmap-chart.ts:146`, `crosstab-widget.ts:60`);
  - a stat tile's label (`stat-widget.ts:15`, `:30`);
  - the "what" line of series and Likert tooltips (`axis-chart.ts:210` into `series-plot.ts:174`, `likert-chart.ts:158`);
  - the value column of "Show as table" (`axis-chart.ts:441-449`) and the accessible summaries;
  - on the API, the email caption, the value column's header and the workbook's "Plotted" line (`ReportDataBuilder.cs:116`, `:201-202`; `Reports/WidgetWorkbook.cs:71`).

  The map's tooltip names the plotted value with the indicator's label, not with `plotted` (`map-widget.ts:789` into `map-tooltip.ts:68`). Pyramids, Likert widgets and box plots draw no caption line (`caption: null` in their export parts, `pyramid-chart.ts:297`, `likert-chart.ts:324`, `boxplot-chart.ts:544`).
- **The unit.** It comes only from the indicator: `widget-view.ts:343-345` builds `formatter` and `boxFormatter` from `IndicatorEntry`. The email and both workbooks take it in `ReportDataBuilder.MetaOf` (`:76-77`), which gives a widget whose indicator the project no longer lists two decimals and no unit, as `plainIndicator` does on the page. MCP takes it in `McpDashboardTools.MetaAsync` (`Mcp/McpDashboardTools.cs:576-579`), and `Row` formats with it (`:583-589`). The indicator's description is loaded with the overview (`IndicatorEntry.description`, `core/projects/indicators.service.ts:293-303`) and shown only in the widget form (`widget-dialog.ts:197-198`).
- **The tile.**
  - Its header holds the drag handle, one truncated title line and the menu (`widget-grid.ts:59-83`, title at `:73`). With `py-1.5` around the 40 px menu button (`min-h-10`, `shared/ui/dropdown-menu.ts:50`) and its bottom border, it is 53 px tall. Its body is `p-3 overflow-hidden` around the widget view (`:84-98`).
  - The widget view puts the chart in `<div class="min-h-0 flex-1">` (`widget-view.ts:214`), after the "not filtered" and row-limit notes (`:197-203`).
  - A stat tile centers its figure (`text-4xl`, a 40 px line) and its label (`text-sm`, 20 px) in a `flex h-full flex-col justify-center gap-1` column (`stat-widget.ts:13-16`): 64 px.
  - Rows are 72 px with 16 px gaps (`grid-layout.ts:4-5`), and a stat tile is 3×2 by default (`:18-19`). The page is at most 1,280 px wide (`app.html:41`, `max-w-screen-xl`), so a grid column is about 88 px.
  - Charts size from `observeSize` of their own box (`chart-data.ts:65`). The axis charts draw at no less than 160×120 (`axis-chart.ts:203-204`).
  - Paper mode keeps the grid and re-measures (`dashboard-print.ts`).
- **A dashboard.**
  - `Data/Dashboard.cs:25-29` holds `Palette` and `ValueColors`, mirrored in `Data/TamizaDbContext.cs:418-426`.
  - `DashboardView` is at `IndicatorModels.cs:327-329` and is read by `DashboardEndpoints.ViewAsync` (`Indicators/DashboardEndpoints.cs:85-100`).
  - `SaveColorsAsync` (`:227-268`) is the pattern of a dashboard setting with its own `PUT`, and `DuplicateAsync` copies the dashboard's settings (`:426-504`, `:476-478`).
- **Versions.**
  - `DashboardChangeKinds` (`Indicators/DashboardVersions.cs:15-57`) is mirrored in `indicators.service.ts:330-361`.
  - `Content` (`DashboardVersions.cs:339-370`) writes `palette` and `valueColors` only when they are not the defaults. `ReadContent` (`:457-477`) reads what a content lacks as the default, and a restore writes the settings back (`:632-634`).
  - `DashboardVersionDetails` is at `DashboardVersionEndpoints.cs:42-57`.
  - `dbUp/README.md:24` names what a script that rewrites `dashboard_versions.content` must keep.
- **Images.**
  - `composeImage` (`widget-export/image-layout.ts:96-276`, its drawing order at `:133-175`) draws the title, the notes above, `parts.caption`, the body (a stat tile's figure at 36 px and its description), the legend, the notes below and the footer, from `ImageInput` (`:27-46`).
  - `ImageJob` (`widget-export/widget-exporter.ts:16-32`) is filled by `project-indicators.ts:766-806`.
  - `FooterFacts` and `footerLines` are at `widget-export/footer.ts:2-25`.
- **Emails and workbooks.**
  - `ReportModel` (`Reports/ReportModel.cs:85-96`) and `ReportWidget` (`:41-54`) are written by `ReportHtml` (header `ReportHtml.cs:78-95`, stat tiles `:98-127`, blocks `:133-176`), by `ReportPlainText` (`:198-278`) and by `ReportWorkbook` (summary `ReportWorkbook.cs:26-46`).
  - The widget workbook's summary is at `WidgetWorkbook.cs:63-90`.
- **The widget workbook's request.** Before `add-indicator-column-metadata`, `project-indicators.ts:808-835` sent `config: queryFor(...)` (`:816`), which drops `display`, the basemap, the ignored filters, and the tooltip labels and formats. That change made it `{ ...queryFor(...), ...(labels ? { display: { labels } } : {}) }` (`:811-818`, `labels` read from `savedConfig(...).display`) and `ReportDataBuilder` reads `Display?.Labels` (`:118`); D10 sends the whole saved config, which carries the labels too. The real-stack check of that change (task 9.4) confirmed the loss: a map's workbook headed its tooltip column "Total de hogares" where the page and the scheduled email read the item's own label "Total propio". The TS doc (`indicators.service.ts:136`) says "as `queryFor` builds it", and the C# doc says "as a widget is saved" (`Indicators/WidgetExportEndpoints.cs:30`). The endpoint normalizes and validates the config (`:73-74`) and `ReportDataBuilder` rebuilds the query with `WidgetQuery.For` (`ReportDataBuilder.cs:123`). It passes the prefix `"config"` without a dot (`:74`), where a widget's save passes `"config."` (`DashboardEndpoints.cs:527`), so its errors read `configindicator`.
- **MCP.**
  - `DisplayHelp` and `ChartTypeChangeHelp` are at `McpDashboardTools.cs:50-60`, and the config template of `create_widget` at `:346`.
  - `update_dashboard` is at `:238-297`, with its refusal when nothing is given at `:257-260`, and `get_widget_data` at `:168-202` and `:592-660`.
  - Results are serialized without nulls (`McpResultBudget.cs:18-21`).
  - Pinned texts: `McpToolCatalogTests.cs:85-100` (the display sentence) and `:121-131` (the kinds of change), `IndicatorDocsTests.cs:202-232` (versions in `docs/mcp.md`) and `:286-308` (the dashboard menu in `docs/indicators-dashboard.md`), and `McpGuideTests`.

## Goals / Non-Goals

**Goals:**
- Every new widget field lives in `config.display`, so the query twins do not change, and versions, duplicates and moves carry the fields with no code of their own.
- A widget or a dashboard that sets nothing keeps its stored JSON and version content byte for byte, and looks, prints, exports and is sent exactly as before.
- One function per side decides what a widget calls what it plots (`plotted` and `Plotted`). One place per side decides its unit, merged before the formatters, which do not change.
- The widget workbook is built from what the page shows, as the email is.

**Non-Goals:**
- Growing a tile to fit its texts, or changing stored layouts.
- Line breaks, links or formatting in the texts, and translations of them.
- A per-widget format or decimals (`add-locale-formatting`), the automatic base and "Sin dato" footers (`add-small-sample-warnings`), the stat tile's font (`improve-stat-cards`).
- Showing the indicator's description anywhere but the widget's own button (print, images, email, MCP widget data).

## Decisions

### D1. The widget's texts live in `config.display`

| Field | JSON | Limits | Chart types | Left out |
|---|---|---|---|---|
| `display.subtitle` | text | 1 to 200 characters once trimmed | every one | no subtitle |
| `display.footnote` | text | 1 to 500 | every one | no footnote |
| `display.caption` | `{ "text": "…" }` or `{ "hidden": true }` | text 1 to 200; not both | every one | today's caption |
| `display.unit` | text | 1 to 32 (`ScriptEndpoints.MaxUnitLength`, the indicator's own limit) | every one | the indicator's unit |
| `display.showDescription` | `true` | only `true` is stored | every one | no description button |

- **C#.**
  - A new record `WidgetCaption(string? Text, bool? Hidden)`, both members `WhenWritingNull`.
  - `WidgetDisplay` gains `Subtitle`, `Footnote`, `Caption`, `Unit` and `ShowDescription`, each `WhenWritingNull` with a `[Description]`. They are appended after `Labels`, which `add-indicator-column-metadata` made the last member of the record (after `Color`, `Ramp` and `Orientation`), so the existing members keep their place in the JSON.
- **TS.** `WidgetDisplay` gains `subtitle?`, `footnote?`, `caption?: WidgetCaption` (`{ text?: string; hidden?: true }`), `unit?` and `showDescription?: true`.
- **Universal.**
  - `CheckDisplay` takes them on every kind, and `displayFor` keeps them on every kind.
  - A change of chart type never strips or refuses them, so `ChartTypeChangeHelp` and its pinned sentences do not change (program rule 5).
- **Not in the query.** `queryFor` and `WidgetQuery.For` already leave `display` out, so editing these fields never queries again, and the shared cases of `query-for.spec.ts` and `WidgetQueryTests.cs` gain one case each: a config with every field gives the query of the same config without them.

*Alternatives:*
- *Columns of `dashboard_widgets`, as `title` is.* Rejected:
  - it needs a migration and code in every copy path (version content, restore, kept copies, duplicate, move, MCP) for text that only draws;
  - versions, duplicates and moves already carry `config` whole.
- *Top-level config fields.* Rejected: the top level is the query, and both twins would have to leave them out by hand.
- *`caption: { "mode": "default" | "text" | "hidden" }`.* Rejected: an enum whose default value a form would store, and whose new values an older API could not refuse cleanly (program rule 7). `{ text }` and `{ hidden: true }` say the same with nothing to store for the default.
- *A caption string with a sentinel such as `"-"` for hidden.* Rejected: it is a trap for a member whose caption is "-".

### D2. Normalizing and validating the fields

- **`WidgetCaption.Normalize()`**:
  - a text is trimmed, and a blank one is none;
  - `Hidden` is kept when `true` and is none when `false`;
  - a caption with neither is none.
- **`WidgetDisplay.Normalize()`**:
  - trims the subtitle, the footnote and the unit, and reads a blank one as none;
  - reads `ShowDescription: false` as none;
  - normalizes the caption;
  - returns null only when every member, old and new, is unset (it extends the condition at `IndicatorModels.cs:167-172`), so a display that holds only a subtitle is kept.

  These fields have no default value a form could store: their default is to be absent. So `false` and blanks are "not set", as an empty ramp is (palettes' deviation "An empty display is none"). The box plot's rule of keeping a saved default (its D1) has nothing to keep here.
- **Trimming** follows titles and dashboard names, which are stored trimmed. A stored config is already trimmed, so a form saved unchanged writes the same text.
- **`CheckDisplay`** adds, on every kind, with `WidgetDisplay.MaxSubtitleLength = 200`, `MaxFootnoteLength = 500` and `MaxCaptionLength = 200` (constants on the record; the page mirrors them in `indicators.service.ts`, and a unit test reads the page's copy as `PalettesTests` does):

| Rule | Key | Message |
|---|---|---|
| Subtitle longer than 200 | `config.display.subtitle` | "The subtitle is longer than 200 characters." |
| Footnote longer than 500 | `config.display.footnote` | "The footnote is longer than 500 characters." |
| Caption text longer than 200 | `config.display.caption.text` | "The caption is longer than 200 characters." |
| Unit longer than 32 | `config.display.unit` | "The unit is longer than 32 characters." |
| A control character in one of the four texts (`char.IsControl`: U+0000 to U+001F and U+007F to U+009F, line breaks and tabs included) | the field's key | "The {subtitle} cannot contain line breaks or other control characters." |
| A caption with a text and `hidden` | `config.display.caption` | "Give the caption its own text or hide it, not both." |

- **Why control characters are refused.**
  - A NUL character cannot be stored in `jsonb` and failed with a 500 (palettes' deviation "A NUL character in a kept value is refused").
  - A line break would need its own handling on each surface (HTML email, SVG text, Excel cells, the clamp on screen).
  - The form turns typed or pasted line breaks into spaces before sending (D11), so the rule only refuses an API or MCP client.
- **Lengths** are counted as `CheckTitle` counts a title: UTF-16 units after trimming.

### D3. The caption: one function says what a widget plots

- **Own text.**
  - `plotted(config, label, names = rawNames)` (`format.ts`) and `ReportDataBuilder.Plotted(config, label, Func<string, string>? names = null)` return `display.caption.text` when it is set, and otherwise what they return today: the indicator's label and the names shown for its measure or its numerator and denominator (`ColumnNamer`). The own text replaces that whole "what", the label and the column names together, so `dashboard-indicators` "Column labels in widget texts" and "Widget caption text" do not contradict each other.
  - Every caller already passes the widget's config (see Context), so the caption lines, the stat tile's label, the series and Likert tooltip lines, the table headers, the accessible summaries, the image parts and the email and workbook headers follow with no code of their own. `plotted`'s `Pick` type (`'measure' | 'ratio'`) gains `'display'`, and its callers keep passing `this.config()`, `this.label()` and `this.names()` as they do (`axis-chart.ts`, `boxplot-chart.ts`, `crosstab-widget.ts`, `heatmap-chart.ts`, `likert-chart.ts`).
  - The map's tooltip line of the plotted value takes the own text when it is set, and keeps the indicator's label otherwise.
- **The rest of the line stays.** The text takes the place of `{what}` in `"{what} by {group}"`, `"Rows per range of {what}, in {buckets} ranges"` and `"{what} by {group} and {series}"`.
- **Hidden.**
  - A new `captionShown(config)` in `format.ts` decides it. Each caption line `<p data-testid="chart-caption">` and the stat tile's label `<p data-testid="stat-label">` stay in the DOM and take `[class.sr-only]="!captionShown(config())"`: they are no longer seen and take no room, and a screen reader still reads them. A stat tile and a cross table have no other text that names what they plot, so removing the line with an `@if` would take it from screen readers too.
  - The image leaves out `parts.caption` and a stat tile's description when the caption is hidden (D8), so no component's `registerExportParts` changes. An image holds no text meant only for screen readers, so the `sr-only` line never reaches it.
  - Accessible names, tooltips, "Show as table" and the email and workbook headers keep `plotted`, as `dashboard-indicators` "Widget caption hidden" requires.
  - A pyramid, a Likert widget and a box plot draw no caption line, so hiding it changes nothing in their chart view. Their "Show as table" view is the cross table or the box table, and the cross table's caption line is hidden.
- **A series legend** (`axis-chart.ts:44-46`) lists series values, and neither mode touches it.

*Alternatives:*
- *The own text replaces the whole line.* Rejected: the line would lose its group and series ("by comuna"), and a histogram's "Rows per range of …, in 10 ranges", which says the bars count rows. An author who wants a line of their own hides the caption and writes a subtitle.
- *Hidden also in tooltips and tables.* Rejected: a table needs a header, a screen reader needs to know what a chart plots, and the user asked to hide the visible line.
- *A fourth mode: own text and hidden, the text only for tooltips and tables.* Rejected: the form would have to explain it. It is refused with a message instead.

### D4. The unit: merged before the formatters

- **UI.** `widget-view.ts:343-345` builds `formatter` and `boxFormatter` from `withWidgetUnit(indicator() ?? plainIndicator, config())`. That is a new helper in `format.ts` (not named `withUnit`, which `valueFormatter` already uses for a local, `format.ts:60`) that returns `{ ...indicator, unit: config.display?.unit ?? indicator.unit }`. `valueFormatter` does not change, and the `%` rule (a unit of `%` is not repeated) applies to a widget's unit as to an indicator's.
- **API.** `ReportDataBuilder.MetaOf(widget, indicator)` takes `widget.Config.Display?.Unit ?? indicator?.Unit`. So the email body, both workbooks, their headers (`ValueColumn`, `:201-202`) and a box plot's table (`BoxplotTable` reads the same meta) follow. A widget whose indicator the project no longer lists keeps its own unit too, on the page (`withWidgetUnit` over `plainIndicator`) as in the email, and its own caption text, since `Plotted` returns it whatever the label (`dashboard-reports` "Missing values in widget tables").
- **MCP.** `WidgetDataAsync` hands `Row`, `SeriesPayload` and `BoxPayload` an `IndicatorInfo` whose `Unit` is the widget's when it has one.
- **Not changed.**
  - `query_indicator` keeps the indicator's unit, as `mcp-server` "Indicator queries and widget data" says, since it previews a query.
  - A map's tooltip columns keep their own formats.
  - A histogram's range labels are written without a unit, as today.
- **Twins.** `valueFormatter`, `ReportFormat` and MCP's English formatter are unchanged. Only the unit they are given changes, in the three places above, which one test per side pins (tasks 4.1, 5.1, 7.2).

*Alternatives:*
- *A unit per column in the indicator's metadata.* Rejected: the base differs per widget over the same indicator (the same `teatro_hoy` gives "de los hogares" and "de quienes asistieron" under different filters).
- *A widget's own format as well.* Rejected: decimals and formats belong to `add-locale-formatting`.

### D5. The texts in a tile of fixed height

- **Where.**
  - The subtitle goes in the tile's header, which becomes a column only for a widget that has one: the existing row (drag handle, title, description button, menu), then `<p id="widget-subtitle-{id}" class="text-xs text-body">`, and the `article` gains `aria-describedby` on it. A widget without a subtitle keeps today's header element and classes.
  - The footnote goes at the foot of the widget view, after the chart or table box: `<p class="shrink-0 text-xs text-body" data-testid="widget-footnote">`. It is shown when the widget shows its chart, its table or its "no rows" message, and not while it loads, fails, or says its indicator is not published or a column is gone, since those messages replace the widget's content.
  - The widget form's preview shows both.
- **Room.**
  - The tile's height never changes.
  - The texts never shrink, and the plot box (`min-h-0 flex-1`, `widget-view.ts:214`) takes the rest. Every chart re-measures through `observeSize`.
  - A widget with a footnote gives its chart's box `overflow-hidden`. The axis charts draw at no less than 160×120, so a chart in a box that is too small is cut at its box and never painted over the footnote. A widget without a footnote keeps today's box and classes, so nothing it draws today is newly cut (a focus ring at the edge, for instance).
  - A stat tile with a subtitle or a footnote centers its column with `justify-center-safe` (Tailwind 4.3, `justify-content: safe center`) in place of `justify-center`: while it fits, it is centered as today; when it does not, it starts at the top, so its figure stays whole and its label is the part cut. A stat tile without them keeps `justify-center`.
- **Clamping on screen.**
  - `line-clamp-2`, or `line-clamp-1` on a tile whose `layout.h` is 2 or less. The whole text stays in the DOM, so screen readers read it whole, and in a `title` attribute, for pointing.
  - On paper the clamp is lifted (`paper:line-clamp-none`), and the texts print whole.
- **What it costs** (on a 1,280 px page, with the 53 px header, the 24 px of body padding and the 2 px border of the tile):
  - A 6×4 tile (336 px) has a body of about 257 px, about 230 of them for its plot under the caption line. Two lines of subtitle and two of footnote (16 px each, plus a 4 px gap) take about 68 px of it.
  - A 3×2 stat tile (160 px) has a body of 81 px, and its figure and label take 64 px. A one-line subtitle takes 16 px and a one-line footnote 20 px, which leaves 45 px: the figure (40 px) shows whole and its label is cut (safe centering above). With the caption hidden, or with only a subtitle (65 px left), the tile shows what it shows today. Teatro's 4×2 stat tiles have the same height and the same arithmetic. `improve-stat-cards` later makes the figure smaller, which gives the label its room back.
  - A line of a 6-column tile holds about 90 characters of 12 px text, and one of a 3-column tile about 40.
  - On paper the texts are whole, so a long footnote in a small tile can leave the plot less than its least size; the chart is then cut at its own box, never over the texts (`dashboard-printing` "Charts on paper").
  - The tile itself never grows, on paper either. When the texts alone need more than the tile's body (a 500-character footnote in a tile 2 rows high, for instance), the tile's body clips what does not fit, on paper as on screen. The whole texts print as long as the tile can hold them; the form's warning is the remedy, and a taller or wider tile the way out.
- **The warning.** The widget form shows a hint (not an error) when the texts are likely to crowd the tile: a tile 2 rows high or less with a subtitle or a footnote, or a subtitle and footnote that, printed whole, would take half of the tile's height or more. The estimate is a pure function `textsCrowd(layout, subtitle, footnote)` in `format.ts`: about 15 characters per grid column per line, 16 px per line and 4 px per text, against half of `h × 72 + (h − 1) × 16`. The hint says the texts take room from the chart or the figure and that a taller tile shows more of them. A new widget's size is its kind's default size.

*Alternatives:*
- *Tiles that grow to fit their texts.* Rejected: stored layouts and the 72 px rows are the dashboard's layout, and growing one tile would push every widget under it, on screen and on paper.
- *Long texts behind a button.* Rejected: it hides what the study asked every chart to show.
- *A fixed minimum plot height.* Rejected: the texts would then overflow the tile instead of the chart.
- *A smaller figure on a stat tile that has texts.* Rejected: `improve-stat-cards` sets the figure's size for every tile, from the room the tile has, and two rules for one figure would disagree. Until then the figure stays whole and the label is the part cut.
- *Clamping the texts on paper too, so the chart keeps its room.* Rejected: the program asks for whole texts on paper, and a cut note on a printed report cannot be pointed at; the form warns instead.

### D6. The indicator's description behind a button

- **When.** The button shows when `display.showDescription` is `true` and `indicatorOf(widget)?.description` is not blank. It sits in the tile's header, before the menu, with `paper:hidden`. Viewers see it, and so does a version shown: like the widget's label, the description is the project's current one.
- **The popover.** New `indicators/description-popover.ts`:
  - an icon button (`tmzButton`, ghost, small) with `aria-label` "About {label}" ("Acerca de {label}"), `aria-expanded` and `aria-controls`;
  - a CDK connected overlay, the overlay `ChartTooltips` already uses (`shared/ui/chart-tooltip.ts:104`), holding a non-modal `role="dialog"` named after the indicator, with the description as plain text (`whitespace-pre-line break-words`, at most 20rem wide);
  - opening moves focus into the panel, and Escape, a click outside or the button closes it and puts focus back on the button; so do Tab and Shift+Tab from the panel, which lives at the end of `<body>` and would otherwise take focus out of the widget's header;
  - the panel is at most `min(24rem, 70vh)` high and scrolls (an indicator's description can have 1,000 characters);
  - one is open at a time.
- **Never outside the page.** An image is composed from texts, not from the header's DOM (D8). Paper hides the button. The email, both workbooks and MCP widget data do not carry the description; MCP already returns it with `list_indicators` and `get_script`.

*Alternatives:*
- *A native `title` tooltip.* Rejected: neither a keyboard nor a touch screen reaches it.
- *An inline disclosure inside the tile.* Rejected: it pushes the plot.
- *Copying the description into the subtitle.* Rejected: the description is kept in the script's metadata and would drift.

### D7. The dashboard source

- **Migration** `0022_add_widget_context.sql`:

  ```sql
  ALTER TABLE tamiza.dashboards
      ADD COLUMN source text NULL,
      ADD CONSTRAINT ck_dashboards_source CHECK (source IS NULL OR char_length(source) BETWEEN 1 AND 300);
  ```

  `char_length` counts code points, at most as many as the API's UTF-16 count, so the check never refuses a source the API accepts. `Dashboard.Source` (`string?`) and `TamizaDbContext` mirror the column and the check.
- **Validation.** `DashboardValidation.Source(string?, FieldErrors)` returns the trimmed source, or null for a blank one. It refuses more than `MaxSourceLength = 300` characters and control characters on `source`, with messages worded as in D2.
- **Saving.** `PUT /api/v1/projects/{projectId}/dashboards/{dashboardId}/source`, role `analyst`, body `{ "source": "…" | null }`, answer `{ "source": … }`, built as `SaveColorsAsync` is:
  1. validate;
  2. return without writing when the source equals the stored one;
  3. `ExecuteUpdateAsync` with `UpdatedBy` and `UpdatedAt`;
  4. `versions.Note(…, SourceChanged)`.

  It has OpenAPI.
- **Reading.** `DashboardView` gains `Source` (`WhenWritingNull`, last), so a dashboard without one returns the JSON it returns today. `DuplicateAsync` copies it.
- **Versions.**
  - `DashboardChangeKinds.SourceChanged = "sourceChanged"` joins `All` after `ColorsChanged`, and the page's list after `colorsChanged`, with the history's words "Source changed" and "Se cambió la fuente".
  - `Content(name, filters, palette, valueColors, source, widgets)` writes `"source"` after `valueColors`, only when it is set. A dashboard without a source keeps its content byte for byte, its next save makes no version for the new shape alone, and a restore's step 2 records no "unrecorded" version. `ContentAsync`, `FirstVersion` and the restore pass the source.
  - `ReadContent` reads a missing or non-string `source` as null, and `VersionContent` gains `Source`.
  - A restore writes it back with the filters and colors, so a version from before sources leaves the dashboard without one.
  - `DashboardVersionDetails` gains `Source` (`WhenWritingNull`).
- **The widget workbook** reads the dashboard's source, or the version's when one is named (`WidgetExportEndpoints.cs:98-131`), into `WidgetWorkbookModel.Source`.
- **The page.**
  - `Dashboard.source?` and `DashboardVersionDetails.source?`, `saveSource(projectId, dashboardId, source)`, and `sourceChanged` in `DashboardChangeKind` and `dashboardChangeKinds`.
  - `project-indicators.ts` keeps a `dashboardSource` signal, set where `dashboardColors` is: read with the dashboard (`:634`), reset to null when another dashboard opens (`:682`), set after a save (as the colors are at `:1238`) and after a restore (`:1424`). A `source` is computed from the version shown or the dashboard, as `colors` is (`:508-511`).
  - The action `{ id: 'source', label: t('dashboards.menu.source'), testId: 'dashboard-source' }` goes after `colors` (`:441`).
  - A new `indicators/dashboard-source-dialog.ts`, built like `dashboard-name-dialog.ts`: one text field of at most 300 characters, an empty one to remove the source, the server's error on the field, and no request when nothing changed. Focus goes back to the actions menu's button when it closes (`custom-dashboards` "Focus after dashboard actions"). A save answered 404, because another member removed the dashboard, tells the member and opens the first dashboard, as a colors save does (`gone`, `:1232`; `custom-dashboards` "Changes by two members at the same time").
  - `<p data-testid="dashboard-source" class="text-sm text-body paper:hidden">` under the grid and before the indicators' disclosure (`:333-369`), with `dashboards.source.line` ("Source: {{ source }}" / "Fuente: {{ source }}").
  - The same words as a line of the print header (`:133-147`, `data-testid="print-source"`).

*Alternatives:*
- *A source on the project.* Rejected: one project's dashboards can come from different surveys or rounds, and versions, duplicates and the email are per dashboard.
- *A field of `PUT …/colors`, or of the rename `PATCH`.* Rejected: each dashboard setting has its own request and change kind, so the history says what changed, and a client of `PUT …/colors` is untouched.
- *The source under the widgets on paper too.* Rejected: it would print on the last sheet, far from the time and publication lines the reader checks first. The print header holds it once.

### D8. Images

- `ImageJob` gains `subtitle: string | null`, `footnote: string | null` and `captionHidden: boolean`. `FooterFacts` gains `source: string | null`, already written in the member's words ("Source: …"). `footerLines` gives it a line of its own after the project, dashboard and version line.
- `ImageInput` gains `subtitle` and `footnote`. `composeImage` draws, top to bottom:
  1. the title (3 lines at most, as today);
  2. the whole subtitle at 12 px in the body color;
  3. the notes above;
  4. the caption, unless `captionHidden`, in which case the exporter passes `parts.caption` as null, and a stat tile's description too (`ExportParts.stat.description` becomes `string | null`, `export-parts.ts:38`, and `composeImage` skips a null one);
  5. the body, the legend and the notes below;
  6. the whole footnote at 12 px;
  7. the footer.
- `project-indicators.ts:766-806` fills them from `widget.config.display` and the shown source.

### D9. Emails and workbooks

- `ReportModel` gains `Source`, read with the dashboard (`ReportDataBuilder.cs:24-27`). `ReportWidget` gains `Subtitle`, `Footnote` and `CaptionHidden`, filled from `widget.Config.Display` for every outcome, a reason included.
- **HTML body** (`ReportHtml`):
  - the header adds the line ("Source", source) after the filters when there is one;
  - a stat tile's cell writes the subtitle under its title, the caption only when it is not hidden, and the footnote after its notes;
  - a block writes the subtitle after its title and the caption only when it is not hidden. Its footnote comes after the "more rows" and "more columns" lines, or after the reason.
- **Plain text** (`ReportPlainText`) says the same in the same order. A stat tile's line drops its "(caption)" when the caption is hidden.
- **The email's workbook** (`ReportWorkbook`) adds a "Source" line after the filters, since it holds the header, and "Subtitle: {title}" and "Footnote: {title}" lines before a widget's notes line.
- **The widget workbook** (`WidgetWorkbook`) adds "Source" after the dashboard and the version, "Subtitle" after the widget, and "Footnote" before the notes. "Plotted" already comes from `Plotted`.
- `ReportText` gains `source`, `subtitle` and `footnote` ("Source" / "Fuente", "Subtitle" / "Subtítulo", "Footnote" / "Nota al pie"), checked by `ReportTextTests.Both_languages_have_the_same_keys`.
- The 80 KB budget of the body (`ReportHtml.cs:37-57`) counts the texts, since they are inside the blocks. A 500-character footnote is about 0.6 KB.

### D10. The widget workbook gets the saved settings

- `exportWorkbook` sends `savedConfig(widget.chartType, widget.config)` instead of `{ ...queryFor(…), display: { labels } }` (`project-indicators.ts:811-818`). The TS doc of `WidgetExportRequest.config` ("the widget's query, as `queryFor` builds it, plus `display.labels` when it has any", `indicators.service.ts:142`) becomes "as `savedConfig` builds it", as the C# doc already says. The widget's own labels still travel, now inside the whole display.
- The endpoint already normalizes and validates the config as a saved one, and builds the query with `WidgetQuery.For`, so the rows do not change. `MapTable` now gets the tooltip labels and formats (`ReportDataBuilder.cs:336-358`), as the email always had.
- `ignoredDashboardFilters` now travels in the config too. The endpoint already combines it with each filter's `ignored` flag (`WidgetExportEndpoints.cs:149`), so the filters that narrow the rows are the same.
- **Errors.** The endpoint passes the prefix `"config."`, as a widget's save does, so a refused display reads `config.display.subtitle` and not `configdisplay.subtitle`. No test or page reads the old keys.
- **No production widget is refused.** The display now reaches `CheckConfig`, which the query never ran on it. The production-shapes test checks that each shape's saved config passes `CheckConfig` for its kind (D13).

*Alternative:* send the display part beside `queryFor`'s config. Rejected: it would still lose the map's tooltip labels, and the endpoint already takes a saved config.

### D11. The widget form

- A **Subtitle** field under **Title** (`widget-dialog.ts:170-172`), `<input maxlength="200">`.
- A fieldset **Text on the widget** ("Textos del widget") after the color fields, with:
  - **Caption**: a radio group, Default / Own text / Hidden, with a text field (`maxlength="200"`) usable under "Own text", whose hint shows the line it makes ("{text} by {group}");
  - **Unit**: a text field (`maxlength="32"`) whose hint names the indicator's unit, or says it has none;
  - **Footnote**: `<textarea maxlength="500" rows="2">`;
  - **Show the indicator's description**: a checkbox, unavailable with the reason when the indicator has no description. One exception: a widget saved with the option on keeps the checkbox available while its indicator has lost its description, so the option can still be turned off.
- **State.**
  - Signals are seeded from `start.display`.
  - `displayDraft()` carries the fields trimmed, with line breaks turned into spaces. So a widget with none of them still saves no `display`, and a form saved unchanged sends the config it read.
  - The preview redraws from the draft. Its query key leaves `display` out, so it never queries again.
- **Errors.** `fieldFor` (`widget-dialog.ts:1605-1628`) maps `display.subtitle`, `display.footnote`, `display.caption`, `display.caption.text` and `display.unit` to their fields. It matches lower-cased substrings in order, and `config.display.subtitle` contains `title`, so the new checks go first, before the `title` check (`:1619`); otherwise a refused subtitle would show on the title field.
- **The warning** of D5 (`textsCrowd`), computed from the draft's texts and the widget's layout, or its kind's default size for a new widget.

### D12. MCP

- **The widget tools' texts.**
  - A new constant, `ContextHelp`, follows `DisplayHelp` in the descriptions of `create_widget` and `update_widget`: "config.display also takes the widget's texts, on every chart type: subtitle (1 to 200 characters), footnote (1 to 500), caption = { text } (1 to 200; it replaces the indicator's name wherever the widget names what it plots) or { hidden: true } (no caption line; tooltips and tables still name what is plotted), unit (1 to 32; it replaces the indicator's unit for this widget) and showDescription: true (a button that shows the indicator's description). None of them may hold a line break."
  - The pinned sentence stays as `add-indicator-column-metadata` left it: "config.display = { color, ramp, orientation, labels } sets how the widget is drawn" (`McpDashboardTools.DisplayHelp`). `ContextHelp` adds the new fields after it and does not rewrite it.
  - `get_widget_data` already returns a `labels` map per widget (the names shown for its columns, nulls left out). The new `subtitle`, `footnote` and `caption` are separate members beside it.
  - `create_widget`'s config template gains `subtitle, footnote, caption, unit, showDescription` in `display: { … }`.
- **The schema.** Each new `WidgetDisplay` member and both members of `WidgetCaption` have a `[Description]` and no enum, as the other nullable fields.
- **`get_widget_data`.**
  - Each widget's result, for every status (`Header` at `McpDashboardTools.cs:596-597` and the shown result at `:644-660`), gains `subtitle`, `footnote` and `caption` (the own text) when it has them. Nulls are left out, so a widget without them returns the result it returns today.
  - Its values are formatted with its unit (D4).
  - Its description says both, in one sentence after what it says today.
- **`update_dashboard`.**
  - It gains `source` (string): left out keeps the source, an empty text removes it.
  - The refusal when nothing is given names it: "Give a name, a position, filters, a palette, colors kept by value or a source to change."
  - It is one more commit of the same `RecordAsync`, so one request makes one version (`mcp-server` "One version per request"), and the result returns `source`.
- **The reads.** `get_dashboard` returns it through `DashboardView`, `get_dashboard_version` through `DashboardVersionDetails`, and `list_dashboard_versions`' list of kinds gains `sourceChanged` (`McpDashboardVersionTools.cs:28`, pinned by `McpToolCatalogTests.cs:121-131`).
- **`Instructions.md`** gains a section "## Words on a widget" after "## Colors":
  - put the question in `subtitle` and notes such as "no suman 100 %" in `footnote` rather than in the title;
  - use `caption` and `unit` when the indicator's name or unit does not fit the widget's base;
  - set the dashboard's `source` once;
  - send `display` again with `update_widget`.

  A `McpGuideTests` case pins it.
- **`docs/mcp.md`**:
  - the fields and `source`;
  - the row `| \`sourceChanged\` |` (pinned by `IndicatorDocsTests.cs:218-232`);
  - a sentence for the source after the pinned "Every change to a dashboard's name, filters, colors or widgets is recorded as a version", which stays.

### D13. Documentation and the production-shapes fixture

- **`docs/indicators-dashboard.md`**:
  - a section "## Words around a widget": the subtitle, the footnote, the caption's three modes, the widget's unit, the description button, the room the texts take in a tile, and what prints;
  - "### Dashboard source";
  - **Source** in the dashboard actions sentence (line 13, whose order `IndicatorDocsTests.cs:286-308` pins);
  - the widget's unit in "What a value means: format and unit";
  - the texts and the source in "Exporting a widget", "Printing a dashboard" and the version history.
- **`docs/operations.md`** "What leaves the deployment by email": the dashboard's source and the widgets' subtitles and footnotes, which members write.
- **`docs/tamiza-brief.md`** section 6: the `display` fields and `dashboards.source`.
- **The production-shapes fixture of `add-indicator-column-metadata`**:
  - is run unchanged: every production shape keeps its stored JSON and its query;
  - gains shapes that hold the new fields: their stored JSON is pinned, and their query equals that of the same shape without the fields;
  - checks that each shape's saved config passes `CheckConfig` for its kind, since the workbook request now sends it (D10);
  - gains a `dashboard` entry with a source, while the golden content of the existing one stays.
- **The shared fixture `testing/column-labels.json`** of the same change, which `plotted` and `Plotted` both pass, gains a case with an own caption text and one with a hidden caption, so the twins agree on the caption.

## Spec ownership

- **Modified, owned by this change** (program section 3):
  - `widget-export` "Parts of a widget image", "Footer of a widget image" and "Summary of a widget workbook";
  - `dashboard-reports` "Email header";
  - `dashboard-printing` "Print header";
  - `dashboard-indicators` "Indicators dashboard and its indicators", where the source follows the widgets.

  Every scenario is kept with its name and body.
- **Modified because this change contradicts it** (program section 3). Each edit is one clause and one scenario, every sentence and scenario of today's text is kept, and the later change that owns the requirement rebases on it with `/opsx:update`:
  - `dashboard-indicators` "Indicator formatting", owned by `add-locale-formatting`: it says a widget shows its values "with the format and unit of its indicator", which a widget's own unit contradicts. Scenario "Widget's own unit".
  - `dashboard-indicators` "Stat tile", owned by `improve-stat-cards`: it says a tile is "formatted with the indicator's format and unit". Scenario "Stat tile with its own unit". Its label is covered by the added caption requirements, since today's text says nothing of it.
  - `dashboard-indicators` "Map tooltip", owned by `add-small-sample-warnings`: it says the tooltip shows the plotted value "with the indicator's label, format and unit", which the own caption text and unit contradict. Scenario "Own caption text and unit in a map tooltip".
  - `dashboard-reports` "Missing values in widget tables", owned by nobody: it gives a widget whose indicator the project no longer lists "no unit, and the indicator's name as its label", while a widget's own unit and caption text still apply there (D3, D4). Scenario "Removed indicator with a unit of its own".
  - `dashboard-printing` "Charts on paper", owned by nobody: it says every chart prints "whole inside its widget's box", which whole texts in a small tile can make impossible (D5). The added sentence says the chart is then cut at its own box, never over the texts. Scenario "Texts that leave a chart too little room".
- **Owned and left as it is:** `dashboard-reports` "Widget notes in a scheduled email". The footnote's place went into the added "Widget footnote in a scheduled email", which keeps that requirement under 500 characters. Its text stays true.
- **Not touched, owned by `add-indicator-column-metadata`:**
  - `dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard" and `dashboard-indicators` "Moving a widget to another dashboard". They were generic when `add-indicator-column-metadata` was archived (every setting a widget is saved with). This change states its own fields in the added "Widget context kept with the widget", "Dashboard source in versions" and "Dashboard source in a copy".
  - `dashboard-reports` "Widget tables in a scheduled email". The own caption text and unit in headers are in the added "Widget context in a scheduled email".
  - `dashboard-indicators` "Column labels in widget texts", "Widgets and indicators without column labels" and `widget-export` "Column labels in widget files", added by that change: the own caption text replaces the whole line they describe, and a hidden caption keeps the names for tooltips, tables and the workbook.
- **Not touched, still true:**
  - `dashboard-indicators` "Marks of a widget with a series" and "Box plot marks": their scenarios read a value "with the indicator's unit" for widgets that have no unit of their own, which stays true.
  - `widget-export` "Sheets of a widget workbook": its "Map" scenario already expects the tooltip columns under their labels, which D10 makes true of the widget workbook.
  - `dashboard-reports` "Email body", "Email workbook" and "Plain-text alternative", and `dashboard-versions` "Changes that make a version", "Viewing a version" and "Restoring a version": they list what a block, a sheet or a version holds without saying it holds nothing else, and the added requirements add to them.
- **Not touched, owned by others:**
  - `mcp-server` "Indicator queries and widget data" (`add-small-sample-warnings`): `query_indicator` keeps the indicator's unit as it states, and `get_widget_data` "returns what one widget shows", which the widget's unit keeps true.
  - `mcp-server` "Dashboards and widgets through MCP", owned by nobody: this change adds "Widget context through MCP" and "Dashboard source through MCP".
- **Names.** Every added requirement starts with "Widget context", "Widget subtitle", "Widget footnote", "Widget caption", "Widget unit", "Widget indicator description" or "Dashboard source", which no other change of the program uses.
- **Builds on `add-indicator-column-metadata`** (archived 2026-11-04): the "what" of a caption with column names, migration 0021, and its two fixtures.

## Production impact

From the Duitama project (program section 5):
- **Widgets.** None of the 190 widgets has these fields, so their configs are byte for byte the same.
  - The 50 displays with `color` and the 13 with `ramp` keep their JSON: the new members are left out while unset, and `Normalize()` only gains fields to look at.
  - The deploy makes no version.
- **Dashboards.** None of the 9 has a source, so their version content is unchanged, their next save of something else makes the version it always made, and a restore's step 2 finds the content equal.
- **Titles.** The titles of mean 80 and up to 153 characters, with question codes, bases, n, "n < 30" and "no suman 100 %", are untouched. No title is rewritten or cut, and authors can move text into subtitles and footnotes when they choose.
- **Units.** "de los hogares" stays on the about 20 widgets whose base is a subgroup until their author sets a widget unit. A box plot of a count keeps ".00"; a widget unit changes only its unit word.
- **Tiles.** Teatro's 44 widgets keep their size and look, its 16 stat tiles (9 of them 2 rows high, 6 of them 3, one 6) included: a widget without a subtitle keeps today's header, one without a footnote today's chart box, and a stat tile without either today's centering (D5). The form warns only when an author adds a text to a tile 2 rows high, or one long enough to crowd its tile.
- **Emails.** The scheduled emails of these dashboards keep their header, captions, values and workbook, since nothing sets a source or these fields.
- **The widget workbook** is the one output that changes for existing widgets, on purpose (D10): it fixes the loss the program names, and `widget-export` "Sheets of a widget workbook" (scenario "Map") already expects tooltip columns under their labels. Only the campo map has tooltip columns (`registros` and `meta` as counts, `calles`, `carreras`): its workbook now heads them with their labels where they have them and writes `registros` and `meta` in their `count` format, as its email already does. Other widgets' workbooks hold the same tables, because their color and scale reach no table. The two Comercio median cards that ignore `estrato` export the same rows.
- **MCP.** `get_dashboard`, `get_dashboard_version` and `get_widget_data` return the same results for widgets and dashboards without these settings. Formatting stays en-US.

## Risks / Trade-offs

- **[A client that does not know the fields drops them when it saves]**: an older UI tab open during the deploy, or an AI client that sends a whole new config. → The UI and the API ship together. The MCP descriptions, `Instructions.md` and `docs/mcp.md` say that `update_widget` replaces the whole config and that `display` must be sent again, as for a color today.
- **[The texts take room from small tiles]** → Clamping on screen, the form's warning (D5), and whole texts on paper, in images, emails and workbooks. The documentation suggests short subtitles: a line of a 6-column tile holds about 90 characters, one of a 3-column tile about 40.
- **[A 3×2 stat tile with a subtitle and a footnote cuts its label]** (D5) → Its figure stays whole through safe centering, the form warns, and hiding the caption or making the tile 3 rows high gives it back; `improve-stat-cards` later shrinks the figure.
- **[Whole texts on paper leave a small chart too little room]** → The chart is cut at its own box, never over the texts (`dashboard-printing` "Charts on paper"), and the form warns when the texts printed whole would take half of the tile. Texts taller than the whole tile are clipped by the tile on paper too, since a tile never grows (D5); the warning is the remedy.
- **[`get_widget_data` of a whole dashboard reaches the 60,000-character budget sooner]** when many widgets have long texts (44 footnotes of 500 characters are about 22,000). → Only the texts a widget has are returned; the budget's note says what was cut and asks for less; `get_dashboard` returns the texts in each config anyway; `improve-mcp-ergonomics` (P3) adds paging and a summary mode.
- **[A cut subtitle hides part of the question on screen]** → The whole text is read by screen readers and shown on pointing, and it is whole everywhere else.
- **[An own caption text drifts from what is plotted]**, such as "% de quienes asistieron" kept after the ratio changes. → The form shows the default line next to the own text, and the documentation says so.
- **[A widget's unit hides a later change of the indicator's unit]** → The documentation says a widget's unit wins over its indicator's, and the form names the indicator's unit as the field's hint.
- **[A hidden caption leaves sighted readers without the line]** → The accessible name, tooltips and tables keep it, and the documentation suggests a subtitle.
- **[An export refused for a saved config that fails today's rules]** → `savedConfig` sends only what the kind takes, and the endpoint applies the rules of a `PATCH` of the same widget.
- **[The source and the texts leave the deployment in images and emails]** → They are written by members, as titles are, and `docs/operations.md` names them.

## Migration Plan

1. **Deploy.** `0022_add_widget_context.sql` adds a nullable column and its check, and rewrites no row. Widgets have none of the new fields and dashboards have no source, so everything reads, draws and records versions as before.
2. **Rollback** (program section 2, rule 7):
   - An older API ignores the new display fields when it reads a config, never refuses them, and drops them on the next save of that widget.
   - It does not map `dashboards.source`, so it neither reads nor writes it, and the column can stay.
   - Its version content has no `source` key. The first change saved to a dashboard that has a source records a version without it, and a restore's step 2 there records an "unrecorded" version, since its content differs from the newest one. Its restore of a version that holds a source ignores the key and leaves `dashboards.source` as it is.
   - An older UI ignores `source` in the answers and shows the raw key for a `sourceChanged` entry of the history.
   - No new value of an existing enumeration is stored in a widget's config.
   - Clearing the sources first is optional: `UPDATE tamiza.dashboards SET source = NULL;`. DbUp never runs scripts backwards, so the column and its check stay, unused.

## Open Questions

- Whether footnotes will take line breaks, or the small Markdown subset of `add-dashboard-sections`, once that change exists. Allowing them later relaxes a rule this change states, with no stored data to migrate.
- Whether a dashboard that mixes surveys needs a source per widget. This change keeps one per dashboard; a widget footnote can name another source meanwhile.

## Verification record

Filled in as the checks of `tasks.md` are run.

- **UI build (task 11.4, 2026-11-04).** `npx ng build` is green, with only the existing NG8113 warning about `Card` in `ProjectReference`.
  - Initial total: 674.64 kB raw (158.52 kB transferred), under the 675 kB warning and the 1 MB error budget of `angular.json`. `main` is 304.28 kB, the shared chunk 291.78 kB and the styles 77.90 kB.
  - The new code (`dashboard-source-dialog`, the description popover, the tile's texts) is in the lazy `project-indicators` chunk, 352.72 kB raw (72.07 kB transferred). The image code is still in the lazy `widget-exporter` chunk, 15.67 kB.
  - No component style passed the 4 kB warning budget (the build prints none).
  - `npm run check:safety` is clean: no markup sinks and no English-only formatting in `src/app`.

- **Suites (tasks 12.1 to 12.3, 2026-11-04).**
  - API: 2179 unit tests pass. Integration: 2075 of 2077 pass. The two failures are `IndicatorSeriesQueryTests.A_ratio_*`, which fail on a clean tree too (decimal comma in this machine's culture).
  - UI (`TZ=UTC`): 3171 of 3173 pass. The two failures are the known polling tests of `project-data.spec` and `project-scripts.spec`. The unhandled `project-performance.ts` error (reading `total`) is the one that already appeared on a clean tree.
  - Analytics, in `tamiza-analytics-test:dev`: 620 pass, so migration 0022 applies in its template database. `git status -- tamiza-analytics` lists no change.
- **Real stack (tasks 12.4 and 12.5, 2026-11-04).** Compose project `tamizaverify` (ports 8189 to 8192, fake sign-in, fake Kobo, Mailpit), with a baseline of the HEAD API and UI built beside it, so every "same as before" below is a comparison and not an argument. Torn down afterwards; the developer's stack was not touched.
  - The ten checks pass: tiles, widths and themes, the description popover, table view, images and workbook, PDF on A4, the form, the Source action, MCP, the scheduled email in both languages, and compatibility.
  - Compatibility: the plain dashboard's JSON, versions, SVG images, A4 PDFs, email body and workbook are identical to the baseline. The only difference is the map tooltip header "Total propio" instead of "denominator" in the workbook, which is the specified change.
  - Measured heights (px, 1,280 px page): 6x4 tile 336, 4x3 tile 248, 3x2 tile 160 in every case. Header 53 without a subtitle, 87 with a two-line one, 71 with a one-line one. Plot area 233 without texts, 199 with a subtitle and a one-line footnote, 159 with a two-line footnote. A 4x3 column with a 500-character footnote clamped to two lines keeps a plot of 105 (the chart is cut at its box).
  - A 6-column tile holds about 108 characters of 12 px text per line, a 3-column tile about 40. D5 assumed 90 per line: the scenario "Long subtitle on a column chart" (180 characters) clamps at 390 px and in narrower tiles, and fits two lines at 1,280 px.
  - Findings, none blocking and none fixed here:
    - On paper, a 4x3 column with a 500-character footnote leaves the chart 0 px (the footnote prints whole and nothing paints over it), and a 3x2 stat with a 500-character footnote loses its figure (the footnote is clipped at the tile after about seven lines). Both follow the written scenarios; a minimum plot height on paper, or a form warning for these sizes, would be a follow-up.
    - On screen, a narrow 3x2 stat with a long unit ("52% de los hogares") wraps its figure onto two lines; with a footnote or subtitle the second line is cut. D5 assumed a one-line figure.
    - `data-testid="dashboard-source"` is on both the menu action and the source line (as design D7 states), so a generic query finds the line first.
    - The Source field has no `maxlength`: the 300-character message comes from the server under the field.
  - Not checked: real screen readers, native hover tooltips, Chrome's print dialog flow, tablet widths, the email in a real mail client.
