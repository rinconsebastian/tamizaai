"""The rules for the names of published tables and columns, shared by the worker and the local SDK."""

import hashlib
import re
import unicodedata

MAX_LENGTH = 63  # PostgreSQL's identifier limit, in bytes; reduced names are ASCII, so also in characters.
_HASH_LENGTH = 6
_INVALID = re.compile(r"[^a-z0-9_]+")
_REPEATED_UNDERSCORES = re.compile(r"__+")
_TABLE_NAME = re.compile(r"^[a-z][a-z0-9_]{0,62}$")


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


def is_valid_table_name(name: object) -> bool:
    """Table names chosen by scripts must already be valid: dashboards select them by name."""
    return isinstance(name, str) and _TABLE_NAME.fullmatch(name) is not None
