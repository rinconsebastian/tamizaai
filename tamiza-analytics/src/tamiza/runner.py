"""Runs one user script in a child process: ``python -I -u -m tamiza.runner <step dir> <kind>``.

The step directory holds ``script.py``, ``context.json``, ``input/<table>.parquet`` and ``reference/<table>.parquet``.
The runner calls the script's contract function and writes ``output/<table>.parquet`` and ``result.json``. Everything
the script prints, and its ``ctx.log`` messages, go to stdout, which the worker captures as the step's log. The runner
never touches the database.
"""

import json
import linecache
import logging
import sys
import traceback
from collections.abc import Mapping
from pathlib import Path

import pandas as pd

from tamiza import interchange, names

FUNCTIONS = {"clean": "clean", "output": "prepare_output", "indicators": "compute_indicators"}
INDICATOR_COLUMNS = ("indicator", "disaggregation", "value", "numerator", "denominator")
SCRIPT_FILE = "script.py"
REFERENCE_DIR = "reference"


class ScriptError(Exception):
    """A contract violation reported to the analyst, without a traceback."""


class Form:
    """The form dictionary: ``questions`` and ``choices`` as DataFrames, and ``label`` to translate a choice."""

    def __init__(self, questions: list[dict], choices: list[dict]):
        self.questions = pd.DataFrame(questions, columns=["name", "path", "type", "label", "list_name"])
        self.choices = pd.DataFrame(choices, columns=["list_name", "name", "label"])
        self._lists = {row["name"]: row["list_name"] for row in questions if row.get("list_name")}
        self._lists.update({row["path"]: row["list_name"] for row in questions if row.get("list_name")})
        self._labels = {(row["list_name"], row["name"]): row["label"] for row in choices}

    def label(self, question: str, value: object) -> str | None:
        """The label of ``value`` in the choice list of ``question`` (its name or path), or None if unknown."""
        list_name = self._lists.get(question)
        return None if list_name is None else self._labels.get((list_name, str(value)))


class ReferenceTableNotFound(KeyError):
    """Raised for a reference table the project does not have; the message lists the ones it has."""

    def __init__(self, name: object, available: list[str]):
        super().__init__(name)
        self.name = name
        self.available = available

    def __str__(self) -> str:
        if not self.available:
            return f"There is no reference table {self.name!r}: the project has no reference tables."
        return f"There is no reference table {self.name!r}. Available: {', '.join(self.available)}."


class ReferenceTables(Mapping):
    """
    The project's reference tables by name (``ctx.reference["population"]``). Each table is read from its Parquet file
    the first time a script asks for it, so scripts pay only for the tables they use.
    """

    def __init__(self, directory: Path):
        files = sorted(directory.glob("*.parquet")) if directory.is_dir() else []
        self._paths = {path.stem: path for path in files}
        self._frames: dict[str, pd.DataFrame] = {}

    def __getitem__(self, name: str) -> pd.DataFrame:
        if name not in self._paths:
            raise ReferenceTableNotFound(name, list(self._paths))
        if name not in self._frames:
            self._frames[name] = interchange.read_parquet(self._paths[name])
        return self._frames[name]

    def __contains__(self, name: object) -> bool:
        return name in self._paths

    def __iter__(self):
        return iter(self._paths)

    def __len__(self) -> int:
        return len(self._paths)

    def __repr__(self) -> str:
        return f"<reference tables: {', '.join(self._paths) or 'none'}>"


class Context:
    """What a script receives besides its data."""

    def __init__(self, project_name: str, form: Form, log: logging.Logger, reference: ReferenceTables | None = None):
        self.project_name = project_name
        self.form = form
        self.log = log
        self.reference = reference if reference is not None else ReferenceTables(Path("/nonexistent"))


def run_step(step_dir: Path, kind: str) -> dict:
    """Runs the script and returns the result written to ``result.json``. Never raises."""
    try:
        if kind not in FUNCTIONS:
            raise ScriptError(f"Unknown script kind {kind!r}.")
        context = _context(step_dir)
        inputs = {path.stem: interchange.read_parquet(path) for path in sorted((step_dir / "input").glob("*.parquet"))}
        function = _load(step_dir / SCRIPT_FILE, FUNCTIONS[kind])
        returned = function(inputs, context)
        tables = _validated(kind, returned)
        written = _write(step_dir / "output", tables)
        return {"status": "succeeded", "tables": written}
    except ScriptError as error:
        return {"status": "failed", "error": str(error)}
    except SystemExit as error:
        return {"status": "failed", "error": f"The script called exit({error.code})."}
    except BaseException as error:  # the script's own exception, with its traceback
        return {"status": "failed", "error": _one_line(error), "traceback": _script_traceback(error)}


def _context(step_dir: Path) -> Context:
    data = json.loads((step_dir / "context.json").read_text(encoding="utf-8"))
    log = logging.getLogger("tamiza.script")
    log.handlers.clear()
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(logging.Formatter("%(levelname)s %(message)s"))
    log.addHandler(handler)
    log.setLevel(logging.INFO)
    log.propagate = False
    form = Form(data.get("questions", []), data.get("choices", []))
    return Context(data.get("project_name", ""), form, log, ReferenceTables(step_dir / REFERENCE_DIR))


def _load(path: Path, function_name: str):
    source = path.read_text(encoding="utf-8")
    # Lets tracebacks show the script's lines, which are compiled from a string.
    linecache.cache[SCRIPT_FILE] = (len(source), None, source.splitlines(keepends=True), SCRIPT_FILE)
    namespace: dict = {"__name__": "script", "__file__": SCRIPT_FILE}
    exec(compile(source, SCRIPT_FILE, "exec"), namespace)  # noqa: S102 — running the analyst's script is the purpose
    function = namespace.get(function_name)
    if not callable(function):
        raise ScriptError(f"The script does not define the function {function_name}(…).")
    return function


def _validated(kind: str, returned: object) -> dict[str, pd.DataFrame]:
    function = FUNCTIONS[kind]
    if kind == "indicators":
        if not isinstance(returned, pd.DataFrame):
            raise ScriptError(f"{function} must return a DataFrame, not {type(returned).__name__}.")
        tables = {"indicators": returned}
    else:
        if not isinstance(returned, Mapping) or not all(isinstance(v, pd.DataFrame) for v in returned.values()):
            raise ScriptError(
                f"{function} must return a dictionary of table names to DataFrames, not {_describe(returned)}."
            )
        if not returned:
            raise ScriptError(f"{function} returned no tables.")
        tables = dict(returned)
        for name in tables:
            if not names.is_valid_table_name(name):
                raise ScriptError(
                    f"The table name {name!r} is not allowed. Use lowercase letters, digits and underscores, starting "
                    "with a letter, at most 63 characters."
                )

    normalized = {name: _normalize_columns(name, frame) for name, frame in tables.items()}
    if kind == "indicators":
        missing = [column for column in INDICATOR_COLUMNS if column not in normalized["indicators"].columns]
        if missing:
            raise ScriptError(
                f"The indicator table is missing the column{'s' if len(missing) > 1 else ''} {', '.join(missing)}."
            )
    return normalized


def _normalize_columns(table: str, frame: pd.DataFrame) -> pd.DataFrame:
    frame = _plain(frame)
    original = [str(column) for column in frame.columns]
    reduced = [names.reduce(column) for column in original]
    seen: dict[str, str] = {}
    for before, after in zip(original, reduced, strict=True):
        if after in seen:
            raise ScriptError(
                f"In table {table}, the columns {seen[after]!r} and {before!r} both become {after!r}. Rename one."
            )
        seen[after] = before
    for before, after in zip(original, reduced, strict=True):
        if before != after:
            print(f"INFO Table {table}: column {before!r} published as {after!r}.")
    return frame.set_axis(reduced, axis=1)


def _plain(frame: pd.DataFrame) -> pd.DataFrame:
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


def _write(out_dir: Path, tables: dict[str, pd.DataFrame]) -> list[dict]:
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for name, frame in tables.items():
        interchange.write_parquet(frame, out_dir / f"{name}.parquet")
        written.append({"name": name, "rows": len(frame)})
    return written


def _describe(value: object) -> str:
    if isinstance(value, Mapping):
        kinds = sorted({type(v).__name__ for v in value.values()})
        return f"a dictionary of {', '.join(kinds)}"
    return "None" if value is None else f"a {type(value).__name__}"


def _one_line(error: BaseException) -> str:
    return "".join(traceback.format_exception_only(error)).strip().splitlines()[-1]


def _script_traceback(error: BaseException) -> str:
    """The traceback limited to the script's own lines, which is what the analyst can act on."""
    if isinstance(error, SyntaxError):
        return "".join(traceback.format_exception_only(error)).rstrip()
    frames = [frame for frame in traceback.extract_tb(error.__traceback__) if frame.filename == SCRIPT_FILE]
    lines = [
        "Traceback (most recent call last):\n",
        *traceback.format_list(frames),
        *traceback.format_exception_only(error),
    ]
    return "".join(lines).rstrip()


def main(argv: list[str]) -> int:
    step_dir, kind = Path(argv[1]), argv[2]
    result = run_step(step_dir, kind)
    sys.stdout.flush()
    (step_dir / "result.json").write_text(json.dumps(result), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
