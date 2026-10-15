"""A script checked on a downloaded sample gives the tables a sample test of it gives (design D2).

The check runs from the unzipped download, in its own process and with the SDK included in the zip, as an analyst would
run it. Its tables are compared with the test's previews: names, columns, types, row counts and the first 100 rows.
"""

import subprocess
import sys
import textwrap

import pyarrow.parquet as pq
import pytest
from helpers import add_script, add_submissions, submission
from test_sample_downloads import prepare
from test_sample_runs import CLEAN, queue_test, result, run_next, table

from tamiza import preview
from tamiza.settings import Settings

# Column names that a run renames, a mix of types and an empty value: what published tables are made of.
SCRIPT_1 = """
def clean(raw, ctx):
    main = raw["main"].copy()
    main["Age Group"] = main["age"].map(lambda age: "adult" if age >= 18 else "minor")
    return {"main": main, "members": raw["members"]}
"""
SCRIPT_2 = """
def prepare_output(cleaned, ctx):
    main = cleaned["main"]
    counts = main.groupby("municipality").size().rename("Household Count").reset_index()
    counts["Share (%)"] = counts["Household Count"] / len(main)
    return {"households": counts, "ages": main[["_id", "age"]]}
"""
INDICATOR = """
import pandas as pd

def compute(cleaned, ctx):
    main = cleaned["main"]
    rows = main.groupby("municipality").agg(numerator=("age", "sum"), denominator=("age", "size")).reset_index()
    rows["value"] = rows["numerator"] / rows["denominator"]
    rows["Notes"] = ctx.form.label("municipality", "cali")
    return rows
"""

DRIVER = """
import sys
from pathlib import Path

sys.path.insert(0, ".")  # the unzipped folder: the SDK included in the download is the one that runs
import tamiza_sdk as tz
from tamiza_sdk import tables

kind, name, script, out = sys.argv[1:5]
result = tz.check(script, kind, name or None, show=False)
for table, frame in result.tables.items():
    tables.write_parquet(frame, Path(out) / f"{table}.parquet")
"""


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def seeded(database, project):
    add_submissions(
        database,
        project,
        [submission(1, "cali", 40), submission(2, "buga", 35), submission(3, "cali", 15), submission(4, "buga", 22)],
    )
    add_script(database, project, "clean", CLEAN)
    return project


def checked_locally(folder, tmp_path, kind, name, code):
    script = tmp_path / "draft.py"
    script.write_text(textwrap.dedent(code), encoding="utf-8")
    driver = tmp_path / "driver.py"
    driver.write_text(DRIVER, encoding="utf-8")
    out = tmp_path / "checked"
    out.mkdir()
    finished = subprocess.run(
        [sys.executable, "-I", str(driver), kind, name or "", str(script), str(out)],
        cwd=folder,
        capture_output=True,
        text=True,
        check=False,
    )
    assert finished.returncode == 0, finished.stdout + finished.stderr
    return {
        path.stem: preview.table_preview(path.stem, pq.ParquetFile(path).metadata.num_rows, path)
        for path in sorted(out.glob("*.parquet"))
    }


@pytest.mark.parametrize(
    ("kind", "name", "code"),
    [("clean", None, SCRIPT_1), ("output", None, SCRIPT_2), ("indicator", "coverage", INDICATOR)],
    ids=["script 1", "script 2", "indicator"],
)
def test_a_check_on_the_download_gives_the_tables_a_sample_test_gives(
    database, seeded, settings, tmp_path, kind, name, code
):
    test_id = queue_test(database, seeded, kind, code, name=name)
    run_next(database, settings)
    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    tested = {t["name"]: t for t in outcome["steps"][kind]["tables"]}

    _, folder = prepare(database, seeded, settings, tmp_path)
    local = checked_locally(folder, tmp_path, kind, name, code)

    assert sorted(local) == sorted(tested)
    for table_name, from_test in tested.items():
        assert local[table_name] == from_test, table_name
    # The renames are part of what is compared: the script's own names are not the published ones.
    published = {column["name"] for t in tested.values() for column in t["columns"]}
    assert published & {"age_group", "household_count", "share", "notes"}


def test_the_comparison_sees_a_difference_in_a_value(database, seeded, settings, tmp_path):
    test_id = queue_test(database, seeded, "indicator", INDICATOR, name="coverage")
    run_next(database, settings)
    from_test = table(result(database, test_id)["steps"]["indicator"], "coverage")
    _, folder = prepare(database, seeded, settings, tmp_path)

    local = checked_locally(folder, tmp_path, "indicator", "coverage", INDICATOR.replace('"cali"', '"buga"'))

    assert local["coverage"] != from_test  # the label of the `Notes` column differs
