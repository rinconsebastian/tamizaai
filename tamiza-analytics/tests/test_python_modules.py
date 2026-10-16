"""The editor's list of importable modules matches what the worker has.

The list is a JSON file of the UI (``python-modules.json``), read here, so a library added to the worker, or a Python
upgrade that changes the standard library, breaks this test instead of leaving the editor warning about an import that
works, or staying quiet about one that does not.
"""

import importlib
import json
import sys
from pathlib import Path

import pytest

from tamiza import kit

MODULES = Path(__file__).resolve().parents[2] / "tamiza-ui/src/app/pages/projects/script-editor/python-modules.json"

# shapely is a dependency of geopandas, not a pin of the worker, and scripts can import it.
EXTRA_LIBRARIES = {"shapely"}


@pytest.fixture(scope="module")
def modules():
    return json.loads(MODULES.read_text(encoding="utf-8"))


def test_the_file_has_the_three_lists(modules):
    assert set(modules) == {"libraries", "never", "stdlib"}


def test_stdlib_is_the_standard_library_of_this_python(modules):
    assert set(modules["stdlib"]) == set(sys.stdlib_module_names)


def test_libraries_are_the_ones_of_the_worker_plus_shapely(modules):
    assert set(modules["libraries"]) == set(kit.LIBRARIES) | EXTRA_LIBRARIES


def test_every_library_imports_in_the_worker(modules):
    for name in modules["libraries"]:
        assert importlib.import_module(name), name


def test_the_never_list_names_standard_library_modules_that_exist(modules):
    assert set(modules["never"]) <= set(sys.stdlib_module_names)
    for name in modules["never"]:
        assert importlib.import_module(name), name


def test_no_module_is_both_a_library_and_a_module_scripts_never_need(modules):
    assert not set(modules["libraries"]) & set(modules["never"])


def test_lists_are_sorted_without_duplicates(modules):
    for name in ("libraries", "stdlib"):
        assert modules[name] == sorted(set(modules[name])), name
