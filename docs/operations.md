# Operations

How to install, run and upgrade a self-hosted Tamiza. The quick start in the [README](../README.md) is the short version.

## Requirements

- Docker Engine 24 or later with the Compose v2 plugin (`docker compose`).
- About 4 GB of RAM. The default container limits add up to 3200 MiB (see [Memory](#memory)).
- An existing Keycloak realm configured as described in [keycloak.md](keycloak.md).
- **Outbound HTTPS from the `api` container** to the Kobo servers your projects use (for example `kf.kobotoolbox.org` or `eu.kobotoolbox.org`). Besides the checks users start, `api` synchronizes every project on a schedule (by default every 12 hours, plus a daily deletion check), so this traffic happens without anyone signed in. Tamiza connects to Kobo directly and does not use an outbound HTTP proxy: a proxy would make the connection on Tamiza's behalf and bypass its check that Kobo addresses are public. Installations that can only reach the Internet through a proxy cannot connect to Kobo yet.
- **Outbound HTTPS from the `api` container to the model provider**, only if you turn on the [AI assistant](#ai-assistant). Each assistant request goes from `api` straight to the base URL you configure (a provider on the Internet, or a model server on your own network over plain HTTP), also without an outbound HTTP proxy. Without the three assistant variables nothing is sent anywhere.
- **Outbound HTTPS from the `api` container to `tile.googleapis.com`**, only if you set `TAMIZA_MAP_BASEMAP_PROVIDER=google` (see [Map basemaps](#map-basemaps-optional)). Without a proxy, as for the other providers. With `osm`, viewers' browsers (not `api`) contact `tile.openstreetmap.org`; with no provider nothing is sent anywhere.
- **Outbound SMTP from the `api` container to your mail server**, only if you turn on [email](#email-optional). `api` connects to `TAMIZA_SMTP_HOST` on its port (587, 465 or 25 by default). Many cloud hosts block outbound port 25, and some block the others until you ask them. Without the SMTP variables no connection is opened.
- **Kobo must reach Tamiza** for the webhook: the `TAMIZA_PUBLIC_URL` must be reachable from the Kobo server (for kf or eu, from the Internet). If it is not, submissions still arrive, but only through the scheduled synchronization.
- **HTTPS in front of Tamiza** for any address other than `localhost`. Browsers expose the WebCrypto API that sign-in (PKCE) needs only in a secure context, so plain HTTP on a server address breaks sign-in. Tamiza does not terminate TLS itself.

## Install

```sh
git clone https://github.com/rinconsebastian/tamizaai.git
cd tamizaai
cp .env.example .env
# Edit .env: fill in every REQUIRED value, and set TAMIZA_SUPERADMIN_EMAILS to your verified email.
# Generate the database password with: openssl rand -hex 24
docker compose up -d --build --wait
```

Set `TAMIZA_SUPERADMIN_EMAILS` to your own email, as it is verified in Keycloak, before you first sign in. Only admins of a project and superadmins can create projects, so on a new installation a superadmin creates the first one; with the variable empty, nobody can (see [Common changes](#common-changes)).

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

**Proxy in Docker on a shared network.** When the reverse proxy itself runs in a container (Nginx Proxy Manager, Traefik...), it cannot reach `127.0.0.1` of the host. Use the override [compose.proxy.yaml](../compose.proxy.yaml): it also connects `ui` to the proxy's existing Docker network, so the proxy forwards to `http://tamiza-ui:8080` and no host port is involved. Set `PROXY_NETWORK` in `.env` to that network's name (default `bridgemysql`) and keep `TAMIZA_HTTP_PORT=127.0.0.1:8088` so nothing is exposed on the server's addresses:

```sh
docker compose -f compose.yaml -f compose.proxy.yaml up -d --build --wait
```

In Portainer, deploy from the repository with Compose path `compose.yaml` and, under *Additional paths*, `compose.proxy.yaml`. Only `ui` joins the proxy network; `api`, `analytics` and `db` stay on the stack's own network. Local development keeps using `compose.yaml` alone.

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

### Who can open Settings and create projects

This upgrade closes Settings to viewers and project creation to project admins. It changes no data and needs no migration. Install the new images as above. To go back, deploy the previous images; there is nothing to undo.

- **Project creation.** A user who is not admin of any project (an accepted membership, not a pending invitation) and is not a superadmin can no longer create projects: the API answers 403 with the code `project.create_not_allowed`. Admins and superadmins keep the right, so an existing installation needs no change. A new installation needs `TAMIZA_SUPERADMIN_EMAILS` for its first project (see [Install](#install)).
- **Settings data.** A viewer no longer opens Settings, and the API answers 403 to a viewer on the reads that only Settings used: members, sampling frame and its download, scripts and their versions, script inputs, runs, pipeline settings, reference tables, ingestion status, synchronization log, form versions, form fields and form choices. A script or integration that reads them with a viewer account needs an analyst account. A viewer's project details also leave out the Kobo server, the asset UID, the form check, the enumerator question, the unavailable form versions count and whether the token can be read (they are `null`). Dashboards, Clean data, exports and the dashboard endpoints stay open to viewers.

### Rolling back the multiple dashboards upgrade

The script `0012_add_multiple_dashboards.sql` lets a project have several indicators dashboards (see [Dashboards](indicators-dashboard.md#dashboards)). It runs at `api` startup in one transaction: every project gets one dashboard, **Indicators**, and all its widgets move to it with their layout and query unchanged. No configuration needs to change; `IndicatorDashboard__MaxWidgetsPerProject` keeps working (see [Common changes](#common-changes)).

An `api` older than that script knows no dashboards. Run against the upgraded database, it cannot add widgets, because every widget now needs a dashboard, and it shows the widgets of all a project's dashboards on one canvas, overlapping. To go back, restore the copy of both volumes (see [Data](#data)) taken before the upgrade, together with the previous images. Without such a copy, stop `api`, undo the script by hand, then start the previous images (`docker compose up -d` with the earlier `TAMIZA_VERSION`):

```sh
docker compose stop api
docker compose exec db psql -U tamiza -d tamiza -c "
BEGIN;
ALTER TABLE tamiza.dashboard_widgets DROP COLUMN dashboard_id;
DROP TABLE tamiza.dashboards;
DELETE FROM tamiza.schema_versions WHERE scriptname = '0012_add_multiple_dashboards.sql';
COMMIT;"
```

Replace `tamiza` after `-U` with your `POSTGRES_USER` if you changed it. What this costs:

- The widgets of every dashboard of a project share one canvas again and may overlap, and the widgets of a duplicated dashboard appear twice.
- The older `api` counts them all against its limit per project, so a project above 50 widgets (or above `IndicatorDashboard__MaxWidgetsPerProject`) refuses new ones until enough are removed.
- The dashboards' names and order are lost.
- The older `api` reads only `IndicatorDashboard__MaxWidgetsPerProject`: if you renamed it to `IndicatorDashboard__MaxWidgetsPerDashboard`, rename it back.

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

**Superadmins.** Edit `TAMIZA_SUPERADMIN_EMAILS` in `.env`, then recreate `api` with `docker compose up -d api`. The list is read at startup. A superadmin acts as admin on every project and can create projects even when none exists, which is how a new installation gets its first project. Other users can create projects only while they are admin of at least one project (a pending invitation does not count); they get a 403 with the code `project.create_not_allowed` otherwise.

**Kobo on a private network.** By default Tamiza only connects to Kobo servers that use HTTPS and resolve to public addresses. For a self-hosted Kobo on the same network, set `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true` in `.env` and run `docker compose up -d api`. This also allows plain HTTP, and lets every admin of a project, and every superadmin, point the API at internal addresses, so enable it only when those users are trusted. See [kobo.md](kobo.md).

**Synchronization tuning.** The defaults suit forms of up to 500,000 submissions on the reference VPS: a back-fill of that size takes about a minute and keeps `api` well under its memory limit. They can be changed with environment variables on the `api` service, for example in a `compose.override.yaml`:

| Variable | Default | Meaning |
|---|---|---|
| `Ingestion__PageSize` | `500` | Submissions per request to Kobo. Lower it if synchronizations of a form with very large submissions fail with a message about the page size. |
| `Ingestion__MaxPageBytes` | `52428800` | Largest page accepted from Kobo (50 MB). |
| `Ingestion__PageTimeout` | `00:02:00` | Time allowed for one page. |
| `Ingestion__MaxConcurrentRuns` | `2` | Synchronizations running at the same time across all projects. |
| `Ingestion__Tick` | `00:00:30` | How often `api` looks for due synchronizations. |

Projects' synchronization intervals are set per project by their admins, in the project's **Settings > Submissions** page.

**Script limits.** Each analyst script runs with a time limit and a memory limit, set by `TAMIZA_RUN_TIMEOUT_SECONDS` (default `600`) and `TAMIZA_RUN_MEMORY_MB` (default `1024`) in `.env`. Apply changes with `docker compose up -d analytics`. The script runs next to the analytics worker, which needs a few hundred MB of its own while it flattens and publishes, so raise `ANALYTICS_MEMORY_LIMIT` together with `TAMIZA_RUN_MEMORY_MB` and keep at least 512 MB between them.

**Reuse in script tests.** A sample test keeps the flattened sample and the result of the saved script 1 on the analytics container's disk, so the next test with the same inputs skips those steps and the step shows "Reused from an earlier test". The key covers the submissions in the sample (and when each was last updated), the form and its labels, the question labels, the version of script 1 and the reference tables, so reuse never changes a result. `TAMIZA_TEST_CACHE_MB` (default `512`) limits the disk it uses: the entries used least recently are removed first, an entry larger than the limit is not kept, and entries unused for 24 hours are removed. `0` turns reuse off. The entries live in the container's work directory and go away when the container is recreated. Apply changes with `docker compose up -d analytics`.

**Sample downloads.** "Download sample" in **Settings > Scripts** gives an analyst a zip with a sample of the project's data to work on their own computer (see [scripts.md](scripts.md#writing-scripts-on-your-computer)). The worker prepares it like a test, after queued tests and before queued runs, and keeps it **in the database** for one hour after it is ready, so the API can hand it to its requester through a single-use link. The database therefore holds at most one download per analyst and project (plus one being prepared), each up to `TAMIZA_SAMPLE_DOWNLOAD_MB` (default `100`): ten analysts each holding a full-size download use about 1 GB for up to an hour, and the same amount goes through the write-ahead log. Lower the setting on a small server; a sample whose zip would be larger fails with a message that gives its size and suggests a smaller sample, and nothing is kept. Reference tables are the usual cause of large downloads. The zip contains submissions of the project, which analysts can already read through scripts; it does not contain credentials, tokens or server addresses. Apply changes with `docker compose up -d analytics`.

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

**Indicators dashboards.** A project's dashboards of widgets (see [indicators-dashboard.md](indicators-dashboard.md)) read the indicator tables with the `DataDashboard__` time limits and query slots above, and export them with the same export settings. Two more variables on the `api` service limit what a project can ask of them:

| Variable | Default | Meaning |
|---|---|---|
| `IndicatorDashboard__MaxWidgetsPerDashboard` | `50` | Most widgets one dashboard can hold, from 1 to 500. A project has up to 7 dashboards, so it can hold up to 7 times as many widgets. Widgets already saved are kept if you lower it: a dashboard at or above the limit cannot get more widgets, added or moved to it, until it is under the limit, and one above the limit cannot be duplicated until it holds no more widgets than the limit. |
| `IndicatorDashboard__MaxWidgetQueryRows` | `5000` | Most rows one widget plots, from 1 to 100,000. A widget with more says that only the first rows are shown. |

`IndicatorDashboard__MaxWidgetsPerProject`, the name of the widget limit before a project could have several dashboards, still works: when `IndicatorDashboard__MaxWidgetsPerDashboard` is not set, `api` uses its value as the limit of each dashboard, checks it the same way (a value that is not a number from 1 to 500 stops `api` at startup), and logs one warning at startup naming both variables. When both are set, `IndicatorDashboard__MaxWidgetsPerDashboard` wins and the old one is ignored. Rename it when convenient. The number of dashboards a project can have, 7, is fixed.

**Per-project tables.** The pipeline publishes each project's results in PostgreSQL schemas named `p_<project id without dashes>_raw`, `_clean`, `_output` and `_indicators` (see [scripts.md](scripts.md)). They are rebuilt by every run (the `_indicators` schema holds one table per indicator, which a run replaces one at a time). To remove a project's tables by hand, drop its four schemas, for example `DROP SCHEMA p_01a0ffdc120470188195fe685d20c5c9_raw CASCADE;`; the next run creates them again.

**One `api` replica.** Run a single `api` container. On startup it marks any synchronization left running as interrupted, which assumes no other `api` is running one.

**Database password.** `POSTGRES_PASSWORD` is applied only when the database volume is first created. To change it later, update the role first, then `.env`:

```sh
docker compose exec db psql -U tamiza -d tamiza -c "ALTER ROLE tamiza PASSWORD 'new-password'"
# Set POSTGRES_PASSWORD=new-password in .env, then recreate the services that connect with it:
docker compose up -d --wait
```

Replace `tamiza` with your `POSTGRES_USER` if you changed it.

## AI assistant

The Scripts section can have an assistant that drafts a script, rewrites a cell and fixes a failed test (see [Writing with the assistant](scripts.md#writing-with-the-assistant)). It is **off by default** and exists only when you give `api` a model provider. Tamiza ships with no provider, no key and no account: you choose one, and the choice is yours to make (see [Choosing a provider](#choosing-a-provider)).

Tamiza talks to any server that speaks the OpenAI chat-completions format (`POST {base URL}/chat/completions`), so changing provider means changing the variables below and restarting `api`. Nothing in Tamiza depends on one vendor's features.

### Turn it on

Set these in `.env` and run `docker compose up -d api ui` (the first time, so that `ui` gets the longer timeout for the assistant's requests; later changes need only `api`). Only `api` reads them, and `analytics` never reaches the provider.

| Variable | Default | Meaning |
|---|---|---|
| `TAMIZA_ASSISTANT_BASE_URL` | empty | The provider's OpenAI-compatible base URL, such as `https://api.example.org/v1`. Absolute `http` or `https`, with no credentials, query or fragment. A server on your network or on the Docker host works too (`http://host.docker.internal:11434/v1`). |
| `TAMIZA_ASSISTANT_MODEL` | empty | The model, as the provider names it. |
| `TAMIZA_ASSISTANT_API_KEY` | empty | Sent as `Authorization: Bearer`. A server without keys still needs some value here. |
| `TAMIZA_ASSISTANT_EXTRA_BODY` | empty | A JSON object merged into every request, for a field a provider needs or to lower its reasoning effort. It may not set `model`, `messages` or `stream`. |
| `TAMIZA_ASSISTANT_TIMEOUT_SECONDS` | `120` | How long `api` waits for the provider's answer, 10 to 240. |
| `TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER` | `500000` | Tokens each user may use per UTC day, across projects, 1,000 to 100,000,000. A request that arrives at or over the budget is refused until the next UTC midnight. |
| `TAMIZA_ASSISTANT_MAX_CONTEXT_KB` | `128` | The most context sent in one request, in KB, 64 to 1,024. What does not fit is left out (see [what is sent](#what-is-sent)); the script being edited is never shortened. |

The assistant is on **only when the first three are all set**. With one or two of them, it stays off and `api` logs once, at startup, which variables are missing. A malformed value (a base URL that is not an `http` or `https` address, extra fields that are not a JSON object or that set a reserved field, a number out of range) stops `api` at startup and names the variable. To turn the assistant off, blank any of the three and restart `api`: the tab disappears, its routes answer that the assistant is not available, and the records stay.

Tamiza sends only these fields: `model`, `messages` and what `TAMIZA_ASSISTANT_EXTRA_BODY` holds. It never sets `temperature`, `max_tokens`, `tools`, `response_format` or `stream`, because providers disagree about them; put one in the extra fields if yours needs it. Requests do not follow redirects, so the key never goes to another address, and there is no check on the address' network: a base URL on a private network is allowed on purpose, unlike Kobo's.

**Never put the key in a file that is committed.** `.gitignore` excludes `.env*` except `.env.example`, so new files such as `.envstaging` are safe, but a file that git already tracks stays tracked: take it out first with `git rm --cached <file>` before adding a key to it, and rotate any key that was committed.

### Example settings

Examples for several providers, each checked against the provider's own pages on **2026-10-16**. Model names, endpoints and terms change: check the pages linked under each example before you copy one, and run [Checking a provider](#checking-a-provider) after you switch. No prices are given here; read the provider's pricing page.

**Gemini** ([OpenAI compatibility](https://ai.google.dev/gemini-api/docs/openai), [terms](https://ai.google.dev/gemini-api/terms)):

```sh
TAMIZA_ASSISTANT_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai/
TAMIZA_ASSISTANT_MODEL=gemini-3.8-flash
TAMIZA_ASSISTANT_API_KEY=your-key
TAMIZA_ASSISTANT_EXTRA_BODY='{"reasoning_effort":"low"}'
```

- Use the key of a **billed project**. The terms (last updated 2026-04-28) say that for the unpaid tier Google uses the prompts and answers to improve its products, that human reviewers may read them, and "Do not submit sensitive, confidential, or personal information to the Unpaid Services". For the paid tier they say Google does not use prompts to improve its products, and logs prompts and answers "for a limited period of time, solely for detecting and preventing violations" without stating how long. Treat the free tier as not allowed for a project with survey data, even though no rows are sent (see [what is sent](#what-is-sent)).
- Gemini models think before they answer, which costs tokens and time. The compatibility page (read on 2026-10-16) maps `reasoning_effort` to a thinking budget, and says that `reasoning_effort` cannot be combined with `thinking_level` or `thinking_budget`, that Gemini 2.5 models accept `"none"` and that Gemini 2.5 Pro and Gemini 3 models cannot turn thinking off. Its curl example for Gemini's own settings sends them in a literal `extra_body` key, `{"extra_body":{"google":{"thinking_config":{"thinking_level":"low"}}}}`. Google says support for the OpenAI libraries is still in beta.

**DeepSeek** ([API docs](https://api-docs.deepseek.com/), [thinking mode](https://api-docs.deepseek.com/guides/thinking_mode), [privacy policy](https://cdn.deepseek.com/policies/en-US/deepseek-privacy-policy.html)):

```sh
TAMIZA_ASSISTANT_BASE_URL=https://api.deepseek.com
TAMIZA_ASSISTANT_MODEL=deepseek-flash
TAMIZA_ASSISTANT_API_KEY=your-key
TAMIZA_ASSISTANT_EXTRA_BODY='{"thinking":{"type":"disabled"}}'
```

- The thinking mode page (read on 2026-10-16) says thinking is enabled by default with a high effort, that `thinking` is a field of the request body (`{"thinking": {"type": "enabled/disabled"}}`) and that `temperature` and the penalties have no effect in thinking mode. Use `'{"reasoning_effort":"low"}'` instead to keep thinking on with less effort.
- The privacy policy (last update 2026-02-10) says DeepSeek stores personal data in the People's Republic of China, lists training and improving its models among the purposes of using personal data, gives an opt-out of that use by emailing `privacy@deepseek.com`, and states no fixed retention period. Whether that is acceptable for your organization is for you to decide.

**OpenAI** ([Your data](https://developers.openai.com/api/docs/guides/your-data), [error codes](https://developers.openai.com/api/docs/guides/error-codes), [models](https://developers.openai.com/api/docs/models)):

```sh
TAMIZA_ASSISTANT_BASE_URL=https://api.openai.com/v1
TAMIZA_ASSISTANT_MODEL=gpt-6-luna
TAMIZA_ASSISTANT_API_KEY=your-key
TAMIZA_ASSISTANT_EXTRA_BODY='{"reasoning_effort":"low"}'
```

- On 2026-10-16 OpenAI documents Chat Completions as still supported, while recommending the Responses API for new projects ([migration guide](https://developers.openai.com/api/docs/guides/migrate-to-responses)). Its models page does not say which models take Chat Completions: confirm yours on its model page, and run the check below.
- `reasoning_effort` accepts values that depend on the model; the reasoning guide says that `none` returns HTTP 400 for at least one current model. Tamiza sends no output limit; add one to the extra fields only if you need it (the reasoning guide says reasoning tokens count against it).
- Your data page says data sent to the API is not used to train OpenAI models unless you opt in, and that abuse-monitoring logs are kept for up to 30 days unless law requires longer. Zero data retention is an approval-based control.
- An account with no credit left gets HTTP 429 with the code `credit_balance_exhausted`, not a 402 (see [exhausted credit](#when-the-provider-fails)).

**OpenRouter**, a gateway to many models ([quickstart](https://openrouter.ai/docs/quickstart), [zero data retention](https://openrouter.ai/docs/guides/features/zdr), [privacy](https://openrouter.ai/docs/guides/privacy/data-collection)):

```sh
TAMIZA_ASSISTANT_BASE_URL=https://openrouter.ai/api/v1
TAMIZA_ASSISTANT_MODEL=author/model-name
TAMIZA_ASSISTANT_API_KEY=your-key
TAMIZA_ASSISTANT_EXTRA_BODY='{"provider":{"zdr":true}}'
```

- Model names have the form `author/model`; the model catalog lists them. With `provider.zdr` set to `true`, the zero-data-retention page (read on 2026-10-16) says a request is routed only to endpoints with a zero-data-retention policy. It does not say what happens when a model has none, so check its [list of those endpoints](https://openrouter.ai/api/v1/endpoints/zdr) first. `"data_collection":"deny"` under `provider` limits routing to providers that do not collect user data ([provider selection](https://openrouter.ai/docs/guides/routing/provider-selection)).
- OpenRouter says it does not store prompts or responses unless you opt in, and that it stores request metadata such as token counts and latency. The model's own provider has its own terms, which `zdr` and `data_collection` are meant to narrow.

**A server of your own** (Ollama, vLLM or any other OpenAI-compatible server) on the Docker host, or on another machine of your network:

```sh
TAMIZA_ASSISTANT_BASE_URL=http://host.docker.internal:11434/v1/
TAMIZA_ASSISTANT_MODEL=the-model-you-pulled
TAMIZA_ASSISTANT_API_KEY=any-value
```

The `api` service reaches the Docker host as `host.docker.internal`. Nothing leaves your network, which is the simplest answer to the data-protection question, but a small local model may write worse scripts: run the check below before you rely on it. Three settings decide whether it works at all:

- **The context window must hold the request and the answer.** Tamiza sends up to `TAMIZA_ASSISTANT_MAX_CONTEXT_KB` of text (128 KB by default, about 33,000 tokens at four bytes a token, more for text with accents), plus the answer. When the window is smaller, the server cuts the beginning of the request, which is the rules and the schema, **without any error**, and the model answers with a generic script. For Ollama, the [FAQ](https://docs.ollama.com/faq) says the default window is 4,096 tokens and the [context length page](https://docs.ollama.com/context-length) says it depends on the memory of the GPU (4k below 24 GiB); the [OpenAI compatibility page](https://docs.ollama.com/api/openai-compatibility) says that this endpoint has no way of setting the context size, so start the server with `OLLAMA_CONTEXT_LENGTH` (for example `OLLAMA_CONTEXT_LENGTH=40000 ollama serve`) or create the model with `PARAMETER num_ctx` in a Modelfile, and read the `CONTEXT` column of `ollama ps`. For vLLM, start it with `--max-model-len`. If the window cannot be that large, lower `TAMIZA_ASSISTANT_MAX_CONTEXT_KB` (down to 64) so that what is sent fits.
- **A reasoning model needs the server to separate its reasoning from its answer.** Otherwise the reasoning is in the answer and is shown, and offered as code, to the analyst. With vLLM, start the server with `--reasoning-parser` and the parser for your model ([reasoning outputs](https://docs.vllm.ai/en/stable/features/reasoning_outputs/)). Tamiza ignores the reasoning fields (`reasoning`, `reasoning_content`) and reads only the answer.
- **On Linux with Docker Engine, the server must listen beyond the loopback address.** `host.docker.internal` resolves to the Docker host's gateway address, so a server that listens only on `127.0.0.1` refuses `api`. Ollama binds `127.0.0.1:11434` by default; set `OLLAMA_HOST=0.0.0.0:11434` ([FAQ](https://docs.ollama.com/faq)) and keep the port closed to the rest of the network with a firewall. Docker Desktop on Windows and macOS normally reaches services on the host's loopback address through `host.docker.internal`, so this is not needed there.

A server without keys (Ollama ignores the key it receives) still needs some value in `TAMIZA_ASSISTANT_API_KEY`, because the assistant stays off without one.

### Choosing a provider

Choosing a provider is a **data-protection decision of your organization**, not a technical one. Before you turn the assistant on, read the provider's current terms and privacy pages (linked above), in particular what it does with prompts (training, human review, how long it logs them for abuse monitoring) and where the data is processed. Providers may also cache the start of each request to answer faster, which is part of their terms. In Colombia, for example, the provider receives or processes data you send, which Ley 1581 de 2012 and the authorization you hold from the people concerned may regulate, and donors and partners often set rules of their own for survey data. This is not legal advice: ask whoever answers for data protection in your organization.

What Tamiza does to keep the question small is in [what is sent](#what-is-sent): no rows. But it does send the **questions, the choice lists and the scripts**, and those can themselves be sensitive.

### What is sent

Each request holds the rules of the script contract and the libraries available to scripts, the templates of the script's kind, the tables the script receives (names, columns, types, row counts and question labels), the reference tables' columns and types, the choice lists of the form, the project's time zone and enumerator question, the saved script 1, the indicator's details, the text in the editor and the earlier turns of the analyst's conversation. A fix request also holds the **shape** of the analyst's own last test: the steps, tables and columns, and its error and log lines, reduced to the script's frames and the lines with a log level, with every word or number that is not a name of the schema, a word of the script or a word of library messages replaced by `…`.

It **never** holds rows of a table, of a preview, of a `ctx.show` value or of a reference table, printed output, nor a value, total or group of an indicator's dashboard view. Two things still reach the provider that the people who work with the project should know: the script is sent as written, with any constant typed in it (a code, a name, a threshold), and the **choice labels** are sent, so a choice list that holds people's names, such as a list of enumerators, is sent with them. Analysts can read exactly what will be sent with *Show what will be sent* in the Assistant tab, and the list is in [What the assistant can see](scripts.md#what-the-assistant-can-see).

When the request is larger than `TAMIZA_ASSISTANT_MAX_CONTEXT_KB`, `api` leaves things out in a fixed order (earlier turns, choice lists and tables the script does not mention, long labels, the end of script 1, extra templates); the view says what was left out. A script larger than 64 KB, or one that does not fit even then, is refused.

### When the provider fails

The tab says what happened in its own words and puts the analyst's message back in the request box; it never shows the provider's error. What `api` logs depends on the provider's answer:

| Provider answers | The analyst sees | `api` log |
|---|---|---|
| 401, 402, 403, 404, 400 or 422 | The assistant is misconfigured: contact the administrator. | Error with the status and the provider's host, at most once a minute. |
| 429 | The provider is limiting requests, with the wait when the provider gave one. | Warning with the status, at most once a minute. |
| 5xx, other statuses, a redirect, a reply that is not text, or no connection | The provider is not available. | Warning with the status. |
| No answer within `TAMIZA_ASSISTANT_TIMEOUT_SECONDS` | The provider did not answer in time. | |

**Exhausted credit** shows differently by provider. Most providers answer 402, which is shown as *misconfigured*. OpenAI answers 429 with the code `credit_balance_exhausted` (its [error codes](https://developers.openai.com/api/docs/guides/error-codes) page, read on 2026-10-16), which is shown as the provider limiting requests, so look at the status in the `api` log (`docker compose logs api`) when waiting does not help. The log never holds the key nor a body.

### Usage records

Every request leaves one row in `tamiza.assistant_calls`: who asked, the project, the script's kind and name, the action, the model, the sizes of the parts of the context, whether a test was attached, the tokens used, the outcome and, when the analyst applied the proposal, how. The rows hold no text of the conversation, the script or the answer, and they are deleted with their project. Tokens are what the provider reported; when it reports none, or the call was cut off, they are an estimate (the bytes sent and received divided by four) and `tokens_estimated` is true. The daily budget adds up these rows.

```sh
docker compose exec db psql -U tamiza -d tamiza -c "
SELECT date_trunc('day', c.started_at) AS day, u.email, count(*) AS requests,
       sum(GREATEST(COALESCE(c.total_tokens, 0), COALESCE(c.prompt_tokens, 0) + COALESCE(c.completion_tokens, 0))) AS tokens,
       count(*) FILTER (WHERE c.status <> 'succeeded') AS not_succeeded
FROM tamiza.assistant_calls c JOIN tamiza.users u ON u.id = c.user_id
GROUP BY 1, 2 ORDER BY 1 DESC, 4 DESC LIMIT 30"
```

Replace `tamiza` after `-U` with your `POSTGRES_USER` if you changed it.

### Timeouts and proxies

One request can wait for the provider for up to `TAMIZA_ASSISTANT_TIMEOUT_SECONDS` (at most 240 seconds). The `ui` service waits up to 300 seconds for the assistant's routes; the 60 seconds that nginx waits by default would cut most answers. A reverse proxy of your own in front of `ui` needs a read timeout above `TAMIZA_ASSISTANT_TIMEOUT_SECONDS` for `/api/v1/projects/*/assistant/`, or analysts see a gateway error while `api` is still waiting. Each user can have one request in progress, `api` reads at most 4 MiB from the provider, and a request that waits holds one `api` request for that time.

### Checking a provider

Providers differ, and so do two models of one provider. Before you switch provider or model, run the seven requests below and keep the results. They are the same every time, so two providers can be compared.

**Set up once.** Use a project, or a copy of one, whose form has at least these questions, with at least 100 submissions in the sample (the default sample is 500):

- `municipality`: a `select_one` with several municipalities.
- `water_source`: a `select_one` whose choice labels include `Piped water` (earlier form versions may have called it `Tap water`).
- `services`: a `select_multiple` with choices such as `Water`, `Electric power`, `Internet` and `Gas`.
- `income`: a `decimal`.
- `visit_date`: a `date`.
- A repeat group `members` with `member_age` (an `integer`) and `member_sex` (a `select_one`).
- A project time zone other than UTC, for example `America/Bogota`, in **Settings > General**.

Sign in as an analyst, open **Settings > Scripts** and use a scratch indicator or a scratch output script that you do not save (or remove after the check). Start each request with **New conversation**, choose the scope and the kind as stated, type the text **exactly as written**, send, and apply the proposal with *Replace script*. Then choose **Test on sample**.

**Passing, for all seven.** The first proposal passes the sample test, or the test passes after **one** fix (*Ask the assistant* on the failed step, sent unchanged), and the editor shows **no** *is not available to scripts* mark on an import. Each request has one more condition. A proposal that needs two or more fixes, or that imports `sklearn`, `scipy` or `statsmodels`, fails that request. Look at the previews, not only at the status: a script that passes the test but returns the wrong rows does not pass.

| # | Kind and scope | Text to send, exactly | It passes when, besides the above |
|---|---|---|---|
| 1 | Indicator `income_slope`, *Script* | `For each municipality, fit a straight line of household income on household size, where household size is the number of rows of the members table for the household, and return the slope and the intercept.` | The script uses numpy or pandas (for example `np.polyfit`) and not scikit-learn, SciPy or statsmodels; it returns one row per municipality with at least one numeric column; the answer does not say that a library "can be installed". |
| 2 | Output, *Script* | `Make a table called households_services with one row per household: _id, municipality, and one true/false column for each service in the services question.` | The script splits `services` on `"; "` and not on a space or a comma; the preview has one column per service answered in the sample, with lowercase names; a household whose answer is `Water; Electric power` shows true in exactly those two columns; the table has as many rows as `main`. |
| 3 | Indicator `piped_water_rate` with format `percent`, *Script* | `Share of households whose water source is Piped water, by municipality, as a percentage.` | The table has `municipality`, `value`, `numerator` and `denominator`; `value` is a fraction between 0 and 1 and not multiplied by 100 (the dashboard view shows no *values above 1* warning); `numerator` and `denominator` are counts; the comparison uses the label `Piped water`. |
| 4 | Output, *Script* | `Make a table called households with one row per household: _id, municipality, and children_under_15, the number of members younger than 15 in the members table, which is 0 for a household with no members.` | The members are counted by `_parent_id` and joined to `_id`; the table has the same number of rows as `main` (no row repeated by the join); `children_under_15` is a whole number with no empty values. |
| 5 | Indicator `visits_by_month` with format `count`, *Script* | `Number of submissions per month, counting each submission in the month of the day it was received in the project's time zone.` | The script converts `_submission_time` to the project's zone (for example `tz_convert("America/Bogota")`) **before** it takes the month; the month is text such as `2026-09`, not a number; the series has one row per month with submissions. |
| 6 | Indicator `piped_water_rate_old`, *Fix* | See below. | The proposal changes the comparison to the current label `Piped water` and leaves the rest of the script as it was; the test then passes. |
| 7 | Indicator `tasa_acueducto` with format `percent`, *Script* | `tasa de hogares con acueducto por municipio` | The explanation is in Spanish; the identifiers, comments and log messages of the code are in English; the code reads the columns `municipality` and `water_source` by the names the schema gives (not `municipio`); the comparison uses the label `Piped water`, not a translation. |

**Request 6** starts from a script that compares with a label the form no longer has. Paste this into the scratch indicator's editor (do not save), choose **Test on sample**, and when it fails choose *Ask the assistant* on the failed step and send the sentence it fills in, unchanged:

```python
def compute(cleaned, ctx):
    main = cleaned["main"]
    rows = main.groupby("municipality").agg(
        numerator=("water_source", lambda answers: int((answers == "Tap water").sum())),
        denominator=("water_source", "size"),
    ).reset_index()
    if rows["numerator"].sum() == 0:
        raise ValueError("No household answered Tap water")
    rows["value"] = rows["numerator"] / rows["denominator"]
    return rows[["municipality", "value", "numerator", "denominator"]]
```

Before you send it, open *Show what will be sent* and check that the error appears as `ValueError: No household answered Tap water` and that no value of the sample is in the text.

**What to record.** For each request, in a table of your own: whether the first proposal passed the sample test; the marks the editor raised on it (imports and the other checks); how many fixes it needed; and the tokens used, including the fixes. Read the tokens from the [usage records](#usage-records) right after the run:

```sh
docker compose exec db psql -U tamiza -d tamiza -c "
SELECT started_at, kind, name, action, status, error_code,
       GREATEST(COALESCE(total_tokens, 0), COALESCE(prompt_tokens, 0) + COALESCE(completion_tokens, 0)) AS tokens,
       tokens_estimated, outcome
FROM tamiza.assistant_calls ORDER BY started_at DESC LIMIT 20"
```

A request that costs several times the tokens of the others, or a `tokens_estimated` of true on every row, usually means a reasoning model thinking at full effort or a provider that reports no usage: lower the effort in the extra fields. Run the seven again whenever you change the model, its settings or the provider, and after a Tamiza upgrade that changes the assistant's rules.

## MCP server (optional)

An AI client such as Claude (claude.ai, Claude Desktop, Claude Code) can work with a project through the Model Context Protocol: read the Kobo form and summaries of the data, write, test and run scripts, and build dashboards and widgets, signed in as the person with their role (see [docs/mcp.md](mcp.md)). It is **off by default**, and when it is on every project still stays off until an admin of the project allows it in **Settings > General**. Tamiza calls no model and ships no account: the person's own Claude plan pays for it, and the server makes no outbound request.

### Turn it on

Register the client in Keycloak first ([Client for AI clients (MCP)](keycloak.md#client-for-ai-clients-mcp-optional)), then set these in `.env` and run `docker compose up -d api ui` (`ui` carries the proxy locations for `/mcp` and `/.well-known/oauth-protected-resource`). Only `api` reads them.

| Variable | Default | Meaning |
|---|---|---|
| `TAMIZA_MCP_ENABLED` | `false` | Turns the server on, at `<TAMIZA_PUBLIC_URL>/mcp`. |
| `TAMIZA_MCP_AUDIENCE` | `tamiza-mcp` | The `aud` that tokens for `/mcp` must carry. It must differ from `OIDC_AUDIENCE`; `api` refuses to start when the server is on and they are equal. |
| `TAMIZA_MCP_CLIENT_ID` | `tamiza-mcp` | The public client registered in Keycloak. Only shown on the connect page. |
| `TAMIZA_MCP_CALLBACK_PORT` | `33418` | The loopback port registered for Claude Code, 1024 to 65535. Only shown in its command. |
| `TAMIZA_MCP_MAX_DATA_ACCESS` | `aggregates` | The most any project may allow: `aggregates` (summaries only) or `rows`. Each project's admin chooses a level up to it. |

A blank value means the default. A value `api` cannot read (a maximum that is neither `aggregates` nor `rows`, an equal audience, a port out of range) stops `api` at startup and names the variable. To turn the server off, set `TAMIZA_MCP_ENABLED=false` and restart `api`: `/mcp` and the protected resource metadata answer 404 to anyone, the connect page and the project cards disappear, and the records and each project's chosen level stay.

The limits have defaults you rarely need to change. They are read from the `Mcp` configuration section, so set them as environment variables of `api` in a compose override rather than in `.env`:

| Setting | Default | Meaning |
|---|---|---|
| `Mcp__MaxResultCharacters` | `60000` | The most characters one tool result holds, 10,000 to 140,000. A longer result is cut from the end of its longest list and says so. |
| `Mcp__MaxConcurrentCallsPerUser` | `4` | Calls one person runs at once across all their clients, 1 to 16. |
| `Mcp__CallsPerMinutePerUser` | `120` | Calls one person makes per minute, 10 to 1,000. |
| `Mcp__ProfileStatementTimeout` | `00:01:00` | How long one column profile may run in PostgreSQL, from `00:00:05` to `00:05:00`. |
| `Mcp__CallRetentionDays` | `180` | How long call records are kept, 7 to 3,650. |

The counters of the limits are in memory, so restarting `api` resets them, and they assume a single `api` instance, as the assistant's budget and the ingestion queue do.

### Who can reach it

The hosted Claude apps (claude.ai, Claude Desktop and the mobile apps) connect from Anthropic's servers, so `TAMIZA_PUBLIC_URL` and Keycloak must be reachable from the Internet over HTTPS; Anthropic publishes the address range (`160.79.104.0/21` at the time of writing) if you filter by source. Claude Code connects from the analyst's computer and works on a private network when that computer reaches both. When the server is on and `TAMIZA_PUBLIC_URL` is not an `https` address, `api` logs a warning at startup. If your installation is private and people use claude.ai, Anthropic documents MCP tunnels for that case.

Requests to `/mcp` that carry an `Origin` header for another origin than `TAMIZA_PUBLIC_URL` are refused with 403, so a page in a browser cannot call the server with the person's credentials. AI clients send no `Origin`.

### Timeouts and proxies

A call can wait for a run's step or a script's test, up to two minutes, and results stream as server-sent events. The `ui` service has `location = /mcp` with a 300-second read timeout, no buffering and a 1 MB body limit (a long script goes in the request), and `location ^~ /.well-known/oauth-protected-resource` with the default timeout. A reverse proxy of your own in front of `ui` needs the same for `/mcp`: a read timeout of at least 300 seconds, response buffering off, and a body limit of at least 1 MB. It must pass `/.well-known/oauth-protected-resource` too, or clients cannot find the realm. Do not add CORS headers: the clients call from servers or native apps.

### Records

Every tool call leaves one row in `tamiza.mcp_calls`: who, the project, the tool, the level that applied, the outcome (`succeeded`, `failed` or `refused`), the error code, how long it took and how many characters the result had. The rows hold no arguments and no result. They are kept 180 days by default and deleted with their project. Project analysts see the latest 50 in **Settings > General**.

```sh
docker compose exec db psql -U tamiza -d tamiza -c "
SELECT date_trunc('day', c.started_at) AS day, u.email, c.tool, count(*) AS calls,
       count(*) FILTER (WHERE c.status <> 'succeeded') AS not_succeeded,
       round(avg(c.duration_ms)) AS avg_ms
FROM tamiza.mcp_calls c JOIN tamiza.users u ON u.id = c.user_id
GROUP BY 1, 2, 3 ORDER BY 1 DESC, 4 DESC LIMIT 30"
```

Replace `tamiza` after `-U` with your `POSTGRES_USER` if you changed it. A `refused` call is an answer the person was meant to get (the project is off, a role is missing, the version changed, the rate limit, an argument the client got wrong): the `error_code` says which. Refusals for a limit are recorded once a minute for a person; the others are all recorded.

### Rolling back

Turning the server off hides it; nothing else needs undoing. The migration adds `projects.mcp_access` (every project `off`), `script_versions.origin` (`app` for every existing version, `mcp` for the ones an AI client saved) and `tamiza.mcp_calls`. Going back to an earlier Tamiza version ignores them. Scripts and dashboards an AI client changed stay as they are: the scripts keep every earlier version, which can be restored from the Versions tab, while a dashboard or a widget changed wrongly is not versioned and must be fixed by hand.

## Map basemaps (optional)

The dashboard's maps can have a background map under their shapes, streets or satellite imagery (see [Maps](indicators-dashboard.md#maps)). It is **off by default**: with no provider, no browser and no server makes a request to any tile provider, and maps still zoom and pan on their plain background. Tamiza ships with no provider account and no key: you choose one, and the choice is yours to make.

A deployment offers **one provider**. The analyst picks None, Streets or Satellite for each map widget, and any project member can switch the background of a map for their own screen without saving anything.

### Turn it on

Set these in `.env` and run `docker compose up -d api`. Only `api` reads them. The `ui` image that came with the release (rebuilt by `docker compose up -d --build`) already carries the content security policy and the tile route that basemaps need.

| Variable | Default | Meaning |
|---|---|---|
| `TAMIZA_MAP_BASEMAP_PROVIDER` | empty (`none`) | `none`, `osm` or `google`. |
| `TAMIZA_MAP_GOOGLE_API_KEY` | empty | Required with `google`: a Map Tiles API key. Letters, digits, `-` and `_` only. |
| `TAMIZA_MAP_GOOGLE_LANGUAGE` | empty (`en-US`) | An IETF language tag for Google's labels, such as `es-419`. |
| `TAMIZA_MAP_GOOGLE_REGION` | empty (`US`) | A region code for Google's labels and borders, such as `CO`. Upper-cased. |
| `TAMIZA_MAP_TILES_PER_MINUTE` | `600` | Google tiles each user may be sent per minute, 60 to 10,000. |
| `TAMIZA_MAP_TILES_PER_USER_PER_DAY` | `3000` | Google tiles each user may be sent per UTC day, 100 to 1,000,000. |

Blank text means unset. A provider other than the three, `google` without a key, or a number out of range stops `api` at startup and names the variable. The key, language and region are read only with `google`; set without it, they are ignored and `api` logs one warning naming them. At startup `api` logs one line saying which hosts will be contacted and by whom; check it with:

```sh
docker compose logs api | grep "Map basemaps"
```

To turn basemaps off, blank `TAMIZA_MAP_BASEMAP_PROVIDER` and run `docker compose up -d api`. Maps saved with a basemap are drawn without one and without an error; the widget form shows the saved basemap as no longer offered and keeps it if the widget is saved unchanged. Links that browsers still hold stop working at once.

### OpenStreetMap (`osm`)

Streets only, from OpenStreetMap's standard tiles. Each viewer's **browser** loads the tiles from `https://tile.openstreetmap.org/{z}/{x}/{y}.png`; `api` is not involved. OpenStreetMap's [tile usage policy](https://operations.osmfoundation.org/policies/tiles/) applies to you as the operator, and in short it asks for:

- exactly that tile address, with no change to its host or path;
- a visible credit, "© OpenStreetMap contributors", which Tamiza always shows inside the map;
- a valid `Referer`: Tamiza sends your deployment's origin only (never a page's path) on tile requests, and keeps `Referrer-Policy: no-referrer` for everything else, such as export links;
- no prefetching and no offline copy: Tamiza loads only the tiles of the area on screen, and never stores them;
- no heavy or commercial use. OpenStreetMap's servers are run by volunteers and may block an address or an application that uses them heavily. If your dashboards are used by many people, or by a business, use `google`, or run your own tile service, which Tamiza does not support yet.

The `ui` content security policy allows images from `https://tile.openstreetmap.org` in **every** deployment, even with no provider set. That single fixed origin is the only one added.

### Google (`google`)

Streets (Google's roadmap) and Satellite, through Google's Map Tiles API. **Only the `api` container contacts Google**, from its own address, with a session it creates and renews itself. The key never reaches a browser: viewers get tiles from `api` through links that `api` issues only to members of at least one project (and superadmins), valid for at most an hour, for one user and one basemap, and that stop working when you switch the provider off. `api` passes each tile on as Google sent it, with Google's cache rules, and stores nothing; the tile route marks tiles for use by your own pages only. `api` needs outbound HTTPS to `tile.googleapis.com`, without a proxy.

What you do, as the operator:

1. In [Google Cloud](https://console.cloud.google.com/), create or choose a project, **enable the Map Tiles API** and **set up billing**. You accept Google's [Maps Platform terms](https://cloud.google.com/maps-platform/terms) and the [Map Tiles API policies](https://developers.google.com/maps/documentation/tile/policies) with your own account.
2. Create an API key and **restrict it** to the Map Tiles API and to the outbound IP address of the `api` container's host. Google describes such a key as one that is not expected to be public, and with the address restriction a leaked copy is of no use elsewhere. Put it in `TAMIZA_MAP_GOOGLE_API_KEY`. Never commit a file that holds it.
3. **Set the daily quota for 2D tiles in the Google Cloud console and billing alerts.** The quota is your cost cap. Read Google's current pricing and quota pages: when this was written, the first 100,000 tiles a month were free, tiles after that were billed each, and the default quota was 15,000 2D tiles a day.
4. Choose per-user limits that fit that quota. A busy map view loads a few dozen tiles, and zooming and panning load more; `TAMIZA_MAP_TILES_PER_USER_PER_DAY` (default 3,000) stops one user, or a script holding their token, from using up the project's whole day. Tamiza's limits protect against bursts and against one user; they are **not** a cost cap, because they reset when `api` restarts and count each user alone. Past the limit a user's tiles answer 429 and their map says the basemap is unavailable.
5. **Mention Google's terms and privacy policy in your deployment's end-user terms.** Google's terms ask for it (section 3.2.2(a) of the Maps Platform Terms of Service); the people who use your dashboards are Google's end users through your deployment. Link [Google's terms](https://policies.google.com/terms) and [privacy policy](https://policies.google.com/privacy).

Tamiza shows Google's logo and the data credits Google gives for the area on screen at all times, and **draws no Google imagery when it cannot get those credits**. Zooming past the imagery Google has for an area shows the deepest tiles enlarged instead of failing.

**One provider per deployment.** Google's terms do not allow showing its maps with or near a non-Google map (section 3.2.3(e)). Offering a single provider makes that true by construction, so a Google map is never next to an OpenStreetMap one. This is a reading of the terms by the authors of Tamiza, **not legal advice**: your organization answers for its own use of Google's services.

### What leaves the deployment

Basemap requests carry only the basemap kind, tile coordinates and, for Google's data credits, the zoom and bounds of the area on screen. **No row, group value, geometry or tooltip value is ever sent** to a tile provider.

- With `osm`, each viewer's browser contacts OpenStreetMap and sends what a browser sends with any image: its IP address, its user agent and your deployment's origin. OpenStreetMap can see which blocks of the map each viewer looks at.
- With `google`, only `api` contacts Google, adding its session, the key, the language and the region. Google sees `api`'s address and the tiles requested, not the viewers.
- **Any project member can turn a basemap on** for any map they can see, once a provider is set. The analyst's choice in the widget form decides what everyone sees first; it is not a privacy control. If you do not want a provider contacted, leave `TAMIZA_MAP_BASEMAP_PROVIDER` empty.

This is a data-protection decision of your organization, like [the assistant's](#choosing-a-provider). The location of the areas people look at can itself be sensitive (for example, the blocks of a survey of households). Read the provider's current terms and privacy pages first. In Colombia, for example, Ley 1581 de 2012 and the authorization you hold from the people concerned may regulate what reaches a provider, and donors often set rules of their own. This is not legal advice: ask whoever answers for data protection in your organization.

### Logs and proxies

- The `ui` container's tile route (`/api/v1/basemap-tiles/`) turns proxy buffering off, so no tile is spooled to disk, and turns its access log off, so tile links stay out of the `ui` access log. nginx's **error log** still holds the request line when `api` fails to answer, for example while it restarts. The error log is kept on purpose, for diagnosis; treat it like any log that may hold a tile link.
- An **outer reverse proxy** or load balancer that logs requests will log tile addresses. A tile link is valid for at most an hour, for one user and one basemap, and tile coordinates show which area a viewer looked at. Keep those logs as private as the rest of your access logs, or exclude `/api/v1/basemap-tiles/` from them.
- The key is never in a tile address, a response, a page, or a log line of `api`: the client that talks to Google logs nothing of its requests, and `api` logs a failure at most once a minute per basemap, without Google's message.

### Rollback

Restore the previous images (`docker compose up -d` with the earlier `TAMIZA_VERSION`). Widgets keep working. But an `api` that predates this change does not know the `tooltip` and `basemap` fields, so **any widget edited or moved under it loses its tooltip columns and its basemap** (a move saves the whole widget). Widgets are not changed otherwise, and nothing is lost until one is saved under the old `api`.

## Email (optional)

Analysts and admins can give each dashboard a **scheduled email**. On the weekdays they choose, at one time of day in the project's time zone, Tamiza emails the dashboard's figures to 1 to 20 addresses, which need not belong to Tamiza users. They set it up with *Scheduled email…* in the dashboard's menu; viewers neither see nor manage it. It is **off by default**: Tamiza sends through an SMTP server that you give `api`, and it ships with no mail account. Without one, the menu action is not shown and no SMTP connection is ever opened. Keycloak's [account emails](keycloak.md#account-emails-optional) are separate and keep using the realm's own SMTP settings.

Each email holds:

- **In its body:** the project, the dashboard, when the email was generated, how current the data is and the filters in force; each stat tile's value; a short table with the first rows of each other widget; notes for anything that could not be shown; and a link to the dashboard for members, under `TAMIZA_PUBLIC_URL`. A plain-text version goes with it.
- **As an attachment:** an XLSX workbook. A *Summary* sheet holds the header and the stat tiles, then one sheet per widget holds its whole table, as *Show as table* shows it, with numbers as numbers. A workbook larger than 7 MB is left out, and the body says so.

It holds no chart, no map drawn as a map, no image and no PDF. It uses the dashboard's filter defaults, as a member sees them on opening it, and widgets that ignore a filter keep ignoring it. Recipients are in Bcc, so none sees another's address, and replies go to the member who last saved the settings. A failed send is **not retried**: the dialog shows the failure until a later send succeeds. A scheduled time that passes while `api` is down is sent once, late, if it is still the same day in the project's time zone. Earlier days are not caught up, and no day gets two scheduled emails.

### Turn it on

Set these in `.env` and run `docker compose up -d api`. Only `api` reads them. The `ui` image of the release already has the dialog; members see *Scheduled email…* once they reload Tamiza in their browser.

| Variable | Default | Meaning |
|---|---|---|
| `TAMIZA_SMTP_HOST` | empty | The SMTP server's host name, such as `smtp.example.org`. `api` must reach it (see [Requirements](#requirements)). |
| `TAMIZA_SMTP_PORT` | `587`, `465` or `25` | 1 to 65,535. The default depends on the security: 587 for `starttls`, 465 for `tls`, 25 for `none`. |
| `TAMIZA_SMTP_SECURITY` | `starttls` | `starttls`: connect, then upgrade with STARTTLS before signing in. A server that does not offer STARTTLS fails the send, so nothing goes unencrypted. `tls`: TLS from the start (implicit TLS, usually port 465). `none`: no encryption, only for a relay inside your own network or a test server such as Mailpit (see [Checking delivery with Mailpit](#checking-delivery-with-mailpit)). |
| `TAMIZA_SMTP_USERNAME` | empty | The user name to sign in with. Set it together with the password, or leave both empty to send without signing in, to a relay that trusts `api`'s address. |
| `TAMIZA_SMTP_PASSWORD` | empty | Its password. It is never logged, returned by the API or shown. Spaces at either end are removed, like a pasted trailing newline. |
| `TAMIZA_SMTP_FROM` | empty | The sender address, a plain address such as `reports@example.org`, without a display name. See [The sender address](#the-sender-address). |
| `TAMIZA_SMTP_FROM_NAME` | `Tamiza` | The sender's display name, so that emails come from `Tamiza <reports@example.org>`. |
| `TAMIZA_SMTP_TIMEOUT_SECONDS` | `30` | How long `api` waits for the server before the send fails as *server unavailable*, 5 to 300. |

Email is on **only when `TAMIZA_SMTP_HOST` and `TAMIZA_SMTP_FROM` are both set**. Blank text means unset. With only one of them, email stays off and `api` logs one warning at startup naming the missing variable. A malformed value stops `api` at startup, and its log names the variable and the values it accepts: a port out of range, a security other than the three, a sender that is not a plain email address, a timeout out of range, or a user name without a password (or the reverse).

With `starttls` and `tls`, `api` always checks the server's certificate: it must be valid for `TAMIZA_SMTP_HOST` and issued by an authority that the `api` image trusts. There is no setting to skip the check, so a relay with a self-signed certificate fails as *server unavailable*.

Put the password in single quotes when it holds a `$`. Compose reads a `$` in `.env` as the start of a variable, so `TAMIZA_SMTP_PASSWORD=pa$word` reaches `api` as `pa`; write `TAMIZA_SMTP_PASSWORD='pa$word'`. As with the [assistant's key](#ai-assistant), **never put the password in a file that is committed**.

**Check it with *Send now*.** Sign in as an analyst or admin, open a dashboard with at least one widget whose indicator is published, choose *Scheduled email…* in its menu, enter your own address, a weekday and a time, and save. Then choose *Send now*: the email is queued and sent shortly, ahead of scheduled emails that have not started yet. Look in your inbox, including the spam folder, then open the dialog again. It shows when the email was last sent or, when the send failed, when and why. *Send now* uses the saved settings, works while the email is paused and does not change the schedule. A second *Send now* for the same dashboard within 10 minutes is refused.

### When a send fails

The dialog names the failure in its own words and never shows the server's reply:

| The dialog shows | What to check |
|---|---|
| Server unavailable | `api` could not connect, could not set up TLS, or got no answer within `TAMIZA_SMTP_TIMEOUT_SECONDS`. Check the host and the port, that `api` can reach them (many hosts block outbound port 25), that the security matches the port (`starttls` on 587, `tls` on 465), and that the server offers STARTTLS and has a valid certificate. |
| Authentication failed | The server refused the user name and password; or they are set and the server offers no sign-in (a relay, or Mailpit); or they are empty and the server requires them. For Microsoft 365, see [Microsoft 365](#microsoft-365). |
| Rejected | The server refused the sender, the message or a recipient, which the dialog names. **Nobody received the email**, not even the other recipients. Usually the account may not send as `TAMIZA_SMTP_FROM`, or a recipient's address does not exist on that server. A provider that has suspended the account or reached its quota may answer this way too. |
| Nothing to send | The dashboard has no widget, or none of its widgets could be read, for example because none of its indicators is published yet. This is not an SMTP problem. |
| Unexpected error | Something else failed; the `api` log has the details. |

`api` logs each failed send with the SMTP host and port, the step that failed (connecting, signing in or sending), the outcome and the SMTP status code when the server gave one, never the password or the server's reply:

```sh
docker compose logs api | grep "SMTP server"
```

To read the server's reply itself, look at your provider's logs, or try the same settings from another mail program. Tamiza does not process bounces: when a recipient's server refuses an email after your SMTP server accepted it, the dialog still shows it as sent. Your provider's logs list such bounces.

### Choosing an SMTP provider

Any SMTP server that accepts a user name and password, or a relay that trusts `api`'s address, works. Two kinds suit Tamiza:

- **A transactional email service**, such as Brevo, Mailjet, SMTP2GO or Amazon SES. You verify your sender's domain with them (see [SPF, DKIM and DMARC](#spf-dkim-and-dmarc)), and they give you an SMTP host, a user name and a password or key. Their logs show what was delivered, bounced or refused. Amazon SES starts every new account in a sandbox that sends only to verified addresses: ask for production access before you rely on it.
- **A relay of your organization**, such as a Postfix server on your network or your mail provider's relay. On your own network, `none` without credentials suits a relay that accepts mail only from known addresses. The email then crosses that network unencrypted as far as the relay.

**Free tiers count every recipient.** Tamiza sends one message per email with all its recipients in Bcc, but providers count each recipient as one email: a dashboard emailed every weekday to 20 people uses 100 a week, and five dashboards emailed daily to 20 people use 100 a day, which uses up an allowance of 1,000 a month in ten days. When this was written (October 2026), Brevo's free plan allowed 300 emails a day and added Brevo's branding to them, Mailjet's allowed 200 a day, and SMTP2GO's allowed 200 a day and 1,000 a month. Read the provider's current pricing and limits before you choose.

The provider receives every email it relays, attachment included, so choosing one is also a data-protection decision (see [What leaves the deployment by email](#what-leaves-the-deployment-by-email)).

### SPF, DKIM and DMARC

Receiving mail servers (Gmail, Outlook.com, Yahoo and most organizations' servers) check that the sender's domain allowed the server that sent an email. Mail that fails goes to the spam folder or is refused, and the dialog cannot tell: it shows the email as sent once your SMTP server has accepted it. Before the first real email, set up these records on the domain of `TAMIZA_SMTP_FROM`, with the values your provider gives:

- **SPF:** a TXT record that lists the servers allowed to send for the domain, usually through an `include:` that your provider names. A domain has **one** SPF record, so add the provider to the existing record instead of adding a second one.
- **DKIM:** the provider signs each email with a key whose public half you publish in DNS, as the TXT or CNAME records it gives you (under `<selector>._domainkey.<your domain>`).
- **DMARC:** a TXT record at `_dmarc.<your domain>` that tells receivers what to do with mail that fails SPF and DKIM, and where to send reports. A domain without one can start with `v=DMARC1; p=none; rua=mailto:dmarc-reports@example.org`, then move to `p=quarantine` or `p=reject` once the reports show only expected senders. If the domain already has a policy, mail from a provider that is not set up for it is refused or sent to spam.

Use a sender at a domain you control. An address at `gmail.com`, `outlook.com` or another public mail service cannot pass these checks when another provider's servers send for it, and receivers refuse it.

To check, *Send now* to a Gmail address and open the email's *Show original*: SPF, DKIM and DMARC should all read `PASS`.

### Microsoft 365

Exchange Online accepts mail from applications through SMTP AUTH (`smtp.office365.com`, port 587, `starttls`, a mailbox's address and password). Microsoft has announced, in its message center post MC786329, that basic authentication for SMTP AUTH is **turned off by default at the end of December 2026**. After that date, Tamiza's sends fail as *authentication failed* unless an admin of the tenant turns it back on for the mailbox that Tamiza uses. Tamiza signs in with a user name and password only: OAuth (XOAUTH2), which Microsoft recommends instead, is not supported yet (see [Not yet available](#not-yet-available)). Read Microsoft's current announcement before you rely on SMTP AUTH: its dates have moved before.

With SMTP AUTH, Exchange Online accepts as the sender only the mailbox that signs in, or an address it may send as, so there the sender is a mailbox. Two ways avoid SMTP AUTH altogether: a transactional provider for the sender's domain (its SPF and DKIM records go next to Microsoft's), or a Microsoft 365 connector that relays mail from your server's fixed public IP address without a password (your domain's MX host, port 25, `starttls`, no user name). Microsoft's page "How to set up a multifunction device or application to send email using Microsoft 365 or Office 365" describes the connector.

### The sender address

Tamiza puts every recipient in Bcc and writes `undisclosed-recipients:;` as the To header, so the sender address is never a recipient. It **needs no mailbox**, and a no-reply address such as `no-reply@example.org` works. The SMTP server must still accept it as a sender (providers accept any address of a verified domain), and its domain needs SPF and DKIM as above. Replies go to the member who last saved the settings, not to the sender. With your own relay, bounces go to the sender address, so they are lost if it has no mailbox; transactional providers collect bounces themselves and list them in their logs.

The exception is a server that lets an account send only as itself, or as an address it may send as, such as Microsoft 365 with SMTP AUTH (see [above](#microsoft-365)). There the sender is that account's mailbox.

### Checking delivery with Mailpit

[Mailpit](https://mailpit.axllent.org/) is an SMTP server for tests: it keeps every email it receives, delivers none, and shows them in a web page. Use it on a test installation to see an email as recipients would, with its headers and attachment, without sending anything out. Every email stays in Mailpit whatever its recipients, so a real schedule can be tried safely.

Start it on the stack's Compose network, so that `api` reaches it by its name. Compose names that network after the project: `tamiza_default` for the project `tamiza` set in `compose.yaml`. A stack deployed under another project name (Portainer uses the stack's name) has `<name>_default` instead; `docker network ls` lists it.

```sh
docker run -d --name mailpit --network tamiza_default -p 127.0.0.1:8025:8025 axllent/mailpit
```

The web page is bound to the loopback address because it shows every email, project data included, to anyone who reaches it. Then set these in `.env` and run `docker compose up -d api`:

```dotenv
TAMIZA_SMTP_HOST=mailpit
TAMIZA_SMTP_PORT=1025
TAMIZA_SMTP_SECURITY=none
TAMIZA_SMTP_USERNAME=
TAMIZA_SMTP_PASSWORD=
TAMIZA_SMTP_FROM=no-reply@example.org
```

Leave the user name and password empty: Mailpit, started this way, offers no sign-in, so credentials fail as *authentication failed*. Choose *Send now* as described under *Turn it on* above, and open `http://localhost:8025` on the test host. From another machine, reach it through an SSH tunnel: `ssh -L 8025:localhost:8025 user@test-host`. Check that the email's To reads `undisclosed-recipients:;`, that the body reads well, and that the workbook is attached.

On Docker Desktop you can instead publish Mailpit's SMTP port on the host (`docker run -d --name mailpit -p 127.0.0.1:8025:8025 -p 1025:1025 axllent/mailpit`, without `--network`) and set `TAMIZA_SMTP_HOST=host.docker.internal`. The published port 1025 then accepts mail from the host's network too. `compose.yaml` maps `host.docker.internal` for `api` on Docker Engine as well, but there a host firewall may block it, so prefer the Compose network.

When you are done, remove Mailpit and the emails it kept, then set your real server's values in `.env` and run `docker compose up -d api` again:

```sh
docker rm -f mailpit
```

### What leaves the deployment by email

Each email carries out of Tamiza what its dashboard shows, in the body and in the workbook: the names of the project, the dashboard, its widgets and its filters; the filter values in force; the aggregated figures; the **group values**, such as municipalities, or enumerators when a widget groups by them; and the **map tooltip text** (see [Tooltip and tooltip columns](indicators-dashboard.md#tooltip-and-tooltip-columns)). The workbook holds every widget's whole table, not only the first rows shown in the body. Group values and tooltip columns can hold **personal data**. The email goes to:

- **your SMTP provider**, which stores and logs what it relays, with the recipients' addresses, under its own terms;
- **the recipients' mail servers and mailboxes**, outside Tamiza's access control. **A sent email cannot be recalled**: removing the schedule, the dashboard or the project removes nothing that was sent.

Analysts and admins can send a dashboard to **any address of any domain**. The dialog warns them that the recipients get the data outside Tamiza, and each email names the project and tells recipients to reply to stop it. Tamiza does not check who the recipients are, so give the analyst and admin roles only to people you trust to share the project's figures.

This is a data-protection decision of your organization, like [the assistant's](#choosing-a-provider). In Colombia, for example, Ley 1581 de 2012 regulates the processing of personal data, including sharing it with third parties such as partners and funders, and the authorization you hold from the people concerned may not cover it; donors and partners often set rules of their own. The operator of the deployment, not Tamiza, answers for what its members send. This is not legal advice: ask whoever answers for data protection in your organization.

### Turning it off and rolling back

- **Stop all sending:** blank `TAMIZA_SMTP_HOST` or `TAMIZA_SMTP_FROM` and run `docker compose up -d api`. The menu action disappears once members reload Tamiza, saving and *Send now* answer 503 with `reports.unavailable`, and saved schedules are kept but not sent. When you set the variables again, each schedule resumes as after `api` was down: a time that passed earlier that same day is sent once, and earlier days are not caught up.
- **Stop one dashboard's email:** *Remove* it in the dialog, or pause it there.
- **Go back to the previous images** with `docker compose up -d` and the earlier `TAMIZA_VERSION`. An `api` older than this change does not know the table `tamiza.dashboard_reports`, sends nothing and is not affected by it; removing a dashboard still removes its schedule. Upgrading again later brings the schedules back as they were.

To also remove the schedules after going back to the previous images, drop the table and remove the record of its script from DbUp's journal, in one transaction:

```sh
docker compose exec db psql -U tamiza -d tamiza -c "
BEGIN;
DROP TABLE tamiza.dashboard_reports;
DELETE FROM tamiza.schema_versions WHERE scriptname = '0015_add_dashboard_reports.sql';
COMMIT;"
```

Replace `tamiza` after `-U` with your `POSTGRES_USER` if you changed it. Every schedule, with its recipients, is lost. DbUp runs a script only when `tamiza.schema_versions` does not list it, so with the record removed a later upgrade creates the table again, empty. Without the `DELETE`, a newer `api` would take the script as applied and fail on the missing table. Run this only under the previous images, never while a newer `api` runs.

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
- **OAuth sign-in to the SMTP server** (XOAUTH2), not planned in a change yet. Tamiza signs in to the [email](#email-optional) server with a user name and password only, so a Microsoft 365 mailbox needs basic SMTP AUTH turned on for it (see [Microsoft 365](#microsoft-365)), or another way of sending.
