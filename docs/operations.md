# Operations

How to install, run and upgrade a self-hosted Tamiza. The quick start in the [README](../README.md) is the short version.

## Requirements

- Docker Engine 24 or later with the Compose v2 plugin (`docker compose`).
- About 4 GB of RAM. The default container limits add up to 3200 MiB (see [Memory](#memory)).
- An existing Keycloak realm configured as described in [keycloak.md](keycloak.md).
- **Outbound HTTPS from the `api` container** to the Kobo servers your projects use (for example `kf.kobotoolbox.org` or `eu.kobotoolbox.org`). Tamiza connects to Kobo directly and does not use an outbound HTTP proxy: a proxy would make the connection on Tamiza's behalf and bypass its check that Kobo addresses are public. Installations that can only reach the Internet through a proxy cannot connect to Kobo yet.
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
| `tamiza_db-data` | The PostgreSQL database. |
| `tamiza_api-keys` | The key ring the API uses to encrypt stored secrets. Without it, encrypted values in the database cannot be read. |

`docker compose down` keeps both volumes. `docker compose down -v` **deletes them irreversibly**.

## Not yet available

These arrive in later changes (see `docs/tamiza-brief.md`, section 8):

- **Backup and restore scripts** (`add-backup-restore`). Until then, protect both volumes above together. A database dump without the key ring cannot decrypt the stored Kobo tokens and webhook secrets; after such a loss, project admins must replace each token and regenerate each webhook secret.
- **Per-service database roles and network isolation for `analytics`** (`harden-deployment`). Today `api` and `analytics` share the database role from `.env`, and `analytics` can reach the Internet.
