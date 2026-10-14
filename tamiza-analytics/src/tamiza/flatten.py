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
from tamiza.labels import Definition, Labels
from tamiza.question_labels import QuestionLabels

# A submission and the form version it was made with (None when it has none).
Submission = tuple[Mapping[str, object], str | None]

MULTIPLE_SEPARATOR = "; "
MAX_LISTED_COLUMNS = 20  # columns named per table in the question-label log line

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


@dataclass(frozen=True)
class ColumnInfo:
    """A column that holds answers: its name, the submission key it comes from, and its question label if any."""

    name: str
    path: str
    label: str | None = None


@dataclass
class TableOutput:
    name: str
    rows: int
    path: Path
    columns: list[ColumnInfo] = field(default_factory=list)  # the columns that hold answers, not the system columns

    @property
    def column_labels(self) -> dict[str, str]:
        """The question label of each column that has one, by column name."""
        return {c.name: c.label for c in self.columns if c.label is not None}


@dataclass
class FlattenResult:
    input_rows: int
    tables: list[TableOutput]
    log: list[str]
    question_labels_version: str | None = None  # the form version the column labels came from, when applied


def flatten(
    form_fields: list[Mapping[str, object]],
    submissions: Callable[[], Iterable[Submission]],
    out_dir: Path,
    batch_rows: int = BATCH_ROWS,
    labels: Labels | None = None,
    question_labels: QuestionLabels | None = None,
) -> FlattenResult:
    """
    ``form_fields`` is the project's form snapshot (``name``, ``xpath``, ``type``...); ``submissions`` returns a fresh
    iterable of ``(submission, form version)`` pairs each time it is called (it is read twice). Writes
    ``<table>.parquet`` files into ``out_dir``.

    With ``labels``, the answers to select questions are written as labels, from the definition of the form version of
    each submission (repeat rows use their submission's); see :meth:`_Writer.translate`. Without them the codes Kobo
    sent are kept.

    With ``question_labels``, each table's ``columns`` carry the label of their question in the deployed form version
    (see :mod:`tamiza.question_labels`). The columns are listed, with their path, whether or not labels are applied.
    """
    types = {str(f["xpath"]): str(f.get("type") or "") for f in form_fields}
    form_order = {str(f["xpath"]): index for index, f in enumerate(form_fields)}

    main = _Table(path=None, parent=None)
    tables: dict[str | None, _Table] = {None: main}
    for submission, _ in submissions():
        _discover(submission, main, tables, types)
    _assign_names(tables, form_order)

    writer = _Writer(out_dir, tables, batch_rows, labels)
    input_rows = 0
    try:
        for submission, version in submissions():
            input_rows += 1
            _emit(
                submission, main, tables, writer, parent_id=None, index=None, definition=writer.definition_for(version)
            )
        outputs = writer.finish()
    finally:
        writer.close()
    by_name = {table.name: table for table in tables.values()}
    for output in outputs:
        output.columns = _column_infos(by_name[output.name], question_labels)

    log = [f"Flattened {input_rows} submissions into {len(outputs)} tables."]
    log += [f"{output.name}: {output.rows} rows" for output in outputs]
    log += [
        f"{table}.{column}: {count} value{'s' if count != 1 else ''} could not be read as {kind}; stored as empty."
        for (table, column, kind), count in sorted(writer.unreadable.items())
    ]
    log += writer.label_log()
    log += _question_label_log(outputs, question_labels)
    version = question_labels.version if question_labels is not None and question_labels.applied else None
    return FlattenResult(input_rows, outputs, log, version)


def _column_infos(table: _Table, question_labels: QuestionLabels | None) -> list[ColumnInfo]:
    if question_labels is None or not question_labels.applied:
        return [ColumnInfo(column.name, column.path) for column in table.columns.values()]
    return [
        ColumnInfo(column.name, column.path, question_labels.label_of(column.path)) for column in table.columns.values()
    ]


def _question_label_log(outputs: list[TableOutput], question_labels: QuestionLabels | None) -> list[str]:
    """
    Where the column labels came from and, per table, the columns whose question the deployed version does not have.
    Nothing is reported without labels, and when they are off only the reason is, since the deployed version's paths
    are then unknown.
    """
    if question_labels is None:
        return []
    lines = [question_labels.log_line()]
    if not question_labels.applied:
        return lines
    for output in outputs:
        missing = [c.name for c in output.columns if not question_labels.in_deployed_version(c.path)]
        if missing:
            listed = ", ".join(missing[:MAX_LISTED_COLUMNS])
            more = f", and {len(missing) - MAX_LISTED_COLUMNS} more" if len(missing) > MAX_LISTED_COLUMNS else ""
            plural = len(missing) != 1
            lines.append(
                f"{output.name}: {_count(len(missing), 'column')} {'are' if plural else 'is'} not in the deployed form "
                f"version and {'have' if plural else 'has'} no question label: {listed}{more}."
            )
    return lines


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
    definition: Definition | None,
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
            value = writer.translate(table, column.name, key, value, definition)
            values[column.name] = writer.convert(table, column.name, column.kind, value)
        elif key in tables and _is_repeat(value):
            for position, item in enumerate(value, start=1):  # type: ignore[arg-type]
                _emit(item, tables[key], tables, writer, parent_id=row_id, index=position, definition=definition)  # type: ignore[arg-type]
    writer.add(table, values)


class _Writer:
    """Buffers converted rows per table and appends them to typed DuckDB tables, then exports Parquet."""

    def __init__(self, out_dir: Path, tables: dict[str | None, _Table], batch_rows: int, labels: Labels | None = None):
        self.out_dir = out_dir
        self.tables = list(tables.values())
        self.batch_rows = batch_rows
        self.labels = labels
        self.unreadable: Counter[tuple[str, str, str]] = Counter()
        # Label fallbacks, counted to be reported: per column, per version without a definition, and without a version.
        self._from_current: Counter[tuple[str, str]] = Counter()
        self._kept: Counter[tuple[str, str]] = Counter()
        self._used_current: Counter[str] = Counter()
        self._without_version = 0
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

    def definition_for(self, version: str | None) -> Definition | None:
        """
        The definition that translates one submission, resolved once and passed down to its repeat rows: its version's
        when available, otherwise the current form, which is counted. None when no labels were given.
        """
        if self.labels is None:
            return None
        definition, own = self.labels.for_submission(version)
        if not own:
            if version is None:
                self._without_version += 1
            else:
                self._used_current[version] += 1
        return definition

    def translate(self, table: _Table, column: str, key: str, value: object, definition: Definition | None) -> object:
        """
        Replaces the codes of an answer to a select question with their labels. ``key`` is the answer's path.

        - A code in the submission's definition takes its label there.
        - A code that definition lacks takes the current form's label for the same path, which is what happens to a
          submission edited in Kobo with a later version. If the definition has no select at that path but the
          current form has (a question added by such an edit), the current form's labels are used.
        - A code without a label anywhere is kept. Both fallbacks are counted per column.
        - Anything else, such as an answer to a question without a choice list, passes through uncounted.

        A ``select_multiple`` answer becomes its labels in the order of the answer, joined by ``; ``.
        """
        if definition is None or self.labels is None or not isinstance(value, str):
            return value
        own, current = definition.get(key), self.labels.current.get(key)
        if own is None and current is None:
            return value
        multiple = (own or current).multiple  # type: ignore[union-attr]
        codes = value.split() if multiple else [value.strip()]
        if not codes or codes == [""]:
            return value

        outcomes = set()
        translated = []
        for code in codes:
            if own is not None and code in own.labels:
                translated.append(own.labels[code])
            elif current is not None and code in current.labels:
                translated.append(current.labels[code])
                outcomes.add("current")
            else:
                translated.append(code)
                outcomes.add("kept")
        # Each cell counts at most once per outcome.
        if "current" in outcomes:
            self._from_current[(table.name, column)] += 1
        if "kept" in outcomes:
            self._kept[(table.name, column)] += 1
        return MULTIPLE_SEPARATOR.join(translated) if multiple else translated[0]

    def label_log(self) -> list[str]:
        """The label fallbacks of this run, so none is silent."""
        if self.labels is None:
            return []
        lines = [
            f"Form version {version}: {_count(count, 'submission')} used the current form "
            f"(definition {self.labels.why_unavailable(version)})."
            for version, count in sorted(self._used_current.items())
        ]
        if self._without_version:
            lines.append(f"{_count(self._without_version, 'submission')} without a form version used the current form.")
        lines += [
            f"{table}.{column}: {_count(count, 'value')} not in {'its' if count == 1 else 'their'} form version; "
            "labeled from the current form."
            for (table, column), count in sorted(self._from_current.items())
        ]
        lines += [
            f"{table}.{column}: {_count(count, 'value')} without a label; kept as codes."
            for (table, column), count in sorted(self._kept.items())
        ]
        return lines

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


def _count(count: int, noun: str) -> str:
    return f"{count} {noun}{'s' if count != 1 else ''}"


def _quote(identifier: str) -> str:
    return '"' + identifier.replace('"', '""') + '"'


def _sql_path(path: Path) -> str:
    return str(path).replace("'", "''")
