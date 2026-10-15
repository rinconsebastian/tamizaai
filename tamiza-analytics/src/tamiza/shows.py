"""What ``ctx.show(data, name)`` records in a sample test.

Each call writes the first rows of the value to ``shows/<n>.parquet`` (or its text) and appends one line to
``shows/index.jsonl``, flushed at once, so the previews survive a script that raises or is killed by a limit. The worker
reads them with ``read_index`` after the child ended; the child's files are data, not trusted, so each field is checked.
"""

import json
import re
import sys
from pathlib import Path

import pandas as pd

from tamiza import interchange

MAX_SHOWN = 20
PREVIEW_ROWS = 100
MAX_TEXT = 2000
MAX_NAME = 100
INDEX_FILE = "index.jsonl"
_FILE = re.compile(r"^\d+\.parquet$")


class ShowRecorder:
    def __init__(self, directory: Path, script_file: str):
        self._directory = directory
        self._script_file = script_file
        self._calls = 0
        self._kept = 0

    def record(self, data: object, name: object = None) -> None:
        self._calls += 1
        if self._kept >= MAX_SHOWN:
            return
        self._kept += 1
        label = str(name)[:MAX_NAME] if name is not None else f"shown {self._calls}"
        entry = {"name": label, "line": self._caller_line(), "rows": 1, "file": None, "text": None}
        self._directory.mkdir(parents=True, exist_ok=True)
        try:
            if isinstance(data, pd.Series):
                data = data.to_frame(name="value" if data.name is None else data.name)
            if isinstance(data, pd.DataFrame):
                entry["rows"] = len(data)
                file = f"{self._kept}.parquet"
                interchange.write_parquet(_snapshot(data), self._directory / file)
                entry["file"] = file
            else:
                entry["text"] = _text(data)
        except Exception as error:  # a value that cannot be shown must not stop the script
            entry["file"] = None
            entry["text"] = f"This value could not be shown: {type(error).__name__}: {error}"[:MAX_TEXT]
        with (self._directory / INDEX_FILE).open("a", encoding="utf-8") as index:
            index.write(json.dumps(entry) + "\n")
            index.flush()

    def finish(self) -> None:
        """Tells the analyst, in the step's log, how many calls were not kept."""
        if self._calls > self._kept:
            print(f"INFO ctx.show: {self._calls - self._kept} calls were not kept; a test keeps the first {MAX_SHOWN}.")

    def _caller_line(self) -> int:
        frame = sys._getframe(1)
        while frame is not None and frame.f_code.co_filename != self._script_file:
            frame = frame.f_back
        return frame.f_lineno if frame is not None else 0


def _snapshot(frame: pd.DataFrame) -> pd.DataFrame:
    """The first rows, copied (the script may change the frame later), with plain unique text column names."""
    head = frame.head(PREVIEW_ROWS)
    if any(name is not None for name in head.index.names):
        head = head.reset_index()
    names: list[str] = []
    for column in head.columns:
        text = str(column)
        unique, number = text, 2
        while unique in names:
            unique, number = f"{text}_{number}", number + 1
        names.append(unique)
    return pd.DataFrame(head).copy().set_axis(names, axis=1)


def _text(value: object) -> str:
    text = value if isinstance(value, str) else repr(value)
    return text if len(text) <= MAX_TEXT else text[:MAX_TEXT] + "…"


def read_index(directory: Path) -> list[dict]:
    """The recorded previews in call order, every field checked; unreadable lines (a kill mid-write) are skipped."""
    path = directory / INDEX_FILE
    if not path.is_file():
        return []
    entries = []
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            entry = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(entry, dict):
            continue
        name, line, rows, file, text = (entry.get(key) for key in ("name", "line", "rows", "file", "text"))
        if not isinstance(name, str) or not isinstance(line, int) or not isinstance(rows, int):
            continue
        if file is not None and not (isinstance(file, str) and _FILE.match(file) and (directory / file).is_file()):
            continue
        if text is not None and not isinstance(text, str):
            continue
        if file is None and text is None:
            continue
        entries.append({"name": name[:MAX_NAME], "line": line, "rows": max(rows, 0), "file": file, "text": text})
    return entries[:MAX_SHOWN]
