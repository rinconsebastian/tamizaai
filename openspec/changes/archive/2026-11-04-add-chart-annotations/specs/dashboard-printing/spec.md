# Spec Delta

## ADDED Requirements

### Requirement: Chart annotations on paper
A printed bar, column, line or pie widget SHALL show its reference lines with their labels and key, its steps and its data labels, laid out for the widget's size on paper: a data label that does not fit there SHALL NOT be printed, and the key SHALL list every reference line, taking its room from the plot as the widget's texts do (see "Widget context on paper"): when the texts alone fill the tile, the tile clips what does not fit, the key and the chart included.

#### Scenario: Reference lines printed
- **WHEN** a member prints "2. Teatro", whose column chart "2.1. Asistió, por comuna" has a `mean` and an `overall` line
- **THEN** the sheet shows both dashed lines across the columns and the key listing them with their values, inside the widget's box

#### Scenario: Key in a tile the texts fill
- **WHEN** a member prints a column chart in a tile 4 columns wide and 3 rows high with a `mean` line and a footnote of 499 characters that needs the whole tile
- **THEN** the tile keeps its size and the sheet shows the footnote as far as the tile holds it, and the chart and its key only if there is room left for them

#### Scenario: Data labels on paper
- **WHEN** a member prints a bar chart with data labels whose bars are thinner on paper than on screen
- **THEN** each label that fits its bar on paper is printed, and none overlaps another

#### Scenario: Steps printed
- **WHEN** a member prints a line chart drawn with steps
- **THEN** the sheet shows the steps, not straight segments
