# Operations

How to install, run and upgrade a self-hosted Tamiza. The quick start in the [README](../README.md) is the short version.

## Requirements

- Docker Engine 24 or later with the Compose v2 plugin (`docker compose`).
- About 4 GB of RAM. The default container limits add up to 3200 MiB (see [Memory](#memory)).
- An existing Keycloak realm configured as described in [keycloak.md](keycloak.md).
- **Outbound HTTPS from the `api` container** to the Kobo servers your projects use (for example `kf.kobotoolbox.org` or `eu.kobotoolbox.org`). Besides the checks users start, `api` synchronizes every project on a schedule (by default every 12 hours, plus a daily deletion check), so this traffic happens without anyone signed in. Tamiza connects to Kobo directly and does not use an outbound HTTP proxy: a proxy would make the connection on Tamiza's behalf and bypass its check that Kobo addresses are public. Installations that can only reach the Internet through a proxy cannot connect to Kobo yet.
- **Outbound HTTPS from the `api` container to the model provider**, only if you turn on the [AI assistant](#ai-assistant). Each assistant request goes from `api` straight to the base URL you configure (a provider on the Internet, or a model server on your own network over plain HTTP), also without an outbound HTTP proxy. Without the three assistant variables nothing is sent anywhere.
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

**Indicators dashboard.** The dashboard of widgets (see [indicators-dashboard.md](indicators-dashboard.md)) reads the indicator tables with the `DataDashboard__` time limits and query slots above, and exports them with the same export settings. Two more variables on the `api` service limit what a project can ask of it:

| Variable | Default | Meaning |
|---|---|---|
| `IndicatorDashboard__MaxWidgetsPerProject` | `50` | Most widgets one project's dashboard can hold, from 1 to 500. Widgets already saved are kept if you lower it; the project just cannot add more until it is under the limit. |
| `IndicatorDashboard__MaxWidgetQueryRows` | `5000` | Most rows one widget plots, from 1 to 100,000. A widget with more says that only the first rows are shown. |

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
