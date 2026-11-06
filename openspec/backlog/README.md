# Backlog of set-aside changes

These nine proposals come from the widget and dashboard program of the Duitama market study (`docs/tamiza-brief.md`, item 7l). On 2026-11-05 they were checked against the production dashboards: the 190 widgets of the 9 Duitama dashboards, read through MCP, and the Teatro and Cruces dashboards seen in the browser. They were then taken out of `openspec/changes/` so that only the changes that matter to the people who read the dashboards stay active. Each folder is kept as it was: a `proposal.md` only, with no design, specs or tasks.

`openspec list` does not show this folder. To bring a change back, move its folder back with `git mv openspec/backlog/<name> openspec/changes/<name>`, then rebase it on the current main specs with `/opsx:update` before writing its design and tasks. The proposals cite code and requirements as they stood on 2026-11-04.

Some active changes still name these ones in their "Not in this change" lists (for example `add-small-sample-warnings` and `improve-stat-cards`). Those lines only say where the work would go. They do not make anything depend on these changes.

## Deferred

These could come back when a project needs them.

| Change | Why it waits | Bring it back when |
|---|---|---|
| `add-color-by-and-custom-palettes` | It would help 3 production widgets, the stacked bars that color reasons by `barrera_tipo`, and a client's own palette. `add-category-highlight` covers setting one value apart and the grey for "No sabe". | A client asks for its own colors, or charts colored by another column become common. |
| `add-authoring-guards` | It warns authors and changes nothing a reader sees. The most frequent traps are handled elsewhere: the multiple-answer trap in `improve-stat-cards`, and no-data values in `add-small-sample-warnings`. | Authors, or AI clients, keep publishing misleading widgets after the active changes ship. |
| `add-weighting` | Every Duitama figure is unweighted ("Sin ponderar" in 44 indicator descriptions), and no current project has expansion factors. | A project with a stratified or quota sample needs weighted figures. |
| `improve-histogram-bins` | No production widget is a histogram. The 11 charts built on hand-made bins work, sorted by `orden`. | Histograms come into use, or a table of more than 5,000 rows feeds one: today the engine cuts it at 5,000 rows in no set order. |
| `improve-mcp-ergonomics` | It helps the people who build dashboards through an AI client, not the readers. If it comes back, a small version is enough: say in each widget's result when its data was cut, and update the widget guide after each change ships. | Results that are cut without notice cause wrong dashboards again. |
| `improve-widget-export` | The images of a fixed 16 cm size are only needed for a Word report. The n columns in the workbook follow `add-small-sample-warnings`. | A study has to deliver a Word report with figures exported from Tamiza. |

## Discarded

| Change | Why |
|---|---|
| `add-filter-exclusions` | Most of it is covered by the no-data values of `add-small-sample-warnings`: an indicator declares "Sin dato" or "No sabe", and a widget leaves them out and says what they held. That replaces the 45 widgets with hand-written `in` lists and the 7 "Sin dato" stat tiles. A filter that leaves out values for the whole dashboard is not worth a change of its own. |
| `add-reference-lines-from-indicators` | About 5 widgets point to a figure in another widget, and their footnotes already do that job. The line would need a numeric price axis that line charts do not have. |
| `add-shared-script-module` | The six demographic columns that about 44 scripts copy (`comuna`, `estrato`, `grupo_edad`, `sexo`, `hogar_menores`, `hogar_vehiculo`) can be built once in script 1, with no product change. The brief already lists a shared helper module across indicator scripts as deferred. |
