# Tamiza

**From survey to indicator.** Tamiza is an open source web application for KoboToolbox: it receives the data captured in Kobo forms, runs Python scripts to clean it, transform it and calculate indicators, and presents the results in dashboards. It replaces the manual Kobo → Jupyter → Excel/Power BI routine of consultants and analysts who run population and market studies.

Tamiza is compatible with KoboToolbox but is not affiliated with it.

> **Status:** early development. This release contains the foundation: the self-hosted stack, sign-in through Keycloak and the service skeletons. Kobo projects, ingestion, the script pipeline and the dashboards come next (see `docs/tamiza-brief.md`, section 8).

## Architecture

| Service | Technology | Role |
|---|---|---|
| `ui` | Angular, served by nginx | User interface. The only service with a published port; proxies `/api/` to `api`. |
| `api` | ASP.NET Core (.NET 10) | REST API under `/api/v1`, authentication, database migrations. |
| `analytics` | Python 3.12 | Worker that will flatten submissions and run user scripts. Internal only. |
| `db` | PostgreSQL 17 + PostGIS | Metadata, submissions and results. Internal only. |

Users sign in with an existing Keycloak instance over OpenID Connect. Keycloak is not part of the stack.

## Quick start

You need Docker with Compose v2, about 4 GB of RAM, and a Keycloak realm prepared as described in [docs/keycloak.md](docs/keycloak.md).

```sh
git clone https://github.com/rinconsebastian/tamizaai.git
cd tamizaai
cp .env.example .env
```

Edit `.env` and fill in the required values:

- `TAMIZA_PUBLIC_URL`: where users open Tamiza, for example `https://tamiza.example.org`, or `http://localhost:8088` on your machine.
- `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_AUDIENCE`: your Keycloak realm URL, UI client and API audience.
- `POSTGRES_PASSWORD`: generate one with `openssl rand -hex 24`.

Then start the stack (the first build takes a few minutes):

```sh
docker compose up -d --build --wait
```

Open `TAMIZA_PUBLIC_URL` in a browser and sign in. Every variable is documented in [.env.example](.env.example).

Outside `localhost`, serve Tamiza over HTTPS through a reverse proxy, because browsers only allow the sign-in flow in a secure context. [docs/operations.md](docs/operations.md) covers HTTPS, upgrades, memory limits and day-to-day operation.

## Development

Prerequisites: .NET SDK 10, Node.js 24, [uv](https://docs.astral.sh/uv/) and Docker.

| Part | Commands |
|---|---|
| API (`tamiza-api/`) | `dotnet build`, `dotnet test`. The integration tests start PostgreSQL with Testcontainers, so Docker must be running. |
| Analytics (`tamiza-analytics/`) | `uv sync`, `uv run pytest`, `uv run ruff check`, `uv run ruff format --check` |
| UI (`tamiza-ui/`) | `npm ci`, `npm test -- --watch=false`, `npm run build`. `npm start` serves the UI on `http://localhost:4200` and proxies `/api` to a stack running on `http://localhost:8088`. |

Planned work is tracked as OpenSpec changes in [openspec/](openspec/). The product brief is [docs/tamiza-brief.md](docs/tamiza-brief.md).

## Repository layout

```
compose.yaml, .env.example   Self-hosted stack
tamiza-api/                  ASP.NET Core API and its tests
tamiza-analytics/            Python worker package (tamiza) and its tests
tamiza-ui/                   Angular UI and nginx configuration
scripts/                     Repository checks
docs/                        Keycloak setup, operations guide, product brief
openspec/                    Specs and planned changes
```

## License

[MIT](LICENSE)
