# Proposal

## Why

Analysts are about to write their first real cleaning, output and indicator scripts, and most of the time goes into the same loop: describe what the script must do, write pandas that respects Tamiza's conventions (labels not codes, `"; "` in select_multiple answers, nullable `Int64`, UTC timestamps, `numerator`/`denominator`, percent as a fraction), test it on a sample, read the error and fix it. The editor already knows everything an AI model needs to help with that loop (the input tables and columns, the choice lists, the contract, the templates, the last test's result), so a model can draft and fix scripts that follow those conventions without seeing respondents' data. The product brief lists an AI assistant as out of scope for the MVP; the core of the MVP is now in place and the user decided to build the assistant before `add-audit-log` and `harden-deployment`.

## What Changes

- **Assistant tab** in the Scripts side panel, next to Tables, Reference and Versions, for analysts and admins when the deployment has configured a model. Three actions: **Draft** a whole script from a request (for an indicator, also from its label, description, unit and format), **Rewrite this cell** (the `# %%` cell holding the cursor) and **Fix** after a failed sample test or a warning of the indicator's dashboard view, started from buttons in the test result. The tab shows the explanation; the proposed code goes into the editor only when the analyst applies it, as one change that can be undone, and is never saved or tested by the assistant.
- **What the model sees**, assembled by the API: the rules of the script contract, the templates, the tables and columns the script receives with their types and question labels, the form's choice lists, the project's time zone and enumerator question, the saved script 1, the indicator's details, the draft, the conversation's earlier explanations and, from the analyst's own last test, its shape (tables, columns, row counts, `ctx.show` names) and its error and log, reduced to the script's frames and to lines with a log level, with every word or number that is not a known name, a word of the draft or a word of library messages replaced by `…`. **Never** rows of the sample, of `ctx.show` or of reference tables, values of the dashboard view, nor printed output. A "Show what will be sent" view displays the exact text, and "Copy context" puts it on the clipboard for use elsewhere.
- **Any OpenAI-compatible provider**, configured by the operator with environment variables read only by the API (base URL, model, key, extra request fields, timeout, a daily token budget per user, a context size limit). Changing provider means changing those variables. Without them the assistant does not exist: no tab, no endpoint answers, nothing leaves the server.
- **Import check** in the editor, with or without a configured model: a warning on imports outside the libraries the worker has and the standard library, and on modules a script never needs (`os`, `subprocess`, `socket`, …). It also runs on every proposal before it is offered.
- **Usage records**: one row per request with who, which script, the model, token counts, duration, outcome and the size of each part of the context, but no message or reply text. The daily budget and a limit of one request at a time per user are enforced from these rows.
- The conversation lives only in the browser tab's session, per user, project and script, and is cleared on sign-out or with "New conversation". A request keeps going while the analyst looks at another side tab or script, and its answer stays with the script it was asked for.
- `docs/operations.md` gains an "AI assistant" section (variables, example configurations for several providers, what is sent, the provider choice as the operator's data-protection decision); `docs/scripts.md` gains "Writing with the assistant" and "What the assistant can see". `openspec/config.yaml` and `docs/tamiza-brief.md` stop listing the AI assistant as out of scope.

No breaking changes: without the new variables Tamiza behaves as today, apart from the new import warnings.

## Capabilities

### New Capabilities
- `script-assistant`: the AI assistant of the Scripts section: when it is available and to whom, the requests it answers (draft, rewrite a cell, fix), what is sent to the model and what never is, how proposals reach the editor, the transparency views, usage limits and records, and how failures of the provider are reported.

### Modified Capabilities
- `script-editor`: a new check marks imports of modules the worker does not have and of modules scripts never need.

## Impact

- **tamiza-api**: a new `Assistant` area (options bound to `TAMIZA_ASSISTANT_*`, a named `HttpClient` speaking chat completions, the context assembler with masking and trimming, embedded prompt files and template copies), `GET /api/v1/projects/{projectId}/assistant`, `POST …/assistant/context`, `POST …/assistant/completions` and `PUT …/assistant/calls/{callId}/outcome` for analysts, `assistant.enabled` in `GET /api/v1/system/config`, the catalog read of `ScriptInputEndpoints` shared with the assembler, and a DbUp migration for `tamiza.assistant_calls`.
- **tamiza-ui**: the Assistant tab in the script side panel, buttons in the test result, a range replacement in the code editor, the import check in the script analysis, an assistant service, session storage cleared on sign-out, translations; `nginx.conf` gains a location with a longer read timeout for the assistant routes.
- **tamiza-analytics**: no change to the worker; a tool that generates the masking vocabulary from the pinned libraries' messages, and a test that keeps the editor's module lists in line with the worker.
- **Deployment**: `compose.yaml` passes the new variables to `api` only; `.env.example` documents them (and the missing `PROXY_NETWORK`); `.gitignore` ignores new env files. The `api` container makes outbound HTTPS requests to the configured provider when the feature is enabled.
- **Docs and planning**: `docs/operations.md` (including how to check a provider with a fixed set of requests), `docs/scripts.md`, `README.md`, `docs/tamiza-brief.md` (sections 2.2, 4, 6 and 8), `openspec/config.yaml`.
- **Out of scope, for later changes**: column profiles computed by the worker, streaming answers, an in-editor diff, suggesting an indicator's details, review of a draft, fixing a failed run, an automatic test-and-fix loop, sending sample rows, a per-project switch, an assistant for viewers, stored conversations, suggesting indicators from the form.
