"""Reference tables read from PostgreSQL into typed Parquet files."""

import datetime

import pandas as pd
from helpers import add_reference_table, create_project

from tamiza import interchange, reference

COLUMNS = [("code", "text"), ("year", "integer"), ("rate", "decimal"), ("day", "date"), ("note", "text")]


def test_loads_each_table_with_its_types(database, project, tmp_path):
    add_reference_table(
        database,
        project,
        "population",
        COLUMNS,
        [["05001", 2026, 12.5, "2026-01-31", None], ["11001", None, 3, None, "x"]],
    )
    add_reference_table(database, project, "codes", [("code", "text")], [["a"]])

    loaded = reference.load(database.url, project.id, tmp_path)

    assert [table.name for table in loaded] == ["codes", "population"]
    population = loaded[1]
    assert population.rows == 2
    assert population.path == tmp_path / "population.parquet"
    assert isinstance(population.updated_at, datetime.datetime)
    frame = interchange.read_parquet(population.path)
    assert list(frame.columns) == ["code", "year", "rate", "day", "note"]
    assert str(frame["year"].dtype) == "Int64"
    assert frame["year"].tolist()[0] == 2026 and pd.isna(frame["year"].tolist()[1])
    assert frame["rate"].tolist() == [12.5, 3.0]
    assert frame["day"].tolist()[0] == pd.Timestamp("2026-01-31") and pd.isna(frame["day"].tolist()[1])
    assert frame["code"].tolist() == ["05001", "11001"]
    assert pd.isna(frame["note"].tolist()[0])


def test_rows_keep_their_order_across_batches(database, project, tmp_path, monkeypatch):
    monkeypatch.setattr(reference, "BATCH", 7)
    add_reference_table(database, project, "numbers", [("n", "integer")], [[i] for i in range(50)])

    (table,) = reference.load(database.url, project.id, tmp_path)

    assert interchange.read_parquet(table.path)["n"].tolist() == list(range(50))


def test_a_project_without_tables_loads_nothing(database, project, tmp_path):
    assert reference.load(database.url, project.id, tmp_path) == []


def test_other_projects_tables_are_not_loaded(database, project, tmp_path):
    other = create_project(database, project.user_id)
    add_reference_table(database, other, "theirs", [("a", "text")], [["x"]])

    assert reference.load(database.url, project.id, tmp_path) == []
