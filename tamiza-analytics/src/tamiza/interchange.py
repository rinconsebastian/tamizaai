"""Tables between the worker and script processes, as Parquet files.

Reading and writing them (pyarrow only) lives in ``tamiza_sdk.tables``, shared with the local SDK, and is re-exported
here. This module adds what only the worker needs: describing a file and streaming its rows through DuckDB.
"""

from collections.abc import Iterator
from pathlib import Path

import duckdb

from tamiza_sdk.tables import read_parquet, write_parquet

__all__ = ["ROW_BATCH", "count_rows", "describe", "iter_rows", "postgres_type", "read_parquet", "write_parquet"]

# Rows handed to the database loader per fetch.
ROW_BATCH = 5_000


def describe(path: Path) -> list[tuple[str, str]]:
    """Column names and DuckDB types of a Parquet file."""
    with duckdb.connect() as con:
        return [
            (row[0], row[1])
            for row in con.execute(f"DESCRIBE SELECT * FROM read_parquet('{_sql_path(path)}')").fetchall()
        ]


def count_rows(path: Path) -> int:
    with duckdb.connect() as con:
        return con.execute(f"SELECT count(*) FROM read_parquet('{_sql_path(path)}')").fetchone()[0]


def iter_rows(path: Path) -> Iterator[tuple]:
    """
    Rows as Python values for loading into PostgreSQL, in the column order of ``describe``. Columns whose type has no
    PostgreSQL counterpart arrive as text. Time-zone-aware timestamps come out as naive UTC datetimes, because
    DuckDB needs pytz, which the image does not install, to turn them into Python objects.
    """
    columns = describe(path)
    select = ", ".join(_python_safe(name, kind) for name, kind in columns) or "*"
    with duckdb.connect() as con:
        cursor = con.execute(f"SELECT {select} FROM read_parquet('{_sql_path(path)}')")
        while batch := cursor.fetchmany(ROW_BATCH):
            yield from batch


def postgres_type(duckdb_type: str) -> str:
    """The PostgreSQL column type for a DuckDB type; anything without a counterpart is published as text."""
    kind = duckdb_type.upper()
    if kind in _INTEGERS:
        return "bigint"
    if kind in ("UBIGINT", "HUGEINT", "UHUGEINT") or kind.startswith("DECIMAL"):
        return "numeric"
    if kind in ("DOUBLE", "FLOAT"):
        return "double precision"
    if kind == "BOOLEAN":
        return "boolean"
    if kind == "DATE":
        return "date"
    if kind.startswith("TIMESTAMP"):
        return "timestamptz"  # naive timestamps are taken as UTC
    return "text"


_INTEGERS = ("BIGINT", "INTEGER", "SMALLINT", "TINYINT", "UINTEGER", "USMALLINT", "UTINYINT")


def _python_safe(name: str, kind: str) -> str:
    quoted = '"' + name.replace('"', '""') + '"'
    if kind == "TIMESTAMP WITH TIME ZONE":
        return f"timezone('UTC', {quoted})"
    if postgres_type(kind) == "text" and kind != "VARCHAR":
        return f"CAST({quoted} AS VARCHAR)"
    return quoted


def _sql_path(path: Path) -> str:
    return str(path).replace("'", "''")
