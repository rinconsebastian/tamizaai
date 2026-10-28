# Proposal

## Why

Projects report their indicators to people who have no Tamiza account: funders, partner organisations, local authorities. Today a member has to open the dashboard, export each indicator or copy figures by hand, and email them, every day or every week. Inviting those people as viewers is not an answer either: they would need an account, and a `viewer` sees all of the project's clean data, not one dashboard.

Tamiza already computes every widget on the server and already writes XLSX files. What is missing is a way to send a dashboard's figures on a schedule to a list of addresses. An earlier analysis compared HTML and PDF attachments with charts. Rendering charts on the server would mean porting about 1,000 lines of chart code, Gmail shows `.html` attachments as source code, and the usable PDF libraries are now AGPL or paid. This change avoids rendering altogether: it sends the numbers and tables in the email body, with the full tables in an XLSX attachment.

## What Changes

- **Email from Tamiza (optional).** The operator configures an SMTP server through `TAMIZA_SMTP_*` variables. Without them, the feature does not appear and nothing is sent. This reverses a non-goal of `add-keycloak-invitation-email` ("sending email from Tamiza itself"). Keycloak's account emails cannot carry this: they reach only Keycloak users and have no attachments.
- **A scheduled email per dashboard.** An `analyst` or `admin` can give a dashboard one scheduled email with these settings:
  - **Recipients:** 1 to 20 addresses, of any domain, who need not be Tamiza users.
  - **Days and time:** chosen weekdays at one time of day (HH:MM), in the project's time zone.
  - **Active or paused.**
  - **"Send now"**, which queues the email to be sent shortly, ahead of scheduled ones.
- **What the email holds:**
  - **Body:** a header with the project, the dashboard, when it was generated, how current the data is and the filters in force. Then each stat tile's value, and a short table for each other widget, with its first rows. Last, notes for anything that could not be shown, and a link to the dashboard for members.
  - **Attachment:** an XLSX workbook. A "Summary" sheet holds the header and the stat tile values. Then there is one sheet per other widget, holding what that widget shows with "Show as table". Numbers are real numbers in the indicator's number format.
  - **Filters:** the email uses the dashboard's filter defaults, as a member sees them when opening it. Widgets that ignore a filter keep ignoring it.
  - **Not included:** charts, maps drawn as maps, images and PDF.
- **Sending rules.**
  - Recipients are in Bcc. The sender address is not a recipient.
  - Replies go to the member who last saved the settings.
  - A scheduled time that passes while the API is down is sent once, late, on the same day. Earlier days are not caught up, and an email is never sent twice for the same day.
  - A failure does not retry; it is shown with the settings.
- **Dashboards.**
  - Duplicating a dashboard does not copy its scheduled email.
  - Removing a dashboard removes its scheduled email.
  - A `viewer` neither sees nor manages scheduled emails.
- **UI.** A *Scheduled email…* action in the dashboard menu opens a dialog with the settings, the time of the last send and the last error. The dialog warns that the data leaves Tamiza and that filter defaults are fixed values.
- **Docs.** `docs/operations.md` gets a section on choosing an SMTP provider, SPF and DKIM for the sender domain, and checking delivery with Mailpit. It also notes that Microsoft 365 turns off basic SMTP authentication by default at the end of December 2026.

## Capabilities

### New Capabilities
- `email-sending`: the operator's SMTP server: how it is configured and turned on, how Tamiza connects and authenticates, which sender it uses, how failures are reported, and how its secrets are protected. Any later Tamiza feature that sends email will rely on it.
- `dashboard-reports`: scheduled emails of a dashboard. Covers the settings and their validation, who manages them, the schedule, the body and the workbook, the filters used, sending now, failure reporting and the dialog.

### Modified Capabilities
- `custom-dashboards`:
  - *Roles for dashboards* adds managing a dashboard's scheduled email to the actions only an `analyst` or `admin` may take.
  - *Duplicating a dashboard* states that a copy has no scheduled email.
- `interface-language`: *Content shown as it was created* makes one exception to "files the backend produces do not change with the interface language". A scheduled email's workbook follows the language and regional format stored with its settings.

## Impact

- **API** (`tamiza-api`):
  - new MailKit dependency (MIT);
  - SMTP options, validator and startup warning;
  - a report builder that runs each widget through the existing widget query, then writes the HTML body, its text alternative and the XLSX with MiniExcel;
  - a background scheduler;
  - `GET`, `PUT` and `DELETE /projects/{id}/dashboards/{dashboardId}/report`, plus `POST .../report/send`;
  - `GET /system/config` gains `email.enabled`;
  - new `reports.*` problem codes.
- **Database:** a DbUp script (`0015_add_dashboard_reports.sql`) that adds the table `tamiza.dashboard_reports`. It holds one row per dashboard with a scheduled email: the settings and their send state. The row is removed with its dashboard or project through `ON DELETE CASCADE`.
- **UI** (`tamiza-ui`):
  - the menu action and the dialog;
  - `IndicatorsService` calls;
  - `SystemConfigService`;
  - English and Spanish texts.
- **Deployment:** optional `TAMIZA_SMTP_*` variables in `compose.yaml` and `.env.example`. The `api` must be able to reach the SMTP server.
- **Security and privacy:**
  - Analysts and admins can send a dashboard's aggregated figures to any address.
  - Group values and map tooltip columns can hold personal data. Once sent, an attachment is outside Tamiza's access control, and no one can recall it.
  - The dialog says so, and the docs point to the operator's data-protection duties (in Colombia, Ley 1581).
  - The SMTP password is never logged or returned.
