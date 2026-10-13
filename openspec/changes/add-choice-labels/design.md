# Design

## Context

Builds on `add-kobo-ingestion`, `add-python-pipeline` and `add-pipeline-authoring` (main specs `ingestion`, `projects`, `pipeline`). Relevant current state:

- **api, Kobo** (`Kobo/`):
  - `KoboClient` calls only `GET {server}/api/v2/assets/{uid}/?format=json` and `/data/`, through the `"kobo"` named client: 20 s timeout, 10 MB buffer, private-network guard, `SendAsync` error mapping to `KoboError` codes (`kobo.unreachable`, `kobo.unauthorized`, `kobo.form_not_found`, `kobo.unexpected_response`, `kobo.token_unreadable`).
  - `KoboForm.Parse` reads `name`, `content.survey` and `content.choices` into `FormField(Name, Xpath, Type, Label, ListName)` and `FormChoice(ListName, Name, Label)`:
    - `type` has spaces turned into underscores.
    - `ListName` comes from `select_from_list_name` or the second word of `select_one x` / `select_multiple x`.
    - `Label` takes the first non-null string of the translation array, so a missing default-language label silently borrows the next language.
  - The asset's `content` is its latest content, which can be an undeployed draft. `version_id`, `deployed_version_id` and `content.translations` are not read.
- **api, snapshot:**
  - `projects.form_fields` / `form_choices` (jsonb, camelCase) are written inline on creation (`ProjectEndpoints.CreateAsync`), and by `FormSnapshot.Apply` on token replacement, on re-check (`POST /projects/{id}/kobo-check`) and at the start of every sync (`SyncRunner.ExecuteAsync`). A failed asset read fails the sync.
  - Re-check and token replacement return the shared `ProjectDetails`.
  - Saved sampling-frame dimensions and the enumerator question are validated against the snapshot's xpaths.
- **api, ingestion:**
  - `KoboSubmission.Parse` stores `__version__` in `raw_submissions.form_version` (varchar 128, null when missing, not a string, empty or too long).
  - `SubmissionStore.UpsertAsync` is the single write path for webhook and sync.
  - `IngestionScheduler` ticks every 30 s and runs up to `MaxConcurrentRuns` (2) syncs.
  - After a sync with changes, `SyncRunner.QueueRunAsync` queues a run after `FinishAsync`.
  - The webhook stores and answers; it never calls Kobo.
- **analytics:**
  - `pipeline.project_context` reads `projects.form_fields` / `form_choices` for `ctx` and passes `form_fields` to `flatten()` (runs and `sample_runs.py` alike).
  - `flatten` types columns from the current form by xpath; selects are `text`.
  - In `_emit`, each value goes through `writer.convert`, which counts unreadable values for the log.
  - Repeat rows are emitted recursively from their submission.
  - `runner.Form` exposes `questions`, `choices` and `label(question, value)`.
  - The worker has no Kobo token and no Data Protection keys.
- **Database:** DbUp scripts `0001`–`0004`; `SchemaDriftTests` compares the EF model with them. There is no index on `raw_submissions.form_version`.
- **UI:**
  - The Data section (`project-data.ts`) polls the ingestion status every 5 s only while a sync is queued or running.
  - The Overview has the "Re-check the form" button.
  - nginx proxies `/api/` with its default 60 s read timeout.
- **Tests:** `TamizaApiFactory` turns the ingestion scheduler off (`Ingestion:SchedulerEnabled`), so tests that do not synchronize make no background Kobo calls. No test asserts the webhook's 500 ms target today; it was measured on the stack.

**Kobo API** (KPI v2, checked against the `kobotoolbox/kpi` and `formpack` sources):
- **What `__version__` holds.** It is the uid of the deployed `AssetVersion` the XForm was generated from (`v` plus 21 characters). KPI injects it as a hidden calculate row at deployment, and its own exports resolve submission versions the same way.
- **Version detail.** `GET /api/v2/assets/{uid}/versions/{version_uid}/` returns `uid`, `content_hash`, `date_modified`, `date_deployed` and `content`. `date_deployed` is a datetime, or `false` for drafts.
  - It requires `view_asset`, which `view_submissions` implies, so the project's token can read it.
  - A uid that does not belong to the asset gives 404.
  - Versions are only deleted with the asset.
  - Content can reach several megabytes on large forms.
- **Asset detail.** `version_id` is the latest version, which can be an undeployed draft. Its top-level `content` is that draft. `deployed_version_id` is the version of the current deployment.
- **Labels and languages.** Label arrays are aligned with `content.translations`. Index 0 is the default language: KPI moves `settings.default_language` there on save, and `null` is the unnamed language.
- **Names in submissions.** Submission values are `$autovalue || name` for choices, and keys are full paths of `$autoname || name`. The stored `$xpath` prefers `name`, ignores rank and matrix groups and is missing in old versions.
- **"Or other" option.** The row carries `_or_other: true`. The `other` choice is not in `content.choices`; pyxform adds it at deployment with the label `Other` in every language.
- **Edited submissions.** Kobo edits them with the latest deployed version, so they can hold choices of a later version than the one they were made with.
- **How Kobo's exports label answers.** They format each submission with its own version's labels and fall back to the newest deployed version when a submission's version is unknown. A missing label falls back to the name. `select_multiple` labels are joined with a space.
- **Rate limiting.** There is no published limit. The public servers answer 429 when an account makes heavy automated requests.

Requirements are in `specs/ingestion`, `specs/pipeline` and `specs/projects`; motivation in `proposal.md`. User decisions for this change:
- The flattened tables carry only labels, never codes.
- Each answer uses the labels of its own submission's version, even when a later version relabeled the choice.
- Labels are always in the form's default language.

## Goals / Non-Goals

**Goals:**

- Labels come from the exact version an answer was given in, with no change to how submissions are stored or to the webhook's response time.
- Each version definition is fetched once per project; Kobo outages delay labels but never fail ingestion or runs.
- Every answer that does not get its own version's label is visible in the flatten log, never silent.
- One parser for form content (the API's `KoboForm`); the worker only reads stored definitions.

**Non-Goals:**

- Contacting Kobo from the analytics worker, or from the webhook request.
- Re-fetching or diffing definitions of versions already stored (Kobo versions are immutable).
- Sharing fetched versions between projects that use the same form (each project has its own token and permissions).
- Translating question names into question labels, or exposing per-version dictionaries in `ctx`.

## Decisions

### D1. Data model (DbUp `0005_add_choice_labels.sql`)

| Table | Columns | Notes |
|---|---|---|
| `form_versions` | `project_id` (FK cascade), `uid` (varchar 128), `status` (`pending`\|`available`\|`failed`\|`not_found`), `fields` (jsonb, default `[]`), `choices` (jsonb, default `[]`), `deployed_at` (timestamptz, null), `recorded_at`, `fetched_at` (null), `attempts` (int, default 0), `next_attempt_at` (null), `error_code` (varchar 64, null), `error` (text, null) | PK (`project_id`, `uid`). Partial index (`next_attempt_at`) where `status IN ('pending', 'failed')`, for the background pass. `fields` and `choices` use the same camelCase shape as `projects.form_fields` / `form_choices`. |

Other changes:
- `projects` gains `form_version` (varchar 128, null): the uid of the deployed version the snapshot describes. The UI marks that version as current.
- `raw_submissions` gains the index (`project_id`, `form_version`) where `NOT deleted`, for the per-version counts (D7) and the re-recording done at each sync (D4).
- The script backfills `form_versions` with one `pending` row per distinct (`project_id`, `form_version`) already stored, with `next_attempt_at = now()`.

*Alternatives:*
- A `form_versions` jsonb column on `projects` would load every version with every project and make concurrent updates race.
- Storing the raw `content` would make the worker re-implement `KoboForm.Parse` in Python, giving two parsers for the same Kobo format.

### D2. Kobo access and parsing (api)

- **New call.** `KoboClient.GetVersionAsync(server, assetUid, versionUid, token)` calls `GET {server}/api/v2/assets/{uid}/versions/{versionUid}/?format=json`. It goes through `SendAsync`, so it gets the same client, guard, timeout and error mapping.
- **New error codes:**
  - A 404 maps to `kobo.version_not_found`, which marks the version `not_found` (D4).
  - A 429 maps to `kobo.rate_limited` for every call; today it falls into `kobo.unexpected_response`.
  - A body over the 10 MB buffer currently surfaces as `kobo.unreachable`. It maps to `kobo.unexpected_response` with a message naming the size, for both calls.
- **`KoboForm.Parse`** is shared by the asset and the version documents, and changes as follows:
  - **Labels strictly in the default language:** element 0 of a label array when it is a non-empty string, otherwise no label. A plain string label, from older content, is used as is.
  - **Deployed names:** question names are `$autoname || name` and choice names `$autovalue || name`, the values Kobo puts in submissions. When a list repeats a choice name, which XLSForm allows with `allow_choice_duplicates`, the last one wins, as in Kobo's exports.
  - **Paths from nesting:** every path is built from the enclosing `begin_group`, `begin_repeat`, `begin_score`, `begin_rank` and `begin_kobomatrix` rows and their `end_*` rows, instead of `$xpath`. This way, grouped selects match the submission keys in every version.
  - **Older shapes:** a `type` given as an object (`{"select_one": "list"}`) is normalized to the string form.
  - **"Or other" option:** `FormField` gains `OrOther` (default false) from `_or_other`.
  - **Version metadata:** `KoboAsset` also returns `version_id` and `deployed_version_id`. `date_deployed: false` reads as no deployment date.
- **Label text** is kept as written in the form, including any Markdown or HTML, the same as Kobo's own exports.

*Alternative:* Kobo's export API (`/exports/` with a label language) already produces labels. But it is asynchronous, works on whole files rather than per submission, and would replace Tamiza's own flattening.

### D3. Snapshot from the deployed version (api)

`FormSnapshot` (now also used by creation) reads the asset as today, then picks the content in this order:

1. **No deployment** (`deployed_version_id` is null): the asset's `content`; `projects.form_version` is null.
2. **Deployed version is the latest content** (`deployed_version_id = version_id`): the asset's `content`. That definition is also upserted into `form_versions` as `available`. `deployed_at` stays null, because the asset detail does not carry it.
3. **Deployed version already stored as `available`:** its stored definition, with no request to Kobo.
4. **Otherwise:** `GetVersionAsync(deployed_version_id)`. On success, the definition is stored as `available` and used. On any error, the version is recorded as `failed` or `not_found` with the fetcher's rules (D4), and the snapshot falls back to the asset's `content`, today's behavior.

So creation, re-check and syncs fail only when the asset itself cannot be read, as today, and a deployed version is fetched once.

In cases 2–4, `projects.form_version` is set to `deployed_version_id`.

**Lock order.** The `form_versions` upsert runs in its own short transaction, committed before the snapshot is saved on `projects`. A webhook transaction holds a key-share lock on the project row (from the foreign key) and may hold an uncommitted `form_versions` row. With this order, the snapshot never waits on that row while holding the project row, so the two cannot deadlock.

*Alternative:* keeping the draft content. It describes questions that no submission can answer yet, and it made the field check report metrics as available before the redeploy that adds them.

### D4. Recording and fetching versions (api)

- **Recording.** `SubmissionStore.UpsertAsync` runs, in the same transaction as the submissions:
  ```sql
  INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at)
  SELECT DISTINCT ... FROM unnest(@form_versions)
  WHERE uid IS NOT NULL
  ON CONFLICT DO NOTHING
  ```
  - It runs after the submissions upsert, with the uids sorted, so concurrent transactions take row locks in the same order.
  - For a webhook this is one primary-key probe, so the 500 ms response target holds.
- **Re-recording.** Each sync also runs the same insert over `SELECT DISTINCT form_version FROM raw_submissions` for the project, using the D1 index. This covers submissions stored by an older image after a rollback, or by any path that skipped the recording.
- **`FormVersionFetcher.FetchAsync(project, token, mode, budget)`** selects the project's versions to fetch:
  - `due`: `pending`, or `failed` with `next_attempt_at <= now`.
  - `all`: everything not `available`.

  For each version it calls `GetVersionAsync`:
  - **Success:** `available`, with `fields`, `choices`, `deployed_at` (from `date_deployed`) and `fetched_at`; the error is cleared.
  - **404:** the fetcher first confirms that the asset itself is readable, with one asset request per pass. If the asset is readable, the version becomes `not_found` with no next attempt. If not, the asset's error is handled as a server-wide error. A form deleted in Kobo, or no longer shared with the token, therefore never marks its versions as not found.
  - **Any other error:** `failed`, with `attempts + 1`, `error_code` and `error`. The next attempt is at `now + min(1 h × 2^(attempts − 1), 24 h)`, so a permanent error, such as an oversized document, is retried daily rather than hourly forever.

  Server-wide errors (`unreachable`, `unauthorized`, `token_unreadable`, `address_not_allowed`, `rate_limited`, and an unreadable asset) stop the pass and mark every remaining selected version `failed` with the same reason. This way an outage costs one timeout, not one per version.

  When the time `budget` runs out, the versions not yet requested are set to `pending` with `next_attempt_at = now`, so the worker takes them next.

  Every write uses `WHERE status <> 'available'`, so two concurrent passes over the same version cannot undo each other.

  `OperationCanceledException` from the stopping token is always rethrown. This way, a sync interrupted by an API shutdown is still recorded as interrupted, not as succeeded.
- **During synchronizations.** `SyncRunner.ExecuteAsync` re-records versions, then calls `FetchAsync(due)` after the page loop and deletion marking. Errors other than cancellation are logged and never change the sync's outcome. Because this runs before `FinishAsync` and `QueueRunAsync`, a run queued by the sync sees the definitions.
- **Between synchronizations.** A new hosted service, `FormVersionWorker`, has its own loop:
  - It is enabled and timed by the scheduler's options (`Ingestion:SchedulerEnabled`, `Ingestion:Tick`), so tests stay quiet unless they turn it on, and can shorten the tick.
  - It waits for migrations like `IngestionScheduler`.
  - Each tick it handles, one project at a time, the projects that have due versions and no sync `running`, with `FetchAsync(due)`. A queued sync does not block it, because writes are guarded.

  It is separate from the sync slots, so long full syncs of other projects never delay the "within two minutes" target.
- **Re-check.** `RecheckAsync` applies the snapshot and then calls `FetchAsync(all)` synchronously with a 20 s budget, well under nginx's 60 s proxy timeout. Versions left over become due at once for the worker.
- **Token replacement.** `ReplaceTokenAsync` sets `failed` and `not_found` versions to `pending` with `next_attempt_at = now`, so the worker retries them within a tick.
- **Status in the project details.** `ProjectDetails` gains `unavailableFormVersions`: the number of versions used by non-deleted submissions whose definition is not `available`, counting a version with no row as pending. Re-check, token replacement and the project page show it.

*Alternatives:*
- Fetching from the webhook request breaks the 500 ms target and couples deliveries to Kobo's availability.
- Fetching only during syncs leaves webhook-only versions without labels for up to 12 h.
- Pulling the next sync forward when a webhook brings a new version adds an extra full submission read only to get one small document.
- Fetching when a run is queued would make "Run now" wait on Kobo.

### D5. Labels in the flatten step (analytics)

- **Loading.** `labels.load(connection, project_id, snapshot)` reads every `available` row of the project's `form_versions`, together with its `deployed_at`. The snapshot is the one `project_context` already read, so `ctx.form` and the fallback always agree. These are parsed lists, small next to the submissions, and loading all of them avoids a separate query that could disagree with the submissions streamed later.
  - It builds a lookup per definition: `xpath → (multiple: bool, {name: label})`.
  - A question qualifies when its type starts with `select_one` or `select_multiple`, it does not contain `from_file`, and its `listName` has at least one choice. Lists defined elsewhere, such as external choices or choices from earlier answers, never qualify.
  - Choices with an empty label are left out, so they count as "without a label".
  - A question with `orOther` also maps `other` to `Other`.
  - Kobo's rank and score levels are not `select_*` rows in the content, so they never qualify.

  The load happens inside `do_flatten` of both `pipeline.py` and `sample_runs.py`, so a failure is recorded as the flatten step's error.
- **Version of a submission.** The run and sample queries also select `raw_submissions.form_version`, the value the API derived with its own rule. The submissions factory yields `(data, form_version)` pairs, and the flatten unit tests' factories pair each dict with its version. No second implementation of the rule exists.
- **Substitution.**
  - `flatten(form_fields, submissions, out_dir, labels=None)` resolves each submission's definition once: its version's if available, otherwise the snapshot, recording the fallback by version. The definition is passed down `_emit` to repeat rows.
  - For each value at a path:
    - **The definition has a select at that path:** a name found there takes its label. A name missing there takes the snapshot's label for the same path when there is one, counted as labeled from the current form; this handles submissions edited with a later version. Otherwise the code is kept and counted.
    - **The definition has no select at that path, but the snapshot has:** the snapshot's labels are used, counted as above. This covers questions added by an edit with a later version.
    - **Otherwise:** the value passes through unchanged and uncounted.
  - Before `writer.convert`:
    - **select_one:** the name is replaced as above.
    - **select_multiple:** the value is split on whitespace, each name is replaced as above, and the result is joined with `; `.

    Each cell counts at most once per outcome: labeled from the current form, or kept. Values that are not strings pass through; Kobo always sends strings for selects.
  - Column types and order still come from the current form, so naming and types do not depend on which versions are present.
- **Log lines,** appended to the existing flatten log:
  - `main.water_source: 1 value not in its form version; labeled from the current form.`
  - `main.has_water: 2 values without a label; kept as codes.`
  - `Form version vB: 15 submissions used the current form (definition pending).`
  - `3 submissions without a form version used the current form.`
- **Compatibility.** `labels=None` keeps today's behavior, so the existing flatten unit tests stay valid and new tests pass a lookup explicitly.

*Alternatives:*
- Rewriting `raw_submissions.data` at ingestion loses the codes for good and makes the stored copy differ from Kobo.
- Using the latest version's labels for every answer was rejected by the user in favor of each answer's own version.
- Separators: a plain space, as Kobo uses for names and in its exports' label columns, is ambiguous because labels contain spaces. A JSON list would turn the column into non-text data that dashboards and scripts would have to decode.
- Keeping the code whenever the submission's version lacks it would leave edited submissions with codes, although the current form knows the label.

### D6. `ctx.form` (analytics)

- `runner.Form` keeps describing the current form (the snapshot) and gains `name(question, label)`, the code a label stands for.
  - **Search order:** the current form first, then the available versions from the newest deployment to the oldest. Versions without a deployment date go last.
  - **Ambiguity:** within one definition, a label that two choices share is ambiguous and is skipped. A label used by an older version, such as `Si`, therefore still resolves after a later version relabeled it to `Sí`, and joins on codes keep those rows.
  - **"Or other":** `Other` resolves to `other` on "or other" questions.
  - **No match:** `None`.
- **How the context gets it.** The pipeline precomputes this reverse map per question path into `context.json` (`label_codes`), from the same definitions D5 loaded, so the runner does not need the versions themselves.
- `label(question, value)` is unchanged; it still serves codes coming from reference tables.
- `ctx.form.questions` and `ctx.form.choices` still describe the current form only.

*Alternative:* exposing every stored version in `ctx`. That widens the contract when one reverse lookup covers the need, which is joining labeled answers with tables keyed by codes.

### D7. API surface

| Method and path | Minimum role | Purpose |
|---|---|---|
| `GET /api/v1/projects/{id}/form-versions` | viewer | Returns `{ versions: [{ uid, current, deployedAt, submissions, status, errorCode, error, nextAttemptAt }], withoutVersion }`. The list holds the versions used by non-deleted submissions (from `raw_submissions` left-joined to `form_versions`; a version with no row reads as `pending`) plus the current one. Order: the current version first, then newest deployment first, then versions without a deployment date by uid. |
| `ProjectDetails` (existing, returned by `GET /projects/{id}`, re-check and token replacement) | viewer | Gains `unavailableFormVersions` (D4). |

Error codes reuse the `kobo.*` set. `kobo.version_not_found` and `kobo.rate_limited` are new and get `en.json` translations.

### D8. UI

The Data section gains a **Form versions** card below the ingestion status.
- **Refresh.** It has its own poll: every 5 s while any version is `pending` or a sync is active, and otherwise only when the section opens or a sync ends. A version fetched by the worker after a webhook therefore shows up without a reload.
- **Contents:**
  - One row per version: a shortened uid (full value on hover and copy), a *current* badge, the deployment date, the number of submissions, and a status badge (*Labels available*, *Pending*, *Failed*, *Not found*).
  - For failed versions, the translated reason and the next attempt.
  - A line for submissions without a version.
  - A hint that re-checking the form retries failed and not found versions, with a link to the Overview.
- **Layout.** On phones the table becomes stacked cards, like the sync log.
- **Overview.** It shows `unavailableFormVersions` when it is not zero, including right after a re-check.

### D9. Test strategy

- **API unit:**
  - `KoboFormTests`:
    - strict default-language labels (a null default with an English label gives no label);
    - paths from nesting and deployed names;
    - older shapes;
    - `OrOther`;
    - duplicate choice names;
    - `version_id` / `deployed_version_id` parsed.
  - Error mapping for 404 → `kobo.version_not_found`, 429 → `kobo.rate_limited`, and oversized bodies.
- **API integration** (Testcontainers + `KoboStub`, which gains a `/versions/{uid}/` mapping, per-version form JSON and version ids in `FormJson`):
  - A sync stores submissions of `vA` and `vB` and fetches both before the after-sync run is queued. The two definitions keep their own labels for a relabeled choice.
  - A webhook with a new version records it as `pending`, and the worker fetches it with a shortened tick.
  - A new timing test: a webhook carrying a new version, delivered while a sync holds a page transaction, answers within 500 ms.
  - Kobo down: the sync succeeds, versions are `failed`, the retry delay doubles, and one request is made per pass.
  - 404 with a readable asset → `not_found`. 404 with an unreadable asset → `failed`.
  - Cancellation during the fetch records the sync as interrupted.
  - Re-check retries `failed` and `not_found` versions within its budget, leaves the rest due, and reports `unavailableFormVersions`.
  - Token replacement makes `failed` and `not_found` versions due.
  - Snapshot:
    - it comes from the deployed version when the draft differs;
    - it is read from `form_versions` on the next sync, with no second request;
    - it falls back to the asset's content when that version cannot be read;
    - creation and token replacement record `form_version`.
  - The endpoint: roles, counts, `withoutVersion`, a version with no row shown as pending, and the ordering.
  - Schema:
    - `SchemaDriftTests`;
    - a `SchemaMigratorTests` upgrade from `0004` with stored submissions that asserts the backfilled `pending` rows;
    - a project with a grouped enumerator question and a sampling frame, checked to still validate after the first snapshot with nesting-built paths.
- **Python unit** (`test_flatten.py`, `test_labels.py`, `test_runner.py`):
  - Per-version labels, including the same code with different labels in two versions.
  - `select_multiple` order and separator.
  - Repeat rows use their submission's version.
  - A removed question labeled from its own version.
  - `other` labeled `Other` on an "or other" question.
  - An edited submission whose code exists only in the current form.
  - Fallback to the snapshot, with log lines.
  - Unknown codes kept and counted; `from_file` questions and lists without choices untouched.
  - `ctx.form.name`: current and earlier-version labels, unknown and repeated labels, `Other`.
- **Python integration:**
  - A run and a sample test on seeded `form_versions` show labels in the published `p_<id>_raw` / `clean` tables and the test preview.
  - The existing assertions on `cali` / `buga` move to `Cali` / `Buga`, or to `ctx.form.name` where they join reference codes.
- **UI:** the form versions card per role and status, and the re-check message, at 360 px and 1280 px.
- **Stack verification:**
  - The fake Kobo gains `/versions/{uid}/` and submissions spread over three versions, one relabeling a choice.
  - Full sync of 500,000 submissions, then a run. Record the flatten duration against the previous measurement in this design.
  - Stop the fake Kobo after a webhook brings a fourth version, and check the sync still succeeds and the run logs the fallback.

## Risks / Trade-offs

- **[Existing scripts compare select columns with codes and silently stop matching]** → Marked BREAKING in the proposal. `docs/scripts.md` gains an "Upgrading" note with before/after examples and `ctx.form.name` for joins. The README status mentions it.
- **[A relabeled choice splits one category in two (`Si` / `Sí`)]** → This is the user's chosen semantics. The docs show how to normalize in script 1, and the form versions card makes the versions visible.
- **[Two choices of a list share a label]** → Their answers become indistinguishable in the tables. `ctx.form.name` returns `None` for that label, and the raw JSON keeps the codes. This is documented.
- **[A label containing `; ` makes `select_multiple` cells ambiguous to split]** → Documented. Splitting into per-choice columns is listed as later work.
- **[Older version documents use a different content shape]** → `KoboForm.Parse` builds paths from nesting with deployed names, accepts string labels and object types. Fixtures for both shapes; stack check against a real Kobo server when a token is available.
- **[Building paths from nesting changes the snapshot's xpaths for forms where `$xpath` disagreed with the submission keys]** → Those xpaths were already wrong for flattening, since the keys are what Kobo sends. The enumerator question and the sampling-frame dimensions are stored as xpaths. If one of them changes, the field check reports the per-enumerator detail as unavailable, or saving the frame names the dimension, until someone selects the field again. The upgrade test covers a grouped enumerator question and a frame dimension. In ordinary forms, the built path equals `$xpath`.
- **[A text answer to a question that a later version turned into a select]** → It is looked up in the current form's list and counted as kept. This is rare, and visible in the log.
- **[Legacy submissions carry numeric or foreign version ids]** → Kobo answers 404, the version shows as not found, and its submissions use the current form, which matches Kobo's own fallback.
- **[`select_multiple` cells differ from Kobo's export, which joins labels with a space]** → Documented in `docs/scripts.md` with the reason.
- **[A question changed type between versions (select to integer)]** → The column type comes from the current form, so labels written into an integer column count as unreadable in the existing log line. This is rare and visible.
- **[Snapshot from the deployed version costs one more request when a draft exists]** → Only the first time a deployed version is seen. Later snapshots read it from `form_versions`.
- **[Many versions after the upgrade (a form redeployed dozens of times)]** → Each version is fetched once. Server-wide errors stop the pass after one request. Re-check stops at its time budget and hands the rest to the worker.
- **[Two writers fetch the same version (a sync, the worker and a re-check)]** → At worst a duplicate request. Writes are guarded by `status <> 'available'`, and version content never changes.
- **[A label cannot be updated after a version is fetched]** → Kobo versions are immutable, and a fix in Kobo creates a new version. Not a real limitation.

## Migration Plan

- `0005_add_choice_labels.sql` creates `form_versions`, adds `projects.form_version` and the partial index on `raw_submissions`, and records the versions of stored submissions as `pending`. It runs in the API's startup migration, like earlier scripts. The index build on large tables runs in the same transaction; a 500,000-row project takes seconds (record the time during stack verification).
- After the upgrade, `FormVersionWorker` fetches the pending versions within minutes. Projects without a reachable Kobo keep them as `failed` and fall back to the current form.
- Published tables keep codes until each project's next run. Admins can use "Run now" to switch at once. Release notes tell analysts to update scripts that compare select columns with codes before that run.
- **Rollback:** previous images ignore `form_versions` and `projects.form_version`, and their next run republishes codes.
  - Submissions stored while rolled back record no versions. On rolling forward, each sync re-records them (D4).
  - To remove the objects by hand, also delete the `0005_add_choice_labels.sql` row from `tamiza.schema_versions`, so that rolling forward recreates them.

## Open Questions

- Whether the form versions card should offer a per-version "Retry" button in addition to re-check. Not required by the specs; add it if users ask.
