"""An indicator table as the indicators dashboard will offer it: the facts its widgets would start from.

Shared by the worker (which puts the facts in a sample test's result) and the local SDK (which returns them from
``check``). It reads a Parquet file, so the facts are defined over the types a published table has.
"""

import math
import re
from pathlib import Path

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq

MAX_CHART_VALUES = 50  # a dimension with more distinct values is listed, not charted
MAX_CHART_DIMENSIONS = 10
_EMPTY = "\x00"  # stands for an empty value while grouping
_GEOMETRY = re.compile(
    r"^\s*(SRID=\d+;)?\s*(MULTI)?(POINT|LINESTRING|POLYGON)\b|^\s*GEOMETRYCOLLECTION\b", re.IGNORECASE
)


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
