# Design

## Context

See proposal.md for the motivation and specs/ for the required behavior. The pieces this change builds on:

- **Outbound HTTP from the api.** `Kobo/KoboHttp.cs` registers named `HttpClient`s on a `SocketsHttpHandler` with `AllowAutoRedirect = false`, `UseProxy = false`, `RedactLoggedHeaders(_ => true)`, a timeout and a response size cap; `KoboClient.SendAsync` puts the token only in the `Authorization` header, never logs bodies and maps outcomes to codes. Kobo's `ConnectCallback` refuses private addresses unless `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS`; that flag is Kobo's only. The api already reaches the Docker host as `host.docker.internal` (`compose.yaml`, `extra_hosts`).
- **Options.** Flat environment variables bind through `[ConfigurationKeyName("TAMIZA_…")]` (`Configuration/TamizaOptions.cs`, `OptionsRegistration.cs`, `ValidateDataAnnotations().ValidateOnStart()`); Compose always passes optional strings, often empty, so blank means absent (`OidcOptions`).
- **What a script receives.** `Pipeline/ScriptInputEndpoints.cs` reads the published raw or clean schema from PostgreSQL's catalog (`TableCatalog.FindAllAsync` inside `DataDashboard.InTransactionAsync`, row counts from `DataDashboard.LatestStepAsync`), the reference tables' columns and `Project.FormFields`. `Project` also holds `FormChoices` (`FormChoice(ListName, Name, Label)`), `TimeZone` (IANA, default `UTC`) and `EnumeratorField` (a question path or null). Saved scripts and indicator details are `ScriptVersion` rows (`Code` null means removed).
- **Tests.** `tamiza.script_tests` rows are visible only to their requester for 24 hours (`ScriptTestEndpoints.GetAsync`: `RequestedBy == user.Id && (FinishedAt == null || FinishedAt > now - Lifetime)`). A step's `Error` is one string: the exception's last line, then a blank line and a traceback filtered to `script.py` frames (`runner._script_traceback`), or a single message for contract violations, limits and worker failures. `Log` is the child's stdout and stderr: `ctx.log` lines start with `INFO `/`WARNING `/`ERROR `/`CRITICAL `, but `print()` can write anything, including lines that look like log lines, and a worker failure appends the worker's own traceback without level prefixes. Previews hold up to 100 real rows; `ShownPreview.Text` is a `repr()`; `IndicatorTestView` carries totals, maxima and chart groups that are data values. Output column names can be data values (a pivot). The dashboard view's warnings exist only in the UI (`indicator-view.ts` `viewWarnings`, computed from the details being edited).
- **Editor.** `CodeEditor.replace(text)` and `insertAtCursor(text)` dispatch one transaction each; `cursorLine()` exists but the page never reads it. `script-analysis.ts` has `cells()`, `cellAt()`, `check()` and the private `contractFunction()`; `script-assist.ts` `lintProblems(state, options)` runs the checks on any `EditorState`. The side panel (`script-side-panel.ts`) has a static `tabs` array and renders Versions as the `@default` branch. `ScriptTestPanel` polls its test privately and exposes no outputs; the page's `test()` stays the queued snapshot.
- **UI plumbing.** `/api/v1/system/config` is read only by the OIDC loader; no service exposes it to pages. `ApiErrors.parse` turns a Problem Details `code` into `errors.codes.<code>` and drops headers and other extensions. `AuthService.logout()` calls `oidc.logoff()`; the OIDC library keeps its state in sessionStorage, so the assistant's keys must be removed by prefix, never with `sessionStorage.clear()`.
- **Edges.** nginx's `/api/` location has the default 60-second read timeout; a regex location with a longer one is the precedent for exports. The api's Docker build context is `tamiza-api/` only, so files from `docs/` or `tamiza-ui/` cannot be read at build time; the unit tests already find `tamiza-ui/` by walking up from `AppContext.BaseDirectory` (`ErrorCodeTranslationTests`). `test_docs_examples.py` runs every ` ```python ` block of `docs/scripts.md` that defines a contract function, and the last such block per kind wins. `scripts/check-env-example.sh` fails today because `PROXY_NETWORK` (from `compose.proxy.yaml`) is not in `.env.example`.

## Goals / Non-Goals

**Goals:**
- The context is a pure function of the request and the database, assembled in one place, so "Show what will be sent" and the request cannot differ, and unit tests can pin both the text and the masking.
- Every privacy rule of the spec is enforced in the api, after the browser: the browser can narrow what is sent but never widen it.
- One wire format, no provider SDK, and nothing provider-specific in code: a deployer changes provider by changing variables.
- No change to the analytics worker, the test queue or the run pipeline.

**Non-Goals:**
- Tools or function calling, an agent loop, or any request the model can make to Tamiza.
- Streaming, an in-editor diff, worker-computed column profiles, a Review action, Fix from a failed run, sending rows: later changes.
- A per-project switch, per-project keys, viewer access, stored conversations.
- Measuring answer quality automatically: seven requests, written down in `docs/operations.md` so they can be run again with any provider, are run by hand (tasks 8.2 and 9.4).

## Decisions

### D1. Configuration

`Assistant/AssistantOptions.cs`, bound like `TamizaOptions` with flat keys:

| Variable | Type, default | Rule |
|---|---|---|
| `TAMIZA_ASSISTANT_BASE_URL` | string, empty | Absolute `http` or `https` URL; a trailing `/` is added. |
| `TAMIZA_ASSISTANT_MODEL` | string, empty | Sent as `model`. |
| `TAMIZA_ASSISTANT_API_KEY` | string, empty | Sent as `Authorization: Bearer`. |
| `TAMIZA_ASSISTANT_EXTRA_BODY` | string, empty | A JSON object merged into every request body; it may not set `model`, `messages` or `stream`. |
| `TAMIZA_ASSISTANT_TIMEOUT_SECONDS` | int, 120 | 10 to 240. |
| `TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER` | int, 500000 | 1,000 to 100,000,000. |
| `TAMIZA_ASSISTANT_MAX_CONTEXT_KB` | int, 128 | 64 to 1,024: below 64 KB the rules, one example and a draft can never fit. |

`Enabled` is true when the first three are all non-blank. They carry no `[Required]`, because a missing value turns the feature off instead of stopping the api. A custom `IValidateOptions<AssistantOptions>` with `ValidateOnStart` stops the api, naming the variable, for a malformed base URL, extra fields that are not a JSON object or set a reserved key, and numbers out of range: those are typos the operator must see at deploy time, as with every other setting. At startup, when one or two of the three are set, the api logs once at Warning which ones are missing. The key never appears in logs, Problem Details or `/system/config`.

*Alternatives:* a `Section__Key` options class (`Assistant__BaseUrl`) like the dashboards' tuning settings; rejected because these are primary deployment settings that operators set in `.env`, like `TAMIZA_KOBO_*`. A per-project key stored with Data Protection; rejected (non-goal): it would let a project admin route data to a vendor the operator did not choose.

### D2. Provider client

`Assistant/AssistantHttp.cs` registers the named client `assistant` the way `AddKoboHttpClient` does: `SocketsHttpHandler` with `AllowAutoRedirect = false`, `UseProxy = false`, `PooledConnectionLifetime` 5 minutes, **no** `ConnectCallback` (the URL is operator configuration, and a model server on the LAN or the Docker host must work), `Timeout` from the options, `MaxResponseContentBufferSize` 4 MiB, `RedactLoggedHeaders(_ => true)`.

`Assistant/AssistantClient.cs` sends one `POST {base}chat/completions` per request:

```json
{ "model": "<model>", "messages": [{ "role": "system", "content": "…" }, { "role": "user", "content": "…" }, …], …extra }
```

It never sends `temperature`, `top_p`, `max_tokens`, `tools`, `tool_choice`, `response_format` or `stream`: providers disagree on them (Anthropic's compatibility layer rejects some temperatures, newer OpenAI models want `max_completion_tokens`, DeepSeek has no JSON schema), and an operator who needs one sets it in the extra fields. The answer is `choices[0].message.content` (a string, or the concatenated `text` parts of an array), `choices[0].finish_reason` and, when present, `usage.prompt_tokens`, `usage.completion_tokens` and `usage.total_tokens`. Gemini's compatible endpoint counts thinking tokens only in `total_tokens`, so the tokens of a call are the larger of `total_tokens` and the sum of the other two. `reasoning_content` and other fields are ignored. The request's `HttpContext.RequestAborted` is linked into the call, so a cancelled browser request ends the provider request.

Outcomes:

| Provider outcome | Result |
|---|---|
| 2xx with text and `finish_reason` `stop`, absent, or any value not listed below (some servers send `eos_token`) | success |
| 2xx with text and `finish_reason` `length` | success with `truncated` |
| 2xx with `finish_reason` `error` or `content_filter` | `assistant.unavailable`, the value logged at Warning, no proposal |
| 2xx with `length` and no text (a reasoning model used its whole output limit) | `assistant.unavailable`, logged at Warning as the output limit |
| 2xx without text, with a top-level `error` object, unparsable JSON, a body over 4 MiB | `assistant.unavailable` |
| 3xx (not followed), 5xx, 408, 409, 413, 499, any other status not listed, connection or TLS failure | `assistant.unavailable`, the status logged at Warning |
| 401, 402, 403, 404 | `assistant.misconfigured`, logged at Error with the status and host (at most once a minute); 402 is the provider's "out of credit" |
| 400, 422 | `assistant.misconfigured` (the model rejected a field; the log names the status) |
| 429 | `assistant.rateLimited`, with `Retry-After` in seconds when the provider sent one, logged at Warning at most once a minute (OpenAI reports exhausted credit this way) |
| `HttpClient.Timeout` | `assistant.timeout` |
| the caller cancelled | `cancelled` (recorded, no response body) |

Provider bodies are read only to extract the answer; error bodies are never logged nor relayed.

*Alternatives:* provider SDKs (Google.GenAI, OpenAI .NET); rejected: one package per provider, each with its own retries and logging, and the operator could no longer switch by configuration. Anthropic's native Messages API; not needed while its compatibility layer accepts this request, and the adapter can be added later behind the same `AssistantClient` if the golden tasks show a gap.

### D3. Endpoints

All under `/api/v1/projects/{projectId:guid}/assistant`, tag `Assistant`, `RequireProjectRole(ProjectRole.Analyst)` (so a `viewer` gets 403 and a non-member 404), and each answers 503 `assistant.disabled` while `Enabled` is false.

| Endpoint | Behavior |
|---|---|
| `GET /` | `{ model, host }` for the disclosure line. |
| `POST /context` | The assembled request for a body like `/completions`, which may have no messages or a blank last one (the view before typing): `{ model, host, text, bytes, omitted[] }`. No provider call, no record, no budget. |
| `POST /completions` | Assembles, checks budget and concurrency, calls the provider, records, and answers `{ callId, explanation, blocks[], truncated, testExpired, omitted[], usage: { promptTokens, completionTokens, totalTokens, estimated } }`. |
| `PUT /calls/{callId}/outcome` | `{ applied: "script" \| "cell" \| "insert" }` from the requester of that call; 204; 404 for another user's call. |

Request body:

```json
{
  "kind": "indicator", "name": "piped_water_rate",
  "action": "script | cell | fix",
  "messages": [{ "role": "user | assistant", "content": "…" }],
  "draft": "…",
  "focus": { "line": 12 },
  "details": { "label": "…", "description": "…", "unit": "…", "format": "percent" },
  "testId": "…",
  "warning": { "key": "emptyValues", "params": { "measure": "value", "count": 4 } }
}
```

Validation (400 with field errors): `kind` as in `ScriptTestEndpoints`; `name` a valid table name for an indicator and absent otherwise; for `/completions`, `messages` 1 to 39 turns alternating, starting and ending with a `user` turn, the last one non-blank, each at most 16 KB, all together at most 64 KB (`/context` also accepts no messages or a blank last turn); `draft` at most 64 KB, else 422 `assistant.contextTooLarge` (the spec's "too large for the assistant"); `focus.line` required for `cell` and a marker line of the draft (`ScriptTestEndpoints.IsMarkerLine`, made reusable); `details` within `ScriptEndpoints`' metadata limits and formats; `testId` required for `fix`; `warning.key` one of the seven keys of `viewWarnings`. Warning params are kept per key and everything else is dropped:

| Key | Params kept |
|---|---|
| `noValue` | `measure` |
| `emptyValues` | `measure`, `count` (an integer) |
| `dimensionAsMeasure` | `column` |
| `duplicateRows` | `count` (an integer) |
| `percentAboveOne`, `noRows`, `ratioIncomplete` | none: `percentAboveOne` is rendered as "values of the default measure are above 1", never with its largest value |

The `measure` or `column` is checked when the test facts are rendered, not as a 400: it is dropped when it is not a column of the test's tables, and the whole `warning` is dropped with the test facts when the test is gone (the answer then carries `testExpired: true`). A kept name is masked like the test's other names (D5).

A fix reads the test through a helper factored out of `ScriptTestEndpoints.GetAsync` (same project, same requester, not expired). When it is gone, the request proceeds without test facts and answers `testExpired: true`. When the test's `Kind` or `Name` differ from the request's, or its `Code` differs from `draft`, it answers 409 `assistant.testStale`: the UI never offers that fix, so the check only guards the rule.

*Alternatives:* `GET /context?kind=…` (rejected: the draft and the messages cannot travel in a query string, so it could not show the exact text); returning `model` and `host` in the anonymous `/system/config` (rejected: an unauthenticated caller would learn which vendor holds the deployment's schema).

### D4. Context assembly

`Assistant/ContextAssembler.cs` builds one system message and passes the conversation through. The system message is a sequence of tagged blocks, most stable first, so providers' prefix caches hit across requests of the same project:

1. `<rules>`: `Prompts/system.md` (D7).
2. `<examples>`: the templates of the script's kind (embedded copies, D7).
3. `<project>`: the kind and its contract, the indicator's name and details (from `details`), the time zone, the enumerator question, and the fact that the input tables were published at a given time or not at all.
4. `<schema>`: the inputs, from `Pipeline/ScriptInputsReader.cs`, the body of `ScriptInputEndpoints.GetAsync` moved into a class both call; for each table a line with its name and row count, then one line per column: name, type, pandas dtype when it is not obvious (`Int64`, `datetime64[ns, UTC]`), the question label in quotes, and `[select_one <list>]`, `[select_multiple <list>]`, `[or other]` from `FormFields`. Repeat tables state their keys (`_parent_id` is the parent's `_id` for a first-level repeat, its `_row_id` for a nested one, named by `_parent_table`). Then reference tables (columns and types only), then the choice lists of the select questions that have columns, from `Project.FormChoices`, each as `list_name: label | label | …`, capped at 50 labels with the count of the rest, and a note that older form versions may have used other labels (`ctx.form.name` resolves them). With nothing published, the block says so and lists the form's questions (name, type, label, list name, "or other") and the choice lists of all its select questions, capped the same way.
5. `<script1>`: the latest saved script 1, for kinds other than `clean`, so drafts reuse its derived columns.
6. `<test>`: fix only (D5).
7. `<draft>`: the editor's text with line numbers; for `cell`, the focused cell's first and last line are named.

Then the conversation's turns as `user` and `assistant` messages, as the browser sends them (D11): an assistant turn is the earlier answer's explanation with each code block replaced by a one-line note (`[proposed code: 34 lines]`), because the code that matters is in the draft. Every block is wrapped and the rules say that block content is data, never instructions.

The compact text is about half the size of the JSON the inputs endpoint returns (measured on a 200-question form: about 35 KB against 78 KB).

### D5. Test facts and masking

`Assistant/TestFacts.cs` renders, for each step of the test: name, status, `stoppedAtLine`; for each table with columns: name, row count, columns with types; for each `ctx.show` entry: name, line, row count and columns, or "a text value"; for the draft's step, the error and log as below; for `dashboard`: measures, default measure, ratio, dimensions, default dimension, geometry columns, rows, empty values of the default measure, duplicate rows and, per chart, the number of groups. Never `Preview`, `CutCells`, `ShownPreview.Text`, `Totals`, `DefaultMeasureMax` or `ChartGroup`. The warning asked about is rendered from its key and the params D3 keeps. **Every name in the block goes through the masker**: step table names, columns of step and `ctx.show` tables, `ctx.show` names, the dashboard's measures, dimensions, geometry columns and chart keys, and the warning's `measure` or `column`, because a script can build any of them from data (a pivot, an output table named after a value).

**Error.** `Error` is the exception's last line, a blank line, then a traceback (see Context). The last line is not reliable: for a message on several lines, an exception with a note, or many library errors (`pd.to_datetime` with a format, DuckDB, Polars), it is message text, a hint, a caret or an expression. So the summary is not sent. When there is a traceback, the api keeps its frame lines, `^  File "script.py", line (\d+), in (.+)$` as `line N in name` and `^  File "script.py", line (\d+)$` (a `SyntaxError` or `IndentationError`) as `line N`, and takes the exception from the end of the traceback: the first line at column 0 after the frames, other than the `Traceback` header, is `<type>: <first line of the message>`. The type and that first message line are sent, both masked by the ordinary rules: a real type such as `KeyError` or `pandas._libs.tslibs.parsing.DateParseError` survives because exception class names and module path segments are in the vocabulary, while a data value that a multi-line message put at the start of the last line never reaches the request. Further message lines, notes, source lines and carets are dropped. A message without a traceback (contract, limit, worker failure) is masked as a whole.

**Log.** Only lines matching `^(INFO|WARNING|ERROR|CRITICAL) `, the last 60, each masked.

**Masking** (`Assistant/Masker.cs`) runs in two passes.

1. *Labels as phrases.* Each choice label and question label in the assembled request, as a whole phrase, case-insensitive with whitespace normalized, is protected where it appears. A label is never split into words that unlock the same words elsewhere: with the labels `Santa Ana` and `Gómez Plata` sent, `Ana Gómez` is not protected.
2. *Tokens.* Outside protected phrases, a number is a run of digits joined by `.`, `,`, `:`, `/`, `-` or spaces, with or without spaces around the separator and with an optional sign, so `300 - 123 - 4567` and `4.6097, -74.0817` are one number each; inside a token with `_`, the digits of all its numeric parts count together (`c_300_123_4567`, `c_1985_03_12`). A number is kept when it has at most six digits in all (`1.023.456.789`, `300 123 4567` and `1985-03-12` are masked, `2026-09` and `4,520` are kept); this also joins number lists such as `1204, 3051`, which are then masked. Any other token, a maximal run of Unicode letters, digits and `_`, is kept when, case-insensitively, it is an identifier taken, after trimming, only from the `<project>` and `<schema>` blocks (input and reference tables, their columns, question and list names), a `ctx` member or the contract function, never from `<test>` or the turns; a word of the vocabulary; or a token of the draft (identifiers, string-literal words and comment words, all sent anyway) that is not a word of any choice or question label in the request. A label's words are kept only inside the whole label (pass 1), even when the draft compares with that label, so a name cannot be rebuilt from the words of two labels. A token with `_` is also kept when every part is. Any other token becomes `…`, and runs of `…` separated only by spaces collapse into one. Quotes and punctuation stay, so `KeyError: '…'` keeps its shape.

**Vocabulary.** `Prompts/message-words.txt`, embedded: the lowercase words of at least two letters found in the message strings of Python 3.12 and the pinned libraries, plus Tamiza's own. It is generated by `tamiza-analytics/tools/message_words.py`, run in the analytics test image, which collects, from the stdlib and the installed `pandas`, `numpy`, `pyarrow`, `polars`, `duckdb`, `geopandas`, `shapely` and `openpyxl` sources: the string constants passed to `raise …(…)` and `warnings.warn(…)`; the names of every `BaseException` and `Warning` subclass and the segments of their modules' dotted paths (`keyerror`, `dateparseerror`, `tslibs`), so an exception named in a log line survives; every string literal of the `tamiza` and `tamiza_sdk` packages; and a short hand-kept file of messages that come from C code (`could not convert string to float`, `invalid literal for int() with base`, `object has no attribute`, `is not defined`, …). The file is regenerated when a pinned library changes; a unit test runs the masker over the messages observed while planning (KeyError on a column and on a row label, conversions, `DateParseError` with its module path, `exit()`, analyst messages, `ctx.log` with arguments, pivot renames, a worker failure) and checks both what is kept and what is masked.

*Alternatives:* masking quoted strings only (rejected: values also appear unquoted, in `DateParseError`, `exit()`, messages the analyst writes and `ctx.log` arguments); keeping chart group labels that are choice labels (rejected: the model needs the count, not the values, and month or free-text groups are never in the vocabulary); sending the log whole minus prints (rejected: `print("INFO …")` is indistinguishable from `ctx.log`).

### D6. Size limits and trimming

`Assistant/ContextTrimmer.cs` measures the UTF-8 size of the system message plus the turns. Above `TAMIZA_ASSISTANT_MAX_CONTEXT_KB`, it leaves out, in this order, until the request fits: earlier turns (always keeping the last user turn and the assistant turn before it); choice lists whose list name, question or columns the draft and the messages do not mention; tables other than `main` that are not mentioned; question labels beyond 80 characters (cut with `…`); `<script1>` beyond its first 16 KB; all examples but the first. Each step adds an entry to `omitted` (for example `choice lists: 14 of 22 left out`), which the context view shows and the record keeps. If the request still does not fit, it answers 422 `assistant.contextTooLarge`. The draft is never shortened. Masking runs after trimming, so the allow-list is exactly what is sent.

### D7. Prompt kit

Embedded resources of `Tamiza.Api` (`<EmbeddedResource Include="Assistant/Prompts/**;Assistant/Examples/**" />`):

- `Prompts/system.md`, about 15 to 20 KB, written for this change from `docs/scripts.md`. It states: the three contracts and what each returns; the flattened tables and system columns; that select answers are labels, select_multiple answers labels joined by `"; "`, and that a comparison uses the label from the choice lists; nullable `Int64` with `pd.NA`, the `str` dtype, timestamps in UTC and the project's zone with `tz_convert` before `.dt.date`; repeat joins by `_parent_id` and `_parent_table`; `numerator` and `denominator` for ratios of sums and `value` as the default measure; a percent format means a fraction, not ×100; dimensions as text, never a numeric year or month; `groupby(..., dropna=False)` when empty groups matter; cells with `# %%` and `ctx.show`; constants for the columns to adapt at the top; the `ctx` members (from `ctx-members.json`); the libraries and versions and that scikit-learn, SciPy, statsmodels, `pip`, files and the network are not available; that there is no `ctx.timezone` and no sampling frame in `ctx` (a reference table is the way); pandas 3.0 copy-on-write idioms (`.loc`, no chained assignment); assumptions (the denominator, exclusions) as comments at the top, one question back when the request is ambiguous; English code, identifiers, comments and log messages, the explanation in the language of the request, table and column names exactly as given; the answer as a short plain-text explanation and exactly one ` ```python ` block holding a whole script, or for a cell request only that cell starting with its `# %%` line; the smallest change for a fix, and never a bare `except` or a silent `fillna` to hide an error.
- `Examples/<kind>/<id>.py`: copies of the seven templates.
- `Prompts/message-words.txt` (D5).
- `AssistantPrompts.Version`, an integer bumped with any change to these files and stored in every record.

Drift tests in `Tamiza.Api.UnitTests` (found by walking up from `AppContext.BaseDirectory`, skipped when the folders are absent, like `ErrorCodeTranslationTests`): the examples equal `tamiza-ui/src/app/pages/projects/script-templates/<kind>/*.py`; the library line of `system.md` names the versions pinned in `tamiza-analytics/pyproject.toml`, and shapely's version, which is not pinned there, as `tamiza-analytics/uv.lock` locks it; every `ctx.` member named in `system.md` is in `tamiza-ui/.../script-editor/ctx-members.json`. A unit test also checks that `system.md` states the language rule and the answer format, so an edit cannot drop them silently.

### D8. Answers

The api splits the answer's text into fenced blocks (` ```python `, ` ```py ` or a bare fence) and the explanation, the text with each block replaced in place by a note such as `[proposed code: 34 lines]`, so the tab can show it and send it back as an earlier turn unchanged. `blocks[]` keeps their order. The UI offers:

- exactly one complete block in a `script` or `fix` answer: Replace script when `hasContract(code, kind)` (the exported form of `contractFunction`) holds, Insert at cursor always;
- exactly one complete block in a `cell` answer: Replace cell when its first non-blank line is a `# %%` marker, it holds no other, and the targeted cell is still in the editor unchanged (D11), Insert at cursor always;
- several blocks: Insert at cursor for each, with a note that the answer was not one script or one cell;
- a block whose closing fence is missing in a `truncated` answer: Insert at cursor only, with a note that the answer was cut off and a suggestion to ask for a smaller change;
- no block: the explanation only.

Before showing the actions, the UI runs `lintProblems` on what the editor would hold after applying (the whole proposal, or the draft with the cell replaced) and lists the marks with their lines.

### D9. Records, budget and concurrency

Migration `0011_add_script_assistant.sql`:

```sql
CREATE TABLE tamiza.assistant_calls (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    user_id uuid NOT NULL,
    kind character varying(16) NOT NULL,
    name character varying(63),
    action character varying(16) NOT NULL,
    test_id uuid,
    model character varying(200) NOT NULL,
    prompt_kit integer NOT NULL,
    context jsonb NOT NULL,
    status character varying(16) NOT NULL,
    error_code character varying(64),
    prompt_tokens integer,
    completion_tokens integer,
    total_tokens integer,
    tokens_estimated boolean NOT NULL DEFAULT false,
    outcome character varying(16),
    started_at timestamp with time zone NOT NULL DEFAULT now(),
    finished_at timestamp with time zone,
    CONSTRAINT pk_assistant_calls PRIMARY KEY (id),
    CONSTRAINT fk_assistant_calls_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_assistant_calls_users_user_id FOREIGN KEY (user_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_assistant_calls_kind CHECK (kind IN ('clean', 'output', 'indicator')),
    CONSTRAINT ck_assistant_calls_action CHECK (action IN ('script', 'cell', 'fix')),
    CONSTRAINT ck_assistant_calls_status CHECK (status IN ('running', 'succeeded', 'failed', 'cancelled')),
    CONSTRAINT ck_assistant_calls_outcome CHECK (outcome IS NULL OR outcome IN ('script', 'cell', 'insert'))
);
CREATE UNIQUE INDEX ix_assistant_calls_user_id_running ON tamiza.assistant_calls (user_id) WHERE status = 'running';
CREATE INDEX ix_assistant_calls_user_id_started_at ON tamiza.assistant_calls (user_id, started_at);
CREATE INDEX ix_assistant_calls_project_id ON tamiza.assistant_calls (project_id);
```

`test_id` has no foreign key because tests are deleted after 24 hours. `context` holds the size of each block, `omitted` and whether a test was attached; no text.

A completion: (1) sums today's tokens for the user (`started_at >= ` the start of the UTC day; per row `GREATEST(COALESCE(total_tokens, 0), COALESCE(prompt_tokens, 0) + COALESCE(completion_tokens, 0))`, estimates included); at or over the budget, 429 `assistant.budget` with `renewsAt`; (2) inserts the `running` row, where a unique violation answers 409 `assistant.busy`; (3) calls the provider; (4) updates the row with the status, tokens (or, only when the provider may have done work, an estimate with `tokens_estimated`: UTF-8 bytes / 4 of what was sent and received, for a 2xx answer without `usage`, including the 2xx failures of D2, a cancelled call and a timeout; every other failure records no tokens and costs nothing), error code and `finished_at`, in a `finally` that uses a fresh `CancellationToken`. At api start, rows still `running` become `failed` with `assistant.interrupted`, which assumes one api instance, as `SyncQueue.FailInterruptedAsync` does. Records are kept; they hold no text and are deleted with their project.

### D10. Errors

A `Problems.Status(status, code, title, detail, extensions)` helper next to `Unavailable` (whose fixed title, "could not be completed in time", fits none of these):

| Situation | Status | Code | Extensions |
|---|---|---|---|
| Not configured | 503 | `assistant.disabled` | |
| Key, model or a field refused | 503 | `assistant.misconfigured` | |
| Provider unreachable or no usable answer | 502 | `assistant.unavailable` | |
| Provider too slow | 504 | `assistant.timeout` | |
| Provider rate limit | 429 | `assistant.rateLimited` | `retryAfter` (seconds); also the `Retry-After` header |
| Daily budget reached | 429 | `assistant.budget` | `renewsAt` |
| Another request in progress | 409 | `assistant.busy` | |
| Test differs from the request | 409 | `assistant.testStale` | |
| Draft or context too large | 422 | `assistant.contextTooLarge` | |

The codes follow the newer camelCase style (`sampleDownload.busy`). `ApiError` gains `extensions` so the UI can format `retryAfter` and `renewsAt`; the translations go under `errors.codes.assistant.*` and into `error-codes.spec.ts`.

### D11. UI

- **System configuration.** `core/system-config.ts`: `SystemConfig` gains `assistant: { enabled: boolean }`, and a root `SystemConfigService` fetches it once and exposes `assistantEnabled` as a signal. `SystemEndpoints` adds `AssistantSettings(bool Enabled)`; `SystemConfigTests` expects the key `assistant`.
- **Service.** `core/projects/assistant.service.ts`: `settings`, `context`, `complete`, `recordOutcome`, typed like `PipelineService`. Cancelling unsubscribes, which aborts the XHR and so the api's request.
- **Cursor.** `CodeEditor` gains a `cursorLine` signal, set from its update listener when `selectionSet` or `docChanged`; the app is zoneless, so a plain method would never re-evaluate the scope when the cursor moves without typing. The page passes it to the side panel.
- **State outside the tab.** The side panel renders its tabs with `@switch`, which destroys a hidden tab's component, so nothing that must outlive the tab lives in it. A page-scoped `AssistantSession` (provided by `ProjectScripts`) holds, per script key, the thread, the latest proposal, the request box's text, the chosen scope, the last failure (its code and `retryAfter` or `renewsAt`, until the next send or dismissal), the in-flight request (its subscription, the text and the cell sent, the start time) and the fix target. A failure or a cancel writes the sent text back into that script's box and its failure into that script's state, whichever script or side tab is on view; the box of another script is never touched. A request is bound to the script key it was sent for: its answer is written into that script's thread, and its proposal is offered only while that script is selected. Selecting another script does not cancel it, and that script's tab says that a request for the other script is in progress and keeps Send disabled (the api would answer `assistant.busy`). Destroying the page cancels it, which records `cancelled`.
- **Tab.** `script-side-panel.ts` turns `tabs` into a computed that adds `assistant` when the deployment enables it and the user can edit (`canEdit()`), gives Versions its own `@case`, and falls back to Tables when the selected tab disappears. It accepts requests from the page to open a tab, and each "Ask the assistant" opens the Assistant tab even when it was requested before and the analyst has since chosen another tab (a fresh request object, a sequence number or a public `open()`; the mechanism is the implementer's). `script-editor/assistant-panel.ts` (`app-assistant-panel`) renders the session's state for the selected script: the disclosure line (model and host from `GET /assistant`, and one sentence: the schema, the choice lists, the saved script 1, the draft and the errors of the last test are sent, without rows); the thread (the analyst's messages and the explanations as plain text with `whitespace-pre-wrap`; code never renders in the tab); the scope (Script, This cell, Fix); the request box, with Send disabled while it is blank or longer than 16 KB, and a counter near that limit; "Draft from the details" for an indicator, filled from the page's `meta()`; "New conversation", not available while that script's request is being written; the "Writing… N s" state with Cancel; the proposal's summary (lines, whether it defines the contract function, the marks of D8) with its actions; "Show what will be sent" (a dialog with the text and what was left out, available before typing); and "Copy context" (clipboard and a toast, like `insertName`). While an older version is on view it shows that the assistant works on the current text and offers no new request; a request already being written keeps its waiting state and Cancel above that message. "This cell" is available when `cellAt(cells(draft), cursorLine())` finds a cell, and names it ("This cell: Rate"); otherwise it says it needs a `# %%` cell.
- **Fix target.** The session's fix target is the test id and, optionally, the warning, set by "Ask the assistant". It stays valid only while the page's latest test has the selected script's kind and name, its code equals `draft()`, no older version is on view and, for a warning, `viewWarnings(facts, current details)` still contains its key (the warnings follow the details being edited); it is recomputed on every edit, test change and script switch. When it stops being valid, the tab leaves the Fix scope and says to test again first. "Ask the assistant" fills the request box with a fixed translated sentence that names the failed step or the kind of warning (`scripts.assistant.fixStep`, `scripts.assistant.fixWarning.<key>`), never with text from the test; the analyst can edit it. The error, log and warning reach the api only as `testId` and `warning`.
- **Sending.** On Send, the session builds `messages` from the thread and the box: assistant turns as their explanation, which already holds `[proposed code: N lines]` in place of each block (D8), then the oldest pairs of a user turn and its answer dropped until there are at most 38 earlier turns, each at most 16 KB, and 64 KB in all (D3), so the request always starts with a user turn; D6 drops turns in pairs too. The user turn joins the thread only together with its answer: a request that fails, is refused or is cancelled adds nothing, and its text goes back into the box, so the thread always alternates.
- **Apply.** The page applies a proposal: if `draft()` differs from the text sent with the request, it asks with its own confirmation (`scripts.assistant.replaceEdited*`). Replace script calls `editor.replace(code)`. Replace cell finds the target again by its text as sent, from its marker line to the line before the next marker, wherever that text now is, and calls `editor.replaceRange(from, to, code)` over it; a line added above does not matter. When no cell or more than one cell has that text (the cell was edited, split or its marker removed), Replace cell is withdrawn and only Insert is offered, with a note. Insert calls `editor.insertAtCursor(code)`. `CodeEditor` gains `replaceRange`, and both replacements carry `isolateHistory.of('full')` and a `userEvent`, so one undo reverts exactly the proposal; templates get the same isolation. Afterwards the page calls `recordOutcome`. The draft becomes unsaved and the test panel's existing `stale()` marks the result out of date; nothing is saved or tested.
- **Conversation storage.** `sessionStorage` key `tamiza.assistant.<user sub>.<projectId>.<kind>[.<name>]` holding the thread, the box's text, the scope and the latest proposal, so a reload offers it again with what applying needs: the `callId`, the action, `truncated`, the blocks, the text sent and, for a cell request, the targeted cell's text. After a reload the draft is the saved code, so applying asks for confirmation whenever it differs from the text sent. An entry holds at most 200 KB: past that, the oldest turns are dropped from the stored copy first (the thread on screen keeps them until a reload), the proposal is never dropped for turns, and a proposal that alone exceeds the cap is kept in memory only. All entries together hold at most 1 MB across `tamiza.assistant.` keys, evicting the least recently used conversation first, because the OIDC library keeps its tokens in the same 5 MB store. Every read and write is wrapped in `try`/`catch`; a full store keeps the conversation in memory only. The subject comes from the OIDC user data. After sign-in succeeds, keys of any other subject are removed, so a person who signs in after another one's session expired in the same tab never inherits that conversation; the same person keeps theirs. `AuthService.logout()` removes every key that starts with `tamiza.assistant.` before `oidc.logoff()`, never with `sessionStorage.clear()`.
- **Translations.** `scripts.sideTabs.assistant`, a `scripts.assistant.*` group, the import check keys (D13), `errors.codes.assistant.*`.

### D12. Test panel

`ScriptTestPanel` gains the input `canAsk` (assistant enabled and the user can edit) and the outputs `changed` (the polled test, emitted after each refresh, so the page knows the last test, its status and its code) and `ask` (`{ testId, warning? }`). "Ask the assistant" appears next to `test-step-error` only for the step named after the test's kind (the draft; a failed `clean` step of an indicator test is the saved script 1) and only while the fix target would be valid (D11): the panel receives `canAsk` as `assistantEnabled && canEdit && !viewing() && !stale() &&` the test's kind and name matching the selected script, because `stale()` compares code only, two scripts can hold the same template, and while an older version is on view `draft()` does not change. For a warning, the button also follows the view: a warning that disappears (its details changed) takes its button with it. `IndicatorView` gains `canAsk`, passed the same value, and an `ask` output with the `ViewWarning`, rendered as a button inside each `view-warning` alert. The page sets the fix target, opens the Assistant tab with the Fix scope, scrolls the side panel into view (`scrollIntoView({ block: 'start' })`, as it already does for a new test) and puts the focus on the request box.

### D13. Import check

`script-editor/python-modules.json`: `libraries` (`duckdb`, `geopandas`, `numpy`, `openpyxl`, `pandas`, `polars`, `pyarrow`, and `shapely`, the geometry package geopandas uses), `never` (`os`, `sys`, `subprocess`, `socket`, `shutil`, `ctypes`, `importlib`, `http`, `urllib`) and `stdlib` (Python 3.12's `sys.stdlib_module_names`). `script-analysis.ts` gains `importProblems(source, tree)`, called by `check()` after the syntax early return and outside the contract branch, so imports anywhere are checked: for each `ImportStatement`, the root module of `import a.b` and `from a.b import c` (a relative import is skipped); a module in `never`, or a call whose callee is `__import__`, gives `scripts.checks.neverImport`; a module in neither `libraries` nor `stdlib` gives `scripts.checks.unavailableImport` with the list of libraries. Both are warnings. An analytics test (`tests/test_python_modules.py`) checks that `stdlib` equals `sys.stdlib_module_names`, that `libraries` contains `kit.LIBRARIES`, and that every library imports in the worker. `docs/scripts.md` names shapely under Limits.

### D14. Deployment files

- `tamiza-ui/nginx.conf`: `location ~ ^/api/v1/projects/[^/]+/assistant/` copying the exports block with `proxy_read_timeout 300s` and `client_max_body_size 1m`, with a comment that the api's own limit is at most 240 seconds.
- `compose.yaml`: the seven variables in the `api` environment only, strings as `${VAR:-}` and numbers with their defaults.
- `.env.example`: a section `# --- AI assistant (optional) ---` between Python scripts and Memory limits, in the file's style, saying that requests go from the `api` container to the provider and pointing to `docs/operations.md`. `PROXY_NETWORK` is documented too, so `scripts/check-env-example.sh` passes again.
- `.gitignore`: `.env*` with `!.env.example`, which also covers the repo's own naming (`.envprod`, `.envstaging`), so a new env file holding a provider key is not committed by accident. It does not untrack a file git already tracks.

### D15. Documentation

- `docs/operations.md`: the Requirements bullet on outbound HTTPS names the model provider when the assistant is configured; a new `## AI assistant` section after Common changes, holding:
  - the variables (a table);
  - example settings as ` ```sh ` blocks without prices, each checked against the provider's current pages and dated: Gemini (a billed project; the free tier may use content to improve products and must not be used with survey data; how to lower its default thinking through the extra fields), DeepSeek (the storage and training facts of its policy), OpenAI, OpenRouter (its zero-retention option), and a self-hosted server through `host.docker.internal`;
  - for self-hosted servers: the context window must hold `TAMIZA_ASSISTANT_MAX_CONTEXT_KB` plus the answer (Ollama defaults to 4,096 tokens and its compatible endpoint cannot change it per request, so start it with `OLLAMA_CONTEXT_LENGTH` or a model with `PARAMETER num_ctx`; vLLM with `--max-model-len`), otherwise the rules and schema are cut silently; reasoning models need the server's reasoning parser (vLLM `--reasoning-parser`) so their reasoning stays out of the answer; on a Linux Docker Engine the server must listen beyond loopback (`OLLAMA_HOST=0.0.0.0:11434`, firewalled from other hosts); a server without keys still needs a placeholder key, because the assistant stays off without one;
  - how exhausted credit shows: `misconfigured` for a 402, `rateLimited` for OpenAI's 429, with the status in the log;
  - what is sent and what never is; that the provider's terms, including prefix caching, are the operator's to check, and that choosing a provider is a data-protection decision of the organization (a hedged paragraph on Ley 1581 and donors' rules, not legal advice);
  - `### Checking a provider`: seven requests to run before switching provider, on a project whose form has a select_one by municipality, a select_multiple, a `members` repeat and dates: a regression that tempts scikit-learn; select_multiple dummies; a percent rate by municipality with `numerator` and `denominator`; a household variable from the repeat joined by `_parent_id`; a monthly series in the project's time zone; a fix of a comparison with a relabeled choice; and a request in Spanish. Each with its exact text and what passes: the first proposal passes the sample test or needs at most one fix, no unavailable import, and for the Spanish one, a Spanish explanation with English code and the column names unchanged; and how to read the tokens from the usage records;
  - the usage records with a SQL example; the timeout of an own reverse proxy; that the key must not be put in a committed file.
- `docs/scripts.md`: "four tabs" and an **Assistant** bullet in The editor; the import checks in Checks; shapely in Limits; `## Writing with the assistant` (the three actions, a worked request, applying and undoing, then testing as usual) with `### What the assistant can see` (the list of D4 and D5, including that the draft and the saved script 1 are sent as written, with any constants typed in them, and that choice labels are sent, so an enumerator list of people's names is too). Examples there use ` ```text ` fences, because `test_docs_examples.py` runs every ` ```python ` block that defines a contract function and the last one per kind replaces the earlier one.
- `README.md` status and the `api` row of the architecture table; `docs/tamiza-brief.md` sections 2.2 (the api calls the configured provider), 4 (no longer out of scope), 6 (`assistant_calls`) and 8 (item `7d`); `openspec/config.yaml` sections 2.2 and 4.

## Risks / Trade-offs

- [A survey value that is also an English word of the vocabulary, a word of the draft, or a whole choice label such as a name in an enumerator choice list, passes the masking] → The vocabulary holds message words only; labels count only as whole phrases, and a label's words are not unlocked by the draft either, so a name cannot be rebuilt from parts of different labels; a value made only of words the draft holds outside labels still passes; choice lists are sent anyway; the docs say that choice labels are sent and that enumerator lists of names are part of them.
- [A self-hosted server with a small context window cuts the rules and schema and still answers 200 with a generic script] → The operations section states the window each server needs and how to set it; the provider check of `docs/operations.md` would expose it.
- [Question labels, choice labels and masked errors are text written by others inside the prompt (injection)] → The model has no tools; its output reaches the editor only on a click, as one undoable edit; the editor's checks, including the import checks, mark what it proposes; nothing runs until the analyst tests.
- [Providers differ on optional fields, and one may require one this design never sends (for example an output limit)] → `TAMIZA_ASSISTANT_EXTRA_BODY`; a 400 or 422 is reported as misconfigured with the status in the log; the golden tasks are rerun with each provider before switching.
- [The provider keeps the schema block in a prefix cache or logs it for abuse monitoring] → Documented as the operator's decision with each provider's terms; no rows are ever sent.
- [A slow provider ties up an api request for up to 240 seconds on a 512 MB container] → One request per user, a 4 MiB response cap, a 64 KB draft cap and a context cap.
- [A deployment behind its own reverse proxy cuts requests at 60 seconds] → Documented in `docs/operations.md` next to the nginx location.
- [The vocabulary file drifts from the libraries] → It is regenerated with the pins; the masker's tests fail on observed messages that lose their structure.
- [Two api instances would mark each other's running calls as interrupted] → The deployment runs one api, as the ingestion queue already assumes.
- [`.envprod` is tracked in git and holds `POSTGRES_PASSWORD`; a provider key added to it would be committed] → `.gitignore` covers new env files; the operations section says not to commit the key; untracking `.envprod` and rotating the password are the owner's decision, outside this change.

## Migration Plan

1. Deploy: migration 0011 creates `tamiza.assistant_calls`; without the variables nothing else changes, apart from the import warnings in the editor.
2. Enable: set the base URL, model and key (and, when needed, the extra fields) in `.env`, then `docker compose up -d api ui` (the ui image carries the new nginx location).
3. Disable: blank any of the three variables and restart `api`; the tab disappears and the routes answer `assistant.disabled`. The records stay.
4. Roll back to an earlier release: the earlier api ignores the table; DbUp never runs scripts backwards.

## Open Questions

- The exact retention wording of each provider's paid tier for the docs (for example Gemini's "limited period" for abuse monitoring) is checked against the providers' pages when `docs/operations.md` is written; it does not change the specs or the design.

## Deviations from the plan

- **Extra members the tests and the endpoints needed.** `AssistantHttpOptions` (`TimeoutOverride`, `MaxResponseBytes`) lets tests lower the timeout below the validated minimum of 10 s. `AssistantFailure` carries `Usage` and `ProviderMayHaveWorked`, so the estimate rule of D9 is applied in one place. `POST /context` also answers `testExpired`. (D1, D2, D3)
- **Shape of `blocks[]`.** D3 did not give it: it is `{ code, complete }`, with `complete` false for a block whose closing fence is missing. Estimated tokens are stored only in `total_tokens`, with `tokens_estimated` true. (D3, D8, D9)
- **Startup recovery** fails only the `running` rows that started before this process did. Without that rule the background service could close a request that a fast client started right after the host started. It still assumes one api instance. (D9)
- **Masking, where D5 was silent or too coarse.** The masker keeps the article `a` and the endings of English contractions (`t`, `s`, `d`, `m`, `re`, `ve`, `ll`), or the spec's own `doesn't match format` message would lose its words. A label with no letter (`12`, `1985`) is not protected as a phrase, because that could carve a long number into harmless pieces. Each error or log line is cut at 500 characters before masking and a message without a traceback sends at most 20 lines. The indicator's name is in the allow-list because the `<project>` block states it. Tokens that mix letters and digits (`INT32`, `utf8`) are masked, so a DuckDB `to INT32` shows as `to …`; an allowance for letters from the vocabulary followed by up to four digits can be added if this proves too coarse. (D5)
- **Trimming** keeps the last three turns (user, assistant, user) when it drops pairs, because a request must start with a user turn. (D6)
- **Tab and panel.** "Show what will be sent" and "Copy context" are hidden while an older version is on view. Fix and This cell are rendered only while available, with their explanation in a note, instead of as disabled buttons. If the request box already holds the analyst's own words, "Ask the assistant" adds its sentence after them. The one confirmation covers the three actions. The session also stores the text of a request still in flight, so a reload during a request does not lose it. (D11, D12)
- **Replace cell** strips the proposal's leading blank lines and trailing whitespace and puts back the trailing newlines of the replaced cell, so the spacing before the next cell does not change. (D11)
- **Provider documentation.** Ollama's pages disagree on its default context window (4,096 tokens in the FAQ, and a VRAM-dependent value on the context-length page); the operations section cites both and tells the operator to check. The OpenAI example uses the model name its models page lists, and tells the operator to confirm that it takes Chat Completions. In the provider check, request 7 asks for the schema's own column names (`municipality`), because the check form has English names. (D15)
- **Found by the real-provider check and fixed.** Gemini rewrote a broken script when asked for a fix, dropped the check that raised the error, and restructured the script into cells. The fix request now says, next to the draft, to change only the lines that cause the failure and to keep every other line, check and name, and `system.md` says the same under "Fixing"; `AssistantPrompts.Version` is 2. (D4, D7)
- **Found by the stack verification and fixed.** The log of a rejected key was written at most once a minute for all statuses together, so a 402 (out of credit) right after a 401 never reached the log; the category now includes the status. A second request of a user is refused by a query before the insert, so the unique violation is no longer logged as an error by EF Core. The `assistant_calls` unique index still decides when two requests arrive at the same moment. (D2, D9)

## Verification Results

Real stack in a separate Compose project (`tamizaverify`, port 8189, images tagged `verify`, `ui` kept off the proxy network), a fake OIDC provider, a fake Kobo and a fake OpenAI-compatible provider on the host, and headless Chrome driven over the DevTools protocol. The user's own stack and Keycloak were not touched. The verification stack, its volumes and images were removed afterwards, and the processes on ports 8190 to 8193 were stopped.

- **Stack check against the fake provider, 13 groups and 51 sub-checks, all passed.** Roles and tabs (analyst four, viewer three and 403, non-member 404, assistant off: three tabs and 503 `assistant.disabled` with nothing reaching the provider). Disclosure, "Show what will be sent" without any seeded respondent value, "Copy context". Draft, Replace script, Ctrl+Z, with no version saved and no test started. Cell rewrite following a click without typing, and still offered after an import was added above while waiting, withdrawn after the cell was edited. Fix from a failed test and from a dashboard warning: the request held the step, table and column facts, the masked error shapes (`KeyError: 'watr_source'`, `ValueError: bad …`, `INFO first …`) and "values … are above 1", and none of the seeded canary values, preview rows, printed lines, chart groups or the largest value (137.5). The buttons and the Fix scope disappeared after an edit or after the format changed. Waiting state kept across side tabs, request bound to its script, a second request refused with 409 `assistant.busy`, Cancel closing the provider connection and recording `cancelled`. 401, 402, 429 with Retry-After, 500, `finish_reason` error, a cut-off answer and a two-block answer, each with its message and the text back in the box; timeout; redirect not followed. Budget: refused requests cost nothing, `total_tokens` larger than the sum is counted, no usage is estimated, and the request over the budget is refused with the renewal time. Reload, New conversation, sign-out and other-script conversations; no message text in `assistant_calls`. A 75-second answer through nginx completed, with `location ~ ^/api/v1/projects/[^/]+/assistant/` and `proxy_read_timeout 300s` in the loaded configuration. At 390 px the page does not scroll horizontally.
- **Seven provider requests against Gemini (`gemini-3.8-flash`, `reasoning_effort` low), prompt kit version 2: 7 of 7 passed their automatic criteria**, each on the first proposal with no fix, no import mark, and between 6.8 and 7.3 thousand tokens. Request 6 now changes the label and its message and leaves the rest of the script as it was. Request 7 explains in Spanish, writes English code and comments, and reads `municipality` and `water_source`. A first run, with prompt kit version 1, passed 5 of 7: request 2's criterion had assumed a column naming the request does not ask for (the proposal was correct on 100 of 100 previewed rows, checked by hand against the shown input), and request 6 had rewritten the script. The prompt change and the criterion were corrected as listed above. The results are the table of the second run and the saved proposals; the code was read by hand for requests 2, 6 and 7 only.
- **Suites.** Analytics 618 passed. UI 1145 passed and 2 failed, the known polling failures. API unit 840 passed. API integration 836 passed (an earlier run had 9 failures in the authentication, database and health tests, caused by a full disk, which passed once space was freed).
- **Not checked.** A self-hosted server (Ollama or vLLM) and a Linux Docker host-gateway; any provider other than Gemini; the form-version case in which an old label (`Tap water`) is relabeled; and the code quality of requests 1, 3, 4 and 5 beyond their automatic criteria.
