"""The facts about an indicator test's table that the UI turns into the dashboard view and its warnings."""

import pandas as pd
import pytest

from tamiza import interchange, preview


@pytest.fixture
def view(tmp_path):
    def make(frame):
        path = tmp_path / "table.parquet"
        interchange.write_parquet(frame, path)
        return preview.dashboard_view(path)

    return make


def rate_table(municipalities=12):
    names = [f"Town {n:02d}" for n in range(municipalities)]
    return pd.DataFrame(
        {
            "municipality": names,
            "value": [0.5] * municipalities,
            "numerator": [5] * municipalities,
            "denominator": [10] * municipalities,
        }
    )


def test_a_rate_by_municipality_offers_measures_the_ratio_and_a_chart(view):
    facts = view(rate_table())

    assert facts["measures"] == ["value", "numerator", "denominator"]
    assert facts["defaultMeasure"] == "value"
    assert facts["ratio"] is True
    assert facts["dimensions"] == ["municipality"]
    assert facts["defaultDimension"] == "municipality"
    assert facts["geometry"] == []
    assert facts["rows"] == 12
    assert facts["totals"] == {"value": 6.0, "numerator": 60.0, "denominator": 120.0}
    chart = facts["charts"]["municipality"]
    assert chart["values"] == 12
    assert [g["group"] for g in chart["groups"]] == [f"Town {n:02d}" for n in range(12)]
    assert chart["groups"][0] == {"group": "Town 00", "value": 0.5, "numerator": 5.0, "denominator": 10.0}


def test_groups_are_summed_and_ordered_by_value_with_empty_last(view):
    frame = pd.DataFrame({"municipality": ["Palmira", None, "Cali", "Cali"], "value": [1.0, 2.0, 3.0, 4.0]})

    chart = view(frame)["charts"]["municipality"]

    assert [(g["group"], g["value"]) for g in chart["groups"]] == [("Cali", 7.0), ("Palmira", 1.0), (None, 2.0)]
    assert chart["values"] == 3
    assert chart["groups"][0]["numerator"] is None


def test_a_dimension_with_many_values_is_listed_but_not_charted(view):
    frame = pd.DataFrame({"code": [f"c{n}" for n in range(300)], "town": ["a", "b", "c"] * 100, "value": 1.0})

    facts = view(frame)

    assert facts["dimensions"] == ["code", "town"]
    assert facts["charts"]["code"] == {"values": 300, "groups": None}
    assert facts["charts"]["town"]["values"] == 3


def test_a_text_column_of_wkt_polygons_is_a_geometry_column_and_is_not_charted(view):
    frame = pd.DataFrame(
        {
            "municipality": ["Cali", "Buga"],
            "geometry": ["POLYGON ((0 0, 1 0, 1 1, 0 0))", "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))"],
            "label": ["not wkt", "POINT of view"],
            "value": [1.0, 2.0],
        }
    )

    facts = view(frame)

    assert facts["geometry"] == ["geometry"]
    assert facts["dimensions"] == ["municipality", "geometry", "label"]
    assert "geometry" not in facts["charts"]
    assert set(facts["charts"]) == {"municipality", "label"}


def test_without_a_value_column_the_first_numeric_column_is_the_default_measure(view):
    facts = view(pd.DataFrame({"municipality": ["Cali", "Buga"], "households": [10, 20]}))

    assert facts["measures"] == ["households"]
    assert facts["defaultMeasure"] == "households"
    assert facts["ratio"] is False
    assert facts["totals"] == {"households": 30.0}


def test_a_ratio_needs_both_columns(view):
    facts = view(pd.DataFrame({"town": ["a"], "value": [0.5], "numerator": [1]}))

    assert facts["ratio"] is False
    assert facts["measures"] == ["value", "numerator"]


def test_rows_that_share_every_dimension_value_are_counted(view):
    frame = pd.DataFrame({"municipality": ["Cali", "Cali", "Buga"], "value": [1.0, 2.0, 3.0]})

    assert view(frame)["duplicateRows"] == 1
    assert view(pd.DataFrame({"municipality": ["Cali", "Buga"], "value": [1.0, 2.0]}))["duplicateRows"] == 0
    assert view(pd.DataFrame({"value": [1.0, 2.0]}))["duplicateRows"] == 0


def test_empty_values_and_the_largest_value_of_the_default_measure(view):
    frame = pd.DataFrame({"town": ["a", "b", "c"], "value": [0.25, None, 45.2]})

    facts = view(frame)

    assert facts["defaultMeasureNulls"] == 1
    assert facts["defaultMeasureMax"] == 45.2
    assert facts["totals"]["value"] == pytest.approx(45.45)


def test_an_empty_table_has_its_columns_and_no_figures(view):
    facts = view(pd.DataFrame({"town": pd.Series([], dtype="object"), "value": pd.Series([], dtype="float64")}))

    assert facts["rows"] == 0
    assert facts["measures"] == ["value"]
    assert facts["defaultMeasureMax"] is None
    assert facts["totals"] == {"value": 0.0}
    assert facts["charts"]["town"] == {"values": 0, "groups": []}


def test_booleans_and_dates_are_dimensions_not_measures(view):
    frame = pd.DataFrame(
        {"urban": [True, False], "day": pd.to_datetime(["2026-01-01", "2026-01-02"]).date, "value": [1.0, 2.0]}
    )

    facts = view(frame)

    assert facts["measures"] == ["value"]
    assert facts["dimensions"] == ["urban", "day"]
    assert [g["group"] for g in facts["charts"]["day"]["groups"]] == ["2026-01-01", "2026-01-02"]


def test_integer_columns_named_like_dimensions_are_offered_as_measures(view):
    facts = view(pd.DataFrame({"year": [2025, 2026], "value": [1.0, 2.0]}))

    assert facts["measures"] == ["year", "value"]
    assert facts["defaultMeasure"] == "value"
    assert facts["dimensions"] == []
    assert facts["defaultDimension"] is None


def test_only_the_first_ten_dimensions_are_charted(view):
    frame = pd.DataFrame({f"d{n:02d}": ["x", "y"] for n in range(12)} | {"value": [1.0, 2.0]})

    facts = view(frame)

    assert len(facts["dimensions"]) == 12
    assert len(facts["charts"]) == 10


def test_the_facts_are_plain_json(view):
    import json

    frame = pd.DataFrame({"town": ["a", None], "value": [float("inf"), 2.0], "n": pd.array([1, None], dtype="Int64")})

    json.dumps(view(frame), allow_nan=False)
