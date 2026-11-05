# Spec Delta

## MODIFIED Requirements

### Requirement: Content shown as it was created
Names and labels created by members, by the Kobo form or by the backend SHALL be shown exactly as they are, in any interface language. This includes project, dashboard, indicator and widget names, Kobo question and choice labels, column names, and the default dashboard name. Files the backend produces SHALL NOT change with the interface language: the sampling-frame workbook, the sample kit, and the CSV and XLSX exports of a table. There are two exceptions. A scheduled email's workbook has its own words and formats follow the language and regional format stored with the email's settings (see `dashboard-reports`). A widget's workbook has its own words and formats follow the interface language and regional format of the member who exports it (see `widget-export`). In both, the numbers written as text follow the project's regional format instead when the project has one (see `projects` "Project regional format"), and names, labels and data values stay as they were created. A widget's images, which the browser makes, write their own words in the interface language and their numbers as the screen shows them.

#### Scenario: Default dashboard
- **WHEN** a user with the Spanish UI who is `admin` of another project creates a project
- **THEN** its first dashboard is named "Indicators"

#### Scenario: Labels from the form
- **WHEN** the form's default language is Spanish and a member with the English UI opens the clean data
- **THEN** the question labels are shown in Spanish, as the form has them

#### Scenario: Export
- **WHEN** a member with the Spanish UI exports a table to CSV
- **THEN** the file is the same one a member with the English UI gets, with decimals written with a dot

#### Scenario: Scheduled email workbook
- **WHEN** a member whose UI is in Spanish with `es-CO` saves the settings of the "Housing" email
- **THEN** the workbook's first sheet is "Resumen", and its widget sheets keep the widget titles as written

#### Scenario: Widget workbook
- **WHEN** a member whose UI is in Spanish with `es-CO` exports the workbook of the widget "Coverage by municipality"
- **THEN** its sheets are "Coverage by municipality", as written, and "Resumen", and a member with the English UI gets "Summary" in its place

#### Scenario: Table export of a project with a regional format
- **WHEN** the project's regional format is `es-CO` and a member exports the table of the indicator `teatro_promedios` to CSV
- **THEN** the file writes its decimals with a dot, as it does for a project with no regional format

### Requirement: Regional formats
Numbers, percentages, dates, times, durations and file sizes SHALL follow the language and region of the browser entry that chose the interface language. When English is used because no entry is Spanish or English, they SHALL follow American English. The one exception is the numbers of a project's dashboards when the project has a regional format (see `projects` "Project regional format" and `dashboard-indicators` "Regional format of a project's dashboards"): they SHALL follow the project's format for every member, while dates, times and the interface's own words keep the browser's. Formats quoted in other capabilities are those an American English browser shows.

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

#### Scenario: Project regional format in an English browser
- **WHEN** the browser entry that chose English is `en-US`, the project's regional format is `es-CO`, and a widget over a `decimal` indicator shows a total of 12345.5 next to a run step that took 8.2 seconds
- **THEN** the UI is in English, the widget's total reads `12.345,50` and the step's duration reads `8.2 s`
