# Spec Delta

## Purpose

Organize work in projects: each one is linked to exactly one Kobo form, holds the people allowed to work on it with their roles, and defines the sampling frame that field coverage is measured against.

## ADDED Requirements

### Requirement: Project creation linked to a Kobo form
Any signed-in user SHALL be able to create a project by giving a name, a Kobo server, the form's asset UID and a Kobo API token. Before saving, the system SHALL check that the token can read both the form definition and its submissions. On success the system SHALL store the token encrypted, generate a webhook secret, and make the creator the project's `admin`. Each project is linked to exactly one form; the server and asset UID cannot be changed after creation.

#### Scenario: Successful creation
- **WHEN** a signed-in user creates a project with a reachable server, an existing asset UID and a token that can read the form and its submissions
- **THEN** the project is saved with the form's name, the creator is its `admin`, and the response includes the webhook URL and credentials to configure in Kobo

#### Scenario: Token without access
- **WHEN** the token is rejected by Kobo or cannot read the form's submissions
- **THEN** creation fails with a message saying the token has no access to that form, and nothing is saved

#### Scenario: Form not found
- **WHEN** no form with that asset UID exists on the server
- **THEN** creation fails with a message saying the form was not found, and nothing is saved

#### Scenario: Server unreachable
- **WHEN** the Kobo server does not answer or the TLS connection fails
- **THEN** creation fails with a message saying the server could not be reached, and nothing is saved

### Requirement: Kobo server address policy
The system SHALL offer `https://kf.kobotoolbox.org` and `https://eu.kobotoolbox.org` as presets and accept any other server URL that uses HTTPS and resolves only to public IP addresses. Addresses that are loopback, private, link-local, unique-local, multicast or unspecified SHALL be rejected, and so SHALL plain HTTP, unless the operator enables private networks in the deployment configuration. The check SHALL apply to every connection the system opens to a Kobo server, including after redirects and DNS changes.

#### Scenario: Plain HTTP URL
- **WHEN** a user gives a server URL that starts with `http://` and private networks are not enabled
- **THEN** the request is rejected with a message that the server must use HTTPS

#### Scenario: Server on a private address
- **WHEN** the server's host name resolves to a loopback or private address and private networks are not enabled
- **THEN** no request is sent to it and the user gets a message that the address is not allowed

#### Scenario: Private networks enabled by the operator
- **WHEN** the operator has enabled private networks and a user gives the URL of a Kobo server on the local network
- **THEN** the connection is checked like any other server

### Requirement: Confidential Kobo token
The Kobo API token SHALL never appear in any API response or log. A project `admin` SHALL be able to replace it; the new token SHALL pass the same access check against the project's form before it replaces the old one.

#### Scenario: Project details hide the token
- **WHEN** any member requests the project's details
- **THEN** the response contains no token, encrypted or not

#### Scenario: Token replaced
- **WHEN** an admin submits a new token that can read the project's form and submissions
- **THEN** the new token replaces the old one

#### Scenario: Invalid replacement token
- **WHEN** an admin submits a token that fails the access check
- **THEN** the request fails with the reason and the previous token stays in use

### Requirement: Project list and details
The system SHALL list the projects a user belongs to, with the user's role in each; superadmins see every project. Project details SHALL include the name, Kobo server, asset UID, form name, the result of the last form field check and the requesting user's role. A project `admin` SHALL be able to rename the project.

#### Scenario: List shows only own projects
- **WHEN** a user who belongs to two of five projects requests the project list
- **THEN** the list contains those two projects with the user's role in each

#### Scenario: Rename by a non-admin
- **WHEN** an `analyst` tries to rename the project
- **THEN** the API responds 403 and the name is unchanged

### Requirement: Form field check for the performance dashboard
When a project is created, and whenever an `admin` or `analyst` re-checks the Kobo connection, the system SHALL read the form definition, keep the list of its fields, and check whether the form contains `start`, `end` and an enumerator identifier. The enumerator identifier is the `username` metadata field, or a form question the admin selects. The system SHALL report which performance-dashboard metrics will be unavailable when fields are missing.

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

### Requirement: Webhook configuration
Each project SHALL have a webhook secret, generated by the system and stored encrypted. A project `admin` SHALL be able to see the webhook URL and the credentials to enter in Kobo's REST Services, and to regenerate the secret; regenerating SHALL invalidate the previous secret. Other members SHALL NOT see the secret.

#### Scenario: Admin views the webhook settings
- **WHEN** an admin opens the project's webhook settings
- **THEN** they see the webhook URL, built from the installation's public URL, and the credentials to configure in Kobo

#### Scenario: Secret regenerated
- **WHEN** an admin regenerates the webhook secret
- **THEN** a new secret is shown and the previous one is no longer valid

#### Scenario: Analyst requests the secret
- **WHEN** an `analyst` requests the webhook settings
- **THEN** the API responds 403

### Requirement: Member invitations by email
A project `admin` SHALL be able to add a person by email address and role. If a user who has signed in with that email verified already exists, the person SHALL become a member at once. Otherwise the system SHALL keep a pending invitation, and SHALL turn it into a membership when a user signs in whose verified email matches it. Email matching SHALL ignore letter case. Pending invitations SHALL NOT match an unverified email. The system sends no email; the admin informs the person.

#### Scenario: Invitee already uses Tamiza
- **WHEN** an admin invites the verified email of a user who has signed in before
- **THEN** that user becomes a member with the chosen role immediately

#### Scenario: Invitee has not signed in yet
- **WHEN** an admin invites an email that no user with a verified email has
- **THEN** a pending invitation with the chosen role is listed for the project

#### Scenario: Invitee signs in
- **WHEN** a person with a pending invitation signs in with that email verified in Keycloak
- **THEN** they become a member of the project with the invited role, and the invitation is no longer pending

#### Scenario: Unverified email
- **WHEN** a person whose email matches a pending invitation signs in with the email unverified
- **THEN** no membership is created and the invitation stays pending

#### Scenario: Invitation revoked
- **WHEN** an admin revokes a pending invitation before the person signs in
- **THEN** a later sign-in with that email does not create a membership

#### Scenario: Already a member
- **WHEN** an admin invites the email of a current member
- **THEN** the request fails with a message that the person is already a member

### Requirement: Member management
A project `admin` SHALL be able to change members' roles and remove members. Members SHALL be able to see the project's member list; only admins SHALL see pending invitations. A user can belong to several projects with different roles. A project SHALL always keep at least one `admin`.

#### Scenario: Role changed
- **WHEN** an admin changes a member's role from `viewer` to `analyst`
- **THEN** the member has `analyst` permissions on their next request

#### Scenario: Member removed
- **WHEN** an admin removes a member
- **THEN** that user gets 404 for the project afterwards

#### Scenario: Last admin
- **WHEN** an admin tries to remove themselves or change their own role while they are the project's only admin
- **THEN** the request fails with a message that the project needs at least one admin

### Requirement: Sampling frame
Each project SHALL be able to have a sampling frame: a list of dimensions, each with a unique name and the form field it maps to, and a target number of surveys for each combination of dimension values. An `admin` or `analyst` SHALL be able to save or replace the frame; every member SHALL be able to read it. The system SHALL reject a frame whose dimensions map to fields that are not in the form, whose targets are not whole numbers of zero or more, whose rows lack a value for a dimension, or that repeats a combination.

#### Scenario: Frame saved
- **WHEN** an analyst saves dimensions mapped to existing form fields and one target per combination
- **THEN** the frame is stored and every member sees the same dimensions and targets

#### Scenario: Unknown field
- **WHEN** a dimension maps to a field the form does not have
- **THEN** the frame is rejected with a message naming the dimension and the field

#### Scenario: Repeated combination
- **WHEN** two rows have the same values for every dimension
- **THEN** the frame is rejected with a message naming the repeated rows

#### Scenario: Viewer saves
- **WHEN** a `viewer` tries to save the sampling frame
- **THEN** the API responds 403 and the frame is unchanged

### Requirement: Sampling frame import from CSV or XLSX
An `admin` or `analyst` SHALL be able to import a sampling frame from a CSV or XLSX file whose first row names the dimensions plus a `target` column, with one combination per row. Importing SHALL only produce a preview: the dimensions found, the targets and any errors. Nothing SHALL change until the user maps each dimension to a form field and saves. Files over 5 MB or 10,000 data rows SHALL be rejected.

#### Scenario: Valid file
- **WHEN** an analyst imports a CSV or XLSX file with columns `municipality`, `sex`, `age_range` and `target`
- **THEN** the preview lists the three dimensions and every row's target, and the saved frame is unchanged

#### Scenario: Missing target column
- **WHEN** the file has no `target` column
- **THEN** the preview reports that the `target` column is required

#### Scenario: Invalid row
- **WHEN** row 7 has a target that is not a whole number of zero or more
- **THEN** the preview reports the error for row 7

#### Scenario: File too large
- **WHEN** the file exceeds 5 MB or 10,000 data rows
- **THEN** the import is rejected with a message stating the limit
