"""Checking a script on a downloaded sample: a run's contract and messages, locally."""

import textwrap
import traceback

import pandas as pd
import pytest

import tamiza_sdk as tz


def script_file(tmp_path, source, name="script.py"):
    path = tmp_path / name
    path.write_text(textwrap.dedent(source).lstrip(), encoding="utf-8")
    return path


def test_an_indicator_without_a_numeric_column_fails_with_the_runs_message(download):
    def compute(cleaned, ctx):
        return cleaned["main"][["municipality", "water_source"]]

    with pytest.raises(tz.ScriptError) as error:
        tz.check(compute, "indicator", "coverage", folder=download())

    assert str(error.value) == "The indicator table has no numeric column; add at least one, such as `value`."


def test_columns_are_renamed_as_in_a_run_and_reported(download, capsys):
    def prepare_output(cleaned, ctx):
        return {"households": pd.DataFrame({"Household size": [3, 4], "Age": [30, 41]})}

    result = tz.check(prepare_output, "output", folder=download())

    assert list(result.tables["households"].columns) == ["household_size", "age"]
    assert result.renamed == [("households", "Household size", "household_size"), ("households", "Age", "age")]
    assert "INFO Table households: column 'Household size' published as 'household_size'." in capsys.readouterr().out
    assert "'Household size' → 'household_size'" in repr(result)


def test_a_script_that_raises_keeps_its_exception_and_the_line_of_its_file(download, tmp_path):
    path = script_file(
        tmp_path,
        """
        import pandas as pd


        def compute(cleaned, ctx):
            main = cleaned["main"]
            ctx.log.info("rows: %d", len(main))
            # a comment so the failing line is the ninth
            counts = main.groupby("municipality").size()
            return counts["missing"]
        """,
    )

    with pytest.raises(KeyError) as raised:
        tz.check(path, "indicator", "coverage", folder=download())

    frames = [f for f in traceback.extract_tb(raised.tb) if f.filename == str(path)]
    assert [(f.lineno, f.name) for f in frames] == [(9, "compute")]
    assert 'counts["missing"]' in frames[0].line  # the traceback shows the file's own line


def test_the_inputs_a_script_changes_in_place_do_not_change_what_was_loaded(download):
    folder = download()
    loaded = tz.load_tables("raw", folder)

    def clean(raw, ctx):
        raw["main"].drop(index=[0, 1], inplace=True)
        raw["main"]["extra"] = 1
        return raw

    from_files = tz.check(clean, "clean", folder=folder)
    from_loaded = tz.check(clean, "clean", tables=loaded, folder=folder)

    assert len(from_files.tables["main"]) == len(from_loaded.tables["main"]) == 2
    assert len(loaded["main"]) == 4 and "extra" not in loaded["main"].columns
    assert len(tz.load_tables("raw", folder)["main"]) == 4


def test_the_tables_of_one_check_can_be_the_input_of_the_next(download):
    folder = download()

    def clean(raw, ctx):
        return {"main": raw["main"][raw["main"]["municipality"] == "Cali"]}

    def compute(cleaned, ctx):
        return pd.DataFrame({"value": [len(cleaned["main"])]})

    chained = tz.check(
        compute, "indicator", "households", tables=tz.check(clean, "clean", folder=folder).tables, folder=folder
    )
    downloaded = tz.check(compute, "indicator", "households", folder=folder)

    assert chained.tables["households"]["value"].iloc[0] == 2  # script 1 kept the two Cali rows
    assert downloaded.tables["households"]["value"].iloc[0] == 3  # the download's clean tables have three rows


def test_a_helper_defined_only_in_the_notebook_is_not_visible_to_the_file(download, tmp_path):
    def tidy(frame):  # defined here, as in a notebook cell, but not in the file
        return frame

    path = script_file(
        tmp_path,
        """
        def compute(cleaned, ctx):
            return tidy(cleaned["main"])
        """,
    )

    with pytest.raises(NameError, match="tidy"):
        tz.check(path, "indicator", "coverage", folder=download())
    assert tidy(1) == 1


def test_a_file_without_the_contract_function_fails_with_the_runs_message(download, tmp_path):
    path = script_file(
        tmp_path,
        """
        def prepare(cleaned, ctx):
            return {"x": cleaned["main"]}
        """,
    )

    with pytest.raises(tz.ScriptError, match=r"does not define the function prepare_output\("):
        tz.check(path, "output", folder=download())


def test_the_dashboard_facts_of_an_indicator_are_returned(download):
    def compute(cleaned, ctx):
        main = cleaned["main"]
        rows = (
            main.groupby("municipality")
            .agg(
                numerator=("water_source", lambda answers: int((answers == "Piped").sum())),
                denominator=("water_source", "size"),
            )
            .reset_index()
        )
        rows["value"] = rows["numerator"] / rows["denominator"]
        return rows[["municipality", "value", "numerator", "denominator"]]

    result = tz.check(compute, "indicator", "piped_water_rate", folder=download())

    facts = result.dashboard
    assert facts["measures"] == ["value", "numerator", "denominator"]
    assert facts["defaultMeasure"] == "value" and facts["ratio"] is True
    assert facts["dimensions"] == ["municipality"] and facts["rows"] == 2
    assert result.types["piped_water_rate"]["municipality"] == "text"
    assert result.types["piped_water_rate"]["numerator"] == "integer"
    assert "measures: value, numerator, denominator (default: value)" in repr(result)
    assert "<pre>" in result._repr_html_()


def test_only_an_indicator_gets_dashboard_facts(download):
    def prepare_output(cleaned, ctx):
        return {"households": cleaned["main"]}

    assert tz.check(prepare_output, "output", folder=download()).dashboard is None


def test_an_indicator_needs_its_name(download):
    with pytest.raises(tz.ScriptError, match="Name the indicator"):
        tz.check(lambda cleaned, ctx: pd.DataFrame({"value": [1]}), "indicator", folder=download())


def test_an_unknown_kind_and_a_missing_file_are_explained(download, tmp_path):
    folder = download()
    with pytest.raises(tz.ScriptError, match="Unknown script kind 'sorting'"):
        tz.check(lambda *_: {}, "sorting", folder=folder)
    with pytest.raises(tz.ScriptError, match="There is no script file"):
        tz.check(tmp_path / "nothing.py", "clean", folder=folder)


def test_a_script_returning_the_wrong_type_gets_the_runs_message(download):
    with pytest.raises(
        tz.ScriptError, match=r"clean must return a dictionary of table names to DataFrames, not a list"
    ):
        tz.check(lambda raw, ctx: [], "clean", folder=download())


def test_ctx_show_displays_in_a_check_unless_turned_off(download, capsys):
    def compute(cleaned, ctx):
        ctx.show(cleaned["main"], "main")
        return pd.DataFrame({"value": [1]})

    tz.check(compute, "indicator", "coverage", folder=download())
    shown = capsys.readouterr().out
    tz.check(compute, "indicator", "coverage", folder=download(), show=False)
    silent = capsys.readouterr().out

    assert shown.startswith("main (3 rows)") and silent == ""


def test_the_raw_tables_are_script_1s_input_and_the_clean_ones_the_rest(download):
    folder = download()
    seen = {}

    def clean(raw, ctx):
        seen["clean"] = len(raw["main"])
        return raw

    def prepare_output(cleaned, ctx):
        seen["output"] = len(cleaned["main"])
        return {"main": cleaned["main"]}

    tz.check(clean, "clean", folder=folder)
    tz.check(prepare_output, "output", folder=folder)

    assert seen == {"clean": 4, "output": 3}


def test_a_check_after_script_1_failed_explains_it_for_the_clean_tables(download):
    with pytest.raises(tz.ScriptError, match="Script 1 .* failed"):
        tz.check(lambda cleaned, ctx: pd.DataFrame({"value": [1]}), "indicator", "x", folder=download(clean="failed"))


def test_the_tables_come_back_as_the_server_would_hold_them_after_the_step(download):
    def clean(raw, ctx):
        main = raw["main"][raw["main"]["municipality"] == "Cali"]  # a filtered frame keeps its gapped row index
        return {"main": main.assign(n=[1, 2], mixed=[1, "two"]), "members": raw["members"]}

    result = tz.check(clean, "clean", folder=download())

    main = result.tables["main"]
    assert list(main.index) == [0, 1]  # the index is not published
    assert str(main["n"].dtype) == "Int64"  # integers are nullable, as the next script receives them
    assert list(main["mixed"]) == ["1", "two"]  # values Parquet cannot type together become text
    assert result.types["main"]["n"] == "integer" and result.types["main"]["mixed"] == "text"


def test_a_chained_script_receives_what_the_server_would_give_it(download):
    folder = download()
    seen = {}

    def clean(raw, ctx):
        main = raw["main"][raw["main"]["municipality"] == "Cali"]
        return {"main": main.assign(n=[1, 2])}

    def compute(cleaned, ctx):
        main = cleaned["main"]
        seen["index"], seen["dtype"] = list(main.index), str(main["n"].dtype)
        return pd.DataFrame({"value": [main.loc[1, "n"]]})  # label 1 exists only on a fresh index

    tz.check(compute, "indicator", "x", tables=tz.check(clean, "clean", folder=folder).tables, folder=folder)

    assert seen == {"index": [0, 1], "dtype": "Int64"}
