# Spec Delta

## MODIFIED Requirements

### Requirement: Member invitations by email
A project `admin` SHALL be able to add a person by email address and role. If a user who has signed in with that email verified already exists, the person SHALL become a member at once. Otherwise the system SHALL keep a pending invitation, and SHALL turn it into a membership when a user signs in whose verified email matches it. Email matching SHALL ignore letter case. Pending invitations SHALL NOT match an unverified email. Without account emails (see `account-emails`), the system sends no email and the admin informs the person; with them, the account email rules apply on top of these.

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

#### Scenario: No account emails configured
- **WHEN** an admin invites an email that no user with a verified email has, on a deployment without account emails
- **THEN** the pending invitation is saved, no email is sent, and the admin is told to let the person know
