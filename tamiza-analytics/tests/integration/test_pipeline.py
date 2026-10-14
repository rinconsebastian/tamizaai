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
SHARE_CALI = """
import pandas as pd

def compute(cleaned, ctx):
    main = cleaned["main"]
    total = len(main)
    cali = int((main["municipality"] == "Cali").sum())
    return pd.DataFrame([{"scope": "total", "value": cali / total, "numerator": cali, "denominator": total}])
"""
ATTENDANCE = """
import pandas as pd

def compute(cleaned, ctx):
    return pd.DataFrame({"municipality": cleaned["main"]["municipality"], "value": cleaned["main"]["age"]})
"""
FAILING = "def compute(cleaned, ctx):\n    raise ValueError('boom')\n"
NO_NUMBER = "import pandas as pd\n\ndef compute(cleaned, ctx):\n    return pd.DataFrame({'municipality': ['Cali']})\n"
SUCCEEDED_STEPS = {
    ("flatten", ""): "succeeded",
    ("clean", ""): "succeeded",
    ("output", ""): "succeeded",
}
WITHOUT_OUTPUT = {**SUCCEEDED_STEPS, ("output", ""): "skipped"}


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


def steps(database, run_id) -> dict[tuple[str, str], tuple]:
    """The run's steps by (step, name): status, input rows, output rows, log and error."""
    rows = database.rows(
        "SELECT step, name, status, input_rows, output_rows, log, error FROM tamiza.run_steps WHERE run_id = %s",
        run_id,
    )
    return {(row[0], row[1]): row[2:] for row in rows}


def statuses(database, run_id) -> dict[tuple[str, str], str]:
    return {key: values[0] for key, values in steps(database, run_id).items()}


def run_status(database, run_id) -> tuple:
    return database.rows("SELECT status, error_code, error FROM tamiza.runs WHERE id = %s", run_id)[0]


def tables(database, project, step) -> list[str]:
    rows = database.rows(
        "SELECT table_name FROM information_schema.tables WHERE table_schema = %s ORDER BY table_name",
        schema_name(project.id, step),
    )
    return [row[0] for row in rows]


def schema_exists(database, project, step) -> bool:
    return bool(database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname = %s", schema_name(project.id, step)))


def indicator_table(database, project, name) -> list[tuple]:
    return database.rows(f'SELECT * FROM "{schema_name(project.id, "indicators")}"."{name}" ORDER BY 1, 2')


def code(text):
    return textwrap.dedent(text)


def test_a_full_run_publishes_every_schema(database, seeded, settings):
    add_script(database, seeded, "clean", code(CLEAN))
    add_script(database, seeded, "output", code(OUTPUT))
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali", label="Share of Cali")
    add_script(database, seeded, "indicator", code(ATTENDANCE), name="attendance")
    run_id = queue_run(database, seeded)

    assert process(database, settings) is True

    assert run_status(database, run_id) == ("succeeded", None, None)
    recorded = steps(database, run_id)
    assert statuses(database, run_id) == {
        **SUCCEEDED_STEPS,
        ("indicator", "attendance"): "succeeded",
        ("indicator", "share_cali"): "succeeded",
    }
    assert recorded[("flatten", "")][1:3] == (3, 9)  # 3 submissions; 3 main rows + 6 members
    assert recorded[("clean", "")][1:3] == (9, 8)
    assert "INFO Removed 1 minors" in recorded[("clean", "")][3]
    assert recorded[("indicator", "share_cali")][1:3] == (8, 1)
    assert recorded[("indicator", "attendance")][1:3] == (8, 2)
    assert tables(database, seeded, "raw") == ["main", "members"]
    assert tables(database, seeded, "clean") == ["main", "members"]
    output_schema = schema_name(seeded.id, "output")
    output = database.rows(
        f'SELECT municipality, households, municipality_code FROM "{output_schema}".households ORDER BY 1'
    )
    assert output == [("Buga", 1, "buga"), ("Cali", 1, "cali")]
    # One table per indicator, each with the columns its script returned.
    assert tables(database, seeded, "indicators") == ["attendance", "share_cali"]
    assert indicator_table(database, seeded, "share_cali") == [("total", 0.5, 1, 2)]
    assert indicator_table(database, seeded, "attendance") == [("Buga", 35), ("Cali", 40)]
    assert database.scalar("SELECT clean_version_id IS NOT NULL AND output_version_id IS NOT NULL FROM tamiza.runs")
    assert database.scalar("SELECT indicator_versions FROM tamiza.runs WHERE id = %s", run_id) == [
        {
            "name": "attendance",
            "number": 1,
            "versionId": str(database.scalar("SELECT id FROM tamiza.script_versions WHERE name = 'attendance'")),
        },
        {
            "name": "share_cali",
            "number": 1,
            "versionId": str(database.scalar("SELECT id FROM tamiza.script_versions WHERE name = 'share_cali'")),
        },
    ]
    assert not (settings.run_work_dir / str(run_id)).exists()


def test_the_log_of_every_step_records_its_duration(database, seeded, settings):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    add_script(database, seeded, "indicator", code(ATTENDANCE), name="attendance")
    run_id = queue_run(database, seeded)

    process(database, settings)

    recorded = steps(database, run_id)
    assert len(recorded) == 5
    for key, values in recorded.items():
        if values[0] != "skipped":  # a skipped step ran nothing
            assert "Duration:" in values[3], key


def test_indicators_run_one_after_another_in_name_order(database, seeded, settings):
    for name in ("zeta", "alpha", "mid_rate"):
        add_script(database, seeded, "indicator", code(ATTENDANCE), name=name)
    run_id = queue_run(database, seeded)

    process(database, settings)

    order = database.rows(
        "SELECT name FROM tamiza.run_steps WHERE run_id = %s AND step = 'indicator' ORDER BY started_at", run_id
    )
    assert [name for (name,) in order] == ["alpha", "mid_rate", "zeta"]


def test_without_scripts_clean_is_the_raw_data_and_there_are_no_output_or_indicator_schemas(database, seeded, settings):
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded"
    recorded = steps(database, run_id)
    # Step 2 is marked skipped and there are no indicator steps at all.
    assert {key: values[0] for key, values in recorded.items()} == {
        ("flatten", ""): "succeeded",
        ("clean", ""): "succeeded",
        ("output", ""): "skipped",
    }
    assert "flattened tables are published as the clean data" in recorded[("clean", "")][3]
    assert database.scalar(f'SELECT count(*) FROM "{schema_name(seeded.id, "clean")}".main') == 3
    assert not schema_exists(database, seeded, "output")
    assert not schema_exists(database, seeded, "indicators")


def test_removing_the_last_indicator_removes_the_indicator_schema(database, seeded, settings):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    queue_run(database, seeded)
    process(database, settings)
    assert tables(database, seeded, "indicators") == ["share_cali"]

    add_script(database, seeded, "indicator", None, name="share_cali")
    run_id = queue_run(database, seeded)
    process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded"
    assert not schema_exists(database, seeded, "indicators")
    assert [key for key in steps(database, run_id) if key[0] == "indicator"] == []


def test_a_removed_indicator_loses_its_table_and_the_others_are_untouched(database, seeded, settings):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    add_script(database, seeded, "indicator", code(ATTENDANCE), name="old_rate")
    queue_run(database, seeded)
    process(database, settings)
    assert tables(database, seeded, "indicators") == ["old_rate", "share_cali"]

    add_script(database, seeded, "indicator", None, name="old_rate")
    run_id = queue_run(database, seeded)
    process(database, settings)

    assert tables(database, seeded, "indicators") == ["share_cali"]
    assert (
        "Removed the tables of indicators that no longer exist: old_rate."
        in steps(database, run_id)[("indicator", "share_cali")][3]
    )


def test_a_failing_clean_script_keeps_the_previous_publication_and_skips_the_rest(database, seeded, settings):
    add_script(database, seeded, "clean", code(CLEAN))
    add_script(database, seeded, "output", code(OUTPUT))
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    queue_run(database, seeded)
    process(database, settings)

    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return {'main': raw['main']['weight']}\n")
    run_id = queue_run(database, seeded)
    process(database, settings)

    status, error_code, error = run_status(database, run_id)
    assert (status, error_code) == ("failed", "step.clean")
    assert error == "KeyError: 'weight'"
    recorded = steps(database, run_id)
    assert recorded[("clean", "")][0] == "failed" and 'File "script.py", line 2, in clean' in recorded[("clean", "")][4]
    assert (recorded[("output", "")][0], recorded[("indicator", "share_cali")][0]) == ("skipped", "skipped")
    assert database.scalar(f'SELECT count(*) FROM "{schema_name(seeded.id, "clean")}".main') == 2  # previous run's data
    assert tables(database, seeded, "output") == ["households"]
    assert tables(database, seeded, "indicators") == ["share_cali"]


def test_a_failing_flatten_step_skips_the_indicators_too(database, seeded, settings, monkeypatch):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    monkeypatch.setattr(pipeline, "flatten", lambda *args, **kwargs: (_ for _ in ()).throw(RuntimeError("disk full")))
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert statuses(database, run_id) == {
        ("flatten", ""): "failed",
        ("clean", ""): "skipped",
        ("output", ""): "skipped",
        ("indicator", "share_cali"): "skipped",
    }
    assert run_status(database, run_id)[:2] == ("failed", "step.flatten")


def test_a_failing_output_script_does_not_stop_the_indicators(database, seeded, settings):
    add_script(database, seeded, "output", "def prepare_output(cleaned, ctx):\n    raise ValueError('boom')\n")
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="share_cali")
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert run_status(database, run_id)[:2] == ("failed", "step.output")
    recorded = statuses(database, run_id)
    assert (recorded[("output", "")], recorded[("indicator", "share_cali")]) == ("failed", "succeeded")
    assert tables(database, seeded, "indicators") == ["share_cali"]


def test_one_failing_indicator_keeps_its_table_and_the_others_publish(database, seeded, settings):
    add_script(database, seeded, "indicator", code(ATTENDANCE), name="attendance")
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="coverage")
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="piped_water_rate")
    queue_run(database, seeded)
    process(database, settings)
    assert indicator_table(database, seeded, "attendance") == [("Buga", 35), ("Cali", 15), ("Cali", 40)]

    add_script(database, seeded, "indicator", FAILING, name="attendance")
    add_script(database, seeded, "indicator", code(SHARE_CALI).replace("cali / total", "0.25"), name="coverage")
    add_submissions(database, seeded, [submission(4, "buga", 60)])
    run_id = queue_run(database, seeded)
    process(database, settings)

    status, error_code, error = run_status(database, run_id)
    assert (status, error_code) == ("failed", "step.indicator")
    assert error == "Indicator attendance: ValueError: boom"
    assert statuses(database, run_id) == {
        **WITHOUT_OUTPUT,
        ("indicator", "attendance"): "failed",
        ("indicator", "coverage"): "succeeded",
        ("indicator", "piped_water_rate"): "succeeded",
    }
    assert "ValueError: boom" in steps(database, run_id)[("indicator", "attendance")][4]
    # The failed indicator keeps what it had; the others were recomputed over the new submission.
    assert indicator_table(database, seeded, "attendance") == [("Buga", 35), ("Cali", 15), ("Cali", 40)]
    assert indicator_table(database, seeded, "coverage")[0][1] == 0.25
    assert indicator_table(database, seeded, "piped_water_rate")[0][3] == 4


def test_an_indicator_without_a_numeric_column_fails_and_keeps_its_previous_table(database, seeded, settings):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="coverage")
    queue_run(database, seeded)
    process(database, settings)

    add_script(database, seeded, "indicator", NO_NUMBER, name="coverage")
    run_id = queue_run(database, seeded)
    process(database, settings)

    assert steps(database, run_id)[("indicator", "coverage")][4] == (
        "The indicator table has no numeric column; add at least one, such as `value`."
    )
    assert indicator_table(database, seeded, "coverage") == [("total", 2 / 3, 2, 3)]  # without script 1, all 3 rows


def test_the_run_uses_the_versions_current_when_it_was_claimed(database, seeded, settings):
    first = add_script(
        database, seeded, "clean", "def clean(raw, ctx):\n    return {'main': raw['main'], 'v1': raw['main']}\n"
    )
    coverage_1 = add_script(database, seeded, "indicator", code(SHARE_CALI), name="coverage")
    run_id = queue_run(database, seeded)

    with database.connect() as connection:
        claimed = pipeline.claim(connection)
        add_script(
            database, seeded, "clean", "def clean(raw, ctx):\n    return {'main': raw['main'], 'v2': raw['main']}\n"
        )
        add_script(database, seeded, "indicator", FAILING, name="coverage")
        # Created while the run runs: this run does not compute it, the next does.
        add_script(database, seeded, "indicator", code(SHARE_CALI), name="attendance")
        pipeline.execute(connection, claimed, settings)

    assert claimed.versions["clean"] == first
    assert [(i.name, i.version_id, i.number) for i in claimed.indicators] == [("coverage", coverage_1, 1)]
    assert database.scalar("SELECT clean_version_id FROM tamiza.runs WHERE id = %s", run_id) == first
    assert run_status(database, run_id)[0] == "succeeded"
    assert tables(database, seeded, "clean") == ["main", "v1"]
    assert tables(database, seeded, "indicators") == ["coverage"]


def test_an_indicator_added_after_a_run_started_is_computed_by_the_next_run(database, seeded, settings):
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="coverage")
    queue_run(database, seeded)
    with database.connect() as connection:
        claimed = pipeline.claim(connection)
        add_script(database, seeded, "indicator", code(ATTENDANCE), name="attendance")
        pipeline.execute(connection, claimed, settings)
    assert tables(database, seeded, "indicators") == ["coverage"]

    queue_run(database, seeded)
    process(database, settings)

    assert tables(database, seeded, "indicators") == ["attendance", "coverage"]


def test_an_indicator_name_is_versioned_apart_from_the_others(database, seeded, settings):
    add_script(database, seeded, "indicator", FAILING, name="coverage")
    add_script(database, seeded, "indicator", code(SHARE_CALI), name="coverage")
    add_script(database, seeded, "indicator", code(ATTENDANCE), name="attendance")
    run_id = queue_run(database, seeded)

    process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded"
    assert database.scalar("SELECT indicator_versions FROM tamiza.runs WHERE id = %s", run_id) is not None
    assert [
        (i["name"], i["number"])
        for i in database.scalar("SELECT indicator_versions FROM tamiza.runs WHERE id = %s", run_id)
    ] == [("attendance", 1), ("coverage", 2)]


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

def compute(cleaned, ctx):
    households = ctx.reference["households"]
    counts = cleaned["main"].groupby("municipality").size().rename("n").reset_index()
    # The tables hold labels; the reference table is keyed by codes, so the join goes through ctx.form.name.
    counts["code"] = [ctx.form.name("municipality", label) for label in counts["municipality"]]
    merged = counts.merge(households, on="code")
    return pd.DataFrame({
        "code": merged["code"],
        "value": merged["n"] / merged["total"],
        "numerator": merged["n"],
        "denominator": merged["total"],
    })
"""


def coverage(database, project) -> dict[str, int]:
    rows = database.rows(f'SELECT code, denominator FROM "{schema_name(project.id, "indicators")}".coverage')
    return {code: int(denominator) for code, denominator in rows}


def test_scripts_read_reference_tables_and_the_run_records_them(database, seeded, settings):
    add_reference_table(
        database, seeded, "households", [("code", "text"), ("total", "integer")], [["cali", 200], ["buga", 50]]
    )
    add_script(database, seeded, "indicator", code(PER_HOUSEHOLD), name="coverage")
    run_id = queue_run(database, seeded)

    assert process(database, settings)

    assert run_status(database, run_id)[0] == "succeeded", steps(database, run_id)
    assert coverage(database, seeded) == {"cali": 200, "buga": 50}
    (recorded,) = database.scalar("SELECT reference_tables FROM tamiza.runs WHERE id = %s", run_id)
    assert recorded["name"] == "households" and recorded["rows"] == 2 and recorded["updatedAt"]
    assert "Reference tables: households (2 rows)." in steps(database, run_id)[("flatten", "")][3]
    assert not (settings.run_work_dir / str(run_id)).exists()


def test_a_run_without_reference_tables_records_none(database, seeded, settings):
    run_id = queue_run(database, seeded)

    assert process(database, settings)

    assert database.scalar("SELECT reference_tables FROM tamiza.runs WHERE id = %s", run_id) == []
    assert "No reference tables." in steps(database, run_id)[("flatten", "")][3]


def test_a_table_replaced_during_a_run_is_seen_only_by_the_next_run(database, seeded, settings, monkeypatch):
    table_id = add_reference_table(
        database, seeded, "households", [("code", "text"), ("total", "integer")], [["cali", 200], ["buga", 50]]
    )
    add_script(database, seeded, "indicator", code(PER_HOUSEHOLD), name="coverage")
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

    assert coverage(database, seeded) == {"cali": 200, "buga": 50}
    first_updated = database.scalar("SELECT reference_tables->0->>'updatedAt' FROM tamiza.runs WHERE id = %s", first)

    second = queue_run(database, seeded)
    assert process(database, settings)

    assert coverage(database, seeded) == {"cali": 400, "buga": 80}
    second_updated = database.scalar("SELECT reference_tables->0->>'updatedAt' FROM tamiza.runs WHERE id = %s", second)
    assert second_updated > first_updated
