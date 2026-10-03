# Spec Delta

## MODIFIED Requirements

### Requirement: Form field check for the performance dashboard
When a project is created, whenever an `admin` or `analyst` re-checks the Kobo connection, and on every synchronization of the project's submissions, the system SHALL read the form definition, keep the list of its fields and its choice lists (each choice's list, name and label), and check whether the form contains `start`, `end` and an enumerator identifier. The enumerator identifier is the `username` metadata field, or a form question the admin selects. The system SHALL report which performance-dashboard metrics will be unavailable when fields are missing.

#### Scenario: Complete form
- **WHEN** the form has `start`, `end` and `username`
- **THEN** the check reports every performance metric as available

#### Scenario: Missing start or end
- **WHEN** the form lacks `start` or `end`
- **THEN** the check reports the completion-time metric as unavailable and names the missing field

#### Scenario: No enumerator identifier
- **WHEN** the form lacks `username` and the admin has not selected an enumerator question
- **THEN** the check reports the per-enumerator detail as unavailable and suggests selecting a question

#### Scenario: Admin selects the enumerator question
- **WHEN** an admin selects a form question as the enumerator identifier
- **THEN** the check reports the per-enumerator detail as available

#### Scenario: Re-check by a viewer
- **WHEN** a `viewer` tries to re-check the Kobo connection
- **THEN** the API responds 403

#### Scenario: Form redeployed with new fields
- **WHEN** the form is redeployed in Kobo with `start` and `end` added, and a synchronization of the project then runs
- **THEN** the field check reports the completion-time metric as available without anyone re-checking the connection

#### Scenario: Choice lists kept
- **WHEN** a project is created for a form whose question `municipality` uses the choice list `municipalities` with the choice `cali` labeled `Cali`
- **THEN** the project's form snapshot records that the question uses `municipalities` and that `cali` is labeled `Cali` in it
