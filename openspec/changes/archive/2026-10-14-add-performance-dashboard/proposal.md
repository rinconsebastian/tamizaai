# Proposal

## Why

Field coordinators need to know, while the survey is running, whether data collection is on track: how many surveys arrive each day, whether enumerators take a plausible time per survey, who is falling behind, and which quotas of the sampling frame are still short. Today they export from Kobo and count in Excel. Tamiza already keeps a copy of every submission, the form's metadata fields (`start`, `end`, `username`) and the project's sampling frame, and the Overview already checks which of these metrics the form allows. The dashboard that uses them, the first of the three in the brief, does not exist yet.

## What Changes

- **Performance dashboard.** A new **Performance** section, which becomes the page a project opens on. Every member can see it, viewers included. It is computed from Tamiza's own stored copy of the submissions, leaving out those marked as deleted, and never contacts Kobo, so it works when Kobo is down. It does not depend on the scripts either, so it works before any script exists and is not affected when a run fails. It has four parts:
  - **Submissions per day:** the total and one count per day.
  - **Completion time:** the time from `start` to `end`.
  - **Per-enumerator detail.**
  - **Sampling frame coverage.**
- **Days in the project's time zone.** A submission's day is the day Kobo received it (`_submission_time`, which the server sets), in a new per-project **time zone**. It defaults to UTC, and an admin sets it in the project settings (for example `America/Bogota`). Without it, evening submissions in UTC−5 count for the next day.
- **Date range.** An optional from–to filter, by the same day, applies to every part of the dashboard.
- **Completion time:**
  - Durations that cannot be used (`end` before `start`, missing or unreadable values) are left out and counted.
  - Outliers are flagged with Tukey's fences (below Q1 − 1.5 × IQR or above Q3 + 1.5 × IQR).
  - The dashboard shows the median, the mean without outliers, and how many outliers there are.
  - When the form has no `start` or `end`, this part says which field is missing, as the Overview check does.
- **Per-enumerator detail:**
  - For each enumerator: submissions, mean completion time without outliers, outliers, and last submission.
  - The enumerator is the question an admin selected or, when none is selected, the `username` field. A selected question now takes precedence over `username`, and admins can select one even when the form has `username`.
  - Values are grouped ignoring case and surrounding spaces. Submissions without a value are shown as "no enumerator".
- **Coverage:**
  - Each combination of the sampling frame shows its target, the submissions achieved, and the percentage. The percentage can pass 100%, and a target of 0 shows none.
  - Answers are compared with the frame's codes (choice names), ignoring case and surrounding spaces, as the frame's own duplicate check does. The choice labels are shown next to the codes.
  - Submissions that match no combination, or leave a dimension empty, are counted as **outside the frame**. Their most frequent combinations are listed, so a mistyped code or a lost leading zero (`5001` for `05001`) is visible.
  - A dimension that cannot be counted is flagged and named: its field is no longer in the form, it sits in a repeat group, or it is a multiple-choice question.
  - Without a sampling frame, this part invites analysts and admins to configure one, and tells viewers that none is defined.
- **Field check.**
  - The Overview check reports coverage as unavailable while there is no sampling frame, or while a dimension maps to a field the form no longer has, and suggests fixing the frame.
  - The Overview card titled "Performance dashboard", which only lists metric availability, is renamed "Form check" and links to the dashboard.
- **Freshness and cost.**
  - The dashboard is computed on request in PostgreSQL. Figures are reused for up to a minute, but only until a webhook delivery or a finished synchronization brings new submissions. The page shows when they were computed and has a **Refresh** button.
  - A computation has a time limit, and only two run at a time. One that takes too long fails with a clear message instead of hanging, and does not slow down the reception of submissions.

Not in this change:
- Filtering by enumerator or form version, and subtotals per dimension of the frame.
- Ranges in the sampling frame (an age range over an integer question). The form must store the range as a code, as today.
- Rejecting multiple-choice or repeat-group fields when a frame is saved. The dashboard flags them instead.
- Excluding test submissions or submissions Kobo marked as not approved, and removing duplicates.
- Locating `start` and `end` per form version. The deployed version's fields are used for every submission.
- Exports, alerts and automatic refresh.
- The clean-data and indicator dashboards (`add-clean-data-dashboard`, `add-indicators-dashboard`).

## Capabilities

### New Capabilities

- `dashboard-performance`: the performance dashboard and its four parts, the date range, and the rules for days, completion time, enumerators and coverage. This is the name the brief gives it, next to the later `dashboard-data` and `dashboard-indicators`.

### Modified Capabilities

- `projects`:
  - the field check reports coverage as unavailable without a usable sampling frame;
  - the enumerator identifier is the selected question first, then `username`;
  - projects gain a time zone that admins set.

## Impact

- **api:**
  - A new endpoint `GET /api/v1/projects/{id}/performance` (viewer and up), computed with SQL over `raw_submissions.data` in one read-only transaction with a statement time limit.
  - A one-minute in-memory cache, with concurrent identical requests sharing one computation.
  - `FieldCheck` changes for coverage and enumerator precedence.
  - `KoboForm` marks the form's questions that sit inside a repeat group (`FormField.InRepeat`), so the dashboard can warn that a sampling-frame dimension cannot be counted. It is stored in the form snapshot like the other keys.
  - `PATCH /projects/{id}` gains `timeZone`, validated against PostgreSQL's time zone names.
  - `ProjectDetails` gains `timeZone`.
  - A new error code for the time limit.
- **Database:** DbUp script `0006_add_performance_dashboard.sql`.
  - It adds `projects.time_zone` (default `UTC`).
  - It adds a partial index on `raw_submissions (project_id, submitted_at) WHERE NOT deleted`, for the date range.
- **UI:**
  - A new **Performance** tab, first in the project's tabs and the project's landing page.
  - Stat tiles, a daily bar chart drawn as inline SVG with a table view of the same numbers, a completion-time summary, an enumerator table, and a coverage table with progress bars.
  - Tables show as cards on phones, with paging for long lists.
  - In the Overview, the "Form check" card, a time zone selector for admins, and an enumerator selector admins can always use.
- **Docs:**
  - a new `docs/performance-dashboard.md`;
  - `docs/kobo.md` (field check, enumerator question, sampling frame matching, roles);
  - the README status;
  - `docs/tamiza-brief.md` (section 5.5, data model, plan).
- **Analytics:** none.
- **Configuration and dependencies:** none new. Charts need no library.
