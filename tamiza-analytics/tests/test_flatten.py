import datetime as dt

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.flatten import flatten

FORM = [
    {"name": "start", "xpath": "start", "type": "start"},
    {
        "name": "municipality",
        "xpath": "respondent/municipality",
        "type": "select_one_municipalities",
        "listName": "municipalities",
    },
    {"name": "age", "xpath": "head/age", "type": "integer"},
    {"name": "age", "xpath": "spouse/age", "type": "integer"},
    {"name": "member_name", "xpath": "members/member_name", "type": "text"},
    {"name": "member_age", "xpath": "members/member_age", "type": "integer"},
    {"name": "visit_date", "xpath": "members/visits/visit_date", "type": "date"},
]


def submission(kobo_id, members=3, **answers):
    return {
        "_id": kobo_id,
        "_uuid": f"uuid-{kobo_id}",
        "_submission_time": "2026-09-01T10:00:00",
        "__version__": "v1",
        "meta/instanceID": f"uuid:uuid-{kobo_id}",
        "formhub/uuid": "x",
        "_attachments": [{"download_url": "https://kf/x.jpg"}],
        "_geolocation": [3.4, -76.5],
        "_status": "submitted_via_web",
        "start": "2026-09-01T09:40:00.000-05:00",
        "respondent/municipality": "cali",
        "head/age": "45",
        "spouse/age": "41",
        "members": [
            {
                "members/member_name": f"m{i}",
                "members/member_age": str(20 + i),
                "members/visits": [{"members/visits/visit_date": "2026-08-0" + str(v)} for v in (1, 2)],
            }
            for i in range(1, members + 1)
        ],
        **answers,
    }


@pytest.fixture
def run(tmp_path):
    def run_flatten(rows, batch_rows=4):
        result = flatten(FORM, lambda: iter(rows), tmp_path, batch_rows=batch_rows)
        tables = {t.name: interchange.read_parquet(t.path) for t in result.tables}
        return result, tables

    return run_flatten


def test_main_and_repeat_tables_with_parent_keys(run):
    result, tables = run([submission(i) for i in range(1, 11)])

    assert result.input_rows == 10
    assert {t.name: t.rows for t in result.tables} == {"main": 10, "members": 30, "visits": 60}
    members, visits = tables["members"], tables["visits"]
    assert members["_row_id"].tolist() == list(range(1, 31))
    assert members.loc[members["_parent_id"] == 4, "member_name"].tolist() == ["m1", "m2", "m3"]
    assert set(members["_parent_table"]) == {"main"}
    assert members["_index"].tolist()[:3] == [1, 2, 3]
    # Nested repeat: each visit points at its members row.
    assert set(visits["_parent_table"]) == {"members"}
    assert visits.loc[visits["_parent_id"] == 5, "_index"].tolist() == [1, 2]


def test_columns_names_types_and_system_columns(run):
    _, tables = run([submission(1)])
    main = tables["main"]

    assert list(main.columns) == [
        "_id",
        "_uuid",
        "_submission_time",
        "_version",
        "_instance_id",
        "start",
        "municipality",
        "age",
        "spouse_age",
    ]
    row = main.iloc[0]
    assert row["_id"] == 1 and row["_version"] == "v1" and row["_instance_id"] == "uuid:uuid-1"
    assert row["start"] == dt.datetime(2026, 9, 1, 14, 40, tzinfo=dt.UTC)
    assert row["_submission_time"] == dt.datetime(2026, 9, 1, 10, 0, tzinfo=dt.UTC)
    assert row["age"] == 45 and row["spouse_age"] == 41
    assert pd.api.types.is_integer_dtype(main["age"])
    assert tables["visits"]["visit_date"][0] == dt.datetime(2026, 8, 1)
    assert list(tables["members"].columns) == [
        "_row_id",
        "_parent_id",
        "_parent_table",
        "_index",
        "member_name",
        "member_age",
    ]


def test_dropped_system_keys_do_not_become_columns(run):
    _, tables = run([submission(1)])

    for dropped in ("formhub_uuid", "_attachments", "_geolocation", "_status", "attachments"):
        assert dropped not in tables["main"].columns
    assert "attachments" not in tables


def test_unreadable_values_are_empty_and_logged(run):
    result, tables = run(
        [submission(1, **{"head/age": "n/a"}), submission(2, **{"head/age": "unknown"}), submission(3)]
    )

    assert tables["main"]["age"].isna().tolist() == [True, True, False]
    assert "main.age: 2 values could not be read as integer; stored as empty." in result.log


def test_answers_to_questions_no_longer_in_the_form_are_kept_as_text(run):
    _, tables = run([submission(1, **{"old_question": 12}), submission(2)])

    assert tables["main"]["old_question"].tolist()[0] == "12"
    assert list(tables["main"].columns)[-1] == "old_question"


def test_column_names_do_not_depend_on_which_submission_comes_first(run, tmp_path):
    late_spouse = submission(1)
    del late_spouse["head/age"]
    _, tables = run([late_spouse, submission(2)])

    assert tables["main"].loc[1, "age"] == 45 and tables["main"].loc[1, "spouse_age"] == 41


def test_a_repeat_with_no_answers_still_gets_its_table(run):
    result, tables = run([submission(1, members=0)])

    assert {t.name: t.rows for t in result.tables}["members"] == 0
    assert "visits" not in tables


def test_no_submissions_produce_an_empty_main_table(run):
    result, tables = run([])

    assert result.input_rows == 0
    assert list(tables) == ["main"]
    assert len(tables["main"]) == 0
    assert "_id" in tables["main"].columns


def test_the_working_database_is_removed(run, tmp_path):
    run([submission(1)])

    assert sorted(p.name for p in tmp_path.iterdir()) == ["main.parquet", "members.parquet", "visits.parquet"]
