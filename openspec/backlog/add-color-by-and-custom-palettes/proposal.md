# Proposal

## Why

The Duitama study's specification asked for colors that carry meaning: reasons for not going to the theater colored by type of barrier, one place in coral against the rest in slate, "No sabe" in grey. `add-category-highlight` covers one value against the rest and the grey. Four needs remain, and each one forced a workaround in production:

- **Colors from another column.** `teatro_barreras` already has a `barrera_tipo` column, with one type per reason, but a bar chart cannot use it. Kept colors follow the group column (`categoria`), and a column keeps each color once, so three types cannot color twenty reasons. The author published a count indicator (`teatro_conteo`) and drew Teatro 23c6 (E8), Teatro 7677 (I5) and Segmentos be41 as stacked bars with `seriesBy: barrera_tipo`, so that each bar is one colored segment. The % charts beside them (3138, 84aa, e6fe) stay in one color. Escuelas 50f0 wrote the type into the label instead ("Falta de tiempo · acceso").
- **The client's own colors.** A dashboard picks one of four fixed palettes (7 of the 9 Duitama dashboards use *Earth*), and nobody can type a color code (`dashboard-colors` "Palettes").
- **More than 8 colored values in a column.** Kept colors are dashboard-wide by column name, and a column keeps each of the 8 colors once. Cruces uses all 8 on `respuesta`, for answers of different questions ('1. Sí' 5, '3. No' 1, '1. A pie o en bicicleta' 8, '5. Bus o buseta' 7…). A ninth answer in that column cannot get a color.
- **Stat tiles and targets.** None of the 67 stat tiles can take a color. "Centro cultural o casa de la cultura" (24.22 %) was moved to stat card a6df, which cannot take the coral of the bar it stands for. A tile or a bar cannot change color when a figure crosses a target.

## What Changes

- **Color by another column (`colorBy`).** A bar or column chart with no series can name a second text column of its indicator. It is a top-level config field, because the query must return it.
  - Each bar keeps its place and size and takes the color of its value in that column. A legend under the caption lists those values.
  - The colors are the colors kept by value for that column (Teatro already keeps `barrera_tipo` Acceso 3, Interés 2, Otra 6), then the palette's free colors in legend order. At most 8 values take colors; the others share the neutral color under one "Other" entry.
  - A bar whose rows hold more than one value of the column is drawn in the neutral color, and a note says how many bars mix values.
  - The tooltip names the bar's value of the column. "Show as table", the scheduled email (body and workbook), the widget workbook and MCP widget data gain a column with it.
  - Sorting does not change; sorting by the column `barrera_tipo` already groups the bars by type.
  - It is refused on other kinds, with a series, on the group column, on a numeric column, and together with `display.highlight` or thresholds: a widget's bars follow one coloring rule.
  - Example: Teatro 3138 (`teatro_barreras` by `categoria`, filtered to E8) with `colorBy: "barrera_tipo"` replaces 23c6 and its count indicator.
- **Custom palettes from color codes.**
  - A project holds up to 4 palettes of its own, made by an `analyst` or `admin` in the dashboard's **Colors** form: a name (1–40 characters), 8 colors as `#rrggbb` for the light theme, their dark-theme values (proposed by Tamiza and editable), and one of the 7 fixed sequential scales as default.
  - Each one passes the checks of the fixed palettes, in both themes: adjacent colors apart for protan, deutan and full-color readers, the lightness band, the chroma floor, and the eighth color apart from the first. The form runs them as colors are typed; the API runs them on save and refuses a failing palette with an error naming the two colors and the check.
  - Every dashboard of the project offers them after the four fixed palettes. "Other" keeps the same neutral color.
  - Editing a custom palette recolors every dashboard that uses it, and the form names them. A palette in use cannot be removed.
- **A color may repeat within a column.** A dashboard may keep one color for several values of a column, so Cruces can color more than 8 answers of `respuesta`. Two values drawn in one widget still never share a color: the later one in the widget's order takes the next free color, and the **Colors** form lists the widgets where that happens. The limit of 64 kept colors per dashboard stays.
- **Stat tile color.** A stat tile takes `display.color`: a palette color, or the neutral color in the encoding `add-category-highlight` chooses. It marks the tile with an accent strip at its edge; the value keeps the text color, so it reads in both themes. The 7 "Sin dato" cards can be neutral.
- **Threshold colors (`display.thresholds`).** On a stat tile, and on a bar or column chart with no series:
  - 1 to 3 ascending break values in the indicator's units (a percent's 0.5 is 50 %) make 2 to 4 bands; a value equal to a break falls in the upper band;
  - each band has a palette color or the neutral color, and an optional label of 1–40 characters;
  - the tile shows its band's label under the value; bars list the bands in a key and name the band in the tooltip; tables, emails and workbooks gain a band column. Color is never the only cue.
- **Everywhere the colors go.** Widget images and printing use the light colors of a custom palette as they use the fixed ones. Emails and workbooks stay tables without colors, with the color-by and band columns. MCP takes the new fields and chooses custom palettes.

Not in this change:
- Color by a column on lines, pies, box plots, histograms or charts with a series; color by a numeric column.
- Palettes of more than 8 colors, custom sequential or diverging scales, and custom Likert colors.
- Creating or editing custom palettes through MCP (MCP reads them and chooses one).
- Colors kept by value shared by all the dashboards of a project ('1. Sí' is color 8 in Teatro and 5 in Cruces), and a per-widget opt-out of kept colors.
- Thresholds on maps, heat maps and lines, which use scales or series.
- Colors in the scheduled email body, and a dark theme switch.

## Depends on

- `add-indicator-column-metadata`: the color-by column's name in its legend and table header.
- `add-category-highlight`: the neutral color, used here by stat tiles and bands, and `display.highlight`, which this change keeps apart from `colorBy` and thresholds.
- `add-declared-category-order`: the color-by legend follows the column's declared order, else natural order.
- `add-small-sample-warnings`: rich tooltips on bars with no series, where the color-by value and the band are added.
- `improve-stat-cards`: the stat tile layout that the accent strip and the band label fit into.

## Capabilities

### New Capabilities

None. Colors belong to `dashboard-colors`.

### Modified Capabilities

- `dashboard-colors`:
  - "Palettes": a color code is typed only into a project's custom palette; the scenario "No color codes" stays true, since a dashboard's palette is never a code;
  - "Palette checks": the checks also run when a custom palette is saved, in the form and in the API;
  - "Dashboard palette": a dashboard can use one of its project's custom palettes;
  - "Widget color": a stat tile takes a color;
  - "Colors kept by value": a color may be kept for several values of one column;
  - "Where colors kept by value apply": the bars of a chart colored by another column take that column's kept colors;
  - "Colors of values with no color kept": color-by values take the free colors in legend order and the neutral past the eighth, and a repeated color yields to the next free one within a widget;
  - "Colors of a dashboard": the form lists and edits custom palettes and shows where a repeated color clashes;
  - new requirements: "Custom palettes", "Custom palette checks", "Colors by another column", "Stat tile color", "Threshold colors".
- `dashboard-reports`: new requirement "Color-by and band columns in a scheduled email".
- `dashboard-versions`: "Changes that are not dashboard changes": editing a project's custom palette makes no dashboard version, since a version holds the palette by its id.
- `mcp-server`: new requirement "Color settings through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/Palettes.cs`: custom palette ids and limits, and a C# twin of the palette checks, which today exist only in the UI test `chart-tokens.spec.ts`.
  - `Indicators/DashboardValidation.cs`: the rule that a column uses a color once (lines 211–216) becomes a per-widget clash report; custom palettes are validated.
  - `Indicators/IndicatorModels.cs`: `WidgetConfigInput`, `Normalize` and `WidgetConfig` gain `colorBy`; `WidgetDisplay` gains `thresholds`; `IndicatorRow` gains the color-by value; `DashboardView` carries the custom palettes.
  - `Indicators/IndicatorValidation.cs`: `CheckDisplay` (lines 164–210), which refuses a stat tile's color today.
  - `Indicators/IndicatorQueryBuilder.cs`, `IndicatorDashboard.cs` and `WidgetQuery.cs`: the color-by output per group, its reader and the query twin.
  - `Indicators/DashboardEndpoints.cs`: custom palette endpoints; `PUT /dashboards/{id}/colors` takes a custom palette's id.
  - `Reports/ReportDataBuilder.cs`: the color-by and band columns.
  - `Mcp/McpDashboardTools.cs` (`ColorsHelp`, `DisplayHelp`, `ChartTypeChangeHelp`), `Mcp/Instructions.md`, `Mcp/McpGuide.cs`.
  - OpenAPI, unit and integration tests, the production shapes fixture and the pinned-text tests.
- **ui** (`tamiza-ui`):
  - `indicators/chart-colors.ts`: colors by column, bands and custom palettes (`colorsOf` reads an unknown palette as *Tamiza* today, lines 28–31).
  - `indicators/axis-chart.ts` (`markColor`, line 300, and the color-by legend), `stat-widget.ts`, `series-legend.ts`.
  - `indicators/dashboard-colors-dialog.ts`: the custom palette editor with live checks, and the clash list.
  - `indicators/widget-dialog.ts`, `widget-view.ts` (`queryFor`, `savedConfig`, `displayFor`), `query-for.spec.ts`.
  - `styles.css`, plus a document-level style that defines a custom palette's tokens while its dashboard is open, so overlay tooltips, images and printing resolve them like the fixed ones; `chart-tokens.spec.ts` shares the checks with the runtime code.
  - `widget-export/image-layout.ts` (the stat block, lines 145–149) and `export-parts.ts`.
  - `core/projects/indicators.service.ts` (`PaletteId`, the new fields), `en.json` and `es.json`.
- **Database:** a DbUp script with the next free number (after 0021–0024 and the script of `add-dashboard-sections`) stores a project's custom palettes and widens `ck_dashboards_palette` (0019) so a dashboard can name one. It rewrites no row.
- **Analytics:** none.
- **Docs:** `docs/indicators-dashboard.md` ("Colors" and "Limits"), `docs/mcp.md`, `docs/tamiza-brief.md`.

**Compatibility.** Every new field is optional. No production widget has `colorBy`, thresholds or a stat tile color, and every dashboard uses a fixed palette (*Earth* 7, *Tamiza* 2). So the 190 widgets, their versions, images and scheduled emails look as today, and stored configs stay byte-identical. The 47 kept colors already follow the stricter rule; the box plots colored by `vehiculo` (Centro a137, parqueadero 8492) and the Escuelas pie b296 keep their colors. The repeat rule only accepts entries that were refused before. An email gains a column only for a widget that opts in. MCP clients get the same results and see the new fields once they list the tools again. On rollback, an older API drops `colorBy` and `thresholds` on the next save, and refuses to save again a stat tile with a color or a column that repeats a color, though both still draw. An older UI draws a dashboard with a custom palette in *Tamiza*.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, on the main specs as they are then. Decisions to take with the user at that time:
- A bar whose rows hold several values of the color-by column: the neutral color with a note (recommended), its first value, or a refusal.
- Where custom palettes live and who edits them: the project, by analysts and admins (recommended), each dashboard, or admins in Settings; and whether a dashboard version keeps a copy of its custom palette's colors.
- Dark-theme values of a custom palette: derived and editable (recommended), or typed.
- More than 8 colored values per column: a color repeated within a column (recommended), or palettes of more than 8 colors.
- Stat tile color: an accent strip (recommended), or the value's text in the color when it reads at 3:1.
- Threshold colors: on stat tiles and on bars and columns with no series (recommended), or on stat tiles only.
