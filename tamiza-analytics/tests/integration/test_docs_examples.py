"""The examples in docs/scripts.md run as written, on submissions of two form versions that relabel a choice.

The code blocks are read from the document itself, so an example that stops working breaks this test instead of
misleading a reader.
"""

import re
from pathlib import Path

import pytest
from helpers import add_form_version, add_reference_table, add_script, add_submissions, queue_run
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
        if "def clean(raw, ctx)" in block:
            found["clean"] = block
        elif "def prepare_output(" in block:
            found["output"] = block
        elif "def compute_indicators(" in block:
            found["reference" if "ctx.reference" in block else "indicators"] = block
    return found


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
    assert set(examples()) == {"clean", "output", "indicators", "reference"}


def test_the_three_examples_run_on_submissions_of_two_versions_that_relabel_a_choice(database, surveyed, settings):
    for kind in ("clean", "output", "indicators"):
        add_script(database, surveyed, kind, examples()[kind])

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
    indicators = database.rows(
        f'SELECT disaggregation, numerator, denominator FROM "{schema_name(surveyed.id, "indicators")}".indicators'
        " ORDER BY 1"
    )
    assert ("total", 3, 3) in indicators
    assert {row[0] for row in indicators} == {
        "total",
        "municipality=Buga",
        "municipality=Cali",
        "municipality=Cali (old)",
    }


def test_the_reference_data_example_joins_labels_to_codes_without_losing_a_row(database, surveyed, settings):
    add_script(database, surveyed, "clean", examples()["clean"])
    add_script(database, surveyed, "indicators", examples()["reference"])
    add_reference_table(
        database,
        surveyed,
        "population",
        [("municipality_code", "text"), ("population_2026", "integer")],
        [["cali", 2_200_000], ["buga", 120_000]],
    )

    run(database, settings, surveyed)

    rows = database.rows(
        f'SELECT disaggregation, numerator, denominator FROM "{schema_name(surveyed.id, "indicators")}".indicators'
        " ORDER BY 1"
    )
    # Both labels of Cali (the old one and the current one) join the same code; nothing is lost.
    assert rows == [("municipality=buga", 1, 120_000), ("municipality=cali", 2, 2_200_000)]
