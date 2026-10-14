# Proposal

## Why

The flattened tables name their columns after the questions' names (`has_water`, `hh_size`), which are short identifiers chosen by the form's author. Nobody outside the analysis team can tell what a column holds without opening the form in Kobo. Analysts copy the question texts by hand into dashboards, BI tools and reports. The question labels are already in Tamiza: the project's form snapshot keeps each question's label in the form's default language. Nothing carries them to the tables. Scripts cannot tell which column comes from which question either, because flattening reduces and deduplicates the names (`spouse/age` becomes `spouse_age`).

Unlike choice labels (`add-choice-labels`), a column has a single label for all its rows. Tamiza therefore takes it from one definition, the form's **latest deployed version**, and never per submission version. A question relabeled between versions shows its current text, which is what people reading the tables today expect.

## What Changes

- **Question labels on the flattened columns.** Each column of a flattened table that holds the answers to a question of the latest deployed form version carries that question's label, in the form's default language. The label is attached as metadata. Column names, order and types do not change, so scripts and published queries keep working.
- **Only the latest deployed version.** Labels come from the project's form snapshot when it describes the deployed version. They never come from earlier versions, even for answers given with them, and never from undeployed edits. When the snapshot does not describe the deployed version (the form was never deployed, or Kobo could not return the deployed version's definition), the run publishes no question labels and its flatten log says why.
- **Columns without a label.** These columns get no label:
  - columns of questions that the deployed version no longer has;
  - columns of questions without a label in the default language;
  - system columns (`_id`, `_index`...).

  The flatten log lists, per table, the columns whose question is not in the deployed version. The free-text column of an "or other" question (`water_source_other`) is labeled from its select question.
- **Where the labels appear:**
  - **Published tables:** as PostgreSQL column comments in `p_<id>_raw`, and in `p_<id>_clean` when there is no script 1 (it publishes the same tables). They are published in the same atomic swap as the tables. Dashboards, BI tools and `psql` (`\d+`) can read them.
  - **Scripts:** a new `ctx.form.columns` table gives the table, column, question path and label of every flattened column, and `ctx.form.column_label(table, column)` looks one up.
  - **Sample tests:** the preview of the flattened tables shows each column's label under its name.
- **Docs.** `docs/scripts.md` describes the column labels and how they differ from choice labels. `docs/kobo.md` says that redeploying the form changes them at the next run.

Not in this change:
- Using question labels as column names.
- Labels on tables returned by scripts. Scripts rename and compute columns, so Tamiza cannot know which question a script's column comes from. Letting scripts declare labels for their outputs is later work.
- Labels for repeat tables and groups. The snapshot keeps only questions.
- Choosing the label language per project.
- Showing column labels in the run details. Runs list only table names and row counts today.
- Any change to choice labels, which still follow each submission's form version.

## Capabilities

### New Capabilities

None. Question labels are part of the flatten step and the script context (`pipeline`).

### Modified Capabilities

- `pipeline`:
  - flattened columns carry their question's label from the deployed version, as published column comments;
  - the flatten log reports the columns without a question in the deployed version;
  - `ctx.form` gains the column-to-question map;
  - the test preview shows the labels.
- `projects`: states that the form snapshot keeps each question's label in the default language. The code already does this; the spec mentioned it only for choices.

## Impact

- **analytics:**
  - `pipeline.project_context` also reads `projects.form_version` and the status of that version's definition, to decide whether the snapshot is the deployed version.
  - `flatten` returns each table's column-to-question map with its labels, and adds the new log lines.
  - `publish` writes `COMMENT ON COLUMN` in the staging schema before the swap.
  - The clean step without script 1 republishes the same comments.
  - `preview` includes each column's label.
  - `runner.Form` gains `columns` and `column_label`, fed through `context.json`.
- **api:** `TestColumn` gains an optional `label`, so the preview keeps it. The OpenAPI document updates from the record. No new endpoint and no database migration: the labels are already in `projects.form_fields`.
- **UI:** the shared table preview shows a column's label on a second line, cut to the column width, with the full text on hover. The reference-table preview is unchanged.
- **Docs:**
  - `docs/scripts.md`
  - `docs/kobo.md`
  - README status
  - `docs/tamiza-brief.md`
- **Compatibility:** not breaking. Column names and values are unchanged. Published tables get their comments at each project's next run.
- **Configuration and dependencies:** none new.
