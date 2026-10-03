import threading

import psycopg
import pytest

from tamiza.settings import Settings
from tamiza.worker import run


class FakeConnection:
    def __init__(self, stop: threading.Event, fail: bool = False):
        self.stop = stop
        self.fail = fail
        self.queries: list[str] = []
        self.closed = False

    def execute(self, query: str) -> None:
        self.queries.append(query)
        self.stop.set()  # one iteration is enough
        if self.fail:
            raise psycopg.OperationalError("database is down")

    def close(self) -> None:
        self.closed = True


@pytest.fixture
def config(tmp_path):
    return Settings(database_url="postgresql://test", poll_seconds=0.01, heartbeat_file=tmp_path / "heartbeat")


def test_successful_round_trip_touches_the_heartbeat(config):
    stop = threading.Event()
    connection = FakeConnection(stop)

    run(config, stop, connect=lambda _: connection, process=lambda _: False)

    assert connection.queries == ["SELECT 1"]
    assert config.heartbeat_file.exists()
    assert connection.closed


def test_database_failure_does_not_touch_the_heartbeat(config):
    stop = threading.Event()
    connection = FakeConnection(stop, fail=True)

    run(config, stop, connect=lambda _: connection, process=lambda _: False)

    assert not config.heartbeat_file.exists()
    assert connection.closed


def test_connection_failure_is_retried_on_the_next_iteration(config):
    stop = threading.Event()
    attempts = []

    def connect(_):
        attempts.append(1)
        if len(attempts) == 1:
            raise psycopg.OperationalError("not yet")
        return FakeConnection(stop)

    run(config, stop, connect=connect, process=lambda _: False)

    assert len(attempts) == 2
    assert config.heartbeat_file.exists()


def test_stop_before_start_exits_without_connecting(config):
    stop = threading.Event()
    stop.set()

    run(config, stop, connect=lambda _: pytest.fail("should not connect"), process=lambda _: False)

    assert not config.heartbeat_file.exists()


def test_runs_are_processed_after_the_database_check_and_the_next_is_sought_at_once(config):
    stop = threading.Event()
    calls = []

    def process(connection):
        calls.append(len(connection.queries))
        if len(calls) == 3:
            stop.set()
        return len(calls) < 3  # two runs, then nothing queued

    run(config, stop, connect=lambda _: RecordingConnection(), process=process)

    assert calls == [1, 2, 3]


def test_the_heartbeat_stays_fresh_during_a_long_run(config):
    stop = threading.Event()
    ages = []

    def slow_run(_):
        import time

        config.heartbeat_file.unlink()
        time.sleep(config.poll_seconds * 5)
        ages.append(config.heartbeat_file.exists())
        stop.set()
        return True

    run(config, stop, connect=lambda _: RecordingConnection(), process=slow_run)

    assert ages == [True]


class RecordingConnection:
    def __init__(self):
        self.queries = []

    def execute(self, query):
        self.queries.append(query)

    def close(self):
        pass
