# Proposal

## Why

The repository holds only the license, an empty README and three empty folders (`tamiza-ui`, `tamiza-api`, `tamiza-analytics`). Every later change in the plan (projects, ingestion, pipeline, dashboards) needs the same base: the four services running with `docker compose up`, a database with its metadata schema, and users authenticated against Keycloak. Building that base once, first, keeps each later change from inventing its own infrastructure.

## What Changes

- Monorepo layout: `tamiza-ui/` (Angular), `tamiza-api/` (ASP.NET Core), `tamiza-analytics/` (Python package `tamiza`), with deployment configuration at the root.
- `compose.yaml` with the `ui`, `api`, `analytics` and `db` (PostgreSQL + PostGIS) services. It sets healthchecks, health-gated startup order, configurable memory limits and persistent volumes. Only `ui` publishes a port.
- A documented `.env.example`. A missing required variable stops startup with a clear message.
- `api` skeleton: versioned `/api/v1` API, OpenAPI, Problem Details errors, health endpoints, EF Core with migrations applied at startup, and ASP.NET Data Protection keys persisted outside the database.
- OIDC authentication against the external Keycloak, in `ui` (authorization code + PKCE) and in `api` (token validation). A local user record is created on first sign-in, and the global `superadmin` role is defined in configuration.
- `analytics` skeleton worker with pinned dependencies (Python 3.12, pandas, numpy, polars, duckdb, geopandas, openpyxl, psycopg), a database connection and a healthcheck. The run queue arrives in `add-python-pipeline`.
- `ui` shell in English, ready for i18n, with sign-in, sign-out and a home page showing the current user. The OIDC settings are read at runtime.
- Documentation: README with a quick start, Keycloak setup guide and operations guide.

Deferred to later changes, as listed in `docs/tamiza-brief.md` section 8: the audit log (`add-audit-log`), per-service database roles and network isolation for `analytics` (`harden-deployment`), backup and restore scripts (`add-backup-restore`), and CI plus a development Compose override (`add-dev-tooling`).

## Capabilities

### New Capabilities

- `auth`: OIDC authentication against an external Keycloak in the UI and the API, creation and refresh of the local user record, the global `superadmin` role from configuration, and the current-user profile. Per-project roles are added in `add-project-management`.
- `deployment`: self-hosted deployment with Docker Compose: single-command startup, documented environment variables, healthchecks, automatic migrations, network exposure, memory limits and persistence.

### Modified Capabilities

None: there are no existing specs in `openspec/specs/`.

## Impact

- **New code:** `tamiza-ui/`, `tamiza-api/`, `tamiza-analytics/`, `compose.yaml`, `.env.example`, `scripts/check-env-example.sh`, and documentation in `README.md` and `docs/`.
- **Initial public API:** `GET /api/v1/system/config` (anonymous) and `GET /api/v1/me` (authenticated), plus `/health/live` and `/health/ready` on the internal network.
- **Database:** database `tamiza` and metadata schema `tamiza` with the `users` table. `api` and `analytics` share one set of database credentials until `harden-deployment`.
- **External dependencies:** an existing Keycloak instance, where the operator registers a public client for the UI and an audience mapper for the API (documented in `docs/keycloak.md`). Deployments not served from `localhost` need HTTPS in front of the Compose stack.
- **Developer tooling:** .NET SDK 10, Node.js 24 and uv to work outside Docker. The images build without depending on locally installed tools.
