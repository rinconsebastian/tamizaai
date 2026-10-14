"""Question labels end to end against PostgreSQL: the deployed version's definition, published as column comments."""

import textwrap
import uuid

import pytest
from helpers import (
    FORM_FIELDS,
    add_form_version,
    add_script,
    add_submissions,
    column_comments,
    deploy_form_version,
    queue_run,
    submission,
)
from psycopg.types.json import Jsonb

from tamiza import pipeline, sample_runs
from tamiza.names import schema_name
from tamiza.settings import Settings

# What the deployed version vB calls the questions; the snapshot (projects.form_fields) says something else.
DEPLOYED = [
    {"name": "start", "xpath": "start", "type": "start", "label": None, "listName": None},
    {
        "name": "municipality",
        "xpath": "municipality",
        "type": "select_one",
        "label": "Municipality of residence",
        "listName": "municipalities",
    },
    {"name": "age", "xpath": "age", "type": "integer", "label": "Age of the respondent", "listName": None},
    {"name": "member_name", "xpath": "members/member_name", "type": "text", "label": "Name of the member"},
]


@pytest.fixture
def settings(database, tmp_path):
    return Settings(database_url=database.url, run_work_dir=tmp_path / "runs", run_timeout_seconds=60)


@pytest.fixture
def deployed(database, project):
    """Two submissions of the deployed version vB, whose definition is available."""
    add_submissions(
        database, project, [submission(1, "Cali", 40), submission(2, "buga", 35)], versions={1: "vB", 2: "vB"}
    )
    deploy_form_version(database, project, "vB", DEPLOYED)
    return project


def process(database, settings) -> bool:
    with database.connect() as connection:
        return pipeline.Processor(settings)(connection)


def comments(database, project, step, table="main") -> dict[str, str | None]:
    return column_comments(database, schema_name(project.id, step), table)


def flatten_log(database, run_id) -> str:
    return database.scalar("SELECT log FROM tamiza.run_steps WHERE run_id = %s AND step = 'flatten'", run_id)


def test_a_run_publishes_the_deployed_versions_question_labels_as_column_comments(database, deployed, settings):
    run_id = queue_run(database, deployed)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"
    main = comments(database, deployed, "raw")
    assert main["municipality"] == "Municipality of residence" and main["age"] == "Age of the respondent"
    assert main["start"] is None and main["_id"] is None
    assert comments(database, deployed, "raw", "members")["member_name"] == "Name of the member"
    assert "Question labels from form version vB." in flatten_log(database, run_id)
    # Cells are unchanged: choice labels still follow each submission's version.
    assert database.rows(
        f'SELECT _id, municipality, age FROM "{schema_name(deployed.id, "raw")}".main ORDER BY _id'
    ) == [
        (1, "Cali", 40),
        (2, "Buga", 35),
    ]


def test_the_clean_tables_without_script_1_carry_the_same_comments_and_script_outputs_carry_none(
    database, deployed, settings
):
    add_script(
        database,
        deployed,
        "output",
        "def prepare_output(cleaned, ctx):\n    return {'main': cleaned['main'][['municipality', 'age']]}\n",
    )
    queue_run(database, deployed)

    assert process(database, settings)

    assert comments(database, deployed, "clean") == comments(database, deployed, "raw")
    assert comments(database, deployed, "clean")["age"] == "Age of the respondent"
    assert set(comments(database, deployed, "output").values()) == {None}


def test_the_clean_tables_of_script_1_carry_no_comments(database, deployed, settings):
    add_script(database, deployed, "clean", "def clean(raw, ctx):\n    return raw\n")
    queue_run(database, deployed)

    assert process(database, settings)

    assert comments(database, deployed, "raw")["age"] == "Age of the respondent"
    assert set(comments(database, deployed, "clean").values()) == {None}


def test_a_relabeled_question_shows_only_the_deployed_versions_label(database, project, settings):
    older = [{**f, "label": "Age (old text)"} if f["name"] == "age" else f for f in DEPLOYED]
    add_submissions(database, project, [submission(1), submission(2)], versions={1: "vA", 2: "vB"})
    add_form_version(database, project, "vA", fields=older, deployed_at="2026-09-01T00:00:00Z")
    deploy_form_version(database, project, "vB", DEPLOYED)
    queue_run(database, project)

    assert process(database, settings)

    assert comments(database, project, "raw")["age"] == "Age of the respondent"


def test_a_form_redeployed_with_a_new_label_publishes_it_at_the_next_run(database, deployed, settings):
    queue_run(database, deployed)
    assert process(database, settings)
    relabeled = [{**f, "label": "Respondent's age"} if f["name"] == "age" else f for f in DEPLOYED]

    deploy_form_version(database, deployed, "vC", relabeled)
    assert comments(database, deployed, "raw")["age"] == "Age of the respondent"  # not before a run
    queue_run(database, deployed)
    assert process(database, settings)

    assert comments(database, deployed, "raw")["age"] == "Respondent's age"


def test_a_label_with_quotes_a_backslash_and_a_newline_is_stored_as_written(database, project, settings):
    awkward = 'It\'s the "age" \\ of the\nrespondent; DROP TABLE x;--'
    fields = [{**f, "label": awkward} if f["name"] == "age" else f for f in DEPLOYED]
    add_submissions(database, project, [submission(1)], versions={1: "vB"})
    deploy_form_version(database, project, "vB", fields)
    queue_run(database, project)

    assert process(database, settings)

    assert comments(database, project, "raw")["age"] == awkward


def test_questions_the_deployed_version_lacks_are_reported_and_left_without_a_label(database, deployed, settings):
    data = submission(3)
    data["old_question"] = "x"
    add_submissions(database, deployed, [data], versions={3: "vB"})
    run_id = queue_run(database, deployed)

    assert process(database, settings)

    assert comments(database, deployed, "raw")["old_question"] is None
    assert "main: 1 column is not in the deployed form version and has no question label: old_question." in flatten_log(
        database, run_id
    )


@pytest.mark.parametrize(("status", "why"), [("failed", "failed"), ("not_found", "not found"), ("pending", "pending")])
def test_an_unreadable_deployed_version_publishes_no_labels_and_the_log_says_why(
    database, project, settings, status, why
):
    add_submissions(database, project, [submission(1)], versions={1: "vC"})
    deploy_form_version(database, project, "vC", status=status)
    run_id = queue_run(database, project)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"
    assert set(comments(database, project, "raw").values()) == {None}
    log = flatten_log(database, run_id)
    assert (
        f"Question labels not applied: the definition of the deployed form version vC is not available ({why})." in log
    )
    assert "not in the deployed form version" not in log


def test_a_deployed_version_without_a_row_reads_as_pending(database, project, settings):
    add_submissions(database, project, [submission(1)])
    deploy_form_version(database, project, "vC", record=False)
    run_id = queue_run(database, project)

    assert process(database, settings)

    assert set(comments(database, project, "raw").values()) == {None}
    assert "the deployed form version vC is not available (pending)." in flatten_log(database, run_id)


def test_a_project_without_a_deployed_version_publishes_no_labels(database, project, settings):
    add_submissions(database, project, [submission(1)])
    run_id = queue_run(database, project)

    assert process(database, settings)

    assert set(comments(database, project, "raw").values()) == {None}
    assert "Question labels not applied: no deployed form version is recorded." in flatten_log(database, run_id)


def test_the_labels_come_from_the_deployed_definition_not_from_the_snapshot_of_the_latest_content(
    database, project, settings
):
    # The snapshot fell back to the latest content (a draft with other texts); the deployed version was read later.
    draft = [{**f, "label": "Draft text"} if f["name"] == "age" else f for f in FORM_FIELDS]
    database.execute("UPDATE tamiza.projects SET form_fields = %s WHERE id = %s", Jsonb(draft), project.id)
    add_submissions(database, project, [submission(1)], versions={1: "vC"})
    deploy_form_version(database, project, "vC", DEPLOYED)  # failed once, now available
    queue_run(database, project)

    assert process(database, settings)

    assert comments(database, project, "raw")["age"] == "Age of the respondent"


# Scripts


def test_ctx_form_gives_a_scripts_the_column_labels_in_a_run(database, deployed, settings):
    add_script(
        database,
        deployed,
        "output",
        textwrap.dedent(
            """
            def prepare_output(cleaned, ctx):
                assert ctx.form.column_label("main", "age") == "Age of the respondent"
                assert ctx.form.column_label("members", "member_name") == "Name of the member"
                assert ctx.form.column_label("main", "_id") is None
                columns = ctx.form.columns
                assert {"table", "column", "path", "label"} == set(columns.columns)
                assert "_id" not in set(columns["column"])
                return {"dictionary": columns[["table", "column", "path", "label"]]}
            """
        ),
    )
    run_id = queue_run(database, deployed)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded", database.rows(
        "SELECT step, error FROM tamiza.run_steps WHERE run_id = %s", run_id
    )
    rows = database.rows(
        f'SELECT "table", "column", path, label FROM "{schema_name(deployed.id, "output")}".dictionary '
        "WHERE \"column\" = 'age'"
    )
    assert rows == [("main", "age", "age", "Age of the respondent")]


def test_ctx_form_gives_no_column_label_when_the_labels_are_off_but_still_lists_the_columns(
    database, project, settings
):
    add_submissions(database, project, [submission(1)])
    add_script(
        database,
        project,
        "output",
        textwrap.dedent(
            """
            def prepare_output(cleaned, ctx):
                assert ctx.form.column_label("main", "age") is None
                columns = ctx.form.columns
                assert "age" in set(columns["column"]) and columns["label"].isna().all()
                return {"main": cleaned["main"]}
            """
        ),
    )
    run_id = queue_run(database, project)

    assert process(database, settings)

    assert database.scalar("SELECT status FROM tamiza.runs WHERE id = %s", run_id) == "succeeded"


def queue_test(database, project, kind, draft, sample_size=500) -> uuid.UUID:
    test_id = uuid.uuid4()
    database.execute(
        """
        INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at)
        VALUES (%s, %s, %s, %s, %s, %s, 'queued', now())
        """,
        test_id,
        project.id,
        project.user_id,
        kind,
        textwrap.dedent(draft),
        sample_size,
    )
    return test_id


def test_a_sample_test_previews_the_labels_of_the_flattened_tables_and_none_for_the_scripts_tables(
    database, deployed, settings
):
    test_id = queue_test(
        database,
        deployed,
        "clean",
        """
        def clean(raw, ctx):
            assert ctx.form.column_label("main", "age") == "Age of the respondent"
            return {"main": raw["main"]}
        """,
    )

    with database.connect() as connection:
        claimed = sample_runs.claim(connection)
        sample_runs.execute(connection, claimed, settings)

    status, steps = database.rows("SELECT status, steps FROM tamiza.script_tests WHERE id = %s", test_id)[0]
    assert status == "succeeded", steps
    by_step = {step["step"]: step for step in steps}
    flattened = {t["name"]: {c["name"]: c["label"] for c in t["columns"]} for t in by_step["flatten"]["tables"]}
    assert flattened["main"]["age"] == "Age of the respondent" and flattened["main"]["_id"] is None
    assert flattened["members"]["member_name"] == "Name of the member"
    assert {c["label"] for t in by_step["clean"]["tables"] for c in t["columns"]} == {None}
    assert "Question labels from form version vB." in by_step["flatten"]["log"]


def test_a_sample_test_without_labels_previews_none_and_gives_the_script_no_labels(database, project, settings):
    add_submissions(database, project, [submission(1)])
    test_id = queue_test(
        database,
        project,
        "clean",
        """
        def clean(raw, ctx):
            assert ctx.form.column_label("main", "age") is None
            return {"main": raw["main"]}
        """,
    )

    with database.connect() as connection:
        claimed = sample_runs.claim(connection)
        sample_runs.execute(connection, claimed, settings)

    status, steps = database.rows("SELECT status, steps FROM tamiza.script_tests WHERE id = %s", test_id)[0]
    assert status == "succeeded", steps
    flatten = next(step for step in steps if step["step"] == "flatten")
    assert {c["label"] for t in flatten["tables"] for c in t["columns"]} == {None}
