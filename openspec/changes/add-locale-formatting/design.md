# Design

## Context

See proposal.md for the motivation and the specs for the behavior. This is the last P1 change of the program (P1-8). It is written against `main` as it is after `add-indicator-column-metadata` and `add-widget-context` were implemented and archived (2026-11-04), and rebased with `/opsx:update` on the P1 changes still to come, which are implemented first (`add-chart-annotations`, `add-declared-category-order`, `add-category-highlight`, `add-small-sample-warnings` and `improve-stat-cards`). It needs three of the earlier changes: `add-chart-annotations` (the data labels that an abbreviation shortens), `add-widget-context` (archived: the widget's own unit, merged before the formatters by `withWidgetUnit` and `MetaOf`, and the widget workbook request that carries the saved config) and `add-indicator-column-metadata` (archived: the production-shapes fixture and migration 0021). Two more add numbers that this change writes in the project's format when they are there: `add-small-sample-warnings` (the n figures, written by its `figureFormatter` and `ReportFormat.Figure`, and the "Sin dato" line) and `improve-stat-cards` (the tile's "Without filters" line and its statistics). Code is cited by function and constant, not by line, since the lines move. The pieces it works on, as they are on `main`:

- **The UI writes numbers in the browser's locale.**
  - `app.config.ts` picks one `LOCALE_ID` before the first render: the browser entry that chose the interface language (`core/i18n/interface-language.ts`), or `en-US`.
  - Every chart injects it for numbers: `axis-chart.ts` (the shares of `seriesMarks`, and `compact` and `whole`, the numbers of a histogram's axis), `pie-widget.ts` (the shares of its table detail, accessible name and legend, and the `pct` pipe of its template), `likert-chart.ts` (`percent`), `crosstab-widget.ts` (the percent of `crossTableModel`, which `axis-chart.ts` also calls for a series shown as a table), `boxplot-chart.ts` (`whole`), `map-widget.ts` (the tooltip context), `widget-view.ts` (`formatter` and `boxFormatter`), and the script editor's preview `script-editor/indicator-view.ts`. The stat tile, heat map, pyramid, series plots and legends take the widget's `ValueFormatter` as an input, so they follow `widget-view.ts` and need no change of their own.
  - `map-tooltip.ts` (`tooltipValue`) writes a tooltip column's numbers and its dates with that one locale.
  - Dates, times, durations and the counts in messages use the same `LOCALE_ID` through the shared pipes (`shared/ui/format-pipes.ts`).
  - What `add-widget-context` added around a widget (subtitle, footnote, own caption, the dashboard's source line, the description popover) and `add-indicator-column-metadata` (column names), the dashboard-source dialog and the version history carry no widget numbers: their words are as authored and their dates are the member's. The widget's own unit is the one thing of them that comes into the formatters.
- **One formatter per indicator.** `valueFormatter(indicator, locale)` (`indicators/format.ts`) fixes the digits by format: `count` 0, `decimal` and `currency` 2, `percent` 0 to 2. Only axis ticks are short: compact notation (used by `tick` for `count`, `decimal` and `currency`) and a percent with one decimal at most. `plain` writes a histogram's range ends with up to two decimals. `boxFormat` turns a `count` into a `decimal` for a box plot (`boxIndicator`). `withWidgetUnit(indicator, config)` (`add-widget-context`) returns the indicator with the widget's own unit, trimmed, when it has one, and `widget-view.ts` builds `unitOf`, then `formatter` and `boxFormatter`, from it. A unit of `%` on a percent indicator is not repeated after the number, whoever's unit it is: `valueFormatter` and its twin `ReportFormat.Value` both drop it, and `ReportDataBuilder.ValueColumn` leaves it out of a header.
- **The stat tile** (`stat-widget.ts`) writes `format()(rows[0].value)` as its `value` and hands the same text to its image through `registerExportParts` (`ExportParts.stat`, drawn by `composeImage` in `widget-export/image-layout.ts`). `improve-stat-cards` is implemented before this change: it gives the value a size container and a `title` with the whole value, adds the statistics and the secondary lines, and extends `ExportParts.stat`. D6 builds on that markup.
- **The widget form is opened outside the page's injector.** `openDialog` in `project-indicators.ts` opens `WidgetDialog` through the CDK `Dialog` with only `data` (`WidgetDialogData` in `widget-dialog.ts`, which already carries `timeZone`, a `dashboards` signal and the dashboard's `colors`). `ProjectContext`, provided by `project-page.ts`, is not visible inside it. The form rebuilds the display in `displayDraft()`, keeps a saved default with the `setColor` rule and maps error keys in `fieldFor`. `displayFor` and `savedConfig` (`widget-view.ts`) decide what a kind saves (`displayFor` already keeps the labels and the five texts of `add-widget-context` on every kind); `queryFor` leaves `display` out.
- **Settings > General** (`project-overview.ts`): an admin edits the name and the time zone in one form (saved together by `saveSettings`); other members see the time zone as a value.
- **The API writes numbers with .NET cultures.** `ReportFormat` (`Reports/ReportFormat.cs`) takes a `CultureInfo` and the project's time zone. `Value` uses `N0`, `N2` and its own `PercentText`, which never puts a space before `%`; `CurrencyText` writes a currency code before the number, `COP 1,234.50`. Its comment accepts "small differences from the browser's Intl".
  - A scheduled email builds it in `ReportDataBuilder.BuildAsync`, from the culture stored with its settings, which `ReportText.Resolve` and `For` limit to Spanish and English cultures; the project is read there for its name and time zone.
  - A widget workbook builds it in `WidgetExportEndpoints.ExportAsync` from the member's locale sent with the request, and reads the project's name and time zone there too. `WidgetWorkbook` writes the Summary's line of the number of rows as text, with the culture's `N0`.
  - MCP keeps three static `en-US` instances (`English` in `McpDashboardTools`, in `SeriesPayload` and in `BoxPayload`).
  - Excel formats: `ReportDataBuilder.ExcelFormat` gives `#,##0`, `0.00%` or `#,##0.00`; `ValueColumn` and `BoxplotTable` (with `BoxFormat`) use it. `MetaOf` builds a widget's `IndicatorMeta` with the widget's own unit over the indicator's, also for an indicator that is gone. Histogram ranges are text, `Plain–Plain` (`HistogramTable`).
- **Indicator metadata** is stored per script version (`ScriptVersion` in `Data/Pipeline.cs`, with the `Columns` text of `add-indicator-column-metadata`), sent as `SaveIndicatorRequest(Code, Label, Description, Unit, Format, Columns)` (`Pipeline/PipelineModels.cs`), checked by `ValidateMetadata` (which builds a `Metadata`, with `KeepColumns` for columns left out), written by `AddVersionAsync` (which copies the columns of the version it numbers after when asked, inside its retry loop), copied by `RestoreCoreAsync` and read through `ToDetails` and `CurrentIndicatorsAsync` (`Pipeline/ScriptEndpoints.cs`). The page's save always sends the label, unit and format, so a save stores what it is sent. `IndicatorEntry` (`Indicators/IndicatorModels.cs`, built in `IndicatorEndpoints`) is what dashboards and `list_indicators` read. MCP `save_script` builds the same request (`columns` left out keeps the current ones); `list_scripts` reads `McpScriptSummary` (`Mcp/McpScripts.cs`).
- **Project settings.** `Project.TimeZone` (`Data/Project.cs`), mirrored with its CHECKs in `TamizaDbContext`. The PATCH (`ProjectEndpoints.UpdateAsync`, admins only) treats a null field as unchanged and an empty `enumeratorField` as "clear". `ToDetails` gives the time zone to every member.
- **Stored widget configs.** `WidgetDisplay` (`Indicators/IndicatorModels.cs`) now ends `Color`, `Ramp`, `Orientation`, `Labels`, `Subtitle`, `Footnote`, `Caption`, `Unit`, `ShowDescription`, and the UI mirrors it in `core/projects/indicators.service.ts`; its members are `WhenWritingNull` with a `Description`, and its `Normalize()` returns null when nothing is set. `CheckDisplay` (`IndicatorValidation.cs`) refuses a setting on a kind that does not take it. `WidgetDisplayTests` pins the stored text of configs without a display. MCP's `DisplayHelp` (whose first sentence is pinned), `ContextHelp` and `ChartTypeChangeHelp` (`McpDashboardTools.cs`) name each display field; the earlier changes add `AnnotationsHelp` and `StatHelp` beside them.
- **MCP results drop nulls** (`Mcp/McpResultBudget.cs`, `WhenWritingNull`), so a field that is null is simply absent.
- **The developer machine's culture uses a decimal comma.** The box plot's verification found integration tests that wrote `0,5`. Nothing new may depend on the current culture.
- **What browsers write.** Checked with Node 26's ICU (the UI tests run on it under jsdom), and to be checked in Chrome (task 11.4). Spaces shown here are U+00A0, a no-break space.

  | Format | 12345.6, 1 decimal | 4500 | 0.4685, percent, 1 decimal | 60514, `COP`, 0 decimals | 1234567, compact |
  |---|---|---|---|---|---|
  | `es-CO` | `12.345,6` | `4.500` | `46,9%` | `$ 60.514` | `1,2 M` |
  | `es-MX` | `12,345.6` | `4,500` | `46.9%` | `COP 60,514` | `1.2 M` |
  | `es-ES` | `12.345,6` | `4500` | `46,9 %` | `60.514 COP` | `1,2 M` |
  | `es-419` | `12,345.6` | `4,500` | `46.9%` | `COP 60,514` | `1.2 M` |
  | `en-US` | `12,345.6` | `4,500` | `46.9%` | `COP 60,514` | `1.2M` |
  | `en-GB` | `12,345.6` | `4,500` | `46.9%` | `COP 60,514` | `1.2m` |
  | `pt-BR` | `12.345,6` | `4.500` | `46,9%` | `COP 60.514` | `1,2 mi` |

  Tamiza writes what the browsers write in every column but one: in a Spanish project format, a percentage has a no-break space before `%` (D2, decided with the user on 2026-11-04).

## Goals / Non-Goals

**Goals:**
- One regional format per project, chosen by an admin, that every surface uses for the numbers of the project's dashboards.
- The UI and the API write the same text for the same number in each of the seven formats, proved by one shared list of cases.
- Without a format, without decimals and without abbreviation, every widget, email, workbook and MCP result writes exactly what it writes today, and every stored config, widget query body and query is byte-identical. The production-shapes fixture proves it.
- Decimals for an indicator and for a widget, and abbreviations for a tile and for data labels, as display settings that never query again.

**Non-Goals:**
- Choosing the interface language, or the format of dates and times.
- A format per dashboard or per member, or formats beyond the seven.
- The performance and clean data dashboards, and the CSV and XLSX exports of a table.
- Abbreviations anywhere the server writes (emails, workbooks, MCP).

## Decisions

### D1. The project's regional format (`projects.number_locale`)

- **Stored.** `Project.NumberLocale` (`string?`), column `number_locale character varying(16) NULL` with `CHECK (number_locale IS NULL OR number_locale IN ('es-CO', 'es-MX', 'es-ES', 'es-419', 'en-US', 'en-GB', 'pt-BR'))`, mirrored in `TamizaDbContext` (`ck_projects_number_locale`). The list is `NumberLocales.All` (D3), in that order.
- **Set** by a project `admin` through the existing PATCH: `UpdateProjectRequest` gains `NumberLocale` (`string? = null`).
  - null leaves it unchanged, as for every field of the PATCH;
  - `""` clears it, as `enumeratorField` does;
  - a value is trimmed and matched exactly. Anything else: key `numberLocale`, "'fr-FR' is not a regional format Tamiza offers. Choose es-CO, es-MX, es-ES, es-419, en-US, en-GB or pt-BR, or none."
- **Read.** `ProjectDetails` gains `NumberLocale` (`string?`), given to every member, viewers included, because every member's dashboards use it. It is not one of the values only Settings shows.
- **Not versioned.** It is a project setting: setting it makes no dashboard version, and a version viewed or restored is drawn in the project's current format (`dashboard-versions` "Number formats and dashboard versions").

*Alternatives:*
- *A format per dashboard.* Rejected: one study is read by one client, and the email, the workbooks and MCP would each need to know which dashboard a number belongs to.
- *A preference per member.* Rejected: the problem is members who read the same dashboard differently.
- *Any BCP 47 tag.* Rejected: the API's table (D3) must know each format it writes, and the CHECK keeps the stored value honest.

### D2. What each format writes

- Each format writes what browsers write for it (the Context table): its decimal and group separators, `es-ES`'s grouping from five integer digits only (`4500`, `12.345`), and each currency where the browser puts it. The one exception is the percent sign of the Spanish formats, below. The no-break spaces are part of the text; the specs write them as spaces.
- **`46,9 %` in every Spanish project format** (decided with the user on 2026-11-04). When the project's format is `es-CO`, `es-MX`, `es-ES` or `es-419`, every percentage has a no-break space (U+00A0) before `%`: `46,9 %` in `es-CO` and `es-ES`, `46.9 %` in `es-MX` and `es-419`. It is what the client's specification asks for and the Spanish norm. Browsers write the space only for `es-ES`, so the UI adds it to `Intl`'s text for the other three, never twice, and the API's table has it (D3).
  - **Every twin and every surface.** The UI formatter, `ReportFormat` and MCP's `formatted` write it, so it reaches the screen, the widget form's and the script editor's previews, images, print, the email's body, plain text and workbook texts, the widget workbook's texts and MCP. It covers every percentage in the project's numbers: a percent indicator's values and axis ticks, shares (pies, 100% layouts, Likert widgets, cross tables), tooltip columns written as percentages, and the percentages the earlier P1 changes add (reference lines, the "Sin dato" line, "Without filters").
  - **The other formats** write no space, as their browsers do: `46.9%` in `en-US` and `en-GB`, `46,9%` in `pt-BR`.
  - **Without a project format nothing changes.** Each browser keeps its own text (`46,9%` in an `es-CO` browser, `46,9 %` in an `es-ES` one), and the scheduled email keeps today's `PercentText` (`45,5%`, so `dashboard-reports` "Percentage in Spanish of Colombia" stays true).

  | 0.4685 with 1 decimal | In a project format (UI, email, workbooks, MCP) | Browser, with no project format | Email, with no project format |
  |---|---|---|---|
  | `es-CO` | `46,9 %` | `46,9%` | `46,9%` |
  | `es-MX`, `es-419` | `46.9 %` | `46.9%` | `46.9%` |
  | `es-ES` | `46,9 %` | `46,9 %` | `46,9%` |
  | `en-US`, `en-GB` | `46.9%` | `46.9%` | `46.9%` |
  | `pt-BR` | `46,9%` | `46,9%` | none: an email is saved in Spanish or English (`ReportText.Resolve`) |

  The study's figure is `46,9 % de los hogares` once `teatro_hoy` sets 1 decimal in a project set to `es-CO`.
- **`$60.514` or `$ 60.514`.** A `currency` indicator whose unit is the code `COP` is written by the browser as `$ 60.514`, with a space. A unit that is a symbol sits against the number, as today (`withUnit` of the `currency` format in `valueFormatter`), so the unit `$` gives `$60.514`. The study's money is `count` with the unit `pesos` today, which writes `60.514 pesos`; an author who wants `$60.514` sets the format `currency`, the unit `$` and 0 decimals. The docs say so.
- **Rounding** is half away from zero on the number's decimal form, as `Intl` rounds by default (`halfExpand`): 0.4685 at 1 decimal is `46,9 %` in `es-CO`, 1.005 at 2 decimals is `1.01`.

*Alternatives:*
- *Follow the browsers: a space before `%` only in `es-ES`* (this design before 2026-11-04). Rejected with the user: a project set to `es-CO` would write `46,9%`, against the client's specification and the Spanish norm.
- *A space in every Spanish text, with or without a project format.* Rejected: members and emails of projects that set no format would see their numbers change without anyone opting in (an `es-CO` browser's `46,9%`, the pinned `45,5%`), against the program's rule that nothing changes until someone opts in.
- *The space only for `es-CO`.* Rejected: the norm is Spanish, not Colombian, and one rule for the four Spanish formats is simpler to state, to test and to explain in Settings.
- *Write a `COP` code as `$` with no space.* Rejected: the API would differ from the browser, which writes `$ 60.514`; a `$` unit already gives `$60.514`.

### D3. Two writers, one text (`Reports/NumberLocales.cs` and `Intl`)

- **UI.** `valueFormatter` and every chart keep using `Intl.NumberFormat`, given the number style of D4. The UI adds one rule of its own, D2's space: `percentFormat(style, options)` (`core/i18n/number-locale.ts`) writes a percentage with `Intl`'s `formatToParts` and, when the style asks for the space and no literal space comes before the `percentSign` part, puts a no-break space there, so `es-ES`'s own space is never doubled. Every writer of the project's percentages uses it: `valueFormatter`'s percent values and ticks, the shares of `axis-chart.ts` (`seriesMarks`), `crosstab-widget.ts` (`crossTableModel`), `likert-chart.ts` and `pie-widget.ts` (whose `pct` pipe and shared `percentText` give way to a share formatter of the component), and Settings' examples (D9). The app's shared `percentText` and `pct` pipe (`shared/ui/formats.ts`, `format-pipes.ts`) keep the browser's text for the pages this change does not touch. Apart from that rule and the Settings choice, nothing in the UI knows the seven formats.
- **API.** A new `NumberLocales` holds, for each of the seven formats, a `NumberLocale` record: decimal separator, group separator, minimum grouping digits (2 for `es-ES`, else 1), whether `%` is set apart by a no-break space (`SpacedPercent`, true for the four Spanish formats, D2), the currency pattern, and the symbols of six currencies, taken from the browsers' data:

  | Format | Before `%` | Pattern | COP | USD | EUR | MXN | BRL | GBP |
  |---|---|---|---|---|---|---|---|---|
  | `es-CO` | no-break space | symbol, space, number | `$` | `US$` | `EUR` | `MXN` | `BRL` | `GBP` |
  | `es-MX`, `es-419` | no-break space | symbol against number | `COP` | `USD` | `EUR` | `$` (`es-MX`), `MXN` (`es-419`) | `BRL` | `GBP` |
  | `es-ES` | no-break space | number, space, symbol | `COP` | `US$` | `€` | `MXN` | `BRL` | `GBP` |
  | `en-US` | nothing | symbol against number | `COP` | `$` | `€` | `MX$` | `R$` | `£` |
  | `en-GB` | nothing | symbol against number | `COP` | `US$` | `€` | `MX$` | `R$` | `£` |
  | `pt-BR` | nothing | symbol, space, number | `COP` | `US$` | `€` | `MX$` | `R$` | `£` |

  - "Against the number" takes a no-break space when the symbol ends in a letter (`COP 60,514`, `US$60,514`), as browsers do. A minus sign goes first (`-$ 60.514`, `-60.514 COP`). Any other currency code is written as its code with the format's pattern, as browsers write most codes (`PEN 60,514`); a few have a symbol of their own in some browsers (`CAD` is `CA$60,514` in `en-US`), which is a known difference (Risks).
  - The digits are produced without any culture: the value is converted to `decimal` (a value outside its range falls back to `double` with invariant formatting), rounded with `MidpointRounding.AwayFromZero`, then written with the record's separators and grouping. This gives the rounding of D2, which `double` formatting would not (1.005 is stored as 1.00499…).
- **`ReportFormat`** gains an optional `NumberLocale`. With one, `Value`, `Plain`, `Whole`, `Share`, `TooltipNumber` and `add-small-sample-warnings`' `Figure` write with the table, so a percentage or a share in a Spanish format reads `45,5 %`. Without one, they run exactly today's `CultureInfo` code, so an email or a workbook of a project with no format keeps its text byte for byte, `45,5%` and `COP 1,234.50` included. `Value` keeps its `%` rule with a table too: a unit of `%` on a percent indicator is not repeated. `Day`, `Moment`, `DateText` and `TimestampText` always use the culture: dates stay the saver's or the member's.
- **The shared list** `tamiza-ui/src/app/pages/projects/indicators/testing/number-formats.json` holds cases `{ locale, format, unit, decimals, box, value, text }` and UI-only cases with `compact: true`. Every case is in a project format, so its `text` is what browsers write with D2's space added in the Spanish formats. `format.spec.ts` runs every case through `valueFormatter` with the number style the project page gives that format (D4); a new `NumberLocalesTests.cs` runs every case without `compact` through `ReportFormat`, reading the file from the repository as `SeriesTableTests` does. The cases cover each format with a count, a decimal, a percent with and without decimals (`46,9 %` in `es-CO`, `46.9 %` in `es-MX` and `es-419`, `46,9 %` with one space in `es-ES`, `46.9%` in `en-US` and `en-GB`, `46,9%` in `pt-BR`), a negative percentage, a percentage with a word unit, the six currencies and a code outside the table that every browser writes as its code (`PEN`, not `CAD`, whose `en-US` text `CA$` the table does not write), a symbol and a word unit, 4- and 5-digit numbers, halves, negatives, zero, and a box plot over a count. A unit test also checks that `NumberLocales.All` equals the UI's `numberLocales` list, as `PalettesTests` reads the UI's copies, and that `SpacedPercent` is true exactly for the formats whose language is `es`, which is the rule the UI's `numberStyleOf` applies (D4). The texts of a project with no format are checked by the production-shapes goldens instead (D12), where no space is added.

*Alternatives:*
- *.NET cultures for the seven formats.* Rejected: .NET has no minimum grouping, so `N0` groups `4500` in `es-ES`; `PercentText` has no space; a culture knows only its own currency's symbol; and the server's ICU data and version need not match the browsers'.
- *Write the texts in the browser and send them to the API.* Rejected: the scheduler builds emails with no browser.
- *Use the table for emails of projects without a format.* Rejected: it would change the text of existing emails saved in any Spanish culture (`45,5%` would become `45,5 %`), and the saver's cultures outside the seven (`es-AR`, `en-AU`) have no entry. Absent must mean today.

### D4. The number locale in the UI (`NUMBER_LOCALE`)

- **Token.** `core/i18n/number-locale.ts` exports:
  - `NumberStyle`, `{ locale: string; spacedPercent: boolean }`: the locale `Intl` writes with, and whether a percentage takes D2's space;
  - `numberStyleOf(projectFormat, browserLocale)`: the project's format, with `spacedPercent` true when its language is `es`, else the browser's locale with `spacedPercent` false, so a browser alone never gets the space;
  - `percentFormat` (D3);
  - `NUMBER_LOCALE`, an `InjectionToken<Signal<NumberStyle>>` whose root default is the browser's `LOCALE_ID` with `spacedPercent` false. It is a signal, so a change saved in Settings redraws open charts.
- **`valueFormatter(indicator, style)`** takes a `NumberStyle` or, as today, a locale string, read as a browser locale with no space. Callers that pass a string, the specs and the production-shapes goldens included, keep their text.
- **Providers.**
  - `ProjectContext` (`project-context.ts`) injects `LOCALE_ID` once and gains `numberLocale = computed(() => numberStyleOf(this.project()?.numberLocale ?? null, this.browserLocale))`, and `project-page.ts` provides `NUMBER_LOCALE` from it. The dashboards, the version viewer, printing and the script editor are under that page.
  - `WidgetDialogData` gains `numberLocale: Signal<NumberStyle>`, passed by `openDialog` in `project-indicators.ts`, and `WidgetDialog` provides `NUMBER_LOCALE` from it, so the preview draws in the project's format.
- **Users.** Every injection of `LOCALE_ID` listed in Context that writes a widget's numbers reads `NUMBER_LOCALE` instead, in a `computed`, passing its `locale` to `Intl` and the whole style to `valueFormatter` and `percentFormat`: field initializers such as `compact` and `whole` in `axis-chart.ts` and `whole` in `boxplot-chart.ts` become computeds, and the pie's `pct` pipe becomes a share formatter of the component. The numbers the earlier P1 changes add to a widget follow the same token: the n of `add-small-sample-warnings` (footer, tooltip line, small-base note, "Sin dato" footer; its `figureFormatter(locale)` is given the number locale by the charts that call `base-figures.ts`), the values of `add-chart-annotations`' reference lines, which already go through the widget's formatter, and `improve-stat-cards`' "Without filters" line, written by the tile's formatter.
- **What stays the member's.** Dates and times (the tooltip's date columns, version times, "last send"), durations, file sizes and counts in messages ("22 more rows") keep `LOCALE_ID`. `map-tooltip.ts` takes both: `tooltipValue` gets the number style for numbers, so a column formatted as a percentage takes D2's space, and keeps `locale` for dates and timestamps, and `TooltipContext` (`map-widget.ts`) carries both.
- **Images and print** read the page's DOM and export parts, so they follow without a change of their own; tasks check them.

*Alternatives:*
- *An input threaded from `project-indicators.ts` through `widget-grid.ts` and `widget-view.ts` to each chart.* Rejected: twelve components, and every new chart would have to remember it; a token reaches the form's preview and the script editor too.
- *Provide a project `LOCALE_ID`.* Rejected: `LOCALE_ID` also drives dates and Angular's pipes, which must stay the member's, and it is fixed at start.
- *A token that holds only the locale, with the space decided from it.* Rejected: an `es-CO` browser and a project set to `es-CO` give the same locale, and only the second takes the space (D2).

### D5. Decimals (`script_versions.decimals`, `display.decimals`)

- **Indicator.** `ScriptVersion.Decimals` (`int?`), column `decimals integer NULL` with `CHECK (decimals IS NULL OR decimals BETWEEN 0 AND 4)` (`ck_script_versions_decimals`), a column of its own like `format`, not a member of the `columns` map of `add-indicator-column-metadata`: it is about the indicator's values, not one column, and a ratio has two.
  - `SaveIndicatorRequest` gains `Decimals` (`int? = null`), appended. `ValidateMetadata` refuses a value outside 0 to 4 with key `decimals`: "Choose 0 to 4 decimals, or leave them to the format." The page's save always sends it, so a save stores what it is sent, as for the unit.
  - `Metadata`, `AddVersionAsync`, `RestoreCoreAsync` (which copies it), `ToDetails` and `ScriptVersionDetails` (appended `int? Decimals`) carry it. A removal stores none. MCP alone can ask to keep the current decimals (D11).
  - `IndicatorEntry` gains `Decimals` (`int?`, appended, `WhenWritingNull`), so the overview of a project whose indicators set none is the same JSON.
- **Widget.** `WidgetDisplay.Decimals` (`int?`, `WhenWritingNull`, `Range(0, 4)`, `Description`), on every kind.
- **Precedence.** Widget, then indicator, then the format's digits. One helper per side merges it before the formatters, the same one that merges `add-widget-context`'s unit (its D4). UI: `withWidgetUnit` in `format.ts` (the name is kept so the rebase stays small) returns the indicator with `unit` replaced by the widget's trimmed own unit when it has one, as it does today, and `decimals` replaced by `display.decimals` when that is set; with neither it returns the indicator itself. `widget-view.ts` builds `unitOf`, `formatter` and `boxFormatter` (from `boxIndicator` of `unitOf`) from its result. API: `IndicatorMeta` (`ReportDataBuilder`) gains `Decimals`, and `MetaOf` takes `Display?.Decimals ?? indicator?.Decimals` where it takes `Display?.Unit ?? indicator?.Unit`, so a widget over a removed indicator keeps its own decimals as it keeps its own unit. MCP: `IndicatorInfo` gains `Decimals`, merged in `WidgetDataAsync` where it merges the unit. The `%` rule stays in the formatters, after the merge: a unit of `%`, the indicator's or the widget's, is not repeated after a percentage, with decimals or not, in every format (`15.70%`, `46,9 %`), and `ValueColumn` still leaves a `%` unit out of a header.
- **What takes them.** With `d` set, the formatter writes exactly `d` decimals (minimum and maximum `d`) for the value with its unit, in every format, so a percent keeps trailing zeros (`15.70%`). A stat tile with a statistic (`improve-stat-cards`) is a stat tile for this: its median of a count reads whole until the tile or the indicator sets decimals. `plain` takes them too, so a histogram's range ends have `d` decimals. Ticks (compact), shares (one decimal at most), counts of rows, values and outliers, and tooltip columns keep their digits.
- **Box plots.** `boxFormat(format, decimals)` / `BoxFormat(format, decimals)` turn a `count` into a `decimal` only when no decimals are set. So a box plot over a count keeps two decimals by default, as "Indicator formatting" still says, and `teatro_precio_hogar` with 0 decimals reads `40,000 pesos`. MCP's `BoxPayload` calls the same function.
- **Excel.** `ExcelFormat(format, decimals)`: `#,##0` followed by `.` and `d` zeros for `count`, `decimal` and `currency`, and `0` followed by `.`, `d` zeros and `%` for a percent; with no decimals, today's three formats. The box plot's columns use the box format.

*Alternatives:*
- *Decimals per format in the project.* Rejected: the study's indicators of one format need different digits (`46,9 %` beside a share in tenths of a percent).
- *A minimum and a maximum.* Rejected: one number covers the study and keeps a table's figures aligned.
- *Turn the box plot's count rule off for everyone.* Rejected: it reverses a documented rule for every existing box plot; setting 0 decimals is one click.

### D6. Abbreviations (`display.compact`)

- **Stored.** `WidgetDisplay.Compact` (`bool?`, `WhenWritingNull`, `Description`). Set to `true` or `false`, it is refused on any kind but `stat`, `bar`, `column`, `line` and `pie` (D7), as `color`, `orientation` and `add-chart-annotations`' `dataLabels` are refused on the kinds that do not take them (program rule 5). `false` is kept as given on the five kinds: the form stores it only for a widget that had `compact` saved (D8), and drops the field on a change to another kind (`displayFor`); MCP's chart-type help names it.
- **The text.** `ValueFormatter` gains `abbreviated(value)`:
  - a percent, or a value under 1,000 in size, is the full text;
  - a `count` or `decimal` is `Intl` compact notation with at most one decimal, then the unit as the full text places it;
  - a `currency` with a code is `Intl` compact currency (`$1,2 M` in `es-CO`); a symbol unit is placed against it and a word before it, as today.
  - At most one decimal, whatever the decimals: decimals say how exact a full figure is, and `$1,2 M` must stay readable when the indicator sets 0.
- **Where.** The stat tile's value and, through `add-chart-annotations`' data labels, each bar's, column's, point's and slice's label. The stat tile writes the short text `aria-hidden`, a visually hidden span with the full text, and the full text in `title`; data labels are already `aria-hidden` and their tooltips and the table carry the full value. `improve-stat-cards`, implemented before, already puts the whole value in the value's `title`: with the abbreviation the visible text is the short one and the `title` and the hidden span keep the full one. `StatWidget.value` stays the text the tile shows, so its export parts take the short text, and a second computed holds the full text. The image takes the tile's short text through its export parts, and printing uses the same DOM.
- **Nowhere on the server.** Emails, workbooks and MCP write full values, so the API has no twin of `abbreviated`.

*Alternatives:*
- *Abbreviate automatically when a tile is small.* Rejected: the same figure would read differently by tile size and print size, and existing tiles would change.
- *Abbreviate in emails.* Rejected: emails and workbooks are where exact figures are read.

### D7. The widget settings: storage, validation, twins

- **Stored.** Both members are appended to `WidgetDisplay` after those the earlier P1 changes add, so existing members keep their place in the JSON. `Normalize()` keeps `0` and `false` as given; `WidgetDisplay.Normalize()` returns null only when every member, these two included, is unset. A config without them serializes exactly as before (`WidgetDisplayTests`, the production-shapes fixture).
- **Validation** (`CheckDisplay`):

  | Rule | Key | Message |
  |---|---|---|
  | decimals outside 0 to 4 | `display.decimals` | "The decimals must be a whole number from 0 to 4." |
  | `compact` (`true` or `false`) on a kind other than stat, bar, column, line or pie (skipped when the chart type is null) | `display.compact` | "Only stat tiles and bar, column, line and pie charts abbreviate their numbers." |

- **Twins.** Neither field is in `queryFor` or `WidgetQuery.For`: changing them never queries again. Neither is a derived flag either: unlike `overall` (`add-chart-annotations` D4) and `unfiltered` (`improve-stat-cards`), which travel on the query request and never in a config, they are stored display settings that no request carries. `displayFor` keeps `decimals` on every kind and `compact` on the five kinds, so a change of kind in the form drops the abbreviation and keeps the decimals.
- **OpenAPI** describes both, and `ProjectDetails.numberLocale`, `UpdateProjectRequest.numberLocale`, `SaveIndicatorRequest.decimals`, `ScriptVersionDetails.decimals` and `IndicatorEntry.decimals`.

### D8. The widget form (`widget-dialog.ts`)

- **Decimals**, for every kind: a select after the existing display fields, "Indicator's default ({digits})" then 0 to 4. `{digits}` names what the widget writes without a choice: the indicator's decimals when set; else `2` for a box plot over a count, "up to 2" for a percent and for a histogram's ranges, `0` for a count and `2` otherwise. A `decimals` signal is seeded from `start.display?.decimals`; the default stores null; a number is stored as chosen.
- **Abbreviate large numbers**, a checkbox for the five kinds, with an example written in the project's format (`1,2 M`). For a bar, column, line or pie its hint says it shortens the data labels, and it is disabled while the data labels are off, keeping its value. `setCompact(on)` stores `true`, or `false` only when the widget had `compact` saved, as `setColor` does, so a form saved unchanged saves what it had.
- `displayDraft()` carries both, so a widget with no display field still saves no `display`. `fieldFor` maps `display.decimals` and `display.compact`. The preview's query key leaves `display` out, so both redraw without a request.
- Strings: `indicators.dialog.decimals.*` and `indicators.dialog.compact.*` in `en.json` and `es.json`.

### D9. The indicator's details and Settings > General

- **Indicator details** (`indicator-metadata-fields.ts`, shared by the add dialog and the editor): a **Decimals** select next to the format, "Format's default ({digits})" then 0 to 4, with `{digits}` `0`, `2`, "up to 2" or `2` by format. `IndicatorMetadata` gains `decimals: number | null`; the editor's read-only details write it. The script editor's preview (`indicator-view.ts`) builds its formatter with it, so a change redraws without a new test, as the format and the unit do.
- **Settings > General** (`project-overview.ts`): in the admin's settings form, a **Regional format** select after the time zone. "Each member's browser" first (value `""`), then the seven, each named in the interface language (`overview.numberLocales.es-CO`, "Spanish (Colombia)" / "Español (Colombia)") with an example written in that format as its dashboards will write it, by `Intl` and `percentFormat` with `numberStyleOf` of the option (D3, D4): `12.345,6 · 46,9 %` for Spanish (Colombia), `12.345,6 · 46,9%` for Portuguese (Brazil). Its hint (`overview.numberLocaleHint`) says that the project's dashboards, emails, exports and AI clients write numbers in it, and that dates and the interface language stay each member's. `saveSettings` sends it with the name and the time zone, and the API's error shows next to it. Analysts see the format, or "Each member's browser", as a value next to the time zone.

*Alternative:* name the formats with `Intl.DisplayNames`. Rejected: the names would change with the browser's data, and the strings would escape `translations.spec.ts`.

### D10. Emails and workbooks

- `ReportDataBuilder.BuildAsync` reads `NumberLocale` with the project's name and zone and builds `new ReportFormat(text.Culture, zone, NumberLocales.Find(numberLocale))`; `WidgetExportEndpoints.ExportAsync` does the same. So numbers follow the project's format, read when the email is sent, and the words, the dates and `ReportText.Format`'s counts in messages stay the saver's or the member's.
- In a Spanish project format, the percentages and shares written as text (the body, the plain text, the workbook's "Summary" and range texts) take D2's space, `45,5 %`, whatever the saver's culture. A cell holding a number keeps its Excel format (`0.0%`), and Excel draws its sign as the reader's settings say.
- `IndicatorMeta` gains `Decimals`, and `MetaOf` takes the widget's, else the indicator's. `Value`, `Plain`, `ValueColumn`, `BoxplotTable` and the histogram's ranges take them; shares, `Whole` and tooltip numbers do not.
- The lines the earlier P1 changes add to a widget's notes (`add-small-sample-warnings`' n and "Sin dato" lines, `improve-stat-cards`' `StatLines`) are written by the same `ReportFormat`, so they follow the project's format; a value in them takes the widget's decimals, and the n keeps `Figure`'s digits.
- The stat tile is written in full in the body, the plain text and the workbook's "Summary", whatever `display.compact` says.
- **The widget workbook** reads the widget's display from the saved config that `add-widget-context` made the page send (`exportWorkbook` in `project-indicators.ts` sends `savedConfig(widget.chartType, widget.config)`, and `displayFor` keeps `decimals` on every kind), so its data sheet and Summary take the widget's decimals with no change to the request. Its Summary's line of the number of rows, written with the culture's `N0` today (`WidgetWorkbook`), is written by `format.Whole`, so it follows the project's format like its other numbers written as text.
- Excel shows separators by the reader's settings, so a number cell is the same in every format; only text cells (ranges, the "Summary" texts) carry the project's format.

### D11. MCP

- **Formatter per call.** The three static `English` instances go. A tool that formats builds one `ReportFormat` per call from the project: the project's `NumberLocale` when set, else `en-US` as today. `Row`, `SeriesPayload.From`, `BoxPayload.From`, `add-chart-annotations`' reference lines, `add-small-sample-warnings`' `noData` and `improve-stat-cards`' `unfiltered` take it, so every `formatted` text of a result is in one format, with D2's space before `%` in a Spanish one (`46,85 % de los hogares`).
- **Decimals.** `IndicatorInfo` gains `Decimals`. `get_widget_data` merges the widget's decimals, as `add-widget-context` merges its unit there; `query_indicator` keeps the indicator's, as it keeps the indicator's unit, because it previews a query (`mcp-server` "Indicator queries and widget data"). `formatted` is never abbreviated.
- **`save_script` keep rule.** A client that does not know `decimals` must not wipe what a member set, so `decimals` left out keeps the current version's, and `null` clears them. The tool cannot tell an absent argument from a null one after binding, so `McpCallFilter.Create` records the argument names of the call in `McpCallScope.Arguments` (in memory for the call only; the call record still holds no arguments, `mcp-server` "Call records"). When `decimals` is not among them, `save_script` passes a keep flag to `SaveIndicatorCoreAsync`, and `AddVersionAsync` copies the decimals of the version it numbers the new one after, inside its retry loop, as `add-indicator-column-metadata` (its D3) copies `columns`; so a save that races another keeps the decimals of the version it follows. Its `Description`: "Optional, for an indicator: 0 to 4 decimals for its widgets' values. Left out, the current version's are kept; null clears them." The tool's description says it is kept like `columns`, unlike the label, unit and description.
- **Reads.** `get_project` adds `numberLocale` and each indicator's `decimals`; `list_indicators` gets `decimals` from `IndicatorEntry`; `McpScriptSummary` (`list_scripts`), `get_script` and the result of `save_script` add `decimals`. All are absent when null.
- **Widgets.** `WidgetDisplay.Decimals` and `Compact` get `Description`s in lower case and no enum (`McpToolCatalogTests.The_schemas_of_the_color_fields_carry_their_allowed_values_and_a_description`). A new constant, `NumbersHelp`, follows `ContextHelp` (and the constants the earlier changes add after it, `AnnotationsHelp` and `StatHelp`) in the descriptions of `create_widget` and `update_widget`; `DisplayHelp`'s pinned first sentence ("config.display = { color, ramp, orientation, labels } sets how the widget is drawn") does not change. It says: "config.display also takes decimals (0 to 4) on every chart type, and compact = true, which abbreviates a stat tile's value and the data labels of a bar, column, line or pie (1,2 M) and never the formatted texts of the tools; compact, true or false, is refused on the other kinds." `ChartTypeChangeHelp` adds `config.display.compact` to the settings a bare kind change is refused on, after what the earlier changes name there; the sentences pinned by `McpToolCatalogTests.Only_update_widget_warns_that_a_new_chart_type_alone_keeps_the_config_and_so_the_color_or_scale` and the two `IndicatorDocsTests` on `docs/mcp.md` change in the same task. The `create_widget` template's `display: { … }` gains `decimals, compact` after the keys the earlier changes add. `WidgetConfigInput.Display`'s description keeps its pinned words ("How the widget is drawn") and names the two settings.
- **Guidance.** `Mcp/Instructions.md`: one Rules bullet: "`formatted` texts are in the project's regional format (`get_project` `numberLocale`), else American English; read numbers from the raw values, never by parsing `formatted`." `Assistant/Prompts/system.md` does not change, so `AssistantPrompts.Version` stays at 5.
- **Budget.** The new fields are absent unless set; a widget with decimals adds nothing but different digits. The 60,000-character budget is unchanged.

*Alternatives:*
- *Replace the decimals on every `save_script`, as the label and unit are.* Rejected: a client that does not know the argument would wipe a member's choice on every save, as `add-indicator-column-metadata` found for `columns`.
- *A sentinel such as `-1` to clear.* Rejected: `null` says "none" in every other nullable setting of the tools.
- *`formatted` always in American English, with a second `formattedLocal`.* Rejected: it doubles every row's text in a budget that large dashboards already reach, and the program asks for the project's format.

### D12. The production-shapes fixture

- **Shapes added** to `production-shapes.json`: a stat tile with `display.decimals: 1`, a box plot with `display.decimals: 0`, an abbreviated stat tile, and a column chart with data labels and `compact`. Each is checked for its own round trip; the existing shapes are checked as before: byte-identical stored JSON, the same `WidgetQuery.For`, plan, `savedConfig`, `queryFor` and unchanged form save.
- **Formats added**: a `formats` section with invented indicators of the study's shapes (a percent with a word unit and a value with two decimals, the same percent given a widget's own unit through `withWidgetUnit`, a percent whose unit is `%` (not repeated), a percent with a trailing zero, a count of pesos with a fractional mean, a whole-number box median of a count, a decimal with no unit) and their texts as today's code writes them: UI `valueFormatter` in `en-US` and `es-CO`, `ReportFormat` with the `en-US` and `es-CO` cultures, and MCP's formatter. The goldens are written from the code before this change; after it, with no project format and no decimals, every text is the same, the UI's `es-CO` `46,85%` with no space included (D2: a browser alone never gets the space).

### D13. Documentation

- `docs/indicators-dashboard.md`:
  - "What a value means: format and unit": the project's regional format and what it changes, including the space before `%` in its four Spanish formats and not in a browser alone, the decimals (indicator and widget, what keeps its digits, the box plot rule), the table's examples in American English, and how to get `$60.514`;
  - a section `### Abbreviated numbers`;
  - the "Unit" section (a widget's unit and decimals together, and the `%` that is not repeated), "The fields in the widget form", the box plot's "Numbers" bullet, the "Images" bullet on words and numbers, the "Workbook" bullets (its numbers, the Summary's rows, "Words and formats follow your interface language and region") and "Printing a dashboard".
- `docs/operations.md`, "Email (optional)": the numbers of the body and the workbook follow the project's regional format when it has one, with the saver's words and dates.
- `docs/scripts.md`: a **Decimals** row in the metadata table, after **Format**, the sentence under it that says what a restore brings back, and the preview bullet ("Figures").
- `docs/mcp.md`: `formatted` and `numberLocale`, `save_script`'s `decimals` and its keep rule, `display.decimals` and `display.compact`, and the chart-type sentence.
- `docs/tamiza-brief.md` section 6 (data model): `projects.number_locale`, `script_versions.decimals`, `display.decimals` and `display.compact`.

## Spec ownership

- **MODIFIED, owned by this change** (program section 3): `dashboard-indicators` "Indicator formatting"; `interface-language` "Regional formats" and "Content shown as it was created"; `dashboard-reports` "Email language and regional format" and "Numbers in the workbook"; `widget-export` "Numbers in a widget workbook" and "Language of widget files". Each keeps every scenario and adds scenarios. One body is made more precise so that it stays true: `dashboard-reports` "Percentage in Spanish of Colombia" (`45,5%`) now says the project has no regional format, since a project set to `es-CO` writes `45,5 %` (D2).
- **Rebase on `add-widget-context` (done on 2026-11-04, after it was archived).** It modified "Indicator formatting" too (a widget's own unit, scenario "Widget's own unit"). This change's block was copied again from main's text and its clauses applied: main's paragraph with its five scenarios, and this change's own. The other six MODIFIED blocks were diffed against main on that date and differ from it only by this change's edits (neither archived change touched them).
- **Order with the other active changes.** No other active change modifies the seven blocks this change modifies, and this change's deltas copy none of the requirements the others modify (`improve-stat-cards` modifies "Stat tile", "Ratio of sums" and "Widget query over one indicator"), so no spec text has to be copied between them whatever the order. The order of implementation matters only in code, and it is fixed: this change is last, so it builds on the tile markup, the export parts, the help constants and the production-shapes count of the others.
- **Not modified, still true:**
  - `dashboard-indicators` "Widget unit" (`add-widget-context`): a widget's own unit still replaces its indicator's with decimals and abbreviations; "Indicator formatting" now says so, and its scenarios `Widget's own unit with decimals` and `Unit of % not repeated` pin it.
  - `dashboard-indicators` "Stat tile" (`improve-stat-cards`): an abbreviated value keeps the indicator's unit and gives the full value in its title.
  - `mcp-server` "Indicator queries and widget data" (`add-small-sample-warnings`): `formatted` is still "with the indicator's format and unit"; the project's format is in the added "Number formats through MCP".
  - `dashboard-reports` "Email body" ("Value formatted", `42%` in American English) and "Missing values in widget tables" (two decimals for a removed indicator), for widgets without decimals in projects without a format.
  - `widget-export` "Parts of a widget image" (`add-widget-context`): the tile's figure is still drawn from the screen's text.
  - `dashboard-reports` "Widget tables in a scheduled email" (`add-indicator-column-metadata`) and `dashboard-printing` "Charts on paper".
  - `dashboard-versions` "Dashboard versions", `custom-dashboards` "Duplicating a dashboard" and `dashboard-indicators` "Moving a widget to another dashboard", whose generic lists (`add-indicator-column-metadata`) cover display settings, so decimals and the abbreviation travel with a widget. Task 5.4 checks it.
- **ADDED names** start with "Regional format", "Project regional format", "Percentages in a Spanish regional format", "Decimals", "Indicator decimals", "Widget decimals", "Abbreviated", "Full values beside abbreviated", "Number format" or "Number formats", which no other change of the program uses.
- **Referred to by text only**, since they do not exist in the main specs yet: `add-chart-annotations`' "Data labels on bars, columns, lines and pies"; `add-small-sample-warnings`' n lines and `improve-stat-cards`' "Stat tile secondary lines" and "Stat tile value size", whose numbers follow the project's format through the formatters this change gives them (D4, D10, D11) and whose text this change does not touch.

## Production impact

From the survey of the Duitama project (program section 5):
- **Nothing changes until someone opts in.** The project has no regional format, no indicator sets decimals and no widget abbreviates. Every member keeps seeing their browser's format (`46.85% de los hogares` in an English browser), emails keep the saver's, and MCP keeps `46.85% de los hogares`, `60,514 pesos`, `9,000.00 pesos`, `4.41` and `15.7%`. No stored config gains a key, so no version is made. The fixture's `formats` goldens check it (D12).
- **Setting `es-CO`** changes every number of the 9 dashboards and about 190 widgets at once, for every member, in the emails and in MCP: `46,85 % de los hogares`, with a no-break space before `%` in the widgets of its 38 percent indicators and in every share (D2), `60.514 pesos`, `1.016.279 pesos`, `4,41`. That is the request, and only an admin can do it. Category labels written by the scripts (`$5.001 a $10.000`, "1. Sí") are data and stay as they are.
- **The six box plots over count indicators** (Teatro f21f and 3e1c, Centro a137, Comercio 944f and 3d99, parqueadero 8492) keep `9.000,00 pesos` until `teatro_precio_hogar`, `centro_tarifa_hogar` and the others set 0 decimals. Then the median reads `40.000 pesos` and the mean `60.514 pesos`, as the stat tile does.
- **Acceptance criterion 10.** `teatro_hoy` with 1 decimal reads `46,9 % de los hogares` in a project set to `es-CO`, as the study's specification writes it (D2). `$60.514` needs the indicator in `currency` with the unit `$` and 0 decimals; today's `count` with `pesos` reads `60.514 pesos` (D2).
- **Percent units with the wrong base** ("de los hogares" on about 20 widgets) are `add-widget-context`'s widget unit; decimals do not touch units.
- **MCP.** 46 indicators were written through `save_script`. A client that does not send `decimals` keeps what members set (D11). Clients that parse `formatted` would see commas once a format is set; the instructions tell them to read raw values.
- **The 67 stat tiles** keep their value text; the smaller figure of `improve-stat-cards` is unrelated to this change.

## Risks / Trade-offs

- **[Browsers differ from the fixture]** (another ICU version, Safari or Firefox) → The fixture is checked in jsdom (Node's ICU) and in Chrome on the real stack (task 11.4). A difference shows only on screen; the email and MCP follow the table. Formats and currencies beyond the table are out of scope.
- **[A currency code the table does not name]** → Written as its code with the format's pattern, as browsers write most codes (`PEN`); a few have a browser symbol (`CAD` is `CA$60,514` in an `en-US` browser and `CAD 60,514` in the email). The shared list checks only codes both sides agree on, the study has no `currency` indicator, and the docs list the six codes.
- **[Setting the format surprises members]** → It is an admin's choice with a hint that says what changes, and clearing it restores each member's browser at once.
- **[Clients that parse `formatted`]** → Raw values are always present, and the instructions say to read them.
- **[A page loaded before the deploy saves an indicator]** → It sends no `decimals`, which clears them; it could not have set them either. Reloading the page fixes it.
- **[The UI's space and the API's table drift apart]** → One shared list holds the Spanish percentages of the four formats, and a unit test checks that `SpacedPercent` is set exactly for the `es` formats, the rule of `numberStyleOf` (D3).
- **[A browser that starts writing its own space]** (a newer ICU for `es-CO`) → `percentFormat` adds the space only when `Intl` gives none before the sign, so it is never doubled; the `es-ES` cases check it.
- **[A Spanish member sees two styles]** → An `es-CO` member's browser writes `46,9%` on the pages this change leaves alone (the performance and clean data dashboards, Non-Goals) and `46,9 %` on the project's dashboards once a Spanish format is set. The docs say the space comes with the project's format.
- **[Decimals and the unit both merge before the formatters]** → One helper per side does both, with shared cases in `number-formats.json`.
- **[The current culture of the machine]** → `NumberLocales` never reads `CultureInfo.CurrentCulture`; its tests run under `es-CO` and invariant cultures.

## Migration Plan

1. **Deploy.** DbUp `0024_add_locale_formatting.sql` (after `0021` and `0022`, applied, and `0023` of `add-small-sample-warnings`) adds `projects.number_locale` and `script_versions.decimals`, both null, each with its CHECK, and rewrites no row (program rule 9). Deploy the `api` and the `ui` together. Until an admin sets a format, an analyst sets decimals or a widget abbreviates, everything reads as before.
2. **Rollback** (program section 2, rule 7):
   - An older `api` does not map the two columns, so it ignores them: numbers go back to each member's browser, the saver's culture and `en-US` in MCP. The values stay in the columns for a later release.
   - A script version saved by the older `api` stores no decimals, so an indicator saved during the rollback loses them in its new version.
   - An older `api` ignores `display.decimals` and `display.compact` when it reads a config, and drops them the next time that widget is saved. List them before rolling back: `SELECT d.name, w.title FROM tamiza.dashboard_widgets w JOIN tamiza.dashboards d ON d.id = w.dashboard_id WHERE w.config -> 'display' ? 'decimals' OR w.config -> 'display' ? 'compact';`.
   - An older `ui` ignores `numberLocale` and `decimals` in responses.
   - Dashboard versions keep the fields in their content; restoring one on a newer release brings them back.
   - DbUp never runs scripts backwards. The columns stay and are harmless.

## Open Questions

- Whether a later change should add formats (`es-AR`, `fr-FR`) or let a deployment configure the list. The table and the CHECK grow together.
- Whether the performance and clean data dashboards should follow the project's format too. They stay the member's here.
