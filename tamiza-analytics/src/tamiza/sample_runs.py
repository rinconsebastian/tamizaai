"""Script tests: a draft script run on a sample of the project's submissions, with previews and nothing published.

A test flattens the sample, runs the saved script 1 first when the draft is script 2 or an indicator, then runs the
draft with the same contract, ``ctx``, validation and limits as a run. Each step is recorded in
``tamiza.script_tests.steps`` as it ends, with previews of the tables it produced. The worker claims queued tests
before queued runs.
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

from tamiza import pipeline, preview
from tamiza.flatten import Submission, flatten
from tamiza.labels import load as load_labels
from tamiza.launcher import Limits, OutputTable, run_script
from tamiza.settings import Settings

logger = logging.getLogger("tamiza.sample_runs")

INTERRUPTED = "test.interrupted"
LIFETIME_HOURS = 24


@dataclass(frozen=True)
class ClaimedTest:
    id: UUID
    project_id: UUID
    kind: str
    name: str | None  # the indicator a draft is tested as; None for script 1 and script 2
    code: str
    sample_size: int
    clean_version_id: UUID | None  # the saved script 1 run before an output or indicator draft


@dataclass
class TestStep:
    step: str
    status: str = "succeeded"
    started_at: datetime | None = None
    finished_at: datetime | None = None
    input_rows: int | None = None
    output_rows: int | None = None
    tables: list[dict] = field(default_factory=list)
    log: str = ""
    error: str | None = None

    def as_json(self) -> dict:
        return {
            "step": self.step,
            "status": self.status,
            "startedAt": self.started_at.isoformat() if self.started_at else None,
            "finishedAt": self.finished_at.isoformat() if self.finished_at else None,
            "inputRows": self.input_rows,
            "outputRows": self.output_rows,
            "tables": self.tables,
            "log": pipeline.capped_log(self.log),
            "error": self.error,
        }


def fail_interrupted(connection: psycopg.Connection) -> int:
    """Marks tests left ``running`` by a previous worker as failed. Assumes a single worker."""
    cursor = connection.execute(
        """
        UPDATE tamiza.script_tests SET status = 'failed', finished_at = now(), error_code = %s,
               error = 'The test was interrupted because the analytics worker stopped.'
        WHERE status = 'running'
        """,
        (INTERRUPTED,),
    )
    return cursor.rowcount


def delete_expired(connection: psycopg.Connection) -> int:
    cursor = connection.execute(
        "DELETE FROM tamiza.script_tests WHERE finished_at < now() - make_interval(hours => %s)", (LIFETIME_HOURS,)
    )
    return cursor.rowcount


def claim(connection: psycopg.Connection) -> ClaimedTest | None:
    """Starts the oldest queued test; a test of script 2 or an indicator records the current version of script 1."""
    with connection.transaction():
        row = connection.execute(
            """
            WITH next AS (
                SELECT id FROM tamiza.script_tests WHERE status = 'queued'
                ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE tamiza.script_tests t
            SET status = 'running', started_at = now(),
                clean_version_id = CASE WHEN t.kind = 'clean' THEN NULL ELSE (
                    SELECT v.id FROM tamiza.script_versions v
                    WHERE v.project_id = t.project_id AND v.kind = 'clean' ORDER BY v.number DESC LIMIT 1) END
            FROM next WHERE t.id = next.id
            RETURNING t.id, t.project_id, t.kind, t.name, t.code, t.sample_size, t.clean_version_id
            """
        ).fetchone()
    return None if row is None else ClaimedTest(*row)


def execute(connection: psycopg.Connection, test: ClaimedTest, settings: Settings) -> None:
    logger.info("Test %s of project %s started.", test.id, test.project_id)
    work_dir = settings.run_work_dir / f"test-{test.id}"
    shutil.rmtree(work_dir, ignore_errors=True)
    work_dir.mkdir(parents=True)
    steps: list[TestStep] = []
    try:
        _execute_steps(connection, test, settings, work_dir, steps)
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)
    failed = next((step for step in steps if step.status == "failed"), None)
    connection.execute(
        "UPDATE tamiza.script_tests SET status = %s, finished_at = now(), error_code = %s, error = %s WHERE id = %s",
        (
            "failed" if failed else "succeeded",
            f"step.{failed.step}" if failed else None,
            failed.error.splitlines()[0] if failed and failed.error else None,
            test.id,
        ),
    )
    logger.info("Test %s of project %s %s.", test.id, test.project_id, "failed" if failed else "succeeded")


def _execute_steps(connection, test: ClaimedTest, settings: Settings, work_dir: Path, steps: list[TestStep]) -> None:
    project = pipeline.project_context(connection, test.project_id)
    limits = Limits(settings.run_timeout_seconds, settings.run_memory_mb)
    references: dict[str, Path] = {}

    raw: list[OutputTable] | None = None

    def do_flatten(step: TestStep) -> None:
        nonlocal raw
        tables, reference_log = pipeline.load_reference(settings, test.project_id, work_dir)
        references.update({t.name: t.path for t in tables})
        sample = _sample(settings.database_url, test.project_id, test.sample_size)
        labels = load_labels(connection, test.project_id, project.form_fields, project.form_choices)
        project.context["label_codes"] = labels.label_codes()
        result = flatten(
            project.form_fields,
            lambda: sample,
            work_dir / "raw",
            labels=labels,
            question_labels=project.question_labels,
        )
        project.context["columns"] = pipeline.column_context(result.tables)
        raw = [OutputTable(t.name, t.rows, t.path, t.column_labels) for t in result.tables]
        step.input_rows = result.input_rows
        step.log = "\n".join([f"Sample: {len(sample):,} submissions.", reference_log, *result.log])
        _set_outputs(step, raw)

    _run_step(connection, test, steps, "flatten", do_flatten)
    if raw is None:
        return

    inputs = raw
    clean_code = _clean_code(connection, test) if test.kind != "clean" else None
    if clean_code is not None:
        cleaned: list[OutputTable] | None = None

        def do_saved_clean(step: TestStep) -> None:
            nonlocal cleaned
            cleaned = _script(step, work_dir, "clean", clean_code, raw, project.context, limits, references)

        _run_step(connection, test, steps, "clean", do_saved_clean)
        if cleaned is None:
            return
        inputs = cleaned

    def do_draft(step: TestStep) -> None:
        _script(step, work_dir, test.kind, test.code, inputs, project.context, limits, references, test.name)

    _run_step(connection, test, steps, test.kind, do_draft)


def _run_step(connection, test: ClaimedTest, steps: list[TestStep], name: str, action: Callable[[TestStep], None]):
    step = TestStep(name, started_at=datetime.now(UTC))
    started = time.monotonic()
    try:
        action(step)
    except Exception as error:  # the worker's own failure (database, files): recorded, never fatal to the loop
        logger.exception("Step %s of test %s failed.", name, test.id)
        step.status = "failed"
        step.error = f"{type(error).__name__}: {error}"
        step.log = (step.log + "\n" + traceback.format_exc()).strip()
    step.finished_at = datetime.now(UTC)
    step.log = (step.log.rstrip() + f"\nDuration: {time.monotonic() - started:.1f} s").strip()
    steps.append(step)
    connection.execute(
        "UPDATE tamiza.script_tests SET steps = %s WHERE id = %s", (Jsonb([s.as_json() for s in steps]), test.id)
    )


def _script(step, work_dir, kind, code, inputs, context, limits, references, name=None) -> list[OutputTable] | None:
    outcome = run_script(
        work_dir / "steps",
        kind,
        code,
        {t.name: t.path for t in inputs},
        context,
        work_dir / kind,
        limits,
        references,
        name,
    )
    step.input_rows = sum(t.rows for t in inputs)
    step.log = outcome.log
    if not outcome.succeeded:
        step.status = "failed"
        step.error = outcome.error
        return None
    _set_outputs(step, outcome.tables)
    return outcome.tables


def _set_outputs(step: TestStep, tables: list[OutputTable]) -> None:
    step.output_rows = sum(t.rows for t in tables)
    step.tables = preview.step_tables([(t.name, t.rows, t.path, t.column_labels) for t in tables])


def _clean_code(connection, test: ClaimedTest) -> str | None:
    """The saved script 1 to run first; None when there is none or it was removed."""
    if test.clean_version_id is None:
        return None
    row = connection.execute(
        "SELECT code FROM tamiza.script_versions WHERE id = %s", (test.clean_version_id,)
    ).fetchone()
    return row[0] if row else None


def _sample(database_url: str, project_id: UUID, size: int) -> list[Submission]:
    """
    Up to ``size`` non-deleted submissions, each with its form version, in hash order: spread across the project, and
    the same ones while the submissions do not change. Read once, since flattening passes over them twice.
    """
    with psycopg.connect(database_url) as connection:
        rows = connection.execute(
            """
            SELECT data, form_version FROM tamiza.raw_submissions WHERE project_id = %s AND NOT deleted
            ORDER BY md5(kobo_uuid), kobo_id LIMIT %s
            """,
            (project_id, size),
        ).fetchall()
    return [(data, form_version) for data, form_version in rows]
