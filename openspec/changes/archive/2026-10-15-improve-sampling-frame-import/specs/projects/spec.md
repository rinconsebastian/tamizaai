## MODIFIED Requirements

### Requirement: Sampling frame import from CSV or XLSX
An `admin` or `analyst` SHALL be able to import a sampling frame from a CSV or XLSX file (its first sheet) whose first non-empty row names the dimensions plus a `target` column, with one combination per row. Importing SHALL only produce a preview: the dimensions found, the targets, any errors and what was left out. Nothing SHALL change until the user maps each dimension to a form field and saves. Files over 5 MB or 10,000 data rows SHALL be rejected; blank lines SHALL NOT count as data rows.

#### Scenario: Valid file
- **WHEN** an analyst imports a CSV or XLSX file with columns `municipality`, `sex`, `age_range` and `target`
- **THEN** the preview lists the three dimensions and every row's target, and the saved frame is unchanged

#### Scenario: Missing target column
- **WHEN** the file has no `target` column
- **THEN** the preview reports that the `target` column is required

#### Scenario: Invalid row
- **WHEN** row 7 has a target that is not empty and is not a whole number of zero or more, such as `1.5` or `-3`
- **THEN** the preview reports the error for row 7

#### Scenario: File too large
- **WHEN** the file exceeds 5 MB or 10,000 data rows
- **THEN** the import is rejected with a message stating the limit

#### Scenario: Blank lines between rows
- **WHEN** a file has 10,000 data rows and five blank lines among them
- **THEN** the preview holds all 10,000 rows and none is dropped

#### Scenario: Row numbers after blank lines
- **WHEN** a CSV file has an empty line 3 and line 7 has the target `1.5`
- **THEN** the preview reports the error for row 7, the line number a spreadsheet program or text editor shows

## ADDED Requirements

### Requirement: Sampling frame import leaves out notes and rows without a target
When a sampling frame is imported, a column whose name starts with `#` SHALL be a note: it SHALL NOT become a dimension and SHALL be listed as ignored. Values under a column without a name SHALL be listed as ignored too. A row whose `target` cell is empty SHALL be left out of the preview without an error, SHALL NOT count as a repeated combination, and SHALL be listed by its row number. A target of `0` SHALL be kept.

#### Scenario: Note column
- **WHEN** a file has the columns `municipio`, `# municipio label`, `sexo` and `target`
- **THEN** the dimensions are `municipio` and `sexo`, and `# municipio label` is listed as an ignored note column

#### Scenario: Values under a column without a name
- **WHEN** column E has no name in the header row but holds values
- **THEN** column E is not a dimension and is listed as ignored because it has no name

#### Scenario: Rows without a target
- **WHEN** rows 4 and 9 have an empty `target` cell
- **THEN** neither row is in the preview, no error is reported for them, and rows 4 and 9 are listed as left out

#### Scenario: Target of zero
- **WHEN** row 5 has the target `0`
- **THEN** row 5 is in the preview with the target 0

#### Scenario: Left-out row repeats a combination
- **WHEN** row 4 has an empty target and row 12 has the same values with the target 30
- **THEN** row 12 is in the preview and no repeated combination is reported

#### Scenario: Every row without a target
- **WHEN** the file has a `target` column and no row has a target
- **THEN** the preview lists the dimensions, holds no rows, lists every row as left out and reports no error

### Requirement: Sampling frame import mapping preselection
After an import, each dimension SHALL be preselected to a form question, trying in order, ignoring case: the question the saved frame maps a dimension of the same name to, while the form has it; a question whose path is the column name; the only question with that name; the only question with that label. When nothing matches, or the saved frame's question differs from a path or name match, no question SHALL be preselected. The user SHALL be able to change every preselection.

#### Scenario: Re-imported frame keeps its mapping
- **WHEN** the saved frame maps the dimension `municipality` to `geo/municipio` and an analyst imports a file with a column `Municipality`
- **THEN** the column is preselected to `geo/municipio`

#### Scenario: Saved question no longer in the form
- **WHEN** the saved frame maps the dimension `municipality` to `geo/municipio`, the form no longer has that question, and the only question named `municipality` is `lugar/municipality`
- **THEN** the column `Municipality` is preselected to `lugar/municipality`

#### Scenario: Column named after a path
- **WHEN** a file has a column `hogar/sexo` and the form has a question with that path
- **THEN** that question is preselected

#### Scenario: Name shared by two questions
- **WHEN** a file has a column `edad`, the saved frame has no such dimension and two questions in different groups are named `edad`
- **THEN** no question is preselected for that column and the user chooses one

#### Scenario: Saved mapping disagrees with the form
- **WHEN** the saved frame maps the dimension `sexo` to `jefe/sexo_jefe` and an analyst imports a file with a column `sexo`, which is also the name of the top-level question `sexo`
- **THEN** no question is preselected for that column, and both `jefe/sexo_jefe` and `sexo` are named as candidates

### Requirement: Form choices
Every project member SHALL be able to read the choice lists of the project's current form snapshot. Each choice SHALL have its list name, its code and the label the snapshot stores for it, if any, in form order.

#### Scenario: Choices listed
- **WHEN** a viewer reads the form choices of a project whose form lists Medellín with the code `05001` in the list `municipalities`
- **THEN** the response includes the choice with list `municipalities`, code `05001` and label `Medellín`

#### Scenario: Not a member
- **WHEN** a user who is not a member reads a project's form choices
- **THEN** the API responds 404

### Requirement: Sampling frame import template
An `admin` or `analyst` SHALL be able to download an XLSX template for 1 to 10 chosen questions. Its first sheet SHALL have a column per question, named after it, then `target`. It SHALL have a row per combination of the chosen values, with codes as text, an empty `target` and a `#` label column after each question with choices. Further sheets SHALL explain the rules and list codes with labels. Once its planned rows have targets and values, it SHALL import with no errors.

#### Scenario: Template for chosen choices
- **WHEN** an analyst requests a template for `municipio` with the choices `05001` (Medellín) and `05088` (Bello), and for `sexo` with every choice (`f`, `m`)
- **THEN** the first sheet has the columns `municipio`, `# municipio label`, `sexo`, `# sexo label` and `target`, and four rows with every `target` empty. `05001` is stored as text and reads `05001`. The guide and choices sheets follow the first sheet

#### Scenario: Question without choices and typed values
- **WHEN** the request includes the integer question `estrato` with the values `1`, `2` and `3`
- **THEN** the template has an `estrato` column without a label column, and each combination of the other questions appears once per value

#### Scenario: Question without choices and no values
- **WHEN** the request includes the text question `barrio` without values, next to `sexo`
- **THEN** the `barrio` column is empty in every row and does not multiply the rows, and importing the file reports "Enter a value for every dimension." for each row with a target until `barrio` is filled in

#### Scenario: Template without rows
- **WHEN** the request includes only the text question `barrio` without values
- **THEN** the first sheet holds only its column names, and importing it gives the dimension `barrio`, no rows and no error

#### Scenario: Name shared by two questions
- **WHEN** a chosen question shares its name with another field of the form, a note or metadata field included
- **THEN** its column is named after the question's path

#### Scenario: Long choice list
- **WHEN** an analyst requests a template for 50 of the 40,000 choices of `vereda` and the full list would make the file larger than the import limit
- **THEN** the choices sheet lists those 50 codes, the guide says the form has more, and the file is under 5 MB

#### Scenario: Filled template imports
- **WHEN** an analyst fills the target of some rows of a downloaded template whose dimension columns all hold values, and imports it
- **THEN** the preview reports no errors, lists the rows without a target as left out and the label columns as ignored, and preselects every column to the question it was made from, unless the saved frame maps a dimension of that name to another question

#### Scenario: Viewer asks for a template
- **WHEN** a `viewer` requests a template
- **THEN** the API responds 403 and no file is prepared

### Requirement: Sampling frame template request rules
The system SHALL refuse a template request, and prepare no file, when it chooses no question or more than 10, the same question twice, a question the form lacks, a multiple-choice or repeat-group question, a top-level question named `target`, questions whose columns would be named alike ignoring case, an empty or unknown choice, a typed value over 500 characters, more than 10,000 rows, or a file over the import limit. The message SHALL name the problem.

#### Scenario: Too many combinations
- **WHEN** an analyst requests every choice of a municipality question with 1,100 choices, of `sexo` with 2 and of `rango_edad` with 5
- **THEN** the request is refused with a message stating 11,000 rows and the limit of 10,000

#### Scenario: Multiple-choice question
- **WHEN** the request includes a multiple-choice question
- **THEN** the request is refused with a message that coverage cannot count that question

#### Scenario: Question named target
- **WHEN** the request includes a top-level question named `Target`
- **THEN** the request is refused with a message that its name is reserved for the target column

#### Scenario: Unknown choice
- **WHEN** the request includes the value `zz` for `municipio`, whose list has no such code
- **THEN** the request is refused with a message naming `zz` and `municipio`

### Requirement: Sampling frame download
Every project member SHALL be able to download the saved sampling frame as an XLSX workbook in the template's layout, with the saved targets. Columns SHALL be named after the saved dimensions, except that a name starting with `#` or equal to `target` SHALL be written as its question's path. Within the import limits, importing that file again SHALL give back the saved rows and targets. Downloading SHALL be refused with a message when the project has no frame.

#### Scenario: Viewer downloads the frame
- **WHEN** a viewer downloads the sampling frame of a project that has one
- **THEN** they get a workbook whose first sheet holds the saved dimensions, values and targets

#### Scenario: No frame yet
- **WHEN** a member downloads the sampling frame of a project without one
- **THEN** the API refuses with a message that the project has no sampling frame yet, and no file is prepared

#### Scenario: Downloaded frame imports again
- **WHEN** an analyst imports, without changing it, a downloaded frame whose questions are all still in the form
- **THEN** the preview holds the saved rows and targets with no errors, and every column is preselected to its saved question

#### Scenario: Dimension named like a note or the target
- **WHEN** the saved frame has the dimensions `#zona`, mapped to `geo/zona`, and `Target`, mapped to `hogar/target`, and an analyst downloads it and imports it again
- **THEN** those columns are named `geo/zona` and `hogar/target`, the preview holds the saved rows and targets with no errors, and both columns are preselected to their questions

#### Scenario: Frame without rows
- **WHEN** a member downloads a frame saved with dimensions and no targets
- **THEN** the first sheet holds the column names only, and importing it gives the dimensions, no rows and no error

### Requirement: Sampling frame start panel
On a project without a sampling frame, the Sampling frame page SHALL show an `admin` or `analyst` a start panel, not an empty editor. It SHALL explain the frame and offer three ways to begin: choose questions, import a file or type it in, with the file guide closed below. A `viewer` SHALL see only that no frame exists yet. Cancelling the editor without a frame SHALL return to the panel. When the frame cannot be loaded, the page SHALL show the error with a retry and nothing that edits the frame.

#### Scenario: Analyst without a frame
- **WHEN** an analyst opens the Sampling frame page of a project without a frame
- **THEN** the start panel is shown with the file guide closed below it, and no editor is open

#### Scenario: Viewer without a frame
- **WHEN** a viewer opens the Sampling frame page of a project without a frame
- **THEN** the page says that no sampling frame has been defined yet and offers no action

#### Scenario: Typing the frame in
- **WHEN** an analyst chooses to type the frame in and then cancels the empty editor
- **THEN** the editor closes and the start panel is shown again

#### Scenario: Frame load fails
- **WHEN** an analyst opens the Sampling frame page and the frame cannot be loaded
- **THEN** the page shows the error and a Retry action, offers no start panel, editor, Import or Download, and shows the frame once Retry succeeds

### Requirement: Sampling frame question picker
The question picker SHALL list the form's questions, without metadata, and let an `admin` or `analyst` choose 1 to 10. A question with choices SHALL use every choice unless narrowed; one without SHALL accept typed values. Multiple-choice, repeat-group and top-level `target` questions SHALL be shown disabled with the reason. The picker SHALL show the row count, block above 10,000, and download the template or fill the editor, keeping targets only when the questions are unchanged.

#### Scenario: Row count
- **WHEN** an analyst chooses 5 municipalities, both choices of `sexo` and 3 age ranges
- **THEN** the picker shows 30 rows

#### Scenario: Too many rows
- **WHEN** the chosen values make 11,000 rows
- **THEN** the picker says that a sampling frame holds at most 10,000 rows, and neither the download nor the rows in the editor can be used

#### Scenario: Multiple-choice question
- **WHEN** the form has a multiple-choice question
- **THEN** the picker lists it disabled, with the reason that coverage cannot count it

#### Scenario: Select all with a search
- **WHEN** every choice of a 1,100-choice question is cleared, the user searches `Bello` and presses Select all
- **THEN** only the matching choices are ticked and the other choices stay cleared

#### Scenario: Rows created in the editor
- **WHEN** an analyst creates the rows in the editor from `municipio` and `sexo`
- **THEN** the editor holds one row per combination with an empty target. Its dimensions are named after the questions and mapped to them. Nothing is saved

#### Scenario: Existing targets kept
- **WHEN** an editor holding the row `05001`, `f` with the target 120 creates its rows again from the same questions with one more municipality
- **THEN** the row `05001`, `f` keeps the target 120 and the new rows have empty targets

#### Scenario: Question added
- **WHEN** an editor whose only dimension maps to `municipio` and holds the row `05001` with the target 120 creates its rows again from `municipio` and `sexo`
- **THEN** the page warns that the current targets will not be kept, and every new row has an empty target

#### Scenario: Dimension name kept
- **WHEN** an editor whose dimension `Municipality` maps to `municipio` creates its rows again from `municipio` with one more municipality
- **THEN** the dimension is still named `Municipality`

#### Scenario: Typed values kept
- **WHEN** an editor holding the integer question `estrato` with the values `1`, `2` and `3` creates its rows again with one more municipality
- **THEN** the picker starts with `1`, `2` and `3` as the values of `estrato`, and the rows that already existed keep their targets

#### Scenario: Values that are not choices
- **WHEN** an editor holding `5001` under `municipio` creates its rows again from questions
- **THEN** the confirmation before replacing says that 1 row has values that are not choices and will not be kept

#### Scenario: Rows without a target removed
- **WHEN** the editor holds 30 rows, 12 of them without a target
- **THEN** the editor offers to remove the 12 rows without a target, and after that 18 rows remain

### Requirement: Sampling frame file guide
The import dialog and the start panel SHALL show a guide to the file layout with an example table. It SHALL cover one column per dimension holding choice codes, an exact `target` column with whole numbers, one row per combination, `#` note columns, empty targets left out, codes kept as text, the accepted formats and the limits. In the import dialog the guide SHALL be open while the project has no saved frame, and SHALL link to the question picker.

#### Scenario: First import
- **WHEN** an analyst opens the import dialog on a project without a saved frame
- **THEN** the guide is open

#### Scenario: Import with a saved frame
- **WHEN** an analyst opens the import dialog on a project with a saved frame
- **THEN** the guide is closed and can be opened

#### Scenario: Template from the guide
- **WHEN** an analyst follows the guide's link to get a template
- **THEN** the import dialog closes and the question picker opens
