# Spec Delta

## ADDED Requirements

### Requirement: Highlighted values in widget files
A widget's PNG and SVG images SHALL draw its highlighted values, the neutral color of the values it leaves out and the neutral color kept by value as the screen does, with the bold names of highlighted values in its legend and on its axis. A widget's workbook SHALL hold the same sheets, summary lines, rows and numbers whether or not the widget highlights values or its dashboard keeps the neutral color.

#### Scenario: Highlight in an image
- **WHEN** a member exports as a PNG image a pie chart that highlights `Culturama (escuelas de formación)`
- **THEN** the image shows that slice in its color with its legend name in bold, and every other slice and legend swatch in the neutral color

#### Scenario: Highlight in a workbook
- **WHEN** a member downloads the workbook of the bar chart "2.4. En qué lugares", which highlights `Centro cultural o casa de la cultura`
- **THEN** the workbook lists every place with its value, and its "Summary" lists the same lines, as before the highlight was set
