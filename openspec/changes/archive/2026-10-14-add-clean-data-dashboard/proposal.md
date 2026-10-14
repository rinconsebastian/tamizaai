# Proposal

## Why

Once a run has cleaned the data, analysts and their clients still have to open `psql` or a BI tool to look at the result, or ask an analyst for an export. The tables that script 2 prepares for this purpose (or the clean tables, when there is no script 2) are published in the project's schemas, but nothing in Tamiza shows them. The second dashboard of the brief, a queryable table with filters and exports, is what turns the published tables into something a client can browse and download. The performance dashboard is done, so this one is next in the plan.

## What Changes

- **Clean data dashboard.** A new **Clean data** section, the second tab after Performance. Every member can open it, viewers included. It reads the tables that the last successful runs published, never the raw submissions, and it does not start runs.
- **Source.** It opens on the **output** tables (script 2) when they are published, and otherwise on the **clean** tables (script 1, or the flattened tables without script 1). When both are published, a switch shows the other. The flattened raw tables are not offered.
- **Table selector.** One entry per published table of the source, with its row count. The page shows when the source was published and by which run.
- **The table.**
  - Paged on the server: 25, 50 (default) or 100 rows per page, with the number of matching rows.
  - Each column shows its name, its type, and the question's label when the column carries one (flattened columns do).
  - Text longer than 500 characters is cut on screen and marked as cut. Exports have the full values.
  - Wide tables scroll inside their own box, at every width.
- **Sorting.** By one column, ascending or descending, with empty values last. Without a sort, rows keep the order the script wrote them in.
- **Typed filters per column, combined with AND, resolved on the server:**
  - text: *contains* (ignoring case), *equals*, or *is one of* values picked from the column's most frequent values, with a search;
  - integers, decimals, dates and timestamps: a range with an optional minimum and maximum, both included. Timestamps are shown and filtered by day in the project's time zone;
  - true/false: *is one of*;
  - every column: *is empty* or *is not empty*.
- **Shareable view.** The source, table, filters, sort and page are kept in the page address, so a reload or a shared link shows the same view.
- **Export to CSV and XLSX.** Any member can export the whole table, with the filters and the sort applied, not only the current page.
  - The API writes the file, then gives the browser a single-use link that expires after a few minutes; the download is streamed from that file. No access token appears in the link.
  - CSV is UTF-8 with a byte order mark and a header of column names. XLSX writes text as text, never as formulas. XLSX is refused above Excel's 1,048,575 rows, with a suggestion to use CSV or filter. Cells longer than Excel's 32,767 characters are cut, and the export says how many.
  - A deployment-wide limit caps the size of an export file, and only a few exports are prepared at a time.
- **Republishing while reading.** When a run republishes the tables, the next page shows the new data and says so. A table that a run removed gives a clear message and the selector reloads.
- **Publishing does not stall readers.** A run that publishes while someone reads or exports a table waits for that reader in short attempts instead of queuing every other reader behind it.
- **Time limit.** A page, a value list or the preparation of an export that takes too long is stopped with a clear message, without affecting other users or the reception of submissions.

Not in this change:
- Editing values, saved views, column hiding or reordering, and charts.
- OR between filters, and filters on more than one condition per column.
- Exports of all tables at once, of the flattened raw tables, or with question labels as a second header row.
- Altering CSV values to defuse spreadsheet formulas (the file keeps the data as published).
- The indicators dashboard (`add-indicators-dashboard`).

## Capabilities

### New Capabilities

- `dashboard-data`: the clean data dashboard: source and table selection, the paged table, sorting, typed filters, the shareable view, CSV and XLSX exports with single-use download links, freshness after a run, and time limits. This is the name the brief gives it, next to `dashboard-performance`.

### Modified Capabilities

- `auth`: export download links are a new kind of public endpoint, protected by a single-use, short-lived link instead of an access token.
- `pipeline`: atomic publishing gains a rule that a step waiting for readers of its published tables does not block new readers for more than a few seconds.

## Impact

- **api:**
  - New endpoints under `/api/v1/projects/{id}/data` (viewer and up): the published sources and tables, a page query with filters and sort, the frequent values of a column, and the preparation of an export.
  - A new public endpoint `GET /api/v1/data-exports/{token}` that streams a prepared file once.
  - Queries are built from validated names only: the source from an allow list, tables and columns from PostgreSQL's catalog, values always as parameters. Read-only transactions with a statement time limit, in the project's time zone.
  - Exports written with MiniExcel (already a dependency) and a CSV writer, to a temporary directory that is cleaned on expiry and at startup.
  - New options (`DataDashboard__*`) and error codes.
- **analytics:** the publishing swap uses a short lock wait with retries.
- **Database:** no migration. The dashboard reads the published `p_<id>_output` and `p_<id>_clean` schemas and `tamiza.run_steps`.
- **UI:** a **Clean data** tab and page: source switch, table selector, the data table with sortable headers, a filter dialog per column with removable filter chips, paging, export buttons. Responsive from 360 px.
- **nginx:** a longer read timeout for the export preparation endpoint only.
- **Docs:** a new `docs/clean-data-dashboard.md`; `docs/scripts.md` (what the dashboard shows from scripts 1 and 2); `docs/operations.md` (export limits and temporary files); the README status; `docs/tamiza-brief.md` (section 5.6 and the plan).
- **Dependencies:** none new.
