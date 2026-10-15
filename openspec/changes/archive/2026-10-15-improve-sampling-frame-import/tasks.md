# Tasks

## 1. Import reading rules (api)

- [x] 1.1 Change `SamplingFrameImport.Read` (design D4, D5, D6):
  - after trimming, headers starting with `#` become `ignoredColumns` entries with reason `note`;
  - non-empty cells under an empty header, or beyond the header width, become one `unnamed` entry per column;
  - rows whose `target` cell is empty are left out of validation and duplicate detection, and listed in `rowsWithoutTarget`. `0` is kept, and with no target column the header error stands;
  - the `MaxRows + 2` raw-row cut is replaced by streaming non-blank rows, with the file error once they pass 10,000, without setting `IgnoreEmptyRows`;
  - CSV blank lines are kept as blank rows through `SpreadsheetReader.Rows(…, keepBlankLines: true)`, so row numbers match the file's lines;
  - `ImportPreview` gains the two fields.

  Verify with a new `tests/Tamiza.Api.UnitTests/Projects/SamplingFrameImportTests.cs` for the scenarios `Note column`, `Values under a column without a name`, `Rows without a target`, `Target of zero`, `Left-out row repeats a combination`, `Every row without a target`, `Blank lines between rows` (XLSX gaps and `;;;` CSV lines) and `Row numbers after blank lines`. The last case covers CSV with an empty line, a whitespace-only line and a quoted cell that spans lines, plus XLSX with a sheet gap; for each, assert the row numbers of an error row and of a left-out row that come after the blank lines. Also verify 10,001 non-blank rows are refused. Then verify that `SamplingFrameApiTests` and `ReferenceTablesApiTests` pass unchanged, and add one integration case that posts an XLSX with a `#` column and empty targets.
- [x] 1.2 Update the file rules in the Sampling frame section of `docs/kobo.md`: `#` note columns, empty target = row left out while `0` is kept, unnamed columns listed, blank lines ignored. Verify the example table still matches the rules and the links resolve.

## 2. Form choices, template and frame download (api)

- [x] 2.1 In `Projects/ProjectEndpoints.cs`, add `GET /api/v1/projects/{projectId:guid}/form-choices` (`GetFormChoices`, viewer) returning `Project.FormChoices` in snapshot order (design D1). Make `TestData` able to seed `form_choices` and fields with `listName`, as `PerformanceHarness` does. Verify integration tests for `Choices listed` and `Not a member`, and that the OpenAPI document lists the operation.
- [x] 2.2 Add `Projects/SamplingFrameTemplate.cs` with the request records, the validation of design D7 and the rows:
  - the "has choices" rule (`select_one` prefix, not `select_one_from_file` prefix, `ListName` with at least one choice);
  - the choice list: codes deduplicated ignoring case (first in form order wins), and `other` labelled `Other` added on or_other unless the list has an `other` code. `null` means the list's own choices and `[]` is refused; codes are matched exactly, then ignoring case, with the matched code written;
  - typed values trimmed, blank and duplicate (ignoring case) values dropped keeping the first, more than 500 characters refused;
  - column names: the name, or the xpath when any other snapshot field (notes and metadata included) has the same name, or when the name equals `target`, all ignoring case. Names colliding ignoring case are refused, and so is a top-level `target` in any letter case;
  - the row count (0 with no non-empty list, otherwise the product, saturating) and the cartesian product in request order.

  Verify unit tests for every refusal message: `Too many combinations` with 11,000, `Multiple-choice question`, `Question named target`, `Unknown choice`, the empty selection, the repeat question, the duplicate field, the case-only collision (`G/edad` and `g/edad`), the typed value over 500 characters and more than 10. Also verify the cases kept in a table that task 3.2 repeats (headers, counts, first rows):
  - a grouped `G/Target` question gets the header `G/Target`;
  - a `note` in another group sharing the chosen question's name gives the xpath header;
  - a list with `A` and `a`: `null` gives one row with `A`, and `["a"]` writes `A`;
  - an or_other question whose list has no `other`, with `null` and with an explicit list that includes `other`;
  - an or_other question whose list already has an `other` code;
  - `"1\n 1\n\n2\n"` gives `["1","2"]`;
  - every chosen list empty gives 0 rows.
- [x] 2.3 Add `Projects/SamplingFrameWorkbook.cs` writing `Frame`, `Guide` and `Choices` in one MiniExcel `SaveAs` for a template and for a saved frame (design D3), and make `XlsxExport.CutToCell` internal for reuse. Verify unit tests that read the file back with MiniExcel and inspect the sheet XML:
  - sheet order;
  - `05001` as a string cell, `#` label columns, every `target` blank in a template and numeric in a saved frame;
  - `in this file` on Choices;
  - the Guide numbers taken from the validator constants, and the empty-column line only when the file has an empty column;
  - widths and the `@` format only on Frame's columns, with no column option on Guide or Choices, and the frozen, filtered header row on every sheet;
  - a template for questions named `code`, `label` and `question` writes without error;
  - a header-only `Frame` (header row present, no data rows) for a template whose only question has no values, and for a saved frame with 0 targets;
  - a saved frame with the dimensions `#zona` and `Target` written with their xpaths as headers, and the Guide naming the renamed columns;
  - Frame label cells cut to 200 characters ending in "…", and a 40,000-character label cut to 32,767 on Choices;
  - the size fallback: a template for 50 of 40,000 choices lists only the chosen codes on Choices and says so in the Guide; a maximal 10,000-row, 10-question template with 1,000-character labels stays under 4.5 MB; a request still over that after both fallbacks is refused.
- [x] 2.4 In `Projects/SamplingFrameEndpoints.cs`, add `POST /sampling-frame/template` (`DownloadSamplingFrameTemplate`, analyst) and `POST /sampling-frame/export` (`DownloadSamplingFrame`, viewer, `409 samplingFrame.none` with no frame). Both register the file with `ExportRegistry` and return `SamplingFrameFile`, with no export slot and the delete-on-failure `finally` (design D2). Verify integration tests:
  - `Template for chosen choices`, `Question without choices and typed values`, `Question without choices and no values` (filling only targets reports "Enter a value for every dimension." only on rows with a target; filling the column too imports with no errors), `Template without rows` (`RowCount` 0), `Name shared by two questions`, `Long choice list`;
  - `Filled template imports`: POST, GET the link once (a second GET gives 404), fill some targets with MiniExcel, POST the import; no errors, rows without a target and label columns listed;
  - `Viewer asks for a template` (403, `ExportRegistry.Pending` unchanged);
  - `Viewer downloads the frame`, `No frame yet`, `Downloaded frame imports again` (same rows and targets, with a form whose labels are longer than 200 characters), `Dimension named like a note or the target`, `Frame without rows`;
  - a 400, and the 409 of `No frame yet`, leave no file in the export directory and `ExportRegistry.Pending` unchanged;
  - a user who is not a member gets 404 from both POST endpoints and no link is issued;
  - the OpenAPI document lists both operations.

## 3. Data layer and shared helpers (ui)

- [x] 3.1 In `core/projects/projects.service.ts`:
  - add `listName` and `orOther` to `FormField`;
  - add the types `FormChoice`, `FrameTemplateRequest` and `SamplingFrameFile`, and `rowsWithoutTarget` and `ignoredColumns` to `ImportPreview`;
  - add the methods `formChoices`, `samplingFrameTemplate` and `exportSamplingFrame`.

  Add `formChoices` and `loadFormChoices()` to `ProjectContext`, with an error state the picker can retry. Cache the choices like `formFields`: `update()` resets `formChoices` and its error state in the same branch that resets `formFields` when `formCheckedAt` changes. Add `errors.codes.samplingFrame.none` to `en.json`. Verify a spec that:
  - choices are fetched once per form check;
  - after an `update()` with a new `formCheckedAt`, the next `loadFormChoices()` fetches again, and with the same `formCheckedAt` it does not;
  - a failed load can be retried;
  - the error-code test covers the new code.
- [x] 3.2 In `sampling-frame-fields.ts`, add `templateIssue`, `hasChoices`, `choiceList`, `templateHeaders(snapshot, xpaths)` (where `snapshot` is the unfiltered `formFields()`), `normalizeValues`, `combinationCount` and `combinations`. Change `guessField(fields, header, saved)` to design D12: saved mapping while the field exists, xpath, unique name, unique label, all ignoring case, and no preselection on a conflict. Verify unit specs:
  - the same cases as task 2.2, including the note clash;
  - for each case, `guessField` on every header `templateHeaders` gives returns the chosen question's xpath;
  - for a downloaded frame's headers with `savedDimensions` set, every header returns its saved field;
  - the scenarios `Re-imported frame keeps its mapping`, `Saved question no longer in the form`, `Column named after a path`, `Name shared by two questions` and `Saved mapping disagrees with the form`.

## 4. File guide and import dialog (ui)

- [x] 4.1 Add `frame-file-guide.ts` (design D11): the rules and an example table built from the form's first question with choices, or the fixed example while choices are not loaded. Add the `frame.guide.*` keys. Verify a spec for both example sources, and that the rules state the limits from one place.
- [x] 4.2 Rework `import-frame-dialog.ts` (design D11, D12):
  - the `data-filter-dialog` layout;
  - the guide in a `tmz-disclosure`, open when the project has no saved frame;
  - the template link, which closes the dialog with `{kind: 'template'}`;
  - the file input as a busy button, with stale-response protection;
  - pluralized `frame.preview.*` summaries;
  - a notice for rows without a target and ignored columns: the counts in the live region, and the rows as at most 20 ranges, then "and N more rows";
  - preselection with the page's saved dimensions, with the conflict hint under the select.

  Verify specs in `project-sampling-frame.spec.ts`:
  - `First import`, `Import with a saved frame` and `Template from the guide`;
  - the busy state, and that a second file drops the first file's late response;
  - 2,170 left-out rows give the count sentence, 20 ranges and "and N more rows", with only the counts inside `aria-live`;
  - the ignored-column text and the conflict hint;
  - the existing import specs still pass.

## 5. Question picker and page (ui)

- [x] 5.1 Add `frame-questions-dialog.ts` (design D8, D9):
  - the `data-filter-dialog` layout with a new sticky footer, full screen on phones and a panel from `md` up;
  - it receives the unfiltered snapshot for `templateHeaders`, and filters metadata only for the list it shows;
  - the three question groups, a search box and the `N of 10` counter;
  - multiple-choice, repeat and top-level `target` questions disabled with their reason, and the 10-question cap;
  - the choices view: search, Select all and Clear scoped to the matching choices, the "N of M chosen" count, every choice ticked and the added `other` unticked, and the narrowing tip;
  - typed values cleaned with `normalizeValues`, with an inline error over 500 characters;
  - the live count, announced politely, with the block above 10,000;
  - the busy **Download Excel template**, ending in `Downloader.start` and one toast;
  - **Create the rows here**;
  - a skeleton while loading and a retry after a failed load;
  - the `frame.picker.*` and `frame.template.*` keys.

  Verify a spec with a `Downloader` stub:
  - `Row count`, `Too many rows`, `Select all with a search` and the cap of 10;
  - `Multiple-choice question`, a question inside a repeat group and a top-level `target` question, each listed disabled with its reason;
  - one `Downloader.start` and one toast per download, and the busy state;
  - a refused request shows the API message.
- [x] 5.2 Change `project-sampling-frame.ts` (design D9, D10):
  - the start panel for analysts without a frame (heading, one sentence, **Choose questions**, **Import a file**, **Type it in**, and the file guide in a closed `tmz-disclosure` below them), replacing `editing.set(!frame && canEdit())`; viewers keep the sentence;
  - a failed frame load showing the error with **Retry**, with no start panel, editor, Import or Download, replacing `editing.set(this.canEdit())` in the error branch;
  - **Type it in**, and **Cancel** returning to the panel: `cancel-edit` is shown whenever the editor is open, and `cancel()` closes the editor;
  - **Download** in the read view for every member, busy while the request runs and ending in one success or one error toast;
  - **Create rows from questions** in the editor, per design D9:
    - it pre-ticks the questions and codes already there and fills typed values from the rows;
    - it asks before replacing rows and names what will not be kept;
    - it keeps the existing dimension names, and carries targets over only when the questions are unchanged;
  - **Remove rows without a target (N)**;
  - opening the picker from the import dialog's `{kind: 'template'}`;
  - the `frame.start.*`, `frame.download.*`, `frame.editor.*` and `frame.loadFailed` keys, the One/Many error-count keys, and the rewritten `importIntro` and `dimensionsIntro`.

  Update the specs that expect the editor to open by itself or the header Import button without a frame, and the `'1 rows'` and `'1 problems'` texts. Verify specs for:
  - `Analyst without a frame` (the guide disclosure present and closed), `Viewer without a frame`, `Typing the frame in` and `Frame load fails`;
  - `Rows created in the editor`, `Existing targets kept`, `Question added`, `Dimension name kept`, `Typed values kept`, `Values that are not choices` and `Rows without a target removed`;
  - a viewer's Download: busy and disabled while the request runs, then one `Downloader.start` and one success toast; a refused request shows one error toast with the API message and no `Downloader.start`;
  - the discard guard still asks before an import or a picker replaces unsaved rows.
- [x] 5.3 Update the docs:
  - the rest of the Sampling frame section of `docs/kobo.md`: start panel, question picker, template sheets with their column names, Download, and "opens the editor straight away" removed;
  - `docs/performance-dashboard.md`: the leading-zero tip pointing to the template, and "Without a frame";
  - the status line in `README.md`.

  Verify the steps match the UI text and the links resolve.

## 6. Integration checks

- [x] 6.1 Run the full suites: `dotnet test` in `tamiza-api/`, and `npm test -- --watch=false` in `tamiza-ui/`, where only the two known polling failures are allowed. Run `openspec validate improve-sampling-frame-import --strict`. Verify that everything passes.
- [x] 6.2 In a separate Compose project (`-p tamizaverify`, fake OIDC and fake Kobo with a form that has a 1,100-choice municipality list, `sexo` with or_other, an integer question and a `select_multiple`):
  - check the start panel, picker, import dialog and editor at 360, 1024 and 1280 px in headless Chrome: no horizontal page scroll, sticky picker footer;
  - download a template and import it back filled in;
  - download the saved frame and import it again with no changes.

  Then ask the user to open one template in Excel (es-CO) and LibreOffice: no repair prompt, `05001` stays text after re-saving, and the re-saved file imports. Record the results in `design.md`.

## Workflow follow-up

- Archive the change after review, then check `openspec/specs/projects/spec.md` with `openspec validate --specs`.
