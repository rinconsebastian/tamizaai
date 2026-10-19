# account-emails Specification

## Purpose

Optional emails that the identity provider sends at Tamiza's request: the invitation email, with an account created for a person who has none, its resend, and a password reset email for project members. Admins no longer need the identity provider's console for these tasks. Keycloak is the only provider that supports them today.

## Requirements

### Requirement: Available only when configured
Account emails SHALL exist only when the operator has configured an identity provider that supports them. Today that means setting the Keycloak admin client ID and secret in the deployment's environment. Without them, no request SHALL be made to any provider's admin API, and inviting SHALL work as without this feature. The system configuration that the UI reads SHALL say whether account emails are enabled and nothing else about them.

#### Scenario: Not configured
- **WHEN** a deployment sets no Keycloak admin variables and an admin invites an email that no user has verified
- **THEN** a pending invitation is saved, the answer carries no email outcome, no admin request is made, and the system configuration reports account emails as disabled

#### Scenario: Partly configured
- **WHEN** the operator sets the client ID but not the client secret
- **THEN** account emails are disabled, as if nothing were configured, and the `api` log says once which variable is missing

#### Scenario: Configured
- **WHEN** the client ID and secret are set and the `api` has restarted
- **THEN** the system configuration reports account emails as enabled, without the provider, the client ID, the secret or any address

#### Scenario: Invalid value
- **WHEN** the operator sets the link lifetime, the hourly limit or the Keycloak address to an invalid value
- **THEN** the `api` refuses to start and its log names the variable and the accepted values

### Requirement: Keycloak address
The API SHALL reach Keycloak's token and admin endpoints for the realm named in the OIDC authority. By default it SHALL use the base URL of the discovery address when one is set, and otherwise the base URL of the authority. The operator SHALL be able to set a different base URL, for a Keycloak whose admin paths are not reachable at the public address.

#### Scenario: Default address
- **WHEN** the authority is `https://auth.example.org/realms/tamiza` and no other address is set
- **THEN** the API asks for its token at `https://auth.example.org/realms/tamiza/protocol/openid-connect/token` and manages users under `https://auth.example.org/admin/realms/tamiza/users`

#### Scenario: Internal address
- **WHEN** the operator sets the Keycloak base URL to `http://keycloak:8080` with the same authority
- **THEN** the API uses `http://keycloak:8080/realms/tamiza/...` for its token and `http://keycloak:8080/admin/realms/tamiza/...` for users, and sign-in keeps using the public authority

### Requirement: Link lifetime
Every link in an account email SHALL be valid for the provider's own default lifetime, unless the operator sets a lifetime in hours. In a default Keycloak realm, the default is 12 hours.

#### Scenario: Default lifetime
- **WHEN** no lifetime is set and Keycloak is asked to send an account email
- **THEN** the request carries no lifetime, so the realm's setting for admin-initiated actions applies

#### Scenario: Lifetime set
- **WHEN** the operator sets the link lifetime to 24 hours
- **THEN** Keycloak is asked to make each link valid for 24 hours

### Requirement: Account creation for a new email
When an admin invites an email that no Tamiza user has verified and the provider has no account with that email, the system SHALL create an enabled account with that email as username and email, unverified and without a password. It SHALL then ask the provider to email the person a link that sets a password, verifies the email and leads back to Tamiza.

#### Scenario: New person
- **WHEN** an admin invites `ana@example.org` and Keycloak has no account with that email
- **THEN** Keycloak has a new enabled account `ana@example.org` with the email unverified, Keycloak is asked to send it the email to update the password and verify the email, the pending invitation is listed, and the answer's email outcome is `sent`

#### Scenario: Person follows the link
- **WHEN** the person opens the link, sets a password and signs in to Tamiza
- **THEN** their email is verified and they become a member with the invited role, as for any pending invitation

#### Scenario: Interface language
- **WHEN** an admin whose interface is in Spanish invites a new email
- **THEN** the new account's locale is `es`, so a realm with Spanish enabled sends the email and the password page in Spanish

### Requirement: Accounts are not edited
The system SHALL NOT change the data of any account in the identity provider, apart from creating accounts for invitations. It SHALL ask the provider to email an existing account only in two cases: to resend the invitation email of an account it created whose email is still unverified, and to send a password reset email that a project admin requested for a member.

#### Scenario: Account made outside Tamiza
- **WHEN** an admin invites an email that already has a Keycloak account created in the Keycloak console
- **THEN** the account is not changed, no email is requested, the pending invitation is listed, and the email outcome is `existingAccount`

#### Scenario: Earlier invitation not used yet
- **WHEN** Tamiza created the account for `ana@example.org` an hour ago, its email is still unverified, and an admin of another project invites the same email
- **THEN** no second account is created, Keycloak is asked to send the invitation email again, and the email outcome is `sent`

#### Scenario: Account already verified
- **WHEN** Tamiza created the account for an email that is now verified, but the person has not signed in to Tamiza yet
- **THEN** no email is requested and the email outcome is `existingAccount`

### Requirement: Email outcome in the invite answer
When account emails are enabled and inviting saves a pending invitation, the answer SHALL say what happened to the email: `sent`, `recentlySent`, `existingAccount`, `rateLimited` or `failed`. An answer that adds a member at once, or a refused invitation, SHALL make no request to the provider. A failed provider request SHALL neither fail the invitation nor remove it.

#### Scenario: Keycloak fails
- **WHEN** Keycloak cannot be reached, or answers that it could not send the email because its SMTP settings are missing
- **THEN** the API answers 201 with the pending invitation and the email outcome `failed`, and the `api` log records the failure with the HTTP status but without the secret or the email address

#### Scenario: Already a Tamiza user
- **WHEN** an admin invites the verified email of a user who has signed in before
- **THEN** the person becomes a member at once and no request reaches the provider

#### Scenario: Duplicate invitation
- **WHEN** an admin invites an email that already has a pending invitation to the project
- **THEN** the request fails with `invitation.exists` as today and no request reaches the provider

### Requirement: Resend the invitation email
A project `admin` SHALL be able to ask for the invitation email again for one of the project's pending invitations. The rules that apply are the same as when inviting. The answer SHALL be the updated invitation when the email was requested, and otherwise a refusal with a code saying why.

#### Scenario: Link expired
- **WHEN** an admin asks to resend the email of a pending invitation whose Tamiza-created account is still unverified, 13 hours after the first email
- **THEN** Keycloak is asked to send the email again and the API answers 200 with the invitation and the time of this email

#### Scenario: Nothing to send
- **WHEN** an admin asks to resend for an email whose account Tamiza did not create, or whose email is already verified
- **THEN** the API answers 409 with the code `accountEmail.existingAccount`

#### Scenario: Account deleted meanwhile
- **WHEN** an admin asks to resend for an invitation whose Keycloak account was deleted in the console
- **THEN** a new account is created and sent the email, as when inviting

#### Scenario: Provider fails on resend
- **WHEN** Keycloak cannot be reached or cannot send the email during a resend
- **THEN** the API answers 502 with the code `accountEmail.failed` and the invitation is kept

#### Scenario: Not configured
- **WHEN** an admin asks to resend on a deployment without account emails
- **THEN** the API answers 503 with the code `accountEmail.disabled`

#### Scenario: Not an admin
- **WHEN** an `analyst` asks to resend an invitation email
- **THEN** the API answers 403

### Requirement: Password reset email for a member
A project `admin` SHALL be able to ask the identity provider to email a member of the project a link that sets a new password and leads back to Tamiza. The system SHALL address the account the member signs in with, and SHALL change nothing in it. The answer SHALL say whether the email was requested or why not.

#### Scenario: Member forgot the password
- **WHEN** an admin asks for a password reset email for a member of the project
- **THEN** Keycloak is asked to send that member's account an email whose only action is to update the password, and the API answers 204

#### Scenario: No active account
- **WHEN** the member's account was deleted or disabled in Keycloak, or has no email
- **THEN** the API answers 409 with the code `accountEmail.noAccount`

#### Scenario: Not a member of the project
- **WHEN** an admin asks for a password reset for a user who is not a member of the project
- **THEN** the API answers 404 and no request reaches the provider

#### Scenario: Provider fails on reset
- **WHEN** Keycloak cannot be reached or cannot send the email
- **THEN** the API answers 502 with the code `accountEmail.failed`

#### Scenario: Not configured
- **WHEN** an admin asks for a password reset on a deployment without account emails
- **THEN** the API answers 503 with the code `accountEmail.disabled`

#### Scenario: Not an admin
- **WHEN** an `analyst` or a `viewer` asks for a password reset email
- **THEN** the API answers 403

### Requirement: Limits on account emails
Each user SHALL be able to cause at most the configured number of account emails per hour, 20 by default. A new account with its first email counts as one, as does each resend and each password reset. The same account SHALL be sent at most one account email every 10 minutes. A limit reached while inviting SHALL still save the invitation.

#### Scenario: Hourly limit while inviting
- **WHEN** an admin who has caused 20 account emails in the last hour invites a new email
- **THEN** the pending invitation is saved, no request reaches the provider, and the email outcome is `rateLimited`

#### Scenario: Same person soon after
- **WHEN** an invitation email went to `ana@example.org` five minutes ago and an admin of another project invites her
- **THEN** no new email is requested and the email outcome is `recentlySent`

#### Scenario: Action too soon
- **WHEN** an admin asks to resend an invitation email or send a password reset to an account that got an account email five minutes ago, or the admin has reached the hourly limit
- **THEN** the API answers 429 with the code `accountEmail.rateLimited` and the seconds to wait

### Requirement: Provider credentials stay secret
The provider client secret and the tokens obtained with it SHALL never appear in logs, API answers or the system configuration. The API SHALL NOT follow redirects from the provider, so the secret and tokens cannot reach another address.

#### Scenario: Redirect from Keycloak
- **WHEN** the token endpoint answers with a redirect to another host
- **THEN** the API does not follow it, treats the request as failed, and the other host receives nothing

### Requirement: Account emails in the members section
When account emails are enabled, the add-member dialog SHALL say that a person without an account gets an email to set a password, and the message after inviting SHALL report the email outcome. Pending invitations SHALL show when the last invitation email went out and offer *Resend invitation email*. Members SHALL offer *Send password reset email* to admins. Without the feature, the section SHALL stay as it is.

#### Scenario: Dialog hint
- **WHEN** an admin opens the add-member dialog on a deployment with account emails enabled
- **THEN** the email field's hint says that a person without an account gets an email to set a password, instead of saying that Tamiza sends no email

#### Scenario: Outcome message
- **WHEN** an invitation is saved with the email outcome `existingAccount`
- **THEN** the message says that the invitation is saved, that the person already has an account and gets no email, and that the admin should let them know

#### Scenario: Pending invitation row
- **WHEN** an admin views a pending invitation whose last email went out at 9:30 today
- **THEN** the row shows the time of that email in the interface's format, and its menu offers *Resend invitation email* and *Revoke*

#### Scenario: Member row
- **WHEN** an admin opens the menu of a member
- **THEN** it offers *Send password reset email* together with the role and removal actions, and choosing it shows a message that the email is on its way to the member's address

#### Scenario: Action refused for now
- **WHEN** an admin chooses *Resend invitation email* or *Send password reset email* and the API answers 429 with 300 seconds to wait
- **THEN** a message says that the email cannot be sent yet and to try again in 5 minutes

#### Scenario: Feature disabled
- **WHEN** an admin views the members section on a deployment without account emails
- **THEN** the dialog hint says that Tamiza sends no email, invitation rows offer only *Revoke*, and member menus offer no email action
