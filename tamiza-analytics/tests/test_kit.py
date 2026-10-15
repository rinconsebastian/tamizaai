"""The files of a sample download: layout, manifest, pinned libraries and the zip."""

import json
import platform
import re
import subprocess
import sys
import tomllib
import zipfile
from datetime import UTC, datetime
from importlib import metadata
from pathlib import Path

import pytest
from sdk_download import clean_tables, raw_tables

import tamiza_sdk as tz
from tamiza import kit
from tamiza.launcher import OutputTable
from tamiza.reference import ReferenceTable
from tamiza_sdk import tables

CREATED = datetime(2026, 10, 20, 14, 3, 11, tzinfo=UTC)
PYPROJECT = Path(__file__).resolve().parents[1] / "pyproject.toml"


def written(directory: Path, frames: dict) -> list[OutputTable]:
    directory.mkdir(parents=True, exist_ok=True)
    out = []
    for name, frame in frames.items():
        tables.write_parquet(frame, directory / f"{name}.parquet")
        out.append(OutputTable(name, len(frame), directory / f"{name}.parquet"))
    return out


@pytest.fixture
def inputs(tmp_path):
    work = tmp_path / "work"
    reference = written(work / "reference", {"population": raw_tables()["members"]})
    return kit.KitInputs(
        project_id="00000000-0000-0000-0000-000000000001",
        project_name="Water survey 2026",
        requested=500,
        submissions=320,
        context={"project_name": "Water survey 2026", "questions": [], "choices": [], "columns": []},
        raw=written(work / "raw", raw_tables()),
        clean=written(work / "clean", clean_tables()),
        clean_status="succeeded",
        clean_version=2,
        reference=[ReferenceTable(t.name, t.rows, datetime(2026, 10, 19, 10, tzinfo=UTC), t.path) for t in reference],
        flatten_log="Sample: 320 submissions.",
        clean_log="Dropped 1 submission.",
        created_at=CREATED,
    )


def test_the_folder_has_the_layout_of_the_design(inputs, tmp_path):
    folder = kit.write_folder(tmp_path / "out", inputs)

    assert folder.name == "tamiza-sample-20261020-140311"
    files = sorted(p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file())
    assert {
        "README.md",
        "getting-started.ipynb",
        "clean/main.parquet",
        "clean/members.parquet",
        "context.json",
        "logs/clean.txt",
        "logs/flatten.txt",
        "manifest.json",
        "raw/main.parquet",
        "raw/members.parquet",
        "reference/population.parquet",
        "requirements.txt",
        "tamiza_sdk/__init__.py",
        "tamiza_sdk/checking.py",
        "tamiza_sdk/contract.py",
    } <= set(files)
    assert not any(name.endswith(".pyc") or "__pycache__" in name for name in files)
    assert (folder / "logs" / "flatten.txt").read_text(encoding="utf-8") == "Sample: 320 submissions."


def test_the_manifest_describes_the_download(inputs, tmp_path):
    folder = kit.write_folder(tmp_path / "out", inputs)

    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))

    assert manifest["format"] == 1 and manifest["tamiza"] == tz.__version__
    assert manifest["createdAt"] == "2026-10-20T14:03:11Z"
    assert manifest["project"] == {"id": "00000000-0000-0000-0000-000000000001", "name": "Water survey 2026"}
    assert manifest["sample"] == {"requested": 500, "submissions": 320}
    assert manifest["python"] == platform.python_version()
    assert manifest["libraries"]["pandas"] == metadata.version("pandas")
    assert manifest["raw"]["tables"] == [{"name": "main", "rows": 4}, {"name": "members", "rows": 3}]
    assert manifest["clean"] == {
        "status": "succeeded",
        "version": 2,
        "error": None,
        "tables": [{"name": "main", "rows": 3}, {"name": "members", "rows": 3}],
    }
    assert manifest["reference"] == [{"name": "population", "rows": 3, "updatedAt": "2026-10-19T10:00:00+00:00"}]


def test_the_requirements_pin_the_servers_libraries_and_no_database_driver(inputs, tmp_path):
    folder = kit.write_folder(tmp_path / "out", inputs)

    lines = (folder / "requirements.txt").read_text(encoding="utf-8").splitlines()

    pins = [line for line in lines if not line.startswith("#")]
    assert pins == [f"{name}=={metadata.version(name)}" for name in sorted(kit.LIBRARIES)]
    assert f"pandas=={metadata.version('pandas')}" in pins and f"pyarrow=={metadata.version('pyarrow')}" in pins
    assert not any(re.match(r"psycopg|duckdb-engine|sqlalchemy", line) for line in pins)
    assert lines[0].startswith("# ") and platform.python_version() in lines[0]


def test_the_libraries_are_the_images_dependencies_except_the_database_driver():
    dependencies = tomllib.loads(PYPROJECT.read_text(encoding="utf-8"))["project"]["dependencies"]
    names = {re.split(r"[\[=<>]", dependency)[0] for dependency in dependencies}

    assert set(kit.LIBRARIES) == names - {"psycopg"}


@pytest.mark.parametrize("status", ["failed", "none"])
def test_without_clean_tables_there_is_no_clean_folder(inputs, tmp_path, status):
    inputs.clean_status = status
    inputs.clean_error = "KeyError: 'age'" if status == "failed" else None
    inputs.clean_log = "Traceback ..." if status == "failed" else None

    folder = kit.write_folder(tmp_path / "out", inputs)

    assert not (folder / "clean").exists()
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["clean"]["status"] == status and manifest["clean"]["tables"] == []
    assert (folder / "logs" / "clean.txt").exists() == (status == "failed")


def test_parquet_files_are_stored_and_text_is_deflated(inputs, tmp_path):
    folder = kit.write_folder(tmp_path / "out", inputs)
    target = tmp_path / "sample.zip"

    size = kit.zip_folder(folder, target)

    assert size == target.stat().st_size
    with zipfile.ZipFile(target) as archive:
        kinds = {info.filename: info.compress_type for info in archive.infolist() if not info.is_dir()}
        assert {name.split("/")[0] for name in kinds} == {"tamiza-sample-20261020-140311"}  # one top-level folder
        assert kinds["tamiza-sample-20261020-140311/raw/main.parquet"] == zipfile.ZIP_STORED
        assert kinds["tamiza-sample-20261020-140311/manifest.json"] == zipfile.ZIP_DEFLATED
        assert archive.testzip() is None


def test_a_project_without_reference_tables_still_has_the_empty_reference_folder_in_the_zip(inputs, tmp_path):
    inputs.reference = []
    folder = kit.write_folder(tmp_path / "out", inputs)
    target = tmp_path / "sample.zip"

    kit.zip_folder(folder, target)

    with zipfile.ZipFile(target) as archive:
        names = set(archive.namelist())
        assert "tamiza-sample-20261020-140311/reference/" in names
        assert not [
            name
            for name in names
            if name.startswith("tamiza-sample-20261020-140311/reference/") and name.endswith(".parquet")
        ]
        archive.extractall(tmp_path / "unzipped")
    assert (tmp_path / "unzipped" / "tamiza-sample-20261020-140311" / "reference").is_dir()
    assert list(tz.load_context(tmp_path / "unzipped" / "tamiza-sample-20261020-140311").reference) == []


def test_what_the_worker_writes_is_what_the_sdk_reads(inputs, tmp_path):
    folder = kit.write_folder(tmp_path / "out", inputs)

    raw = tz.load_tables("raw", folder)
    clean = tz.load_tables("clean", folder)
    ctx = tz.load_context(folder)

    assert sorted(raw) == ["main", "members"] and len(raw["main"]) == 4 and len(clean["main"]) == 3
    assert ctx.project_name == "Water survey 2026"
    assert list(ctx.reference) == ["population"]


# The starter notebook and the README.

DRIVER = '''
import importlib.abc
import json
import os
import sys


class Block(importlib.abc.MetaPathFinder):
    """The server's packages and the database are not on an analyst's computer."""

    def find_spec(self, name, path, target=None):
        if name.split(".")[0] in {"tamiza", "psycopg", "duckdb"}:
            raise ImportError(f"{name} is not available here")
        return None


sys.meta_path.insert(0, Block())
sys.path.insert(0, os.getcwd())  # the folder first: the SDK included in the download is the one that runs
namespace = {"__name__": "__main__"}
for index, source in enumerate(json.load(open(sys.argv[1], encoding="utf-8")), 1):
    exec(compile(source, f"<cell {index}>", "exec"), namespace)
import tamiza_sdk

assert os.path.dirname(os.path.dirname(os.path.realpath(tamiza_sdk.__file__))) == os.path.realpath(os.getcwd())
print(repr(namespace["result"]))
'''


def run_notebook(folder: Path, work: Path) -> subprocess.CompletedProcess:
    notebook = json.loads((folder / "getting-started.ipynb").read_text(encoding="utf-8"))
    cells = ["".join(cell["source"]) for cell in notebook["cells"] if cell["cell_type"] == "code"]
    (work / "cells.json").write_text(json.dumps(cells), encoding="utf-8")
    (work / "driver.py").write_text(DRIVER, encoding="utf-8")
    return subprocess.run(
        [sys.executable, "-I", str(work / "driver.py"), str(work / "cells.json")],
        cwd=folder,
        capture_output=True,
        text=True,
        check=False,
    )


@pytest.mark.parametrize("status", ["succeeded", "failed", "none"])
def test_the_starter_notebook_runs_offline_with_the_included_sdk(inputs, tmp_path, status):
    inputs.clean_status = status
    inputs.clean_error = "KeyError: 'age'" if status == "failed" else None
    folder = kit.write_folder(tmp_path / "out", inputs)

    result = run_notebook(folder, tmp_path)

    assert result.returncode == 0, result.stdout + result.stderr
    assert "Check passed for indicator 'households_by_group'" in result.stdout
    assert "measures: value (default: value)" in result.stdout
    assert (folder / "households_by_group.py").is_file()
    if status == "failed":
        assert "Script 1 (version 2) failed" in result.stdout  # the notebook explained it and went on


def test_the_notebook_and_readme_are_filled_in_and_the_notebook_is_valid_json(inputs, tmp_path):
    inputs.project_name = r'Water "survey" \ 2026'
    folder = kit.write_folder(tmp_path / "out", inputs)

    notebook = json.loads((folder / "getting-started.ipynb").read_text(encoding="utf-8"))
    readme = (folder / "README.md").read_text(encoding="utf-8")

    assert notebook["nbformat"] == 4 and all({"cell_type", "source", "metadata"} <= set(c) for c in notebook["cells"])
    assert notebook["cells"][0]["source"][0] == r'# Water "survey" \ 2026: a sample to work on your computer' + "\n"
    assert "320 submissions" in "".join(notebook["cells"][0]["source"]) and "2026-10-20 14:03 UTC" in readme
    assert "{{" not in readme and "{{" not in json.dumps(notebook)
    assert "pip install -r requirements.txt jupyterlab" in readme
    assert "Script 1 (version 2) ran on the sample" in readme


def test_the_readme_says_when_script_1_failed(inputs, tmp_path):
    inputs.clean_status, inputs.clean_error = "failed", "KeyError: 'age'"

    readme = (kit.write_folder(tmp_path / "out", inputs) / "README.md").read_text(encoding="utf-8")

    assert "Script 1 (version 2) failed on this sample" in readme and "KeyError: 'age'" in readme
