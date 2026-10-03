# Design

## Context

Builds on `setup-foundation` (archived; main specs `auth` and `deployment`). Relevant current state:

- **API** (`tamiza-api/src/Tamiza.Api`): Minimal APIs under `/api/v1`. `TamizaDbContext` holds a single `users` table in schema `tamiza`. `UserProvisioner` upserts users from token claims with separate insert and update paths and a 5-minute cache. `ICurrentUser` exposes the local user and `IsSuperAdmin`. Data Protection keys live on the `api-keys` volume. Errors are Problem Details. Integration tests run on Testcontainers PostGIS with locally signed JWTs.
- **UI** (`tamiza-ui`): Angular 22 (zoneless, standalone), Transloco (`en`), hand-written CSS, a shell with a top bar, `HomePage`, `NotFoundPage` and `AuthUnavailablePage`. OIDC through `angular-auth-oidc-client`, the bearer token added only to same-origin `/api/` calls.
- **Users decided** (for this change): any signed-in user can create projects; members are added by email invitation; the UI uses Tailwind CSS with community components and must be responsive; Kobo servers are kf/eu presets or public HTTPS, with private networks only if the operator enables them.

See `proposal.md` for motivation and scope. The requirements are in `specs/projects`, `specs/auth` and `specs/web-ui`.

## Goals / Non-Goals

**Goals:**

- A project model and authorization pattern (`ProjectAccess`) that every later project-scoped endpoint reuses.
- A Kobo API client that is safe to point at user-supplied URLs, for `add-kobo-ingestion` to reuse.
- A UI foundation (Tailwind CSS, Flowbite-based shared components, responsive shell, error feedback) that the dashboards build on.

**Non-Goals:**

- The webhook endpoint, synchronization and submission storage (`add-kobo-ingestion`). This change only fixes the webhook contract shown to admins (D6).
- Deleting or archiving projects. Changing a project's server or asset UID (only the token is replaceable).
- Sending email. Admins share invitations out of band.
- Checking sampling-frame values against the form's choice lists, or deriving dimensions (such as age ranges) from numeric fields. A dimension maps to one raw form field and values are compared as text.
- Audit events. `add-audit-log` adds them for project, member and frame operations.
- Charts and data tables for dashboards. Their libraries are chosen in the dashboard changes.

## Decisions

### D1. Data model

New tables in schema `tamiza` (snake_case, `uuid` v7 keys, `timestamptz`):

| Table | Columns | Notes |
|---|---|---|
| `projects` | `id`, `name` (≤ 200), `kobo_server_url`, `kobo_asset_uid` (≤ 64), `encrypted_api_token`, `encrypted_webhook_secret`, `form_name`, `form_fields` (jsonb), `form_checked_at`, `enumerator_field` (nullable), `created_by` (FK `users`), `created_at`, `updated_at` | The same form may back several projects. |
| `project_members` | `project_id` (FK, cascade), `user_id` (FK), `role`, `created_at` | PK (`project_id`, `user_id`); `role` checked in (`admin`, `analyst`, `viewer`). |
| `project_invitations` | `id`, `project_id` (FK, cascade), `email` (as typed), `normalized_email`, `role`, `invited_by` (FK), `created_at` | Unique (`project_id`, `normalized_email`). |
| `sampling_frames` | `project_id` (PK, FK cascade), `dimensions` (jsonb), `targets` (jsonb), `updated_by`, `updated_at` | One frame per project, replaced as a whole. |

`users` gains `email_verified boolean not null default false`, written by provisioning. Existing rows get `false` until the user's next request refreshes them (D7).

`form_fields` is the snapshot of the form taken at the last check: `[{ name, xpath, type, label }]`. The field check (D5) is computed from it and `enumerator_field` when read, so there is no stored result to go stale. The sampling frame stores `dimensions: [{ name, field }]` (where `field` is a form field's `xpath`) and `targets: [{ values: [..], target }]`, with `values` in dimension order.

*Alternative:* normalized tables for dimensions and target rows. Rejected because the frame is always read and replaced whole, is small (≤ 10,000 rows), and the performance dashboard reads it in one piece.

### D2. Project authorization (`ProjectAccess`)

- A `ProjectRole` enum (`Viewer < Analyst < Admin`), stored as lowercase text.
- A scoped `ProjectAccess.GetRoleAsync(projectId)` returns the caller's role: `Admin` for superadmins whether or not they are members, the membership role otherwise, or `null`.
- An endpoint filter, `RequireProjectRole(minimum)`, on the `/api/v1/projects/{projectId:guid}` route group answers `null` with **404** (identical to a missing project) and a lower role with **403**. Both are Problem Details. The resolved role is stored in `HttpContext.Items` so handlers do not query it again.

*Alternative:* resource-based `IAuthorizationHandler` policies. Rejected because they return 403 by default, and the spec needs 404 for non-members. The filter is also shorter to apply per endpoint.

### D3. API surface

| Method and path | Minimum role | Purpose |
|---|---|---|
| `GET /projects` | signed in | Own projects with role (superadmin: all). |
| `POST /projects` | signed in | Create: `{ name, koboServerUrl, assetUid, apiToken }` → 201 with details and webhook settings. |
| `GET /projects/{id}` | viewer | Details, field check, `myRole`. Never the token or the secret. |
| `PATCH /projects/{id}` | admin | `{ name?, enumeratorField? }`. |
| `PUT /projects/{id}/kobo-token` | admin | Replace the token after an access check. |
| `POST /projects/{id}/kobo-check` | analyst | Re-read the form, refresh `form_fields`, return the field check. |
| `GET /projects/{id}/form-fields` | viewer | Field snapshot, for mapping dimensions and choosing the enumerator question. |
| `GET /projects/{id}/webhook` | admin | `{ url, username, secret }`. |
| `POST /projects/{id}/webhook/secret` | admin | Regenerate the secret. |
| `GET /projects/{id}/members` | viewer | Members with name, email and role. |
| `POST /projects/{id}/members` | admin | `{ email, role }` → 201 `{ kind: "member" \| "invitation", ... }`. |
| `PATCH /projects/{id}/members/{userId}` | admin | `{ role }`. |
| `DELETE /projects/{id}/members/{userId}` | admin | Remove a member. |
| `GET /projects/{id}/invitations` | admin | Pending invitations. |
| `PATCH` / `DELETE /projects/{id}/invitations/{invitationId}` | admin | Change role or revoke. |
| `GET /projects/{id}/sampling-frame` | viewer | 200 with the frame, or 204 when there is none. |
| `PUT /projects/{id}/sampling-frame` | analyst | Validate and replace. |
| `POST /projects/{id}/sampling-frame/import` | analyst | Multipart `file` (CSV/XLSX) → preview; saves nothing. |

**Errors:**

- 400 `ValidationProblemDetails`, with `errors` keyed by camelCase field path (`name`, `dimensions[1].field`, `targets[6].target`).
- 409 for conflicts, with a `code` extension (`member.exists`, `invitation.exists`, `project.last_admin`).
- 422 for Kobo check failures, with `code` set to `kobo.unauthorized`, `kobo.form_not_found`, `kobo.unreachable`, `kobo.https_required`, `kobo.address_not_allowed` or `kobo.unexpected_response`.

`detail` always carries a human-readable message, and the UI translates by `code` when it has a key for it.

### D4. Kobo client and SSRF protection

- **URL policy** (`KoboServerUrl.Parse`) accepts absolute URLs only: no user info, query or fragment; trailing slash and path whitespace trimmed. It requires `https` unless `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true`. The UI offers presets for `https://kf.kobotoolbox.org` and `https://eu.kobotoolbox.org`.
- **Address policy at connect time.** The named `HttpClient` "kobo" uses a `SocketsHttpHandler` whose `ConnectCallback` resolves the host itself, drops every address that `IpAddressPolicy.IsPublic` rejects, and connects only to an allowed one. Otherwise it throws `KoboAddressNotAllowedException`. Because the check runs on the address actually dialed, DNS rebinding and late DNS changes cannot get around it.
- **Blocked ranges.**
  - IPv4: `0/8`, `10/8`, `100.64/10`, `127/8`, `169.254/16`, `172.16/12`, `192.0.0/24`, `192.168/16`, `198.18/15`, `224/4`, `240/4`.
  - IPv6: `::`, `::1`, `fc00::/7`, `fe80::/10`, `ff00::/8`.
  - IPv4-mapped and NAT64 (`64:ff9b::/96`) addresses are judged by their embedded IPv4 address.
- **Handler settings.** `AllowAutoRedirect = false` (a 3xx becomes `kobo.unexpected_response`), `UseProxy = false` (a proxy would dial on the client's behalf and bypass the check), 20 s timeout, response body capped at 10 MB.
- **Calls,** with `Authorization: Token <token>`:
  1. `GET {server}/api/v2/assets/{uid}/?format=json` reads the form name and `content.survey`.
  2. `GET {server}/api/v2/assets/{uid}/data/?format=json&limit=1` proves the token can read submissions.
- **Error mapping:** 401/403 → `kobo.unauthorized`; 404 → `kobo.form_not_found`; timeouts, DNS failures and TLS failures → `kobo.unreachable`.
- **Field snapshot.** Each `survey` row becomes `{ name: name ?? $autoname, xpath: $xpath ?? name, type, label: first label }`. Group and repeat begin/end rows are skipped; metadata rows (`start`, `end`, `username`, `deviceid`, `today`) are kept.

*Alternatives:* validating the IP once before the request (vulnerable to DNS rebinding); an operator allowlist of hosts only (blocks the common case of a self-hosted Kobo with a public URL).

### D5. Form field check

Computed from `form_fields` and `enumerator_field`:

- **Completion time:** available when the snapshot has rows of type `start` and `end`. Otherwise unavailable, naming the missing ones.
- **Per-enumerator detail:** available when a `username` row exists, or when `enumerator_field` names a field in the snapshot. Otherwise unavailable, with a hint to select a question.
- **Daily evolution and sampling-frame coverage** need no special fields, so they are always reported as available.

`PATCH enumeratorField` validates that the field exists in the snapshot. A re-check that removes the selected field from the form makes the metric unavailable again; the setting is kept so it works again if the field returns.

### D6. Secrets and the webhook contract

- **Encryption.** `SecretProtector` wraps an `IDataProtector` with purpose `Tamiza.Projects.Secrets.v1`. It encrypts the Kobo token and the webhook secret. Neither is logged; the Kobo client's logging excludes request headers.
- **Webhook secret:** 32 random bytes, base64url.
- **Contract shown to admins.** URL `{TAMIZA_PUBLIC_URL}/api/v1/webhooks/kobo/{projectId}`, with **HTTP Basic** authentication: username `tamiza`, password set to the secret. Kobo's REST Services offer *Basic Authorization* as a built-in option, so admins only paste three values. `add-kobo-ingestion` implements the endpoint against this contract.
- **Key-loss recovery.** If the token cannot be decrypted (for example after losing the key ring), project details flag `tokenUnreadable: true`, and the UI asks the admin to replace the token and regenerate the secret.

*Alternatives:* a secret in the URL path (copy-paste friendly, but it lands in proxy and access logs); a custom header (Kobo supports it, but it takes more steps to configure).

### D7. Invitations and provisioning

- **Normalization.** Emails are normalized as trimmed `ToLowerInvariant()`.
- **Inviting** (`POST members`), in one transaction:
  1. If a current member has that email → 409 `member.exists`.
  2. If exactly one user with that verified email exists → insert the membership (`kind: member`).
  3. Otherwise insert an invitation (409 `invitation.exists` on the unique key).
  4. Several verified users sharing one email (Keycloak normally prevents this) take the invitation path.
- **Accepting.** `UserProvisioner` now also compares `email_verified`, and its cache entry includes it. On the insert path, or when the email or verification changed and the email is now verified, it calls `InvitationAcceptor.AcceptAsync(user)` in the same transaction. That inserts memberships for every invitation matching the normalized email (`ON CONFLICT DO NOTHING`) and deletes those invitations.
- **Expiry.** Invitations do not expire. Admins can revoke them or change their role.

### D8. Last-admin rule

Role changes and removals run in a transaction that locks the project's admin memberships (`SELECT ... FOR UPDATE` on `project_members WHERE project_id = @id AND role = 'admin'`). The operation is refused with 409 `project.last_admin` if it would leave zero admin members. Superadmins do not count as admin members. The lock serializes concurrent demotions, so two admins cannot demote each other at the same time.

### D9. Sampling frame validation and import

- **One validator** serves both `PUT` and the import preview:
  - 1–10 dimensions with unique names (case-insensitive, trimmed, non-empty).
  - Each dimension's `field` is in the form snapshot (checked on `PUT` only; the preview has no mapping yet).
  - Every target row has one non-empty value per dimension.
  - `target` is an integer from 0 to 1,000,000.
  - Combinations are unique (compared trimmed and case-insensitively), and there are at most 10,000 rows.
- **Import** uses **MiniExcel** (Apache-2.0), which reads both CSV and XLSX (the first sheet):
  - The first row is the header. The `target` column is matched case-insensitively, and the other columns become dimensions in file order. Empty trailing rows are ignored.
  - The format is chosen by extension (`.csv`, `.xlsx`). Others are rejected.
  - The endpoint caps the body at 5 MB and disables antiforgery, since the API authenticates with bearer tokens.
  - The response is `{ dimensions: [names], rows: [{ values, target }], errors: [{ row, message }] }`, with spreadsheet row numbers (header = row 1). The UI then asks for a field per dimension and sends `PUT`.

*Alternatives:* ClosedXML (heavier, and XLSX only); parsing in the browser (would duplicate validation, and the backend must validate anyway).

### D10. UI foundation: Tailwind CSS 4 and Flowbite

- **Tailwind CSS 4** through PostCSS (`@tailwindcss/postcss`, `.postcssrc.json`). `src/styles.css` imports `tailwindcss`, Flowbite's default theme (`flowbite/src/themes/default`) and its plugin (`@plugin "flowbite/plugin"`), then overrides the theme variables with Tamiza's teal accent and neutral surfaces. It uses the system font stack, because the CSP's `font-src 'self'` would block web fonts.
- **Flowbite (MIT) as the component library.** Component designs come from Flowbite's documentation. Their markup and utility classes go into Tamiza's own standalone Angular components in `tamiza-ui/src/app/shared/ui/`: button, input and form field with error text, select, card, badge, alert, table wrapper, tabs, dialog, dropdown menu, navbar with drawer, toast region and skeleton.
  - The `flowbite` npm package is installed only for its Tailwind plugin and theme. **Its JavaScript is not loaded:** it initializes components imperatively through data attributes, which breaks when Angular adds or removes elements.
  - `tamiza-ui/THIRD-PARTY-NOTICES.md` carries Flowbite's MIT notice for the ported markup.
- **Behavior from Angular CDK** (`@angular/cdk`):
  - `Dialog` handles modals (focus trap, Escape, focus restored on close) and the mobile navigation drawer, which opens as a side panel.
  - `CdkMenu` handles dropdowns, with keyboard navigation.
  - `Overlay` handles positioning.
  - Toasts come from a small signal-based `ToastService`, rendered in a fixed region with `aria-live="polite"`.
- **Responsive rules.**
  - Layouts are mobile-first, with Tailwind breakpoints `sm` 640, `md` 768 and `lg` 1024.
  - The shell switches from the drawer menu to inline navigation at `md`.
  - Pages use a centered container with responsive padding.
  - Forms are one column below `md` and two at `lg` where it helps.
  - Tables sit in an `overflow-x-auto` wrapper; on phones, the member list renders as stacked cards.
- **Tests and CSP.** Existing hooks the tests rely on (`.user-name`, `.sign-out`) are kept as `data-testid` attributes. The CDK overlay's inline styles are already allowed by `style-src 'unsafe-inline'`.

*Alternatives:*
- Spartan UI: set aside in favor of plain Tailwind with a component library.
- Material Tailwind by Creative Tim: MIT, but its HTML version is still in beta and configured for Tailwind 3.
- Notus Angular by Creative Tim: MIT, but unmaintained since 2023 and built for Tailwind 2.
- daisyUI: CSS-only classes, but fewer composite blocks.
- Flowbite's own JavaScript: initializes the DOM imperatively, which conflicts with Angular's rendering.
- Angular Material: decided against in favor of Tailwind.

### D11. UI structure and error feedback

- **Routes:**
  - `''` redirects to `projects`. `projects` is the list; `projects/new` is the create form.
  - `projects/:id` is the project page, with child routes `overview`, `members`, `sampling-frame` and `webhook` (shown only to admins).
  - The `**` route renders `NotFoundPage`.
- **State.** Feature services hold Angular signals and use `HttpClient`. Forms use reactive forms.
- **Error feedback.** An `ApiError` helper turns an `HttpErrorResponse` into `{ message, fieldErrors, code }`:
  - Forms bind `fieldErrors` under the matching inputs.
  - Other failures raise a toast, translated by `code` when an `en.json` key exists, else using `detail`.
  - Network errors get a generic "could not reach the server" message.
- **Role-aware UI.** Controls hide according to `myRole` from project details. The API enforces the roles regardless.

### D12. Configuration

`TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS` (bool, default `false`) is added to `TamizaOptions`, `compose.yaml` and `.env.example`. When `true`, it allows plain HTTP and private addresses for Kobo servers.

### D13. Test strategy

- **API unit tests:** IP and URL policies (every blocked range, mapped IPv6), survey parsing, field check, role ordering, frame validator, CSV and XLSX parsing (fixture files).
- **API integration tests,** with WireMock.Net running in-process as a Kobo stub (the factory sets `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true`):
  - Create (success and each failure code), with the token absent from responses and captured logs.
  - Token replacement, re-check and the enumerator field.
  - 404/403 per role; superadmin access.
  - Webhook settings and regeneration.
  - Invitations: direct membership; pending, then accepted on sign-in with a verified email; unverified email ignored; revocation.
  - Last-admin rule, including two concurrent demotions.
  - Frame save, validation and import preview.
  - One test with the flag `false`, showing that the loopback stub is refused without any request reaching it.
- **UI unit tests:** services, the `ApiError` mapping, the shell menu (open, Escape, focus return), and each screen's role-dependent controls.
- **Stack verification:**
  - Every screen at 360 px and 1280 px in headless Chrome, asserting `scrollWidth <= clientWidth` and capturing screenshots.
  - The invitation flow end to end with the local Keycloak and two test users.
  - Creating a project against a real Kobo form if a token is available; otherwise against a WireMock container on the stack's network, with the flag enabled.

## Risks / Trade-offs

- [SSRF through ranges or encodings not covered] → the check runs on the dialed address, unit tests cover every range including mapped IPv6, and redirects and proxies are disabled.
- [`UseProxy = false` breaks installations that need an outbound proxy to reach Kobo] → documented. A proxy-aware variant would need the proxy itself to enforce the policy; it can be added if someone needs it.
- [Admins can read the webhook secret] → by design, so they can configure Kobo. Only admins see it, and it can be regenerated at any time.
- [Invitations trust Keycloak's email verification] → matching requires `email_verified`. `docs/keycloak.md` already tells operators to verify emails, and the projects guide repeats it.
- [Ported Flowbite markup drifts from upstream, and the behavior is our own code] → only the components in use are ported, under the MIT notice. Angular CDK supplies the hard parts (focus trapping, overlay positioning, keyboard menus), so the components stay small. The `flowbite` package is pinned for its plugin and theme.
- [The form snapshot goes stale when the form is redeployed in Kobo] → the "Re-check" action refreshes it. `add-kobo-ingestion` will also refresh it on each synchronization.
- [Losing the Data Protection keys makes tokens and secrets unreadable] → recovery path (D6). `add-backup-restore` remains a prerequisite for production use.

## Migration Plan

- One EF Core migration creates the four tables and adds `users.email_verified`, applied automatically at API startup. No data migration is needed; `email_verified` fills in on each user's next request.
- Rollback: revert the image and drop the new tables. No existing data changes meaning.
- The UI restyle ships in the same release as the API, since the new screens need the new endpoints.

## Open Questions

- Whether to offer CSV and XLSX **export** of the sampling frame. Not required by the brief; easy to add later on the same model.
