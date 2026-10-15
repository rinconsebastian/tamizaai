"""Reading a download: the manifest, the refusal of an unknown format and the warnings about this environment."""

import json
import sys
import types
import warnings

import pandas as pd
import pytest

from tamiza_sdk import bundle, notebook
from tamiza_sdk.bundle import EnvironmentWarning, open_bundle
from tamiza_sdk.context import Context
from tamiza_sdk.contract import ScriptError


def caught(action):
    with warnings.catch_warnings(record=True) as recorded:
        warnings.simplefilter("always")
        result = action()
    return result, [str(w.message) for w in recorded if issubclass(w.category, EnvironmentWarning)]


def test_a_download_opens_with_its_manifest(download):
    folder = download()

    opened, messages = caught(lambda: open_bundle(folder))

    assert opened.manifest["sample"] == {"requested": 500, "submissions": 4}
    assert opened.clean_status == "succeeded"
    assert opened.clean_version == 2
    assert messages == []


def test_a_folder_without_a_manifest_is_explained(tmp_path):
    with pytest.raises(ScriptError, match="not a Tamiza sample download"):
        open_bundle(tmp_path)


def test_an_unreadable_manifest_is_explained(download):
    folder = download()
    (folder / "manifest.json").write_text("{not json", encoding="utf-8")

    with pytest.raises(ScriptError, match="manifest.json"):
        open_bundle(folder)


def test_a_newer_format_is_refused_with_the_way_out(download):
    folder = download(format_version=bundle.FORMAT + 1)

    with pytest.raises(ScriptError) as error:
        open_bundle(folder)

    assert "tamiza_sdk folder included in that download" in str(error.value)


def test_a_different_pandas_version_is_warned_once_and_the_tables_still_load(download):
    local = __import__("pandas").__version__
    libraries = {"pandas": "9.9.9"}
    first = download("first", libraries=libraries)
    second = download("second", libraries=libraries)

    opened, messages = caught(lambda: open_bundle(first))
    _, again = caught(lambda: open_bundle(second))

    assert opened.folder == first.resolve()
    assert len(messages) == 1
    assert f"pandas {local} is installed here" in messages[0] and "pandas 9.9.9" in messages[0]
    assert "results may differ" in messages[0]
    assert again == []  # once per session


def test_a_different_python_minor_version_is_warned(download):
    folder = download(python="3.8.1")

    _, messages = caught(lambda: open_bundle(folder))

    assert len(messages) == 1 and "Python 3.8.1" in messages[0]


def test_a_different_patch_version_is_not_a_difference(download):
    folder = download(python=f"{sys.version_info.major}.{sys.version_info.minor}.99")

    _, messages = caught(lambda: open_bundle(folder))

    assert messages == []


def test_a_library_that_is_not_installed_is_not_reported(download):
    folder = download(libraries={"not_a_real_library": "1.0"})

    _, messages = caught(lambda: open_bundle(folder))

    assert messages == []


def test_a_download_made_by_another_sdk_version_says_to_use_its_own(download):
    folder = download(sdk_version="0.0.1")

    _, messages = caught(lambda: open_bundle(folder))

    assert len(messages) == 1 and "tamiza_sdk folder included in the download" in messages[0]


def test_a_folder_is_read_once_per_session(download):
    folder = download()
    first = open_bundle(folder)
    (folder / "manifest.json").write_text(json.dumps({"format": 99}), encoding="utf-8")

    assert open_bundle(folder) is first


# ctx.show in a notebook.


def test_show_prints_the_name_rows_and_value_outside_a_notebook(capsys):
    frame = pd.DataFrame({"a": [1, 2, 3]})
    ctx = Context("p", None, None, show=notebook.NotebookShow())  # type: ignore[arg-type]

    returned = ctx.show(frame, "main")

    out = capsys.readouterr().out
    assert returned is frame
    assert out.splitlines()[0] == "main (3 rows)"
    assert "3" in out


def test_show_numbers_unnamed_calls_and_prints_other_values(capsys):
    ctx = Context("p", None, None, show=notebook.NotebookShow())  # type: ignore[arg-type]

    assert ctx.show({"count": 5}) == {"count": 5}
    ctx.show(len("abc"))

    lines = capsys.readouterr().out.splitlines()
    assert lines == ["shown 1", "{'count': 5}", "shown 2", "3"]


@pytest.fixture
def stub_notebook(monkeypatch):
    """A stand-in for IPython: a running shell and a display function that records what it is given."""
    displayed = []
    ipython = types.ModuleType("IPython")
    ipython.get_ipython = lambda: object()  # type: ignore[attr-defined]
    display = types.ModuleType("IPython.display")
    display.display = displayed.append  # type: ignore[attr-defined]
    monkeypatch.setitem(sys.modules, "IPython", ipython)
    monkeypatch.setitem(sys.modules, "IPython.display", display)
    return displayed


def test_show_displays_the_value_in_a_notebook(stub_notebook, capsys):
    frame = pd.DataFrame({"a": [1]})
    ctx = Context("p", None, None, show=notebook.NotebookShow())  # type: ignore[arg-type]

    returned = ctx.show(frame, "main")

    assert returned is frame
    assert stub_notebook == [frame]
    assert capsys.readouterr().out.strip() == "main (1 rows)"


def test_show_prints_when_ipython_is_installed_but_not_running(monkeypatch, capsys):
    ipython = types.ModuleType("IPython")
    ipython.get_ipython = lambda: None  # type: ignore[attr-defined]
    monkeypatch.setitem(sys.modules, "IPython", ipython)
    ctx = Context("p", None, None, show=notebook.NotebookShow())  # type: ignore[arg-type]

    ctx.show("text", "note")

    assert capsys.readouterr().out.splitlines() == ["note", "text"]
