# Spec Delta

## Purpose

Let every project member browse, filter, sort and export the tables that the project's scripts published for analysis, without database tools, reading only what the last successful runs published.

## ADDED Requirements

### Requirement: Clean data dashboard
Every project member SHALL be able to open the project's clean data dashboard. It SHALL read only the tables published by the project's runs and SHALL NOT read the stored submissions, contact Kobo or start a run. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens the dashboard
- **WHEN** a `viewer` opens the clean data dashboard of a project with published tables
- **THEN** the tables are shown

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests any part of its clean data dashboard
- **THEN** the API responds 404

#### Scenario: Last run failed
- **WHEN** script 2 published `households` in one run and failed in the next
- **THEN** the dashboard shows the `households` table of the earlier run

### Requirement: Sources of the dashboard
The dashboard SHALL offer two sources: the output tables published by script 2 and the clean tables published by the clean step. It SHALL open on the output tables when they are published and on the clean tables otherwise, and SHALL let the member switch to the other source when both are published. The flattened raw tables SHALL NOT be offered.

#### Scenario: Project with script 2
- **WHEN** the last runs published the output tables `households` and `persons` and the clean table `main`
- **THEN** the dashboard opens on the output tables, and the member can switch to the clean tables to see `main`

#### Scenario: Project without script 2
- **WHEN** the project has no script 2 and the clean step published `main` and `members`
- **THEN** the dashboard shows the clean tables and offers no switch

#### Scenario: Nothing published
- **WHEN** no run of the project has published clean or output tables
- **THEN** the dashboard says that there are no published tables yet, links analysts and admins to the scripts to start a run, and shows viewers the text only

### Requirement: Table selection
For the selected source, the dashboard SHALL list every published table with its row count, and SHALL show when the source was published and by which run. A member SHALL be able to select any listed table. A request for a table or source that is not published SHALL be answered as not found.

#### Scenario: Table list
- **WHEN** the output tables `households` (1,200 rows) and `persons` (4,830 rows) are published by one run
- **THEN** the selector lists both with their row counts, and the page shows the publishing time and the run's identifier

#### Scenario: Unknown table
- **WHEN** a member requests the table `salaries` and the source has no such table
- **THEN** the API responds 404 with a code that the table is not published

### Requirement: Paged table
The dashboard SHALL show the selected table one page at a time, with 25, 50 or 100 rows per page and 50 by default, together with the number of rows that match the filters. Paging, filtering and sorting SHALL be resolved on the server, so the browser never receives the whole table.

#### Scenario: Second page
- **WHEN** a member opens page 2 of a table with 120 rows at 50 rows per page
- **THEN** rows 51 to 100 are shown and the count says 120 rows

#### Scenario: Invalid page size
- **WHEN** a request asks for 500 rows per page
- **THEN** the API rejects it with a validation error on the page size

### Requirement: Columns
The dashboard SHALL show every column of the selected table, in the table's order, with its name, its type (integer, decimal, true/false, date, timestamp or text) and, when the published column carries a question label, that label. Text values longer than 500 characters SHALL be cut on screen and marked as cut. An empty value SHALL be shown as empty, distinct from an empty text.

#### Scenario: Question label of a flattened column
- **WHEN** the clean table `main` holds the flattened column `has_water`, labeled `Does the household have running water?`
- **THEN** its header shows `has_water`, the label, and the type text

#### Scenario: Script table without labels
- **WHEN** script 2 published the table `households` with the column `size`
- **THEN** the header shows `size` and its type, and no label

#### Scenario: Long text
- **WHEN** a cell holds a comment of 2,000 characters
- **THEN** the dashboard shows its first 500 characters marked as cut

### Requirement: Sorting
A member SHALL be able to sort the table by one column, ascending or descending, with empty values last in both directions. Rows with equal values SHALL keep a stable order between pages. Without a sort, rows SHALL keep the order in which the step published them.

#### Scenario: Sort by age descending
- **WHEN** a member sorts `persons` by `age` descending and some rows have no age
- **THEN** the oldest persons come first and the rows without age come last

#### Scenario: No sort
- **WHEN** a member opens a table without choosing a sort
- **THEN** its rows appear in the order the script returned them

### Requirement: Filters
A member SHALL be able to filter the table by any of its columns, with at most one filter per column, and rows SHALL match every filter. The available filters SHALL depend on the column's type. A filter whose value cannot be read as the column's type, or that names a column the table does not have, SHALL be rejected with a validation error.

#### Scenario: Two filters
- **WHEN** a member filters `municipality` equal to `Cali` and `age` from 18 to 29
- **THEN** only rows of Cali with an age from 18 to 29, both included, are shown and counted

#### Scenario: Value of the wrong type
- **WHEN** a request filters the integer column `age` with the minimum `adult`
- **THEN** the API rejects it with a validation error naming the column

#### Scenario: Unknown column
- **WHEN** a request filters the column `salary` and the table has no such column
- **THEN** the API rejects it with a validation error naming the column

### Requirement: Text filters
A text column SHALL offer *contains*, which ignores case, *equals*, which compares the exact value, and *is one of*, which matches any of up to 100 chosen values. For *is one of*, the dashboard SHALL offer the column's 50 most frequent values with their counts, optionally restricted to values that contain a search text, ignoring case.

#### Scenario: Contains ignores case
- **WHEN** a member filters `water_source` containing `well`
- **THEN** rows holding `Well`, `Deep well` and `WELL` match

#### Scenario: Picking frequent values
- **WHEN** a member opens the *is one of* filter of `municipality`
- **THEN** the dashboard lists its most frequent values with their counts, and choosing `Cali` and `Palmira` shows the rows of either

#### Scenario: Search among values
- **WHEN** the column `municipality` has more than 50 distinct values and the member searches `pal`
- **THEN** the list shows the most frequent values that contain `pal`, such as `Palmira`

#### Scenario: Value too long to filter by
- **WHEN** a column holds a value of 600 characters, longer than a filter accepts
- **THEN** that value is not listed among the values to pick from
### Requirement: Range filters
Integer, decimal, date and timestamp columns SHALL offer a range with an optional minimum and an optional maximum, both included. A timestamp range SHALL be given in days, in the project's time zone, and SHALL include the whole last day. A range whose minimum is greater than its maximum SHALL be rejected.

#### Scenario: Decimal range
- **WHEN** a member filters `income` from 500000 without a maximum
- **THEN** rows with an income of 500000 or more are shown

#### Scenario: Timestamp range by day
- **WHEN** the project's time zone is `America/Bogota` and a member filters `end` from 2026-09-01 to 2026-09-01
- **THEN** rows whose `end` falls on 2026-09-01 in Bogotá are shown, including one at 23:30 local time

#### Scenario: Reversed range
- **WHEN** a request filters `age` from 60 to 18
- **THEN** the API rejects it with a validation error naming the column

### Requirement: True/false and empty filters
A true/false column SHALL offer *is one of* true and false. Every column SHALL also offer *is empty* and *is not empty*; an empty text SHALL count as empty.

#### Scenario: Empty values
- **WHEN** a member filters `phone` as empty
- **THEN** rows with no value and rows with an empty text in `phone` are shown

#### Scenario: True values
- **WHEN** a member filters the true/false column `consent` to true
- **THEN** only rows whose `consent` is true are shown

### Requirement: Timestamps in the project's time zone
Timestamp values SHALL be shown in the project's time zone, and the dashboard SHALL say which zone it uses.

#### Scenario: Timestamp display
- **WHEN** the project's time zone is `America/Bogota` and a cell holds 2026-09-02 02:30 UTC
- **THEN** the dashboard shows 2026-09-01 21:30 and names the zone

### Requirement: Shareable view
The selected source, table, filters, sort, page and page size SHALL be kept in the page address. Opening that address SHALL show the same view to any member, and a part of it that no longer applies, such as a removed table or column, SHALL be dropped with a message instead of failing the page.

#### Scenario: Shared link
- **WHEN** an analyst sends a viewer the address of `persons` filtered by `municipality` and sorted by `age`
- **THEN** the viewer opens the same filtered and sorted table

#### Scenario: Column removed since
- **WHEN** a member opens an address that filters a column the table no longer has
- **THEN** the table is shown without that filter and a message says the filter was removed

### Requirement: Export
Every project member SHALL be able to export the selected table to CSV or XLSX, with the filters and sort applied and with every matching row, not only the current page. Exported values SHALL be complete, except as the XLSX format requires, and timestamps SHALL be written in the project's time zone.

#### Scenario: Filtered export
- **WHEN** a viewer exports `persons` filtered by `municipality` equal to `Cali`, which matches 830 of 4,830 rows
- **THEN** the file holds the header and those 830 rows in the chosen order

#### Scenario: Long text in an export
- **WHEN** a cell holds a comment of 2,000 characters
- **THEN** the exported cell holds all 2,000 characters

### Requirement: CSV format
A CSV export SHALL be UTF-8 with a byte order mark, comma separated, with a header row of column names and values quoted when they contain a comma, a quote or a line break. Empty values SHALL be empty fields, decimals SHALL use a dot, dates SHALL be `YYYY-MM-DD` and timestamps ISO 8601 with their offset. Values SHALL otherwise be written as published.

#### Scenario: Accents and commas
- **WHEN** a member exports a table with the value `Bogotá, D.C.`
- **THEN** the CSV holds `"Bogotá, D.C."` and a spreadsheet opening it shows the accent

#### Scenario: Timestamp in CSV
- **WHEN** the project's time zone is `America/Bogota` and a cell holds 2026-09-02 02:30 UTC
- **THEN** the CSV holds `2026-09-01T21:30:00-05:00`

### Requirement: XLSX format
An XLSX export SHALL hold one sheet named after the table, with a header row of column names and each value in a cell of its type: numbers as numbers, dates and timestamps as dates, true/false as booleans and text as text, never as a formula. An export of more than 1,048,575 rows SHALL be refused. A text longer than 32,767 characters SHALL be cut to that length, and the export SHALL report how many cells were cut.

#### Scenario: Text that looks like a formula
- **WHEN** a member exports to XLSX a cell holding `=1+1`
- **THEN** the cell shows the text `=1+1`, not 2

#### Scenario: Too many rows for XLSX
- **WHEN** a member exports 1,200,000 matching rows to XLSX
- **THEN** the export is refused with a message suggesting CSV or a filter, and no file is prepared

#### Scenario: Very long text
- **WHEN** an XLSX export holds one cell of 40,000 characters
- **THEN** that cell holds its first 32,767 characters and the dashboard says one cell was cut

### Requirement: Export download
The file SHALL be prepared on the server, and then offered through a link that downloads it once and expires a few minutes after it was prepared. The link SHALL work only for that file and SHALL NOT carry the member's access token. A prepared file SHALL be deleted once downloaded or expired.

#### Scenario: Download
- **WHEN** a member exports a table
- **THEN** the dashboard shows that the file is being prepared, and the browser then downloads it under a name made of the project, the table and the date

#### Scenario: Link used twice
- **WHEN** a download link is opened a second time
- **THEN** the API responds 404 and serves nothing

#### Scenario: Expired link
- **WHEN** a download link is opened after it expired
- **THEN** the API responds 404 and the file no longer exists on the server

### Requirement: Export limits
The operator SHALL be able to limit the size of an export file, and only a limited number of exports SHALL be prepared at a time. An export that exceeds the size limit SHALL fail with a message naming the limit and SHALL leave no file behind.

#### Scenario: File too large
- **WHEN** an export grows past the deployment's size limit
- **THEN** it fails with a message naming the limit and suggesting a filter, and its partial file is deleted

### Requirement: Republished tables
Every page, value list and export SHALL read the tables as they are published at the time of the request, and SHALL say when they were published. When a run republishes the source while a member is reading it, the next page SHALL show the new tables and the dashboard SHALL say that they were republished. A table that the run removed SHALL give a message and the table list SHALL be reloaded.

#### Scenario: Run finishes while browsing
- **WHEN** a member is on page 3 of `persons` and a run republishes the output tables
- **THEN** the next page comes from the new tables and the dashboard says when they were published

#### Scenario: Table removed by a run
- **WHEN** a member is reading `persons` and a run publishes output tables without `persons`
- **THEN** the next request says that `persons` is no longer published, and the selector shows the new tables

### Requirement: Time limit
Computing a page or a value list that takes longer than 25 seconds, or preparing an export that takes longer than the deployment's export time limit, SHALL be stopped and reported as too long. Other members' requests and the reception of submissions SHALL NOT be stopped by it.

#### Scenario: Page too slow
- **WHEN** computing a page takes more than 25 seconds
- **THEN** the dashboard shows a message that the request took too long, and the next request is answered normally

### Requirement: Responsive dashboard
The dashboard SHALL be usable from 360 pixels wide: the source switch, table selector, filters, paging and export SHALL be reachable without horizontal page scrolling, and the table SHALL scroll inside its own container at every width.

#### Scenario: Phone width
- **WHEN** a member opens a table of 80 columns on a 360-pixel-wide screen
- **THEN** the controls fit the screen and the table scrolls horizontally inside its container
