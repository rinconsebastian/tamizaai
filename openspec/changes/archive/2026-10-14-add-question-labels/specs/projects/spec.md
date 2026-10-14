# Spec Delta

## MODIFIED Requirements

### Requirement: Form field check for the performance dashboard
When a project is created, whenever an `admin` or `analyst` re-checks the Kobo connection, and on every synchronization of the project's submissions, the system SHALL read the definition of the form's deployed version, keep the list of its fields (each question's name, path, type and label in the form's default language) and its choice lists (each choice's list, name and label in the form's default language), and check whether the form contains `start`, `end` and an enumerator identifier. The enumerator identifier is the `username` metadata field, or a form question the admin selects. The system SHALL report which performance-dashboard metrics will be unavailable when fields are missing.

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

#### Scenario: Question labels kept
- **WHEN** a project is created for a form whose question `respondent/age` is labeled `Age of the respondent` in the deployed version
- **THEN** the project's form snapshot records the question `age` with the path `respondent/age` and the label `Age of the respondent`

#### Scenario: Undeployed edits
- **WHEN** the form's deployed version has 20 questions and someone adds a question `income` in Kobo's form builder without redeploying, and a synchronization runs
- **THEN** the project's form snapshot keeps the 20 deployed questions and does not include `income`

#### Scenario: Deployed version unreadable
- **WHEN** the form has undeployed edits and Kobo cannot return the definition of the deployed version, while the form itself can be read
- **THEN** the creation, re-check or synchronization does not fail, and the snapshot is taken from the form's latest content

#### Scenario: Label missing in the default language
- **WHEN** the form's languages are Spanish (default) and English, and the choice `3` and the question `notes` have an English label but no Spanish one
- **THEN** the project's form snapshot records the choice `3` and the question `notes` without a label
