# Spec Delta

## MODIFIED Requirements

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

### Requirement: ctx reference
Hovering a member of `ctx`, `ctx.form` or `ctx.log` in the editor SHALL show its signature and description. The side panel SHALL have a reference tab that lists every member of `ctx`, `ctx.form`, `ctx.log` and `ctx.reference`, the contract of the selected script's kind, the rules for what it returns, cells and `ctx.show`, each with a short example.

#### Scenario: Hover
- **WHEN** an analyst hovers `column_label` in `ctx.form.column_label("main", "age")`
- **THEN** a tooltip shows `column_label(table, column)` and that it returns the question label of a flattened column, or nothing

#### Scenario: Reference of an indicator
- **WHEN** an analyst opens the reference tab while an indicator is selected
- **THEN** it shows the `compute(cleaned, ctx)` contract, that the result is one DataFrame with at least one numeric column, and the conventions for `value`, `numerator` and `denominator`

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
