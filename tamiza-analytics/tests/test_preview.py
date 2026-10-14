"""Table previews for tests."""

import datetime
import json

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq
import pytest

from tamiza import interchange, preview


@pytest.fixture
def write(tmp_path):
    def make(frame, name="main"):
        path = tmp_path / f"{name}.parquet"
        if isinstance(frame, pa.Table):
            pq.write_table(frame, path)
        else:
            interchange.write_parquet(frame, path)
        return name, len(frame), path

    return make


def test_columns_types_and_values(write):
    frame = pd.DataFrame(
        {
            "id": pd.array([1, None], dtype="Int64"),
            "share": [0.5, float("nan")],
            "ok": pd.array([True, None], dtype="boolean"),
            "day": pd.to_datetime(["2026-01-31", None]).date,
            "at": pd.to_datetime(["2026-01-31T08:30:00Z", None], utc=True),
            "name": ["Ana", None],
        }
    )

    table = preview.table_preview(*write(frame))

    assert table["name"] == "main" and table["rows"] == 2 and table["previewRows"] == 2
    assert [(c["name"], c["type"]) for c in table["columns"]] == [
        ("id", "integer"),
        ("share", "decimal"),
        ("ok", "boolean"),
        ("day", "date"),
        ("at", "timestamp"),
        ("name", "text"),
    ]
    assert table["preview"][0] == [1, 0.5, True, "2026-01-31", "2026-01-31T08:30:00+00:00", "Ana"]
    assert table["preview"][1] == [None, None, None, None, None, None]
    assert table["cutCells"] == []
    json.dumps(table, allow_nan=False)  # what the database receives


def test_naive_timestamps_are_utc(write):
    table = preview.table_preview(*write(pd.DataFrame({"at": [datetime.datetime(2026, 1, 31, 8, 30)]})))

    assert table["preview"][0] == ["2026-01-31T08:30:00+00:00"]


def test_integers_beyond_javascript_precision_become_text(write):
    table = preview.table_preview(*write(pd.DataFrame({"n": [2**53, 2**53 + 1, -(2**60)]})))

    assert [row[0] for row in table["preview"]] == [2**53, str(2**53 + 1), str(-(2**60))]


def test_only_the_first_100_rows_are_shown(write):
    table = preview.table_preview(*write(pd.DataFrame({"n": range(250)})))

    assert table["rows"] == 250
    assert table["previewRows"] == 100
    assert table["preview"][-1] == [99]


def test_long_text_is_cut_and_marked(write):
    table = preview.table_preview(*write(pd.DataFrame({"a": ["short", "x" * 600], "b": ["y" * 501, "z"]})))

    assert table["preview"][0][1] == "y" * 500 + "…"
    assert table["preview"][1][0] == "x" * 500 + "…"
    assert table["cutCells"] == [[0, 1], [1, 0]]


def test_rows_are_dropped_to_stay_under_1_mb(write):
    frame = pd.DataFrame({f"c{i}": ["x" * 500] * 100 for i in range(40)})  # about 2 MB of preview

    table = preview.table_preview(*write(frame))

    assert 0 < table["previewRows"] < 100
    assert len(table["preview"]) == table["previewRows"]
    assert len(json.dumps(table["preview"], ensure_ascii=False).encode()) <= preview.MAX_BYTES
    assert all(cell[0] < table["previewRows"] for cell in table["cutCells"])


def test_an_empty_table_has_columns_and_no_rows(write):
    table = preview.table_preview(*write(pa.table({"a": pa.array([], pa.int64())})))

    assert table["columns"] == [{"name": "a", "type": "integer", "label": None}]
    assert table["preview"] == [] and table["previewRows"] == 0


def test_only_the_first_20_tables_of_a_step_get_a_preview(write):
    tables = [write(pd.DataFrame({"n": [i]}), name=f"t{i}") for i in range(22)]

    previews = preview.step_tables(tables)

    assert [p["name"] for p in previews] == [f"t{i}" for i in range(22)]
    assert previews[19]["preview"] == [[19]]
    assert previews[20] == {
        "name": "t20",
        "rows": 1,
        "columns": None,
        "previewRows": 0,
        "preview": None,
        "cutCells": None,
    }


def test_columns_carry_their_question_label_when_the_table_has_labels(write):
    name, rows, path = write(pd.DataFrame({"has_water": ["Yes"], "age": [30], "_id": [1]}))

    table = preview.table_preview(name, rows, path, {"has_water": "Does the household have water?"})

    assert [(c["name"], c["label"]) for c in table["columns"]] == [
        ("has_water", "Does the household have water?"),
        ("age", None),
        ("_id", None),
    ]


def test_step_tables_pass_each_tables_labels_on(write):
    first = (*write(pd.DataFrame({"a": [1]}), name="main"), {"a": "The first column"})
    second = write(pd.DataFrame({"a": [1]}), name="other")

    previews = preview.step_tables([first, second])

    assert previews[0]["columns"][0]["label"] == "The first column"
    assert previews[1]["columns"][0]["label"] is None
