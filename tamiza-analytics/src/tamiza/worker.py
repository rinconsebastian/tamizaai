"""Worker loop. For now it only proves the database answers; add-python-pipeline adds job claiming."""

import logging
import signal
import threading
from collections.abc import Callable
from typing import Protocol

import psycopg

from tamiza import logs, settings

logger = logging.getLogger("tamiza.worker")


class Connection(Protocol):
    def execute(self, query: str) -> object: ...

    def close(self) -> None: ...


Connect = Callable[[str], Connection]


def _connect(database_url: str) -> Connection:
    return psycopg.connect(database_url, autocommit=True, connect_timeout=5)


def run(config: settings.Settings, stop: threading.Event, connect: Connect = _connect) -> None:
    """Runs until ``stop`` is set. The heartbeat is touched only after a successful database round trip."""
    connection: Connection | None = None
    logger.info("Worker started; polling every %s seconds.", config.poll_seconds)
    while not stop.is_set():
        try:
            if connection is None:
                connection = connect(config.database_url)
            connection.execute("SELECT 1")
            config.heartbeat_file.touch()
        except psycopg.Error as error:
            logger.warning("Database check failed: %s", error)
            if connection is not None:
                connection.close()
                connection = None
        stop.wait(config.poll_seconds)

    if connection is not None:
        connection.close()
    logger.info("Worker stopped.")


def main() -> None:
    logs.configure()
    stop = threading.Event()
    for signum in (signal.SIGTERM, signal.SIGINT):
        signal.signal(signum, lambda *_: stop.set())
    run(settings.load(), stop)


if __name__ == "__main__":
    main()
