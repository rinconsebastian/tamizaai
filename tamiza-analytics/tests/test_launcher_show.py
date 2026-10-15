"""``ctx.show`` and running through a cell, in real child processes."""

import textwrap

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.launcher import Limits, run_script

CONTEXT = {"project_name": "P", "questions": [], "choices": []}
LIMITS = Limits(timeout_seconds=20, memory_mb=1024)


@pytest.fixture
def inputs(tmp_path):
    path = tmp_path / "main.parquet"
    interchange.write_parquet(pd.DataFrame({"age": [30, 41, 17]}), path)
    return {"main": path}


def run(tmp_path, inputs, code, limits=LIMITS, **options):
    return run_script(
        tmp_path / "work",
        "indicator",
        textwrap.dedent(code),
        inputs,
        CONTEXT,
        tmp_path / "out",
        limits,
        name="coverage",
        **options,
    )


def test_shown_values_come_back_when_the_script_fails(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            ctx.show(7)
            raise KeyError("age")
    """,
        show=True,
    )

    assert not outcome.succeeded
    assert "KeyError" in outcome.error
    assert [(s.name, s.rows, s.text) for s in outcome.shown] == [("main", 3, None), ("shown 2", 1, "7")]
    assert interchange.read_parquet(outcome.shown[0].path)["age"].tolist() == [30, 41, 17]
    assert not (tmp_path / "work" / "indicator").exists()


def test_shown_values_come_back_when_the_script_is_stopped_by_the_time_limit(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        import time

        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            time.sleep(60)
    """,
        limits=Limits(timeout_seconds=2, memory_mb=1024),
        show=True,
    )

    assert not outcome.succeeded
    assert outcome.error_code == "limit.time"
    assert [s.name for s in outcome.shown] == ["main"]
    assert outcome.shown[0].path.is_file()


def test_nothing_is_shown_in_a_run(tmp_path, inputs):
    outcome = run(
        tmp_path,
        inputs,
        """
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            return cleaned["main"]
    """,
    )

    assert outcome.succeeded
    assert outcome.shown == []
    assert outcome.stopped_at is None
