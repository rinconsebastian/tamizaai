"""Every script template of the editor runs unchanged in a sample test, whole and through each of its cells.

The templates are the ``.py`` files of the UI, read from there, so a template that stops working breaks this test
instead of handing an analyst a script that fails on its first run.
"""

from pathlib import Path

import pytest
from helpers import add_script, add_submissions
from psycopg.types.json import Jsonb
from test_sample_runs import queue_test, result, run_next

from tamiza.settings import Settings

TEMPLATES = Path(__file__).resolve().parents[3] / "tamiza-ui/src/app/pages/projects/script-templates"
KINDS = ("clean", "output", "indicator")
FILES = sorted(path for kind in KINDS for path in (TEMPLATES / kind).glob("*.py"))

FIELDS = [
    {"name": "start", "xpath": "start", "type": "start", "label": None, "listName": None},
    {"name": "age", "xpath": "age", "type": "integer", "label": "Age", "listName": None},
    {"name": "town", "xpath": "town", "type": "text", "label": "Town", "listName": None},
]


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def one_table_form(database, project):
    """A form with no repeat group, so the flattened data is only the main table."""
    database.execute("UPDATE tamiza.projects SET form_fields = %s WHERE id = %s", Jsonb(FIELDS), project.id)
    rows = [
        {
            "_id": number,
            "_uuid": f"uuid-{number}",
            "_submission_time": f"2026-0{1 + number % 3}-15T10:00:00",
            "start": "2026-09-01T09:40:00.000-05:00",
            "age": str(20 + number),
            "town": ["Cali", "Buga", "Palmira"][number % 3],
        }
        for number in range(1, 13)
    ]
    add_submissions(database, project, rows)
    return project


def test_there_are_templates_for_every_kind():
    kinds = {path.parent.name for path in FILES}

    assert kinds == set(KINDS)
    assert len(FILES) >= 7
    for path in FILES:
        assert "# %%" in path.read_text(encoding="utf-8"), path.name  # every template is written in cells


def markers(source):
    return [number for number, line in enumerate(source.splitlines(), start=1) if line.lstrip().startswith("# %%")]


@pytest.mark.parametrize("path", FILES, ids=lambda path: f"{path.parent.name}/{path.stem}")
def test_a_template_runs_unchanged_on_any_form_whole_and_through_each_cell(path, database, one_table_form, settings):
    source = path.read_text(encoding="utf-8")
    kind = path.parent.name
    name = "template_test" if kind == "indicator" else None
    if kind != "clean":
        add_script(database, one_table_form, "clean", "def clean(raw, ctx):\n    return raw\n")

    def run(stop_line=None):
        test_id = queue_test(database, one_table_form, kind, source, name=name)
        if stop_line is not None:
            database.execute("UPDATE tamiza.script_tests SET stop_line = %s WHERE id = %s", stop_line, test_id)
        run_next(database, settings)
        outcome = result(database, test_id)
        assert outcome["status"] == "succeeded", (path.name, stop_line, outcome)
        return outcome["steps"][kind]

    whole = run()

    assert whole["status"] == "succeeded"
    assert whole["tables"], "a template returns a table"
    assert whole["shown"], "a template shows what it works on"
    if kind == "indicator":
        assert whole["dashboard"]["defaultMeasure"] == "value"
    # Each cell can be run on its own: the script stops at the next marker, with the previews so far.
    for stop_line in markers(source)[1:]:
        stopped = run(stop_line)
        assert (stopped["status"], stopped["stoppedAtLine"]) == ("stopped", stop_line)
