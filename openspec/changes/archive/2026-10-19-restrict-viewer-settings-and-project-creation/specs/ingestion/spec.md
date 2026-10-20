# Spec Delta

## MODIFIED Requirements

### Requirement: Synchronization schedule settings
A project `admin` SHALL be able to set the synchronization interval and the deletion reconciliation interval of the project, each as a whole number of hours from 1 to 168. The defaults SHALL be 12 hours for synchronization and 24 hours for reconciliation. A new interval SHALL apply from the next scheduled run, computed from the start of the last run of that kind. A project `analyst` or `admin` SHALL be able to read the settings, and the API SHALL respond 403 to a `viewer`.

#### Scenario: Interval changed
- **WHEN** an admin sets the synchronization interval to 6 hours
- **THEN** the next scheduled synchronization is due 6 hours after the last one started

#### Scenario: Interval out of range
- **WHEN** an admin sets an interval of 0 or 200 hours
- **THEN** the request fails with a message giving the allowed range, and the settings are unchanged

#### Scenario: Analyst changes the interval
- **WHEN** an `analyst` tries to change the synchronization interval
- **THEN** the API responds 403 and the settings are unchanged

#### Scenario: Viewer reads the settings
- **WHEN** a `viewer` asks the API for the project's synchronization and reconciliation intervals
- **THEN** the API responds 403 and returns no interval

### Requirement: Synchronization log
The system SHALL record each synchronization and reconciliation with its kind (`incremental`, `full` or `reconciliation`), its trigger (`schedule`, `manual` or `initial`), the user who requested it when manual, its status (`queued`, `running`, `succeeded` or `failed`), its start and end, the numbers of created, updated and deleted records, and the error when it failed. A submission received with the same JSON as stored SHALL count as neither created nor updated. A project `analyst` or `admin` SHALL be able to read the log, newest first, in pages, and the API SHALL respond 403 to a `viewer`.

#### Scenario: Successful synchronization recorded
- **WHEN** a synchronization reads 10 submissions, of which 4 are new, 2 changed and 4 identical to the stored ones
- **THEN** the log shows it as succeeded with 4 created, 2 updated and 0 deleted, and its start and end times

#### Scenario: Failed synchronization recorded
- **WHEN** a synchronization fails because the Kobo server cannot be reached
- **THEN** the log shows it as failed with the error message

#### Scenario: Viewer reads the log
- **WHEN** a `viewer` asks the API for the project's synchronization log
- **THEN** the API responds 403 and returns no synchronization

#### Scenario: Analyst reads the log
- **WHEN** an `analyst` opens the project's synchronization log
- **THEN** they see every recorded synchronization of the project, newest first

### Requirement: Ingestion status
A project `analyst` or `admin` SHALL be able to see the project's ingestion status: the number of stored submissions not marked as deleted, the number marked as deleted, when the last webhook delivery was accepted, the last synchronization with its status, when the next scheduled synchronization and reconciliation are due, and whether a synchronization is queued or running. The API SHALL respond 403 to a `viewer`.

#### Scenario: Webhook never received
- **WHEN** no webhook delivery has been accepted for the project
- **THEN** the status shows that no webhook has been received yet

#### Scenario: Status after a synchronization
- **WHEN** a synchronization that stored 120 submissions has finished and 3 of them were later marked as deleted
- **THEN** the status shows 117 submissions, 3 deleted, and the finished synchronization as the last one

#### Scenario: Viewer reads the status
- **WHEN** a `viewer` asks the API for the project's ingestion status
- **THEN** the API responds 403 and returns no status

### Requirement: Form version status
A project `analyst` or `admin` SHALL be able to see the form versions that the project's non-deleted submissions use, and the API SHALL respond 403 to a `viewer`. For each version, the system SHALL show its identifier, its deployment date when known, its number of non-deleted submissions, and the status of its definition: `available`, `pending`, `failed` with the reason and the time of the next attempt, or `not_found`. The list SHALL also show how many non-deleted submissions have no form version.

#### Scenario: Viewer reads the versions
- **WHEN** a `viewer` asks the API for the project's form versions
- **THEN** the API responds 403 and returns no version

#### Scenario: Analyst reads the versions
- **WHEN** an `analyst` opens the Submissions section of a project with 120 submissions made with `vA`, whose definition is kept, and 15 made with `vB`, whose fetch failed
- **THEN** they see `vA` as available with 120 submissions, and `vB` as failed with 15 submissions, the reason and the time of the next attempt

#### Scenario: Submissions without a version
- **WHEN** 3 of the project's non-deleted submissions have no form version
- **THEN** the list shows that 3 submissions have no form version

#### Scenario: Re-check offered where the versions are listed
- **WHEN** an `analyst` reads a version marked as `failed` or `not_found`
- **THEN** the list offers to re-check the form in place
