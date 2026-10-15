# Proposal

## Why

To set up a sampling frame today, the analyst has to work out the file layout from one sentence in the import dialog. The file needs one column per dimension, an exact `target` column and choice *codes* (not labels). The analyst then has to type every combination by hand, keep codes like `05001` from turning into `5001`, and fill or delete every row. For a frame of municipality × sex that can mean 2,200 rows. Mistakes show up only after the upload, or later as submissions "outside the frame" on the Performance dashboard. Real projects are about to load their frames, and the form already knows the questions and their choice codes. Tamiza should build the file for the user rather than ask them to reverse-engineer it.

## What Changes

**Starting a frame**
- **Start panel.** A project without a frame no longer opens an empty editor for analysts. They see a short explanation and three ways to start: **Choose questions**, **Import a file** and **Type it in**. A closed "How to organize a file" guide sits below them. Viewers still see the "no frame yet" sentence.
- **Question picker.** The analyst picks 1 to 10 form questions.
  - For a question with a choice list, all choices are used by default; the analyst can narrow them to the ones they sample.
  - For a question without a list, the analyst can type the values.
  - Multiple-choice and repeat-group questions are shown disabled, with the reason: coverage cannot count them.
  - A live counter shows the number of rows the choices make. It blocks above the 10,000-row limit.
  - The picker has two outputs: **Download Excel template**, or **Create the rows here**, which fills the editor directly and needs no Excel.

**The workbook**
- **Import template.** The API writes an XLSX workbook for the chosen questions.
  - The first sheet, **Frame**, has one column per question, named after the question. Every combination is pre-filled with its codes, stored as text so `05001` survives.
  - A `# <question> label` note column sits next to each code column, and the `target` column is left empty.
  - A **Guide** sheet holds the rules, and a **Choices** sheet lists the codes with their labels. Every file stays under the 5 MB import limit.
- **Download the saved frame.** Every member can download the saved frame in the same workbook layout, with its targets. Analysts can edit it in Excel and import it again.

**Import changes (preview only, as today)**
- Columns whose name starts with `#` are notes. They are ignored and listed.
- Data under a column with no name is listed instead of being dropped silently.
- A row whose `target` cell is empty is left out and listed by row number. `0` is still a kept target.
- Blank lines no longer count toward the 10,000-row read limit, so trailing rows are no longer dropped silently.
- Row numbers in the preview count blank CSV lines, so they match what the user sees in the file.

**Import dialog**
- It shows the shared file guide, open while the project has no frame, with a link to the template.
- It shows a busy state while reading the file, and the left-out rows and ignored columns.
- Each column is preselected to a question. The order of preference is: the saved frame's mapping, then the exact path, then a name only one question has, then a label only one question has.

**Editor and API**
- The editor gets **Create rows from questions** and **Remove rows without a target (N)**. **Cancel** is always available, and a failed load of the frame no longer opens an editor whose Save would replace a frame nobody could see.
- A new read-only `GET /form-choices` endpoint gives the UI the form's choice lists (code and label).
- No save rule, limit or stored shape of the sampling frame changes. Coverage matching is unchanged.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `projects`:
  - The requirement "Sampling frame import from CSV or XLSX" changes in four ways:
    - the preview also lists what was left out;
    - the invalid-target scenario is limited to non-empty targets;
    - blank lines never truncate a file within the limits;
    - row numbers count blank lines.

    Its wording also now names the first sheet and the first non-empty row, which is today's behavior.
  - New requirements:
    - note columns and rows without a target are left out of an import;
    - mapping preselection on import;
    - form choices readable by every member;
    - the import template and its request rules;
    - the saved-frame download;
    - the start panel and question picker;
    - the file guide.

## Impact

- **tamiza-api**:
  - Existing files to update:
    - `Projects/SamplingFrameImport.cs`, which gets the reading rules and additive preview fields `rowsWithoutTarget` and `ignoredColumns`;
    - `Projects/SpreadsheetReader.cs`, which gets an opt-in that keeps blank CSV lines;
    - `Projects/SamplingFrameEndpoints.cs`, which gets `POST /sampling-frame/template` (analyst) and `POST /sampling-frame/export` (viewer), served through the existing single-use `/data-exports/{token}` links;
    - `Projects/ProjectEndpoints.cs`, which gets `GET /form-choices` (viewer);
    - `CleanData/XlsxExport.cs`, whose cell-length cut becomes internal for reuse.
  - New files:
    - `Projects/SamplingFrameTemplate.cs` (request rules and rows);
    - `Projects/SamplingFrameWorkbook.cs` (a three-sheet MiniExcel writer).
  - Unit and integration tests, including a round trip: template → fill → import with no errors.
  - No database migration and no new dependency: MiniExcel 1.46.0 already writes multiple sheets, text cells and frozen headers.
- **tamiza-ui**:
  - Existing files to update:
    - `project-sampling-frame.ts` (start panel, Download, the editor actions);
    - `import-frame-dialog.ts` (guide, busy state, notices, preselection);
    - `sampling-frame-fields.ts` (header naming, combination count and rows, shared with the picker);
    - `core/projects/projects.service.ts` (`FormField.listName`/`orOther`, `FormChoice`, new methods);
    - `project-context.ts` (cached form choices);
    - `public/i18n/en.json` (`frame.*`);
    - the Vitest specs, including the spec that expects the editor to open on its own.
  - New files: `frame-questions-dialog.ts` (the picker) and `frame-file-guide.ts` (the shared guide).
- **Docs**: the Sampling frame section of `docs/kobo.md` (including "opens the editor straight away"), the leading-zero tip and "Without a frame" in `docs/performance-dashboard.md`, and the `README.md` status line.
- **Scope note**: `openspec/config.yaml` (taken from `docs/tamiza-brief.md` section 4) lists "Word/Excel/PowerPoint templates" as out of scope for the MVP. Neither file defines the item. This change reads it as templates for report deliverables:
  - the brief names Power BI, Excel and Word as the outputs of the manual flow Tamiza replaces (section 1);
  - section 4 lists the item next to "Power BI endpoint".

  The import template is an input file for the sampling frame, agreed as part of this change's scope, and it does not touch reports.
- **Out of scope, deferred**:
  - reading files the way spreadsheet programs sometimes save them: title rows above the column names, merged cells, Windows-1252 CSV;
  - "how to fix it" rewording of import errors;
  - loading a file with row errors into the editor;
  - hints that suggest codes for values that are not codes;
  - dropdowns or data validation in the workbook;
  - a CSV template;
  - cascading choice filters (`choice_filter`).
