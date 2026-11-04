# Spec Delta

## MODIFIED Requirements

### Requirement: Email header
The email body SHALL open with a header. It SHALL give the project, the dashboard and the time the email was generated, with the project's time zone. It SHALL give the latest time one of the widgets' indicators was published, and the filters in force. It SHALL give the dashboard's source when the dashboard has one.

#### Scenario: Header
- **WHEN** the "Housing" email of "Census 2026" is generated at 07:00 in `America/Bogota`, and its indicators were last published at 05:12
- **THEN** its header names "Census 2026" and "Housing", gives 07:00 with the time zone, gives 05:12 as the data's publication time, and lists the filters in force

#### Scenario: Source in the header
- **WHEN** the English email of "Teatro", whose source is "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas", is generated
- **THEN** its header gives "Source" with that text, in the HTML body, in the plain text and in the workbook's "Summary" sheet

#### Scenario: Dashboard without a source
- **WHEN** the email of "Escuelas", which has no source, is generated
- **THEN** its header has no source line

### Requirement: Missing values in widget tables
A missing group value SHALL read as the interface's text for no group, such as "(none)". A missing value SHALL read as "—" in the body and be an empty cell in the workbook. A widget whose indicator the project no longer lists SHALL use the `decimal` format with no unit, and the indicator's name as its label, as the dashboard does; a widget's own unit and own caption text still take their place there (see `dashboard-indicators` "Widget unit" and "Widget caption text").

#### Scenario: Rows without a group value
- **WHEN** a bar chart of "Housing" has rows whose `municipality` is empty, in an English email
- **THEN** their bar's row reads "(none)"

#### Scenario: Removed indicator
- **WHEN** a widget's indicator script was removed but its table is still published
- **THEN** the widget's values have two decimals and no unit, and its caption uses the indicator's name

#### Scenario: Removed indicator with a unit of its own
- **WHEN** the script of `teatro_precio_hogar` was removed but its table is still published, and a box plot over it has the unit "pesos por boleta", in an English email
- **THEN** its median reads "40,000.00 pesos por boleta"

## ADDED Requirements

### Requirement: Widget context in a scheduled email
In the HTML body and its plain text, a widget's subtitle SHALL follow its title, its caption line SHALL use its own caption text, or be left out when the widget hides it, and its values and its value column's header SHALL use its own unit. The workbook's widget sheets SHALL use the same headers, and its "Summary" sheet SHALL list each widget's subtitle and footnote. A widget without these settings SHALL be sent as before.

#### Scenario: Subtitle under the title
- **WHEN** the "Teatro" email holds the bar chart "2.4. En qué lugares" with the subtitle "¿En qué lugares ha visto teatro en el último año? (E4)"
- **THEN** its block shows the subtitle under the title, in the HTML body and in the plain text

#### Scenario: Caption hidden on a stat tile
- **WHEN** the stat tile "Fuera de Duitama" hides its caption
- **THEN** its cell shows its title and its value, with no caption line

#### Scenario: Own caption text
- **WHEN** a bar chart's caption text is "% de quienes asistieron"
- **THEN** its block's caption line and its value column's header read "% de quienes asistieron"

#### Scenario: Own unit
- **WHEN** the column chart "2.2" over `teatro_hoy` has the unit "de quienes asistieron", in an English email
- **THEN** its values read like "11.21% de quienes asistieron", and its workbook sheet's value header names "de quienes asistieron"

#### Scenario: Texts in the summary sheet
- **WHEN** that column chart has a subtitle and a footnote
- **THEN** the workbook's "Summary" sheet lists both, each with the widget's title

#### Scenario: Email of widgets without these settings
- **WHEN** the email of "Escuelas", none of whose widgets has a subtitle, a footnote, a caption setting or a unit, is sent
- **THEN** its body, plain text and workbook have the same captions, values, headers and notes as before these settings existed

### Requirement: Widget footnote in a scheduled email
In the HTML body and its plain text, a widget's footnote SHALL follow its table, its value or the reason it is not shown, after the lines that say how many rows or columns the body leaves out, and apart from the widget's notes.

#### Scenario: Footnote after the table
- **WHEN** the bar chart "2.4. En qué lugares" of "Teatro" has the footnote "No suman 100 %: respuesta múltiple." and 12 bars
- **THEN** its block shows its first 10 rows, the line saying it has 2 more rows, and then the footnote

#### Scenario: Footnote of a stat tile
- **WHEN** the stat tile "Fuera de Duitama" has the footnote "Sin dato aparte (E5)."
- **THEN** its cell shows the footnote under its value and caption

#### Scenario: Footnote of a widget not shown
- **WHEN** a widget with a footnote cannot be read in time
- **THEN** its block shows its title, the note that it could not be read in time, and then its footnote
