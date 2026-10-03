"""PostgreSQL for integration tests: one PostGIS container per session, a fresh database per test.

The schema comes from the API's DbUp scripts, applied in order to a template database, so the worker is tested
against exactly what the API creates. The tests are skipped when Docker is not available.
"""

import uuid
from collections.abc import Iterator
from pathlib import Path

import psycopg
import pytest
from helpers import Database, Project, create_project

SCRIPTS = Path(__file__).resolve().parents[3] / "tamiza-api" / "src" / "dbUp" / "Scripts"
IMAGE = "postgis/postgis:17-3.5"
TEMPLATE = "tamiza_template"


@pytest.fixture(scope="session")
def server() -> Iterator[dict[str, str]]:
    try:
        from testcontainers.postgres import PostgresContainer

        container = PostgresContainer(IMAGE, username="tamiza", password="tamiza", dbname="postgres")
        container.start()
    except Exception as error:  # no Docker, no image
        pytest.skip(f"PostgreSQL container unavailable: {error}")
    try:
        conninfo = {
            "host": container.get_container_host_ip(),
            "port": str(container.get_exposed_port(5432)),
            "user": "tamiza",
            "password": "tamiza",
        }
        with psycopg.connect(**conninfo, dbname="postgres", autocommit=True) as admin:
            admin.execute(f"CREATE DATABASE {TEMPLATE}")
        with psycopg.connect(**conninfo, dbname=TEMPLATE, autocommit=True) as template:
            for script in sorted(SCRIPTS.glob("*.sql")):
                template.execute(script.read_text(encoding="utf-8"))
        yield conninfo
    finally:
        container.stop()


@pytest.fixture
def database(server) -> Database:
    name = f"t_{uuid.uuid4().hex}"
    with psycopg.connect(**server, dbname="postgres", autocommit=True) as admin:
        admin.execute(f"CREATE DATABASE {name} TEMPLATE {TEMPLATE}")
    return Database(server, name)


@pytest.fixture
def project(database) -> Project:
    return create_project(database)
