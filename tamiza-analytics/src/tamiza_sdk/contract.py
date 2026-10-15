"""The contract of a script: loading its function and validating what it returns.

The worker's runner and the local SDK's ``check`` both go through this module, so a script gets the same messages,
the same renamed columns and the same published tables in both places.
"""

import ast
from collections.abc import Callable, Mapping

import pandas as pd

from tamiza_sdk import names

FUNCTIONS = {"clean": "clean", "output": "prepare_output", "indicator": "compute"}


class ScriptError(Exception):
    """A contract violation reported to the analyst, without a traceback."""


def load_function(
    source: str,
    filename: str,
    function_name: str,
    prepare: Callable[[ast.Module, dict], None] | None = None,
):
    """
    Runs ``source`` in a fresh namespace, as the server does, and returns the function ``function_name`` it defines.
    ``prepare`` may change the syntax tree and the namespace before the code runs (the runner uses it to stop at a
    ``# %%`` marker). The code is compiled under ``filename``, so tracebacks name it.
    """
    namespace: dict = {"__name__": "script", "__file__": filename}
    tree = ast.parse(source, filename)
    if prepare is not None:
        prepare(tree, namespace)
    exec(compile(tree, filename, "exec"), namespace)  # noqa: S102 — running the analyst's script is the purpose
    function = namespace.get(function_name)
    if not callable(function):
        raise ScriptError(f"The script does not define the function {function_name}(…).")
    return function


def validated(
    kind: str,
    returned: object,
    name: str | None = None,
    renames: list[tuple[str, str, str]] | None = None,
) -> dict[str, pd.DataFrame]:
    """
    The tables as they would be published, or a ``ScriptError``. Column names are reduced to the published form; each
    change is appended to ``renames`` as ``(table, before, after)`` as it is found, so a caller that passes a list
    still has it when a later table fails.
    """
    renames = [] if renames is None else renames
    function = FUNCTIONS[kind]
    if kind == "indicator":
        if not isinstance(returned, pd.DataFrame):
            raise ScriptError(f"{function} must return a DataFrame, not {_describe(returned)}.")
        if not names.is_valid_table_name(name):
            raise ScriptError(f"The indicator name {name!r} is not allowed.")
        tables = {name: returned}
    else:
        if not isinstance(returned, Mapping) or not all(isinstance(v, pd.DataFrame) for v in returned.values()):
            raise ScriptError(
                f"{function} must return a dictionary of table names to DataFrames, not {_describe(returned)}."
            )
        if not returned:
            raise ScriptError(f"{function} returned no tables.")
        tables = dict(returned)
        for table in tables:
            if not names.is_valid_table_name(table):
                raise ScriptError(
                    f"The table name {table!r} is not allowed. Use lowercase letters, digits and underscores, starting "
                    "with a letter, at most 63 characters."
                )

    normalized = {table: _normalize_columns(table, frame, renames) for table, frame in tables.items()}
    if kind == "indicator" and not any(_is_numeric(dtype) for dtype in normalized[name].dtypes):
        raise ScriptError("The indicator table has no numeric column; add at least one, such as `value`.")
    return normalized


def _is_numeric(dtype: object) -> bool:
    """Integer, floating point and nullable numeric columns; booleans are not measures."""
    return pd.api.types.is_numeric_dtype(dtype) and not pd.api.types.is_bool_dtype(dtype)


def _normalize_columns(table: str, frame: pd.DataFrame, renames: list[tuple[str, str, str]]) -> pd.DataFrame:
    frame = plain(frame)
    original = [str(column) for column in frame.columns]
    reduced = [names.reduce(column) for column in original]
    seen: dict[str, str] = {}
    for before, after in zip(original, reduced, strict=True):
        if after in seen:
            raise ScriptError(
                f"In table {table}, the columns {seen[after]!r} and {before!r} both become {after!r}. Rename one."
            )
        seen[after] = before
    renames.extend((table, before, after) for before, after in zip(original, reduced, strict=True) if before != after)
    return frame.set_axis(reduced, axis=1)


def plain(frame: pd.DataFrame) -> pd.DataFrame:
    """Geometry columns become WKT text; a GeoDataFrame becomes a plain DataFrame. Named indexes become columns."""
    if any(name is not None for name in frame.index.names):
        frame = frame.reset_index()
    geometry = [column for column in frame.columns if getattr(frame[column].dtype, "name", "") == "geometry"]
    if geometry:
        import geopandas  # imported only when needed: it takes most of a second

        frame = pd.DataFrame({column: frame[column] for column in frame.columns})
        for column in geometry:
            frame[column] = geopandas.GeoSeries(frame[column]).to_wkt()
    return pd.DataFrame(frame) if type(frame) is not pd.DataFrame else frame


def _describe(value: object) -> str:
    if isinstance(value, Mapping):
        kinds = sorted({type(v).__name__ for v in value.values()})
        return f"a dictionary of {', '.join(kinds)}"
    return "None" if value is None else f"a {type(value).__name__}"
