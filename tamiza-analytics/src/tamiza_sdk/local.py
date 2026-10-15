"""Loading a downloaded sample: the ``ctx`` a script receives and the tables it is called with."""

import json
from pathlib import Path

import pandas as pd

from tamiza_sdk import bundle, tables
from tamiza_sdk.context import Context, build_context
from tamiza_sdk.contract import ScriptError
from tamiza_sdk.notebook import NotebookShow

SOURCES = (bundle.RAW, bundle.CLEAN)


def load_context(folder: str | Path = ".", *, show: bool = True) -> Context:
    """
    The ``ctx`` a script receives on the server, built from the download in ``folder``: the project name, the form
    dictionary with its lookups, the reference tables and ``ctx.log``. ``ctx.show`` displays the value in the notebook
    (or prints it) and returns it unchanged; with ``show=False`` it does nothing, as in a run.
    """
    opened = bundle.open_bundle(folder)
    data = json.loads((opened.folder / bundle.CONTEXT).read_text(encoding="utf-8"))
    return build_context(data, opened.folder / bundle.REFERENCE, NotebookShow() if show else None)


def load_tables(source: str, folder: str | Path = ".") -> dict[str, pd.DataFrame]:
    """
    The tables of ``source`` in the download as a dictionary of DataFrames, read the way the server reads them, so the
    column types are the ones a script receives. ``"raw"`` is the flattened sample, script 1's input; ``"clean"`` is
    what script 1 returned on it, the input of script 2 and of the indicators. Each call reads the files again, so a
    script that changes its tables in place does not change what you load next.
    """
    if source not in SOURCES:
        raise ScriptError(
            f"Unknown source {source!r}: use 'raw' (script 1's input) or 'clean' (the input of the rest)."
        )
    opened = bundle.open_bundle(folder)
    directory = opened.folder / source
    if source == bundle.CLEAN:
        if opened.clean_status == "failed":
            version = f" (version {opened.clean_version})" if opened.clean_version else ""
            raise ScriptError(
                f"Script 1{version} failed on this sample: {opened.clean_error or 'see logs/clean.txt'}. "
                "There are no clean tables; load_tables('raw') gives the input script 1 receives, to debug it."
            )
        if opened.clean_status == "none":
            directory = opened.folder / bundle.RAW  # without script 1 the clean tables are the flattened ones
    if not directory.is_dir():
        raise ScriptError(f"The download has no {directory.name} folder.")
    return {path.stem: tables.read_parquet(path) for path in sorted(directory.glob("*.parquet"))}
