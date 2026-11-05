# Proposal

## Why

Long dashboards have no structure. In the Duitama study, Teatro holds 44 widgets in one scroll down to grid row 88, Comercio 32, Escuelas 28 and Centro 27. The study asked for sections with a heading and a short note on how to read them, but every widget is a query over one indicator, so there is nothing to write a heading in. Authors number their titles instead: Teatro's sections exist only as the prefixes "2.1." to "2.12." (five titles have none), Escuelas numbers every title, and Comercio and Centro number only the first widget of each block. Reading notes go into titles too: Teatro's average 80 characters, and the longest has 153.

Teatro is 6 widgets short of the 50-widget limit, so headers that counted as widgets would leave room for only 6 of its 12 sections. Building a section is slow too. Teatro's 2.1 row shows one indicator three times with the same filters, grouped by comuna, estrato and grupo_edad (4e00, 5d50 and a third). Each copy was built from an empty form, because a widget cannot be duplicated: unify-widget-menu-and-ten-dashboards left that for later. The tiles are then placed one by one on the 12-column grid.

## What Changes

- **Text widget** (new kind, `text`):
  - `config.text`: 1 to 2,000 characters in a small Markdown subset. It allows paragraphs, line breaks, bold, italic, bulleted and numbered lists, two levels of headings, and links to `https:` and `mailto:` addresses. Anything else, raw HTML included, is shown exactly as typed.
  - Tamiza's own parser turns the text into page elements and never inserts an HTML string. A C# twin produces the email, and a shared fixture tests both.
  - It has no indicator, query or filters. So it is never stale, a dashboard filter never narrows it, and it never says "Not filtered by".
  - Its title shows in its header, as on every widget. Its default size is 6 × 2.
  - Its menu holds only the editing actions, so viewers see no menu button on it. It has no "Show as table" and no export.
- **Section headers.** A text widget with `config.section: true` is a section header:
  - it is drawn as a heading band with its title, and its text, if any, below it (for example "2.8 Intención de asistir" over "Base: 476 hogares; Comuna 7 y Sin nombre tienen menos de 30");
  - it always takes the whole width of the grid (`x` 0, `w` 12) and is 1 row high by default;
  - it is a real heading for screen readers;
  - its section holds every widget whose top row is at or below the header and above the next header.
- **Index and anchors.** A dashboard with two or more section headers shows an index under its filter bar: one link per section, in reading order. Each section has an anchor in the page address, so a link can open the dashboard at a given section.
- **Collapsing.** A header with `config.collapsible: true` lets any member collapse its section on their own screen. Collapsing is not saved and makes no version, like "Show as table". Printing, images and emails always show every section open.
- **Arrange section.** An `analyst` or `admin` can choose **Arrange section** from a header's menu. The section's widgets keep their reading order and their height and are laid out again: stat tiles in rows of four, 3 columns each, and the other widgets in two columns, 6 each. The widgets below the section move by the rows gained or lost. The action makes one version, and restoring the version before it undoes it.
- **Duplicate a widget.** The editing part of every widget's menu gains **Duplicate widget**:
  - it opens the widget form as a new widget with every setting of the original and its title marked as a copy, so changing only the group column takes one choice;
  - saving places the copy next to the original when the row has room, otherwise below it;
  - the copy counts toward the dashboard's limit.
- **Limits.**
  - Text widgets do not count toward the 50 widgets a dashboard holds. A dashboard holds at most 30 text widgets.
  - Adding, moving, duplicating a dashboard, saving the layout and restoring a version check the two limits separately.
- **Everywhere a dashboard goes**:
  - Printing prints text and headers as they are on screen, and keeps a header on the same sheet as the first widget under it.
  - In the scheduled email, a text widget's block holds its text, in reading order, as safe HTML and as plain text. A header becomes a heading of the body. The workbook has no sheet for text widgets, and a dashboard that holds only text widgets has nothing to send.
  - Versions, restores and dashboard duplicates carry text widgets like any widget, and a moved text widget keeps its text.
  - MCP `create_widget` and `update_widget` take the kind `text` with `text`, `section` and `collapsible`, and `get_dashboard` and `get_widget_data` return them.
- **Migration.** A DbUp script lets `dashboard_widgets.chart_type` hold `text`. It takes the next free number: `0025_add_text_widget.sql` if the P1 changes land first. Existing widgets and layouts do not change.

Not in this change:
- Tabs inside a dashboard (dashboards already are the tabs, up to 10 per project), nested sections, and sections that rearrange themselves on every change.
- Markdown in titles, or in add-widget-context's `display.subtitle` and `display.footnote`, which stay plain text. Images, tables and colors in a text widget.
- Taking the "2.1." prefixes out of existing titles. No title is ever rewritten.
- Duplicating a widget straight onto another dashboard. The copy can be moved with the widget form, as today.
- Text widgets in widget images and widget workbooks.

## Depends on

- `add-indicator-column-metadata`: it rewrites "Dashboard versions", "Duplicating a dashboard" and "Moving a widget to another dashboard" so they list a widget's settings in general terms. That list then covers a text widget's settings and a duplicate's copied settings.
- `add-widget-context`: a section header's text sits beside `display.subtitle`, `display.footnote` and `dashboards.source` and does not replace them. Its tile-height rules apply to text widgets.
- Every other P1 change, only in that a duplicate copies their display fields (`display.labels`, `referenceLines`, `highlight`, `order`, `minN`, `decimals` and the rest) like any other setting.

## Capabilities

### New Capabilities

- `dashboard-sections`: text widgets and their Markdown subset, section headers and what a section holds, the index and anchors, collapsing, arranging a section, and the limit of text widgets.

### Modified Capabilities

- `dashboard-indicators`:
  - "Widgets": the list of kinds gains the text widget, which has no indicator, and the widget limit counts the other kinds only;
  - "Widget menu" and "Editing actions in the widget menu": **Duplicate widget** joins the editing part, and a text widget's menu holds only the editing actions;
  - new requirement "Duplicating a widget".
- `dashboard-filters`: "Widgets a filter does not narrow": a text widget is never listed as not narrowed. New requirement "Text widgets and dashboard filters": they are left out of "narrowing 4 of 5 widgets" and of the columns offered.
- `widget-export`: "Export in the widget menu": a text widget has no Export group.
- `dashboard-reports`: "Email body": a text widget's block holds its text. New requirement "Text widgets in a scheduled email": headings, no workbook sheet, and nothing to send when only text widgets remain.
- `dashboard-printing`: new requirement "Section headers on paper": kept with the next widget, every section printed open.
- `mcp-server`: new requirement "Text widgets and sections through MCP", and "Tools and their roles" if duplicating a widget becomes a tool.

## Impact

- **api** (`tamiza-api`):
  - `Indicators/IndicatorModels.cs`: `ChartTypes` gains `Text`; the config gains `text`, `section` and `collapsible`, written only when set.
  - `Indicators/IndicatorValidation.cs`: `CheckConfig` asks no indicator of a text widget (today "Choose an indicator.", line 95), the text length and the header's width; `DefaultSize` (lines 20-27 throw on an unknown kind).
  - `Indicators/DashboardEndpoints.cs`: the two limits on create (line 548), move (663), dashboard duplicate (465) and layout save (786, which takes "the layout of 1 to {max} widgets"); placing a duplicate; arranging a section.
  - `Indicators/DashboardVersions.cs`: the restore limit (line 568).
  - `Indicators/IndicatorDashboard.cs` and `WidgetQuery.cs`: a text widget is never queried.
  - `Reports/ReportDataBuilder.cs` (its kind switch throws for an unknown kind at line 196), `ReportHtml.cs`, `ReportModel.cs` (`NothingToSend`, line 99), `ReportWorkbook.cs`; `Indicators/WidgetExportEndpoints.cs` refuses a text widget.
  - `Mcp/McpDashboardTools.cs` (`ChartTypeHelp` and the config key lists) and `Mcp/Instructions.md`, which names every kind (pinned by `McpGuideTests`).
  - `Data/TamizaDbContext.cs` mirrors the CHECK (line 440).
  - OpenAPI and tests: the Markdown twin and its fixture, `IndicatorDocsTests` (every kind documented), `McpToolCatalogTests`, `ReportDataTests`, and the production shapes fixture.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: `ChartType`, `chartTypes` and the config fields.
  - New `indicators/text-widget.ts` and the Markdown parser.
  - `indicators/widget-view.ts`: `axisKindOf` is an exhaustive switch (lines 127-144).
  - `indicators/widget-dialog.ts`: the text fields with a preview, the section choices and the duplicate flow.
  - `indicators/widget-grid.ts`: the menu items (lines 240-241), the header band and collapsing. `grid-layout.ts`: sizes and arranging. `dashboard-print.ts`.
  - `project-indicators.ts`: the index, the duplicate flow and the widget counts. `dashboard-filters-dialog.ts`: `columnChoices` counts.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** a DbUp script widens `ck_dashboard_widgets_chart_type` (0025 if the P1 changes land first). No stored config or version is rewritten.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: text widgets, sections, arranging, duplicating and the limits.
  - `docs/mcp.md`, and `docs/operations.md` for the two limits.
  - `README.md` (the widget kinds) and `docs/tamiza-brief.md` (roadmap).

**Compatibility.** Nothing in production changes until someone adds a text widget or uses the new actions:
- No title is rewritten and no widget moves, and a dashboard without headers shows no index.
- Teatro keeps its 44 widgets, its 50-widget limit and its title prefixes, and headers added later do not use up that limit.
- Versions, restores, dashboard duplicates and scheduled emails of dashboards without text widgets are exactly as today.
- MCP clients that never send `text` see the same tools and results, except that `get_dashboard` lists text widgets with their kind. The instructions tell clients to skip kinds they do not know.

A rollback cannot undo the widened CHECK. An older release refuses to save, query or email a text widget, and its UI cannot draw one, as with box plots. The design says what each older surface does with one, and whether text widgets must be removed before rolling back.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, against the main specs as they are then, after the P1 changes are archived. These decisions are to be taken with the user at that time:
- The Markdown subset, the 2,000-character limit, and whether links are allowed.
- How many text widgets a dashboard holds (30 suggested).
- How a section's anchor appears in the address, whether the index prints, and whether a collapsed section is kept in the address.
- Whether every header can collapse or only those marked `collapsible`.
- The rules of **Arrange section**: stat tiles in fours and charts in pairs, and what happens to a widget wider than 6 columns (heat map 5b7a is 9, the 2.5 column by comuna is 12).
- In the email: stat tiles first, as today, or each section's tiles under its heading.
- Duplicating through MCP: a new `duplicate_widget` tool with an optional `groupBy`, or `create_widget` with the config `get_dashboard` returns, as today.
- Where a copy goes: next to the original, or at the end of its section.
