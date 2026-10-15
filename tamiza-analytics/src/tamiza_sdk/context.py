"""What a script receives besides its data: ``ctx``. Built the same way by the worker and by the local SDK."""

import logging
import sys
from collections.abc import Callable, Mapping
from pathlib import Path

import pandas as pd

from tamiza_sdk import tables


class Form:
    """
    The form dictionary of the project's current form: ``questions`` and ``choices`` as DataFrames, ``label`` to
    translate a code and ``name`` to find the code a label stands for. ``columns`` lists the columns of the
    flattened tables that hold answers, with the path of their question and its label in the deployed form version,
    and ``column_label`` looks one up.
    """

    def __init__(
        self,
        questions: list[dict],
        choices: list[dict],
        label_codes: Mapping[str, Mapping[str, str]] | None = None,
        columns: list[dict] | None = None,
    ):
        self.questions = pd.DataFrame(questions, columns=["name", "path", "type", "label", "list_name"])
        self.choices = pd.DataFrame(choices, columns=["list_name", "name", "label"])
        self._lists = {row["name"]: row["list_name"] for row in questions if row.get("list_name")}
        self._lists.update({row["path"]: row["list_name"] for row in questions if row.get("list_name")})
        self._labels = {(row["list_name"], row["name"]): row["label"] for row in choices}
        self.columns = pd.DataFrame(columns or [], columns=["table", "column", "path", "label"])
        self._column_labels = {(row["table"], row["column"]): row["label"] for row in columns or [] if row.get("label")}
        self._label_codes = label_codes or {}
        # A question is asked for by its path, or by its name: the current form's names first, then the last segment of
        # the paths of questions only earlier versions had.
        self._paths = {row["name"]: row["path"] for row in questions if row.get("path")}
        for path in self._label_codes:
            self._paths.setdefault(path.rsplit("/", 1)[-1], path)

    def label(self, question: str, value: object) -> str | None:
        """The label of ``value`` in the choice list of ``question`` (its name or path), or None if unknown."""
        list_name = self._lists.get(question)
        return None if list_name is None else self._labels.get((list_name, str(value)))

    def column_label(self, table: str, column: str) -> str | None:
        """
        The label of the question whose answers are in ``column`` of the flattened table ``table``, from the deployed
        form version, or None: for a system column, a column whose question the deployed version does not have, a
        question without a label, a column of a table a script built, or when the labels were not available.
        """
        return self._column_labels.get((table, column))

    def name(self, question: str, label: object) -> str | None:
        """
        The code (choice name) that ``label`` stands for in ``question`` (its name or path), or None. It is searched in
        the current form, then in earlier versions from the newest to the oldest, so a label an older version used
        still resolves after a later one relabeled the choice. A label that two choices of a version share is
        ambiguous in it and is skipped. The "or other" option is labeled ``Other`` and resolves to ``other``.
        """
        codes = self._label_codes.get(question)
        if codes is None:
            codes = self._label_codes.get(self._paths.get(question, ""))
        return None if codes is None else codes.get(str(label))


class ReferenceTableNotFound(KeyError):
    """Raised for a reference table the project does not have; the message lists the ones it has."""

    def __init__(self, name: object, available: list[str]):
        super().__init__(name)
        self.name = name
        self.available = available

    def __str__(self) -> str:
        if not self.available:
            return f"There is no reference table {self.name!r}: the project has no reference tables."
        return f"There is no reference table {self.name!r}. Available: {', '.join(self.available)}."


class ReferenceTables(Mapping):
    """
    The project's reference tables by name (``ctx.reference["population"]``). Each table is read from its Parquet file
    the first time a script asks for it, so scripts pay only for the tables they use.
    """

    def __init__(self, directory: Path):
        files = sorted(directory.glob("*.parquet")) if directory.is_dir() else []
        self._paths = {path.stem: path for path in files}
        self._frames: dict[str, pd.DataFrame] = {}

    def __getitem__(self, name: str) -> pd.DataFrame:
        if name not in self._paths:
            raise ReferenceTableNotFound(name, list(self._paths))
        if name not in self._frames:
            self._frames[name] = tables.read_parquet(self._paths[name])
        return self._frames[name]

    def __contains__(self, name: object) -> bool:
        return name in self._paths

    def __iter__(self):
        return iter(self._paths)

    def __len__(self) -> int:
        return len(self._paths)

    def __repr__(self) -> str:
        return f"<reference tables: {', '.join(self._paths) or 'none'}>"


class Context:
    """What a script receives besides its data."""

    def __init__(
        self,
        project_name: str,
        form: Form,
        log: logging.Logger,
        reference: ReferenceTables | None = None,
        show: Callable[[object, str | None], None] | None = None,
    ):
        self.project_name = project_name
        self.form = form
        self.log = log
        self.reference = reference if reference is not None else ReferenceTables(Path("/nonexistent"))
        self._show = show

    def show(self, data: object, name: str | None = None):
        """
        Shows a DataFrame, a Series or any other value in the sample test's result, as it is at this call, and returns
        it unchanged so it can be used inside an expression. In a run it does nothing.
        """
        if self._show is not None:
            self._show(data, name)
        return data


def build_context(
    data: Mapping, reference_dir: Path, show: Callable[[object, str | None], None] | None = None
) -> Context:
    """
    The ``ctx`` for the contents of a ``context.json`` and a folder of reference Parquet files. ``ctx.log`` writes to
    standard output as ``LEVEL message``, the format of a run's log.
    """
    log = logging.getLogger("tamiza.script")
    log.handlers.clear()
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(logging.Formatter("%(levelname)s %(message)s"))
    log.addHandler(handler)
    log.setLevel(logging.INFO)
    log.propagate = False
    form = Form(data.get("questions", []), data.get("choices", []), data.get("label_codes"), data.get("columns"))
    return Context(data.get("project_name", ""), form, log, ReferenceTables(reference_dir), show)
