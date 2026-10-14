import datetime as dt
import threading
import uuid

import pandas as pd
import pytest
from helpers import column_comments

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


# Column comments (question labels)


def labeled_table(tmp_path, name, frame, labels):
    path = tmp_path / f"{name}-{uuid.uuid4().hex}.parquet"
    interchange.write_parquet(frame, path)
    return Table(name, path, labels)


def comments(database, project_id, step, name):
    return column_comments(database, schema_name(project_id, step), name)


def test_labels_are_published_as_column_comments_with_their_tables(database, tmp_path, project_id):
    frame = pd.DataFrame({"has_water": ["Yes"], "age": [30]})
    with database.connect() as connection:
        publish(
            connection, project_id, "raw", [labeled_table(tmp_path, "main", frame, {"has_water": "Has running water?"})]
        )

    assert comments(database, project_id, "raw", "main") == {"has_water": "Has running water?", "age": None}


def test_a_label_is_stored_exactly_as_written(database, tmp_path, project_id):
    labels = {
        "quote": 'It\'s the "age"',
        "backslash": "a \\ b \\\\ c",
        "newline": "first line\nsecond line",
        "injection": "x'; DROP SCHEMA public CASCADE; --",
        "long": "L" * 2000,
        "accents": "¿Cuántos años tiene? ñ ü",
    }
    frame = pd.DataFrame({column: [1] for column in labels})
    with database.connect() as connection:
        publish(connection, project_id, "raw", [labeled_table(tmp_path, "main", frame, labels)])

    assert comments(database, project_id, "raw", "main") == labels
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname = 'public'") == 1


def test_a_label_for_a_column_the_table_does_not_have_is_ignored(database, tmp_path, project_id):
    with database.connect() as connection:
        publish(
            connection,
            project_id,
            "raw",
            [labeled_table(tmp_path, "main", pd.DataFrame({"a": [1]}), {"a": "Kept", "gone": "Ignored"})],
        )

    assert comments(database, project_id, "raw", "main") == {"a": "Kept"}


def test_publishing_again_replaces_the_comments(database, tmp_path, project_id):
    frame = pd.DataFrame({"a": [1], "b": [2]})
    with database.connect() as connection:
        publish(connection, project_id, "raw", [labeled_table(tmp_path, "main", frame, {"a": "First", "b": "Second"})])
        publish(connection, project_id, "raw", [labeled_table(tmp_path, "main", frame, {"a": "Renewed"})])

    assert comments(database, project_id, "raw", "main") == {"a": "Renewed", "b": None}


def test_publishing_without_labels_leaves_no_comments(database, tmp_path, project_id):
    frame = pd.DataFrame({"a": [1]})
    with database.connect() as connection:
        publish(connection, project_id, "raw", [labeled_table(tmp_path, "main", frame, {"a": "First"})])
        publish(connection, project_id, "raw", [table(tmp_path, "main", frame)])

    assert comments(database, project_id, "raw", "main") == {"a": None}


def test_a_failed_load_leaves_the_published_tables_and_their_comments_untouched(database, tmp_path, project_id):
    good = labeled_table(tmp_path, "main", pd.DataFrame({"a": [1]}), {"a": "Original"})
    bad = labeled_table(tmp_path, "main", pd.DataFrame({"Not Valid": [2]}), {"Not Valid": "Never published"})
    with database.connect() as connection:
        publish(connection, project_id, "raw", [good])
        with pytest.raises(PublishError):
            publish(connection, project_id, "raw", [bad])

    assert comments(database, project_id, "raw", "main") == {"a": "Original"}
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0
