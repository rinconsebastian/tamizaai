"""The SDK runs on an analyst's computer, so it must not depend on the server's environment.

It is the code the worker shares with a download, and a download is used on Windows, macOS and Linux with only pandas
and its companions installed. These tests keep the database, DuckDB, networking and POSIX-only calls out of it, and keep
the versions of the worker, the SDK and the project in step.
"""

import ast
import subprocess
import sys
import tomllib
from pathlib import Path

import pytest

import tamiza
import tamiza_sdk

ROOT = Path(__file__).resolve().parents[1]
SDK_DIR = ROOT / "src" / "tamiza_sdk"
FORBIDDEN_IMPORTS = {
    "tamiza",
    "psycopg",
    "duckdb",
    "socket",
    "urllib",
    "http",
    "subprocess",
    "signal",
    "resource",
    "fcntl",
}
FORBIDDEN_TEXT = ("os.killpg", "/proc")


def violations(source: str) -> list[str]:
    """What in ``source`` ties it to the server: forbidden imports (also lazy ones) and POSIX-only calls."""
    found = []
    for node in ast.walk(ast.parse(source)):
        if isinstance(node, ast.Import):
            modules = [alias.name for alias in node.names]
        elif isinstance(node, ast.ImportFrom):
            modules = [node.module or ""] if node.level == 0 else []
        else:
            continue
        found += [f"imports {module}" for module in modules if module.split(".")[0] in FORBIDDEN_IMPORTS]
    found += [f"uses {text}" for text in FORBIDDEN_TEXT if text in source]
    return found


SDK_MODULES = sorted(SDK_DIR.glob("*.py"))


def test_there_are_sdk_modules_to_check():
    assert {path.name for path in SDK_MODULES} >= {"__init__.py", "tables.py", "names.py", "context.py", "contract.py"}


@pytest.mark.parametrize("path", SDK_MODULES, ids=lambda path: path.name)
def test_an_sdk_module_does_not_depend_on_the_server(path):
    assert violations(path.read_text(encoding="utf-8")) == []


@pytest.mark.parametrize(
    ("source", "expected"),
    [
        ("import psycopg\n", ["imports psycopg"]),
        ("def f():\n    from duckdb import connect\n", ["imports duckdb"]),
        ("from tamiza import names\n", ["imports tamiza"]),
        ("import urllib.request\n", ["imports urllib.request"]),
        ("import os\nos.killpg(1, 9)\n", ["uses os.killpg"]),
        ("from pathlib import Path\nPath('/proc')\n", ["uses /proc"]),
        ("from tamiza_sdk import tables\nimport importlib.metadata\n", []),
    ],
)
def test_the_check_finds_what_it_forbids(source, expected):
    assert violations(source) == expected


def test_the_versions_of_the_worker_the_sdk_and_the_project_agree():
    project = tomllib.loads((ROOT / "pyproject.toml").read_text(encoding="utf-8"))["project"]["version"]
    assert tamiza_sdk.__version__ == tamiza.__version__ == project


def test_the_healthcheck_does_not_load_pandas():
    # It runs every ten seconds in the container; importing the SDK or pandas would make each run cost a second.
    code = "import sys, tamiza.health; sys.exit(1 if {'pandas', 'tamiza_sdk'} & set(sys.modules) else 0)"
    result = subprocess.run([sys.executable, "-I", "-c", code], capture_output=True, text=True, check=False)
    assert result.returncode == 0, result.stderr
