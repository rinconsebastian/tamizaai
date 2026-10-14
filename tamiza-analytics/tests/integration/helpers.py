"""Seeding helpers for the integration tests (imported by conftest and the tests)."""

import json
import uuid
from dataclasses import dataclass

import psycopg
from psycopg.types.json import Jsonb


@dataclass
class Database:
    conninfo: dict[str, str]
    name: str

    @property
    def url(self) -> str:
        """libpq keywords, as the worker receives TAMIZA_ANALYTICS_DATABASE_URL (password included for tests)."""
        return " ".join(f"{key}={value}" for key, value in {**self.conninfo, "dbname": self.name}.items())

    def connect(self) -> psycopg.Connection:
        return psycopg.connect(self.url, autocommit=True)

    def scalar(self, sql: str, *params: object) -> object:
        with self.connect() as connection:
            row = connection.execute(sql, params or None).fetchone()
            return None if row is None else row[0]

    def rows(self, sql: str, *params: object) -> list[tuple]:
        with self.connect() as connection:
            return connection.execute(sql, params or None).fetchall()

    def execute(self, sql: str, *params: object) -> None:
        with self.connect() as connection:
            connection.execute(sql, params or None)


FORM_FIELDS = [
    {"name": "start", "xpath": "start", "type": "start", "label": None, "listName": None},
    {
        "name": "municipality",
        "xpath": "municipality",
        "type": "select_one",
        "label": "Municipality",
        "listName": "municipalities",
    },
    {"name": "age", "xpath": "age", "type": "integer", "label": "Age", "listName": None},
    {"name": "member_name", "xpath": "members/member_name", "type": "text", "label": "Name", "listName": None},
]
FORM_CHOICES = [
    {"listName": "municipalities", "name": "cali", "label": "Cali"},
    {"listName": "municipalities", "name": "buga", "label": "Buga"},
]


def submission(kobo_id: int, municipality: str = "cali", age: int | str = 30, members: int = 2) -> dict:
    return {
        "_id": kobo_id,
        "_uuid": f"uuid-{kobo_id}",
        "_submission_time": "2026-09-01T10:00:00",
        "__version__": "v1",
        "start": "2026-09-01T09:40:00.000-05:00",
        "municipality": municipality,
        "age": str(age),
        "members": [{"members/member_name": f"m{i}"} for i in range(1, members + 1)],
    }


@dataclass
class Project:
    id: uuid.UUID
    user_id: uuid.UUID


def create_project(database: Database, user_id: uuid.UUID | None = None) -> Project:
    """A project with a form and no submissions yet, by a new author unless ``user_id`` is given."""
    project_id = uuid.uuid4()
    if user_id is None:
        user_id = uuid.uuid4()
        database.execute(
            "INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) "
            "VALUES (%s, 'sub', 'Ada', now(), now())",
            user_id,
        )
    database.execute(
        """
        INSERT INTO tamiza.projects (
            id, name, kobo_server_url, kobo_asset_uid, encrypted_api_token, encrypted_webhook_secret, form_name,
            form_fields, form_choices, form_checked_at, created_by, created_at, updated_at)
        VALUES (%s, 'Household survey', 'https://kf.kobotoolbox.org', 'aAsset', 'x', 'x', 'Form',
                %s, %s, now(), %s, now(), now())
        """,
        project_id,
        Jsonb(FORM_FIELDS),
        Jsonb(FORM_CHOICES),
        user_id,
    )
    return Project(project_id, user_id)


def add_submissions(
    database: Database,
    project: Project,
    submissions: list[dict],
    deleted: bool = False,
    versions: dict[int, str] | None = None,
) -> None:
    """Stores submissions as the API does; ``versions`` gives the form_version column of a Kobo id (null otherwise)."""
    with database.connect() as connection, connection.cursor() as cursor:
        cursor.executemany(
            """
            INSERT INTO tamiza.raw_submissions
                (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at, deleted)
            VALUES (%s, %s, %s, %s, %s, 'sync', now(), now(), %s)
            """,
            [
                (project.id, s["_id"], s["_uuid"], (versions or {}).get(s["_id"]), json.dumps(s), deleted)
                for s in submissions
            ],
        )


def add_form_version(
    database: Database,
    project: Project,
    uid: str,
    choices: list[dict] | None = None,
    status: str = "available",
    deployed_at: str | None = None,
    fields: list[dict] | None = None,
) -> None:
    """Records a form version as the API does: its definition when available, otherwise only its state."""
    database.execute(
        """
        INSERT INTO tamiza.form_versions
            (project_id, uid, status, fields, choices, deployed_at, recorded_at, fetched_at)
        VALUES (%s, %s, %s, %s, %s, %s, now(), CASE WHEN %s = 'available' THEN now() END)
        """,
        project.id,
        uid,
        status,
        Jsonb(fields if fields is not None else FORM_FIELDS),
        Jsonb(choices if choices is not None else []),
        deployed_at,
        status,
    )


def deploy_form_version(
    database: Database,
    project: Project,
    uid: str,
    fields: list[dict] | None = None,
    status: str = "available",
    record: bool = True,
) -> None:
    """
    Makes ``uid`` the version the project's form is deployed with, as the API's snapshot records it: the
    ``projects.form_version`` column and, unless ``record`` is false, the version's ``form_versions`` row (whose
    ``fields`` are where question labels are read from).
    """
    if record:
        add_form_version(database, project, uid, status=status, fields=fields)
    database.execute("UPDATE tamiza.projects SET form_version = %s WHERE id = %s", uid, project.id)


def column_comments(database: Database, schema: str, table: str) -> dict[str, str | None]:
    """The comment of each column of ``schema.table`` (None when it has none), by column name."""
    return dict(
        database.rows(
            """
            SELECT a.attname, d.description
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_description d ON d.objoid = c.oid AND d.objsubid = a.attnum
            WHERE n.nspname = %s AND c.relname = %s AND a.attnum > 0 AND NOT a.attisdropped
            """,
            schema,
            table,
        )
    )


def add_script(
    database: Database,
    project: Project,
    kind: str,
    code: str | None,
    name: str | None = None,
    label: str | None = None,
    format: str = "decimal",
) -> uuid.UUID:
    """
    Saves a new version of a script (None marks it removed) and returns its id. An `indicator` script needs its
    `name`; it is numbered per name and carries a label and a format unless it is a removal.
    """
    version_id = uuid.uuid4()
    database.execute(
        """
        INSERT INTO tamiza.script_versions
            (id, project_id, kind, name, number, code, label, format, author_id, created_at)
        VALUES (%s, %s, %s, %s,
                (SELECT coalesce(max(number), 0) + 1 FROM tamiza.script_versions
                 WHERE project_id = %s AND kind = %s AND name IS NOT DISTINCT FROM %s),
                %s, %s, %s, %s, now())
        """,
        version_id,
        project.id,
        kind,
        name,
        project.id,
        kind,
        name,
        code,
        None if code is None or name is None else label or name,
        None if code is None or name is None else format,
        project.user_id,
    )
    return version_id


def queue_run(database: Database, project: Project, status: str = "queued") -> uuid.UUID:
    run_id = uuid.uuid4()
    database.execute(
        "INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at) VALUES (%s, %s, 'manual', %s, now())",
        run_id,
        project.id,
        status,
    )
    return run_id


def add_reference_table(
    database: Database, project: Project, name: str, columns: list[tuple[str, str]], rows: list[list]
) -> uuid.UUID:
    """Stores a reference table as the API does: ``columns`` are (name, type) pairs, rows are lists in column order."""
    table_id = uuid.uuid4()
    database.execute(
        """
        INSERT INTO tamiza.reference_tables
            (id, project_id, name, file_name, columns, row_count, created_at, updated_by, updated_at)
        VALUES (%s, %s, %s, %s, %s, %s, now(), %s, now())
        """,
        table_id,
        project.id,
        name,
        f"{name}.csv",
        Jsonb([{"name": column, "header": column, "type": kind} for column, kind in columns]),
        len(rows),
        project.user_id,
    )
    database.execute(
        "INSERT INTO tamiza.reference_table_contents (reference_table_id, rows) VALUES (%s, %s)", table_id, Jsonb(rows)
    )
    return table_id
