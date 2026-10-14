# Operations

How to install, run and upgrade a self-hosted Tamiza. The quick start in the [README](../README.md) is the short version.

## Requirements

- Docker Engine 24 or later with the Compose v2 plugin (`docker compose`).
- About 4 GB of RAM. The default container limits add up to 3200 MiB (see [Memory](#memory)).
- An existing Keycloak realm configured as described in [keycloak.md](keycloak.md).
- **Outbound HTTPS from the `api` container** to the Kobo servers your projects use (for example `kf.kobotoolbox.org` or `eu.kobotoolbox.org`). Besides the checks users start, `api` synchronizes every project on a schedule (by default every 12 hours, plus a daily deletion check), so this traffic happens without anyone signed in. Tamiza connects to Kobo directly and does not use an outbound HTTP proxy: a proxy would make the connection on Tamiza's behalf and bypass its check that Kobo addresses are public. Installations that can only reach the Internet through a proxy cannot connect to Kobo yet.
- **Kobo must reach Tamiza** for the webhook: the `TAMIZA_PUBLIC_URL` must be reachable from the Kobo server (for kf or eu, from the Internet). If it is not, submissions still arrive, but only through the scheduled synchronization.
- **HTTPS in front of Tamiza** for any address other than `localhost`. Browsers expose the WebCrypto API that sign-in (PKCE) needs only in a secure context, so plain HTTP on a server address breaks sign-in. Tamiza does not terminate TLS itself.

## Install

```sh
git clone https://github.com/rinconsebastian/tamizaai.git
cd tamizaai
cp .env.example .env
# Edit .env: fill in every REQUIRED value. Generate the database password with: openssl rand -hex 24
docker compose up -d --build --wait
```

`--wait` returns once all four services report healthy. On first start, `api` creates the database schema before it reports healthy. Check the state at any time with:

```sh
docker compose ps
```

### HTTPS with a reverse proxy

Bind the `ui` port to the loopback interface and let a reverse proxy on the host terminate TLS. In `.env`:

```dotenv
TAMIZA_PUBLIC_URL=https://tamiza.example.org
TAMIZA_HTTP_PORT=127.0.0.1:8088
```

With [Caddy](https://caddyserver.com/), which obtains certificates automatically, the whole site block is:

```
tamiza.example.org {
    reverse_proxy 127.0.0.1:8088
}
```

Any proxy works if it forwards to the `ui` port and passes `X-Forwarded-Proto`. Only the `ui` service publishes a port. The API is served by the same origin under `/api/`, and `analytics` and `db` are reachable only on the internal Compose network.

## Upgrade

```sh
git pull
docker compose up -d --build --wait
```

`api` applies pending database migrations on startup, before it reports healthy. If a migration fails, `api` stays unhealthy, the services that depend on it do not start, and the error is in its log:

```sh
docker compose logs api
```

Each migration is a SQL script that runs in its own transaction, so a failed script leaves no partial change and runs again on the next start. Applied scripts are recorded in the `tamiza.schema_versions` table:

```sh
docker compose exec db psql -U tamiza -d tamiza -c "SELECT scriptname, applied FROM tamiza.schema_versions ORDER BY scriptname"
```

Replace `tamiza` after `-U` with your `POSTGRES_USER` if you changed it.

**Databases from early development builds.** Development builds that predate the SQL scripts tracked the schema in a `tamiza.__ef_migrations_history` table. On its first start, a newer `api` takes such a database over once: it keeps all data, records the initial script as applied and drops the old table. A database from an even older build is refused, and the `api` log says it must be recreated. Recreating deletes all data, including the encryption key ring: run `docker compose down -v`, then `docker compose up -d --build --wait`.

## Health and logs

| Service | Healthy when |
|---|---|
| `db` | `pg_isready` succeeds. |
| `api` | Migrations have finished and the database answers (`/health/ready`). |
| `analytics` | The worker has completed a cycle, including a database round trip, within the last three poll intervals. |
| `ui` | nginx answers on `/healthz`. |

`api` and `analytics` log one JSON object per line. Follow a service with `docker compose logs -f <service>`.

## Memory

Each service has a memory limit set by an environment variable. The defaults fit a 4 GB VPS and leave room for the operating system:

| Variable | Default |
|---|---|
| `DB_MEMORY_LIMIT` | `1024m` |
| `API_MEMORY_LIMIT` | `512m` |
| `ANALYTICS_MEMORY_LIMIT` | `1536m` |
| `UI_MEMORY_LIMIT` | `128m` |

Change a value in `.env` and run `docker compose up -d` to recreate the affected container. `docker stats` shows current usage against each limit.

## Common changes

**Superadmins.** Edit `TAMIZA_SUPERADMIN_EMAILS` in `.env`, then recreate `api` with `docker compose up -d api`. The list is read at startup.

**Kobo on a private network.** By default Tamiza only connects to Kobo servers that use HTTPS and resolve to public addresses. For a self-hosted Kobo on the same network, set `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true` in `.env` and run `docker compose up -d api`. This also allows plain HTTP, and lets anyone who can create projects point the API at internal addresses, so enable it only when those users are trusted. See [kobo.md](kobo.md).

**Synchronization tuning.** The defaults suit forms of up to 500,000 submissions on the reference VPS: a back-fill of that size takes about a minute and keeps `api` well under its memory limit. They can be changed with environment variables on the `api` service, for example in a `compose.override.yaml`:

| Variable | Default | Meaning |
|---|---|---|
| `Ingestion__PageSize` | `500` | Submissions per request to Kobo. Lower it if synchronizations of a form with very large submissions fail with a message about the page size. |
| `Ingestion__MaxPageBytes` | `52428800` | Largest page accepted from Kobo (50 MB). |
| `Ingestion__PageTimeout` | `00:02:00` | Time allowed for one page. |
| `Ingestion__MaxConcurrentRuns` | `2` | Synchronizations running at the same time across all projects. |
| `Ingestion__Tick` | `00:00:30` | How often `api` looks for due synchronizations. |

Projects' synchronization intervals are set per project by their admins, in the project's **Data** section.

**Script limits.** Each analyst script runs with a time limit and a memory limit, set by `TAMIZA_RUN_TIMEOUT_SECONDS` (default `600`) and `TAMIZA_RUN_MEMORY_MB` (default `1024`) in `.env`. Apply changes with `docker compose up -d analytics`. The script runs next to the analytics worker, which needs a few hundred MB of its own while it flattens and publishes, so raise `ANALYTICS_MEMORY_LIMIT` together with `TAMIZA_RUN_MEMORY_MB` and keep at least 512 MB between them.

**Publishing while tables are read.** A run replaces a project's published tables in one short swap that needs the tables to be free. If a dashboard export is still reading them, the swap tries again every few seconds, holding other readers for no more than 5 seconds at a time, and the step's log says how long it waited. After `TAMIZA_PUBLISH_WAIT_SECONDS` (default `900`, 15 minutes) the step fails with "The published tables were in use" and the previous tables stay published. Keep it above the export time limit (see [clean-data-dashboard.md](clean-data-dashboard.md)). Apply changes with `docker compose up -d analytics`.

**Clean data dashboard and exports.** Reading the published tables and exporting them to CSV or XLSX is limited by environment variables on the `api` service, for example in a `compose.override.yaml` (see [clean-data-dashboard.md](clean-data-dashboard.md)). The defaults suit tables of up to 500,000 rows:

| Variable | Default | Meaning |
|---|---|---|
| `DataDashboard__StatementTimeout` | `00:00:25` | Time a page or a list of values may take in PostgreSQL before it is stopped. |
| `DataDashboard__QueueTimeout` | `00:00:25` | Time a request waits for a free slot (pages and values, or exports) before it fails. |
| `DataDashboard__MaxConcurrentQueries` | `4` | Pages and value lists computed at the same time, across all projects. |
| `DataDashboard__LockTimeout` | `00:00:05` | Time a request waits for a published table that a run is replacing. |
| `DataDashboard__ExportStatementTimeout` | `00:05:00` | Time preparing an export may take. Keep `TAMIZA_PUBLISH_WAIT_SECONDS` above it. nginx waits up to 6 minutes for the request. |
| `DataDashboard__MaxConcurrentExports` | `2` | Exports prepared at the same time, across all projects. |
| `DataDashboard__MaxExportFileSize` | `1073741824` | Largest export file in bytes (1 GiB). An export that grows past it fails and leaves no file. |
| `DataDashboard__ExportLinkLifetime` | `00:05:00` | How long a prepared file can be downloaded. |
| `DataDashboard__ExportDirectory` | `/tmp/tamiza-exports` | Where prepared files wait for their download, inside the `api` container. |

Exports are written to `ExportDirectory` before the browser downloads them, so they use the `api` container's disk (its writable layer), not memory. A file is deleted as soon as it is downloaded or its link expires, and the directory is emptied whenever `api` starts. In the worst case the disk holds `MaxExportFileSize` for each export prepared in the last `ExportLinkLifetime` that nobody has downloaded yet; lower `MaxExportFileSize` on a small disk. Download links live in the memory of the `api` process, so a restart invalidates links that were not used yet, which is another reason to run a single `api` replica.

**Per-project tables.** The pipeline publishes each project's results in PostgreSQL schemas named `p_<project id without dashes>_raw`, `_clean`, `_output` and `_indicators` (see [scripts.md](scripts.md)). They are rebuilt by every run. To remove a project's tables by hand, drop its four schemas, for example `DROP SCHEMA p_01a0ffdc120470188195fe685d20c5c9_raw CASCADE;`; the next run creates them again.

**One `api` replica.** Run a single `api` container. On startup it marks any synchronization left running as interrupted, which assumes no other `api` is running one.

**Database password.** `POSTGRES_PASSWORD` is applied only when the database volume is first created. To change it later, update the role first, then `.env`:

```sh
docker compose exec db psql -U tamiza -d tamiza -c "ALTER ROLE tamiza PASSWORD 'new-password'"
# Set POSTGRES_PASSWORD=new-password in .env, then recreate the services that connect with it:
docker compose up -d --wait
```

Replace `tamiza` with your `POSTGRES_USER` if you changed it.

## Data

Two named volumes hold everything that must survive container re-creation:

| Volume | Contents |
|---|---|
| `tamiza_db-data` | The PostgreSQL database, including the raw submissions received from Kobo. |
| `tamiza_api-keys` | The key ring the API uses to encrypt stored secrets. Without it, encrypted values in the database cannot be read. |

`docker compose down` keeps both volumes. `docker compose down -v` **deletes them irreversibly**.

## Not yet available

These arrive in later changes (see `docs/tamiza-brief.md`, section 8):

- **Backup and restore scripts** (`add-backup-restore`). Until then, protect both volumes above together. A database dump without the key ring cannot decrypt the stored Kobo tokens and webhook secrets; after such a loss, project admins must replace each token and regenerate each webhook secret.
- **Per-service database roles and network isolation for `analytics`** (`harden-deployment`). Today `api` and `analytics` share the database role from `.env`, and `analytics` can reach the Internet. Analysts' scripts run inside `analytics` without the database credentials in their environment, but they are not sandboxed, so give the analyst and admin roles only to people you trust with the server.
