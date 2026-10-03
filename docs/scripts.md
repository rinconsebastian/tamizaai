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

**Columns** are named after the questions (`age`, not `respondent/age`), lowercased, with accents removed and other characters turned into underscores. When two questions of the same table have the same name (for example `age` in the groups `head` and `spouse`), the second one gets its full path: `spouse_age`.

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

**Types** come from the form: `integer` → integer, `decimal` and `range` → float, `date` → date, `start`, `end` and `dateTime` → timestamp in UTC. Everything else is text, including `select_one`, `select_multiple` (choice names separated by spaces, as Kobo sends them), `time` and `geopoint`. An answer that cannot be read as its question's type (for example `n/a` in an integer question) becomes empty, and the flatten step's log counts them per column. Answers to questions no longer in the form are kept as text columns.

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

```python
def prepare_output(cleaned, ctx):
    main = cleaned["main"].copy()
    main["municipality_label"] = [ctx.form.label("municipality", value) for value in main["municipality"]]

    size = cleaned["members"].groupby("_parent_id").size().rename("household_size")
    households = main.merge(size, left_on="_id", right_index=True, how="left").fillna({"household_size": 0})

    return {"households": households[["_id", "municipality", "municipality_label", "household_size"]]}
```

### 3. `compute_indicators(cleaned, ctx)`

Receives the cleaned tables; returns **one** DataFrame with the columns `indicator`, `disaggregation`, `value`, `numerator` and `denominator`. Other columns are allowed.

```python
import pandas as pd


def compute_indicators(cleaned, ctx):
    main = cleaned["main"]
    rows = []
    for label, group in [("total", main), *[(f"municipality={m}", g) for m, g in main.groupby("municipality")]]:
        with_water = int((group["water_source"] == "piped").sum())
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
| `ctx.form.questions` | DataFrame of the form's questions: `name`, `path`, `type`, `label`, `list_name`. |
| `ctx.form.choices` | DataFrame of the choice lists: `list_name`, `name`, `label`. |
| `ctx.form.label(question, value)` | The label of a choice, by question name or path; `None` if unknown. |
| `ctx.log` | A Python logger: `ctx.log.info(...)`, `ctx.log.warning(...)`. Messages appear in the run log. |
| `ctx.project_name` | The project's name. |

`print()` also writes to the run log. Labels come from the form's first language.

## Rules for what scripts return

- Scripts 1 and 2 return at least one table. **Table names** must already use lowercase letters, digits and underscores, start with a letter and have at most 63 characters (`households`, `persons_2026`), because the dashboards select tables by name.
- **Column names** are adjusted for you the same way question names are (`Household size` → `household_size`); the run log lists every renamed column. If two columns of a table end up with the same name (`Age` and `age`), the step fails.
- A **named index** (for example after `groupby`) is published as columns; an unnamed index is dropped.
- **Geometry columns** (geopandas) are published as text in WKT format.
- Script 3's table is published as `indicators`.

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

The log of each step holds your `ctx.log` messages and printed output, and for flattening the count of values that could not be read.

## Limits

- Each script may run for **10 minutes** and use **1024 MB** of memory, unless the operator changed the limits (see [operations.md](operations.md)). A script that goes over is stopped and its step fails with a message naming the limit.
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
