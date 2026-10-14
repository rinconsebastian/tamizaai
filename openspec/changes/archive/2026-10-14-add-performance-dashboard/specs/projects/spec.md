# Spec Delta

## MODIFIED Requirements

### Requirement: Form field check for the performance dashboard
When a project is created, whenever an `admin` or `analyst` re-checks the Kobo connection, and on every synchronization of the project's submissions, the system SHALL read the definition of the form's deployed version, keep the list of its fields (each question's name, path, type, label in the form's default language, and whether it is inside a repeat group) and its choice lists (each choice's list, name and label in the form's default language), and check whether the form contains `start`, `end` and an enumerator identifier. The enumerator identifier is the form question the admin selected, while the deployed form still has it, or else the `username` metadata field. The system SHALL report which performance-dashboard metrics will be unavailable when fields are missing, and SHALL report sampling frame coverage as unavailable while the project has no sampling frame or a dimension of its frame maps to a field the form no longer has.

#### Scenario: Complete form
- **WHEN** the form has `start`, `end` and `username`, and the project has a sampling frame whose fields are all in the form
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

#### Scenario: Selected question over username
- **WHEN** the form has `username` and an admin selects the question `enumerator_code`
- **THEN** the selection is saved and `enumerator_code` is the enumerator identifier

#### Scenario: Selected question removed by a redeploy
- **WHEN** an admin selected the question `enumerator_code` and the form is redeployed without it
- **THEN** the check uses the `username` field if the form has it, and otherwise reports the per-enumerator detail as unavailable

#### Scenario: No sampling frame
- **WHEN** the project has no sampling frame
- **THEN** the check reports sampling frame coverage as unavailable and suggests defining a frame

#### Scenario: Frame field removed from the form
- **WHEN** a dimension of the sampling frame maps to `municipality` and the form is redeployed without that question
- **THEN** the check reports sampling frame coverage as unavailable and names `municipality`

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

#### Scenario: Questions in repeat groups
- **WHEN** a project is created for a form whose question `member_age` sits inside the repeat group `members`, and whose question `municipality` sits in a plain group
- **THEN** the project's form snapshot marks `member_age` as inside a repeat group and `municipality` as not

#### Scenario: Undeployed edits
- **WHEN** the form's deployed version has 20 questions and someone adds a question `income` in Kobo's form builder without redeploying, and a synchronization runs
- **THEN** the project's form snapshot keeps the 20 deployed questions and does not include `income`

#### Scenario: Deployed version unreadable
- **WHEN** the form has undeployed edits and Kobo cannot return the definition of the deployed version, while the form itself can be read
- **THEN** the creation, re-check or synchronization does not fail, and the snapshot is taken from the form's latest content

#### Scenario: Label missing in the default language
- **WHEN** the form's languages are Spanish (default) and English, and the choice `3` and the question `notes` have an English label but no Spanish one
- **THEN** the project's form snapshot records the choice `3` and the question `notes` without a label

## ADDED Requirements

### Requirement: Project time zone
Each project SHALL have a time zone, `UTC` until an admin sets another. A project `admin` SHALL be able to set it to any time zone name PostgreSQL knows (such as `America/Bogota`); the system SHALL reject any other value. Project details SHALL include it for every member.

#### Scenario: Admin sets the time zone
- **WHEN** an admin sets the project's time zone to `America/Bogota`
- **THEN** the project details show `America/Bogota` to every member

#### Scenario: Unknown time zone
- **WHEN** an admin sets the time zone to `Mars/Olympus`
- **THEN** the API rejects it with a message naming the value, and the time zone is unchanged

#### Scenario: Time zone by a non-admin
- **WHEN** an `analyst` tries to set the project's time zone
- **THEN** the API responds 403 and the time zone is unchanged
