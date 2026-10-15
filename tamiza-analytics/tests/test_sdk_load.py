"""Loading a download: ``ctx`` and the tables, matching what a script receives on the server."""

import pandas as pd
import pytest

import tamiza_sdk as tz
from tamiza import interchange
from tamiza.runner import run_step
from tamiza_sdk.context import ReferenceTableNotFound


def test_the_context_answers_label_lookups_as_on_the_server(download):
    ctx = tz.load_context(download())

    assert ctx.project_name == "Water survey 2026"
    assert ctx.form.label("municipality", "cali") == "Cali"
    assert ctx.form.name("municipality", "Buga") == "buga"
    assert ctx.form.column_label("main", "water_source") == "Water source"
    assert list(ctx.form.columns["column"]) == ["municipality", "water_source", "age"]


def test_an_unknown_reference_table_names_the_available_ones(download):
    ctx = tz.load_context(download())

    assert sorted(ctx.reference) == ["population"]
    assert list(ctx.reference["population"]["municipality_code"]) == ["cali", "buga"]
    with pytest.raises(ReferenceTableNotFound) as error:
        ctx.reference["populaton"]
    assert "'populaton'" in str(error.value) and "Available: population" in str(error.value)


def test_the_log_prints_like_a_runs_log(download, capsys):
    ctx = tz.load_context(download())

    ctx.log.info("Removed %d test submissions", 12)
    ctx.log.warning("careful")

    assert capsys.readouterr().out.splitlines() == ["INFO Removed 12 test submissions", "WARNING careful"]


def test_show_is_silent_when_asked_to_be(download, capsys):
    ctx = tz.load_context(download(), show=False)

    assert ctx.show("value", "name") == "value"
    assert capsys.readouterr().out == ""


def test_the_raw_and_clean_tables_load_as_dictionaries(download):
    folder = download()

    raw = tz.load_tables("raw", folder)
    clean = tz.load_tables("clean", folder)

    assert sorted(raw) == ["main", "members"]
    assert len(raw["main"]) == 4 and len(clean["main"]) == 3  # script 1 dropped a submission


def test_the_column_types_are_the_ones_a_script_receives_on_the_server(download, tmp_path):
    folder = download()
    local = tz.load_tables("raw", folder)["main"]

    # The same table given to a script in the worker's runner, which reports the dtypes it saw.
    step = tmp_path / "step"
    (step / "input").mkdir(parents=True)
    interchange.write_parquet(pd.read_parquet(folder / "raw" / "main.parquet"), step / "input" / "main.parquet")
    (step / "context.json").write_text("{}", encoding="utf-8")
    (step / "script.py").write_text(
        "import pandas as pd\n"
        "def clean(raw, ctx):\n"
        "    main = raw['main']\n"
        "    return {'seen': pd.DataFrame({'column': list(main.columns), 'dtype': [str(d) for d in main.dtypes]})}\n",
        encoding="utf-8",
    )
    assert run_step(step, "clean")["status"] == "succeeded"
    seen = interchange.read_parquet(step / "output" / "seen.parquet")

    assert {c: str(d) for c, d in local.dtypes.items()} == dict(zip(seen["column"], seen["dtype"], strict=True))
    assert str(local["age"].dtype) == "Int64" and pd.isna(local["age"][1])  # a missing integer is <NA>, not a float


def test_each_call_reads_new_objects(download):
    folder = download()
    first = tz.load_tables("raw", folder)
    first["main"].drop(index=[0, 1], inplace=True)
    first["main"]["extra"] = 1

    second = tz.load_tables("raw", folder)

    assert len(second["main"]) == 4 and "extra" not in second["main"].columns
    assert second["main"] is not first["main"]


def test_clean_tables_after_script_1_failed_explain_and_suggest_the_raw_ones(download):
    folder = download(clean="failed")

    with pytest.raises(tz.ScriptError) as error:
        tz.load_tables("clean", folder)

    message = str(error.value)
    assert "Script 1 (version 2) failed" in message and "KeyError: 'age'" in message
    assert "load_tables('raw')" in message
    assert sorted(tz.load_tables("raw", folder)) == ["main", "members"]


def test_without_script_1_the_clean_tables_are_the_flattened_ones(download):
    folder = download(clean="none")

    clean = tz.load_tables("clean", folder)

    assert len(clean["main"]) == 4
    pd.testing.assert_frame_equal(clean["main"], tz.load_tables("raw", folder)["main"])


def test_an_unknown_source_is_rejected(download):
    with pytest.raises(tz.ScriptError, match="Unknown source 'output'"):
        tz.load_tables("output", download())
