"""The files of a sample download: the data, what ``ctx`` is built from, the SDK and how to use it, in one zip.

A download is one folder, ``tamiza-sample-<UTC timestamp>/``, so two downloads unzip side by side::

    README.md  getting-started.ipynb  requirements.txt  manifest.json  context.json
    raw/<table>.parquet       the flattened sample, script 1's input
    clean/<table>.parquet     the saved script 1's result on it; absent when it failed or the project has none
    reference/<table>.parquet
    logs/flatten.txt  logs/clean.txt
    tamiza_sdk/*.py           the SDK's source, copied from the package the worker runs

It holds no credentials, server addresses or tokens: only the sample's data and what the worker already gives a script.
"""

import json
import platform
import shutil
import time
import zipfile
from dataclasses import dataclass, field
from datetime import UTC, datetime
from importlib import metadata, resources
from pathlib import Path

import tamiza
import tamiza_sdk
from tamiza.launcher import OutputTable
from tamiza.reference import ReferenceTable

FORMAT = 1  # the layout of a download; ``tamiza_sdk.bundle.FORMAT`` is the newest it reads
# The libraries scripts can use (docs/scripts.md): the dependencies of the analytics image except the database driver.
LIBRARIES = ("duckdb", "geopandas", "numpy", "openpyxl", "pandas", "polars", "pyarrow")
STORED_SUFFIXES = (".parquet",)  # already compressed: stored, not deflated again

README_TEMPLATE = "README.md"
NOTEBOOK_TEMPLATE = "getting-started.ipynb"


@dataclass
class KitInputs:
    """What the worker prepared for a download."""

    project_id: str
    project_name: str
    requested: int  # the sample size asked for
    submissions: int  # how many submissions the sample holds
    context: dict  # what ``context.json`` holds: the project name, form dictionary, label codes and column labels
    raw: list[OutputTable]
    clean_status: str  # succeeded, failed (script 1 raised on the sample) or none (the project has no script 1)
    reference: list[ReferenceTable] = field(default_factory=list)
    clean: list[OutputTable] = field(default_factory=list)
    clean_version: int | None = None
    clean_error: str | None = None
    flatten_log: str = ""
    clean_log: str | None = None
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))


def folder_name(created_at: datetime) -> str:
    return f"tamiza-sample-{created_at.astimezone(UTC):%Y%m%d-%H%M%S}"


def library_versions() -> dict[str, str]:
    return {name: metadata.version(name) for name in LIBRARIES}


def requirements(versions: dict[str, str] | None = None) -> str:
    """A pip requirements file that pins the libraries scripts can use to the versions of this server."""
    versions = versions or library_versions()
    header = (
        "# The libraries Tamiza scripts can use, at the versions of the server that made this download "
        f"(Python {platform.python_version()}).\n"
    )
    return header + "".join(f"{name}=={version}\n" for name, version in sorted(versions.items()))


def manifest(inputs: KitInputs) -> dict:
    """The manifest of design D4: what the download holds and the environment it was made in."""

    def tables(items: list[OutputTable]) -> list[dict]:
        return [{"name": t.name, "rows": t.rows} for t in items]

    return {
        "format": FORMAT,
        "tamiza": tamiza.__version__,
        "createdAt": inputs.created_at.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "project": {"id": inputs.project_id, "name": inputs.project_name},
        "sample": {"requested": inputs.requested, "submissions": inputs.submissions},
        "python": platform.python_version(),
        "libraries": library_versions(),
        "raw": {"tables": tables(inputs.raw)},
        "clean": {
            "status": inputs.clean_status,
            "version": inputs.clean_version,
            "error": inputs.clean_error,
            "tables": tables(inputs.clean) if inputs.clean_status == "succeeded" else [],
        },
        "reference": [t.summary() for t in inputs.reference],
    }


def write_folder(root: Path, inputs: KitInputs) -> Path:
    """Writes the download's folder under ``root`` and returns it."""
    folder = root / folder_name(inputs.created_at)
    folder.mkdir(parents=True)
    _copy_tables(folder / "raw", inputs.raw)
    if inputs.clean_status == "succeeded":
        _copy_tables(folder / "clean", inputs.clean)
    _copy_tables(folder / "reference", inputs.reference)

    (folder / "context.json").write_text(json.dumps(inputs.context), encoding="utf-8")
    (folder / "manifest.json").write_text(json.dumps(manifest(inputs), indent=2), encoding="utf-8")
    (folder / "requirements.txt").write_text(requirements(), encoding="utf-8")
    logs = folder / "logs"
    logs.mkdir()
    (logs / "flatten.txt").write_text(inputs.flatten_log, encoding="utf-8")
    if inputs.clean_log is not None:
        (logs / "clean.txt").write_text(inputs.clean_log, encoding="utf-8")

    sdk = folder / "tamiza_sdk"
    sdk.mkdir()
    for source in sorted(Path(tamiza_sdk.__file__).parent.glob("*.py")):
        shutil.copyfile(source, sdk / source.name)
    _write_guides(folder, inputs)
    return folder


def _copy_tables(directory: Path, items: list) -> None:
    directory.mkdir()
    for item in items:
        shutil.copyfile(item.path, directory / f"{item.name}.parquet")


def _write_guides(folder: Path, inputs: KitInputs) -> None:
    """The README and the starter notebook, from the package's templates with this download's facts filled in."""
    values = _guide_values(inputs)
    (folder / README_TEMPLATE).write_text(render(template(README_TEMPLATE), values), encoding="utf-8")
    notebook = render(template(NOTEBOOK_TEMPLATE), values, json_text=True)
    (folder / NOTEBOOK_TEMPLATE).write_text(notebook, encoding="utf-8")


def _guide_values(inputs: KitInputs) -> dict[str, str]:
    count = inputs.submissions
    version = f" (version {inputs.clean_version})" if inputs.clean_version else ""
    if inputs.clean_status == "succeeded":
        script1 = f"Script 1{version} ran on the sample: `clean/` holds its result."
    elif inputs.clean_status == "failed":
        script1 = (
            f"**Script 1{version} failed on this sample** ({inputs.clean_error or 'see logs/clean.txt'}), so there is "
            "no `clean/` folder and the notebook continues with the flattened tables. `logs/clean.txt` has the error."
        )
    else:
        script1 = "The project has no script 1, so the clean tables are the flattened ones."
    return {
        "project": inputs.project_name,
        "submissions": f"{count:,} submission{'' if count == 1 else 's'}",
        "requested": f"{inputs.requested:,}",
        "created": f"{inputs.created_at.astimezone(UTC):%Y-%m-%d %H:%M} UTC",
        "python_minor": ".".join(platform.python_version().split(".")[:2]),
        "script1": script1,
    }


def render(text: str, values: dict[str, str], json_text: bool = False) -> str:
    """Replaces each ``{{name}}``; in a JSON document (``json_text``) the values are escaped to sit inside a string."""
    for name, value in values.items():
        text = text.replace("{{" + name + "}}", json.dumps(value, ensure_ascii=False)[1:-1] if json_text else value)
    return text


def zip_folder(folder: Path, target: Path) -> int:
    """Zips ``folder`` (with the folder itself as the top level) into ``target``; returns the zip's size in bytes."""
    with zipfile.ZipFile(target, "w", allowZip64=True) as archive:
        for path in sorted(folder.rglob("*")):
            name = path.relative_to(folder.parent).as_posix()
            if path.is_dir():
                # An empty folder (a project without reference tables) is part of the layout the SDK expects.
                if not any(path.iterdir()):
                    archive.writestr(zipfile.ZipInfo(f"{name}/", date_time=time.gmtime(path.stat().st_mtime)[:6]), b"")
                continue
            compression = zipfile.ZIP_STORED if path.suffix in STORED_SUFFIXES else zipfile.ZIP_DEFLATED
            archive.write(path, name, compress_type=compression)
    return target.stat().st_size


def template(name: str) -> str:
    """The text of one of the kit's templates (the README, the notebook)."""
    return resources.files("tamiza").joinpath("kit_files", name).read_text(encoding="utf-8")
