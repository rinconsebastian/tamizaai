# Tasks

**Order:**
- Groups 1 to 8 are the `api` work. Do them in order, and end each group with its tests green.
- Group 9 (deployment and docs) can follow group 8.
- Groups 10 and 11 are the `ui` work, in order.
- Group 12 is last.

**How to run the tests:**
- `dotnet test` in `tamiza-api/`. Integration tests start PostgreSQL, and in 1.4 Mailpit, with Testcontainers. Run one suite at a time.
- `npm test -- --watch=false` in `tamiza-ui/`. The two "stops polling when the page is destroyed" tests (`project-data.spec.ts` and `project-scripts.spec.ts`) are known failures.
- A single UI spec: `npx ng test --watch=false --include='src/app/<path>.spec.ts'`.

## 1. Email sending (api)

- [x] 1.1 Add MailKit to `Tamiza.Api.csproj`, pinned to the newest release that is at least two weeks old (design D1). Verify that `dotnet build` succeeds and that `dotnet list package` shows MailKit and MimeKit at that version.
- [x] 1.2 Add `Email/EmailAddressRule.cs`, `Email/EmailOptions.cs` and the options validator (design D1, spec `email-sending`).
  - **`EmailAddressRule`:** an address is at most 254 characters after trimming and passes `MailAddress.TryCreate(x, out var p) && p.Address == x`.
  - **`EmailOptions`:**
    - the eight `TAMIZA_SMTP_*` variables, with trimmed text, and the port and the timeout bound as `int?`;
    - `Enabled` when the host and the sender are both set;
    - the default port by security (587, 465 or 25) and the default timeout of 30 seconds.
  - **Validator rules:** the port is 1 to 65535; the security is `starttls`, `tls` or `none`; the sender passes `EmailAddressRule`; the timeout is 5 to 300; the user name and the password are set together.
  - **Startup warning:** for a host without a sender, or the reverse.

  Verify:
  - unit tests for each rule and default, including blanks for the port and the timeout, as Compose passes them;
  - integration tests that the `api` fails to start for *Port out of range*, *User name without password* and *Sender without a domain*;
  - a `CapturingLoggerProvider` test for *Partly configured*: one warning that names the missing variable.
- [x] 1.3 Add `IEmailSender`, `EmailResult` and `MailKitEmailSender` (design D1). The sender:
  - opens one connection per send, with `StartTls`, `SslOnConnect` or `None` and never `Auto`;
  - authenticates only when credentials are set;
  - applies the timeout;
  - maps exceptions by phase, as the table in D1 does, and rethrows a cancellation that comes from shutdown;
  - logs the host, the port and the outcome, never the password or the reply text.

  Register it only when email is enabled. Verify:
  - unit tests of the mapping for each row of the D1 table, including `NotSupportedException` at connect and at authenticate, `ServiceNotAuthenticatedException` (*Authentication required*), `SmtpCommandException` with `UnexpectedStatusCode` at connect, `SslHandshakeException`, `IOException` and `TimeoutException`;
  - a log line that holds neither the password nor the reply text (*Failure log*).
- [x] 1.4 Add `FakeEmailSender` to `Tamiza.Api.IntegrationTests/Infrastructure`. It keeps every `MimeMessage` with its envelope recipients and can be told to fail with a given outcome. Add `MailKitEmailSender` integration tests against Mailpit (`axllent/mailpit`, Testcontainers), configured per case as design D16 says. Verify, through Mailpit's API:
  - *Relay without authentication*, *Display name* and *Automatic message marked*, with the envelope recipients exactly the Bcc list;
  - *Default security and port*: with Mailpit offering STARTTLS with a certificate the test trusts and an auth file, the send upgrades and then authenticates;
  - *Server without STARTTLS* and *Untrusted certificate*: `unavailable`, and no message stored;
  - *Credentials refused*: `authFailed`;
  - *Recipient refused*: `rejected` naming the address, and no message stored;
  - *Server down*, using a `TcpListener` that never greets: `unavailable` after the 5-second timeout.
- [x] 1.5 Add `email: { enabled }` to `GET /system/config` (`SystemInfo/SystemEndpoints.cs`). Verify with `SystemConfigTests` cases for *Not configured* and *Configured*. The latter checks that the response holds no host, port, user name, password or sender.

## 2. Schema (api)

- [x] 2.1 Add `tamiza-api/src/dbUp/Scripts/0015_add_dashboard_reports.sql`, exactly as design D3 gives it: named constraints, `ix_dashboard_reports_updated_by`, and a comment that explains the table.

  Add the `DashboardReport` entity, its `DbSet`, and its mapping in `TamizaDbContext.cs`:
  - `List<string>` maps to `text[]`, `short[]` to `smallint[]` and `TimeOnly` to `time`;
  - `HasCheckConstraint` for `ck_dashboard_reports_recipients` and `ck_dashboard_reports_days`;
  - `HasDefaultValueSql("now()")` on `UpdatedAt` and `HasDefaultValue(false)` on `SendNowPending`;
  - a one-to-one foreign key to `dashboards` with cascade, and a foreign key to `users` with restrict.

  Verify:
  - a new `ScriptConventionTests` case, `The_dashboard_reports_script_names_every_constraint_and_index`, modelled on the 0013 case;
  - the `SchemaMigratorTests` upgrade from 0014;
  - `SchemaDriftTests`.
- [x] 2.2 Pin the dashboard lifecycle with integration tests:
  - removing a dashboard that has a report removes the report (*Dashboard removed*);
  - removing the project does the same;
  - duplicating a dashboard that has a report gives a copy with none (*Scheduled email not copied*).

  Verify that these tests pass with no change to `DuplicateAsync`.

## 3. Report data (api)

- [x] 3.1 Make `ReadConfig` and `ReadFilters` of `DashboardEndpoints` internal. Extract the inline widget ordering of `GetDashboardAsync` (`:73-77`) into an internal method, and have both callers use it (design D7). Verify that the existing dashboard tests pass unchanged.
- [x] 3.2 Add `Indicators/WidgetQuery.cs`, which turns a saved `WidgetConfig` into the `WidgetConfigInput` that `QueryAsync` takes, by chart type: a port of `widget-view.ts:25-43`. Add a comment on both sides that names the twin code. Verify unit tests that mirror `query-for.spec.ts` and cover each chart type, including:
  - a stat tile that loses a `groupBy`;
  - a bar chart that keeps a column sort;
  - a non-map widget that loses a geometry column;
  - a measure dropped when a ratio is set;
  - buckets kept only for a histogram.
- [x] 3.3 Add the applied-filters helper, a port of `appliedOf` from `dashboard-filters.ts:147-160`. It keeps filters that have a default, skips those the widget ignores, and passes `Values` or `Min`/`Max` by kind. Add the "not narrowed by" list, a port of `widget-view.ts:184-190`: filters with a default that the widget ignores, plus those the response lists in `NotFiltered`, by label and in bar order, with null counted as empty. Verify unit tests for:
  - *Default applied* and *Fixed date range*;
  - *Ignored filter*, including its note;
  - a filter without a default;
  - a filter whose column the table lacks, which gives the same note.
- [x] 3.4 Add `Reports/ReportDataBuilder.cs` and `ReportModel` (design D7). The builder:
  - loads the dashboard, its filters, its widgets in reading order, the indicators' label, format and unit (with the fallback for an unlisted indicator), and the publications;
  - runs each widget through `QueryAsync`, one at a time, with its chart type and the applied filters;
  - shapes each table like the UI's `table()` methods: the natural order of a line, histogram binning (a port of `chart-data.ts:33-50`), the pie share, a map with no geometry, and the "(none)" and "—" fallbacks;
  - collects every note of D7 step 6, and never dereferences a null `Response`.

  Verify:
  - unit tests of the binning and the natural order, on the same cases as `chart-scales.spec.ts`, which tests `histogram()` and `naturalCompare`;
  - integration tests over a seeded indicators schema for *Stat tiles first*, *Histogram*, *Pie chart*, *Map*, *Line chart order*, *Rows without a group value*, *Removed indicator*, *Indicator not published*, *Settings no longer fit* (a measure republished as text) and a widget with no rows;
  - *Read timed out*, with the lock recipe of design D16: only the locked widget gets the note, and the others have their tables.

## 4. Formatting and report text (api)

- [x] 4.1 Add `Reports/ReportFormat.cs` (design D8) with:
  - `count`, `decimal`, `percent` (built by hand, 0 to 2 decimals) and `currency` values in the report's `CultureInfo`;
  - units;
  - the plotted caption;
  - dates to the minute that name the month, with the project's time zone.

  Verify unit tests for:
  - *Spanish of Colombia*: `es-CO`, `decimal`, 12345.5 gives `12.345,50`;
  - *Percentage in Spanish of Colombia*: 0.455 gives `45,5%`;
  - *Value formatted*: `en-US` `42%`;
  - *Names kept*: an English report keeps the dashboard name "Vivienda";
  - the subject date `Oct 28, 2026`;
  - a `count` value with a unit.
- [x] 4.2 Add `Reports/ReportText.cs` with the `en` and `es` dictionaries, including the no-group, no-rows, not-published and note texts. Add the locale check that accepts a culture whose language is `es` or `en`. Verify:
  - a unit test that both dictionaries have the same keys;
  - a test that `fr-FR` and `xx` are refused;
  - a test that `es`, `es-MX` and `en-GB` are accepted.

## 5. Email body and workbook (api)

- [x] 5.1 Add `Reports/ReportHtml.cs` and the plain-text writer (design D2, D10). The body:
  - has inline styles and a single table layout;
  - holds the header, the stat tiles, then blocks of 10 rows with "N more rows", the notes and the footer;
  - takes `attachmentIncluded`, and says once whether the full tables are attached or the attachment was too large;
  - HTML-encodes every member-written or data value;
  - stays within the 80 KB budget.

  Verify unit tests for:
  - *Header* and *Long table*;
  - *Large dashboard*: the remaining widgets are listed by title as not shown in the body;
  - *Footer*;
  - *Text reader*;
  - both bodies with `attachmentIncluded: false`: the too-large note, and no wording that points to an attachment;
  - a widget title `<script>` that comes out encoded.
- [x] 5.2 Add `Reports/ReportWorkbook.cs` with MiniExcel (design D11):
  - a "Summary" or "Resumen" sheet in text;
  - one sheet per other widget with header rows;
  - the number formats of the D11 table through `DynamicExcelColumn.Format`;
  - unit headers, with a renamed header when two sheets need different formats;
  - unique sheet names that Excel accepts;
  - the file name, with `ExportFileName.Slug` gaining its `fallback` parameter.

  Verify unit tests that read the file back with MiniExcel and the cell styles from the package for:
  - *Summary sheet*, *Sheets*, *Whole table* and *Long title*;
  - *Scheduled email workbook*: an `es-CO` report's first sheet is "Resumen", and widget titles stay as written;
  - *Percentage cell*, which holds the number 0.42 styled `0.00%`;
  - *Tooltip column keeps its own format*, which holds 20 styled `#,##0`;
  - a histogram over a percent indicator whose counts are styled `#,##0`;
  - a pie share styled `0.0%`;
  - *Unit in the header*;
  - a dashboard named "住房", which gives `{project}_dashboard_{date}.xlsx`, while existing `ExportFileName` callers are unchanged.

## 6. Message and send results (api)

- [x] 6.1 Add `Reports/ReportMessage.cs`, which builds the `MimeMessage` (design D12):
  - From is the sender;
  - To is the empty group `undisclosed-recipients`;
  - Bcc holds the recipients;
  - Reply-To is the email of `updated_by`, when there is one;
  - the subject has CR and LF removed;
  - the HTML and text alternatives come with the XLSX attachment, when it is included.

  Verify unit tests for *Recipients hidden from each other*, *Sender not a recipient* (the envelope recipients exclude the sender), *Replies*, *Subject* and a subject in which CR and LF are stripped.
- [x] 6.2 Add `Reports/ReportSender.cs` (design D9, D10). For one dashboard it:
  - builds the model;
  - writes the workbook first, to `ExportRegistry.NewFilePath()`, and checks its size;
  - writes both bodies with `attachmentIncluded`, and sends;
  - updates only the result columns;
  - deletes the workbook in a `finally` block;
  - gives `reports.nothingToSend` by the rule of the spec.

  Verify integration tests with `FakeEmailSender` for:
  - *Empty dashboard* and *Nothing published yet*;
  - *Widget added later*: a widget added after the settings were saved is in the next email;
  - *Read timed out*: the email is still sent with the lock recipe;
  - every widget timed out, with `TestStatementSleep`, which gives `reports.nothingToSend`;
  - *Workbook too large*, with a lowered limit: no attachment, and the too-large note in the body;
  - *Failure shown* (`email.authFailed`) and *Failure cleared*;
  - *Recipient refused* (`email.rejected` with the address);
  - *Failure shown to members*: `last_failure` holds only the code;
  - no `.export` file left in the export directory.

## 7. Scheduler (api)

- [x] 7.1 Add `Reports/ReportScheduler.cs` and `ReportSignal`, modelled on `IngestionScheduler` (design D4 to D6). The scheduler:
  - returns, after logging once, when email is disabled;
  - waits for migrations;
  - ticks every 60 seconds or when woken;
  - runs a pass of steps, where each step claims a pending "send now" request, or else one due report through the atomic claim of D4, and sends it;
  - takes `@now` from `TimeProvider`.

  Register it in the api host. Verify integration tests with a fake `TimeProvider` and test-driven ticks for:
  - *Weekday schedule*, *Every day* and *Time zone changed*;
  - *Back the same day*, *Down for several days* and *Restart during a send*. For the restart, claim and then stop before sending;
  - *Paused* and *Turned on again*;
  - a Sunday-only schedule in `America/Bogota` around midnight UTC;
  - a save that pauses the report between the selection and the claim, after which nothing is sent.
- [x] 7.2 Add the save-time rule for `not_before` (design D4). Verify integration tests for:
  - *Saved after the time* and *Saved before the time*;
  - *Time corrected later the same day*;
  - *Paused edit, then activated*;
  - *Re-saved after the day's email*.

## 8. Report endpoints (api)

- [x] 8.1 Add `Reports/ReportEndpoints.cs` (design D13), with `GET`, `PUT` and `DELETE /projects/{projectId}/dashboards/{dashboardId}/report` and `POST .../report/send`. All require `RequireProjectRole(ProjectRole.Analyst)`. Errors are `dashboards.notFound` (`DashboardEndpoints.NotFoundCode`), `reports.unavailable`, `reports.none` and `reports.tooSoon`. Verify integration tests for:
  - *Email settings saved*, *One email per dashboard* and *Email removed*;
  - *Viewer refused*, *Not a member* and an `admin` allowed;
  - *Turned off*, which answers 503;
  - a change to a removed dashboard, which answers 404.
- [x] 8.2 Add validation for the recipients (through `EmailAddressRule`), weekdays, time and locale, with field errors (design D13). Verify integration tests for:
  - *Address of any domain*, *Invalid address*, *No domain or display name*, *Too many recipients* and *Same address twice*;
  - *No weekday*, an invalid time `25:00` and an unsupported locale.
- [x] 8.3 Add send-now with the 10-minute rule and the wake signal (design D5). Verify integration tests for:
  - *Send now*: 202, sent on the next step, still paused;
  - *Send now during a busy hour*: sent right after the report in progress, before the remaining due ones;
  - *No settings* (409 `reports.none`) and *Twice in a row* (409 `reports.tooSoon`);
  - *Schedule unchanged*.
- [x] 8.4 Check the OpenAPI document. Verify that `/api/v1/openapi.json` lists the four operations with their request and response bodies.

## 9. Deployment and docs

- [x] 9.1 Pass the eight `TAMIZA_SMTP_*` variables to `api` in `compose.yaml` with empty defaults (`${VAR:-}`). Document each in `.env.example`: its purpose, whether it is required and its default (port by security, timeout 30). Verify:
  - `docker compose config` renders with no `TAMIZA_SMTP_*` set and with all of them set;
  - the `api` starts healthy with them all blank.
- [x] 9.2 Add an "Email (optional)" section to `docs/operations.md` (design D15). It covers:
  - turning email on, and checking with *Send now*;
  - choosing a provider;
  - SPF, DKIM and DMARC;
  - the Microsoft 365 basic SMTP AUTH change;
  - free-tier quotas per Bcc recipient;
  - a sender address with no mailbox being fine;
  - checking delivery with Mailpit;
  - what leaves the deployment, with the data-protection note;
  - the `DROP TABLE` for a full revert.

  Verify that the Mailpit commands in the section run as written against a local stack.

## 10. UI service and configuration

- [x] 10.1 Add `email.enabled` to `SystemConfig`, and `emailEnabled` to `SystemConfigService`. A missing key reads as off. Verify that `system-config.spec.ts` covers enabled, disabled, a missing key and a failed request.
- [x] 10.2 Add `getReport`, `saveReport`, `removeReport` and `sendReportNow` to `IndicatorsService`, with their types. Verify with `indicators.service.spec.ts` cases for each URL, method and body.

## 11. UI dialog and menu

- [x] 11.1 Add `indicators/dashboard-report-dialog.ts`, modelled on `dashboard-filters-dialog.ts` (design D14). It has:
  - recipients, one per line or separated by commas or semicolons;
  - weekday toggles, Monday first;
  - a time input and the "Active" switch;
  - the project's time zone;
  - the last send and the last failure, translated from its code;
  - the warning about data leaving Tamiza and the note on fixed filter defaults;
  - *Save*, *Remove* (after a confirmation) and *Send now* (busy until the API answers, then "queued, will be sent shortly");
  - field errors from the API next to their field.

  The dialog sends `LOCALE_ID` as the locale. Verify with `dashboard-report-dialog.spec.ts` cases for:
  - *Warning shown*, *Error next to the field* and *Send now while busy*;
  - a new dialog with no settings;
  - each failure code translated;
  - *Remove*, which needs a confirmation.
- [x] 11.2 Add *Scheduled email…* after *Filters…* in `project-indicators.ts` `actions()`, shown only when `emailEnabled()`. A `case 'report'` opens the dialog. Verify with `project-indicators.spec.ts` cases for *Action offered*, *Not offered without email* and *No actions for a viewer*.
- [x] 11.3 Add the `dashboards.menu.report`, `dashboards.report.*` and failure-code texts to `public/i18n/en.json` and `es.json`. Verify that `translations.spec.ts` passes, so both files have the same keys.
- [x] 11.4 Check the dialog at phone width. Verify by running the UI against a local API and opening the dialog at 360 px in headless Chrome: every field and action is usable with no horizontal page scroll (*Phone width*).

## 12. Integration checks

- [x] 12.1 Run the full suites. Verify:
  - `dotnet test` in `tamiza-api/` is green, including the existing tests behind the unchanged scenarios of *Duplicating a dashboard*, *Roles for dashboards* and *Content shown as it was created*;
  - `npm test -- --watch=false` in `tamiza-ui/` is green apart from the two known polling failures;
  - `openspec validate add-dashboard-email-reports --strict` passes.
- [x] 12.2 Check on a separate Compose stack (`-p tamizaverify`) with Mailpit as the SMTP server (`TAMIZA_SMTP_SECURITY=none`) and a seeded dashboard that has stat, bar, pie, histogram and map widgets and a filter default. Verify in Mailpit's web UI:
  - *Send now* delivers one email whose envelope holds only the Bcc recipients;
  - the body shows the header, the stat values, the 10-row tables and the footer;
  - the XLSX opens with MiniExcel, with the expected sheets and numbers;
  - a scheduled time set two minutes ahead sends exactly one email.
- [x] 12.3 Ask the user to check delivery through their own SMTP server to a real Gmail and a real Outlook mailbox. Verify by the user's confirmation that:
  - the body reads correctly in both;
  - the XLSX opens without a repair prompt;
  - the message does not land in spam.

## 13. Fixes from the implementation review

- [x] 13.1 Make `EmailAddressRule` also require MimeKit to parse the address unchanged, so a saved recipient or `TAMIZA_SMTP_FROM` can always be sent to. Verify unit cases `ana.@partner.org` and `ana@partner.org.`, the sender case `reports.@example.org`, and the integration test *A local part ending in a dot is refused*.
- [x] 13.2 Bound connecting and signing in by the timeout as a whole, and each exchange while sending by `SmtpClient.Timeout` (spec *Send timeout*). Verify that the Mailpit tests, including *Server down*, still pass.
- [x] 13.3 Make removing an email a soft removal (`removed_at`, recipients cleared, paused), so saving it again keeps the day already sent and the 10-minute rule (spec *Removed and saved again*). Verify the integration tests *Removing and saving again keeps the send-now rule*, *Removing and saving again never sends a day twice* and *A removed email is not sent*, and that `SchemaDriftTests` pass.
- [x] 13.4 Refuse a recipient list longer than 40 entries before checking addresses, deduplicate with a `HashSet`, name every refused address in one message, and limit the PUT body to 64 KB. Verify *A huge list is refused before any address is checked* and *No domain or display name* (one message naming both).
- [x] 13.5 Resolve a locale the server has no regional data for to its language (`en-CO` → `en`). Verify the unit test *A region the server has no data for resolves to its language* and the integration test *A browser region without data is saved as its language*.
- [x] 13.6 Send only while the author is still an analyst or admin, or a superadmin, and record `reports.authorNoAccess` otherwise, with its en/es text (spec *Author who no longer manages the project*). Verify *An author who no longer manages the project sends nothing* (removed and demoted), *Another analyst saving it again takes it over*, and `error-codes.spec.ts`.

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied, and sync the `email-sending`, `dashboard-reports`, `custom-dashboards` and `interface-language` specs.
- Update the stored memory of this feature with the outcome.
