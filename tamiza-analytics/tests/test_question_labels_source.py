"""Question labels read from the deployed form version's stored definition."""

import pytest

from tamiza.question_labels import question_labels

FIELDS = [
    {"name": "has_water", "xpath": "has_water", "type": "select_one_yes_no", "label": "Does the household have water?"},
    {"name": "age", "xpath": "spouse/age", "type": "integer", "label": "Age of the spouse"},
    {"name": "start", "xpath": "start", "type": "start", "label": None},
    {"name": "notes", "xpath": "notes", "type": "text", "label": ""},
    {
        "name": "water_source",
        "xpath": "water/water_source",
        "type": "select_one_sources",
        "label": "Main water source",
        "orOther": True,
    },
    {"name": "level", "xpath": "level", "type": "select_one_levels", "label": None, "orOther": True},
    {"name": "no_path", "type": "text", "label": "Ignored"},
]


def test_an_available_version_gives_its_labels():
    labels = question_labels("vB", "available", FIELDS)

    assert labels.applied and labels.version == "vB" and labels.unavailable is None
    assert labels.label_of("has_water") == "Does the household have water?"
    assert labels.label_of("spouse/age") == "Age of the spouse"
    assert labels.log_line() == "Question labels from form version vB."


def test_questions_without_a_label_are_deployed_but_not_labeled():
    labels = question_labels("vB", "available", FIELDS)

    assert labels.label_of("start") is None and labels.label_of("notes") is None
    assert labels.in_deployed_version("start") and labels.in_deployed_version("notes")
    assert not labels.in_deployed_version("old_question")


def test_the_text_of_an_or_other_option_takes_its_questions_label():
    labels = question_labels("vB", "available", FIELDS)

    assert labels.label_of("water/water_source_other") == "Main water source: Specify other"
    assert labels.in_deployed_version("water/water_source_other")


def test_the_text_of_an_unlabeled_or_other_question_is_deployed_without_a_label():
    labels = question_labels("vB", "available", FIELDS)

    assert labels.label_of("level_other") is None
    assert labels.in_deployed_version("level_other")


def test_a_question_really_named_like_the_other_text_keeps_its_own_label():
    fields = [
        {"name": "source", "xpath": "source", "type": "select_one_s", "label": "Source", "orOther": True},
        {"name": "source_other", "xpath": "source_other", "type": "text", "label": "Which one?"},
    ]

    assert question_labels("vB", "available", fields).label_of("source_other") == "Which one?"


@pytest.mark.parametrize(
    ("status", "why"),
    [("pending", "pending"), ("failed", "failed"), ("not_found", "not found"), (None, "pending")],
)
def test_a_version_without_a_definition_gives_no_labels(status, why):
    labels = question_labels("vC", status, None)

    assert not labels.applied and labels.version == "vC"
    assert labels.by_path == {} and labels.deployed_paths == frozenset()
    assert labels.log_line() == (
        f"Question labels not applied: the definition of the deployed form version vC is not available ({why})."
    )


def test_a_project_without_a_deployed_version_gives_no_labels():
    labels = question_labels(None, None, None)

    assert not labels.applied and labels.version is None
    assert labels.log_line() == "Question labels not applied: no deployed form version is recorded."


def test_only_the_available_definition_counts_even_when_fields_are_present():
    assert not question_labels("vC", "failed", FIELDS).applied
