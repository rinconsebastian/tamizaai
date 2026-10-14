"""Runs a user script in a child process with time and memory limits, and collects its outputs.

The child gets a clean environment (no database settings or credentials), its own working directory and process
group, and is killed when it runs too long or uses too much memory. This contains accidents; it is not a security
sandbox: the child runs as the same OS user as the worker.
"""

import contextlib
import json
import os
import shutil
import signal
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

from tamiza import interchange, names

POLL_SECONDS = 0.25
MAX_LOG_BYTES = 1024 * 1024


@dataclass(frozen=True)
class Limits:
    timeout_seconds: float = 600.0
    memory_mb: int = 1024


@dataclass
class OutputTable:
    name: str
    rows: int
    path: Path
    column_labels: dict[str, str] = field(default_factory=dict)  # question labels of flattened columns, by name
    column_labels: dict[str, str] = field(default_factory=dict)  # question labels of flattened columns, by name


@dataclass
class ScriptOutcome:
    succeeded: bool
    log: str
    error: str | None = None
    error_code: str | None = None  # limit.time, limit.memory or None
    tables: list[OutputTable] = field(default_factory=list)


def run_script(
    work_dir: Path,
    kind: str,
    code: str,
    inputs: dict[str, Path],
    context: dict,
    out_dir: Path,
    limits: Limits,
    reference: dict[str, Path] | None = None,
) -> ScriptOutcome:
    """
    Prepares ``work_dir/<kind>``, runs the script there, moves its Parquet outputs into ``out_dir`` and removes the
    step directory, whatever happened. ``reference`` maps the project's reference tables to their Parquet files.
    """
    step_dir = work_dir / kind
    shutil.rmtree(step_dir, ignore_errors=True)
    (step_dir / "input").mkdir(parents=True)
    (step_dir / "reference").mkdir()
    (step_dir / "tmp").mkdir()
    try:
        # Copies, not links: a script writing to its files must not change what later steps receive.
        for name, path in inputs.items():
            shutil.copyfile(path, step_dir / "input" / f"{name}.parquet")
        for name, path in (reference or {}).items():
            shutil.copyfile(path, step_dir / "reference" / f"{name}.parquet")
        (step_dir / "script.py").write_text(code, encoding="utf-8")
        (step_dir / "context.json").write_text(json.dumps(context), encoding="utf-8")
        return _collect(step_dir, out_dir, *launch(step_dir, kind, limits))
    finally:
        shutil.rmtree(step_dir, ignore_errors=True)


def launch(step_dir: Path, kind: str, limits: Limits) -> tuple[dict | None, str, str | None]:
    """Runs the child and returns its result (or None), its log and the limit it hit (``time``/``memory``) if any."""
    log_path = step_dir / "log.txt"
    with log_path.open("wb") as log_file:
        process = subprocess.Popen(
            [sys.executable, "-I", "-u", "-m", "tamiza.runner", str(step_dir), kind],
            cwd=step_dir,
            env=child_environment(step_dir),
            stdin=subprocess.DEVNULL,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            start_new_session=True,  # its own process group, so a kill reaches anything it started
        )
        limit = _watch(process, limits)

    result = None
    result_path = step_dir / "result.json"
    if limit is None and result_path.exists():
        try:
            result = json.loads(result_path.read_text(encoding="utf-8"))
        except ValueError:
            result = None
    if limit is None and process.returncode == -signal.SIGKILL:
        # Killed by someone else, almost always the kernel's out-of-memory killer under the container limit.
        limit = "memory"
    return result, _read_log(log_path), limit


def child_environment(step_dir: Path) -> dict[str, str]:
    """Only what Python needs: no PG* or TAMIZA_* variables reach the script."""
    return {
        "PATH": f"{Path(sys.executable).parent}:/usr/local/bin:/usr/bin:/bin",
        "HOME": str(step_dir),
        "TMPDIR": str(step_dir / "tmp"),
        "LANG": "C.UTF-8",
        "TZ": "UTC",
        "OMP_NUM_THREADS": "2",
        "OPENBLAS_NUM_THREADS": "2",
        "POLARS_MAX_THREADS": "2",
    }


def _watch(process: subprocess.Popen, limits: Limits) -> str | None:
    started = time.monotonic()
    limit_bytes = limits.memory_mb * 1024 * 1024
    while process.poll() is None:
        if time.monotonic() - started > limits.timeout_seconds:
            _kill(process)
            return "time"
        if _group_rss_bytes(process.pid) > limit_bytes:
            _kill(process)
            return "memory"
        time.sleep(POLL_SECONDS)
    return None


def _kill(process: subprocess.Popen) -> None:
    with contextlib.suppress(ProcessLookupError):
        os.killpg(process.pid, signal.SIGKILL)
    process.wait()


def _group_rss_bytes(session_id: int) -> int:
    """Resident memory of every process in the child's session (the child and anything it started)."""
    total = 0
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            stat = (entry / "stat").read_text()
            # Fields after the command name, which is in parentheses and may contain spaces; session is the 4th.
            fields = stat[stat.rindex(")") + 2 :].split()
            if int(fields[3]) != session_id:
                continue
            for line in (entry / "status").read_text().splitlines():
                if line.startswith("VmRSS:"):
                    total += int(line.split()[1]) * 1024
                    break
        except (OSError, ValueError, IndexError):
            continue  # the process ended while being read
    return total


def _read_log(path: Path) -> str:
    data = path.read_bytes() if path.exists() else b""
    if len(data) > MAX_LOG_BYTES:
        data = b"[Log truncated: only the last 1 MB is kept.]\n" + data[-MAX_LOG_BYTES:]
    return data.decode("utf-8", errors="replace")


def _collect(step_dir: Path, out_dir: Path, result: dict | None, log: str, limit: str | None) -> ScriptOutcome:
    if limit == "time":
        return ScriptOutcome(False, log, "The script was stopped because it exceeded the time limit.", "limit.time")
    if limit == "memory":
        return ScriptOutcome(False, log, "The script was stopped because it exceeded the memory limit.", "limit.memory")
    if result is None:
        return ScriptOutcome(False, log, "The script process ended without a result.")
    if result.get("status") != "succeeded":
        error = result.get("error") or "The script failed."
        if result.get("traceback"):
            error = f"{error}\n\n{result['traceback']}"
        return ScriptOutcome(False, log, error)

    out_dir.mkdir(parents=True, exist_ok=True)
    tables = []
    for table in result.get("tables", []):
        name = table.get("name")
        # result.json sits where the script could overwrite it: never use a name as a path before validating it.
        if not names.is_valid_table_name(name):
            return ScriptOutcome(False, log, f"The script's result names an invalid table: {name!r}.")
        source = step_dir / "output" / f"{name}.parquet"
        if not source.is_file():
            return ScriptOutcome(False, log, f"The script's output table {name!r} is missing.")
        target = out_dir / f"{name}.parquet"
        shutil.move(source, target)
        tables.append(OutputTable(name, interchange.count_rows(target), target))
    return ScriptOutcome(True, log, tables=tables)
