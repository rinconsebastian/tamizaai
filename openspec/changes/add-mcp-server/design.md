# Design

## Context

See proposal.md for the motivation and specs/ for the required behavior. The pieces this change builds on:

- **Authentication.** `Auth/AuthenticationRegistration.cs` registers one JWT bearer scheme against `OIDC_AUTHORITY` (discovery from `OIDC_METADATA_ADDRESS` when set) with `ValidAudience = OIDC_AUDIENCE`, `MapInboundClaims = false` and a fallback policy that requires an authenticated user. `UserProvisioningMiddleware` runs between authentication and authorization, turns `context.User` into a local user (`TokenIdentity`, `UserProvisioner`) and sets the scoped `CurrentUser`, which also knows whether the person is a superadmin (`SuperAdminRule`). `TestTokens.TrustTestKey(JwtBearerOptions)` makes the integration tests sign their own tokens.
- **Roles.** `Projects/ProjectAccess.GetRoleAsync` resolves a role (superadmins act as admin); `RequireProjectRole` is an endpoint filter that answers 404 to non-members and 403 to lower roles before a handler runs, so handlers never check roles themselves.
- **Handlers.** Every REST operation is a `private static` handler with explicit parameters (ids, a request record, `TamizaDbContext`, `ICurrentUser`, `TimeProvider`, services, `CancellationToken`) that returns `Results<Ok<T> | Created<T> | Accepted<T>, ValidationProblem, ProblemHttpResult, NotFound>`. Validation lives in the handlers and their helpers (`FieldErrors`, `Problems.Validation`, `DashboardValidation`, `IndicatorValidation`). Dashboards serialize their changes with one lock per project; widget placement without a layout is `PlaceBelow`.
- **What a script receives.** `Pipeline/ScriptInputsReader` reads the published raw or clean schema from the catalog (`TableCatalog.FindAllAsync` inside `DataDashboard.InTransactionAsync`, row counts from `DataDashboard.LatestStepAsync`), the reference tables' columns and `Project.FormFields`. The assistant's `SchemaBlock` adds `[select_one <list>]`, `[select_multiple <list>]` and `[or other]` from the form fields.
- **Tests and runs.** `ScriptTestEndpoints.StartAsync` validates and queues (one queued test per user and project, replaced by the next), `FindOwnAsync` reads a test of its requester for 24 hours, and `ScriptTestDetails` carries steps with previews (up to 100 rows), `ctx.show` values and, for an indicator, `IndicatorTestView` (totals, maxima, chart groups). `RunQueue.EnqueueManualAsync` queues a run and refuses one already queued (`run.already_queued`).
- **Masking.** `Assistant/Masker` keeps names of the schema, words of library messages and words of the draft and replaces any other token with `…`; `Assistant/TestFacts.ParseError` and `LogLines` reduce an error to its frames and exception and a log to its leveled lines. `AssistantPrompts` embeds `Prompts/system.md` (contract rules and the assistant's answer format), the templates of each kind and the vocabulary, with drift tests against the UI's templates, the pinned libraries and `ctx-members.json`.
- **Data reads.** `DataQueryBuilder` builds the clean data dashboard's filtered, sorted, paged queries over validated identifiers; `DataDashboardOptions.StatementTimeout` is 25 seconds; publishing waits in short attempts for readers of the tables it replaces. `IndicatorDashboard` runs widget queries (`IndicatorQueryRequest`, `MaxQueryRows`).
- **Edges.** nginx proxies `/api/` with a 60-second read timeout, with longer regex locations for exports and the assistant; `location /` serves the SPA, so `/mcp` and `/.well-known/…` would return `index.html` today. The `api` knows `TAMIZA_PUBLIC_URL`. One `api` instance runs, as the ingestion queue and the assistant already assume.
- **Clients.** Claude's hosted apps (claude.ai, Desktop, mobile, Cowork) connect from Anthropic's servers (`160.79.104.0/21`) and use the redirect URI `https://claude.ai/api/mcp/auth_callback`; Claude Code connects from the person's computer with a loopback redirect, and with `--client-id` and `--callback-port` it uses a pre-registered public client on a fixed port. Both send PKCE S256, start sign-in only on a `401` with a `resource_metadata` pointer, use the first authorization server listed, refresh tokens, and append `offline_access` when the authorization server lists it. A custom connector can be given a client ID with no secret ("Use your own OAuth client"). Tool results are limited to about 150,000 characters in the hosted apps and 25,000 tokens by default in Claude Code; a hosted tool call times out after 240 seconds. Claude implements the authorization revisions 2025-03-26, 2025-06-18 and 2025-11-25, and does not implement sampling, resource subscriptions or elicitation.
- **Keycloak.** Version 26 supports public clients with PKCE, audience mappers in client scopes and offline tokens. Dynamic Client Registration is available but closed to anonymous callers by default, and Client ID Metadata Documents and RFC 8707 resource indicators are experimental features.

## Goals / Non-Goals

**Goals:**
- One implementation per operation: a tool and its REST endpoint run the same handler, so validation, limits, locks and authorship cannot drift.
- Privacy decided twice, in the `api`, per call: by the operator (deployment maximum) and by each project's admin (off by default). The client can narrow what it reads, never widen it.
- A token held by an AI client can do nothing outside `/mcp`, and `/mcp` accepts nothing but those tokens.
- Nothing new to install or run: no new container, no outbound request, no model provider, and no change to the analytics worker.
- Results that fit the hosted clients' limits and stay useful when cut.

**Non-Goals:**
- A sandbox for scripts. The unsafe-code check (D9) is defense in depth, not isolation; `harden-deployment` removes the worker's Internet egress.
- Supporting Dynamic Client Registration, Client ID Metadata Documents or personal access tokens. A realm that enables them works without changes in Tamiza, but this change documents and verifies only the pre-registered public client.
- Tools for settings, members, the sampling frame, the Kobo connection, synchronizations, reference table uploads, email reports or printing.
- An MCP App, a text widget, MCP resources, or a model call from Tamiza.

## Decisions

### D1. Configuration

`Mcp/McpOptions.cs`, bound like `TamizaOptions` with flat keys for deployment settings and `Mcp__*` for tuning, as the dashboards and the assistant do:

| Variable | Type, default | Rule |
|---|---|---|
| `TAMIZA_MCP_ENABLED` | bool, `false` | Turns the server on. |
| `TAMIZA_MCP_AUDIENCE` | string, `tamiza-mcp` | The `aud` that `/mcp` requires; it must differ from `OIDC_AUDIENCE`. |
| `TAMIZA_MCP_CLIENT_ID` | string, `tamiza-mcp` | The public client registered in Keycloak; only shown on the connect page. |
| `TAMIZA_MCP_CALLBACK_PORT` | int, `33418` | 1,024 to 65,535: the loopback port registered for Claude Code, shown in its command. |
| `TAMIZA_MCP_MAX_DATA_ACCESS` | `aggregates` (default) or `rows` | The deployment's maximum (D5). |
| `Mcp__MaxResultCharacters` | int, 60,000 | 10,000 to 140,000 (D11). |
| `Mcp__MaxConcurrentCallsPerUser` | int, 4 | 1 to 16 (D12). |
| `Mcp__CallsPerMinutePerUser` | int, 120 | 10 to 1,000 (D12). |
| `Mcp__ProfileStatementTimeout` | timespan, 60 s | 5 s to 5 min (D8). |
| `Mcp__CallRetentionDays` | int, 180 | 7 to 3,650 (D13). |

A custom `IValidateOptions` with `ValidateOnStart` stops the `api`, naming the variable, for an unknown data access value, an audience equal to `OIDC_AUDIENCE` or a number out of range. When the server is enabled and `TAMIZA_PUBLIC_URL` is not `https` (other than `localhost`), the `api` logs once at Warning that the hosted Claude apps will not connect. Compose passes the five flat variables to `api` only.

*Alternatives:* deriving the audience from the public URL (`https://…/mcp`), as RFC 8707 resource indicators would; rejected while Keycloak's support is experimental. An operator who enables it can set the audience to that URL.

### D2. SDK and transport

`ModelContextProtocol.AspNetCore`, pinned to 2.2.0 (2026-08-13), the newest release at least two weeks old when this change was written (2.3.0 is from 2026-10-20); the implementer re-checks the pin, as the `MailKit` pin does. `Mcp/McpRegistration.cs` adds, only when enabled, `AddMcpServer(o => { o.ServerInfo = ("tamiza", version); o.ServerInstructions = McpGuide.Instructions; }).WithHttpTransport(o => o.Stateless = true).WithTools<…>().WithPrompts<McpPrompts>()` and `app.MapMcp("/mcp").RequireAuthorization(McpPolicy)`. When disabled nothing is mapped, so `/mcp` answers 404.

Stateless: each POST is answered by itself, there is no session store and no affinity, and every request carries its token, which suits one `api` instance and its restarts. A call that waits (D10) answers with an event stream so progress notifications can precede the result; other calls answer with a single JSON message. The SDK negotiates the protocol version with the client, and the verification checks it with Claude (task 11.2).

*Alternatives:* a separate MCP container (rejected: a fifth service and a second copy of every rule, or HTTP calls to the REST API with a token it should not accept); stdio for local clients (rejected: it needs a database connection or a stored credential on the analyst's computer).

### D3. Authentication

`Mcp/McpAuthentication.cs`:

- A second `JwtBearer` scheme, `Mcp`, configured from the same `OidcOptions` (authority, metadata address, HTTPS metadata, issuer, `MapInboundClaims = false`, `NameClaimType`) with `ValidAudience = TAMIZA_MCP_AUDIENCE`.
- A policy scheme registered as the default authenticate scheme, whose `ForwardDefaultSelector` returns `Mcp` for requests whose path starts with `/mcp` and the existing scheme otherwise. `UseAuthentication` therefore validates each path against its own audience, and `UserProvisioningMiddleware` provisions the person the same way for both. A token for `tamiza-api` sent to `/mcp`, or a token for `tamiza-mcp` sent to `/api/v1`, fails validation and gets 401.
- The SDK's `AddMcp` authentication scheme as the challenge scheme of `McpPolicy`, with `ResourceMetadata = { Resource = <public URL>/mcp, AuthorizationServers = [OIDC_AUTHORITY], ScopesSupported = ["tamiza-mcp"], ResourceName = "Tamiza" }` and the metadata served at `/.well-known/oauth-protected-resource/mcp` (RFC 9728 path form). Its challenge writes `WWW-Authenticate: Bearer resource_metadata="<public URL>/.well-known/oauth-protected-resource/mcp"`. The metadata is anonymous; it holds nothing secret.
- Every URL is built from `TAMIZA_PUBLIC_URL`, never from the request's `Host`, because Claude compares `resource` with the address the person entered.
- `Origin`: an endpoint filter on `/mcp` answers 403 when an `Origin` header is present and is not the origin of `TAMIZA_PUBLIC_URL`.

The `tamiza-mcp` client scope in Keycloak carries the audience mapper (D16), so the scope that the metadata advertises is the one that puts `tamiza-mcp` in `aud`. The `api` checks the audience only, not `azp` or `scope`, so a realm that later allows registered clients works unchanged. Tokens never leave the `api`: no tool calls anything with them.

### D4. The person and the project of a call

Tools are classes with `[McpServerToolType]` and methods with `[McpServerTool(Name = "…", ReadOnly = …, Destructive = …, Idempotent = …)]` and `[Description]`; the SDK resolves their parameters from the request's scoped services, so `ICurrentUser`, `TamizaDbContext` and the rest are those of the HTTP request that provisioning already set.

`Mcp/McpAccess.cs`:

```csharp
Task<McpProject> RequireAsync(Guid projectId, ProjectRole minimum, McpNeeds needs, CancellationToken ct)
```

reads the role with `ProjectAccess.GetRoleAsync` (null → `mcp.projectNotFound`; below `minimum` → `mcp.forbidden` naming the role), then the project's `McpAccess` and the deployment's maximum; the effective level is the lower one; `off` → `mcp.projectOff`; `needs == Rows` and level `aggregates` → `mcp.rowsNotAllowed`. It returns the role and the level for the tool to shape its result (D7). A refusal is an `McpRefusal` exception that `McpCallFilter` (D12) turns into a tool result with `isError: true` and the body `{ "code": "…", "message": "…", "fields": { … } }`. Unknown tools and arguments that do not match the schema are JSON-RPC errors, as the SDK gives them.

### D5. Data access levels

`projects.mcp_access` (`off`, `aggregates`, `rows`) is edited by an admin through `PATCH /api/v1/projects/{id}` with `mcpAccess`, which rejects `rows` while the maximum is `aggregates` (400 on `mcpAccess`). `ProjectDetails` gains `McpAccess` and `McpMaxAccess` (null for a viewer). Lowering the maximum later does not rewrite the column: the effective level is computed per call, and Settings > General shows "limited to Summaries only by the deployment" next to a stored `rows`. Changing the level takes effect on the next call; there is no session to invalidate.

### D6. Tools

Each tool resolves access (D4), then calls the handler of its REST endpoint, made `internal` where it is `private`, and translates the typed result with `McpResults.From`: `Ok`, `Created` and `Accepted` give their value as structured content and as JSON text; `ValidationProblem` gives `mcp.validation` with its field errors; a `ProblemHttpResult` gives its `code` extension (such as `dashboards.limit` or `run.already_queued`) or `mcp.failed` with its title and detail; `NotFound` gives `mcp.notFound`. Where a handler mixes reading and shaping, the read moves into a method both call. Arguments are camelCase like the REST bodies; tool names are snake_case.

| Tool | Reuses | Role | Annotations | Notes |
|---|---|---|---|---|
| `list_projects` | `ProjectEndpoints.ListAsync` | signed in | read-only | Adds each project's effective level; projects that are off are listed with `available: false` |
| `get_project` | `ProjectEndpoints.GetAsync`, `IngestionEndpoints.StatusAsync`, `DataEndpoints.GetOverviewAsync`, `RunEndpoints.ListAsync` (first), `ScriptEndpoints.ListAsync` (analyst) | viewer | read-only | Stored, deleted and latest submission; last sync |
| `get_form` | `Project.FormFields`, `FormChoices`, `IngestionEndpoints.ListFormVersionsAsync` with counts per version | analyst | read-only | `list` argument reads one choice list in full |
| `get_tables` | `ScriptInputsReader` generalized to a source; `IndicatorEndpoints.GetColumnsAsync`; reference table summaries | viewer / analyst | read-only | `source`: `raw`, `clean`, `output`, `indicators`, `reference` |
| `profile_table` | `ColumnProfiler` (D8) | as `get_tables` | read-only | `columns` up to 40 |
| `get_fieldwork` | `PerformanceEndpoints.GetAsync` | viewer | read-only | `from`, `to` days |
| `query_rows` | `DataEndpoints.QueryAsync` widened to the raw and indicator schemas; reference rows from `reference_table_contents` | as `get_tables`, rows | read-only | `columns`, `filters`, `sort`, `page`; 100 rows at most |
| `get_authoring_guide` | `AssistantPrompts` (D14) | analyst of a project with access | read-only | `kind` |
| `list_scripts`, `get_script`, `list_script_versions` | `ScriptEndpoints` list, versions, version | analyst | read-only | Versions carry `origin` |
| `save_script` | `SaveAsync`, `SaveIndicatorAsync` | analyst | changes | `baseVersion` (D9) |
| `remove_script` | `RemoveAsync`, `RemoveIndicatorAsync` | analyst | destructive | `baseVersion` |
| `restore_script_version` | `RestoreAsync`, `RestoreIndicatorAsync` | analyst | changes | `baseVersion` |
| `test_script`, `get_test` | `ScriptTestEndpoints.StartAsync`, `FindOwnAsync`, `DetailsAsync` | analyst | changes / read-only | `waitSeconds`, `previewRows` (rows only) |
| `start_run`, `get_run` | `RunEndpoints.StartAsync`, `GetAsync`, `ListAsync` | analyst | changes / read-only | `waitSeconds` |
| `list_indicators`, `get_indicator_columns` | `IndicatorEndpoints.GetOverviewAsync`, `GetColumnsAsync` | viewer | read-only | |
| `query_indicator` | `IndicatorEndpoints.QueryAsync` | viewer | read-only | `chartType`, `config` |
| `list_dashboards`, `get_dashboard` | project details, `DashboardEndpoints.GetDashboardAsync` | viewer | read-only | widget counts per dashboard |
| `get_widget_data` | `ReportDataBuilder` and `WidgetQuery`, which already read a dashboard as a member opens it and turn each widget into its "Show as table" rows; extended to take filter values instead of the defaults | viewer | read-only | `widgetId` optional, `filterValues` |
| `create_dashboard` | `AddAsync`, then `ReorderAsync` when `position` is given | analyst | changes | |
| `update_dashboard` | `RenameAsync`, `ReorderAsync`, `SaveFiltersAsync` | analyst | changes | `position` is turned into the whole order, with the current order as `expectedIds` |
| `duplicate_dashboard` | `DuplicateAsync` | analyst | changes | |
| `remove_dashboard` | `RemoveAsync` | analyst | destructive | `expectedName` (D9) |
| `create_widget`, `update_widget` | `CreateWidgetAsync`, `UpdateWidgetAsync` | analyst | changes | `moveToDashboardId` maps to the PATCH's `dashboardId` |
| `remove_widget` | `DeleteWidgetAsync` | analyst | destructive | `expectedTitle` |

A tool's description says what it returns at each level, which tool to call next (for example "then `start_run`; a widget needs a published indicator"), and the shape of `config` with an example, copied from `WidgetConfigInput`'s documentation. A unit test checks that every tool has a description, a role and annotations, and that its name is in `docs/mcp.md`.

### D7. Summaries and rows

Each tool shapes its result from the level returned by `McpAccess`:

- **Tests.** `McpTestView.From(ScriptTestDetails, level, masker)`: steps with status, duration, input and output rows and stop line; tables with name, rows, columns, types and question labels; `ctx.show` entries with name, line, rows and columns or "a text value"; for an indicator, the dashboard view's roles, defaults, rows, empty values, duplicate rows, totals, warnings and up to 50 groups per chart. At `aggregates`, the error is `ParseError`'s frames and exception and the log is `LogLines`, both through a `Masker` built as the assistant builds it (identifiers from the inputs schema, labels from the form, the draft's tokens); previews, `ShownPreview.Text`, `ShownPreview.Table` rows and unleveled log lines are left out with a `withheld` note. At `rows`, the first 20 rows of each table (`previewRows` up to 100), the `ctx.show` values and the full error and log are included.
- **Runs.** The same for each step's error and log; the masker's draft is the code of the version the step ran.
- **Profiles** are aggregates at both levels (D8).
- **Indicator queries and widget data** are what dashboards show, at both levels.
- **`query_rows`** needs `rows`.

The rules sit in one class with one unit test per kind of value that must never reach a summary (preview rows, `ctx.show` text, a printed line, a value inside an error), modelled on the assistant's canary tests.

### D8. Column profiles

`CleanData/ColumnProfiler.cs`, inside `DataDashboard.InTransactionAsync` with `Mcp__ProfileStatementTimeout`. The table and columns come from the catalog only (`TableCatalog`), never from request text, and are quoted. Up to 40 columns per call: the first 40 in table order, or those named (unknown names are field errors).

1. One statement for every column: `count(*)`, `count(c)`, empty values (`c IS NULL`, or blank text), `count(DISTINCT c)`; for numbers `min`, `max`, `avg` and `percentile_cont(ARRAY[0.25, 0.5, 0.75]) WITHIN GROUP (ORDER BY c)`; for dates and timestamps `min`, `max`; for booleans `count(*) FILTER (WHERE c)`.
2. For a column that answers a select question (matched to `FormFields` by column name, as `SchemaBlock` matches it, including repeat tables): `SELECT c, count(*) … GROUP BY c ORDER BY 2 DESC LIMIT 201`, or with `unnest(string_to_array(c, '; '))` for select_multiple; counts below 5 become `"<5"`.
3. For another text column with at most 50 distinct values: the same grouping, keeping values with at least 5 rows and folding the rest into `{ otherValues, otherRows }`.

`MinGroupRows = 5`, `MaxListValues = 50` and `MaxColumns = 40` are constants. Results are cached in `IMemoryCache` for 30 minutes under the schema, table, column set and the `finished_at` of the step that last published the schema, so a republish starts fresh. Profiles of the raw schema serve "what arrives from Kobo"; of the clean and output schemas, what script 1 and script 2 made; of an indicator table, what an indicator published.

*Alternatives:* profiles computed by the worker at publish time (rejected for now: it lengthens every run and changes the worker, while most tables are never profiled); approximate distinct counts (rejected: PostgreSQL has none built in, and exact counts over 40 columns fit the timeout on the reference volume, task 4.3 measures it).

### D9. Guards on changes

- **`baseVersion`.** `save_script`, `remove_script` and `restore_script_version` read the current version of the script, under the same transaction that adds the version, and refuse with `mcp.scriptChanged` (`current: { number, author, at }`) when it is not `baseVersion`; `null` means "no current version" (new, or removed). The editor's own saves are unchanged.
- **Origin.** `script_versions.origin` is `mcp` for versions added through a tool; `AddVersionAsync` takes it as a parameter, `app` everywhere else. `ScriptVersionSummary` and `ScriptVersionDetails` gain `origin`.
- **Names.** `remove_dashboard` and `remove_widget` compare `expectedName` and `expectedTitle` with the stored name or title (trimmed, case-insensitive) before calling the handler, and refuse with `mcp.nameMismatch` (`current`). A rename between the check and the removal is caught by the handler's own lock and row checks.
- **Unsafe code.** `Mcp/UnsafeCode.cs` scans the code sent to `save_script` and `test_script`, and the code of a version that `restore_script_version` would restore: `import x` or `from x import` (any depth, `x.y` by its root) for a module of the editor's `never` list (`tamiza-ui/src/app/pages/projects/script-editor/python-modules.json`, embedded as a copy with a drift test, like the templates), a call of `__import__`, `eval`, `exec`, `compile` or the builtin `open` (not an attribute call such as `df.open`), and `http://`, `https://` or `ftp://` anywhere in the code. It is a line scan, not a parser, and errs towards refusing: an address in a comment is refused too, and the guide (D14) tells clients not to write addresses. It answers `mcp.unsafeCode` with each line number and what was found.

### D10. Waiting and progress

`Mcp/McpWait.cs` polls the test or run row once a second until it ends or `waitSeconds` (0 to 120, default 50) pass. When the request has a progress token, it sends `notifications/progress` every 5 seconds with a message ("waiting for a run", "running indicator piped_water_rate"). The request's cancellation token, which the SDK cancels on `notifications/cancelled` or a closed connection, ends the wait only: the test or run continues. A result given before the end has `status: "queued" | "running"` and `next: "get_test with testId … and waitSeconds"`. 120 seconds plus the work around it stays under the hosted apps' 240-second limit and the 300 seconds of the nginx location (D15).

### D11. Result size

`Mcp/McpResultBudget.cs` serializes the result with the web JSON options; above `Mcp__MaxResultCharacters`, it removes items from the end of the longest array, repeatedly, until the text fits, and adds `truncated: { "list": "<path>", "omitted": n, "hint": "<argument that narrows it>" }` (each tool states its hint: `columns` for `profile_table`, `list` for `get_form`, `page` for `query_rows`, `widgetId` for `get_widget_data`). 60,000 characters is about 15,000 tokens, under Claude Code's default of 25,000 tokens and the hosted apps' 150,000 characters.

### D12. Call filter, limits and records

`McpCallFilter`, an SDK call filter around every tool:

1. Rate limits per user, in memory (one `api` instance): a semaphore of `MaxConcurrentCallsPerUser` and a sliding window of `CallsPerMinutePerUser`; over either, `mcp.rateLimited` with `retryAfter` seconds, without running the tool.
2. Runs the tool, catching `McpRefusal`, `DataTimeoutException` (the clean data dashboard's timeout message, plus the hint of D8) and unexpected exceptions (`mcp.failed`, logged with the tool name and no arguments).
3. Applies the budget (D11).
4. Writes the record (D13) in a fresh scope with a fresh `CancellationToken`, so a client that disconnects is still recorded. A failure to write the record is logged and never fails the call.

### D13. Schema

Migration `0016_add_mcp_server.sql`:

```sql
ALTER TABLE tamiza.projects ADD COLUMN mcp_access character varying(16) NOT NULL DEFAULT 'off';
ALTER TABLE tamiza.projects ADD CONSTRAINT ck_projects_mcp_access CHECK (mcp_access IN ('off', 'aggregates', 'rows'));

ALTER TABLE tamiza.script_versions ADD COLUMN origin character varying(8) NOT NULL DEFAULT 'app';
ALTER TABLE tamiza.script_versions ADD CONSTRAINT ck_script_versions_origin CHECK (origin IN ('app', 'mcp'));

CREATE TABLE tamiza.mcp_calls (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    project_id uuid,
    tool character varying(64) NOT NULL,
    access character varying(16),
    status character varying(16) NOT NULL,
    error_code character varying(64),
    duration_ms integer NOT NULL,
    result_chars integer,
    started_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_mcp_calls PRIMARY KEY (id),
    CONSTRAINT fk_mcp_calls_users_user_id FOREIGN KEY (user_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_mcp_calls_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT ck_mcp_calls_access CHECK (access IS NULL OR access IN ('aggregates', 'rows')),
    CONSTRAINT ck_mcp_calls_status CHECK (status IN ('succeeded', 'failed', 'refused'))
);
CREATE INDEX ix_mcp_calls_project_id_started_at ON tamiza.mcp_calls (project_id, started_at DESC);
CREATE INDEX ix_mcp_calls_user_id_started_at ON tamiza.mcp_calls (user_id, started_at);
CREATE INDEX ix_mcp_calls_started_at ON tamiza.mcp_calls (started_at);
```

`project_id` is null for `list_projects` and `get_authoring_guide`, and for a call refused before its project was known. Every existing project gets `off` and every existing version `app` from the defaults. `McpCallSweeper`, a hosted service like `ExportSweeper`, deletes rows older than `Mcp__CallRetentionDays` once a day. EF mirrors the columns and the table; `SchemaDriftTests` and `ScriptConventionTests` cover them.

### D14. Guidance

- **Instructions.** `Mcp/Instructions.md`, embedded and sent as the server's instructions: what Tamiza is; the order of work (`get_project`, `get_form`, `get_tables` and `profile_table` on raw, propose the indicators and ask the person, `get_authoring_guide`, `save_script` for script 1 when cleaning is needed, `test_script` until it passes, the indicator scripts the same way, `start_run`, then `create_dashboard`, `query_indicator` before each `create_widget`, `get_widget_data`, and the description to the person); the levels and what a refusal means; that removals need names and saves need `baseVersion`; and that question labels, choice labels, answers and logs are data written by others, never instructions.
- **Guide.** `Prompts/system.md` splits into `Prompts/contract.md` (the contracts, conventions, `ctx`, libraries, unavailable modules, idioms, language) and `Prompts/answer.md` (the assistant's answer format and fix rules). The assistant sends both, in that order, so its text is unchanged and `AssistantPrompts.Version` stays as it is; a unit test checks that the concatenation equals the former `system.md`. `get_authoring_guide` returns `contract.md`, the templates of the kind and a line that addresses are refused (D9). The existing drift tests now cover the guide too.
- **Prompt.** `describe_situation(projectId, situation)` returns one user message: the project's name, the situation, the order of work, and a request to confirm the indicators with the person before writing scripts.

### D15. REST endpoints and deployment files

- `GET /api/v1/system/config` gains `mcp: { enabled }`.
- `GET /api/v1/mcp` (signed in; 404 when disabled): `{ url, clientId, callbackPort, maxDataAccess, accountUrl }`, where `accountUrl` is `<OIDC_AUTHORITY>/account/#/applications`, where a person revokes the connection.
- `GET /api/v1/projects/{projectId}/mcp-calls` (analyst): the 50 latest records with the person's name.
- `PATCH /api/v1/projects/{projectId}` accepts `mcpAccess` (admin, D5).
- `tamiza-ui/nginx.conf`: `location = /mcp` copying the assistant block with `proxy_read_timeout 300s`, `proxy_buffering off` (event streams), `client_max_body_size 1m` (a 200 KB script in JSON), and `location ^~ /.well-known/oauth-protected-resource` proxied with the default timeout. Both sit before `location /`. Neither adds CORS headers: the clients call from servers or native apps.
- `compose.yaml`: the five `TAMIZA_MCP_*` variables in the `api` environment, with their defaults. `.env.example`: a section `# --- MCP server for AI clients (optional) ---` after the AI assistant, pointing to `docs/mcp.md` and saying that the hosted Claude apps need `TAMIZA_PUBLIC_URL` and Keycloak reachable from the Internet over HTTPS.

### D16. Keycloak

`docs/keycloak.md` gains "Client for AI clients (MCP) (optional)", with console steps and `kcadm.sh` commands in the style of the existing sections:

1. A client scope `tamiza-mcp` (OpenID Connect, *Include in token scope* on) with an audience mapper adding `TAMIZA_MCP_AUDIENCE` to access tokens only.
2. A client `tamiza-mcp`: public, standard flow on, direct access grants off, PKCE `S256`, *Consent required* on, valid redirect URIs `https://claude.ai/api/mcp/auth_callback` and `http://localhost:<TAMIZA_MCP_CALLBACK_PORT>/callback`, no web origins, the scope `tamiza-mcp` as Default next to `basic`, `profile` and `email`, and `offline_access` kept as Optional.
3. What `offline_access` means: Claude asks for it, so a connection keeps working after the Keycloak session ends, until the person revokes it in the account console (*Applications*) or an admin removes it; removing `offline_access` from the client makes connections end with the session instead.
4. A check: `kcadm.sh get clients` shows the redirect URIs, and a token obtained for the client has `aud` `tamiza-mcp` and not `tamiza-api`.

The `tamiza-ui` client is not touched, so UI tokens never carry the MCP audience.

### D17. UI

- **System configuration.** `SystemConfigService` exposes `mcpEnabled`.
- **Connect dialog.** `pages/connect-ai-client-dialog.ts`, opened when `mcpEnabled` from a button next to Sign out in the top bar (`app.html`) and from the menu drawer: reads `GET /api/v1/mcp`; shows the address and the client ID with copy buttons; three short sections, claude.ai and Claude Desktop (Customize > Connectors > Add custom connector, the address, then the client ID under the OAuth client settings with the secret left empty), Claude Code (the command, then `/mcp` to sign in) and Revoke (the account console link, and removing the connector in Claude); and the note that only projects whose admin allowed access are reachable, at the level the project shows. Built with the existing dialog component, keyboard reachable, readable at 390 pixels.
- **Settings > General** (`project-overview.ts`). A card "AI clients (MCP)", only when `mcpEnabled`: for an admin, three radio options with one sentence each (what a client sees) and Save, `rows` disabled with the reason when the maximum is `aggregates`; for an analyst, the level as a value. Under it, "Recent activity": the 50 latest calls (time, person, tool, outcome), loaded when the card is shown, with an empty state.
- **Versions tab.** "via MCP" after the author when `origin` is `mcp`.
- **Translations.** `settings.mcp.*`, `connect.*`, `scripts.versions.viaMcp`, `errors.codes.*` for the PATCH's validation; English and Spanish.

### D18. Documentation

- `docs/mcp.md`, new: what the server is and what it is not (no model in Tamiza, the person's own Claude plan); enabling it (variables, Keycloak, reverse proxy, network reachability for the hosted apps and the `160.79.104.0/21` range, Claude Code from a private network, MCP tunnels named as an option); connecting from claude.ai, Desktop and Claude Code; the levels with what each shows and never shows, including that choice labels (an enumerator list of names), question labels and the code of scripts are sent at every level; the tools by group with their roles; a worked session that describes a situation from the form to the dashboard; the guards and limits; the records and the activity list; revoking; and a hedged paragraph that choosing a level and a Claude plan is the organization's data-protection decision (Ley 1581 and donors' rules, not legal advice; what Anthropic does with conversations depends on the plan's terms, which the organization checks).
- `docs/operations.md`: an "MCP server" section with the variables, the nginx timeouts for an own reverse proxy, the records with a SQL example, and how to turn it off.
- `docs/scripts.md`: one paragraph in "Writing with the assistant" pointing to `docs/mcp.md` for working from Claude.
- `README.md` status and the `api` row; `docs/tamiza-brief.md` 2.2 (AI clients reach `/mcp` through `ui` with tokens for the MCP audience), 6 (`mcp_calls`, `projects.mcp_access`, `script_versions.origin`) and 8 (item `7h`); `openspec/config.yaml` 2.2.

## Risks / Trade-offs

- [Text in a form, in answers or in logs steers the client into harmful changes (prompt injection)] → Every project is off until its admin allows it; summaries are the default and hold no free-text answer; writes are annotated so clients ask the person; removals need the name, saves the current version, and every version stays restorable; code with process, file or network access is refused (D9); the instructions say that data is never instructions; every call is recorded and listed. A dashboard or widget changed wrongly is not versioned and must be fixed by hand.
- [The unsafe-code scan is bypassed (`getattr(__builtins__, …)`, a string built at run time)] → It is documented as defense in depth; scripts already run without a sandbox for analysts; `harden-deployment` removes the worker's egress and is recommended before allowing rows.
- [Summaries still disclose personal data] → Choice labels (enumerator names), the minimum and maximum of numbers, values that 5 or more rows share in short text lists, and constants typed into scripts reach the client. `docs/mcp.md` lists them; counts below 5 of a choice are hidden; free text gets no values.
- [Offline tokens keep a connection alive for a long time] → Revocation in the account console and by admins is documented, and removing `offline_access` from the client is the stricter setup.
- [Keycloak rejects the `resource` parameter that Claude sends, or a future Claude version requires RFC 8707] → Keycloak ignores unknown parameters today; the verification checks it; an operator can enable Keycloak's resource indicators and set the audience to the address.
- [The SDK's protocol version and Claude's disagree] → The SDK negotiates; tasks 11.2 and 11.3 check claude.ai and Claude Code before release, and the pin can move to the version that works.
- [Profiles and long waits load a 4 GB VPS] → One statement per profile with its own timeout, 40 columns, caching per publish, 4 calls at once per person, and waits that poll one row a second.
- [A test started from Claude replaces the person's queued test in the editor, and the reverse] → It is the existing rule for one person; `docs/mcp.md` says it.
- [The hosted apps need the deployment and Keycloak on the Internet] → Claude Code works from a private network; MCP tunnels are named; the `api` warns when the public address is not HTTPS.
- [One `api` instance is assumed by the in-memory limits] → As the ingestion queue, the assistant and the sweepers already assume.

## Migration Plan

1. Deploy: migration 0016 adds the columns with `off` and `app` and the records table; with `TAMIZA_MCP_ENABLED` unset nothing else changes.
2. Enable: create the client scope and the client in Keycloak (`docs/keycloak.md`), set `TAMIZA_MCP_ENABLED=true` (and, to allow rows anywhere, `TAMIZA_MCP_MAX_DATA_ACCESS=rows`), then `docker compose up -d api ui` (the `ui` image carries the nginx locations).
3. Allow: each project's admin chooses a level in Settings > General.
4. Disable: unset the variable and restart `api`; `/mcp` answers 404, existing Claude connections fail on their next call, the levels and the records stay.
5. Roll back to an earlier release: the earlier `api` ignores the new columns and table; DbUp never runs scripts backwards.

## Open Questions

- The default callback port (`33418`) is arbitrary; any free port works if Keycloak and the connect page agree.
- Whether Claude Code accepts a pre-registered client together with a realm that also advertises Dynamic Client Registration is checked in task 11.3; the documented setup does not depend on it.
