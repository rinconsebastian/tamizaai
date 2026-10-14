import datetime as dt
import threading
import time
import uuid

import pandas as pd
import pytest
from helpers import column_comments

from tamiza import interchange
from tamiza.names import schema_name
from tamiza.publish import PublishError, Table, drop, drop_tables_except, publish, publish_table


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


# Publishing while the published tables are being read (a dashboard export)


class Reader:
    """Holds a read lock on a published table, as a long export does, until ``release`` is called."""

    def __init__(self, database, schema, table="main"):
        self.release_event = threading.Event()
        self._holding = threading.Event()

        def hold():
            with database.connect() as connection, connection.transaction():
                connection.execute(f'SELECT count(*) FROM "{schema}"."{table}"').fetchone()
                self._holding.set()
                self.release_event.wait(60)

        self._thread = threading.Thread(target=hold)
        self._thread.start()
        assert self._holding.wait(10)

    def release(self):
        self.release_event.set()
        self._thread.join(10)


def publish_two_versions(database, tmp_path, project_id, step="output"):
    first = table(tmp_path, "main", pd.DataFrame({"v": [1, 1]}))
    second = table(tmp_path, "main", pd.DataFrame({"v": [2, 2, 2]}))
    with database.connect() as connection:
        publish(connection, project_id, step, [first])
    return second, schema_name(project_id, step)


def test_the_swap_waits_for_a_reader_in_short_attempts_and_then_succeeds(database, tmp_path, project_id):
    second, schema = publish_two_versions(database, tmp_path, project_id)
    reader = Reader(database, schema)
    pauses = []

    def pause(seconds):
        pauses.append(seconds)
        reader.release()  # the export finished while the step was waiting

    with database.connect() as connection:
        waited = publish(connection, project_id, "output", [second], lock_timeout_seconds=0.3, sleep=pause)

    assert pauses[0] == 1.0
    assert waited >= 0.3
    assert database.scalar(f'SELECT count(*) FROM "{schema}".main') == 3
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_a_reader_that_starts_while_the_swap_waits_is_held_for_one_attempt_at_most(database, tmp_path, project_id):
    second, schema = publish_two_versions(database, tmp_path, project_id)
    reader = Reader(database, schema)
    outcome = {}

    def swap():
        with database.connect() as connection:
            outcome["waited"] = publish(
                connection, project_id, "output", [second], lock_timeout_seconds=1.0, sleep=lambda s: time.sleep(0.05)
            )

    thread = threading.Thread(target=swap)
    thread.start()
    time.sleep(0.4)  # the first attempt is queued for its exclusive lock behind the first reader

    started = time.monotonic()
    count = database.scalar(f'SELECT count(*) FROM "{schema}".main')
    held = time.monotonic() - started

    reader.release()
    thread.join(30)
    assert count == 2  # still the previous tables
    assert held < 4.0
    assert database.scalar(f'SELECT count(*) FROM "{schema}".main') == 3


def test_a_step_gives_up_when_the_tables_stay_in_use_and_keeps_the_previous_ones(database, tmp_path, project_id):
    second, schema = publish_two_versions(database, tmp_path, project_id)
    reader = Reader(database, schema)
    try:
        with (
            database.connect() as connection,
            pytest.raises(PublishError, match="in use for more than 3 seconds") as error,
        ):
            publish(connection, project_id, "output", [second], max_wait_seconds=3, lock_timeout_seconds=0.5)
    finally:
        reader.release()

    assert "previous tables stay published" in str(error.value)
    assert database.scalar(f'SELECT count(*) FROM "{schema}".main') == 2
    assert database.scalar(f'SELECT max(v) FROM "{schema}".main') == 1
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


# Publishing one indicator table at a time


def publish_indicator(database, tmp_path, project_id, name, rows, **options):
    with database.connect() as connection:
        return publish_table(
            connection, project_id, "indicators", table(tmp_path, name, pd.DataFrame({"value": rows})), **options
        )


def indicator_tables(database, project_id):
    schema = schema_name(project_id, "indicators")
    return [
        name
        for (name,) in database.rows(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = %s ORDER BY table_name", schema
        )
    ]


def test_an_indicator_table_is_created_and_replaced_without_touching_the_others(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "coverage", [1, 2])
    publish_indicator(database, tmp_path, project_id, "attendance", [5])
    publish_indicator(database, tmp_path, project_id, "coverage", [1, 2, 3])

    schema = schema_name(project_id, "indicators")
    assert indicator_tables(database, project_id) == ["attendance", "coverage"]
    assert database.scalar(f'SELECT count(*) FROM "{schema}".coverage') == 3
    assert database.scalar(f'SELECT count(*) FROM "{schema}".attendance') == 1
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_a_failed_indicator_load_keeps_its_previous_table(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "coverage", [1, 2])
    bad = Table("coverage", tmp_path / "missing.parquet")

    with database.connect() as connection, pytest.raises(Exception):  # noqa: B017, PT011 — whatever the load raises
        publish_table(connection, project_id, "indicators", bad)

    schema = schema_name(project_id, "indicators")
    assert database.scalar(f'SELECT count(*) FROM "{schema}".coverage') == 2
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_a_reader_of_one_indicator_does_not_delay_another(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "coverage", [1])
    publish_indicator(database, tmp_path, project_id, "attendance", [1])
    reader = Reader(database, schema_name(project_id, "indicators"), "coverage")
    try:
        started = time.monotonic()
        waited = publish_indicator(
            database,
            tmp_path,
            project_id,
            "attendance",
            [1, 2],
            lock_timeout_seconds=0.3,
            sleep=lambda s: pytest.fail(),
        )
    finally:
        reader.release()

    assert time.monotonic() - started < 3.0
    assert waited < 0.3
    schema = schema_name(project_id, "indicators")
    assert database.scalar(f'SELECT count(*) FROM "{schema}".attendance') == 2


def test_the_table_swap_waits_for_a_reader_of_the_same_table_and_then_succeeds(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "coverage", [1])
    schema = schema_name(project_id, "indicators")
    reader = Reader(database, schema, "coverage")
    pauses = []

    def pause(seconds):
        pauses.append(seconds)
        reader.release()  # the export finished while the step was waiting

    waited = publish_indicator(
        database, tmp_path, project_id, "coverage", [1, 2, 3], lock_timeout_seconds=0.3, sleep=pause
    )

    assert pauses[0] == 1.0
    assert waited >= 0.3
    assert database.scalar(f'SELECT count(*) FROM "{schema}".coverage') == 3


def test_an_indicator_gives_up_when_its_table_stays_in_use_and_keeps_the_previous_one(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "coverage", [1])
    schema = schema_name(project_id, "indicators")
    reader = Reader(database, schema, "coverage")
    try:
        with pytest.raises(PublishError, match="previous tables stay published"):
            publish_indicator(
                database, tmp_path, project_id, "coverage", [1, 2], max_wait_seconds=3, lock_timeout_seconds=0.5
            )
    finally:
        reader.release()

    assert database.scalar(f'SELECT count(*) FROM "{schema}".coverage') == 1
    assert database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname LIKE '%%__next'") == 0


def test_tables_of_removed_indicators_are_dropped_and_the_others_stay(database, tmp_path, project_id):
    for name in ("coverage", "attendance", "old_rate"):
        publish_indicator(database, tmp_path, project_id, name, [1])

    with database.connect() as connection:
        removed = drop_tables_except(connection, project_id, "indicators", ["coverage", "attendance"])

    assert removed == ["old_rate"]
    assert indicator_tables(database, project_id) == ["attendance", "coverage"]


def test_dropping_removed_tables_creates_the_schema_when_there_is_none(database, project_id):
    with database.connect() as connection:
        assert drop_tables_except(connection, project_id, "indicators", ["coverage"]) == []

    assert (
        database.scalar("SELECT count(*) FROM pg_namespace WHERE nspname = %s", schema_name(project_id, "indicators"))
        == 1
    )


def test_a_removed_indicator_table_in_use_is_waited_for(database, tmp_path, project_id):
    publish_indicator(database, tmp_path, project_id, "old_rate", [1])
    reader = Reader(database, schema_name(project_id, "indicators"), "old_rate")

    def pause(seconds):
        reader.release()

    with database.connect() as connection:
        removed = drop_tables_except(connection, project_id, "indicators", [], lock_timeout_seconds=0.3, sleep=pause)

    assert removed == ["old_rate"]
    assert indicator_tables(database, project_id) == []
