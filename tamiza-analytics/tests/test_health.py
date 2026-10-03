import os
import time

from tamiza.health import is_healthy


def test_fresh_heartbeat_is_healthy(tmp_path):
    heartbeat = tmp_path / "heartbeat"
    heartbeat.touch()

    assert is_healthy(heartbeat, poll_seconds=5)


def test_stale_heartbeat_is_unhealthy(tmp_path):
    heartbeat = tmp_path / "heartbeat"
    heartbeat.touch()
    old = time.time() - 16
    os.utime(heartbeat, (old, old))

    assert not is_healthy(heartbeat, poll_seconds=5)


def test_missing_heartbeat_is_unhealthy(tmp_path):
    assert not is_healthy(tmp_path / "missing", poll_seconds=5)
