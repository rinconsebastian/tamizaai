# Design

## Context

This change builds on `add-project-management`, `add-performance-dashboard`, `add-choice-labels` and `restructure-analysis-ui`. The motivation is in `proposal.md` (Why), and the requirements are in `specs/projects/spec.md`. Current state that shapes the approach:

**Frame and save**
- `tamiza.sampling_frames` holds `dimensions = [{name, field}]` (field = xpath) and `targets = [{values, target}]`.
- `SamplingFrameValidator` enforces:
  - 1 to 10 dimensions with unique names (ignoring case), at most 10,000 rows, no minimum;
  - whole targets from 0 to 1,000,000;
  - every value non-empty;
  - combinations unique after trimming, ignoring case.
- Save (`PUT /sampling-frame`, analyst) also checks that each field is in the snapshot. Nothing compares values with the form's choices or limits a value's length.

**Import** (`POST /sampling-frame/import`, analyst, preview only)
- `SamplingFrameImport.Read` takes the first non-blank row as the header. It reads only the first XLSX sheet, and sniffs `,` or `;` from the first CSV line.
- Every named column except the first `target` (ignoring case) is a dimension. Columns without a name are dropped silently.
- An empty target is a row error.
- It reads `Take(MaxRows + 2)` raw rows, and blank rows count toward that cut.
  - MiniExcel returns XLSX gaps as empty rows, and separator-only CSV lines such as `;;;` (what Excel writes for a blank row) as empty cells. A file with 10,000 data rows and such blank rows among them loses its last rows without an error.
  - MiniExcel drops truly empty and whitespace-only CSV lines instead. Rows are numbered by position (`headerIndex + offset + 2`), so every row number after such a line is too low.
- XLSX numbers become invariant text, so `05001` stored as a number arrives as `5001`.

**Coverage**
- The Performance dashboard matches frame values with answers after trimming both (spaces, tabs, line breaks and non-breaking spaces) and ignoring case (`lower(btrim(…))` on each side, `PerformanceSql`).
- Frame values must therefore be choice **codes** (`FormChoice.Name`, the deployed `$autovalue`), not labels.

**Form snapshot**
- `projects.form_fields` holds `FormField(Name, Xpath, Type, Label, ListName, OrOther, InRepeat)`, and `projects.form_choices` holds `FormChoice(ListName, Name, Label)` in form order.
- `KoboForm` drops exact duplicate codes; codes that differ only by case stay.
- `GET /form-fields` (viewer) returns the fields. No endpoint returns the choices.
- Select types:
  - `select_one` when Kobo's JSON uses `select_from_list_name`, and `select_one_<list>` from XLSForm text.
  - `select_one_from_file_…` also starts with `select_one`, and its `ListName` is usually null.
- The `other` choice of an or_other question is not in the snapshot: pyxform adds it on deployment. The analytics labels add it as `other` / `Other`.

**Downloads**
- Data exports and sample downloads write a file to `ExportRegistry.NewFilePath()` and register it with `ExportRegistry.Add`. They return `{downloadUrl: "/api/v1/data-exports/{token}", fileName, …, expiresAt}`.
- The UI calls `Downloader.start(url)`. The link is public, single-use and lasts 5 minutes (auth requirement "Single-use download links").
- File names come from `ExportFileName.Create(project, table, day, ext)`.
- Data exports and the sample-download copy share 2 export slots (`MaxConcurrentExports`) with a 25 s queue timeout, then 503.

**MiniExcel 1.46.0** (verified with probes during planning)
- What it can do:
  - write several sheets in one `SaveAs` over an ordered dictionary, which `Query` reads back first-sheet-first;
  - write string cells that keep `05001`.
- How its options apply in one `SaveAs`:
  - `DynamicColumns` (widths, formats) apply **by column name to every sheet**, and two entries with the same key make it throw.
  - `FreezeRowCount` (default 1) and `AutoFilter` (default on) apply to **every sheet**, whatever its columns are called. There is no per-sheet setting.
- An empty row list writes a sheet with no header row. `XlsxExport.Write` already works around this with a `DataTable`.
- Every text cell is written inline, not through shared strings, so the size grows with value length. A workbook with 10,000 rows × 10 code columns, 10 label columns at 200 characters and `target` is about 3.4 MB (about 1.4 MB with 20-character labels).
- What it cannot do: data validation (dropdowns), per-cell styles, comments, sheet protection.

**UI**
- `project-sampling-frame.ts` opens the editor itself for analysts when there is no frame (`editing.set(!frame && canEdit())`), and also when loading the frame fails (`editing.set(this.canEdit())`). The first case is tested in `project-sampling-frame.spec.ts` and documented in `docs/kobo.md`.
- **Cancel** is shown only when there is a frame or unsaved edits (`frame() || dirty()`), and `cancel()` keeps the editor open when there is no frame.
- `import-frame-dialog.ts` uploads the file and shows the first 20 row errors. It maps each dimension with `guessField`, which picks the first field whose name, xpath or label equals the header.
- Building blocks already exist:
  - shared UI: `tmz-disclosure`, `tmz-card`, `tmz-alert`, `ConfirmDialog`, `ToastService`;
  - the full-screen-on-phones dialog layout of `data-filter-dialog.ts`;
  - `Downloader`.
- `ProjectContext` caches `formFields` until the next form check: `update()` clears them when `formCheckedAt` changes.

## Goals / Non-Goals

**Goals:**
- One source of truth for codes. The API writes every file from the snapshot, and the UI uses the same lists to count rows and show labels.
- A downloaded template, filled in, imports with no errors and with every column preselected. This holds once its planned rows have targets and every column left empty is filled in those rows.
- A downloaded frame imports back to the same rows and targets.
- The frame's save rules, limits, stored shape and coverage matching stay exactly as they are.
- Everything works at 360 px. The template download and the saved-frame **Download** follow the web-ui rule "Feedback while an action runs".

**Non-Goals:**
- Reading files in shapes the import does not read today: title rows above the column names, merged cells, Windows-1252 CSV.
- Rewording import errors as fix-it instructions, loading files that have row errors into the editor, and value hints.
- Dropdowns, protection, hidden sheets or comments in the workbook, and a CSV template.
- `choice_filter` cascades, choices from older form versions, and labels in other languages.
- Changes to the reference-table import, and audit events.

## Decisions

### D1. The API writes the workbooks; the UI reads choices from a new endpoint

New endpoints, all under `/api/v1/projects/{projectId:guid}`:

| Endpoint | Role | Response |
|---|---|---|
| `GET /form-choices` (`GetFormChoices`, in `ProjectEndpoints.cs`) | viewer | `[{listName, name, label}]` from `Project.FormChoices`, in snapshot order |
| `POST /sampling-frame/template` (`DownloadSamplingFrameTemplate`, in `SamplingFrameEndpoints.cs`) | analyst | `SamplingFrameFile(DownloadUrl, FileName, RowCount, ExpiresAt)`, file name `ExportFileName.Create(name, "sampling-frame-template", today, "xlsx")` |
| `POST /sampling-frame/export` (`DownloadSamplingFrame`, in `SamplingFrameEndpoints.cs`) | viewer | the same record, file name `…"sampling-frame"…`; `409 samplingFrame.none` "This project has no sampling frame yet." when there is none |

- `GET /form-fields` keeps its list shape. The UI types `listName` and `orOther`, which it already receives.
- The downloads are `POST`s because they prepare a file, as the data exports do. A non-member gets 404 and no link.
- The UI needs the choices anyway, for choice subsets, the live row count, the labels in the picker and the guide's example. Generating the file on the server keeps the code rules (text cells, header names, `#` columns) in one place, next to the importer that must read the file back.

Alternatives considered:
- **An XLSX library in the browser.** It adds a new dependency and a Blob download path the app does not have.
- **A CSV built in the browser.** Excel strips leading zeros from CSV and saves it as ANSI.
- **Reusing `XlsxExport`.** It is single-sheet by design and tied to `TableColumn`.

### D2. Delivery through the single-use link, without an export slot

- The endpoint writes the workbook to `ExportRegistry.NewFilePath()` and registers it with `exports.Add(projectId, user.Id, path, fileName, DataDashboard.XlsxContentType)`. It deletes the file in a `finally` when registration did not happen, as `SampleDownloadEndpoints.LinkAsync` does.
- No file is written on any 400 or 409.
- It does **not** take an export slot. The file is under 4.5 MB (D3) and takes about a second to write, but behind two long data exports it would wait 25 s and then fail with 503.
- Alternatives considered:
  - **Taking a slot.** The wait and the 503 above.
  - **An authenticated blob fetch.** The app has none; the auth interceptor covers `HttpClient` only.

### D3. Workbook layout

The new writer is `Projects/SamplingFrameWorkbook.cs`. It makes one `MiniExcel.SaveAs` call over an ordered `Dictionary<string, object>` with three visible sheets, in this order.

- `DynamicColumns` are set only for Frame's columns, one entry per name: width and the text format `@` for code columns, and width for `#` label columns and `target`. Guide and Choices get none.
- Every Guide and Choices column name contains a space. Template headers are XLSForm names or paths, which never do, so no Frame option reaches them. A saved frame's hand-typed dimension name could still match one; that changes only a width or format, never a value, and is accepted.
- The frozen first row and the autofilter apply to all three sheets, because they cannot be set per sheet. That is accepted.

**Text cells**
- Label cells on Frame are cut to 200 characters, ending in "…" when cut.
- Every other text cell is cut at Excel's 32,767-character cell limit, never on half a surrogate pair. This uses `XlsxExport.CutToCell`, made internal for reuse.

**1. `Frame`.** It must be first, because the importer reads only the first sheet.
- **Header** in row 1, with nothing above it. For each chosen question, in request order (the UI sends form order):
  - a **dimension column** named `FormField.Name`. The xpath is used instead when any other field of the snapshot has the same name, ignoring case (notes and metadata count), or when the name equals `target`, ignoring case;
  - for a question with choices, a **`# <column> label`** column right after it.
- The last column is `target`.
- **Rows** are the cartesian product of the non-empty value lists, with the first column varying slowest.
  - Choices keep form order; typed values keep the order they were sent in.
  - An empty value list does not multiply the rows, and its cells stay blank.
  - With no non-empty list there are no rows (D7).
- **Cells:**
  - dimension cells are string cells with the code;
  - label cells hold the cut `FormChoice.Label`, or are blank;
  - `target` is blank.
- **No rows.** Frame is written from a `DataTable` with one string column per header, as `XlsxExport.Write` does, so the header row still exists. This covers both a template without rows and a saved frame with no targets.

**2. `Guide`.** One column, "How to fill this file". The numbers come from the `SamplingFrameValidator` and `SamplingFrameImport` constants. Its points:
- only the first sheet is read;
- each row is one group; write a whole number from 0 to 1,000,000 in `target`, without separators;
- an empty target leaves the row out, and `0` keeps it;
- keep the column names; `#` columns are notes and you can add your own;
- write codes, not labels (see Choices);
- format new cells as Text before typing codes such as 05001, and ignore Excel's green triangle;
- written only when the file has such a column: a column left empty (a question you typed no values for) needs a value in every row you give a target;
- each combination at most once; the limits;
- save as Excel Workbook (.xlsx) and import it in Settings > Sampling frame;
- the project, the date and the form version it was prepared from, and to download a new template when the form changes;
- when D3's size fallback applied, what was left out ("Your form has more choices than this sheet lists…", "Label columns were left out to keep the file importable").

**3. `Choices`.** Columns `question column | choice code | choice label | in this file`.
- One row for every choice of each chosen question's list (after D7's deduplication), so rows can be added later. The added `other` is included.
- `in this file` is `yes` or `no`.
- Codes are string cells, and labels are cut as above.

**Size fallback.** The import limit is 5 MB, so the writer keeps every file under 4.5 MB:
1. When the written file is larger than that, it writes it again with Choices limited to the codes in the file.
2. If it is still larger, it writes it again without the `#` label columns.
3. If it is still larger:
   - a template request is refused: "This template would be larger than the 5 MB import limit. Choose fewer choices or questions.";
   - a saved-frame download is served anyway, and its Guide says that the file is too large to import back as it is.

Each write takes about a second.

**Saved-frame download.** The same writer, with these differences:
- the headers are the saved dimension names. A name the importer would not read back as a dimension (it starts with `#`, or equals `target`, ignoring case) is written as the dimension's field xpath instead. If that xpath is `target` or is already another header (ignoring case), ` (<n>)` is appended, where n is the dimension's position. The Guide names each renamed column and its saved name;
- the values are the stored codes;
- a `#` label column is added only where the dimension's field still has a choice list, with labels looked up by list and code, ignoring case, as `ChoiceLabels` does, and cut as above;
- `target` holds the saved numbers as numeric cells;
- the Guide's last line says that importing the file replaces the frame after review and Save.

Alternatives considered:
- **Dropdowns.** They need a template file or patched `dataValidations` XML. The probe's XML was out of OOXML order, and CI cannot open Excel to check.
- **The Guide as the first sheet.** It would be imported as data.
- **Hidden helper sheets.** They confuse users who unhide them.
- **Separate `MiniExcel.Insert` calls per sheet**, to get per-sheet freeze and filter. It is more code for a frozen header that helps on Choices anyway.

### D4. `#` columns are notes; data under a column without a name is listed

- After trimming, a header that starts with `#` is ignored and returned as `{column: "B", name: "# municipio label", reason: "note"}`.
- Non-empty cells under an empty header, or beyond the header width, are returned once per column as `{column: "F", name: null, reason: "unnamed"}`. Today they are dropped silently.
- This is what lets labels sit next to codes on Frame. XLSForm names start with a letter or underscore, so no question or path can collide with the rule. The saved-frame download renames hand-typed `#` dimensions (D3).
- Alternatives considered:
  - **Labels only on the Choices sheet.** Users would have to flip between sheets while filling targets.
  - **A `(label)` suffix rule.** It is fragile when users rename columns.
  - **Converting labels to codes on import.** The mapping is not known on the server at import time.

### D5. An empty target means "not planned"

- In the frame import only, a row whose target cell is empty after trimming is left out:
  - it is not validated;
  - it does not take part in duplicate detection;
  - its row number goes to `rowsWithoutTarget`.
- `0` is kept.
- If the target column is missing, today's header error stands, and no row is left out.
- Rows without a target still count toward the 10,000 non-blank data rows. A template never has more than 10,000.
- Save rules do not change: the editor still needs a target in every row. That is why the editor gets **Remove rows without a target (N)** (D9).
- Alternatives considered:
  - **Prefilling `0`.** It fills coverage with groups nobody plans.
  - **Keeping empty as an error.** Users would have to delete hundreds or thousands of unplanned template rows.

### D6. Preview fields, the row-limit fix and row numbers

`ImportPreview` gains two fields. Both are additive JSON, so older UIs ignore them.
- `rowsWithoutTarget: int[]`, row numbers in order.
- `ignoredColumns: [{column, name, reason}]`, where `column` is the spreadsheet letter.

**Row limit.** `ReadTable` stops taking `MaxRows + 2` raw rows. It streams the rows, skips blank ones in its own loop and returns the file error once the non-blank data rows pass 10,000. It never sets MiniExcel's `IgnoreEmptyRows`. This makes the existing requirement hold: a file within the limits is read whole.

**Row numbers** in errors and in `rowsWithoutTarget` are the numbers a spreadsheet program or text editor shows.
- **XLSX:** the row's position in `SpreadsheetReader.Rows`, because MiniExcel fills sheet gaps with empty rows.
- **CSV:** `SpreadsheetReader.Rows` gains an optional `keepBlankLines` argument, false by default, so the reference-table import is unchanged. With it, the reader passed as `CsvConfiguration.StreamReaderFunc` returns the separator for an empty or whitespace-only line, so MiniExcel keeps that line as a blank row (verified with 1.46.0). A quoted value that spans lines still counts as one row.

This also corrects today's row-error numbers after empty CSV lines. The reference-table import is unchanged.

### D7. Template request rules

Body: `{ "dimensions": [ { "field": "<xpath>", "values": ["…"] | null } ] }`

**Questions with choices.** A field has choices when all of these hold:
- its type starts with `select_one`;
- its type does not start with `select_one_from_file`;
- `ListName` is set, and the list has at least one choice.

**Choice list.** A question's choice list is built as follows:
- the list's codes, trimmed and deduplicated ignoring case, keeping the first in form order, as `ChoiceLabels` does;
- on an or_other question, `other` labelled `Other` is added after them, unless the list already has a code equal to `other`, ignoring case.

That list is what the picker offers, what the row count multiplies, and what the Choices sheet and the saved-frame label lookup use.

Rules for a question with choices:
- `values: null` means the list's own choices, without the added `other`. `[]` is refused: "Choose at least one choice."
- Each value must be a code of the list, matched exactly and then ignoring case. The matched code is written to the file.
- Duplicates are dropped, ignoring case.

**Other questions.** `null` or `[]` gives an empty column. Each value is trimmed. Blank values are dropped, and so are duplicates ignoring case, keeping the first. When nothing is left, the column is empty.

**Row count.** It is 0 when no value list is non-empty. Otherwise it is the product of the lengths of the non-empty lists. `RowCount`, the picker counter and the toast all use this number.

**Refusals** (400 `ValidationProblem` on `dimensions`, `dimensions[i].field` or `dimensions[i].values`):
- 0 or more than 10 questions;
- a field the snapshot does not have, or the same field twice;
- `select_multiple…`, or `InRepeat`;
- a top-level question whose xpath equals `target`, ignoring case: "'Target' cannot be used: its name is reserved for the target column.";
- two columns whose names are equal, ignoring case;
- an unknown choice: "'zz' is not a choice of 'municipio'.";
- a typed value longer than 500 characters: "A value of 'estrato' is longer than 500 characters.";
- more than 10,000 rows: "These choices make 11,000 rows; a sampling frame holds at most 10,000. Choose fewer choices or questions." The count uses saturating 64-bit multiplication and prints "more than 1,000,000" past that;
- a file over the size fallback (D3).

Metadata types are not refused, the same as on save. The UI does not offer them.

**Mirrored in the UI.** `sampling-frame-fields.ts` mirrors these rules, and the API stays the authority for the file. The shared helpers:
- `templateIssue`: `fieldIssue`, or `target` for a top-level `target` question;
- `hasChoices` and `choiceList`;
- `templateHeaders(snapshot, xpaths)`: takes the unfiltered `ProjectContext.formFields()`, not `frameFields`, so its name-clash test sees the same fields as the API's;
- `normalizeValues`, `combinationCount` and `combinations`.

Both sides are tested against the same cases, listed in both test suites.

### D8. Multiple-choice, repeat and `target` questions are not offered by the guided path

- The picker lists multiple-choice and repeat questions disabled, with the existing `frame.warning.*` reason. It lists a top-level `target` question disabled with `frame.picker.targetName`: "Its name is reserved for the target column. Map it in the editor under another name."
- The template endpoint refuses all three.
- The editor is unchanged: it still allows multiple-choice and repeat questions with today's warning.
- Alternative considered: **allowing them with a warning**, as the editor does. The guided path should not produce a frame coverage cannot count.

### D9. One question picker, two outputs

The new `frame-questions-dialog.ts` uses the `data-filter-dialog` layout: full screen on phones and a panel from `md` up. It adds a new sticky footer. It has two views, held in one signal: the questions, and the choices of one question. No stepper component is needed.

**Data.** It receives the unfiltered field snapshot, for `templateHeaders`, and filters metadata only for the list it shows. While choices load it shows a skeleton; after a failed load, "The form's questions could not be loaded." with **Try again**.

**Questions view**
- Three groups:
  - **Questions with choices**, for example "Municipality · municipio · 1,100 choices";
  - **Questions you type the values for**, with an optional "Values, one per line" textarea;
  - **Coverage cannot count these**, disabled (D8).
- The textarea is split into lines and cleaned with `normalizeValues` before the count, the request and **Create the rows here**. A line over 500 characters shows an inline error and disables both actions.
- A search box, and a counter "N of 10". At 10 the remaining checkboxes are disabled.

**Choices view**
- Search, **Select all** and **Clear**.
  - With a search term, they change only the matching choices, and their labels say so ("Select the 12 shown"). Choices hidden by the search keep their ticks.
  - Without a search term, they act on the whole list.
- The view shows "N of M chosen" for the whole list.
- Rows read "Medellín — 05001", and the added `other` reads "Other — other". All choices start ticked, the added `other` unticked.
- Whenever the ticked set is not exactly the list's own choices, the request sends the codes explicitly.
- A tip says to choose only the municipalities you sample, or only the more detailed question, when one question narrows another.

**Footer**
- The live count, for example "5 × 2 × 3 = 30 rows", announced politely.
- Above 10,000 it shows the limit message, and both actions are disabled.

**Outputs**
- **Download Excel template** (primary) is busy while the request runs. It calls `Downloader.start`, closes the dialog and shows one toast: "Template downloaded with 30 rows. Fill in the target column, then import it here." A refused request shows its message in the dialog.
- **Create the rows here** returns `{dimensions, rows}`, and the page loads them into the editor:
  - the dimensions are mapped to their questions, and the targets are empty;
  - a question the editor already maps keeps that dimension's name, and new questions take the template header;
  - when the editor already has rows, the page asks before replacing them (`frame.editor.replaceRows`);
  - targets carry over only when the chosen questions are exactly the questions the editor's dimensions map to (same set, any order, each mapped by one dimension). A new row then keeps the target of the first editor row with the same value for every question, trimmed and ignoring case. Otherwise every target starts empty, and the confirmation adds: "The questions changed, so the current targets will not be kept."
  - with 0 rows, the mapped dimensions are loaded with no rows.

**Opening from the editor.** **Create rows from questions** opens the picker with the editor's mapped questions ticked, when the picker can offer them:
- for a question with choices, the codes matching a value in the rows are ticked, ignoring case; if none match, every choice starts ticked;
- for a question without choices, the textarea is filled with the column's distinct trimmed values, in row order;
- when the editor already has rows, the replace confirmation also names what the new rows will not keep: the dimensions that cannot be created from questions (unmapped, missing from the form, multiple-choice or repeat), and the number of rows whose values are not choices of their question.

The editor also offers **Remove rows without a target (N)** when N > 0.

Alternatives considered:
- **Template only.** Phone users, and anyone with a small frame, would still need Excel.
- **A separate row generator.** Two mental models for the same choice.

### D10. A start panel replaces the auto-opened editor

**Without a frame**
- An analyst or admin sees a `tmz-card`:
  - a heading ("Plan your sample") and one sentence on what the frame is for;
  - three buttons, stacked full width on phones: **Choose questions** (primary), **Import a file** and **Type it in**;
  - a closed `tmz-disclosure` with the guide.
- **Type it in** opens today's empty editor.
- Viewers keep the existing sentence.

**Editor**
- The editor always shows **Cancel**: the `frame() || dirty()` condition goes.
- `cancel()` discards the edits and closes the editor (`editing.set(false)`). It returns to the read view with a frame, and to the start panel without one.

**With a frame**
- The read view's header shows **Edit** (analyst+), **Download** (every member) and **Import from file** (analyst+).
- **Download** is busy (`aria-busy`, disabled) while `POST /sampling-frame/export` runs.
  - On success it calls `Downloader.start` and shows one toast (`frame.download.ready`).
  - On failure it shows one error toast with the API message and does not call `Downloader.start`.

**Load failure**
- When the frame cannot be loaded, the page shows the API's message in a danger `tmz-alert` with **Retry**. It shows no start panel, editor, sentence, Edit, Download or Import until a load succeeds.
- The constructor's error branch sets a `loadFailed` signal instead of `editing`. Otherwise an analyst could save a new frame over one they never saw, because Save replaces the frame.

This follows the web-ui requirement "Set-once settings shown as values": controls appear on request.

Alternative considered: **keep auto-opening the editor with the guide above it.** The empty grid and the Import button keep competing for attention.

### D11. A shared guide, and hand-off by closing dialogs

- `frame-file-guide.ts` renders an example table and the rules. The table is built from the form's first question with choices (two codes) once choices are loaded. Otherwise it shows a fixed example: `municipio | # municipio label | sexo | target`, with an empty target in one row. The page does not load choices just for the guide.
- The import dialog moves to the `data-filter-dialog` layout and shows the guide in a disclosure, open when the project has no saved frame.
- The guide's link "Get a template with your form's codes" closes the import dialog with `{kind: 'template'}`, and the page then opens the picker. No dialog opens on top of another.
- The import dialog also gets:
  - the file input as a busy button ("Reading the file…");
  - protection against stale responses, so choosing a new file drops the earlier result;
  - plural-aware summaries.
- It gets a notice for left-out rows and ignored columns:
  - the count ("2,170 rows without a target are left out");
  - the rows as ranges of consecutive numbers ("rows 2–41, 43–2,201"), at most 20 ranges, then "and N more rows";
  - the ignored columns by letter and name ("B · # municipio label").

  Only the count sentences sit in the `aria-live` region.

### D12. Mapping preselection

`guessField(fields, header, saved)` compares ignoring case and tries:
1. the field the saved frame maps a dimension of the same name to, if it is still in the form;
2. a field whose xpath equals the header;
3. the only field with that name;
4. the only field with that label.

- **Conflict.** When step 1 gives a field and the header is also the xpath, or the only name, of a different field, the file cannot tell a downloaded frame from a template. Nothing is preselected, and the select shows "Saved frame: {saved} · Form question: {other}" (`frame.preview.mappingConflict`).
- **No match.** Nothing is preselected.
- Without a conflict:
  - the template headers (the name, or the xpath on a clash) make step 2 or 3 hit;
  - step 1 makes downloaded frames round-trip with renamed dimensions;
  - renamed reserved columns (D3) hit step 2.

The dialog receives `savedDimensions` from the page. "Nothing changes until the user maps and saves" still holds: the user sees and confirms each preselection.

### D13. i18n and error codes

- New `frame.*` groups in `en.json`:
  - `frame.start.*` and `frame.guide.*`;
  - `frame.picker.*` (including `targetName`), `frame.template.*` and `frame.download.*` (including `ready`);
  - `frame.preview.*` (`summaryOne`/`summaryMany`, `rowsWithoutTargetOne`/`rowsWithoutTargetMany`, `rowRanges`, `moreRows`, `ignoredNote`, `ignoredUnnamed`, `reading`, `mappingConflict`);
  - `frame.editor.*` (`createRows`, `removeEmpty`, `replaceRows`, `targetsNotKept`) and `frame.loadFailed`.
- `importIntro` and `dimensionsIntro` are rewritten. `dimensionsIntro` adds that values are choice codes, not labels.
- `errors.codes.samplingFrame.none` is added, with its entry in the error-code test.
- Plurals follow the existing One/Many key pattern. The editor's error counter also gets `errorCountOne`/`errorCountMany` and `errorCountFirstOne`/`errorCountFirstMany`, so "1 problems" goes away.

## Risks / Trade-offs

- **Excel's acceptance of the file can't be checked in CI.**
  - Mitigation: use only probe-verified MiniExcel features and inject no XML.
  - During stack verification, open a template in Excel (es-CO locale) and LibreOffice: no repair prompt, `05001` stays text, re-save, re-import.
- **Codes the user retypes lose leading zeros.** The writer's string cells and the `@` format cover only the cells it filled.
  - Mitigation: the Guide line and the Choices sheet. The dashboard's outside-the-frame list remains the backstop.
- **Leaving out rows without a target can hide a forgotten target.**
  - Mitigation: the left-out rows are counted in the summary and listed as row ranges before Load, and `0` stays a kept value.
- **The `#` rule and the `target` column change how some files read.** A header starting with `#` becomes a note on import, and a second `target` header is a dimension.
  - Mitigation: the saved-frame download renames such dimensions (D3), so downloaded frames still import back. The preview lists ignored columns, and the docs state both rules. Only hand-made files are affected.
- **MiniExcel options apply across sheets.**
  - Mitigation: column options are set only for Frame's columns, and Guide and Choices column names contain spaces. The frozen, filtered header is accepted on all three sheets. A unit test with questions named `code` and `label` reads each sheet's XML.
- **Large lists or long labels make large files.**
  - Mitigation: label cells are cut, and the size fallback (D3) keeps templates importable or refuses them with a clear message.
  - A saved frame with very long hand-typed values can still download above the limit. Its Guide says so.
- **Cascades such as department × municipality produce impossible rows.**
  - Mitigation: choice subsets, empty targets left out, and the narrowing tip.
- **UI and C# combination logic drift.**
  - Mitigation: shared cases in both suites, with the API authoritative for the file.
- **`/form-choices` is large on big forms**, about 65 KB for 1,100 choices.
  - Mitigation: fetched once per form check, cached in `ProjectContext` next to `formFields` and cleared with them when `formCheckedAt` changes, and requested only by the picker and guide on the Sampling frame page.
- **Template and frame files fill the export directory.**
  - Mitigation: files are under 4.5 MB, links last 5 minutes, and the sweeper runs every minute. This is the same unbounded-but-small exposure the existing downloads accept (`docs/operations.md`).
- **Existing UI specs assume the editor opens by itself, the header Import button without a frame, and `'1 rows'` / `'1 problems'`.**
  - Mitigation: updated in the tasks.

## Migration Plan

No database migration. The preview fields are additive, and the new endpoints are additive. Deploy the API and UI together, as usual; an older UI ignores the new preview fields. Rollback is a plain redeploy of the previous images. Docs are updated in the same change: `docs/kobo.md` (Sampling frame section, including "opens the editor straight away"), `docs/performance-dashboard.md` (leading-zero tip, "Without a frame") and `README.md`.

## Verification

**Stack check (2026-10-15).** A separate Compose project (`tamizaverify`) ran the new API and UI images with a fake OIDC provider and a fake Kobo. The form had a 1,100-choice municipality list, `sexo` with or_other, an integer question and a `select_multiple`. Headless Chrome drove the UI:
- At 360, 1024 and 1280 px, the start panel, the picker (questions and choices views), the import dialog and the editor showed no horizontal page scroll. The picker's footer stayed in view while its list scrolled.
- The picker counted 1,100 × 2 × 3 = 6,600 rows and listed the `select_multiple` question disabled with its reason.
- A template for 3 municipalities × 2 sexes × 2 strata had 12 rows (43 KB) and came through the single-use link with one toast. Filled with 6 targets and imported, it gave no errors. The preview listed the 6 left-out rows as ranges and both label columns as notes, and preselected all three columns. The frame was saved with `05001` as text.
- The saved frame, downloaded and imported again unchanged, gave the same 6 rows and targets with every column preselected. Saved again, it was identical.
- **Create rows from questions** started with the editor's codes and typed values (3 × 1 × 2 = 6 rows).
- The browser console showed no errors.

**LibreOffice (2026-10-15).** The user opened the 12-row template in LibreOffice 26.2 and edited it with no repair prompt. They saved it and reopened it.
- `05001`, `05003` and `05005` stayed text: shared-string cells with the `@` format.
- The re-saved file kept the three sheets in order, the frozen header and the autofilter.
- Through `SamplingFrameImport.Read`, the file read with no errors: the dimensions `municipio`, `sexo` and `estrato`, both label columns listed as notes, and all 12 rows left out, since no target was filled in.

**Excel (es-CO).** Not tested: Excel is not installed on the machine that ran the checks, and the user chose to close the verification with LibreOffice only. The workbook uses only probe-verified MiniExcel features and injects no XML, so the risk "Excel's acceptance of the file can't be checked in CI" stays open for Excel.
