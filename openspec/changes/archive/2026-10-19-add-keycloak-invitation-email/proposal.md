# Proposal

## Why

Today an admin who adds a person by email only creates a pending invitation. Tamiza sends nothing, and the person has no account in Keycloak either. The admin has to create the account in the Keycloak console, mark the email as verified or send the actions email by hand, and then tell the person. The same console work is needed when an invitation link expires (12 hours in a default realm) or when a member forgets their password. Self-registration would avoid the console work, but it opens the realm to anyone. Any signed-in user can create projects and use the AI assistant, so open registration is not acceptable.

Tamiza signs users in with standard OpenID Connect. This change keeps it that way: everything that needs Keycloak's admin API is optional and sits behind one adapter. A deployment that uses another OIDC provider keeps working sign-in, without these email actions.

## What Changes

- **Account emails through the identity provider (optional).** When the operator configures a confidential Keycloak client for Tamiza, three actions become available, and Keycloak sends every email with the realm's own SMTP settings:
  - **Invitation.** Inviting an email that has no account makes the API create one (username and email only, no password). It then asks Keycloak to email the person a link to set a password and verify the email.
  - **Resend invitation email.** Shown on each pending invitation, for a person who did not use the link in time.
  - **Send password reset email.** Shown on each project member. Keycloak emails the member a link to set a new password.

  When the variables are not set, nothing changes from today.
- **Link lifetime.** By default, links last as long as the realm says (12 hours in a default Keycloak realm). The operator can set a different lifetime.
- **Accounts are not edited.** Tamiza never changes an account's data in the identity provider. It only creates accounts for invitations and asks the provider to send emails:
  - the invitation email, only to accounts Tamiza created whose email is still unverified;
  - the password reset email, only to project members, at a project admin's request.
- **Pending invitations flow unchanged.** The invitation is saved as today and becomes a membership when the person signs in with the verified email. A failed or skipped email never loses the invitation. The answer says what happened to the email (sent, recently sent, existing account, limit reached, failed).
- **Limits.** Each user has an hourly limit on account emails (invitations, resends and password resets together). The same person gets at most one account email every 10 minutes.
- **Minimal coupling with Keycloak.**
  - Sign-in relies only on standard OIDC: discovery, the authorization code flow with PKCE, JWT access tokens with audience and `sub`, and the standard `name`, `email` and `email_verified` claims.
  - The account emails talk to a provider-neutral interface, implemented today only by a Keycloak adapter.
  - The API contract, the database and the UI use neutral names. Only the Keycloak variables and the adapter mention Keycloak.
- **UI.** The add-member dialog says whether the person will get an email, and the toast after inviting reports the email outcome. Pending invitations show when the last email went out and offer *Resend invitation email*. Members offer *Send password reset email*. All of these appear only when the feature is enabled. The new account gets the inviter's interface language, so a realm with Spanish enabled sends emails and pages in Spanish.
- **Configuration and docs.**
  - New variables in `compose.yaml` and `.env.example`: `TAMIZA_KEYCLOAK_ADMIN_*` for the Keycloak client and its address, `TAMIZA_KEYCLOAK_LINK_HOURS`, and `TAMIZA_ACCOUNT_EMAILS_PER_HOUR`.
  - `docs/keycloak.md` gets a section on the confidential client: its least-privilege roles (`realm-management` → `manage-users` and `view-users`), SMTP, the link lifetime, and the warning that the realm must be dedicated to Tamiza.
  - `docs/keycloak.md` also gets a section on what another OIDC provider must offer.

## Capabilities

### New Capabilities
- `account-emails`: optional emails that the identity provider sends at Tamiza's request, with account creation for invitations, resend, and password reset. Covers configuration and enabling, which accounts Tamiza may create or email, limits, the outcomes reported to the admin, and the members-section UI.

### Modified Capabilities
- `projects`: the *Member invitations by email* requirement no longer states that the system never sends email. It defers to `account-emails` when that feature is enabled, and keeps today's behavior when it is not.
- `auth`: the *External identity provider configured through environment variables* requirement no longer names Keycloak as the only provider. It states the standard OIDC behavior sign-in relies on, so any compliant provider works for sign-in.

## Impact

- **API** (`tamiza-api`):
  - a provider-neutral account interface, with a Keycloak adapter: client-credentials token, user search, user creation, and actions email. It comes with its options, validator and named `HttpClient`;
  - `POST /projects/{id}/members` answers with the email outcome;
  - new `POST /projects/{id}/invitations/{invitationId}/email` (resend) and `POST /projects/{id}/members/{userId}/password-reset`;
  - `InvitationView` gains the time of the last email, and `GET /system/config` gains `accountEmails.enabled`;
  - new `accountEmail.*` problem codes.

  Sign-in code is unchanged apart from comments that no longer say Keycloak.
- **Database**: one DbUp script adding a table of the accounts Tamiza created, with the time of the last email to each.
- **UI** (`tamiza-ui`): add-member dialog hint, invite toast, invitation and member row actions, `SystemConfigService`, and English and Spanish texts with the new error codes.
- **Deployment**: new optional variables. The API must reach Keycloak's token and admin endpoints, which may be an internal URL when the proxy blocks `/admin` publicly.
- **Docs**: `docs/keycloak.md` (client, roles, SMTP, link lifetime, dedicated realm, other OIDC providers) and `docs/kobo.md` (members guide).
- **Security**: the API holds a secret that can create and manage users in the realm. The realm must be dedicated to Tamiza, and the secret is never logged or returned. A project admin can trigger a password reset email for a member. The email goes only to the account's owner, as with Keycloak's own "Forgot password".
