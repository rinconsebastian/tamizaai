# Tamiza

**From survey to indicator.** Tamiza is an open source web application for KoboToolbox: it receives the data captured in Kobo forms, runs Python scripts to clean it, transform it and calculate indicators, and presents the results in dashboards. It replaces the manual Kobo → Jupyter → Excel/Power BI routine of consultants and analysts who run population and market studies.

Tamiza is compatible with KoboToolbox but is not affiliated with it.

> **Status:** early development. Available: the self-hosted stack, sign-in through Keycloak, projects linked to a Kobo form with members, roles and a sampling frame, ingestion of submissions through Kobo's webhook plus scheduled synchronization ([docs/kobo.md](docs/kobo.md)), and the Python script chain that flattens, cleans and computes indicators into per-project tables, with tests of a draft script on a sample and project reference data ([docs/scripts.md](docs/scripts.md)). Select answers in those tables are choice labels, taken from the form version each submission was made with ([docs/kobo.md](docs/kobo.md#7-form-versions-and-choice-labels)), and each column carries the text of its question, from the form's latest deployed version, as a column comment ([docs/scripts.md](docs/scripts.md#question-labels)). The dashboards come next (see `docs/tamiza-brief.md`, section 8).
>
> **Breaking change:** scripts that compare select columns with codes (`main["water_source"] == "piped"`) stop matching, because the columns now hold labels (`"Piped"`). Update them to compare with labels, or use `ctx.form.name` to get the code of a label; see [Upgrading](docs/scripts.md#upgrading-from-scripts-that-compare-with-codes). Tables already published keep their codes until the project's next run.

## Architecture

| Service | Technology | Role |
|---|---|---|
| `ui` | Angular, served by nginx | User interface. The only service with a published port; proxies `/api/` to `api`. |
| `api` | ASP.NET Core (.NET 10) | REST API under `/api/v1`, authentication, database migrations, Kobo webhook intake and synchronization. |
| `analytics` | Python 3.12 | Worker that flattens submissions and runs the analysts' scripts. Internal only. |
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

Open `TAMIZA_PUBLIC_URL` in a browser, sign in and create a project from a Kobo form, following [docs/kobo.md](docs/kobo.md). Every variable is documented in [.env.example](.env.example).

Outside `localhost`, serve Tamiza over HTTPS through a reverse proxy, because browsers only allow the sign-in flow in a secure context. [docs/operations.md](docs/operations.md) covers HTTPS, upgrades, memory limits and day-to-day operation.

## Development

Prerequisites: .NET SDK 10, Node.js 24, [uv](https://docs.astral.sh/uv/) and Docker.

| Part | Commands |
|---|---|
| API (`tamiza-api/`) | `dotnet build`, `dotnet test`. The integration tests start PostgreSQL with Testcontainers, so Docker must be running. Database schema changes are SQL scripts; see [tamiza-api/src/dbUp/README.md](tamiza-api/src/dbUp/README.md). |
| Analytics (`tamiza-analytics/`) | `uv sync`, `uv run pytest`, `uv run ruff check`, `uv run ruff format --check`. The tests in `tests/integration` start PostgreSQL with Testcontainers and are skipped without Docker. |
| UI (`tamiza-ui/`) | `npm ci`, `npm test -- --watch=false`, `npm run build`. `npm start` serves the UI on `http://localhost:4200` and proxies `/api` to a stack running on `http://localhost:8088`. |

Planned work is tracked as OpenSpec changes in [openspec/](openspec/). The product brief is [docs/tamiza-brief.md](docs/tamiza-brief.md).

## Repository layout

```
compose.yaml, .env.example   Self-hosted stack
tamiza-api/                  ASP.NET Core API and its tests
tamiza-analytics/            Python worker package (tamiza) and its tests
tamiza-ui/                   Angular UI and nginx configuration
scripts/                     Repository checks
docs/                        Keycloak setup, Kobo guide, scripts guide, operations guide, product brief
openspec/                    Specs and planned changes
```

## License

[MIT](LICENSE)
