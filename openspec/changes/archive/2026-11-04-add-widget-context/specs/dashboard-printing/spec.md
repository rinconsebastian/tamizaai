# Spec Delta

## MODIFIED Requirements

### Requirement: Print header
Above the widgets, a printed dashboard SHALL give when it was printed, to the minute, in the project's time zone, naming that zone, and the latest time one of its widgets' indicators was published. When none has published, it SHALL say so. It SHALL give the source of the dashboard, or of the version shown, when it has one, and SHALL NOT repeat it under the widgets. Times SHALL use the interface's shared date format.

#### Scenario: Header
- **WHEN** a member with the Spanish UI prints "Housing" of "Census 2026" at 10:30 in `America/Bogota`, and its widgets' indicators were last published at 05:12 that day
- **THEN** the sheet names "Census 2026" and "Housing", gives 10:30 with `America/Bogota` as the print time, and 05:12 as the time the data was published

#### Scenario: Nothing published
- **WHEN** a member prints a dashboard none of whose widgets' indicators has published
- **THEN** the header says the data has not been published yet

#### Scenario: Source in the print header
- **WHEN** a member with the Spanish UI prints "Teatro", whose source is "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas"
- **THEN** the print header reads "Fuente: Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas", and the sheets do not repeat it under the widgets

#### Scenario: No source printed
- **WHEN** a member prints a dashboard with no source
- **THEN** the print header has no source line

### Requirement: Charts on paper
Every chart, pie chart, heat map, population pyramid, Likert widget, box plot and stat tile SHALL print whole inside its widget's box, with all its marks, labels and legend. Each widget SHALL print as the member shows it at that moment, as a chart or as a table. A widget's subtitle and footnote print whole and take their room from its chart (see "Widget context on paper"); a chart they leave less room than its smallest size SHALL be cut at its own box and SHALL NOT be drawn over them.

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

#### Scenario: Texts that leave a chart too little room
- **WHEN** a member prints a column chart 4 columns wide and 3 rows high whose footnote of 500 characters needs about nine lines on paper
- **THEN** the sheet shows the whole footnote, and the chart is cut at its own box, never over the footnote

## ADDED Requirements

### Requirement: Widget context on paper
A printed widget SHALL show its whole subtitle and its whole footnote, which take room from its plot inside its box, its caption as on screen (its own text, or none when the widget hides it), and its values with its own unit. The tile never changes size, so texts that need more room than the whole tile has SHALL be cut by the tile. The indicator's description and its button SHALL NOT be printed.

#### Scenario: Whole texts on paper
- **WHEN** a member prints a widget whose subtitle and footnote each show two cut lines on screen
- **THEN** the sheet shows the whole subtitle and the whole footnote, and the chart drawn in the room left inside the widget's box

#### Scenario: Texts taller than the tile
- **WHEN** a member prints a widget in a tile 3 columns wide and 2 rows high whose footnote of 500 characters needs more height than the tile has
- **THEN** the tile keeps its size and the sheet shows the footnote as far as the tile holds it

#### Scenario: Hidden caption on paper
- **WHEN** a member prints a column chart that hides its caption
- **THEN** the sheet shows no caption line for it

#### Scenario: Description not printed
- **WHEN** a member prints a widget that offers its indicator's description
- **THEN** the sheet shows neither the description button nor the description
