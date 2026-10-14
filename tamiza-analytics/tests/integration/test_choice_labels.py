"""Choice labels end to end against PostgreSQL: versions recorded by the API, and runs and tests using them."""

import textwrap
import uuid

import pytest
from helpers import (
    FORM_CHOICES,
    add_form_version,
    add_reference_table,
    add_script,
    add_submissions,
    queue_run,
    submission,
)

from tamiza import pipeline, sample_runs
from tamiza.names import schema_name
from tamiza.settings import Settings

# Version vA called Cali "Cali (old)"; vB, the current form, calls it "Cali".
VERSION_A = [
    {"listName": "municipalities", "name": "cali", "label": "Cali (old)"},
    {"listName": "municipalities", "name": "buga", "label": "Buga"},
]
VERSION_B = FORM_CHOICES


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def versioned(database, project):
    """Three submissions: kobo 1 made with vA, kobo 2 and 3 with vB; both definitions available."""
    add_submissions(
        database,
        project,
        [submission(1, "cali"), submission(2, "buga"), submission(3, "cali")],
        versions={1: "vA", 2: "vB", 3: "vB"},
    )
    add_form_version(database, project, "vA", VERSION_A, deployed_at="2026-09-01T00:00:00Z")
    add_form_version(database, project, "vB", VERSION_B, deployed_at="2026-09-10T00:00:00Z")
    return project


def process(database, settings) -> bool:
    with database.connect() as connection:
        return pipeline.Processor(settings)(connection)


def municipalities(database, project, step) -> list[tuple]:
    return database.rows(f'SELECT _id, municipality FROM "{schema_name(project.id, step)}".main ORDER BY _id')


def flatten_log(database, run_id) -> str:
    return database.scalar("SELECT log FROM tamiza.run_steps WHERE run_id = %s AND step = 'flatten'", run_id)


def test_a_run_publishes_each_answer_with_the_label_of_its_own_version(database, versioned, settings):
    run_id = queue_run(database, versioned)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"
    expected = [(1, "Cali (old)"), (2, "Buga"), (3, "Cali")]
    assert municipalities(database, versioned, "raw") == expected
    # With no script 1, the flattened tables are published as the clean data.
    assert municipalities(database, versioned, "clean") == expected
    log = flatten_log(database, run_id)
    assert "form version" not in log and "without a label" not in log
    # The raw submissions are not touched.
    assert database.scalar("SELECT data->>'municipality' FROM tamiza.raw_submissions WHERE kobo_id = 1") == "cali"


def test_a_pending_version_falls_back_to_the_current_form_and_the_log_says_so(database, project, settings):
    add_submissions(
        database,
        project,
        [submission(1, "cali"), submission(2, "buga"), submission(3, "cali")],
        versions={1: "vB", 2: "vB"},
    )
    add_form_version(database, project, "vB", status="pending")
    run_id = queue_run(database, project)

    assert process(database, settings)

    assert municipalities(database, project, "raw") == [(1, "Cali"), (2, "Buga"), (3, "Cali")]
    log = flatten_log(database, run_id)
    assert "Form version vB: 2 submissions used the current form (definition pending)." in log
    assert "1 submission without a form version used the current form." in log


def test_a_failure_while_loading_labels_fails_the_flatten_step_and_publishes_nothing(database, versioned, settings):
    database.execute("UPDATE tamiza.form_versions SET fields = '\"not a list\"'::jsonb WHERE uid = 'vA'")
    run_id = queue_run(database, versioned)

    assert process(database, settings)

    assert database.rows("SELECT status, error_code FROM tamiza.runs WHERE id = %s", run_id)[0] == (
        "failed",
        "step.flatten",
    )
    statuses = dict(database.rows("SELECT step, status FROM tamiza.run_steps WHERE run_id = %s", run_id))
    assert statuses == {"flatten": "failed", "clean": "skipped", "output": "skipped", "indicators": "skipped"}
    assert database.scalar("SELECT error FROM tamiza.run_steps WHERE run_id = %s AND step = 'flatten'", run_id)
    assert (
        database.scalar(
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = %s", schema_name(versioned.id, "raw")
        )
        == 0
    )


def test_ctx_form_name_resolves_the_labels_of_every_version_a_run_translated_with(database, versioned, settings):
    add_reference_table(
        database, versioned, "households", [("code", "text"), ("total", "integer")], [["cali", 200], ["buga", 50]]
    )
    add_script(
        database,
        versioned,
        "clean",
        textwrap.dedent(
            """
            def clean(raw, ctx):
                main = raw["main"].copy()
                main["code"] = [ctx.form.name("municipality", label) for label in main["municipality"]]
                assert main["code"].tolist() == ["cali", "buga", "cali"], main["code"].tolist()
                joined = main.merge(ctx.reference["households"], on="code")
                assert len(joined) == len(main), "no row may be lost in the join"
                return {"main": main}
            """
        ),
    )
    run_id = queue_run(database, versioned)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"
    assert database.rows(
        f'SELECT municipality, code FROM "{schema_name(versioned.id, "clean")}".main ORDER BY _id'
    ) == [
        ("Cali (old)", "cali"),
        ("Buga", "buga"),
        ("Cali", "cali"),
    ]


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
        textwrap.dedent(draft),
        sample_size,
    )
    return test_id


def test_a_sample_test_receives_and_previews_labels(database, versioned, settings):
    test_id = queue_test(
        database,
        versioned,
        "clean",
        """
        def clean(raw, ctx):
            main = raw["main"]
            assert sorted(main["municipality"]) == ["Buga", "Cali", "Cali (old)"], sorted(main["municipality"])
            assert ctx.form.name("municipality", "Cali (old)") == "cali"
            return {"main": main}
        """,
    )

    with database.connect() as connection:
        claimed = sample_runs.claim(connection)
        sample_runs.execute(connection, claimed, settings)

    status, steps = database.rows("SELECT status, steps FROM tamiza.script_tests WHERE id = %s", test_id)[0]
    assert status == "succeeded", steps
    by_step = {step["step"]: step for step in steps}
    for name in ("flatten", "clean"):
        main = next(t for t in by_step[name]["tables"] if t["name"] == "main")
        index = [c["name"] for c in main["columns"]].index("municipality")
        assert sorted(row[index] for row in main["preview"]) == ["Buga", "Cali", "Cali (old)"]


def test_a_sample_test_of_script_3_gets_the_labels_through_the_saved_script_1(database, versioned, settings):
    add_script(database, versioned, "clean", "def clean(raw, ctx):\n    return raw\n")
    test_id = queue_test(
        database,
        versioned,
        "indicators",
        """
        import pandas as pd

        def compute_indicators(cleaned, ctx):
            labels = sorted(cleaned["main"]["municipality"])
            assert labels == ["Buga", "Cali", "Cali (old)"], labels
            row = {"indicator": "n", "disaggregation": "total", "value": 1, "numerator": 1, "denominator": 1}
            return pd.DataFrame([row])
        """,
    )

    with database.connect() as connection:
        claimed = sample_runs.claim(connection)
        sample_runs.execute(connection, claimed, settings)

    status, error = database.rows("SELECT status, error FROM tamiza.script_tests WHERE id = %s", test_id)[0]
    assert status == "succeeded", error
