# Tamiza — Project brief for OpenSpec

> **Tamiza — from survey to indicator.** Domain: `tamiza.ai`.
> Tamiza is compatible with KoboToolbox but is not affiliated with Kobo. The "Kobo" trademark is used only descriptively ("for KoboToolbox"), never as part of the product name.
> Pending: trademark check with SIC and WIPO (class 42).
> This document is the source for the OpenSpec project context and for generating the first changes.

---

## 1. Purpose

Tamiza is an open source web application that complements KoboToolbox: it receives the data captured in Kobo forms, runs Python scripts for cleaning, transformation and indicator calculation, and presents the results in three dashboards.

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

### 2.3 Result storage

Each project has its own PostgreSQL schemas:

- `p_<id>_raw`: flattened tables (main table + one table per repeat group, with a key to the parent).
- `p_<id>_clean`: output of script 1.
- `p_<id>_output`: output of script 2.
- `p_<id>_indicators`: output of script 3.

Schema and table names are never built from user text without validation (allow list + quoted identifiers).

---

## 3. Conventions

- Technical product name: `tamiza` (repository, Docker images `tamiza-ui`, `tamiza-api`, `tamiza-analytics`, .NET namespace `Tamiza.*`, Python package `tamiza`).
- Everything in English: code, identifiers, database objects, API names, the UI and documentation. The UI is ready for i18n so other languages can be added later.
- Versioned REST API (`/api/v1/...`), documented with OpenAPI.
- Database migrations with DbUp: versioned SQL scripts in `tamiza-api/src/dbUp`; EF Core is only the data-access layer (`api` owns the metadata schema).
- Python 3.12, dependencies pinned in the image: pandas, numpy, polars, duckdb, geopandas, openpyxl, psycopg.
- Secrets (Kobo tokens, webhook secrets) encrypted at rest with ASP.NET Data Protection.
- Every relevant operation leaves a record: who, what, when. The audit mechanism arrives in `add-audit-log` (see section 8).
- Tests: unit tests in .NET and Python; integration tests for the webhook, synchronization and the script chain.

---

## 4. Out of scope for the MVP

AI assistant, strong sandbox for user code, multi-tenant SaaS, semantic layer, Word/Excel/PowerPoint templates, Power BI endpoint, S3 storage, dedicated message queue, visual block editor.

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
Before the user scripts, the system SHALL flatten the non-deleted raw submissions into the `p_<id>_raw` schema: one main table and one table per repeat group with a key to the parent record.

#### Requirement: Three scripts with a fixed contract
Each project SHALL have up to three optional scripts, each with a fixed-signature function:

```python
# 1. General cleaning
def clean(raw: dict[str, pd.DataFrame], ctx) -> dict[str, pd.DataFrame]: ...

# 2. Output processing (dashboard 2)
def prepare_output(cleaned: dict[str, pd.DataFrame], ctx) -> dict[str, pd.DataFrame]: ...

# 3. Indicator calculation (dashboard 3)
def compute_indicators(cleaned: dict[str, pd.DataFrame], ctx) -> pd.DataFrame: ...
```

`ctx` exposes: the project's reference data, the form dictionary (questions, choices and labels) and a logger whose messages are shown in the UI.

#### Requirement: Optional scripts with default behavior
- Without script 1, `cleaned` = `raw`.
- Without script 2, dashboard 2 uses `cleaned`.
- Without script 3, dashboard 3 shows an empty state with instructions.

#### Requirement: Execution order
Script 1 SHALL run first. Scripts 2 and 3 depend only on script 1 and can run in parallel. If script 1 fails, scripts 2 and 3 do not run.

#### Requirement: Atomic publishing
Each script SHALL write its results to a temporary schema and replace the published schema only if it finishes without errors and passes validation.

##### Scenario: Script failure
- WHEN a script raises an exception
- THEN the dashboards keep showing the last published version and the run ends in state `failed` with the traceback visible

#### Requirement: Indicator output validation
The output of script 3 SHALL contain the columns `indicator`, `disaggregation`, `value`, `numerator`, `denominator`. If any are missing, the run fails and nothing is published.

#### Requirement: Run triggers
The chain SHALL run manually, when a synchronization finishes (configurable) or on a configurable schedule. Never on every webhook.

#### Requirement: Script versioning
Every save of a script SHALL create an immutable version. Each run records which version of each script it used, duration, input and output rows, logs and errors.

#### Requirement: Sample test run
An `analyst` SHALL be able to run a script in test mode on a sample (500 records by default) and see a preview of the resulting tables without publishing anything.

#### Requirement: Run limits
Each run SHALL have configurable time and memory limits. Scripts cannot install packages.

#### Requirement: Reference data
An `analyst` SHALL be able to upload reference tables (CSV/XLSX), for example population projections, accessible from `ctx`.
> Pending decision: in the MVP, reference data is per project; evaluate a catalog shared across projects later.

### 5.5 Dashboard 1: project performance (`dashboard-performance`)

#### Requirement: Field metrics on raw data
The dashboard SHALL be computed on the non-deleted raw submissions (it does not depend on the scripts) and show:
- Total records and their daily evolution.
- Mean completion time (`end` − `start`), with outlier detection.
- Per-enumerator detail: records, mean time, last submission.
- Sampling frame coverage: achieved vs target per combination of dimensions, with progress percentage.

##### Scenario: Form without `start`/`end`
- WHEN the form does not have those fields
- THEN the time metric is shown as unavailable with the explanation

##### Scenario: Project without a sampling frame
- WHEN no sampling frame has been defined
- THEN the coverage section invites the user to configure one

### 5.6 Dashboard 2: clean data (`dashboard-data`)

#### Requirement: Queryable table
The dashboard SHALL show the tables in `p_<id>_output` (or `p_<id>_clean` if there is no script 2) with a table selector, pagination, per-column filters and sorting, all resolved on the server.

#### Requirement: Export
The user SHALL be able to export the full table, with the applied filters, to CSV and XLSX. The export is generated in the backend.

### 5.7 Dashboard 3: indicators (`dashboard-indicators`)

#### Requirement: Generic indicator view
The dashboard SHALL read the standard indicator table and show each indicator with its value, numerator and denominator, with a filter by disaggregation and a bar chart by disaggregation.

#### Requirement: Indicator export
The user SHALL be able to export the indicator table to XLSX.

---

## 6. Minimal data model (metadata)

- `users`: id, keycloak_sub, name, email.
- `projects`: id, name, kobo_server_url, asset_uid, encrypted_api_token, encrypted_webhook_secret, sync_interval, settings.
- `project_members`: project_id, user_id, role.
- `raw_submissions`: id, project_id, kobo_uuid (unique per project), kobo_id, form_version, data (JSONB), origin, received_at, deleted.
- `syncs`: id, project_id, type, started_at, finished_at, created_count, updated_count, deleted_count, error.
- `script_versions`: id, project_id, kind (1/2/3), code, author_id, created_at.
- `runs`: id, project_id, status (queued/running/succeeded/failed), trigger, script_versions_used, started_at, finished_at, logs, error.
- `sampling_frames`: project_id, dimensions, targets.
- `reference_tables`: id, project_id, name, table_name.

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
4. `add-python-pipeline`: flattening, run queue, the three scripts, versioning, sample test run, atomic publishing, reference data.
5. `add-performance-dashboard`
6. `add-clean-data-dashboard`
7. `add-indicators-dashboard`

### Deferred from `setup-foundation`

These items were scoped out of `setup-foundation` to keep it small. They come after the changes above unless reprioritized; each one can be scheduled earlier without affecting the others.

8. `add-audit-log`: immutable audit log (actor, action, target, project, UTC timestamp, details) written in the same transaction as the operation and rejected by the database on update or delete. It also adds events for the operations delivered before it (user provisioning, project and member management, synchronizations, script saves, runs).
9. `harden-deployment`: separate database roles per service, with a least-privilege role for `analytics` that cannot change the metadata schema, and no Internet egress for `analytics` (internal-only network).
10. `add-backup-restore`: documented backup and restore scripts. A backup contains a `pg_dump` of the database and the `api` Data Protection keys, which are needed to decrypt stored secrets.
11. `add-dev-tooling`: GitHub Actions CI (build, unit and integration tests for the three services, Compose smoke test) and a `compose.dev.yaml` override that publishes `db` and `api` on `127.0.0.1` for local development.
