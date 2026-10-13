"""The project's reference tables as Parquet files, read once when a run or test starts.

The API parses the uploaded files and stores each table's rows as a JSON array of arrays, with every column's type
(``integer``, ``decimal``, ``date`` or ``text``). Here the rows are streamed with a server-side cursor and written in
batches with those types, so scripts receive the same columns the project shows.
"""

import datetime
from dataclasses import dataclass
from pathlib import Path
from uuid import UUID

import psycopg
import pyarrow as pa
import pyarrow.parquet as pq

from tamiza import names

BATCH = 5_000

_ARROW_TYPES = {"integer": pa.int64(), "decimal": pa.float64(), "date": pa.date32(), "text": pa.string()}


@dataclass(frozen=True)
class ReferenceTable:
    name: str
    rows: int
    updated_at: datetime.datetime
    path: Path

    def summary(self) -> dict:
        """What the run records about the table: ``{name, rows, updatedAt}``."""
        return {"name": self.name, "rows": self.rows, "updatedAt": self.updated_at.isoformat()}


def load(database_url: str, project_id: UUID, out_dir: Path) -> list[ReferenceTable]:
    """Writes ``out_dir/<name>.parquet`` for each of the project's reference tables, in name order."""
    out_dir.mkdir(parents=True, exist_ok=True)
    loaded = []
    # One connection and one snapshot, so a table replaced meanwhile is read either whole before or whole after.
    with psycopg.connect(database_url) as connection:
        connection.isolation_level = psycopg.IsolationLevel.REPEATABLE_READ
        tables = connection.execute(
            "SELECT id, name, columns, updated_at FROM tamiza.reference_tables WHERE project_id = %s ORDER BY name",
            (project_id,),
        ).fetchall()
        for table_id, name, columns, updated_at in tables:
            if not names.is_valid_table_name(name):  # the database checks it too; never trust a name for a path
                raise ValueError(f"Invalid reference table name {name!r}.")
            path = out_dir / f"{name}.parquet"
            rows = _write(connection, table_id, columns, path)
            loaded.append(ReferenceTable(name, rows, updated_at, path))
    return loaded


def _write(connection: psycopg.Connection, table_id: UUID, columns: list[dict], path: Path) -> int:
    schema = pa.schema([(column["name"], _ARROW_TYPES.get(column["type"], pa.string())) for column in columns])
    count = 0
    with (
        pq.ParquetWriter(path, schema) as writer,
        connection.cursor(name=f"reference_{table_id.hex}") as cursor,
    ):
        cursor.itersize = BATCH
        cursor.execute(
            """
            SELECT row.value
            FROM tamiza.reference_table_contents c, jsonb_array_elements(c.rows) WITH ORDINALITY AS row(value, position)
            WHERE c.reference_table_id = %s
            ORDER BY row.position
            """,
            (table_id,),
        )
        while batch := cursor.fetchmany(BATCH):
            writer.write_table(_batch([row for (row,) in batch], schema))
            count += len(batch)
        if count == 0:
            writer.write_table(schema.empty_table())
    return count


def _batch(rows: list[list], schema: pa.Schema) -> pa.Table:
    arrays = []
    for index, field in enumerate(schema):
        values = [row[index] if index < len(row) else None for row in rows]
        if field.type == pa.date32():
            values = [None if value is None else datetime.date.fromisoformat(value) for value in values]
        elif field.type == pa.string():
            values = [None if value is None else str(value) for value in values]
        arrays.append(pa.array(values, type=field.type))
    return pa.Table.from_arrays(arrays, schema=schema)
