# Design

## Context

Greenfield repository. What exists today:

- `LICENSE` (MIT), a one-line `README.md`, a `.gitignore` that only ignores `.claude/`.
- Three empty folders that fix the service layout: `tamiza-ui/`, `tamiza-api/`, `tamiza-analytics/`.
- `docs/tamiza-brief.md` (product brief) and `openspec/` with no specs yet.

Constraints that shape the approach (see `proposal.md` for motivation):

- Keycloak is an existing external instance; nothing in this repo may assume it runs next to Tamiza.
- Target host is a small VPS (reference: 4 GB RAM) run by non-specialist operators.
- Local toolchain on the author's machine: .NET SDK 10.0.112, Node 24, Docker 29. The images must still build without depending on locally installed tools.

## Goals / Non-Goals

**Goals:**

- One `docker compose up` brings up a working, authenticated, empty Tamiza.
- Establish the cross-cutting patterns later changes reuse: API conventions, schema ownership and naming, runtime configuration, and the test harnesses.

**Non-Goals:**

- Per-project roles and membership (`add-project-management`), the run queue table (`add-python-pipeline`), and anything Kobo-related.
- Work deferred to dedicated later changes (`docs/tamiza-brief.md` section 8):
  - `add-audit-log`: audit events, including retrofitting `user.provisioned` / `user.updated` into the provisioning step (D6).
  - `harden-deployment`: per-service database roles and no Internet egress for `analytics`.
  - `add-backup-restore`: backup/restore scripts covering the database and the Data Protection keys.
  - `add-dev-tooling`: GitHub Actions CI and a `compose.dev.yaml` override.
- Running Keycloak inside the Compose stack, even for development.
- TLS termination inside the stack. Operators put their own HTTPS reverse proxy in front.
- Publishing images to a registry.
- Multiple `api` replicas.
- Choosing a UI component library. The shell is small enough to need none. The first change with real forms (`add-project-management`) picks one.

## Decisions

### D1. Repository layout

```
compose.yaml, .env.example
scripts/check-env-example.sh
tamiza-api/                 # Tamiza.sln, global.json, src/Tamiza.Api, tests/Tamiza.Api.UnitTests, tests/Tamiza.Api.IntegrationTests
tamiza-analytics/           # pyproject.toml, uv.lock, src/tamiza, tests
tamiza-ui/                  # Angular workspace, nginx config
docs/                       # keycloak.md, operations.md (+ existing brief)
```

`Tamiza.Api` is a single project organized by feature folders (`Auth/`, `System/`, `Data/`). Splitting into Domain/Infrastructure projects is deferred until a real boundary appears. *Alternative:* a clean-architecture split from day one. Rejected because it adds ceremony with nothing to put in it yet.

### D2. Platform versions

| Piece | Choice | Why |
|---|---|---|
| API runtime | .NET 10 (LTS), `mcr.microsoft.com/dotnet/aspnet:10.0` | .NET 9 support ends in November 2026. A `global.json` pins SDK 10 (`rollForward: latestFeature`). |
| UI | Current stable Angular, standalone components and signals; build on `node:24-alpine`, serve on `nginxinc/nginx-unprivileged:stable-alpine` | Node 24 is the active LTS. The unprivileged nginx runs as non-root on port 8080. |
| Analytics | `python:3.12-slim` (fixed by the brief), dependencies managed with uv (`pyproject.toml` + `uv.lock`) | The lock file gives exact, reproducible pins. geopandas wheels ship GDAL, so a slim image is enough. |
| Database | `postgis/postgis:17-3.5` | Current PostgreSQL with PostGIS, created in the default database by the image's init scripts. |

### D3. `ui` is the only entry point

nginx in the `ui` container serves the Angular build (SPA fallback to `index.html`) and reverse-proxies `/api/` to `api:8080`. Kobo webhooks in later changes arrive through the same path. The UI and the API share one origin, so CORS and a second published port are not needed. nginx also sets security headers (CSP, `X-Content-Type-Options`, `Referrer-Policy`).

*Alternatives:* publishing `api` on its own port (needs CORS, gives two public surfaces); adding Traefik or Caddy to the stack (an extra container and TLS logic that operators often already have).

### D4. UI configuration at runtime from the API

On startup the UI calls `GET /api/v1/system/config` (anonymous) and receives `{ oidc: { authority, clientId, scope }, version }`, then configures OIDC. Every deployment value lives in one `.env`, read by `api`, and the same `tamiza-ui` image works for any installation.

*Alternatives:* generating `config.json` with `envsubst` in the nginx entrypoint (duplicates the configuration across two containers); Angular build-time environments (one image per installation).

### D5. OIDC integration

- **UI:** `angular-auth-oidc-client` (OpenID certified), with authorization code + PKCE and refresh tokens. Tokens stay in session storage, never in local storage. A route guard covers every route. An HTTP interceptor attaches the bearer token only to same-origin `/api/` requests. When discovery or the token endpoint is unreachable, the app shows an error page instead of a blank screen.
- **API:** `JwtBearer` with `Authority = OIDC_AUTHORITY` and `Audience = OIDC_AUDIENCE`, `MapInboundClaims = false`, and issuer, audience, lifetime and signature all validated. The optional `OIDC_METADATA_ADDRESS` lets `api` fetch discovery and JWKS from an internal URL while the issuer stays the public one. Fallback policy: authenticated user required. Public endpoints opt out with `AllowAnonymous`.
- **Keycloak:** a public client `tamiza-ui` (PKCE S256, redirect URI `${TAMIZA_PUBLIC_URL}/*`) plus an audience mapper that adds `OIDC_AUDIENCE` to access tokens. Documented step by step in `docs/keycloak.md`.

*Alternatives:* `keycloak-js` (ties the UI to Keycloak specifically); a backend-for-frontend with cookie sessions (stronger against token theft by XSS, but adds session state and CSRF handling to `api`, and the brief specifies `ui → api` with an OIDC token). The BFF stays an option if the threat model changes.

### D6. User provisioning

Middleware that runs after authentication resolves the local user:

1. Read `sub`, `name` (fallback `preferred_username`, then `sub`), `email` (nullable) and `email_verified`.
2. A memory cache keyed by `sub` holds `(userId, name, email)` for 5 minutes. On a hit with unchanged claims, nothing touches the database.
3. On a miss or a change: `INSERT ... ON CONFLICT (keycloak_sub) DO UPDATE` when name or email differ.
4. The resolved user (local id plus superadmin flag) is exposed to endpoints through a scoped `ICurrentUser`.

The unique index on `keycloak_sub` handles concurrent first requests. The insert and update paths are kept distinct in code, so `add-audit-log` can attach `user.provisioned` and `user.updated` events to them in the same transaction.

### D7. Superadmin from configuration, computed per request

`TAMIZA_SUPERADMIN_EMAILS` (comma-separated) is read at startup. `ICurrentUser.IsSuperAdmin` is true when `email_verified == true` and the email matches case-insensitively. Nothing is stored, so removing an email takes effect on restart, as the spec requires.

*Alternatives:* a list of Keycloak `sub` values (immutable, but unknown until the user exists, which complicates first setup); a database flag (needs a bootstrap mechanism and UI).

### D8. Database layout and naming

- Database `tamiza`; metadata schema `tamiza`, owned by `api`; EF Core migrations history in the same schema.
- EF Core with Npgsql and `EFCore.NamingConventions` (snake_case). All timestamps are `timestamptz` in UTC. Primary keys are `uuid` (version 7, generated in the app).
- **Naming:** everything is in English with no exceptions, including the contracts analysts touch. The brief now uses the English names: metadata tables (`users`, `projects`, `project_members`, `raw_submissions`, `syncs`, `script_versions`, `runs`, `sampling_frames`, `reference_tables`), project schemas (`p_<id>_raw|clean|output|indicators`), script functions (`clean`, `prepare_output`, `compute_indicators`), indicator columns (`indicator`, `disaggregation`, `value`, `numerator`, `denominator`) and roles (`admin`, `analyst`, `viewer`).

The only table in this change is `users`: `id`, `keycloak_sub` (unique), `name`, `email` (nullable), `created_at`, `updated_at`.

### D9. Database credentials

`api` and `analytics` connect with the credentials the postgis image creates from `POSTGRES_USER` (default `tamiza`) and `POSTGRES_PASSWORD` (required). Compose builds `ConnectionStrings__Tamiza` for `api` and `TAMIZA_ANALYTICS_DATABASE_URL` for `analytics` from them. That role is a database superuser. Splitting it into a least-privilege `analytics` role and an owner role for `api` is the job of `harden-deployment`. No first-boot init scripts are needed in this change.

*Alternative:* creating per-service roles now. Deferred by decision, to keep this change small (see Risks).

### D10. Migrations at startup

When `TAMIZA_MIGRATE_ON_STARTUP=true` (the default), `api` runs `Database.MigrateAsync()` before it starts serving. `/health/ready` stays unhealthy until migrations have completed and the database answers. If a migration fails, the process logs the error and exits non-zero, Compose restarts it, and dependents never see a healthy `api`.

*Alternative:* a one-shot `api-migrate` service built from an EF migration bundle. Cleaner with several replicas, which this design does not support.

### D11. Data Protection keys outside the database

`PersistKeysToFileSystem("/var/lib/tamiza/keys")` on a named volume `api-keys` that only `api` mounts, with `SetApplicationName("tamiza")`. Because ciphertext and keys live in different places, a database dump or code running in `analytics` cannot decrypt the Kobo tokens and webhook secrets that later changes store. The keys are not encrypted at rest on the volume in the MVP. Including them in backups is part of `add-backup-restore`.

*Alternative:* `PersistKeysToDbContext`. Simpler backups, but it puts keys and ciphertext side by side, within reach of anything with database access.

### D12. API conventions

- Minimal APIs, with one `MapGroup("/api/v1")` and one route group per feature.
- Every error response is RFC 9457 Problem Details (`AddProblemDetails`, status code pages for 401/403/404, an exception handler that never leaks stack traces outside Development).
- JSON in camelCase. OpenAPI document generated with `Microsoft.AspNetCore.OpenApi` and served at `/api/v1/openapi.json`. No interactive viewer in production.
- Structured JSON console logging.
- Health: `/health/live` (process up) and `/health/ready` (migrations done + database reachable), not proxied by nginx. The image installs `curl` so the Compose healthcheck can call them.

### D13. Analytics skeleton

- Package `tamiza` under `tamiza-analytics/src/tamiza`. Entrypoint `python -m tamiza.worker`, run as a non-root user.
- The loop runs every `TAMIZA_WORKER_POLL_SECONDS` (default 5): `SELECT 1` through psycopg, then a touch of the heartbeat file `/tmp/tamiza-heartbeat`. `add-python-pipeline` replaces the body with job claiming.
- Healthcheck `python -m tamiza.health`: healthy when the heartbeat is younger than three poll intervals.

### D14. Compose topology and limits

- **Network:** the default Compose network for all four services. Isolating `analytics` from the Internet is part of `harden-deployment`.
- **Published port:** `ui` only, `${TAMIZA_HTTP_PORT:-8088}:8080`. The host default is 8088 because 8080 is Keycloak's default port, which developers often run locally. `api`, `analytics` and `db` declare no `ports`.
- **Startup order:** `depends_on` with `condition: service_healthy` as specified. `restart: unless-stopped` on every service.
- **Memory defaults** (`deploy.resources.limits.memory`, each overridable): `db` 1024m, `api` 512m, `analytics` 1536m, `ui` 128m, for a total of 3200m.
- **Volumes:** `db-data` and `api-keys`.
- **Required variables** use `${VAR:?message}` so Compose fails fast and names the variable: `TAMIZA_PUBLIC_URL`, `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_AUDIENCE`, `POSTGRES_PASSWORD`.
- **`scripts/check-env-example.sh`** lists every `${VAR...}` in `compose.yaml` and fails if one is missing from `.env.example`.
- **Local development** without `compose.dev.yaml`: API integration tests use Testcontainers, so they need no running stack, and `ng serve` proxies `/api` to the running stack's public port (`http://localhost:8088`), which nginx forwards to `api`.
- **Documentation:** `docs/operations.md` covers install, the HTTPS requirement, upgrade and memory tuning.

### D15. UI shell

Standalone Angular app with Transloco for runtime i18n. Only `en` ships in this change, and every user-facing string goes through translation keys, so adding a language means adding a file. The shell has a top bar with the product name, the user's name and "Sign out", a home page that greets the user from `GET /api/v1/me`, and the authentication-unavailable error page.

*Alternative:* `@angular/localize`, which needs one build per locale and does not fit a single self-hosted image.

### D16. Test strategy

- **API:** xUnit unit tests (claim mapping, superadmin rule). Integration tests use `WebApplicationFactory` + Testcontainers (`postgis/postgis:17-3.5`) + JWTs signed with a test RSA key: the factory replaces the JwtBearer signing key, issuer and audience, so the 401 scenarios run without Keycloak. They cover migrations, health, provisioning and concurrency.
- **Analytics:** pytest (worker loop, health logic) and ruff.
- **UI:** unit tests with the Angular CLI's default runner for the guard, the interceptor, the config loader, the shell and the error page.
- **Stack verification:** commands listed in `tasks.md`, run by hand against `docker compose up --wait` with dummy OIDC values (`api` contacts Keycloak only when a token arrives). `add-dev-tooling` automates them in CI.
- **Manual:** sign-in, return-to-route, sign-out, session expiry and Keycloak-unavailable against a real Keycloak, following a checklist in `docs/keycloak.md`.

## Risks / Trade-offs

- [PKCE needs WebCrypto, which browsers only offer in a secure context; serving the UI over plain HTTP on a non-localhost address breaks sign-in] → `docs/operations.md` makes HTTPS a hard requirement outside localhost, and the error page names the cause when `crypto.subtle` is missing.
- [The issuer the browser sees can differ from the URL `api` can reach (for example, Keycloak on the same host)] → the `OIDC_METADATA_ADDRESS` override, with issuer validation kept on the public URL.
- [Access tokens in browser storage are exposed to XSS] → session storage only, a strict CSP from nginx, and Angular's built-in sanitization. A BFF remains the upgrade path (D5).
- [`api` and `analytics` share a superuser database role, and `analytics` has Internet egress] → harmless while `analytics` runs no user code. User scripts arrive with `add-python-pipeline`, so `harden-deployment` should land before that change is used with real data.
- [No backup tooling, and losing the `api-keys` volume would make stored secrets unreadable] → this change stores no secrets. The first ones arrive with `add-project-management`, so `add-backup-restore` should land before production use.
- [No CI until `add-dev-tooling`] → every task in `tasks.md` names the local command that verifies it.
- [Startup migrations with more than one `api` replica would race] → single replica is a stated non-goal. Switch to a one-shot migration service (D10) before scaling out.

## Migration Plan

Greenfield: no data to migrate. To roll back, run `docker compose down -v` on a test host. Production installs do not exist yet.

## Open Questions

- **Hardware target:** the brief leaves it open. The defaults assume a 4 GB VPS and every limit is overridable.
- **Image distribution:** whether to publish images to GHCR so operators can install without building. Not needed for this change.
