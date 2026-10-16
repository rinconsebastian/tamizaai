# Tasks

Groups 1 to 4 are the api and come first, in order. Group 5 is independent of them. Groups 6 and 7 need groups 4 and 5. Group 8 can start after group 4. Group 9 closes the change.

## 1. Configuration and provider client (api)

- [x] 1.1 Add `Assistant/AssistantOptions.cs` with the seven `TAMIZA_ASSISTANT_*` variables, their defaults and `Enabled`, an `IValidateOptions` that stops startup for a malformed base URL, extra fields that are not a JSON object or set `model`, `messages` or `stream`, and numbers out of range (context size from 64 KB), and a startup Warning naming the missing variables when only some of the three are set (design D1). Register it in a new `AssistantRegistration.cs` called from `Program.cs`. Add the variables to the `api` environment of `compose.yaml` and a section to `.env.example`; document `PROXY_NETWORK` there too; add `.env*` and `!.env.example` to `.gitignore` (D14). Verify unit tests for the defaults, `Enabled` with three, two and zero values, blank values as absent, each rejected value naming its variable (including a context size of 16), and that `sh scripts/check-env-example.sh` passes.
- [x] 1.2 Add the named client `assistant` (`AssistantHttp.cs`) and `AssistantClient` (D2): the request body with the extra fields merged and none of the fields D2 never sends, the answer from string or array content, `finish_reason`, and optional `usage` with `total_tokens`, and the outcome table. Add `AssistantStub` (WireMock, like `KoboStub`) to the integration tests. Verify integration tests for:
  - success with string and with array content, with `usage` absent, and with `total_tokens` larger than prompt plus completion;
  - `finish_reason` `length` with text (truncated) and without text, `error` with partial text, `content_filter`, and an unknown `eos_token` that still succeeds;
  - a 200 holding only an `error` object;
  - 401, 402, 404, 400, 418, 429 with and without `Retry-After`, and 500;
  - a 302 that is not followed, a timeout, a cancelled caller and a body over 4 MiB;
  - a stub on `localhost` reached without any private-network flag;
  - the `Authorization` header holding the key, and no captured log line containing the key or a response body.
- [x] 1.3 Add `assistant: { enabled }` to `GET /api/v1/system/config` (D11). Verify `SystemConfigTests` expects the keys `oidc`, `version` and `assistant`, with `enabled` false without the variables and true with them, and that the response holds neither the model, the host nor the key (scenario `Configured`).

## 2. Inputs reader and prompt kit (api, analytics tools)

- [x] 2.1 Move the body of `ScriptInputEndpoints.GetAsync` into `Pipeline/ScriptInputsReader.cs` and make the endpoint call it (D4). Verify that `ScriptInputsApiTests` pass unchanged.
- [x] 2.2 Write `Assistant/Prompts/system.md` with the content listed in D7, copy the seven templates to `Assistant/Examples/<kind>/`, embed both with `AssistantPrompts.Version`, and add the drift tests of D7 to `Tamiza.Api.UnitTests`. Verify that the drift tests pass; that they fail when a template copy, a pinned version or shapely's locked version is changed by hand; that they are skipped when `tamiza-ui/` is absent; and that the test of the language rule and the answer format passes.
- [x] 2.3 Write `tamiza-analytics/tools/message_words.py` and its hand-kept file of C-level messages, generate `Assistant/Prompts/message-words.txt` in the `tamiza-analytics-test:dev` image and embed it (D5). Verify that running the tool twice gives the same file; that it contains `could`, `convert`, `float`, `literal`, `unable`, `parse`, `published`, `column`, `keyerror`, `valueerror`, `dateparseerror` and `tslibs`; and that it contains none of `juan`, `perez`, `gomez`, `maria` and `ana`.

## 3. Context assembly, masking and trimming (api)

- [x] 3.1 Add `ContextAssembler` with the blocks and order of D4 and the compact schema rendering. Verify unit tests that render:
  - `main` with its row count, a repeat and a nested repeat with their keys named (scenario `Schema and choices`);
  - select_one, select_multiple and "or other" columns with their lists, and a list of 120 choices cut at 50 with the count;
  - the time zone, the enumerator question and an indicator's unsaved details;
  - script 1 for an indicator, and not for script 1 itself;
  - a project that never ran, with the form's questions and a choice list (scenario `Nothing published yet`);
  - the library line and the unavailable libraries (scenario `Libraries`);
  - the turns passed through as sent;
  - and that the same inputs give the same text byte for byte.
- [x] 3.2 Add `TestFacts` and `Masker` (D5) and the per-key warning params (D3). Verify unit tests for the scenarios `Known column kept`, `Quoted value masked`, `Unquoted value masked`, `Message the script wrote`, `Column made from data`, `Name split across labels` (with a draft that compares with both labels), `Whole label kept`, `Separated numbers`, `Exception type kept`, `Message on several lines`, `Library hint after the message`, `Traceback`, `Syntax error`, `Log lines`, `Long log`, `Preview rows`, `Printed output`, `Chart groups`, `Failed indicator`, `Warning asked about` and `Percent warning` (a `max` of 87.5 and a `shown` param are dropped). Also verify:
  - the messages observed while planning: a KeyError on a row label, `invalid literal for int()`, `Unable to parse string`, `time data … doesn't match format`, `exit()`, `ReferenceTableNotFound`, a worker failure whose log holds the worker's traceback, and a pivot rename line;
  - that a pivot-made output table name and default measure appear as `…` in every place the block names them, and that a name listed only in `<test>` never allows itself;
  - an exception with a note, and a DuckDB `ConversionException` whose last line is a caret, sending the type and the first message line from the end of the traceback;
  - that no `Preview`, `CutCells`, `Text`, `Totals`, `DefaultMeasureMax` or chart group value appears in the output.
- [x] 3.3 Add `ContextTrimmer` (D6). Verify unit tests for the scenarios `Large form`, `Large draft` and `Still too large`, the order of the omissions, the `omitted` entries, the last user turn kept, and that masking runs on the trimmed text (a label left out is masked in the error).

## 4. Records, endpoints and errors (api)

- [x] 4.1 Add `0011_add_script_assistant.sql` as in D9, the `AssistantCall` entity and its mapping, and the startup step that marks `running` rows as `failed` with `assistant.interrupted`. Verify `ScriptConventionTests` for 0011 (named constraints, `ix_assistant_calls_` prefixes), `SchemaDriftTests`, a `SchemaMigratorTests` upgrade from 0010 that keeps existing rows, and an integration test of the startup step.
- [x] 4.2 Add `Problems.Status` and the codes of D10. Verify unit tests for the status, code, title and extensions of each, and the `Retry-After` header for `assistant.rateLimited`.
- [x] 4.3 Add `AssistantEndpoints` with `GET /`, `POST /context`, `POST /completions` and `PUT /calls/{callId}/outcome`, the request validation, the own-unexpired-test helper factored out of `ScriptTestEndpoints.GetAsync`, `assistant.testStale`, the budget and the one-request rule (D3, D9). Verify integration tests for:
  - the scenarios `Not configured`, `Partly configured`, `Viewer`, `Not a member`, `Switching provider` (two stubs, two factories), `Server on the local network`, `Extra fields`, `Provider redirects`, `Expired test`, `Same text as the request`, `Before typing`, `Large draft`, `Budget reached`, `Reasoning tokens`, `Refused request costs nothing` (a 401 and a 429 add nothing, a cancelled call adds its estimate), `Provider reports no usage`, `Second request at the same time`, `Cancel` (the stub sees the request end and the record is `cancelled`), `Request recorded`, `Failed request recorded`, `Wrong key`, `Out of credit`, `Failed while writing`, `Timeout` and `Rate limit`;
  - each 400 rule of D3: turns that do not alternate, a blank last user turn on `/completions`, a turn over 16 KB, more than 64 KB of turns, `focus.line` not on a marker, `fix` without `testId`, a `name` on script 1, details over their limits;
  - warning params outside the per-key list being dropped, and an expired test with a `noValue` warning answering with `testExpired: true`, no 400 and no measure name;
  - turns that start with an `assistant` turn refused with 400;
  - 409 `assistant.testStale` when the test's code differs from `draft`;
  - `GET /` returning the model and the base URL's host;
  - `POST /context` leaving `assistant_calls` empty and the stub uncalled;
  - a fix whose test belongs to another analyst getting no test facts, and an outcome for another user's call answering 404;
  - the routes in the OpenAPI document.

## 5. Editor: import check, cursor and range replacement (ui, analytics)

- [x] 5.1 Add `script-editor/python-modules.json`, `importProblems` in `script-analysis.ts` called from `check()`, and the keys `scripts.checks.unavailableImport` and `scripts.checks.neverImport` in `en.json` (D13). Add `tamiza-analytics/tests/test_python_modules.py`. Verify `script-analysis.spec.ts` for the scenarios `Library the worker does not have`, `Available libraries and the standard library`, `Import inside the contract function`, `Process or network module`, `Dynamic import` and `Standard library modules that are fine`, `project-scripts.spec.ts` for `Saving with an import warning`, and the analytics test in the test image.
- [x] 5.2 Add `CodeEditor.replaceRange(from, to, text)`, give it and `replace` the `isolateHistory` annotation and a `userEvent`, and add the `cursorLine` signal set on selection and document changes (D11). Verify `code-editor.spec.ts`: after typing, a replacement, then more typing, two undos bring back the text before the replacement and not earlier; `replaceRange` returns false and changes nothing when read-only; `cursorLine` follows a selection change without any typing.
- [x] 5.3 Document the import checks in the Checks list of `docs/scripts.md` and shapely in Limits. Verify that `test_docs_examples.py` passes.

## 6. Assistant tab (ui)

- [x] 6.1 Add `assistant` to `SystemConfig` and a root `SystemConfigService` exposing `assistantEnabled` (D11). Verify a service spec for enabled, disabled and a failed request (treated as disabled).
- [x] 6.2 Add `AssistantService`, `extensions` on `ApiError`, and `errors.codes.assistant.*` in `en.json` and `error-codes.spec.ts` (D10, D11). Verify the service spec for each route and body, and that `retryAfter` and `renewsAt` reach the parsed error.
- [x] 6.3 Make the side panel's tabs a computed with the Assistant tab, an explicit Versions case, the fallback to Tables and a requested tab (D11). Verify `project-scripts.spec.ts` for the scenarios `Not configured` (three tabs), `Analyst` (four), `Viewer` (none), and that hiding the tab while it is selected shows Tables.
- [x] 6.4 Add `assistant-panel.ts` with the disclosure line, the scopes, the request box, "Draft from the details", "New conversation", the waiting state with Cancel, the errors, "Show what will be sent", "Copy context" and the older-version message (D11). Verify a component spec for the scenarios `Disclosure` (naming the saved script 1), `Draft an indicator`, `Draft from the details`, `Unsaved details`, `Cursor outside any cell`, `Cursor moved into a cell`, `Older version on view` (a request being written keeps its waiting state and Cancel), `Blank request`, `Request too long`, `Cancel`, `Wrong key`, `Out of credit`, `Failed while writing`, `Timeout`, `Rate limit`, `Budget reached`, `Second request at the same time`, `Expired test` (the note shown with the answer), `Before typing` and `Copy context` (the clipboard holds the text of `POST /context` and a toast appears). Verify the layout at phone width.
- [x] 6.5 Add the page-scoped `AssistantSession` with requests bound to their script, the classification of answers into actions with their pre-check (D8), and applying from `project-scripts.ts` with the confirmation when the text changed and the outcome recorded (D11). Verify `project-scripts.spec.ts` for the scenarios `Apply and undo`, `Not saved, not tested`, `Partial proposal`, `Answer cut off`, `Several blocks`, `Edited while waiting`, `Rewrite a cell`, `Answer that is not one cell`, `Cell edited while waiting`, `Line added above while waiting`, `Proposal with an unavailable import`, `Another side tab while writing`, `Typed request kept across side tabs`, `Failure while away`, `Another script while writing` and `Leaving the Scripts page while writing`, and that `PUT …/outcome` is sent with `script`, `cell` or `insert`.
- [x] 6.6 Keep each conversation and its latest proposal in `sessionStorage` under its user, project and script, build the turns sent with each request, remove other subjects' keys after sign-in, and remove the `tamiza.assistant.` keys in `AuthService.logout()` (D11). Verify specs for:
  - the scenarios `Reload`, `Sign-out`, `Another script`, `New conversation`, `New conversation while writing`, `Failed request`, `Long conversation` (the body's first turn is a `user` turn) and `Earlier answers`;
  - after a reload: Replace cell still finds its cell or is withdrawn with the note, applying asks for confirmation when the restored draft differs from the text sent, and `PUT …/outcome` carries the stored `callId`;
  - the 200 KB per-script cap dropping the oldest stored turns first and never the proposal, a proposal larger than the cap kept in memory only, the 1 MB total cap with least-recently-used eviction, and a write that hits the quota keeping the conversation in memory;
  - an expired session followed by another user (the keys are gone) and by the same user (the conversation is kept);
  - the OIDC library's keys left in place.

## 7. Fix from the test result (ui)

- [x] 7.1 Add `canAsk` and the outputs `changed` and `ask` to `ScriptTestPanel`, `canAsk` and `ask` to `IndicatorView`, the fix target and the fixed request sentences in the session, and the wiring in `project-scripts.ts` that opens, scrolls to and focuses the Assistant tab (D11, D12). Verify specs for the scenarios `Fix a failed test`, `Dashboard warning`, `Saved script 1 failed`, `Text changed since the test` (both the step button and a warning button), `Edited after asking`, `Test of another script`, `Ask while an older version is on view`, `Details changed after asking`, `Asking again` and `Fix with nothing typed`, in which the posted last user turn equals the translated sentence. Verify that no button appears when the assistant is disabled or for a `viewer`.

## 8. Deployment and documentation (ui, docs)

- [x] 8.1 Add the assistant location to `tamiza-ui/nginx.conf` (D14). Verify `nginx -t` in the built ui image, and that a request to `/api/v1/projects/<id>/assistant/completions` through it is held for longer than 60 seconds by a slow fake provider in the verification stack (task 9.3).
- [x] 8.2 Write the `## AI assistant` section of `docs/operations.md`, including `### Checking a provider`, and the Requirements bullet (D15), reading each provider's current terms and compatibility pages and dating the facts. Verify that the variables match `.env.example`, that the links resolve, and that each of the seven requests has its exact text and pass criteria.
- [x] 8.3 Update "The editor" in `docs/scripts.md` (four tabs, the Assistant bullet) and add `## Writing with the assistant` with `### What the assistant can see` (D15). Verify that `test_docs_examples.py` passes with the same set of examples and that the anchors resolve.
- [x] 8.4 Update the README status and the `api` row, `docs/tamiza-brief.md` sections 2.2, 4, 6 and 8 (item `7d`, before `add-audit-log`), and `openspec/config.yaml` sections 2.2 and 4. Verify that the links resolve and that item 7d reads like 7a to 7c.

## 9. Verification

- [x] 9.1 Run `openspec validate add-script-assistant --strict`. Verify that it passes.
- [x] 9.2 Run the full analytics, API and UI suites, and `ruff check` and `ruff format --check` on the new tool and test. Verify that they pass, except the two known UI polling failures (`project-data.spec.ts`, `project-scripts.spec.ts`).
- [x] 9.3 In a separate Compose project (`tamizaverify`, port 8189, fake OIDC and Kobo as in earlier changes, the `compose.verify.yaml` override that keeps `ui` off the proxy network) with a fake OpenAI-compatible server on the host as the provider (`TAMIZA_ASSISTANT_BASE_URL=http://host.docker.internal:<port>/v1/`), check in headless Chrome:
  - the tab for an analyst and not for a viewer;
  - a draft applied and undone, and a cell rewritten;
  - a fix from a failed test and from a dashboard warning, with the request the fake provider received holding no preview value, no printed line, no dashboard value and the masked error;
  - Cancel ending the fake provider's request, and a request continuing while another side tab is open;
  - the budget message after lowering the budget;
  - the conversation after a reload and gone after sign-out;
  - nothing in the tab with the variables blank.

  Record the results in a `## Verification Results` section of `design.md`.
- [x] 9.4 Ask the user for a billed provider key for the verification stack (never written to a tracked file) and run the seven requests of `docs/operations.md` "Checking a provider", including the scenario `Request in Spanish`. For each, record in `design.md` whether the first proposal passed the sample test, the checks it raised, the fix rounds and the tokens used.
- [x] 9.5 Mark item 7d as implemented in `docs/tamiza-brief.md`, section 8. Verify that the line reads like items 7a to 7c.

## Workflow follow-up

- Archive the change with `/opsx:archive` after review, syncing `script-assistant` and the `script-editor` delta into `openspec/specs`.
- Commit the work; the user commits after reviewing.
