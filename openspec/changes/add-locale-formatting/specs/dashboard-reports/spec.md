# Spec Delta

## MODIFIED Requirements

### Requirement: Email language and regional format
A scheduled email SHALL use the interface language and regional format of the member who last saved its settings (see `interface-language`); a region the server has no regional data for SHALL use its language's formats. The email's own words, its numbers and its dates SHALL follow them, except that when the project has a regional format (see `projects` "Project regional format"), the numbers of its widgets SHALL follow the project's format instead, as it is when the email is sent, with a no-break space before `%` in a Spanish format (see `dashboard-indicators` "Percentages in a Spanish regional format"); the email's own words, the counts in its messages and its dates keep the saver's. Project, dashboard, widget, indicator, filter and column names, and data values, SHALL be shown exactly as they were created.

#### Scenario: Region without data
- **WHEN** a member whose browser reports `en-CO` saves the settings
- **THEN** the settings are saved, and the email uses English with the formats of `en`

#### Scenario: Spanish of Colombia
- **WHEN** a member whose interface is in Spanish with the regional format `es-CO` saves the settings, and a stat tile over a `decimal` indicator totals 12345.5
- **THEN** the email's own words are in Spanish and the total reads `12.345,50`

#### Scenario: Percentage in Spanish of Colombia
- **WHEN** the email's regional format is `es-CO`, the project has no regional format, and a stat tile over a `percent` indicator has the value 0.455
- **THEN** the body shows `45,5%`

#### Scenario: Names kept
- **WHEN** a member whose interface is in English saves the settings of the dashboard "Vivienda"
- **THEN** the email's own words are in English, and the dashboard is named "Vivienda"

#### Scenario: Project format in an English email
- **WHEN** the Duitama project's regional format is `es-CO`, a member whose interface is in English with `en-US` saved the settings of the "2. Teatro" email, and its stat tile over `teatro_hoy` has the value 0.4685
- **THEN** the email's own words and its date are in English, and the tile reads `46,85 % de los hogares`

#### Scenario: Percentage with a space for a Spanish project format
- **WHEN** a member whose interface is in Spanish with `es-CO` saved the settings of an email, the project's regional format is `es-CO`, and a stat tile over a `percent` indicator has the value 0.455
- **THEN** the body, the plain text and the workbook's "Summary" show `45,5 %`, with a no-break space before `%`

#### Scenario: Format set after the settings were saved
- **WHEN** a member with `en-US` saved the settings of the "2. Teatro" email, and an admin later sets the project's regional format to `es-CO`
- **THEN** the next email writes the stat tile over `teatro_precio_hogar` as `60.514 pesos`, with no new save of the settings

### Requirement: Numbers in the workbook
In each widget's sheet, plotted values, row counts, pie shares, the shares of a 100% layout or a Likert widget, totals, the figures of a box plot and numeric tooltip values SHALL be numbers, not text. Plotted values, totals and the figures of a box plot SHALL follow the indicator's format: `count` a whole number with thousands separators, `decimal` and `currency` two decimals, `percent` a percentage; the figures of a box plot over a `count` indicator SHALL instead have two decimals, as in the dashboard. When the widget or its indicator sets its decimals (see `dashboard-indicators` "Decimals of a widget"), plotted values, totals and the figures of a box plot SHALL be shown with exactly that many decimals, a box plot over a `count` indicator included. Row counts, and a box plot's numbers of values and of outliers, SHALL be whole numbers, and shares percentages. A tooltip value SHALL follow its tooltip column's format, as in its tooltip. A unit SHALL be written in the column's header.

#### Scenario: Percentage cell
- **WHEN** a column chart over `piped_water_rate`, whose format is `percent`, has the value 0.42 for `Cali`
- **THEN** the cell holds the number 0.42, shown as a percentage

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over `survey_progress`, whose format is `percent`, has the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** its cell holds the number 20, shown as `20`, not as a percentage

#### Scenario: Unit in the header
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`
- **THEN** its value column's header names `households`, and its cells hold whole numbers

#### Scenario: Shares of a Likert widget
- **WHEN** a Likert widget's `Cali` row has 30% `Agree`
- **THEN** that cell holds the number 0.3, shown as a percentage, whatever the indicator's format

#### Scenario: Median of a count in the workbook
- **WHEN** a box plot over `household_size`, whose format is `count`, has a box of 1,200 values whose median is 4.5
- **THEN** that box's median cell holds the number 4.5, shown with two decimals, and its number of values cell holds 1,200, shown as a whole number

#### Scenario: Decimals in the workbook
- **WHEN** the column chart "2.2. Con qué frecuencia asiste (E2, % de quienes asistieron)" over `teatro_hoy` sets 1 decimal and one category has the value 0.157
- **THEN** that category's cell holds the number 0.157, shown as a percentage with one decimal

#### Scenario: Box plot with its indicator's decimals in the workbook
- **WHEN** `teatro_precio_hogar`, whose format is `count`, sets 0 decimals and a box plot over it has a median of 40000 and a mean of 60513.51
- **THEN** the median cell holds 40000 and the mean cell 60513.51, both shown as whole numbers

## ADDED Requirements

### Requirement: Widget decimals in a scheduled email
A scheduled email's body, its plain text and its workbook's "Summary" SHALL write each widget's values, totals and box plot figures with the widget's decimals, else its indicator's, else the digits of its format, as the dashboard does. A stat tile that abbreviates its value on the dashboard SHALL be written in full. Shares and row counts SHALL keep their digits.

#### Scenario: One decimal in the email
- **WHEN** the Duitama project's regional format is `es-CO` and the stat tile over `teatro_hoy` in the "2. Teatro" email sets 1 decimal and has the value 0.4685
- **THEN** the body, the plain text and the workbook's "Summary" read `46,9 % de los hogares`

#### Scenario: Abbreviated tile written in full
- **WHEN** a stat tile over `comercio_gastos_media` that abbreviates its value of 1016279 is in an email of a project whose regional format is `es-CO`
- **THEN** the body and the workbook's "Summary" read `1.016.279 pesos`

#### Scenario: Widget without decimals
- **WHEN** the column chart "2.2. Con qué frecuencia asiste (E2, % de quienes asistieron)" sets no decimals, its indicator sets none, and the email is in American English for a project with no regional format
- **THEN** its block reads `15.7% de los hogares` and `11.21% de los hogares`, as before this change
