# Tasks

## 1. Repository base

- [x] 1.1 Extend `.gitignore` for .NET (`bin/`, `obj/`), Node (`node_modules/`, `dist/`, `.angular/`), Python (`.venv/`, `__pycache__/`, `.ruff_cache/`, `.pytest_cache/`) and `.env`, keeping `.claude/`; verify with `git status` after creating sample ignored files
- [x] 1.2 Add `.editorconfig` (UTF-8, LF, 4 spaces for C#/Python, 2 for TS/JSON/YAML) and `.gitattributes` (`* text=auto eol=lf`); verify `git ls-files --eol` reports LF for new files

## 2. API skeleton

- [x] 2.1 Create `tamiza-api/` with `global.json` (SDK 10, `rollForward: latestFeature`), `Tamiza.sln`, `src/Tamiza.Api` (ASP.NET Core, .NET 10), `tests/Tamiza.Api.UnitTests` and `tests/Tamiza.Api.IntegrationTests` (xUnit); verify `dotnet build` and `dotnet test` succeed
- [x] 2.2 Configure the host: strongly typed options bound from environment variables (`TAMIZA_*`, `OIDC_*`, `ConnectionStrings__Tamiza`), JSON console logging, Problem Details for exceptions and 401/403/404 status codes, `/api/v1` route group, OpenAPI at `/api/v1/openapi.json`; verify an integration test gets `application/problem+json` for an unknown `/api/v1` route and 200 for the OpenAPI document
- [x] 2.3 Add `TamizaDbContext` (Npgsql, snake_case naming, default schema `tamiza`, migrations history in `tamiza`) with the `users` table and a unique index on `keycloak_sub`, plus the initial migration; verify an integration test against Testcontainers `postgis/postgis:17-3.5` applies migrations and finds `tamiza.users`
- [x] 2.4 Apply migrations at startup when `TAMIZA_MIGRATE_ON_STARTUP=true`, exit non-zero on failure, and expose `/health/live` and `/health/ready` (ready = migrations done + database reachable); verify integration tests: ready is 200 against a migrated database and 503 after the test container is stopped, and starting the app with an unreachable database exits non-zero without serving requests
- [x] 2.5 Configure Data Protection to persist keys at `/var/lib/tamiza/keys` with application name `tamiza`; verify a unit test that protects with one provider instance and unprotects with a second instance pointing at the same directory
- [x] 2.6 Write `tamiza-api/Dockerfile` (multi-stage SDK 10 → `aspnet:10.0`, non-root `app` user, `curl` installed, listens on 8080); verify `docker build` succeeds and `docker run` answers `/health/live`

## 3. API authentication

- [x] 3.1 Configure JwtBearer from `OIDC_AUTHORITY`, `OIDC_AUDIENCE` and optional `OIDC_METADATA_ADDRESS` (`MapInboundClaims = false`, issuer/audience/lifetime/signature validation) and a fallback policy requiring an authenticated user; in the integration test factory, replace the signing key, issuer and audience with test values; verify tests return 401 for no token, expired token, wrong issuer and wrong audience
- [x] 3.2 Implement `GET /api/v1/system/config` (anonymous) returning `{ oidc: { authority, clientId, scope }, version }`; verify integration tests: 200 without a token, no keys beyond those in the body, and two factories with different `OIDC_*` values return their own values
- [x] 3.3 Implement the user-provisioning middleware and `ICurrentUser` (claim mapping with `name` → `preferred_username` → `sub` fallback, `INSERT ... ON CONFLICT` upsert with separate insert and update paths, 5-minute cache keyed by `sub`); verify integration tests: the first request creates one user, a changed email updates the same row, and 10 concurrent first requests leave exactly one row
- [x] 3.4 Implement the superadmin rule from `TAMIZA_SUPERADMIN_EMAILS` (case-insensitive, requires `email_verified == true`); verify unit tests for listed+verified, listed+unverified, unlisted and mixed-case emails, and for an email dropped from the configuration
- [x] 3.5 Implement `GET /api/v1/me` returning `{ id, name, email, isSuperAdmin }`; verify integration tests for 200 with a valid token and 401 without one

## 4. Analytics skeleton

- [x] 4.1 Create `tamiza-analytics/` with `pyproject.toml` (package `tamiza`, `requires-python = "==3.12.*"`, dependencies pandas, numpy, polars, duckdb, geopandas, openpyxl, psycopg[binary]; dev: pytest, ruff) and a committed `uv.lock`; verify `uv sync --locked` and `uv run ruff check` succeed
- [x] 4.2 Implement `tamiza.worker` (loop every `TAMIZA_WORKER_POLL_SECONDS`, default 5: `SELECT 1` through psycopg using `TAMIZA_ANALYTICS_DATABASE_URL`, then touch `/tmp/tamiza-heartbeat`; clean shutdown on SIGTERM) and `tamiza.health` (exit 0 when the heartbeat is younger than three intervals, 1 otherwise); verify pytest covers a fresh heartbeat, a stale heartbeat and a missing heartbeat
- [x] 4.3 Write `tamiza-analytics/Dockerfile` (`python:3.12-slim`, uv installs from the lock, non-root user, entrypoint `python -m tamiza.worker`); verify `docker build` succeeds and `docker run --rm <image> python -c "import pandas, numpy, polars, duckdb, geopandas, openpyxl, psycopg"` exits 0

## 5. UI shell

- [x] 5.1 Create the Angular workspace in `tamiza-ui/` (standalone components, strict mode) with Transloco and an `en` translation file; verify `npm ci && npm run build` succeeds and no hard-coded user-facing string remains in templates (lint rule or review)
- [x] 5.2 Load `/api/v1/system/config` at startup and configure `angular-auth-oidc-client` (code + PKCE, refresh tokens, session storage) from it; verify a unit test with a mocked HTTP backend that the OIDC config is built from the response
- [x] 5.3 Add the auth guard on all routes (keeping the requested URL for return after login), the interceptor that attaches the bearer token only to same-origin `/api/` requests, and the authentication-unavailable error page (also shown when `crypto.subtle` is missing); verify unit tests for guard redirect, interceptor scoping and error-page routing
- [x] 5.4 Build the shell: top bar with product name, user name and "Sign out" (RP-initiated logout), and a home page greeting the user from `GET /api/v1/me`; verify a component test renders the name from a mocked `/me` response and that sign-out calls the OIDC logout
- [x] 5.5 Write `tamiza-ui/nginx.conf` (SPA fallback, `/api/` proxied to `http://api:8080`, CSP and security headers, port 8080) and `tamiza-ui/Dockerfile` (`node:24-alpine` build → `nginx-unprivileged:stable-alpine`); verify `docker build` succeeds and the container serves `index.html` for a deep link such as `/projects`
- [x] 5.6 Add `proxy.conf.json` for `ng serve` pointing `/api` at `http://localhost:8080` (the running stack's `ui` port); verify that with the stack from 6.3 running, `ng serve` loads the config endpoint through the proxy

## 6. Compose stack

- [x] 6.1 Write `compose.yaml` with services `ui`, `api`, `analytics`, `db` (image names `tamiza-ui`, `tamiza-api`, `tamiza-analytics`; `postgis/postgis:17-3.5`), the default network, `db-data` and `api-keys` volumes, healthchecks, `depends_on: service_healthy`, `restart: unless-stopped`, memory limits with defaults 1024m/512m/1536m/128m, database settings built from `POSTGRES_USER`/`POSTGRES_PASSWORD`, a published port only on `ui`, and `${VAR:?message}` for required variables; verify `docker compose config` succeeds with a filled `.env` and fails naming the variable when `OIDC_AUTHORITY` is removed
- [x] 6.2 Write `.env.example` documenting every variable used by `compose.yaml` (purpose, required/optional, default) and `scripts/check-env-example.sh`; verify the script passes, and fails after temporarily deleting one variable from `.env.example`
- [x] 6.3 Verify the full stack: with dummy OIDC values, `docker compose up -d --wait` reports all four services healthy, `curl <public URL>/` returns 200, `/api/v1/system/config` returns 200, `/api/v1/me` returns 401, and `docker compose ps --format json` shows a published port only on `ui`
- [x] 6.4 Verify resilience and persistence on the running stack: stopping `db` makes `api` unhealthy until `db` is back; after a signed-in request has created a user (or a row is inserted with `psql`), `docker compose down && docker compose up -d --wait` keeps the row and leaves the files in the `api-keys` volume unchanged by checksum
- [x] 6.5 Verify memory limits: `docker inspect` shows the default limits adding up to 3200m, and setting `ANALYTICS_MEMORY_LIMIT=2g` in `.env` changes the `analytics` limit

## 7. Documentation

- [x] 7.1 Rewrite `README.md` (what Tamiza is, "for KoboToolbox" wording, MIT license, architecture overview, quick start with `.env` and `docker compose up`, local development prerequisites: .NET SDK 10, Node 24, uv, Docker); verify the quick start works on a clean clone by following it literally
- [x] 7.2 Write `docs/keycloak.md`: public client `tamiza-ui` (PKCE S256, redirect URIs, web origins, post-logout redirect), audience mapper for `OIDC_AUDIENCE`, `email_verified` for superadmins, and the manual checklist (sign-in, return-to-route, sign-out, session expiry, Keycloak unavailable); verify by configuring a test realm from the document alone and completing the checklist
- [x] 7.3 Write `docs/operations.md`: install, HTTPS requirement outside localhost, upgrade (`git pull` + `docker compose up -d --build`), memory tuning, and a note that backups, per-service database roles and analytics isolation arrive in later changes; verify every command in it was run during group 6
