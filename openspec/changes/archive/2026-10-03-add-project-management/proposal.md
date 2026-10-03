# Proposal

## Why

Tamiza can sign people in, but there is nothing to sign in to. Every later feature (ingestion, the script pipeline, the three dashboards) works inside a project: one Kobo form, the people allowed to work on it, and the sampling frame the performance dashboard measures coverage against. Projects come next because everything else hangs off them.

## What Changes

- **Projects linked to a Kobo form.** Any signed-in user can create a project from a Kobo server URL (presets for `kf.kobotoolbox.org` and `eu.kobotoolbox.org`, or any public HTTPS server), an asset UID and an API token. Tamiza checks that the token can read the form and its submissions before saving. The token is stored encrypted and never returned by the API. The creator becomes the project's `admin`.
- **Server URL policy.** Kobo servers must use HTTPS and resolve to public addresses. An operator setting allows private networks (and plain HTTP) for a self-hosted Kobo on the same network. This protects the API against being used to reach internal services.
- **Form field check.** On creation, and whenever an admin or analyst re-checks the connection, Tamiza reads the form definition and reports which performance-dashboard metrics will be unavailable when `start`, `end` or an enumerator identifier (`username` or a question the admin picks) is missing.
- **Webhook configuration.** Each project gets a generated webhook secret, stored encrypted. Admins see the webhook URL and credentials to enter in Kobo's REST Services, and can regenerate the secret. The webhook endpoint itself arrives in `add-kobo-ingestion`.
- **Per-project roles** (`admin`, `analyst`, `viewer`), stored in Tamiza's database, not in Keycloak. Non-members get 404 for a project; members without the required role get 403. Superadmins act as admin on every project. A project always keeps at least one admin.
- **Members by email invitation.** An admin adds a person by email and role. If someone with that verified email has already signed in, they become a member at once. Otherwise the invitation stays pending and turns into a membership when that person first signs in with the email verified in Keycloak. Admins can change roles, remove members and revoke pending invitations. Tamiza sends no email; the admin tells the person.
- **Sampling frame.** Admins and analysts define dimensions (for example municipality, sex, age range), the form field each dimension maps to, and a target number of surveys per combination of values. The frame is edited in the UI or imported from a CSV or XLSX file, with a preview before saving.
- **UI on Tailwind CSS and Flowbite.** The UI moves from hand-written CSS to Tailwind CSS 4 with Flowbite's MIT-licensed component designs, built as Tamiza's own Angular components, with Angular CDK providing dialogs, menus and focus handling. Every screen, including the existing shell, works from phone width to desktop. New screens: project list, create project, and a project page with overview, webhook, members and sampling-frame sections.

Not in this change: deleting or archiving projects, changing a project's Kobo server or form after creation (only the token can be replaced), email notifications, validating sampling-frame values against the form's choice lists, and audit events (`add-audit-log` retrofits them).

## Capabilities

### New Capabilities

- `projects`: projects linked to one Kobo form, the connection check and server URL policy, the form field check, webhook configuration, members and invitations, and the sampling frame.
- `web-ui`: cross-cutting UI behavior: responsive layout from phone to desktop and the navigation shell.

### Modified Capabilities

- `auth`: adds the per-project roles requirement (`admin`, `analyst`, `viewer`, superadmin as admin everywhere, 404 for non-members, 403 for insufficient roles).

## Impact

- **API:** new endpoints under `/api/v1/projects` (projects, Kobo connection check, form fields, webhook, members, invitations, sampling frame and its file import). An outbound HTTP client for the Kobo API v2 with SSRF protection.
- **Database:** new tables `projects`, `project_members`, `project_invitations` and `sampling_frames`; `users` gains `email_verified` so invitations only match verified emails.
- **Provisioning:** first sign-in and email-verification changes accept matching pending invitations.
- **Configuration:** new optional variable `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS` (default `false`).
- **UI:** Tailwind CSS 4, shared components built from Flowbite markup with Angular CDK behavior, restyled shell with responsive navigation, and the new project screens.
- **Dependencies:** API: a CSV/XLSX reader (MiniExcel) and an in-process HTTP stub for tests (WireMock.Net). UI: `tailwindcss`, `@tailwindcss/postcss`, `flowbite` (Tailwind plugin and theme only, not its JavaScript), `@angular/cdk`.
- **Docs:** a guide for connecting a Kobo form (where to find the asset UID and token, how to set up the REST Service), and the new variable in `.env.example`.
