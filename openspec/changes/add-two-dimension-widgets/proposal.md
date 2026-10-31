# Proposal

## Why

Each widget plots one indicator grouped by one column, so a dashboard can show households by municipality or by sex, but never by municipality *and* sex. Survey reports are built on exactly that cross: composition of a category (water source by municipality), gaps between two groups (women and men by age band), perception scales by group, and the contingency tables that analysts paste from pandas into Word. Today they need one widget per value of the second column, and the dashboard cannot show the cross at all.

The indicator contract already allows several text dimensions in one indicator table; only the widgets cannot use a second one.

## What Changes

- **A second dimension in a widget.** A widget's query can name a *series* column next to its group column: one value per pair of group and series, summed or as a ratio of sums, with row, column and grand totals. The series of a widget keep their colors and their order while members change dashboard filters. Past 8 series (30 in a heat map or a cross table), the smallest are shown together as *Other*.
- **Bars and columns by series.** A bar or column widget with a series is drawn **grouped** (side by side), **stacked**, or **stacked to 100%** (each category's composition). Stacked and 100% widgets take a measure, not a ratio: rates do not add up.
- **Lines by series.** A line widget with a series draws one line per series value.
- **Heat map** (new kind): groups as rows, series as columns, each cell shaded by its value on the sequential scale the maps already use, with its value on hover and a legend.
- **Population pyramid** (new kind): groups (age bands) as rows in natural order, with the bars of one series value to the left and another to the right, both chosen in the widget's settings. Rows of any other series value are left out, a row with no group or no series value draws no bar, and the widget says how many rows it left out. Values read as the indicator formats them: counts or percents.
- **Diverging Likert bars** (new kind): for agreement scales. The analyst orders the scale's values and marks each as negative, neutral or positive. Each group's bar is its composition in percent, centered on the neutral value, negatives to the left. Values outside the scale are left out and counted, like the pyramid's.
- **Cross table** (new kind): a widget that is a table, groups as rows and series as columns, with totals per row and column and the indicator's format.
- **Everywhere a widget goes.** "Show as table", printing, the scheduled dashboard emails (body and workbook) and the MCP server's widget tools handle the series: tables become cross tables with totals, and the MCP tools take and return the new settings. The indicator table export stays the whole table as published.
- **Migration.** `0017_add_two_dimension_widgets.sql` lets `dashboard_widgets.chart_type` hold `heatmap`, `pyramid`, `likert` and `crosstab`. Existing widgets have no series and do not change.

Not in this change:
- A third dimension, small multiples, or several indicators in one widget (comparing indicator tables stays out of scope, as the brief says).
- Two measures in one widget (scatter, bubble, dual axes).
- Sorting series by value, or choosing series colors by hand.
- Diverging Likert bars that show counts instead of percents.
- Changes to the indicator script contract or its templates: a table with two text dimensions already works.

## Capabilities

### New Capabilities

None. Widget kinds and queries belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`: the widget query gains a series column and totals; the widget kinds gain the series layouts of bars, columns and lines and the four new kinds; stale configuration covers the series column; "Show as table" becomes a cross table for a widget with a series.
- `dashboard-reports`: a scheduled email shows a widget with a series as a cross table, at most 6 series columns wide in the body and whole in the workbook.
- `dashboard-printing`: the new kinds print whole with their legends; a cross table widget prints as a table, with the columns that fit.
- `mcp-server` (from the `add-mcp-server` change, which must be archived first): the widget tools describe, accept and return the series and the new kinds.

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`, `ChartTypes`: four kinds, the `seriesBy` column and the per-kind settings (`layout`, `left` and `right`, `scale`).
  - `Indicators/IndicatorDashboard.cs`: the query groups by group and series with `GROUPING SETS` for the totals, and computes each widget's series domain over its own filters.
  - `Indicators/WidgetQuery.cs`, and `Reports/` (`ReportDataBuilder`, `ReportHtml`, `ReportText`, `ReportWorkbook`): cross tables in emails and their attachments.
  - `Mcp/McpDashboardTools.cs` and `Mcp/Instructions.md`: the new settings and kinds.
  - OpenAPI, unit and integration tests.
- **ui** (`tamiza-ui`):
  - `indicators/widget-dialog.ts`: the series field and each kind's settings, with the preview.
  - `indicators/axis-chart.ts` and `chart-data.ts`: grouped, stacked and 100% bars, lines by series; new components for the heat map, the pyramid, the Likert bars and the cross table.
  - `indicators/widget-table.ts`: cross tables.
  - `styles.css`: diverging tokens for the Likert scale, light and dark.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0017_add_two_dimension_widgets.sql` widens `ck_dashboard_widgets_chart_type`. `TamizaDbContext` mirrors it.
- **Analytics:** none.
- **Docs:** `docs/indicators-dashboard.md` (the series, each kind, and how to prepare an indicator with two dimensions), `docs/mcp.md` (the tools' new settings), `docs/tamiza-brief.md` (data model and roadmap).
