# Writing scripts

Each project can have up to three Python scripts that turn the raw Kobo submissions into the tables the dashboards read. Tamiza runs them in order every time the project's data changes, publishes each result only if it succeeds, and records what happened in the run log.

Analysts and admins write scripts in the project's **Scripts** section; viewers can read them. Every save creates a version you can view and restore.

## The chain

```
raw submissions ──► flatten ──► 1. clean ──┬─► 2. prepare_output ──► clean-data dashboard
                                           └─► 3. compute_indicators ──► indicators dashboard
```

1. **Flatten** (automatic) turns the submissions into tables.
2. **Script 1, `clean`**, receives those tables and returns cleaned tables. Without it, the flattened tables are the clean data.
3. **Script 2, `prepare_output`**, and **script 3, `compute_indicators`**, each receive the cleaned tables. They do not depend on each other: if one fails, the other still runs and publishes. Both are optional; without them nothing is published for that step.

If flattening or script 1 fails, scripts 2 and 3 do not run.

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
- **Each answer uses the labels of the form version its submission was made with.** A form is often redeployed during fieldwork, and a choice can be added, removed or relabeled between versions. If version `vA` called a choice `Si` and a later version `vB` calls it `Sí`, the submissions made with `vA` hold `Si` and those made with `vB` hold `Sí`. Answers in repeat groups use their submission's version, and a question that the current form no longer has is still translated by the version that had it. The project's **Data** section lists the versions in use and whether their labels are available (see [kobo.md](kobo.md#7-form-versions-and-choice-labels)).
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
- **When labels are not applied.** If the form has no deployed version recorded, or Tamiza has not been able to read the deployed version's definition yet (the **Data** section shows its status), the run publishes the tables without labels, and the flatten log says so (`Question labels not applied: the definition of the deployed form version vC is not available (failed).`). The next run after the definition is read publishes them. Re-checking the form in the Overview retries it.
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

### 3. `compute_indicators(cleaned, ctx)`

Receives the cleaned tables; returns **one** DataFrame with the columns `indicator`, `disaggregation`, `value`, `numerator` and `denominator`. Other columns are allowed.

```python
import pandas as pd


def compute_indicators(cleaned, ctx):
    main = cleaned["main"]
    rows = []
    for label, group in [("total", main), *[(f"municipality={m}", g) for m, g in main.groupby("municipality")]]:
        with_water = int((group["water_source"] == "Piped").sum())  # select answers are labels
        rows.append({
            "indicator": "households_with_piped_water",
            "disaggregation": label,
            "value": with_water / len(group) if len(group) else None,
            "numerator": with_water,
            "denominator": len(group),
        })
    return pd.DataFrame(rows)
```

**Insert template** in the editor writes a starting point for the selected script, and **Upload .py** loads a file you wrote elsewhere (for example in Jupyter).

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

`print()` also writes to the run log. Labels are in the form's default language, its first language.

Asking for a reference table the project does not have, such as `ctx.reference["populaton"]`, fails the step with a message that lists the tables it has.

## Testing on a sample

**Test on sample**, under the editor, runs the code in the editor on a sample of the project's submissions, whether you saved it or not. It is how to try a change before saving it: nothing is saved, no version is created, nothing is published and the test does not appear in **Runs**. Analysts and admins can test; viewers cannot.

- **What runs.** The test flattens the sample, then runs the script you are editing. When that is script 2 or 3, the current saved version of script 1 runs first, as in a run (or the flattened tables are used when there is no script 1). To test a change to script 1 together with script 2, save script 1 first.
- **The sample.** 500 submissions by default; choose from 1 to 5,000. They are spread across the whole project, not only the newest, and consecutive tests use the same submissions as long as the data does not change, so you can compare one attempt with the next. Submissions deleted in Kobo are left out. A project with fewer submissions than the sample size uses all of them.
- **The same rules as a run.** The test uses the same contract, `ctx` (reference tables included), validation and time and memory limits, and the tables the draft receives hold choice labels, as in a run. A test that passes predicts a run that passes, apart from problems that only appear with more data.
- **Waiting.** Tests run in the same worker as runs, one job at a time. A test goes ahead of runs that are waiting, but waits for a run that has already started, which can take a few minutes on a large project; the panel says what it is waiting for. Starting another test replaces your test that has not started yet.
- **The result.** Each step shows its status, duration, rows in and out, log and, if it failed, the error with the traceback. For each table a step produced you see its columns with their types (`integer`, `decimal`, `boolean`, `date`, `timestamp`, `text`) and its first 100 rows. In the flattened tables, a column whose question has a [label](#question-labels) shows it under the column's name, cut when it is long, with the whole text on hover; the tables a script returns show none. Text longer than 500 characters is cut, and fewer rows are shown when 100 would exceed 1 MB; only the first 20 tables of a step are previewed. If you edit the code after testing, the panel says the result no longer matches the editor.
- **How long it is kept.** Only you can see your tests, and each is deleted 24 hours after it ended.

## Reference data

Reference tables hold data that does not come from the form but that scripts need: population projections, municipality codes, price lists. They are per project, in the **Reference data** section. Every member can list them and preview their first 100 rows; analysts and admins upload, replace and delete them.

**Uploading.** Choose a CSV or XLSX file of up to 10 MB and a name. The name is how scripts find the table (`ctx.reference["population"]`): lowercase letters, digits and underscores, starting with a letter, at most 63 characters, unique in the project. A project can have up to 50 reference tables.

**Reading the file.**

- The first non-empty row is the header. XLSX files are read from their first sheet; CSV files must be UTF-8, separated by commas or semicolons.
- A file needs at least one row below the header, at most 200 columns and at most 5,000,000 values (rows times columns).
- **Column names** are adjusted the same way question names are (`Municipality code` → `municipality_code`, `Población 2026` → `poblacion_2026`); an empty header becomes `column`, and repeated names get a number (`age`, `age_2`). The upload lists every column it renamed.
- **Column types** come from the values. A column is `integer` when every value is a whole number, `decimal` when every value is a number, `date` when every value is a date (an Excel date or `2026-01-31`), and `text` otherwise. Empty cells are empty values and do not change the type.
- Numbers use a dot as decimal separator; in files separated by semicolons, as Excel writes them in many Spanish-language settings, a comma also works (`12,5`). Thousands separators are not understood: `1.234.567` makes the column text.
- **Codes keep their zeros.** A value that starts with `0` followed by more digits, such as the DANE code `05001`, is not a number, so its column stays text and the zero is kept. In Excel, store such codes as text.
- One stray value (`n/a` in a column of numbers) makes the whole column text. Clean the file, or convert the column in your script.

In pandas, `integer` columns arrive as the nullable `Int64` type, `decimal` as `float64`, `date` as `datetime64` and `text` as `str`, like the flattened tables.

```python
import pandas as pd


def compute_indicators(cleaned, ctx):
    population = ctx.reference["population"]  # columns municipality_code, population_2026
    # The table is keyed by codes and the tables hold labels, so the join goes through ctx.form.name.
    main = cleaned["main"].copy()
    main["municipality_code"] = [ctx.form.name("municipality", label) for label in main["municipality"]]
    counts = main.groupby("municipality_code").size().rename("households").reset_index()
    merged = counts.merge(population, on="municipality_code")
    return pd.DataFrame({
        "indicator": "households_per_1000_people",
        "disaggregation": "municipality=" + merged["municipality_code"],
        "value": merged["households"] / merged["population_2026"] * 1000,
        "numerator": merged["households"],
        "denominator": merged["population_2026"],
    })
```

**Replacing and deleting.** **Replace** loads a new file into an existing table: it keeps its name, and its rows and columns become those of the new file. **Delete** removes it; scripts that still read it fail from the next run. A run reads every reference table once when it starts, so replacing a table during a run does not affect that run; the next run uses the new rows. The run's details list the reference tables it used, with their row counts and when each was last updated.

## Rules for what scripts return

- Scripts 1 and 2 return at least one table. **Table names** must already use lowercase letters, digits and underscores, start with a letter and have at most 63 characters (`households`, `persons_2026`), because the dashboards select tables by name.
- **Column names** are adjusted for you the same way question names are (`Household size` → `household_size`); the run log lists every renamed column. If two columns of a table end up with the same name (`Age` and `age`), the step fails.
- A **named index** (for example after `groupby`) is published as columns; an unnamed index is dropped.
- **Geometry columns** (geopandas) are published as text in WKT format.
- **Row order** is kept: the clean data dashboard lists a table's rows in the order your script returned them until a member sorts by a column, so sort the DataFrame (`sort_values`) if the order matters to your readers.
- Script 3's table is published as `indicators`.
- **Column labels.** Only the flattened columns carry a [question label](#question-labels). The dashboard shows it under the column name when there is one, so a table you build in script 2 shows names only; use descriptive column names (`household_size`) for those.

A step that fails publishes nothing: the dashboards keep showing the last successful results.

## When scripts run

- **After a synchronization** that brought new, changed or deleted submissions (on by default).
- **On a schedule**, every 1 to 168 hours, counted from the start of the last run.
- **Run now**, for analysts and admins.

Admins change the first two under **When scripts run** in the Scripts section. A webhook delivery never starts a run by itself. A project has at most one run waiting: if one is already queued, another trigger does not add a second; if one is running, one more is queued to follow it.

Saving a script does not start a run; the next run uses the latest version. A run records which version of each script it used.

## The run log

**Runs** lists every run with its status, trigger, the script versions it used and how long it took. **Details** shows each step:

- **Succeeded**, with rows in and out and the tables it published.
- **Failed**, with the error. For an exception, the traceback shows the lines of your script that raised it.
- **Skipped**, when the script does not exist or an earlier step failed.

The log of each step holds your `ctx.log` messages and printed output, and for flattening the reference tables the run read, the count of values that could not be read, every choice-label fallback (see [Choice labels](#choice-labels)), and where the column labels came from, the columns that have none because their question is not in the deployed version, or why no labels were applied (see [Question labels](#question-labels)). Below the steps, **Reference data used** lists those tables with their row counts and when each was last updated.

## Limits

- Each script may run for **10 minutes** and use **1024 MB** of memory, unless the operator changed the limits (see [operations.md](operations.md)). A script that goes over is stopped and its step fails with a message naming the limit. Tests on a sample have the same limits.
- Available libraries: **pandas 3.0, pyarrow 25, numpy 2.5, polars 1.44, duckdb 1.5, geopandas 1.2, openpyxl 3.1** and the Python 3.12 standard library. Scripts cannot install other packages; importing one that is not there fails with `ModuleNotFoundError`.
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
| `p_<id>_indicators` | The `indicators` table (absent without script 3). |

Each schema is replaced as a whole when its step succeeds.

**Column comments.** The columns of the flattened tables hold their [question labels](#question-labels) as PostgreSQL column comments, in `p_<id>_raw` and, when there is no script 1, in `p_<id>_clean`. They are published in the same swap as the tables, so a reader never sees tables with the comments of another run. To list them:

```sql
SELECT a.attname AS "column", d.description AS label
FROM pg_attribute a
JOIN pg_description d ON d.objoid = a.attrelid AND d.objsubid = a.attnum
WHERE a.attrelid = 'p_<id>_raw.main'::regclass;
```
