# Proposal

## Why

In the Duitama study, most authoring mistakes were found by reading the numbers after the dashboard was built. Tamiza refuses a widget whose settings cannot work, such as a bar chart without a group column, but it says nothing about one that is valid and misleading:
- **Long tables mix questions.** teatro_hoy holds 9 variables. Its description, and that of cruces_intencion, has to tell authors "Filtrar siempre por variable y, en las tarjetas, por una sola categoría". A widget that forgets the `variable` filter adds nine questions together without a word.
- **Ranges sort as text.** A bar chart without an order puts "$120.000" before "$80.000" and "10 a 19" before "2 a 9". The 21 price bins of Teatro e636 read right only because they are sorted by `orden`.
- **Codes for no answer.** 98 ("no sabe / no recuerda") is the smallest value of E7, G2 and G7 in the clean data. Scripts move it to "Sin dato" variables by hand, and nothing would warn if one did not.
- **Filters that do not apply.** The median cards on escuelas_pago_mediana and comercio_gastos_resumen are never narrowed by the dashboard's filters, because their tables lack those columns. An author who did not mean that learns it only when a member filters.
- **Repeated titles.** Nothing says when two widgets of a dashboard have the same title.

Today the only warnings come when an indicator is tested (no rows, no `value` column, a percent above 1, and four more), never when a widget is saved. MCP clients get none either, and they wrote 46 of the project's 64 indicators.

## What Changes

- **Warnings when a widget is saved.** A warning never stops a save. The API runs the checks in one place, after the existing validation, and returns `warnings`. Each warning has a `code`, a message, the field it is about and what to do:
  - **`sentinelValues`**: the widget's measure, numerator or denominator, over the rows its own filters keep, has 98, 99 or -99 as its smallest or largest value, or holds a value its column metadata declares as `missing`. Fractions of a `percent` indicator are not checked.
  - **`alphabeticalRanges`**: a bar or column widget with no order is grouped by a text column whose values start with a number or an amount ("$0", "$10.000", "$120.000"; "10 a 19"; "Más de $100.000"), and their alphabetical order differs from their natural order. The message names the first pair out of place. It suggests ordering by a column such as `orden`, or declaring the order (add-declared-category-order), which silences it.
  - **`duplicateTitle`**: another widget of the same dashboard has the same title, ignoring case and spaces at either end.
  - **`filtersNotNarrowing`**: the widget's table has no column with a dashboard filter's name and kind, and the widget does not ignore that filter. The message names the filters and suggests marking them as ignored if that is meant. To avoid noise, only filters that narrow most of the dashboard's other widgets count (to be confirmed). Escuelas' "Persona" filter, for example, narrows only person-level indicators.
  - **`variableNotFiltered`**: the table has a text column `variable` with more than one value in the widget's rows, and the widget neither filters it to one value nor groups or series by it.
- **Required columns** (`required: true` in the indicator's column metadata, the "mandatory filter"):
  - it is entered in the "Column names" section of the indicator's save form and through MCP `save_script` `columns`; leaving it out of a save keeps it, as for the map's other properties;
  - a widget over the indicator must filter that column to one value (`equals`, or `in` with one value) or group or series by it. Saving one that does not is refused, with an error on `config.filters` that names the column. Examples: `variable` on teatro_hoy and on cruces_intencion;
  - the rule applies when a widget is added or its query changes. Existing widgets are still drawn, moved, restored and copied with their dashboard, and the form shows the error when one of them is edited;
  - a `required` column replaces `variableNotFiltered` for that indicator.
- **In the widget form.** The warnings are listed above **Save** while the author edits, each with its fix and a link to its field. They never disable saving. Today the form's only alert says that the indicator is not published.
- **Through MCP:**
  - `create_widget` and `update_widget` return the same `warnings`, and a refused `required` comes back like any refused field;
  - the tool descriptions and `Mcp/Instructions.md` tell clients to read the warnings, then fix them or tell the person.
- **Text.** The UI translates warnings by `code` (`en.json` and `es.json`), and the API and MCP give English messages, as for the API's other messages.

Not in this change:
- Warnings for widgets already saved, before someone edits them: no dashboard-wide check and no badge on tiles.
- Making any warning blocking, or refusing repeated titles.
- New warnings when an indicator is tested; its dashboard view keeps its seven warnings.
- The guard on `in` over several declared `categories` (the 23.8 % against 95.2 % case), which belongs to improve-stat-cards. If improve-stat-cards leaves its form hint to this change, that hint joins the same list.
- Fixing anything for the author: no filter, order or title is ever changed automatically.

## Depends on

- `add-indicator-column-metadata`: the column metadata map that `required` joins, its "Column names" section, and `save_script` keeping `columns` when they are omitted.
- `add-small-sample-warnings`: the `missing` values used by `sentinelValues`. Without them, only 98, 99 and -99 are checked.
- `add-declared-category-order`: an indicator's `order` or a widget's `display.order` silences `alphabeticalRanges`. Without them, only the widget's `sort` does.
- `improve-stat-cards`: its `in` hint, if it is not made there.
- `add-dashboard-sections`: text widgets are never checked, and a duplicated widget is checked like a new one.

## Capabilities

### New Capabilities

- `widget-authoring-warnings`: the warnings a widget gets when it is saved, each check and when it fires, and how the widget form shows them.

### Modified Capabilities

- `pipeline`: new requirement "Required columns of an indicator": the `required` property of the column metadata, saved and restored with the script version.
- `dashboard-indicators`: new requirement "Widgets over an indicator with required columns": the refusal and when it applies.
- `mcp-server`: new requirement "Authoring warnings through MCP", covering the warnings in `create_widget` and `update_widget` results and `required` in `save_script` `columns`.

No existing requirement changes: the warnings add to "Same rules as the UI" and contradict none of its rules.

## Impact

- **api** (`tamiza-api`):
  - New `Indicators/WidgetGuards.cs`: the checks. They run after `CheckConfig` and `CheckConfigAgainstTableAsync` (`IndicatorDashboard.cs:93-104`), with one small read-only statement over the widget's rows: smallest and largest values, the values of `variable`, and the group values.
  - `Indicators/DashboardEndpoints.cs`: `CreateWidgetAsync` (line 507) and `UpdateWidgetAsync` (line 583) return `warnings` only when there are some, and apply the `required` rule.
  - `Indicators/IndicatorModels.cs`: the `Widget` response gains an optional `warnings`. `Indicators/IndicatorValidation.cs`: the `required` rule.
  - `Pipeline/ScriptEndpoints.cs`: the column metadata validation gains `required`.
  - `Mcp/McpDashboardTools.cs`: `WithVersions` (lines 498-508) carries `warnings`, and the descriptions. `Mcp/McpScriptTools.cs`: the `columns` help. `Mcp/Instructions.md`.
  - OpenAPI and tests: unit and integration tests, the pinned texts (`McpToolCatalogTests`, `McpGuideTests`, `IndicatorDocsTests`), and `AssistantPrompts.Version` if the authoring guide changes.
- **ui** (`tamiza-ui`):
  - `core/projects/indicators.service.ts`: `Widget.warnings`, and the check call the form makes while editing.
  - `indicators/widget-dialog.ts`: the list of warnings (the only alert there today is the unpublished one, line 205) and the `required` error.
  - `pages/projects/indicator-metadata-fields.ts` and `add-indicator-dialog.ts`: the "Required filter" choice in the "Column names" section.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** none. `required` lives in `script_versions.columns` (migration 0021 of add-indicator-column-metadata), and warnings are never stored.
- **Analytics:** none.
- **Docs:**
  - `docs/indicators-dashboard.md`: a section on the warnings when saving a widget, and required columns under "What an indicator script must provide".
  - `docs/scripts.md`: the metadata table. `docs/mcp.md`.

**Compatibility.** Warnings are worked out at save time and never stored, so nothing in production changes:
- No production widget, version or scheduled email changes, and a save without warnings answers exactly as today: `warnings` is left out when empty.
- MCP clients that ignore the new field lose nothing.
- `required` is opt-in for each indicator, and no production indicator declares it. Declaring it never hides, breaks or rewrites an existing widget.
- The median cards that rely on silently skipped dashboard filters, and the two that ignore `estrato`, keep working. The warning only suggests marking those filters as ignored, which shows the same "Not filtered by" note as today.
- The checks add one statement to each widget save, within the existing query slots and timeout.

On rollback, an older API returns no warnings, does not enforce `required`, and drops it on the next save of the indicator's metadata, as add-indicator-column-metadata does with unknown properties of the map.

## Next step

`design.md`, `tasks.md` and the delta specs are written with `/opsx:update` when this change is next, against the main specs as they are then, after the P1 changes and add-filter-exclusions are archived. These decisions are to be taken with the user at that time:
- How the form gets warnings while editing: a check endpoint for the dashboard (suggested), a flag on the preview query, or only after saving.
- The sentinel rule: 98, 99 and -99 only as the smallest or largest value (suggested), or anywhere in the rows. Whether other codes (999, -999) are checked.
- The rule for `filtersNotNarrowing`: every filter, or only filters that narrow most of the other widgets.
- `variableNotFiltered`: found by the column name `variable`, or only through `required`.
- What satisfies `required`: one value or a group or series column (suggested), or any `in` list.
- Whether `required` is checked when a widget is moved or a version restored (suggested: no).
- Whether a later change adds a "Check this dashboard" action, so the 190 existing widgets get the warnings without being edited.
