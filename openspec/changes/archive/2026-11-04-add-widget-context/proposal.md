# Proposal

## Why

A widget has only a title, shown on one cut line of its tile. In the Duitama market study (478 surveys, 9 dashboards, about 190 widgets) authors put everything a reader needs into it: Teatro's titles average 80 characters and reach 153 (heat map 5b7a), with the question code, the base ("% de quienes asistieron"), "no suman 100 %" and how to read the chart. The line that names what is plotted reads "teatro_hoy: numerator / denominator by categoria", and nobody can change or hide it. The unit belongs to the indicator, so about 20 widgets print "11.21% de los hogares" when the base is "de quienes asistieron" (Teatro 8196). Indicator descriptions hold rules readers need ("Sin ponderar", "Filtrar siempre por variable") but appear only in the widget form, and a dashboard cannot say where its data comes from.

## What Changes

- **Subtitle.** A widget can have a subtitle of 1 to 200 characters: the question the chart answers, under the title. On screen it shows at most two lines; images, emails and workbooks give it whole, and paper prints it whole.
- **Footnote.** A widget can have a footnote of 1 to 500 characters at its foot, such as "No suman 100 %: respuesta múltiple". It shows on screen, on paper, in images, in the scheduled email and in both workbooks.
- **Caption: default, own text or hidden.** The line that names what a widget plots ("{what} by {group}" above a chart with no series, the line above a pie, a map, a heat map or a cross table, and a stat tile's label) has three modes:
  - default: as today;
  - own text (1 to 200 characters): it replaces "what" everywhere a widget names what it plots: that line, the stat tile's label, the tooltip line of the plotted value, the value column of "Show as table", and the email and workbook headers;
  - hidden: the line is no longer shown, but screen readers, tooltips and tables still name what is plotted.

  A series legend, which lists series values, is never hidden by it.
- **Unit of one widget.** A widget can replace its indicator's unit (1 to 32 characters), for example "de quienes asistieron" in place of "de los hogares". It shows on screen, in images, on paper, in the email, in both workbooks and in what AI clients read through MCP. The indicator's other widgets keep its unit.
- **The indicator's description on demand.** A widget can show an "i" button in its header that opens the indicator's description. It is never printed or exported.
- **Dashboard source.** An `analyst` or `admin` can give a dashboard a source of 1 to 300 characters from its actions menu. It is shown:
  - under the widgets on screen, and in the print header;
  - in the footer of every widget image;
  - in the scheduled email's header and its workbook's summary, and in the widget workbook's summary;
  - to AI clients through MCP.

  Saving it makes a version, a restore brings it back, and a duplicate copies it.
- **A widget's Excel file carries its texts.** The workbook a member downloads for one widget now gives its subtitle, footnote, caption and unit. A map's tooltip columns get their names and formats there too, which that file loses today.
- **Tile heights stay as they are.** The subtitle and the footnote take room from the chart, which is drawn smaller. The widget form warns when the texts would crowd the tile: on a tile two rows high, or when they are long for the tile's size. On a small stat tile the figure always stays whole; its label is what gets cut.
- **Nothing changes until someone sets it.** Every new setting is optional. Widgets and dashboards that set none look, print, export and are emailed exactly as today, and the update itself creates no new dashboard version. An AI client (through MCP) that does not know the new settings gets the same answers as before; one that rewrites all of a widget's settings without them clears them, as it clears a widget's color today. The one exception is a fix: the widget workbook of the field map now names and formats its tooltip columns as its scheduled email already does.
- **Migration.** `0022_add_widget_context.sql` adds `dashboards.source`, empty by default.

Builds on `add-indicator-column-metadata`, implemented and archived on 2026-11-04. The caption's "what" is built from the indicator's column names (`ColumnNamer`, `display.labels`), its migration 0021 is applied, and its fixtures `production-shapes.json` and `column-labels.json` exist and are extended here. The later changes `add-small-sample-warnings` and `improve-stat-cards` rebase on this change's wording of "Map tooltip" and "Stat tile", and `add-locale-formatting` on its wording of "Indicator formatting".

Not in this change:
- The automatic "n = 223 hogares" footer and the "Sin dato" footer (`add-small-sample-warnings`).
- Rich text, Markdown and line breaks in these texts, and text widgets (`add-dashboard-sections`).
- A source per widget.
- Hiding the unit on one widget. A percent widget can use `%`, which is never repeated after the number.
- A widget's unit in `query_indicator`, which previews a query and keeps the indicator's unit.
- Translating these texts. They are shown as their author wrote them.

## Capabilities

### New Capabilities

None. Widget settings belong to `dashboard-indicators` and dashboard settings to `custom-dashboards`.

### Modified Capabilities

- `dashboard-indicators`:
  - "Indicators dashboard and its indicators": the dashboard's source follows its widgets;
  - "Indicator formatting" and "Stat tile": a widget's own unit replaces its indicator's;
  - "Map tooltip": the widget's own caption text and unit name the plotted value;
  - new: "Widget subtitle", "Widget footnote", "Widget caption text", "Widget caption hidden", "Widget unit", "Widget indicator description", "Widget context validation", "Widget context in the widget form", "Widget context in a tile", "Widget context warning in the widget form", "Widget context kept with the widget", "Widget context left unset".
- `custom-dashboards`: new "Dashboard source", "Dashboard source shown", "Dashboard source in a copy".
- `dashboard-versions`: new "Dashboard source in versions".
- `widget-export`:
  - "Parts of a widget image", "Footer of a widget image" and "Summary of a widget workbook";
  - new: "Widget context in a widget workbook".
- `dashboard-reports`:
  - "Email header";
  - "Missing values in widget tables": a widget whose indicator is gone keeps its own unit and caption text;
  - new: "Widget context in a scheduled email", "Widget footnote in a scheduled email".
- `dashboard-printing`:
  - "Print header";
  - "Charts on paper": a chart that the whole texts leave too little room is cut at its own box, never over them;
  - new: "Widget context on paper".
- `mcp-server`: new "Widget context through MCP", "Dashboard source through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`:
    - `WidgetDisplay` gains `subtitle`, `footnote`, `caption` (a new `WidgetCaption`), `unit` and `showDescription`, each left out of the JSON while unset, and `Normalize()` keeps a display that holds only them;
    - `DashboardView` gains `source`.
  - `Indicators/IndicatorValidation.cs`: the lengths, control characters and the caption rule.
  - `Data/Dashboard.cs` and `Data/TamizaDbContext.cs`: `Source`.
  - `Indicators/DashboardValidation.cs`, `Indicators/DashboardEndpoints.cs`: `PUT /dashboards/{id}/source`, and duplicate copies the source.
  - `Indicators/DashboardVersions.cs` and `Indicators/DashboardVersionEndpoints.cs`: the source in a version's content, its details and a restore, and the change kind `sourceChanged`.
  - `Indicators/WidgetExportEndpoints.cs`: the source of the dashboard or of the version shown, and its config errors keyed `config.…` as a widget's save keys them.
  - `Reports/`: `ReportDataBuilder` (`Plotted`, `MetaOf`), `ReportModel`, `ReportHtml` (and its plain text), `ReportWorkbook`, `WidgetWorkbook`, `ReportText`.
  - `Mcp/McpDashboardTools.cs`, `Mcp/McpDashboardVersionTools.cs` and `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, the pinned-text tests included.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: the display fields, `source`, `saveSource` and `sourceChanged`.
  - `indicators/format.ts` (`plotted`, `captionShown`, `withWidgetUnit`, `textsCrowd`), `widget-view.ts` (`displayFor`, the formatters, the footnote), `widget-grid.ts` (subtitle, "i" button), `widget-dialog.ts`.
  - The captions of `axis-chart.ts`, `pie-widget.ts`, `map-widget.ts`, `heatmap-chart.ts`, `crosstab-widget.ts` and `stat-widget.ts` (and the stat tile's centering when it has texts), and the map's tooltip.
  - New `indicators/description-popover.ts` and `indicators/dashboard-source-dialog.ts`.
  - `project-indicators.ts`: the **Source** action, the source under the widgets and in the print header, the image job and the workbook request.
  - `indicators/widget-export/` (`image-layout.ts`, `widget-exporter.ts`, `footer.ts`).
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0022_add_widget_context.sql` adds `dashboards.source` (`text`, null by default) with a check on its length. It rewrites no row, and `dbUp/README.md` names the source in its rule about scripts that rewrite `dashboard_versions.content`.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: the widget's texts, caption and unit, the "i" button, the dashboard source, the actions menu and the tile heights;
  - `docs/mcp.md`: the new fields, `source` and `sourceChanged`;
  - `docs/operations.md`: the source, subtitles and footnotes among what leaves the deployment by email;
  - `docs/tamiza-brief.md`: the data model, and this change done in the program's list.
