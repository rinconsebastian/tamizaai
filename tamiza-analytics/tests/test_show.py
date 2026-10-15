"""``ctx.show`` in the child-side runner, called in-process."""

import json
import textwrap

import pandas as pd
import pytest

from tamiza import interchange, shows
from tamiza.runner import run_step

CONTEXT = {"project_name": "Household survey", "questions": [], "choices": []}


@pytest.fixture
def step(tmp_path):
    def make(code):
        (tmp_path / "input").mkdir(exist_ok=True)
        interchange.write_parquet(pd.DataFrame({"age": [30, 41, 17]}), tmp_path / "input" / "main.parquet")
        (tmp_path / "context.json").write_text(json.dumps(CONTEXT))
        (tmp_path / "script.py").write_text(textwrap.dedent(code))
        return tmp_path

    return make


def shown(step_dir):
    return shows.read_index(step_dir / "shows")


def test_show_records_a_table_with_its_name_line_and_rows(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            main = cleaned["main"]
            ctx.show(main, "main")
            return main
    """)

    result = run_step(step_dir, "indicator", "coverage", show=True)

    assert result["status"] == "succeeded"
    [entry] = shown(step_dir)
    # Line 1 of the dedented text is blank, so the call is on line 4.
    assert (entry["name"], entry["line"], entry["rows"], entry["text"]) == ("main", 4, 3, None)
    assert interchange.read_parquet(step_dir / "shows" / entry["file"])["age"].tolist() == [30, 41, 17]


def test_show_returns_its_argument_and_keeps_the_rows_the_value_had_at_the_call(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            main = cleaned["main"]
            ctx.show(main, "before")
            main = ctx.show(main[main["age"] > 20], "adults")
            main["age"] = 0
            return main
    """)

    run_step(step_dir, "indicator", "coverage", show=True)

    before, adults = shown(step_dir)
    assert (before["rows"], adults["rows"]) == (3, 2)
    assert interchange.read_parquet(step_dir / "shows" / adults["file"])["age"].tolist() == [30, 41]


def test_a_value_that_is_not_a_table_is_shown_as_text_with_a_numbered_name(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(len(cleaned["main"]))
            ctx.show("done", "message")
            ctx.show({"a": 1})
            return cleaned["main"]
    """)

    run_step(step_dir, "indicator", "coverage", show=True)

    first, second, third = shown(step_dir)
    assert (first["name"], first["text"], first["file"]) == ("shown 1", "3", None)
    assert (second["name"], second["text"]) == ("message", "done")
    assert (third["name"], third["text"]) == ("shown 3", "{'a': 1}")


def test_a_series_is_shown_as_a_one_column_table(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"]["age"], "ages")
            ctx.show(cleaned["main"]["age"] * 2)
            return cleaned["main"]
    """)

    run_step(step_dir, "indicator", "coverage", show=True)

    ages, doubled = shown(step_dir)
    assert interchange.read_parquet(step_dir / "shows" / ages["file"]).columns.tolist() == ["age"]
    assert interchange.read_parquet(step_dir / "shows" / doubled["file"]).columns.tolist() == ["age"]


def test_a_long_text_is_cut_and_only_the_first_rows_of_a_table_are_kept(step):
    step_dir = step("""
        import pandas as pd

        def compute(cleaned, ctx):
            ctx.show("x" * 5000, "long")
            ctx.show(pd.DataFrame({"n": range(1000)}), "many")
            return cleaned["main"]
    """)

    run_step(step_dir, "indicator", "coverage", show=True)

    long, many = shown(step_dir)
    assert len(long["text"]) == 2001 and long["text"].endswith("…")
    assert many["rows"] == 1000
    assert len(interchange.read_parquet(step_dir / "shows" / many["file"])) == 100


def test_a_value_that_cannot_be_written_is_shown_as_the_error(step, monkeypatch):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            return cleaned["main"]
    """)

    def broken(frame, path):
        raise OSError("disk full")

    monkeypatch.setattr("tamiza.shows.interchange.write_parquet", broken)

    run_step(step_dir, "indicator", "coverage", show=True)

    [entry] = shown(step_dir)
    assert entry["file"] is None
    assert "OSError: disk full" in entry["text"]


def test_show_does_nothing_in_a_run(step, capsys):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            return ctx.show(cleaned["main"])
    """)

    result = run_step(step_dir, "indicator", "coverage")

    assert result["status"] == "succeeded"
    assert not (step_dir / "shows").exists()
    assert capsys.readouterr().out == ""


def test_calls_beyond_twenty_are_counted_in_the_log(step, capsys):
    step_dir = step("""
        def compute(cleaned, ctx):
            for number in range(25):
                ctx.show(number)
            return cleaned["main"]
    """)

    run_step(step_dir, "indicator", "coverage", show=True)

    assert [entry["text"] for entry in shown(step_dir)] == [str(n) for n in range(20)]
    assert "5 calls were not kept" in capsys.readouterr().out


def test_previews_are_kept_when_the_script_then_raises(step):
    step_dir = step("""
        def compute(cleaned, ctx):
            ctx.show(cleaned["main"], "main")
            return cleaned["main"]["missing"]
    """)

    result = run_step(step_dir, "indicator", "coverage", show=True)

    assert result["status"] == "failed"
    assert "KeyError" in result["error"]
    assert [entry["name"] for entry in shown(step_dir)] == ["main"]


def test_the_index_skips_a_line_cut_by_a_kill_and_lines_naming_other_files(tmp_path):
    directory = tmp_path / "shows"
    directory.mkdir()
    (directory / "1.parquet").write_bytes(b"x")
    lines = [
        '{"name": "ok", "line": 1, "rows": 2, "file": "1.parquet", "text": null}',
        '{"name": "escape", "line": 1, "rows": 2, "file": "../script.py", "text": null}',
        '{"name": "missing", "line": 1, "rows": 2, "file": "9.parquet", "text": null}',
        '{"name": "neither", "line": 1, "rows": 2, "file": null, "text": null}',
        '{"name": "cut", "line": 1, "ro',
    ]
    (directory / "index.jsonl").write_text("\n".join(lines))

    assert [entry["name"] for entry in shows.read_index(directory)] == ["ok"]
