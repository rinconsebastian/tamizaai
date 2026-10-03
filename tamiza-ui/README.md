# tamiza-ui

The Tamiza user interface: an Angular application served by nginx in the `ui` container. See the [repository README](../README.md) for the full stack.

```sh
npm ci
npm start                  # http://localhost:4200, proxies /api to the stack on http://localhost:8088
npm test -- --watch=false  # unit tests (Vitest)
npm run build              # production build in dist/
```

- User-facing text lives in `public/i18n/en.json` and is rendered through Transloco. Add a language by adding a file and listing it in `src/app/app.config.ts`.
- OIDC settings are loaded at runtime from `GET /api/v1/system/config`, so the same image works for any installation.
- `nginx.conf` and `nginx-security-headers.conf` configure the container: SPA fallback, the `/api/` proxy and the Content Security Policy.
