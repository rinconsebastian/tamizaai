import datetime as dt

import pandas as pd
import pytest

from tamiza import interchange
from tamiza.flatten import flatten
from tamiza.labels import Labels, build_definition

FORM = [
    {"name": "start", "xpath": "start", "type": "start"},
    {
        "name": "municipality",
        "xpath": "respondent/municipality",
        "type": "select_one_municipalities",
        "listName": "municipalities",
    },
    {"name": "age", "xpath": "head/age", "type": "integer"},
    {"name": "age", "xpath": "spouse/age", "type": "integer"},
    {"name": "member_name", "xpath": "members/member_name", "type": "text"},
    {"name": "member_age", "xpath": "members/member_age", "type": "integer"},
    {"name": "visit_date", "xpath": "members/visits/visit_date", "type": "date"},
]


def submission(kobo_id, members=3, **answers):
    return {
        "_id": kobo_id,
        "_uuid": f"uuid-{kobo_id}",
        "_submission_time": "2026-09-01T10:00:00",
        "__version__": "v1",
        "meta/instanceID": f"uuid:uuid-{kobo_id}",
        "formhub/uuid": "x",
        "_attachments": [{"download_url": "https://kf/x.jpg"}],
        "_geolocation": [3.4, -76.5],
        "_status": "submitted_via_web",
        "start": "2026-09-01T09:40:00.000-05:00",
        "respondent/municipality": "cali",
        "head/age": "45",
        "spouse/age": "41",
        "members": [
            {
                "members/member_name": f"m{i}",
                "members/member_age": str(20 + i),
                "members/visits": [{"members/visits/visit_date": "2026-08-0" + str(v)} for v in (1, 2)],
            }
            for i in range(1, members + 1)
        ],
        **answers,
    }


@pytest.fixture
def run(tmp_path):
    def run_flatten(rows, batch_rows=4, labels=None, form=None):
        # A plain dict is paired with its own __version__; a (dict, version) pair is used as given.
        paired = [row if isinstance(row, tuple) else (row, row.get("__version__")) for row in rows]
        result = flatten(form or FORM, lambda: iter(paired), tmp_path, batch_rows=batch_rows, labels=labels)
        tables = {t.name: interchange.read_parquet(t.path) for t in result.tables}
        return result, tables

    return run_flatten


def test_main_and_repeat_tables_with_parent_keys(run):
    result, tables = run([submission(i) for i in range(1, 11)])

    assert result.input_rows == 10
    assert {t.name: t.rows for t in result.tables} == {"main": 10, "members": 30, "visits": 60}
    members, visits = tables["members"], tables["visits"]
    assert members["_row_id"].tolist() == list(range(1, 31))
    assert members.loc[members["_parent_id"] == 4, "member_name"].tolist() == ["m1", "m2", "m3"]
    assert set(members["_parent_table"]) == {"main"}
    assert members["_index"].tolist()[:3] == [1, 2, 3]
    # Nested repeat: each visit points at its members row.
    assert set(visits["_parent_table"]) == {"members"}
    assert visits.loc[visits["_parent_id"] == 5, "_index"].tolist() == [1, 2]


def test_columns_names_types_and_system_columns(run):
    _, tables = run([submission(1)])
    main = tables["main"]

    assert list(main.columns) == [
        "_id",
        "_uuid",
        "_submission_time",
        "_version",
        "_instance_id",
        "start",
        "municipality",
        "age",
        "spouse_age",
    ]
    row = main.iloc[0]
    assert row["_id"] == 1 and row["_version"] == "v1" and row["_instance_id"] == "uuid:uuid-1"
    assert row["start"] == dt.datetime(2026, 9, 1, 14, 40, tzinfo=dt.UTC)
    assert row["_submission_time"] == dt.datetime(2026, 9, 1, 10, 0, tzinfo=dt.UTC)
    assert row["age"] == 45 and row["spouse_age"] == 41
    assert pd.api.types.is_integer_dtype(main["age"])
    assert tables["visits"]["visit_date"][0] == dt.datetime(2026, 8, 1)
    assert list(tables["members"].columns) == [
        "_row_id",
        "_parent_id",
        "_parent_table",
        "_index",
        "member_name",
        "member_age",
    ]


def test_dropped_system_keys_do_not_become_columns(run):
    _, tables = run([submission(1)])

    for dropped in ("formhub_uuid", "_attachments", "_geolocation", "_status", "attachments"):
        assert dropped not in tables["main"].columns
    assert "attachments" not in tables


def test_unreadable_values_are_empty_and_logged(run):
    result, tables = run(
        [submission(1, **{"head/age": "n/a"}), submission(2, **{"head/age": "unknown"}), submission(3)]
    )

    assert tables["main"]["age"].isna().tolist() == [True, True, False]
    assert "main.age: 2 values could not be read as integer; stored as empty." in result.log


def test_answers_to_questions_no_longer_in_the_form_are_kept_as_text(run):
    _, tables = run([submission(1, **{"old_question": 12}), submission(2)])

    assert tables["main"]["old_question"].tolist()[0] == "12"
    assert list(tables["main"].columns)[-1] == "old_question"


def test_column_names_do_not_depend_on_which_submission_comes_first(run, tmp_path):
    late_spouse = submission(1)
    del late_spouse["head/age"]
    _, tables = run([late_spouse, submission(2)])

    assert tables["main"].loc[1, "age"] == 45 and tables["main"].loc[1, "spouse_age"] == 41


def test_a_repeat_with_no_answers_still_gets_its_table(run):
    result, tables = run([submission(1, members=0)])

    assert {t.name: t.rows for t in result.tables}["members"] == 0
    assert "visits" not in tables


def test_no_submissions_produce_an_empty_main_table(run):
    result, tables = run([])

    assert result.input_rows == 0
    assert list(tables) == ["main"]
    assert len(tables["main"]) == 0
    assert "_id" in tables["main"].columns


def test_the_working_database_is_removed(run, tmp_path):
    run([submission(1)])

    assert sorted(p.name for p in tmp_path.iterdir()) == ["main.parquet", "members.parquet", "visits.parquet"]


# Choice labels

LABEL_FORM = [
    {"name": "has_water", "xpath": "has_water", "type": "select_one_yes_no", "listName": "yes_no"},
    {"name": "services", "xpath": "services", "type": "select_multiple_services", "listName": "services"},
    {
        "name": "water_source",
        "xpath": "water_source",
        "type": "select_one_sources",
        "listName": "sources",
        "orOther": True,
    },
    {"name": "water_source_other", "xpath": "water_source_other", "type": "text"},
    {"name": "city", "xpath": "city", "type": "select_one_from_file_cities.csv", "listName": "cities.csv"},
    {"name": "level_1", "xpath": "ranking/level_1", "type": "rank__level"},
    {"name": "sex", "xpath": "members/sex", "type": "select_one_sex", "listName": "sex"},
    {"name": "age", "xpath": "age", "type": "integer"},
]

YES_NO = [("yes_no", "1", "Yes"), ("yes_no", "2", "No")]
SERVICES = [("services", "1", "Water"), ("services", "2", "Gas"), ("services", "3", "Electric power")]
SEX = [("sex", "f", "Female"), ("sex", "m", "Male")]
SOURCES = [("sources", "piped", "Piped"), ("sources", "well", "Well")]
EVERYTHING = [*YES_NO, *SERVICES, *SEX, *SOURCES]


def values(column):
    """A column as a list with None for the empty cells (pandas shows them as NaN)."""
    return [None if pd.isna(value) else value for value in column]


def choices(*items):
    """``("yes_no", "1", "Yes")`` triples as the API stores choices."""
    return [{"listName": list_name, "name": name, "label": label} for list_name, name, label in items]


def labeled(version="vB", **answers):
    """A submission made with ``version``, as the (data, version) pair flatten reads."""
    data = {"_id": 1, "_uuid": "u-1", "_submission_time": "2026-09-01T10:00:00", "__version__": version, **answers}
    return data, version


def labels_for(current=EVERYTHING, versions=None, **extra):
    """Labels whose current form has the given choices; ``versions`` maps a version uid to its choices."""
    return Labels(
        build_definition(LABEL_FORM, choices(*current)),
        versions={uid: build_definition(LABEL_FORM, choices(*items)) for uid, items in (versions or {}).items()},
        **extra,
    )


def test_select_answers_are_written_as_labels(run):
    _, tables = run([labeled(has_water="2")], labels=labels_for(versions={"vB": EVERYTHING}), form=LABEL_FORM)

    assert tables["main"]["has_water"].tolist() == ["No"]


def test_codes_are_kept_without_labels(run):
    _, tables = run([labeled(has_water="2", services="1 3")], form=LABEL_FORM)

    assert tables["main"]["has_water"].tolist() == ["2"]
    assert tables["main"]["services"].tolist() == ["1 3"]


def test_select_multiple_answers_are_labels_in_the_order_of_the_answer_joined_by_a_semicolon(run):
    result, tables = run(
        [labeled(services="1 3"), labeled(services="3 1 2"), labeled(services="2")],
        labels=labels_for(versions={"vB": EVERYTHING}),
        form=LABEL_FORM,
    )

    assert tables["main"]["services"].tolist() == ["Water; Electric power", "Electric power; Water; Gas", "Gas"]
    assert not any("form version" in line or "without a label" in line for line in result.log)


def test_a_relabeled_choice_takes_the_label_of_each_submissions_own_version(run):
    versions = {
        "vA": [("yes_no", "1", "Si"), ("yes_no", "2", "No")],
        "vB": [("yes_no", "1", "Sí"), ("yes_no", "2", "No")],
    }
    result, tables = run(
        [labeled("vA", has_water="1"), labeled("vB", has_water="1")],
        labels=labels_for(current=versions["vB"], versions=versions),
        form=LABEL_FORM,
    )

    assert tables["main"]["has_water"].tolist() == ["Si", "Sí"]
    assert not any("form version" in line for line in result.log)


def test_repeat_rows_use_their_submissions_version(run):
    versions = {"vA": [("sex", "f", "Femenino"), ("sex", "m", "Masculino")], "vB": SEX}
    submission_a = labeled("vA", members=[{"members/sex": "f"}, {"members/sex": "m"}, {"members/sex": "f"}])
    submission_b = labeled("vB", members=[{"members/sex": "f"}])
    _, tables = run([submission_a, submission_b], labels=labels_for(current=SEX, versions=versions), form=LABEL_FORM)

    assert tables["members"]["sex"].tolist() == ["Femenino", "Masculino", "Femenino", "Female"]


def test_a_question_removed_from_the_current_form_is_labeled_by_its_own_version(run):
    removed = [{"name": "old_question", "xpath": "old_question", "type": "select_one_old", "listName": "old"}]
    labels = Labels(
        build_definition(LABEL_FORM, choices(*EVERYTHING)),
        versions={"vA": build_definition([*LABEL_FORM, *removed], choices(*EVERYTHING, ("old", "x", "Old answer")))},
    )

    _, tables = run([labeled("vA", old_question="x")], labels=labels, form=LABEL_FORM)

    assert tables["main"]["old_question"].tolist() == ["Old answer"]


def test_the_or_other_answer_is_labeled_other_and_the_typed_text_stays_in_its_column(run):
    _, tables = run(
        [labeled(water_source="other", water_source_other="Rainwater"), labeled(water_source="piped")],
        labels=labels_for(versions={"vB": EVERYTHING}),
        form=LABEL_FORM,
    )

    assert tables["main"]["water_source"].tolist() == ["Other", "Piped"]
    assert values(tables["main"]["water_source_other"]) == ["Rainwater", None]


def test_a_version_without_a_definition_uses_the_current_form_and_reports_it(run):
    rows = [labeled("vB", has_water="1") for _ in range(15)]
    result, tables = run(rows, labels=labels_for(statuses={"vB": "pending"}), form=LABEL_FORM)

    assert set(tables["main"]["has_water"]) == {"Yes"}
    assert "Form version vB: 15 submissions used the current form (definition pending)." in result.log


@pytest.mark.parametrize(
    ("statuses", "reason"),
    [({"vB": "failed"}, "failed"), ({"vB": "not_found"}, "not found"), ({}, "not recorded")],
)
def test_the_log_says_why_a_version_has_no_definition(run, statuses, reason):
    result, _ = run([labeled("vB", has_water="1")], labels=labels_for(statuses=statuses), form=LABEL_FORM)

    assert f"Form version vB: 1 submission used the current form (definition {reason})." in result.log


def test_a_submission_without_a_version_uses_the_current_form_and_is_counted(run):
    rows = [({"_id": 1, "_uuid": "u-1", "has_water": "2"}, None), ({"_id": 2, "_uuid": "u-2", "has_water": "1"}, None)]
    result, tables = run(rows, labels=labels_for(), form=LABEL_FORM)

    assert tables["main"]["has_water"].tolist() == ["No", "Yes"]
    assert "2 submissions without a form version used the current form." in result.log


def test_a_choice_added_in_a_later_version_takes_the_current_forms_label(run):
    # Edited in Kobo with the current version vB, so the answer 3 exists only there.
    current = [*YES_NO, ("yes_no", "3", "Maybe")]
    result, tables = run(
        [labeled("vA", has_water="3"), labeled("vA", has_water="1")],
        labels=labels_for(current=current, versions={"vA": YES_NO, "vB": current}),
        form=LABEL_FORM,
    )

    assert tables["main"]["has_water"].tolist() == ["Maybe", "Yes"]
    assert "main.has_water: 1 value not in its form version; labeled from the current form." in result.log


def test_a_question_added_by_an_edit_is_labeled_from_the_current_form(run):
    older = [f for f in LABEL_FORM if f["xpath"] != "has_water"]
    labels = Labels(
        build_definition(LABEL_FORM, choices(*YES_NO)), versions={"vA": build_definition(older, choices(*YES_NO))}
    )

    result, tables = run([labeled("vA", has_water="1")], labels=labels, form=LABEL_FORM)

    assert tables["main"]["has_water"].tolist() == ["Yes"]
    assert "main.has_water: 1 value not in its form version; labeled from the current form." in result.log


def test_a_code_without_a_label_is_kept_and_counted_once_per_cell(run):
    result, tables = run(
        [labeled(has_water="9"), labeled(has_water="9"), labeled(has_water="1"), labeled(services="1 9 8")],
        labels=labels_for(versions={"vB": EVERYTHING}),
        form=LABEL_FORM,
    )

    assert values(tables["main"]["has_water"]) == ["9", "9", "Yes", None]
    assert values(tables["main"]["services"]) == [None, None, None, "Water; 9; 8"]
    assert "main.has_water: 2 values without a label; kept as codes." in result.log
    assert "main.services: 1 value without a label; kept as codes." in result.log


def test_a_choice_with_an_empty_label_counts_as_without_a_label(run):
    items = [("yes_no", "1", "Yes"), ("yes_no", "2", "")]

    result, tables = run(
        [labeled(has_water="2")], labels=labels_for(current=items, versions={"vB": items}), form=LABEL_FORM
    )

    assert tables["main"]["has_water"].tolist() == ["2"]
    assert "main.has_water: 1 value without a label; kept as codes." in result.log


def test_questions_whose_choices_live_in_a_file_or_that_are_ranks_are_untouched_and_unreported(run):
    result, tables = run(
        [labeled(city="05001", **{"ranking/level_1": "well"}, age="30")],
        labels=labels_for(versions={"vB": EVERYTHING}),
        form=LABEL_FORM,
    )

    assert tables["main"]["city"].tolist() == ["05001"]
    assert tables["main"]["level_1"].tolist() == ["well"]
    assert not any("city" in line or "level_1" in line for line in result.log)


def test_answers_that_are_not_text_pass_through(run):
    rows = [labeled(has_water=2), labeled(has_water="")]
    _, tables = run(rows, labels=labels_for(versions={"vB": EVERYTHING}), form=LABEL_FORM)

    assert values(tables["main"]["has_water"]) == ["2", None]


def test_labels_do_not_change_column_names_or_order(run):
    _, plain = run([labeled(has_water="1")], form=LABEL_FORM)
    _, translated = run([labeled(has_water="1")], labels=labels_for(versions={"vB": EVERYTHING}), form=LABEL_FORM)

    assert list(translated["main"].columns) == list(plain["main"].columns)
