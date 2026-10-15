"""The editor's list of ``ctx`` members (completion, hover, checks, reference) matches what the runner provides.

The list is a JSON file of the UI, read here, so a member added to or removed from the runner breaks this test instead
of leaving the editor offering a name that does not exist.
"""

import json
import logging
from pathlib import Path

import pytest

from tamiza.runner import Context, Form, ReferenceTables

MEMBERS = Path(__file__).resolve().parents[2] / "tamiza-ui/src/app/pages/projects/script-editor/ctx-members.json"


@pytest.fixture(scope="module")
def members():
    return json.loads(MEMBERS.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def objects():
    log = logging.getLogger("tamiza.test")
    reference = ReferenceTables(Path("/nonexistent"))
    form = Form([], [])
    return {"ctx": Context("p", form, log, reference), "ctx.form": form, "ctx.log": log, "ctx.reference": reference}


def public(obj):
    return {name for name in dir(obj) if not name.startswith("_")}


@pytest.mark.parametrize("path", ["ctx", "ctx.form", "ctx.reference"])
def test_the_list_names_exactly_the_public_members_of_the_runner_objects(members, objects, path):
    assert {m["name"] for m in members[path]} == public(objects[path])


def test_the_listed_log_methods_exist_on_a_logger(members, objects):
    for member in members["ctx.log"]:
        assert callable(getattr(objects["ctx.log"], member["name"])), member["name"]


@pytest.mark.parametrize("path", ["ctx", "ctx.form", "ctx.log", "ctx.reference"])
def test_the_kind_says_whether_a_member_is_called(members, objects, path):
    for member in members[path]:
        assert callable(getattr(objects[path], member["name"])) == (member["kind"] == "method"), member["name"]


@pytest.mark.parametrize("path", ["ctx", "ctx.form", "ctx.log", "ctx.reference"])
def test_every_signature_starts_with_the_member_path(members, path):
    for member in members[path]:
        assert member["signature"].startswith(f"{path}.{member['name']}"), member
