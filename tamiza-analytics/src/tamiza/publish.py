"""Publishes a step's tables into its per-project schema, atomically.

The tables are loaded into ``<schema>__next`` first; then one short transaction drops the published schema and renames
the new one into place, so readers see either the previous tables or the new ones. A failure before the swap leaves
the published schema untouched.

The swap needs an exclusive lock on the published tables, so it waits for readers that are still using them (a
dashboard page, an export). A waiting exclusive lock also queues every new reader behind it, so each attempt gives up
after ``lock_timeout_seconds`` and is retried after a growing pause: readers are held for one attempt at most, and the
swap succeeds in a gap. After ``max_wait_seconds`` the step fails and the previous tables stay published.

An indicator publishes one table at a time (``publish_table``): the table is loaded into the ``<schema>__next`` schema
and one transaction drops the published table and moves the new one into place, so the exclusive lock is on that table
only and readers of the other indicator tables are never held.
"""

import time
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from uuid import UUID

import psycopg
from psycopg import errors, sql

from tamiza import interchange, names

DEFAULT_LOCK_TIMEOUT_SECONDS = 5.0
DEFAULT_MAX_WAIT_SECONDS = 900.0
_FIRST_PAUSE_SECONDS = 1.0
_LAST_PAUSE_SECONDS = 10.0


class PublishError(Exception):
    pass


@dataclass(frozen=True)
class Table:
    name: str
    path: Path
    column_labels: Mapping[str, str] = field(default_factory=dict)  # published as column comments


def publish(
    connection: psycopg.Connection,
    project_id: UUID | str,
    step: str,
    tables: Sequence[Table],
    *,
    max_wait_seconds: float = DEFAULT_MAX_WAIT_SECONDS,
    lock_timeout_seconds: float = DEFAULT_LOCK_TIMEOUT_SECONDS,
    sleep: Callable[[float], None] = time.sleep,
) -> float:
    """
    Replaces the step's published schema with exactly ``tables``. The connection must be in autocommit mode.
    Returns the seconds the swap took, which is long only when it waited for readers.
    """
    published = names.schema_name(project_id, step)
    staging = f"{published}__next"
    connection.execute("SET TIME ZONE 'UTC'")  # naive timestamps from Parquet are UTC
    connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))
    connection.execute(sql.SQL("CREATE SCHEMA {}").format(sql.Identifier(staging)))
    try:
        for table in tables:
            _load(connection, staging, table)
        return _swap(connection, published, staging, max_wait_seconds, lock_timeout_seconds, sleep)
    except BaseException:
        connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))
        raise


def _swap(
    connection: psycopg.Connection,
    published: str,
    staging: str,
    max_wait_seconds: float,
    lock_timeout_seconds: float,
    sleep: Callable[[float], None],
) -> float:
    def replace_schema() -> None:
        connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(published)))
        connection.execute(
            sql.SQL("ALTER SCHEMA {} RENAME TO {}").format(sql.Identifier(staging), sql.Identifier(published))
        )

    return _locked(connection, replace_schema, max_wait_seconds, lock_timeout_seconds, sleep)


def _locked(
    connection: psycopg.Connection,
    statements: Callable[[], None],
    max_wait_seconds: float,
    lock_timeout_seconds: float,
    sleep: Callable[[float], None],
) -> float:
    """Runs ``statements`` in one transaction that waits for readers, retrying as the module docstring describes."""
    started = time.monotonic()
    pause = _FIRST_PAUSE_SECONDS
    while True:
        try:
            with connection.transaction():
                # SET takes no parameters, so the timeout goes through set_config (local to this transaction).
                connection.execute(
                    "SELECT set_config('lock_timeout', %s, true)", (f"{int(lock_timeout_seconds * 1000)}ms",)
                )
                statements()
            return time.monotonic() - started
        except errors.LockNotAvailable:
            if time.monotonic() - started + pause > max_wait_seconds:
                raise PublishError(
                    f"The published tables were in use for more than {_duration(max_wait_seconds)}; "
                    "the previous tables stay published."
                ) from None
            sleep(pause)
            pause = min(pause * 2, _LAST_PAUSE_SECONDS)


def _duration(seconds: float) -> str:
    if seconds >= 60 and seconds % 60 == 0:
        minutes = int(seconds // 60)
        return f"{minutes} minute{'s' if minutes != 1 else ''}"
    whole = f"{seconds:g}"
    return f"{whole} second{'s' if seconds != 1 else ''}"


def publish_table(
    connection: psycopg.Connection,
    project_id: UUID | str,
    step: str,
    table: Table,
    *,
    max_wait_seconds: float = DEFAULT_MAX_WAIT_SECONDS,
    lock_timeout_seconds: float = DEFAULT_LOCK_TIMEOUT_SECONDS,
    sleep: Callable[[float], None] = time.sleep,
) -> float:
    """
    Replaces one table of the step's schema, creating the schema if it does not exist, and leaves the others as they
    are. The connection must be in autocommit mode. Returns the seconds the swap took.
    """
    published = names.schema_name(project_id, step)
    staging = f"{published}__next"
    connection.execute("SET TIME ZONE 'UTC'")  # naive timestamps from Parquet are UTC
    connection.execute(sql.SQL("CREATE SCHEMA IF NOT EXISTS {}").format(sql.Identifier(published)))
    connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))
    connection.execute(sql.SQL("CREATE SCHEMA {}").format(sql.Identifier(staging)))
    try:
        _load(connection, staging, table)

        def replace_table() -> None:
            connection.execute(sql.SQL("DROP TABLE IF EXISTS {}").format(sql.Identifier(published, table.name)))
            connection.execute(
                sql.SQL("ALTER TABLE {} SET SCHEMA {}").format(
                    sql.Identifier(staging, table.name), sql.Identifier(published)
                )
            )

        return _locked(connection, replace_table, max_wait_seconds, lock_timeout_seconds, sleep)
    finally:
        connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))


def drop_tables_except(
    connection: psycopg.Connection,
    project_id: UUID | str,
    step: str,
    keep: Sequence[str],
    *,
    max_wait_seconds: float = DEFAULT_MAX_WAIT_SECONDS,
    lock_timeout_seconds: float = DEFAULT_LOCK_TIMEOUT_SECONDS,
    sleep: Callable[[float], None] = time.sleep,
) -> list[str]:
    """
    Removes from the step's schema every table not named in ``keep`` (an indicator that no longer exists), creating the
    schema if it does not exist. Waits for readers like a swap does. Returns the names it removed.
    """
    published = names.schema_name(project_id, step)
    connection.execute(sql.SQL("CREATE SCHEMA IF NOT EXISTS {}").format(sql.Identifier(published)))
    present = [
        row[0]
        for row in connection.execute(
            """
            SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = %s AND c.relkind = 'r' ORDER BY c.relname
            """,
            (published,),
        )
    ]
    stale = [name for name in present if name not in set(keep)]
    if stale:

        def drop_stale() -> None:
            for name in stale:
                connection.execute(sql.SQL("DROP TABLE IF EXISTS {}").format(sql.Identifier(published, name)))

        _locked(connection, drop_stale, max_wait_seconds, lock_timeout_seconds, sleep)
    return stale


def drop(connection: psycopg.Connection, project_id: UUID | str, step: str) -> None:
    """Removes the step's published schema, for a script that no longer exists."""
    connection.execute(
        sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(names.schema_name(project_id, step)))
    )


def _comment_columns(connection: psycopg.Connection, schema: str, table: Table, columns: list[str]) -> None:
    """
    Sets the table's column comments in one statement. COMMENT is a utility statement, which takes no bound
    parameters, so each label goes through ``sql.Literal``, which escapes it. Labels for columns the table does not
    have are ignored. The comments belong to the staging table, so they are swapped in with it.
    """
    comments = [
        sql.SQL("COMMENT ON COLUMN {} IS {}").format(sql.Identifier(schema, table.name, column), sql.Literal(label))
        for column in columns
        if (label := table.column_labels.get(column)) is not None
    ]
    if comments:
        connection.execute(sql.SQL("; ").join(comments))


def _load(connection: psycopg.Connection, schema: str, table: Table) -> None:
    if not names.is_identifier(table.name):
        raise PublishError(f"Invalid table name {table.name!r}.")
    columns = interchange.describe(table.path)
    for column, _ in columns:
        if not names.is_identifier(column):
            raise PublishError(f"Invalid column name {column!r} in table {table.name}.")

    target = sql.Identifier(schema, table.name)
    definition = sql.SQL(", ").join(
        sql.SQL("{} {}").format(sql.Identifier(column), sql.SQL(interchange.postgres_type(kind)))
        for column, kind in columns
    )
    connection.execute(sql.SQL("CREATE TABLE {} ({})").format(target, definition))
    _comment_columns(connection, schema, table, [column for column, _ in columns])
    if not columns:
        return
    column_list = sql.SQL(", ").join(sql.Identifier(column) for column, _ in columns)
    with (
        connection.cursor() as cursor,
        cursor.copy(sql.SQL("COPY {} ({}) FROM STDIN").format(target, column_list)) as copy,
    ):
        for row in interchange.iter_rows(table.path):
            copy.write_row(row)
