"""Script tests of the workbench: reuse of the flattened sample and script 1, ctx.show, running through a cell, and the
dashboard facts of an indicator's table. Against PostgreSQL, like ``test_sample_runs``."""

import pytest
from helpers import add_form_version, add_reference_table, add_script, add_submissions, submission
from test_sample_runs import CLEAN, COVERAGE, OUTPUT, code, column, queue_test, result, run_next, table

from tamiza.settings import Settings

CLEAN_SCRIPT = code(CLEAN)


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def seeded(database, project):
    add_submissions(
        database, project, [submission(1, "cali", 40), submission(2, "buga", 35), submission(3, "cali", 15)]
    )
    return project


def test_a_second_test_reuses_the_flattened_sample_and_script_1_and_runs_only_the_draft(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    first = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)
    second = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)

    one, two = result(database, first)["steps"], result(database, second)["steps"]

    assert [one[s]["reused"] for s in ("flatten", "clean", "indicator")] == [False, False, False]
    assert [two[s]["reused"] for s in ("flatten", "clean", "indicator")] == [True, True, False]
    for step in ("flatten", "clean"):
        assert two[step]["inputRows"] == one[step]["inputRows"] and two[step]["outputRows"] == one[step]["outputRows"]
        assert two[step]["tables"] == one[step]["tables"]
        assert "Reused from an earlier test." in two[step]["log"]
        assert "Reused from an earlier test." not in one[step]["log"]
    assert "Sample: 3 submissions." in two["flatten"]["log"]
    assert table(two["indicator"], "coverage") == table(one["indicator"], "coverage")


def test_a_reused_result_is_the_same_as_a_computed_one(database, seeded, tmp_path):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    cached = Settings(database_url=database.url, run_work_dir=tmp_path / "on", run_timeout_seconds=60)
    off = Settings(database_url=database.url, run_work_dir=tmp_path / "off", run_timeout_seconds=60, test_cache_mb=0)
    ids = []
    for settings in (cached, cached, off, off):
        ids.append(queue_test(database, seeded, "output", OUTPUT))
        run_next(database, settings)

    steps = [result(database, test_id)["steps"] for test_id in ids]

    assert [s["flatten"]["reused"] for s in steps] == [False, True, False, False]
    assert [s["clean"]["reused"] for s in steps] == [False, True, False, False]
    for other in steps[1:]:
        for name in ("flatten", "clean", "output"):
            assert other[name]["tables"] == steps[0][name]["tables"]
            assert other[name]["outputRows"] == steps[0][name]["outputRows"]
    assert not (tmp_path / "off" / "sample-cache").exists()


def test_an_updated_submission_makes_the_next_test_flatten_again(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)
    database.execute(
        "UPDATE tamiza.raw_submissions SET updated_at = now() + interval '1 minute' "
        "WHERE kobo_id = 2 AND project_id = %s",
        seeded.id,
    )
    second = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)

    steps = result(database, second)["steps"]

    assert [steps[s]["reused"] for s in ("flatten", "clean")] == [False, False]


def test_a_deleted_submission_makes_the_next_test_flatten_again(database, seeded, settings):
    first = queue_test(database, seeded, "clean", CLEAN)
    run_next(database, settings)
    database.execute(
        "UPDATE tamiza.raw_submissions SET deleted = true WHERE kobo_id = 3 AND project_id = %s", seeded.id
    )
    second = queue_test(database, seeded, "clean", CLEAN)
    run_next(database, settings)

    steps = result(database, second)["steps"]

    assert steps["flatten"]["reused"] is False
    assert table(steps["flatten"], "main")["rows"] == 2
    assert table(result(database, first)["steps"]["flatten"], "main")["rows"] == 3


def test_a_new_version_of_script_1_reuses_the_flattened_sample_but_runs_the_new_script(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw\n")
    second = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)

    steps = result(database, second)["steps"]

    assert [steps[s]["reused"] for s in ("flatten", "clean")] == [True, False]
    assert table(steps["clean"], "main")["rows"] == 3  # the new script keeps every row; the old one dropped a minor


def test_a_replaced_reference_table_runs_script_1_again_but_not_flattening(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    add_reference_table(database, seeded, "codes", [("code", "text")], [["cali"]])
    queue_test(database, seeded, "output", OUTPUT)
    run_next(database, settings)
    database.execute("UPDATE tamiza.reference_tables SET updated_at = now() + interval '1 minute'")
    second = queue_test(database, seeded, "output", OUTPUT)
    run_next(database, settings)

    steps = result(database, second)["steps"]

    assert [steps[s]["reused"] for s in ("flatten", "clean")] == [True, False]


def test_a_different_sample_of_the_same_project_flattens_again(database, project, settings):
    add_submissions(database, project, [submission(i) for i in range(1, 41)])
    queue_test(database, project, "clean", CLEAN, sample_size=10)
    run_next(database, settings)
    second = queue_test(database, project, "clean", CLEAN, sample_size=20)
    run_next(database, settings)
    third = queue_test(database, project, "clean", CLEAN, sample_size=20)
    run_next(database, settings)

    assert result(database, second)["steps"]["flatten"]["reused"] is False
    assert result(database, third)["steps"]["flatten"]["reused"] is True


def test_labels_fetched_between_two_tests_make_the_next_one_flatten_again(database, project, settings):
    add_submissions(
        database, project, [submission(1, "cali", 40), submission(2, "buga", 35)], versions={1: "vA", 2: "vA"}
    )
    queue_test(database, project, "clean", CLEAN)
    run_next(database, settings)
    add_form_version(
        database, project, "vA", choices=[{"listName": "municipalities", "name": "cali", "label": "Santiago de Cali"}]
    )
    second = queue_test(database, project, "clean", CLEAN)
    run_next(database, settings)

    flatten = result(database, second)["steps"]["flatten"]

    assert flatten["reused"] is False
    assert "Santiago de Cali" in column(table(flatten, "main"), "municipality")


def test_a_failed_script_1_is_not_kept_for_reuse(database, seeded, settings):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw['missing']\n")
    queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)
    second = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    run_next(database, settings)

    steps = result(database, second)["steps"]

    assert steps["flatten"]["reused"] is True
    assert steps["clean"]["reused"] is False and steps["clean"]["status"] == "failed"


# ctx.show


SHOW_MAIN = """
def compute(cleaned, ctx):
    main = cleaned["main"]
    ctx.show(main, "main")
    return main.groupby("municipality").size().rename("value").reset_index()
"""


def test_a_draft_shows_previews_with_their_name_line_and_rows(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    test_id = queue_test(database, seeded, "indicator", SHOW_MAIN, name="coverage")

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    [shown] = outcome["steps"]["indicator"]["shown"]
    assert (shown["name"], shown["line"], shown["rows"], shown["text"]) == ("main", 4, 2, None)
    assert [c["name"] for c in shown["table"]["columns"]][-2:] == ["municipality", "age"]
    assert sorted(column(shown["table"], "age")) == [35, 40]
    assert outcome["steps"]["clean"]["shown"] is None


def test_the_saved_script_1_shows_nothing_in_a_test_of_an_indicator(database, seeded, settings):
    add_script(
        database, seeded, "clean", "def clean(raw, ctx):\n    ctx.show(raw['main'], 'from clean')\n    return raw\n"
    )
    test_id = queue_test(database, seeded, "indicator", SHOW_MAIN, name="coverage")

    run_next(database, settings)

    steps = result(database, test_id)["steps"]
    assert [s["name"] for s in steps["indicator"]["shown"]] == ["main"]
    assert steps["clean"]["shown"] is None


def test_previews_shown_before_a_failure_are_kept_with_the_traceback(database, seeded, settings):
    test_id = queue_test(
        database,
        seeded,
        "indicator",
        'def compute(cleaned, ctx):\n    ctx.show(cleaned["main"], "main")\n    return cleaned["main"]["missing"]\n',
        name="coverage",
    )

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed" and outcome["error_code"] == "step.indicator"
    step = outcome["steps"]["indicator"]
    assert "KeyError: 'missing'" in step["error"] and "line 3, in compute" in step["error"]
    assert [s["name"] for s in step["shown"]] == ["main"]


def test_previews_shown_before_the_time_limit_are_kept(database, seeded, tmp_path):
    limited = Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=5)
    test_id = queue_test(
        database,
        seeded,
        "indicator",
        'import time\n\ndef compute(cleaned, ctx):\n    ctx.show(cleaned["main"], "main")\n    time.sleep(60)\n',
        name="coverage",
    )

    run_next(database, limited)

    outcome = result(database, test_id)
    assert outcome["error"] == "The script was stopped because it exceeded the time limit."
    assert [s["name"] for s in outcome["steps"]["indicator"]["shown"]] == ["main"]


# Running through a cell


STOPPABLE = """
def compute(cleaned, ctx):
    # %% Load
    main = cleaned["main"]
    ctx.show(main, "main")
    # %% Rate
    return main["missing"]
"""


def queue_through(database, project, draft, stop_line, kind="indicator", name="coverage"):
    test_id = queue_test(database, project, kind, draft, name=name if kind == "indicator" else None)
    database.execute("UPDATE tamiza.script_tests SET stop_line = %s WHERE id = %s", stop_line, test_id)
    return test_id


def test_a_test_through_a_cell_stops_there_with_its_previews_and_no_validation(database, seeded, settings):
    test_id = queue_through(database, seeded, STOPPABLE, stop_line=6)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded", outcome
    assert outcome["error"] is None and outcome["error_code"] is None
    step = outcome["steps"]["indicator"]
    assert (step["status"], step["stoppedAtLine"], step["tables"], step["outputRows"]) == ("stopped", 6, [], None)
    assert [s["name"] for s in step["shown"]] == ["main"]
    assert step["dashboard"] is None


def test_the_same_draft_run_whole_fails_where_the_next_cell_does(database, seeded, settings):
    test_id = queue_test(database, seeded, "indicator", STOPPABLE, name="coverage")

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed"
    assert "KeyError: 'missing'" in outcome["steps"]["indicator"]["error"]
    assert outcome["steps"]["indicator"]["stoppedAtLine"] is None


def test_a_marker_never_reached_is_validated_as_a_whole_test(database, seeded, settings):
    draft = """
import pandas as pd

def compute(cleaned, ctx):
    if len(cleaned["main"]) > 100:
        # %% Big
        ctx.show("big")
    return pd.DataFrame({"municipality": ["Cali"], "value": [1.0]})
"""
    test_id = queue_through(database, seeded, draft, stop_line=6)

    run_next(database, settings)

    step = result(database, test_id)["steps"]["indicator"]
    assert step["status"] == "succeeded" and step["stoppedAtLine"] is None
    assert table(step, "coverage")["rows"] == 1


def test_nothing_is_kept_in_the_script_between_two_tests(database, seeded, settings):
    first = queue_through(
        database, seeded, 'cache = "kept"\n\ndef compute(cleaned, ctx):\n    # %% Stop\n    return cleaned["main"]\n', 4
    )
    run_next(database, settings)
    second = queue_test(
        database,
        seeded,
        "indicator",
        'def compute(cleaned, ctx):\n    ctx.show(cache)\n    return cleaned["main"]\n',
        name="coverage",
    )
    run_next(database, settings)

    assert result(database, first)["steps"]["indicator"]["status"] == "stopped"
    outcome = result(database, second)
    assert outcome["status"] == "failed"
    assert "NameError" in outcome["steps"]["indicator"]["error"]


def test_a_marker_inside_a_statement_fails_the_step_with_a_message(database, seeded, settings):
    draft = """
def compute(cleaned, ctx):
    main = cleaned["main"].assign(
        # %% Inside the call
        double=2,
    )
    return main
"""
    test_id = queue_through(database, seeded, draft, stop_line=4)

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "failed"
    assert "own line between two statements" in outcome["steps"]["indicator"]["error"]


def test_a_test_of_script_1_can_stop_at_a_cell_too(database, seeded, settings):
    draft = """
def clean(raw, ctx):
    ctx.show(raw["main"], "before")
    # %% Rest
    return 5
"""
    test_id = queue_through(database, seeded, draft, stop_line=4, kind="clean")

    run_next(database, settings)

    outcome = result(database, test_id)
    assert outcome["status"] == "succeeded"
    assert outcome["steps"]["clean"]["status"] == "stopped"


# The dashboard facts


def test_a_succeeded_indicator_draft_carries_the_facts_of_its_table(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    test_id = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")

    run_next(database, settings)

    dashboard = result(database, test_id)["steps"]["indicator"]["dashboard"]
    assert dashboard["measures"] == ["value", "denominator"]
    assert dashboard["defaultMeasure"] == "value"
    assert dashboard["ratio"] is False
    assert dashboard["dimensions"] == ["municipality"]
    assert dashboard["rows"] == 2
    assert [g["group"] for g in dashboard["charts"]["municipality"]["groups"]] == ["Buga", "Cali"]


def test_no_dashboard_facts_for_the_other_scripts_or_for_a_failed_indicator(database, seeded, settings):
    add_script(database, seeded, "clean", CLEAN_SCRIPT)
    output = queue_test(database, seeded, "output", OUTPUT)
    run_next(database, settings)
    failed = queue_test(database, seeded, "indicator", "def compute(cleaned, ctx):\n    return 5\n", name="coverage")
    run_next(database, settings)

    assert result(database, output)["steps"]["output"]["dashboard"] is None
    assert result(database, failed)["steps"]["indicator"]["dashboard"] is None
