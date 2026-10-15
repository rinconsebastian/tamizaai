"""Script tests: a draft script run on a sample of the project's submissions, with previews and nothing published.

A test flattens the sample, runs the saved script 1 first when the draft is script 2 or an indicator, then runs the
draft with the same contract, ``ctx``, validation and limits as a run, or only up to a ``# %%`` marker. Each step is
recorded in ``tamiza.script_tests.steps`` as it ends, with previews of the tables it produced and of what the draft
passed to ``ctx.show``. Flattening and the saved script 1 are reused from an earlier test with the same inputs
(``sample_cache``). The worker claims queued tests before queued runs.
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

from tamiza import pipeline, preview, sample_cache
from tamiza.flatten import Submission, flatten
from tamiza.labels import load as load_labels
from tamiza.launcher import Limits, OutputTable, ScriptOutcome, ShownValue, run_script
from tamiza.sample_cache import SampleCache
from tamiza.settings import Settings

logger = logging.getLogger("tamiza.sample_runs")

INTERRUPTED = "test.interrupted"
LIFETIME_HOURS = 24
REUSED_NOTE = "Reused from an earlier test."


@dataclass(frozen=True)
class ClaimedTest:
    id: UUID
    project_id: UUID
    kind: str
    name: str | None  # the indicator a draft is tested as; None for script 1 and script 2
    code: str
    sample_size: int
    clean_version_id: UUID | None  # the saved script 1 run before an output or indicator draft
    stop_line: int | None = None  # the ``# %%`` marker the draft is run through; None for a whole test


@dataclass
class TestStep:
    step: str
    status: str = "succeeded"  # succeeded, failed or, for a draft run through a cell, stopped
    started_at: datetime | None = None
    finished_at: datetime | None = None
    input_rows: int | None = None
    output_rows: int | None = None
    tables: list[dict] = field(default_factory=list)
    log: str = ""
    error: str | None = None
    reused: bool = False
    stopped_at_line: int | None = None
    shown: list[dict] | None = None  # what the draft passed to ctx.show; None for the other steps
    dashboard: dict | None = None  # an indicator draft's table as the indicators dashboard will offer it

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
            "reused": self.reused,
            "stoppedAtLine": self.stopped_at_line,
            "shown": self.shown,
            "dashboard": self.dashboard,
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


def cache_for(settings: Settings) -> SampleCache:
    return SampleCache(settings.run_work_dir / sample_cache.CACHE_DIR, settings.test_cache_mb * 1024 * 1024)


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
            RETURNING t.id, t.project_id, t.kind, t.name, t.code, t.sample_size, t.clean_version_id, t.stop_line
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
    cache = cache_for(settings)
    references: dict[str, Path] = {}
    reference_state: list[tuple[str, str]] = []  # (name, last update) of each reference table, for the clean key

    raw: list[OutputTable] | None = None
    flatten_key = ""

    def do_flatten(step: TestStep) -> None:
        nonlocal raw, flatten_key
        tables, reference_log = pipeline.load_reference(settings, test.project_id, work_dir)
        references.update({t.name: t.path for t in tables})
        reference_state.extend((t.name, t.updated_at.isoformat()) for t in tables)
        rows = _sample_rows(settings.database_url, test.project_id, test.sample_size)
        labels = load_labels(connection, test.project_id, project.form_fields, project.form_choices)
        project.context["label_codes"] = labels.label_codes()
        flatten_key = sample_cache.flatten_key(
            test.project_id, rows, project.form_fields, project.form_choices, labels, project.question_labels
        )
        if (entry := cache.get(flatten_key)) is not None:
            raw = _reuse(step, entry)
            project.context["columns"] = entry.step["columns"]
            return
        sample = _sample(settings.database_url, rows)
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
        cache.put(flatten_key, _record(step, raw, columns=project.context["columns"]), {t.name: t.path for t in raw})

    _run_step(connection, test, steps, "flatten", do_flatten)
    if raw is None:
        return

    inputs = raw
    clean_code = _clean_code(connection, test) if test.kind != "clean" else None
    if clean_code is not None:
        cleaned: list[OutputTable] | None = None

        def do_saved_clean(step: TestStep) -> None:
            nonlocal cleaned
            key = sample_cache.clean_key(flatten_key, test.clean_version_id, reference_state)
            if (entry := cache.get(key)) is not None:
                cleaned = _reuse(step, entry)
                return
            cleaned = _script(step, work_dir, "clean", clean_code, raw, project.context, limits, references)
            if cleaned is not None:
                cache.put(key, _record(step, cleaned), {t.name: t.path for t in cleaned}, protect=[flatten_key])

        _run_step(connection, test, steps, "clean", do_saved_clean)
        if cleaned is None:
            return
        inputs = cleaned

    def do_draft(step: TestStep) -> None:
        _script(
            step,
            work_dir,
            test.kind,
            test.code,
            inputs,
            project.context,
            limits,
            references,
            test.name,
            draft=True,
            draft_stop_line=test.stop_line,
        )

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


def _script(
    step, work_dir, kind, code, inputs, context, limits, references, name=None, draft=False, draft_stop_line=None
) -> list[OutputTable] | None:
    """
    Runs one script. The ``draft`` records what it passes to ``ctx.show`` and can stop at ``draft_stop_line``; the saved
    script 1 that runs before it does neither. Returns the tables, or None when the step failed.
    """
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
        show=draft,
        stop_line=draft_stop_line if draft else None,
    )
    step.input_rows = sum(t.rows for t in inputs)
    step.log = outcome.log
    if draft:
        step.shown = [_shown(value) for value in outcome.shown]
    if not outcome.succeeded:
        step.status = "failed"
        step.error = outcome.error
        return None
    if outcome.stopped_at is not None:
        step.status = "stopped"
        step.stopped_at_line = outcome.stopped_at
        return []
    _set_outputs(step, outcome.tables)
    if draft and kind == "indicator":
        step.dashboard = _dashboard(step, outcome)
    return outcome.tables


def _shown(value: ShownValue) -> dict:
    table = preview.table_preview(value.name, value.rows, value.path) if value.path is not None else None
    return {"name": value.name, "line": value.line, "rows": value.rows, "table": table, "text": value.text}


def _dashboard(step: TestStep, outcome: ScriptOutcome) -> dict | None:
    """The indicator table as the dashboard will offer it; a table the facts cannot be read from is only left out."""
    try:
        return preview.dashboard_view(outcome.tables[0].path)
    except Exception:  # the view is an extra: it must not fail a test whose script worked
        logger.exception("Could not read the dashboard view of an indicator table.")
        step.log = (step.log.rstrip() + "\nThe dashboard view of this table could not be built.").strip()
        return None


def _set_outputs(step: TestStep, tables: list[OutputTable]) -> None:
    step.output_rows = sum(t.rows for t in tables)
    step.tables = preview.step_tables([(t.name, t.rows, t.path, t.column_labels) for t in tables])


def _record(step: TestStep, tables: list[OutputTable], columns: list[dict] | None = None) -> dict:
    """What a step leaves in the cache: its numbers, log and previews, and what its tables are."""
    record = {
        "inputRows": step.input_rows,
        "outputRows": step.output_rows,
        "log": step.log,
        "tables": step.tables,
        "tableInfo": [{"name": t.name, "rows": t.rows, "columnLabels": t.column_labels} for t in tables],
    }
    if columns is not None:
        record["columns"] = columns
    return record


def _reuse(step: TestStep, entry: sample_cache.Entry) -> list[OutputTable]:
    """Fills ``step`` from a cache entry and returns the tables, read from the entry's files."""
    recorded = entry.step
    step.input_rows = recorded["inputRows"]
    step.output_rows = recorded["outputRows"]
    step.tables = recorded["tables"]
    step.log = f"{recorded['log']}\n{REUSED_NOTE}".strip()
    step.reused = True
    return [
        OutputTable(info["name"], info["rows"], entry.table(info["name"]), info["columnLabels"])
        for info in recorded["tableInfo"]
    ]


def _clean_code(connection, test: ClaimedTest) -> str | None:
    """The saved script 1 to run first; None when there is none or it was removed."""
    if test.clean_version_id is None:
        return None
    row = connection.execute(
        "SELECT code FROM tamiza.script_versions WHERE id = %s", (test.clean_version_id,)
    ).fetchone()
    return row[0] if row else None


def _sample_rows(database_url: str, project_id: UUID, size: int) -> list[tuple[int, str, str | None]]:
    """
    Up to ``size`` non-deleted submissions in hash order: spread across the project, and the same ones while the
    submissions do not change. Only what identifies each version of a submission is read: its id, last update and form
    version, which is also what a cached sample is keyed by.
    """
    with psycopg.connect(database_url) as connection:
        rows = connection.execute(
            """
            SELECT id, updated_at, form_version FROM tamiza.raw_submissions WHERE project_id = %s AND NOT deleted
            ORDER BY md5(kobo_uuid), kobo_id LIMIT %s
            """,
            (project_id, size),
        ).fetchall()
    return [(submission_id, updated_at.isoformat(), form_version) for submission_id, updated_at, form_version in rows]


def _sample(database_url: str, rows: list[tuple[int, str, str | None]]) -> list[Submission]:
    """The answers of the sampled submissions in the sample's order, read once: flattening passes over them twice."""
    ids = [submission_id for submission_id, _, _ in rows]
    with psycopg.connect(database_url) as connection:
        fetched = {
            submission_id: (data, form_version)
            for submission_id, data, form_version in connection.execute(
                "SELECT id, data, form_version FROM tamiza.raw_submissions WHERE id = ANY(%s)", (ids,)
            )
        }
    return [fetched[submission_id] for submission_id in ids if submission_id in fetched]
