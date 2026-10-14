"""Question labels: the text the deployed form version gives each question, for the columns of the flattened tables.

Unlike choice labels (``tamiza.labels``, which follow each submission's form version), a column has one label for
all its rows, so it comes from one definition: the stored definition of the form's deployed version
(``tamiza.form_versions`` for ``projects.form_version``). The project's snapshot is not used for this: after a failed
fetch it holds the form's latest content, which can include undeployed edits. Nothing here talks to Kobo.
"""

from collections.abc import Iterable, Mapping
from dataclasses import dataclass, field

OTHER_SUFFIX = "_other"  # what Kobo adds to a question's name for the text of its "or other" option
OTHER_TEXT = "Specify other"

_STATUS_TEXT = {"pending": "pending", "failed": "failed", "not_found": "not found"}


@dataclass(frozen=True)
class QuestionLabels:
    """
    What the flatten step needs to label columns. ``by_path`` maps a path to the label of its column: a question's label
    in the default language (questions without one are left out), and for the text column of an "or other" question
    its question's label followed by ``: Specify other``. ``deployed_paths`` holds every path the deployed version
    has, including those text columns. ``unavailable`` is why no labels were read, or None.
    """

    version: str | None = None
    by_path: Mapping[str, str] = field(default_factory=dict)
    deployed_paths: frozenset[str] = frozenset()
    unavailable: str | None = None

    @property
    def applied(self) -> bool:
        return self.unavailable is None

    def label_of(self, path: str) -> str | None:
        """The label of the column that holds the answers at ``path``, or None."""
        return self.by_path.get(path)

    def in_deployed_version(self, path: str) -> bool:
        return path in self.deployed_paths

    def log_line(self) -> str:
        """The line the flatten log opens its question-label section with."""
        if self.unavailable is not None:
            return f"Question labels not applied: {self.unavailable}."
        return f"Question labels from form version {self.version}."


def question_labels(
    form_version: str | None, status: str | None, fields: Iterable[Mapping[str, object]] | None
) -> QuestionLabels:
    """
    The labels of the project's deployed version. ``form_version`` is ``projects.form_version``; ``status`` and
    ``fields`` are those of its ``form_versions`` row (None for both when the row does not exist, which reads as
    pending).
    """
    if form_version is None:
        return QuestionLabels(unavailable="no deployed form version is recorded")
    if status != "available":
        why = _STATUS_TEXT.get(status or "pending", "pending")
        return QuestionLabels(
            form_version,
            unavailable=f"the definition of the deployed form version {form_version} is not available ({why})",
        )

    by_path: dict[str, str] = {}
    deployed: set[str] = set()
    other_text: dict[str, str] = {}
    for question in fields or ():
        path = question.get("xpath")
        if not isinstance(path, str):
            continue
        deployed.add(path)
        label = question.get("label")
        has_label = isinstance(label, str) and label != ""
        if has_label:
            by_path[path] = label  # type: ignore[assignment]
        if question.get("orOther"):
            deployed.add(path + OTHER_SUFFIX)
            if has_label:
                other_text[path + OTHER_SUFFIX] = f"{label}: {OTHER_TEXT}"
    for path, text in other_text.items():
        by_path.setdefault(path, text)  # a question really named like that keeps its own label
    return QuestionLabels(form_version, by_path, frozenset(deployed))
