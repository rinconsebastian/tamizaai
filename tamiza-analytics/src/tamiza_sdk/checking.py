"""Checking a script on your own computer the way the server runs it."""

import html
import tempfile
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path

import pandas as pd
import pyarrow.parquet as pq

from tamiza_sdk import contract, dashboard
from tamiza_sdk import tables as parquet
from tamiza_sdk.contract import FUNCTIONS, ScriptError
from tamiza_sdk.local import load_context, load_tables


@dataclass
class CheckResult:
    """
    What a script would publish. ``tables`` are the tables as the server would hold them after the step: read back from
    Parquet like the next script receives them, with the column names a run publishes; ``renamed`` lists
    ``(table, before, after)`` for each column that was renamed; ``types`` gives each column's type as the dashboards
    know it (``integer``, ``decimal``, ``boolean``, ``date``, ``timestamp`` or ``text``); ``dashboard`` is, for an
    indicator, its table as the indicators dashboard will offer it.
    """

    kind: str
    name: str | None
    tables: dict[str, pd.DataFrame]
    renamed: list[tuple[str, str, str]] = field(default_factory=list)
    types: dict[str, dict[str, str]] = field(default_factory=dict)
    dashboard: dict | None = None

    def __repr__(self) -> str:
        return "\n".join(self._lines())

    def _lines(self) -> list[str]:
        subject = f"indicator {self.name!r}" if self.kind == "indicator" else f"script kind {self.kind!r}"
        count = len(self.tables)
        lines = [f"Check passed for {subject}: {count} table{'' if count == 1 else 's'} would be published."]
        for table, frame in self.tables.items():
            columns = ", ".join(f"{column} ({kind})" for column, kind in self.types.get(table, {}).items())
            lines += [f"  {table}: {len(frame):,} rows", f"    {columns}"]
        lines += [f"  Renamed: table {t}: {before!r} → {after!r}" for t, before, after in self.renamed]
        if self.dashboard is not None:
            facts = self.dashboard
            lines.append("  As the dashboard will see it:")
            lines.append(f"    measures: {', '.join(facts['measures'])} (default: {facts['defaultMeasure']})")
            lines.append(f"    ratio of numerator to denominator: {'available' if facts['ratio'] else 'not available'}")
            lines.append(f"    dimensions: {', '.join(facts['dimensions']) or 'none'}")
            if facts["geometry"]:
                lines.append(f"    geometry columns: {', '.join(facts['geometry'])}")
        return lines

    def _repr_html_(self) -> str:
        return "<pre>" + html.escape(repr(self)) + "</pre>"


def check(
    script: Callable | str | Path,
    kind: str,
    name: str | None = None,
    *,
    tables: dict[str, pd.DataFrame] | None = None,
    folder: str | Path = ".",
    show: bool = True,
) -> CheckResult:
    """
    Runs a script on the downloaded sample with a run's contract, ``ctx`` and validation.

    ``script`` is the contract function itself, or the path of a ``.py`` file, which is run in a fresh namespace as the
    server does (so names you defined only in your notebook are not visible to it). ``kind`` is ``"clean"``,
    ``"output"`` or ``"indicator"``, and an indicator needs its ``name``. The script receives copies of the raw tables
    (script 1) or the clean tables (the others), or of ``tables`` when you pass them, for example the result of the
    check of your script 1. A contract violation raises ``ScriptError`` with the message a run would show; an
    exception in the script is raised as it is, with its traceback.
    """
    if kind not in FUNCTIONS:
        raise ScriptError(f"Unknown script kind {kind!r}.")
    if kind == "indicator" and not name:
        raise ScriptError("Name the indicator you are checking: check(script, 'indicator', name='piped_water_rate').")

    function = _function(script, kind)
    if tables is not None:
        inputs = {table: frame.copy(deep=True) for table, frame in tables.items()}
    else:
        inputs = load_tables("raw" if kind == "clean" else "clean", folder)
    ctx = load_context(folder, show=show)

    returned = function(inputs, ctx)

    renamed: list[tuple[str, str, str]] = []
    try:
        published = contract.validated(kind, returned, name, renamed)
    finally:
        for table, before, after in renamed:
            print(f"INFO Table {table}: column {before!r} published as {after!r}.")
    tables_out, types, facts = _published(published, name if kind == "indicator" else None)
    return CheckResult(kind, name, tables_out, renamed, types, facts)


def _function(script: Callable | str | Path, kind: str) -> Callable:
    if callable(script):
        return script
    path = Path(script)
    if not path.is_file():
        raise ScriptError(f"There is no script file {str(path)!r}.")
    # The file's real path is the file name, so a traceback shows its lines from disk.
    return contract.load_function(path.read_text(encoding="utf-8"), str(path), FUNCTIONS[kind])


def _published(published: dict[str, pd.DataFrame], indicator: str | None) -> tuple[dict, dict, dict | None]:
    """
    Passes each table through Parquet, as the server does between steps and before publishing: what comes back is what
    the next script would receive and what the dashboards would read (nullable integers, a fresh index, mixed values as
    text). Also reads each column's type, and for an indicator the dashboard facts, from that file.
    """
    tables_out: dict[str, pd.DataFrame] = {}
    types: dict[str, dict[str, str]] = {}
    facts = None
    with tempfile.TemporaryDirectory(prefix="tamiza-check-") as directory:
        for table, frame in published.items():
            path = Path(directory) / f"{table}.parquet"
            parquet.write_parquet(frame, path)
            tables_out[table] = parquet.read_parquet(path)
            types[table] = {f.name: dashboard.column_type(f.type) for f in pq.ParquetFile(path).schema_arrow}
            if table == indicator:
                facts = dashboard.dashboard_view(path)
    return tables_out, types, facts
