# Tamiza — Project brief for OpenSpec

> **Tamiza — from survey to indicator.** Domain: `tamiza.ai`.
> Tamiza is compatible with KoboToolbox but is not affiliated with Kobo. The "Kobo" trademark is used only descriptively ("for KoboToolbox"), never as part of the product name.
> Pending: trademark check with SIC and WIPO (class 42).
> This document is the source for the OpenSpec project context and for generating the first changes.

---

## 1. Purpose

Tamiza is an open source web application that complements KoboToolbox: it receives the data captured in Kobo forms, runs Python scripts for cleaning, transformation and indicator calculation, and presents the results in dashboards: performance, clean data, and the project's own indicators dashboards (1 to 7 per project).

It replaces today's manual flow: Kobo → Jupyter/pandas → indicators → Power BI/Excel/Word.

**Initial users:** consultants and analysts who run population characterizations and market studies with Kobo.

**License:** MIT. Distributed as a self-hosted package with Docker Compose and, later, as a managed service.

---

## 2. Architecture

### 2.1 Containers (Docker Compose)

| Service | Technology | Responsibility |
|---|---|---|
| `ui` | Angular | User interface. Talks only to `api`. |
| `api` | .NET (ASP.NET Core) | Management backend: projects, users and roles, webhook intake, Kobo synchronization, run orchestration, dashboard queries. |
| `analytics` | Python | Flattening of raw data and execution of user scripts. Not exposed outside the internal network. |
| `db` | PostgreSQL + PostGIS | Metadata, raw submissions (JSONB), script results and geospatial data. |

**Keycloak is external:** an existing instance is used through OIDC. It is not part of the Compose stack.

### 2.2 Communication

- `ui` → `api`: HTTP/JSON with a Keycloak OIDC token.
- `api` → `analytics`: through a job queue in PostgreSQL (table `runs`). The Python worker claims jobs with `SELECT ... FOR UPDATE SKIP LOCKED`. No queue container is added in the MVP.
- `analytics` reads and writes `db` directly.
- `api` calls the KoboToolbox API v2 to synchronize.
- `api` calls the model provider the operator configures (any OpenAI-compatible chat-completions server) when the AI assistant is enabled; only `api` talks to it, and never `analytics`.

### 2.3 Result storage

Each project has its own PostgreSQL schemas:

- `p_<id>_raw`: flattened tables (main table + one table per repeat group, with a key to the parent).
- `p_<id>_clean`: output of script 1.
- `p_<id>_output`: output of script 2.
- `p_<id>_indicators`: one table per indicator script, named after the indicator.

Schema and table names are never built from user text without validation (allow list + quoted identifiers).

---

## 3. Conventions

- Technical product name: `tamiza` (repository, Docker images `tamiza-ui`, `tamiza-api`, `tamiza-analytics`, .NET namespace `Tamiza.*`, Python package `tamiza`).
- Everything in English: code, identifiers, database objects, API names, the UI and documentation. The UI is ready for i18n so other languages can be added later.
- Versioned REST API (`/api/v1/...`), documented with OpenAPI.
- Database migrations with DbUp: versioned SQL scripts in `tamiza-api/src/dbUp`; EF Core is only the data-access layer (`api` owns the metadata schema).
- Python 3.12, dependencies pinned in the image: pandas, pyarrow, numpy, polars, duckdb, geopandas, openpyxl, psycopg.
- Secrets (Kobo tokens, webhook secrets) encrypted at rest with ASP.NET Data Protection.
- Every relevant operation leaves a record: who, what, when. The audit mechanism arrives in `add-audit-log` (see section 8).
- Tests: unit tests in .NET and Python; integration tests for the webhook, synchronization and the script chain.

---

## 4. Out of scope for the MVP

Strong sandbox for user code, multi-tenant SaaS, semantic layer, Word/Excel/PowerPoint templates, Power BI endpoint, S3 storage, dedicated message queue, visual block editor.

---

## 5. Requirements by capability

### 5.1 Authentication and authorization (`auth`)

#### Requirement: Authentication with an external Keycloak
The system SHALL authenticate users with OIDC against an existing Keycloak instance configured through environment variables.

##### Scenario: User without a session
- WHEN a user without a session opens the UI
- THEN they are redirected to the Keycloak login page

##### Scenario: First sign-in
- WHEN an authenticated user signs in for the first time
- THEN the system creates their local record from the token's `sub`, name and email

#### Requirement: Per-project roles in the local database
The system SHALL manage project membership and roles in PostgreSQL, not in Keycloak. Roles: `admin` (manages the project and its members), `analyst` (edits and runs scripts), `viewer` (only views dashboards). There is also a global `superadmin` role, defined in configuration.

##### Scenario: Access to another project
- WHEN a user requests a project they are not a member of
- THEN the API responds 404

##### Scenario: Viewer tries to edit a script
- WHEN a user with the `viewer` role tries to save a script
- THEN the API responds 403

### 5.2 Project management (`projects`)

#### Requirement: Project linked to a Kobo form
Each project SHALL be linked to exactly one Kobo form, defined by server URL (kf, eu or self-hosted), asset UID and API token.

##### Scenario: Project creation
- WHEN a user creates a project with a valid server, UID and token
- THEN the system checks access to the form, stores the token encrypted, generates a webhook secret and shows the webhook URL to configure in Kobo

##### Scenario: Invalid token
- WHEN the token has no access to the form
- THEN creation fails with a clear message

#### Requirement: Check of the fields the performance dashboard needs
When a project is created or synchronized, the system SHALL check whether the form contains `start`, `end` and an enumerator identifier (`username` or a configured question), and report which metrics will be unavailable if any are missing.

#### Requirement: Member management
A project `admin` SHALL be able to add members, change their role and remove them. A user can belong to several projects with different roles.

#### Requirement: Sampling frame
Each project SHALL be able to define a sampling frame: dimensions (for example municipality, sex, age range), the form variable that maps to each one, and the target number of surveys per combination. It is loaded from the UI or from a CSV/XLSX file.

### 5.3 Kobo ingestion (`ingestion`)

#### Requirement: Webhook intake
The system SHALL expose one endpoint per project for Kobo REST Services, authenticated with the project's secret.

##### Scenario: Valid submission
- WHEN Kobo sends a record with the correct secret
- THEN the system stores the raw JSON in `raw_submissions` and responds 200 without waiting for further processing

##### Scenario: Invalid secret
- WHEN a request arrives with a wrong or missing secret
- THEN it responds 401 and stores nothing

##### Scenario: Duplicate record
- WHEN a record arrives whose `_uuid` already exists in the project
- THEN the existing record is updated (upsert); it is never duplicated

#### Requirement: Periodic synchronization
The system SHALL run a synchronization per project every 12 hours (interval configurable per project) that queries the Kobo API v2 for submissions after the last successful synchronization, with a safety margin, and stores them with upsert.

##### Scenario: Lost webhook
- WHEN a submission did not arrive by webhook
- THEN the next synchronization picks it up

##### Scenario: Manual synchronization
- WHEN an `admin` or `analyst` clicks "Sync now"
- THEN the synchronization runs immediately

#### Requirement: Deletion reconciliation
Periodically (configurable, once a day by default) the system SHALL compare the IDs present in Kobo with the stored ones and mark as deleted (soft delete) those that no longer exist in Kobo.

#### Requirement: Synchronization log
Each synchronization SHALL be recorded with start, end, new, updated and deleted records, and the error if there was one.

#### Requirement: Record origin
Each raw submission SHALL record its origin (`webhook` or `sync`), the form version (`__version__`) and the reception date.

### 5.4 Python script chain (`pipeline`)

#### Requirement: Automatic flattening
Before the user scripts, the system SHALL flatten the non-deleted raw submissions into the `p_<id>_raw` schema: one main table and one table per repeat group with a key to the parent record. Answers to select questions are written as choice labels in the form's default language, taken from the definition of the form version each submission was made with. Each column that holds the answers to a question carries that question's label, in the default language, from the form's latest deployed version only, as a PostgreSQL column comment published with the table.

#### Requirement: Scripts with a fixed contract
Each project SHALL have up to two optional scripts, script 1 and script 2, and any number of indicator scripts (up to 100), each with a fixed-signature function:

```python
# 1. General cleaning
def clean(raw: dict[str, pd.DataFrame], ctx) -> dict[str, pd.DataFrame]: ...

# 2. Output processing (dashboard 2)
def prepare_output(cleaned: dict[str, pd.DataFrame], ctx) -> dict[str, pd.DataFrame]: ...

# 3. One indicator (dashboard 3); a project has one such script per indicator, named like a table
def compute(cleaned: dict[str, pd.DataFrame], ctx) -> pd.DataFrame: ...
```

An indicator has a name (the name of its published table, immutable), a label, an optional description, an optional unit and a format (`count`, `decimal`, `percent` or `currency`), saved with every version of its script.

`ctx` exposes: the project's reference data, the form dictionary (questions, choices and labels), a logger whose messages are shown in the UI and `ctx.show(data, name)`, which adds a preview of a DataFrame or value to the result of a sample test (and does nothing in a run).

#### Requirement: Optional scripts with default behavior
- Without script 1, `cleaned` = `raw`.
- Without script 2, dashboard 2 uses `cleaned`.
- Without indicator scripts, no indicator step runs, the indicator schema is removed and dashboard 3 shows an empty state with instructions.

#### Requirement: Execution order
Script 1 SHALL run first. Script 2 and the indicator scripts depend only on script 1; they run after it one at a time, the indicators in the order of their names. If script 1 fails, script 2 and the indicator scripts do not run.

#### Requirement: Atomic publishing
Script 1 and script 2 SHALL write their results to a temporary schema and replace the published schema only if they finish without errors and pass validation. Each indicator SHALL replace only its own table in the indicator schema the same way, so an indicator that fails keeps its previous table and does not stop the others; the run still ends as failed and names the indicator.

##### Scenario: Script failure
- WHEN a script raises an exception
- THEN the dashboards keep showing the last published version and the run ends in state `failed` with the traceback visible

#### Requirement: Indicator output validation
The output of an indicator script SHALL be one DataFrame with at least one numeric column; any other columns are its dimensions. By convention `value` is the default measure and `numerator` and `denominator` let a widget plot a ratio of sums. If there is no numeric column, the indicator's step fails and nothing is published for it.

#### Requirement: Run triggers
The chain SHALL run manually, when a synchronization finishes (configurable) or on a configurable schedule. Never on every webhook.

#### Requirement: Script versioning
Every save of a script SHALL create an immutable version. Each run records which version of each script it used, duration, input and output rows, logs and errors.

#### Requirement: Sample test run
An `analyst` SHALL be able to run a script in test mode on a sample (500 records by default) and see a preview of the resulting tables without publishing anything. A test can run through one `# %%` cell of the script (stopping at the next marker, with no output validation), shows what the script passed to `ctx.show`, reuses the flattened sample and the result of the saved script 1 from an earlier test when nothing they read changed, and for an indicator shows its table as the indicators dashboard will offer it, with warnings.

#### Requirement: Run limits
Each run SHALL have configurable time and memory limits. Scripts cannot install packages.

#### Requirement: Reference data
An `analyst` SHALL be able to upload reference tables (CSV/XLSX), for example population projections, accessible from `ctx`.
> Pending decision: in the MVP, reference data is per project; evaluate a catalog shared across projects later.

### 5.5 Dashboard 1: project performance (`dashboard-performance`)

#### Requirement: Field metrics on raw data
The dashboard SHALL be computed on Tamiza's stored copy of the non-deleted raw submissions (it does not contact Kobo and does not depend on the scripts), for an optional range of days, and show:
- Total records and their daily evolution. A day is the date Kobo received the submission, in the project's time zone (UTC by default; an admin sets it).
- Completion time (`end` − `start`): median and mean without outliers (Tukey's fences), with unusable durations counted apart.
- Per-enumerator detail: records, mean time, outliers, last submission. The enumerator is the question an admin selected or else `username`; values are compared ignoring case and spaces.
- Sampling frame coverage: achieved vs target per combination of dimensions, matching the form's codes ignoring case and spaces, with an uncapped progress percentage, overall progress, the submissions outside the frame, and warnings for dimensions that cannot be counted (field removed, inside a repeat group, multiple choice).

##### Scenario: Form without `start`/`end`
- WHEN the form does not have those fields
- THEN the time metric is shown as unavailable with the explanation

##### Scenario: Project without a sampling frame
- WHEN no sampling frame has been defined
- THEN the coverage section invites the user to configure one

### 5.6 Dashboard 2: clean data (`dashboard-data`)

#### Requirement: Queryable table
Every project member, viewers included, SHALL be able to browse the tables that the last successful runs published: the output tables (script 2) when published, otherwise the clean tables, with a switch to the other when both exist (the flattened raw tables are not offered). The dashboard SHALL offer a table selector with row counts, pages of 25, 50 or 100 rows, sorting by one column (empty values last) and one filter per column combined with AND, all resolved on the server:
- text: contains (ignoring case), is exactly, is one of (picked from the column's most frequent values);
- integers, decimals, dates and timestamps: a range with an optional minimum and maximum, both included, where a timestamp range is whole days in the project's time zone;
- true/false: is one of; any column: is empty, is not empty.

Column headers show the type and the question label when the column has one. The source, table, filters, sort and page are kept in the page address, and a part that no longer applies (a removed column or table) is dropped with a message. When a run republishes, the next request reads the new tables and the page says so.

#### Requirement: Export
Every member SHALL be able to export the full table, with the applied filters and sort, to CSV (UTF-8 with a byte order mark) and XLSX (typed cells, text never as a formula, at most 1,048,575 rows, cells cut at 32,767 characters and reported). The file is generated in the backend and downloaded through a single-use link that expires after a few minutes and needs no access token. A deployment limits the size of an export file and how many are prepared at once. Publishing a run's tables does not wait for a download, only for the file to be written.

### 5.7 Dashboard 3: indicators (`dashboard-indicators`)

> **Superseded by `add-indicators-dashboard`** (section 8, item 7). The two requirements below are the original scope, kept for reference: one fixed bar chart by disaggregation and an XLSX export. The change replaces them with the widget canvas described next; see `docs/indicators-dashboard.md`.

#### Requirement: Generic indicator view *(original, superseded)*
The dashboard SHALL read the standard indicator table and show each indicator with its value, numerator and denominator, with a filter by disaggregation and a bar chart by disaggregation.

#### Requirement: Indicator export *(original, superseded)*
The user SHALL be able to export the indicator table to XLSX.

#### Requirement: Widget canvas
A project SHALL have 1 to 7 indicators dashboards of its own (`add-multiple-dashboards`), each with a name, its own widgets and an address that survives renames and moves; `analyst` and `admin` members SHALL be able to add, rename, move, duplicate and remove them, and a project always keeps one. Every project member SHALL be able to open each of them, a canvas of widgets over the tables the indicator scripts published, and `analyst` and `admin` members SHALL be able to add, edit, arrange, remove and move widgets between them; a `viewer` only sees them. A widget is a stat tile, a bar, column, line, histogram or pie chart, or a map, each with a title and a query over exactly one indicator's table: a measure (a numeric column, `value` by default) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie and map widgets) and up to 50 filters built as the clean data dashboard's. Rows that share a group value are summed, or combined as a ratio of sums, before they are plotted, and values are formatted with the indicator's format and unit. A dashboard holds a limited number of widgets and a widget plots a limited number of rows, and the dashboard says when a limit is reached.

Widgets sit on a 12-column grid that analysts and admins move and resize by dragging or with the keyboard; the reading order is top to bottom, left to right. Charts and maps are drawn as inline SVG with no chart library, each with a "Show as table" alternative. A map draws the `POINT`, `POLYGON` and `MULTIPOLYGON` geometry (WKT) that an indicator script publishes in a column of its table, as regions or markers colored by the plotted value; rows whose geometry cannot be read are left out and counted. A widget whose indicator has not published, or whose column the table no longer has, shows a message naming what is missing instead of failing the page. Every member can export the whole published table of one indicator to CSV and XLSX through the single-use link of the clean data dashboard.

---

## 6. Minimal data model (metadata)

- `users`: id, keycloak_sub, name, email.
- `projects`: id, name, kobo_server_url, asset_uid, encrypted_api_token, encrypted_webhook_secret, sync_interval, settings, form_fields and form_choices (the snapshot of the form's deployed version), form_version (the uid of that version), time_zone (the IANA zone that defines the project's days, UTC by default).
- `project_members`: project_id, user_id, role.
- `raw_submissions`: id, project_id, kobo_uuid (unique per project), kobo_id, form_version, data (JSONB), origin, received_at, deleted.
- `syncs`: id, project_id, type, started_at, finished_at, created_count, updated_count, deleted_count, error.
- `script_versions`: id, project_id, kind (clean/output/indicator), name (the indicator's name; empty for the two fixed scripts), code, label, description, unit, format (the indicator's metadata at that version), number (per project and kind, and per name), author_id, created_at.
- `runs`: id, project_id, status (queued/running/succeeded/failed), trigger, the versions of script 1 and script 2 used and `indicator_versions` (name, version, number of each indicator script used), reference_tables, started_at, finished_at, error. Its steps are in `run_steps`: one for flatten, clean and output and one per indicator (`step` = `indicator` and its `name`), each with status, duration, rows and log.
- `sampling_frames`: project_id, dimensions, targets.
- `script_tests`: id, project_id, requested_by, kind, code (the tested draft), sample_size, status, clean_version_id, queued_at, started_at, finished_at, error, stop_line (the line of the `# %%` marker the draft is run through, null for a whole test), steps (per-step logs, table previews, what the draft showed and, for an indicator, the facts of its table). Deleted 24 hours after they end.
- `reference_tables`: id, project_id, name, file_name, columns (name, header, type), row_count, updated_by, updated_at. The rows are in `reference_table_contents` (one JSONB array per table), not in a per-project schema.
- `dashboards`: id, project_id, name, normalized_name (the trimmed name in lower case, unique per project), position (0 to n - 1 within the project), created_by, created_at, updated_by, updated_at. One row per indicators dashboard of a project's own, 1 to 7 per project; a new project starts with one named "Indicators". Removed with its project.
- `dashboard_widgets`: id, project_id, dashboard_id (the dashboard it is on; removing the dashboard removes its widgets), chart_type (stat/bar/column/line/histogram/pie/map), title, config (the widget's query: indicator, measure or ratio, group column, filters, geometry column, histogram ranges), layout (x, y, w, h on its dashboard's 12-column grid), created_by, created_at, updated_by, updated_at. One row per widget.
- `assistant_calls`: id, project_id, user_id, kind, name, action (script/cell/fix), test_id, model, prompt_kit, context (the size of each part of the context, what was left out and whether a test was attached; no text), status (running/succeeded/failed/cancelled), error_code, prompt_tokens, completion_tokens, total_tokens, tokens_estimated, outcome (how the analyst applied the proposal: script/cell/insert), started_at, finished_at. One row per request to the AI assistant; it holds no message, script or answer. The daily token budget per user adds up these rows.
- `form_versions`: project_id, uid (a Kobo form version, `__version__`), status (pending/available/failed/not_found), fields and choices (the version's questions and choice lists, with labels in the default language), deployed_at, recorded_at, fetched_at, attempts, next_attempt_at, error. One row per version a project's submissions use; recorded when a submission with the version is stored, fetched from Kobo once.

---

## 7. Non-functional requirements

- Full deployment with `docker compose up` and a documented `.env` file (`.env.example`).
- Hardware target for self-hosting: to be defined; initial reference, a 4 GB RAM VPS with per-service memory limits.
- Webhook response < 500 ms.
- Reference volume per project: up to 500,000 submissions.
- Backup: documented `pg_dump` script (delivered in `add-backup-restore`).
- Security: while there is no sandbox, only `analyst` and `admin` save code; the `analytics` container has no published ports.

---

## 8. Suggested change plan for OpenSpec

Implement in this order, one change per line:

1. `setup-foundation`: repository structure, Docker Compose with the 4 services, healthchecks, `.env.example`, OIDC integration with Keycloak in `api` and `ui`.
2. `add-project-management`: projects, Kobo connection, members and roles, sampling frame.
3. `add-kobo-ingestion`: webhook, periodic and manual synchronization, deletion reconciliation, synchronization log.
4. `add-python-pipeline`: flattening, run queue, the three scripts, versioning, atomic publishing, run triggers and limits.
4b. `add-pipeline-authoring`: sample test run with preview, reference data available from `ctx`.
4c. `add-choice-labels`: the flattened tables show choice labels, from the form version of each submission, instead of codes (BREAKING for scripts that compare select columns with codes); `ctx.form.name`; form versions listed in the Data section.
4d. `add-question-labels`: the flattened columns carry their question's label, from the latest deployed form version only, as column comments, in `ctx.form.columns` and in the sample-test preview. Not breaking.
5. `add-performance-dashboard`: the first dashboard (submissions per day, completion time, enumerators, sampling frame coverage) computed in SQL on the stored submissions, with a per-project time zone and a date range; the field check reports coverage unavailable without a usable frame.
6. `add-clean-data-dashboard`: the second dashboard: the tables the last runs published (output, else clean) in a paged table with typed server-side filters, sorting and a view kept in the page address, and CSV and XLSX exports downloaded through single-use links; publishing waits in short attempts for readers of the tables, so a long export never blocks the dashboard.
7. `add-indicators-dashboard`: the third dashboard, replacing the fixed bar chart of section 5.7 with a canvas of widgets (stat tile, bar, column, line, histogram, pie and map) over the indicator table script 3 published, each a saved query that can group by any column, with a drag-and-resize grid editable by analysts and admins, maps drawn from a WKT geometry column without a basemap, and CSV and XLSX export of the whole table; widgets are one row each in `dashboard_widgets`.

### Next, in this order (agreed 2026-10-14, before loading real data)

7a. `split-indicator-scripts`: instead of one script 3 returning one `indicators` table, a project has any number of named indicator scripts, each returning one table published under its name in `p_<id>_indicators`, with the rows and columns its indicator needs (any numeric column as a measure, `value` by default; `numerator`/`denominator` optional for ratios of sums), metadata per indicator (label, description, unit, format), per-table publishing so one failing indicator leaves its previous table and the others publish, and widgets that plot one indicator table each. Clean cut: the single-table contract is dropped without migration, since no real data exists yet. Implemented.
7b. `add-script-workbench`: script-editing ergonomics: a panel of the available tables and columns with their types, autocompletion of `ctx`, tables and columns in the editor, Tab indentation and basic linting, inline `ctx` reference and better templates, and stateless exploration cells on the sample test (`# %%` cells and `ctx.show(df, name)` previews re-run from scratch on a cached sample; no persistent kernel) with an "as the dashboard will see it" preview for indicator tables. Implemented.
7c. `add-local-dev-kit`: "Download sample" in the Scripts section: the worker prepares, like a test and reusing its cached sample, a zip with the flattened tables (script 1's input), the saved script 1's result on them, the reference tables and `context.json`, kept an hour in the database and downloaded through a single-use link; the zip also holds the `tamiza_sdk` package, built from the database-free runner (`load_context`, `load_tables`, `check`), pinned requirements, a README and a starter notebook, to write scripts in Jupyter locally, check them as the server runs them and upload them. Implemented.
7d. `add-script-assistant`: an optional AI assistant in the Scripts section, available only when the operator sets an OpenAI-compatible provider (base URL, model and key) for `api`: an Assistant tab, for analysts and admins, that drafts a whole script, rewrites the `# %%` cell at the cursor and fixes a failed sample test or a warning of an indicator's dashboard view, from the contract rules, the templates, the tables and columns with types and question labels, the choice lists, the saved script 1 and the draft. It sends no rows: from the analyst's own last test only its shape and its error and log lines, reduced to the script's frames and masked, with every word or number that is not a known name replaced by `…`; "Show what will be sent" and "Copy context" show the exact text. A proposal reaches the editor only when applied, as one undoable change, after the editor's checks (including a new import check for libraries the worker lacks) have marked it; the assistant never saves or tests. A daily token budget and one request at a time per user, usage records without text in `assistant_calls`, the conversation only in the browser session, and a `docs/operations.md` section with example providers and seven requests to check a provider. Implemented.
7e. `improve-map-widget`: the map widget becomes usable for field work such as following survey progress block by block: zoom and pan (buttons, mouse, Ctrl/⌘ and the wheel, two fingers, keyboard) on a Leaflet engine loaded with the map, shapes at their geographic position when the coordinates are degrees, a tooltip with up to 5 chosen columns of the indicator table (labels, formats; numbers summed and other columns showing their smallest value when rows share a group) and keyboard access to the shapes, and an optional basemap per widget (streets or satellite) that any member can switch for their own screen. The operator chooses one provider with `TAMIZA_MAP_BASEMAP_PROVIDER`: none (the default, no request to anyone), `osm` (OpenStreetMap streets, loaded by the browser) or `google` (streets and satellite relayed by `api` through expiring tile links, with the key kept on the server, per-user limits, no storage and Google's logo and credits always shown). Specified; being implemented.
7f. `add-multiple-dashboards`: a project has 1 to 7 indicators dashboards of its own instead of one, shown as tabs after Performance and Clean data, each with a name (1 to 40 characters, unique in the project whatever the case, never Performance, Clean data or Settings, shown as typed), its own widgets on its own grid, and an address by identifier, `/projects/<id>/dashboards/<dashboard id>`, that survives renames and moves (the former `/indicators` address, and a removed dashboard's with a notice, open the first one). Analysts and admins add, rename, move left and right, duplicate (with a copy of every widget, right after the original) and remove (with its widgets, after a confirmation that counts them; never the last) dashboards from a header on each, and move a widget to another dashboard from its form, even a stale one. The widget limit counts one dashboard's widgets (`IndicatorDashboard__MaxWidgetsPerDashboard`, with the former `IndicatorDashboard__MaxWidgetsPerProject` still read); one lock per project serializes the changes to its dashboards and their widget counts, and a change that meets another member's removal or reorder is refused with a `dashboards.*` code and the page catches up. The tab row scrolls inside itself from 768 pixels with Settings kept visible. `tamiza.dashboards` and `dashboard_widgets.dashboard_id`, with every existing project migrated to one "Indicators" dashboard holding all its widgets. Specified; being implemented.

Deferred with a reason: a Jupyter-like persistent kernel in the browser (it contradicts the per-step process isolation, the one-job worker and the 4 GB VPS target); a shared helper module across indicator scripts; widgets that compare several indicator tables.

### Deferred from `setup-foundation`

These items were scoped out of `setup-foundation` to keep it small. They come after the changes above unless reprioritized; each one can be scheduled earlier without affecting the others.

8. `add-audit-log`: immutable audit log (actor, action, target, project, UTC timestamp, details) written in the same transaction as the operation and rejected by the database on update or delete. It also adds events for the operations delivered before it (user provisioning, project and member management, synchronizations, script saves, runs), including the changes to a project's dashboards that `add-multiple-dashboards` does not record: adding, renaming, moving, duplicating and removing a dashboard, and moving a widget to another.
9. `harden-deployment`: separate database roles per service, with a least-privilege role for `analytics` that cannot change the metadata schema, and no Internet egress for `analytics` (internal-only network).
10. `add-backup-restore`: documented backup and restore scripts. A backup contains a `pg_dump` of the database and the `api` Data Protection keys, which are needed to decrypt stored secrets.
11. `add-dev-tooling`: GitHub Actions CI (build, unit and integration tests for the three services, Compose smoke test) and a `compose.dev.yaml` override that publishes `db` and `api` on `127.0.0.1` for local development.
