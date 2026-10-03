# Tasks

## 1. Backend foundations

- [x] 1.1 Add `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS` (bool, default `false`) to `TamizaOptions`, pass it in `compose.yaml` and document it in `.env.example`; verify `scripts/check-env-example.sh` passes and a unit test binds `true`/`false`/absent correctly
- [x] 1.2 Add the EF Core entities and one migration for `projects`, `project_members`, `project_invitations`, `sampling_frames` and `users.email_verified` (columns, keys, checks and unique indexes per design D1); verify an integration test applies the migration on an empty database and inspects the four tables, the role check constraint and the invitation unique index
- [x] 1.3 Store `email_verified` in `UserProvisioner` (compared on the update path and part of the cache entry); verify integration tests: first sign-in stores the flag, and a later token with `email_verified` changed updates the same row
- [x] 1.4 Add `ProjectRole` (ordered `Viewer < Analyst < Admin`), `ProjectAccess.GetRoleAsync` (superadmin → `Admin`) and the `RequireProjectRole` endpoint filter returning 404 for no role and 403 for a lower role as Problem Details; verify unit tests for role ordering and integration tests on a probe endpoint for non-member (404), missing project (404), lower role (403), sufficient role (200) and superadmin non-member (200)

## 2. Kobo integration

- [x] 2.1 Implement `IpAddressPolicy.IsPublic` with every range in design D4, including IPv4-mapped and NAT64 IPv6 addresses; verify unit tests cover one address inside and one just outside each range
- [x] 2.2 Implement `KoboServerUrl.Parse` (absolute URL, no user info/query/fragment, trailing slash trimmed, HTTPS unless private networks are allowed); verify unit tests for presets, custom HTTPS, `http://` rejected and accepted with the flag, and malformed URLs
- [x] 2.3 Register the named `HttpClient` "kobo" with a `SocketsHttpHandler` whose `ConnectCallback` resolves the host and dials only addresses the policy allows (no auto-redirect, no proxy, 20 s timeout, 10 MB response cap) unless the flag is set; verify integration tests: with the flag `false`, a request to a WireMock stub on `127.0.0.1` fails with `KoboAddressNotAllowedException` and WireMock records zero requests; with the flag `true` it succeeds
- [x] 2.4 Implement `KoboClient` (`GetAssetAsync`, `CanReadSubmissionsAsync`) with `Authorization: Token`, mapping responses to `kobo.unauthorized`, `kobo.form_not_found`, `kobo.unreachable`, `kobo.unexpected_response`, and never logging the token; verify integration tests against WireMock for 200, 401, 403, 404, 302, timeout and an unresolvable host, asserting the token never appears in captured logs
- [x] 2.5 Parse `content.survey` into the field snapshot and compute the field check (design D4/D5); verify unit tests with fixture JSON for a complete form, a form without `start`/`end`, a form without `username` with and without an enumerator field, groups and repeats skipped, and `$autoname`/`$xpath` fallbacks

## 3. Projects API

- [x] 3.1 Implement `SecretProtector` (Data Protection purpose `Tamiza.Projects.Secrets.v1`) and webhook secret generation (32 random bytes, base64url); verify unit tests: round trip, different purpose cannot unprotect, two generated secrets differ and decode to 32 bytes
- [x] 3.2 Implement `POST /projects` (validate input, URL policy, Kobo access check, encrypt token, generate secret, creator becomes `admin`, return details plus webhook settings); verify integration tests for success and for each failure (`kobo.unauthorized`, `kobo.form_not_found`, `kobo.unreachable`, `kobo.https_required`, validation 400), each failure leaving no row behind
- [x] 3.3 Implement `GET /projects` and `GET /projects/{id}` (role, field check, `tokenUnreadable`) and `PATCH /projects/{id}` (name, enumerator field validated against the snapshot); verify integration tests: list returns only memberships (all for superadmin), details never contain the token or secret (asserted on the raw JSON), analyst rename gets 403, admin rename and enumerator selection succeed, unknown enumerator field gets 400
- [x] 3.4 Implement `PUT /projects/{id}/kobo-token` and `POST /projects/{id}/kobo-check`; verify integration tests: a valid new token replaces the old one (the next check uses it, per WireMock's recorded header), an invalid one returns 422 and the old token keeps working, re-check refreshes `form_fields` after the stub's form changes, viewer re-check gets 403
- [x] 3.5 Implement `GET /projects/{id}/form-fields`, `GET /projects/{id}/webhook` and `POST /projects/{id}/webhook/secret`; verify integration tests: fields returned to a viewer, webhook URL built from `TAMIZA_PUBLIC_URL` with username `tamiza`, analyst gets 403, regenerating returns a different secret and the stored ciphertext changes
- [x] 3.6 Report an undecryptable token as `tokenUnreadable: true` instead of failing; verify an integration test that corrupts `encrypted_api_token` and gets 200 with the flag set

## 4. Members and invitations API

- [x] 4.1 Implement `GET /projects/{id}/members` (viewer) and `GET /projects/{id}/invitations` (admin); verify integration tests for contents and for 403 on invitations as analyst
- [x] 4.2 Implement `POST /projects/{id}/members` (normalized email; existing member → 409 `member.exists`; exactly one verified user → membership; otherwise invitation; duplicate invitation → 409 `invitation.exists`); verify integration tests for each branch, including mixed-case email and an existing unverified user getting an invitation
- [x] 4.3 Implement `InvitationAcceptor` and call it from `UserProvisioner` on the insert path and when the email or its verification changes to verified, in the same transaction; verify integration tests: a pending invitee signing in with a verified email becomes a member and the invitation disappears; an unverified sign-in leaves it pending; a later verified token accepts it; a revoked invitation is never accepted
- [x] 4.4 Implement `PATCH`/`DELETE /projects/{id}/members/{userId}` and `PATCH`/`DELETE /projects/{id}/invitations/{invitationId}` with the last-admin rule and row lock (design D8); verify integration tests: role change takes effect on the next request, a removed member gets 404, removing or demoting the only admin gets 409 `project.last_admin`, and two concurrent demotions of the last two admins leave exactly one admin

## 5. Sampling frame API

- [x] 5.1 Implement the frame validator (design D9) with field-path errors; verify unit tests for duplicate dimension names, unknown field, missing value, negative and non-integer targets, repeated combinations (trimmed, case-insensitive) and the row and dimension limits
- [x] 5.2 Implement `GET` (200/204) and `PUT /projects/{id}/sampling-frame` (analyst); verify integration tests: save then read returns the same frame to a viewer, invalid frame returns 400 with field paths, viewer `PUT` gets 403 and the frame is unchanged
- [x] 5.3 Add MiniExcel and implement `POST /projects/{id}/sampling-frame/import` (CSV/XLSX by extension, header row, `target` column, row-numbered errors, 5 MB and 10,000-row limits, antiforgery disabled, nothing saved); verify integration tests with fixture files: valid CSV and XLSX previews, missing `target`, invalid row 7 reported as row 7, unsupported extension, oversized file rejected, and the saved frame unchanged afterwards

## 6. UI foundation

- [x] 6.1 Add Tailwind CSS 4 (`tailwindcss`, `@tailwindcss/postcss`, `.postcssrc.json`), the `flowbite` package for its Tailwind plugin and default theme, and Tamiza's theme overrides in `src/styles.css`, replacing the hand-written styles; verify `npm run build` succeeds, the built CSS contains the theme variables, and the JavaScript bundle contains no Flowbite code
- [x] 6.2 Add `@angular/cdk` and build the shared components listed in design D10 in `src/app/shared/ui/` from Flowbite markup (dialog on CDK `Dialog`, dropdown on `CdkMenu`, toast region with `aria-live`), plus `THIRD-PARTY-NOTICES.md` with Flowbite's MIT license; verify unit tests for the dialog (focus trapped, Escape closes, focus restored), the dropdown (arrow keys and Escape) and the form field error binding, and `npm run build` succeeds
- [x] 6.3 Rebuild the shell with Tailwind and the shared components: Flowbite-style navbar with brand, Projects link, user name and sign-out at `md` and above; menu button opening a drawer (CDK `Dialog` as a side panel) with the same items below `md`; `data-testid` hooks kept; verify unit tests for the menu (opens on Enter, closes on Escape, focus returns to the button) and that existing shell tests still pass
- [x] 6.4 Implement the `ApiError` helper and toast-based feedback (design D11), with `en.json` keys for every error `code`; verify unit tests mapping a `ValidationProblemDetails`, a 409 with `code`, a 422 Kobo error and a network error
- [x] 6.5 Restyle `AuthUnavailablePage` and `NotFoundPage` with Tailwind; verify their unit tests still pass

## 7. UI project screens

- [x] 7.1 Add the projects service and the project list page (`projects`, the new default route): cards with name, form name and role, an empty state, and a "New project" action; verify unit tests for the list, the empty state and the default route redirect
- [x] 7.2 Add the create-project page: name, Kobo server (kf, eu or custom URL), asset UID, API token (password input), inline field errors and Kobo error messages; on success, go to the project's webhook settings; verify unit tests for presets, field errors bound from a 400 and a 422 `kobo.unauthorized` message
- [x] 7.3 Add the project page shell with section navigation (tabs at `md` and above, a select below) and the overview section: form details, field check per metric, enumerator question selector, re-check and replace-token actions shown by role; verify unit tests that viewer, analyst and admin see the right controls and that the field check renders unavailable metrics
- [x] 7.4 Add the webhook section (admin): URL, username and secret with copy buttons, step-by-step Kobo REST Services instructions, regenerate with confirmation dialog; verify unit tests for copy, the confirmation and the refreshed secret
- [x] 7.5 Add the members section: member list (table at `md`, stacked cards below), invite form (email, role), role change, remove with confirmation, pending invitations with revoke (admin only); verify unit tests for each action, the `member.exists` message, the last-admin error and the read-only view for non-admins
- [x] 7.6 Add the sampling-frame section: read-only table for viewers; for analysts and admins a dimension editor (name plus form-field select from `form-fields`), an editable targets grid (add, edit and delete rows), save with inline errors, and file import showing the preview, row errors and a field per imported dimension before saving; verify unit tests for editing, mapping imported dimensions, showing row errors and sending the expected `PUT` body

## 8. Documentation

- [x] 8.1 Write `docs/kobo.md`: finding the asset UID and API token in KoboToolbox, creating the project, configuring the REST Service with Basic authorization, re-checking the form, the field check, and `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS` for a Kobo on a private network; verify by following it to connect a form during group 9
- [x] 8.2 Update `README.md` (status, link to `docs/kobo.md`) and `docs/operations.md` (new variable, outbound connections to Kobo and the no-proxy limitation); verify links resolve and `scripts/check-env-example.sh` passes

## 9. Verification

- [x] 9.1 Run all suites: `dotnet test`, analytics `pytest`/`ruff`, UI `npm test -- --watch=false` and `npm run build`; verify all pass
- [x] 9.2 Rebuild and start the stack with the local Keycloak; with headless Chrome, open every screen (project list, create, overview, webhook, members, sampling frame, not found, auth unavailable) at 360 px and 1280 px, asserting no horizontal page scroll and saving screenshots for review
- [x] 9.3 End to end with two Keycloak test users: user A creates a project (against a real Kobo form if a token is available, otherwise a WireMock container on the stack network with `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true`), invites user B's email as `analyst`, user B signs in and sees the project, user B edits and saves the sampling frame, and user B gets 403 when trying to invite; verify each step in the browser and the database
