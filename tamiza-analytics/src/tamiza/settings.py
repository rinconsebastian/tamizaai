"""Worker settings, read from environment variables."""

import os
from dataclasses import dataclass
from pathlib import Path

DEFAULT_POLL_SECONDS = 5.0
DEFAULT_HEARTBEAT_FILE = Path("/tmp/tamiza-heartbeat")
DEFAULT_RUN_TIMEOUT_SECONDS = 600.0
DEFAULT_RUN_MEMORY_MB = 1024
DEFAULT_RUN_WORK_DIR = Path("/tmp/tamiza-runs")
DEFAULT_PUBLISH_WAIT_SECONDS = 900.0
DEFAULT_TEST_CACHE_MB = 512
DEFAULT_SAMPLE_DOWNLOAD_MB = 100


@dataclass(frozen=True)
class Settings:
    database_url: str
    poll_seconds: float = DEFAULT_POLL_SECONDS
    heartbeat_file: Path = DEFAULT_HEARTBEAT_FILE
    # Limits for each user script, and where runs keep their files while they execute.
    run_timeout_seconds: float = DEFAULT_RUN_TIMEOUT_SECONDS
    run_memory_mb: int = DEFAULT_RUN_MEMORY_MB
    run_work_dir: Path = DEFAULT_RUN_WORK_DIR
    # How long publishing waits for readers of the published tables (a dashboard export) before the step fails.
    publish_wait_seconds: float = DEFAULT_PUBLISH_WAIT_SECONDS
    # Disk that the flattened samples and script 1 results kept for reuse by script tests may use; 0 turns reuse off.
    test_cache_mb: int = DEFAULT_TEST_CACHE_MB
    # The largest zip a sample download may be; a sample that would exceed it fails and nothing is kept.
    sample_download_mb: int = DEFAULT_SAMPLE_DOWNLOAD_MB


def poll_seconds_from_env(environ: dict[str, str] | None = None) -> float:
    environ = os.environ if environ is None else environ
    raw = environ.get("TAMIZA_WORKER_POLL_SECONDS", "")
    if not raw:
        return DEFAULT_POLL_SECONDS
    value = float(raw)
    if value <= 0:
        raise ValueError("TAMIZA_WORKER_POLL_SECONDS must be greater than zero")
    return value


def heartbeat_file_from_env(environ: dict[str, str] | None = None) -> Path:
    environ = os.environ if environ is None else environ
    return Path(environ.get("TAMIZA_HEARTBEAT_FILE") or DEFAULT_HEARTBEAT_FILE)


def _positive(environ: dict[str, str], name: str, default: float, cast: type) -> float:
    raw = environ.get(name, "")
    if not raw:
        return default
    try:
        value = cast(raw)
    except ValueError as error:
        raise ValueError(f"{name} must be a number") from error
    if value <= 0:
        raise ValueError(f"{name} must be greater than zero")
    return value


def _cache_mb(environ: dict[str, str]) -> int:
    raw = environ.get("TAMIZA_TEST_CACHE_MB", "")
    if not raw:
        return DEFAULT_TEST_CACHE_MB
    try:
        value = int(raw)
    except ValueError as error:
        raise ValueError("TAMIZA_TEST_CACHE_MB must be a whole number") from error
    if value < 0:
        raise ValueError("TAMIZA_TEST_CACHE_MB must not be negative")
    return value


def load(environ: dict[str, str] | None = None) -> Settings:
    environ = os.environ if environ is None else environ
    database_url = environ.get("TAMIZA_ANALYTICS_DATABASE_URL", "")
    if not database_url:
        raise ValueError("TAMIZA_ANALYTICS_DATABASE_URL is not set")
    return Settings(
        database_url=database_url,
        poll_seconds=poll_seconds_from_env(environ),
        heartbeat_file=heartbeat_file_from_env(environ),
        run_timeout_seconds=_positive(environ, "TAMIZA_RUN_TIMEOUT_SECONDS", DEFAULT_RUN_TIMEOUT_SECONDS, float),
        run_memory_mb=int(_positive(environ, "TAMIZA_RUN_MEMORY_MB", DEFAULT_RUN_MEMORY_MB, int)),
        run_work_dir=Path(environ.get("TAMIZA_RUN_WORK_DIR") or DEFAULT_RUN_WORK_DIR),
        publish_wait_seconds=_positive(environ, "TAMIZA_PUBLISH_WAIT_SECONDS", DEFAULT_PUBLISH_WAIT_SECONDS, float),
        test_cache_mb=_cache_mb(environ),
        sample_download_mb=int(_positive(environ, "TAMIZA_SAMPLE_DOWNLOAD_MB", DEFAULT_SAMPLE_DOWNLOAD_MB, int)),
    )
