# Design

## Context

See proposal.md (Why) for the motivation. The current state that shapes the approach:

- **Roles.** `ProjectAccess.GetRoleAsync` resolves the caller's role. A superadmin is `admin` on every existing project. `RequireProjectRole(minimum)` returns 404 to non-members and a code-less 403 to members with a lower role. `POST /api/v1/projects` has no check beyond being signed in. `CreateAsync` validates the fields, applies the Kobo address policy, calls `kobo.VerifyAccessAsync` and then makes the creator `admin`.
- **Superadmins.** They are listed in `TAMIZA_SUPERADMIN_EMAILS`, which is optional and empty by default, and they need a verified email. `/me` returns `{id, name, email, isSuperAdmin}` from the token and does not read the database.
- **Settings in the UI.** The gear is the group item `TabsNav` draws for a tab with `children` (desktop anchor `settings-tab`, phone `<optgroup>`). `ProjectPage.tabs()` always appends it, built from `projectSections(id, isAdmin)`. The sections sit under an empty-path route whose component is `ProjectSettingsLayout`. That layout draws the pills and an outlet and has no role check. `ProjectContext`, which holds `myRole`, is provided by the `ProjectPage` component, not by the route. The page renders its outlet only after the details are loaded.
- **Leave guards.** The inline `canDeactivate` guards on `scripts` and `sampling-frame` call `component.confirmDiscard()` without a null check. Angular 22 passes `null` when the route's outlet was never activated.
- **Who reads what.** Every link from Performance, Clean data, the dashboards and the widgets into Settings is already gated to analyst or admin. Of the 30 viewer-level endpoints, 19 are called only from Settings pages and their dialogs. The project details are needed by the page header, the tabs and the dashboards. The UI reads their General-only values (`koboServerUrl`, `assetUid`, `formCheckedAt`, `enumeratorField`, `fieldCheck`, `tokenUnreadable`, `unavailableFormVersions`) only in General, in the admin-only token banner, and in `ProjectContext.update` to invalidate the field cache.
- **Error codes.** The UI translates a Problem Details `code` through `errors.codes.<code>` and shows any other text verbatim, marked as English.

## Goals / Non-Goals

**Goals:**

- The API decides both rules. The UI only mirrors them so users do not see actions or pages they cannot use.
- A viewer's journey through the dashboards makes no request that now answers 403.
- One source of truth for "can create projects", shared by the create endpoint and `/me`.

**Non-Goals:**

- A dedicated "project creator" role, or a Keycloak role or group for creation.
- Codes for the existing role 403 or for other English API texts (planned `add-api-message-codes`).
- Reacting live to role changes made by someone else in another session. They apply on the next load of the project or the list.

## Decisions

### 1. Creation check in `ProjectAccess`, run first in `CreateAsync`

Add `ProjectAccess.CanCreateProjectsAsync(ct)`: `user.IsAuthenticated && (user.IsSuperAdmin || ProjectMembers.Any(m => m.UserId == user.Id && m.Role == Admin))`. This mirrors the any-membership check that gates basemaps (`MapEndpoints`). `CreateAsync` calls it as its first statement. When it is false, it returns `Problems.Status(403, "project.create_not_allowed", "Not allowed.", "Only admins of a project and superadmins can create projects.")`. That happens before field validation, before the Kobo address policy and before `VerifyAccessAsync`. A constant `ProjectEndpoints.CreateNotAllowed` holds the code. Snake case matches its family (`project.last_admin`). Add `.ProducesProblem(403)` to the OpenAPI metadata.

- Pending invitations live in `project_invitations`, so they never count. An invitation is turned into a membership by the provisioning middleware on the first request with a verified email, so it counts from that same request.
- The membership is read on every request (only the user snapshot is cached), so losing the last admin role takes effect at once.
- Body binding runs before the handler. A malformed JSON body still gets 400. A well-formed body with invalid fields gets the 403. The specs promise only the latter.

Alternatives: an endpoint filter next to `RequireProjectRole` (no project id in the route, so a filter only adds indirection); a Keycloak realm role (adds Keycloak coupling, which the project keeps minimal); a configured allow-list of creators (a second list beside superadmins, with nothing gained).

### 2. Bootstrap through superadmins, documented, not enforced

A superadmin can always create, even when no project exists. That is how a new installation gets its first project. `TAMIZA_SUPERADMIN_EMAILS` stays optional. `README.md`, `.env.example`, `docs/keycloak.md` and `docs/operations.md` say it is needed to create the first project.

Alternatives: make the variable `[Required]` (existing installations that left it empty would stop starting, although they already have admins); let anyone create while no project exists (whoever signs in first takes the installation); log a warning at startup (needs a database read at startup and is noise for every existing installation).

### 3. `canCreateProjects` on `/me`, refreshed by the UI

`MeResponse` gains `CanCreateProjects`, computed by the same `CanCreateProjectsAsync`, so the handler becomes async. In the UI, `CurrentUser` gains `canCreateProjects`, and `CurrentUserService` gains:

- `refresh()`, which reads `/me` again and shares a request already in flight with `load()`;
- an awaitable accessor for guards;
- a `canCreateProjects` computed that is false while loading, on error and when the field is missing.

The project list calls `refresh()` when it opens, so a promotion or demotion made elsewhere shows on the next visit.

Alternative: derive the flag in the UI as `isSuperAdmin || list.some(p => p.myRole === 'admin')`. Rejected because it duplicates the rule, needs the list on `/projects/new`, and is wrong for a superadmin who is a viewer member (the list shows their membership role).

### 4. Creation entry points and the `/projects/new` guard

`ProjectListPage` shows the header "New project" and the empty state's "Create a project" only when `canCreateProjects` is true. Both are hidden while `/me` loads, so they do not flash. Without the right, the empty state shows a new text, `projects.emptyBodyNoCreate`, inviting the user to ask a project admin. A new functional guard `canCreateProjectsGuard` on `projects/new` waits for the profile. It returns `true` or a `UrlTree` to `/projects`, and also `/projects` when `/me` fails. A `UrlTree` redirect never commits `/projects/new` to history.

If the right is lost while the form is open, submitting gets the coded 403. `CreateProjectPage` already shows it, translated, in its form error. It also calls `refresh()`. The creator still lands on `/webhook` after creating, because a creator is always admin.

### 5. Settings left out of the tab row for viewers

`ProjectPage.tabs()` appends the Settings group only when `context.can('analyst')`. With no group item, `TabsNav` draws neither the gear nor the phone `<optgroup>`, so `TabsNav` itself does not change. It must not pass an empty `children` list, because `sections[0].link` would throw. `projectSections()` keeps its signature. Its doc comment now says the sections are for analysts and admins.

### 6. Direct configuration addresses: gate in the layout, then redirect

`ProjectSettingsLayout` gets `allowed = computed(() => context.can('analyst'))`. Its template renders the pills and the `<router-outlet>` only when `allowed()`, so for a viewer no section component is created and no section request is sent. An effect on `allowed()` navigates to `['/projects', id, 'performance']` with `replaceUrl: true` after render, like `FirstDashboard`. Because it is an effect, the same redirect also happens when the role in the context drops while a section is open. After a change to the user's own role, or a self-removal, Members calls `currentUser.refresh()` and `context.reload()`. That call applies the new details and then emits, unlike the fire-and-forget `refresh()`, which swallows errors. Members reloads its list only once the new details are applied, and only when the new role is at least `analyst`. So a self-demotion to analyst does not ask for the admin-only invitations with a stale role, and a self-demotion to viewer leaves Settings through the layout effect instead of showing 403 toasts. After a self-removal, `reload()` fails with 404 for a non-superadmin. Members then navigates to `/projects` with `replaceUrl: true` and shows no error. A superadmin still resolves as admin, so they stay and the list reloads.

The leave guards in `app.routes.ts` become null-safe (`component?.confirmDiscard() ?? true`). Otherwise the redirect away from `/scripts` or `/sampling-frame`, whose outlet never activated, would throw and leave the viewer stuck.

Alternatives: a `canActivate`/`canMatch` guard on the layout route (it cannot inject the component-provided `ProjectContext`, so it would need a second `GET /projects/{id}` and would break the single-request test harness); an in-page notice like `webhook.adminsOnly` (no tab would be current, which conflicts with the navigation requirement; the user chose the redirect).

### 7. Settings-only reads move to `analyst`

These mappings change from `RequireProjectRole(ProjectRole.Viewer)` to `Analyst`:

| Area | Endpoints |
|---|---|
| Members | `GET /members` |
| Sampling frame | `GET /sampling-frame`, `POST /sampling-frame/export` |
| Ingestion | `GET /ingestion`, `GET /syncs`, `GET /form-versions` |
| Pipeline | `GET /runs`, `GET /runs/{runId}`, `GET /pipeline` |
| Reference tables | `GET /reference-tables`, `GET /reference-tables/{name}` |
| Scripts | `GET /scripts`, `GET /scripts/{kind}/versions`, `GET /scripts/{kind}/versions/{number}`, `GET /scripts/indicators/{name}/versions`, `GET /scripts/indicators/{name}/versions/{number}`, `GET /script-inputs` |
| Form | `GET /form-fields`, `GET /form-choices` |

The project details, Performance, Clean data (`/data`), the indicators endpoints and `GET /dashboards/{id}` stay at viewer. So does `GET /indicators/{name}/columns`, a dashboard endpoint that only the analyst widget editor calls today. The resulting 403 keeps today's code-less shape. No viewer UI calls these endpoints, so there is nothing to translate. Export links already issued (`/data-exports/{token}`) stay valid until they expire.

Alternative: one shared "Settings data" filter. Rejected because each endpoint already declares its own minimum role, and changing the argument is clearer and easier to review.

### 8. A viewer's project details leave out the General-only values

`ProjectDetails` makes `KoboServerUrl`, `AssetUid`, `FormCheckedAt`, `EnumeratorField`, `FieldCheck`, `TokenUnreadable` and `UnavailableFormVersions` nullable. `DetailsAsync` fills them only when the resolved role is at least `analyst`. For a viewer they are `null` (serialized as JSON `null`). For a viewer, the handler also skips the token decryption, the unavailable-version count and the frame-dimension read. Name, form name, `myRole`, `createdAt`, `timeZone` and `dashboards` stay for everyone. Responses for analysts and admins keep their current shape, including the create and update responses (admin only). In the UI, `ProjectDetails` marks these fields `| null`. General, the token banner and `ProjectContext.update` read them only for analysts and admins (`null === null` keeps the cache for viewers).

Alternatives: move these values into a nested `configuration` object that is null for viewers (cleaner type, but it changes the payload for every role and every UI fixture); a separate `GET /projects/{id}/configuration` (one more request for General and its tests).

### 9. Test infrastructure

- **API.** `IngestionHarness` sets `TAMIZA_SUPERADMIN_EMAILS=ada@example.org` by default, and its `settings` can override it. In the 13 files that use it, ada is already the admin of the only project, and their 404 checks use other users. Seeding an admin membership in an extra project was rejected: the scheduler would synchronize that project (`next_sync_at` defaults to now), and many queries read `tamiza.projects` without a filter. `ProjectsApiTests` does the same, with a separate factory without the default for the creation-rule tests. `The_list_shows_own_projects_and_superadmins_see_all` is reworked. The viewer-read tests of the moved endpoints become viewer-403 tests, plus an analyst read where none exists. `MembersApiTests.Members_are_listed_to_viewers_and_invitations_only_to_admins` already uses an analyst; it is renamed and gains a viewer 403.
- **UI.** `testing/project-harness.ts` gains a way to answer `/me` (or stub `CurrentUserService`) for route specs. The section specs that open a section as `viewer` switch to `analyst`, or become layout-redirect tests in `project-settings-layout.spec.ts`. In `project-page.spec.ts`, assertions on the missing gear use `toBeUndefined()`/absence, not `getAttribute(...)` being null.

### 10. Viewer-only branches inside Settings are removed

The branches and texts that only a viewer could reach inside Settings are removed together with their en/es keys: `overview.hints.defineSamplingFrameViewer`, `overview.hints.updateSamplingFrameViewer`, `data.versions.retryHintViewer`, `frame.emptyViewer`, `reference.emptyViewer`, `scripts.noIndicators`. The translations spec checks that both languages have the same keys, but nothing detects unused keys. Kept: `webhook.adminsOnly` (analysts), `overview.hints.selectEnumeratorFieldViewer` (shown to analysts despite its name), the viewer texts on Performance, Clean data and the dashboards, and the `canEdit`/`viewing()` logic of Scripts. Viewing an older version still makes the editor read-only.

## Risks / Trade-offs

- [Scripts or integrations that read Settings endpoints with a viewer account get 403] → Note it in the upgrade notes of `docs/operations.md`; such clients need an analyst account.
- [An installation without a configured superadmin, or one whose identity provider sends no `email_verified`, cannot create a new project once no admin exists] → Documented in README, `.env.example` and `docs/keycloak.md`. Existing installations already have project admins.
- [The cached `canCreateProjects` is stale after a promotion or demotion during the session] → The API decides; the list refreshes the profile when it opens; the form shows the translated 403 and refreshes.
- [The redirect leaves a section whose outlet never activated, and a leave guard receives `null`] → Null-safe guards, covered by a layout spec that redirects from `/scripts` and `/sampling-frame`.
- [Viewers still see data that Settings also shows] → Accepted. The Performance coverage card shows the frame's combinations and targets, `GET /indicators` returns indicator descriptions and run ids, and Performance and Clean data show the time zone and the publish time. These belong to the dashboards viewers keep.
- [A superadmin who is a viewer member sees the badge "viewer" in the list but gets the gear and can create projects] → This mismatch already exists (the list reports the membership role). Unchanged.
- [Admins now pass on the right to create projects, together with the Kobo address reach that comes with it, whenever they make someone admin] → Accepted; that is the rule asked for. `docs/operations.md` already warns about private networks.

## Migration Plan

1. Ship API and UI together (same Compose release). No database migration.
2. Operators of a new installation set `TAMIZA_SUPERADMIN_EMAILS` before the first sign-in. Existing installations need no change. Their users who are not project admins lose project creation at once, and viewers lose Settings.
3. Rollback: redeploy the previous images. No data changes, so nothing to undo.
