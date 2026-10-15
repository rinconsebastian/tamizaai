# {{project}}: a sample to work on your computer

This folder holds {{submissions}} of the project **{{project}}** (a sample of {{requested}} was requested), taken from Tamiza on {{created}}. With it you can write a script in Jupyter or any Python editor, check it here, and upload it to Tamiza.

{{script1}}

**This folder contains the project's submissions.** Keep it as safely as the survey data itself: do not put it in a shared drive or a public repository, and delete it when you no longer need it.

## What is in this folder

| Path | What it is |
|---|---|
| `getting-started.ipynb` | A notebook to open first: loads the sample, looks at it, writes an indicator and checks it. |
| `raw/` | The flattened tables (Parquet), the input of script 1. |
| `clean/` | What script 1 returned on them, the input of script 2 and of the indicators. Absent when the project has no script 1 or it failed on the sample. |
| `reference/` | The project's reference tables, as `ctx.reference` gives them. |
| `context.json` | The form dictionary, choice labels and column labels that `ctx` is built from. |
| `manifest.json` | What this download holds and the Python and library versions of the server that made it. |
| `requirements.txt` | The libraries Tamiza scripts can use, at the server's versions. |
| `logs/` | The log of the flatten step and of script 1 on this sample. |
| `tamiza_sdk/` | The small package that reads this folder and checks scripts. It is the code the server uses. |

## Set up

You need Python {{python_minor}}, the version the server runs. Open a terminal in this folder:

```
python -m venv .venv
source .venv/bin/activate   # on Windows: .venv\Scripts\activate
pip install -r requirements.txt jupyterlab
jupyter lab
```

Then open `getting-started.ipynb`. Because you start Jupyter in this folder, `import tamiza_sdk` finds the package in `tamiza_sdk/` and the functions find the data without being told where it is.

## The three functions

```python
import tamiza_sdk as tz

ctx = tz.load_context()  # the ctx a script receives: ctx.form, ctx.reference, ctx.log, ctx.show
raw = tz.load_tables("raw")  # a dictionary of DataFrames: script 1's input
cleaned = tz.load_tables("clean")  # script 2's and the indicators' input

result = tz.check("my_indicator.py", "indicator", name="my_indicator")
result.tables  # the tables the script would publish
result.dashboard  # for an indicator: its measures, dimensions and so on, as the dashboard will see it
```

- `load_tables` gives the tables with the column types a script gets on the server, and new objects on each call.
- `check(script, kind, name=None)` runs a contract function (`clean`, `prepare_output` or `compute`), or a `.py` file, on the sample. The kind is `"clean"`, `"output"` or `"indicator"`; an indicator needs its name. It validates what the script returns with the same messages as a run, renames columns as a run does, and returns the tables as they would be published. An exception in your script is raised as it is.
- Pass `tables=` to `check` to give a script other inputs, for example the result of checking your script 1: `tz.check("script2.py", "output", tables=first.tables)`.
- If script 1 failed on the sample, `load_tables("clean")` says so and tells you to use `"raw"`; `logs/clean.txt` has the error.

## From the notebook to Tamiza

1. Explore in the notebook, where variables stay in memory.
2. Move the function, and what it imports and defines, into a `.py` file. A file is run in a fresh namespace, as on the server, so a helper you defined only in the notebook is not visible to it.
3. `tz.check("file.py", ...)` until it passes.
4. In Tamiza, in the **Scripts** section, select the script, click **Upload .py** and choose the file.
5. **Test on sample** to see it on the server, then **Save**.

For the same sample and script, `check` and a test return the same tables.

## What is different from the server

- No time or memory limit, and no isolation: your script runs in your Python.
- One sample, the one in this folder, not the whole project.
- Your Python and libraries may differ from the server's. The SDK warns once when they do; use `requirements.txt` to get the same versions.
- Do not rely on the network or on your own files in a script: on the server it has only the tables it is given, and a future release removes its network access.
