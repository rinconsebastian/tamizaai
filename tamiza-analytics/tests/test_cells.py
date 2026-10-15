"""Stopping at a ``# %%`` marker: where the stop call is placed, and what a stopped script returns."""

import json
import textwrap

import pandas as pd
import pytest

from tamiza import cells, interchange, shows
from tamiza.runner import run_step


@pytest.fixture
def step(tmp_path):
    def make(code):
        (tmp_path / "input").mkdir(exist_ok=True)
        interchange.write_parquet(
            pd.DataFrame({"municipality": ["cali", "buga", "cali"], "age": [30, 41, 17]}),
            tmp_path / "input" / "main.parquet",
        )
        (tmp_path / "context.json").write_text(json.dumps({"project_name": "P", "questions": [], "choices": []}))
        (tmp_path / "script.py").write_text(textwrap.dedent(code).lstrip("\n"))
        return tmp_path

    return make


def line_of(code, text):
    """The 1-based line of the first line of the dedented code that contains ``text``."""
    for number, line in enumerate(textwrap.dedent(code).lstrip("\n").split("\n"), start=1):
        if text in line:
            return number
    raise AssertionError(text)


UNFINISHED = """
    def compute(cleaned, ctx):
        # %% Load
        main = cleaned["main"]
        ctx.show(main, "main")
        # %% Rate
        rate = main.groupby("nonexistent").size()
        ctx.show(rate, "rate")
        # %% Return
        return rate
"""


def test_a_script_stops_at_the_end_of_its_first_cell_with_no_validation(step):
    step_dir = step(UNFINISHED)
    stop = line_of(UNFINISHED, "# %% Rate")

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=stop)

    assert result == {"status": "stopped", "line": stop}
    assert [entry["name"] for entry in shows.read_index(step_dir / "shows")] == ["main"]
    assert not (step_dir / "output").exists()


def test_the_next_cell_would_have_failed_without_the_stop(step):
    step_dir = step(UNFINISHED)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(UNFINISHED, "# %% Return"))

    assert result["status"] == "failed"
    assert "nonexistent" in result["error"]
    assert [entry["name"] for entry in shows.read_index(step_dir / "shows")] == ["main"]


def test_a_stopped_script_is_not_validated_even_when_it_would_return_nothing_valid(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            # %% Rest
            return "not a data frame"
    """)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=3)

    assert result == {"status": "stopped", "line": 3}


def test_a_marker_in_a_loop_stops_in_the_first_iteration(step):
    code = """
        def compute(cleaned, ctx):
            for municipality in ["cali", "buga", "palmira"]:
                ctx.show(municipality)
                # %% Per municipality
                ctx.show("after")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert [entry["text"] for entry in shows.read_index(step_dir / "shows")] == ["cali"]


def test_a_marker_that_is_never_reached_lets_the_script_run_to_its_end(step):
    code = """
        def compute(cleaned, ctx):
            main = cleaned["main"]
            if len(main) > 100:
                # %% Big
                ctx.show("big")
            return main
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "succeeded"
    assert result["tables"] == [{"name": "coverage", "rows": 3}]


def test_a_marker_after_the_last_statement_of_a_block_belongs_to_the_enclosing_block(step):
    code = """
        def compute(cleaned, ctx):
            for number in range(3):
                ctx.show(number)
            # %% After the loop
            ctx.show("after")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert [entry["text"] for entry in shows.read_index(step_dir / "shows")] == ["0", "1", "2"]


def test_a_marker_indented_as_the_end_of_a_block_stops_inside_that_block(step):
    code = """
        def compute(cleaned, ctx):
            for number in range(3):
                ctx.show(number)
                # %% End of the body
            ctx.show("after")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert [entry["text"] for entry in shows.read_index(step_dir / "shows")] == ["0"]


def test_a_marker_at_module_level_stops_before_the_function_runs(step):
    code = """
        import pandas as pd

        # %% Setup
        LIMIT = 10

        def compute(cleaned, ctx):
            ctx.show("never")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert shows.read_index(step_dir / "shows") == []


def test_a_marker_before_the_first_statement_of_a_function_stops_at_once(step):
    code = """
        def compute(cleaned, ctx):
            # %% Start
            ctx.show("never")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert shows.read_index(step_dir / "shows") == []


def test_a_marker_inside_a_statement_that_continues_over_lines_fails_with_a_message(step):
    code = """
        def compute(cleaned, ctx):
            main = cleaned["main"].assign(
                # %% Inside the call
                double=2,
            )
            return main
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "failed"
    assert "own line between two statements" in result["error"]


def test_a_stop_line_that_is_not_a_marker_fails_with_a_message(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            return cleaned["main"]
    """)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=2)

    assert result["status"] == "failed"
    assert "not a # %% marker" in result["error"]


def test_nothing_is_kept_between_runs_of_the_same_step_directory(step):
    first = """
        cache = "kept?"

        def compute(cleaned, ctx):
            # %% Stop
            return cleaned["main"]
    """
    step_dir = step(first)
    run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(first, "# %%"))
    (step_dir / "script.py").write_text(
        textwrap.dedent("""
        def compute(cleaned, ctx):
            ctx.show(cache)
            return cleaned["main"]
    """)
    )

    result = run_step(step_dir, "indicator", "coverage", show=True)

    assert result["status"] == "failed"
    assert "NameError" in result["error"]


def test_a_traceback_after_the_inserted_call_keeps_the_scripts_line_numbers(step):
    code = """
        def compute(cleaned, ctx):
            main = cleaned["main"]
            if len(main) > 100:
                # %% Never reached
                ctx.show("big")
            return main["missing"]
    """
    step_dir = step(code)

    # The stop call is inserted at the marker but never reached, so the script fails on the line after it.
    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "failed"
    assert f'script.py", line {line_of(code, "main[")}, in compute' in result["traceback"]


def test_the_stop_call_is_not_swallowed_by_except_exception(step):
    code = """
        def compute(cleaned, ctx):
            try:
                ctx.show("before")
                # %% Inside try
                ctx.show("after")
            except Exception:
                ctx.show("swallowed")
            return cleaned["main"]
    """
    step_dir = step(code)

    result = run_step(step_dir, "indicator", "coverage", show=True, stop_line=line_of(code, "# %%"))

    assert result["status"] == "stopped"
    assert [entry["text"] for entry in shows.read_index(step_dir / "shows")] == ["before"]


def test_is_marker_accepts_leading_spaces_and_tabs_only():
    assert cells.is_marker("# %% Load")
    assert cells.is_marker("    # %%")
    assert cells.is_marker("\t# %%")
    assert not cells.is_marker("x = 1  # %%")
    assert not cells.is_marker("#%%")
