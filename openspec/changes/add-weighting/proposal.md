# Proposal

## Why

A survey rarely interviews households in the proportions they have in the population. A stratified or quota sample, or uneven response between areas, makes a city-wide figure mix the areas in the proportions of the sample. In Duitama, 121 of the 476 households in the base live in Comuna 1, 16 in Comuna 7 and 20 in "SIN NOMBRE", and a city-wide figure such as "46.85 % of households attended the theater" mixes the comunas in those proportions. The usual remedy is a weight per household (an expansion factor). Every figure meant for the population is then a weighted sum or a weighted ratio.

Tamiza has no notion of a weight. Every Duitama denominator is an unweighted count of households, and every indicator's description says "Sin ponderar" by hand. The only way to weight today is to multiply inside the script and publish weighted numerators and denominators. Then:
- the indicator can no longer show the sample's own figures, which a methodology annex reports next to the weighted ones, unless every indicator is published twice (the study already keeps 7 count twins for another reason);
- the base n is lost unless the script publishes it apart, and n must stay unweighted for the n footer and the small-base dimming;
- box plots, median tiles and histograms cannot be weighted at all, because a quantile needs each row's weight (`teatro_precio_hogar`: a median of 40,000 pesos over 37 households);
- nothing on the screen, in an image, an email or an MCP result says whether a figure is weighted.

## What Changes

- **A weight declared on the indicator.** The column metadata of `add-indicator-column-metadata` gains `weight: true`, on at most one numeric column (for example `factor`). It is entered in the "Column names" section of the save form and in MCP `save_script` `columns`, and it is versioned with the script: restoring a version brings back its weight, or its absence.
  - The weight is the weight of one unit of the row. A table with one row per unit, such as the box plot tables, takes any weight. A pre-aggregated table, such as the Duitama % tables, is right only when every unit of a row has the same weight, for example a weight per comuna and estrato. The docs and the authoring guide say so.
  - The weight column cannot be the `base` column, and a widget cannot group, split or divide by it.
  - Saving a weight on an indicator that widgets use says how many widgets, dashboards and scheduled emails will show weighted figures.
- **Weighted figures.** With the weight `w`, every widget over the indicator is computed with it:
  - a measure is `sum(value × w)`, and a ratio is `sum(numerator × w) / sum(denominator × w)`. Series cells, totals, "Other", pie slices, heat maps, cross tables, pyramids, Likert shares, map values and the `overall` reference line follow;
  - a box plot's and a stat tile's mean is weighted, and their quartiles and median are weighted quantiles, by one named method computed in SQL. A box's number of values and number of outliers stay counts of rows;
  - a histogram counts each value's weight;
  - a row with no weight, a negative weight or a zero weight adds nothing, and the widget notes how many rows it left out;
  - the base n stays unweighted everywhere: the footer "n = 223 hogares", the small-base dimming and the n in tooltips;
  - the series a widget shows, their order, "Other" and their colors are chosen as today, without the weight, so the switch never moves a legend or a color;
  - map tooltip columns stay plain sums.
- **Weighted / Unweighted switch.** The dashboard's filter bar shows a two-option switch when at least one of its widgets' indicators declares a weight.
  - It is per member, like a filter value: it changes only that member's screen, is never saved and makes no version.
  - "Weighted" is the default. "Unweighted" is kept in the page address (`weighted=0`), so a shared link keeps it; opening another dashboard does not carry it over.
  - While it is on "Weighted", a widget whose indicator declares no weight is drawn as today, with the note "Not weighted: <indicator> has no weight", as "Not filtered by" works.
  - A dashboard with no weighted indicator shows no switch and no note.
- **Every output says which.** The dashboard next to the switch, the widget image footer, the print header, the scheduled email header, the email and widget workbooks' summary, and MCP results say "Weighted by <weight column's name>" or "Unweighted". A scheduled email is computed as a member sees the dashboard on opening it: weighted where a weight is declared.
- **MCP.** `query_indicator` and `get_widget_data` take an optional `weighted` (true by default) and say, per widget, whether it is weighted and by which column. `list_indicators`, `get_script` and `get_indicator_columns` return the declaration. The authoring guide explains it.
- **Test warnings.** The dashboard view of an indicator test warns when the weight column has empty or negative values, or only zeros.

Not in this change:
- Computing weights (post-stratification, raking, weights from the sampling frame). The script computes the weight column.
- Weighted twin columns (`numerator_w` beside `numerator`) for pre-aggregated tables whose rows mix weights, unless the user chooses them at design time.
- Variance, standard errors, confidence intervals, design effects and an effective n.
- A saved per-dashboard or per-email default, and a widget that always ignores the weight, unless the user chooses them at design time.
- Weights on the clean data dashboard and the performance dashboard.

## Depends on

- `add-indicator-column-metadata`: the column metadata map, its save form section and MCP `columns`.
- `add-small-sample-warnings`: the base n (the `base` column, else the denominator) that stays unweighted, and the `categories` declaration, whose rule of counting a denominator once per cell also holds for weighted denominators.
- `improve-stat-cards`: the median, quartile and mean stat tiles weighted here.
- `add-chart-annotations` and `add-reference-lines-from-indicators`: the `overall` line and the lines from another indicator, each weighted by its own indicator's weight.
- `add-normalization-and-totals`: shares and the Total series, computed from weighted sums.
- `improve-histogram-bins`: bins computed in SQL, which weighted counts need.
- `add-widget-context`: the context lines of images, printing, emails and workbooks, where the weighting line goes.

## Capabilities

### New Capabilities

- `indicator-weights`: the weight declaration and its rules, how each kind of widget applies a weight, the unweighted base n, the Weighted / Unweighted switch and its address, the note on widgets without a weight, and what each output says.

### Modified Capabilities

- `pipeline`:
  - the column metadata requirement that `add-indicator-column-metadata` adds gains the `weight` property;
  - "Warnings in the dashboard view": the weight column's warnings.
- `dashboard-indicators`:
  - "Widget query over one indicator": rows are summed with their weights when the indicator declares one;
  - "Box plot statistics": weighted quartiles, median and mean by a named method; the scenario "Same figures as pandas" holds for an unweighted box plot;
  - "Histogram": a weighted histogram counts weights;
  - "Stat tile": a tile's sum, ratio or statistic is weighted.
- `dashboard-versions`: "Changes that are not dashboard changes": the switch makes no version.
- `dashboard-reports`: new requirement "Weighting in a scheduled email".
- `widget-export`: new requirement "Weighting in widget files".
- `dashboard-printing`: new requirement "Weighting on a printed dashboard".
- `mcp-server`: new requirement "Weighting through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorQueryBuilder.cs`: the `aggregate` and `plotted` expressions (from line 234), which every builder reuses, take the weight.
  - `Indicators/SeriesQueryBuilder.cs`: weighted cells and totals; the series domain stays unweighted.
  - `Indicators/BoxQueryBuilder.cs`: weighted quantiles and mean (`percentile_cont`, line 57, takes no weights).
  - `Indicators/IndicatorModels.cs`: `IndicatorQueryRequest` (line 429) gains `weighted`; the response says whether and by which column it is weighted, and how many rows had no weight.
  - `Indicators/IndicatorDashboard.cs` and `WidgetQuery.cs`: the mode reaches emails, workbooks and MCP.
  - The column metadata validation of `add-indicator-column-metadata`: `weight` on one numeric column.
  - `Reports/ReportDataBuilder.cs`, `ReportHtml.cs`, `ReportWorkbook.cs`, `WidgetWorkbook.cs`, `ReportText.cs`: the weighting line.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpScriptTools.cs`, `Mcp/Instructions.md`, `Mcp/McpGuide.cs`, and `Assistant/Prompts/system.md` with `AssistantPrompts.Version`.
  - OpenAPI, unit and integration tests (weighted sums, ratios and quantiles checked against a Python reference), the production shapes fixture and the pinned-text tests.
- **ui** (`tamiza-ui`):
  - `project-indicators.ts` and `indicators/dashboard-filter-bar.ts`: the switch and its address.
  - `indicators/widget-view.ts`: the mode in the query key, and the "Not weighted" note.
  - `indicator-metadata-fields.ts`: the weight in "Column names", with the count of widgets it changes.
  - `script-editor/indicator-view.ts`: the weight warnings.
  - `indicators/widget-export/footer.ts` and `indicators/dashboard-print.ts`: the weighting line.
  - `core/projects/indicators.service.ts`, `en.json` and `es.json`.
- **Database:** none. The weight lives in the column metadata (`script_versions.columns`, migration 0021 of `add-indicator-column-metadata`), and the switch is not saved.
- **Analytics:** none. A script publishes the weight column like any numeric column.
- **Docs:** `docs/indicators-dashboard.md` (a "Weights" section: declaring a weight, what is weighted and what is not, the switch), `docs/scripts.md` (the weight in the metadata table, and how to prepare a table for it), `docs/mcp.md`, `docs/tamiza-brief.md`.

**Compatibility.** No indicator declares a weight today: the API, the UI and the analytics have no weight concept. So no dashboard shows the switch, and every widget, version, scheduled email, image, workbook and MCP result is as today; the production shapes fixture keeps byte-identical configs and the same queries. Weighting starts only when an analyst declares a weight on an indicator. From then on, its widgets and their emails show weighted figures by default; the save form says so first, and restoring the previous script version undoes it. The box plot scenario "Same figures as pandas" stays true for every box plot without a weight. MCP clients that do not send `weighted` get today's results until a weight is declared, and then each result says it is weighted. Widgets that filter to the "Todos los vehículos" rows keep working, and a dashboard filter whose column a table lacks is still skipped silently, so the median cards keep their figures. On rollback, an older API ignores the weight and draws every figure unweighted, and an older page ignores `weighted=0` in the address.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, on the main specs as they are then. Decisions to take with the user at that time:
- A weight per row only (recommended), or also weighted twin columns for pre-aggregated tables whose rows mix weights.
- The switch's default once a weight is declared: "Weighted" (recommended) or "Unweighted"; and whether a scheduled email gets its own choice.
- Weighted quantiles: which method (for example the weighted inverse of the cumulative distribution, with no interpolation), or box plots and median tiles kept unweighted with a note.
- A widget that always ignores the weight (for example a tile that counts interviews): in this change or later.
- The series a widget shows: chosen without the weight (recommended, stable legends and colors) or following the switch.
