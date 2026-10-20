# Spec Delta

## MODIFIED Requirements

### Requirement: Current-user profile
The API SHALL expose the authenticated user's profile: local identifier, name, email, whether they are `superadmin` and whether they can create projects. Whether they can create projects SHALL follow the same rule as project creation (see Per-project roles), applied to the user's `superadmin` flag and memberships at the time of the request. The UI SHALL show the user's name and the sign-out option.

#### Scenario: Profile request
- **WHEN** an authenticated user requests `GET /api/v1/me`
- **THEN** the API responds 200 with their local identifier, name, email, `superadmin` flag and whether they can create projects

#### Scenario: Profile request without a session
- **WHEN** `GET /api/v1/me` is requested without a token
- **THEN** the API responds 401

#### Scenario: Admin of a project
- **WHEN** a user who is `admin` of one project and `viewer` of another requests `GET /api/v1/me`
- **THEN** the profile says that they can create projects

#### Scenario: Superadmin on an installation without projects
- **WHEN** a `superadmin` requests `GET /api/v1/me` on an installation that has no project yet
- **THEN** the profile says that they can create projects

#### Scenario: Admin of no project
- **WHEN** a user who is not `superadmin`, and who is `analyst` of one project and `viewer` of another, requests `GET /api/v1/me`
- **THEN** the profile says that they cannot create projects

#### Scenario: Pending admin invitation
- **WHEN** a user who belongs to no project, and whose email has a pending `admin` invitation because it is not verified, requests `GET /api/v1/me`
- **THEN** the profile says that they cannot create projects

#### Scenario: Last admin role lost
- **WHEN** another admin changes the role of a user who is not `superadmin` from `admin` to `analyst` in the only project where that user was `admin`, and the user then requests `GET /api/v1/me`
- **THEN** the profile says that they cannot create projects

### Requirement: Per-project roles
The system SHALL manage project membership and roles in its own database, not in Keycloak. The roles are `admin` (manages the project, its connection and its members), `analyst` (reads every configuration section except the webhook settings, and the member list without pending invitations; edits the project's analysis settings, such as the sampling frame, and re-checks the Kobo connection) and `viewer` (reads the project's dashboards, which are Performance, Clean data and the project's own dashboards, and exports their data; of the project's settings it sees only the name, the form name and the time zone, and it does not open the configuration sections or read the data only they show). Each role includes the permissions of the roles below it. A user who is `admin` of at least one project SHALL also be able to create projects; a pending invitation does not count. A `superadmin` SHALL act as `admin` on every project, whether or not they are a member, and SHALL be able to create projects even when no project exists.

#### Scenario: Access to another project
- **WHEN** a user requests a project they are not a member of
- **THEN** the API responds 404, the same as for a project that does not exist

#### Scenario: Viewer tries to change the project
- **WHEN** a user with the `viewer` role tries to save the project's sampling frame
- **THEN** the API responds 403

#### Scenario: Viewer reads the project's configuration
- **WHEN** a user with the `viewer` role requests the project's member list or one of its scripts
- **THEN** the API responds 403

#### Scenario: Viewer reads the dashboards
- **WHEN** a user with the `viewer` role opens the project's Performance dashboard, its Clean data dashboard and one of its own dashboards
- **THEN** the API returns the data each of them shows

#### Scenario: Analyst tries to manage members
- **WHEN** a user with the `analyst` role tries to invite a member
- **THEN** the API responds 403

#### Scenario: Superadmin without membership
- **WHEN** a superadmin opens a project they are not a member of
- **THEN** the API returns it and treats them as `admin`

#### Scenario: Different roles in different projects
- **WHEN** a user is `admin` of one project and `viewer` of another
- **THEN** each project applies the role the user has in it
