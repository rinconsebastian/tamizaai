"""Scripts in real child processes: limits, environment, clean-up and outputs."""

import textwrap

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.launcher import Limits, child_environment, run_script

CONTEXT = {"project_name": "P", "questions": [], "choices": []}


@pytest.fixture
def inputs(tmp_path):
    path = tmp_path / "main.parquet"
    interchange.write_parquet(pd.DataFrame({"age": [30, 41, 17]}), path)
    return {"main": path}


DEFAULT_LIMITS = Limits(timeout_seconds=20, memory_mb=1024)


def run(tmp_path, inputs, code, kind="clean", limits=DEFAULT_LIMITS):
    return run_script(tmp_path / "work", kind, textwrap.dedent(code), inputs, CONTEXT, tmp_path / "out", limits)


def test_outputs_are_moved_out_and_the_step_directory_removed(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        def clean(raw, ctx):
            open("scratch.txt", "w").write("left behind?")
            print("hello")
            return {"main": raw["main"][raw["main"]["age"] >= 18]}
    """,
    )

    assert outcome.succeeded, outcome.error
    assert [(t.name, t.rows) for t in outcome.tables] == [("main", 2)]
    assert (tmp_path / "out" / "main.parquet").is_file()
    assert "hello" in outcome.log
    assert list((tmp_path / "work").iterdir()) == []


def test_a_slow_script_is_stopped_at_the_time_limit(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import time
        def clean(raw, ctx):
            time.sleep(30)
    """,
        limits=Limits(timeout_seconds=1, memory_mb=1024),
    )

    assert not outcome.succeeded
    assert outcome.error_code == "limit.time"
    assert "time limit" in outcome.error


def test_a_script_using_too_much_memory_is_stopped(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import time
        def clean(raw, ctx):
            hog = b"x" * (600 * 1024 * 1024)
            time.sleep(30)
    """,
        limits=Limits(timeout_seconds=20, memory_mb=300),
    )

    assert outcome.error_code == "limit.memory"
    assert "memory limit" in outcome.error


def test_memory_of_processes_the_script_starts_counts_too(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import subprocess, sys
        def clean(raw, ctx):
            subprocess.run([sys.executable, "-c", "import time; hog = b'x' * (600 * 1024 * 1024); time.sleep(30)"])
    """,
        limits=Limits(timeout_seconds=20, memory_mb=300),
    )

    assert outcome.error_code == "limit.memory"


def test_the_script_sees_no_database_settings(tmp_path, inputs, monkeypatch):
    monkeypatch.setenv("PGPASSWORD", "secret-db-password")
    monkeypatch.setenv("TAMIZA_ANALYTICS_DATABASE_URL", "host=db user=tamiza")

    outcome = run(
        tmp_path,
        inputs,
        """
        import os
        def clean(raw, ctx):
            print(sorted(os.environ.items()))
            return raw
    """,
    )

    assert outcome.succeeded
    assert "secret-db-password" not in outcome.log
    assert "PGPASSWORD" not in outcome.log and "TAMIZA_" not in outcome.log
    assert not any(key.startswith(("PG", "TAMIZA_")) for key in child_environment(tmp_path))


def test_a_missing_library_fails_with_module_not_found(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import requests_not_installed
        def clean(raw, ctx):
            return raw
    """,
    )

    assert not outcome.succeeded
    assert "ModuleNotFoundError: No module named 'requests_not_installed'" in outcome.error


def test_script_errors_carry_the_traceback(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        def clean(raw, ctx):
            return {"main": raw["main"]["weight"]}
    """,
    )

    assert outcome.error.startswith("KeyError: 'weight'")
    assert 'File "script.py", line 3, in clean' in outcome.error


def test_a_result_naming_a_path_is_rejected(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import json, os, atexit
        def clean(raw, ctx):
            # Overwrite the runner's result after it is written.
            forged = {"status": "succeeded", "tables": [{"name": "../../etc"}]}
            atexit.register(lambda: open("result.json", "w").write(json.dumps(forged)))
            return raw
    """,
    )

    assert not outcome.succeeded
    assert "invalid table" in outcome.error


def test_reference_tables_reach_the_script_as_copies(tmp_path, inputs):
    source = tmp_path / "population.parquet"
    interchange.write_parquet(pd.DataFrame({"n": [1, 2]}), source)

    outcome = run_script(
        tmp_path / "work",
        "clean",
        textwrap.dedent("""
            def clean(raw, ctx):
                print("rows:", len(ctx.reference["population"]))
                open("reference/population.parquet", "wb").write(b"overwritten")
                return raw
        """),
        inputs,
        CONTEXT,
        tmp_path / "out",
        DEFAULT_LIMITS,
        reference={"population": source},
    )

    assert outcome.succeeded, outcome.error
    assert "rows: 2" in outcome.log
    assert interchange.read_parquet(source)["n"].tolist() == [1, 2]
