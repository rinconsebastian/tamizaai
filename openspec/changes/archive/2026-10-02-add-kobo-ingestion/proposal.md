# Proposal

## Why

Projects are linked to a Kobo form and admins already see a webhook URL and credentials, but nothing receives the submissions yet. The script pipeline and all three dashboards read raw submissions, so Tamiza needs a reliable copy of each form's data: one that catches every new record through the webhook, recovers what the webhook missed, backfills what existed before the project was created, and follows deletions made in Kobo.

## What Changes

- **Webhook intake.** `POST /api/v1/webhooks/kobo/{projectId}` implements the contract already shown to admins: HTTP Basic with username `tamiza` and the project's secret. A valid JSON submission is stored as raw JSON and answered with 200 at once; nothing else runs on a webhook. A wrong or missing secret, or an unknown project, gets 401 and stores nothing.
- **One copy per submission.** Submissions are stored with upsert, keyed by Kobo's submission ID (`_id`) within the project. A resent record never creates a duplicate, and a submission edited in Kobo (which gets a new `_uuid`) updates the stored row instead of adding one.
- **Periodic synchronization.** The API reads submissions from the Kobo API v2 for each project on a schedule: every 12 hours by default, configurable per project by an admin. Each synchronization fetches submissions newer than the last successful one, minus a safety margin, so records the webhook missed arrive anyway. A project's first synchronization fetches everything, which backfills submissions made before the project existed.
- **Manual synchronization.** Admins and analysts can run "Sync now", and a full resynchronization that re-reads every submission (to pick up edits the webhook did not deliver).
- **Deletion reconciliation.** Once a day by default (configurable per project), the system compares the submission IDs in Kobo with the stored ones and marks missing ones as deleted (soft delete). A record that reappears in Kobo is restored.
- **Synchronization log.** Every synchronization and reconciliation is recorded with its kind, trigger, start and end, created, updated and deleted counts, and the error if it failed. Members see the log and the ingestion status (submission count, last webhook, last and next synchronization) on a new **Data** section of the project.
- **Record origin.** Each raw submission records how it first arrived (`webhook` or `sync`), the form version (`__version__`), Kobo's submission time and when Tamiza received and last updated it.
- **Form snapshot refreshed on synchronization.** Each synchronization re-reads the form definition, so the field check stays current after the form is redeployed in Kobo.

Not in this change: flattening submissions into `p_<id>_raw` and starting the script chain after a synchronization (`add-python-pipeline`); downloading attachments (photos, audio); audit events (`add-audit-log`); XML webhook payloads.

## Capabilities

### New Capabilities

- `ingestion`: webhook intake and its authentication, raw submission storage and identity, periodic, manual and full synchronization, deletion reconciliation, the synchronization log and ingestion status, and per-project schedule settings.

### Modified Capabilities

- `projects`: the form field check also refreshes the form snapshot on every synchronization, not only on creation and manual re-check.

## Impact

- **API:** new public endpoint `POST /api/v1/webhooks/kobo/{projectId}` (Basic authentication, no bearer token); new project endpoints for ingestion status, schedule settings, starting a synchronization and listing the synchronization log. A hosted background service in `api` runs scheduled and queued synchronizations. `KoboClient` gains paged, streamed reads of submissions and of submission IDs.
- **Database:** new tables `raw_submissions` and `syncs` in schema `tamiza` (DbUp script `0002`); `projects` gains schedule columns and the time of the last webhook.
- **UI:** a new **Data** section in the project page (status, "Sync now", full resync, synchronization log, schedule settings for admins). The webhook section drops its "available in a later release" note and shows when the last webhook arrived.
- **Docs:** `docs/kobo.md` explains the REST Service payload type (JSON), synchronization, reconciliation and troubleshooting; `README.md` status updated.
- **Dependencies:** none new.
- **Performance:** the webhook does one indexed upsert and answers within the 500 ms target. Synchronization handles the reference volume of 500,000 submissions per project by paging and streaming.
