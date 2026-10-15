"""Previews of the tables a test step produced: names, row counts, column types and the first rows, as JSON.

Reading Parquet only parses data (it never runs the script's code), so this happens in the worker. Values are made
safe for a browser: integers beyond JavaScript's exact range become text, dates and timestamps ISO text, and long
text is cut. The preview of a table is kept under a size limit by showing fewer rows.
"""

import datetime
import decimal
import json
import math
import re
from collections.abc import Mapping, Sequence
from pathlib import Path

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq

MAX_ROWS = 100
MAX_TABLES = 20  # per step; later tables list only their name and row count
MAX_TEXT = 500
MAX_BYTES = 1024 * 1024
CUT_MARK = "…"
_EXACT_INTEGER = 2**53


def column_type(kind: pa.DataType) -> str:
    """One vocabulary for previews and reference tables: integer, decimal, boolean, date, timestamp or text."""
    if pa.types.is_integer(kind):
        return "integer"
    if pa.types.is_floating(kind) or pa.types.is_decimal(kind):
        return "decimal"
    if pa.types.is_boolean(kind):
        return "boolean"
    if pa.types.is_date(kind):
        return "date"
    if pa.types.is_timestamp(kind):
        return "timestamp"
    return "text"


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


# The indicators dashboard's view of an indicator test table (design D8): the facts its widgets would start from.

MAX_CHART_VALUES = 50  # a dimension with more distinct values is listed, not charted
MAX_CHART_DIMENSIONS = 10
_EMPTY = "\x00"  # stands for an empty value while grouping
_GEOMETRY = re.compile(
    r"^\s*(SRID=\d+;)?\s*(MULTI)?(POINT|LINESTRING|POLYGON)\b|^\s*GEOMETRYCOLLECTION\b", re.IGNORECASE
)


def dashboard_view(path: Path) -> dict:
    """
    The table at ``path`` as the indicators dashboard will offer it: its measures (numeric columns, never booleans),
    the default measure (``value``, else the first), whether the ratio of ``numerator`` to ``denominator`` exists, its
    dimensions (the other columns), the text columns that hold WKT geometries, and for the first dimensions the sums per
    value that a bar chart plots. The UI turns these facts into the view and its warnings.
    """
    table = pq.read_table(path)
    kinds = {field.name: column_type(field.type) for field in table.schema}
    measures = [name for name, kind in kinds.items() if kind in ("integer", "decimal")]
    dimensions = [name for name in kinds if name not in measures]
    default_measure = "value" if "value" in measures else (measures[0] if measures else None)
    ratio = "numerator" in measures and "denominator" in measures
    geometry = [name for name in dimensions if kinds[name] == "text" and _holds_geometry(table.column(name))]

    numbers = pd.DataFrame({name: table.column(name).cast(pa.float64()).to_pandas() for name in measures})
    labels = pd.DataFrame({name: table.column(name).cast(pa.string()).to_pandas() for name in dimensions})
    sums = {name: float(numbers[name].sum()) for name in measures}
    totals = {name: total for name, total in sums.items() if math.isfinite(total)}
    default = numbers[default_measure] if default_measure else None
    maximum = default.max() if default is not None and default.notna().any() else None

    charts = {}
    for name in [d for d in dimensions if d not in geometry][:MAX_CHART_DIMENSIONS]:
        charts[name] = _chart(name, labels, numbers, default_measure, ratio)

    return {
        "measures": measures,
        "defaultMeasure": default_measure,
        "ratio": ratio,
        "dimensions": dimensions,
        "defaultDimension": dimensions[0] if dimensions else None,
        "geometry": geometry,
        "rows": table.num_rows,
        "defaultMeasureNulls": int(default.isna().sum()) if default is not None else 0,
        "defaultMeasureMax": float(maximum) if maximum is not None and math.isfinite(maximum) else None,
        "totals": totals,
        "duplicateRows": int(len(labels) - len(labels.drop_duplicates())) if dimensions else 0,
        "charts": charts,
    }


def _holds_geometry(column: pa.ChunkedArray) -> bool:
    """Whether the first 20 values that are not empty are all WKT geometries (and there is at least one)."""
    sample = []
    for value in column.to_pylist():
        if isinstance(value, str) and value.strip():
            sample.append(value)
            if len(sample) == 20:
                break
    return bool(sample) and all(_GEOMETRY.match(value) for value in sample)


def _chart(name: str, labels: pd.DataFrame, numbers: pd.DataFrame, measure: str | None, ratio: bool) -> dict:
    column = labels[name]
    values = int(column.nunique()) + int(column.isna().any())
    if values > MAX_CHART_VALUES:
        return {"values": values, "groups": None}
    keys = column.fillna(_EMPTY)  # told apart from any text value, and listed last
    wanted = [c for c in (measure, *(("numerator", "denominator") if ratio else ())) if c is not None]
    grouped = numbers[wanted].groupby(keys, sort=True).sum(min_count=1) if wanted else None
    groups = []
    present = keys.unique()
    for key in [*sorted(k for k in present if k != _EMPTY), *([_EMPTY] if _EMPTY in present else [])]:
        row = grouped.loc[key] if grouped is not None else None
        groups.append(
            {
                "group": None if key == _EMPTY else key,
                "value": _finite(row[measure]) if row is not None and measure else None,
                "numerator": _finite(row["numerator"]) if row is not None and ratio else None,
                "denominator": _finite(row["denominator"]) if row is not None and ratio else None,
            }
        )
    return {"values": values, "groups": groups}


def _finite(value: object) -> float | None:
    number = float(value)  # type: ignore[arg-type]
    return number if math.isfinite(number) else None
