# Spec Delta

## ADDED Requirements

### Requirement: Chart annotations in widget images
An image of a bar, column, line or pie widget SHALL show its reference lines with the labels drawn at them, its steps and its data labels as the member sees them. Its legend SHALL list each reference line drawn with its dash pattern, label and formatted value, after the series of a widget with a series. An image of such a widget shown as a table SHALL list the lines under the table. The lines SHALL come before the footnote. An image of a widget without them SHALL have the parts it has today.

#### Scenario: Mean line in an image
- **WHEN** a member exports as a PNG image the column chart "2.1. Asistió, por comuna" over `teatro_hoy`, which has a `mean` line at 34.17%
- **THEN** the image shows the dashed line across the columns, and its legend reads "Mean: 34.17% de los hogares" next to a dashed swatch

#### Scenario: Data labels in an image
- **WHEN** a member exports as an SVG image a bar chart with data labels
- **THEN** the image shows each label the chart draws on screen, and no label the chart left out

#### Scenario: Shown as a table
- **WHEN** a member shows a column chart of a `percent` indicator with no unit and an `overall` line at 45.99% as a table, and exports it as a PNG image
- **THEN** the image shows the table and, under it, "Overall: 45.99%"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image the column chart "2.1. Asistió, por comuna", which has a footnote and a `mean` line
- **THEN** the image shows the legend with the line under the columns, the footnote under the legend, and the footer under the footnote

### Requirement: Reference lines in a widget workbook
The "Summary" sheet of the workbook of a widget with reference lines SHALL list each line drawn, with its label and its value as the widget's values are written, in the order the widget lists them, after what the widget plots. The data sheet SHALL keep the widget's table, with no row or column for the lines.

#### Scenario: Lines in the summary
- **WHEN** a member exports the workbook of a column chart of a `percent` indicator with no unit, with a `mean` line at 34.17% and a `value` line of 0.5 labelled "Meta"
- **THEN** its "Summary" sheet lists the reference lines as "Mean: 34.17%" and "Meta: 50%", and its data sheet lists the comunas as before

#### Scenario: Widget without reference lines
- **WHEN** a member exports the workbook of a column chart without reference lines
- **THEN** its "Summary" sheet has no reference line item
