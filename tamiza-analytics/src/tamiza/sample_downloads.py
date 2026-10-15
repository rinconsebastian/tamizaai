"""Sample downloads: a sample of a project's data prepared for local work and kept an hour as one zip in the database.

The worker prepares a download like a test: it flattens the sample and runs the saved script 1 on it, reusing what an
earlier test or download cached (``sample_runs.prepare_inputs``), writes the kit's folder (``kit``), zips it and
stores the zip in ``tamiza.sample_download_parts``, one megabyte at a time. The API and the worker share no disk, so
the database is where the API finds the file when its requester asks for a link. Queued downloads are claimed after
queued tests and before queued runs.
"""

import hashlib
import logging
import math
import shutil
from dataclasses import dataclass
from pathlib import Path
from uuid import UUID

import psycopg
from psycopg.types.json import Jsonb

from tamiza import kit, sample_runs
from tamiza.sample_runs import Prepared, TestStep
from tamiza.settings import Settings

logger = logging.getLogger("tamiza.sample_downloads")

LIFETIME_HOURS = 1
PART_BYTES = 1024 * 1024
INTERRUPTED = "download.interrupted"
TOO_LARGE = "download.tooLarge"
FAILED = "download.failed"
MEGABYTE = 1024 * 1024


@dataclass(frozen=True)
class ClaimedDownload:
    id: UUID
    project_id: UUID
    sample_size: int
    clean_version_id: UUID | None  # the current script 1 when the download was claimed; None when there is none


class DownloadFailed(Exception):
    """The download cannot be prepared; ``code`` and the message are what the requester sees."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def fail_interrupted(connection: psycopg.Connection) -> int:
    """Marks downloads left ``running`` by a previous worker as failed. Assumes a single worker."""
    cursor = connection.execute(
        """
        UPDATE tamiza.sample_downloads SET status = 'failed', finished_at = now(), error_code = %s,
               error = 'The download was interrupted because the analytics worker stopped.'
        WHERE status = 'running'
        """,
        (INTERRUPTED,),
    )
    return cursor.rowcount


def delete_expired(connection: psycopg.Connection) -> int:
    """Deletes downloads that ended more than an hour ago; their parts go with them."""
    cursor = connection.execute(
        "DELETE FROM tamiza.sample_downloads WHERE finished_at < now() - make_interval(hours => %s)", (LIFETIME_HOURS,)
    )
    return cursor.rowcount


def claim(connection: psycopg.Connection) -> ClaimedDownload | None:
    """Starts the oldest queued download and records the current version of script 1 for it."""
    with connection.transaction():
        row = connection.execute(
            """
            WITH next AS (
                SELECT id FROM tamiza.sample_downloads WHERE status = 'queued'
                ORDER BY queued_at FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE tamiza.sample_downloads d
            SET status = 'running', started_at = now(),
                clean_version_id = (
                    SELECT v.id FROM tamiza.script_versions v
                    WHERE v.project_id = d.project_id AND v.kind = 'clean' ORDER BY v.number DESC LIMIT 1)
            FROM next WHERE d.id = next.id
            RETURNING d.id, d.project_id, d.sample_size, d.clean_version_id
            """
        ).fetchone()
    return None if row is None else ClaimedDownload(*row)


def execute(connection: psycopg.Connection, download: ClaimedDownload, settings: Settings) -> None:
    logger.info("Download %s of project %s started.", download.id, download.project_id)
    work_dir = settings.run_work_dir / f"download-{download.id}"
    shutil.rmtree(work_dir, ignore_errors=True)
    work_dir.mkdir(parents=True)
    try:
        _prepare_and_store(connection, download, settings, work_dir)
        logger.info("Download %s of project %s is ready.", download.id, download.project_id)
    except DownloadFailed as error:
        _fail(connection, download, error.code, str(error))
    except Exception as error:  # the worker's own failure (database, disk): recorded, never fatal to the loop
        logger.exception("Download %s of project %s failed.", download.id, download.project_id)
        _fail(connection, download, FAILED, f"The sample could not be prepared: {type(error).__name__}: {error}")
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)


def _fail(connection: psycopg.Connection, download: ClaimedDownload, code: str, message: str) -> None:
    connection.execute(
        "UPDATE tamiza.sample_downloads SET status = 'failed', finished_at = now(), error_code = %s, error = %s"
        " WHERE id = %s",
        (code, message, download.id),
    )


def _prepare_and_store(
    connection: psycopg.Connection, download: ClaimedDownload, settings: Settings, work_dir: Path
) -> None:
    steps: list[TestStep] = []
    prepared = sample_runs.prepare_inputs(
        connection,
        download.project_id,
        download.sample_size,
        download.clean_version_id,
        True,
        settings,
        work_dir,
        steps,
        lambda: None,
        f"download {download.id}",
    )
    flatten = steps[0]
    if prepared.raw is None:
        first_line = (flatten.error or "Flattening the sample failed.").splitlines()[0]
        raise DownloadFailed("step.flatten", first_line)

    inputs = _kit_inputs(connection, download, prepared, steps)
    folder = kit.write_folder(work_dir / "kit", inputs)
    archive = work_dir / "sample.zip"
    size = kit.zip_folder(folder, archive)
    limit = settings.sample_download_mb * MEGABYTE
    if size > limit:
        raise DownloadFailed(TOO_LARGE, _too_large(size, settings.sample_download_mb, folder))

    contents = {key: value for key, value in kit.manifest(inputs).items() if key != "libraries"}
    _store(connection, download, archive, size, contents)


def _kit_inputs(
    connection: psycopg.Connection, download: ClaimedDownload, prepared: Prepared, steps: list[TestStep]
) -> kit.KitInputs:
    flatten = steps[0]
    clean_status, clean_version, clean_error, clean_log = "none", None, None, None
    if prepared.clean_ran:
        clean = steps[1]
        if clean.internal:
            # A fault of the worker is not script 1 failing: blaming it would send the analyst to debug a correct
            # script, and its traceback names the server's files.
            reason = (clean.error or "an unexpected error").splitlines()[0]
            raise DownloadFailed(FAILED, f"The saved script 1 could not be run: {reason}")
        row = connection.execute(
            "SELECT number FROM tamiza.script_versions WHERE id = %s", (download.clean_version_id,)
        ).fetchone()
        clean_version = row[0] if row else None
        if prepared.cleaned is None:
            clean_status = "failed"
            clean_error = (clean.error or "Script 1 failed.").splitlines()[0]
            clean_log = f"{clean.log}\n\n{clean.error or ''}".strip()
        else:
            clean_status, clean_log = "succeeded", clean.log
    project_name = prepared.project.context["project_name"]
    return kit.KitInputs(
        project_id=str(download.project_id),
        project_name=project_name,
        requested=download.sample_size,
        submissions=flatten.input_rows or 0,
        context=prepared.project.context,
        raw=prepared.raw or [],
        clean=prepared.cleaned or [],
        clean_status=clean_status,
        clean_version=clean_version,
        clean_error=clean_error,
        reference=prepared.reference_tables,
        flatten_log=flatten.log,
        clean_log=clean_log,
    )


def _too_large(size: int, limit_mb: int, folder: Path) -> str:
    """The message for a zip over the limit: its size, the limit and the files that make it large."""
    largest = sorted((path for path in folder.rglob("*.parquet")), key=lambda path: path.stat().st_size, reverse=True)[
        :3
    ]
    files = ", ".join(
        f"{path.relative_to(folder).as_posix()} ({math.ceil(path.stat().st_size / MEGABYTE)} MB)" for path in largest
    )
    return (
        f"The sample is {math.ceil(size / MEGABYTE)} MB, more than the {limit_mb} MB this server allows. "
        f"Choose a smaller sample. The largest files are {files}."
    )


def _store(connection: psycopg.Connection, download: ClaimedDownload, archive: Path, size: int, contents: dict) -> None:
    """Writes the zip in parts and marks the download ready in one transaction, so a reader never sees half of it."""
    digest = hashlib.sha256()
    with connection.transaction():
        with archive.open("rb") as file:
            part = 0
            while chunk := file.read(PART_BYTES):
                digest.update(chunk)
                connection.execute(
                    "INSERT INTO tamiza.sample_download_parts (download_id, part, data) VALUES (%s, %s, %s)",
                    (download.id, part, chunk),
                )
                part += 1
        connection.execute(
            """
            UPDATE tamiza.sample_downloads
            SET status = 'succeeded', finished_at = now(), size_bytes = %s, file_sha256 = %s, contents = %s
            WHERE id = %s
            """,
            (size, digest.hexdigest(), Jsonb(contents), download.id),
        )
        # A request made while this one was being prepared did not replace it (a running download is never touched);
        # now that it is ready, the user's earlier finished downloads in the project are superseded, so the database
        # holds at most one ready download per user and project.
        connection.execute(
            """
            DELETE FROM tamiza.sample_downloads o USING tamiza.sample_downloads d
            WHERE d.id = %s AND o.id <> d.id AND o.project_id = d.project_id AND o.requested_by = d.requested_by
              AND o.status IN ('succeeded', 'failed')
            """,
            (download.id,),
        )
