"""Reusable results of a script test's first steps: the flattened sample and the result of the saved script 1.

A test that finds an entry with the same key skips the work; the key covers everything the step read, so reuse never
changes an outcome. Entries are directories of Parquet files and a ``step.json`` under the worker's own work directory.
An entry is built in a ``.tmp-`` directory and renamed, so a crash never leaves a half entry. The total size is kept
under a limit by removing the least recently used entries; removing one only makes the next test compute it again.
"""

import dataclasses
import hashlib
import json
import logging
import os
import shutil
import time
import uuid
from collections.abc import Collection, Mapping
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from tamiza import __version__

logger = logging.getLogger("tamiza.sample_cache")

FORMAT_VERSION = 1  # bump when what an entry holds, or what a key covers, changes
CACHE_DIR = "sample-cache"
TEMP_PREFIX = ".tmp-"
STEP_FILE = "step.json"
TABLES_DIR = "tables"
UNUSED_HOURS = 24  # the lifetime of a test: an entry unused for that long may belong to a deleted project


def _plain(value: object) -> object:
    """What ``json`` cannot encode, for hashing: dataclasses, sets, dates and paths."""
    if dataclasses.is_dataclass(value) and not isinstance(value, type):
        return dataclasses.asdict(value)
    if isinstance(value, set | frozenset):
        return sorted(value)
    if isinstance(value, datetime):
        return value.isoformat()
    return str(value)


def digest(*parts: object) -> str:
    text = json.dumps([FORMAT_VERSION, __version__, *parts], sort_keys=True, separators=(",", ":"), default=_plain)
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def flatten_key(
    project_id: object, sample: list, form_fields: list, form_choices: list, labels: object, questions: object
) -> str:
    """
    Everything flattening reads: the submissions of the sample (id, last update and form version of each, in order),
    the form snapshot, the choice labels of every form version and the question labels.
    """
    return digest("flatten", str(project_id), sample, form_fields, form_choices, labels, questions)


def clean_key(flatten: str, clean_version_id: object, reference: list[tuple[str, object]]) -> str:
    """The flattened sample, the saved script 1 (versions are immutable) and the reference tables (name, update)."""
    return digest("clean", flatten, str(clean_version_id), reference)


@dataclass(frozen=True)
class Entry:
    path: Path
    step: dict  # what the step recorded: rows, log, previews, table list and, for flattening, the columns

    def table(self, name: str) -> Path:
        return self.path / TABLES_DIR / f"{name}.parquet"


class SampleCache:
    def __init__(self, directory: Path, limit_bytes: int):
        self.directory = directory
        self.limit_bytes = limit_bytes

    @property
    def enabled(self) -> bool:
        return self.limit_bytes > 0

    def get(self, key: str) -> Entry | None:
        """The entry for ``key``, marked as used now, or None. An entry that cannot be read is removed."""
        if not self.enabled:
            return None
        path = self.directory / key
        try:
            step = json.loads((path / STEP_FILE).read_text(encoding="utf-8"))
            os.utime(path)
        except FileNotFoundError:
            return None
        except (OSError, ValueError):
            shutil.rmtree(path, ignore_errors=True)
            return None
        return Entry(path, step)

    def put(self, key: str, step: dict, tables: Mapping[str, Path], protect: Collection[str] = ()) -> Entry | None:
        """
        Keeps copies of ``tables`` and the ``step`` record under ``key``, then removes the least recently used entries
        until the total fits the limit, never ``key`` or those in ``protect`` (a running test's other inputs). Returns
        None when the entry alone exceeds the limit or could not be written; a test never fails because of the cache.
        """
        if not self.enabled:
            return None
        temp = self.directory / f"{TEMP_PREFIX}{uuid.uuid4().hex}"
        try:
            (temp / TABLES_DIR).mkdir(parents=True)
            for name, source in tables.items():
                shutil.copyfile(source, temp / TABLES_DIR / f"{name}.parquet")
            (temp / STEP_FILE).write_text(json.dumps(step), encoding="utf-8")
            if _size(temp) > self.limit_bytes:
                shutil.rmtree(temp, ignore_errors=True)
                return None
            target = self.directory / key
            shutil.rmtree(target, ignore_errors=True)
            temp.rename(target)
        except OSError:
            logger.warning("Could not keep a test sample for reuse.", exc_info=True)
            shutil.rmtree(temp, ignore_errors=True)
            return None
        self._evict({key, *protect})
        return Entry(target, step)

    def _evict(self, keep: Collection[str]) -> None:
        entries = [p for p in self._entries() if p.is_dir()]
        sizes = {p: _size(p) for p in entries}
        total = sum(sizes.values())
        for path in sorted(entries, key=lambda p: p.stat().st_mtime):
            if total <= self.limit_bytes:
                break
            if path.name in keep:
                continue
            shutil.rmtree(path, ignore_errors=True)
            total -= sizes[path]

    def sweep(self, now: float | None = None) -> int:
        """Removes entries unused for 24 hours; returns how many."""
        cutoff = (time.time() if now is None else now) - UNUSED_HOURS * 3600
        removed = 0
        for path in self._entries():
            try:
                if path.stat().st_mtime < cutoff:
                    shutil.rmtree(path, ignore_errors=True)
                    removed += 1
            except OSError:
                continue
        return removed

    def clean_leftovers(self) -> None:
        """Removes the half-built entries a worker stopped in the middle of."""
        if self.directory.is_dir():
            for path in self.directory.glob(f"{TEMP_PREFIX}*"):
                shutil.rmtree(path, ignore_errors=True)

    def _entries(self) -> list[Path]:
        if not self.directory.is_dir():
            return []
        return [p for p in self.directory.iterdir() if not p.name.startswith(TEMP_PREFIX)]


def _size(path: Path) -> int:
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file())
