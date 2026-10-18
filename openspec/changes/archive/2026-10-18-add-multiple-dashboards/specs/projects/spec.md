# Spec Delta

## MODIFIED Requirements

### Requirement: Project list and details
The system SHALL list the projects a user belongs to, with the user's role in each; superadmins see every project. Project details SHALL include the name, Kobo server, asset UID, form name, the result of the last form field check, the requesting user's role, and the project's dashboards in their order, each with its identifier and name. A project `admin` SHALL be able to rename the project.

#### Scenario: List shows only own projects
- **WHEN** a user who belongs to two of five projects requests the project list
- **THEN** the list contains those two projects with the user's role in each

#### Scenario: Rename by a non-admin
- **WHEN** an `analyst` tries to rename the project
- **THEN** the API responds 403 and the name is unchanged

#### Scenario: Details list the dashboards
- **WHEN** a `viewer` requests the details of a project whose dashboards are "Health" and then "Field"
- **THEN** the details list "Health" and then "Field", each with its identifier
