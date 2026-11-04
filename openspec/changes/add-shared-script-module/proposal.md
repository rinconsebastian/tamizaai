# Proposal

## Why

Indicator scripts cannot share code. Each step runs in its own process started with `python -I`, and the script runs in a fresh namespace (`launcher.py:109-123`, `contract.py:21-40`). The script contract says that script 2 and the indicators "cannot import from each other" (`system.md:43`). A shared helper module was left out when indicators were split into their own scripts, and the roadmap still defers it (`docs/tamiza-brief.md:331`).

The Duitama study shows what that costs. Its `households()` function keeps the complete interviews and maps each census block to its comuna through the reference table `manzanas_poligonos` (a block with no match becomes "Sin dato"). It then builds `estrato`, `grupo_edad` ("18 a 29" … "60 y más"), `sexo`, `hogar_menores` and `hogar_vehiculo`. That is about 20 lines plus 12 of support code (a `yes_no` helper and five constants), copied word for word. It appears in 10 of the 12 scripts that were read, and 44 of the 46 indicators written through MCP publish exactly its six columns, so it is likely repeated about 44 times. Other helpers are copied as well: `block()`, which puts every unit in the denominator of every category (teatro_hoy, teatro_intencion, comercio_gastos); `price_bins()`, which builds the 21 bins of $5.000; `ordered()`, which numbers categories; and `people_interested()`.

Copies drift apart. The copy in centro_tarifa_hogar already adds `household_id`. Changing one age band or one label means editing and saving about 44 scripts, each as a new version, and any copy that is missed publishes other values. Dashboard filters and colors kept by value match a column by its name and a value by its exact text. One stale copy would make a Teatro filter such as "Grupo de edad" narrow some widgets and leave others empty.

Script 1 could add the six columns to `main`, and it stays the right place for data that every indicator needs. But script 1 returns tables, not functions. Each indicator calls `block()`, `price_bins()` and `ordered()` on its own question, so they cannot live in script 1. The docs also promise more than the code delivers: `docs/scripts.md:219` says common preparation can go in script 1 "or script 2, whose tables every indicator receives", but indicators receive only script 1's tables (`pipeline.py:389-397`).

## What Changes

- **A shared module per project.** This is an optional script of a new kind, `module`. The Scripts section shows it as "Shared module", and a project has at most one.
  - It has no contract function. It defines functions and constants, and indicator scripts import it as `shared` (`from shared import households, block`).
  - It reads nothing on its own. A function gets only what the caller passes it, for example `households(cleaned, ctx)`.
  - It has the same limits and checks as a script: at most 200 KB, the same libraries, the same import warnings in the editor, and the same refusals through MCP.
- **Versioned like every script.** Saving the module creates a version with its author, its time and its origin (the editor or MCP). Restoring and removing it also create versions. Viewers cannot read it. Saving it runs nothing; the next run uses it.
- **In a run.**
  - A run takes the module's current version when it starts, as it does for script 1. The run records that version (`runs.module_version_id`), and the run details show it.
  - Each indicator step writes the module next to its script, and the runner loads it under the name `shared` before the script runs. `-I` keeps the step folder off the import path, so no other file can be imported from it.
  - A traceback names `shared.py` and the line that failed.
  - When the module fails to load, every indicator that imports it fails with that error and keeps its previous table. The other indicators run. This is today's rule for a failing indicator.
- **In a test.**
  - A test of an indicator draft uses the saved module.
  - To test a module draft, the analyst picks one saved indicator. The test runs that indicator with the draft module, so a change to `households()` can be checked on teatro_hoy before it is saved.
  - The reused flattened sample and script 1 result work as they do today.
- **In the editor.**
  - The Scripts section lists the module with its versions, and the indicators whose current version imports it. That list comes from scanning their import lines.
  - The check of unavailable imports stops marking `import shared` and `from shared import …` in an indicator. It still marks them in script 1 and script 2.
- **MCP and the assistant.**
  - `list_scripts`, `get_script`, `list_script_versions`, `save_script`, `remove_script`, `restore_script_version` and `test_script` take the kind `module`, with no name and no indicator metadata. The code scan (`mcp.unsafeCode`) applies to the module too.
  - The script contract in `system.md` is shared by `get_authoring_guide` and the script assistant. It changes to say what goes in the module and what goes in script 1, and `AssistantPrompts.Version` is bumped.
  - The assistant sends the module's code with a request about an indicator.
- **Local kit.** A sample download holds the module as `shared.py`. The SDK's `check()` loads it the way the server does, so an indicator that imports it can be checked offline.
- **Migration.** A DbUp migration takes the next free number when this change lands (0025 or later; the P1 changes take 0021 to 0024). It adds `module` to `ck_script_versions_kind`, `ck_script_tests_kind` and `ck_assistant_calls_kind`. It adds `module_version_id` to `runs`, `script_tests` and `sample_downloads`, each a foreign key to `script_versions`. Existing rows do not change.
- **Docs.** `docs/scripts.md:219` stops saying that indicators receive script 2's tables.

Not in this change:
- Several modules, packages, or a module shared between projects.
- Script 1 or script 2 importing the module. That would make the module part of the clean step and of its test reuse. It is a decision for later, and the recommendation is not now.
- Moving the existing copies into the module. Nothing is rewritten. Each author moves a script when they choose, and its test shows whether it still publishes the same table.
- Pinning a module version to an indicator version. A restored indicator uses the current module.
- Completion of the module's names in the editor.
- A stronger sandbox. The module runs with the same isolation as a script, and that isolation is not a security boundary.

Depends on:
- None of the widget changes. This change sits outside widgets.
- It changes files that add-indicator-column-metadata also changes (`script_versions` and `save_script`), and files that improve-mcp-ergonomics also changes (the MCP script tools and the guide). So it lands after both. A module version holds no indicator metadata and no column metadata.

## Capabilities

### New Capabilities

None. Scripts, runs and tests belong to `pipeline`.

### Modified Capabilities

- `pipeline`:
  - "Runs and the run log": a run also records the module version it used;
  - "Sample test of one script": a module draft can be tested with one saved indicator, and a test records the module version it used;
  - new requirements: "Shared module", "Shared module in a run", "Shared module that fails to load" and "Shared module in a test".
- `script-editor`:
  - "Check of unavailable imports": an indicator's import of the project's module is not marked;
  - "Script templates", only if the module does not get the two templates that the requirement asks of every kind;
  - a new requirement: "Shared module in the Scripts section".
- `script-assistant`: "What is sent to the model" also includes the project's module for a request about an indicator.
- `mcp-server`: a new requirement, "Shared module through MCP".
- `local-dev-kit`: new requirements, "Shared module in the sample download" and "Shared module checked locally".

## Impact

- **analytics** (`tamiza-analytics`):
  - `tamiza/launcher.py` writes the module into the step folder, and `tamiza/runner.py` and `tamiza_sdk/contract.py` load it before the script.
  - `tamiza/pipeline.py` takes the module's version for the run and passes it to each indicator step.
  - `tamiza/sample_runs.py` tests with the saved module or a draft of it.
  - `tamiza/kit.py`, `tamiza/sample_downloads.py`, `tamiza/kit_files`, and `tamiza_sdk/bundle.py`, `checking.py` and `local.py` handle the download and the local check.
  - Tests, including `tests/integration/test_docs_examples.py`, which runs the code blocks of `docs/scripts.md`.
- **api** (`tamiza-api`):
  - `Data/Pipeline.cs` (`ScriptKind.Module` and the module version of runs and tests) and `Data/TamizaDbContext.cs`.
  - `Pipeline/ScriptEndpoints.cs` (save, restore and remove, with no metadata), `ScriptTestEndpoints.cs`, `RunEndpoints.cs` and `SampleDownloadEndpoints.cs`.
  - `Assistant/ContextAssembler.cs`, `Assistant/TestFacts.cs` (frames of `shared.py` kept when errors are masked), and `Assistant/Prompts/system.md` with `AssistantPrompts.Version`.
  - `Mcp/McpScriptTools.cs` (`KindHelp`, `ParseKind`), `Mcp/McpPipelineTools.cs`, `Mcp/McpTestView.cs`, `Mcp/UnsafeCode.cs` and `Mcp/Instructions.md`.
  - OpenAPI, and tests, including `McpGuideTests`, `PromptKitTests`, `McpScriptToolsTests` and `UnsafeCodeTests`.
- **ui** (`tamiza-ui`):
  - `core/projects/pipeline.service.ts`: the kind.
  - `pages/projects/project-scripts.ts`: the module and the indicators that import it.
  - `script-editor/script-analysis.ts` and `python-modules.json`: the import check.
  - `script-test-panel.ts`: choosing an indicator for a module test.
  - `run-details-dialog.ts`: the module version of a run.
  - `script-templates/` if the module has templates, the `en.json` and `es.json` strings, and the specs.
- **Database:** the migration above. `TamizaDbContext` mirrors the constraints and the columns.
- **MCP:** the kind `module` in the script and test tools, and the new contract text in `get_authoring_guide`.
- **Docs:** `docs/scripts.md` (the module, and the fixed line 219), `docs/mcp.md` (the kind) and `docs/tamiza-brief.md` (the deferral at line 331 is reversed).

**Compatibility.** A project without a module runs, tests, downloads samples and publishes exactly as it does today, and its runs record no module version. Existing indicator scripts import nothing from the module and are not changed. The roughly 44 `households()` copies keep working, and nothing moves them. Every published table stays the same, and so does every widget, dashboard, version and scheduled email. No query or widget setting changes: heat maps with precomputed %, the "Todos los vehículos" totals, the 50-widget limit and silently skipped dashboard filters all behave as before. MCP clients see the new kind value `module` in `list_scripts` only in a project that has a module. The contract text that `get_authoring_guide` and the assistant give changes for every client, with a new `AssistantPrompts.Version`. A rollback needs care. Older code parses a script's kind strictly, so it fails to read rows of the kind `module` (versions, tests and assistant records). The design gives a down script that removes them first. Older code ignores the new `module_version_id` columns.

## Next step

When this change is next, write design.md, tasks.md and the delta specs with /opsx:update, against the main specs as they are then. Take these decisions with the user at that time:
- The names: the kind `module` and the import name `shared`, and whether a project has one module or several named modules.
- Who may import it: only indicators (recommended), script 2 as well, or script 1 as well. Script 1 would make the clean step and its test reuse depend on the module.
- How to test a module draft: with one saved indicator (recommended), with an indicator draft, or with a check that only loads it.
- Removing the module while indicators import it: refuse it, or allow it and let those indicators fail at the next run.
- Whether saving the module offers to run the project. Saving a script does not do that today.
- Templates: the two templates that "Script templates" asks of every kind, or an exemption for the module.
- What the assistant sends: the whole module, or only the names an indicator imports, within its context size.
- The local kit: add the file within download format 1, or bump the format.
- The rollback's down script for rows of the kind `module`.
