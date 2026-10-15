"""Previews of the tables a test step produced: names, row counts, column types and the first rows, as JSON.

Reading Parquet only parses data (it never runs the script's code), so this happens in the worker. Values are made
safe for a browser: integers beyond JavaScript's exact range become text, dates and timestamps ISO text, and long
text is cut. The preview of a table is kept under a size limit by showing fewer rows.
"""

import datetime
import decimal
import json
import math
from collections.abc import Mapping, Sequence
from pathlib import Path

import pyarrow.parquet as pq

# The dashboard facts of an indicator table are shared with the local SDK and live there.
from tamiza_sdk.dashboard import MAX_CHART_DIMENSIONS, MAX_CHART_VALUES, column_type, dashboard_view

__all__ = [
    "CUT_MARK",
    "MAX_BYTES",
    "MAX_CHART_DIMENSIONS",
    "MAX_CHART_VALUES",
    "MAX_ROWS",
    "MAX_TABLES",
    "MAX_TEXT",
    "column_type",
    "dashboard_view",
    "step_tables",
    "table_preview",
]

MAX_ROWS = 100
MAX_TABLES = 20  # per step; later tables list only their name and row count
MAX_TEXT = 500
MAX_BYTES = 1024 * 1024
CUT_MARK = "…"
_EXACT_INTEGER = 2**53


def step_tables(tables: Sequence[tuple[str, int, Path] | tuple[str, int, Path, Mapping[str, str]]]) -> list[dict]:
    """
    ``(name, rows, path)`` of each table a step produced, as the test records them, with the question labels of
    its columns by name as a fourth item when it has any.
    """
    previews = []
    for index, (name, rows, path, *labels) in enumerate(tables):
        if index < MAX_TABLES:
            previews.append(table_preview(name, rows, path, labels[0] if labels else None))
        else:
            previews.append(
                {"name": name, "rows": rows, "columns": None, "previewRows": 0, "preview": None, "cutCells": None}
            )
    return previews


def table_preview(name: str, rows: int, path: Path, column_labels: Mapping[str, str] | None = None) -> dict:
    parquet = pq.ParquetFile(path)
    schema = parquet.schema_arrow
    labels = column_labels or {}
    columns = [
        {"name": field.name, "type": column_type(field.type), "label": labels.get(field.name)} for field in schema
    ]
    batch = next(parquet.iter_batches(batch_size=MAX_ROWS), None)
    preview: list[list] = []
    cut_cells: list[list[int]] = []
    if batch is not None:
        values = [batch.column(index).to_pylist() for index in range(batch.num_columns)]
        size = 0
        for row_index in range(batch.num_rows):
            row = []
            row_cuts = []
            for column_index, column in enumerate(values):
                value, cut = _json_value(column[row_index])
                row.append(value)
                if cut:
                    row_cuts.append([row_index, column_index])
            size += len(json.dumps(row, ensure_ascii=False).encode("utf-8")) + 1
            if size > MAX_BYTES:
                break
            preview.append(row)
            cut_cells.extend(row_cuts)
    return {
        "name": name,
        "rows": rows,
        "columns": columns,
        "previewRows": len(preview),
        "preview": preview,
        "cutCells": cut_cells,
    }


def _json_value(value: object) -> tuple[object, bool]:
    """The value as JSON can carry it, and whether it was cut."""
    if value is None or isinstance(value, bool):
        return value, False
    if isinstance(value, int):
        return (str(value) if abs(value) > _EXACT_INTEGER else value), False
    if isinstance(value, float):
        return (value if math.isfinite(value) else None), False
    if isinstance(value, decimal.Decimal):
        number = float(value)
        return (number if math.isfinite(number) else None), False
    if isinstance(value, datetime.datetime):
        aware = value if value.tzinfo is not None else value.replace(tzinfo=datetime.UTC)
        return aware.astimezone(datetime.UTC).isoformat(), False
    if isinstance(value, datetime.date | datetime.time):
        return value.isoformat(), False
    text = value.hex() if isinstance(value, bytes) else str(value)
    if len(text) > MAX_TEXT:
        return text[:MAX_TEXT] + CUT_MARK, True
    return text, False
