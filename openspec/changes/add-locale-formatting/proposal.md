# Proposal

## Why

Tamiza writes a number in the format of the browser that shows it. In the market study of the Duitama Bicentennial Center the client reads Colombian figures, but the study's tiles read `46.85% de los hogares` and `60,514 pesos` in an English browser and through the AI tools, `46,85 %` and `4500` in a browser set to plain Spanish, and the scheduled email follows whoever saved it. Two members print the same dashboard with different numbers, and the study's rule, figures in the Colombian format (`46,9 %` and `$60.514` in its specification), cannot be met.

Decimals are fixed by the indicator's format. A percent has up to two (`15.7%` next to `11.21%`), and a box plot over a count of pesos writes `40,000.00 pesos` next to a stat tile that writes `60,514 pesos`. A large figure such as `1,016,279 pesos` cannot be shortened on a small tile.

## What Changes

- **A regional format for the project's numbers.** In Settings > General a project `admin` keeps "Each member's browser" (the default, as today) or chooses one of seven formats: Spanish of Colombia (`es-CO`), of Mexico (`es-MX`), of Spain (`es-ES`) or of Latin America (`es-419`), English of the United States (`en-US`) or of the United Kingdom (`en-GB`), and Portuguese of Brazil (`pt-BR`). Once one is chosen, every member gets the numbers of the project's dashboards in it:
  - on screen, in the widget form's preview and in the script editor's preview of an indicator;
  - in printed dashboards and widget images;
  - in the scheduled emails and their spreadsheets;
  - in a widget's spreadsheet;
  - in the figures the AI tools read, which are in American English today.

  Dates, times and the interface's own words stay each member's. An email's words and dates stay those of the member who saved it. Viewers get the format too, so their screens use it, although only admins and analysts open Settings.
- **Decimals.** An indicator can say how many decimals its widgets show, 0 to 4, next to its format; the choice is saved with each version of its script. A widget can choose its own, on every chart type, and the widget's choice comes first. With neither, numbers keep today's digits:
  - counts are whole;
  - decimals and currencies have two;
  - percentages have up to two;
  - a box plot over a count has two.

  Axis ticks, shares, numbers of rows and a map's tooltip columns keep their digits. A histogram's ranges take the decimals. A widget's own unit still takes the place of its indicator's, and a unit of `%` on a percent indicator is still not repeated after the number, with decimals and in every format.
- **Abbreviated numbers.** A stat tile, and the value labels of bar, column, line and pie charts, can shorten large numbers in the project's format: `1 M pesos`, `$1,2 M`. Numbers under 1,000 and percentages are never shortened. Tooltips, tables, emails, spreadsheets and the AI tools always give the full number, and a shortened tile shows it when pointed at and reads it to screen readers.
- **The same figures everywhere.** The browser and the server write the same text for the same number in each of the seven formats, checked against one shared list of examples. Both write what browsers write for each format, with one exception that the client's specification asks for and that the user chose on 2026-11-04:
  - in a Spanish format (`es-CO`, `es-MX`, `es-ES` or `es-419`), every percentage has a no-break space before `%`: `46,9 %` for 46.85 % with one decimal in `es-CO`, on screen, in images, printed dashboards, emails, spreadsheets and the AI tools. Browsers write it so only for Spain; Tamiza adds it for the other three. The English and Portuguese formats write `46.9%` and `46,9%`;
  - without a project format nothing changes: an `es-CO` browser keeps `46,9%`, and the email keeps the `45,5%` it writes today;
  - `$ 60.514` for a currency indicator with the unit `COP` and no decimals, and `$60.514` with the unit `$`;
  - `60.514 pesos` for a count with the unit `pesos`, and `4500` in Spain's format.
- **Through the AI tools (MCP).**
  - `get_project` gives the project's format.
  - `save_script` takes an indicator's decimals: left out, the current ones are kept; `null` clears them.
  - The tools that list indicators give their decimals.
  - The widget tools take a widget's decimals and abbreviation, with the rules of the form.
- **Compatibility.** Every new setting is optional, and leaving it out keeps today's behavior and look:
  - existing widgets are saved exactly as they are and make no new dashboard version;
  - dashboard versions, duplicates and moved widgets are unchanged;
  - scheduled emails keep the saver's formats, and the AI tools keep American English, until an admin sets the project's format;
  - setting the format changes every number of the project's dashboards at once. That is the point, and only an admin can do it.
- **Migration.** `0024_add_locale_formatting.sql` (after 0021 and 0022, and the 0023 of `add-small-sample-warnings`) adds two empty settings, the project's format and the decimals of an indicator's version. It rewrites no row.

Not in this change:
- A choice of the interface language, or of the format of dates and times.
- A format per dashboard or per member.
- Formats other than the seven, such as `es-AR` or `fr-FR`.
- The performance and clean data dashboards, and the CSV and XLSX exports of a whole table, which keep their formats.
- Abbreviated numbers in emails, spreadsheets, tooltips, tables or the AI tools.
- The separators Excel shows in a number cell, which the reader's own settings decide.
- The script assistant suggesting decimals.

Depends on:
- `add-chart-annotations`, for the value labels that an abbreviation shortens.
- `add-widget-context` (implemented and archived on 2026-11-04), whose widget spreadsheet request already carries the widget's display settings, so a widget's decimals reach its spreadsheet, and whose widget unit is merged before the formatters (`withWidgetUnit`, `MetaOf`) next to the decimals.
- `add-indicator-column-metadata` (implemented and archived on 2026-11-04), for the production-shapes fixture this change extends, and for migration 0021 before this change's 0024.

The first comes earlier in the order of implementation, and the other two are done. The other P1 changes (`add-declared-category-order`, `add-category-highlight`, `add-small-sample-warnings`, `improve-stat-cards`) are implemented before this one too, and this change is rebased on them: the n lines of `add-small-sample-warnings` and the "Without filters" line of `improve-stat-cards` are written in the project's format and with the widget's decimals.

## Capabilities

### New Capabilities

None. The project setting belongs to `projects`, and the formats to the capabilities that write numbers.

### Modified Capabilities

- `projects`: new requirements "Project regional format" (the setting, its values and who sets it) and "Project regional format in Settings".
- `dashboard-indicators`:
  - "Indicator formatting" (main's text, which now carries the widget's own unit): decimals replace a format's digits, separators and the space before `%` of a Spanish format follow the project's format, a box plot over a count keeps two decimals unless decimals are set, and the widget's own unit and the rule that a unit of `%` is not repeated stay true with decimals;
  - new requirements "Regional format of a project's dashboards", "Percentages in a Spanish regional format", "Decimals of a widget", "Abbreviated numbers", "Abbreviated number text", "Full values beside abbreviated numbers" and "Number format in the widget form".
- `pipeline`: new requirement "Indicator decimals", saved and restored with the script.
- `interface-language`: "Regional formats" (the project's format for its dashboards' numbers) and "Content shown as it was created" (numbers in the two workbooks).
- `dashboard-reports`:
  - "Email language and regional format": numbers follow the project's format, with the space before `%` of a Spanish one, and the `45,5%` it pins stays for a project with no format;
  - "Numbers in the workbook": decimals in the cells' formats;
  - new requirement "Widget decimals in a scheduled email".
- `widget-export`: "Numbers in a widget workbook" (decimals, and a tile's value in full) and "Language of widget files" (numbers of a project with a format, the Summary's number of rows included).
- `dashboard-versions`: new requirement "Number formats and dashboard versions".
- `dashboard-printing`: new requirement "Number formats on paper".
- `mcp-server`: new requirements "Number formats through MCP", "Decimals through MCP" and "Widget decimals and abbreviations through MCP".

## Impact

- **api** (`tamiza-api`):
  - `Data/Project.cs`, `Data/Pipeline.cs` (`ScriptVersion`) and `Data/TamizaDbContext.cs`: `NumberLocale`, `Decimals` and their CHECKs.
  - `Projects/ProjectModels.cs` and `ProjectEndpoints.cs`: `numberLocale` in the PATCH request and in project details.
  - `Pipeline/PipelineModels.cs` and `ScriptEndpoints.cs`: `decimals` in the save request, its validation, the version details, the restore and the MCP keep rule.
  - `Indicators/IndicatorModels.cs` and `IndicatorEndpoints.cs`: `WidgetDisplay` gains `decimals` and `compact`, and `IndicatorEntry` gains `decimals`. `IndicatorValidation.CheckDisplay` validates them.
  - A new `Reports/NumberLocales.cs`: the API's own table of the seven formats, the space before `%` of the Spanish ones included, so its text matches the UI's without depending on the server's culture data.
  - `Reports/ReportFormat.cs`: decimals, and the project's format for numbers.
  - `Reports/ReportDataBuilder.cs`: the indicator's and the widget's decimals, and the cells' Excel formats.
  - `Indicators/WidgetExportEndpoints.cs`, and `Reports/WidgetWorkbook.cs` (the Summary's line of the number of rows).
  - `Mcp/McpDashboardTools.cs`, `McpScriptTools.cs`, `McpScripts.cs`, `McpProjectTools.cs`, `McpCallFilter.cs` and `McpCallScope.cs` (which arguments a call sent, for the keep rule of `decimals`), and `Mcp/Instructions.md`.
  - OpenAPI, unit and integration tests, including the pinned-text tests.
- **ui** (`tamiza-ui`):
  - `core/projects/projects.service.ts`, `pipeline.service.ts` and `indicators.service.ts`: the new fields.
  - A new `core/i18n/number-locale.ts`: the locale numbers are written in, the project's or the browser's, and whether percentages take the space of a Spanish project format, given by `project-context.ts` and `project-page.ts` and by the widget form to every chart below them.
  - `indicators/format.ts`: decimals, a short form for abbreviations, and percentages through the space rule of a Spanish project format. `indicators/map-tooltip.ts`: numbers and dates in their own locales.
  - Every chart that writes numbers (`axis-chart.ts`, `pie-widget.ts`, `likert-chart.ts`, `crosstab-widget.ts`, `boxplot-chart.ts`, `map-widget.ts`, `widget-view.ts`) and `script-editor/indicator-view.ts`.
  - `stat-widget.ts`, and the data labels.
  - `project-overview.ts` (Settings > General), `indicator-metadata-fields.ts` and `project-scripts.ts` (the indicator's details), `widget-dialog.ts` and `project-indicators.ts` (what the widget form is given).
  - `en.json` and `es.json` strings, and the specs.
  - A shared list of cases, `indicators/testing/number-formats.json`, that the UI and API tests both read, and new shapes in the production-shapes fixture.
- **Database:** `0024_add_locale_formatting.sql` adds `projects.number_locale` (a CHECK on the seven formats) and `script_versions.decimals` (a CHECK on 0 to 4), both null for every existing row.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: the project's format, decimals and abbreviations, and what each surface writes.
  - `docs/scripts.md`: the decimals in the metadata table.
  - `docs/operations.md`: the numbers of a scheduled email.
  - `docs/mcp.md`: `formatted`, `numberLocale`, `decimals` and the display settings.
  - `docs/tamiza-brief.md`: data model and roadmap.
