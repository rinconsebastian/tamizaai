"""The cache of a test's flattened sample and script 1 result: keys, entries, eviction and expiry."""

import json
import os
import time

import pytest

from tamiza import sample_cache
from tamiza.labels import Labels, SelectLookup
from tamiza.question_labels import QuestionLabels
from tamiza.sample_cache import SampleCache

MB = 1024 * 1024


def base_inputs():
    return {
        "project_id": "p1",
        "sample": [[1, "2026-10-01T10:00:00+00:00", "vA"], [2, "2026-10-01T11:00:00+00:00", "vA"]],
        "form_fields": [{"name": "age", "xpath": "age", "type": "integer"}],
        "form_choices": [{"listName": "yn", "name": "1", "label": "Yes"}],
        "labels": Labels({"water": SelectLookup(False, {"1": "Yes"})}, {"vA": {}}, {"vA": None}, {}),
        "questions": QuestionLabels("vA", {"age": "Age"}, frozenset({"age"})),
    }


def key(**changes):
    inputs = {**base_inputs(), **changes}
    return sample_cache.flatten_key(
        inputs["project_id"],
        inputs["sample"],
        inputs["form_fields"],
        inputs["form_choices"],
        inputs["labels"],
        inputs["questions"],
    )


def test_the_same_inputs_give_the_same_key():
    assert key() == key()
    assert len(key()) == 64


@pytest.mark.parametrize(
    "changes",
    [
        {"project_id": "p2"},
        {"sample": [[1, "2026-10-01T10:00:00+00:00", "vA"]]},
        {"sample": [[1, "2026-10-02T10:00:00+00:00", "vA"], [2, "2026-10-01T11:00:00+00:00", "vA"]]},
        {"sample": [[1, "2026-10-01T10:00:00+00:00", "vB"], [2, "2026-10-01T11:00:00+00:00", "vA"]]},
        {"sample": [[2, "2026-10-01T11:00:00+00:00", "vA"], [1, "2026-10-01T10:00:00+00:00", "vA"]]},
        {"form_fields": [{"name": "age", "xpath": "age", "type": "text"}]},
        {"form_choices": [{"listName": "yn", "name": "1", "label": "Sí"}]},
        {"labels": Labels({"water": SelectLookup(False, {"1": "Sí"})}, {"vA": {}}, {"vA": None}, {})},
        {"labels": Labels({"water": SelectLookup(False, {"1": "Yes"})}, {"vA": {}, "vB": {}}, {"vA": None}, {})},
        {"questions": QuestionLabels("vA", {"age": "Age of the respondent"}, frozenset({"age"}))},
        {"questions": QuestionLabels("vB", {"age": "Age"}, frozenset({"age"}))},
        {"questions": QuestionLabels(unavailable="no deployed form version is recorded")},
    ],
)
def test_every_input_of_flattening_changes_the_key(changes):
    assert key(**changes) != key()


def test_the_clean_key_covers_the_sample_the_script_version_and_the_reference_tables():
    flatten = key()
    reference = [("population", "2026-10-01T00:00:00+00:00")]
    base = sample_cache.clean_key(flatten, "v3", reference)

    assert sample_cache.clean_key(flatten, "v3", reference) == base
    assert sample_cache.clean_key(key(project_id="p2"), "v3", reference) != base
    assert sample_cache.clean_key(flatten, "v4", reference) != base
    assert sample_cache.clean_key(flatten, "v3", [("population", "2026-10-02T00:00:00+00:00")]) != base
    assert sample_cache.clean_key(flatten, "v3", []) != base
    assert sample_cache.clean_key(flatten, "v3", reference) != flatten


@pytest.fixture
def parquet(tmp_path):
    """A file of the given size in bytes; the cache copies files and never reads them."""

    def make(name="main", size=1000):
        path = tmp_path / "source" / f"{name}.parquet"
        path.parent.mkdir(exist_ok=True)
        path.write_bytes(b"x" * size)
        return path

    return make


@pytest.fixture
def cache(tmp_path):
    return SampleCache(tmp_path / "cache", 10 * 1024)


def test_an_entry_is_kept_and_read_back_with_its_step_and_tables(cache, parquet):
    entry = cache.put("k1", {"inputRows": 3, "log": "hello"}, {"main": parquet("main", 100)})

    found = cache.get("k1")

    assert entry is not None and found is not None
    assert found.step == {"inputRows": 3, "log": "hello"}
    assert found.table("main").read_bytes() == b"x" * 100
    assert cache.get("missing") is None


def test_no_half_built_entry_is_left_and_the_temp_directory_is_gone(cache, parquet):
    cache.put("k1", {}, {"main": parquet()})

    assert [p.name for p in cache.directory.iterdir()] == ["k1"]


def test_putting_the_same_key_again_replaces_the_entry(cache, parquet):
    cache.put("k1", {"n": 1}, {"main": parquet("main", 100)})
    cache.put("k1", {"n": 2}, {"main": parquet("main", 200)})

    found = cache.get("k1")

    assert found.step == {"n": 2} and found.table("main").stat().st_size == 200


def test_the_least_recently_used_entries_go_first_when_the_limit_is_exceeded(cache, parquet):
    for name in ("a", "b", "c"):
        cache.put(name, {}, {"main": parquet(size=3000)})
        os.utime(cache.directory / name, (time.time() - 100 + ord(name), time.time() - 100 + ord(name)))
    cache.get("a")  # a becomes the most recently used

    cache.put("d", {}, {"main": parquet(size=3000)})

    # Four entries of 3 KB do not fit in 10 KB: b was used least recently, and removing it is enough.
    assert sorted(p.name for p in cache.directory.iterdir()) == ["a", "c", "d"]
    assert cache.get("b") is None


def test_entries_in_use_by_the_running_test_are_not_evicted(cache, parquet):
    cache.put("flatten", {}, {"main": parquet(size=6000)})
    os.utime(cache.directory / "flatten", (1, 1))  # the oldest of all

    cache.put("clean", {}, {"main": parquet(size=6000)}, protect=["flatten"])

    assert cache.get("flatten") is not None and cache.get("clean") is not None


def test_an_entry_larger_than_the_limit_is_not_kept(cache, parquet):
    kept = cache.put("big", {}, {"main": parquet(size=20 * 1024)})

    assert kept is None
    assert cache.get("big") is None
    assert list(cache.directory.iterdir()) == []


def test_a_limit_of_zero_turns_reuse_off(tmp_path, parquet):
    off = SampleCache(tmp_path / "cache", 0)

    assert off.enabled is False
    assert off.put("k", {}, {"main": parquet()}) is None
    assert off.get("k") is None
    assert not (tmp_path / "cache").exists()


def test_an_entry_unused_for_24_hours_is_swept_and_a_recent_one_is_kept(cache, parquet):
    cache.put("old", {}, {"main": parquet()})
    cache.put("recent", {}, {"main": parquet()})
    old = time.time() - 25 * 3600
    os.utime(cache.directory / "old", (old, old))

    removed = cache.sweep()

    assert removed == 1
    assert cache.get("old") is None and cache.get("recent") is not None


def test_using_an_entry_keeps_it_from_being_swept(cache, parquet):
    cache.put("k", {}, {"main": parquet()})
    old = time.time() - 25 * 3600
    os.utime(cache.directory / "k", (old, old))

    assert cache.get("k") is not None
    assert cache.sweep() == 0


def test_leftover_temp_directories_are_removed_at_start(cache, parquet):
    cache.put("k", {}, {"main": parquet()})
    (cache.directory / ".tmp-abc").mkdir()
    (cache.directory / ".tmp-abc" / "x").write_bytes(b"half")

    cache.clean_leftovers()

    assert [p.name for p in cache.directory.iterdir()] == ["k"]


def test_an_entry_that_cannot_be_read_is_removed_and_counts_as_a_miss(cache, parquet):
    cache.put("k", {"n": 1}, {"main": parquet()})
    (cache.directory / "k" / "step.json").write_text("{not json")

    assert cache.get("k") is None
    assert not (cache.directory / "k").exists()


def test_a_write_failure_leaves_nothing_and_does_not_raise(cache, tmp_path):
    missing = tmp_path / "nowhere" / "main.parquet"

    assert cache.put("k", {}, {"main": missing}) is None
    assert [p.name for p in cache.directory.iterdir()] == []


def test_the_step_record_is_plain_json(cache, parquet):
    entry = cache.put("k", {"tables": [{"name": "main", "rows": 2}], "columns": []}, {"main": parquet()})

    assert json.loads((entry.path / "step.json").read_text())["tables"][0]["name"] == "main"
