## MODIFIED Requirements

### Requirement: Widget context on paper
A printed widget SHALL show:

- its whole title and its whole subtitle, the subtitle under the title, except a compact stat tile's, which prints as a line of its foot under the figure;
- its caption as on screen: its own text or the default line above the chart, or none when it is hidden or out of sight under the subtitle. An own text out of sight SHALL print whole as the first line of its foot;
- its values with its own unit;
- its whole footnote as the last line of its foot.

The texts take room from its plot inside its box. The tile never changes size, so texts that need more room than the tile SHALL be cut by it. The indicator's description and its button SHALL NOT be printed.

#### Scenario: Whole texts on paper
- **WHEN** a member prints a widget whose title, subtitle and footnote each show two cut lines on screen
- **THEN** the sheet shows the whole title, the whole subtitle and the whole footnote, and the chart drawn in the room left inside the widget's box

#### Scenario: Texts taller than the tile
- **WHEN** a member prints a widget in a tile 3 columns wide and 2 rows high whose footnote of 500 characters needs more height than the tile has
- **THEN** the tile keeps its size and the sheet shows the footnote as far as the tile holds it

#### Scenario: Hidden caption on paper
- **WHEN** a member prints a column chart that hides its caption, or has a subtitle and does not keep its caption in sight
- **THEN** the sheet shows no caption line above its columns

#### Scenario: Own caption text on paper
- **WHEN** a member prints "2.1. Asistió a espectáculos, por estrato", whose caption text "% de los hogares que asistieron" is out of sight under its subtitle
- **THEN** the sheet shows no line above its columns, and the whole caption line as the first line of its foot

#### Scenario: Description not printed
- **WHEN** a member prints a widget that offers its indicator's description
- **THEN** the sheet shows neither the description button nor the description

### Requirement: Chart annotations on paper
A printed bar, column, line or pie widget SHALL show its reference lines with their texts on their boxes, its steps and its data labels, laid out for the widget's size on paper. A data label that does not fit there SHALL NOT be printed. A reference line whose text is not drawn at the line SHALL print its text as a line of its foot (see "Widget foot on paper"). When the widget's texts alone fill the tile, the tile SHALL clip what does not fit, the chart and its foot included.

#### Scenario: Reference lines printed
- **WHEN** a member prints "2. Teatro", whose column chart "2.1. Asistió, por comuna" has a `mean` and an `overall` line
- **THEN** the sheet shows both dashed lines across the columns, each with its text and value at the line or, when it did not fit there, in the widget's foot, inside the widget's box

#### Scenario: Key in a tile the texts fill
- **WHEN** a member prints a column chart in a tile 4 columns wide and 3 rows high with a `mean` line and a footnote of 499 characters that needs the whole tile
- **THEN** the tile keeps its size and the sheet shows the footnote as far as the tile holds it, and the chart only if there is room left for it

#### Scenario: Data labels on paper
- **WHEN** a member prints a bar chart with data labels whose bars are thinner on paper than on screen
- **THEN** each label that fits its bar on paper is printed, and none overlaps another

#### Scenario: Steps printed
- **WHEN** a member prints a line chart drawn with steps
- **THEN** the sheet shows the steps, not straight segments

### Requirement: Small bases on paper
A printed dashboard SHALL draw dimmed marks with their dashed outline or hatching, which read without color. Each widget's foot SHALL print whole as lines (see "Widget foot on paper"): its n, its small bases naming the marks, and its no-data value. A stat tile's figure SHALL print at full contrast.

#### Scenario: Printed in black and white
- **WHEN** a member prints "Teatro", whose threshold is 30, on a black-and-white printer
- **THEN** the columns of `COMUNA 7` and `SIN NOMBRE` keep their dashed outline, and the lines naming them and the n lines print under their charts

### Requirement: Stat tile on paper
A printed stat tile SHALL size its value to its box on paper by the rule of the screen, at most 30 px and at least 18 px, and break it only between words. It SHALL print at full contrast, followed by what it plots, unless the widget hides its caption, and its line without filters. Its n SHALL print as a line of its foot, with its subtitle and footnote as "Widget context on paper" says.

#### Scenario: Tiles of a long dashboard
- **WHEN** a member prints "Teatro", whose 16 stat tiles are 3 or 4 columns wide and 2, 3 or 6 rows high
- **THEN** each tile's value prints whole inside its box, at no more than 30 px, with no break inside a number

#### Scenario: Secondary lines on paper
- **WHEN** a member prints a dashboard while the filter "Comuna" is set to `COMUNA 1` and a stat tile shows both secondary lines
- **THEN** the sheet shows the tile's value, what it plots and the line without filters inside its box, and its n as a line of its foot

## ADDED Requirements

### Requirement: Widget foot on paper
On paper the chips of a widget's foot SHALL NOT be printed as chips. Each SHALL print as one line of its whole text, in the order of the foot, the footnote last. The detail chip of a compact stat tile is a line like the others: its subtitle prints under the figure, and not under the title. Every text the foot opens on screen SHALL be on the sheet, but the amber n chip of a stat tile, which SHALL print a short line (see "Amber n of a stat tile on paper").

#### Scenario: Foot printed as lines
- **WHEN** a member prints "2.1. Asistió, por comuna" of "Teatro", whose foot has the chips "n = 475 Hogares", "2 with a small base" and "Note"
- **THEN** the sheet shows under the bars "n = 475 Hogares", then "Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.", then the whole footnote, and no chip

#### Scenario: Amber n of a stat tile on paper
- **WHEN** a member prints the stat tile "3.4. Mediana de lo que pagan hoy al mes", whose n chip is amber with `n = 26`
- **THEN** the sheet shows the figure at full contrast and, in the tile's foot, the one line "Small base: n = 26, below 30.", while a screen reader still gets the whole sentence that says the figure rests on n = 26, below 30 and is to be read with caution

#### Scenario: Line chip on paper
- **WHEN** a member prints a column chart whose reference line's text did not fit at the line
- **THEN** the sheet shows that line's text and value as a line of its foot
