import datetime as dt

from tamiza.labels import Labels, build_definition


def field(xpath, question_type, list_name=None, **extra):
    return {"name": xpath.rsplit("/", 1)[-1], "xpath": xpath, "type": question_type, "listName": list_name, **extra}


def choices(*items):
    return [{"listName": list_name, "name": name, "label": label} for list_name, name, label in items]


def test_select_questions_with_a_list_get_a_lookup():
    definition = build_definition(
        [
            field("has_water", "select_one_yes_no", "yes_no"),
            field("household/services", "select_multiple", "services"),
            field("age", "integer"),
            field("note", "text"),
        ],
        choices(("yes_no", "1", "Yes"), ("yes_no", "2", "No"), ("services", "w", "Water")),
    )

    assert set(definition) == {"has_water", "household/services"}
    assert definition["has_water"].multiple is False
    assert definition["has_water"].labels == {"1": "Yes", "2": "No"}
    assert definition["household/services"].multiple is True
    assert definition["household/services"].labels == {"w": "Water"}


def test_questions_read_from_a_file_are_excluded():
    definition = build_definition(
        [
            field("city", "select_one_from_file_cities.csv", "cities.csv"),
            field("towns", "select_multiple_from_file_towns.csv", "towns.csv"),
            field("sex", "select_one_sex", "sex"),
        ],
        choices(("cities.csv", "05001", "Medellín"), ("towns.csv", "1", "One"), ("sex", "f", "Female")),
    )

    assert set(definition) == {"sex"}


def test_a_question_whose_list_has_no_choices_is_excluded():
    definition = build_definition(
        [
            field("no_list", "select_one"),
            field("external", "select_one_external"),
            field("empty_list", "select_one_nothing", "nothing"),
            field("sex", "select_one_sex", "sex"),
        ],
        choices(("sex", "f", "Female")),
    )

    assert set(definition) == {"sex"}


def test_rank_and_score_levels_are_not_select_questions():
    definition = build_definition(
        [field("ranking/level_1", "rank__level", "levels"), field("scores/water", "score__row", "levels")],
        choices(("levels", "a", "A")),
    )

    assert definition == {}


def test_choices_with_an_empty_label_are_left_out_of_the_lookup():
    definition = build_definition(
        [field("has_water", "select_one_yes_no", "yes_no")],
        choices(("yes_no", "1", "Yes"), ("yes_no", "2", ""), ("yes_no", "3", None)),
    )

    assert definition["has_water"].labels == {"1": "Yes"}


def test_a_list_whose_choices_all_lack_labels_still_qualifies_so_the_codes_are_reported_as_kept():
    definition = build_definition([field("has_water", "select_one_yes_no", "yes_no")], choices(("yes_no", "1", None)))

    assert definition["has_water"].labels == {}


def test_the_or_other_option_adds_other_labeled_other():
    definition = build_definition(
        [
            field("source", "select_one_sources", "sources", orOther=True),
            field("sewage", "select_one_sources", "sources"),
        ],
        choices(("sources", "piped", "Piped")),
    )

    assert definition["source"].labels == {"piped": "Piped", "other": "Other"}
    assert "other" not in definition["sewage"].labels


def test_the_or_other_option_does_not_replace_a_choice_the_form_defines_as_other():
    definition = build_definition(
        [field("source", "select_one_sources", "sources", orOther=True)],
        choices(("sources", "other", "Something else")),
    )

    assert definition["source"].labels == {"other": "Something else"}


def test_a_submission_uses_its_versions_definition_or_falls_back_to_the_current_form():
    current = build_definition([field("q", "select_one_l", "l")], choices(("l", "1", "Now")))
    older = build_definition([field("q", "select_one_l", "l")], choices(("l", "1", "Then")))
    labels = Labels(current, versions={"vA": older}, statuses={"vB": "pending"})

    assert labels.for_submission("vA") == (older, True)
    assert labels.for_submission("vB") == (current, False)
    assert labels.for_submission("vUnknown") == (current, False)
    assert labels.for_submission(None) == (current, False)


def test_why_a_version_has_no_definition():
    labels = Labels({}, statuses={"vB": "pending", "vC": "failed", "vD": "not_found"})

    assert [labels.why_unavailable(v) for v in ("vB", "vC", "vD", "vE")] == [
        "pending",
        "failed",
        "not found",
        "not recorded",
    ]


def definitions(*versions):
    """Labels with the given (uid, deployed_at, choices) versions, all of one question ``q``."""
    field_q = [field("q", "select_one_l", "l")]
    current = build_definition(field_q, choices(("l", "1", "Current")))
    return Labels(
        current,
        versions={uid: build_definition(field_q, choices(*items)) for uid, _, items in versions},
        deployed_at={uid: when for uid, when, _ in versions},
    )


def day(number):
    return dt.datetime(2026, 9, number, tzinfo=dt.UTC)


def test_definitions_are_searched_from_the_current_form_to_the_oldest_version_with_undated_ones_last():
    labels = definitions(
        ("vOld", day(1), [("l", "1", "Old")]),
        ("vNew", day(9), [("l", "1", "New")]),
        ("vNoDate", None, [("l", "1", "Undated")]),
        ("vMid", day(5), [("l", "1", "Mid")]),
    )

    order = [definition["q"].labels["1"] for definition in labels.search_order()]

    assert order == ["Current", "New", "Mid", "Old", "Undated"]


def test_a_label_of_an_earlier_version_still_resolves_after_a_later_relabeling():
    labels = definitions(("vA", day(1), [("l", "1", "Si")]))
    labels = Labels(
        build_definition([field("q", "select_one_l", "l")], choices(("l", "1", "Sí"))),
        labels.versions,
        labels.deployed_at,
    )

    codes = labels.label_codes()["q"]

    assert codes == {"Sí": "1", "Si": "1"}


def test_a_label_shared_by_two_choices_of_a_definition_is_skipped_in_it_only():
    labels = definitions(("vA", day(1), [("l", "1", "Yes"), ("l", "2", "Yes"), ("l", "3", "No")]))
    labels = Labels(
        build_definition(
            [field("q", "select_one_l", "l")], choices(("l", "1", "Yes"), ("l", "2", "Yes"), ("l", "3", "No"))
        ),
        {
            "vA": labels.versions["vA"],
            "vB": build_definition([field("q", "select_one_l", "l")], choices(("l", "7", "Yes"))),
        },
        {"vA": day(1), "vB": day(5)},
    )

    codes = labels.label_codes()["q"]

    # Ambiguous in the current form and in vA, so it is only the newer vB that gives "Yes" a code.
    assert codes == {"No": "3", "Yes": "7"}


def test_a_label_no_definition_has_resolves_to_nothing_and_questions_only_old_versions_had_are_included():
    removed = [field("old_question", "select_one_o", "o")]
    labels = definitions(("vA", day(1), [("l", "1", "Old")]))
    labels.versions["vA"].update(build_definition(removed, choices(("o", "x", "Old answer"))))

    codes = labels.label_codes()

    assert "Nope" not in codes["q"]
    assert codes["old_question"] == {"Old answer": "x"}


def test_the_or_other_label_resolves_to_other():
    labels = Labels(
        build_definition(
            [field("source", "select_one_sources", "sources", orOther=True)], choices(("sources", "piped", "Piped"))
        )
    )

    assert labels.label_codes()["source"] == {"Piped": "piped", "Other": "other"}
