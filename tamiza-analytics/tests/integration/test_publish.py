import datetime as dt
import threading
import uuid

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.names import schema_name
from tamiza.publish import PublishError, Table, drop, publish


@pytest.fixture
def project_id():
    return uuid.uuid4()


def table(tmp_path, name, frame):
    path = tmp_path / f"{name}-{uuid.uuid4().hex}.parquet"
    interchange.write_parquet(frame, path)
    return Table(name, path)


def test_tables_types_and_values(database, tmp_path, project_id):
    frame = pd.DataFrame(
        {
            "n": pd.Series([1, None], dtype="Int64"),
            "ratio": [0.5, 1.5],
            "flag": [True, False],
            "day": [dt.date(2026, 9, 1), None],
            "at": pd.to_datetime(["2026-09-01T14:40:00Z", None], utc=True),
            "naive": pd.to_datetime(["2026-09-01 10:00", None]),
            "label": ["Cali", None],
            "kind": pd.Categorical(["a", "b"]),
        }
    )
    with database.connect() as connection:
        publish(connection, project_id, "clean", [table(tmp_path, "main", frame)])

    schema = schema_name(project_id, "clean")
    types = dict(
        database.rows(
            "SELECT column_name, data_type FROM information_schema.columns"
            " WHERE table_schema = %s ORDER BY ordinal_position",
            schema,
        )
    )
    assert types == {
        "n": "bigint",
        "ratio": "double precision",
        "flag": "boolean",
        "day": "date",
        "at": "timestamp with time zone",
        "naive": "timestamp with time zone",
        "label": "text",
        "kind": "text",
    }
    row = database.rows(f'SELECT n, at, naive, kind FROM "{schema}".main ORDER BY n NULLS LAST')[0]
    assert row == (
        1,
        dt.datetime(2026, 9, 1, 14, 40, tzinfo=dt.UTC),
        dt.datetime(2026, 9, 1, 10, 0, tzinfo=dt.UTC),
        "a",
    )
    assert database.scalar(f'SELECT count(*) FROM "{schema}".main') == 2


def test_publishing_again_replaces_every_table(database, tmp_path, project_id):
    with database.connect() as connection:
        publish(connection, project_id, "output", [table(tmp_path, "old", pd.DataFrame({"a": [1]}))])
        publish(connection, project_id, "output", [table(tmp_path, "households", pd.DataFrame({"a": [1, 2]}))])

    schema = schema_name(project_id, "output")
    assert database.rows("SELECT table_name FROM information_schema.tables WHERE table_schema = %s", schema) == [
        ("households",)
    ]
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_a_failed_load_leaves_the_published_schema_untouched(database, tmp_path, project_id):
    with database.connect() as connection:
        publish(connection, project_id, "indicators", [table(tmp_path, "indicators", pd.DataFrame({"value": [1.0]}))])
        bad = table(tmp_path, "indicators", pd.DataFrame({"Not Valid": [2.0]}))
        with pytest.raises(PublishError):
            publish(connection, project_id, "indicators", [bad])
        with pytest.raises(PublishError):
            publish(connection, project_id, "indicators", [Table("../x", bad.path)])

    schema = schema_name(project_id, "indicators")
    assert database.scalar(f'SELECT value FROM "{schema}".indicators') == 1.0
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_reserved_words_are_quoted(database, tmp_path, project_id):
    with database.connect() as connection:
        publish(
            connection, project_id, "output", [table(tmp_path, "order", pd.DataFrame({"select": [1], "from": ["x"]}))]
        )

    assert database.scalar(f'SELECT "select" FROM "{schema_name(project_id, "output")}"."order"') == 1


def test_drop_removes_the_schema(database, tmp_path, project_id):
    with database.connect() as connection:
        publish(connection, project_id, "output", [table(tmp_path, "t", pd.DataFrame({"a": [1]}))])
        drop(connection, project_id, "output")
        drop(connection, project_id, "output")  # absent already: no error

    assert (
        database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname = %s", schema_name(project_id, "output")) == 0
    )


def test_readers_never_see_a_mix_of_old_and_new_tables(database, tmp_path, project_id):
    schema = schema_name(project_id, "clean")
    versions = [
        [table(tmp_path, "a", pd.DataFrame({"v": [n] * n})), table(tmp_path, "b", pd.DataFrame({"v": [n] * n}))]
        for n in (1, 2)
    ]
    with database.connect() as connection:
        publish(connection, project_id, "clean", versions[0])

    seen, errors, stop = set(), [], threading.Event()

    def read():
        with database.connect() as reader:
            while not stop.is_set():
                try:
                    seen.add(
                        reader.execute(
                            f'SELECT (SELECT count(*) FROM "{schema}".a), (SELECT count(*) FROM "{schema}".b)'
                        ).fetchone()
                    )
                except Exception as error:  # noqa: BLE001 — any error is a visible gap
                    errors.append(error)

    thread = threading.Thread(target=read)
    thread.start()
    with database.connect() as connection:
        for i in range(10):
            publish(connection, project_id, "clean", versions[i % 2 == 0])
    stop.set()
    thread.join()

    assert seen <= {(1, 1), (2, 2)}
    assert errors == []
