"""Tables as Parquet files, read and written with pyarrow only.

Parquet keeps column types (CSV would not) and, unlike pickle, reading it never runs code from the process that wrote
it. The worker and the local SDK read the same way, so a script receives the same dtypes in both.
"""

import math
from pathlib import Path

import pandas as pd
import pyarrow as pa
import pyarrow.parquet as pq


def write_parquet(frame: pd.DataFrame, path: Path) -> None:
    """
    Writes the frame's columns (and a named index, as columns) to a Parquet file. Uses pyarrow, which shares the
    frame's Arrow-backed text instead of copying it; columns mixing types are written as text.
    """
    frame = _normalized(frame)
    try:
        table = pa.Table.from_pandas(frame, preserve_index=False)
    except (pa.ArrowTypeError, pa.ArrowInvalid):
        table = pa.Table.from_pandas(_mixed_as_text(frame), preserve_index=False)
    pq.write_table(table, path)


def _mixed_as_text(frame: pd.DataFrame) -> pd.DataFrame:
    """Object columns that Arrow cannot type (for example [1, "two", 3.0]) become text; missing values stay missing."""
    frame = frame.copy(deep=False)
    for column in frame.columns:
        if frame[column].dtype == object:
            try:
                pa.array(frame[column], from_pandas=True)
            except (pa.ArrowTypeError, pa.ArrowInvalid):
                frame[column] = [None if _missing(value) else str(value) for value in frame[column]]
    return frame


def read_parquet(path: Path) -> pd.DataFrame:
    """
    Reads with pyarrow rather than DuckDB: converting column by column and releasing the Arrow buffers as it goes
    keeps the peak memory about 30% lower, which matters inside a script's memory limit.
    """
    table = pq.read_table(path)
    # Integers and booleans as pandas' nullable types, so a missing value never turns IDs into floats; dates as
    # datetime64 rather than Python date objects, like the timestamp columns.
    return table.to_pandas(split_blocks=True, self_destruct=True, date_as_object=False, types_mapper=_NULLABLE.get)


_NULLABLE = {
    pa.int8(): pd.Int8Dtype(),
    pa.int16(): pd.Int16Dtype(),
    pa.int32(): pd.Int32Dtype(),
    pa.int64(): pd.Int64Dtype(),
    pa.uint8(): pd.UInt8Dtype(),
    pa.uint16(): pd.UInt16Dtype(),
    pa.uint32(): pd.UInt32Dtype(),
    pa.uint64(): pd.UInt64Dtype(),
    pa.bool_(): pd.BooleanDtype(),
}


def _missing(value: object) -> bool:
    # pd.isna would answer element-wise for a list value.
    return value is None or value is pd.NA or value is pd.NaT or (isinstance(value, float) and math.isnan(value))


def _normalized(frame: pd.DataFrame) -> pd.DataFrame:
    """Named indexes become columns (an unnamed one, such as a filtered row index, is dropped) and names become text."""
    if any(name is not None for name in frame.index.names):
        frame = frame.reset_index()
    if not all(isinstance(column, str) for column in frame.columns):
        frame = frame.set_axis([str(column) for column in frame.columns], axis=1)
    return frame
