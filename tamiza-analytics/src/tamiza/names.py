"""Table, column and schema names for the published tables.

Every name that reaches SQL comes from here, is limited to lowercase letters, digits and underscores, and is still
quoted with ``psycopg.sql.Identifier`` when used.
"""

import re
import uuid

from tamiza_sdk.names import MAX_LENGTH, is_valid_table_name, reduce

__all__ = ["MAX_LENGTH", "STEPS", "deduplicate", "is_identifier", "is_valid_table_name", "reduce", "schema_name"]

_IDENTIFIER = re.compile(r"^[a-z_][a-z0-9_]{0,62}$")

STEPS = ("raw", "clean", "output", "indicators")


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


def is_identifier(name: object) -> bool:
    """What the publisher accepts for any table or column name before quoting it: the shape ``reduce`` produces."""
    return isinstance(name, str) and _IDENTIFIER.fullmatch(name) is not None


def schema_name(project_id: uuid.UUID | str, step: str) -> str:
    """``p_<32 hex>_<step>``: built only from the project's UUID, never from user text."""
    if step not in STEPS:
        raise ValueError(f"Unknown step {step!r}")
    return f"p_{uuid.UUID(str(project_id)).hex}_{step}"
