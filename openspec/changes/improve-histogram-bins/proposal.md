# Proposal

## Why

The study shows how money is spread (what a household paid for its last ticket, what it spends each month), but none of its 190 widgets is a histogram. Authors build the bins in the script and draw them as a column chart:
- `teatro_hoy` builds 21 bins of $5.000 up to $100.000, plus "Más de $100.000", for E7. Teatro e636 draws them sorted by `orden`, with n = 37: 11 of the 21 bins are at 0 %, and the open last bin holds 21.62 %.
- Escuelas f139 uses $20.000 bins with an open "Más de $200.000", and drops the "$0 · No paga" bin with the filter `orden in ['02'..'12']`.
- Comercio a1a2 and 4a98 use fixed edges, and `parqueadero_respuestas` ends with "5. Más de $5.000".

The values are already there, one row per household (`teatro_precio_hogar`, `comercio_gastos_hogar`), and they feed box plots.

Today's histogram cannot replace those charts:
- It only splits the range between the smallest and the largest value into a number of equal-width bins (1 to 100, 10 by default; `IndicatorModels.cs:185`, `IndicatorValidation.cs:137-140`). There is no bin width, no first edge and no open last bin, so edges fall on odd figures and a few large payments stretch every bin.
- A sentinel code such as 98 ("no sabe", found in `e7`, `g2` and `g7` of the clean data) falls into the first bin. Only a range filter can drop it, and a widget has one filter per column.
- The engine sends one value per row, cut at the 5,000-row limit with no order (`IndicatorQueryBuilder.cs:284-292`), so past 5,000 rows the histogram counts an arbitrary subset. The bins are then computed twice, in the browser (`chart-data.ts:36-53`) and in the email builder (`ReportDataBuilder.cs:401-425`), by twins that must agree.
- It only counts, while the study's charts show each bin's share of the base (21.62 % of n = 37).

## What Changes

- **Bin width and start.**
  - `config.binWidth` is a number above 0, in the measure's units. `config.binStart` is the lower edge of the first bin; by default it is the largest multiple of the width at or below the smallest value. E7 takes `binWidth: 5000` and `binStart: 0`.
  - A widget has either `buckets` (today's equal-width bins) or `binWidth`, not both.
  - A histogram draws at most 100 bins, the same limit `buckets` has.
- **Open last bin** (`config.openLastBin`, a number). Values at or above it fall into one last bin, labelled "100,000 or more" (in Spanish "100.000 o más"). Without it, bins run to the largest value.
- **Values left out:**
  - `config.excludeValues` takes up to 10 numbers, for example `[98]`. Rows whose value is one of them are not binned, and the declared `missing` values of the measure column (column metadata) are left out the same way;
  - values below `binStart` are left out too;
  - a note says how many values were left out and why, for example "6 values left out: 98", on screen and in every output.
- **Shares.** A histogram takes `normalize: "total"` from `add-normalization-and-totals`. Each bar is then its share of the values binned ("21.6 %"), with the count in its tooltip and in its table.
- **Binned in the database:**
  - the query that sends every row becomes one statement that returns one row per bin (its lower and upper edge, its count, and whether it is open), counted over every matching row;
  - a histogram saved with `buckets` keeps its edges: the statement uses the formula the twins use today (equal width from the smallest to the largest value, the last bin closed);
  - with a group column, it bins the group values, as today;
  - the response gains `bins`. The 5,000-row limit no longer cuts a histogram, which draws at most 100 bins;
  - `histogram` in `chart-data.ts` and `ReportDataBuilder.Histogram` stop binning: they read the bins.
- **Everywhere a widget goes:**
  - the widget form gains bin width, start, open last bin and the values to leave out, with the live preview;
  - bin labels follow the indicator's format, and the open bin has its own label;
  - the notes appear on screen, in printing, in widget images, in the scheduled email (body and workbook) and in the widget workbook;
  - in MCP, `create_widget` and `update_widget` take the fields, and `query_indicator` and `get_widget_data` return the bins;
  - the docs, with how to publish one row per household for a histogram.

Not in this change:
- Bins of different widths other than the open last bin, such as Comercio's "Hasta $400.000 … Más de $2.000.000", and an open first bin.
- Leaving values out of box plots and stat tiles (a decision, see Next step).
- A median or mean line on a histogram (a decision, see Next step).
- A histogram with a series, density curves, and weighted counts (`add-weighting`).
- Replacing the bins scripts build today. Teatro e636, Escuelas f139 and Comercio a1a2 and 4a98 stay column charts until an author changes them.

Depends on:
- `add-indicator-column-metadata`: the column metadata map that holds `missing`.
- `add-small-sample-warnings`: the `missing` property, and the histogram tooltip that gives the count.
- `add-normalization-and-totals`: `normalize: "total"`.

## Capabilities

### New Capabilities

None. Widget kinds and queries belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Histogram": bins of a chosen width and start, an open last bin, values left out, shares, and every matching row counted;
  - "Widget query over one indicator": the row limit counts a histogram's bins, not its rows. This edit is minimal, because `add-small-sample-warnings` owns the requirement, and the design says why;
  - new requirements: histogram bins counted in the database, the open last bin of a histogram, and values left out of a histogram.
- `dashboard-reports`: a new requirement for histogram bins in a scheduled email (the open bin's label, the note on values left out, shares). "Widget tables in a scheduled email" stays true: a histogram's table still lists each range with the number of values in it.
- `mcp-server`: a new requirement, "Histogram bins through MCP": bins in place of every row's value, and the new fields.
- `pipeline`: a new requirement for missing values of a number column, only if the user chooses declared `missing` values for measures (see Next step).

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`: `WidgetConfigInput`, `Normalize()` and `WidgetConfig` gain `binWidth`, `binStart`, `openLastBin` and `excludeValues`, each `[JsonIgnore(Condition = WhenWritingNull)]`, with their limits; `IndicatorQueryResponse` gains `bins`.
  - `Indicators/IndicatorValidation.cs`: the bucket check at lines 137-140 and the new rules.
  - `Indicators/IndicatorQueryBuilder.cs`: the binning statement in place of the per-row query (lines 230 and 284-292).
  - `Indicators/IndicatorDashboard.cs`: a bins reader in place of the per-row read (line 139).
  - `Indicators/WidgetQuery.cs`: line 21 sends the new fields.
  - `Reports/ReportDataBuilder.cs`: `HistogramTable` (line 310) reads the bins, and `Histogram` (line 401) goes.
  - `Mcp/McpDashboardTools.cs` (the help constants, the widget data payload) and `Mcp/Instructions.md`.
  - OpenAPI, and the tests: `IndicatorQueryBuilderTests` (line 186), `IndicatorQueryTests` (lines 82 and 277), `ReportDataBuilderTests` (line 14), `WidgetQueryTests`, `McpToolCatalogTests`, `IndicatorDocsTests`, and the production shapes fixture of `add-indicator-column-metadata`, extended with a histogram by width and by number of bins.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the types and the response's `bins`.
  - `indicators/widget-view.ts`: `queryFor` (line 110) and `savedConfig`.
  - `indicators/widget-dialog.ts`: the form.
  - `chart-data.ts` (lines 36-53) and `axis-chart.ts` (bin labels, lines 256-265).
  - `en.json` and `es.json`, and the specs `chart-data.spec.ts` and `query-for.spec.ts`.
- **Database:** none. The fields live in `dashboard_widgets.config`, and declared `missing` values in `script_versions.columns` (migration 0021 of `add-indicator-column-metadata`).
- **Analytics:** none.
- **MCP:** the tool descriptions, `Mcp/Instructions.md` and `docs/mcp.md`.
- **Docs:**
  - `docs/indicators-dashboard.md`: the histogram row of the kinds table (line 140), the row limit's exception for histograms (line 115), and a section on bins;
  - `docs/mcp.md`, and `docs/scripts.md` if number columns can declare `missing` values.

**Compatibility.** Production has no histogram widget and no saved `buckets`, so nothing it shows changes, and the 50-widget limit, scheduled emails and dashboard filters are untouched. In other projects, a histogram saved with `buckets` keeps its edges and counts. Its numbers change only when more than 5,000 rows match: the arbitrary subset it counts today becomes every row, and the row-limit note goes away. The new fields are written only when set, so untouched widgets keep byte-identical stored JSON and make no spurious version, and versions, duplicates and moved widgets carry them. For MCP clients, `get_widget_data` and `query_indicator` return a histogram's `bins` instead of up to 5,000 `rows`. That changes the shape for clients that read histograms (none in production), and the tool descriptions say so. After a rollback, an older API ignores the new fields and drops them on the next save. The widget falls back to 10 equal-width bins over at most 5,000 rows, and nothing is refused.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next in line, against the main specs as they are then (after the P1 changes and `add-normalization-and-totals` are archived). Decisions to take with the user at that time:
- Which side of a bin is closed: the lower side, as today, or the upper side, as the study's labels read ("Hasta $5.000", "$5.001 a $10.000").
- What happens when a width would need more than 100 bins: refuse with a message naming the limit (proposed), or fold the rest into the open last bin.
- Where values to leave out come from: the widget's `excludeValues` and, in addition, `missing` values declared on number columns (proposed, which extends the property of `add-small-sample-warnings`), or the widget only.
- Whether box plots take `excludeValues` in this change, since the same SQL condition fits them.
- Whether a histogram takes the median and mean lines of `add-chart-annotations` and the references of `add-reference-lines-from-indicators` on its value range, for Teatro e636 and its median of 40,000.
- How `binStart` is chosen by default, and the format of bin labels (a range, or "up to" style).
