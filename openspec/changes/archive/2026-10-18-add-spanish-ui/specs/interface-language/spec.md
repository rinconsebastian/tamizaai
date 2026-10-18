# Spec Delta

## Purpose

Decides which language and regional formats the web interface uses, which text the interface translates, and which text it shows exactly as the backend, the Kobo form or the user wrote it.

## ADDED Requirements

### Requirement: Interface language from the browser
The UI SHALL be shown in English or Spanish, chosen when the page loads from the browser's preferred languages in their order: the first entry that is Spanish or English, of any region, SHALL decide. When no entry is Spanish or English, the UI SHALL be in English. The UI SHALL offer no control to change the language and SHALL store no choice; a change in the browser's settings SHALL apply on the next load.

#### Scenario: Spanish browser
- **WHEN** a member whose browser prefers `es-CO`, then `es`, opens Tamiza
- **THEN** the UI is in Spanish

#### Scenario: Spanish after an unsupported language
- **WHEN** a member whose browser prefers `fr-FR`, then `es-ES`, then `en` opens Tamiza
- **THEN** the UI is in Spanish

#### Scenario: English before Spanish
- **WHEN** a member whose browser prefers `en-US`, then `es-CO`, opens Tamiza
- **THEN** the UI is in English

#### Scenario: No supported language
- **WHEN** a member whose browser prefers `de-DE`, then `pt-BR`, opens Tamiza
- **THEN** the UI is in English

#### Scenario: Browser setting changed
- **WHEN** a member using the Spanish UI moves English above Spanish in the browser's languages and reloads the page
- **THEN** the UI is in English, and nothing in Tamiza had to be changed

### Requirement: Page language declared
The page SHALL declare the language it is shown in. The sign-in request SHALL ask the identity provider for the same language, which the identity provider uses when it offers it.

#### Scenario: Spanish page
- **WHEN** the UI is in Spanish
- **THEN** the page declares Spanish as its language

#### Scenario: Sign-in in Spanish
- **WHEN** a member whose UI is in Spanish is sent to sign in
- **THEN** the sign-in request asks for Spanish

### Requirement: Interface text in the interface language
Every text the UI writes SHALL be in the interface language. This includes labels, buttons, headings, help, empty states, confirmations, notices, accessible names, chart captions and summaries, and the script editor's own controls and messages. It also includes the messages the UI gives for API error codes it knows, and the requests it writes for the assistant. Text that other capabilities quote is its English wording; in Spanish the UI SHALL show its translation.

#### Scenario: Project sections in Spanish
- **WHEN** a member with the Spanish UI opens a project whose dashboards are "Indicators" and "Field"
- **THEN** the tab row reads Desempeño, Datos limpios, Indicators, Field and Configuración

#### Scenario: Known API error code
- **WHEN** a member with the Spanish UI saves a Kobo API token that Kobo rejects
- **THEN** the message saying Kobo rejected the token is in Spanish

#### Scenario: Script editor search
- **WHEN** an analyst with the Spanish UI opens the search panel of the script editor
- **THEN** its fields, buttons and messages are in Spanish

#### Scenario: Request for the assistant
- **WHEN** an analyst with the Spanish UI asks the assistant to fix a failed step
- **THEN** the request the UI writes for the analyst is in Spanish

#### Scenario: Name proposed for a duplicate
- **WHEN** an analyst with the Spanish UI duplicates the dashboard "Housing"
- **THEN** the name field reads "Housing (copia)"

### Requirement: Counts in the interface language
A message that includes a count SHALL use the singular or plural form that the interface language requires for that count.

#### Scenario: One row
- **WHEN** an export of 1 row is ready
- **THEN** the message speaks of 1 row in the singular, in English and in Spanish

#### Scenario: Schedule of one hour
- **WHEN** a project runs its scripts every 1 hour
- **THEN** the schedule reads "every 1 hour" in English and uses the singular "hora" in Spanish

### Requirement: Backend text shown as written
Text written by the API or the analytics worker SHALL be shown as received, in English, whatever the interface language. This includes field validation messages, error messages without a code the UI knows, run and test errors and logs, and sample-download errors. Such text SHALL be marked as English. Words the UI adds to introduce it SHALL be in the interface language.

#### Scenario: Field message in the Spanish UI
- **WHEN** a member with the Spanish UI creates a project and the API rejects its name with "Enter a name of 1 to 200 characters."
- **THEN** that English message appears next to the name field, marked as English, and the rest of the form is in Spanish

#### Scenario: Step failure in the Spanish UI
- **WHEN** script 1 fails with `KeyError: 'edad'` and a member with the Spanish UI opens the run
- **THEN** the run says in Spanish that script 1 failed, followed by `KeyError: 'edad'` unchanged

#### Scenario: Error without a known code
- **WHEN** a member with the Spanish UI acts on a widget that was just deleted and the API answers "Widget not found."
- **THEN** the message reads "Widget not found."

### Requirement: Content shown as it was created
Names and labels created by members, by the Kobo form or by the backend SHALL be shown exactly as they are, in any interface language. This includes project, dashboard, indicator and widget names, Kobo question and choice labels, column names, and the default dashboard name. Files the backend produces SHALL NOT change with the interface language: the sampling-frame workbook, the sample kit, and the CSV and XLSX exports.

#### Scenario: Default dashboard
- **WHEN** a member with the Spanish UI creates a project
- **THEN** its first dashboard is named "Indicators"

#### Scenario: Labels from the form
- **WHEN** the form's default language is Spanish and a member with the English UI opens the clean data
- **THEN** the question labels are shown in Spanish, as the form has them

#### Scenario: Export
- **WHEN** a member with the Spanish UI exports a table to CSV
- **THEN** the file is the same one a member with the English UI gets, with decimals written with a dot

### Requirement: Regional formats
Numbers, percentages, dates, times, durations and file sizes SHALL follow the language and region of the browser entry that chose the interface language. When English is used because no entry is Spanish or English, they SHALL follow American English. Formats quoted in other capabilities are those an American English browser shows.

#### Scenario: Colombia
- **WHEN** the browser entry that chose Spanish is `es-CO` and a total of 12345.5 is shown
- **THEN** it reads `12.345,5`

#### Scenario: Mexico
- **WHEN** the browser entry that chose Spanish is `es-MX` and a total of 12345.5 is shown
- **THEN** it reads `12,345.5`

#### Scenario: Duration in Spanish
- **WHEN** the browser entry that chose Spanish is `es-CO` and a step took 8.2 seconds
- **THEN** its duration reads `8,2 s`

#### Scenario: English as a fallback
- **WHEN** the browser prefers `de-DE` only and a total of 12345.5 is shown
- **THEN** the UI is in English and the total reads `12,345.5`
