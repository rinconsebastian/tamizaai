"""The tool that generates the script assistant's masking vocabulary, and the vocabulary file it produced.

The tool reads the installed sources of the pinned libraries, so these tests use small synthetic modules and the
committed file, never a full run (which tools/message_words.py does in a few seconds when needed).
"""

import importlib.util
import re
from pathlib import Path

import pytest

TOOLS = Path(__file__).resolve().parents[1] / "tools"
spec = importlib.util.spec_from_file_location("message_words", TOOLS / "message_words.py")
message_words = importlib.util.module_from_spec(spec)
spec.loader.exec_module(message_words)

VOCABULARY = message_words.OUTPUT


def scan(source: str, segments=("pkg", "mod"), **options):
    return message_words.scan_source(source, segments, **options)


def test_words_are_lowercase_letter_runs_of_at_least_two_letters():
    assert message_words.words_of("Could NOT convert 'x' to float64 (a_b) é") == {
        "could",
        "not",
        "convert",
        "to",
        "float",
    }


def test_the_strings_of_a_raise_and_of_a_warning_are_collected_and_other_strings_are_not():
    source = (
        "import warnings\n"
        "LABEL = 'ignored label'\n"
        "def f(x):\n"
        "    if x:\n"
        "        raise ValueError(f'bad column {x!r} in table')\n"
        "    warnings.warn('deprecated behavior', FutureWarning)\n"
        "    warn(message='keyword message')\n"
        "    return 'unrelated text'\n"
    )
    assert scan(source).words == {"bad", "column", "in", "table", "deprecated", "behavior", "keyword", "message"}


def test_a_message_held_in_a_variable_is_followed_to_its_assignment():
    source = "def f():\n    msg = 'held message'\n    raise KeyError(msg)\n"
    assert scan(source).words == {"held", "message"}


def test_a_bare_re_raise_has_no_message():
    assert scan("try:\n    pass\nexcept Exception:\n    raise\n").words == set()


def test_the_own_packages_give_every_string_literal():
    source = '"""Module text."""\nNAME = "published tables"\n'
    assert scan(source, every_string=True).words == {"module", "text", "published", "tables"}


def test_a_source_that_does_not_parse_gives_nothing():
    result = scan("def broken(:\n")
    assert result.words == set() and result.classes == []


def test_exception_and_warning_classes_are_followed_through_their_bases():
    classes = []
    for segments, source in [
        (("pandas", "_libs", "tslibs", "parsing"), "class DateParseError(ValueError): ...\n"),
        (("lib", "errors"), "class LibError(DateParseError): ...\nclass LibWarning(builtins.UserWarning): ...\n"),
        (("lib", "other"), "class NotAnException(object): ...\nclass Mapping(dict): ...\n"),
    ]:
        classes += scan(source, segments).classes
    words = message_words.exception_names(classes)
    assert {"dateparseerror", "tslibs", "parsing", "libs", "pandas", "liberror", "libwarning", "errors"} <= words
    assert {"keyerror", "valueerror", "userwarning"} <= words  # the built-in ones, always
    assert not {"notanexception", "mapping", "other"} & words


def test_the_hand_kept_messages_ignore_comments_and_blank_lines(tmp_path):
    path = tmp_path / "c.txt"
    path.write_text("# a comment\n\ncould not convert string to float\n  # indented comment\n", encoding="utf-8")
    assert message_words.read_c_messages(path) == {"could", "not", "convert", "string", "to", "float"}


def test_the_hand_kept_file_holds_what_c_code_prints():
    words = message_words.read_c_messages()
    assert {"could", "convert", "float", "literal", "unable", "parse", "attribute", "defined"} <= words


def test_the_output_is_sorted_lowercase_one_word_per_line():
    assert message_words.render({"zeta", "alpha", "beta"}) == "alpha\nbeta\nzeta\n"


def test_the_source_files_skip_tests_and_pycache(tmp_path):
    for name in ("pkg/a.py", "pkg/b.pyi", "pkg/tests/c.py", "pkg/__pycache__/d.py", "pkg/conftest.py", "pkg/e.txt"):
        (tmp_path / name).parent.mkdir(parents=True, exist_ok=True)
        (tmp_path / name).write_text("", encoding="utf-8")
    (tmp_path / "pkg-stubs").mkdir()
    (tmp_path / "pkg-stubs/f.pyi").write_text("", encoding="utf-8")
    (tmp_path / "other").mkdir()
    (tmp_path / "other/g.py").write_text("", encoding="utf-8")
    found = [path.relative_to(tmp_path).as_posix() for path, _ in message_words.source_files(tmp_path, "pkg")]
    assert found == ["pkg/a.py", "pkg/b.pyi", "pkg-stubs/f.pyi"]


@pytest.fixture(scope="module")
def lines():
    if not VOCABULARY.exists():
        pytest.skip("the API folder is not in this checkout")
    return VOCABULARY.read_bytes().decode("utf-8").splitlines()


def test_the_committed_file_is_sorted_lowercase_one_word_per_line_with_a_final_newline(lines):
    assert VOCABULARY.read_bytes().endswith(b"\n") and b"\r" not in VOCABULARY.read_bytes()
    words = lines
    assert words == sorted(set(words))
    assert all(re.fullmatch(r"[^\W\d_]{2,}", word) and word == word.lower() for word in words)


@pytest.mark.parametrize(
    "word",
    [
        "could", "convert", "float", "literal", "unable", "parse", "published", "column",
        "keyerror", "valueerror", "dateparseerror", "tslibs",
    ],
)  # fmt: skip
def test_the_committed_file_keeps_message_words_and_exception_names(lines, word):
    assert word in lines


@pytest.mark.parametrize("word", ["juan", "perez", "gomez", "maria", "ana"])
def test_the_committed_file_holds_no_respondent_names(lines, word):
    assert word not in lines
