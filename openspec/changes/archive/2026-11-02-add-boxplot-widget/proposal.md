# Proposal

## Why

Survey reports compare how a value is spread across groups: household income by municipality, travel time to the health post by zone, household size by stratum. The histogram shows one distribution, and bars show one sum or one rate per group, so a dashboard cannot show the median, the spread and the extreme values of each group side by side. Analysts still do that in pandas and paste a box plot into Word, which is the manual flow Tamiza replaces.

The indicator contract already lets a script publish one row per household or person. The widgets just cannot read those rows as a distribution per group.

## What Changes

- **Box plot** (new kind, `boxplot`). It draws one box per value of the group column, or one box over all rows when there is no group column. Each row of the indicator table that the query matches is one value, and rows are never summed: the value is the row's measure, or the row's own numerator divided by its own denominator. Rows with no value are left out.
- **Statistics computed in the database**:
  - for each box: its number of values, minimum, first quartile, median, third quartile, maximum and mean;
  - quartiles and median by linear interpolation, the method `pandas` uses by default;
  - the statistics cover every matching row, so they are never computed from a truncated sample.
- **Tukey whiskers and outliers**:
  - whiskers reach the last value within 1.5 times the interquartile range of the box;
  - values beyond them are drawn as points and counted;
  - a box draws at most its 100 outliers farthest from its median, and says how many it did not draw.
- **Vertical or horizontal.** A display setting saved with the widget: vertical by default (first box on the left) or horizontal (first box at the top, for long group names). Changing it does not query the indicator again.
- **Order.** By default the boxes follow natural order (2 before 10, a missing group last). They can be ordered by median, ascending or descending. The row limit counts boxes.
- **Drawing**:
  - one linear scale for all boxes, which need not start at zero;
  - a line at the median and a cross at the mean;
  - the widget's palette color, or the color kept for the group value, as bars with no series have;
  - a key that says what each mark means;
  - tooltips and keyboard access to every box.
- **Count indicators.** A box plot over a `count` indicator shows its figures with two decimals, because quartiles, medians and means fall between whole numbers. Its numbers of values and of outliers stay whole numbers.
- **Everywhere a widget goes**:
  - "Show as table" lists one row per box: group, values, minimum, Q1, median, Q3, maximum, mean, outliers. The scheduled emails (body and workbook) and printing show the same table.
  - The MCP widget tools take the new kind and its orientation, and return each box's statistics and outlier count, without the outliers' values.
- **Guidance.** The indicator authoring guide (script assistant and MCP), the MCP instructions and `docs/indicators-dashboard.md` explain how to prepare a table for a box plot: one row per unit, with the unit's id as text so the rows stay unique.
- **Migration.** `0020_add_boxplot_widget.sql` lets `dashboard_widgets.chart_type` hold `boxplot`. Existing widgets do not change.

Not in this change:
- A series in a box plot (boxes side by side by a second dimension). A box plot with a series is refused, as a histogram with one is.
- Ordering boxes by a column, by their mean or by their spread.
- Other whisker rules (minimum to maximum, percentiles), notched boxes, violin plots and jittered points.
- Aggregating rows by a unit column before the box is drawn. The script decides what one row is.
- A new indicator template. The guide and the documentation give an example instead.
- Changes to the indicator script contract: a table with one row per unit already publishes.

## Capabilities

### New Capabilities

None. Widget kinds and queries belong to `dashboard-indicators`.

### Modified Capabilities

- `dashboard-indicators`:
  - the list of widget kinds;
  - a box plot's rows are not summed, and its ratio is computed per row;
  - a box plot's figures of a `count` indicator have two decimals;
  - a box plot may be ordered by median, and may not have a series;
  - the row limit counts what a widget plots, a box plot's boxes included;
  - a moved widget keeps its orientation;
  - new requirements for the box plot: what it summarizes, its statistics, whiskers and outliers, order, orientation, drawing, marks, and "Show as table".
- `dashboard-colors`: a box plot takes a widget color, and its boxes take the colors kept for their group value.
- `dashboard-reports`: a box plot's table in the body and the workbook, and its numbers in the workbook.
- `dashboard-printing`: box plots print whole, with their whiskers, outliers and key.
- `mcp-server`: the widget tools accept a box plot and its orientation, and return its statistics.
- `dashboard-versions`: a version holds a box plot's orientation.
- `custom-dashboards`: a duplicated widget keeps its orientation.

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`:
    - `ChartTypes` gains `Boxplot`;
    - `WidgetDisplay` gains `orientation`;
    - the query response gains `boxes`.
  - `Indicators/IndicatorQueryBuilder.cs` and a new box plan: the statistics, whiskers and outliers in one SQL statement.
  - `Indicators/IndicatorDashboard.cs`: the reader and the natural order.
  - `Indicators/IndicatorValidation.cs`: the kind, the orientation, the median order, and no series.
  - `Indicators/WidgetQuery.cs`.
  - `Reports/`: `ReportDataBuilder`, `ReportText`, `ReportWorkbook`.
  - `Mcp/McpDashboardTools.cs` and `Mcp/Instructions.md`.
  - `Assistant/Prompts/system.md`, with `AssistantPrompts.Version`.
  - `Data/TamizaDbContext.cs`.
  - OpenAPI, unit and integration tests.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the kind, the display and the response types.
  - `indicators/widget-view.ts`, `widget-dialog.ts`, `grid-layout.ts`, `chart-colors.ts`.
  - A new `indicators/boxplot-chart.ts`, with "Show as table" and printing.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0020_add_boxplot_widget.sql` widens `ck_dashboard_widgets_chart_type`; `TamizaDbContext` mirrors it.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: the kind, preparing a table for it, and the limits.
  - `docs/mcp.md`, `docs/operations.md` (the row limit counts boxes).
  - `docs/tamiza-brief.md` (data model and roadmap).
  - `README.md` (the widget kinds).
