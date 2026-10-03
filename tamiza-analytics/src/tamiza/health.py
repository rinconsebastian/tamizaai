"""Container healthcheck: healthy while the worker keeps touching its heartbeat file."""

import sys
import time
from pathlib import Path

from tamiza import settings

STALE_AFTER_INTERVALS = 3


def is_healthy(heartbeat_file: Path, poll_seconds: float, now: float | None = None) -> bool:
    try:
        age = (time.time() if now is None else now) - heartbeat_file.stat().st_mtime
    except FileNotFoundError:
        return False
    return age < STALE_AFTER_INTERVALS * poll_seconds


def main() -> int:
    healthy = is_healthy(settings.heartbeat_file_from_env(), settings.poll_seconds_from_env())
    return 0 if healthy else 1


if __name__ == "__main__":
    sys.exit(main())
