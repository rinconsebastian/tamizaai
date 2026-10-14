"""The whole chain against PostgreSQL: claim, flatten, scripts in child processes, publishing, step records."""

import textwrap

import pytest
from helpers import add_reference_table, add_script, add_submissions, queue_run, submission

from tamiza import pipeline
from tamiza.names import schema_name
from tamiza.settings import Settings

CLEAN = """
def clean(raw, ctx):
    main = raw["main"]
    ctx.log.info("Removed %d minors", int((main["age"] < 18).sum()))
    return {"main": main[main["age"] >= 18], "members": raw["members"]}
"""
OUTPUT = """
def prepare_output(cleaned, ctx):
    main = cleaned["main"]
    counts = main.groupby("municipality").size().rename("households").reset_index()
    counts["municipality_code"] = [ctx.form.name("municipality", m) for m in counts["municipality"]]
    return {"households": counts}
"""
INDICATORS = """
import pandas as pd

def compute_indicators(cleaned, ctx):
    main = cleaned["main"]
    total = len(main)
    cali = int((main["municipality"] == "Cali").sum())
    return pd.DataFrame([{"indicator": "share_cali", "disaggregation": "total", "value": cali / total,
                          "numerator": cali, "denominator": total}])
"""


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def seeded(database, project):
    add_submissions(
        database, project, [submission(1, "cali", 40), submission(2, "buga", 35), submission(3, "cali", 15)]
    )
    return project


def process(database, settings) -> bool:
    with database.connect() as connection:
        return pipeline.Processor(settings)(connection)


def steps(database, run_id) -> dict[str, tuple]:
    rows = database.rows(
        "SELECT step, status, input_rows, output_rows, log, error FROM tamiza.run_steps WHERE run_id = %s", run_id
    )
    return {row[0]: row[1:] for row in rows}


def run_status(database, run_id) -> tuple:
    return database.rows("SELECT status, error_code, error FROM tamiza.runs WHERE id = %s", run_id)[0]


def tables(database, project, step) -> list[str]:
    rows = database.rows(
        "SELECT table_name FROM information_schema.tables WHERE table_schema = %s ORDER BY table_name",
        schema_name(project.id, step),
    )
    return [row[0] for row in rows]


def code(text):
    return textwrap.dedent(text)


def test_a_full_run_publishes_every_schema(database, seeded, settings):
    add_script(database, seeded, "clean", code(CLEAN))
    add_script(database, seeded, "output", code(OUTPUT))
    add_script(database, seeded, "indicators", code(INDICATORS))
    run_id = queue_run(database, seeded)

    assert process(database, settings) is True

    assert run_status(database, run_id) == ("succeeded", None, None)
    recorded = steps(database, run_id)
    assert {step: values[0] for step, values in recorded.items()} == dict.fromkeys(pipeline.STEPS, "succeeded")
    assert recorded["flatten"][1:3] == (3, 9)  # 3 submissions; 3 main rows + 6 members
    assert recorded["clean"][1:3] == (9, 8)
    assert "INFO Removed 1 minors" in recorded["clean"][3]
    assert tables(database, seeded, "raw") == ["main", "members"]
    assert tables(database, seeded, "clean") == ["main", "members"]
    output_schema = schema_name(seeded.id, "output")
    output = database.rows(
        f'SELECT municipality, households, municipality_code FROM "{output_schema}".households ORDER BY 1'
    )
    assert output == [("Buga", 1, "buga"), ("Cali", 1, "cali")]
    indicator = database.rows(
        f'SELECT indicator, numerator, denominator FROM "{schema_name(seeded.id, "indicators")}".indicators'
    )
    assert indicator == [("share_cali", 1, 2)]
    assert (
        database.scalar("SELECT clean_version_id IS NOT NULL AND output_version_id IS NOT NULL FROM tamiza.runs")
        is True
    )
    assert not (settings.run_work_dir / str(run_id)).exists()


def test_without_scripts_clean_is_the_raw_data_and_steps_2_and_3_are_skipped(database, seeded, settings):
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded"
    recorded = steps(database, run_id)
    assert (recorded["clean"][0], recorded["output"][0], recorded["indicators"][0]) == (
        "succeeded",
        "skipped",
        "skipped",
    )
    assert "flattened tables are published as the clean data" in recorded["clean"][3]
    assert database.scalar(f'SELECT count(*) FROM "{schema_name(seeded.id, "clean")}".main') == 3
    assert tables(database, seeded, "output") == [] and tables(database, seeded, "indicators") == []


def test_removing_a_script_removes_its_published_schema(database, seeded, settings):
    add_script(database, seeded, "indicators", code(INDICATORS))
    queue_run(database, seeded)
    process(database, settings)
    assert tables(database, seeded, "indicators") == ["indicators"]

    add_script(database, seeded, "indicators", None)
    queue_run(database, seeded)
    process(database, settings)

    assert tables(database, seeded, "indicators") == []


def test_a_failing_clean_script_keeps_the_previous_publication_and_skips_2_and_3(database, seeded, settings):
    add_script(database, seeded, "clean", code(CLEAN))
    add_script(database, seeded, "output", code(OUTPUT))
    queue_run(database, seeded)
    process(database, settings)

    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return {'main': raw['main']['weight']}\n")
    run_id = queue_run(database, seeded)
    process(database, settings)

    status, error_code, error = run_status(database, run_id)
    assert (status, error_code) == ("failed", "step.clean")
    assert error == "KeyError: 'weight'"
    recorded = steps(database, run_id)
    assert recorded["clean"][0] == "failed" and 'File "script.py", line 2, in clean' in recorded["clean"][4]
    assert (recorded["output"][0], recorded["indicators"][0]) == ("skipped", "skipped")
    assert database.scalar(f'SELECT count(*) FROM "{schema_name(seeded.id, "clean")}".main') == 2  # previous run's data
    assert tables(database, seeded, "output") == ["households"]


def test_a_failing_output_script_does_not_stop_the_indicators(database, seeded, settings):
    add_script(database, seeded, "output", "def prepare_output(cleaned, ctx):\n    raise ValueError('boom')\n")
    add_script(database, seeded, "indicators", code(INDICATORS))
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert run_status(database, run_id)[:2] == ("failed", "step.output")
    recorded = steps(database, run_id)
    assert (recorded["output"][0], recorded["indicators"][0]) == ("failed", "succeeded")
    assert tables(database, seeded, "indicators") == ["indicators"]


def test_a_missing_indicator_column_publishes_nothing(database, seeded, settings):
    add_script(database, seeded, "indicators", code(INDICATORS))
    queue_run(database, seeded)
    process(database, settings)

    add_script(database, seeded, "indicators", code(INDICATORS.replace('"denominator": total', '"total": total')))
    run_id = queue_run(database, seeded)
    process(database, settings)

    assert steps(database, run_id)["indicators"][4] == "The indicator table is missing the column denominator."
    assert database.scalar(f'SELECT denominator FROM "{schema_name(seeded.id, "indicators")}".indicators') == 3


def test_the_run_uses_the_versions_current_when_it_was_claimed(database, seeded, settings):
    first = add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return {'v1': raw['main']}\n")
    run_id = queue_run(database, seeded)

    with database.connect() as connection:
        claimed = pipeline.claim(connection)
        add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return {'v2': raw['main']}\n")
        pipeline.execute(connection, claimed, settings)

    assert claimed.versions["clean"] == first
    assert database.scalar("SELECT clean_version_id FROM tamiza.runs WHERE id = %s", run_id) == first
    assert tables(database, seeded, "clean") == ["v1"]


def test_deleted_submissions_are_left_out(database, seeded, settings):
    add_submissions(database, seeded, [submission(99, "buga", 50)], deleted=True)
    queue_run(database, seeded)

    process(database, settings)

    assert database.scalar(f'SELECT count(*) FROM "{schema_name(seeded.id, "raw")}".main') == 3


def test_a_run_left_running_is_marked_interrupted_at_startup(database, seeded, settings):
    stale = queue_run(database, seeded, status="running")
    queued = queue_run(database, seeded)

    assert process(database, settings) is True

    assert run_status(database, stale)[:2] == ("failed", "run.interrupted")
    assert run_status(database, queued)[0] == "succeeded"


def test_nothing_queued(database, project, settings):
    assert process(database, settings) is False


def test_claiming_a_run_moves_the_schedule_to_one_interval_after_its_start(database, seeded, settings):
    database.execute("UPDATE tamiza.projects SET run_interval_hours = 24, next_run_at = now() - interval '1 minute'")
    run_id = queue_run(database, seeded)

    with database.connect() as connection:
        pipeline.claim(connection)

    assert database.scalar(
        "SELECT p.next_run_at = r.started_at + interval '24 hours'"
        " FROM tamiza.projects p JOIN tamiza.runs r ON r.id = %s",
        run_id,
    )


PER_HOUSEHOLD = """
import pandas as pd

def compute_indicators(cleaned, ctx):
    households = ctx.reference["households"]
    counts = cleaned["main"].groupby("municipality").size().rename("n").reset_index()
    # The tables hold labels; the reference table is keyed by codes, so the join goes through ctx.form.name.
    counts["code"] = [ctx.form.name("municipality", label) for label in counts["municipality"]]
    merged = counts.merge(households, on="code")
    return pd.DataFrame({
        "indicator": "coverage",
        "disaggregation": merged["code"],
        "value": merged["n"] / merged["total"],
        "numerator": merged["n"],
        "denominator": merged["total"],
    })
"""


def indicators(database, project) -> dict[str, int]:
    rows = database.rows(
        f'SELECT disaggregation, denominator FROM "{schema_name(project.id, "indicators")}".indicators'
    )
    return {disaggregation: int(denominator) for disaggregation, denominator in rows}


def test_scripts_read_reference_tables_and_the_run_records_them(database, seeded, settings):
    add_reference_table(
        database, seeded, "households", [("code", "text"), ("total", "integer")], [["cali", 200], ["buga", 50]]
    )
    add_script(database, seeded, "indicators", code(PER_HOUSEHOLD))
    run_id = queue_run(database, seeded)

    assert process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded", steps(database, run_id)
    assert indicators(database, seeded) == {"cali": 200, "buga": 50}
    (recorded,) = database.scalar("SELECT reference_tables FROM tamiza.runs WHERE id = %s", run_id)
    assert recorded["name"] == "households" and recorded["rows"] == 2 and recorded["updatedAt"]
    assert "Reference tables: households (2 rows)." in steps(database, run_id)["flatten"][3]
    assert not (settings.run_work_dir / str(run_id)).exists()


def test_a_run_without_reference_tables_records_none(database, seeded, settings):
    run_id = queue_run(database, seeded)

    assert process(database, settings)

    assert database.scalar("SELECT reference_tables FROM tamiza.runs WHERE id = %s", run_id) == []
    assert "No reference tables." in steps(database, run_id)["flatten"][3]


def test_a_table_replaced_during_a_run_is_seen_only_by_the_next_run(database, seeded, settings, monkeypatch):
    table_id = add_reference_table(
        database, seeded, "households", [("code", "text"), ("total", "integer")], [["cali", 200], ["buga", 50]]
    )
    add_script(database, seeded, "indicators", code(PER_HOUSEHOLD))
    original = pipeline.flatten

    def flatten_while_replacing(*args, **kwargs):
        # The run has read its reference tables; an analyst replaces one now.
        database.execute(
            'UPDATE tamiza.reference_table_contents SET rows = \'[["cali", 400], ["buga", 80]]\' '
            "WHERE reference_table_id = %s",
            table_id,
        )
        database.execute(
            "UPDATE tamiza.reference_tables SET updated_at = now() + interval '1 minute' WHERE id = %s", table_id
        )
        return original(*args, **kwargs)

    monkeypatch.setattr(pipeline, "flatten", flatten_while_replacing)
    first = queue_run(database, seeded)
    assert process(database, settings)
    monkeypatch.setattr(pipeline, "flatten", original)

    assert indicators(database, seeded) == {"cali": 200, "buga": 50}
    first_updated = database.scalar("SELECT reference_tables->0->>'updatedAt' FROM tamiza.runs WHERE id = %s", first)

    second = queue_run(database, seeded)
    assert process(database, settings)

    assert indicators(database, seeded) == {"cali": 400, "buga": 80}
    second_updated = database.scalar("SELECT reference_tables->0->>'updatedAt' FROM tamiza.runs WHERE id = %s", second)
    assert second_updated > first_updated
