"""Reading a downloaded sample folder: its manifest, its format and how this computer differs from the server's."""

import json
import sys
import warnings
from dataclasses import dataclass
from importlib import metadata
from pathlib import Path

import tamiza_sdk
from tamiza_sdk.contract import ScriptError

FORMAT = 1  # the layout of a download: bumped when the folders or the manifest change incompatibly
MANIFEST = "manifest.json"
CONTEXT = "context.json"
RAW = "raw"
CLEAN = "clean"
REFERENCE = "reference"


class EnvironmentWarning(UserWarning):
    """This computer's Python or libraries differ from the server's that made the download."""


@dataclass(frozen=True)
class Bundle:
    folder: Path
    manifest: dict

    @property
    def clean_status(self) -> str:
        """``succeeded``, ``failed`` (script 1 raised on the sample) or ``none`` (the project has no script 1)."""
        return (self.manifest.get("clean") or {}).get("status", "none")

    @property
    def clean_error(self) -> str | None:
        return (self.manifest.get("clean") or {}).get("error")

    @property
    def clean_version(self) -> int | None:
        return (self.manifest.get("clean") or {}).get("version")


_opened: dict[Path, Bundle] = {}
_warned: set[str] = set()  # the differences already reported in this session


def open_bundle(folder: str | Path = ".") -> Bundle:
    """
    The download in ``folder``. Reads and checks the manifest once per folder and session: an unknown format is
    refused, and each difference from the server's environment is warned about once.
    """
    path = Path(folder).resolve()
    if path in _opened:
        return _opened[path]
    manifest_path = path / MANIFEST
    if not manifest_path.is_file():
        raise ScriptError(
            f"{path} is not a Tamiza sample download: it has no {MANIFEST}. Unzip the download and pass its folder."
        )
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except ValueError as error:
        raise ScriptError(f"The {MANIFEST} of {path} cannot be read: {error}.") from error
    version = manifest.get("format")
    if not isinstance(version, int) or version > FORMAT:
        raise ScriptError(
            f"This download has format {version!r}, which tamiza_sdk {tamiza_sdk.__version__} does not read. "
            "Use the tamiza_sdk folder included in that download instead of an installed copy."
        )
    bundle = Bundle(path, manifest)
    _warn_about_differences(manifest)
    _opened[path] = bundle
    return bundle


def _warn_about_differences(manifest: dict) -> None:
    local_python = f"{sys.version_info.major}.{sys.version_info.minor}"
    server_python = str(manifest.get("python", ""))
    if server_python and ".".join(server_python.split(".")[:2]) != local_python:
        _warn(
            f"Python {local_python} is running here, but this download was made with Python {server_python}; "
            "results may differ."
        )
    for library, server_version in (manifest.get("libraries") or {}).items():
        try:
            local_version = metadata.version(library)
        except metadata.PackageNotFoundError:
            continue  # reported by Python itself when a script imports it
        if local_version != server_version:
            _warn(
                f"{library} {local_version} is installed here, but this download was made with "
                f"{library} {server_version}; results may differ. Install the versions in requirements.txt."
            )
    server_sdk = manifest.get("tamiza")
    if server_sdk and server_sdk != tamiza_sdk.__version__:
        _warn(
            f"tamiza_sdk {tamiza_sdk.__version__} is running here, but this download was made by Tamiza "
            f"{server_sdk}; use the tamiza_sdk folder included in the download."
        )


def _warn(message: str) -> None:
    if message not in _warned:
        _warned.add(message)
        warnings.warn(message, EnvironmentWarning, stacklevel=4)
