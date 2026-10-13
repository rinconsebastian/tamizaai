import json
import uuid
from pathlib import Path

import psycopg.sql
import pytest

from tamiza import names

# Cases shared with the API's port of the rule (Tamiza.Api ColumnNames), so the two cannot drift apart.
SHARED = json.loads((Path(__file__).parent / "fixtures" / "column_names.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize("case", SHARED["reduce"], ids=lambda case: case["name"][:20] or "<empty>")
def test_reduce_shared_cases(case):
    assert names.reduce(case["name"]) == case["reduced"]


@pytest.mark.parametrize("case", SHARED["deduplicate"])
def test_deduplicate_shared_cases(case):
    assert names.deduplicate(case["names"]) == case["deduplicated"]


@pytest.mark.parametrize(
    ("raw", "reduced"),
    [
        ("age", "age"),
        ("Household size", "household_size"),
        ("edad_años", "edad_anos"),
        ("Ingreso (COP)", "ingreso_cop"),
        ("a--b", "a_b"),
        ("respondent/age", "respondent_age"),
        ("2026_visits", "c_2026_visits"),
        ("_id", "_id"),
        ("¿?", "column"),
        ("select", "select"),
    ],
)
def test_reduce(raw, reduced):
    assert names.reduce(raw) == reduced


def test_valid_names_are_unchanged():
    for name in ("age", "household_size", "q_01", "_parent_id"):
        assert names.reduce(name) == name


def test_long_names_are_cut_with_a_hash_that_keeps_them_distinct():
    first = "household_" + "x" * 80 + "_first"
    second = "household_" + "x" * 80 + "_second"

    a, b = names.reduce(first), names.reduce(second)

    assert len(a) == len(b) == names.MAX_LENGTH
    assert a != b
    assert a.startswith("household_xxx")


def test_multibyte_names_fit_in_63_bytes():
    reduced = names.reduce("ñ" * 100)

    assert len(reduced.encode("utf-8")) <= names.MAX_LENGTH


def test_deduplicate_numbers_repeats_within_the_limit():
    long = "a" * 63

    assert names.deduplicate(["age", "age", "name", "age"]) == ["age", "age_2", "name", "age_3"]
    assert names.deduplicate([long, long]) == [long, "a" * 61 + "_2"]


@pytest.mark.parametrize("name", ["households", "persons_2026", "a"])
def test_valid_table_names(name):
    assert names.is_valid_table_name(name)


@pytest.mark.parametrize("name", ["my table!", "Households", "2026", "_private", "", "a" * 64, 7, None])
def test_invalid_table_names(name):
    assert not names.is_valid_table_name(name)


def test_schema_names_come_from_the_project_uuid():
    project = uuid.UUID("01a0ffdc-1204-7018-8195-fe685d20c5c9")

    assert names.schema_name(project, "clean") == "p_01a0ffdc120470188195fe685d20c5c9_clean"
    assert len(names.schema_name(project, "indicators")) <= names.MAX_LENGTH
    with pytest.raises(ValueError):
        names.schema_name(project, "x; DROP SCHEMA")
    with pytest.raises(ValueError):
        names.schema_name("not-a-uuid", "raw")


def test_reserved_words_are_safe_once_quoted():
    assert psycopg.sql.Identifier("select").as_string(None) == '"select"'
