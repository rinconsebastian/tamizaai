"""Claims queued tests and runs, and executes the chain: flatten → clean → output and indicators.

Each step records itself in ``tamiza.run_steps`` as it ends. A step whose script is absent falls back to the default
behavior (clean = flattened data; output and indicators are removed). Scripts 2 and 3 depend only on script 1.
"""

import logging
import shutil
import time
import traceback
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from uuid import UUID

import psycopg
from psycopg.types.json import Jsonb

from tamiza import publish, reference
from tamiza.flatten import flatten
from tamiza.labels import load as load_labels
from tamiza.launcher import MAX_LOG_BYTES, Limits, OutputTable, run_script
from tamiza.reference import ReferenceTable
from tamiza.settings import Settings

logger = logging.getLogger("tamiza.pipeline")

STEPS = ("flatten", "clean", "output", "indicators")
INTERRUPTED = "run.interrupted"


@dataclass(frozen=True)
class ClaimedRun:
    id: UUID
    project_id: UUID
    versions: dict[str, UUID | None]  # script kind → version id, resolved when the run was claimed


@dataclass
class StepRecord:
    step: str
    status: str = "skipped"
    started_at: datetime | None = None
    finished_at: datetime | None = None
    input_rows: int | None = None
    output_rows: int | None = None
    tables: list[dict] = field(default_factory=list)
    log: str = ""
    error: str | None = None


def fail_interrupted(connection: psycopg.Connection) -> int:
    """Marks runs left ``running`` by a previous worker as failed. Assumes a single worker."""
    cursor = connection.execute(
        """
        UPDATE tamiza.runs SET status = 'failed', finished_at = now(), error_code = %s,
               error = 'The run was interrupted because the analytics worker stopped.'
        WHERE status = 'running'
        """,
        (INTERRUPTED,),
    )
    return cursor.rowcount


def claim(connection: psycopg.Connection) -> ClaimedRun | None:
    """Starts the oldest queued run, recording the current version of each script for it."""
    latest = (
        "(SELECT v.id FROM tamiza.script_versions v"
        " WHERE v.project_id = r.project_id AND v.kind = '{}' ORDER BY v.number DESC LIMIT 1)"
    )
    with connection.transaction():
        row = connection.execute(
            f"""
            WITH next AS (
                SELECT id FROM tamiza.runs WHERE status = 'queued' ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE tamiza.runs r
            SET status = 'running', started_at = now(),
                clean_version_id = {latest.format("clean")},
                output_version_id = {latest.format("output")},
                indicators_version_id = {latest.format("indicators")}
            FROM next WHERE r.id = next.id
            RETURNING r.id, r.project_id, r.clean_version_id, r.output_version_id, r.indicators_version_id
            """
        ).fetchone()
        if row is not None:
            # A scheduled project is next due one interval after this run started.
            connection.execute(
                """
                UPDATE tamiza.projects SET next_run_at = now() + make_interval(hours => run_interval_hours)
                WHERE id = %s AND run_interval_hours IS NOT NULL
                """,
                (row[1],),
            )
    if row is None:
        return None
    return ClaimedRun(row[0], row[1], {"clean": row[2], "output": row[3], "indicators": row[4]})


class Processor:
    """
    Called once per worker cycle with the worker's connection; returns True when it executed a test or a run. Queued
    script tests go before queued runs: they are short and an analyst is waiting for them.
    """

    def __init__(self, settings: Settings):
        self.settings = settings
        self._started = False

    def __call__(self, connection: psycopg.Connection) -> bool:
        from tamiza import sample_runs  # imports this module

        if not self._started:
            interrupted = fail_interrupted(connection)
            if interrupted:
                logger.warning("Marked %d runs interrupted by the last shutdown as failed.", interrupted)
            interrupted = sample_runs.fail_interrupted(connection)
            if interrupted:
                logger.warning("Marked %d tests interrupted by the last shutdown as failed.", interrupted)
            self._started = True
        sample_runs.delete_expired(connection)
        test = sample_runs.claim(connection)
        if test is not None:
            sample_runs.execute(connection, test, self.settings)
            return True
        claimed = claim(connection)
        if claimed is None:
            return False
        execute(connection, claimed, self.settings)
        return True


def execute(connection: psycopg.Connection, run: ClaimedRun, settings: Settings) -> None:
    logger.info("Run %s of project %s started.", run.id, run.project_id)
    work_dir = settings.run_work_dir / str(run.id)
    shutil.rmtree(work_dir, ignore_errors=True)
    work_dir.mkdir(parents=True)
    records = {step: StepRecord(step) for step in STEPS}
    try:
        _execute_steps(connection, run, settings, work_dir, records)
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)
    failed = next((records[step] for step in STEPS if records[step].status == "failed"), None)
    connection.execute(
        """
        UPDATE tamiza.runs SET status = %s, finished_at = now(), error_code = %s, error = %s WHERE id = %s
        """,
        (
            "failed" if failed else "succeeded",
            f"step.{failed.step}" if failed else None,
            failed.error.splitlines()[0] if failed and failed.error else None,
            run.id,
        ),
    )
    logger.info("Run %s of project %s %s.", run.id, run.project_id, "failed" if failed else "succeeded")


@dataclass(frozen=True)
class ProjectContext:
    form_fields: list[dict]
    form_choices: list[dict]
    context: dict  # what context.json gives the runner: project name and form dictionary


def project_context(connection: psycopg.Connection, project_id: UUID) -> ProjectContext:
    """The form snapshot and the ``ctx`` data every script of the project receives, in runs and tests alike."""
    project_name, form_fields, form_choices = connection.execute(
        "SELECT name, form_fields, form_choices FROM tamiza.projects WHERE id = %s", (project_id,)
    ).fetchone()
    context = {
        "project_name": project_name,
        "questions": [
            {
                "name": f.get("name"),
                "path": f.get("xpath"),
                "type": f.get("type"),
                "label": f.get("label"),
                "list_name": f.get("listName"),
            }
            for f in form_fields
        ],
        "choices": [
            {"list_name": c.get("listName"), "name": c.get("name"), "label": c.get("label")} for c in form_choices
        ],
    }
    return ProjectContext(form_fields, form_choices, context)


def load_reference(settings: Settings, project_id: UUID, work_dir: Path) -> tuple[list[ReferenceTable], str]:
    """Reads the project's reference tables once for a run or test; returns them and a line for the step log."""
    tables = reference.load(settings.database_url, project_id, work_dir / "reference")
    if not tables:
        return tables, "No reference tables."
    listed = ", ".join(f"{t.name} ({t.rows:,} rows)" for t in tables)
    return tables, f"Reference tables: {listed}."


def _execute_steps(connection, run: ClaimedRun, settings: Settings, work_dir: Path, records: dict[str, StepRecord]):
    project = project_context(connection, run.project_id)
    form_fields, context = project.form_fields, project.context
    codes = _codes(connection, run)
    limits = Limits(settings.run_timeout_seconds, settings.run_memory_mb)
    references: dict[str, Path] = {}

    # Reference tables, read once for the whole run, then flatten.
    raw: list[OutputTable] | None = None

    def do_flatten(record: StepRecord) -> None:
        nonlocal raw
        tables, reference_log = load_reference(settings, run.project_id, work_dir)
        references.update({t.name: t.path for t in tables})
        connection.execute(
            "UPDATE tamiza.runs SET reference_tables = %s WHERE id = %s",
            (Jsonb([t.summary() for t in tables]), run.id),
        )
        labels = load_labels(connection, run.project_id, form_fields, project.form_choices)
        # Scripts resolve a label back to its code with ctx.form.name, across the versions the run translated with.
        context["label_codes"] = labels.label_codes()
        result = flatten(
            form_fields, lambda: _submissions(settings.database_url, run.project_id), work_dir / "raw", labels=labels
        )
        raw = [OutputTable(t.name, t.rows, t.path) for t in result.tables]
        publish.publish(connection, run.project_id, "raw", [publish.Table(t.name, t.path) for t in raw])
        record.input_rows = result.input_rows
        record.log = "\n".join([reference_log, *result.log])
        _set_outputs(record, raw)

    _run_step(connection, run, records["flatten"], do_flatten)
    if raw is None:
        _record_skipped(connection, run, records, ("clean", "output", "indicators"), "Not run: flattening failed.")
        return

    # Script 1, or the flattened tables as the clean data.
    cleaned: list[OutputTable] | None = None

    def do_clean(record: StepRecord) -> None:
        nonlocal cleaned
        if codes["clean"] is None:
            cleaned = raw
            record.log = "No clean script: the flattened tables are published as the clean data."
        else:
            cleaned = _script(record, work_dir, "clean", codes["clean"], raw, context, limits, references)
            if cleaned is None:
                return
        publish.publish(connection, run.project_id, "clean", [publish.Table(t.name, t.path) for t in cleaned])
        record.input_rows = sum(t.rows for t in raw)
        _set_outputs(record, cleaned)

    _run_step(connection, run, records["clean"], do_clean)
    if cleaned is None:
        _record_skipped(connection, run, records, ("output", "indicators"), "Not run: the clean step failed.")
        return

    # Scripts 2 and 3 each depend only on the clean step.
    for kind in ("output", "indicators"):

        def do_script(record: StepRecord, kind: str = kind) -> None:
            if codes[kind] is None:
                publish.drop(connection, run.project_id, kind)
                record.status = "skipped"
                record.log = f"No {kind} script: nothing is published for this step."
                return
            outputs = _script(record, work_dir, kind, codes[kind], cleaned, context, limits, references)
            if outputs is None:
                return
            publish.publish(connection, run.project_id, kind, [publish.Table(t.name, t.path) for t in outputs])
            record.input_rows = sum(t.rows for t in cleaned)
            _set_outputs(record, outputs)

        _run_step(connection, run, records[kind], do_script)


def _run_step(connection, run: ClaimedRun, record: StepRecord, action: Callable[[StepRecord], None]) -> None:
    record.started_at = datetime.now(UTC)
    started = time.monotonic()
    record.status = "succeeded"
    try:
        action(record)
    except Exception as error:  # the worker's own failure (database, files): recorded, never fatal to the loop
        logger.exception("Step %s of run %s failed.", record.step, run.id)
        record.status = "failed"
        record.error = f"{type(error).__name__}: {error}"
        record.log = (record.log + "\n" + traceback.format_exc()).strip()
    record.finished_at = datetime.now(UTC)
    if record.status != "skipped":
        record.log = (record.log.rstrip() + f"\nDuration: {time.monotonic() - started:.1f} s").strip()
    _save(connection, run, record)


def _script(record, work_dir, kind, code, inputs, context, limits, references) -> list[OutputTable] | None:
    outcome = run_script(
        work_dir / "steps",
        kind,
        code,
        {t.name: t.path for t in inputs},
        context,
        work_dir / kind,
        limits,
        references,
    )
    record.log = outcome.log
    if not outcome.succeeded:
        record.status = "failed"
        record.error = outcome.error
        return None
    return outcome.tables


def _set_outputs(record: StepRecord, tables: list[OutputTable]) -> None:
    record.tables = [{"name": t.name, "rows": t.rows} for t in tables]
    record.output_rows = sum(t.rows for t in tables)


def _record_skipped(connection, run, records, steps, reason) -> None:
    for step in steps:
        records[step].log = reason
        _save(connection, run, records[step])


def capped_log(log: str) -> str:
    """The last 1 MB of a step's log, noting the cut."""
    if len(log.encode("utf-8")) <= MAX_LOG_BYTES:
        return log
    return "[Log truncated: only the last 1 MB is kept.]\n" + log.encode("utf-8")[-MAX_LOG_BYTES:].decode(
        "utf-8", "ignore"
    )


def _save(connection, run: ClaimedRun, record: StepRecord) -> None:
    log = capped_log(record.log)
    connection.execute(
        """
        INSERT INTO tamiza.run_steps
            (run_id, step, status, started_at, finished_at, input_rows, output_rows, tables, log, error)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
        ON CONFLICT (run_id, step) DO UPDATE SET status = excluded.status, started_at = excluded.started_at,
            finished_at = excluded.finished_at, input_rows = excluded.input_rows, output_rows = excluded.output_rows,
            tables = excluded.tables, log = excluded.log, error = excluded.error
        """,
        (
            run.id,
            record.step,
            record.status,
            record.started_at,
            record.finished_at,
            record.input_rows,
            record.output_rows,
            Jsonb(record.tables),
            log,
            record.error,
        ),
    )


def _codes(connection, run: ClaimedRun) -> dict[str, str | None]:
    """The code of each script version the run uses; None when there is no script or it was removed."""
    codes: dict[str, str | None] = {}
    for kind, version_id in run.versions.items():
        row = None
        if version_id is not None:
            row = connection.execute("SELECT code FROM tamiza.script_versions WHERE id = %s", (version_id,)).fetchone()
        codes[kind] = row[0] if row else None
    return codes


def _submissions(database_url: str, project_id: UUID):
    """
    Streams the project's non-deleted submissions with a server-side cursor on its own connection, each with the
    form version the API recorded for it (``__version__``, null when it has none).
    """
    with psycopg.connect(database_url) as connection, connection.cursor(name="submissions") as cursor:
        cursor.itersize = 2_000
        cursor.execute(
            "SELECT data, form_version FROM tamiza.raw_submissions"
            " WHERE project_id = %s AND NOT deleted ORDER BY kobo_id",
            (project_id,),
        )
        yield from cursor
