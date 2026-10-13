# Proposal

## Why

Kobo stores and sends the answers to select questions as choice names, the codes defined in the form (`1` and `2`), not their labels (`Yes` and `No`). The flattened tables Tamiza gives the scripts, and later the dashboards, therefore show codes, and analysts must translate every select column by hand with `ctx.form.label` before the data is readable. That is exactly the routine Tamiza is meant to remove. Translating is also not a single lookup: a form is redeployed during fieldwork, and a choice can be added, removed or relabeled between versions, so each answer has to be translated with the form version it was submitted with.

## What Changes

- **Form version definitions.** Each submission names the deployed form version it was made with (`__version__`, already stored). Tamiza records every version its submissions use, as they are received by webhook or synchronization. The API then fetches each version's definition once from the Kobo API v2 (`/api/v2/assets/{uid}/versions/{version}/`) and keeps its questions and choice lists for the project. A fetch happens during each synchronization and, for versions that arrived by webhook, in the background within two minutes. Version definitions never change in Kobo, so a fetched version is not read again.
- **Failures do not block ingestion.**
  - A version that cannot be read, because Kobo is unreachable or the token is rejected, is retried after an hour, then at doubling intervals up to a day.
  - A version Kobo does not know, while the form itself is readable, is marked as not found.
  - Neither failure fails the synchronization.
  - Re-checking the form, or replacing the token, retries both.
- **Labels in the flattened tables. BREAKING.** The flatten step replaces each answer to a `select_one` or `select_multiple` question with its label, in the form's default language, taken from the definition of the submission's own version. The answers in repeat groups use the version of their submission. A `select_multiple` answer becomes its labels joined by `; ` in the order of the answer. The scripts, the sample test preview, and the clean schema when there is no script 1 all receive labels instead of codes. The raw submissions stay stored exactly as Kobo sent them.
- **Fallbacks, always counted.**
  - A submission whose version is unknown, not fetched yet or not found in Kobo is translated with the project's current form, which is what Kobo's own exports do.
  - A code missing from its version's list takes the current form's label. This happens, for example, when a submission edited in Kobo with a later version uses a choice added in that version.
  - A code that has no label anywhere keeps its code, and so do answers to questions with external choice files.
  - The `other` answer of an "or other" question is labeled `Other`, as Kobo shows it.
  - The flatten step's log counts every fallback per column and per version.
- **Current form = deployed form.** The project's form snapshot is read from the form's deployed version, not from the asset's latest draft, which may hold undeployed edits. If that version cannot be read, the snapshot falls back to the latest content, as today. Labels are read strictly in the default language: a missing default-language label counts as no label, instead of borrowing another language's label.
- **`ctx.form.name(question, label)`.** The script context gains the reverse lookup, from a label to its code. It searches the current form, then earlier versions, so scripts can still join reference tables keyed by codes when a label changed between versions.
- **UI.** The project's Data section lists the form versions its submissions use, with the number of submissions of each and whether its labels are available, pending, failed (with the reason) or not found. The project page shows how many version definitions are unavailable.
- **Docs.** `docs/scripts.md` describes the label rules and updates its examples. `docs/kobo.md` explains form versions and why some answers may keep their codes.

Not in this change:
- Question labels as column names. Columns keep the question names.
- Choosing the label language per project.
- Splitting `select_multiple` answers into one column per choice.
- Translating the choices of `select_one_from_file` and `select_multiple_from_file` questions, whose lists live in files attached to the form.
- Translating Kobo's ranking, scoring and matrix questions, which Kobo expands into generated questions at deployment.
- Reading the version history that recent Kobo releases add to edited submissions (`meta/formVersions`).
- Changing what the sampling frame and the future performance dashboard read. Both use the raw submissions, so they still match codes.
- Audit events (`add-audit-log`).

## Capabilities

### New Capabilities

None. Version definitions are part of keeping a copy of the project's Kobo data (`ingestion`). Labels are part of the flatten step (`pipeline`).

### Modified Capabilities

- `ingestion`:
  - records the form versions used by the stored submissions;
  - fetches and keeps each version's definition;
  - retries failed fetches without failing synchronizations, also on re-check and token replacement;
  - shows each version's status to project members.
- `pipeline`: the flatten step writes choice labels from each submission's form version, with counted fallbacks. The script context gains the label-to-code lookup.
- `projects`: the form snapshot taken on creation, re-check and synchronization describes the deployed version, with labels in the default language only.

## Impact

- **api:**
  - A Kobo client call for one asset version, through the same HTTP client, private-network guard and error mapping as the other calls.
  - `KoboForm`:
    - reads `deployed_version_id`;
    - takes labels strictly from the default language;
    - builds question paths from the group nesting with the names Kobo deploys (`$autoname`, `$autovalue`), because the stored `$xpath` is missing or unreliable in some versions;
    - records the "or other" option.
  - `SubmissionStore` records new version uids in the same transaction that stores submissions, so the webhook makes no Kobo call.
  - `SyncRunner` and a new background worker fetch pending definitions, and a failed fetch does not fail the sync.
  - Re-check and token replacement retry failed and not found versions.
  - New endpoint `GET /api/v1/projects/{id}/form-versions`; the project details gain the count of unavailable definitions.
- **analytics:**
  - The flatten step loads the project's stored version definitions and reads each submission's stored version, for runs and sample tests alike.
  - It replaces select codes with labels before type conversion, and logs the fallbacks.
  - `ctx.form` gains `name(question, label)`.
- **Database:** DbUp script `0005_add_choice_labels.sql` with the table `form_versions`. It records the versions already used by stored submissions as pending, so existing projects fetch them within minutes of the upgrade.
- **UI:** a form versions panel in the Data section.
- **Docs:**
  - `docs/scripts.md`
  - `docs/kobo.md`
  - README status
  - `docs/tamiza-brief.md` (data model)
- **Existing scripts:** scripts that compare select columns with codes (`main["water_source"] == "piped"`) stop matching after the first run with this change. They must compare with labels or use `ctx.form.name`. Published tables keep codes until each project's next run.
- **Configuration and dependencies:** none new.
