from helpers import add_submissions, submission


def test_the_schema_comes_from_the_api_scripts_and_seeding_works(database, project):
    add_submissions(database, project, [submission(1), submission(2)])

    assert database.scalar("SELECT count(*) FROM tamiza.raw_submissions WHERE project_id = %s", project.id) == 2
    assert database.scalar("SELECT to_regclass('tamiza.runs') IS NOT NULL") is True
    assert database.scalar("SELECT form_choices->0->>'label' FROM tamiza.projects") == "Cali"
