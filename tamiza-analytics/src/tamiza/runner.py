"""Runs one user script in a child process.

``python -I -u -m tamiza.runner <step dir> <kind> [<name>] [--show] [--stop-line N]``

The step directory holds ``script.py``, ``context.json``, ``input/<table>.parquet`` and ``reference/<table>.parquet``.
The runner calls the script's contract function and writes ``output/<table>.parquet`` and ``result.json``; an
indicator script returns one DataFrame, written as the table named after the indicator (``<name>``). Everything
the script prints, and its ``ctx.log`` messages, go to stdout, which the worker captures as the step's log. The runner
never touches the database.

``--show`` (sample tests) records what the script passes to ``ctx.show``; ``--stop-line N`` stops the script, with no
output and no validation, when execution first reaches the ``# %%`` marker on line N (see ``cells``).

What ``ctx`` is, how a script is loaded and how its result is validated live in ``tamiza_sdk``, which the local SDK
shares; this module keeps what only a worker's child process does.
"""

import ast
import json
import linecache
import sys
import traceback
from pathlib import Path

from tamiza import cells, interchange
from tamiza.shows import ShowRecorder
from tamiza_sdk import contract
from tamiza_sdk.context import Context, Form, ReferenceTableNotFound, ReferenceTables, build_context
from tamiza_sdk.contract import FUNCTIONS, ScriptError

__all__ = [
    "FUNCTIONS",
    "Context",
    "Form",
    "ReferenceTableNotFound",
    "ReferenceTables",
    "ScriptError",
    "main",
    "run_step",
]

SCRIPT_FILE = "script.py"
REFERENCE_DIR = "reference"
SHOWS_DIR = "shows"


def run_step(
    step_dir: Path, kind: str, name: str | None = None, show: bool = False, stop_line: int | None = None
) -> dict:
    """
    Runs the script and returns the result written to ``result.json``. Never raises. ``name`` is the indicator's name
    for the ``indicator`` kind: the table its DataFrame is published as. With ``show``, ``ctx.show`` records its
    previews; with ``stop_line``, the script stops at that marker and the result is ``stopped``, with no tables.
    """
    recorder = None
    try:
        if kind not in FUNCTIONS:
            raise ScriptError(f"Unknown script kind {kind!r}.")
        recorder = ShowRecorder(step_dir / SHOWS_DIR, SCRIPT_FILE) if show else None
        context = _context(step_dir, recorder)
        inputs = {path.stem: interchange.read_parquet(path) for path in sorted((step_dir / "input").glob("*.parquet"))}
        function = _load(step_dir / SCRIPT_FILE, FUNCTIONS[kind], stop_line)
        returned = function(inputs, context)
        renames: list[tuple[str, str, str]] = []
        try:
            tables = contract.validated(kind, returned, name, renames)
        finally:
            for table, before, after in renames:
                print(f"INFO Table {table}: column {before!r} published as {after!r}.")
        written = _write(step_dir / "output", tables)
        return {"status": "succeeded", "tables": written}
    except cells.CellStop as stop:
        return {"status": "stopped", "line": stop.line}
    except (ScriptError, cells.MarkerError) as error:
        return {"status": "failed", "error": str(error)}
    except SystemExit as error:
        return {"status": "failed", "error": f"The script called exit({error.code})."}
    except BaseException as error:  # the script's own exception, with its traceback
        return {"status": "failed", "error": _one_line(error), "traceback": _script_traceback(error)}
    finally:
        if recorder is not None:
            recorder.finish()


def _context(step_dir: Path, recorder: ShowRecorder | None = None) -> Context:
    data = json.loads((step_dir / "context.json").read_text(encoding="utf-8"))
    return build_context(data, step_dir / REFERENCE_DIR, recorder.record if recorder is not None else None)


def _load(path: Path, function_name: str, stop_line: int | None = None):
    source = path.read_text(encoding="utf-8")
    # Lets tracebacks show the script's lines, which are compiled from a string.
    linecache.cache[SCRIPT_FILE] = (len(source), None, source.splitlines(keepends=True), SCRIPT_FILE)
    prepare = None
    if stop_line is not None:

        def prepare(tree: ast.Module, namespace: dict) -> None:
            cells.insert_stop(tree, source, stop_line)

            def stop() -> None:
                raise cells.CellStop(stop_line)

            namespace[cells.STOP_NAME] = stop

    return contract.load_function(source, SCRIPT_FILE, function_name, prepare)


def _write(out_dir: Path, tables: dict) -> list[dict]:
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []
    for name, frame in tables.items():
        interchange.write_parquet(frame, out_dir / f"{name}.parquet")
        written.append({"name": name, "rows": len(frame)})
    return written


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
    rest = argv[3:]
    show = "--show" in rest
    stop_line = int(rest[rest.index("--stop-line") + 1]) if "--stop-line" in rest else None
    name = next((a for a in rest if not a.startswith("--") and a != str(stop_line)), None)
    result = run_step(step_dir, kind, name, show, stop_line)
    sys.stdout.flush()
    (step_dir / "result.json").write_text(json.dumps(result), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
