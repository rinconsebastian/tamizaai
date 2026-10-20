# Writing scripts

Each project can have up to two Python scripts that clean the raw Kobo submissions and prepare tables for reading, and any number of **indicator scripts** that each compute one indicator, up to 100. Tamiza runs them in order every time the project's data changes, publishes each result only if it succeeds, and records what happened in the run log.

Analysts and admins write scripts in the project's **Settings > Scripts** page; viewers do not open Settings. Every save creates a version you can view and restore.

## The chain

```
raw submissions ──► flatten ──► 1. clean ──┬─► 2. prepare_output ──► clean-data dashboard
                                           └─► indicator scripts, one `compute` each ──► indicators dashboard
```

1. **Flatten** (automatic) turns the submissions into tables.
2. **Script 1, `clean`**, receives those tables and returns cleaned tables. Without it, the flattened tables are the clean data.
3. **Script 2, `prepare_output`**, and each **indicator script, `compute`**, receive the cleaned tables. They do not depend on each other: if one fails, the others still run and publish. All of them are optional; without script 2 nothing is published for it, and without indicator scripts there are no indicator tables.

If flattening or script 1 fails, script 2 and the indicator scripts do not run. The indicator scripts run after script 2, one at a time, in the order of their names.

## The flattened tables

Scripts receive a dictionary of pandas DataFrames.

- **`main`** has one row per submission. Submissions deleted in Kobo are left out.
- **One table per repeat group**, named after the group (for example `members`), with one row per repeat answer. Nested repeat groups get their own table too.
- **Groups that are not repeats** do not create tables: their questions are columns of the table they sit in.

**Columns** are named after the questions (`age`, not `respondent/age`), lowercased, with accents removed and other characters turned into underscores. When two questions of the same table have the same name (for example `age` in the groups `head` and `spouse`), the second one gets its full path: `spouse_age`. Each of these columns also carries the text of its question as a label; see [Question labels](#question-labels).

**System columns:**

| Table | Column | Meaning |
|---|---|---|
| `main` | `_id` | Kobo's submission ID. Use it to join repeat tables. |
| `main` | `_uuid`, `_instance_id` | Kobo's identifiers of the submission. |
| `main` | `_submission_time` | When Kobo received it (UTC). |
| `main` | `_version` | The form version it was submitted with. |
| repeat tables | `_row_id` | Number of the row within the table. |
| repeat tables | `_parent_id` | `_id` of the submission, or `_row_id` of the enclosing repeat row for nested repeats. |
| repeat tables | `_parent_table` | `main`, or the enclosing repeat table. |
| repeat tables | `_index` | Position of the answer within its parent (1, 2, 3…). |

In pandas, integer columns use the nullable `Int64` type (a missing value is `<NA>`, not a float), text columns the `str` type, and timestamps are timezone-aware in UTC.

**Types** come from the form: `integer` → integer, `decimal` and `range` → float, `date` → date, `start`, `end` and `dateTime` → timestamp in UTC. Everything else is text, including `select_one` and `select_multiple` (which hold [choice labels](#choice-labels)), `time` and `geopoint`. An answer that cannot be read as its question's type (for example `n/a` in an integer question) becomes empty, and the flatten step's log counts them per column. Answers to questions no longer in the form are kept as text columns.

### Choice labels

Kobo stores and sends the answers to select questions as the choices' **names**, the codes defined in the form (`1`, `piped`), not as their labels (`Yes`, `Piped`). The flattened tables hold the **labels**, so the data is readable as it is:

- A `select_one` answer is the label of its choice: `No`.
- A `select_multiple` answer is the labels of its choices, in the order of the answer, joined by `; `: `Water; Electric power`. Kobo's own exports join labels with a space, but labels contain spaces too, so a space cannot be split back reliably; `main["services"].str.split("; ")` can.
- Labels are always in the form's **default language**, its first language.
- **Each answer uses the labels of the form version its submission was made with.** A form is often redeployed during fieldwork, and a choice can be added, removed or relabeled between versions. If version `vA` called a choice `Si` and a later version `vB` calls it `Sí`, the submissions made with `vA` hold `Si` and those made with `vB` hold `Sí`. Answers in repeat groups use their submission's version, and a question that the current form no longer has is still translated by the version that had it. The project's **Settings > Submissions** page lists the versions in use and whether their labels are available (see [kobo.md](kobo.md#7-form-versions-and-choice-labels)).
- The "or other" option of a select question is labeled `Other`, as Kobo shows it. What the respondent typed stays in its own column.
- The raw submissions are never changed; only the tables the scripts receive and the dashboards read show labels.

When a label cannot come from the submission's own version, the step falls back and counts it in the flatten step's log, so nothing changes silently:

| Situation | What the table holds | Log line |
|---|---|---|
| The submission has no form version, or its version's labels are not available yet (pending, failed or not found). | The label from the project's current form, which is what Kobo's own exports do. | `Form version vB: 15 submissions used the current form (definition pending).` and `3 submissions without a form version used the current form.` |
| The code is not in its version's list, for example a submission edited in Kobo with a later version that added the choice. | The label from the current form. | `main.water_source: 1 value not in its form version; labeled from the current form.` |
| The code has no label anywhere, for example because the choice has no label in the default language. | The code. | `main.has_water: 2 values without a label; kept as codes.` |

Some questions are never translated and keep what Kobo sent, without a log line: questions whose choices come from a file (`select_one_from_file`, `select_multiple_from_file`) or from earlier answers, and Kobo's ranking, scoring and matrix questions.

**When a relabeled choice splits a category.** In the example above, a column now holds both `Si` and `Sí`. To count them together, map every label to the current one in script 1:

```python
def current_label(value):
    code = ctx.form.name("municipality", value)  # the code that label stands for, in any version
    return ctx.form.label("municipality", code) or value  # its label in the current form

main["municipality"] = main["municipality"].map(current_label)
```

**Joining with data keyed by codes.** Reference tables and other outside data often use the codes. `ctx.form.name(question, label)` gives the code a label stands for, in the current form or in an earlier version; see [`ctx`](#ctx).

#### Upgrading from scripts that compare with codes

Before choice labels, select columns held codes. A script that compares them with codes stops matching after the first run with labels, and its tables come out empty or wrong rather than failing. Tables already published keep their codes until the project's next run, so use **Run now** only after updating the scripts.

```python
# Before: the column held the choice name.
piped = main["water_source"] == "piped"

# Now: compare with the label...
piped = main["water_source"] == "Piped"

# ...or with the code, whichever version the answer was given in.
piped = main["water_source"].map(lambda label: ctx.form.name("water_source", label)) == "piped"
```

For a `select_multiple` column, split it first with `main["services"].str.split("; ")` and compare each label.

### Question labels

A column is named after its question (`has_water`), which is short and meant for scripts. The text the question asks (`Does the household have running water?`) is kept as the column's **label**, so people reading the tables, and tools such as Power BI, can tell what a column holds. A label never changes a column's name, order, type or values.

- **Where to find it.**
  - In PostgreSQL, as the **column comment** of the published tables: `\d+ main` in `psql`, or `SELECT col_description('p_<id>_raw.main'::regclass, 2)` for the second column. Dashboards and BI tools that read column descriptions show it.
  - In scripts, with `ctx.form.column_label(table, column)`, or all at once in the DataFrame `ctx.form.columns`.
  - In a test on a sample, under the column's name in the preview of the flattened tables.
- **Only the latest deployed version.** Unlike [choice labels](#choice-labels), which follow the version of each submission, a column has one label for all its rows, taken from the form's **latest deployed version** in its default language. If a question was relabeled in a later version, every answer, including those given with the earlier one, is under the new text. Edits that were not deployed do not count.
- **Columns without a label.** These have none: system columns (`_id`, `_index`...); columns of questions that the deployed version no longer has, which the flatten log lists per table (`main: 2 columns are not in the deployed form version and have no question label: old_question, legacy_age.`); and questions with no label in the default language, which the log does not list.
- **The "or other" text.** What the respondent typed for an "or other" option (`water_source_other`) belongs to its select question and is labeled with the question's label and `: Specify other`.
- **When labels are not applied.** If the form has no deployed version recorded, or Tamiza has not been able to read the deployed version's definition yet (the **Settings > Submissions** page shows its status), the run publishes the tables without labels, and the flatten log says so (`Question labels not applied: the definition of the deployed form version vC is not available (failed).`). The next run after the definition is read publishes them. Re-checking the form in **Settings > General** retries it.
- **Redeploying.** The published comments change at the next run, like the rest of the published tables; **Run now** applies them at once.
- **Tables a script returns carry no labels.** Script 1 can rename, merge and compute columns, so Tamiza cannot tell which question an output column comes from. The clean tables are the exception when there is no script 1: they are the flattened tables, so they keep the labels. Output column names are always reduced to lowercase letters, digits and underscores (see [Rules for what scripts return](#rules-for-what-scripts-return)), so a label cannot be used as a name either.

To give readers the labels with an output, publish a **data dictionary** from script 2: a table that lists each column, where it comes from and what its question asks.

```python
def prepare_output(cleaned, ctx):
    # One row per column of the flattened tables: its table, name, question path and label.
    # Columns of questions the deployed version no longer has are listed too, with an empty label.
    return {"data_dictionary": ctx.form.columns[["table", "column", "path", "label"]]}
```

## The scripts

### 1. `clean(raw, ctx)`

Receives the flattened tables; returns a dictionary of table names to DataFrames.

```python
def clean(raw, ctx):
    main = raw["main"]
    members = raw["members"]

    # Drop test submissions and their members.
    tests = main["enumerator"].str.startswith("test", na=False)
    ctx.log.info("Dropping %d test submissions", int(tests.sum()))
    main = main[~tests]
    members = members[members["_parent_id"].isin(main["_id"])]

    # Ages outside 0–110 are data-entry errors.
    members.loc[~members["member_age"].between(0, 110), "member_age"] = None

    return {"main": main, "members": members}
```

### 2. `prepare_output(cleaned, ctx)`

Receives the cleaned tables; returns a dictionary of the tables the clean-data dashboard will show.

The [clean data dashboard](clean-data-dashboard.md) shows exactly these tables, one at a time, to every member of the project. Without script 2 it shows the clean tables instead (script 1's, or the flattened ones), and when both exist members can switch between them. The flattened raw tables are not shown.

```python
def prepare_output(cleaned, ctx):
    main = cleaned["main"].copy()
    # The column holds labels; the code each label stands for is one call away.
    main["municipality_code"] = [ctx.form.name("municipality", label) for label in main["municipality"]]

    size = cleaned["members"].groupby("_parent_id").size().rename("household_size")
    households = main.merge(size, left_on="_id", right_index=True, how="left").fillna({"household_size": 0})

    return {"households": households[["_id", "municipality", "municipality_code", "household_size"]]}
```

### 3. Indicator scripts: `compute(cleaned, ctx)`

An indicator is one function that receives the cleaned tables and returns **one DataFrame** shaped for that indicator: a rate by municipality, a series by month, a top ten. Each indicator is its own script, published as its own table in the indicators schema, named after the indicator.

**The contract is one sentence: return a DataFrame with the dimensions you need and at least one numeric column.** Nothing else is required, and the step fails with a message when the DataFrame has no numeric column. By convention, and not by rule:

- `value` is the number a widget plots by default. When the table has no `value`, the dashboard offers its first numeric column.
- `numerator` and `denominator` let a widget plot a **rate** as `sum(numerator) / sum(denominator)` over any group of rows. Return them next to `value` whenever the indicator is a ratio: a widget that groups several rows into one then gets the right rate, while summing `value` would add up percentages.
- Every other column is a **dimension** the dashboard can group and filter by: `municipality`, a `round` or `year` column for a trend, `sex`.
- For a map, add a geometry column: a geopandas column, or text in WKT (`POINT`, `POLYGON` or `MULTIPOLYGON`), projected to degrees (longitude and latitude) before you return it; see [Maps](indicators-dashboard.md#maps).

#### The indicator's name and metadata

Create an indicator with **Add indicator** in the Scripts section (under **Settings**). It has:

| Field | Rules |
|---|---|
| **Name** | The name of its table: lowercase letters, digits and underscores, starting with a letter, at most 63 characters (`piped_water_rate`). It cannot be changed; remove the indicator and create another to rename it. |
| **Label** | The text the dashboard shows, 1 to 120 characters (`Households with piped water`). |
| **Description** | Optional, up to 1,000 characters. |
| **Unit** | Optional, up to 32 characters (`households`, `COP`, `%`). |
| **Format** | How the dashboard shows the numbers: `count` (a whole number with thousands separators), `decimal` (two decimals), `percent` (a **fraction** shown as a percentage: 0.42 reads `42%`) or `currency` (two decimals; the unit is the currency code, such as `COP`). |

The metadata is saved with every version of the script, so restoring a version brings back its label, unit and format too. Above the editor the indicator is one line (label, name, format and unit) with an **Edit details** button that opens the fields; they stay open while they hold edits or the API refused one. Anyone looking at an older version sees the same details as plain text. If you store 42 for 42 %, choose `decimal` with the unit `%` instead of `percent`.

A project can have up to **100 indicators**. Each has its own versions, its own sample test and its own step in the run, so an indicator that fails never stops the others.

#### A rate by municipality

```python
import pandas as pd


def compute(cleaned, ctx):
    main = cleaned["main"]
    rows = main.groupby("municipality").agg(
        numerator=("water_source", lambda answers: int((answers == "Piped").sum())),  # select answers are labels
        denominator=("water_source", "size"),
    ).reset_index()
    rows["value"] = rows["numerator"] / rows["denominator"]
    return rows[["municipality", "value", "numerator", "denominator"]]
```

Saved as `piped_water_rate` with the format `percent`, a widget can plot `value` by municipality, or the ratio of `numerator` to `denominator` for any selection of rows.

#### A series by month

```python
def compute(cleaned, ctx):
    main = cleaned["main"]
    month = main["_submission_time"].dt.strftime("%Y-%m")
    return main.groupby(month).size().rename("value").rename_axis("month").reset_index()
```

Saved as `households_surveyed` with the format `count`, a line chart groups by `month`. A named index (here `month`) is published as a column.

**Templates** in the editor offers starting points for the selected script (see [The editor](#the-editor)), and **Upload .py** loads a file you wrote elsewhere (for example in Jupyter; see [Writing scripts on your computer](#writing-scripts-on-your-computer)). Common preparation that several indicators need belongs in script 1 or script 2, whose tables every indicator receives; indicator scripts cannot import from each other.

## `ctx`

| Attribute | What it is |
|---|---|
| `ctx.form.questions` | DataFrame of the form's questions: `name`, `path`, `type`, `label`, `list_name`. It describes the project's current form, the deployed version. |
| `ctx.form.choices` | DataFrame of the choice lists of the current form: `list_name`, `name`, `label`. |
| `ctx.form.label(question, value)` | The label of a choice code in the current form, by question name or path; `None` if unknown. Use it for codes that come from outside the tables, such as a reference table. |
| `ctx.form.name(question, label)` | The code (choice name) a label stands for, by question name or path; `None` if there is none. It searches the current form first, then earlier versions from the newest to the oldest, so a label an older version used still resolves after a later one relabeled the choice. `None` also for a label that two choices of a version share, since it does not identify one. `Other` gives `other` for questions with the "or other" option. |
| `ctx.form.columns` | DataFrame of the columns of the flattened tables that hold answers: `table`, `column`, `path` (the question's path, or the submission key for a column whose question the form no longer has) and `label` (its [question label](#question-labels), empty when it has none). System columns are not listed, and neither are the columns of tables a script builds. |
| `ctx.form.column_label(table, column)` | The question label of a column of the flattened tables, from the latest deployed version; `None` for a system column, a column whose question is not in that version, a question without a label, a table a script built, or when the labels were not applied. |
| `ctx.reference` | The project's [reference tables](#reference-data) by name: `ctx.reference["population"]` is a DataFrame. `list(ctx.reference)` gives the names and `"population" in ctx.reference` checks one. |
| `ctx.log` | A Python logger: `ctx.log.info(...)`, `ctx.log.warning(...)`. Messages appear in the run log. |
| `ctx.project_name` | The project's name. |
| `ctx.show(data, name=None)` | Shows a DataFrame, a Series or any other value in the result of a [sample test](#cells-and-ctxshow) and returns it unchanged. In a run it does nothing, so you can leave the calls in the script. |

`print()` also writes to the run log. Labels are in the form's default language, its first language.

Asking for a reference table the project does not have, such as `ctx.reference["populaton"]`, fails the step with a message that lists the tables it has.

## The editor

The Scripts section lives under **Settings**. Above the editor one row picks the script: **1. Clean**, **2. Output** and an **Indicators** menu that lists each indicator with its label, name and version (a project with more than ten gets a filter), next to **Add indicator**. The selected script shows its version. Under the row, a line shows the **latest run** (its status, when it started and, if a step failed, which one) with a link to the run list at the bottom of the page and, for analysts and admins, **Run now**.

Under the editor, one row holds **Save**, the **Sample size**, **Test on sample**, **Download sample**, **Templates** and a **More actions** menu with **Upload .py** and **Remove script**. The result of a test appears right below the editor, and the sample download below it. **Checks** lists what the editor found while you type (it also shows while an older version is on view; the note that checks never block saving is for those who can save). The signature the script has to define is shown above the editor.

The editor is for writing a script without leaving the page. It knows the tables your script receives, what `ctx` offers and what the contract asks for, and it tells you what it can before you run anything. None of this blocks saving or testing: a test and a run decide whether a script works.

**The side panel.** Beside the editor (below it on a phone) there are three tabs, and four when the administrator has turned on the [assistant](#writing-with-the-assistant).

- **Tables** lists what the selected script receives *as the last run published it*: the flattened tables for script 1, the clean tables for script 2 and the indicators, with each table's row count and each column's type and [question label](#question-labels), and when they were published. It also lists the project's [reference tables](#reference-data) with their columns (analysts and admins get a **Manage** link to them), and, in a collapsed group, the form's questions (a filter opens it). A script you saved after the last run does not change the list until the next run (a test shows the tables its draft really received). Before the first run the panel says so, and the editor works without it. **Filter** matches names and labels; the refresh button reads the tables again, which also happens when a run ends. Click a name to put it in the editor at the cursor as a quoted string (`"has_water"`); when you cannot edit (an old version on view) the click copies the name instead.
- **Reference** lists the contract of the selected script, what it must return, cells and `ctx.show`, and every member of `ctx`, `ctx.form`, `ctx.log` and `ctx.reference` with a description and an example.
- **Versions** lists the saved versions of the script, newest first. Click one to read it (read-only); analysts and admins can restore it.
- **Assistant**, only if your administrator configured one, and only for analysts and admins: it drafts a script, rewrites a cell or fixes a failed test. See [Writing with the assistant](#writing-with-the-assistant).

**Completion.** While you type, the editor offers the members of `ctx`, `ctx.form` and `ctx.log` after a dot (with their signatures), the questions of the form as the first argument of `ctx.form.label(` and `ctx.form.name(`, the input tables inside `cleaned["`, the reference tables inside `ctx.reference["`, and columns with their type and table inside the brackets after a table: `cleaned["main"]["`, `ctx.reference["population"]["` or a variable you assigned from a table (`main = cleaned["main"]` and then `main["`). Anywhere else in a subscript string it offers the columns of every input table. **Ctrl+Space** opens the list on demand. Hovering a member of `ctx` shows its signature and description.

**Keys.**

| Key | What it does |
|---|---|
| Tab | Indents the selected lines by four spaces; with nothing selected, inserts spaces up to the next multiple of four. Never a tab character. |
| Shift+Tab | Removes up to four spaces from the start of the selected lines, or of the cursor's line. |
| Esc, then Tab | Moves the focus out of the editor, to the next control. This is how keyboard users leave it, since Tab indents. |
| Ctrl+Space | Opens the completion list. |
| Ctrl+Enter (Cmd+Enter on macOS) | Runs the [cell](#cells-and-ctxshow) the cursor is in. |

**Checks.** About half a second after you stop typing, the editor marks, on the line and in the list under the editor, what it can find without running the script:

- a syntax error (when there is one, it is the only thing reported, since the rest of the file cannot be read reliably);
- no top-level function with the contract's name (`clean`, `prepare_output` or `compute`), or a contract function that does not take two parameters;
- a member of `ctx`, `ctx.form` or `ctx.log` that does not exist (`ctx.refrence`);
- a reference table the project does not have (`ctx.reference["populaton"]`), naming the ones it has;
- an input table the published tables do not have (`cleaned["member"]`), unless the script adds that table itself (`raw["summary"] = ...`); nothing is marked while no tables are published;
- an import, at any level of the script (also inside the contract function), of a module that is neither one of the [available libraries](#limits) nor part of the Python standard library (`from sklearn.linear_model import LinearRegression`), naming the module and listing the libraries that are available;
- an import of `os`, `sys`, `subprocess`, `socket`, `shutil`, `ctypes`, `importlib`, `http` or `urllib`, or of a submodule of one of them, and any call of `__import__`: scripts receive their data from Tamiza and need neither files, processes nor the network;
- indentation that mixes tabs and spaces.

Column names are not checked, because scripts add and rename columns. Marks are hints, and the import marks are warnings like the others: they never block saving or testing.

**Templates.** **Templates** opens a menu of starting points for the selected kind of script, each with a name and a one-line description:

| Script | Templates |
|---|---|
| Script 1 | *Drop test submissions*; *Fix types and recode* |
| Script 2 | *Columns for the dashboard*; *Join a repeat group to main* |
| Indicator | *Count by group*; *Rate by group*; *Monthly series* |

A template replaces the text of the editor as one edit, so Ctrl+Z brings the previous text back; if the editor has unsaved changes it asks first. Each is written in [cells](#cells-and-ctxshow) with `ctx.show`, names the columns to adapt in constants at the top, and uses only columns every form has, so it runs unchanged on any project. A new indicator starts from *Count by group*.

## Testing on a sample

**Test on sample**, under the editor, runs the code in the editor on a sample of the project's submissions, whether you saved it or not. It is how to try a change before saving it: nothing is saved, no version is created, nothing is published and the test does not appear in **Runs**. Analysts and admins can test.

- **What runs.** The test flattens the sample, then runs the script you are editing. When that is script 2 or an indicator, the current saved version of script 1 runs first, as in a run (or the flattened tables are used when there is no script 1). To test a change to script 1 together with script 2 or an indicator, save script 1 first. A test runs **one** indicator, the one selected, and its preview shows the table named after that indicator.
- **The sample.** 500 submissions by default; choose from 1 to 5,000. They are spread across the whole project, not only the newest, and consecutive tests use the same submissions as long as the data does not change, so you can compare one attempt with the next. Submissions deleted in Kobo are left out. A project with fewer submissions than the sample size uses all of them.
- **Reuse.** The flattened sample, and the result of the saved script 1, are kept for the next test, so testing the same thing again skips those steps and the step says "Reused from an earlier test". A step is recomputed whenever something it read changed: a submission in the sample was updated, added to it or deleted, the form or its labels changed, a new version of script 1 was saved or a reference table was replaced. Reuse never changes a result; it only saves time, and your server's administrator can turn it off or limit its disk use (see [operations.md](operations.md)).
- **The same rules as a run.** The test uses the same contract, `ctx` (reference tables included), validation and time and memory limits, and the tables the draft receives hold choice labels, as in a run. A test that passes predicts a run that passes, apart from problems that only appear with more data.
- **Waiting.** Tests run in the same worker as runs, one job at a time. A test goes ahead of runs that are waiting, but waits for a run that has already started, which can take a few minutes on a large project; the panel says what it is waiting for. Starting another test replaces your test that has not started yet.
- **The result.** Each step shows its status, duration, rows in and out, log and, if it failed, the error with the traceback. For each table a step produced you see its columns with their types (`integer`, `decimal`, `boolean`, `date`, `timestamp`, `text`) and its first 100 rows. In the flattened tables, a column whose question has a [label](#question-labels) shows it under the column's name, cut when it is long, with the whole text on hover; the tables a script returns show none. Text longer than 500 characters is cut, and fewer rows are shown when 100 would exceed 1 MB; only the first 20 tables of a step are previewed. If you edit the code after testing, the panel says the result no longer matches the editor.
- **How long it is kept.** Only you can see your tests, and each is deleted 24 hours after it ended.

### Cells and `ctx.show`

A script is easier to write when you can look at the data between steps, as in a notebook. There is no notebook kernel: every test starts the script from the beginning, in a new process, and nothing stays in memory from one test to the next. What you get instead is a way to look and a way to stop.

**Look: `ctx.show(data, name)`.** In a test, each call adds a preview to the result: the name you gave it (`shown 1`, `shown 2`… without one), the line of the script that called it, how many rows the value had and its first 100 rows, taken **at the call**, so changing the DataFrame afterwards does not change what you see. A Series is shown as a one-column table, and anything else (a number, a dictionary, a string) as text cut at 2,000 characters. A test keeps the first 20 calls; the log says how many were left out. The previews are kept when the script goes on to fail or runs out of time, which is the moment you most want them. Only the script you are testing shows previews, not the saved script 1 that runs before it. In a run, `ctx.show` does nothing.

**Stop: cells.** A line that starts with `# %%` begins a *cell*, and the rest of the line names it. Cells are ordinary comments, so they change nothing in a run. In the editor, **Run cell** (or Ctrl+Enter, Cmd+Enter on macOS, for the cell the cursor is in) starts a test that runs the script from the start and **stops the first time execution reaches the end of that cell**, where the next `# %%` marker is. The result shows the previews and the log up to that point and the step is marked *stopped* at that line. Nothing after the cell runs, and the output is **not validated**, so a script you have not finished writing still gives you its previews. Running the last cell runs the whole script, as **Test on sample** does.

```python
import pandas as pd


def compute(cleaned, ctx):
    # %% Load the cleaned households
    main = cleaned["main"]
    ctx.show(main, "main")

    # %% Piped water by municipality
    rows = main.groupby("municipality").agg(
        numerator=("water_source", lambda answers: int((answers == "Piped").sum())),
        denominator=("water_source", "size"),
    ).reset_index()
    ctx.show(rows, "counts")

    # %% The indicator
    rows["value"] = rows["numerator"] / rows["denominator"]
    return rows[["municipality", "value", "numerator", "denominator"]]
```

Running the first cell shows `main`; running the second shows `counts` too. The third is where the indicator is built and validated.

Good to know:

- A marker must be on its own line, between two statements, at the indentation of the block it sits in: inside a function, a loop or an `if`. A marker inside a call or a list that continues over several lines fails the step with a message. A marker inside a loop stops in the first iteration; inside a branch that is never taken, the script runs to its end and the test is a whole test.
- The stop is an exception that your own `try`/`except Exception` does not catch, but a bare `except:` or `except BaseException:` would swallow it and the script would carry on past the cell.
- Variables are not kept between tests. A cell can use what the cells above it defined, because they run again each time; it cannot use something you ran in an earlier test. Code that is slow to run will be slow in every cell, which is why the flattened sample and script 1 are [reused](#testing-on-a-sample).

### Your indicator as the dashboard will see it

When a test of an indicator succeeds, under its table the result shows **As the dashboard will see it**: the indicator's table read the way the [indicators dashboard](indicators-dashboard.md) reads a published one.

- **Measures, rate and dimensions.** The numeric columns are the measures a widget can plot (booleans are not), with the one it plots by default: `value`, or else the first numeric column. If the table has `numerator` and `denominator` the view says that a rate can be plotted as a ratio of sums. Every other column is a dimension, and text columns that hold WKT geometries are listed as columns a map can use.
- **Figures.** A stat tile shows the default measure summed over the whole table and, when there is a rate, the ratio of the two sums, written with the **format and unit** in the fields above the editor. Change them and the view redraws without a new test. A bar chart shows the same by a dimension you choose (and, with a rate, you can choose to plot the ratio). The figures come from the whole table, not only the 100 rows of the preview. A dimension with more than 50 values is listed but not charted. Groups are ordered by their text, which can differ slightly from the dashboard's order for accented text.
- **Warnings.** The view warns about what usually surprises people on the dashboard: a table with no rows; no `value` column (it names the column widgets will plot instead); `numerator` without `denominator` or the reverse; empty values in the default measure; the format `percent` with a value above 1, showing what it would read as (a percent is a fraction: 0.42 reads `42%`); a numeric column named like a dimension (`year`, `month`, `code`, `id`, or ending in `_id` or `_code`), which widgets would offer as a measure, so return it as text; and rows that share the values of every dimension, which widgets add up.

A warning is a hint about the table, not an error: the test still succeeded.

## Writing with the assistant

When your administrator has configured one (see [operations.md](operations.md#ai-assistant)), analysts and admins get a fourth tab in the side panel, **Assistant**. It writes pandas for you from what the editor already knows: the tables and columns your script receives, the form's choice lists, the contract and the `ctx` it offers. Without that configuration the tab does not exist and nothing leaves the server. Viewers never see it.

The tab shows the explanation in plain text. The code it proposes goes into the editor only when you apply it. The assistant never saves a version and never starts a test: you read the code, test it on a sample and save it as you always do.

**Three things to ask for.**

- **Script.** Describe what you want, for example `share of households with piped water by municipality`, and it answers with a whole script of the selected kind. For an indicator, **Draft from the details** fills the request from the indicator's label, description, unit and format, as the fields hold them even if you have not saved them.
- **This cell.** With the cursor inside a [`# %%` cell](#cells-and-ctxshow), it rewrites that cell only and sees the rest of the script as context. The scope names the cell it will rewrite; without a cell it asks you to put the cursor inside one.
- **Fix the last test.** After a sample test fails, or when the dashboard view of an indicator warns about something, **Ask the assistant** next to the error or the warning opens the tab with a request for that fix. It is offered only for the script you tested, while the editor still holds the text that test ran, and never for an old version. The test that goes with the request is yours and lasts the 24 hours a test is kept.

**Applying a proposal.** Under the explanation the tab lists the code blocks with their length, whether the script defines its function, and the marks the editor's [checks](#the-editor) raise on the result, such as an import that is not available. The marks do not stop you. The actions are **Replace script** (only for a complete script that defines its function), **Replace cell** (only for a single cell that starts with `# %%` and when that cell is still in the editor unchanged, wherever it now is) and **Insert at cursor**, which is always possible. An answer that was cut off, or that holds several blocks, can only be inserted. Each action is one edit: **Ctrl+Z** brings back the text you had. If you typed in the editor after sending the request, the tab asks before applying.

**The conversation.** Each script has its own conversation, kept in your browser tab for the session and gone when you sign out or choose **New conversation**; the server keeps none. A request keeps running if you look at another tab or another script, and its answer waits in the script you asked it for. **Cancel** ends it. Only one request per person runs at a time, and each person has a daily budget of tokens set by the administrator.

**Language.** Write the request in your language: the explanation comes back in it. The code, its identifiers, comments and log messages are always in English, and column and table names stay exactly as the form gave them.

```text
You:        Rate of households whose water_source is "Piped", by municipality.
            Percent, with numerator and denominator.
Assistant:  I count households per municipality and divide those that answer
            "Piped" by all of them. [proposed code: 21 lines]
            -> Replace script | Insert at cursor
```

### What the assistant can see

Every request carries only this, and **Show what will be sent** displays the exact text before you send it (**Copy context** puts it on the clipboard, for example to use it elsewhere):

- the rules of the contract and the libraries your script can and cannot use, and the templates of its kind;
- the tables your script receives, with their row counts, columns, types and [question labels](#question-labels); the reference tables' columns and types; the form's questions and **the labels of its choices**, up to 50 per list. A choice list of people's names, such as the enumerators', is sent like any other;
- the project's time zone and enumerator question, the indicator's details, **the saved script 1** and **the text in the editor**, both exactly as written. Constants you typed in them (a submission to drop, a name to correct) go with them;
- your messages and the explanations of the earlier answers;
- for a fix, the shape of your last test: its steps, tables and columns, the number of groups of each chart, and its error and log lines, reduced to the line and function of each frame of your script and to the lines that start with a log level, with **every word that is not a name of the form, a word of your script or a word of the libraries' own messages replaced by `…`**.

It never carries rows of the sample or of a published table, the rows or text of a `ctx.show`, what your script printed, a value, total or group of a chart, or the rows of a reference table. If a name in your tables is itself a respondent's (a pivot made from names), it shows as `…` in the test facts, but it is a column name of the schema the model receives.

Your administrator chooses the provider, and with it who processes this text. Ask them which one it is: the tab names the model and the host.

## Writing scripts on your computer

A test runs your script from the start every time. To explore the data the way you do in Jupyter, with variables that stay in memory from one cell to the next, work on your own computer and upload the script when it is ready. Tamiza gives you a sample of the project's data and a small package, `tamiza_sdk`, that builds the same `ctx` a script receives and checks a script the way the server runs it. Nothing in it contacts Tamiza.

### Download a sample

In **Settings > Scripts**, set the **Sample size** (the same field as a test) and click **Download sample**. Analysts and admins can. The worker prepares the sample like a test, after the tests that are waiting and before the runs, reusing the flattened sample a recent test kept, and a strip under the test result shows when it is ready: the number of submissions, the size and until when it is kept, the tables of the zip on one line (flattened, clean and reference), and a notice that the file holds the project's submissions. **Download .zip** then saves a file named after the project and the date.

| In the zip | What it is |
|---|---|
| `raw/` | The flattened tables of the sample, the input of script 1. |
| `clean/` | What the saved script 1 returns on them, the input of script 2 and of every indicator. Without a script 1 the clean tables are the flattened ones and there is no `clean/` folder. |
| `reference/` | The project's reference tables, as `ctx.reference` gives them. |
| `context.json` | The form dictionary, choice labels and column labels that `ctx` is built from. |
| `tamiza_sdk/`, `requirements.txt` | The SDK, and the libraries scripts can use at the server's versions. |
| `README.md`, `getting-started.ipynb` | How to set up, and a notebook that loads the sample, writes an indicator and checks it. |
| `manifest.json`, `logs/` | What the download holds and the Python and library versions of the server; the log of the flatten step and of script 1 on this sample. |

- The sample is the one a test of the same size would use: the same submissions, with choice labels and question labels, as long as nothing changed.
- If the saved script 1 fails on the sample, the download still succeeds with the flattened tables only. The panel warns, with the version and the error, and `logs/clean.txt` has the traceback; load `raw` to debug script 1.
- The file is kept **for one hour** after it is ready; the button works as many times as you need within that hour, and each click uses a new single-use link. Asking for another sample replaces the earlier one. A request over the server's size limit (100 MB unless the operator changed `TAMIZA_SAMPLE_DOWNLOAD_MB`) fails with a message that gives its size and suggests a smaller sample.
- **The zip contains the project's submissions.** Keep it as safely as the survey data, do not share it or commit it, and delete it when you no longer need it. It contains no credentials or server addresses.

### Set up

You need Python 3.12, the version the server runs. Unzip the file, open a terminal in its folder and run:

```bash
python -m venv .venv
source .venv/bin/activate   # on Windows: .venv\Scripts\activate
pip install -r requirements.txt jupyterlab
jupyter lab
```

Starting Jupyter in that folder is what lets `import tamiza_sdk` find the package in `tamiza_sdk/` and the functions find the data without being told where it is. The SDK needs no network, runs on Windows, macOS and Linux, and warns once if your Python or a library differs from the server's.

### Load the data and look at it

```python
import tamiza_sdk as tz

ctx = tz.load_context()            # the ctx your script receives
raw = tz.load_tables("raw")        # script 1's input: a dictionary of DataFrames
cleaned = tz.load_tables("clean")  # the input of script 2 and of the indicators

ctx.show(cleaned["main"].head(), "main")
print(ctx.form.label("municipality", "cali"))
```

- `load_tables` gives the tables with the column types a script gets on the server (nullable integers, text, timestamps in UTC) and new objects on every call, so a table you change in place does not change the next load.
- `ctx` has the same members and gives the same answers as on the server: `ctx.form`, `ctx.reference`, `ctx.log` (printed as `LEVEL message`) and `ctx.project_name`. `ctx.show(value, name)` displays the value in Jupyter, or prints it, under its name, and returns it.
- If script 1 failed on the sample, `load_tables("clean")` says so and points to `"raw"`.

### Check a script

`check(script, kind, name=None)` runs a script on the sample with a run's contract, `ctx`, validation and messages, and returns the tables it would publish. `script` is the contract function itself or the path of a `.py` file. The file is run in a **fresh namespace, as the server does**, so a helper you defined only in your notebook is not visible to it: that is the usual difference between "works in Jupyter" and "fails in a test". `kind` is `"clean"`, `"output"` or `"indicator"`, and an indicator needs its `name`.

```python
import tamiza_sdk as tz
from pathlib import Path

Path("piped_water_rate.py").write_text('''
def compute(cleaned, ctx):
    main = cleaned["main"]
    rows = main.groupby("municipality").agg(
        numerator=("water_source", lambda answers: int((answers == "Piped").sum())),
        denominator=("water_source", "size"),
    ).reset_index()
    rows["value"] = rows["numerator"] / rows["denominator"]
    return rows[["municipality", "value", "numerator", "denominator"]]
'''.lstrip(), encoding="utf-8")

result = tz.check("piped_water_rate.py", "indicator", name="piped_water_rate")
result                          # a summary; result.tables has the DataFrames
```

- A contract violation raises `ScriptError` with the message a run shows (`compute must return a DataFrame…`, `The indicator table has no numeric column…`). An exception in your script is raised as it is, with the traceback pointing at the line of your file, so `%debug` works.
- Column names are reduced as in a run (`Household size` → `household_size`); `result.renamed` lists the changes and they are printed as in a run's log.
- For an indicator, `result.dashboard` has the measures, the default measure, whether the rate can be plotted, the dimensions and the geometry columns, as in [Your indicator as the dashboard will see it](#your-indicator-as-the-dashboard-will-see-it). The warnings of that view are in the test on the server.
- `check` gives the script copies of the tables, so it never changes what you loaded. Pass `tables=` to give it other inputs, for example the result of checking your script 1:

```python
import tamiza_sdk as tz
from pathlib import Path

Path("clean.py").write_text('''
def clean(raw, ctx):
    main = raw["main"]
    tests = main["enumerator"].str.startswith("test", na=False)
    return {"main": main[~tests], "members": raw["members"]}
'''.lstrip(), encoding="utf-8")

first = tz.check("clean.py", "clean")
second = tz.check("piped_water_rate.py", "indicator", name="piped_water_rate", tables=first.tables)
second.tables["piped_water_rate"]
```

### Upload it

In **Settings > Scripts**, select the script (or add the indicator with the same name), click **Upload .py**, choose the file, run **Test on sample** and **Save**. For the same sample and script, a successful `check` returns the same tables as the test: the same names, columns, types and values.

### What is different from the server

- No time or memory limit and no process isolation: your script runs in your Python, which can read your files and use the network. Do not rely on either: on the server a script has only the tables it is given, and a future release removes its network access.
- One sample, the one in the zip, not the whole project; a rare category can be missing from it.
- Your Python and libraries may differ from the server's. Install `requirements.txt` to get the same versions.
- `ctx.show` has no limit of 20 calls and keeps nothing: what you displayed stays in the notebook.

## Reference data

Reference tables hold data that does not come from the form but that scripts need: population projections, municipality codes, price lists. They are per project, in the **Settings > Reference data** page. Analysts and admins can list them and preview their first 100 rows (a column whose header was renamed shows the file's original header under its name), and upload, replace and delete them. **Preview** is the button of each row; **Replace** and **Delete** are in the row's actions menu. The script editor's **Tables** tab links to this page with **Manage**.

**Uploading.** Choose a CSV or XLSX file of up to 10 MB and a name. The name is how scripts find the table (`ctx.reference["population"]`): lowercase letters, digits and underscores, starting with a letter, at most 63 characters, unique in the project. A project can have up to 50 reference tables.

**Replacing.** **Replace** opens the same dialog with the name fixed: choose the file, and the table keeps its name while its rows and columns become those of the file. If the file is refused, the dialog says why and keeps your choice. Runs that already started keep the rows the table had when they started.

**Reading the file.**

- The first non-empty row is the header. XLSX files are read from their first sheet; CSV files must be UTF-8, separated by commas or semicolons.
- A file needs at least one row below the header, at most 200 columns and at most 5,000,000 values (rows times columns).
- **Column names** are adjusted the same way question names are (`Municipality code` → `municipality_code`, `Población 2026` → `poblacion_2026`); an empty header becomes `column`, and repeated names get a number (`age`, `age_2`). The upload lists every column it renamed, in a note you can dismiss (a replacement that renames the same columns as before says nothing).
- **Column types** come from the values. A column is `integer` when every value is a whole number, `decimal` when every value is a number, `date` when every value is a date (an Excel date or `2026-01-31`), and `text` otherwise. Empty cells are empty values and do not change the type.
- Numbers use a dot as decimal separator; in files separated by semicolons, as Excel writes them in many Spanish-language settings, a comma also works (`12,5`). Thousands separators are not understood: `1.234.567` makes the column text.
- **Codes keep their zeros.** A value that starts with `0` followed by more digits, such as the DANE code `05001`, is not a number, so its column stays text and the zero is kept. In Excel, store such codes as text.
- One stray value (`n/a` in a column of numbers) makes the whole column text. Clean the file, or convert the column in your script.

In pandas, `integer` columns arrive as the nullable `Int64` type, `decimal` as `float64`, `date` as `datetime64` and `text` as `str`, like the flattened tables.

```python
import pandas as pd


def compute(cleaned, ctx):
    population = ctx.reference["population"]  # columns municipality_code, population_2026
    # The table is keyed by codes and the tables hold labels, so the join goes through ctx.form.name.
    main = cleaned["main"].copy()
    main["municipality_code"] = [ctx.form.name("municipality", label) for label in main["municipality"]]
    counts = main.groupby("municipality_code").size().rename("households").reset_index()
    merged = counts.merge(population, on="municipality_code")
    return pd.DataFrame({
        "municipality_code": merged["municipality_code"],
        "value": merged["households"] / merged["population_2026"] * 1000,
        "numerator": merged["households"],
        "denominator": merged["population_2026"],
    })
```

**Deleting, and runs.** **Delete** (after a confirmation) removes a table; scripts that still read it fail from the next run. A run reads every reference table once when it starts, so replacing a table during a run does not affect that run; the next run uses the new rows. The run's details list the reference tables it used, with their row counts and when each was last updated.

## Rules for what scripts return

- Scripts 1 and 2 return at least one table. **Table names** must already use lowercase letters, digits and underscores, start with a letter and have at most 63 characters (`households`, `persons_2026`), because the dashboards select tables by name.
- An indicator script returns **one DataFrame** with at least one numeric column (integer or decimal, nullable ones included; true/false columns do not count). It is published as the table named after the indicator.
- **Column names** are adjusted for you the same way question names are (`Household size` → `household_size`); the run log lists every renamed column. If two columns of a table end up with the same name (`Age` and `age`), the step fails.
- A **named index** (for example after `groupby`) is published as columns; an unnamed index is dropped.
- **Geometry columns** (geopandas) are published as text in WKT format.
- **Row order** is kept: the clean data dashboard lists a table's rows in the order your script returned them until a member sorts by a column, so sort the DataFrame (`sort_values`) if the order matters to your readers.
- Each indicator's table is published under the indicator's name, one table per indicator.
- **Column labels.** Only the flattened columns carry a [question label](#question-labels). The dashboard shows it under the column name when there is one, so a table you build in script 2 shows names only; use descriptive column names (`household_size`) for those.

A step that fails publishes nothing: the dashboards keep showing the last successful results. For an indicator that means its own previous table stays and the other indicators still publish.

## When scripts run

- **After a synchronization** that brought new, changed or deleted submissions (on by default).
- **On a schedule**, every 1 to 168 hours, counted from the start of the last run.
- **Run now**, for analysts and admins.

Analysts and admins read these settings as a line on **Settings > Submissions** (the Scripts section only links to the latest run) ("Scripts run after each synchronization with changes · also run every 24 hours · Next scheduled run …"). Admins change the first two with **Edit schedules** on that page, under **When scripts run**. A webhook delivery never starts a run by itself. A project has at most one run waiting: if one is already queued, another trigger does not add a second; if one is running, one more is queued to follow it.

Saving a script does not start a run; the next run uses the latest version of each script. A run records which version of script 1, script 2 and each indicator it used. An indicator you create while a run is running is computed from the next run.

## The run log

**Runs** lists every run with its status, trigger and how long it took; the steps are shown as badges only for a run where one failed or was skipped. **Details** names the versions of script 1 and script 2 the run used and shows each step, with one step per indicator listed by its label and name after the three fixed ones:

- **Succeeded**, with rows in and out and the tables it published.
- **Failed**, with the error. For an exception, the traceback shows the lines of your script that raised it. A run with a failed indicator ends as failed and its error names the indicator; the run list shows it, and the other indicators are published all the same.
- **Skipped**, when the script does not exist or an earlier step failed.

The log of each step holds your `ctx.log` messages and printed output, and for flattening the reference tables the run read, the count of values that could not be read, every choice-label fallback (see [Choice labels](#choice-labels)), and where the column labels came from, the columns that have none because their question is not in the deployed version, or why no labels were applied (see [Question labels](#question-labels)). Below the steps, **Reference data used** lists those tables with their row counts and when each was last updated.

## Limits

- Each script may run for **10 minutes** and use **1024 MB** of memory, unless the operator changed the limits (see [operations.md](operations.md)). A script that goes over is stopped and its step fails with a message naming the limit. Tests on a sample have the same limits.
- Available libraries: **pandas 3.0, pyarrow 25, numpy 2.5, polars 1.44, duckdb 1.5, geopandas 1.2, openpyxl 3.1** and the Python 3.12 standard library. **shapely 2.1** (the geometry package geopandas uses) is also there, installed with geopandas rather than pinned by Tamiza. scikit-learn, SciPy and statsmodels are **not** available. Scripts cannot install other packages; importing one that is not there fails with `ModuleNotFoundError`, and the editor warns about such an import before you test (see [Checks](#the-editor)).
- Do not rely on network access: a future release removes it from the analytics service.
- A script larger than 200 KB is rejected.

## What isolation does and does not protect

Each script runs in its own process with only its input files, in a working directory that is deleted afterwards, and without the database's address or password in its environment. This prevents accidents, such as a script overwriting another project's files or reading credentials it was handed.

It is **not a security sandbox**: a script runs as the same operating-system user as the analytics worker and could, with deliberate effort, reach things it should not. That is why only analysts and admins can save scripts. Give those roles only to people you would trust with the server.

## Where the results live

For people who query the database directly: each project's tables are in PostgreSQL schemas named after the project's ID (its UUID without dashes):

| Schema | Contents |
|---|---|
| `p_<id>_raw` | The flattened tables. |
| `p_<id>_clean` | Script 1's tables, or the flattened ones. |
| `p_<id>_output` | Script 2's tables (absent without script 2). |
| `p_<id>_indicators` | One table per indicator, named after it (absent when the project has no indicators). |

The raw, clean and output schemas are replaced as a whole when their step succeeds. In the indicators schema each indicator replaces only its own table, and the tables of indicators that were removed are dropped at the start of the next run's indicator steps.

**Column comments.** The columns of the flattened tables hold their [question labels](#question-labels) as PostgreSQL column comments, in `p_<id>_raw` and, when there is no script 1, in `p_<id>_clean`. They are published in the same swap as the tables, so a reader never sees tables with the comments of another run. To list them:

```sql
SELECT a.attname AS "column", d.description AS label
FROM pg_attribute a
JOIN pg_description d ON d.objoid = a.attrelid AND d.objsubid = a.attnum
WHERE a.attrelid = 'p_<id>_raw.main'::regclass;
```
