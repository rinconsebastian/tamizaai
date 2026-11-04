# Proposal

## Why

An AI client built most of the Duitama study through MCP. It wrote 46 of the study's 64 indicators, and the study's 9 dashboards hold about 190 widgets. Three problems made that work slower and less safe than it should be.

- **Test results get cut.** `test_script` and `get_test` return every step of a test: the flattened sample, the saved script 1 and the draft. Each step lists every table with its full column list and question labels, whatever `previewRows` asks for (`McpTestView.cs:73-79`; `previewRows` only limits preview rows, `McpTestView.cs:96-99`). On a wide form such as the Duitama survey's, those column lists alone fill the 60,000-character budget. The result is cut before the client reaches the indicator's own table and dashboard view. The cut note then tells the client to ask for fewer preview rows (`McpPipelineTools.cs:50,66`), which does not help.
- **Widget data gets cut without saying so.** `get_widget_data` reads every widget of a dashboard into one result, one widget after another, with no pages and no summary (`McpDashboardTools.cs:194-201`). Teatro has 44 widgets. When a result is too long, the longest lists (rows, cells, boxes) lose their last items (`McpResultBudget.cs:46-53`). Only a note at the top of the result names the cut lists, at most six of them. The widget's own entry never says its data is partial, so a client can describe a chart from half of its bars. This program makes widget data larger: add-small-sample-warnings adds `n`, `numerator` and `denominator` to every row and cell.
- **The guidance is behind the widgets.** `get_authoring_guide` covers scripts only (`McpGuide.cs:21-26`). Widget guidance lives in `Instructions.md` and in the tool descriptions. In a widget's config, only `display` has schema descriptions. The catalog test exempts `config` from that check (`McpToolCatalogTests.cs:62`), and the `create_widget` description lists the config keys without `basemap` and `ignoredDashboardFilters`. Production shows how much a client follows this guidance. The rule "`stacked`, `percent` and `likert` need a measure (counts), not a ratio" (`Instructions.md:34`) is why 7 count indicators duplicate about 20 questions. The other workarounds came the same way: numbered labels ("1. Sí"), "Todos los vehículos" rows, separate "Sin dato" stat cards, and n typed into 19 titles. This program removes those workarounds. A client will keep making them until the guide shows the new way.

## What Changes

- **Compact tests.** `test_script` and `get_test` gain `compact` (optional, `false` by default). With `compact: true`:
  - the draft step comes whole: its tables with their columns and up to `previewRows` rows, its `ctx.show` values, its error and log, and, for an indicator, the dashboard view with its warnings;
  - each step before it (flatten, saved script 1) gives only its status, duration, row counts, whether it was reused, and the names and row counts of its tables, without columns or rows;
  - it works for every kind of script and at both access levels.
  - When a full test result is cut, its note says to ask again with `compact: true`.
- **Widget data in pages.** `get_widget_data` gains three optional arguments:
  - `offset` (0 by default) and `limit` (1 to 20) choose a page of the dashboard's widgets, in the order `get_dashboard` lists them. The result gives `total` and, while widgets are left, `nextOffset`;
  - `summary: true` gives, for each widget, its id, title, chart type, indicator, status and notes, and the size of its data (how many rows, series, cells or boxes). A stat tile also gives its value. No rows are sent;
  - with none of them, the tool reads every widget, as it does today.
- **Honest cuts.** When the budget cuts a widget's rows, cells or boxes, that widget's own entry says so: `partial` = `{ kept, omitted }`. The note at the top says which widget, or which page, to read next. The rule that cuts the longest list first does not change.
- **A widget authoring guide.** `get_authoring_guide` gains the kind `widget`. It returns a guide to building dashboards, kept in its own embedded file next to `Instructions.md`, apart from the script assistant's rules. The guide covers:
  - which kind of widget answers which question (today's "Choosing a widget", moved and expanded);
  - every config and display setting, with its limits, its default and the kinds that take it, including the settings this program adds: the column metadata `label`, `order`, `last`, `stripPrefix`, `base`, `missing` and `categories`; the widget settings `labels`, `subtitle`, `footnote`, `caption`, `unit`, `referenceLines`, `interpolation`, `dataLabels`, `order`, `highlight`, `minN`, `showN`, `missingFooter`, `statistic`, `secondary`, `decimals` and `compact`; and the dashboard settings `source`, `minN` and `showN`;
  - the study's presentation rules, each with the setting that meets it: a short title with the question in `subtitle`, the base n in the footer, "Sin dato" kept apart, natural or declared order, colors by meaning, small bases dimmed;
  - the traps seen in production: an `in` filter over the categories of one base (23.8 % instead of 95.2 %), an `overall` line over the categories of one question, "Todos" rows that already act as totals, numbered labels that filters and kept colors match on, and colors kept by value that apply to every widget of the dashboard that has that column.
  - `Instructions.md` keeps a short "Choosing a widget" and tells clients to read the guide before they build a dashboard. A test keeps the guide well within the budget.
- **Every setting described.** Every field of a widget's config gets a schema description: `WidgetConfigInput`, `WidgetDisplay` and the records inside them. The catalog test stops exempting `config` and checks the properties inside it. The `create_widget` and `update_widget` descriptions name every key. A test checks that the guide names every chart type and every config key, so a new field cannot ship without its guidance.

Not in this change:
- New tools. The 34 tools stay. There is still no export, report, batch layout or duplicate tool.
- A new default size of results (60,000 characters; the operator can set 10,000 to 140,000), or `compact` as the default.
- How `formatted` numbers read (en-US today; add-locale-formatting owns that), and the outliers' values of a box plot, which the spec keeps out of MCP.
- Each program change's own MCP text. Every change ships the descriptions and `docs/mcp.md` lines of its own fields. This change adds the guide and the tests that keep that text complete.

Depends on:
- Nothing, for compact tests, pages, summaries and honest cuts.
- For the widget guide and the description tests: the program changes archived before this one. These are the 8 P1 changes (add-indicator-column-metadata, add-widget-context, add-chart-annotations, add-declared-category-order, add-category-highlight, add-small-sample-warnings, improve-stat-cards, add-locale-formatting) and the P2 and P3 changes ahead of it in the program's order. A change that is not archived yet adds its own part of the guide when it lands. add-shared-script-module comes after this change and updates the script guide itself.

## Capabilities

### New Capabilities

None. The MCP tools belong to `mcp-server`.

### Modified Capabilities

- `mcp-server`:
  - "Tools and their roles": the row for `get_authoring_guide` also names the widget guide. This is a minimal edit;
  - new requirements: "Compact test results through MCP", "Widget data in pages through MCP", "Widget data summary through MCP", "Partial widget data through MCP", "Widget authoring guide through MCP" and "Widget settings described through MCP";
  - "Tests through MCP", "Indicator queries and widget data" (owned by add-small-sample-warnings), "Size of results" and "Guidance for clients" do not change. Their rules still hold when a call gives none of the new arguments.

## Impact

- **api** (`tamiza-api`):
  - `Mcp/McpPipelineTools.cs`: `compact` on `test_script` and `get_test`, and their cut hint.
  - `Mcp/McpTestView.cs`: the compact view of a test.
  - `Mcp/McpDashboardTools.cs`: `offset`, `limit` and `summary` on `get_widget_data`, its hint, and help texts and descriptions that name every setting.
  - `Mcp/McpResultBudget.cs`: the `partial` mark on a widget whose lists were cut.
  - `Mcp/McpScriptTools.cs` and `Mcp/McpGuide.cs`: the kind `widget` of `get_authoring_guide`. The script tools keep refusing that kind.
  - A new `Mcp/WidgetGuide.md`, embedded in `Tamiza.Api.csproj`, and a shorter "Choosing a widget" in `Mcp/Instructions.md`.
  - `Indicators/IndicatorModels.cs` and `Indicators/Palettes.cs`: schema descriptions on every config field.
  - Tests: `McpToolCatalogTests`, `McpGuideTests`, `McpPipelineToolsTests`, `McpDashboardToolsTests`, `McpResultBudgetTests` and `IndicatorDocsTests`. `system.md` and the templates do not change, so `AssistantPrompts.Version` stays as it is.
- **ui:** none.
- **analytics:** none. The worker already stores what a compact view leaves out.
- **Database:** none. No migration.
- **MCP:** three tools gain arguments, `get_authoring_guide` gains a kind, and the widget tools' descriptions get longer. Clients see the new arguments when they list the tools again.
- **Docs:** `docs/mcp.md` (the tool table, the limits paragraph and a short section on reading large results) and `docs/tamiza-brief.md` (the roadmap).

**Compatibility.** Nothing stored changes. Widgets, dashboards, versions, scheduled emails and exports read and look as they do today, and no query changes. An MCP client that sends none of the new arguments gets today's results: every step of a test with its columns, in the shape existing clients and tests read; every widget of the dashboard; and the same 60,000-character budget. Such a client can see two small differences: the `partial` field on a cut widget, and a correct hint in the cut note. A client can ignore both. `get_widget_data` still applies the dashboard filters' defaults. It still skips, without an error, a filter whose column the widget's indicator lacks, and the median cards Escuelas cef7 and Comercio aea1 and 5901 rely on that. It still reports the skip in "Not narrowed by the filters". The 2 widgets with `ignoredDashboardFilters` read as before. The tool list keeps its 34 names. After a rollback, the tool list no longer shows the new arguments or the widget guide kind.

## Next step

When this change is next, write design.md, tasks.md and the delta specs with /opsx:update, against the main specs as they are then. Take these decisions with the user at that time:
- `compact`: the argument's name, and whether it should become the default later.
- `get_widget_data`: pages, a summary or both; the page size; and whether a page counts widgets or characters.
- Cuts: the shape of `partial`, and whether each widget gets an equal share of the budget instead of the longest list being cut first.
- The widget guide: a kind of `get_authoring_guide` (recommended, since it adds no tool), a new tool, or a longer `Instructions.md`.
- Whether `query_indicator` should take dashboard filter values, as the REST query does, so a client can check a widget under the dashboard's filters before saving it.
- Whether 60,000 characters still fits the clients' own output limits.
- Which program changes are archived by then, so the guide covers exactly those.
