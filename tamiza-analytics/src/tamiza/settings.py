"""Worker settings, read from environment variables."""

import os
from dataclasses import dataclass
from pathlib import Path

DEFAULT_POLL_SECONDS = 5.0
DEFAULT_HEARTBEAT_FILE = Path("/tmp/tamiza-heartbeat")


@dataclass(frozen=True)
class Settings:
    database_url: str
    poll_seconds: float = DEFAULT_POLL_SECONDS
    heartbeat_file: Path = DEFAULT_HEARTBEAT_FILE


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


def load(environ: dict[str, str] | None = None) -> Settings:
    environ = os.environ if environ is None else environ
    database_url = environ.get("TAMIZA_ANALYTICS_DATABASE_URL", "")
    if not database_url:
        raise ValueError("TAMIZA_ANALYTICS_DATABASE_URL is not set")
    return Settings(
        database_url=database_url,
        poll_seconds=poll_seconds_from_env(environ),
        heartbeat_file=heartbeat_file_from_env(environ),
    )
