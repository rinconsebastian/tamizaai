# Proposal

## Why

Tamiza's first users are consultants and analysts in Latin America who run their Kobo surveys in Spanish, and today the whole interface is English. The UI already keeps its text in one translation file, so adding Spanish is mostly translation work. The rest is fixing the code that assumes English: plurals chosen with `count === 1`, sentences built from translated fragments, a date pattern in English order, and formatting hard-coded to `'en'`. The text the API and the worker write stays English for now. Translating it needs message codes in two backends, and this change stays inside the UI.

## What Changes

- The UI speaks Spanish as well as English. A new `public/i18n/es.json` holds every key of `en.json`, written in neutral Latin-American Spanish with *tú* and terms aligned with KoboToolbox's Spanish (*envío*, *encuestador*, *marco muestral*, *tablero*).
- **Language choice.** The language is chosen when the page loads, from the browser's preferred languages taken in order: the first Spanish or English entry wins, and English is used when there is neither. There is no language switcher and nothing is stored. The page's `lang` attribute follows the chosen language, and the sign-in request asks the identity provider for the same language (`ui_locales`).
- **Regional formats.** Dates, times, numbers, percentages, durations and file sizes follow the region of the browser entry that was chosen (`es-CO` → `12.345,5` and `9:30 p. m.`; `es-ES` → 24-hour clock; `es-MX` → decimal point). When English is used as a fallback, formats are `en-US`. The shared date-and-time format moves from a fixed English pattern to the browser's own order for that region. Angular's locale-data pipes, `toFixed` and the hard-coded `'en'` calls are replaced by `Intl`.
- **Grammar fixes Spanish needs, which also fix English.** Counts that can be 1 get a singular form, which also fixes "every 1 hours". Sentences assembled from translated fragments become whole sentences wherever Spanish needs agreement.
- **Hard-coded English left in the code goes through translations:**
  - the toast's "Dismiss" label;
  - the script editor's autocomplete details;
  - the editor's built-in search, fold and lint text;
  - the units in durations and file sizes.
- **Backend text is unchanged.** The UI shows it as received, in English, marked as English for assistive technology. This covers:
  - field validation messages;
  - API errors that carry no code;
  - run and test errors and logs, and sample-download errors;
  - downloads (sampling-frame workbook, sample kit, CSV and XLSX exports);
  - the default dashboard name "Indicators".

  The messages the UI already writes for API error codes (`errors.codes.*`) are UI text, so they appear in Spanish.
- **Tests:**
  - every `es.json` key must match its `en.json` key and have the same parameters;
  - language choice and regional formats are unit tested;
  - a few screens are rendered in Spanish.

  The existing English specs stay pinned to `en` / `en-US`.

Out of scope: a language switcher or a saved preference; translating API or worker messages, the frame workbook, the sample kit or the exports; Spanish reserved dashboard names; choosing which language of a Kobo form to show labels in; other languages.

## Capabilities

### New Capabilities
- `interface-language`: which language and regional formats the UI uses, what text is translated, and what text stays as the backend wrote it.

### Modified Capabilities
- `web-ui`: two requirements change.
  - "Feedback on failed actions": the UI writes its own messages in the interface language, and shows messages written by the API as received.
  - "Date and time display": the shared format follows the browser's language and region.

## Impact

- **`tamiza-ui` code:**
  - `app.config.ts`: Transloco languages, and `LOCALE_ID` set to the resolved locale.
  - A new language resolver in `core/i18n`.
  - `index.html` / bootstrap: the `lang` attribute.
  - `core/auth/oidc-config.ts`: `ui_locales`.
  - `shared/ui/date-format.ts`, `indicators/format.ts` and the chart, map and tooltip formatters.
- **Formatting call sites:**
  - about 33 `| date`, 34 `| number` and 1 `| percent`;
  - 4 `DecimalPipe` sites;
  - the hard-coded `'en'` / `toFixed` sites (`project-scripts.ts`, `sample-download-panel.ts`, `script-assist.ts`, `assistant-panel.ts`, `pipeline-runs.util.ts`, `assistant-context-dialog.ts`).
- **Text call sites:**
  - about 18 plural sites;
  - the fragment-composition sites;
  - `shared/ui/toast.ts` and `shared/ui/code-editor.ts`;
  - the places that show backend text, which get `lang="en"`.
- **Translation files:**
  - `public/i18n/es.json` is new, about 1,220 strings;
  - `en.json` gains singular keys and a few reworded sentence keys;
  - `src/testing/transloco-testing.ts` takes a language.
- **Checks:** new specs, and `scripts/check-ui-safety.sh` rejects Angular's locale-data pipes and hard-coded `'en'` formatting.
- **`tamiza-api` and `tamiza-analytics`:** no change. `ErrorCodeTranslationTests` keeps reading `en.json`.
- **No new dependencies.** nginx already serves `/i18n/` with `no-cache`, so `es.json` needs no configuration.
- **Keycloak** shows a Spanish sign-in page only when the realm has Spanish enabled, which is an operator setting.
- **Docs:** README, plus the conventions line in `docs/tamiza-brief.md` and the `openspec/config.yaml` context.
