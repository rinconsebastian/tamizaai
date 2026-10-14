"""Script tests against PostgreSQL: sample, saved script 1 for drafts of 2 and 3, previews, nothing published."""

import textwrap
import uuid

import pytest
from helpers import add_reference_table, add_script, add_submissions, submission

from tamiza import sample_runs
from tamiza.settings import Settings

CLEAN = """
def clean(raw, ctx):
    main = raw["main"]
    return {"main": main[main["age"] >= 18], "members": raw["members"]}
"""
OUTPUT = """
def prepare_output(cleaned, ctx):
    main = cleaned["main"]
    return {"households": main.groupby("municipality").size().rename("households").reset_index()}
"""
NO_DENOMINATOR = """
import pandas as pd

def compute_indicators(cleaned, ctx):
    return pd.DataFrame([{"indicator": "n", "disaggregation": "total", "value": 1, "numerator": 1}])
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


def code(text):
    return textwrap.dedent(text)


def queue_test(database, project, kind, draft, sample_size=500) -> uuid.UUID:
    test_id = uuid.uuid4()
    database.execute(
        """
        INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at)
        VALUES (%s, %s, %s, %s, %s, %s, 'queued', now())
        """,
        test_id,
        project.id,
        project.user_id,
        kind,
        code(draft),
        sample_size,
    )
    return test_id


def run_next(database, settings):
    with database.connect() as connection:
        claimed = sample_runs.claim(connection)
        assert claimed is not None
        sample_runs.execute(connection, claimed, settings)
        return claimed


def result(database, test_id) -> dict:
    status, error_code, error, steps = database.rows(
        "SELECT status, error_code, error, steps FROM tamiza.script_tests WHERE id = %s", test_id
    )[0]
    return {"status": status, "error_code": error_code, "error": error, "steps": {s["step"]: s for s in steps}}


def table(step, name) -> dict:
    return next(t for t in step["tables"] if t["name"] == name)


def column(preview_table, name) -> list:
    index = [c["name"] for c in preview_table["columns"]].index(name)
    return [row[index] for row in preview_table["preview"]]


def test_a_draft_of_script_1_runs_without_a_version_or_a_publication(database, seeded, settings):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw\n")
    test_id = queue_test(database, seeded, "clean", CLEAN)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    assert list(outcome["steps"]) == ["flatten", "clean"]
    flattened = table(outcome["steps"]["flatten"], "main")
    assert flattened["rows"] == 3 and sorted(column(flattened, "_id")) == [1, 2, 3]
    assert sorted(column(table(outcome["steps"]["flatten"], "members"), "member_name")) == ["m1"] * 3 + ["m2"] * 3
    cleaned = table(outcome["steps"]["clean"], "main")
    assert cleaned["rows"] == 2 and sorted(column(cleaned, "age")) == [35, 40]
    assert {"name": "age", "type": "integer"} in cleaned["columns"]
    assert outcome["steps"]["clean"]["inputRows"] == 9 and outcome["steps"]["clean"]["outputRows"] == 8
    assert database.scalar("SELECT count(*) FROM tamiza.script_versions") == 1
    assert database.scalar("SELECT count(*) FROM tamiza.runs") == 0
    assert (
        database.scalar(
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name LIKE %s", f"p_{seeded.id.hex}%"
        )
        == 0
    )
    assert not (settings.run_work_dir / f"test-{test_id}").exists()


def test_a_draft_of_script_2_runs_on_the_saved_script_1(database, seeded, settings):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw\n")
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    raise ValueError('old')\n")
    version_3 = add_script(database, seeded, "clean", code(CLEAN))
    test_id = queue_test(database, seeded, "output", OUTPUT)

    claimed = run_next(database, settings)

    assert claimed.clean_version_id == version_3
    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    assert list(outcome["steps"]) == ["flatten", "clean", "output"]
    households = table(outcome["steps"]["output"], "households")
    assert dict(zip(column(households, "municipality"), column(households, "households"), strict=True)) == {
        "Buga": 1,
        "Cali": 1,
    }
    assert database.scalar("SELECT clean_version_id FROM tamiza.script_tests WHERE id = %s", test_id) == version_3


def test_without_a_saved_script_1_the_draft_receives_the_flattened_tables(database, seeded, settings):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw\n")
    add_script(database, seeded, "clean", None)  # removed
    test_id = queue_test(database, seeded, "output", OUTPUT)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert list(outcome["steps"]) == ["flatten", "output"]
    assert sum(column(table(outcome["steps"]["output"], "households"), "households")) == 3


def test_a_failing_saved_script_1_stops_a_test_of_script_3(database, seeded, settings):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw['missing']\n")
    test_id = queue_test(database, seeded, "indicators", NO_DENOMINATOR)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed"
    assert outcome["error_code"] == "step.clean"
    assert list(outcome["steps"]) == ["flatten", "clean"]
    assert "KeyError: 'missing'" in outcome["steps"]["clean"]["error"]
    assert "line 2, in clean" in outcome["steps"]["clean"]["error"]


def test_a_missing_indicator_column_fails_with_the_runs_message(database, seeded, settings):
    test_id = queue_test(database, seeded, "indicators", NO_DENOMINATOR)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed" and outcome["error_code"] == "step.indicators"
    assert outcome["error"] == "The indicator table is missing the column denominator."


def test_a_draft_exception_shows_its_traceback_and_the_earlier_tables(database, seeded, settings):
    test_id = queue_test(database, seeded, "clean", "def clean(raw, ctx):\n    return raw['main']['nope']\n")

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["steps"]["clean"]["status"] == "failed"
    assert "line 2, in clean" in outcome["steps"]["clean"]["error"]
    assert table(outcome["steps"]["flatten"], "main")["rows"] == 3


def test_a_project_smaller_than_the_sample_uses_every_submission(database, seeded, settings):
    add_submissions(database, seeded, [submission(9, "cali", 50)], deleted=True)
    test_id = queue_test(database, seeded, "clean", CLEAN, sample_size=500)

    run_next(database, settings)

    flatten = result(database, test_id)["steps"]["flatten"]
    assert flatten["inputRows"] == 3
    assert "Sample: 3 submissions." in flatten["log"]
    assert 9 not in column(table(flatten, "main"), "_id")


def test_consecutive_tests_use_the_same_spread_sample(database, project, settings):
    add_submissions(database, project, [submission(i) for i in range(1, 41)])
    first = queue_test(database, project, "clean", CLEAN, sample_size=10)
    run_next(database, settings)
    second = queue_test(database, project, "clean", CLEAN, sample_size=10)
    run_next(database, settings)

    ids = [sorted(column(table(result(database, t)["steps"]["flatten"], "main"), "_id")) for t in (first, second)]

    assert ids[0] == ids[1]
    assert len(ids[0]) == 10
    assert ids[0] != list(range(31, 41)) and ids[0] != list(range(1, 11))  # neither only the newest nor the oldest


def test_reference_tables_reach_the_draft(database, seeded, settings):
    add_reference_table(database, seeded, "codes", [("code", "text")], [["cali"], ["buga"]])
    test_id = queue_test(
        database,
        seeded,
        "clean",
        """
        def clean(raw, ctx):
            main = raw["main"]
            # The table holds labels and the reference table codes, so the labels are turned back into codes.
            codes = main["municipality"].map(lambda label: ctx.form.name("municipality", label))
            return {"main": main[codes.isin(ctx.reference["codes"]["code"])]}
        """,
    )

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    assert "Reference tables: codes (2 rows)." in outcome["steps"]["flatten"]["log"]
    assert table(outcome["steps"]["clean"], "main")["rows"] == 3


def process(database, settings) -> bool:
    from tamiza import pipeline

    with database.connect() as connection:
        return pipeline.Processor(settings)(connection)


def test_the_worker_runs_a_queued_test_before_a_queued_run(database, seeded, settings):
    from helpers import queue_run

    run_id = queue_run(database, seeded)
    test_id = queue_test(database, seeded, "clean", CLEAN)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.script_tests WHERE id = %s", test_id) == "succeeded"
    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "queued"

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"


def test_a_test_left_running_is_marked_interrupted_at_startup(database, seeded, settings):
    test_id = queue_test(database, seeded, "clean", CLEAN)
    database.execute("UPDATE tamiza.script_tests SET status = 'running', started_at = now()")

    assert not process(database, settings)

    assert database.rows("SELECT status, error_code FROM tamiza.script_tests WHERE id = %s", test_id)[0] == (
        "failed",
        "test.interrupted",
    )


def test_tests_are_deleted_24_hours_after_they_ended(database, seeded, settings):
    old = queue_test(database, seeded, "clean", CLEAN)
    database.execute(
        "UPDATE tamiza.script_tests SET status = 'succeeded', finished_at = now() - interval '25 hours' WHERE id = %s",
        old,
    )
    recent = queue_test(database, seeded, "output", OUTPUT)
    database.execute(
        "UPDATE tamiza.script_tests SET status = 'succeeded', finished_at = now() - interval '23 hours' WHERE id = %s",
        recent,
    )

    process(database, settings)

    assert [row[0] for row in database.rows("SELECT id FROM tamiza.script_tests")] == [recent]


def test_a_test_over_the_time_limit_fails_with_the_limit_message(database, seeded, tmp_path):
    settings = Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=1)
    test_id = queue_test(database, seeded, "clean", "import time\n\ndef clean(raw, ctx):\n    time.sleep(30)\n")

    assert process(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed"
    assert outcome["error"] == "The script was stopped because it exceeded the time limit."
