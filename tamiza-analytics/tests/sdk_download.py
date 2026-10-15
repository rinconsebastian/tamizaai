"""Builds a download folder as the worker writes it (design D4), for tests of the SDK that need no database."""

import json
import platform
from importlib import metadata
from pathlib import Path

import pandas as pd

import tamiza_sdk
from tamiza_sdk import tables

LIBRARIES = ("duckdb", "geopandas", "numpy", "openpyxl", "pandas", "polars", "pyarrow")

QUESTIONS = [
    {
        "name": "municipality",
        "path": "municipality",
        "type": "select_one",
        "label": "Municipality",
        "list_name": "towns",
    },
    {
        "name": "water_source",
        "path": "water_source",
        "type": "select_one",
        "label": "Water source",
        "list_name": "water",
    },
    {"name": "age", "path": "members/age", "type": "integer", "label": "Age", "list_name": None},
]
CHOICES = [
    {"list_name": "towns", "name": "cali", "label": "Cali"},
    {"list_name": "towns", "name": "buga", "label": "Buga"},
    {"list_name": "water", "name": "piped", "label": "Piped"},
    {"list_name": "water", "name": "well", "label": "Well"},
]
COLUMNS = [
    {"table": "main", "column": "municipality", "path": "municipality", "label": "Municipality"},
    {"table": "main", "column": "water_source", "path": "water_source", "label": "Water source"},
    {"table": "members", "column": "age", "path": "members/age", "label": "Age"},
]


def raw_tables() -> dict[str, pd.DataFrame]:
    return {
        "main": pd.DataFrame(
            {
                "_id": pd.array([1, 2, 3, 4], dtype="Int64"),
                "_version": ["v1", "v1", "v2", "v2"],
                "municipality": ["Cali", "Buga", "Cali", "Buga"],
                "water_source": ["Piped", "Well", "Piped", "Piped"],
                "age": pd.array([30, None, 41, 25], dtype="Int64"),
            }
        ),
        "members": pd.DataFrame(
            {
                "_row_id": pd.array([1, 2, 3], dtype="Int64"),
                "_parent_id": pd.array([1, 1, 2], dtype="Int64"),
                "age": pd.array([30, 3, None], dtype="Int64"),
            }
        ),
    }


def clean_tables() -> dict[str, pd.DataFrame]:
    """What a script 1 that drops the last submission would give."""
    raw = raw_tables()
    return {"main": raw["main"].iloc[:3].reset_index(drop=True), "members": raw["members"]}


def make_download(
    folder: Path,
    *,
    clean: str = "succeeded",
    format_version: int = 1,
    python: str | None = None,
    libraries: dict[str, str] | None = None,
    sdk_version: str | None = None,
) -> Path:
    """
    Writes a download into ``folder``. ``clean`` is ``succeeded`` (script 1 ran), ``failed`` (it raised on the sample,
    so there is no ``clean/``) or ``none`` (no script 1). Versions default to this environment's, so nothing warns.
    """
    folder.mkdir(parents=True, exist_ok=True)
    sources = {"raw": raw_tables(), "reference": {"population": _population()}}
    if clean == "succeeded":
        sources["clean"] = clean_tables()
    for source, frames in sources.items():
        (folder / source).mkdir(exist_ok=True)
        for name, frame in frames.items():
            tables.write_parquet(frame, folder / source / f"{name}.parquet")

    context = {
        "project_name": "Water survey 2026",
        "questions": QUESTIONS,
        "choices": CHOICES,
        "label_codes": {"municipality": {"Cali": "cali", "Buga": "buga"}},
        "columns": COLUMNS,
    }
    (folder / "context.json").write_text(json.dumps(context), encoding="utf-8")

    manifest = {
        "format": format_version,
        "tamiza": sdk_version or tamiza_sdk.__version__,
        "createdAt": "2026-10-20T14:03:11Z",
        "project": {"id": "00000000-0000-0000-0000-000000000001", "name": "Water survey 2026"},
        "sample": {"requested": 500, "submissions": 4},
        "python": python or platform.python_version(),
        "libraries": libraries if libraries is not None else {name: metadata.version(name) for name in LIBRARIES},
        "raw": {"tables": [{"name": name, "rows": len(frame)} for name, frame in raw_tables().items()]},
        "clean": {
            "status": clean,
            "version": None if clean == "none" else 2,
            "error": "KeyError: 'age'" if clean == "failed" else None,
            "tables": [{"name": name, "rows": len(frame)} for name, frame in clean_tables().items()]
            if clean == "succeeded"
            else [],
        },
        "reference": [{"name": "population", "rows": 2, "updatedAt": "2026-10-19T10:00:00Z"}],
    }
    (folder / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return folder


def _population() -> pd.DataFrame:
    return pd.DataFrame(
        {"municipality_code": ["cali", "buga"], "population_2026": pd.array([2_200_000, 130_000], dtype="Int64")}
    )
