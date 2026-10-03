"""Table, column and schema names for the published tables.

Every name that reaches SQL comes from here, is limited to lowercase letters, digits and underscores, and is still
quoted with ``psycopg.sql.Identifier`` when used.
"""

import hashlib
import re
import unicodedata
import uuid

MAX_LENGTH = 63  # PostgreSQL's identifier limit, in bytes; reduced names are ASCII, so also in characters.
_HASH_LENGTH = 6
_INVALID = re.compile(r"[^a-z0-9_]+")
_REPEATED_UNDERSCORES = re.compile(r"__+")
_TABLE_NAME = re.compile(r"^[a-z][a-z0-9_]{0,62}$")
_IDENTIFIER = re.compile(r"^[a-z_][a-z0-9_]{0,62}$")

STEPS = ("raw", "clean", "output", "indicators")


def reduce(name: str) -> str:
    """
    A column name made of lowercase ASCII letters, digits and underscores: accents are dropped (``años`` → ``anos``),
    other characters become underscores, a leading digit gets ``c_``, and a name over 63 characters is cut and given a
    short hash of the original so different long names stay different.
    """
    ascii_name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode("ascii").lower()
    # Trailing underscores (from "Income (COP)") are dropped; leading ones are kept for system columns like "_id".
    reduced = _REPEATED_UNDERSCORES.sub("_", _INVALID.sub("_", ascii_name)).rstrip("_")
    if reduced.strip("_") == "":
        reduced = "column"
    if reduced[0].isdigit():
        reduced = "c_" + reduced
    if len(reduced) > MAX_LENGTH:
        digest = hashlib.sha1(name.encode("utf-8")).hexdigest()[:_HASH_LENGTH]
        reduced = f"{reduced[: MAX_LENGTH - _HASH_LENGTH - 1]}_{digest}"
    return reduced


def deduplicate(names: list[str]) -> list[str]:
    """Keeps the first of equal names and numbers the rest (``age``, ``age_2``, ``age_3``), within 63 characters."""
    taken: set[str] = set()
    result = []
    for name in names:
        candidate, counter = name, 1
        while candidate in taken:
            counter += 1
            suffix = f"_{counter}"
            candidate = name[: MAX_LENGTH - len(suffix)] + suffix
        taken.add(candidate)
        result.append(candidate)
    return result


def is_valid_table_name(name: object) -> bool:
    """Table names chosen by scripts must already be valid: dashboards select them by name."""
    return isinstance(name, str) and _TABLE_NAME.fullmatch(name) is not None


def is_identifier(name: object) -> bool:
    """What the publisher accepts for any table or column name before quoting it: the shape ``reduce`` produces."""
    return isinstance(name, str) and _IDENTIFIER.fullmatch(name) is not None


def schema_name(project_id: uuid.UUID | str, step: str) -> str:
    """``p_<32 hex>_<step>``: built only from the project's UUID, never from user text."""
    if step not in STEPS:
        raise ValueError(f"Unknown step {step!r}")
    return f"p_{uuid.UUID(str(project_id)).hex}_{step}"
