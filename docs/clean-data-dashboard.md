# Clean data dashboard

The clean data dashboard lets every member of a project browse, filter, sort and download the tables that the project's [scripts](scripts.md) published. It is the second tab of a project, after [Performance](performance-dashboard.md). Viewers can use all of it, including the exports.

It shows what the last successful runs published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, the tables of the last successful run stay on screen.

## What it shows

Two **sources** can be shown:

| Source | Tables | Published by |
|---|---|---|
| **Output tables** | The tables script 2 (`prepare_output`) returned. | The output step. |
| **Clean tables** | Script 1's tables, or the flattened tables when there is no script 1. | The clean step. |

The dashboard opens on the output tables when the project has published them and on the clean tables otherwise. When both exist, a switch above the table changes source. The flattened *raw* tables and the indicators are not shown here.

The **Table** list names every published table of the source with its row count. Below it, one line says when the tables were published and by which run (its short ID; analysts and admins also get a link to the runs list) and which time zone times are shown in. Without any published table the page says so; analysts and admins get a link to the scripts.

Each column header shows the column's name and, on one secondary line, the **question label** when the column has one (only flattened columns do, see [question labels](scripts.md#question-labels)) and its **type**: text, integer, decimal, true/false, date or timestamp. Empty values appear as a dash. Text longer than 500 characters is cut on screen and marked as cut; exports hold the full text.

Rows come in the order your script returned them until you sort a column, so sort the DataFrame in the script if the order matters ([Rules for what scripts return](scripts.md#rules-for-what-scripts-return)).

## Paging and sorting

The table is read from the database one page at a time, at 25, 50 (the default) or 100 rows per page. A line above the table gives the number of rows that match, with **Export** at its right end. The paging controls appear only when the table has more rows than the smallest page. Nothing is loaded into the browser except the page.

Click a column's name to sort by it: first ascending, then descending, then back to no sort. Empty values always come last. Rows with the same value keep one order from page to page.

## Filters

The filter button next to a column name opens that column's filter. A column has at most one filter, and a row has to match **all** of them. Active filters appear as chips above the table; click a chip to edit it, or its × to remove it.

| Column type | Conditions |
|---|---|
| Text | **contains** (ignores upper and lower case), **is exactly**, **is one of** |
| Integer, decimal, date, timestamp | **is between**, with a minimum, a maximum or both; both ends are included |
| True/false | **is one of** true and false |
| Any | **is empty**, **is not empty** (an empty text counts as empty) |

- **Is one of** lists the 50 most frequent values of the column with how many rows hold each; type in the search box to find others. Values longer than 500 characters are not listed. The list does not change as you add other filters.
- A **timestamp** is filtered by whole days in the project's time zone, and the last day is included: from 2026-09-01 to 2026-09-01 is that entire day. Timestamps are shown in the same zone, which the page names; hover a cell for the full value.
- Numbers use a dot as the decimal separator. A value the column cannot hold (`adult` for an age) is refused with a message in the filter, and the filter is not applied.

## The address keeps the view

The source, table, sort, page, page size and filters are kept in the page address. Reloading the page, or sending the link to another member, shows the same rows. If the address names something that no longer exists (a column a run removed, a table that is no longer published), the dashboard leaves that part out and tells you what it dropped.

## When a run publishes again

A run replaces the published tables as a whole. If one finishes while you browse, the next page, filter or export comes from the new tables and the page says when they were republished. If a run removed the table you were reading, the page says so and shows the first table of the new list.

## Exports

**Export**, on the line with the row count, downloads the whole table, with the filters and sort you have applied, as:

- **CSV**: UTF-8 with a byte order mark (so Excel shows accents), comma separated, with a header row of column names. Dates are `2026-09-01`, timestamps `2026-09-01T21:30:00-05:00` in the project's time zone, decimals use a dot, true/false are `true`/`false`, and empty values are empty fields. Values are written as published. Spreadsheet programs read a text that starts with `=`, `+`, `-` or `@` in a CSV as a formula, so use XLSX when the data may hold such text and you open it in a spreadsheet.
- **XLSX**: one sheet named after the table, a header row, and each value in a cell of its type: numbers as numbers, dates and timestamps as dates (a timestamp keeps the project's local time, since Excel has no time zones), true/false as booleans, and text as text, never as a formula. A sheet holds at most 1,048,575 rows, so a bigger export is refused with a suggestion to use CSV or a filter. A text longer than Excel's limit of 32,767 characters is cut, and Tamiza tells you how many cells were.

The file is prepared on the server first: the Export button shows *Preparing the export…* while the database is read, and then the browser downloads the file under the name `<project>_<table>_<date>.<csv|xlsx>`. The download link works **once** and expires about five minutes after the file is ready; opening it again, or after it expired, gives a "not found". The link does not need your sign-in, so do not share it. Preparing an export does not stop a run from publishing: the tables are only held while the file is being written, not while it downloads.

An operator can limit the size of a file and how many exports are prepared at once; see [operations.md](operations.md#common-changes). An export that exceeds the size limit fails with a message and leaves nothing behind.

## Time limits

A page, a list of values or an export that takes too long is stopped with a message ("took too long"); add a filter to read fewer rows or try again. This never affects other members or the reception of submissions.

A table of 500,000 rows with 60 columns (about 1 KB per row) answers its first page in about a tenth of a second to a third of a second, a text filter in about 0.3 seconds, and a page 5,000 pages in, sorted by a text column, in about 1.5 seconds. An export of all those rows takes about 4 seconds to prepare as CSV and 16 to 22 seconds as XLSX, with the `api` container staying under 200 MiB. These times were measured on a development machine with the default limits; the change's design records them.
