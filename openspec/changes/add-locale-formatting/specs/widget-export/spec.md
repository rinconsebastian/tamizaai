# Spec Delta

## MODIFIED Requirements

### Requirement: Numbers in a widget workbook
The data sheet's numbers SHALL be numbers in the formats of a scheduled email's workbook (see `dashboard-reports` "Numbers in the workbook"), with the decimals of the widget the member sees, and a stat tile's value SHALL follow the indicator's format and those decimals, in full even when the tile abbreviates it. Text SHALL never be written as a formula, and a cell longer than 32,767 characters SHALL be cut to that length.

#### Scenario: Percentage cell
- **WHEN** a column chart over `piped_water_rate`, whose format is `percent`, has the value 0.42 for `Cali`
- **THEN** its cell in the workbook holds the number 0.42, shown as a percentage

#### Scenario: Median of a count
- **WHEN** a box plot over `household_size`, whose format is `count`, has a box whose median is 4.5
- **THEN** that box's median cell holds the number 4.5, shown with two decimals

#### Scenario: Text that looks like a formula
- **WHEN** a group value is `=1+1`
- **THEN** its cell holds the text `=1+1`, not a formula

#### Scenario: Long text
- **WHEN** a tooltip value of a map has 40,000 characters
- **THEN** its cell holds its first 32,767 characters

#### Scenario: Widget decimals in its workbook
- **WHEN** a member exports the workbook of the box plot "2.6. Lo que pagó por la última boleta: mediana y dispersión (E7, pesos)", which sets 0 decimals, with a median of 40000
- **THEN** that median cell holds the number 40000, shown as a whole number

#### Scenario: Abbreviated tile in its workbook
- **WHEN** a member exports the workbook of a stat tile over `comercio_gastos_media` that reads `1 M pesos` on screen
- **THEN** its cell holds the number 1016279, shown in full as a whole number

### Requirement: Language of widget files
An image's own words, such as its notes and footer, SHALL be in the interface language, and its numbers SHALL read as on screen, in the project's regional format when the project has one. A workbook's own words, such as "Summary", "Total", "(none)", "Other" and its summary items, and its dates, SHALL follow the member's interface language and regional format. Its range texts and the other numbers it writes as text SHALL follow the project's regional format when the project has one, else the member's. Names, labels and data values SHALL stay as they were created.

#### Scenario: Spanish member
- **WHEN** a member whose UI is in Spanish with `es-CO` exports the workbook of a stacked column chart titled "Coverage by municipality"
- **THEN** its sheets are "Coverage by municipality" and "Resumen", and its total column reads "Total"

#### Scenario: Spanish image
- **WHEN** that member exports the chart as a PNG image
- **THEN** its footer is in Spanish, and its values read as on the member's screen, such as `12.345,5`

#### Scenario: Image in the project's format
- **WHEN** the Duitama project's regional format is `es-CO` and a member whose UI is in English with `en-US` exports as a PNG image the stat tile over `teatro_hoy` with the value 0.4685
- **THEN** the image's footer is in English, and its value reads `46,85 % de los hogares`, as on that member's screen

#### Scenario: Workbook ranges in the project's format
- **WHEN** the project's regional format is `es-CO` and a member whose UI is in English with `en-US` exports the workbook of a histogram over a `count` indicator with a range from 10000 to 15000
- **THEN** its second sheet is "Summary", in English, and that range reads `10.000–15.000`

#### Scenario: Rows in the Summary
- **WHEN** the project's regional format is `es-CO` and a member whose UI is in English with `en-US` exports the workbook of a widget that lists 12,345 rows
- **THEN** the "Summary" line for the number of rows reads `12.345`, and the line's name is in English
