# Spec Delta

## ADDED Requirements

### Requirement: Per-project roles
The system SHALL manage project membership and roles in its own database, not in Keycloak. The roles are `admin` (manages the project, its connection and its members), `analyst` (edits the project's analysis settings, such as the sampling frame, and re-checks the Kobo connection) and `viewer` (only reads). Each role includes the permissions of the roles below it. A `superadmin` SHALL act as `admin` on every project, whether or not they are a member.

#### Scenario: Access to another project
- **WHEN** a user requests a project they are not a member of
- **THEN** the API responds 404, the same as for a project that does not exist

#### Scenario: Viewer tries to change the project
- **WHEN** a user with the `viewer` role tries to save the project's sampling frame
- **THEN** the API responds 403

#### Scenario: Analyst tries to manage members
- **WHEN** a user with the `analyst` role tries to invite a member
- **THEN** the API responds 403

#### Scenario: Superadmin without membership
- **WHEN** a superadmin opens a project they are not a member of
- **THEN** the API returns it and treats them as `admin`

#### Scenario: Different roles in different projects
- **WHEN** a user is `admin` of one project and `viewer` of another
- **THEN** each project applies the role the user has in it
