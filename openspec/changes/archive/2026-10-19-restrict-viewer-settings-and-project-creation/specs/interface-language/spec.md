# Spec Delta

## MODIFIED Requirements

### Requirement: Interface text in the interface language
Every text the UI writes SHALL be in the interface language. This includes labels, buttons, headings, help, empty states, confirmations, notices, accessible names, chart captions and summaries, and the script editor's own controls and messages. It also includes the messages the UI gives for API error codes it knows, and the requests it writes for the assistant. Text that other capabilities quote is its English wording; in Spanish the UI SHALL show its translation.

#### Scenario: Project sections in Spanish
- **WHEN** an `analyst` with the Spanish UI opens a project whose dashboards are "Indicators" and "Field"
- **THEN** the tab row reads Desempeño, Datos limpios, Indicators, Field and Configuración

#### Scenario: Known API error code
- **WHEN** an `admin` with the Spanish UI saves a Kobo API token that Kobo rejects
- **THEN** the message saying Kobo rejected the token is in Spanish

#### Scenario: Project creation refused in Spanish
- **WHEN** a user with the Spanish UI who is not `superadmin` has the project creation form open, their role in the only project where they are `admin` is changed to `analyst`, and they then send the form, which the API refuses with the code `project.create_not_allowed`
- **THEN** the form says in Spanish that only project admins and superadmins can create projects
- **AND** that message is not marked as English

#### Scenario: Script editor search
- **WHEN** an analyst with the Spanish UI opens the search panel of the script editor
- **THEN** its fields, buttons and messages are in Spanish

#### Scenario: Request for the assistant
- **WHEN** an analyst with the Spanish UI asks the assistant to fix a failed step
- **THEN** the request the UI writes for the analyst is in Spanish

#### Scenario: Name proposed for a duplicate
- **WHEN** an analyst with the Spanish UI duplicates the dashboard "Housing"
- **THEN** the name field reads "Housing (copia)"

### Requirement: Backend text shown as written
Text written by the API or the analytics worker SHALL be shown as received, in English, whatever the interface language. This includes field validation messages, error messages without a code the UI knows, run and test errors and logs, and sample-download errors. Such text SHALL be marked as English. Words the UI adds to introduce it SHALL be in the interface language.

#### Scenario: Field message in the Spanish UI
- **WHEN** a user with the Spanish UI who is `admin` of another project creates a project and the API rejects its name with "Enter a name of 1 to 200 characters."
- **THEN** that English message appears next to the name field, marked as English, and the rest of the form is in Spanish

#### Scenario: Step failure in the Spanish UI
- **WHEN** script 1 fails with `KeyError: 'edad'` and an `analyst` with the Spanish UI opens the run
- **THEN** the run says in Spanish that script 1 failed, followed by `KeyError: 'edad'` unchanged

#### Scenario: Error without a known code
- **WHEN** a member with the Spanish UI acts on a widget that was just deleted and the API answers "Widget not found."
- **THEN** the message reads "Widget not found."

### Requirement: Content shown as it was created
Names and labels created by members, by the Kobo form or by the backend SHALL be shown exactly as they are, in any interface language. This includes project, dashboard, indicator and widget names, Kobo question and choice labels, column names, and the default dashboard name. Files the backend produces SHALL NOT change with the interface language: the sampling-frame workbook, the sample kit, and the CSV and XLSX exports.

#### Scenario: Default dashboard
- **WHEN** a user with the Spanish UI who is `admin` of another project creates a project
- **THEN** its first dashboard is named "Indicators"

#### Scenario: Labels from the form
- **WHEN** the form's default language is Spanish and a member with the English UI opens the clean data
- **THEN** the question labels are shown in Spanish, as the form has them

#### Scenario: Export
- **WHEN** a member with the Spanish UI exports a table to CSV
- **THEN** the file is the same one a member with the English UI gets, with decimals written with a dot
