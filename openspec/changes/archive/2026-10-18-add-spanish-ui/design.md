# Design

## Context

See proposal.md for the motivation and specs/ for the behavior. This section covers only the state of `tamiza-ui` that shapes the approach.

- **Transloco 8.4.**
  - Configured in `app.config.ts` with `availableLangs: ['en']` and no `fallbackLang` or `missingHandler`.
  - The only file is `public/i18n/en.json`: 1,218 strings, 290 of them with `{{ }}` parameters, no ICU.
  - Text is read through the `*transloco` directive (about 990 `t()` calls) and `TranslocoService.translate` (135 calls in 31 files).
  - The root `*transloco` wraps the router outlet, so nothing renders until the file has loaded.
  - A missing key comes back as the key itself. `api-error.ts` and `script-assist.ts` use that to tell whether a code has a message.
- **Plurals.** Separate keys, `…One` with `…Many` (or with a bare base key), chosen by `count === 1` at about 18 sites. Many of those sites pass a count that is already formatted.
- **Dates and numbers.**
  - No `LOCALE_ID` is provided, so Angular falls back to `en-US`, the only locale data it bundles.
  - About 25 files use `| date`, `| number`, `| percent`, `formatDate` or `DecimalPipe`.
  - The shared pattern is `dateTimeFormat = 'MMM d, y, h:mm a'` (`shared/ui/date-format.ts`).
  - Five chart and map components already pass `LOCALE_ID` to `Intl.NumberFormat`.
  - `'en'` is hard-coded in `toLocaleString`, `formatDate` and `valueFormatter` calls, and `toFixed` builds "8.2 s" and "12.3 MB".
- **Hard-coded English outside Transloco:**
  - the toast's "Dismiss" label;
  - the autocomplete details in `script-assist.ts`;
  - CodeMirror's own phrases (search, go to line, fold, lint, completion), since no `EditorState.phrases` is set;
  - the size and duration units.

  Templates are otherwise clean.
- **Backend text.**
  - `core/api-error.ts` is the one parser. It translates `errors.codes.<code>` when a key exists. Otherwise it shows the field messages, `detail` or `title` verbatim.
  - Run and test errors and logs are shown verbatim in `<pre>` blocks.
  - `pipeline-runs.ts` and `script-test-panel.ts` build one string from a translated step message and the worker's English error.
- **Tests.**
  - `src/testing/transloco-testing.ts` loads the real `en.json`, pinned to `en`.
  - jsdom reports `en-US`, and specs get Angular's default `en-US` `LOCALE_ID`.
  - 541 assertions compare English text. About 60 compare en-US numbers and dates.

## Goals / Non-Goals

**Goals:**
- Spanish and English chosen once, before the first render, with no change to how components read text.
- Regional formats from `Intl` alone, correct for any region of either language, with no Angular locale data.
- Existing English specs keep passing unchanged. Both new behaviors, the Spanish text and the regional formats, are covered by tests that fail if a key or a format regresses.
- `shared/ui` stays free of Transloco: it receives its text through inputs, as it does today.

**Non-Goals:**
- Switching language at runtime.
- Lazy or scoped translation files.
- An extraction or key-management tool.
- Translating the Python templates and code examples. Code stays English, and the templates must stay byte-identical to the API's examples.

## Decisions

### D1. Resolve the language once, from `navigator.languages`, before bootstrap

A pure function `resolveInterfaceLanguage(languages: readonly string[]): { lang: 'en' | 'es'; locale: string }` lives in `core/i18n/interface-language.ts`. It works as follows:

1. It walks the list in order. When `navigator.languages` is empty it uses `[navigator.language]`.
2. It canonicalizes each entry with `Intl.getCanonicalLocales` inside `try/catch`, skipping tags that are not valid.
3. It returns the first entry whose primary subtag is `es` or `en`, with that canonical tag as the locale (`es-CO`, `es`, `en-GB`).
4. When no entry matches, it returns `{ lang: 'en', locale: 'en-US' }`.

`app.config.ts` calls it once when the module is evaluated. It sets `document.documentElement.lang` and feeds the Transloco config, `LOCALE_ID` and the OIDC config. `index.html` keeps `lang="en"` for the moment before the script runs.

- *Not chosen: Transloco's `getBrowserLang()`.* It reads only the first entry and drops the region, so `fr-FR, es-ES` would give English and `es-CO` would lose its formats.
- *Not chosen: nginx `Accept-Language` negotiation.* The UI is static files, and the browser already exposes the ordered list.
- *Not chosen: a switcher or a stored choice.* The user decided against it. The resolver stays a single seam, so a switcher can be added later.

### D2. Transloco: two languages, fallback only for a failed load

The config becomes `availableLangs: ['en', 'es']`, `defaultLang: lang`, `fallbackLang: 'en'`. `missingHandler.useFallbackTranslation` stays off.

- In Transloco 8.4, `fallbackLang` only takes over when `es.json` fails to load after its retries.
- `useFallbackTranslation` would also load `en.json` on every Spanish page. It would also stop `translate(key) === key` from detecting codes without a message.
- Completeness is enforced by the parity test (D9) instead of at runtime.
- The loader stays as it is, one file per language. `es.json` is about the size of `en.json` (about 65 KB, about 15 KB gzipped), and nginx already serves `/i18n/` with `no-cache`.

### D3. `LOCALE_ID` is the resolved locale, and all formatting goes through `Intl`

`app.config.ts` provides `{ provide: LOCALE_ID, useValue: locale }`. Every Angular locale-data API is removed from app code: `DatePipe`, `DecimalPipe`, `PercentPipe`, `formatDate`, `formatNumber` and the `| date`, `| number`, `| percent` pipes. They are replaced by a new `shared/ui/formats.ts` and two thin standalone pipes:

- `dateTimeText`, `dateText` and `timeText` (value, locale, optional IANA time zone) use `Intl.DateTimeFormat` with `{ day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }`, or the date or time subset of it. Formatters are cached per locale, zone and style.
- `timestampText` keeps its contract: the wall time of the offset the value carries. It now reads the wall-clock fields from the string and formats them with `timeZone: 'UTC'`, so it does not depend on offset time zones in `Intl`, which only recent browsers support.
- `numberText(value, locale, digits?)` accepts the same `'1.0-1'` digits string as `DecimalPipe`, which keeps call sites unchanged. Its default is at most 3 decimals, like `DecimalPipe`. `percentText` writes a fraction as a percentage.
- Durations and sizes keep their symbols (`s`, `min`, `KB`, `MB`) and localize only the number: `Intl`'s unit names (`8.2 sec`, `8.2kB`) would change today's English text for no gain. The existing `durationText` (`pipeline-runs.util.ts`) and `sizeText` (`sample-download-panel.ts`) take the locale and call `numberText` instead of `toFixed`.
- The pipes are `| dateTime: 'dateTime' | 'date' | 'time'`, `| num: digits?` and `| pct: digits?`. All read `LOCALE_ID`.

Why this approach:
- Angular's pipes need `registerLocaleData` for every locale. Registering `es` and `en` would collapse `es-CO`, `es-MX` and `es-ES` into one set of separators. Registering every regional variant would grow the bundle and still miss regions.
- `Intl` ships with the browser and knows every region.
- Providing `LOCALE_ID`, rather than a token of our own, keeps the five components that already pass it to `Intl` unchanged. It also leaves specs on their `en-US` default.
- The cost: an Angular locale pipe that is left in, or added later, throws "Missing locale data" in Spanish only. D10's check rejects them in app code.

Explicit date components rather than `dateStyle: 'medium'`:
- `medium` is numeric for `es-CO` (`1/09/2026`), which is ambiguous between regions that write day/month and month/day.
- Explicit components name the month everywhere: `1 de sept de 2026, 9:30 p. m.`.
- They also reproduce today's `en-US` output exactly (`Sep 1, 2026, 9:30 PM`), so the English specs and the scenario in `dashboard-indicators` hold.

### D4. Plurals: keep the key convention and choose with `Intl.PluralRules`

One helper in `core/i18n` takes a count and a singular and a plural key. It picks the singular when `new Intl.PluralRules(lang).select(count) === 'one'` and passes the count, formatted with D3, as a parameter. The ~18 existing sites move to the helper.

Each count key whose count can be 1 gets a singular key, in English too. This fixes "every 1 hours". Keys whose count is never 1, such as limits, stay single.

Spanish's CLDR `many` category (multiples of a million) uses the plural key, which is correct.

- *Not chosen: ICU through `@jsverse/transloco-messageformat`.* It adds a dependency and a transpiler that applies to every string, and it rewrites 290 parameterized strings. The two languages need only one/other.

### D5. Whole sentences where Spanish needs agreement

A key that inserts another translated fragment stays only when its Spanish sentence reads correctly for every value of that fragment. Otherwise there are two options:

- **(a)** Split it into one whole-sentence key per value.
- **(b)** Rephrase it in Spanish so no agreement is needed, for example with a colon form or a verb that does not change with gender. The English key and text stay as they are.

Each key that embeds a fragment was checked against every value of its fragment. None needs (a); the Spanish wording is fixed here so the translation follows it:

| Key | Fragment | Spanish |
|---|---|---|
| `runs.stepFailed` | step name, or an indicator's name | `Error en {{ step }}` |
| `members.pendingRole` | role | `Invitación como {{ role }}` |
| `scripts.editorLabel`, `scriptTests.title`, `scriptTests.draft` | script kind (`1. Limpieza`, `2. Salida`, `Indicador`) | the kind follows a noun or stands alone, so it never needs agreement |
| `scripts.checks.noContract` | `Script 1`, `Script 2`, `Un script de indicador` | `{{ script }} debe definir {{ signature }}.` |
| `scripts.assistant.pendingElsewhere`, `scripts.assistant.fixStep` | `scripts.assistant.scriptLabels`, which carry their article (`el script 1`, `el indicador {{ name }}`) | `Hay una solicitud en curso para {{ script }}…`, `La última prueba falló en {{ script }}…` |
| `data.activeSync` | synchronization, full resync, deletion check | `{{ kind }} en curso…`, invariable |
| `frame.summary`, `indicators.chart.caption`, `indicators.map.caption` | counts and labels joined with ` · ` or `por` | independent facts, no agreement |
| `frame.preview.summaryOne/Many` | a count of rows and a count of dimensions | the gendered noun (`fila encontrada`) agrees only with its own count, which the `One`/`Many` pair already handles |

The one change to a sentence key is a count that can be 1 (`minSampleSize` is 1): `scriptTests.title` gets `scriptTests.titleOne`.

The TypeScript concatenations of translated pieces were each read with its Spanish text, and none needs to become one key:
- `${ready} ${readyCut}` and `parts.join(' ')` join complete, independent sentences;
- `${listed} ${moreRows}` reads "Filas omitidas: 1, 2 y 4 filas más", the same order as the English;
- `${t(scriptName)} ${t(versionShort)}` ("Script 1 v3") and `${t('frame.unnamed')} ${i + 1}` ("(sin nombre) 1") are a label and a number.

Joins with ` · ` between independent facts stay too. A concatenation is rewritten as one key only when its Spanish needs a different word order or agreement; none does.

English wording changes only where a key is split or a singular is added. Every other English string stays byte-identical, so the 541 English assertions keep passing.

### D6. Backend text is wrapped, not translated

- `ApiErrors.parse` records each text the API wrote (field messages, and the `detail` or `title` of an error without a code the UI knows) in a small registry kept by value (`shared/ui/api-text.ts`). Whoever renders a text asks whether the API wrote it: the form field and the toast do, and a directive does for any other element (`<p [tmzApiLang]="message">`), setting `lang="en"` on API text and nothing on text the UI wrote. Recording by value keeps the many signals that hold these messages plain strings, and a message translated from an error code is never in the registry.
- The run and test error and log `<pre>` blocks, the sample-download error, the `ctx.show` fallback and the assistant's context dialog always carry `lang="en"`. It is correct in the English UI too, and simpler than a condition.
- The assistant's reply is not marked, because the model writes it in the analyst's language.
- Where the UI builds one string from both kinds of text (`step.*` + `run.error`, `sampleDownloads.script1Failed`, `frame.rowError`), the builder returns the translated introduction and the English detail separately. The template renders them as two spans, the second with `lang="en"`. In a toast, where only one string fits, the whole text stays in the interface language's element.
- `errors.codes.*` stays the contract with the API (`ErrorCodeTranslationTests` reads `en.json`). `es.json` mirrors it through the parity test.

### D7. Remaining hard-coded English

- **Toast "Dismiss".** The toast host gets its label as an input from the app shell, which translates it.
- **CodeMirror phrases.** `code-editor` gets an optional `phrases` input applied as `EditorState.phrases.of(...)`. `project-scripts` builds the input from `codeEditor.phrases.*` keys, one key per phrase id, because CodeMirror's phrase texts contain spaces and `$` and do not work as Transloco paths. The phrase list is taken at implementation from the `state.phrase(` calls of the installed `@codemirror/*` packages.
- **Autocomplete details.** "N rows" and "reference table" go to keys. Raw column types use the existing `preview.types.*`.
- **Kept:** the `'Other'` choice in `sampling-frame-fields.ts`, which mirrors the API's choice label and is matched as data; the OSM attribution fallback; the `UTC` option; the Python templates.

### D8. Sign-in language

`oidc-config.ts` adds `customParamsAuthRequest: { ui_locales: lang }`, which `angular-auth-oidc-client` 22 supports. Keycloak uses it when the realm has internationalization turned on with Spanish. Otherwise the realm's default applies. The README tells operators how to turn it on.

### D9. Translating `en.json`

`es.json` is written by hand in the same key order as `en.json`, so the two files diff line by line. The style is neutral Latin-American Spanish with *tú*, sentence case like the English, and no gendered slashes: a sentence is rephrased instead.

The 24 Python code examples (`scripts.ctx.members.*.example`, `scripts.reference.cellsExample`) keep their English code and comments, as the code convention requires. The parity test allows a Spanish value identical to the English one.

Glossary:

| English | Spanish |
|---|---|
| Performance (dashboard) | Desempeño |
| Clean data | Datos limpios |
| Settings / General | Configuración / General |
| Submission(s) | Envío(s) |
| Form | Formulario |
| Enumerator | Encuestador |
| Sampling frame / sample target | Marco muestral / meta muestral |
| Dashboard | Tablero |
| Widget / stat tile | Widget / cifra destacada |
| Indicator | Indicador |
| Script / run / test | Script / ejecución / prueba |
| Flatten / Clean / Output (steps) | Aplanado / Limpieza / Salida |
| Synchronization | Sincronización |
| Reference data / reference table | Datos de referencia / tabla de referencia |
| Members | Miembros |
| Admin / Analyst / Viewer | Administrador / Analista / Lector |
| Assistant | Asistente |
| Basemap | Mapa base |
| Sign out | Cerrar sesión |
| API token / webhook | Token de API / webhook |

### D10. Tests and guard

- **`core/i18n/translations.spec.ts`.** `es.json` and `en.json` must satisfy all of the following:
  - they have the same leaf keys;
  - each pair has the same set of `{{ param }}` names;
  - each pair has the same leading and trailing whitespace (`" (copy)"` → `" (copia)"`);
  - no value is empty.
- **`interface-language.spec.ts`.** Every scenario of "Interface language from the browser", plus an empty list, invalid tags and lowercase tags (`es-co`).
- **`formats.spec.ts`.** The examples from the specs for `en-US`, `en-GB`, `es-CO`, `es-ES` and `es-MX`, and `timestampText` from another browser zone. Assertions normalize U+00A0 and U+202F to spaces, because ICU versions differ in spacing.
- **`translocoTesting(lang = 'en')`** and a `LOCALE_ID` testing provider. Spanish specs cover:
  - the shell and the project tab row;
  - `api-error`: the network message in Spanish, and an API `detail` verbatim with `lang="en"`;
  - a failed run row;
  - the code editor's phrases;
  - one count with its singular.
- **`scripts/check-ui-safety.sh`** fails on these patterns in `tamiza-ui/src/app`, outside `*.spec.ts`:
  - `| date`, `| number`, `| percent`, `| currency`;
  - `formatDate(`, `formatNumber(`, `formatPercent(`;
  - `DatePipe`, `DecimalPipe`, `PercentPipe`;
  - `toLocaleString('en')`, `.toFixed(`.

## Risks / Trade-offs

- **[Spanish text is about 20–30% longer]** It can overflow buttons, dialog footers, chart captions and the tab row at 360 px. → A manual pass in Spanish at 360, 768 and 1280 px over every screen, listed in tasks. The tab row already scrolls and truncates.
- **[Translation quality]** The parity test catches missing keys, not wrong Spanish. → Glossary, plus a review of `es.json` and the main screens by the user, a native speaker, before archive.
- **[Mixed-language screens]** English API messages, logs and the "Indicators" default name appear inside a Spanish UI. → Accepted for this change. They are marked `lang="en"`, and the follow-up `add-api-message-codes` translates them.
- **[Reserved dashboard names are English only]** The API blocks "Settings" but not "Configuración", so a Spanish user can create a tab that reads like the Settings tab. → Accepted. It belongs to `add-api-message-codes`.
- **[`Intl` output differs between browsers and ICU versions]** Examples: `sept` vs `sep`, spaces vs U+202F. → Specs give examples. Tests normalize spaces and run on the pinned Node. No code parses formatted text.
- **[An Angular locale pipe slips back in]** It would throw only in Spanish. → The D10 guard, plus Spanish component specs.
- **[`es.json` fails to load]** Transloco falls back to English text while formats stay regional. → Rare and readable. It needs no handling.
- **[Keycloak stays English]** This happens unless the operator turns Spanish on in the realm. → The README covers the step. Tamiza still works.

## Migration Plan

No data or API change. Deploy the new `tamiza-ui` image. Members whose browser prefers Spanish get Spanish on their next load. To roll back, deploy the previous image. Operators who want a Spanish sign-in page turn on internationalization with Spanish in the Keycloak realm.
