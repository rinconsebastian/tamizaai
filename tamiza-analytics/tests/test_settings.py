from pathlib import Path

import pytest

from tamiza import settings


def test_defaults():
    config = settings.load({"TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db/tamiza"})

    assert config.poll_seconds == 5.0
    assert config.heartbeat_file == Path("/tmp/tamiza-heartbeat")


def test_overrides():
    config = settings.load(
        {
            "TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db/tamiza",
            "TAMIZA_WORKER_POLL_SECONDS": "2.5",
            "TAMIZA_HEARTBEAT_FILE": "/run/hb",
        }
    )

    assert (config.poll_seconds, config.heartbeat_file) == (2.5, Path("/run/hb"))


def test_database_url_is_required():
    with pytest.raises(ValueError, match="TAMIZA_ANALYTICS_DATABASE_URL"):
        settings.load({})


@pytest.mark.parametrize("value", ["0", "-1"])
def test_poll_interval_must_be_positive(value):
    with pytest.raises(ValueError, match="TAMIZA_WORKER_POLL_SECONDS"):
        settings.load({"TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db", "TAMIZA_WORKER_POLL_SECONDS": value})


def test_run_limit_defaults():
    config = settings.load({"TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db/tamiza"})

    assert (config.run_timeout_seconds, config.run_memory_mb) == (600.0, 1024)
    assert config.run_work_dir == Path("/tmp/tamiza-runs")


def test_run_limit_overrides():
    config = settings.load(
        {
            "TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db/tamiza",
            "TAMIZA_RUN_TIMEOUT_SECONDS": "90",
            "TAMIZA_RUN_MEMORY_MB": "2048",
            "TAMIZA_RUN_WORK_DIR": "/data/runs",
        }
    )

    assert (config.run_timeout_seconds, config.run_memory_mb, config.run_work_dir) == (90.0, 2048, Path("/data/runs"))


@pytest.mark.parametrize(
    ("name", "value"),
    [
        ("TAMIZA_RUN_TIMEOUT_SECONDS", "0"),
        ("TAMIZA_RUN_MEMORY_MB", "-1"),
        ("TAMIZA_RUN_MEMORY_MB", "1.5"),
        ("TAMIZA_RUN_TIMEOUT_SECONDS", "ten"),
    ],
)
def test_invalid_run_limits_are_rejected(name, value):
    with pytest.raises(ValueError, match=name):
        settings.load({"TAMIZA_ANALYTICS_DATABASE_URL": "postgresql://db/tamiza", name: value})
