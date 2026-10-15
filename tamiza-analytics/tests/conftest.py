import pytest
from sdk_download import make_download

from tamiza_sdk import bundle


@pytest.fixture(autouse=True)
def fresh_sdk_session():
    """Each test starts a new SDK session: folders are read again and environment warnings are reported again."""
    bundle._opened.clear()
    bundle._warned.clear()


@pytest.fixture
def download(tmp_path):
    """A download folder; call it with the options of ``make_download`` for another one (it returns the folder)."""

    def build(name: str = "download", **options):
        return make_download(tmp_path / name, **options)

    return build
