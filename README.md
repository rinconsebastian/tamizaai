# Tamiza

**From survey to indicator.** Tamiza is an open source web application for KoboToolbox: it receives the data captured in Kobo forms, runs Python scripts to clean it, transform it and calculate indicators, and presents the results in dashboards. It replaces the manual Kobo → Jupyter → Excel/Power BI routine of consultants and analysts who run population and market studies.

Tamiza is compatible with KoboToolbox but is not affiliated with it.

> **Status:** early development. Available: the self-hosted stack, sign-in through Keycloak, projects linked to a Kobo form with members, roles and a sampling frame (built from the form's questions and choices, imported from a file with an Excel template, or typed in, and downloadable as a workbook; [docs/kobo.md](docs/kobo.md#sampling-frame)), ingestion of submissions through Kobo's webhook plus scheduled synchronization ([docs/kobo.md](docs/kobo.md)), and the Python script chain that flattens, cleans and computes indicators into per-project tables (one script and one table per indicator), with tests of a draft script on a sample and project reference data ([docs/scripts.md](docs/scripts.md)). The script editor shows the tables each script receives, completes `ctx`, tables and columns, checks the script as you type, offers templates and runs a test through one `# %%` cell, with the previews of `ctx.show` and an "as the dashboard will see it" view of an indicator's table ([docs/scripts.md](docs/scripts.md#the-editor)). Select answers in those tables are choice labels, taken from the form version each submission was made with ([docs/kobo.md](docs/kobo.md#7-form-versions-and-choice-labels)), and each column carries the text of its question, from the form's latest deployed version, as a column comment ([docs/scripts.md](docs/scripts.md#question-labels)). A project opens on its analysis dashboards (Performance, Clean data and the project's own indicators dashboards, from one to seven, each with its name and address); everything that configures it sits under Settings: General, Submissions, Scripts, Reference data, Sampling frame, Members and, for admins, Webhook ([docs/kobo.md](docs/kobo.md)). The performance dashboard shows, from the stored submissions, submissions per day, completion time, the work of each enumerator and the coverage of the sampling frame ([docs/performance-dashboard.md](docs/performance-dashboard.md)). The clean data dashboard lets every member browse, filter, sort and export (CSV or XLSX) the tables that the scripts published ([docs/clean-data-dashboard.md](docs/clean-data-dashboard.md)). Each indicators dashboard is a canvas of widgets (stat tiles, bar, column, line, histogram and pie charts, and maps) that analysts and admins arrange over the indicator tables, each widget plotting one indicator formatted with its unit, and that every member can see and export to CSV or XLSX ([docs/indicators-dashboard.md](docs/indicators-dashboard.md)); analysts and admins add, rename, move, duplicate and remove dashboards, and move widgets between them ([docs/indicators-dashboard.md](docs/indicators-dashboard.md#dashboards)). An optional AI assistant in the Scripts section, on only when the operator sets an OpenAI-compatible model provider, drafts a script, rewrites a cell and fixes a failed test from the project's schema, the choice lists and the shape and masked errors of the analyst's last test, and never sends rows of data; its proposals reach the editor only when the analyst applies them ([docs/scripts.md](docs/scripts.md#writing-with-the-assistant), [docs/operations.md](docs/operations.md#ai-assistant)). The editor also warns about imports the worker cannot satisfy.
>
> **Breaking change:** scripts that compare select columns with codes (`main["water_source"] == "piped"`) stop matching, because the columns now hold labels (`"Piped"`). Update them to compare with labels, or use `ctx.form.name` to get the code of a label; see [Upgrading](docs/scripts.md#upgrading-from-scripts-that-compare-with-codes). Tables already published keep their codes until the project's next run.
>
> **Breaking change:** the single script 3 `compute_indicators` is replaced by any number of indicator scripts, each `compute(cleaned, ctx)` returning its own table. Upgrading deletes the old script 3 versions and tests, every `indicators` table and every indicators dashboard widget; create the indicators again in **Settings > Scripts** ([docs/scripts.md](docs/scripts.md#3-indicator-scripts-computecleaned-ctx)).

## Architecture

| Service | Technology | Role |
|---|---|---|
| `ui` | Angular, served by nginx | User interface. The only service with a published port; proxies `/api/` to `api`. |
| `api` | ASP.NET Core (.NET 10) | REST API under `/api/v1`, authentication, database migrations, Kobo webhook intake and synchronization. With the AI assistant configured, it also calls the model provider. |
| `analytics` | Python 3.12 | Worker that flattens submissions and runs the analysts' scripts. Internal only. |
| `db` | PostgreSQL 17 + PostGIS | Metadata, submissions and results. Internal only. |

Users sign in with an existing Keycloak instance over OpenID Connect. Keycloak is not part of the stack.

The interface is in English or Spanish. It follows the browser's preferred languages (the first one that is Spanish or English wins, and English is used when there is none), and dates, numbers and times follow that language's region, such as `es-CO` or `es-ES`. There is no language switcher: change the language in the browser. Text written by the `api` and the `analytics` worker (field validation messages, run logs and errors) and the files they produce stay in English for now.

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
docs/                        Keycloak setup, Kobo guide, scripts guide, dashboards guides, operations guide, product brief
openspec/                    Specs and planned changes
```

## License

[MIT](LICENSE)
