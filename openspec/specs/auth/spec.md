# auth Specification

## Purpose

Identify the people who use Tamiza through an existing Keycloak instance (OIDC), keep a local record of each user, and determine the global permissions that do not depend on a project.

## Requirements

### Requirement: External identity provider configured through environment variables
The system SHALL authenticate users with OIDC against an existing Keycloak instance that is not part of the deployment. The realm URL, the UI client identifier and the audience the API expects SHALL be configured only through environment variables.

#### Scenario: Switching Keycloak instance
- **WHEN** the operator changes the OIDC variables in `.env` and restarts the deployment
- **THEN** the UI and the API use the new instance without rebuilding any image

### Requirement: UI sign-in
The UI SHALL require a valid session before showing any screen. Sign-in SHALL use the authorization code flow with PKCE.

#### Scenario: User without a session
- **WHEN** a user without a session opens any UI route
- **THEN** they are redirected to the Keycloak login page

#### Scenario: Return after login
- **WHEN** a user signs in to Keycloak after requesting a specific route
- **THEN** the UI takes them to the route they originally requested

#### Scenario: Expired session
- **WHEN** the access token expires and cannot be renewed
- **THEN** the UI redirects to the Keycloak login page

#### Scenario: Keycloak unavailable
- **WHEN** the UI cannot reach Keycloak
- **THEN** it shows a message saying the authentication service is unavailable, instead of a blank screen

### Requirement: Sign-out
The UI SHALL let users sign out, which also ends their Keycloak session.

#### Scenario: User signs out
- **WHEN** a signed-in user clicks "Sign out"
- **THEN** the session ends in Tamiza and in Keycloak, and the next visit to the UI asks for credentials again

### Requirement: API protection
Every endpoint under `/api/v1` SHALL require a valid access token issued by the configured realm, except endpoints explicitly declared public. A token is valid only if its signature, issuer, audience and lifetime are all correct.

#### Scenario: Request without a token
- **WHEN** a request without a token reaches a protected endpoint
- **THEN** the API responds 401

#### Scenario: Expired token
- **WHEN** a request arrives with an expired token
- **THEN** the API responds 401

#### Scenario: Token from another issuer or audience
- **WHEN** a request arrives with a token signed by another realm or issued for another audience
- **THEN** the API responds 401

#### Scenario: Public endpoint
- **WHEN** a request without a token reaches `GET /api/v1/system/config`
- **THEN** the API responds 200 with the public settings the UI needs to start sign-in, and the response contains no secret

### Requirement: Local user provisioning and refresh
The system SHALL keep one local record per authenticated user, identified by the token's `sub` claim. The name and email SHALL come from the token.

#### Scenario: First sign-in
- **WHEN** an authenticated user makes their first request to the API
- **THEN** the system creates their local record from the token's `sub`, name and email

#### Scenario: Name or email changed in Keycloak
- **WHEN** a registered user arrives with a token whose name or email differs from the local record
- **THEN** the system updates the existing record and does not create a new one

#### Scenario: Concurrent requests on first sign-in
- **WHEN** a new user makes several requests at the same time
- **THEN** exactly one local record exists for their `sub`

### Requirement: Global superadmin role from configuration
The `superadmin` role SHALL be granted to users whose email appears in the email list set through an environment variable, compared case-insensitively. The role SHALL be granted only when the token marks the email as verified. The role is not managed from the UI or in Keycloak.

#### Scenario: Listed and verified email
- **WHEN** a user whose verified email is on the list signs in
- **THEN** the system treats them as `superadmin`

#### Scenario: Listed but unverified email
- **WHEN** a user whose email is on the list but not verified signs in
- **THEN** the system does not treat them as `superadmin`

#### Scenario: Removal from the list
- **WHEN** the operator removes an email from the list and restarts the API
- **THEN** that user is no longer `superadmin` on their next requests

### Requirement: Current-user profile
The API SHALL expose the authenticated user's profile: local identifier, name, email and whether they are `superadmin`. The UI SHALL show the user's name and the sign-out option.

#### Scenario: Profile request
- **WHEN** an authenticated user requests `GET /api/v1/me`
- **THEN** the API responds 200 with their local identifier, name, email and `superadmin` flag

#### Scenario: Profile request without a session
- **WHEN** `GET /api/v1/me` is requested without a token
- **THEN** the API responds 401
