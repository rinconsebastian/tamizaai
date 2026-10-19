# Tasks

**Order.** Groups 1 to 6 (api) come first, in order, and each ends with its tests green. Group 7 (deployment and docs) can follow group 6. Groups 8 and 9 (ui) follow, in order. Group 10 is last.

**How to run the tests:**
- `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL with Testcontainers; run one at a time.
- `npm test -- --watch=false` in `tamiza-ui/`. The two "stops polling when the page is destroyed" tests (`project-data.spec.ts`, `project-scripts.spec.ts`) are known failures.
- A single UI spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.

## 1. Provider boundary and configuration (api)

- [x] 1.1 Add the neutral `Accounts/` folder (design D1, D2):
  - `IAccountProvider`, `ProviderAccount` and `ProviderResult` with its failure kinds;
  - `AccountEmailOptions` (`TAMIZA_ACCOUNT_EMAILS_PER_HOUR`, default 20, range 1..1000, with a validator);
  - `AccountsRegistration.AddTamizaAccounts()`, called from `Program.cs`.

  Verify unit tests for the default, the binding, and both ends of the range, and that the api starts with no provider registered.
- [x] 1.2 Add `Accounts/Keycloak/KeycloakAdminOptions.cs` and its validator (design D2). It holds the four `TAMIZA_KEYCLOAK_*` variables with trimmed text, `Enabled` when the client ID and secret are both set, and `MissingVariables()`. The validator refuses:
  - a malformed `TAMIZA_KEYCLOAK_ADMIN_URL`;
  - `TAMIZA_KEYCLOAK_LINK_HOURS` outside 1..720, with empty allowed;
  - an `OIDC_AUTHORITY` without `/realms/<name>`, only when enabled.

  Register `KeycloakAccountProvider` as `IAccountProvider` only when enabled. Verify:
  - unit tests for each rule and for `Enabled` with only one of the two set;
  - an integration test that the api fails to start with a bad lifetime (`Invalid value`);
  - an integration test that it starts with a non-Keycloak authority and no Keycloak variables.
- [x] 1.3 Add `KeycloakAddress` (design D3), which derives the realm from the last `/realms/` segment and the base URL from `TAMIZA_KEYCLOAK_ADMIN_URL`, then `OIDC_METADATA_ADDRESS`, then `OIDC_AUTHORITY`. Verify unit tests: `Default address`, `Internal address`, a discovery address on `host.docker.internal`, a legacy `/auth/realms/x` authority, and trailing slashes.
- [x] 1.4 Add the startup check that warns once when only one of the Keycloak client ID and secret is set. Verify with `CapturingLoggerProvider`: one warning naming the missing variable (`Partly configured`), and none when both or neither are set.
- [x] 1.5 Make the sign-in comments neutral (design D1). `TokenIdentity`, `OidcOptions`, `User`, `UserProvisioner`, `auth.service.ts`, `auth.guard.ts` and `oidc-config.ts` say "identity provider", and the `offline_access` comment points to `OIDC_SCOPE`. Verify with `git diff` that only comments changed in those files, and that `AuthenticationTests`, `MeTests`, `ProvisioningTests` and the UI auth specs pass unchanged.

## 2. Schema (api)

- [x] 2.1 Add `tamiza-api/src/dbUp/Scripts/0013_add_account_emails.sql` exactly as design D5. Keep upper-case `CHECK`, `FOREIGN KEY` and `PRIMARY KEY` out of the comment. Verify the analytics integration tests still build their template database.
- [x] 2.2 Mirror it in EF: an `InvitedAccount` entity, `DbSet<InvitedAccount> InvitedAccounts`, the FK and the two indexes. Add a `ScriptConventionTests` test for `0013` that accepts the `ix_invited_accounts_` prefix. Verify `SchemaDriftTests` and the convention test pass.

## 3. Keycloak adapter (api)

- [x] 3.1 Add the named `HttpClient` `keycloak-admin` (design D4), with no redirects, no proxy, redacted logged headers, a 15-second timeout and a 1 MB buffer. Add `KeycloakStub` to `IntegrationTests/Infrastructure`: a WireMock server for the token endpoint, user search, user by id, creation with `Location`, and `execute-actions-email`. It records requests and has a `Settings()` helper, like `AssistantStub`. Verify a smoke test reaches the stub's token endpoint through the named client.
- [x] 3.2 Implement `KeycloakAccountProvider` (design D4), covering the cached token with serialized refresh, the 401 retry and the five operations. Verify integration tests against `KeycloakStub`:
  - two calls fetch one token, and an expired token is replaced;
  - a 401 fetches a new token and retries once;
  - `FindByEmailAsync` sends `exact=true`, and `FindBySubjectAsync` maps 404 to null;
  - the create body, with `locale` only for `en` or `es`, the `Location` id, and 409 → `Conflict`;
  - setup sends `["UPDATE_PASSWORD","VERIFY_EMAIL"]` and reset sends `["UPDATE_PASSWORD"]`, both with `client_id` and `redirect_uri`;
  - `Default lifetime` sends no `lifespan`, and `Lifetime set` to 24 sends `lifespan=86400`;
  - a 400 "User is disabled" maps to `AccountUnavailable`; a 500, an unreachable server and a timeout map to failures;
  - `Redirect from Keycloak`: a 302 to a second stub reaches nothing there;
  - no captured log line contains the secret, the token or an email address.

## 4. Account email policy (api)

- [x] 4.1 Add `AccountEmailLimits` (design D8): the per-requester one-hour window from `TAMIZA_ACCOUNT_EMAILS_PER_HOUR`, the 10-minute per-account interval in `IMemoryCache`, and the 32 striped locks. Verify unit tests: the hourly limit, separate users, the wait times, and the interval expiring after 10 minutes with `AdjustableTimeProvider`.
- [x] 4.2 Implement `AccountEmails.InviteAsync` and `ResendInvitationAsync` (design D6). Verify unit tests with a fake `IAccountProvider` and the test database:
  - `New person` (row inserted, `last_email_at` set, outcome `sent`);
  - `Account made outside Tamiza`, `Account already verified` and a disabled account (`existingAccount`, no create and no send);
  - `Earlier invitation not used yet` (send only, `sent`);
  - `Same person soon after` (`recentlySent`);
  - `Hourly limit while inviting` (`rateLimited`, no provider call; the allowance is not used by `existingAccount` or `recentlySent`);
  - a send failing after create (row kept, `last_email_at` null, `failed`, and a later call sends);
  - `Conflict` on create (`existingAccount`);
  - two parallel calls for the same new address create one account;
  - null when no provider is registered.
- [x] 4.3 Implement `AccountEmails.SendPasswordResetAsync` (design D6). Verify unit tests with the fake provider:
  - `Member forgot the password` (reset sent to the account found by subject, `sent`);
  - `No active account` (null, disabled, or without email → `noAccount`; `AccountUnavailable` on send → `noAccount`);
  - the interval shared with invitation emails (`recentlySent`);
  - the hourly allowance shared across all three actions (`rateLimited`);
  - provider failure (`failed`).

## 5. Invite and resend endpoints (api)

- [x] 5.1 Wire `AccountEmails.InviteAsync` into `InviteAsync` after the commit (design D7), with `InviteRequest.Language` and `InviteResult.EmailOutcome`. Verify integration tests in a new `AccountEmailApiTests` against `KeycloakStub`:
  - `Not configured` (no `emailOutcome`, no stub request);
  - `New person` (201, invitation listed, `sent`);
  - `Interface language` (`locale` `es` in the create body);
  - `Keycloak fails` (201, invitation listed, `failed`, a log line with the status but without the secret or email);
  - `Already a Tamiza user` and `Duplicate invitation` (no stub request).

  Every existing `MembersApiTests` case passes unchanged.
- [x] 5.2 Add `POST /projects/{projectId}/invitations/{invitationId}/email` with `AccountEmailErrors` (design D7). Verify integration tests:
  - `Link expired` (200 with the new `lastEmailAt`);
  - `Nothing to send` (409 `accountEmail.existingAccount`);
  - `Account deleted meanwhile` (a new account is created);
  - `Provider fails on resend` (502, invitation kept);
  - `Action too soon`, both the interval and the hourly limit (429 with `retryAfter` and `Retry-After`);
  - `Not configured` (503);
  - `Not an admin` (403 for analyst and viewer);
  - an outsider, and an invitation of another project (404).

## 6. Password reset endpoint, lists and system config (api)

- [x] 6.1 Add `POST /projects/{projectId}/members/{userId}/password-reset` (design D7). Verify integration tests against `KeycloakStub`:
  - `Member forgot the password` (204; the stub got `execute-actions-email` for the member's `sub` with only `UPDATE_PASSWORD`);
  - an admin resetting their own account;
  - `No active account` (409 `accountEmail.noAccount` for a 404 user and for a 400 "User is disabled");
  - `Not a member of the project` (404, no stub request);
  - `Provider fails on reset` (502);
  - `Action too soon` (429);
  - `Not configured` (503);
  - `Not an admin` (403 for analyst and viewer);
  - an outsider (404).
- [x] 6.2 Add `LastEmailAt` to `InvitationView` in the list, change-role and resend answers, and `accountEmails.enabled` to `GET /system/config`. Verify integration tests:
  - the list shows the time after a send, and null without one;
  - `SystemConfigTests` reports disabled by default, and enabled with the stub settings, with no client ID, secret, provider name or address in the body (`Configured`);
  - the OpenAPI document describes both new routes and the new fields.

  The full `dotnet test` run is green.

## 7. Deployment and documentation

- [x] 7.1 Add the five variables to the `api` service in `compose.yaml` (empty defaults, and `20` for the limit). Add them to `.env.example` in a new "Account emails (optional, Keycloak only)" section, each stating its purpose, that it is optional, its default, and a pointer to `docs/keycloak.md`. Verify `scripts/check-env-example.sh` passes and `docker compose config` shows the variables.
- [x] 7.2 Write the "Account emails (optional)" and "Other OpenID Connect providers" sections of `docs/keycloak.md` (design D10), with console steps, `kcadm.sh` commands and troubleshooting rows. Update the members paragraph of `docs/kobo.md` for both modes and the two actions. Verify the `kcadm.sh` commands run as written against the throwaway Keycloak 26 container of group 10, never the user's own Keycloak.

## 8. Services and configuration (ui)

- [x] 8.1 Add `accountEmails` to `SystemConfig` and `SystemConfigService.accountEmailsEnabled` (design D9). It is false while loading, on error, and for an older API without the key. Verify `system-config.spec.ts` cases mirroring the assistant ones.
- [x] 8.2 Extend `ProjectsService` with `invite(id, email, role, language)`, `InviteResult.emailOutcome`, `Invitation.lastEmailAt`, `resendInvitationEmail(id, invitationId, language)` and `sendPasswordReset(id, userId)`. Verify service tests for request bodies and URLs, and that the UI build has no type errors.

## 9. Members section (ui)

- [x] 9.1 Add-member dialog: send the active language, and show `members.emailHintInvite` when the feature is enabled, otherwise `members.emailHint`. Verify `add-member-dialog` specs: `Dialog hint` with the feature on and off, and the language in the request.
- [x] 9.2 Invite toast per outcome (design D9): no outcome gives `members.invited`; `sent` and `recentlySent` a success toast; `existingAccount` an info toast; `rateLimited` and `failed` a danger toast that says the invitation is saved. Verify `project-members.spec.ts` cases for each outcome, including `Outcome message`.
- [x] 9.3 Invitation rows: the "Email sent {{ when }}" line when `lastEmailAt` is set, and *Resend invitation email* before *Revoke* when enabled, with dispatch by item id. Member menus: *Send password reset email* before *Remove* when enabled, with a success toast naming the member's email. Both actions show `errors.codes.accountEmail.rateLimitedIn` with the minutes rounded up on 429, and use `errors.report` otherwise. Verify specs:
  - `Pending invitation row` and `Member row`;
  - `Action refused for now`: 300 seconds reads 5 minutes and 61 seconds reads 2 minutes, for both actions;
  - `Feature disabled`: only *Revoke*, no email item in member menus, and no email line without `lastEmailAt`;
  - the invitation list reloads after a resend.
- [x] 9.4 Add the English texts and their Spanish translations, in neutral Latin-American Spanish with tú and never naming Keycloak:
  - the new members keys;
  - `errors.codes.accountEmail.disabled`, `.existingAccount`, `.noAccount`, `.rateLimited` and `.failed`;
  - `rateLimitedIn` with its singular form.

  Add the five codes to `error-codes.spec.ts`. Verify `translations.spec.ts` and `error-codes.spec.ts` pass, and the full UI suite shows only the two known failures.

## 10. End-to-end checks

- [x] 10.1 Check against a real Keycloak 26 on a throwaway stack: a separate Compose project, its own env file and port, a throwaway Keycloak container with a `tamiza` realm set up from `docs/keycloak.md`, and Mailpit as SMTP. Never touch the user's stack or their Keycloak. Verify in the browser and in Mailpit:
  - inviting a new address gives `sent`, and the email arrives in Spanish with realm internationalization on and an admin interface in Spanish;
  - following the link asks for a password (and first and last name), verifies the email, and leads back to Tamiza, where signing in makes the person a member. This confirms the `VERIFY_EMAIL` assumption;
  - with no lifetime set, the link's expiry in the token matches the realm's 12 hours;
  - *Resend invitation email* sends a new email;
  - *Send password reset email* on a member sends a reset email, and the new password works;
  - an account made in the console gives `existingAccount` and is unchanged;
  - with SMTP removed, inviting gives `failed`, and after restoring it a resend works;
  - with internationalization off, creation still works;
  - `TAMIZA_KEYCLOAK_ADMIN_URL` set to the internal address works while `OIDC_AUTHORITY` stays public.
- [x] 10.2 Check sign-in with a provider other than Keycloak: the fake OIDC provider of the verification recipe (discovery, RS256 JWKS, code flow), with no Keycloak variables. Verify in the browser:
  - sign-in, sign-out and the profile work;
  - an invitation stays pending and is accepted when that user signs in with a verified email;
  - the members section shows no email actions;
  - the `api` log shows no admin requests.

  Tear both stacks down and remove their images afterwards.
