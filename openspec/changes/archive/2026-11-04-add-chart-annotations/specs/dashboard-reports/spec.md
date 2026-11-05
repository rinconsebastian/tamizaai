# Spec Delta

## ADDED Requirements

### Requirement: Reference lines in a scheduled email
The block of a widget with reference lines SHALL list, under its table in the HTML body and in the plain text, after the lines that say how many more rows or columns it has and before its footnote, each line drawn with its label and value in the email's language and regional format, in the order the widget lists them. The workbook's "Summary" sheet SHALL list them for that widget, after its subtitle and footnote. The widget's table SHALL keep the columns and rows of its "Show as table" view.

#### Scenario: Lines under the table
- **WHEN** the email of "2. Teatro" holds the column chart "2.1. Asistió, por comuna" over `teatro_hoy`, whose unit is `de los hogares`, with a `mean` line at 0.3417 and an `overall` line at 0.4599, in American English
- **THEN** the chart's block shows its table and, under it, "Mean: 34.17% de los hogares" and "Overall: 45.99% de los hogares"

#### Scenario: Default labels in Spanish
- **WHEN** that email is sent in Spanish
- **THEN** the lines read with the Spanish default labels, "Promedio" and "Valor general"

#### Scenario: Lines before the footnote
- **WHEN** the block of the bar chart "2.4. En qué lugares" of "Teatro", which has 12 bars, the footnote "No suman 100 %: respuesta múltiple." and a `mean` line, is in the email
- **THEN** it shows its first 10 rows, the line saying it has 2 more rows, the mean line, and then the footnote

#### Scenario: Lines in the workbook
- **WHEN** that email attaches its workbook
- **THEN** the "Summary" sheet lists the chart's two reference lines with their values, and the chart's sheet holds its table as before

#### Scenario: Widget without reference lines
- **WHEN** a widget of "2. Teatro" has no reference lines
- **THEN** its block and the workbook's "Summary" sheet are as before
