"""Worker loop: checks the database, touches the heartbeat and executes queued pipeline runs one at a time."""

import contextlib
import logging
import signal
import threading
from collections.abc import Callable, Iterator
from typing import Protocol

import psycopg

from tamiza import logs, pipeline, settings

logger = logging.getLogger("tamiza.worker")


class Connection(Protocol):
    def execute(self, query: str) -> object: ...

    def close(self) -> None: ...


Connect = Callable[[str], Connection]
# Called once per cycle after a successful database check; returns True when it executed a run.
Process = Callable[[Connection], bool]


def _connect(database_url: str) -> Connection:
    return psycopg.connect(database_url, autocommit=True, connect_timeout=5)


def run(
    config: settings.Settings,
    stop: threading.Event,
    connect: Connect = _connect,
    process: Process | None = None,
) -> None:
    """Runs until ``stop`` is set. The heartbeat is touched only after a successful database round trip."""
    process = process if process is not None else pipeline.Processor(config)
    connection: Connection | None = None
    logger.info("Worker started; polling every %s seconds.", config.poll_seconds)
    while not stop.is_set():
        executed = False
        try:
            if connection is None:
                connection = connect(config.database_url)
            connection.execute("SELECT 1")
            config.heartbeat_file.touch()
            with _heartbeat(config, stop):
                executed = process(connection)
        except psycopg.Error as error:
            logger.warning("Database check failed: %s", error)
            if connection is not None:
                connection.close()
                connection = None
        # After a run, look for the next one at once.
        if not executed:
            stop.wait(config.poll_seconds)

    if connection is not None:
        connection.close()
    logger.info("Worker stopped.")


@contextlib.contextmanager
def _heartbeat(config: settings.Settings, stop: threading.Event) -> Iterator[None]:
    """Keeps the healthcheck green while a long run executes; the run itself proves the database is reachable."""
    done = threading.Event()

    def beat() -> None:
        while not done.wait(config.poll_seconds) and not stop.is_set():
            config.heartbeat_file.touch()

    thread = threading.Thread(target=beat, name="heartbeat", daemon=True)
    thread.start()
    try:
        yield
    finally:
        done.set()
        thread.join()


def main() -> None:
    logs.configure()
    stop = threading.Event()
    for signum in (signal.SIGTERM, signal.SIGINT):
        signal.signal(signum, lambda *_: stop.set())
    run(settings.load(), stop)


if __name__ == "__main__":
    main()
