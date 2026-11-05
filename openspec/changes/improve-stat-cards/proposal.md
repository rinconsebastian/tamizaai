# Proposal

## Why

Stat tiles are the most used widget: 67 of the 190 widgets of the Duitama market study are stat tiles ("Cifra destacada" in the Spanish interface), and Teatro alone has 16. The study showed four problems with them.

- **The figure is too large.** The value is drawn at 36 px in a default tile of 3 by 2 cells, and a long number can be split across two lines. The user asked for a smaller font.
- **A tile can only add up.** A tile shows the measure summed, or the ratio of sums. The study needed medians: Escuelas card cef7 ("Mediana de lo que pagan hoy al mes (sin filtros)") reads a one-row indicator, so it shows 60,000 pesos under any filter; Comercio cards aea1 and 5901 read percentiles computed in the script and ignore the `estrato` filter, so aea1 stays at 800,000 pesos with `Estrato 1`. The tables with one row per household that would answer it already exist (`teatro_precio_hogar`, `comercio_gastos_hogar`), and a box plot over them follows the filters (median 40,000 pesos, 35,000 with `COMUNA 1`).
- **A tile says nothing about its base or about the filters.** Authors typed "n = 37" into titles, and nothing tells a reader how a filtered figure compares with the whole study. The "Stat tile" row of the widget types table in `docs/indicators-dashboard.md` even claims that a tile says over how many rows it was summed, which it does not.
- **A tile over several answers of one question is silently wrong.** In the study's tables of shares, each household is counted in the base of every answer of its question. A filter that keeps several answers therefore counts the base once per answer. `cruces_intencion` with `variable` "Componentes con sí o tal vez" and `categoria` in `1`, `2`, `3`, `4` shows 23.79 %, while the true share is 95.17 %. The authors avoided it by publishing extra rows ("5. Cualquier componente", "Fuera de Duitama (E5)", "Clave") and by writing "a card must filter one category only" in indicator descriptions.

## What Changes

- **A smaller figure that fits its tile.** The value takes its size from the tile: at most 30 px (below today's 36 px), at least 18 px. It breaks only between words, never inside a number. The widget image, the printed dashboard and the scheduled email (20 px instead of 24 px) follow. A 3 by 2 tile with a one-line subtitle and a one-line footnote, which keeps its figure whole and cuts its label since `add-widget-context`, can show its label again. This is the one default that changes for every existing tile.
- **A statistic for stat tiles** (`config.statistic`): the median, the first or third quartile, or the mean of the matching rows, each row one value (its measure, or its own numerator over its own denominator, as a box plot takes it). It covers every matching row, uses the quartile method pandas uses, and follows the widget filters and the dashboard filters. The text under the value says which statistic it is ("Median of …"). Without it, a tile adds up as today.
- **Secondary lines** (`config.display.secondary`), each optional, under the value:
  - `n`: the base of the tile, as `add-small-sample-warnings` counts it ("n = 476 Hogares"). It is the same line that change can show under any widget, drawn once inside the tile. A tile with a statistic counts its values instead, as a box plot's box does ("n = 37"), or sums the base column over them when the indicator declares one;
  - `unfiltered`: the value without the dashboard filters, shown only while one narrows the tile ("Without filters: 46.85% de los hogares"). It needs one more figure from the database, so checking it reloads the tile. Like the `overall` value of `add-chart-annotations`, it is asked for by a flag of the query request that no widget stores.
- **Answers of one question are never added up as if they were separate bases.** When an indicator declares the columns that name a question and its answers (the `categories` setting of `add-small-sample-warnings`, on `variable`, `orden` and `categoria`, or on the answer column alone):
  - single choice: a ratio over several answers counts the base once, by the rule that change counts its n with: the sum of the numerators over the largest of the answers' denominator sums, for each value it plots (each comuna of a chart by comuna, for example). The card above shows 95.17 %;
  - multiple choice: a widget whose figure would add up several answers shows a message instead, explaining the double counting and what to do (keep one answer, group by the column, or publish an "any of" row); totals and "Other" series that would add up several answers have no value.

  Widgets that group or split by the column, or keep one of its answers, show the numbers they show today. This applies to every widget that plots a ratio, not only to stat tiles.
- **A hint in the widget form** when a ratio widget keeps several values of a text column that is neither its group nor its series, and the indicator declares nothing about it.
- **Everywhere a stat tile goes:** the widget form, the preview, "Show as table" (none, as today), the widget image, printing, the scheduled email (body, text and workbook), the widget workbook, the MCP tools, `Mcp/Instructions.md`, `docs/mcp.md`, `docs/scripts.md` and `docs/indicators-dashboard.md` (whose "Stat tile" row of the widget types table is fixed). The lines go between what the tile plots and its notes and footnote, and a tile that hides its caption shows them under its figure all the same.
- **Compatibility.** Every new field is optional and left out of the stored settings while unset. Existing widgets, dashboard versions, duplicates, moved widgets, scheduled emails and MCP clients read and save exactly what they do today, and show the same numbers, except the smaller figure. The smaller figure is one of the program's intended default changes, with the richer tooltips of `add-small-sample-warnings`. Nothing changes for an indicator until its author declares its answer columns, and since both changes ship in the same release, no indicator holds a declaration before this correction applies. No stored setting is rewritten.

Not in this change:
- Colors for stat tiles, colors by threshold, sparklines, or a comparison with another indicator or a previous period.
- Statistics on other kinds (a median bar per group), other percentiles than 25, 50 and 75, weighted statistics (`add-weighting`), and the number of decimals (`add-locale-formatting`).
- Warnings returned when a widget is saved, and the other authoring guards (`add-authoring-guards`). The hint here is shown only in the widget form.
- Fixing a ratio over several answers without a declaration: the engine cannot know that a column holds the answers of one question.
- Measures added up over several answers: a count of mentions is a figure in its own right.
- A database migration: no column or stored setting changes.

## Depends on

- `add-small-sample-warnings`: the base n of the `n` line, the base n line under a widget, the answer columns (`categories`) of indicator column metadata with the reader that gives them to the query, and its rule for counting a base once across answers. Through it, `add-indicator-column-metadata` (the column names in the n line and the captions). Both changes ship in the same release, decided with the user on 2026-11-04: `add-small-sample-warnings` is implemented first, then this change, and neither is deployed alone.
- `add-indicator-column-metadata` and `add-widget-context`, implemented and archived on 2026-11-04. The stored `display` ends with `ShowDescription`, after which this change appends `secondary`. The widget's own unit and caption (its own text, or hidden) are in "Stat tile", and the subtitle and footnote sit around a tile. The widget workbook's request already sends the widget's saved config, so the lines and the flag reach its workbook with no change to the request.
- `add-chart-annotations`, earlier in the program: its reference line's overall value is a stat tile's statement, so it follows the same rule for declared answers, and its `overall` flag of `IndicatorQueryRequest` is the pattern the `unfiltered` line follows (decided with the user on 2026-11-04): a flag of the request, derived from the saved widget by the code that sends it, that `WidgetConfigInput`, `WidgetConfig` and `queryFor` never hold. A stat tile has no reference line, so its request never carries both flags.

## Capabilities

### New Capabilities

None. Stat tiles and widget queries belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Stat tile": the tile shows a sum, a ratio of sums or a statistic, with the widget's own unit as before, and what it shows under its value unless the widget hides its caption;
  - "Ratio of sums": a stat tile with a statistic divides per row, and a ratio over several declared answers follows the new rules wherever a ratio of sums is taken;
  - "Widget query over one indicator": a stat tile with a statistic does not sum its rows;
  - new requirements: "Stat tile value size", "Stat tile statistic", "Stat tile statistic n", "Stat tile statistic settings", "Stat tile secondary lines", "Stat tile secondary line settings", "Stat tiles saved before statistics and secondary lines", "Ratio over declared categories never summed across their values", "Ratio over declared multiple-choice categories refused", "Hint for a ratio over several values of one column".
- `widget-export`: "Stat tile in a widget image" and "Stat tile lines in a widget workbook".
- `dashboard-reports`: "Stat tile in a scheduled email" and "Ratio over declared categories in a scheduled email".
- `dashboard-printing`: "Stat tile on paper".
- `mcp-server`: "Stat tiles through MCP" and "Ratio over declared categories through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`: `Statistic` on `WidgetConfigInput` and `WidgetConfig`, `WidgetDisplay.Secondary` (after `ShowDescription`), `StatStatistics` and `StatSecondaryLines`, the flag `IndicatorQueryRequest.Unfiltered` (a request flag, never part of a widget's config), and the response's `unfiltered` and `combinedCategories`.
  - `Indicators/IndicatorValidation.cs`: the statistic and the secondary lines.
  - `Indicators/IndicatorQueryBuilder.cs`: the statistic statement with the tile's n, the statement without dashboard filters, and the ratio over several answers of a declared `single` column, grouping the answers with the helper of `add-small-sample-warnings`' `BaseQueryBuilder.cs`; `Indicators/SeriesQueryBuilder.cs` for the same ratio and for the totals and "Other" series of a `multiple` column.
  - `Indicators/IndicatorDashboard.cs` and `Indicators/IndicatorEndpoints.cs`: the kinds of the answer columns, the `unfiltered` argument of `QueryAsync` and the second statement it runs, the check for a `multiple` column and the response.
  - `Indicators/WidgetQuery.cs`: the statistic in `For`, and `Unfiltered(chartType, config)`, which decides the request flag, twins of the UI's `queryFor` and `unfilteredFor`.
  - `Reports/`: `ReportDataBuilder` (`ReadWidgetAsync` passes the flag), `ReportHtml` (the 20 px figure and the lines), `ReportText`, `ReportWorkbook`, `WidgetWorkbook`.
  - `Mcp/McpDashboardTools.cs` (`WidgetDataAsync` passes the flag), `Mcp/McpScriptTools.cs` (what `categories` does) and `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, and the production-shapes fixture.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the new fields, the `unfiltered` argument of `query` and the response fields.
  - `indicators/stat-widget.ts`: the sized figure, the statistic's name and the secondary lines.
  - `indicators/widget-view.ts`: `queryFor` (the statistic), `unfilteredFor` and the query key, `displayFor`, the tile's n line, and the message for a refused widget.
  - `indicators/widget-dialog.ts`: the statistic, the secondary lines and the hint.
  - `indicator-metadata-fields.ts` and `add-indicator-dialog.ts`: the help of the answers setting.
  - `indicators/export-parts.ts` and `widget-export/image-layout.ts`.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** none. No migration and no stored setting is rewritten.
- **Analytics:** none.
- **Docs:** `docs/indicators-dashboard.md` (stat tiles and the "Stat tile" row of its widget types table, the sentence on a 3 by 2 tile with texts, images, workbooks and the email, and ratios over answers of one question), `docs/scripts.md` (the answers setting), `docs/mcp.md`.
