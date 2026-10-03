"""Flattens raw Kobo submissions into one main table and one table per repeat group, written as Parquet files.

Two passes over the submissions: the first finds every table and column (so names are assigned once, in form order,
whatever order the data arrives in); the second converts the answers and writes them in batches through a DuckDB
database file, so memory stays bounded for hundreds of thousands of submissions.
"""

from collections import Counter
from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass, field
from pathlib import Path

import duckdb
import pandas as pd

from tamiza import names
from tamiza.convert import SQL_TYPES, Unreadable, convert, kind_of

MAIN = "main"
BATCH_ROWS = 2_000

# Kobo system keys kept on the main table, with their column name and kind.
_SYSTEM_COLUMNS = {
    "_id": ("_id", "integer"),
    "_uuid": ("_uuid", "text"),
    "_submission_time": ("_submission_time", "timestamp"),
    "__version__": ("_version", "text"),
    "meta/instanceID": ("_instance_id", "text"),
}
_REPEAT_COLUMNS = (("_row_id", "integer"), ("_parent_id", "integer"), ("_parent_table", "text"), ("_index", "integer"))


@dataclass
class _Column:
    path: str
    kind: str
    name: str = ""


@dataclass
class _Table:
    path: str | None  # None for the main table
    parent: "_Table | None"
    name: str = ""
    columns: dict[str, _Column] = field(default_factory=dict)  # by key path, in discovery order


@dataclass
class TableOutput:
    name: str
    rows: int
    path: Path


@dataclass
class FlattenResult:
    input_rows: int
    tables: list[TableOutput]
    log: list[str]


def flatten(
    form_fields: list[Mapping[str, object]],
    submissions: Callable[[], Iterable[Mapping[str, object]]],
    out_dir: Path,
    batch_rows: int = BATCH_ROWS,
) -> FlattenResult:
    """
    ``form_fields`` is the project's form snapshot (``name``, ``xpath``, ``type``...); ``submissions`` returns a fresh
    iterable of submission dictionaries each time it is called (it is read twice). Writes ``<table>.parquet`` files
    into ``out_dir``.
    """
    types = {str(f["xpath"]): str(f.get("type") or "") for f in form_fields}
    form_order = {str(f["xpath"]): index for index, f in enumerate(form_fields)}

    main = _Table(path=None, parent=None)
    tables: dict[str | None, _Table] = {None: main}
    for submission in submissions():
        _discover(submission, main, tables, types)
    _assign_names(tables, form_order)

    writer = _Writer(out_dir, tables, batch_rows)
    input_rows = 0
    try:
        for submission in submissions():
            input_rows += 1
            _emit(submission, main, tables, writer, parent_id=None, index=None)
        outputs = writer.finish()
    finally:
        writer.close()

    log = [f"Flattened {input_rows} submissions into {len(outputs)} tables."]
    log += [f"{output.name}: {output.rows} rows" for output in outputs]
    log += [
        f"{table}.{column}: {count} value{'s' if count != 1 else ''} could not be read as {kind}; stored as empty."
        for (table, column, kind), count in sorted(writer.unreadable.items())
    ]
    return FlattenResult(input_rows, outputs, log)


def _is_dropped(key: str) -> bool:
    if key in _SYSTEM_COLUMNS:
        return False
    return key.startswith("_") or key.startswith("formhub/") or key.startswith("meta/")


def _is_repeat(value: object) -> bool:
    return isinstance(value, list) and all(isinstance(item, Mapping) for item in value)


def _discover(
    row: Mapping[str, object], table: _Table, tables: dict[str | None, _Table], types: dict[str, str]
) -> None:
    for key, value in row.items():
        if table.path is None and _is_dropped(key):
            continue
        if table.path is None and key in _SYSTEM_COLUMNS:
            continue
        if _is_repeat(value) and not key.startswith("_"):
            child = tables.get(key)
            if child is None:
                child = tables[key] = _Table(path=key, parent=table)
            for item in value:  # type: ignore[union-attr]
                _discover(item, child, tables, types)
        elif key not in table.columns:
            table.columns[key] = _Column(path=key, kind=kind_of(types.get(key)))


def _assign_names(tables: dict[str | None, _Table], form_order: dict[str, int]) -> None:
    table_names = names.deduplicate(
        [MAIN] + [names.reduce(path.rsplit("/", 1)[-1]) for path in tables if path is not None]
    )
    for table, name in zip(tables.values(), table_names, strict=True):
        table.name = name
        reserved = [column for column, _ in _system_columns(table)]
        # Questions in form order; keys the current form no longer has follow, in the order they were found.
        ordered = sorted(
            table.columns.values(), key=lambda c: (form_order.get(c.path, len(form_order)), _position(table, c))
        )
        taken = set(reserved)
        chosen = []
        for column in ordered:
            candidate = names.reduce(column.path.rsplit("/", 1)[-1])
            if candidate in taken:
                candidate = names.reduce(column.path)
            chosen.append(candidate)
            taken.add(candidate)
        for column, name in zip(ordered, names.deduplicate(reserved + chosen)[len(reserved) :], strict=True):
            column.name = name
        table.columns = {column.path: column for column in ordered}


def _position(table: _Table, column: _Column) -> int:
    return list(table.columns).index(column.path)


def _system_columns(table: _Table) -> list[tuple[str, str]]:
    return list(_SYSTEM_COLUMNS.values()) if table.path is None else list(_REPEAT_COLUMNS)


def _emit(
    row: Mapping[str, object],
    table: _Table,
    tables: dict[str | None, _Table],
    writer: "_Writer",
    parent_id: int | None,
    index: int | None,
) -> None:
    values: dict[str, object] = {}
    if table.path is None:
        for key, (column, kind) in _SYSTEM_COLUMNS.items():
            values[column] = writer.convert(table, column, kind, row.get(key))
        row_id = values["_id"]
    else:
        row_id = writer.next_row_id(table)
        values.update(_row_id=row_id, _parent_id=parent_id, _parent_table=table.parent.name, _index=index)  # type: ignore[union-attr]

    for key, value in row.items():
        column = table.columns.get(key)
        if column is not None:
            values[column.name] = writer.convert(table, column.name, column.kind, value)
        elif key in tables and _is_repeat(value):
            for position, item in enumerate(value, start=1):  # type: ignore[arg-type]
                _emit(item, tables[key], tables, writer, parent_id=row_id, index=position)  # type: ignore[arg-type]
    writer.add(table, values)


class _Writer:
    """Buffers converted rows per table and appends them to typed DuckDB tables, then exports Parquet."""

    def __init__(self, out_dir: Path, tables: dict[str | None, _Table], batch_rows: int):
        self.out_dir = out_dir
        self.tables = list(tables.values())
        self.batch_rows = batch_rows
        self.unreadable: Counter[tuple[str, str, str]] = Counter()
        self._buffers: dict[str, list[dict[str, object]]] = {t.name: [] for t in self.tables}
        self._row_ids: Counter[str] = Counter()
        self._columns = {
            t.name: _system_columns(t) + [(c.name, c.kind) for c in t.columns.values()] for t in self.tables
        }
        out_dir.mkdir(parents=True, exist_ok=True)
        self._db_path = out_dir / "flatten.duckdb"
        self._db = duckdb.connect(str(self._db_path))
        for table in self.tables:
            definition = ", ".join(f"{_quote(name)} {SQL_TYPES[kind]}" for name, kind in self._columns[table.name])
            self._db.execute(f"CREATE TABLE {_quote(table.name)} ({definition})")

    def convert(self, table: _Table, column: str, kind: str, value: object) -> object:
        try:
            return convert(kind, value)
        except Unreadable:
            self.unreadable[(table.name, column, kind)] += 1
            return None

    def next_row_id(self, table: _Table) -> int:
        self._row_ids[table.name] += 1
        return self._row_ids[table.name]

    def add(self, table: _Table, values: dict[str, object]) -> None:
        buffer = self._buffers[table.name]
        buffer.append(values)
        if len(buffer) >= self.batch_rows:
            self._flush(table.name)

    def _flush(self, name: str) -> None:
        buffer = self._buffers[name]
        if not buffer:
            return
        columns = self._columns[name]
        # Object columns keep Python ints exact (pandas would turn [1, None] into floats); DuckDB casts each column.
        frame = pd.DataFrame(
            {column: pd.Series([row.get(column) for row in buffer], dtype="object") for column, _ in columns}
        )
        self._db.register("batch", frame)
        select = ", ".join(f"CAST({_quote(column)} AS {SQL_TYPES[kind]})" for column, kind in columns)
        self._db.execute(f"INSERT INTO {_quote(name)} SELECT {select} FROM batch")
        self._db.unregister("batch")
        buffer.clear()

    def finish(self) -> list[TableOutput]:
        outputs = []
        for table in self.tables:
            self._flush(table.name)
            path = self.out_dir / f"{table.name}.parquet"
            self._db.execute(f"COPY {_quote(table.name)} TO '{_sql_path(path)}' (FORMAT PARQUET)")
            rows = self._db.execute(f"SELECT count(*) FROM {_quote(table.name)}").fetchone()[0]
            outputs.append(TableOutput(table.name, rows, path))
        return outputs

    def close(self) -> None:
        self._db.close()
        self._db_path.unlink(missing_ok=True)
        Path(str(self._db_path) + ".wal").unlink(missing_ok=True)


def _quote(identifier: str) -> str:
    return '"' + identifier.replace('"', '""') + '"'


def _sql_path(path: Path) -> str:
    return str(path).replace("'", "''")
