# Design

## Context

See proposal.md for the motivation. The specs (`email-sending`, `dashboard-reports`, and the `custom-dashboards` delta) define the behavior. This document covers how to build it.

What exists and is reused:

- **Widget data on the server.** `IndicatorDashboard.QueryAsync(projectId, WidgetConfigInput, chartType, ct, dashboardFilters)` (`Indicators/IndicatorDashboard.cs:112`) is a singleton and needs no user or `HttpContext`. It runs in the clean data dashboard's query slots: 4 at a time, a 25 s queue and statement timeout, and at most 5000 rows. `GetPublicationsAsync` (`:40`) gives each indicator's publication time. `ScriptEndpoints.CurrentIndicatorsAsync` (`Pipeline/ScriptEndpoints.cs:51`) gives each indicator's label, format and unit.
- **Dashboard reading.** `DashboardEndpoints` reads widget configs and filters with private helpers: `ReadConfig` and `ReadFilters` (`:715-721`), and the reading order (`:73-77`).
- **XLSX.** MiniExcel 1.46 already writes multi-sheet workbooks (`Projects/SamplingFrameWorkbook.cs:177-188`). `XlsxExport.SheetName` (`CleanData/XlsxExport.cs:87-96`) makes valid sheet names, and `ExportFileName.Slug` (`CleanData/ExportSweeper.cs:51`) makes file names.
  - A probe during planning confirmed that `DynamicExcelColumn.Format` gives numeric cells an Excel number format across sheets: `0.##%` and `#,##0` came out as styles on the right cells.
  - The format is keyed by the header text for the whole workbook.
- **Background work.** `IngestionScheduler` (`Ingestion/IngestionScheduler.cs`) is the pattern to copy:
  - a `BackgroundService` that waits on `MigrationStatus`;
  - a tick, plus a bounded channel (`IngestionSignal`) to wake it early.
- **Optional features.** `AssistantOptions` and `AssistantRegistration` show the pattern for a feature that is on only when configured:
  - flat `TAMIZA_*` variables, a validator that names the variable, a startup warning for partial settings;
  - a flag in `GET /system/config` (`SystemInfo/SystemEndpoints.cs`) and in the UI's `SystemConfigService` (`core/system-config.ts`).
- **What Tamiza cannot do today.** Tamiza has no SMTP client. Keycloak's account emails reach only Keycloak users and carry no attachments. `add-keycloak-invitation-email` recorded "sending email from Tamiza itself" as a non-goal, and this change reverses it.
- **Constraints.**
  - One `api` replica.
  - Project time zones are IANA names validated against PostgreSQL's `pg_timezone_names`.
  - Users are never deleted: `dashboards.updated_by` uses `ON DELETE RESTRICT`.
  - The UI's `LOCALE_ID` is the browser entry that chose the interface language (`app.config.ts:44`).

## Goals / Non-Goals

**Goals:**
- No rendering engine and no new container. The only new dependency is MailKit.
- Each widget table matches what the widget's "Show as table" view shows. The C# code that reproduces the UI's steps is small, and tests pin it to the UI's behavior.
- An email is never sent twice for the same scheduled day, even across restarts.

**Non-Goals:**
- Charts, images, maps drawn as maps, PDF, or Excel charts.
- Relative date filters, several schedules per dashboard, or several times a day.
- Automatic retries, bounce processing, unsubscribe links, or per-recipient tracking.
- OAuth (XOAUTH2) for Microsoft 365. Only a user name and password are supported.
- A recipient domain allow-list. The user chose to allow any address.
- An audit trail. The table records who last saved the settings; audit events arrive with `add-audit-log`.

## Decisions

### D1. Tamiza sends through the operator's SMTP server with MailKit

`Email/` holds the parts:
- `EmailOptions`, read from the variables `TAMIZA_SMTP_HOST`, `_PORT`, `_SECURITY` (`starttls|tls|none`), `_USERNAME`, `_PASSWORD`, `_FROM`, `_FROM_NAME` and `_TIMEOUT_SECONDS` (default 30). Compose passes blanks for unset values, so the port and the timeout bind as `int?`, as `KeycloakAdminOptions.LinkHours` does;
- `EmailAddressRule`, one shared check for `TAMIZA_SMTP_FROM` and for recipients. After trimming, an address has at most 254 characters and passes `MailAddress.TryCreate(x, out var p) && p.Address == x`, the check `MemberEndpoints.cs:73` already uses. That refuses `luis` (no domain) and `Ana <ana@x.org>` (a display name). The address must also pass MimeKit's `MailboxAddress.TryParse` unchanged, since the message builder uses MimeKit and refuses forms .NET accepts, such as `ana.@x.org`. MimeKit alone would not do: by default it accepts `luis`;
- `EmailOptionsValidator`;
- a startup check that warns once when the host and the sender are not both set;
- an `IEmailSender` seam with a single method, `SendAsync(MimeMessage, ct)`, which returns `EmailResult` (sent, `unavailable`, `authFailed` or `rejected` with the refused address).

The MailKit implementation behaves as follows:
- It opens a connection per send. There are a few sends a day, so pooling would buy nothing.
- With `starttls` it uses `SecureSocketOptions.StartTls`, which fails when the server does not offer STARTTLS. With `tls` it uses `SslOnConnect`. With `none` it uses `None`. It never uses `Auto`, which would fall back to plain text silently.
- It authenticates only when credentials are set.
- The timeout bounds connecting and signing in as a whole, through a linked token. While the message is sent, `SmtpClient.Timeout` bounds each read and write instead, so a 9.6 MB message on a slow uplink is not cut off while the server keeps answering.
- It maps exceptions by phase, because MailKit throws the same type with different meanings. The connect, authenticate and send calls each sit in their own `try` block:

  | Phase | Exceptions | Outcome |
  |---|---|---|
  | Connect (greeting, EHLO, STARTTLS) | any, including `NotSupportedException` (no STARTTLS), `SslHandshakeException`, `SocketException`, `IOException`, `TimeoutException`, `SmtpProtocolException` and `SmtpCommandException` with `UnexpectedStatusCode` | `unavailable` |
  | Authenticate | `AuthenticationException`, and `NotSupportedException` (server offers no AUTH) | `authFailed` |
  | Authenticate | `IOException`, `TimeoutException`, `SmtpProtocolException` | `unavailable` |
  | Send | `SmtpCommandException` with `SenderNotAccepted`, `RecipientNotAccepted` (address from `ex.Mailbox`) or `MessageNotAccepted`, and `UnexpectedStatusCode` at `DATA` | `rejected` |
  | Send | `ServiceNotAuthenticatedException` (a 530 while no credentials are set) | `authFailed` |
  | Send | `IOException`, `TimeoutException`, `SmtpProtocolException` | `unavailable` |

  An `OperationCanceledException` is `unavailable` only when the timeout's token fired. When the shutdown token fired, it is rethrown.

  MailKit's default `OnRecipientNotAccepted` throws, and `Send` resets before `DATA`, so a refused recipient means no one receives the message, as the spec requires.

Logs carry the host, the port and the outcome. They never carry the password or the server's reply text.

Version: pin the newest MailKit release that is at least two weeks old (4.18.1 at the time of writing; 4.18.2 is from 2026-10-23).

Alternatives considered:
- **Keycloak.** Rejected: it cannot send custom content.
- **`System.Net.Mail.SmtpClient`.** Rejected: Microsoft discourages it for new work, and its STARTTLS control is weaker.
- **HTTP APIs of email providers.** Rejected: each provider has its own API, while any provider also speaks SMTP.

### D2. Content: an HTML body with a text alternative, plus an XLSX; no rendering

Two writers sit on a renderer-neutral `ReportModel`:
- `ReportHtml` writes the body. It is hand-built with inline styles, one centred table of at most 640 px, and no CSS classes, images or scripts. Gmail and Outlook render plain tables reliably, and Gmail strips inline SVG.
  - Every member-written or data value is HTML-encoded: names, titles, group values, tooltip text and filter values.
  - The same model also yields the `text/plain` part.
- `ReportWorkbook` writes the XLSX.

Alternatives considered:
- **An HTML attachment with charts ported from TypeScript.** About 8 days of work, and Gmail web shows it as source code.
- **PDF.** PDFsharp is AGPL from 6.5.0, and QuestPDF needs a paid licence above USD 1M revenue.
- **Headless Chromium.** About 1 GB image and 300-700 MB per render, which does not fit the 4 GB host.
- **A public share link.** Recipients have to pull it, and it needs anonymous endpoints.

The model leaves room for a later renderer.

### D3. A one-to-one table `tamiza.dashboard_reports`

The script follows `tamiza-api/src/dbUp/README.md` (named `pk_`, `fk_`, `ix_` and `ck_` objects). It is modelled on `sampling_frames`, the existing one-to-one table (`0001_initial_schema.sql:72-83`):

```sql
-- 0015_add_dashboard_reports.sql
CREATE TABLE tamiza.dashboard_reports (
    dashboard_id uuid NOT NULL,
    recipients text[] NOT NULL,
    days smallint[] NOT NULL,
    send_time time NOT NULL,
    active boolean NOT NULL,
    locale text NOT NULL,
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    not_before timestamp with time zone,            -- a save after the day's time: that day is not due (D4)
    last_scheduled_at timestamp with time zone,     -- the last scheduled day the scheduler took (D4)
    send_now_pending boolean NOT NULL DEFAULT false,
    send_now_requested_at timestamp with time zone, -- the last "send now" request, for the 10-minute rule
    last_sent_at timestamp with time zone,
    last_failed_at timestamp with time zone,
    last_failure text,                              -- a code, see D9
    last_failure_address text,                      -- the refused recipient, for email.rejected
    CONSTRAINT pk_dashboard_reports PRIMARY KEY (dashboard_id),
    CONSTRAINT ck_dashboard_reports_recipients CHECK (cardinality(recipients) BETWEEN 1 AND 20),
    CONSTRAINT ck_dashboard_reports_days CHECK (cardinality(days) BETWEEN 1 AND 7 AND days <@ ARRAY[1,2,3,4,5,6,7]::smallint[]),
    CONSTRAINT fk_dashboard_reports_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_reports_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_dashboard_reports_updated_by ON tamiza.dashboard_reports (updated_by);
```

The EF mapping has to match it for `SchemaDriftTests`:
- the two checks through `HasCheckConstraint`;
- `HasDefaultValueSql("now()")` on `UpdatedAt` and `HasDefaultValue(false)` on `SendNowPending`;
- `HasOne<Dashboard>().WithOne().HasForeignKey<DashboardReport>(...)` with cascade, as `sampling_frames` does;
- `HasOne<User>()` with restrict.

A new `ScriptConventionTests` case checks the names for 0015.

The script also has `removed_at timestamp with time zone`, and the recipients check reads `cardinality(recipients) <= 20 AND (removed_at IS NOT NULL OR cardinality(recipients) >= 1)`. Removing an email does not delete its row; it sets `removed_at`, clears the recipients and the pending "send now", and pauses it. A removed row reads as no settings, and the scheduler and the sender skip it. Saving again clears `removed_at` and keeps `last_scheduled_at` and `send_now_requested_at`, so removing and saving again cannot send a day twice or skip the 10-minute rule.

Removing a dashboard or a project removes its report through the cascade. Duplicating a dashboard copies only what `DuplicateAsync` copies explicitly, so a copy has no report and no code change is needed. A test pins this.

EF maps the table:
- `text[]` to `List<string>`;
- `smallint[]` to `short[]`;
- `time` to `TimeOnly`.

Alternatives considered:
- **Columns on `tamiza.dashboards`.** Rejected: they would add eleven columns to an entity that every dashboard endpoint loads and saves.
- **A jsonb settings column.** Rejected: SQL could not check the settings, and the scheduling query would be harder to read.

### D4. Due reports are found in SQL, with a claim per day

The design keeps two facts apart:
- `last_scheduled_at` is written only by the scheduler's claim. It records the day actually taken.
- `not_before` is written by every save. It records "this save came after that day's time".

One predicate decides whether a report is due. With `local = @now AT TIME ZONE p.time_zone`, it reads:

```sql
r.active
AND extract(isodow FROM local)::smallint = ANY (r.days)
AND local::time >= r.send_time
AND (r.last_scheduled_at IS NULL OR (r.last_scheduled_at AT TIME ZONE p.time_zone)::date < local::date)
AND (r.not_before IS NULL OR (r.not_before AT TIME ZONE p.time_zone)::date < local::date)
```

`@now` comes from the reports' own `TimeProvider`, a keyed service (`reports`) that is the system clock by default. Tests set a day and time on it without moving the clock that validates sign-in tokens. Each step of the scheduler claims one report atomically:

```sql
UPDATE tamiza.dashboard_reports r SET last_scheduled_at = @now
FROM tamiza.dashboards d JOIN tamiza.projects p ON p.id = d.project_id
WHERE d.id = r.dashboard_id AND r.dashboard_id = (
    SELECT r2.dashboard_id FROM ... WHERE <the predicate> ORDER BY r2.send_time, r2.dashboard_id LIMIT 1)
  AND <the predicate>
RETURNING r.dashboard_id
```

The predicate is repeated in the `UPDATE`. A save, pause or time change that happens between the selection and the claim therefore wins.

The claim comes before the send, so a restart in the middle of a send skips that day rather than repeating it. This gives the spec's rules without extra code:
- **Late send the same day.** A day missed while the `api` or email was down is sent once, late, on the same day.
- **No catch-up.** Earlier days are never sent.
- **Time zone changes.** They take effect at the next tick, because the local day is computed in the project's current zone.

**First send after saving.** In the same statement as the upsert, each save sets:

```
not_before = CASE WHEN local::time >= @send_time THEN @now ELSE NULL END
```

It never touches `last_scheduled_at`. So:
- a save after today's time makes today not due;
- a later save before a new time re-opens a day on which nothing was sent, because only the latest save counts;
- a day already sent stays sent through `last_scheduled_at`, so a re-save never sends a second email that day.

Pausing and activating are saves too, so they follow the same rule.

Alternative considered: a `next_send_at` column computed in .NET. Rejected, because it would need:
- a `NextAfter` function that handles DST gaps;
- recomputing on every time zone change;
- matching .NET's time zone names to PostgreSQL's.

### D5. "Send now" goes through the scheduler

`POST .../report/send` runs a single `UPDATE`:
- it sets `send_now_pending = true` and `send_now_requested_at = @now`;
- its `WHERE` clause requires `send_now_requested_at IS NULL OR send_now_requested_at <= @now - interval '10 minutes'`.

When the update changes no row, the API answers 409 `reports.tooSoon`, or 409 `reports.none` when the dashboard has no settings. Otherwise it wakes the scheduler and answers 202.

The scheduler claims pending requests before scheduled ones with `UPDATE ... SET send_now_pending = false WHERE ... AND send_now_pending`. It never touches `last_scheduled_at` there, so the schedule is unchanged.

Alternative considered: sending inside the request. Rejected: a report with 50 widgets can take minutes, longer than the proxy timeouts allow.

### D6. One `ReportScheduler`, one report and one widget at a time

`Reports/ReportScheduler.cs` copies `IngestionScheduler`:
- It returns at once, logging once, when email is disabled.
- It waits for migrations, then loops on a 60-second tick or a `ReportSignal.Wake()`.
- A pass is a loop of steps, and each step sends one report. A step first claims a pending "send now" request. Only when there is none does it claim one due report (D4). The pass ends when neither is left.

This order lets a "send now" made during a busy 07:00 wait for at most the one report already being built, not for the whole pass.

The widgets of a report run one after another through `QueryAsync`. That way a report holds at most one of the 4 query slots that members share. A `DataTimeoutException` becomes that widget's note, not a failed email.

Many reports due at 07:00 are sent one by one. The spec's "as soon after as possible" allows that.

### D7. `ReportDataBuilder` reproduces the UI's steps in C#

`Reports/ReportDataBuilder.cs` builds the `ReportModel` (header, stat values, tables of typed cells with their format, and notes):

1. **Load the data.** It loads the dashboard, its filters and its widgets in reading order. To do so it makes `ReadConfig` and `ReadFilters` internal. It also extracts the inline ordering of `GetDashboardAsync` (`DashboardEndpoints.cs:73-77`) into an internal method that both use, instead of copying it. It also loads the indicator metadata and the publications.
   - An indicator that `CurrentIndicatorsAsync` no longer lists, such as a removed script whose table is still published, gets the `decimal` format, no unit, and its name as the label. That mirrors `plainIndicator` (`format.ts:21-22`) and `widget-view.ts:197-199`, so a missing lookup never throws.
2. **Strip each widget's config by chart type** (`Indicators/WidgetQuery.cs`, a port of `widget-view.ts:25-43`). The API accepts a `groupBy` on a stat tile and a geometry column on a non-map widget (`IndicatorValidation.cs:123-141`), and both change the rows. The UI strips them before querying, so the report must too.
3. **Apply the filter defaults** (a port of `appliedOf`, `dashboard-filters.ts:147-160`). It keeps each filter that has a default, except those the widget ignores. A filter that takes values passes `Values`; any other passes `Min` and `Max`.
4. **Query.** It calls `QueryAsync` with the stripped config, the widget's chart type (only a histogram without groups reads one value per row) and the applied filters.
5. **Shape each table like the UI's `table()` methods:**
   - **bar, column, line:** group, then the plotted value. A line sorts its groups in natural order, as `naturalCompare` does in `chart-scales.ts:116` (`numeric: true, sensitivity: 'base'`). The port uses `CompareOptions.NumericOrdering | IgnoreCase | IgnoreNonSpace`, with missing values last.
   - **histogram:** equal-width ranges, a port of `chart-data.ts:33-50` with the same default number of buckets. The table holds each range and the number of rows in it.
   - **pie:** group, value, and share of the positive total (`pie-widget.ts:146-156`).
   - **map:** group, value, then each tooltip column under its label, with no geometry.
   - **stat:** the first row's value.
   - **Fallbacks:** a missing group reads as `indicators.chart.noGroup` ("(none)" or "(ninguno)"), and a line sorts it last. A missing cell, such as a missing tooltip value or the share of a non-positive value, reads "—" in the bodies and stays empty in the workbook.
6. **Collect the notes.** The builder never dereferences a null `Response`.
   - **In place of a table:**
     - not published;
     - missing columns;
     - settings that no longer fit: an outcome with `Errors` and a null `Response` (`IndicatorDashboard.cs:121,137`), for example a measure column republished as text;
     - a timeout;
     - no rows, which uses `indicators.widget.empty`.
   - **With the table:**
     - the row limit reached;
     - not narrowed by a filter. This note lists, in the filter bar's order and by label, the filters with a default that the widget ignores, plus those whose column the response lists in `NotFiltered`. It is a port of `widget-view.ts:184-190`, and a null `NotFiltered` counts as empty.

   The notes use `ReportText`'s own words, never the API's English field messages.

Unit tests run these ports on the same inputs as the UI specs. That includes the natural-order and histogram-edge cases, so a later change on either side shows up as a failing test.

### D8. Language, regional format and text

At save time the UI sends its `LOCALE_ID`. `ReportText.Resolve` turns it into the name stored as `locale`:
- a culture the server has regional data for keeps its name, such as `es-CO`;
- a well-formed tag it has no data for falls back to its language, as `en-CO` becomes `en` and `es-DE` becomes `es`. Browsers send such tags for a language set with a device's region, and ICU 74 in the image has no data for them;
- anything whose language is not `es` or `en` is a 400 on `locale`. The `aspnet:10.0` image ships ICU, so `CultureInfo(locale)` gives real regional formats.

`ReportFormat` follows the spec's "Indicator formatting" rules:
- `count`: `N0`;
- `decimal`: `N2`;
- `percent`: the value times 100 with 0-2 decimals, then `%`. It is built from the number pattern, not from .NET's `P`, which adds a space in `es-CO` and so would differ from the UI;
- `currency`: `N2`, with the unit before the number;
- any other unit is written after the value.

Dates name the month, to the minute, in the culture's pattern:
- the subject uses `MMM d, yyyy` for English and `d MMM yyyy` for Spanish;
- the header uses the long date and the short time, plus the project's time zone.

The report's own words (about 35 strings) sit in `Reports/ReportText.cs` as two dictionaries, `en` and `es`, keyed like the UI's translation keys. A test fails when the two sets of keys differ.

Small differences from the browser's `Intl` output are accepted, for example grouping in `es-ES` and currency symbols.

### D9. Results and failure codes

After each attempt the scheduler updates only the result columns:
- **Sent:** `last_sent_at`, and the failure columns cleared.
- **Failed:** `last_failed_at`, `last_failure`, and `last_failure_address` when one applies.

The failure codes are:
- from `email-sending`: `email.unavailable`, `email.authFailed`, `email.rejected`;
- `reports.nothingToSend`, when there is no widget, or when every widget has a note in place of its table for one of these reasons: not published, missing columns, settings that no longer fit, or a timeout. A widget with no rows counts as read;
- `reports.authorNoAccess`, when the member in `updated_by` is no longer an analyst or admin of the project, nor a superadmin by their stored, verified email (`SuperAdminRule.IsSuperAdmin(email, verified)`). Nothing is sent until another analyst or admin saves the email, which makes it theirs. The check runs in `ReportSender`, so it covers scheduled sends and "send now", removal and demotion alike;
- `reports.failed`, for an unexpected exception, which is also logged.

The UI translates each code. The API never stores the server's reply text.

### D10. The HTML body's size and the attachment's size

`ReportSender` works in this order:
1. It writes the workbook first, to a path from `ExportRegistry.NewFilePath()`. That gives a `{guid}.export` file, so the startup `ExportSweeper` removes a file left behind by a crash.
2. It checks the file's size. When it passes 7 MB (7168 KB, about 9.6 MB after base64), the workbook is left off. That stays under the 10 MB limit that Exchange and many gateways apply by default.
3. It passes `attachmentIncluded` to `ReportHtml` and to the text writer. They say once either "the full tables are in the attached workbook" or "the attachment was too large; members can export the indicator tables from the dashboard".
4. It deletes the file in a `finally` block after the send.

The block text itself is neutral, such as "22 more rows", so the 80 KB measurement does not depend on whether the workbook is attached.

`ReportHtml` appends the blocks in order and measures UTF-8 bytes as it goes. Before adding a block, it checks that the block still fits within 80 KB, leaving room for the footer. Once one does not fit, each remaining widget is written as its title, marked as not shown in the body. 80 KB keeps clear of Gmail's clipping at about 102 KB.

The limits live in `ReportOptions` (section `Reports`), so tests can lower them. Their defaults are 10 rows per block, 80 KB and 7168 KB.

### D11. Workbook layout

Sheets:
- **"Summary" (or "Resumen").** It holds two text columns:
  - the header rows;
  - each stat tile's title and value, as the email shows them;
  - each widget's notes.

  It holds text because formats are per column, and a column cannot mix a percentage format with a count format.
- **`"{n}. {title}"`, one per other widget.** `n` is the widget's position in reading order. The name goes through `XlsxExport.SheetName` and is made unique ignoring case. A trailing apostrophe is trimmed, and the reserved name "History" is suffixed.

Number formats are set through `DynamicExcelColumn.Format`:

| Column | Excel number format |
|---|---|
| plotted value, `count` | `#,##0` |
| plotted value, `decimal` or `currency` | `#,##0.00` |
| plotted value, `percent` | `0.00%` (`0.##%` would leave a trailing separator for whole percentages) |
| histogram row count | `#,##0` |
| pie share | `0.0%`, like the UI's one decimal |
| tooltip column with format `count` / `decimal` / `percent` | `#,##0` / `#,##0.00` / `0.00%` |
| tooltip column with no format | `#,##0` for an integer column, `#,##0.00` for a decimal column |

Text and date tooltip columns stay text.

Because formats are keyed by header text across the workbook, a header that two sheets need with different formats gets the later sheet's number in brackets. The "Summary" sheet's text columns never block a format, since a number format leaves text as it is. Units go in the header, for example "Households visited (households)".

The file name is `{project slug}_{dashboard slug}_{yyyy-MM-dd}.xlsx`, with the date in the project's time zone. `ExportFileName.Slug` gains an optional `fallback` parameter, `"project"` by default so its callers keep their behavior. The dashboard slug passes `"dashboard"`, for a name such as "住房" that leaves no ASCII letter.

The workbook's language depends on the stored locale. That is the one exception the `interface-language` delta adds to "files the backend produces do not change with the interface language".

### D12. Envelope

The message is built as follows:
- **From:** the sender address and display name from `email-sending`.
- **To:** an empty group, `new GroupAddress("undisclosed-recipients")`, written as `undisclosed-recipients:;`. MailKit builds the SMTP envelope from To, Cc and Bcc (`SmtpClient.cs`, `GetMessageRecipients`). Putting the sender in To would make it an envelope recipient: a no-reply sender without a mailbox would be refused at RCPT and fail every send, or receive a copy of every email. The sender address therefore needs no mailbox.
- **Bcc:** the recipients. MailKit does not write Bcc into the transmitted message.
- **Reply-To:** the email of `updated_by` from `tamiza.users`, read at send time, when it has one.
- **Subject:** `{project} – {dashboard} – {date}`, with CR and LF removed.
- **Header:** `Auto-Submitted: auto-generated`.
- **Footer link:** `TAMIZA_PUBLIC_URL` (`TamizaOptions.PublicUrl`) plus `/projects/{projectId}/dashboards/{dashboardId}`, the dashboard's own address (`app.routes.ts:25`).

### D13. API

All endpoints are under `/api/v1/projects/{projectId}/dashboards/{dashboardId}/report`, in `Reports/ReportEndpoints.cs`. All require `RequireProjectRole(ProjectRole.Analyst)`, so a viewer gets 403 and a non-member 404. A dashboard that does not exist gets 404 `dashboards.notFound`, as elsewhere.

- **`GET`:** answers `{ settings | null, timeZone, lastSentAt, lastFailure: { code, at, address } | null, updatedBy: { name }, updatedAt }`. It works while email is off, so settings stay visible to the API.
- **`PUT`:** takes `{ recipients[], days[], time "HH:mm", active, locale }`.
  - It answers 503 `reports.unavailable` while email is off.
  - It validates `recipients`, `days`, `time` and `locale` as field errors. A list longer than 40 entries is refused before any address is checked. The others go through `EmailAddressRule` (D1), with duplicates dropped ignoring case through a `HashSet`. One error on `recipients` names every refused address, each cut at 60 characters, because the UI shows a field's first message only. The route also takes a 64 KB request size limit.
  - It upserts the settings with the rule from D4 and answers with the `GET` body.
- **`DELETE`:** marks the email removed (D3) and answers 204, even while email is off.
- **`POST .../send`:** answers 202, or 409 `reports.none` / `reports.tooSoon`, or 503 `reports.unavailable`.

`GET /system/config` gains `email: { enabled }`.

### D14. UI

- **Configuration flag.** `SystemConfigService.emailEnabled`. A missing key reads as off, as `accountEmails` does.
- **Service calls.** `IndicatorsService` gains `getReport`, `saveReport`, `removeReport` and `sendReportNow`.
- **Menu action.** In `project-indicators.ts` `actions()`, after `filters`, add `{ id: 'report', label: t('dashboards.menu.report'), testId: 'dashboard-report' }` only when `emailEnabled()`. The menu is already shown only to analysts and admins. A `case 'report'` opens the dialog, the same way `editFilters()` does.
- **`indicators/dashboard-report-dialog.ts`**, modelled on `dashboard-filters-dialog.ts`, with:
  - recipients as a textarea, one address per line or separated by commas or semicolons;
  - seven weekday toggles, Monday first;
  - a native time input;
  - an "Active" switch;
  - the project's time zone;
  - the last send and the last failure, translated from its code;
  - the warning about data leaving Tamiza and the note that filter defaults are fixed;
  - *Save*, *Remove* (after a confirmation), and *Send now*, which is busy until the API answers.

  Field errors from the API appear next to their field.
- **Texts.** `public/i18n/en.json` and `es.json` gain `dashboards.report.*` and the failure codes.

### D15. Deployment and docs

- **Configuration files.** `compose.yaml` passes the eight `TAMIZA_SMTP_*` variables with empty defaults, and `.env.example` documents each of them.
- **`docs/operations.md`** gets an "Email (optional)" section covering:
  - how to turn email on;
  - choosing a provider: a transactional SMTP service, or a relay of the operator's own;
  - SPF, DKIM and DMARC for the sender domain;
  - the Microsoft 365 basic SMTP AUTH change at the end of December 2026;
  - free-tier limits, which count each Bcc recipient;
  - the sender address needs no mailbox, because it is never a recipient;
  - checking delivery with Mailpit;
  - what leaves the deployment: aggregated figures and tooltip text, possibly personal data under Ley 1581.

### D16. Tests

**Integration tests** use a `FakeEmailSender` that keeps the `MimeMessage`s. They cover:
- the envelope, the body, the text part and the workbook, by opening the XLSX with MiniExcel;
- every scheduling scenario of the spec, with a fake `TimeProvider` and `ReportScheduler` ticks driven by the test;
- roles, validation and error codes.

**Real sender:** `MailKitEmailSender` runs against Mailpit containers through Testcontainers, configured with `MP_SMTP_AUTH_FILE`, `MP_SMTP_REQUIRE_TLS`, `MP_SMTP_TLS_CERT`/`KEY` and `MP_SMTP_ALLOWED_RECIPIENTS` as each case needs, and is checked through Mailpit's API. The cases are:
- delivery without credentials, with the envelope recipients exactly the Bcc list;
- `starttls` against plain Mailpit gives `unavailable`;
- `tls` with a certificate for another host gives `unavailable`;
- a wrong password gives `authFailed`;
- one refused recipient out of three gives `rejected`, and no message is stored.

The timeout case uses a `TcpListener` in the test that accepts a connection and never greets. An unreachable port would be refused at once and so would not test the timeout.

**Widget timeout:** a test sets `DataDashboard:LockTimeout` to about 500 ms and holds `LOCK TABLE ... IN ACCESS EXCLUSIVE MODE` on one widget's indicator table from a second connection, as `DataQueryTests.cs:499-510` does. Only that widget times out. The host-wide `TestStatementSleep` would slow every widget at once, so it serves only the "every widget timed out" case.

**Unit tests** cover the options and validator, the ports (D7), the formatting (D8), sheet naming and the size budget.

## Risks / Trade-offs

- **[Analysts send project data to any address]** Admins and analysts can mail aggregated figures, group values and tooltip text, which may be personal data, to any domain. A sent attachment cannot be recalled. → The dialog warns about it, the docs name the operator's data-protection duties, and replies go to the member who saved the settings. `updated_by` and `updated_at` show who last changed them, and an email stops when that member loses the analyst or admin role. A domain allow-list can come later without changing the data model.
- **[No volume cap]** Besides the 10-minute "send now" rule per dashboard, nothing limits how many emails an analyst sends. Removing a dashboard and adding a new one starts a fresh rule. → Accepted for this change. A per-user or per-project hourly allowance, configurable like `TAMIZA_ACCOUNT_EMAILS_PER_HOUR`, is a possible follow-up.
- **[Phishing-like content from the operator's domain]** Member-written names travel in emails sent from the operator's domain. → Every value is HTML-encoded. The email has no links other than the dashboard's own address, and CR/LF are stripped from the subject.
- **[Deliverability]** Mail without SPF and DKIM is rejected or goes to spam. Microsoft 365 turns off basic SMTP AUTH by default at the end of December 2026. Free tiers count each Bcc recipient. → The docs cover each point, the dialog shows the last failure, and *Send now* gives a quick check.
- **[Fixed filter defaults]** A daily email of a dashboard whose default date range is fixed repeats the same range. → The dialog says so. Relative dates are a later change.
- **[Drift between the C# ports and the UI]** Changes to `queryFor`, histogram binning or natural order in TypeScript could break parity. → Tests share cases with the UI specs, and comments on both sides point to the twin code.
- **[Formatting differences from `Intl`]** .NET and the browser format some locales slightly differently. → They are accepted. Percent is built by hand to avoid the largest difference.
- **[Many reports due at the same time]** Reports run one by one, and a widget can take up to 25 s. → The spec allows "as soon after as possible". A report uses at most one query slot, so members are not starved.
- **[Histogram sample]** A histogram without groups reads at most 5000 rows, with no fixed order. That is the same capped, unordered sample the UI reads, so the email and the screen may not see the same rows. → The row-limit note says so, as in the UI.
- **[Westward time zone change]** A time zone change that moves the local date back can skip that day's email once. → This is rare and accepted.
- **[Header collisions in the workbook]** Two sheets may need the same header with different number formats. → D11 renames the later header.

## Migration Plan

1. DbUp `0015_add_dashboard_reports.sql` adds the table. Existing dashboards get no report.
2. Deploying with no SMTP variables changes nothing that a member can see.
3. The operator sets `TAMIZA_SMTP_HOST`, `TAMIZA_SMTP_FROM` and credentials, restarts `api`, and checks with *Send now*.

Rollback: unset the variables to stop all sending. To remove the feature, deploy the previous image. The extra table is harmless to it, and `docs/operations.md` gives the `DROP TABLE` for a full revert.
