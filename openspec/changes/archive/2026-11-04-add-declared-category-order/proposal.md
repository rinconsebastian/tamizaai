# Proposal

## Why

Survey answers have an order of their own: Sí, Tal vez, No, No sabe, or the price ranges of a question. Dashboards draw them in alphabetical or natural order, and put "Otro", "No sabe" and "Sin dato" wherever the alphabet does: "Sin dato" lands between COMUNA 7 and SIN NOMBRE. In the Duitama market study the authors worked around it twice. At least eight indicators number their labels ("1. Sí" … "4. No sabe", "1. No pagaría" … "6. Más de $50.000"), so the number shows in every legend, tooltip and email. Most long tables publish an `orden` column, and 27 widgets sort by it. That only works for bars and columns: series, lines, heat maps, pies and box plots have no order to choose.

## What Changes

- **An order declared once, for a column of an indicator.** Next to the column names of an indicator (`add-indicator-column-metadata`, implemented), a column that widgets group or split by can have:
  - an order: up to 100 values, in the order to show them;
  - values kept at the end: up to 20, such as "Otro", "No sabe" or "Sin dato", whatever the order or the sort;
  - hidden numbers: a leading number such as "1. ", "2) " or "3 - " is not shown.

  They are entered in the "Column names" section of the indicator's details, or by an AI client through `save_script`, and are saved with the script's version, so restoring a version brings them back.
- **A widget can have its own order** of its groups and of its series, up to 100 values each, set in the widget form by moving values up and down. It is a display setting (`display.order`): changing it redraws the chart without asking for the data again.
- **Which order wins.** The widget's own order, else the indicator's, else today's order: the group column's own order for bars and columns, largest first for a pie, natural order for lines, heat maps, pyramids, Likert bars, cross tables, box plots and series. Values the order does not list follow in today's order, then the values kept at the end, then "(none)" and "Other".
- **Sorts stay in charge.** A bar or column chart with a sort, and a box plot ordered by median, keep that order even when the indicator declares one: only the values kept at the end, then "(none)", move to the end. A widget cannot have both a sort and its own order of groups. Inside sorted bars, the series follow their declared order.
- **Everywhere values are listed:** axes, legends, the keyboard order of marks, "Show as table" and cross tables, images, printing, the scheduled email's body and workbook, the widget workbook, and the order of the widget data AI clients read. A pyramid's two sides and a Likert scale keep the order the widget sets for them. A pie in a declared order still tells screen readers which slice is the largest.
- **Numbers hidden, values kept.** Hiding numbers changes only the text shown. Filters, colors kept by value, dashboard filters and AI clients keep matching the full value ("1. No pagaría" still selects the row). A range such as "10 - 14" is not a number to hide, and two values that would read alike keep their full text.
- **Colors.** Values with no color kept take the palette's colors in the order they are shown, so declaring an order can recolor series and pie slices. The forms say so and point to colors kept by value.
- **Guidance.** The script assistant's guide, the MCP instructions and the documentation say to declare the order instead of numbering labels or publishing an order column.
- **Compatibility.** Nothing changes until someone declares an order:
  - existing widgets, dashboard versions, scheduled emails, workbooks and MCP results keep today's order, labels and colors, exactly as stored and compared today;
  - no stored setting is rewritten and this change adds no database migration;
  - numbered labels and sorts by `orden` keep working, and hidden numbers clean those labels without touching the scripts;
  - an AI client that does not know the new settings sees the same results as today.

Not in this change:
- Top or bottom N groups with the rest folded into "Other" (P2 `add-normalization-and-totals`).
- Choosing which series or slices are kept: past the limit, the largest are still kept and the rest combined as "Other", whatever the order.
- Hiding values such as "Sin dato" (P2 `add-filter-exclusions`).
- An order or hidden numbers in the values offered by dashboard filters, widget filters and the Colors form: they keep listing full values.
- Ordering a map, a histogram's ranges or a stat tile.
- An order declared by the script itself (rejected by `add-indicator-column-metadata`: metadata lives in the details form and MCP).
- Prefixes other than a leading number, such as "a)" or roman numerals.

Builds on two changes implemented and archived on 2026-11-04:
- `add-indicator-column-metadata`: the per-column metadata map (`script_versions.columns`, `IndicatorColumnMetadata`), its "Column names" section and `save_script`'s `columns`.
- `add-widget-context`, for the widget workbook only: its export request already sends the widget's saved config, display included, which this change reads.

`add-chart-annotations` is implemented before this change in the program's order; it modifies "Line chart" too (see design.md, "Spec ownership").

## Capabilities

### New Capabilities

None. Orders belong to `dashboard-indicators` and the column metadata to `pipeline`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Bar and column order": a sort and a declared order are told apart, and a sort keeps the values kept last at the end;
  - "Series order and colors": series follow a declared order;
  - "Pie chart": slices follow a declared order, and its summary still names the largest slice;
  - "Line chart", "Heat map", "Population pyramid" and "Cross table widget": their natural order gives way to a declared order;
  - "Box plot order": boxes follow a declared order, and an order by median keeps the values kept last at the end;
  - new requirements "Declared category order of a widget", "Declared category order applied", "Declared category order in charts and tables", "Numbered category prefixes hidden", "Declared category order in the widget form" and "Widgets without a declared category order".
- `pipeline`: new requirement "Declared category order of an indicator column".
- `dashboard-colors`: new requirement "Colors follow a declared category order".
- `dashboard-reports`: new requirement "Declared category order in a scheduled email".
- `widget-export`: new requirement "Declared category order in widget files".
- `dashboard-printing`: new requirement "Declared category order on paper".
- `dashboard-versions`: new requirement "Declared category order in dashboard versions".
- `mcp-server`: new requirement "Declared category order through MCP".

## Impact

- **api** (`tamiza-api`):
  - New `Indicators/CategoryOrder.cs`: the shared ordering and prefix rule and its limits, twin of the UI's `category-order.ts`.
  - `Indicators/IndicatorModels.cs`: `WidgetDisplay.Order` and its record `WidgetCategoryOrder`, left out of the JSON while unset.
  - `Indicators/IndicatorValidation.cs`: the display order's rules.
  - `Indicators/ColumnMetadata.cs` (from add-indicator-column-metadata, which holds only `label` today): the record `IndicatorColumnMetadata` gains `order`, `last` and `stripPrefix`, normalized in `ColumnMetadata.Normalize` and checked in `ColumnMetadata.Check`. `ScriptEndpoints.ValidateMetadata` already calls that check, so `Pipeline/ScriptEndpoints.cs` does not change.
  - `Reports/ReportDataBuilder.cs`: the email and widget workbook tables in the widget's order, with prefixes hidden.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpScriptTools.cs`, `Mcp/Instructions.md`.
  - `Assistant/Prompts/system.md`, with `AssistantPrompts.Version`.
  - OpenAPI, unit and integration tests, and the production-shapes fixture.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts` (`WidgetDisplay.order`, the limits) and `core/projects/pipeline.service.ts` (the new properties of `ColumnMetadata` and of the form's `ColumnDraft`), with `pages/projects/column-names.ts` (`draftsOf`, `columnsOf` and `sameColumns`, which carry the form's rows).
  - New `indicators/category-order.ts`, with the shared fixture `indicators/testing/category-orders.json`, and `indicators/value-order-editor.ts`, used by both forms.
  - `indicators/chart-data.ts`, `series-table.ts`, `series-legend.ts`, `axis-chart.ts`, `series-plot.ts`, `pie-widget.ts`, `heatmap-chart.ts`, `pyramid-chart.ts`, `likert-chart.ts`, `crosstab-widget.ts`, `boxplot-chart.ts`, `map-tooltip.ts`, `map-widget.ts`.
  - `indicators/widget-view.ts`, `indicators/widget-dialog.ts`, `indicator-metadata-fields.ts`, `project-scripts.ts`.
  - `indicators/testing/production-shapes.json`, `en.json` and `es.json` strings, and the specs.
- **Database:** no migration. The properties live in `script_versions.columns`, which add-indicator-column-metadata added (migration 0021, applied); widget settings stay in `dashboard_widgets.config`.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: declared order, values kept last, prefixes, colors, limits, and the sections that say "natural order".
  - `docs/scripts.md` (the metadata table), `docs/mcp.md`, `docs/tamiza-brief.md` (the program's list).
