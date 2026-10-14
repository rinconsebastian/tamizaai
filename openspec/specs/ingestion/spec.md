# ingestion Specification

## Purpose

Keep a complete, current copy of each project's Kobo submissions: received through Kobo's webhook as they happen, recovered by periodic synchronization when the webhook misses them, and marked as deleted when they are removed in Kobo.

## Requirements

### Requirement: Webhook intake
The system SHALL accept Kobo REST Service deliveries at `POST /api/v1/webhooks/kobo/{projectId}`, authenticated with HTTP Basic: the username `tamiza` and the project's current webhook secret. The endpoint SHALL NOT require a bearer token. A request with valid credentials SHALL store the submission and respond 200 without running any further processing (no flattening or scripts). A request with a missing or wrong username or secret, or for a project that does not exist, SHALL respond 401 and store nothing; the response SHALL NOT reveal whether the project exists.

#### Scenario: Valid submission
- **WHEN** Kobo sends a JSON submission with the correct username and secret
- **THEN** the system stores the raw JSON for the project and responds 200

#### Scenario: Invalid secret
- **WHEN** a request arrives with a wrong or missing secret
- **THEN** the system responds 401 and stores nothing

#### Scenario: Unknown project
- **WHEN** a request arrives for a project ID that does not exist
- **THEN** the system responds 401, the same as for a wrong secret, and stores nothing

#### Scenario: Regenerated secret
- **WHEN** an admin has regenerated the webhook secret and Kobo still sends the previous one
- **THEN** the system responds 401 and stores nothing

#### Scenario: Response time
- **WHEN** a valid submission arrives while the project is synchronizing
- **THEN** the system responds within 500 ms

### Requirement: Webhook payload validation
The webhook SHALL accept only a JSON object that contains Kobo's submission ID (`_id`) and UUID (`_uuid`). A body that is not JSON SHALL get 415, a JSON body that is not an object or lacks `_id` or `_uuid` SHALL get 400 with a message naming the problem, and a body larger than 10 MB SHALL get 413. In every rejected case nothing is stored. Credentials SHALL be checked before the body is read.

#### Scenario: XML payload
- **WHEN** the REST Service is configured to send XML and delivers a submission
- **THEN** the system responds 415 with a message that the service must use the JSON type, and stores nothing

#### Scenario: Missing submission ID
- **WHEN** a JSON object without `_id` arrives with valid credentials
- **THEN** the system responds 400 with a message that `_id` is missing, and stores nothing

#### Scenario: Body too large
- **WHEN** a body larger than 10 MB arrives
- **THEN** the system responds 413 and stores nothing

### Requirement: One stored record per submission
The system SHALL keep at most one record per Kobo submission in a project, identified by the submission's `_id`. Receiving a submission that is already stored, by webhook or synchronization, SHALL replace the stored JSON with the received one. Receiving a submission that is marked as deleted SHALL restore it.

#### Scenario: Duplicate record
- **WHEN** a record arrives whose `_id` is already stored in the project
- **THEN** the stored record is updated with the received JSON and no second record is created

#### Scenario: Submission edited in Kobo
- **WHEN** a submission edited in Kobo arrives with the same `_id` and a new `_uuid`
- **THEN** the stored record is updated, keeps a single row, and records the new `_uuid`

#### Scenario: Same form in two projects
- **WHEN** two projects use the same Kobo form and a submission arrives for each
- **THEN** each project stores its own copy, independent of the other

#### Scenario: Deleted record arrives again
- **WHEN** a submission marked as deleted is received again by webhook or synchronization
- **THEN** it is no longer marked as deleted

### Requirement: Record origin
Each stored submission SHALL record how it first arrived (`webhook` or `sync`), the form version it was submitted with (`__version__`), Kobo's submission time (`_submission_time`), its current `_uuid`, when Tamiza first received it and when Tamiza last changed it. Later updates SHALL NOT change the recorded origin or first reception time.

#### Scenario: Submission first received by synchronization
- **WHEN** a submission missed by the webhook is stored by a synchronization and later delivered again by the webhook
- **THEN** its origin stays `sync` and its last-changed time is updated

#### Scenario: Submission without a form version
- **WHEN** a submission arrives without `__version__`
- **THEN** it is stored with no form version

### Requirement: Periodic synchronization
The system SHALL synchronize each project with the Kobo API v2 on a schedule, every 12 hours by default. A synchronization SHALL read the submissions whose Kobo submission time is no earlier than the start of the project's last successful synchronization minus a safety margin of one hour, and store each one as described in "One stored record per submission". When the project has no successful synchronization yet, the synchronization SHALL read every submission of the form. Synchronizations SHALL run while the API is running, without any user signed in.

#### Scenario: Lost webhook
- **WHEN** a submission did not arrive by webhook
- **THEN** the next synchronization stores it with origin `sync`

#### Scenario: Backfill on project creation
- **WHEN** a project is created for a form that already has submissions
- **THEN** its first synchronization starts without waiting for the schedule and stores every existing submission

#### Scenario: Scheduled run
- **WHEN** the project's synchronization interval has passed since its last synchronization started
- **THEN** a new synchronization runs with trigger `schedule`

### Requirement: Synchronization schedule settings
A project `admin` SHALL be able to set the synchronization interval and the deletion reconciliation interval of the project, each as a whole number of hours from 1 to 168. The defaults SHALL be 12 hours for synchronization and 24 hours for reconciliation. A new interval SHALL apply from the next scheduled run, computed from the start of the last run of that kind. Every member SHALL be able to read the settings.

#### Scenario: Interval changed
- **WHEN** an admin sets the synchronization interval to 6 hours
- **THEN** the next scheduled synchronization is due 6 hours after the last one started

#### Scenario: Interval out of range
- **WHEN** an admin sets an interval of 0 or 200 hours
- **THEN** the request fails with a message giving the allowed range, and the settings are unchanged

#### Scenario: Analyst changes the interval
- **WHEN** an `analyst` tries to change the synchronization interval
- **THEN** the API responds 403 and the settings are unchanged

### Requirement: Manual synchronization
A project `admin` or `analyst` SHALL be able to start a synchronization at once ("Sync now"), and a full synchronization that reads every submission of the form regardless of the last successful one. A full synchronization SHALL also mark as deleted the stored submissions that are no longer in Kobo, as in "Deletion reconciliation". The request SHALL return without waiting for the synchronization to finish.

#### Scenario: Sync now
- **WHEN** an `admin` or `analyst` clicks "Sync now"
- **THEN** a synchronization with trigger `manual` starts within seconds and appears in the synchronization log

#### Scenario: Full synchronization
- **WHEN** an analyst starts a full synchronization and a stored submission was edited in Kobo without a webhook delivery
- **THEN** the stored record is updated with the edited JSON

#### Scenario: Viewer requests a synchronization
- **WHEN** a `viewer` tries to start a synchronization
- **THEN** the API responds 403 and nothing starts

### Requirement: One synchronization at a time
The system SHALL run at most one synchronization or reconciliation per project at any time. A manual request while one is queued or running SHALL be refused with a message that a synchronization is already in progress. A scheduled run that falls due while another is running SHALL wait until it ends. A run interrupted by an API restart SHALL be recorded as failed, and SHALL NOT block later runs.

#### Scenario: Sync now while one is running
- **WHEN** an analyst clicks "Sync now" while a synchronization of the project is running
- **THEN** the request fails with a message that a synchronization is already in progress, and no second one starts

#### Scenario: API restarted during a synchronization
- **WHEN** the API restarts while a synchronization is running
- **THEN** that synchronization appears in the log as failed because it was interrupted, and the next one runs normally

### Requirement: Deletion reconciliation
The system SHALL periodically, once a day by default, compare the submission IDs present in Kobo with the stored ones and mark as deleted the stored submissions that no longer exist in Kobo. Deleted submissions SHALL be kept, flagged as deleted with the time they were marked. The system SHALL mark nothing when it could not read the complete list of IDs from Kobo, and SHALL record that reconciliation as failed.

#### Scenario: Submission deleted in Kobo
- **WHEN** a submission stored in Tamiza was deleted in Kobo and a reconciliation runs
- **THEN** the stored submission is marked as deleted and counted in the reconciliation's deleted count

#### Scenario: Incomplete listing
- **WHEN** Kobo stops answering halfway through the list of IDs
- **THEN** no submission is marked as deleted and the reconciliation is recorded as failed with the reason

#### Scenario: Form emptied in Kobo
- **WHEN** every submission was deleted in Kobo and Kobo reports zero submissions
- **THEN** every stored submission of the project is marked as deleted

### Requirement: Synchronization failures
When a synchronization or reconciliation fails, because the stored Kobo token cannot be read, Kobo rejects it, the server cannot be reached or it answers unexpectedly, the system SHALL record the failure with a message stating the reason, keep the submissions stored before the failure, and SHALL NOT count the failed run as the last successful synchronization. A failed scheduled run SHALL be retried after one hour, or after the project's interval when that is shorter.

#### Scenario: Token rejected
- **WHEN** Kobo rejects the project's token during a synchronization
- **THEN** the synchronization is recorded as failed with a message that the token has no access to the form

#### Scenario: Failure halfway
- **WHEN** a synchronization stores 3 pages of submissions and then the Kobo server stops answering
- **THEN** the submissions of the 3 pages stay stored, the synchronization is recorded as failed, and the next synchronization reads again from the last successful one

#### Scenario: Retry after failure
- **WHEN** a scheduled synchronization with a 12-hour interval fails
- **THEN** the next scheduled attempt is due one hour later

### Requirement: Synchronization log
The system SHALL record each synchronization and reconciliation with its kind (`incremental`, `full` or `reconciliation`), its trigger (`schedule`, `manual` or `initial`), the user who requested it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the numbers of created, updated and deleted records, and the error when it failed. A submission received with the same JSON as stored SHALL count as neither created nor updated. Every project member SHALL be able to read the log, newest first, in pages.

#### Scenario: Successful synchronization recorded
- **WHEN** a synchronization reads 10 submissions, of which 4 are new, 2 changed and 4 identical to the stored ones
- **THEN** the log shows it as succeeded with 4 created, 2 updated and 0 deleted, and its start and end times

#### Scenario: Failed synchronization recorded
- **WHEN** a synchronization fails because the Kobo server cannot be reached
- **THEN** the log shows it as failed with the error message

#### Scenario: Viewer reads the log
- **WHEN** a `viewer` opens the project's synchronization log
- **THEN** they see every recorded synchronization of the project, newest first

### Requirement: Ingestion status
Every project member SHALL be able to see the project's ingestion status: the number of stored submissions not marked as deleted, the number marked as deleted, when the last webhook delivery was accepted, the last synchronization with its status, when the next scheduled synchronization and reconciliation are due, and whether a synchronization is queued or running.

#### Scenario: Webhook never received
- **WHEN** no webhook delivery has been accepted for the project
- **THEN** the status shows that no webhook has been received yet

#### Scenario: Status after a synchronization
- **WHEN** a synchronization that stored 120 submissions has finished and 3 of them were later marked as deleted
- **THEN** the status shows 117 submissions, 3 deleted, and the finished synchronization as the last one

### Requirement: Form versions in use
The system SHALL record every form version (`__version__`) that a project's stored submissions were made with, at the moment a submission with that version is stored, whether it arrived by webhook or by synchronization. Recording a version SHALL NOT contact Kobo, so webhook deliveries keep their response time. A submission without a form version SHALL NOT record any version.

#### Scenario: Webhook with a new version
- **WHEN** Kobo delivers by webhook the first submission made with the form version `vB`
- **THEN** the system responds 200 without contacting Kobo, and the project records `vB` as a version whose definition is pending

#### Scenario: Submission without a form version
- **WHEN** a submission without `__version__` is stored
- **THEN** no form version is recorded for it

#### Scenario: Version already recorded
- **WHEN** another submission made with the recorded version `vA` is stored
- **THEN** the project still records `vA` once

### Requirement: Form version definitions
For each recorded form version, the system SHALL fetch that version's definition from the Kobo API and keep its questions and choice lists, with each choice's name and its label in the form's default language. A synchronization SHALL fetch the project's pending versions before it ends and before it queues a run. Versions recorded between synchronizations SHALL be fetched in the background within two minutes. A fetched definition SHALL NOT be fetched again.

#### Scenario: Versions fetched by a synchronization
- **WHEN** a synchronization stores submissions made with the versions `vA` and `vB`, and neither definition is kept yet
- **THEN** both definitions are fetched and kept before the synchronization ends, and the run it queues uses them

#### Scenario: Version that arrived by webhook
- **WHEN** a webhook delivery records the version `vC` and the next synchronization is due in 10 hours
- **THEN** the definition of `vC` is fetched and kept within two minutes

#### Scenario: Definition fetched once
- **WHEN** the definition of `vA` is kept and later synchronizations store more submissions made with `vA`
- **THEN** the system does not request `vA` from Kobo again

#### Scenario: Relabeled choice
- **WHEN** the choice `1` of the list `yes_no` is labeled `Si` in version `vA` and `Sí` in the later version `vB`
- **THEN** the project keeps the label `Si` for `vA` and `Sí` for `vB`

### Requirement: Form version fetch failures
A failed fetch of a version definition SHALL NOT fail the synchronization that attempted it, and SHALL NOT discard any stored submission. A version that could not be read SHALL be marked as failed with the reason and retried after one hour, then at intervals that double up to one day. A version that Kobo reports as not existing while the form itself is readable SHALL be marked as not found and SHALL NOT be retried automatically.

#### Scenario: Kobo unreachable while fetching
- **WHEN** a synchronization stores 40 submissions and then cannot reach the Kobo server to fetch the version `vB`
- **THEN** the 40 submissions stay stored, the synchronization is recorded as succeeded, and `vB` is marked as failed with a message that the server could not be reached

#### Scenario: Retry after an hour
- **WHEN** the fetch of `vB` failed and one hour has passed
- **THEN** the system tries to fetch `vB` again without waiting for the next synchronization

#### Scenario: Repeated failures
- **WHEN** the fetch of `vB` has failed three times in a row
- **THEN** the next attempt is due four hours after the third failure

#### Scenario: Version unknown to Kobo
- **WHEN** Kobo answers that the version `vB` does not exist, and the form itself can be read
- **THEN** `vB` is marked as not found and is not requested again until someone re-checks the form or replaces the token

#### Scenario: Form no longer readable
- **WHEN** Kobo answers that the version `vB` does not exist, and the form itself can no longer be read with the token
- **THEN** `vB` is marked as failed with the form's error, not as not found

### Requirement: Form version retry on re-check
When an `admin` or `analyst` re-checks the Kobo connection, the system SHALL also try again to fetch every recorded version whose definition is failed or not found, and SHALL report how many definitions are still unavailable afterwards.

#### Scenario: Re-check retries versions
- **WHEN** an analyst re-checks the form while `vB` is marked as failed and `vD` as not found
- **THEN** the system tries to fetch both again, and the re-check reports how many of the two are still unavailable

#### Scenario: Version restored by re-check
- **WHEN** `vB` failed because the token was rejected, an admin replaces the token, and an analyst re-checks the form
- **THEN** the definition of `vB` is fetched and the version is shown as available

#### Scenario: Token replaced
- **WHEN** an admin replaces the token while `vB` is marked as failed and nobody re-checks the form
- **THEN** the system tries to fetch `vB` again within two minutes

### Requirement: Form version status
Every project member SHALL be able to see the form versions that the project's non-deleted submissions use. For each version, the system SHALL show its identifier, its deployment date when known, its number of non-deleted submissions, and the status of its definition: `available`, `pending`, `failed` with the reason and the time of the next attempt, or `not_found`. The list SHALL also show how many non-deleted submissions have no form version.

#### Scenario: Viewer reads the versions
- **WHEN** a `viewer` opens the Data section of a project with 120 submissions made with `vA`, whose definition is kept, and 15 made with `vB`, whose fetch failed
- **THEN** they see `vA` as available with 120 submissions, and `vB` as failed with 15 submissions, the reason and the time of the next attempt

#### Scenario: Submissions without a version
- **WHEN** 3 of the project's non-deleted submissions have no form version
- **THEN** the list shows that 3 submissions have no form version
