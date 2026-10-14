# Design

## Context

Builds on `add-choice-labels` (main specs `pipeline`, `projects`, `ingestion`). Relevant current state:

- **Snapshot** (api):
  - `projects.form_fields` is a jsonb list of `{name, xpath, type, label, listName, orOther}`.
  - `KoboForm.Label` keeps only the default-language label, or null.
  - Paths are built from the group, repeat, score, rank and matrix nesting with the deployed names, so they equal the submission keys.
  - `begin_*`/`end_*` rows are not kept, so groups and repeats have no label in the snapshot.
- **Which content the snapshot holds.** `FormSnapshot.ResolveAsync` takes the snapshot from:
  1. the asset's content when there is no deployment (`projects.form_version` null);
  2. the asset's content when the deployed version is the latest, recorded as `available` in `form_versions`;
  3. the stored definition of the deployed version;
  4. a fresh fetch of it, stored as `available`. If that fetch fails, the snapshot falls back to the asset's latest content, which can hold undeployed edits. `form_version` still names the deployed uid, whose `form_versions` row is `failed` or `not_found`.

  The snapshot is not a reliable witness of the deployed version. After a fallback (case 4), the version's row can later become `available` through a re-check's retry or the background worker, which update `form_versions` only; `projects.form_fields` keeps the fallback content until the next snapshot. The `form_versions` row of `projects.form_version` is the trustworthy source: Kobo versions are immutable, so once it is `available` its `fields` are the deployed version's.
- **flatten** (analytics):
  - `_Column(path, kind, name)` keeps the submission key (`path`) of each column.
  - `_assign_names` reduces the names, falls back to the full path on a collision (`spouse_age`), and deduplicates them.
  - The mapping lives only inside `flatten()`. `TableOutput(name, rows, path)` does not expose it.
  - System columns are prepended by the writer and are not in `table.columns`.
  - Parquet is written by DuckDB `COPY`, which cannot write per-field metadata. Scripts read inputs through pandas, which drops Arrow field metadata anyway.
- **publish:**
  - Each table is created with `CREATE TABLE` and `COPY` in `<schema>__next`.
  - One transaction then drops the published schema and renames the staging one.
  - The connection is in autocommit. Nothing uses `COMMENT ON` today.
- **pipeline:**
  - `project_context` reads `name, form_fields, form_choices` once per run and per sample test.
  - `do_flatten` adds `label_codes` to `context.json`.
  - Without script 1, `cleaned = raw`: the same `OutputTable` objects are published as clean.
- **Previews:**
  - `preview.step_tables((name, rows, path))` builds `columns: [{name, type}]` from the Parquet schema.
  - The API deserializes them into `TestColumn(string Name, string Type)` and silently drops unknown keys.
  - The UI's shared `TablePreview` shows the name with the type on a second line. The reference-table preview uses the same component.
- **API reads of published schemas:** none. Run steps record only `[{name, rows}]` per table.

Requirements are in `specs/pipeline` and `specs/projects`; motivation in `proposal.md`. User decisions for this change:
- Labels are column metadata. Column names stay the question names.
- Labels come from the latest deployed version only, not per version.
- A column whose question is not in that version gets no label, and the flatten log reports it.

## Goals / Non-Goals

**Goals:**

- Labels are exact: mapped through each column's submission path, never guessed from the reduced column name.
- No change to column names, order, types or values, to the stored data, or to the flatten step's memory profile.
- Labels are published atomically with their tables. A run whose labels cannot be trusted publishes none and says so.
- No database migration and no new API endpoint.

**Non-Goals:**

- Contacting Kobo from the analytics worker.
- Labels on columns returned by scripts, or a label-to-column lookup.
- Table comments for repeat tables (the snapshot has no group labels).
- Normalizing label text. Labels are kept as written, like choice labels.

## Decisions

### D1. Source: the deployed version's own definition

`project_context` reads, in the same query as the snapshot:

```sql
SELECT p.name, p.form_fields, p.form_choices, p.form_version, v.status, v.fields AS version_fields
FROM tamiza.projects p
LEFT JOIN tamiza.form_versions v ON v.project_id = p.id AND v.uid = p.form_version
WHERE p.id = %s
```

A pure function `question_labels(form_version, status, fields)` builds `QuestionLabels`, which `ProjectContext` holds:
- `version`: the deployed uid, or None;
- `by_path`: xpath to label, from the version's `fields`, keeping only non-empty string labels;
- `deployed_paths`: every xpath of the version's `fields`, plus `<X>_other` for each question `X` with `orOther`, whether or not `X` has a label;
- `unavailable`: the reason labels are off, or None.

| `form_version` | Row status | Result |
|---|---|---|
| set | `available` | labels from the row's `fields` |
| set | `pending`, `failed`, `not_found`, or no row (read as `pending`) | no labels; reason names the version and the status |
| null | — | no labels; reason "no deployed form version is recorded" |

The labels come from the deployed version's stored definition, not from `projects.form_fields`. So they follow the deployed version even while the snapshot holds fallback content, and they never include undeployed edits. They still come from the same query as `form_version`, so the version named in the log is the one the labels describe. `ctx.form.questions` keeps describing the snapshot, as today.

*Alternatives:*
- Reading the labels from `projects.form_fields` when the row is `available` is wrong in the fallback case above.
- Comparing `form_fields` with the row's `fields` and refusing labels when they differ is more code for the same labels, and turns labels off for no benefit.
- Making the fetcher refresh the project snapshot when the deployed version becomes available would fix the snapshot too, but changes the API's ingestion code and is out of scope here.

### D2. Mapping columns to labels in `flatten`

`flatten(..., question_labels: QuestionLabels | None = None)`. After `_assign_names`, each `TableOutput` gets `columns: list[ColumnInfo]` (`name`, `path`, `label`) for its non-system columns, in table order, with `label` None when there is none. It is filled whether labels are applied or not, because `ctx.form.columns` (D4) needs every column and its path. `TableOutput` also has a `column_labels` property, from name to label for the labeled columns, which `publish` and the preview use.

A column's label, when labels are applied:
- **Snapshot path:** `by_path[column.path]`.
- **"Or other" text column:** a path `X_other` that is not in `by_path`, where `X` has `orOther` and a label in `by_path`, takes `f"{label(X)}: Specify other"`. Kobo generates that text question at deployment, labeled `Specify other.` in every language, so it is not among the version's fields.
- **System columns:** never labeled and not listed in `columns`; they are not in `table.columns`.

`TableOutput`, `OutputTable` and `publish.Table` are dataclasses, so the new mapping fields use `field(default_factory=dict)` (or `default_factory=list` for `columns`). `FlattenResult` gains `question_labels_version`. `pipeline` copies the labels to the raw `OutputTable`s, and the `cleaned = raw` path keeps them. Script outputs never set them.

**Log lines,** appended after the choice-label lines:
- When labels are applied, `Question labels from form version vC.`, then per table, for columns whose path is not in `deployed_paths`, in column order: `main: 2 columns are not in the deployed form version and have no question label: old_question, legacy_age.` At most 20 names are listed, followed by `and N more`, to keep the log bounded.
- When labels are off, only the reason line: `Question labels not applied: the definition of the deployed form version vC is not available (failed).` or `... no deployed form version is recorded.` No per-table lines are written, because without the deployed version's definition there is nothing to compare against.

A column whose path is in `deployed_paths` with no label (no default-language text, types such as `calculate` and `start`, or an `X_other` of an unlabeled `X`) is not reported. The form defines no label for it, and listing every metadata column on every run would bury the useful line.

*Alternatives:*
- Writing labels into Parquet field metadata would mean replacing DuckDB `COPY` with a streamed pyarrow writer. Pandas drops the metadata before scripts see it, and `publish` reads columns with DuckDB `DESCRIBE`. It is a lot of work for a carrier that only `publish` and `preview` would read.
- A sidecar labels file in `out_dir` breaks the assumption that the directory holds only the tables (`test_the_working_database_is_removed`) and adds another file to keep consistent.

### D3. Column comments in `publish`

`publish.Table` (a frozen dataclass) gains `column_labels: Mapping[str, str] = field(default_factory=dict)`. In `_load`, after `CREATE TABLE` and before `COPY`, one statement holds every comment of the table:

```python
sql.SQL("; ").join(
    sql.SQL("COMMENT ON COLUMN {} IS {}").format(sql.Identifier(schema, table.name, column), sql.Literal(label))
    for column, label in labels  # only columns the table has, in table order
)
```

- **Quoting.** `COMMENT ON` is a utility statement, so PostgreSQL does not accept bound parameters (`$1`) in it. The label goes through `sql.Literal`, which escapes quotes and backslashes. It is never formatted into the string by hand.
- **Columns the table does not have.** `_load` already gets the table's columns from `interchange.describe`. A label for any other column is ignored, so a stale map can never make the load fail.
- **Atomicity.** The comments sit on the staging tables, are stored per table OID, move with `ALTER SCHEMA ... RENAME`, and are dropped with the old schema. Readers see the old tables with their old comments or the new ones with theirs.
- `pipeline.do_flatten` and the clean step without script 1 pass `column_labels`. Steps that publish script outputs pass none, so those tables carry no comments.

### D4. `ctx.form` (analytics)

`do_flatten`, for runs and sample tests, adds `context["columns"]`: `[{table, column, path, label}]` built from each `TableOutput.columns`, for every non-system column of every flattened table, in table and column order, with `label` null when there is none. Columns whose question is not in the deployed version are listed with their submission key as `path`. When labels are not applied (D1), every `label` is null, but the table, column and path are still given.

`runner.Form` gains:
- `columns`: a DataFrame with the columns `table`, `column`, `path` and `label`;
- `column_label(table, column) -> str | None`.

The context is written before script 1 runs. Its `columns` therefore describe the flattened tables, which is what scripts receive as `main`, the repeat tables and, in scripts 2 and 3 without script 1, the clean tables. The docs say that tables a script builds have no entry.

The script template docstring in `tamiza-ui/src/app/core/projects/pipeline.service.ts` mentions `ctx.form.columns`.

*Alternative:* `DataFrame.attrs` on the input tables. Most pandas operations do not keep attrs reliably, and output validation renames columns without updating them.

### D5. Test preview

- **analytics.** `preview.step_tables` accepts `OutputTable`-like items with an optional `column_labels`. `table_preview` adds `label` (or null) to each column. `sample_runs` passes the flatten outputs, and the saved script 1's outputs carry no labels.
- **api.** `TestColumn(string Name, string Type, string? Label = null)` keeps the key through the JSON round trip. The OpenAPI document follows the record.
- **UI:**
  - The column type in `pipeline.service.ts` gains `label?: string | null`. `TablePreview`'s input accepts it as optional, so the reference preview, whose columns have no label, is unchanged.
  - The header shows the label as an extra line between the name and the type (`data-testid="column-label"`). The line is cut to `max-w-xs` with `truncate`, and the full text goes in `title`, as the cells do.
  - Text is rendered with interpolation, never `innerHTML`, so Markdown or HTML in a Kobo label shows as text.
  - When there is no label, no line is shown.

### D6. API, database and run details

- **No migration.** The labels are already in `projects.form_fields`, and `form_versions` already holds the status D1 needs.
- **No endpoint changes** other than the `TestColumn` field.
- **Run details stay as they are.** Showing columns there would need a new shape in `run_steps.tables`. The published comments and the sample preview cover the need for now.
- **`projects` spec.** The delta only states what `KoboForm` already does (question labels in the default language). It needs a new `FormSnapshotTests` case for a question, but no code change. The analytics worker reads labels from the stored definition of the deployed version (D1), which the API already writes with the same parser.

### D7. Test strategy

- **Python unit:**
  - `test_flatten.py`:
    - labels and paths by column for `main`, the collided `spouse_age` and a repeat column, also with labels off;
    - system columns unlabeled;
    - an "or other" text column, and an unlabeled "or other" select whose `_other` column is neither labeled nor reported;
    - a removed question listed in the log, with the 20-name cap;
    - a question with a null label neither labeled nor reported;
    - `question_labels=None` keeping today's output and log;
    - names, order and values unchanged with labels.
  - `tests/test_question_labels.py`: the pure `question_labels(form_version, status, fields)` for every row of the D1 table, and the `deployed_paths` set including `X_other`.
  - `test_runner.py`: `ctx.form.columns` and `column_label`, including unknown tables and columns and the labels-off case.
  - `test_docs_examples.py`: the data-dictionary example of `docs/scripts.md`, keyed by its own marker, with the `surveyed` fixture recording a deployed version.
  - `test_preview.py`: `label` in columns, and null without labels.
- **Python integration** (Testcontainers):
  - `test_publish.py`:
    - `col_description` after the swap;
    - a label with quotes, a backslash, a newline and 2,000 characters;
    - comments replaced on republish;
    - a label for a missing column ignored;
    - a failed load leaving the old comments.
  - A helper in `tests/integration/helpers.py` records a deployed version (the `form_versions` row and `projects.form_version`, with labeled fields). Today no fixture sets `form_version`, so every label scenario depends on it.
  - `test_pipeline.py`:
    - raw and clean (no script 1) carry comments, and output carries none;
    - a `form_version` with no `form_versions` row gives no labels, and the reason says pending;
    - the snapshot fell back to draft content, then the version's row became `available` with different labels: the run publishes the version's own labels, never the draft's;
    - a `failed` deployed version gives no comments, its reason line and no per-table lines;
    - a relabel between `vA` and `vB` gives only `vB`'s label.
  - `test_sample_runs.py`: the preview of the flatten step has labels.
- **API integration:**
  - `ScriptTestsApiTests`: a column `label` written by the worker is returned.
  - `FormSnapshotTests`: a question with only an English label is stored without a label.
- **UI:**
  - `project-scripts.spec.ts`:
    - a labeled column shows `column-label`;
    - an unlabeled one shows none;
    - a long label is cut and has the full text in `title`.
  - `project-reference.spec.ts` still passes unchanged.
  - Headless Chrome screenshots at 360 px and 1280 px.
- **Stack:** on the fake Kobo with three versions, one of which relabels a question, run a sync and a run. Check `\d+` on `p_<id>_raw.main` and `p_<id>_clean.main`, and the flatten log. Record the flatten and publish durations against the `add-choice-labels` figures.

## Risks / Trade-offs

- **[A path reused for a different question in the deployed version]** → Old answers get the new question's label. This is inherent in "deployed version only", is documented in `docs/kobo.md`, and is rare: Kobo forms normally rename instead of reusing a name.
- **[Labels off while the deployed version cannot be read]** → The run is not blocked, and the log line says why. As soon as the version's definition is available (a retry, the worker or a re-check), the next run publishes its labels, even if the snapshot still holds fallback content. The form versions card already shows the failed version.
- **[A project created before `form_version` existed has none recorded]** → It publishes no labels, with the reason in the log, until its next synchronization or re-check records the deployed version.
- **[Labels go stale between a redeploy and the next run]** → Same as the rest of the published data, which also changes only at runs. Documented. "Run now" refreshes at once.
- **[Long or formatted labels]** → Stored verbatim, as choice labels are. The preview cuts them and keeps the full text in a tooltip.
- **[Two columns with the same label (`Age` for `head/age` and `spouse/age`)]** → Harmless for comments. There is no label-to-column lookup.
- **[Script outputs have no labels, which may surprise users who see them in raw]** → Stated in `docs/scripts.md`, together with a data-dictionary example built from `ctx.form.columns`. Output column names are always reduced to lowercase identifiers, so labels cannot be used as names. Script-declared labels are later work.
- **[Many comments on wide forms]** → One statement per table. Comments are tiny next to the `COPY`. The stack check measures the publish time.
- **[Kobo's "or other" text label differs from `Specify other.`]** → Tamiza's label is its own composition, documented as such. It does not depend on the generated text.

## Migration Plan

- No schema change. Deploy the `analytics`, `api` and `ui` images together. The API change is an optional field, so an older UI ignores it.
- Each project's published tables gain comments at its next run. "Run now" applies them at once.
- Sample tests recorded before the upgrade show no labels, because their stored preview has none.
- **Rollback:** older images publish without comments at their next run, because the swap replaces the tables. Older API images drop the `label` key from previews. Nothing to clean up.

## Verification Results

**Narrower than task 6.1 (analytics only).** This section records a timing of the flatten step alone, not the Compose-stack check of task 6.1: no API, no fake Kobo and no sign-in were involved, so the stack checks (a sync across three versions, a deployed version made unreadable after a webhook) have not been run.

Measured on the development machine in the analytics test image, with the pre-change source (`git archive HEAD`) and the changed source against the same data: 500,000 submissions of about 1.2 KB, each with a `members` repeat group of three rows, seeded straight into `raw_submissions` in a PostGIS container, then one run with no scripts (flatten, publish raw, publish clean). The project's deployed version was recorded with labeled questions, so the changed code applied them. Each side ran twice, alternating:

| Measure | Before | With this change |
|---|---|---|
| Flatten step (500,000 → 2,000,000 rows) | 54.0 s, 57.5 s | 54.9 s, 58.0 s |
| `flatten()` alone | 50.1 s, 53.3 s | 51.0 s, 54.2 s |
| Publishing raw and clean (4,000,000 rows in all) | 7.7 s, 8.7 s | 7.8 s, 7.6 s |

The difference between the sides is smaller than the difference between two runs of the same side, so the cost of the label mapping and of the comments is not measurable at this volume. A two-thousand-row check confirmed that the changed code writes the comments (`municipality`, `age`) and the old code writes none.
