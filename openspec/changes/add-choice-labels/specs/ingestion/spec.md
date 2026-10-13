# Spec Delta

## ADDED Requirements

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
