"""Sample downloads against PostgreSQL: the worker prepares a zip of the sample and stores it in parts."""

import dataclasses
import hashlib
import io
import json
import platform
import uuid
import zipfile
from importlib import metadata
from pathlib import Path

import pytest
from helpers import add_reference_table, add_script, add_submissions, create_project, queue_run, submission
from test_sample_runs import CLEAN, COVERAGE, queue_test

import tamiza_sdk as tz
from tamiza import pipeline, sample_downloads, sample_runs
from tamiza.settings import Settings

RAISING_CLEAN = """
def clean(raw, ctx):
    return {"main": raw["main"][raw["main"]["no_such_column"] > 1]}
"""


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def seeded(database, project):
    add_submissions(
        database, project, [submission(1, "cali", 40), submission(2, "buga", 35), submission(3, "cali", 15)]
    )
    return project


def queue_download(database, project, sample_size=500, status="queued") -> uuid.UUID:
    download_id = uuid.uuid4()
    database.execute(
        """
        INSERT INTO tamiza.sample_downloads (id, project_id, requested_by, sample_size, status, queued_at)
        VALUES (%s, %s, %s, %s, %s, now())
        """,
        download_id,
        project.id,
        project.user_id,
        sample_size,
        status,
    )
    return download_id


def run_next_download(database, settings) -> uuid.UUID:
    with database.connect() as connection:
        claimed = sample_downloads.claim(connection)
        assert claimed is not None
        sample_downloads.execute(connection, claimed, settings)
        return claimed.id


def row(database, download_id) -> dict:
    columns = ["status", "error_code", "error", "size_bytes", "file_sha256", "contents", "clean_version_id"]
    values = database.rows(f"SELECT {', '.join(columns)} FROM tamiza.sample_downloads WHERE id = %s", download_id)[0]
    return dict(zip(columns, values, strict=True))


def stored_zip(database, download_id) -> bytes:
    parts = database.rows(
        "SELECT data FROM tamiza.sample_download_parts WHERE download_id = %s ORDER BY part", download_id
    )
    return b"".join(bytes(data) for (data,) in parts)


def unzip(database, download_id, target: Path) -> Path:
    """Extracts the stored zip and returns the folder it holds (the one a downloader unzips)."""
    with zipfile.ZipFile(io.BytesIO(stored_zip(database, download_id))) as archive:
        archive.extractall(target)
        (top,) = {name.split("/")[0] for name in archive.namelist()}
    return target / top


def prepare(database, project, settings, tmp_path, sample_size=500) -> tuple[uuid.UUID, Path]:
    download_id = queue_download(database, project, sample_size)
    run_next_download(database, settings)
    assert row(database, download_id)["status"] == "succeeded", row(database, download_id)
    return download_id, unzip(database, download_id, tmp_path / "unzipped")


def add_population(database, project):
    add_reference_table(
        database,
        project,
        "population",
        [("municipality_code", "text"), ("population_2026", "integer")],
        [["cali", 2_200_000], ["buga", 130_000]],
    )


def test_a_download_is_prepared_without_a_version_a_run_or_a_publication(database, seeded, settings, tmp_path):
    add_script(database, seeded, "clean", "def clean(raw, ctx):\n    return raw\n")

    download_id, _ = prepare(database, seeded, settings, tmp_path)

    assert database.scalar("SELECT count(*) FROM tamiza.script_versions") == 1
    assert database.scalar("SELECT count(*) FROM tamiza.runs") == 0
    assert database.scalar("SELECT count(*) FROM tamiza.script_tests") == 0
    assert (
        database.scalar(
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name LIKE %s", f"p_{seeded.id.hex}%"
        )
        == 0
    )
    stored = row(database, download_id)
    assert stored["clean_version_id"] is not None and stored["error"] is None


def test_the_download_holds_the_flattened_sample_script_1s_result_and_the_reference_tables(
    database, seeded, settings, tmp_path
):
    add_script(database, seeded, "clean", CLEAN)
    add_population(database, seeded)

    _, folder = prepare(database, seeded, settings, tmp_path)

    raw = tz.load_tables("raw", folder)
    clean = tz.load_tables("clean", folder)
    assert sorted(raw) == ["main", "members"] and len(raw["main"]) == 3 and len(raw["members"]) == 6
    assert sorted(clean) == ["main", "members"] and len(clean["main"]) == 2  # script 1 dropped the minor
    assert sorted(raw["main"]["_id"]) == [1, 2, 3] and sorted(clean["main"]["_id"]) == [1, 2]
    ctx = tz.load_context(folder)
    assert list(ctx.reference) == ["population"] and list(ctx.reference["population"]["population_2026"]) == [
        2_200_000,
        130_000,
    ]
    assert ctx.project_name == "Household survey"
    assert ctx.form.label("municipality", "buga") == "Buga"
    assert ctx.form.name("municipality", "Cali") == "cali"  # the label codes travel in context.json
    # The columns of the flattened tables that hold answers are listed for ctx.form.columns.
    assert {"municipality", "age"} <= set(ctx.form.columns.loc[ctx.form.columns["table"] == "main", "column"])


def test_select_answers_are_labels_as_in_the_tables_a_test_gives_the_scripts(database, seeded, settings, tmp_path):
    _, folder = prepare(database, seeded, settings, tmp_path)

    assert sorted(tz.load_tables("raw", folder)["main"]["municipality"]) == ["Buga", "Cali", "Cali"]


def test_without_script_1_the_download_says_so_and_the_clean_tables_are_the_flattened_ones(
    database, seeded, settings, tmp_path
):
    _, folder = prepare(database, seeded, settings, tmp_path)

    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["clean"] == {"status": "none", "version": None, "error": None, "tables": []}
    assert not (folder / "clean").exists()
    assert len(tz.load_tables("clean", folder)["main"]) == 3


def test_deleted_submissions_are_not_in_the_download(database, seeded, settings, tmp_path):
    add_submissions(database, seeded, [submission(9, "buga", 50)], deleted=True)

    _, folder = prepare(database, seeded, settings, tmp_path)

    assert sorted(tz.load_tables("raw", folder)["main"]["_id"]) == [1, 2, 3]


def test_a_download_succeeds_when_script_1_raises_and_says_what_failed(database, seeded, settings, tmp_path):
    add_script(database, seeded, "clean", RAISING_CLEAN)

    _, folder = prepare(database, seeded, settings, tmp_path)

    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["clean"]["status"] == "failed" and manifest["clean"]["version"] == 1
    assert "KeyError" in manifest["clean"]["error"] and "no_such_column" in manifest["clean"]["error"]
    assert not (folder / "clean").exists()
    assert "no_such_column" in (folder / "logs" / "clean.txt").read_text(encoding="utf-8")
    with pytest.raises(tz.ScriptError, match="Script 1 .* failed"):
        tz.load_tables("clean", folder)
    assert len(tz.load_tables("raw", folder)["main"]) == 3
    assert "Script 1 (version 1) failed" in (folder / "README.md").read_text(encoding="utf-8")


def test_a_download_after_a_test_reuses_both_cached_steps(database, seeded, settings, tmp_path):
    add_script(database, seeded, "clean", CLEAN)
    queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    with database.connect() as connection:
        sample_runs.execute(connection, sample_runs.claim(connection), settings)

    _, folder = prepare(database, seeded, settings, tmp_path)

    assert "Reused from an earlier test." in (folder / "logs" / "flatten.txt").read_text(encoding="utf-8")
    assert "Reused from an earlier test." in (folder / "logs" / "clean.txt").read_text(encoding="utf-8")
    assert len(tz.load_tables("clean", folder)["main"]) == 2


def test_the_manifest_describes_the_download(database, seeded, settings, tmp_path):
    add_script(database, seeded, "clean", CLEAN)
    add_script(database, seeded, "clean", CLEAN)  # script 1 is at version 2
    add_population(database, seeded)

    download_id, folder = prepare(database, seeded, settings, tmp_path)

    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["sample"] == {"requested": 500, "submissions": 3}
    assert manifest["project"] == {"id": str(seeded.id), "name": "Household survey"}
    assert manifest["python"] == platform.python_version()
    assert manifest["libraries"]["pandas"] == metadata.version("pandas")
    assert manifest["raw"]["tables"] == [{"name": "main", "rows": 3}, {"name": "members", "rows": 6}]
    assert manifest["clean"] == {
        "status": "succeeded",
        "version": 2,
        "error": None,
        "tables": [{"name": "main", "rows": 2}, {"name": "members", "rows": 6}],
    }
    assert [(t["name"], t["rows"]) for t in manifest["reference"]] == [("population", 2)]
    # The page reads the manifest without the libraries.
    contents = row(database, download_id)["contents"]
    assert "libraries" not in contents and contents["sample"]["submissions"] == 3
    assert contents["clean"]["version"] == 2 and contents["reference"][0]["name"] == "population"


def test_the_stored_parts_are_the_zip_and_match_its_checksum(database, seeded, settings, tmp_path):
    # Enough poorly compressible reference data that the zip needs more than one 1 MiB part.
    add_reference_table(
        database, seeded, "codes", [("code", "text")], [[uuid.uuid4().hex + uuid.uuid4().hex] for _ in range(40_000)]
    )

    download_id, _ = prepare(database, seeded, settings, tmp_path)

    stored = row(database, download_id)
    parts = database.rows(
        "SELECT part, length(data) FROM tamiza.sample_download_parts WHERE download_id = %s ORDER BY part", download_id
    )
    assert len(parts) >= 2 and [part for part, _ in parts] == list(range(len(parts)))
    assert all(size == sample_downloads.PART_BYTES for _, size in parts[:-1])
    data = stored_zip(database, download_id)
    assert len(data) == stored["size_bytes"] > sample_downloads.PART_BYTES
    assert hashlib.sha256(data).hexdigest() == stored["file_sha256"]
    assert zipfile.ZipFile(io.BytesIO(data)).testzip() is None


def test_a_sample_over_the_size_limit_fails_with_its_size_and_keeps_nothing(database, seeded, settings, tmp_path):
    add_reference_table(
        database, seeded, "codes", [("code", "text")], [[uuid.uuid4().hex + uuid.uuid4().hex] for _ in range(40_000)]
    )
    download_id = queue_download(database, seeded)

    run_next_download(database, dataclasses.replace(settings, sample_download_mb=1))

    stored = row(database, download_id)
    assert stored["status"] == "failed" and stored["error_code"] == "download.tooLarge"
    assert "more than the 1 MB this server allows" in stored["error"] and "Choose a smaller sample" in stored["error"]
    assert "reference/codes.parquet" in stored["error"]  # the file that makes it large
    assert stored["size_bytes"] is None and stored["contents"] is None
    assert database.scalar("SELECT count(*) FROM tamiza.sample_download_parts") == 0
    assert not list((tmp_path / "runs").glob("download-*"))  # the work directory is removed


def test_the_download_holds_nothing_secret(database, seeded, settings, tmp_path):
    database.execute(
        "UPDATE tamiza.projects SET encrypted_api_token = %s, encrypted_webhook_secret = %s WHERE id = %s",
        "TOKEN-9f3a-kobo",
        "SECRET-4b1c-webhook",
        seeded.id,
    )
    add_script(database, seeded, "clean", CLEAN)
    download_id, _ = prepare(database, seeded, settings, tmp_path)

    text = b""
    with zipfile.ZipFile(io.BytesIO(stored_zip(database, download_id))) as archive:
        text = b"\n".join(archive.read(name) for name in archive.namelist())
    for secret in (
        "TOKEN-9f3a-kobo",
        "SECRET-4b1c-webhook",
        "password=",
        "PGPASSWORD",
        "kf.kobotoolbox.org",
        "aAsset",
        database.name,
    ):
        assert secret.encode() not in text, secret


def test_a_download_left_running_by_a_stopped_worker_is_failed_at_start(database, seeded, settings):
    download_id = queue_download(database, seeded, status="running")

    with database.connect() as connection:
        assert pipeline.Processor(settings)(connection) is False

    stored = row(database, download_id)
    assert stored["status"] == "failed" and stored["error_code"] == "download.interrupted"
    assert "interrupted" in stored["error"]


def test_a_download_is_deleted_with_its_parts_an_hour_after_it_ended(database, seeded, settings, tmp_path):
    fresh, _ = prepare(database, seeded, settings, tmp_path)
    # A second project: in one project a ready download supersedes the user's earlier one at once.
    old, _ = prepare(database, create_project(database, seeded.user_id), settings, tmp_path / "second")
    database.execute(
        "UPDATE tamiza.sample_downloads SET finished_at = now() - interval '61 minutes' WHERE id = %s", old
    )

    with database.connect() as connection:
        pipeline.Processor(settings)(connection)

    ids = {r[0] for r in database.rows("SELECT id FROM tamiza.sample_downloads")}
    assert ids == {fresh}
    assert database.scalar("SELECT count(*) FROM tamiza.sample_download_parts WHERE download_id = %s", old) == 0
    assert database.scalar("SELECT count(*) FROM tamiza.sample_download_parts WHERE download_id = %s", fresh) > 0


def test_a_queued_download_goes_ahead_of_a_queued_run(database, seeded, settings):
    run_id = queue_run(database, seeded)
    download_id = queue_download(database, seeded)

    with database.connect() as connection:
        processor = pipeline.Processor(settings)
        assert processor(connection) is True
        assert row(database, download_id)["status"] == "succeeded"
        assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "queued"
        assert processor(connection) is True
    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"


def test_a_queued_test_goes_ahead_of_a_queued_download(database, seeded, settings):
    download_id = queue_download(database, seeded)
    test_id = queue_test(database, seeded, "indicator", COVERAGE, name="coverage")

    with database.connect() as connection:
        processor = pipeline.Processor(settings)
        assert processor(connection) is True
        assert database.scalar("SELECT status FROM tamiza.script_tests WHERE id = %s", test_id) == "succeeded"
        assert row(database, download_id)["status"] == "queued"
        assert processor(connection) is True
    assert row(database, download_id)["status"] == "succeeded"


def test_a_download_of_a_project_without_submissions_holds_empty_tables(database, project, settings, tmp_path):
    _, folder = prepare(database, project, settings, tmp_path)

    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["sample"] == {"requested": 500, "submissions": 0}
    assert len(tz.load_tables("raw", folder)["main"]) == 0


def test_a_worker_fault_in_script_1s_step_is_not_blamed_on_script_1_and_leaves_no_traceback(
    database, seeded, settings, tmp_path
):
    add_script(database, seeded, "clean", CLEAN)
    queue_test(database, seeded, "indicator", COVERAGE, name="coverage")
    with database.connect() as connection:
        sample_runs.execute(connection, sample_runs.claim(connection), settings)
    # An entry of the cache that parses but lacks what reuse reads: a fault of the worker, not of the script.
    corrupted = 0
    for step_file in (settings.run_work_dir / "sample-cache").glob("*/step.json"):
        record = json.loads(step_file.read_text(encoding="utf-8"))
        if "columns" not in record:  # the clean entry; the flatten entry carries the column list
            step_file.write_text(json.dumps({"inputRows": 1}), encoding="utf-8")
            corrupted += 1
    assert corrupted == 1
    download_id = queue_download(database, seeded)

    run_next_download(database, settings)

    stored = row(database, download_id)
    assert stored["status"] == "failed" and stored["error_code"] == "download.failed"
    assert stored["error"].startswith("The saved script 1 could not be run: KeyError")
    assert "Traceback" not in stored["error"] and "site-packages" not in stored["error"]
    assert database.scalar("SELECT count(*) FROM tamiza.sample_download_parts") == 0


def test_a_ready_download_supersedes_the_users_earlier_finished_ones(database, seeded, settings, tmp_path):
    first, _ = prepare(database, seeded, settings, tmp_path)
    # The user asked again while the first was still being prepared, so the API left it alone: both rows exist.
    second = queue_download(database, seeded)

    run_next_download(database, settings)

    assert row(database, second)["status"] == "succeeded"
    ids = {r[0] for r in database.rows("SELECT id FROM tamiza.sample_downloads WHERE status = 'succeeded'")}
    assert ids == {second}
    assert database.scalar("SELECT count(*) FROM tamiza.sample_download_parts WHERE download_id = %s", first) == 0


def test_the_worker_removes_the_work_directories_a_kill_left_behind_but_keeps_the_sample_cache(
    database, seeded, settings
):
    root = settings.run_work_dir
    for name in ("download-dead", "test-dead", str(uuid.uuid4())):
        (root / name).mkdir(parents=True)
        (root / name / "big.parquet").write_bytes(b"x" * 10)
    (root / "sample-cache" / "entry").mkdir(parents=True)

    with database.connect() as connection:
        pipeline.Processor(settings)(connection)

    assert sorted(path.name for path in root.iterdir()) == ["sample-cache"]
    assert (root / "sample-cache" / "entry").is_dir()
