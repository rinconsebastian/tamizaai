"""Converts Kobo answers (mostly strings in the JSON) to the type of their question."""

import datetime as dt
import json
import math

# Question type (as stored in the form snapshot, lowercase) to column kind. Everything else is text.
_KIND_BY_TYPE = {
    "integer": "integer",
    "decimal": "decimal",
    "range": "decimal",
    "date": "date",
    "today": "date",
    "start": "timestamp",
    "end": "timestamp",
    "datetime": "timestamp",
}

SQL_TYPES = {"integer": "BIGINT", "decimal": "DOUBLE", "date": "DATE", "timestamp": "TIMESTAMPTZ", "text": "VARCHAR"}


def kind_of(question_type: str | None) -> str:
    return _KIND_BY_TYPE.get((question_type or "").lower(), "text")


class Unreadable(ValueError):
    """The answer cannot be read as its question's type; it is stored as empty and counted."""


def convert(kind: str, value: object) -> object:
    """
    The answer as a Python value of its kind. ``None`` and empty strings are empty answers, not errors. Timestamps are
    returned as ISO strings in UTC, which the loader casts to ``TIMESTAMPTZ``.
    """
    if value is None or (isinstance(value, str) and value.strip() == ""):
        return None
    match kind:
        case "integer":
            return _integer(value)
        case "decimal":
            return _decimal(value)
        case "date":
            return _date(value)
        case "timestamp":
            return _timestamp(value)
        case _:
            return _text(value)


def _integer(value: object) -> int:
    if isinstance(value, bool):
        raise Unreadable(value)
    if isinstance(value, int):
        return value
    if isinstance(value, str):
        try:
            return int(value.strip())  # exact for integers beyond a float's precision
        except ValueError:
            pass
    number = _decimal(value)
    if not number.is_integer():
        raise Unreadable(value)
    return int(number)


def _decimal(value: object) -> float:
    if isinstance(value, bool):
        raise Unreadable(value)
    try:
        number = float(value.strip() if isinstance(value, str) else value)  # type: ignore[arg-type]
    except (TypeError, ValueError) as error:
        raise Unreadable(value) from error
    if not math.isfinite(number):
        raise Unreadable(value)
    return number


def _date(value: object) -> dt.date:
    if not isinstance(value, str):
        raise Unreadable(value)
    try:
        return dt.date.fromisoformat(value.strip()[:10])
    except ValueError as error:
        raise Unreadable(value) from error


def _timestamp(value: object) -> str:
    if not isinstance(value, str):
        raise Unreadable(value)
    try:
        parsed = dt.datetime.fromisoformat(value.strip())
    except ValueError as error:
        raise Unreadable(value) from error
    # Kobo's start and end carry the device's offset; a time without one is taken as UTC, like _submission_time.
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=dt.UTC)
    return parsed.astimezone(dt.UTC).isoformat()


def _text(value: object) -> str:
    if isinstance(value, str):
        return value
    if isinstance(value, dict | list):
        return json.dumps(value, ensure_ascii=False)
    return str(value)
