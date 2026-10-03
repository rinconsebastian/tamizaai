"""pandas 3 ↔ Parquet through DuckDB: every dtype a script is likely to return."""

import datetime as dt
import importlib.util

import numpy as np
import pandas as pd
import pytest

from tamiza import interchange


def test_text_columns_are_backed_by_pyarrow(tmp_path):
    # Without pyarrow, pandas keeps each text value as a Python object: about 12 times the memory, which made a
    # 500,000-submission run exceed the script memory limit.
    assert importlib.util.find_spec("pyarrow") is not None
    interchange.write_parquet(pd.DataFrame({"municipality": ["cali", "buga"]}), tmp_path / "t.parquet")

    assert interchange.read_parquet(tmp_path / "t.parquet")["municipality"].dtype.storage == "pyarrow"


@pytest.fixture
def frame():
    return pd.DataFrame(
        {
            "text": pd.Series(["a", None, "ñandú"]),
            "count": pd.Series([1, None, 3], dtype="Int64"),
            "ratio": [1.5, np.nan, 2.0],
            "flag": [True, False, True],
            "maybe": pd.Series([True, None, False], dtype="boolean"),
            "aware": pd.to_datetime(["2026-01-01T10:00:00Z", None, "2026-02-01T00:00:00Z"], utc=True),
            "naive": pd.to_datetime(["2026-01-01 10:00", "2026-01-02 11:00", None]),
            "day": [dt.date(2026, 1, 1), None, dt.date(2026, 3, 1)],
            "category": pd.Categorical(["x", "y", "x"]),
            "mixed": [1, "two", [3.0]],
        }
    )


def test_round_trip_keeps_values_and_types(tmp_path, frame):
    path = tmp_path / "t.parquet"

    interchange.write_parquet(frame, path)
    back = interchange.read_parquet(path)

    assert list(back.columns) == list(frame.columns)
    assert back["text"].tolist()[0::2] == ["a", "ñandú"] and pd.isna(back["text"][1])
    assert back["count"].dtype == "Int64" and back["count"].tolist()[0] == 1 and pd.isna(back["count"][1])
    assert back["ratio"].dtype == "float64"
    assert back["flag"].dtype == "boolean"  # nullable, like integers
    assert back["maybe"].tolist()[0] is True and pd.isna(back["maybe"][1])
    assert back["aware"][0] == pd.Timestamp("2026-01-01T10:00:00Z")
    assert back["naive"][1] == pd.Timestamp("2026-01-02 11:00")
    assert back["day"][2] == pd.Timestamp("2026-03-01")
    assert back["category"].tolist() == ["x", "y", "x"]
    assert back["mixed"].tolist() == ["1", "two", "[3.0]"]


def test_file_types_map_to_database_friendly_types(tmp_path, frame):
    path = tmp_path / "t.parquet"
    interchange.write_parquet(frame, path)

    types = dict(interchange.describe(path))

    assert types["count"] == "BIGINT"
    assert types["ratio"] == "DOUBLE"
    assert types["maybe"] == "BOOLEAN"
    assert types["aware"] == "TIMESTAMP WITH TIME ZONE"
    assert types["day"] == "DATE"
    assert types["mixed"] == "VARCHAR"


def test_rows_come_out_as_plain_python_values(tmp_path, frame):
    path = tmp_path / "t.parquet"
    interchange.write_parquet(frame, path)

    rows = list(interchange.iter_rows(path))

    assert len(rows) == 3 == interchange.count_rows(path)
    first = dict(zip([name for name, _ in interchange.describe(path)], rows[0], strict=True))
    # Aware timestamps become naive UTC (no pytz needed); enums become text.
    assert first["aware"] == dt.datetime(2026, 1, 1, 10, 0)
    assert first["category"] == "x"
    assert first["day"] == dt.date(2026, 1, 1)


def test_a_named_index_becomes_a_column_and_an_unnamed_one_is_dropped(tmp_path):
    grouped = pd.DataFrame({"municipality": ["cali", "cali", "buga"], "n": [1, 2, 3]}).groupby("municipality").sum()
    filtered = pd.DataFrame({"n": [1, 2, 3]})[lambda d: d["n"] > 1]

    interchange.write_parquet(grouped, tmp_path / "g.parquet")
    interchange.write_parquet(filtered, tmp_path / "f.parquet")

    assert list(interchange.read_parquet(tmp_path / "g.parquet").columns) == ["municipality", "n"]
    assert list(interchange.read_parquet(tmp_path / "f.parquet").columns) == ["n"]


def test_non_text_column_names_become_text(tmp_path):
    interchange.write_parquet(pd.DataFrame({0: [1], 2026: [2]}), tmp_path / "t.parquet")

    assert list(interchange.read_parquet(tmp_path / "t.parquet").columns) == ["0", "2026"]


def test_paths_with_quotes_are_safe(tmp_path):
    path = tmp_path / "it's.parquet"

    interchange.write_parquet(pd.DataFrame({"a": [1]}), path)

    assert interchange.count_rows(path) == 1


def test_large_integer_ids_with_gaps_stay_exact(tmp_path):
    ids = pd.Series([9007199254740993, None], dtype="Int64")
    interchange.write_parquet(pd.DataFrame({"_id": ids}), tmp_path / "t.parquet")

    back = interchange.read_parquet(tmp_path / "t.parquet")["_id"]

    assert back.dtype == "Int64"
    assert back[0] == 9007199254740993 and pd.isna(back[1])
