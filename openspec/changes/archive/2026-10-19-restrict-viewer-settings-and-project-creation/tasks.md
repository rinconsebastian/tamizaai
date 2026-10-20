# Tasks

## 1. API: who can create projects

- [x] 1.1 Add `ProjectAccess.CanCreateProjectsAsync(ct)`: signed in and either superadmin or `admin` in `project_members`, so pending invitations never count. Update the class summary. Verify with the creation tests in 1.4.
- [x] 1.2 In `ProjectEndpoints.CreateAsync`, run the check as the first statement, before `FieldErrors`, the Kobo address policy and `kobo.VerifyAccessAsync`. On refusal return `Problems.Status(403, ProjectEndpoints.CreateNotAllowed = "project.create_not_allowed", "Not allowed.", …)`. Add `.ProducesProblem(StatusCodes.Status403Forbidden)` to `CreateProject`. Verify that the OpenAPI document lists 403 for `CreateProject`.
- [x] 1.3 Add `CanCreateProjects` to `MeResponse` and make the `/me` handler async, using the same `CanCreateProjectsAsync`. Verify with `MeTests`: add the field to its `Me` record; true for a superadmin with no projects and for an admin member; false for a new user, an analyst-only or viewer-only member, an unverified listed email and a pending admin invitation; an admin member reads true, another admin changes their role to analyst, and the next `/me` reads false.
- [x] 1.4 Test infrastructure and creation tests:
  - `IngestionHarness` defaults `TAMIZA_SUPERADMIN_EMAILS=ada@example.org` (callers can override it) and says why in its summary.
  - `ProjectsApiTests.FactoryAsync` gets the same default, and a factory without it serves the creation-rule tests.
  - `The_list_shows_own_projects_and_superadmins_see_all` seeds its projects with `TestData` instead of creating them.
  - New tests: no membership, analyst-only and viewer-only users get 403 with `project.create_not_allowed`, the project count is unchanged and the fake Kobo logged no request; a well-formed body with invalid fields also gets 403, not field errors; an admin of a seeded project gets 201; a verified superadmin on an empty database gets 201 and becomes admin; an unverified sign-in with a pending admin invitation gets 403, then 201 after a verified sign-in; an admin demoted to analyst, and an admin removed from their only admin project, each get 403 `project.create_not_allowed` on the next attempt.
  - Verify: `dotnet test` in `tamiza-api/` passes for `ProjectsApiTests`, `MeTests` and the 13 harness-based files.
- [x] 1.5 Document the creation rule and the bootstrap:
  - `README.md` quick start: set `TAMIZA_SUPERADMIN_EMAILS` to your verified email before the first sign-in.
  - `.env.example`: what superadmins are for, and that without one nobody can create the first project.
  - `docs/keycloak.md`: lines 25, 133 (expected `/me` answer with `canCreateProjects`), 252 and the troubleshooting table (no New project button).
  - `docs/operations.md`: the Install section (lines 16-24) also sets `TAMIZA_SUPERADMIN_EMAILS` before the first sign-in, and lines 131-133 say who can create projects.
  - `docs/kobo.md:11`, `:31` and `:149`.
  - Verify by reading each changed passage against the projects and auth delta specs.

## 2. API: Settings data limited to analysts and admins

- [x] 2.1 Change the minimum role from `Viewer` to `Analyst` on the 19 mappings listed in design.md §7. These are in `MemberEndpoints`, `SamplingFrameEndpoints`, `IngestionEndpoints`, `RunEndpoints`, `ReferenceTableEndpoints`, `ScriptEndpoints`, `ScriptInputEndpoints` and `ProjectEndpoints` (form-fields and form-choices). Leave project details, Performance, `/data`, indicators and `GET /dashboards/{id}` at `Viewer`. Verify with the role matrix in 2.2.
- [x] 2.2 Add `SettingsRoleMatrixTests`, modeled on `IndicatorRoleMatrixTests` and built on `CleanDataHarness`. For every moved endpoint: viewer 403, analyst and admin get their normal answer, non-member 404. For the project details: viewer 200. Verify: the theory passes.
- [x] 2.3 Update the existing tests that read these endpoints as a viewer. Each read moves to an analyst, gains a viewer-403 assertion, and gets a new name where the old one says viewer:
  - `MembersApiTests.Members_are_listed_to_viewers_and_invitations_only_to_admins`
  - `FormVersionsApiTests.A_viewer_reads_the_versions_with_their_counts_and_states`
  - `IngestionApiTests` (status and log reads; extend `Viewers_cannot_start_runs_and_non_members_get_404`)
  - `ReferenceTablesApiTests` (`An_analyst_uploads_a_table_that_members_list_and_preview`, `A_viewer_cannot_upload_replace_or_delete`)
  - `RunsApiTests` (list, details, indicator steps)
  - `ScriptsApiTests` (`Viewers_read_scripts_but_cannot_save_and_non_members_get_404`, `A_viewer_reads_indicator_scripts_but_cannot_save_remove_or_restore_them`)
  - `ScriptInputsApiTests` (`A_viewer_sees_the_same_tables_as_an_analyst`, and the viewer read in `Reference_tables_and_form_questions_are_listed_whichever_script_is_selected`)
  - `SamplingFrameApiTests` (the four viewer frame reads)
  - `SamplingFrameFilesApiTests` (`Viewer_downloads_the_frame`, `No_frame_yet`, `Frame_without_rows`, `Choices_listed`)
  - `ProjectsApiTests.Form_fields_are_readable_by_viewers`
  - Verify: these files pass.
- [x] 2.4 Make the General-only values of `ProjectDetails` nullable: `KoboServerUrl`, `AssetUid`, `FormCheckedAt`, `EnumeratorField`, `FieldCheck`, `TokenUnreadable`, `UnavailableFormVersions`. `DetailsAsync` fills them only for `analyst` and above. For a viewer it skips the token decryption, the unavailable-version count and the frame-dimension read. Verify with a new `ProjectsApiTests` case: a viewer's details carry name, form name, role, time zone and dashboards, and `null` for each General-only value, while an analyst's carry them all. Also switch the viewer details read in `SamplingFrameApiTests.The_field_check_reports_coverage_unavailable_until_a_frame_is_saved_and_when_a_field_leaves_the_form` to an analyst.
- [x] 2.5 Add an upgrade note to `docs/operations.md`: non-admins lose project creation, and viewer accounts get 403 on Settings data, so API clients need an analyst account. Verify by reading it against the proposal's Impact section.

## 3. UI: project creation offered only to those who may create

- [x] 3.1 Extend `CurrentUserService`:
  - `CurrentUser.canCreateProjects`;
  - `refresh()`, which re-reads `/me` and shares a request already in flight with `load()`;
  - an awaitable accessor for guards;
  - a `canCreateProjects` computed that is false while loading, on error and when the field is missing.

  Add `current-user.service.spec.ts` covering refresh, the shared request and the flag. Update the `CurrentUser` literals in `app.spec.ts`, `app-language.spec.ts` and `project-members.spec.ts`. Verify: those specs pass.
- [x] 3.2 Add `canCreateProjectsGuard` on `projects/new`. It waits for the profile and returns `true`, or a `UrlTree` to `/projects` when the flag is false or `/me` fails. Add a guard spec. Restructure `create-project.spec.ts` so its `open()` answers `/me`, and add: no right → the list, with no form rendered. Verify: both specs pass.
- [x] 3.3 `ProjectListPage`:
  - Show "New project" (`new-project`) and the empty state's "Create a project" only when `canCreateProjects`, hidden while `/me` loads.
  - Without the right, the empty state shows `projects.emptyBodyNoCreate` and no link.
  - Call `refresh()` when the page opens.
  - Add a `/me` helper to `testing/project-harness.ts`.
  - Extend `project-list.spec.ts`: buttons hidden without the right, empty state without a link, nothing until `/me` arrives, refresh on open.

  Verify: the spec passes.
- [x] 3.4 Add `errors.codes.project.create_not_allowed` and `projects.emptyBodyNoCreate` to `public/i18n/en.json` and `es.json`, and add the code to `core/i18n/error-codes.spec.ts`. On a 403 with that code, `CreateProjectPage` refreshes the current user. Test in `create-project.spec.ts`, with the app in Spanish (`configureApp([], 'es')`):
  - the Spanish text of the code appears in `form-error` without a `lang` attribute;
  - there are no field errors and no navigation;
  - a new `GET /api/v1/me` is sent after the refusal.

  Verify: `translations.spec.ts`, `error-codes.spec.ts` and `create-project.spec.ts` pass.

## 4. UI: Settings closed to viewers

- [x] 4.1 In `ProjectPage.tabs()`, append the Settings group only when `context.can('analyst')`. Update the `projectSections()` doc comment. Update `project-page.spec.ts`: the Settings-dependent viewer cases become analyst cases. Add viewer cases: no `settings-tab` and no "Settings"/"Configuración" in the tab row; no `<optgroup>` in the phone select; Performance, Clean data and dashboards unchanged. Use absence (not a null attribute) for the missing gear. Verify: the spec passes.
- [x] 4.2 Add viewer cases to `project-performance.spec.ts`, `project-clean-data.spec.ts` and `project-indicators.spec.ts` (a dashboard with a widget). Each opens the page as a viewer without the harness's automatic `/form-fields` answer, flushes the page's own requests, and then asserts two things: no request was made to any of the 19 moved paths (`http.match` on `members|sampling-frame|ingestion|syncs|form-versions|runs|pipeline|reference-tables|scripts|script-inputs|form-fields|form-choices` is empty, or `http.verify()`), and no error toast appears. Verify: the three specs pass.
- [x] 4.3 Gate `ProjectSettingsLayout` with `allowed = computed(() => context.can('analyst'))`: no pills and no `<router-outlet>` when not allowed. An effect navigates to `performance` with `replaceUrl: true`. Make the `canDeactivate` guards of `scripts` and `sampling-frame` in `app.routes.ts` null-safe. Add to `project-settings-layout.spec.ts`:
  - For each of overview, data, scripts, reference, sampling-frame, members and webhook: a viewer lands on `/projects/p1/performance` with `replaceUrl`, no section element, no Settings nav and no request to any Settings endpoint.
  - The redirects from scripts and sampling-frame end without a navigation error.
  - A role lowered to viewer while a section is open redirects.
  - An analyst still gets the pills and the outlet.

  Verify: the spec passes.
- [x] 4.4 In `ProjectMembers`, after a change to the user's own role or a self-removal, call `currentUser.refresh()` and `context.reload()`. Reload the member list only after the new details are applied, and only when the new role is at least `analyst`. When `reload()` fails with 404 after a self-removal, navigate to `/projects` with `replaceUrl: true`, with no error toast. Add `project-members.spec.ts` cases:
  - An admin who makes themselves analyst: `GET /projects/p1` is answered first, then only `GET /members` follows (no `GET /invitations`), with no toast.
  - An admin who makes themselves viewer: they land on performance with `replaceUrl`, with no further `GET /members` and no toast.
  - A non-superadmin admin of a two-admin project who removes themselves: they land on `/projects` with `replaceUrl`, with no further `GET /members` and no toast.
  - A superadmin who removes their membership: they stay on Members and the list reloads.

  Verify: the spec passes.
- [x] 4.5 Mark the General-only fields of the UI `ProjectDetails` as `| null`. Keep General, the token banner and `ProjectContext.update` correct with `null`. Make the harness `details('viewer')` fixture send `null` for them, as the API does. Verify: the build type-checks, and `project-context` and `project-overview` specs pass.
- [x] 4.6 Update the section specs that open a section as `viewer`. Generic cases switch to `analyst`. Viewer-only cases are deleted, or rewritten as "older version on view" in Scripts. The specs: `project-overview`, `project-data`, `project-scripts`, `project-reference`, `project-sampling-frame`, `project-members`, `project-webhook` (keep the analyst half of `adminsOnly`). Verify: each spec passes, apart from the two known polling failures recorded for this environment.
- [x] 4.7 Remove the viewer-only branches and their en/es keys: `overview.hints.defineSamplingFrameViewer`, `overview.hints.updateSamplingFrameViewer`, `data.versions.retryHintViewer`, `frame.emptyViewer`, `reference.emptyViewer`, `scripts.noIndicators`. Keep `webhook.adminsOnly`, `overview.hints.selectEnumeratorFieldViewer` and the dashboard viewer texts. Verify: `translations.spec.ts` passes and a grep of `src/` finds no removed key.
- [x] 4.8 Update the docs that describe what viewers see:
  - `docs/kobo.md`: lines 72, 93, 110, the roles table at 135-141 (viewer: dashboards only; analyst: every Settings section but Webhook; admin: also creates projects; superadmin), 153 and 157.
  - `docs/scripts.md`: lines 5, 185, 243, 249, 498 and 556.
  - `docs/indicators-dashboard.md:9` and `:11`: Settings, and the phone select's Settings group, are only for analysts and admins.
  - `docs/performance-dashboard.md:9`: the Form check card in General is for analysts and admins; viewers see the per-part messages.
  - The Settings clause of `README.md:7`.

  Verify by reading each passage against the web-ui, projects, ingestion and pipeline delta specs.

## 5. Integration checks

- [x] 5.1 Run the full .NET suite (`dotnet test` in `tamiza-api/`, one run at a time) and the full UI suite (`npm test -- --watch=false` in `tamiza-ui/`). Verify: everything passes except the two known UI polling failures.
- [x] 5.2 Check on a separate verification stack (`-p tamizaverify`, fake OIDC and fake Kobo):
  - On an empty database, a verified superadmin creates the first project, and a user who is not one sees no create action and is sent from `/projects/new` to the list.
  - An admin of that project creates a second one; an analyst cannot.
  - A viewer sees no gear on desktop or phone width, lands on Performance from a bookmarked `/members`, and gets 403 from `GET /members` and `GET /form-fields` with curl.
  - A viewer's `GET /projects/{id}` has no Kobo server or field check.
  - With the browser's network log open, a viewer goes through Performance, Clean data and each dashboard without a single 403.
  - An analyst still sees every section except Webhook.

  Verify: each check behaves as described; tear the stack down afterwards.
- [x] 5.3 Run `openspec validate restrict-viewer-settings-and-project-creation --strict`. Verify: no errors.

## Workflow follow-up

- Archive the change after the user has reviewed the manual checks.
