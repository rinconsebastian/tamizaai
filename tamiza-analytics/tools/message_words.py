"""Generate the masking vocabulary of the script assistant.

The assistant sends the error and log of an analyst's last test to a model provider, with every word or number that is
not a known name replaced by an ellipsis (design D5 of ``add-script-assistant``). The words that stay are those of
messages written by Python and the pinned libraries, so ``KeyError: '...'`` keeps its shape while a respondent's name
in a message does not get through. This tool collects them into ``message-words.txt``, which the API embeds.

It runs in the analytics test image (the pinned libraries must be installed) and reads sources only, importing none of
the libraries it scans::

    python tools/message_words.py [--output PATH]

The vocabulary holds the lowercase words of at least two letters of:

- the string constants passed to ``raise ...(...)`` and ``warnings.warn(...)`` in the standard library and in pandas,
  numpy, pyarrow, polars, duckdb, geopandas, shapely and openpyxl (a name that holds the message, as in
  ``msg = "..."; raise X(msg)``, is followed to its assignments in the same file);
- the names of every ``BaseException`` and ``Warning`` subclass, and the segments of their modules' dotted paths, so an
  exception named in a log line (``pandas._libs.tslibs.parsing.DateParseError``) survives;
- every string literal of the ``tamiza`` and ``tamiza_sdk`` packages;
- ``c_messages.txt``, the messages that come from C code and so are in no Python source.

The output is sorted, lowercase and has one word per line, so running the tool twice gives the same file. Regenerate
it when a pinned library changes.
"""

import argparse
import ast
import builtins
import importlib.util
import os
import re
import sysconfig
import warnings
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "tamiza-api/src/Tamiza.Api/Assistant/Prompts/message-words.txt"
C_MESSAGES = Path(__file__).with_name("c_messages.txt")

# Libraries whose messages are collected. shapely is not pinned in pyproject.toml: geopandas brings it.
LIBRARIES = ("pandas", "numpy", "pyarrow", "polars", "duckdb", "geopandas", "shapely", "openpyxl")
# Packages whose every string literal is collected.
OWN_PACKAGES = ("tamiza", "tamiza_sdk")
# Folders that hold tests, demos and tools, not the messages a script can meet.
SKIP_DIRS = frozenset({"__pycache__", "test", "tests", "idlelib", "turtledemo", "ensurepip", "site-packages"})
WARN_FUNCTIONS = frozenset({"warn", "warn_explicit"})

WORD = re.compile(r"[^\W\d_]+")


def words_of(text: str) -> set[str]:
    """The lowercase words of at least two letters of ``text``."""
    return {word for word in (match.lower() for match in WORD.findall(text)) if len(word) >= 2}


def name_words(name: str) -> set[str]:
    """The words of an identifier: a plain name (``DateParseError``) is one word, so is not split at capitals."""
    return {name.lower()} if name.isalpha() and len(name) >= 2 else words_of(name)


@dataclass
class Scan:
    """What one source file gives: the words of its strings and the exception candidates it defines."""

    words: set[str] = field(default_factory=set)
    # (dotted path segments of the module, class name, last names of its bases)
    classes: list[tuple[tuple[str, ...], str, tuple[str, ...]]] = field(default_factory=list)


def _last_name(node: ast.expr) -> str | None:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        return node.attr
    return None


def _assignments(tree: ast.AST) -> dict[str, list[ast.expr]]:
    """The values assigned to each plain name anywhere in the file, to follow a message held in a variable."""
    assigned: dict[str, list[ast.expr]] = {}
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign):
            targets, value = node.targets, node.value
        elif isinstance(node, ast.AnnAssign | ast.AugAssign) and node.value is not None:
            targets, value = [node.target], node.value
        else:
            continue
        for target in targets:
            if isinstance(target, ast.Name):
                assigned.setdefault(target.id, []).append(value)
    return assigned


def _strings(expr: ast.AST, assigned: dict[str, list[ast.expr]], depth: int = 2):
    for node in ast.walk(expr):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            yield node.value
        elif isinstance(node, ast.Name) and depth > 0:
            for value in assigned.get(node.id, ()):
                yield from _strings(value, assigned, depth - 1)


def scan_source(source: str, segments: tuple[str, ...], *, every_string: bool = False) -> Scan:
    """Scan one module. ``every_string`` takes all string constants (Tamiza's own packages), else only messages."""
    scan = Scan()
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")  # invalid escape sequences of old sources
        try:
            tree = ast.parse(source)
        except (SyntaxError, ValueError, RecursionError):
            return scan
    assigned = _assignments(tree)
    for node in ast.walk(tree):
        if every_string:
            if isinstance(node, ast.Constant) and isinstance(node.value, str):
                scan.words |= words_of(node.value)
        elif isinstance(node, ast.Raise) and node.exc is not None:
            for text in _strings(node.exc, assigned):
                scan.words |= words_of(text)
        elif isinstance(node, ast.Call) and _last_name(node.func) in WARN_FUNCTIONS:
            message = node.args[:1] + [kw.value for kw in node.keywords if kw.arg == "message"]
            for expr in message:
                for text in _strings(expr, assigned):
                    scan.words |= words_of(text)
        if isinstance(node, ast.ClassDef):
            bases = tuple(name for name in (_last_name(base) for base in node.bases) if name)
            scan.classes.append((segments, node.name, bases))
    return scan


def exception_names(classes: list[tuple[tuple[str, ...], str, tuple[str, ...]]]) -> set[str]:
    """Words of the names of exception and warning classes and of their modules' path segments.

    A class is one when a base is a built-in exception or warning, or a class already found to be one: the sources are
    read, not imported, so the bases are followed by name until nothing more is added.
    """
    builtin = {
        name for name, value in vars(builtins).items() if isinstance(value, type) and issubclass(value, BaseException)
    }
    known = set(builtin)
    found: set[int] = set()
    changed = True
    while changed:
        changed = False
        for index, (_, name, bases) in enumerate(classes):
            if index not in found and any(base in known for base in bases):
                found.add(index)
                known.add(name)
                changed = True
    words = set()
    for name in builtin:
        words |= name_words(name)
    for index in found:
        segments, name, _ = classes[index]
        words |= name_words(name)
        for segment in segments:
            words |= words_of(segment)
    return words


def source_files(root: Path, package: str | None = None):
    """The ``.py`` and ``.pyi`` files under ``root``, or under the folders of ``package`` and its stubs.

    Stub folders (``_duckdb-stubs``) sit beside the package and define the classes of compiled exceptions.
    """
    if package is None:
        starts = [root]
    else:
        starts = sorted(
            entry
            for entry in root.iterdir()
            if entry.is_dir() and entry.name.lstrip("_").removesuffix("-stubs") == package
        )
    for start in starts:
        for folder, dirs, files in os.walk(start):
            dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
            for file in sorted(files):
                if file.endswith((".py", ".pyi")) and file != "conftest.py":
                    yield Path(folder) / file, Path(folder).relative_to(root)


def _segments(relative: Path, file: str) -> tuple[str, ...]:
    stem = file.split(".")[0]
    return (*relative.parts, *(() if stem == "__init__" else (stem,)))


def read_c_messages(path: Path = C_MESSAGES) -> set[str]:
    words: set[str] = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.lstrip().startswith("#"):
            words |= words_of(line)
    return words


def _package_root(package: str) -> Path:
    spec = importlib.util.find_spec(package)
    if spec is None or not spec.submodule_search_locations:
        raise SystemExit(f"{package} is not installed: run the tool in the analytics test image")
    return Path(next(iter(spec.submodule_search_locations))).parent


def collect() -> set[str]:
    """The whole vocabulary, from the installed sources."""
    words = read_c_messages()
    classes: list[tuple[tuple[str, ...], str, tuple[str, ...]]] = []

    def read(root: Path, package: str | None, *, every_string: bool) -> None:
        for path, relative in source_files(root, package):
            source = path.read_text(encoding="utf-8", errors="replace")
            scan = scan_source(source, _segments(relative, path.name), every_string=every_string)
            words.update(scan.words)
            classes.extend(scan.classes)

    read(Path(sysconfig.get_paths()["stdlib"]), None, every_string=False)
    for package in LIBRARIES:
        read(_package_root(package), package, every_string=False)
    for package in OWN_PACKAGES:
        read(_package_root(package), package, every_string=True)
    return words | exception_names(classes)


def render(words: set[str]) -> str:
    return "".join(f"{word}\n" for word in sorted(words))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument(
        "--output", type=Path, default=OUTPUT, help="the file to write (default: the API's embedded one)"
    )
    args = parser.parse_args()
    words = collect()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(render(words).encode("utf-8"))
    print(f"{len(words)} words written to {args.output}")


if __name__ == "__main__":
    main()
