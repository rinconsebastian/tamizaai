# Proposal

## Why

Today every project member, `viewer` included, can open the project's Settings group, and the API serves viewers everything those sections show: member names and emails, script code, runs, reference tables, the sampling frame and the synchronization log. Also, any signed-in user can create a project, which lets anyone with an account add Kobo forms to the installation and make the API contact Kobo servers. Operators want viewers kept to the dashboards, and project creation kept to people already trusted as project admins.

## What Changes

- The project's Settings group (the gear tab on desktop, the Settings group of the phone select) is offered only to `analyst` and `admin`; a superadmin acts as admin. A `viewer` keeps Performance, Clean data and the project's dashboards, unchanged.
- A `viewer` who opens a configuration address directly (bookmark, shared link, history) is taken to the project's performance dashboard, and the address is replaced. No section loads.
- **BREAKING** (API): the reads that only the Settings sections use now require `analyst`. A viewer gets 403, and a non-member still gets 404. These are: members; sampling frame and its download; scripts and their versions; script inputs; runs and run details; pipeline settings; reference tables and their preview; ingestion status; synchronization log; form versions; form fields; form choices.
- **BREAKING** (API): a viewer's project details leave out the values that only Settings > General shows. These are the Kobo server and asset UID, the form field check and when it ran, the enumerator question, the count of unavailable form versions and whether the token can be read. Name, form name, role, time zone and dashboards stay.
- **BREAKING**: creating a project requires being `admin` of at least one project (an accepted membership, not a pending invitation) or a superadmin. A superadmin can create one even when no project exists. Anyone else gets 403 with the message code `project.create_not_allowed`, before the fields are checked and before any request to Kobo, and nothing is saved.
- `GET /api/v1/me` reports whether the user can create projects.
- The project list offers "New project" and "Create a project" only to users who can create projects. Anyone else who has no projects is told to ask a project admin to invite them. Opening `/projects/new` without the right leads to the project list.
- Documentation explains that the first project of a new installation is created by a superadmin (`TAMIZA_SUPERADMIN_EMAILS`), and the roles table no longer promises viewers the Settings pages.

Non-goals: moving any Settings information onto the dashboards; changing what analysts can do; giving a code to the existing role 403 (left to the planned `add-api-message-codes`).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `projects`: who can create a project, checked before the Kobo address policy, and the project list offering creation only to them; a viewer's project details leave out the General-only values; changing one's own role or removing oneself in Members leaves no stale page; members, sampling frame, frame download, form fields and form choices are readable by analysts and admins only; the viewer case of the sampling frame start panel becomes the redirect to the performance dashboard.
- `auth`: the `viewer` role covers the dashboards, not the configuration; being admin of a project allows creating projects; the current-user profile reports whether the user can create projects.
- `web-ui`: Settings and its sections are offered only to analysts and admins, on every screen width; a viewer who opens a configuration address is taken to the performance dashboard; the viewer scenarios set in Settings move to analysts.
- `interface-language`: the scenarios that show Settings or project creation in Spanish use an analyst or an admin; the refused creation is shown in the interface language.
- `ingestion`: synchronization settings, the synchronization log, the ingestion status and the form version status are readable by analysts and admins only.
- `pipeline`: script versions, runs, pipeline settings and reference tables are readable by analysts and admins only.
- `script-editor`: the script inputs panel is for analysts and admins, and a viewer's request for it is refused; the read-only editor cases become an older version on view, the viewer cases become the redirect to the performance dashboard, and the `ctx` reference example uses an analyst.
- `script-assistant`: the viewer case keeps only the API refusal, because viewers no longer reach the Scripts section.
- `local-dev-kit`: the viewer case of the sample download in the Scripts section becomes the redirect to the performance dashboard; the API refusal stays.

OpenSpec does not let a modified requirement drop a scenario, so viewer scenarios that no longer apply keep their names and now describe the refusal or the redirect.

## Impact

- **API** (`tamiza-api`): project creation check and message code; `/me` flag; role changes on 19 read endpoints in the Projects, Members, Sampling frame, Ingestion, Scripts, Script inputs, Runs and Reference tables endpoints; role-dependent project details. Integration test harnesses and every test that creates a project or reads these endpoints as a viewer.
- **UI** (`tamiza-ui`): project page tabs, settings layout redirect, route guards (`/projects/new`, null-safe leave guards), current-user service, project list, en/es texts (new error code and empty-state text; removal of viewer-only texts inside Settings). Specs of the project page, the settings sections, the project list and project creation.
- **Docs**: `README.md`, `.env.example`, `docs/kobo.md`, `docs/keycloak.md`, `docs/operations.md`, and the docs that point viewers to Settings pages.
- **Upgrade**: after the upgrade, users who are not admins of any project can no longer create projects. API clients that use a viewer account to read the endpoints above get 403. No data migration.
