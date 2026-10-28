# Spec Delta

## MODIFIED Requirements

### Requirement: Content shown as it was created
Names and labels created by members, by the Kobo form or by the backend SHALL be shown exactly as they are, in any interface language. This includes project, dashboard, indicator and widget names, Kobo question and choice labels, column names, and the default dashboard name. Files the backend produces SHALL NOT change with the interface language: the sampling-frame workbook, the sample kit, and the CSV and XLSX exports. The one exception is a scheduled email's workbook. Its own words and formats follow the language and regional format stored with the email's settings (see `dashboard-reports`), while names, labels and data values in it stay as they were created.

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
