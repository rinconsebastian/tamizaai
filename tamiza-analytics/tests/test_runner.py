"""The child-side runner, called in-process."""

import json
import textwrap

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.runner import run_step

CONTEXT = {
    "project_name": "Household survey",
    "questions": [
        {
            "name": "municipality",
            "path": "respondent/municipality",
            "type": "select_one",
            "label": "Municipality",
            "list_name": "municipalities",
        },
        {"name": "age", "path": "age", "type": "integer", "label": "Age", "list_name": None},
    ],
    "choices": [
        {"list_name": "municipalities", "name": "cali", "label": "Cali"},
        {"list_name": "municipalities", "name": "buga", "label": "Buga"},
    ],
}


@pytest.fixture
def step(tmp_path):
    def make(code, tables=None, context=None):
        (tmp_path / "input").mkdir(exist_ok=True)
        tables = (
            tables
            if tables is not None
            else {"main": pd.DataFrame({"municipality": ["cali", "buga", "cali"], "age": [30, 41, 17]})}
        )
        for name, frame in tables.items():
            interchange.write_parquet(frame, tmp_path / "input" / f"{name}.parquet")
        (tmp_path / "context.json").write_text(json.dumps(context or CONTEXT))
        (tmp_path / "script.py").write_text(textwrap.dedent(code))
        return tmp_path

    return make


def output(step_dir, name):
    return interchange.read_parquet(step_dir / "output" / f"{name}.parquet")


def test_clean_returns_tables(step):
    step_dir = step("""
        def clean(raw, ctx):
            main = raw["main"]
            return {"main": main[main["age"] >= 18]}
    """)

    result = run_step(step_dir, "clean")

    assert result == {"status": "succeeded", "tables": [{"name": "main", "rows": 2}]}
    assert output(step_dir, "main")["age"].tolist() == [30, 41]


def test_prepare_output_and_an_indicator(step):
    step_dir = step("""
        import pandas as pd

        def prepare_output(cleaned, ctx):
            return {"by_municipality": cleaned["main"].groupby("municipality").size().rename("n").to_frame()}

        def compute(cleaned, ctx):
            main = cleaned["main"]
            adults = (main["age"] >= 18).sum()
            return pd.DataFrame([{"value": adults / len(main), "numerator": adults, "denominator": len(main)}])
    """)

    assert run_step(step_dir, "output")["status"] == "succeeded"
    assert output(step_dir, "by_municipality").to_dict("records") == [
        {"municipality": "buga", "n": 1},
        {"municipality": "cali", "n": 2},
    ]
    # The table is named after the indicator.
    assert run_step(step_dir, "indicator", "adult_rate")["tables"] == [{"name": "adult_rate", "rows": 1}]
    assert output(step_dir, "adult_rate")["numerator"].tolist() == [2]


def test_missing_function(step):
    result = run_step(step("def prepare(cleaned, ctx):\n    return {}\n"), "output")

    assert result["status"] == "failed"
    assert "prepare_output" in result["error"]
    assert "traceback" not in result


@pytest.mark.parametrize(
    ("code", "kind", "message"),
    [
        (
            "def clean(raw, ctx):\n    return [1]\n",
            "clean",
            "must return a dictionary of table names to DataFrames, not a list",
        ),
        ("def clean(raw, ctx):\n    return {'a': 1}\n", "clean", "not a dictionary of int"),
        ("def clean(raw, ctx):\n    return None\n", "clean", "not None"),
        ("def clean(raw, ctx):\n    return {}\n", "clean", "returned no tables"),
        (
            "def compute(cleaned, ctx):\n    return {'rate': 1}\n",
            "indicator",
            "compute must return a DataFrame, not a dictionary",
        ),
        ("def compute(cleaned, ctx):\n    return [1]\n", "indicator", "compute must return a DataFrame, not a list"),
    ],
)
def test_wrong_return_types(step, code, kind, message):
    result = run_step(step(code), kind, "rate")

    assert result["status"] == "failed"
    assert message in result["error"]


def test_exception_shows_the_script_line(step):
    result = run_step(
        step("""
        def clean(raw, ctx):
            main = raw["main"]
            return {"main": main[main["weight"] > 0]}
    """),
        "clean",
    )

    assert result["status"] == "failed"
    assert result["error"] == "KeyError: 'weight'"
    assert 'File "script.py", line 4, in clean' in result["traceback"]
    assert 'return {"main": main[main["weight"] > 0]}' in result["traceback"]
    assert "pandas" not in result["traceback"]


def test_syntax_error_names_the_line(step):
    result = run_step(step("def clean(raw, ctx)\n    return raw\n"), "clean")

    assert result["status"] == "failed"
    assert "SyntaxError" in result["error"]
    assert "line 1" in result["traceback"]


def test_exit_is_reported(step):
    result = run_step(step("import sys\ndef clean(raw, ctx):\n    sys.exit(3)\n"), "clean")

    assert result == {"status": "failed", "error": "The script called exit(3)."}


def test_log_and_print_appear_in_order(step, capsys):
    run_step(
        step("""
        def clean(raw, ctx):
            ctx.log.info("Removed %d test submissions", 12)
            print("done")
            ctx.log.warning("check ages")
            return raw
    """),
        "clean",
    )

    assert capsys.readouterr().out.splitlines() == ["INFO Removed 12 test submissions", "done", "WARNING check ages"]


def test_form_dictionary_and_project_name(step):
    step_dir = step("""
        def clean(raw, ctx):
            main = raw["main"].copy()
            main["municipality_label"] = [ctx.form.label("municipality", v) for v in main["municipality"]]
            assert ctx.form.label("respondent/municipality", "buga") == "Buga"
            assert ctx.form.label("municipality", "nowhere") is None
            assert ctx.form.label("age", 30) is None
            assert list(ctx.form.questions.columns) == ["name", "path", "type", "label", "list_name"]
            assert len(ctx.form.choices) == 2
            assert ctx.project_name == "Household survey"
            return {"main": main}
    """)

    assert run_step(step_dir, "clean")["status"] == "succeeded"
    assert output(step_dir, "main")["municipality_label"].tolist() == ["Cali", "Buga", "Cali"]


# The reverse lookup the pipeline precomputes (label_codes): the current form's labels, then earlier versions'.
LABEL_CODES_CONTEXT = {
    **CONTEXT,
    "questions": [
        *CONTEXT["questions"],
        {"name": "has_water", "path": "has_water", "type": "select_one", "label": "Has water", "list_name": "yes_no"},
        {"name": "source", "path": "water/source", "type": "select_one", "label": "Source", "list_name": "sources"},
    ],
    "label_codes": {
        "respondent/municipality": {"Cali": "cali", "Buga": "buga"},
        # "Sí" is the current label of 1 and vA called it "Si"; "Maybe" is shared by two choices: it has no code.
        "has_water": {"Sí": "1", "Si": "1", "No": "2"},
        "water/source": {"Piped": "piped", "Other": "other"},
        "old_question": {"Old answer": "x"},
    },
}


def test_ctx_form_name_gives_the_code_of_a_label(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.name("municipality", "Cali") == "cali"
            assert ctx.form.name("respondent/municipality", "Buga") == "buga"
            main = raw["main"].copy()
            main["code"] = [ctx.form.name("municipality", label) for label in ["Cali", "Buga", "Cali"]]
            return {"main": main}
        """,
        context=LABEL_CODES_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"
    assert output(step_dir, "main")["code"].tolist() == ["cali", "buga", "cali"]


def test_ctx_form_name_resolves_the_label_of_an_earlier_version(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.name("has_water", "Sí") == "1"
            assert ctx.form.name("has_water", "Si") == "1"
            return {"main": raw["main"]}
        """,
        context=LABEL_CODES_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_name_gives_none_for_an_unknown_or_repeated_label_or_question(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.name("has_water", "Maybe") is None
            assert ctx.form.name("has_water", "Perhaps") is None
            assert ctx.form.name("age", 30) is None
            assert ctx.form.name("no_such_question", "Cali") is None
            return {"main": raw["main"]}
        """,
        context=LABEL_CODES_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_name_resolves_other_to_other_and_reaches_questions_of_earlier_versions(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.name("source", "Other") == "other"
            assert ctx.form.name("water/source", "Piped") == "piped"
            assert ctx.form.name("old_question", "Old answer") == "x"
            return {"main": raw["main"]}
        """,
        context=LABEL_CODES_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_name_works_in_a_context_without_label_codes(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.name("municipality", "Cali") is None
            return {"main": raw["main"]}
        """
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


# 4.3 — output validation


def test_table_names_must_already_be_valid(step):
    result = run_step(step("def prepare_output(cleaned, ctx):\n    return {'my table!': cleaned['main']}\n"), "output")

    assert result["status"] == "failed"
    assert "'my table!'" in result["error"] and "lowercase letters, digits and underscores" in result["error"]


def test_column_names_are_normalized_and_the_renaming_logged(step, capsys):
    step_dir = step("""
        import pandas as pd
        def clean(raw, ctx):
            return {"main": pd.DataFrame({"Household size": [3], "Age": [30], "age_group": ["adult"]})}
    """)

    result = run_step(step_dir, "clean")

    assert result["status"] == "succeeded"
    assert list(output(step_dir, "main").columns) == ["household_size", "age", "age_group"]
    log = capsys.readouterr().out
    assert "Table main: column 'Household size' published as 'household_size'." in log
    assert "column 'Age' published as 'age'." in log
    assert "age_group" not in log


def test_columns_that_collide_after_normalizing_fail(step):
    result = run_step(
        step("""
        import pandas as pd
        def prepare_output(cleaned, ctx):
            return {"t": pd.DataFrame({"Age": [1], "age": [2]})}
    """),
        "output",
    )

    assert result["status"] == "failed"
    assert "'Age'" in result["error"] and "'age'" in result["error"]


def test_indicator_publishes_the_columns_it_returns(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            main = cleaned["main"]
            rows = main.groupby("municipality").agg(numerator=("age", lambda ages: (ages >= 18).sum()),
                                                    denominator=("age", "size")).reset_index()
            rows["value"] = rows["numerator"] / rows["denominator"]
            return rows
    """)

    result = run_step(step_dir, "indicator", "adult_rate")

    assert result == {"status": "succeeded", "tables": [{"name": "adult_rate", "rows": 2}]}
    assert list(output(step_dir, "adult_rate").columns) == ["municipality", "numerator", "denominator", "value"]


def test_indicator_without_a_numeric_column_fails(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame({"municipality": ["cali", "buga"], "note": ["a", "b"]})
    """)

    result = run_step(step_dir, "indicator", "coverage")

    assert result["status"] == "failed"
    assert result["error"] == "The indicator table has no numeric column; add at least one, such as `value`."
    assert not (step_dir / "output").exists()


def test_a_boolean_column_is_not_a_numeric_column(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame({"municipality": ["cali"], "flag": [True]})
    """)

    assert "no numeric column" in run_step(step_dir, "indicator", "coverage")["error"]


def test_an_all_missing_nullable_numeric_column_is_accepted(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame({"municipality": ["cali", "buga"], "value": pd.array([None, None], dtype="Float64")})
    """)

    assert run_step(step_dir, "indicator", "coverage")["status"] == "succeeded"


def test_indicator_column_names_are_reduced_and_reported(step, capfd):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame([["cali", 1.0]], columns=["Municipality Name", "Value"])
    """)

    assert run_step(step_dir, "indicator", "coverage")["status"] == "succeeded"
    assert list(output(step_dir, "coverage").columns) == ["municipality_name", "value"]
    assert "column 'Municipality Name' published as 'municipality_name'" in capfd.readouterr().out


def test_indicator_columns_that_collide_fail(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame([[1, 2]], columns=["Value", "value"])
    """)

    result = run_step(step_dir, "indicator", "coverage")

    assert result["status"] == "failed"
    assert "'Value'" in result["error"] and "'value'" in result["error"]


def test_an_indicator_name_that_is_not_a_table_name_fails(step):
    step_dir = step("""
        import pandas as pd
        def compute(cleaned, ctx):
            return pd.DataFrame({"value": [1]})
    """)

    assert "not allowed" in run_step(step_dir, "indicator", "Piped Water")["error"]
    assert "not allowed" in run_step(step_dir, "indicator")["error"]


def test_an_indicator_may_have_a_geometry_column(step):
    step_dir = step("""
        import geopandas
        def compute(cleaned, ctx):
            points = geopandas.GeoDataFrame(
                {"value": [3.0]}, geometry=geopandas.points_from_xy([-76.5], [3.4]), crs="EPSG:4326"
            )
            return points
    """)

    assert run_step(step_dir, "indicator", "sites")["status"] == "succeeded"
    assert output(step_dir, "sites")["geometry"].tolist() == ["POINT (-76.5 3.4)"]


def test_geometry_columns_are_published_as_wkt(step):
    step_dir = step("""
        import geopandas
        def prepare_output(cleaned, ctx):
            geometry = geopandas.points_from_xy([-76.5], [3.4])
            points = geopandas.GeoDataFrame({"name": ["a"]}, geometry=geometry, crs="EPSG:4326")
            return {"points": points}
    """)

    assert run_step(step_dir, "output")["status"] == "succeeded"
    assert output(step_dir, "points")["geometry"].tolist() == ["POINT (-76.5 3.4)"]


@pytest.fixture
def with_reference(step):
    def make(code, reference):
        step_dir = step(code)
        (step_dir / "reference").mkdir(exist_ok=True)
        for name, frame in reference.items():
            interchange.write_parquet(frame, step_dir / "reference" / f"{name}.parquet")
        return step_dir

    return make


def test_a_script_reads_a_reference_table(with_reference):
    population = pd.DataFrame({"municipality": ["cali", "buga"], "population_2026": pd.array([2_300_000, 130_000])})
    step_dir = with_reference(
        """
        import pandas as pd

        def compute(cleaned, ctx):
            population = ctx.reference["population"]
            counts = cleaned["main"].groupby("municipality").size().rename("n").reset_index()
            merged = counts.merge(population, on="municipality")
            return pd.DataFrame({
                "municipality": merged["municipality"],
                "value": merged["n"] / merged["population_2026"] * 100_000,
                "numerator": merged["n"],
                "denominator": merged["population_2026"],
            })
        """,
        {"population": population},
    )

    result = run_step(step_dir, "indicator", "per_100k")

    assert result["status"] == "succeeded", result
    indicators = output(step_dir, "per_100k").sort_values("municipality")
    assert indicators["denominator"].tolist() == [130_000, 2_300_000]


def test_an_unknown_reference_table_names_the_available_ones(with_reference):
    step_dir = with_reference(
        """
        def clean(raw, ctx):
            ctx.reference["populaton"]
            return raw
        """,
        {"population": pd.DataFrame({"a": [1]})},
    )

    result = run_step(step_dir, "clean")

    assert result["status"] == "failed"
    assert "'populaton'" in result["error"] and "Available: population" in result["error"]
    assert "line 3, in clean" in result["traceback"]


def test_reference_tables_can_be_listed_and_checked_without_loading(with_reference, monkeypatch, capsys):
    step_dir = with_reference(
        """
        def clean(raw, ctx):
            print(sorted(ctx.reference), "codes" in ctx.reference, "other" in ctx.reference, ctx.reference.get("other"))
            return raw
        """,
        {"population": pd.DataFrame({"a": [1]}), "codes": pd.DataFrame({"b": ["x"]})},
    )
    loaded = []
    original = interchange.read_parquet
    monkeypatch.setattr(interchange, "read_parquet", lambda path: loaded.append(path.parent.name) or original(path))

    result = run_step(step_dir, "clean")

    assert result["status"] == "succeeded", result
    assert "reference" not in loaded  # only the input tables were read
    assert "['codes', 'population'] True False None" in capsys.readouterr().out


def test_a_project_without_reference_tables_lists_none(step, capsys):
    step_dir = step("""
        def clean(raw, ctx):
            print("names:", list(ctx.reference))
            return raw
    """)

    result = run_step(step_dir, "clean")

    assert result["status"] == "succeeded", result
    assert "names: []" in capsys.readouterr().out


# The flattened columns with their question labels (columns), as the pipeline precomputes them.
COLUMNS_CONTEXT = {
    **CONTEXT,
    "columns": [
        {"table": "main", "column": "municipality", "path": "respondent/municipality", "label": "Municipality"},
        {"table": "main", "column": "spouse_age", "path": "spouse/age", "label": "Age of the spouse"},
        {"table": "main", "column": "old_question", "path": "old_question", "label": None},
        {"table": "members", "column": "member_name", "path": "members/member_name", "label": "Name of the member"},
    ],
}


def test_ctx_form_columns_lists_the_flattened_columns_with_their_question_and_label(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            columns = ctx.form.columns
            assert list(columns.columns) == ["table", "column", "path", "label"]
            assert len(columns) == 4 and "_id" not in set(columns["column"])
            row = columns[columns["column"] == "spouse_age"].iloc[0]
            assert (row["table"], row["path"], row["label"]) == ("main", "spouse/age", "Age of the spouse")
            return raw
    """,
        context=COLUMNS_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_column_label_gives_the_label_of_a_column(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.column_label("main", "spouse_age") == "Age of the spouse"
            assert ctx.form.column_label("members", "member_name") == "Name of the member"
            return raw
    """,
        context=COLUMNS_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_column_label_gives_none_without_a_label(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.column_label("main", "_id") is None  # a system column
            assert ctx.form.column_label("main", "old_question") is None  # not in the deployed version
            assert ctx.form.column_label("main", "spouse_age_2") is None  # no such column
            assert ctx.form.column_label("households", "spouse_age") is None  # a table the script built
            return raw
    """,
        context=COLUMNS_CONTEXT,
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"


def test_ctx_form_columns_work_in_a_context_without_columns(step):
    step_dir = step(
        """
        def clean(raw, ctx):
            assert ctx.form.columns.empty and list(ctx.form.columns.columns) == ["table", "column", "path", "label"]
            assert ctx.form.column_label("main", "municipality") is None
            return raw
    """
    )

    assert run_step(step_dir, "clean")["status"] == "succeeded"
