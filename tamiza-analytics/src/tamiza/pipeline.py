"""Claims queued tests and runs, and executes the chain: flatten → clean → output, then the indicators one by one.

Each step records itself in ``tamiza.run_steps`` as it ends. A step whose script is absent falls back to the default
behavior (clean = flattened data; output is removed, and so is the indicator schema when the project has no
indicators). Script 2 and every indicator script depend only on script 1.
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
from tamiza.question_labels import QuestionLabels, question_labels
from tamiza.reference import ReferenceTable
from tamiza.settings import Settings

logger = logging.getLogger("tamiza.pipeline")

INTERRUPTED = "run.interrupted"
INDICATOR = "indicator"


@dataclass(frozen=True)
class IndicatorVersion:
    name: str
    version_id: UUID
    number: int


@dataclass(frozen=True)
class ClaimedRun:
    id: UUID
    project_id: UUID
    versions: dict[str, UUID | None]  # script 1 and 2 → version id, resolved when the run was claimed
    indicators: list[IndicatorVersion] = field(default_factory=list)  # current indicator versions, in name order


@dataclass
class StepRecord:
    step: str
    name: str = ""  # the indicator's name for an indicator step
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
    """Starts the oldest queued run, recording the current version of each script, and of each indicator, for it."""
    latest = (
        "(SELECT v.id FROM tamiza.script_versions v"
        " WHERE v.project_id = r.project_id AND v.kind = '{}' ORDER BY v.number DESC LIMIT 1)"
    )
    # The latest version of each indicator name, unless it marks the indicator as removed; a list in name order.
    indicators = """
        COALESCE((
            SELECT jsonb_agg(jsonb_build_object('name', l.name, 'versionId', l.id, 'number', l.number)
                             ORDER BY l.name COLLATE "C")
            FROM (
                SELECT DISTINCT ON (v.name) v.id, v.name, v.number, v.code FROM tamiza.script_versions v
                WHERE v.project_id = r.project_id AND v.kind = 'indicator' ORDER BY v.name, v.number DESC
            ) l WHERE l.code IS NOT NULL
        ), '[]'::jsonb)
    """
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
                indicator_versions = {indicators}
            FROM next WHERE r.id = next.id
            RETURNING r.id, r.project_id, r.clean_version_id, r.output_version_id, r.indicator_versions
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
    return ClaimedRun(
        row[0],
        row[1],
        {"clean": row[2], "output": row[3]},
        [IndicatorVersion(i["name"], UUID(i["versionId"]), i["number"]) for i in row[4]],
    )


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
    # In the order the steps run: the three fixed ones, then the indicators by name.
    records = {(step, ""): StepRecord(step) for step in ("flatten", "clean", "output")}
    records.update({(INDICATOR, i.name): StepRecord(INDICATOR, i.name) for i in run.indicators})
    try:
        _execute_steps(connection, run, settings, work_dir, records)
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)
    failed = next((record for record in records.values() if record.status == "failed"), None)
    connection.execute(
        """
        UPDATE tamiza.runs SET status = %s, finished_at = now(), error_code = %s, error = %s WHERE id = %s
        """,
        (
            "failed" if failed else "succeeded",
            f"step.{failed.step}" if failed else None,
            _run_error(failed),
            run.id,
        ),
    )
    logger.info("Run %s of project %s %s.", run.id, run.project_id, "failed" if failed else "succeeded")


def _run_error(failed: StepRecord | None) -> str | None:
    """The first line of the first failed step's error; for an indicator it names the indicator."""
    if failed is None or not failed.error:
        return None
    line = failed.error.splitlines()[0]
    return f"Indicator {failed.name}: {line}" if failed.step == INDICATOR else line


@dataclass(frozen=True)
class ProjectContext:
    form_fields: list[dict]
    form_choices: list[dict]
    context: dict  # what context.json gives the runner: project name and form dictionary
    question_labels: QuestionLabels  # the labels of the deployed form version, for the flattened columns


def project_context(connection: psycopg.Connection, project_id: UUID) -> ProjectContext:
    """The form snapshot and the ``ctx`` data every script of the project receives, in runs and tests alike."""
    # The question labels come from the stored definition of the deployed version, not from the snapshot, which holds
    # the form's latest content after a failed fetch. Same query, so the version and its labels agree.
    project_name, form_fields, form_choices, form_version, version_status, version_fields = connection.execute(
        """
        SELECT p.name, p.form_fields, p.form_choices, p.form_version, v.status, v.fields
        FROM tamiza.projects p
        LEFT JOIN tamiza.form_versions v ON v.project_id = p.id AND v.uid = p.form_version
        WHERE p.id = %s
        """,
        (project_id,),
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
    return ProjectContext(
        form_fields, form_choices, context, question_labels(form_version, version_status, version_fields)
    )


def column_context(tables) -> list[dict]:
    """
    What ``ctx.form.columns`` is built from: the table, column, path and question label of every column of the
    flattened tables that holds answers (the system columns are left out).
    """
    return [{"table": t.name, "column": c.name, "path": c.path, "label": c.label} for t in tables for c in t.columns]


def load_reference(settings: Settings, project_id: UUID, work_dir: Path) -> tuple[list[ReferenceTable], str]:
    """Reads the project's reference tables once for a run or test; returns them and a line for the step log."""
    tables = reference.load(settings.database_url, project_id, work_dir / "reference")
    if not tables:
        return tables, "No reference tables."
    listed = ", ".join(f"{t.name} ({t.rows:,} rows)" for t in tables)
    return tables, f"Reference tables: {listed}."


_NOTEWORTHY_PUBLISH_WAIT_SECONDS = 5.0


def _publish(
    connection, run: ClaimedRun, settings: Settings, record: StepRecord, step: str, tables: list[publish.Table]
) -> None:
    """Publishes a step's tables; when the swap had to wait for readers (an export), the step's log says so."""
    waited = publish.publish(connection, run.project_id, step, tables, max_wait_seconds=settings.publish_wait_seconds)
    if waited > _NOTEWORTHY_PUBLISH_WAIT_SECONDS:
        line = f"Publishing waited {waited:.0f} s for readers of the published tables."
        record.log = f"{record.log}\n{line}".strip()


def _publish_table(connection, run: ClaimedRun, settings: Settings, record: StepRecord, table: publish.Table) -> None:
    """Publishes one indicator's table; when the swap had to wait for readers (an export), the step's log says so."""
    waited = publish.publish_table(
        connection, run.project_id, "indicators", table, max_wait_seconds=settings.publish_wait_seconds
    )
    if waited > _NOTEWORTHY_PUBLISH_WAIT_SECONDS:
        line = f"Publishing waited {waited:.0f} s for readers of the published table."
        record.log = f"{record.log}\n{line}".strip()


def _execute_steps(
    connection, run: ClaimedRun, settings: Settings, work_dir: Path, records: dict[tuple[str, str], StepRecord]
):
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
            form_fields,
            lambda: _submissions(settings.database_url, run.project_id),
            work_dir / "raw",
            labels=labels,
            question_labels=project.question_labels,
        )
        context["columns"] = column_context(result.tables)
        raw = [OutputTable(t.name, t.rows, t.path, t.column_labels) for t in result.tables]
        record.log = "\n".join([reference_log, *result.log])
        _publish(
            connection, run, settings, record, "raw", [publish.Table(t.name, t.path, t.column_labels) for t in raw]
        )
        record.input_rows = result.input_rows
        _set_outputs(record, raw)

    _run_step(connection, run, records[("flatten", "")], do_flatten)
    if raw is None:
        _record_skipped(connection, run, records, "Not run: flattening failed.")
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
        # Without script 1 the clean tables are the flattened ones, so they keep their column labels; a script's
        # tables carry none.
        _publish(
            connection,
            run,
            settings,
            record,
            "clean",
            [publish.Table(t.name, t.path, t.column_labels) for t in cleaned],
        )
        record.input_rows = sum(t.rows for t in raw)
        _set_outputs(record, cleaned)

    _run_step(connection, run, records[("clean", "")], do_clean)
    if cleaned is None:
        _record_skipped(connection, run, records, "Not run: the clean step failed.")
        return

    # Script 2 and the indicator scripts each depend only on the clean step.
    def do_output(record: StepRecord) -> None:
        if codes["output"] is None:
            publish.drop(connection, run.project_id, "output")
            record.status = "skipped"
            record.log = "No output script: nothing is published for this step."
            return
        outputs = _script(record, work_dir, "output", codes["output"], cleaned, context, limits, references)
        if outputs is None:
            return
        _publish(connection, run, settings, record, "output", [publish.Table(t.name, t.path) for t in outputs])
        record.input_rows = sum(t.rows for t in cleaned)
        _set_outputs(record, outputs)

    _run_step(connection, run, records[("output", "")], do_output)

    # The indicators, one at a time in name order: a failing one keeps its previous table and does not stop the rest.
    if not run.indicators:
        publish.drop(connection, run.project_id, "indicators")
        return
    removal_note = _drop_removed_indicators(connection, run, settings)
    for position, indicator in enumerate(run.indicators):

        def do_indicator(
            record: StepRecord, indicator: IndicatorVersion = indicator, first: bool = position == 0
        ) -> None:
            code = _code(connection, indicator.version_id)
            if code is None:
                raise RuntimeError(f"Version {indicator.number} of the indicator has no code.")
            outputs = _script(
                record, work_dir, INDICATOR, code, cleaned, context, limits, references, name=indicator.name
            )
            if first and removal_note:
                record.log = f"{removal_note}\n{record.log}".strip()
            if outputs is None:
                return
            _publish_table(connection, run, settings, record, publish.Table(outputs[0].name, outputs[0].path))
            record.input_rows = sum(t.rows for t in cleaned)
            _set_outputs(record, outputs)

        _run_step(connection, run, records[(INDICATOR, indicator.name)], do_indicator)


def _drop_removed_indicators(connection, run: ClaimedRun, settings: Settings) -> str:
    """
    Drops the tables of indicators the project no longer has, before the indicators run. A table that stays in use past
    the publish wait is left for the next run; the first indicator's log says what happened.
    """
    try:
        removed = publish.drop_tables_except(
            connection,
            run.project_id,
            "indicators",
            [i.name for i in run.indicators],
            max_wait_seconds=settings.publish_wait_seconds,
        )
    except publish.PublishError as error:
        logger.warning("Run %s could not remove tables of removed indicators: %s", run.id, error)
        return f"Tables of removed indicators were kept: {error}"
    return f"Removed the tables of indicators that no longer exist: {', '.join(removed)}." if removed else ""


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


def _script(
    record, work_dir, kind, code, inputs, context, limits, references, name: str | None = None
) -> list[OutputTable] | None:
    outcome = run_script(
        work_dir / "steps",
        kind,
        code,
        {t.name: t.path for t in inputs},
        context,
        work_dir / kind if name is None else work_dir / "indicators" / name,
        limits,
        references,
        name,
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


def _record_skipped(connection, run, records, reason) -> None:
    """Records every step that has not run yet as skipped."""
    for record in records.values():
        if record.started_at is None:
            record.log = reason
            _save(connection, run, record)


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
            (run_id, step, name, status, started_at, finished_at, input_rows, output_rows, tables, log, error)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
        ON CONFLICT (run_id, step, name) DO UPDATE SET status = excluded.status, started_at = excluded.started_at,
            finished_at = excluded.finished_at, input_rows = excluded.input_rows, output_rows = excluded.output_rows,
            tables = excluded.tables, log = excluded.log, error = excluded.error
        """,
        (
            run.id,
            record.step,
            record.name,
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
    """The code of script 1 and script 2 as the run uses them; None when there is no script or it was removed."""
    return {
        kind: None if version_id is None else _code(connection, version_id) for kind, version_id in run.versions.items()
    }


def _code(connection, version_id: UUID) -> str | None:
    row = connection.execute("SELECT code FROM tamiza.script_versions WHERE id = %s", (version_id,)).fetchone()
    return row[0] if row else None


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
