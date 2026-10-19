# Connecting a KoboToolbox form

A Tamiza project is linked to exactly one KoboToolbox form. This guide covers finding the form's details in Kobo, creating the project, connecting Kobo's webhook, how submissions reach Tamiza, and the project settings that depend on the form.

Tamiza is compatible with KoboToolbox but is not affiliated with it.

## Before you start

- A KoboToolbox account that can **view the form's submissions**: the form's owner, or an account the form is shared with, holding the *View submissions* permission. A dedicated account with view-only access is a good choice: Tamiza never changes anything in Kobo.
- The form must be deployed.
- Sign-in to Tamiza. Any signed-in user can create a project and becomes its admin.

## 1. Find the form's asset UID

Open the form in KoboToolbox. The address looks like this:

```
https://kf.kobotoolbox.org/#/forms/aBcD1234eFgH5678iJkL/summary
```

The asset UID is the code after `/forms/`: `aBcD1234eFgH5678iJkL` in the example. It contains only letters and digits.

## 2. Get the API token

In KoboToolbox, open **Account settings → Security** and copy the **API key**. While signed in to Kobo you can also open `https://<your Kobo server>/token/?format=json` to see it.

The token acts as your Kobo account, so treat it like a password. Tamiza stores it encrypted and never shows it again, not even to project admins.

## 3. Create the project

In Tamiza, open **Projects → New project** and fill in:

| Field | Value |
|---|---|
| Project name | Any name, for example the study and year. |
| Kobo server | `kf.kobotoolbox.org` (global server), `eu.kobotoolbox.org` (EU server), or *Other server* for a self-hosted Kobo. Other servers must use HTTPS. |
| Form asset UID | From step 1. |
| Kobo API token | From step 2. |

Before saving, Tamiza checks that the token can read both the form and its submissions. If the check fails, nothing is saved and the page explains why (see [Troubleshooting](#troubleshooting)). After creation, Tamiza opens the project's webhook settings and, in the background, copies every submission the form already has (see [Submissions in Tamiza](#6-submissions-in-tamiza)).

The server and the asset UID cannot be changed after creation; only the token can be replaced. To use another form, create another project. Several projects may use the same form.

## 4. Configure the webhook in Kobo

**Settings > Webhook** (admins only) shows three values: the endpoint URL, the username (`tamiza`) and the secret that Tamiza generated, which Kobo calls the password. The page opens with the status of deliveries, and the setup steps below are listed there too, open until the first delivery arrives. In KoboToolbox:

1. Open the form and go to **Settings → REST Services**.
2. Choose **Register a new service** and give it a name, for example *Tamiza*.
3. Paste the **endpoint URL**.
4. Keep **JSON** as the type. Leave **Select fields subset** and **Custom wrapper** empty: Tamiza needs every submission whole, including Kobo's `_id` and `_uuid`.
5. Under **Security**, choose **Basic Authorization** and paste the username and the secret.
6. Save the service.

From then on, Kobo sends each new submission to Tamiza as it arrives, and it shows up in the project's **Settings > Submissions** page within seconds. The status line at the top of **Settings > Webhook** shows when the last delivery was received; until one arrives it says so and links to **Settings > Submissions**. Kobo's own REST Service page lists each delivery and whether it failed.

If the secret is ever exposed, use **Regenerate secret** in Tamiza and update the secret in Kobo's REST Service. If Tamiza can no longer read the stored secret (see [Troubleshooting](#troubleshooting)), the page hides the value and offers **Regenerate secret** in its warning. Until you do, Kobo's deliveries are refused; the next synchronization still picks those submissions up.

## 5. Check the form for the performance dashboard

The project's **Settings > General** page has a **Form check** card that shows which metrics of the performance dashboard (see [performance-dashboard.md](performance-dashboard.md)) the form supports:

| Metric | Needs |
|---|---|
| Submissions per day | Nothing extra. |
| Completion time | The `start` and `end` metadata fields. |
| Detail per enumerator | The `username` metadata field, or a question that identifies the enumerator. |
| Sampling frame coverage | A sampling frame whose dimensions all map to questions the form still has (see [Sampling frame](#sampling-frame)). Without a frame the check says to define one; if a redeploy removed a mapped question, it names the question so the frame can be updated. |

To add the metadata fields, edit the form in Kobo's form builder, open **Layout & Settings → Meta questions**, enable *start time*, *end time* and *username*, then redeploy the form. If the form has no `username` field but asks for the enumerator's name or code in a question, a project admin can select that question as the **enumerator question**: General shows the current one as a line (a question, `username`, or none) with a **Change** button, and opens the select by itself while the check asks for it. A selected question takes precedence over `username`, and admins can select one even when the form has `username`. If a redeploy removes the selected question, Tamiza goes back to `username` when the form has it.

**Time zone.** The dashboard counts submissions per day, and a day depends on the time zone. Admins set the project's time zone in **Settings > General**, next to the project name and saved with it (for example `America/Bogota`; **Use my browser's time zone** picks yours); until they do, days are counted in UTC. Analysts and viewers see the time zone as a read-only line.

After changing and redeploying the form in Kobo, press **Re-check the form** (admins and analysts) so Tamiza reads the new version. Tamiza describes the form's **deployed** version: questions you add or edit in Kobo's form builder do not count until you redeploy the form (see [Form versions and choice labels](#7-form-versions-and-choice-labels)).

## 6. Submissions in Tamiza

The project's **Settings > Submissions** page shows how many submissions Tamiza holds, how many were deleted in Kobo, when the last webhook delivery arrived, and the synchronization log. Submissions reach Tamiza in three ways:

- **Webhook.** Kobo sends each new submission right away (step 4). This is the normal path.
- **Synchronization.** Every 12 hours, Tamiza asks Kobo's API for the submissions made since its last successful synchronization, with a one-hour margin, and stores any the webhook missed: for example while the REST Service was not configured yet, its password was out of date, or Tamiza was down. The project's first synchronization runs as soon as the project is created and copies every existing submission.
- **Deletion check.** Once a day, Tamiza compares the list of submissions in Kobo with its own and marks the ones deleted in Kobo. Marked submissions are kept but left out of counts and, later, out of the analysis. A submission that reappears in Kobo is restored.

Each submission is stored once, identified by Kobo's `_id`, however often it arrives. A submission edited in Kobo replaces the stored copy the next time it arrives.

Admins and analysts have two buttons:

- **Sync now** runs a synchronization at once, for example right after configuring the webhook or to check that recent submissions are complete.
- **Full resync** re-reads every submission of the form and also checks for deletions. Use it after editing submissions in Kobo (for example, correcting answers in the data table), since a regular synchronization only reads recent submissions and may not see older ones that changed. On large forms it takes a few minutes.

Only one synchronization of a project runs at a time; the buttons are disabled while one is queued or running, and the page updates by itself until it ends.

Everyone sees the schedules as a line under the status ("Synchronizes every 12 hours · checks for deletions every 24 hours"). Admins press **Edit schedules** to change them: how often to synchronize and how often to check for deletions, each from 1 to 168 hours. The same button opens the settings for when scripts run (see [scripts.md](scripts.md#when-scripts-run)); the inputs hide again after a save. A new interval counts from the start of the last run of that kind. When a scheduled synchronization fails, Tamiza tries again after an hour.

Every synchronization records when it ran, what started it, how many submissions were new, updated or deleted, and the error if it failed. A submission received again with the same content counts as neither new nor updated.

By default, a synchronization that brings new, changed or deleted submissions also runs the project's Python scripts, which clean the data and compute the indicators. See [scripts.md](scripts.md).

## 7. Form versions and choice labels

Kobo stores and sends the answers to select questions as the choices' **names**, the codes defined in the form (`1`, `2`), not as their labels (`Yes`, `No`). Tamiza turns them into labels when it flattens the data for the scripts (see [scripts.md](scripts.md)). Because a form is often redeployed during fieldwork, and a choice can be added, removed or relabeled between deployments, each answer is translated with the labels of the form version its submission was made with.

How Tamiza gets those labels:

- Every submission carries the id of the form version it was made with. When Tamiza stores a submission made with a version it has not seen before, it records that version. This never contacts Kobo, so webhook deliveries stay as fast as before.
- Tamiza then fetches each recorded version's definition (its questions and choice lists) from the Kobo API, once. It does so during every synchronization, before the scripts run, and for versions that arrive by webhook, in the background within about two minutes. A version never changes in Kobo, so a fetched definition is not fetched again.
- Labels are always in the form's default language, the first language of the form. A choice without a label in that language has no label: Tamiza does not borrow another language's text, and its answers keep their code.
- The project's **current form**, which General's checks and `ctx.form` in the scripts describe, is the form's deployed version, not the latest draft in the form builder. If Kobo cannot return the deployed version, Tamiza uses the form's latest content and tries again later.

The **Form versions** list at the end of the project's **Settings > Submissions** page lists the versions the project's submissions use, with how many submissions each has and the state of its definition. **Settings > General** also shows how many definitions are unavailable. Admins, analysts and viewers can all see them. Next to a failed or not found version, analysts and admins get a **Re-check the form** button that retries it in place; viewers are told to ask an analyst or admin.

| Status | Meaning | What to do |
|---|---|---|
| Labels available | The definition is kept. | Nothing. |
| Pending | The version was recorded and its definition is not fetched yet. | Wait: it is fetched within a couple of minutes, or by the next synchronization. |
| Failed | The last attempt did not work, for example Kobo was unreachable, rejected the token, limited the requests, or the form can no longer be read. The card shows the reason and when Tamiza tries again: after an hour, then at intervals that double up to once a day. The synchronization that attempted it is not affected. | Fix the cause. After **Replace API token** (a button in General), Tamiza retries within about two minutes. **Re-check the form** retries at once. |
| Not found | Kobo answered that this version does not exist, while the form itself can be read. This happens with submissions made with ids Kobo no longer knows. Tamiza does not retry it on its own. | Usually nothing: its answers are translated with the current form, as Kobo's own exports do. If Kobo was at fault, **Re-check the form** tries again. |

Answers whose version has no definition yet (pending, failed or not found), and submissions without any form version, are translated with the project's current form. Every run reports in the flatten step's log how many submissions of each version did, so nothing changes silently. A choice that no version knows keeps its code in the table.

The raw submissions are never changed: they stay exactly as Kobo sent them. The sampling frame reads them, so it still matches choice **names**, not labels (see [Sampling frame](#sampling-frame)).

### Question labels

The tables' columns also carry the text of their questions, as labels (see [scripts.md](scripts.md#question-labels)). They work differently from choice labels:

- They come from **one** definition, the form's **latest deployed version**, in the form's default language, whatever version each submission was made with. A question that a later version relabeled shows its new text for all its answers.
- Redeploying the form with a new question text changes the labels at the next run, once Tamiza has read the new deployed version (a synchronization or **Re-check the form** does it). Edits in the form builder that are not deployed do not count.
- Tamiza reads them from the stored definition of the deployed version, so it needs that version's status in the **Form versions** card to be *Labels available*. While it is pending, failed or not found, runs publish the tables without question labels and the flatten log says so; they appear at the first run after the definition is read.
- A question that the deployed version dropped, or renamed, has no label. If a later version reuses a name for a different question, the old answers are under the new question's label: choose a new name in the form builder when a question changes meaning.
- Questions with no label in the default language have none; Tamiza does not borrow another language's text.

## Members and roles

| Role | Can |
|---|---|
| Admin | Everything below, plus project settings, the synchronization and script schedules, the Kobo token, the webhook and members. |
| Analyst | Edit the sampling frame, re-check the form, run **Sync now** and **Full resync**, write and run the [scripts](scripts.md), and build the project's [indicators dashboards](indicators-dashboard.md): add, rename, move, duplicate and remove [dashboards](indicators-dashboard.md#dashboards), and add, edit, arrange, remove and move widgets between them. |
| Viewer | Read the project, including every dashboard and its widgets, the Settings > Submissions page and its synchronization log. |

Everyone in the project sees its people in **Settings > Members**, with their role (you are marked **You**). Admins press **Add member** to add a person by email and role in a dialog, and use each row's menu to change a role, remove someone or revoke an invitation; people invited who have not signed in yet are rows of the same list, marked with the role they are invited as. The only admin has no demotion or removal in their menu, because a project always keeps one:

- If someone with that email has already signed in to Tamiza with the email **verified** in Keycloak, they become a member at once.
- Otherwise the invitation stays pending and turns into a membership the first time that person signs in with the email verified. By default Tamiza sends no email, so tell the person yourself.
- If the operator turned on [account emails](keycloak.md#account-emails-optional), Tamiza also creates the person's account when they have none and Keycloak emails them a link to set a password (the message after inviting says what happened to the email). A pending invitation then shows when its last email went out and has **Resend invitation email**, for a person who missed the link (it lasts 12 hours unless the operator changed it). A person who already had an account gets no email: tell them yourself.

With account emails on, a member's menu also has **Send password reset email**, for a member who forgot their password; Keycloak emails them a link to set a new one. Each admin can cause 20 such emails per hour, and the same person is never sent two within 10 minutes.

Make someone else admin before stepping down. A superadmin (configured by the operator in `TAMIZA_SUPERADMIN_EMAILS`) acts as admin on every project.

## Sampling frame

The sampling frame lists the dimensions of the sample (for example municipality, sex, age range), the form field each one maps to, and the target number of surveys for each combination of values. Every member reads it in **Settings > Sampling frame**: a summary line (dimensions, combinations and the date it was saved) over a table that pages after 50 rows. Every member can **Download** it as an Excel workbook in the layout of the [template](#the-import-template), with its targets. Admins and analysts press **Edit** to change it, or **Import from file** to load it from a file. The performance dashboard measures coverage against it.

### Starting a frame

On a project without a frame, admins and analysts see **Plan your sample** with three ways to start, and a closed **How to organize a file** guide below them (viewers only read that no frame is defined yet):

- **Choose questions** opens the question picker, which writes the rows for you from the form's choices.
- **Import a file** opens the import dialog described [below](#importing-a-file).
- **Type it in** opens an empty editor. **Cancel** closes it and returns to the panel.

### Choosing questions

The question picker lists the form's questions in three groups:

- **Questions with choices**, such as "Municipality · municipio · 1,100 choices". Every choice is used until you narrow them: **Choices** opens the list with a search box, and **Select all** and **Clear** act on the choices the search shows ("Select the 12 shown"), leaving the others as they are. The `other` choice that Kobo adds to an "or other" question is offered unticked.
- **Questions you type the values for**, such as an integer question, with one value per line. A line over 500 characters is refused.
- **Coverage cannot count these**: multiple-choice questions, questions inside a repeat group and a top-level question named `target`, shown with the reason and not selectable.

You choose 1 to 10 questions. The footer counts the rows the choices make, for example "5 × 2 × 3 = 30 rows", and stops at the frame's limit of 10,000 rows. When one question narrows another (a department and its municipalities), choose only the more detailed one, or only the municipalities you sample, so the frame does not list combinations that cannot happen. The picker ends in one of two ways:

- **Download Excel template** prepares the [template](#the-import-template) and downloads it.
- **Create the rows here** puts the rows straight into the editor, with every target empty, so a small frame needs no spreadsheet.

In the editor, **Create rows from questions** opens the picker with the editor's questions ticked: the codes found in the rows for a question with choices, and the values found in the rows for the others. Before it replaces rows it asks, and says what will not be kept: dimensions that cannot be made from questions, rows whose values are not choices of their question and, when the questions changed, the targets. When the questions are the same, each new row keeps the target of the matching row, and dimensions keep their names. **Remove rows without a target (N)** deletes the rows you did not plan, since saving needs a target in every row.

### The import template

The template is an Excel workbook with three sheets:

- **Frame**, the only sheet the import reads. It has one column per chosen question, named after the question (its path instead, such as `hogar/edad`, when another field of the form has the same name or the question is named `target`). A `# <column> label` note column follows each question with choices, and `target` comes last. Every combination is filled in with its codes, stored as text so `05001` keeps its leading zero, and every `target` is empty. A question you typed no values for gives an empty column, which you fill in on the rows you give a target.
- **Guide**, the rules below, with the project, the day and the form version the template was prepared from. When the form changes, download a new template.
- **Choices**, with the columns `question column`, `choice code`, `choice label` and `in this file`, so you can look up codes or add rows later.

Fill in the targets you plan, leave the other rows empty, save the file as Excel Workbook (.xlsx) and import it. Every file stays under the 5 MB import limit: when a form's lists are long, the Choices sheet lists only the codes in Frame, and if the file is still too large the label columns are left out (the Guide says which). A template that would still be too large is refused with a message to choose fewer choices or questions.

The downloaded frame uses the same layout with its targets, so it can be edited in Excel and imported again. A dimension whose name starts with `#` or is `target` would not be read back, so its column is named after its question's path, and the Guide says so.

### File rules

The file can be **CSV** (comma- or semicolon-separated, UTF-8) or **XLSX** (only the first sheet is read). The first non-empty row names the dimensions plus a `target` column; each following row is one combination:

| municipality | # municipality label | sex | age_range | target |
|---|---|---|---|---|
| cali | Cali | f | 18_29 | 120 |
| cali | Cali | m | 18_29 | 110 |
| cali | Cali | f | 30_44 | |

- **`target`** holds a whole number from 0 to 1,000,000, without thousands separators. A row whose `target` is empty is left out: it is not checked, and the preview lists its row number. A target of `0` keeps the row (the third row above is left out).
- **Columns whose name starts with `#`** are notes, such as the label next to a code. They are not read, and the preview lists them. Values under a column without a name are not read either, and the preview lists that column too.
- **Blank lines** are skipped and do not count toward the 10,000-row limit. Row numbers in the preview are the ones your spreadsheet program shows.

### Importing a file

**Import from file** opens a dialog with the file guide (open while the project has no frame) and its link to a template with the form's codes. The dialog shows a preview with any row errors (the first 20, and how many more there are), the rows left out for having no target (as ranges such as "2–41, 43–2,201") and the columns not read, and asks for the form field of each dimension. Each column starts with a question chosen for it: the question the saved frame maps a dimension of that name to, then the question whose path is the column name, then the only question with that name, then the only question with that label. When the saved frame and the form point at different questions, nothing is chosen and both are named under the field. Nothing is saved until you press **Load into the editor** and then **Save sampling frame**; **Cancel** in the editor discards the edits, and leaving the page with unsaved edits asks first. A question inside a repeat group or with several choices is marked in the list of form fields, and a note under the field says the dashboard cannot count it. When a save is refused, the editor opens on the first row at fault and names it. Limits: 10 dimensions, 10,000 rows, 5 MB per file. Values are matched against the raw submissions as text, so use the same codes the form stores (the choice *names*, as in the example above, not their labels). This is different from the tables the scripts receive, which show labels (see [Form versions and choice labels](#7-form-versions-and-choice-labels)).

### How submissions are matched

The performance dashboard compares them ignoring case and surrounding spaces, so `Cali` in the frame counts the answers `cali` and ` cali`. Nothing else is adjusted: a code that lost its leading zero in a spreadsheet (`5001` for `05001`) matches nothing, and those submissions appear as **outside the frame** in the dashboard (keep such codes as text in the file, as the [template](#the-import-template) does). The dashboard cannot count a dimension that maps to a question inside a repeat group or to a multiple-choice question, or whose question a redeploy removed, and says so (see [performance-dashboard.md](performance-dashboard.md#sampling-frame-coverage)). A frame cannot express ranges (an age range over an integer question): store the range as a code in the form, for example in a `calculate` question.

## Self-hosted Kobo on a private network

By default Tamiza only connects to Kobo servers that use HTTPS and resolve to public IP addresses. This prevents users from making the API call internal services.

If your Kobo runs on the same private network as Tamiza (for example `http://kobo.lan` or `https://10.0.0.20`), the operator can set `TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS=true` in `.env` and recreate the `api` service. That also allows plain HTTP. Only enable it when every user who can create projects is trusted, since it lets them point the API at internal addresses.

Tamiza connects to Kobo directly and does not use an outbound HTTP proxy (see [operations.md](operations.md)).

## Troubleshooting

| Message | Cause and fix |
|---|---|
| Kobo rejected the API token… | The token is wrong, or its account cannot read this form's submissions. Check the token and the form's sharing permissions in Kobo. |
| No form with this asset UID was found… | Check the asset UID (step 1) and the selected server (global or EU). |
| The Kobo server could not be reached… | Wrong address, a network problem, or the server is down. The `api` container needs outbound HTTPS access to the Kobo server. |
| The Kobo server must use HTTPS | Use the server's `https://` address, or see the private-network setting above. |
| …points to a private or local network… | The server resolves to a private address. See the private-network setting above. |
| The saved API token can no longer be read… | The API's encryption keys changed (for example, the `api-keys` volume was lost). An admin must replace the token and regenerate the webhook secret. |
| Kobo has no version … of the form… | A form version is *Not found* (see [Form versions and choice labels](#7-form-versions-and-choice-labels)). Its submissions use the current form's labels. |
| The Kobo server is limiting Tamiza's requests (HTTP 429) | Kobo answers 429 when an account makes many automated requests. Tamiza tries again later; nothing is lost. If it keeps happening, lower the load on that account (for example, fewer projects on the same token) or ask the Kobo server's operator. |
| The Kobo server's response was larger than … | Kobo returned a document bigger than Tamiza reads (10 MB). The version stays *Failed* and is retried once a day. |

**Webhook deliveries failing in Kobo.** Kobo's REST Service page shows the HTTP status of each failed delivery:

| Status | Cause and fix |
|---|---|
| 401 | The username or secret does not match. Copy both again from **Settings > Webhook**; after **Regenerate secret**, the old secret stops working. |
| 415 | The service sends XML. Edit it in Kobo and choose the JSON type. |
| 400 | The submission has no `_id` or `_uuid`, usually because the service uses **Select fields subset** or a **Custom wrapper**. Clear both. |
| 413 | The submission is larger than 10 MB. Attachments are not sent in the JSON, so this is rare; the next synchronization still stores the submission. |

Missed deliveries are not lost: the next synchronization picks them up, or use **Sync now**.

**Failed synchronizations.** The synchronization log shows the reason. Token, server and address messages mean the same as in the table above; fix the cause, then use **Sync now**. *Interrupted* means the API stopped while the synchronization was running, for example during an upgrade; the next one runs normally.
