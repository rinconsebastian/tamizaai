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
