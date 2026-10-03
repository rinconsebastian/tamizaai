import datetime as dt

import pytest

from tamiza.convert import SQL_TYPES, Unreadable, convert, kind_of


@pytest.mark.parametrize(
    ("question_type", "kind"),
    [
        ("integer", "integer"),
        ("decimal", "decimal"),
        ("range", "decimal"),
        ("date", "date"),
        ("today", "date"),
        ("start", "timestamp"),
        ("end", "timestamp"),
        ("dateTime", "timestamp"),
        ("select_one_municipalities", "text"),
        ("select_multiple", "text"),
        ("time", "text"),
        ("geopoint", "text"),
        ("calculate", "text"),
        (None, "text"),
    ],
)
def test_kind_of(question_type, kind):
    assert kind_of(question_type) == kind
    assert kind in SQL_TYPES


@pytest.mark.parametrize(
    ("kind", "value", "expected"),
    [
        ("integer", "34", 34),
        ("integer", " 34 ", 34),
        ("integer", "34.0", 34),
        ("integer", 7, 7),
        ("integer", 9007199254740993, 9007199254740993),
        ("integer", "9007199254740993", 9007199254740993),
        ("decimal", "1.25", 1.25),
        ("decimal", 3, 3.0),
        ("date", "2026-09-01", dt.date(2026, 9, 1)),
        ("date", "2026-09-01T00:00:00", dt.date(2026, 9, 1)),
        ("timestamp", "2026-09-01T09:40:00.000-05:00", "2026-09-01T14:40:00+00:00"),
        ("timestamp", "2026-09-01T10:00:00", "2026-09-01T10:00:00+00:00"),
        ("text", "cali palmira", "cali palmira"),
        ("text", 12, "12"),
        ("text", {"a": "ñ"}, '{"a": "ñ"}'),
    ],
)
def test_convert(kind, value, expected):
    assert convert(kind, value) == expected


@pytest.mark.parametrize("kind", ["integer", "decimal", "date", "timestamp", "text"])
@pytest.mark.parametrize("empty", [None, "", "   "])
def test_empty_answers_are_none_not_errors(kind, empty):
    assert convert(kind, empty) is None


@pytest.mark.parametrize(
    ("kind", "value"),
    [
        ("integer", "n/a"),
        ("integer", "3.5"),
        ("integer", True),
        ("decimal", "abc"),
        ("decimal", "nan"),
        ("decimal", "inf"),
        ("date", "01/09/2026"),
        ("date", 20260901),
        ("timestamp", "yesterday"),
    ],
)
def test_unreadable_values_raise(kind, value):
    with pytest.raises(Unreadable):
        convert(kind, value)
