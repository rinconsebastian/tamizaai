# Design

## Context

See proposal.md for the motivation. The parts this change builds on:

- **Sign-in is already standard OIDC.**
  - The API validates JWT access tokens with `AddJwtBearer`: authority, audience, issuer and lifetime, with `MetadataAddress` for an internal discovery URL (`AuthenticationRegistration.cs`).
  - It reads only `sub`, `name`, `preferred_username`, `email` and `email_verified` (`TokenIdentity.cs`).
  - The UI uses `angular-auth-oidc-client` with the code flow, PKCE, refresh tokens and RP-initiated logout. It also sends `ui_locales`, a standard OIDC parameter (`oidc-config.ts`).
  - The only Keycloak-specific traces are names and comments: `users.keycloak_sub` holds the token's `sub`, some comments say "Keycloak", and one comment explains why the UI does not request `offline_access`.
- **Inviting today.** `POST /projects/{id}/members` (`MemberEndpoints.cs:56-122`) validates the email and role and opens a transaction. It answers 409 `member.exists` for a current member. If exactly one *verified* user has the email, it adds a membership. Otherwise it inserts a `project_invitations` row (409 `invitation.exists` on the unique index) and commits. The answer is `InviteResult(Kind, Member, Invitation)` with 201. `InvitationView` is `(Id, Email, Role, CreatedAt)`. `InvitationAcceptor` turns pending invitations into memberships when `UserProvisioner` sees the email verified; this change does not touch it.
- **Optional features follow one pattern.** `AssistantOptions` binds flat `TAMIZA_ASSISTANT_*` variables from the configuration root. It trims text values and exposes `Enabled` and `MissingVariables()`. An `IValidateOptions` fails startup on typos, naming the variable, and a startup check warns once when the feature is only partly configured. The named `HttpClient` never follows redirects, never uses a proxy and redacts logged headers (`AssistantHttp.cs`). `GET /system/config` exposes only `assistant.enabled`, which the UI reads through `SystemConfigService`.
- **Limits.** `TileLimits` uses an in-memory `PartitionedRateLimiter<Guid>` per user and assumes one api instance. 429 answers carry `retryAfter`.
- **OIDC settings.** `OIDC_AUTHORITY` (the issuer; for Keycloak, the realm URL), `OIDC_CLIENT_ID`, the optional `OIDC_METADATA_ADDRESS` and `TAMIZA_PUBLIC_URL`. The UI client allows `<TAMIZA_PUBLIC_URL>/*` as redirect URIs (`docs/keycloak.md`).
- **UI.**
  - `AddMemberDialog` posts `{ email, role }`, and `ProjectMembers.addMember` toasts `members.added` or `members.invited`.
  - Member menus offer role changes and *Remove* (`memberActions`). Invitation menus offer *Revoke* (`project-members.ts:105-113`).
  - Toast tones are `success`, `danger` and `info`. The interface language is `en` or `es`.
  - Every API code must be in `error-codes.spec.ts` and in `errors.codes` of `en.json` and `es.json`.
- **Tests.** `TamizaApiFactory` takes setting overrides, and outbound services are stubbed with WireMock (`KoboStub`, `AssistantStub`, `GoogleStub`). DbUp scripts end at `0012`. `ScriptConventionTests` and `SchemaDriftTests` guard the scripts.

**Decided with the user (2026-10-18):**
- The realm is dedicated to Tamiza. Tamiza creates accounts through Keycloak's admin API and Keycloak sends the emails; self-registration stays closed.
- The feature is optional, with an hourly limit per user.
- Tamiza never edits accounts.
- Two per-person actions: *Resend invitation email* (for invitees who missed the link, which lasts 12 hours in a default realm) and *Send password reset email* for members. Project admins can use both.
- The link lifetime defaults to the realm's setting.
- Coupling with Keycloak stays minimal: another OIDC provider must still sign users in, without these actions.

The requirements are in `specs/account-emails`, `specs/projects` and `specs/auth`.

## Goals / Non-Goals

**Goals:**
- Sign-in code paths stay provider-neutral. With another compliant OIDC provider, sign-in, sign-out, profile and manual invitations work, and nothing calls an admin API.
- All Keycloak knowledge lives in one adapter folder. The API contract, database, UI texts and policy (limits, ownership, outcomes) use neutral names and do not change if another provider's adapter is added later.
- No database transaction stays open while the provider is called, and a provider failure never loses an invitation.

**Non-Goals:**
- Adapters for other providers. The interface makes them possible, but none ships.
- Renaming `users.keycloak_sub`. It already holds any provider's `sub`, and renaming it would churn the schema and every test seed for a name.
- Deleting or disabling accounts when an invitation is revoked or accepted.
- A self-service "change my password" in Tamiza. Keycloak's "Forgot password" and account console cover it.
- Custom email text (the realm's theme owns it), and sending email from Tamiza itself.
- Several api instances sharing the limits, as everywhere else in the MVP.
- Audit records. They arrive with `add-audit-log`.

## Decisions

### D1. Provider boundary

Code is split into two folders:
- **`Tamiza.Api/Accounts/`** is neutral: the interface, the `AccountEmails` service (D6), limits, endpoints, errors and the neutral options.
- **`Tamiza.Api/Accounts/Keycloak/`** holds the adapter: options, address, `HttpClient` and the REST calls. Nothing outside this folder mentions Keycloak.

```csharp
public sealed record ProviderAccount(string Id, bool EmailVerified, bool Enabled, bool HasEmail);

public interface IAccountProvider
{
    Task<ProviderResult<ProviderAccount?>> FindByEmailAsync(string normalizedEmail, CancellationToken ct);
    /// <summary>The account a user signs in with, from the token's <c>sub</c>.</summary>
    Task<ProviderResult<ProviderAccount?>> FindBySubjectAsync(string subject, CancellationToken ct);
    /// <summary>Creates an enabled, unverified account without a password; Conflict when one exists.</summary>
    Task<ProviderResult<string>> CreateAsync(string email, string? language, CancellationToken ct);
    /// <summary>Emails the link that sets a password and verifies the email.</summary>
    Task<ProviderResult> SendSetupEmailAsync(string accountId, CancellationToken ct);
    /// <summary>Emails the link that sets a new password.</summary>
    Task<ProviderResult> SendPasswordResetEmailAsync(string accountId, CancellationToken ct);
}
```

`ProviderResult` carries a failure kind: `Unreachable` (including timeouts), `Unauthorized`, `Conflict`, `AccountUnavailable` or `Other`, plus the HTTP status for logs. `KeycloakAccountProvider` is registered only when the Keycloak admin options are enabled. Otherwise there is no `IAccountProvider`, `AccountEmails.Enabled` is false, and every action short-circuits.

*Why this granularity:* policy belongs to Tamiza and must not differ by provider. That covers who may be emailed, the ownership record, limits, outcomes and HTTP mapping. With this split, a future adapter is only HTTP mapping, and the policy is unit-tested with a fake provider. *Alternative:* a high-level `InviteAsync` on the interface. Each adapter would then re-implement the rules.

**Sign-in is not touched**, apart from comments that say "identity provider" instead of "Keycloak" (`TokenIdentity`, `OidcOptions`, `User`, `UserProvisioner`, `auth.service.ts`, `auth.guard.ts`). The comment in `oidc-config.ts` becomes neutral and points to `OIDC_SCOPE` for providers that need `offline_access`.

### D2. Configuration

| Variable | Default | Valid | Owner |
|---|---|---|---|
| `TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID` | empty | any text | adapter |
| `TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET` | empty | any text, never logged | adapter |
| `TAMIZA_KEYCLOAK_ADMIN_URL` | empty (derived, D3) | absolute http/https URL without user info, query or fragment | adapter |
| `TAMIZA_KEYCLOAK_LINK_HOURS` | empty (realm setting) | 1 to 720 | adapter |
| `TAMIZA_ACCOUNT_EMAILS_PER_HOUR` | 20 | 1 to 1000 | neutral |

- `KeycloakAdminOptions.Enabled` is true when both the client ID and the secret are set. Text values are trimmed, because Compose passes blanks.
- `KeycloakAdminOptionsValidator` fails startup on an invalid URL or lifetime, naming the variable. When the adapter is enabled, it also fails startup if `OIDC_AUTHORITY` has no `/realms/<name>` segment. A deployment on another provider never sets these variables, so it never meets that check.
- `AccountEmailOptions` holds the limit, with its own range check.
- A startup check warns once when only one of the ID and secret is set.

The Keycloak variables say "Keycloak" on purpose: they configure only the adapter. The limit is neutral, because it applies to whatever provider sends the emails.

### D3. Keycloak address

- **Realm:** the path segment after the last `/realms/` in `OIDC_AUTHORITY`, which also covers a legacy `/auth/realms/x`.
- **Base URL:** `TAMIZA_KEYCLOAK_ADMIN_URL` if set. Otherwise the part of `OIDC_METADATA_ADDRESS` before `/realms/` when that variable is set, and otherwise the part of `OIDC_AUTHORITY` before `/realms/`.
- **Token:** `{base}/realms/{realm}/protocol/openid-connect/token`. **Admin:** `{base}/admin/realms/{realm}`.

The token and the admin calls use the same base URL, so the token's issuer matches what the admin API expects, whether Keycloak computes issuers per request or has `KC_HOSTNAME` set.

*Alternative:* `token_endpoint` from the discovery document. Discovery returns the public hostname, which proxies often block for `/admin` and the container may not reach. It also costs a request.

### D4. Keycloak adapter

The named `HttpClient` `keycloak-admin` follows the assistant's: no redirects, no proxy, redacted logged headers, a 15-second timeout per request and a 1 MB response buffer.

- **Token:** `POST` form `grant_type=client_credentials`, `client_id` and `client_secret`. The token is cached until 30 seconds before `expires_in`, with refreshes serialized by a `SemaphoreSlim`. A 401 from an admin call drops the token and retries once.
- **`FindByEmailAsync`:** `GET /users?email={normalized}&exact=true&max=2`. Keycloak stores emails in lower case.
- **`FindBySubjectAsync`:** `GET /users/{sub}`. In Keycloak the access token's `sub` is the user id. A 404 returns null.
- **`CreateAsync`:** `POST /users` with `{ username: email, email, enabled: true, emailVerified: false }`, plus `attributes: { locale: [lang] }` for `en` or `es`. 201 gives the id from the `Location` header's last segment, and 409 maps to `Conflict`.
- **`SendSetupEmailAsync`** and **`SendPasswordResetEmailAsync`:** `PUT /users/{id}/execute-actions-email?client_id={OIDC_CLIENT_ID}&redirect_uri={TAMIZA_PUBLIC_URL}/`, plus `&lifespan={hours*3600}` only when `TAMIZA_KEYCLOAK_LINK_HOURS` is set. The body is `["UPDATE_PASSWORD","VERIFY_EMAIL"]` for setup and `["UPDATE_PASSWORD"]` for reset.
  - 204 means sent.
  - 400 for a disabled user or a user without an email maps to `AccountUnavailable`.
  - 500 (SMTP missing or failing) maps to `Other`.
  - Without `lifespan`, Keycloak applies the realm's lifespan for admin-initiated actions, 12 hours by default.
- **Failure logs:** Warning with the operation, the HTTP status and the account id when there is one. Never the email address, the secret or the token. An `Unauthorized` message names the service-account roles to check.

### D5. Data: `tamiza.invited_accounts` (DbUp `0013_add_account_emails.sql`)

```sql
-- Identity-provider accounts Tamiza created for invited emails, and when it last had an invitation email sent to each.
CREATE TABLE tamiza.invited_accounts (
    account_id character varying(255) NOT NULL,
    normalized_email character varying(320) NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    last_email_at timestamp with time zone NULL,
    CONSTRAINT pk_invited_accounts PRIMARY KEY (account_id),
    CONSTRAINT fk_invited_accounts_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);
CREATE INDEX ix_invited_accounts_created_by ON tamiza.invited_accounts (created_by);
CREATE INDEX ix_invited_accounts_normalized_email ON tamiza.invited_accounts (normalized_email);
```

- `account_id` is the provider's account id. It is 255 characters, like `keycloak_sub`, and the column is named neutrally.
- The row is inserted right after *create* succeeds, before the email is requested. A failed email can then be resent later as an account Tamiza created.
- `last_email_at` is shown on invitation rows. If an operator ever switches providers, old rows match no account and are harmless.

*Alternatives:*
- A Keycloak user attribute as the marker. Keycloak 24+ drops unmanaged attributes unless the realm declares them, and the marker would live in the provider.
- A column on `project_invitations`. Invitation rows disappear on accept and revoke, and one account serves invitations to several projects.

### D6. The `AccountEmails` service

All three actions take a striped lock for the person (D8). Keycloak calls do not use the request's cancellation token, so a tab closed between *create* and *send* cannot leave an account with no email.

**Invitation:** `InviteAsync(normalizedEmail, requesterId, language)` returns `sent`, `recentlySent`, `existingAccount`, `rateLimited` or `failed`. It returns null when disabled.
1. **Find by email.** On failure → `failed`.
2. **Found:**
   - not in `invited_accounts`, verified, or disabled → `existingAccount`;
   - an account email within the last 10 minutes → `recentlySent`;
   - no hourly allowance left → `rateLimited`;
   - otherwise **send setup**. On success, set `last_email_at` and mark the interval → `sent`. On failure → `failed`.
3. **Not found:**
   - no hourly allowance left → `rateLimited`;
   - **create.** `Conflict` → `existingAccount`; other failure → `failed`;
   - insert the `invited_accounts` row;
   - **send setup**, as above.

**Resend:** `ResendInvitationAsync` runs the same steps for the invitation's email.

**Password reset:** `SendPasswordResetAsync(subject, requesterId)` returns `sent`, `recentlySent`, `noAccount`, `rateLimited` or `failed`.
1. **Find by subject.** Null, disabled or without email → `noAccount`.
2. The 10-minute interval → `recentlySent`.
3. The allowance → `rateLimited`.
4. **Send reset.** `AccountUnavailable` → `noAccount`; other failure → `failed`; success → `sent`, and mark the interval.

Outcomes carry the seconds to wait for `recentlySent` and `rateLimited`.

### D7. API

- **Invite.** `InviteAsync` is unchanged up to the commit. After it, `AccountEmails.InviteAsync` runs when enabled. `InviteRequest` gains an optional `Language`. `InviteResult` gains `EmailOutcome` (JSON `emailOutcome`), which is null for `member`, refusals and a disabled feature. The answer stays 201.
- **Resend.** `POST /projects/{projectId}/invitations/{invitationId}/email` (admin; optional body `{ language }`; 404 for a missing invitation).
- **Password reset.** `POST /projects/{projectId}/members/{userId}/password-reset` (admin). It returns 404 when the user is not a member of the project, before any provider call. An admin may target themselves.
- **Outcome mapping** for both actions:

  | Outcome | Resend | Password reset |
  |---|---|---|
  | `sent` | 200 with the `InvitationView` | 204 |
  | `recentlySent`, `rateLimited` | 429 `accountEmail.rateLimited`, `retryAfter` and `Retry-After` | same |
  | `existingAccount` | 409 `accountEmail.existingAccount` | — |
  | `noAccount` | — | 409 `accountEmail.noAccount` |
  | `failed` | 502 `accountEmail.failed` | same |
  | feature off | 503 `accountEmail.disabled` | same |
- **Lists.** `InvitationView` gains `LastEmailAt`: the latest `last_email_at` among `invited_accounts` rows with the invitation's normalized email.
- **System config.** `SystemConfigResponse` gains `accountEmails: { enabled }`.
- **Codes** live in `AccountEmailErrors`, and the problems are built with `Problems.Status`, following `AssistantProblems`.

### D8. Limits and concurrency

- **Hourly allowance:** `AccountEmailLimits` keeps a `PartitionedRateLimiter<Guid>` keyed by the requester, with a fixed one-hour window, `TAMIZA_ACCOUNT_EMAILS_PER_HOUR` permits and no queue, as in `TileLimits`.
- **Per-account interval:** 10 minutes, a constant, kept in `IMemoryCache` by account id. It is set on every successful send, of either kind, so one mechanism covers invitations and resets.
- Both are in memory, so a restart resets them, which is accepted for the MVP.
- **Concurrency:** 32 striped `SemaphoreSlim`s chosen by a hash of the normalized email (invitations) or the subject (resets). Two admins inviting the same new address cannot both create an account, and a `Conflict` on *create* covers anything else.

*Alternative:* the interval from `last_email_at` in the database. It would cover only invitation emails, and resets would need another table.

### D9. UI

- **Services.**
  - `SystemConfigService.accountEmailsEnabled`, computed like `assistantEnabled`.
  - `ProjectsService`: `invite(id, email, role, language)`, `InviteResult.emailOutcome`, `Invitation.lastEmailAt`, `resendInvitationEmail(id, invitationId, language)` and `sendPasswordReset(id, userId)`. The language is Transloco's active language.
- **Add-member dialog:** the hint is `members.emailHintInvite` when enabled and `members.emailHint` otherwise.
- **Invite toast:**
  - without an outcome: `members.invited`, as today;
  - `sent` and `recentlySent`: success toast;
  - `existingAccount`: info toast;
  - `rateLimited` and `failed`: danger toast, saying the invitation is saved and the admin can resend later or tell the person.
- **Invitation rows:** a second line "Email sent {{ when }}" in the interface's date-time format when `lastEmailAt` is set. When enabled, the menu is *Resend invitation email* then *Revoke*. Selection dispatches on the item id.
- **Member menus:** when enabled, *Send password reset email* goes before *Remove*. Success toasts `members.passwordResetSent` with the member's email.
- **Errors:** a 429 from either action shows `errors.codes.accountEmail.rateLimitedIn` with the minutes rounded up, through `countKey`, like `assistant-panel.ts`. Other errors go through `errors.report`.
- **Texts.** UI strings never say "Keycloak". Neutral English source texts, Spanish translations, and the five new codes in `error-codes.spec.ts`.

### D10. Documentation

- **`docs/keycloak.md`, section "Account emails (optional)":**
  - realm SMTP (*Realm settings → Email*, test connection);
  - the confidential client `tamiza-admin` (*Client authentication* On, *Service accounts roles* On, *Standard flow* and *Direct access grants* Off), its service-account roles `realm-management` → `manage-users` and `view-users`, and the `kcadm.sh` equivalents;
  - the dedicated-realm warning;
  - `TAMIZA_KEYCLOAK_ADMIN_URL`, for proxies that block `/admin`;
  - link lifetime (*Realm settings → Tokens → Admin-initiated actions*, or `TAMIZA_KEYCLOAK_LINK_HOURS`), and email language (realm internationalization);
  - the first-and-last-name prompt of the default user profile;
  - federated accounts and password reset;
  - that revoking does not delete the account;
  - troubleshooting rows (`failed` → SMTP or roles; `existingAccount` → account made in the console; `noAccount` → account deleted, disabled or without email).
- **`docs/keycloak.md`, section "Other OpenID Connect providers":**
  - what the provider must offer: discovery; a public client with code flow and PKCE, and CORS on its token endpoint; signed JWT access tokens with `aud` = `OIDC_AUDIENCE`, `sub`, and preferably `name`, `email` and `email_verified` in the access token itself;
  - refresh tokens: add `offline_access` to `OIDC_SCOPE` if the provider requires it; logout uses `end_session_endpoint` when present;
  - what does not work there: account emails;
  - what depends on claims: superadmin and invitation matching need `email_verified`.
- **`docs/kobo.md`:** the members paragraph describes both modes and the two actions.
- **`.env.example` and `compose.yaml`:** the five variables.

## Risks / Trade-offs

- **The secret can manage every user in the realm.** → The realm must be dedicated to Tamiza, which the docs state, and the service account gets only `manage-users` and `view-users`. On Keycloak 26.2+, fine-grained admin permissions v2 can narrow it further. The secret is never logged, returned or sent after a redirect.
- **Any project admin can create accounts and trigger password reset emails.** Any signed-in user can create a project. → The hourly allowance and the 10-minute interval per account bound it. Accounts Tamiza creates are listed in `invited_accounts` with their creator. A reset email only reaches the account's owner and changes nothing until the owner acts, like Keycloak's "Forgot password".
- **Federated accounts** (for example Google through Keycloak) get a local password if the member uses a reset link. → Documented. It does not affect the federated login.
- **`sub` not equal to the Keycloak user id**, with a custom subject mapper. → `FindBySubjectAsync` returns null, the answer is `noAccount`, and the docs mention it.
- **Revoked invitations leave accounts** whose link still works until it expires. → Documented. Operators delete the account in Keycloak if needed.
- **`VERIFY_EMAIL` through the actions link.** This design assumes that completing the setup email leaves the email verified. → The manual check against Keycloak 26 confirms that the invitation is accepted. If that fails, the design needs revisiting before release.
- **Another provider's access tokens may lack `email_verified`.** Sign-in still works, but invitations and superadmin never match. → The "Other OpenID Connect providers" section says which claims to map into the access token.
- **Restart resets the hourly allowance and the interval.** → Accepted, as for map tiles.
- **"Existing account" answers can be probed.** An admin can learn whether an address has an account at the provider from the `existingAccount` outcome, and those answers cost no allowance. → Accepted: a verified Tamiza user is already revealed at once by the `member` answer, only admins can ask, and the provider lookup is the only cost. The outcome is what lets the admin know to tell the person themselves.
- **A lost reply after the provider made the account.** A timeout or a reply without the account's address could leave an account Tamiza made but did not record, which no later invitation could email. → After an ambiguous create failure, `AccountEmails` looks the address up again and, under the address's lock, adopts the unverified enabled account that appears. A failed write of the record or of the email time is logged and answered as `failed` or `sent`, never as a 500.
- **Links carry Keycloak's own hostname.** The emails' links are built from the address Keycloak was reached at, so an internal admin address with no fixed hostname produces links that do not open. → Documented: set Keycloak's hostname (and `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` when `api` uses another address). Verified end to end against Keycloak 26.

## Migration Plan

1. Deploy with the new variables unset. The DbUp script adds an empty table and nothing else changes.
2. To enable: configure SMTP in the realm, create the `tamiza-admin` client and its roles, set the two client variables (and `TAMIZA_KEYCLOAK_ADMIN_URL` if needed), and restart `api`.
3. Rollback: unset the variables and restart. The table stays and is unused; DbUp has no down scripts.
