# script-editor Specification

## Purpose

The editor of the Scripts section: what it shows beside the code (the tables a script receives, the `ctx` reference), how it helps while typing (completion, indentation, checks), the templates it starts from, and the cell actions that test part of a script.

## Requirements

### Requirement: Script inputs panel
Beside the editor, the Scripts section SHALL show the tables the selected script receives, as the last run published them: the flattened tables for script 1, the clean tables for script 2 and the indicator scripts. It SHALL give each table's name and row count, each column's name, type and question label when it has one, and when the tables were published. Every `analyst` and `admin` of the project SHALL see it. The API SHALL refuse a `viewer`'s request for the script inputs with 403.

#### Scenario: Inputs of script 1
- **WHEN** an analyst selects script 1 in a project whose last run flattened a form with a repeat group `members`
- **THEN** the panel lists the tables `main` and `members` with their row counts, and under `main` the column `has_water` with the type text and the label `Does the household have running water?`

#### Scenario: Inputs of an indicator
- **WHEN** script 1 drops the column `notes` from `main` and a run has published since, and an analyst selects an indicator
- **THEN** the panel lists the clean table `main` without `notes`, and says when it was published

#### Scenario: Script changed after the last run
- **WHEN** an analyst saves a script 1 that adds a column and no run has published since
- **THEN** the panel of an indicator does not list the new column, and shows the publish time of the tables it lists

#### Scenario: Nothing published yet
- **WHEN** an analyst opens the Scripts section of a project that has never run
- **THEN** the panel says that the tables appear after the first run, and the editor works without them

#### Scenario: Viewer sees the inputs
- **WHEN** a `viewer` of the project requests its script inputs from the API
- **THEN** the API responds 403 and returns no table, column, reference table or question

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its script inputs from the API
- **THEN** the API responds as for a project that does not exist

### Requirement: Reference tables and questions in the panel
The panel SHALL also list the project's reference tables with their columns and types, and the questions of the project's current form with their name, type and label. It SHALL filter tables, columns and questions by a text the user types, matched against names and labels.

#### Scenario: Reference table listed
- **WHEN** the project has the reference table `population` with the integer column `population_2026`
- **THEN** the panel lists `population` with that column and type, whichever script is selected

#### Scenario: Filter by label
- **WHEN** a user types `water` in the panel's filter
- **THEN** the panel shows the columns and questions whose name or label contains `water`, under their tables, and hides the rest

### Requirement: Names inserted from the panel
Activating a table, a column or a reference table in the panel with a click or with Enter SHALL insert its name as a quoted Python string at the editor's cursor, while the editor can be edited. While it cannot, because an older version is on view, activating a name SHALL copy it instead.

#### Scenario: Column inserted
- **WHEN** an analyst places the cursor after `main[` and activates the column `has_water` in the panel
- **THEN** the editor reads `main["has_water"` and the cursor follows the inserted text

#### Scenario: Viewer copies a name
- **WHEN** a `viewer` opens the address of the project's Scripts section
- **THEN** the project's performance dashboard opens instead, and no panel is shown to insert or copy a name from

#### Scenario: Older version copies a name
- **WHEN** an analyst views version 3 of a script whose current version is 5 and activates the column `has_water`
- **THEN** the name is copied to the clipboard and the code does not change

### Requirement: Completion of ctx
The editor SHALL offer completions of the members of `ctx`, `ctx.form` and `ctx.log` after a dot, and of question names as the first argument of `ctx.form.label` and `ctx.form.name`, as the user types or on Ctrl+Space. Each `ctx` completion SHALL show its signature and a one-line description.

#### Scenario: Members of ctx.form
- **WHEN** an analyst types `ctx.form.` in the editor
- **THEN** the editor offers `questions`, `choices`, `columns`, `label`, `name` and `column_label`, and `label` shows `label(question, code)` with its description

#### Scenario: Question name
- **WHEN** an analyst types `ctx.form.label("mun` in a project whose form has the question `municipality`
- **THEN** the editor offers `municipality` with its label

### Requirement: Completion of tables and columns
Inside a subscript of the contract function's first parameter, the editor SHALL offer the panel's input tables; inside `ctx.reference[...]`, the reference tables. Inside a subscript of a variable assigned from one table (`main = cleaned["main"]`), or of a reference table, it SHALL offer that table's columns, and elsewhere inside a subscript string the columns of every input table. Each column SHALL show its type and table.

#### Scenario: Table name
- **WHEN** an analyst types `cleaned["` in an indicator whose clean tables are `main` and `members`
- **THEN** the editor offers `main` and `members`

#### Scenario: Column of an assigned table
- **WHEN** the function assigns `members = cleaned["members"]` and the analyst types `members["`
- **THEN** the editor offers the columns of `members` only, each with its type

#### Scenario: Column of a reference table
- **WHEN** an analyst types `ctx.reference["population"]["`
- **THEN** the editor offers the columns of the reference table `population`

#### Scenario: Nothing published
- **WHEN** the project has never run and an analyst types `cleaned["`
- **THEN** the editor offers no table names and shows no error

### Requirement: Indentation with the keyboard
Tab SHALL indent the lines of a selection by four spaces, or insert spaces up to the next multiple of four columns when nothing is selected; Shift+Tab SHALL remove up to four spaces from the start of each selected line or the cursor's line. The editor SHALL NOT insert tab characters. Escape followed by Tab SHALL move the focus out of the editor, and the editor SHALL say so next to it.

#### Scenario: Indent a block
- **WHEN** an analyst selects three lines and presses Tab
- **THEN** each of the three lines starts with four more spaces

#### Scenario: Outdent
- **WHEN** the cursor is on a line indented by eight spaces and the analyst presses Shift+Tab
- **THEN** the line is indented by four spaces

#### Scenario: Leave the editor with the keyboard
- **WHEN** a keyboard user presses Escape and then Tab inside the editor
- **THEN** the focus moves to the next control after the editor and no text is inserted

### Requirement: Checks while editing
Within a second of the last edit, the editor SHALL mark problems it can find without running the script: a syntax error; no top-level function with the name of the script's contract; a contract function without two parameters; indentation mixing tabs and spaces; a member of `ctx`, `ctx.form` or `ctx.log` that does not exist; and a reference table the project does not have. Each problem SHALL show its message on its line and in a list.

#### Scenario: Wrong function name
- **WHEN** script 2 defines `prepare(cleaned, ctx)` and no `prepare_output`
- **THEN** the editor marks the script with a message that script 2 must define `prepare_output(cleaned, ctx)`

#### Scenario: Syntax error
- **WHEN** an analyst leaves a parenthesis open on line 7
- **THEN** the editor marks a syntax error at that point within a second

#### Scenario: Unknown ctx member
- **WHEN** an indicator script reads `ctx.refrence`
- **THEN** the editor marks it with a message that lists the members of `ctx`

#### Scenario: Unknown reference table
- **WHEN** a script reads `ctx.reference["populaton"]` and the project only has `population`
- **THEN** the editor marks it with a message that names `population` as available

### Requirement: Checks of input tables
The editor SHALL mark a read of the contract function's first parameter by a table name the published input tables do not have, unless the script assigns that table itself. It SHALL NOT mark tables while no inputs are published.

#### Scenario: Table the inputs do not have
- **WHEN** an indicator reads `cleaned["member"]` and the published clean tables are `main` and `members`
- **THEN** the editor marks `member` with a message that lists `main` and `members`

#### Scenario: Table the script adds
- **WHEN** script 1 assigns `raw["summary"] = ...` and later reads `raw["summary"]`
- **THEN** the editor does not mark the read

### Requirement: Checks never block
Problems found by the editor's checks SHALL NOT prevent saving or testing a script; the sample test and the run remain the authority on whether a script works.

#### Scenario: Save with a problem
- **WHEN** the editor marks a syntax error and the analyst saves
- **THEN** the script is saved as a new version

### Requirement: ctx reference
Hovering a member of `ctx`, `ctx.form` or `ctx.log` in the editor SHALL show its signature and description. The side panel SHALL have a reference tab that lists every member of `ctx`, `ctx.form`, `ctx.log` and `ctx.reference`, the contract of the selected script's kind, the rules for what it returns, cells and `ctx.show`, each with a short example.

#### Scenario: Hover
- **WHEN** an analyst hovers `column_label` in `ctx.form.column_label("main", "age")`
- **THEN** a tooltip shows `column_label(table, column)` and that it returns the question label of a flattened column, or nothing

#### Scenario: Reference of an indicator
- **WHEN** an analyst opens the reference tab while an indicator is selected
- **THEN** it shows the `compute(cleaned, ctx)` contract, that the result is one DataFrame with at least one numeric column, and the conventions for `value`, `numerator` and `denominator`

### Requirement: Script templates
For each script kind, the editor SHALL offer a menu of at least two templates, each with a name and a one-line description. Choosing one SHALL replace the editor's text as one edit that can be undone, after a confirmation when the editor has unsaved changes. Templates SHALL be organized in `# %%` cells, use `ctx.show`, and run unchanged in a sample test of any form, naming the columns to adapt in comments.

#### Scenario: Indicator templates
- **WHEN** an analyst opens the template menu of an indicator
- **THEN** it offers at least a count by group and a rate from `numerator` and `denominator`, each with its description

#### Scenario: Template over unsaved edits
- **WHEN** an analyst with unsaved edits chooses a template and cancels the confirmation
- **THEN** the editor keeps the edits

#### Scenario: Template tested unchanged
- **WHEN** an analyst inserts any template and tests it on a sample of a form that has only a main table
- **THEN** the test succeeds

### Requirement: Cells in the editor
A line whose text, after leading spaces, starts with `# %%` SHALL begin a cell that ends before the next such line or at the end of the script; the rest of the line names the cell. While the analyst can test, each cell SHALL show a "Run cell" action, also bound to Ctrl+Enter (Cmd+Enter on macOS) for the cell holding the cursor, that starts a sample test through that cell.

#### Scenario: Run the first cell
- **WHEN** an indicator has three cells and the analyst runs the first one
- **THEN** a test starts with the editor's text, the sample size field's value and the line of the second marker as its stop line

#### Scenario: Run the last cell
- **WHEN** the analyst runs the last cell
- **THEN** a whole test starts, as with "Test on sample"

#### Scenario: Keyboard
- **WHEN** the cursor is inside the second cell and the analyst presses Ctrl+Enter
- **THEN** a test through the second cell starts

#### Scenario: Viewer
- **WHEN** a `viewer` opens the address of the Scripts section of a project whose scripts have cells
- **THEN** the project's performance dashboard opens instead, and no "Run cell" action is shown

#### Scenario: Older version on view
- **WHEN** an analyst views an older version of a script with cells
- **THEN** no "Run cell" action is shown and Ctrl+Enter does nothing

#### Scenario: Result names the cell
- **WHEN** a test through the cell named `Rate` stops
- **THEN** the test result says that it ran through the cell `Rate` and stopped at its end

### Requirement: Check of unavailable imports
With the other checks while editing, the editor SHALL mark an import, at any level of the script, of a module that is neither one of the libraries the worker provides to scripts nor part of the Python standard library, with a warning that names the module and lists the available libraries. The mark follows the rule that checks never block.

#### Scenario: Library the worker does not have
- **WHEN** an indicator script contains `from sklearn.linear_model import LinearRegression`
- **THEN** the editor marks that line with a warning that `sklearn` is not available to scripts, listing the libraries that are

#### Scenario: Available libraries and the standard library
- **WHEN** a script imports `pandas as pd`, `numpy as np`, `geopandas`, `shapely.geometry`, `duckdb` and `from datetime import date`
- **THEN** the editor marks none of those imports

#### Scenario: Import inside the contract function
- **WHEN** an import of `requests` appears inside `compute`, not at the top of the script
- **THEN** the editor marks it the same way as a top-level import

#### Scenario: Saving with an import warning
- **WHEN** the editor marks `import statsmodels.api as sm` and the analyst saves
- **THEN** the script is saved as a new version

### Requirement: Check of imports scripts never need
With the other checks while editing, the editor SHALL mark an import of `os`, `sys`, `subprocess`, `socket`, `shutil`, `ctypes`, `importlib`, `http` or `urllib`, or of a submodule of one of them, and any call of `__import__`, with a warning that scripts receive their data from Tamiza and need neither files, processes nor the network. The mark follows the rule that checks never block.

#### Scenario: Process or network module
- **WHEN** a script contains `import subprocess` or `from urllib.request import urlopen`
- **THEN** the editor marks the line with the warning that scripts need neither files, processes nor the network

#### Scenario: Dynamic import
- **WHEN** a script calls `__import__("os")`
- **THEN** the editor marks the call with the same warning

#### Scenario: Standard library modules that are fine
- **WHEN** a script imports `re`, `math`, `json`, `datetime` and `unicodedata`
- **THEN** the editor marks none of them

### Requirement: Versions saved through MCP
The Versions tab SHALL mark each version created through the MCP server (a save, a removal or a restore made by an AI client) with "via MCP" next to its author and time. A version created in the editor, including the restore of a version saved through MCP, SHALL carry no mark.

#### Scenario: Version written by Claude
- **WHEN** an analyst's Claude saves version 3 of the indicator `coverage` and the analyst opens its Versions tab
- **THEN** version 3 shows the analyst as its author and "via MCP", and versions 1 and 2 show no mark

#### Scenario: Restored in the editor
- **WHEN** the analyst restores version 3 in the editor
- **THEN** the new version 4 shows no mark
