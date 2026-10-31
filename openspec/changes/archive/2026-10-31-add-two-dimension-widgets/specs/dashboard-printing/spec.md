# Spec Delta

## MODIFIED Requirements

### Requirement: Charts on paper
Every chart, pie chart, heat map, population pyramid, Likert widget and stat tile SHALL print whole inside its widget's box, with all its marks, labels and legend. Each widget SHALL print as the member shows it at that moment, as a chart or as a table.

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

### Requirement: Tables on paper
A widget shown as a table, and a cross table widget, SHALL print the rows that fit in its box. When some rows do not fit, it SHALL say how many were left out. A table whose columns do not all fit SHALL print the group column and the columns that fit, and SHALL say how many columns were left out.

#### Scenario: Long table
- **WHEN** a member prints a widget shown as a table with 40 rows, of which 12 fit in its box
- **THEN** the sheet shows those 12 rows and says that 28 rows were not printed

#### Scenario: Short table
- **WHEN** a member prints a widget shown as a table whose 5 rows all fit
- **THEN** the sheet shows the 5 rows and no note about rows left out

#### Scenario: Wide cross table
- **WHEN** a member prints a cross table with 20 series columns, of which 9 fit in its box
- **THEN** the sheet shows the group column and those 9 columns, and says that 12 columns were not printed
