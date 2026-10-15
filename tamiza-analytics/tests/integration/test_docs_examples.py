"""The examples in docs/scripts.md run as written, on submissions of two form versions that relabel a choice.

The code blocks are read from the document itself, so an example that stops working breaks this test instead of
misleading a reader.
"""

import re
from pathlib import Path

import pytest
from helpers import add_form_version, add_reference_table, add_script, add_submissions, column_comments, queue_run
from psycopg.types.json import Jsonb

from tamiza import pipeline
from tamiza.names import schema_name
from tamiza.settings import Settings

DOCUMENT = Path(__file__).resolve().parents[3] / "docs" / "scripts.md"

FIELDS = [
    {"name": "start", "xpath": "start", "type": "start", "label": None, "listName": None},
    {"name": "enumerator", "xpath": "enumerator", "type": "text", "label": "Enumerator", "listName": None},
    {
        "name": "municipality",
        "xpath": "municipality",
        "type": "select_one_municipalities",
        "label": "Municipality",
        "listName": "municipalities",
    },
    {
        "name": "water_source",
        "xpath": "water_source",
        "type": "select_one_water_sources",
        "label": "Water source",
        "listName": "water_sources",
    },
    {"name": "member_age", "xpath": "members/member_age", "type": "integer", "label": "Age", "listName": None},
]
WATER = [
    {"listName": "water_sources", "name": "piped", "label": "Piped"},
    {"listName": "water_sources", "name": "well", "label": "Well"},
]
# Version vA called Cali "Cali (old)"; the current form, vB, calls it "Cali".
VERSION_A = [
    {"listName": "municipalities", "name": "cali", "label": "Cali (old)"},
    {"listName": "municipalities", "name": "buga", "label": "Buga"},
    *WATER,
]
VERSION_B = [
    {"listName": "municipalities", "name": "cali", "label": "Cali"},
    {"listName": "municipalities", "name": "buga", "label": "Buga"},
    *WATER,
]


def examples() -> dict[str, str]:
    """The Python blocks of docs/scripts.md that define a script, by what they are."""
    blocks = re.findall(r"```python\n(.*?)```", DOCUMENT.read_text(encoding="utf-8"), flags=re.DOTALL)
    found = {}
    for block in blocks:
        if "tamiza_sdk" in block:  # the examples of "Writing scripts on your computer" run in a download, below
            continue
        if "def clean(raw, ctx)" in block:
            found["clean"] = block
        elif "def prepare_output(" in block and "ctx.form.columns" in block:
            found["dictionary"] = block
        elif "def prepare_output(" in block:
            found["output"] = block
        elif "def compute(" in block and "# %%" in block:
            found["cells"] = block
        elif "def compute(" in block:
            found[
                "reference" if "ctx.reference" in block else "series" if "_submission_time" in block else "indicators"
            ] = block
    return found


def local_examples() -> list[str]:
    """The Python blocks of docs/scripts.md that use the SDK on a downloaded sample, in the order of the document."""
    blocks = re.findall(r"```python\n(.*?)```", DOCUMENT.read_text(encoding="utf-8"), flags=re.DOTALL)
    return [block for block in blocks if "tamiza_sdk" in block]


def answers(kobo_id, enumerator, municipality, water_source, ages):
    return {
        "_id": kobo_id,
        "_uuid": f"uuid-{kobo_id}",
        "_submission_time": "2026-09-01T10:00:00",
        "start": "2026-09-01T09:40:00.000-05:00",
        "enumerator": enumerator,
        "municipality": municipality,
        "water_source": water_source,
        "members": [{"members/member_age": str(age)} for age in ages],
    }


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=120)


@pytest.fixture
def surveyed(database, project):
    """Four submissions: kobo 1 and 2 made with vA, 3 and 4 with vB; kobo 2 is a test the cleaning script drops."""
    database.execute(
        "UPDATE tamiza.projects SET form_fields = %s, form_choices = %s WHERE id = %s",
        Jsonb(FIELDS),
        Jsonb(VERSION_B),
        project.id,
    )
    add_form_version(database, project, "vA", VERSION_A, deployed_at="2026-09-01T00:00:00Z", fields=FIELDS)
    add_form_version(database, project, "vB", VERSION_B, deployed_at="2026-09-10T00:00:00Z", fields=FIELDS)
    # vB is the deployed version: the column labels are read from its definition.
    database.execute("UPDATE tamiza.projects SET form_version = 'vB' WHERE id = %s", project.id)
    rows = [
        answers(1, "ana", "cali", "piped", [30, 200]),  # 200 is a data-entry error the example removes
        answers(2, "test-1", "buga", "well", [5]),
        answers(3, "luis", "cali", "piped", [41, 8, 62]),
        answers(4, "eva", "buga", "piped", [25]),
    ]
    add_submissions(database, project, rows, versions={1: "vA", 2: "vA", 3: "vB", 4: "vB"})
    return project


def run(database, settings, project) -> str:
    run_id = queue_run(database, project)
    with database.connect() as connection:
        assert pipeline.Processor(settings)(connection)
    status = database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id)
    steps = database.rows("SELECT step, status, error FROM tamiza.run_steps WHERE run_id = %s", run_id)
    assert status == "succeeded", steps
    return run_id


def test_the_document_has_every_example():
    assert set(examples()) == {"clean", "output", "indicators", "series", "reference", "dictionary", "cells"}


def test_the_examples_run_on_submissions_of_two_versions_that_relabel_a_choice(database, surveyed, settings):
    add_script(database, surveyed, "clean", examples()["clean"])
    add_script(database, surveyed, "output", examples()["output"])
    add_script(database, surveyed, "indicator", examples()["indicators"], name="piped_water_rate", format="percent")
    add_script(database, surveyed, "indicator", examples()["series"], name="households_surveyed", format="count")

    run(database, settings, surveyed)

    clean = schema_name(surveyed.id, "clean")
    assert database.rows(f'SELECT _id, municipality, water_source FROM "{clean}".main ORDER BY _id') == [
        (1, "Cali (old)", "Piped"),
        (3, "Cali", "Piped"),
        (4, "Buga", "Piped"),
    ]
    assert database.scalar(f'SELECT count(*) FROM "{clean}".members WHERE member_age IS NULL') == 1
    # Script 2 turns each label back into its code, whichever version it came from.
    households = database.rows(
        f'SELECT _id, municipality_code, household_size FROM "{schema_name(surveyed.id, "output")}".households'
        " ORDER BY _id"
    )
    assert households == [(1, "cali", 2), (3, "cali", 3), (4, "buga", 1)]
    indicators = schema_name(surveyed.id, "indicators")
    # A rate by municipality: the dimension, the rate and the two sums it comes from.
    assert database.rows(
        f'SELECT municipality, value, numerator, denominator FROM "{indicators}".piped_water_rate ORDER BY 1'
    ) == [
        ("Buga", 1.0, 1, 1),
        ("Cali", 1.0, 1, 1),
        ("Cali (old)", 1.0, 1, 1),
    ]
    # A series by month, from a named index published as a column.
    assert database.rows(f'SELECT month, value FROM "{indicators}".households_surveyed') == [("2026-09", 3)]


def test_the_reference_data_example_joins_labels_to_codes_without_losing_a_row(database, surveyed, settings):
    add_script(database, surveyed, "clean", examples()["clean"])
    add_script(database, surveyed, "indicator", examples()["reference"], name="households_per_1000_people")
    add_reference_table(
        database,
        surveyed,
        "population",
        [("municipality_code", "text"), ("population_2026", "integer")],
        [["cali", 2_200_000], ["buga", 120_000]],
    )

    run(database, settings, surveyed)

    rows = database.rows(
        "SELECT municipality_code, numerator, denominator"
        f' FROM "{schema_name(surveyed.id, "indicators")}".households_per_1000_people ORDER BY 1'
    )
    # Both labels of Cali (the old one and the current one) join the same code; nothing is lost.
    assert rows == [("buga", 1, 120_000), ("cali", 2, 2_200_000)]


def test_the_data_dictionary_example_lists_every_column_with_its_question_label(database, surveyed, settings):
    add_script(database, surveyed, "clean", examples()["clean"])
    add_script(database, surveyed, "output", examples()["dictionary"])

    run(database, settings, surveyed)

    dictionary = database.rows(
        f'SELECT "table", "column", path, label FROM "{schema_name(surveyed.id, "output")}".data_dictionary'
        ' ORDER BY "table", "column"'
    )
    assert ("main", "municipality", "municipality", "Municipality") in dictionary
    assert ("main", "water_source", "water_source", "Water source") in dictionary
    assert ("members", "member_age", "members/member_age", "Age") in dictionary
    assert ("main", "start", "start", None) in dictionary  # a question without a label is listed, with no label
    assert not [row for row in dictionary if row[1].startswith("_")]  # no system columns
    # The flattened tables of the same run carry the labels as column comments.
    assert column_comments(database, schema_name(surveyed.id, "raw"), "main")["municipality"] == "Municipality"


def test_the_cells_example_runs_whole_and_through_each_of_its_cells(database, surveyed, settings):
    from test_sample_runs import queue_test, result, run_next

    cells = examples()["cells"]
    add_script(database, surveyed, "clean", examples()["clean"])
    add_script(database, surveyed, "indicator", cells, name="piped_water_rate", format="percent")
    run(database, settings, surveyed)  # the markers and ctx.show change nothing in a run
    markers = [number for number, line in enumerate(cells.splitlines(), start=1) if line.lstrip().startswith("# %%")]
    assert len(markers) == 3

    def test(stop_line=None):
        test_id = queue_test(database, surveyed, "indicator", cells, name="piped_water_rate")
        if stop_line is not None:
            database.execute("UPDATE tamiza.script_tests SET stop_line = %s WHERE id = %s", stop_line, test_id)
        run_next(database, settings)
        outcome = result(database, test_id)
        assert outcome["status"] == "succeeded", outcome
        return outcome["steps"]["indicator"]

    first, second, whole = test(markers[1]), test(markers[2]), test()

    assert (first["status"], first["stoppedAtLine"]) == ("stopped", markers[1])
    assert [shown["name"] for shown in first["shown"]] == ["main"]
    assert (second["status"], second["stoppedAtLine"]) == ("stopped", markers[2])
    assert [shown["name"] for shown in second["shown"]] == ["main", "counts"]
    assert whole["status"] == "succeeded" and whole["stoppedAtLine"] is None
    assert [shown["name"] for shown in whole["shown"]] == ["main", "counts"]
    assert whole["tables"][0]["name"] == "piped_water_rate" and whole["tables"][0]["rows"] == 3
    assert whole["dashboard"]["ratio"] is True and whole["dashboard"]["defaultMeasure"] == "value"


KIT_README = Path(__file__).resolve().parents[2] / "src" / "tamiza" / "kit_files" / "README.md"
LOCAL_DRIVER = """
import importlib.abc
import json
import os
import sys


class Block(importlib.abc.MetaPathFinder):
    def find_spec(self, name, path, target=None):
        if name.split(".")[0] in {"tamiza", "psycopg", "duckdb"}:
            raise ImportError(f"{name} is not available on an analyst's computer")
        return None


sys.meta_path.insert(0, Block())
sys.path.insert(0, os.getcwd())  # the unzipped folder: the SDK in the download is the one that runs
namespace = {"__name__": "__main__"}
for index, source in enumerate(json.load(open(sys.argv[1], encoding="utf-8")), 1):
    exec(compile(source, f"<docs example {index}>", "exec"), namespace)
print("CHECK", repr(namespace["result"]).splitlines()[0])
chained = namespace["second"].tables["piped_water_rate"]
downloaded = namespace["result"].tables["piped_water_rate"]
print("CHAINED", len(chained), len(downloaded))
print("SAME", chained.equals(downloaded))
"""


def test_the_sdk_examples_run_in_order_in_a_downloaded_sample(database, surveyed, settings, tmp_path):
    import json
    import subprocess
    import sys

    from test_sample_downloads import prepare

    add_script(database, surveyed, "clean", examples()["clean"])
    blocks = local_examples()
    assert len(blocks) == 3  # load and look, check an indicator, chain script 1 into the indicator
    _, folder = prepare(database, surveyed, settings, tmp_path)
    (tmp_path / "blocks.json").write_text(json.dumps(blocks), encoding="utf-8")
    (tmp_path / "driver.py").write_text(LOCAL_DRIVER, encoding="utf-8")

    finished = subprocess.run(
        [sys.executable, "-I", str(tmp_path / "driver.py"), str(tmp_path / "blocks.json")],
        cwd=folder,
        capture_output=True,
        text=True,
        check=False,
    )

    assert finished.returncode == 0, finished.stdout + finished.stderr
    assert "Cali" in finished.stdout  # ctx.form.label("municipality", "cali")
    assert "CHECK Check passed for indicator 'piped_water_rate'" in finished.stdout
    # The download's clean tables and the local script 1 drop the same test submission: three households either way.
    assert "CHAINED 3 3" in finished.stdout and "SAME True" in finished.stdout


def test_the_setup_commands_of_the_document_are_the_ones_in_the_downloaded_readme():
    def commands(text: str) -> list[str]:
        block = re.search(r"```(?:bash)?\n(python -m venv.*?)```", text, flags=re.DOTALL)
        assert block, "no setup block found"
        return [line.strip() for line in block.group(1).splitlines() if line.strip()]

    document = commands(DOCUMENT.read_text(encoding="utf-8"))
    readme = commands(KIT_README.read_text(encoding="utf-8"))

    assert document == readme
    assert "pip install -r requirements.txt jupyterlab" in document
