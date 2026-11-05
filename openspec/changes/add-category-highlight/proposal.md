# Proposal

## Why

The Duitama study asked for colors by meaning: one category set apart from the rest, and answers such as "No sabe" or "Sin dato" in grey in every chart. Tamiza can already keep a color for a value of a column on a dashboard, and that color already reaches the bars of a bar or column chart with no series. Yet no bar or column chart with no series among the study's 190 widgets uses it. In "2. Teatro", the bar chart "2.4. En qué lugares" (places, largest first, in color 2) draws every place in the same indigo, so "Centro cultural o casa de la cultura" (24.22 %) was moved to a stat card of its own. "Fuera de Duitama" (10.31 %) went the same way, next to the column chart "2.5. En qué municipio".

Three rules made colors kept by value the wrong tool:
- **They apply to the whole dashboard, by column name.** The column `categoria` holds the answers of three Teatro indicators (places, intention and barriers), so a color kept for one of its values shows in every widget of the dashboard that has that value.
- **A column keeps each color once.** Several values cannot share a color, and a column keeps at most 8. The refusal ("respuesta already uses color 5.") does not say so. In "8. Cruces", the column `respuesta` already uses all eight.
- **There is no grey.** Only "Other" is grey, so "4. No sabe" is plum (color 7) in Teatro and brown (color 6) in Cruces.

Claude's tool descriptions and instructions never say that kept colors reach the bars of a chart with no series, so Claude did not use them either.

## What Changes

- **Highlighted values** (`display.highlight`, up to 20 values). A widget lists the values it sets apart: values of its group column, or of its series column when it has one.
  - A highlighted value keeps the color it has today: the color kept for it, or else the widget's own color (a chart with no series) or its series color.
  - Every other value, "(none)" and "Other" are drawn in the grey of "Other", the neutral color.
  - Bar and column charts (with or without a series), line charts with a series, pie charts and box plots with a group column take it.
  - It belongs to the widget, so it never changes another widget, and the dashboard's colors kept by value stay as they are. Changing it never queries the indicator again.
  - Teatro's "2.4. En qué lugares" can draw "Centro cultural o casa de la cultura" in terracotta (color 1 of *Earth*) and the other places in grey, with no stat card.
- **Grey as a color kept by value** (the number 0 in place of a palette color). A dashboard can keep the neutral grey for any number of values of a column, for example "4. No sabe" and "3. No o no sabe" of `respuesta`, so they are grey in every widget that colors by that column. Grey is exempt from "each color once per column", does not use up one of the palette's colors, and counts toward the 64 colors a dashboard keeps.
- **Refusals that name the limits**: each of the palette's 8 colors once per column (with grey for more values), 64 colors kept by value per dashboard, 20 highlighted values per widget.
- **Not by color alone.** A highlighted value's name is written in bold on the axis and in the legend, and what a screen reader reads of the chart names the highlighted values.
- **Forms**:
  - the widget form gains **Highlight**: rows of values, each picked from the values the widget plots or typed, up to 20, shown in the preview at once;
  - **Colors** offers the neutral grey, named in words, after the palette's eight colors, and a new entry on a column that already uses all eight starts on grey.
- **Everywhere a widget goes**:
  - printing (with the browser's background graphics off) and the widget's PNG and SVG images draw the highlight, the grey and the bold names as the screen does;
  - "Show as table", the scheduled email (body and workbook) and the widget workbook list every row as before, since they have no colors;
  - Claude can set highlighted values and grey through MCP, and reads them back with the dashboard.
- **Guidance.** Claude's tool descriptions and instructions, and the user documentation, say plainly where colors kept by value apply, including the bars of a bar or column chart with no series and the boxes of a box plot, and when a highlight fits better.
- **Compatibility.** There is no database script and no default changes:
  - no saved widget has a highlight and no dashboard keeps grey, so every widget, dashboard, version, scheduled email and MCP client looks and works as it does today;
  - a widget saved before this change keeps its stored settings byte for byte, so saving it unchanged makes no version;
  - MCP clients see longer tool descriptions; every call that worked before works the same, and only two refusal messages of the Colors form are worded differently.
  - Going back to the previous release is safe: it ignores highlights, and draws grey values as if no color were kept. It refuses to save a dashboard's colors that hold grey, so those entries are changed first (design, Migration Plan).

Not in this change:
- Coloring bars by another column (reasons colored by type of barrier), hex palettes and more than 8 colors per column. They come with P2 `add-color-by-and-custom-palettes`.
- A highlight on a map, a heat map, Likert bars, a cross table, a population pyramid, a histogram, a stat tile, a line with no series or a box plot with no group column. These have no categorical colors per value, or only one line or box.
- A highlight by rule (the largest bar, values above a threshold), and a color other than grey for the values a highlight leaves out. A bar in coral with the rest in the widget's color is already possible with a color kept by value.
- A second grey, or a choice of grey. There is one neutral, the grey of "Other".
- Colors or highlight marks in the scheduled email and the workbooks, which have no colors today.
- A per-widget switch to ignore the dashboard's colors kept by value. A highlight is the per-widget way to set values apart.

## Depends on

Nothing for its behavior. In the program's order it lands after `add-indicator-column-metadata` and `add-widget-context`, both implemented and archived on 2026-11-04, and after `add-chart-annotations` and `add-declared-category-order`:
- it extends the production-shapes test fixture that `add-indicator-column-metadata` created and `add-widget-context` extended (20 shapes), after the shapes the two changes before it add;
- it adds its words to the MCP help texts after theirs, in a constant of its own (`HighlightHelp`) said after `ContextHelp`, as theirs are;
- the widget workbook request already sends the saved config (`add-widget-context`), so a highlight reaches the builder without a page change, and the builder ignores it;
- it matches highlighted values as full text, so `add-declared-category-order`'s hidden number prefixes and declared orders leave the match alone.

P2 `add-color-by-and-custom-palettes` builds on it: the neutral color (number 0) and `display.highlight`.

## Capabilities

### New Capabilities

None. Colors kept by value and a widget's colors belong to `dashboard-colors`.

### Modified Capabilities

- `dashboard-colors`:
  - "Colors kept by value": the neutral color, and a palette color (not the neutral) once per column;
  - "Where colors kept by value apply": a highlight draws the values it leaves out in the neutral, whatever is kept for them;
  - "Colors of values with no color kept": values may share the neutral, and a highlight overrides the widget's color for the values it leaves out;
  - new requirements "Neutral color kept by value", "Color limits named in refusals", "Highlighted values", "Highlighted values checked when saved", "Highlighted values not shown by color alone", "Highlighted values saved with the widget", "Highlighted values in the widget form" and "Neutral color in the Colors form".
- `dashboard-printing`: new requirement "Highlighted values and the neutral color on paper".
- `widget-export`: new requirement "Highlighted values in widget files".
- `dashboard-reports`: new requirement "Highlighted values in a scheduled email".
- `mcp-server`: new requirement "Highlighted values and the neutral color through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/Palettes.cs`: `Palettes.Neutral = 0` and `IsKeptColor`; `ValueColorInput.Color` takes 0 to 8, with its description.
  - `Indicators/DashboardValidation.cs`: `Colors` accepts the neutral for any number of values of a column, and its messages name the limits.
  - `Indicators/IndicatorModels.cs`: `WidgetDisplay.Highlight` (`WhenWritingNull`, appended after the members the record has by then: `Labels`, the five texts and what `add-chart-annotations` adds), `Normalize()`, `WidgetDisplay.MaxHighlight = 20` beside the text limits, `ChartTypes.TakesHighlight`, and the `display` descriptions.
  - `Indicators/IndicatorValidation.cs`: the highlight rules in `CheckDisplay`.
  - `Mcp/McpDashboardTools.cs` (`ColorsHelp`, a new `HighlightHelp`, `ChartTypeChangeHelp`, the `valueColors` argument of `update_dashboard`, the `create_widget` template and the `config` argument descriptions) and `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, the pinned-text tests (`McpToolCatalogTests`, `McpGuideTests`, `IndicatorDocsTests`) and the production-shapes fixture. `AssistantPrompts.Version` does not change: the script guide is untouched.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: `WidgetDisplay.highlight`, `takesHighlight`, `maxHighlightValues`, `neutralPlace`.
  - `indicators/chart-colors.ts`: the neutral kept by value and the highlight, resolved in one place.
  - `indicators/axis-chart.ts`, `series-plot.ts`, `series-legend.ts`, `pie-widget.ts`, `boxplot-chart.ts`: colors, bold labels, summaries, lines painted in order, and the bold legend entries they hand to images.
  - `indicators/export-parts.ts` and `widget-export/image-layout.ts`: bold legend entries in images (`widget-export/svg-clean.ts` already carries a text's `font-weight`).
  - `indicators/widget-view.ts` (`displayFor`), `widget-dialog.ts` (**Highlight**), `dashboard-colors-dialog.ts` (the neutral choice).
  - `en.json` and `es.json` strings, and the specs.
- **Database:** none, so no script takes a number (the last applied is `0022_add_widget_context`). `dashboards.value_colors` is `jsonb` and holds the number 0 as it holds 1 to 8.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: where colors kept by value apply, the neutral, a "Highlighted values" section, a sentence each in "Images" and "Printing a dashboard", the limits and "What it does not do";
  - `docs/mcp.md`: the neutral, `display.highlight` and the chart type sentence;
  - `docs/tamiza-brief.md`: the data model sentences on `value_colors` and `display`, and the program's list.
