"""Choice labels: the definitions of the form versions a project's submissions were made with.

Kobo stores the answers to select questions as choice names (the codes defined in the form). The API keeps the
definition of every form version a project uses (``tamiza.form_versions``); this module reads them and builds, for each
one, the lookup that turns an answer's codes into labels in the form's default language. The flatten step applies the
lookup of each submission's own version. Nothing here talks to Kobo.
"""

from collections.abc import Iterable, Mapping
from dataclasses import dataclass, field
from datetime import datetime
from uuid import UUID

import psycopg

OTHER_CODE = "other"
OTHER_LABEL = "Other"  # what Kobo shows for the "or other" option, in every language
_FROM_FILE = ("select_one_from_file", "select_multiple_from_file")


@dataclass(frozen=True)
class SelectLookup:
    """The choices of one select question: whether it takes several, and each code's label (empty labels left out)."""

    multiple: bool
    labels: dict[str, str]


# A form version as the flatten step uses it: the lookup of each select question, by the question's path.
Definition = dict[str, SelectLookup]


def build_definition(fields: Iterable[Mapping[str, object]], choices: Iterable[Mapping[str, object]]) -> Definition:
    """
    The lookups of one form definition (a version's, or the project's snapshot). A question qualifies when it is
    ``select_one`` or ``select_multiple``, is not read from a file, and its list has at least one choice, so lists
    defined elsewhere (external choices, choices from earlier answers) never do. Kobo's rank and score levels are not
    select questions in the form, so they never qualify either.
    """
    by_list: dict[str, dict[str, str]] = {}
    for choice in choices:
        list_name, name = choice.get("listName"), choice.get("name")
        if list_name is None or name is None:
            continue
        labels = by_list.setdefault(str(list_name), {})
        label = choice.get("label")
        if isinstance(label, str) and label != "":
            labels[str(name)] = label

    definition: Definition = {}
    for question in fields:
        question_type = str(question.get("type") or "")
        multiple = question_type.startswith("select_multiple")
        if not (multiple or question_type.startswith("select_one")) or question_type.startswith(_FROM_FILE):
            continue
        list_name = question.get("listName")
        if list_name is None or str(list_name) not in by_list:
            continue
        labels = dict(by_list[str(list_name)])
        if question.get("orOther"):
            labels.setdefault(OTHER_CODE, OTHER_LABEL)
        definition[str(question["xpath"])] = SelectLookup(multiple, labels)
    return definition


@dataclass
class Labels:
    """
    Every definition the flatten step may need: the project's ``current`` form, and the ``versions`` whose definition is
    available. ``statuses`` gives the state of the versions that have none (pending, failed or not found).
    """

    current: Definition
    versions: dict[str, Definition] = field(default_factory=dict)
    deployed_at: dict[str, datetime | None] = field(default_factory=dict)
    statuses: dict[str, str] = field(default_factory=dict)

    def for_submission(self, version: str | None) -> tuple[Definition, bool]:
        """
        The definition for a submission made with ``version``, and whether it is the version's own. A submission
        without a version, or whose version has no definition, uses the current form.
        """
        if version is not None and version in self.versions:
            return self.versions[version], True
        return self.current, False

    def why_unavailable(self, version: str) -> str:
        """Why a version has no definition, for the flatten log."""
        status = self.statuses.get(version)
        return {"pending": "pending", "failed": "failed", "not_found": "not found"}.get(status or "", "not recorded")

    def search_order(self) -> list[Definition]:
        """The current form, then the versions from the newest deployment to the oldest; undated versions go last."""
        dated = [(uid, when) for uid, when in self.deployed_at.items() if uid in self.versions and when is not None]
        undated = sorted(uid for uid in self.versions if self.deployed_at.get(uid) is None)
        ordered = [uid for uid, _ in sorted(dated, key=lambda item: (item[1], item[0]), reverse=True)] + undated
        return [self.current, *(self.versions[uid] for uid in ordered)]

    def label_codes(self) -> dict[str, dict[str, str]]:
        """
        For each select question (by path), the code each label stands for, for ``ctx.form.name``. Definitions are
        searched in :meth:`search_order`; a label that two choices of one definition share is ambiguous there and is
        skipped, so an older version can still resolve it.
        """
        codes: dict[str, dict[str, str]] = {}
        for definition in self.search_order():
            for path, lookup in definition.items():
                reverse: dict[str, str | None] = {}
                for code, label in lookup.labels.items():
                    reverse[label] = None if label in reverse else code
                known = codes.setdefault(path, {})
                for label, code in reverse.items():
                    if code is not None:
                        known.setdefault(label, code)
        return codes


def load(
    connection: psycopg.Connection,
    project_id: UUID,
    form_fields: list[Mapping[str, object]],
    form_choices: list[Mapping[str, object]],
) -> Labels:
    """
    Reads the project's form versions. ``form_fields`` and ``form_choices`` are the snapshot ``project_context`` already
    read, so the fallback and ``ctx.form`` always agree. All definitions are loaded up front: they are small next to the
    submissions, and a separate query per version could disagree with the submissions streamed later.
    """
    labels = Labels(build_definition(form_fields, form_choices))
    rows = connection.execute(
        "SELECT uid, status, fields, choices, deployed_at FROM tamiza.form_versions WHERE project_id = %s ORDER BY uid",
        (project_id,),
    ).fetchall()
    for uid, status, fields, choices, deployed_at in rows:
        if status == "available":
            labels.versions[uid] = build_definition(fields, choices)
            labels.deployed_at[uid] = deployed_at
        else:
            labels.statuses[uid] = status
    return labels
