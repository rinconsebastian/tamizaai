# Tasks

**Order.** Groups 1 to 7 run in order, and each ends with the UI suite green. Group 1 adds `es.json` as a copy of `en.json`, so the parity test runs from the start. From then on, every group that adds or changes a key does so in both files. Group 7 replaces the copied English with Spanish. Group 8 runs last.

**How to run the checks.** All commands run in `tamiza-ui/`:
- The whole suite: `npm test -- --watch=false`. The two "stops polling when the page is destroyed" tests (`project-data.spec.ts`, `project-scripts.spec.ts`) are known failures.
- A single spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.
- The other checks: `npm run build` and `npm run check:safety`.

The API and the worker do not change.

## 1. Language choice and wiring

- [x] 1.1 Add `src/app/core/i18n/interface-language.ts` with `resolveInterfaceLanguage(languages)` (design D1). Verify with `interface-language.spec.ts`:
  - every scenario of "Interface language from the browser": `es-CO, es` → es/`es-CO`; `fr-FR, es-ES, en` → es/`es-ES`; `en-US, es-CO` → en/`en-US`; `de-DE, pt-BR` → en/`en-US`;
  - a bare `es` → es/`es`;
  - lowercase `es-co` → `es-CO`;
  - an invalid tag skipped;
  - an empty list falling back to `navigator.language`.
- [x] 1.2 Add `public/i18n/es.json` as an exact copy of `en.json`. Add `src/app/core/i18n/translations.spec.ts` (design D10). It checks that both files have the same leaf keys, the same `{{ param }}` names per key, the same leading and trailing whitespace, and no empty values. Verify it passes. Then verify it fails on a copy that lacks one key, renames a parameter or drops the leading space of `dashboards.nameDialog.copy`.
- [x] 1.3 In `app.config.ts`, resolve the language once (design D1, D2, D3):
  - Transloco: `availableLangs: ['en', 'es']`, `defaultLang: lang`, `fallbackLang: 'en'`.
  - `{ provide: LOCALE_ID, useValue: locale }`.
  - `document.documentElement.lang = lang`.

  Verify `npm run build`, and that the suite is unchanged (no spec uses `appConfig`).
- [x] 1.4 Change `translocoTesting()` in `src/testing/transloco-testing.ts` to take a language (default `en`), loading both files. Add a testing provider for `LOCALE_ID`. Verify the whole suite still passes with the defaults, and that a smoke spec rendering the shell with `es` gets the text from `es.json`.
- [x] 1.5 Add `customParamsAuthRequest: { ui_locales: lang }` to `core/auth/oidc-config.ts` (design D8). Verify with `oidc-config.spec.ts` that the config carries `ui_locales` `es` for a Spanish language and `en` for English ("Sign-in in Spanish").
- [x] 1.6 Update the docs:
  - Root `README.md`: the UI is in English or Spanish, chosen from the browser.
  - `docs/keycloak.md`: how to turn on internationalization with Spanish in the realm, for a Spanish sign-in page.
  - `tamiza-ui/README.md`, replacing the "add a language" line: both files hold every key, the parity test enforces it, no Angular locale pipes, and formats go through `shared/ui/formats.ts`.
  - The conventions line in `docs/tamiza-brief.md` (line 59) and in the `openspec/config.yaml` context (line 68): the UI's source text is English, with a Spanish translation.

  Verify each link and command in the edited docs.

## 2. Regional formats

- [x] 2.1 Add `src/app/shared/ui/formats.ts` (design D3) with `dateTimeText`, `dateText`, `timeText`, `timestampText`, `numberText` (with the `'1.0-1'` digits string) and `percentText`. Formatters are cached per locale, zone and style. Verify `formats.spec.ts`, with spaces normalized:
  - `Sep 1, 2026, 9:30 PM` for `en-US`;
  - `1 Sept 2026, 21:30` for `en-GB`;
  - `1 de sept de 2026, 9:30 p. m.` for `es-CO`;
  - `1 sept 2026, 21:30` for `es-ES`;
  - `12.345,5` for `es-CO` and `12,345.5` for `es-MX` and `en-US`;
  - `timestampText('2026-09-01T21:30:05-05:00', …)` showing 21:30 whatever the test process's zone;
  - text that is not a timestamp returned unchanged.

  Move the cases of `date-format.spec.ts` into it.
- [x] 2.2 Add the standalone pipes `| dateTime: style`, `| num: digits` and `| pct: digits`, all reading `LOCALE_ID`. Verify a spec rendering both with `en-US` and with `es-CO`.
- [x] 2.3 Replace every `| date`, `| number`, `| percent`, `formatDate`, `DatePipe` and `DecimalPipe` in app code with the new pipes and functions. This covers about 25 files: `project-performance.ts`, `project-data.ts`, `project-clean-data.ts`, `project-reference.ts`, `project-indicators.ts`, `script-inputs-panel.ts`, `run-details-dialog.ts`, `project-sampling-frame.ts`, `pipeline-runs.ts`, `script-test-panel.ts`, `script-side-panel.ts`, `sample-download-panel.ts`, `reference-preview-dialog.ts`, `project-webhook.ts`, `project-scripts.ts`, `project-overview.ts`, `pie-widget.ts`, `import-frame-dialog.ts`, `frame-questions-dialog.ts`, `frame-file-guide.ts`, `indicator-view.ts`, `assistant-panel.ts`, `map-tooltip.ts` and `data.service.ts`. Then remove `date-format.ts` and `dateTimeFormat`. Verify:
  - the whole suite passes with its en-US assertions unchanged;
  - `grep` finds no Angular locale API left in `src/app` outside specs.
- [x] 2.4 Replace the hard-coded `'en'` and `toFixed` formatting with the functions from 2.1:
  - `toLocaleString('en')` in `project-scripts.ts`, `sample-download-panel.ts` and `script-assist.ts`;
  - `formatDate(…, 'en')` in `assistant-panel.ts`;
  - `toFixed` in `pipeline-runs.util.ts`, `sample-download-panel.ts`, `assistant-panel.ts` and `assistant-context-dialog.ts`, keeping the unit symbols and localizing only the number;
  - the literal `%` after numbers in `project-performance.ts`.

  Pass raw counts that go into translations (`rows: result.rowCount`, `version.submissions`, `String(n)` durations) through `numberText`. Leave `valueFormatter(…, 'en')` at `project-scripts.ts:825` as it is, and comment why: it only compares warning keys. Verify the `pipeline-runs.util` and `sample-download-panel` specs in en-US and es-CO (`8,2 s`, `12,3 MB`), and the suite.
- [x] 2.5 Extend `scripts/check-ui-safety.sh` with the patterns of design D10 for `tamiza-ui/src/app`, outside `*.spec.ts`. Verify `npm run check:safety` passes on the tree, and fails when a `| date` or a `.toFixed(` is added to a component.

## 3. Counts

- [x] 3.1 Add the plural helper in `core/i18n` (design D4). It picks the singular key when `Intl.PluralRules(lang).select(count) === 'one'` and passes the count formatted with `numberText`. Verify a spec covering 0, 1, 2 and 1,000,000 in `en` and `es`.
- [x] 3.2 Move the ~18 `count === 1` sites to the helper:
  - `import-frame-dialog.ts`, `project-sampling-frame.ts`, `frame-questions-dialog.ts`;
  - `axis-chart.ts`, `project-indicators.ts`, `project-data.ts`, `project-overview.ts`;
  - `assistant-panel.ts`, `data-view.ts`, `dashboard-name-dialog.ts`.

  Verify their specs still pass unchanged.
- [x] 3.3 Go through the ~45 count keys that have no singular. For each one whose count can be 1, add a singular key to both files and switch its site to the helper. Examples: `data.runsEvery`, `cleanData.total`, `reference.size`, `scripts.inputs.rows`, `sampleDownloads.tableRows`, `cleanData.export.ready`, `errors.codes.assistant.rateLimitedIn`. Verify specs for "Schedule of one hour" ("also run every 1 hour") and "One row" (an export of 1 row), and the parity test.

## 4. Whole sentences

- [x] 4.1 Settle the Spanish wording of the sentences that embed a translated fragment (design D5): check each key against every value of its fragment, record the wording in the design table, and fix the one that needs code: `scriptTests.title` gets `scriptTests.titleOne` for a test on one submission, in both files. Verify the singular title in `script-test-panel.spec.ts`, the English assertions unchanged, and the parity test. (The Spanish texts themselves are written in group 7 and follow the table.)
- [x] 4.2 Review the TypeScript concatenations of translated pieces against their Spanish text (design D5), and rewrite as one key with parameters only those whose Spanish needs another word order or agreement. Reviewed, all kept because each joins independent sentences or a label and a number:
  - `${ready} ${readyCut}` in `project-clean-data.ts` and `project-indicators.ts`;
  - `${listed} ${moreRows}` in `import-frame-dialog.ts`;
  - `parts.join(' ')` in `project-sampling-frame.ts`;
  - `${t(scriptName)} ${t(versionShort)}` in `run-details-dialog.ts`;
  - `${t('frame.unnamed')} ${i + 1}` in `project-sampling-frame.ts`.

  Verify the Spanish of each is written in group 7 in the order of its English, and that the specs of those components pass with the English output unchanged.

## 5. Backend text marked as English

- [x] 5.1 Make `ParsedError` (`core/api-error.ts`) record whether its message came from the API (field messages, `detail`, `title`) or from the UI (`errors.*` keys) (design D6). The toast and the field-error templates set `lang="en"` on API text. Verify `api-error.spec.ts` with the Spanish testing setup:
  - status 0 gives the Spanish network message with no `lang="en"` ("Server unreachable in Spanish");
  - a 404 `{ detail: "Widget not found." }` shows that text with `lang="en"` ("API message in the Spanish UI");
  - `kobo.unauthorized` gives the Spanish message ("Known API error code");
  - a field message keeps its English text next to its field with `lang="en"` ("Field message in the Spanish UI").
- [x] 5.2 Add `lang="en"` to:
  - the run and test error and log `<pre>` blocks (`run-details-dialog.ts`, `script-test-panel.ts`);
  - the sample-download error (`sample-download-panel.ts`);
  - the `ctx.show` fallback;
  - the prompt in the assistant's context dialog.

  Where one string joins a translated introduction and worker text (`pipeline-runs.ts:265`, `script-test-panel.ts:291`, `sampleDownloads.script1Failed`, `frame.rowError`), return the two parts separately and render the worker text in its own `lang="en"` span. Verify specs for "Step failure in the Spanish UI" (`KeyError: 'edad'` kept unchanged after the Spanish introduction) and for the English layout, which keeps the same text.

## 6. Remaining hard-coded English

- [x] 6.1 Give the toast host its "Dismiss" label as an input, translated by the app shell (`shared/ui/toast.ts:79`), and add the key to both files. Verify a toast spec reads the label from the input, and a Spanish shell spec gets the Spanish label.
- [x] 6.2 Collect CodeMirror's phrase ids from the `state.phrase(` calls in the installed `@codemirror/search`, `language`, `lint`, `autocomplete`, `view` and `commands` packages. Add one `codeEditor.phrases.*` key per id to both files. Give `code-editor` an optional `phrases` input applied with `EditorState.phrases.of(...)`, and have `project-scripts` build it (design D7). Verify:
  - a `code-editor` spec opens the search panel with Spanish phrases and finds the Spanish field label ("Script editor search");
  - without the input, the panel stays English.
- [x] 6.3 Move the autocomplete details in `script-editor/script-assist.ts` to keys: "N rows" (through the plural helper), "reference table", and the column types (through `preview.types.*`). Verify `script-assist.spec.ts` in English, unchanged apart from the types, and in Spanish.

## 7. Spanish translation

Translate `es.json` following the glossary and style of design D9, in the key order of `en.json`. Keep the Python code examples in English. After each task, verify that the parity test passes and that no value in the translated sections still equals its English value, apart from code examples, product names and terms the glossary keeps (Script, Webhook, Widget, General).

- [x] 7.1 Translate `app`, `shell`, `authUnavailable`, `notFound`, `common`, `roles`, `errors` (including every `errors.codes.*`), `projects`, `createProject` and `project`.
- [x] 7.2 Translate `overview`, `metrics`, `performance`, `data`, `webhook` and `members`.
- [x] 7.3 Translate `scripts`, `scriptTests`, `preview`, `pipeline`, `runs` and `codeEditor`.
- [x] 7.4 Translate `frame`, `sampleDownloads` and `reference`.
- [x] 7.5 Translate `cleanData`, `indicators` and `dashboards`.
- [x] 7.6 Add Spanish component specs:
  - the project tab row reads Desempeño, Datos limpios, Indicators, Field and Configuración ("Project sections in Spanish");
  - the dashboard duplicate proposes "Housing (copia)" ("Name proposed for a duplicate");
  - the assistant's fix request is written in Spanish ("Request for the assistant");
  - an export of 1 row uses the singular.

  Verify they pass, and that the whole suite passes.

## 8. Integration checks

- [x] 8.1 Run `npm test -- --watch=false`, `npm run build` and `npm run check:safety` in `tamiza-ui/`. Verify everything passes apart from the two known polling failures.
- [x] 8.2 With the stack running and the browser set to `es-CO` only, walk every screen at 360, 768 and 1280 px:
  - the shell;
  - the projects list and project creation;
  - the Performance and Clean data dashboards;
  - an indicators dashboard with each widget type and a map tooltip;
  - every Settings section, the script editor with search, a test, the assistant and a failed run;
  - the sampling-frame import.

  Verify that:
  - no UI-owned English text remains;
  - nothing overflows or forces horizontal page scrolling;
  - dates read `1 de sept de 2026, 9:30 p. m.` style and numbers `12.345,5`;
  - the page declares `lang="es"`;
  - API and worker messages appear in English;
  - the sign-in redirect carries `ui_locales=es`.

  Fix what the walk finds, adding a spec for each fix.
- [x] 8.3 Reload with the browser set to `es-ES`, then `en-GB`, then `de-DE`. Verify 24-hour times for `es-ES`, `1 Sept 2026, 21:30` for `en-GB`, and an English UI with `12,345.5` for `de-DE`.
- [x] 8.4 Ask the user to review `es.json` and the main screens in Spanish. Apply their corrections and verify the parity test and the suite again.

## Workflow follow-up

- Archive the change once the user has reviewed the Spanish text and the manual checks pass.
- Record any reserved-name or mixed-language findings for the later `add-api-message-codes` change.
