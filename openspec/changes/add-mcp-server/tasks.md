# Tasks

**Order.** Groups 1 to 9 (api) come first, in order; each ends with its tests green. Group 10 (ui) follows. Group 11 (deployment and docs) can follow each group, and group 12 (verification) is last.

**How to run the tests:** `dotnet test` in `tamiza-api/` (integration tests start PostgreSQL with Testcontainers; one run at a time). `npm test -- --watch=false` in `tamiza-ui/`, where the two "stops polling when the page is destroyed" tests (`project-data.spec.ts`, `project-scripts.spec.ts`) are known failures. MCP integration tests use the SDK's own client (`McpClient` over `HttpClientTransport`) against `TamizaApiFactory`'s test server.

## 1. Schema (api)

- [x] 1.1 Add `tamiza-api/src/dbUp/Scripts/0016_add_mcp_server.sql` exactly as design D13: `projects.mcp_access` with `ck_projects_mcp_access`, `script_versions.origin` with `ck_script_versions_origin`, and `tamiza.mcp_calls` with `pk_mcp_calls`, `fk_mcp_calls_users_user_id` (restrict), `fk_mcp_calls_projects_project_id` (cascade), `ck_mcp_calls_access`, `ck_mcp_calls_status` and the three indexes. Keep upper-case `CHECK`, `FOREIGN KEY` and `PRIMARY KEY` out of the comments. Verify the analytics integration tests still build their template database.
- [x] 1.2 Mirror it in EF: `Project.McpAccess`, `ScriptVersion.Origin`, an `McpCall` entity with `DbSet<McpCall> McpCalls`, the checks, FKs and indexes. Verify `SchemaDriftTests` passes.
- [x] 1.3 Add `The_mcp_server_script_names_every_constraint_and_index` to `ScriptConventionTests`, in the style of the 0011 and 0012 tests. Verify it passes, and fails when a constraint name is removed from a copy of the script.
- [x] 1.4 Add a `SchemaMigratorTests` upgrade test from `0015`: two projects with script versions, then `MigrateAsync`. Verify every project is `off`, every version is `app`, `rows` and `app` are accepted and `everything` and `bot` are refused by the checks, and removing a project removes its `mcp_calls` (projects `Existing project upgraded`).

## 2. Options and the server's existence (api)

- [ ] 2.1 Add `Mcp/McpOptions.cs` and its validator (design D1). Verify `McpOptionsTests`: defaults; binding of every flat and `Mcp__*` key; start refused for an unknown data access value, an audience equal to `OIDC_AUDIENCE` and each number out of range, with the variable named (`Malformed setting`); the Warning for a non-HTTPS public address other than `localhost`.
- [ ] 2.2 Add `ModelContextProtocol.AspNetCore` pinned with a comment in the style of the `MailKit` pin (design D2; re-check that the pinned version is the newest release at least two weeks old). Add `AddTamizaMcp` and `MapTamizaMcp`, registering and mapping nothing while disabled, stateless HTTP transport, server info and instructions. Verify `McpAvailabilityTests`: disabled, `/mcp` and `/.well-known/oauth-protected-resource/mcp` answer 404 and `system/config` gives `mcp.enabled: false` (`Not enabled`); enabled, an unauthenticated `initialize` answers 401 (`Enabled`).

## 3. Authentication (api)

- [ ] 3.1 Add `Mcp/McpAuthentication.cs` (design D3): the `Mcp` JWT scheme from `OidcOptions` with the MCP audience, the policy scheme that selects it for `/mcp`, the SDK's `AddMcp` challenge with the resource metadata built from `TAMIZA_PUBLIC_URL`, and the policy on `MapMcp`. Make `TestTokens.TrustTestKey` apply to both schemes. Verify `McpAuthenticationTests`: `First request of a client` (header and metadata document, including behind a request whose `Host` differs from the public address); `Token of the UI`; `Token of an AI client used on the REST API` (`GET /api/v1/projects` and one endpoint per area); `Expired token`; a token without `sub` refused; a token from another issuer refused; the person of an MCP token is the same local user as their UI token (`Person signs in`), with name and email refreshed; a superadmin email in an MCP token makes the person a superadmin.
- [ ] 3.2 Add the `Origin` filter. Verify `Page of another site` (403), the public origin accepted, and no `Origin` accepted.

## 4. Access, results, limits and records (api)

- [ ] 4.1 Add `Mcp/McpAccess.cs` and `McpRefusal` (design D4, D5). Verify unit tests over every role (none, viewer, analyst, admin, superadmin without membership) × level (`off`, `aggregates`, `rows`) × maximum (`aggregates`, `rows`) × needs (summaries, rows), with the codes `mcp.projectNotFound`, `mcp.forbidden`, `mcp.projectOff` and `mcp.rowsNotAllowed` (`Project off`, `Deployment maximum lower than the project`).
- [ ] 4.2 Add `Mcp/McpResults.cs` translating `Ok`, `Created`, `Accepted`, `ValidationProblem`, `ProblemHttpResult` (with and without a `code` extension) and `NotFound` (design D6). Verify unit tests for each.
- [ ] 4.3 Add `Mcp/McpResultBudget.cs` (design D11). Verify unit tests: a result under the size is unchanged; a result over it is cut from the end of its longest list, repeatedly, and carries `truncated` with the list, the count and the tool's hint; the size setting is honored (`Large form`).
- [ ] 4.4 Add `McpCallFilter`, the in-memory limits and the ledger (design D12, D13). Verify `McpCallRecordTests`: a succeeded, a failed and a refused call each write one row with tool, access, status, code, duration and result size, and no argument or result text (`Script saved by Claude`, `Refused call`); a client that disconnects mid-call is still recorded; a failing record write does not fail the call; `Burst of calls` (130 calls, the last 10 refused with `retryAfter` and not run); a fifth concurrent call refused.
- [ ] 4.5 Add `McpCallSweeper`. Verify that rows older than the retention are deleted and newer rows kept.

## 5. Reading the Kobo data (api)

- [ ] 5.1 `list_projects` and `get_project` (design D6). Verify: the person's projects with role and effective level, projects that are off listed with `available: false`; a viewer's `get_project` without scripts and configuration; an analyst's with submissions, last sync, published tables, scripts, indicators, dashboards and last run.
- [ ] 5.2 `get_form`, with the `list` argument. Verify `Form and choices`, the form versions with their submission counts, and `Large form` (a form whose choice lists exceed the size, then one list read in full).
- [ ] 5.3 Generalize `ScriptInputsReader` to read any source and add `get_tables`. Verify for each source the tables, columns, types, labels, select lists and row counts; a viewer refused on `raw` and `reference` and served on `clean`, `output` and `indicators`; a project never run says so.
- [ ] 5.4 Add `CleanData/ColumnProfiler.cs` and `profile_table` (design D8). Verify `ColumnProfilerTests` on published tables: `Select question`, a select_multiple split on `"; "`, `Free text`, `Short list`, `Numbers with a sentinel`, dates and booleans, empty values counting blank text, `Wide table`, unknown column names as field errors, the cache reused for the same publish and refreshed after a republish, and the timeout stopping a slow statement with the message and hint. Measure a profile of 40 columns over 500,000 rows on the reference hardware and record the time in the design.
- [ ] 5.5 `get_fieldwork` from the performance dashboard. Verify it equals `GET /performance` for the same range.
- [ ] 5.6 Widen `DataEndpoints.QueryAsync`'s source to the raw and indicator schemas for internal callers only, read reference rows from `reference_table_contents`, and add `query_rows`. Verify `Rows refused`, `Filtered rows`, at most 100 rows, text cut at 500 characters, the roles per source, and that the REST routes still refuse `raw` and `indicators`.

## 6. Scripts (api)

- [ ] 6.1 Split `Assistant/Prompts/system.md` into `contract.md` and `answer.md` (design D14). Verify a unit test that their concatenation equals the former text, that `AssistantPrompts.System` is unchanged, and that the existing drift tests cover `contract.md`.
- [ ] 6.2 Add `Mcp/Instructions.md`, `get_authoring_guide` and the `describe_situation` prompt. Verify `Guide and editor agree` (the guide's templates equal the editor's), the guide holds `contract.md` and the address rule, the instructions state the order of work and that data is never instructions, and `Prompt`.
- [ ] 6.3 `list_scripts`, `get_script`, `list_script_versions`, with `origin` added to the version records of the REST API. Verify the lists, a removed script, a given version's code, and a viewer refused (`Viewer tries to save a script` for reads too).
- [ ] 6.4 `AddVersionAsync` takes the origin; add `save_script`, `remove_script` and `restore_script_version` with `baseVersion` (design D9). Verify `Member saved meanwhile`, `New indicator`, a restore and a removal against a stale `baseVersion`, the endpoint's own validation passed through (a 201 KB script, an invalid indicator name, an unknown format), and the indicator limit.
- [ ] 6.5 Add `Mcp/UnsafeCode.cs` and a copy of `python-modules.json` with a drift test (design D9). Verify unit tests: each `never` module in `import x`, `import x.y`, `from x import y` and `from x.y import z`; `__import__`, `eval(`, `exec(`, `compile(`, builtin `open(` refused and `df.open(`, `evaluate(` and `reopen(` accepted; `http://`, `https://` and `ftp://` refused in code, strings and comments; the lines reported. Verify integration tests `Script that sends data away` through `save_script`, `test_script` and the restore of an unsafe version, and `Same code in the editor` through the REST API.

## 7. Tests and runs (api)

- [ ] 7.1 Add `Mcp/McpTestView.cs` (design D7). Verify canary unit tests: a test whose previews, `ctx.show` table and text, printed lines and error hold seeded values gives, at `aggregates`, the shapes, the masked error (`Masked error`) and leveled log lines and none of the seeded values, with the `withheld` note (`Test preview withheld`); at `rows`, 20 preview rows by default and up to 100 when asked, and the `ctx.show` values (`Test preview with rows allowed`); the indicator view's totals, warnings and at most 50 groups per chart at both levels.
- [ ] 7.2 Add `Mcp/McpWait.cs` (design D10) and `test_script`, `get_test`. Verify `Test of a new indicator` against the test worker harness used by the pipeline integration tests, `Test replaced`, another member's test not found, a wait of 0 answering at once, a result before the end giving `status` and `next`, progress notifications when a progress token is sent, and a cancelled call leaving the test running.
- [ ] 7.3 `start_run`, `get_run` (latest when no id). Verify `Run after saving`, `Long run` (with a fake clock or a short wait), a run already queued refused with `run.already_queued`, and masked step errors at `aggregates`.

## 8. Indicators and dashboards (api)

- [ ] 8.1 `list_indicators`, `get_indicator_columns`, `query_indicator`. Verify `Checking a widget before saving it`, the row limit reported, and an unpublished indicator.
- [ ] 8.2 Extend `ReportDataBuilder` to take filter values, and add `get_widget_data`. Verify `Widget values`, `Stale widget`, one widget or all, defaults and given filter values, and that the dashboard emails still render as before (the existing report tests).
- [ ] 8.3 `list_dashboards`, `get_dashboard`, `create_dashboard`, `update_dashboard` (rename, position, filters), `duplicate_dashboard`, `remove_dashboard`, `create_widget`, `update_widget`, `remove_widget`, with the handlers made `internal` (design D6, D9). Verify `Dashboard for a situation`, `Move to a position`, `Dashboard limit`, `Group column required`, `Wrong dashboard` and the same for a widget title, `Author of a widget`, and a widget moved to another dashboard.
- [ ] 8.4 Add `McpRoleMatrixTests`: every tool for viewer, analyst, admin, superadmin and outsider, on a project at each level, in the style of `IndicatorRoleMatrixTests`, plus a check that every tool appears in the matrix. Verify `Viewer reads a dashboard`, `Viewer tries to save a script` and `Not a member`.
- [ ] 8.5 Add `McpToolCatalogTests`: the tool list equals the spec's table, every tool has a description, an input schema and the annotations of design D6 (`Tool list`, `Removal marked`), and every tool name appears in `docs/mcp.md`.

## 9. REST endpoints and the project setting (api)

- [ ] 9.1 `system/config` gains `mcp.enabled`; `GET /api/v1/mcp` (design D15). Verify `SystemConfigTests` expects the key, and `GET /api/v1/mcp` answers 404 when disabled and the address, client ID, port, maximum and account URL when enabled.
- [ ] 9.2 `PATCH /projects/{id}` accepts `mcpAccess`; `ProjectDetails` gains `mcpAccess` and `mcpMaxAccess`. Verify in `ProjectsApiTests`: `New project`, `Admin allows summaries`, `Rows above the deployment's maximum`, `Analyst tries to change it`, `Viewer details`.
- [ ] 9.3 `GET /projects/{id}/mcp-calls`. Verify `Analyst checks what Claude did` (API part: 50 newest first, with names) and `Viewer` (403).
- [ ] 9.4 Add `The_openapi_document_describes_the_mcp_endpoints` (the two new paths and the new fields). Verify the full `dotnet test` run is green.

## 10. UI

- [ ] 10.1 `SystemConfigService.mcpEnabled`, an `McpService` (`connection`, `calls`), and `mcpAccess` in the project model and its update. Verify the services' specs.
- [ ] 10.2 Add `pages/connect-ai-client-dialog.ts` and its buttons in the top bar and the menu drawer (design D17). Verify a spec: the address, client ID and command for the defaults (`Claude Code command`), copy buttons, the steps and the revoke link, nothing offered when disabled (`Disabled`), and no horizontal scroll at 390 pixels.
- [ ] 10.3 Add the "AI clients (MCP)" card and "Recent activity" to `project-overview.ts`. Verify a spec: the admin's radio options and Save, `rows` disabled with its reason under an `aggregates` maximum, the analyst's read-only value, the stored `rows` limited by the deployment, the activity list and its empty state (`Analyst checks what Claude did`, UI part), and no card when disabled.
- [ ] 10.4 Show "via MCP" in the Versions tab. Verify `Version written by Claude` and `Restored in the editor`.
- [ ] 10.5 Add the translations in English and Spanish, and the new error codes to `error-codes.spec.ts`. Verify the translation key checks pass.
- [ ] 10.6 Add the two nginx locations (design D15) and extend `nginx-config.spec.ts`. Verify that `/mcp` and `/.well-known/oauth-protected-resource/mcp` reach the `api`, with a 300-second read timeout and no buffering for `/mcp`, and that `/` still serves the app.

## 11. Deployment and documentation

- [ ] 11.1 `compose.yaml` and `.env.example` (design D15). Verify `scripts/check-env-example.sh` passes and `docker compose config` shows the variables for `api` only.
- [ ] 11.2 `docs/keycloak.md` "Client for AI clients (MCP) (optional)" (design D16), with console steps and `kcadm.sh` commands. Verify the commands against a Keycloak 26 development container and that a token for the client has `aud` `tamiza-mcp` and not `tamiza-api`.
- [ ] 11.3 `docs/mcp.md` (design D18), `docs/operations.md` "MCP server", the paragraph in `docs/scripts.md`, `README.md`, `docs/tamiza-brief.md` (2.2, 6, 8) and `openspec/config.yaml` (2.2). Verify the links and that every tool is named in `docs/mcp.md` (task 8.5).

## 12. Verification

- [ ] 12.1 Real stack in a separate Compose project with a Keycloak 26 development realm configured from `docs/keycloak.md` (the user's own stack and Keycloak untouched). Check: sign-in from the MCP Inspector with the public client; the 401 and metadata; UI tokens refused at `/mcp` and MCP tokens refused at `/api/v1`; the levels per project; rate limits; records and the activity list; the connect dialog and the General card at 390 pixels.
- [ ] 12.2 Claude Code with `claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 …`: sign-in, the tool list, protocol version negotiation with the pinned SDK, a test that waits and sends progress, and a result cut by the budget. Note whether a realm that also advertises Dynamic Client Registration changes anything (Open Questions).
- [ ] 12.3 claude.ai with a custom connector and the client ID, against the verification stack published over HTTPS through a tunnel: sign-in through the `https://claude.ai/api/mcp/auth_callback` redirect, the `resource` parameter accepted by Keycloak, offline access, and revocation from the account console.
- [ ] 12.4 A session that describes a situation, on a project with a select_one by municipality, a select_multiple, a `members` repeat and dates, at *Summaries only*: from "describe the access to water by municipality" to a dashboard with a filter and at least three widgets, with every script tested before saving. Record the calls (from `mcp_calls`), what was refused and why, and whether any value that summaries must withhold appeared in the conversation (it must not). Write the results in a "Verification Results" section of this design.
