"""Publishes a step's tables into its per-project schema, atomically.

The tables are loaded into ``<schema>__next`` first; then one short transaction drops the published schema and renames
the new one into place, so readers see either the previous tables or the new ones. A failure before the swap leaves
the published schema untouched.
"""

from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path
from uuid import UUID

import psycopg
from psycopg import sql

from tamiza import interchange, names


class PublishError(Exception):
    pass


@dataclass(frozen=True)
class Table:
    name: str
    path: Path


def publish(connection: psycopg.Connection, project_id: UUID | str, step: str, tables: Sequence[Table]) -> None:
    """Replaces the step's published schema with exactly ``tables``. The connection must be in autocommit mode."""
    published = names.schema_name(project_id, step)
    staging = f"{published}__next"
    connection.execute("SET TIME ZONE 'UTC'")  # naive timestamps from Parquet are UTC
    connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))
    connection.execute(sql.SQL("CREATE SCHEMA {}").format(sql.Identifier(staging)))
    try:
        for table in tables:
            _load(connection, staging, table)
        with connection.transaction():
            connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(published)))
            connection.execute(
                sql.SQL("ALTER SCHEMA {} RENAME TO {}").format(sql.Identifier(staging), sql.Identifier(published))
            )
    except BaseException:
        connection.execute(sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(staging)))
        raise


def drop(connection: psycopg.Connection, project_id: UUID | str, step: str) -> None:
    """Removes the step's published schema, for a script that no longer exists."""
    connection.execute(
        sql.SQL("DROP SCHEMA IF EXISTS {} CASCADE").format(sql.Identifier(names.schema_name(project_id, step)))
    )


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
    if not columns:
        return
    column_list = sql.SQL(", ").join(sql.Identifier(column) for column, _ in columns)
    with (
        connection.cursor() as cursor,
        cursor.copy(sql.SQL("COPY {} ({}) FROM STDIN").format(target, column_list)) as copy,
    ):
        for row in interchange.iter_rows(table.path):
            copy.write_row(row)
