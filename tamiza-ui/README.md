# tamiza-ui

The Tamiza user interface: an Angular application served by nginx in the `ui` container. See the [repository README](../README.md) for the full stack.

```sh
npm ci
npm start                  # http://localhost:4200, proxies /api to the stack on http://localhost:8088
npm test -- --watch=false  # unit tests (Vitest)
npm run build              # production build in dist/
```

- User-facing text lives in `public/i18n/en.json` and `public/i18n/es.json` and is rendered through Transloco. Both files hold every key, with the same `{{ param }}` names; `src/app/core/i18n/translations.spec.ts` fails when they drift. The language is chosen once at load from the browser's languages (`src/app/core/i18n/interface-language.ts`), with no switcher. To add a language, add its file, list it in `interfaceLanguages` and translate every key.
- Numbers, dates, times, durations and sizes go through `Intl` with the locale the browser chose (`shared/ui/formats.ts` and its pipes), never through Angular's locale-data pipes (`date`, `number`, `percent`), which throw for a locale that is not registered; `npm run check:safety` rejects them.
- Text written by the API and the analytics worker is shown as received, in English, and marked `lang="en"`.
- OIDC settings are loaded at runtime from `GET /api/v1/system/config`, so the same image works for any installation.
- `nginx.conf` and `nginx-security-headers.conf` configure the container: SPA fallback, the `/api/` proxy and the Content Security Policy.
