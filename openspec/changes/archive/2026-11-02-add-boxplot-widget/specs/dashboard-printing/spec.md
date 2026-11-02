## MODIFIED Requirements

### Requirement: Charts on paper
Every chart, pie chart, heat map, population pyramid, Likert widget, box plot and stat tile SHALL print whole inside its widget's box, with all its marks, labels and legend. Each widget SHALL print as the member shows it at that moment, as a chart or as a table.

#### Scenario: Column chart
- **WHEN** a member prints a column chart with 12 columns
- **THEN** the sheet shows the 12 columns and their axis labels inside the widget's box

#### Scenario: Widget shown as a table
- **WHEN** a member shows one widget of "Housing" as a table and prints the dashboard
- **THEN** that widget prints as a table and the others as charts

#### Scenario: Heat map
- **WHEN** a member prints a heat map with 10 rows and 8 columns
- **THEN** the sheet shows its 80 cells, its row and column labels and the legend of its scale inside the widget's box

#### Scenario: Stacked chart
- **WHEN** a member prints a stacked column chart with 4 series
- **THEN** the sheet shows every segment and the legend naming the 4 series

#### Scenario: Box plot
- **WHEN** a member prints a horizontal box plot with 6 boxes, some of them with outliers
- **THEN** the sheet shows the 6 boxes with their medians, means, whiskers and outliers, their group labels, the scale and the key inside the widget's box
