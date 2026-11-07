## MODIFIED Requirements

### Requirement: Parts of a widget image
An image SHALL hold, top to bottom:

- the widget's full title, then its whole subtitle when it has one, a compact stat tile's too;
- the notes shown above its chart;
- its caption unless the widget hides it or has it out of sight under its subtitle;
- its chart or table, then its whole legend, key or scale;
- the notes shown below it, its foot as lines of whole text, and a footer.

It SHALL NOT hold tooltips, buttons, chips, handles, map controls, menus, focus or hover marks, the indicator's description, or text meant only for screen readers.

#### Scenario: Long title
- **WHEN** a widget's title has 120 characters and is cut with an ellipsis on screen
- **THEN** its image shows the 120 characters, over more than one line

#### Scenario: Stacked columns
- **WHEN** a member exports a stacked column chart with 4 series as a PNG image
- **THEN** the image shows every segment and the legend naming the 4 series

#### Scenario: Pie legend
- **WHEN** a pie chart's legend has 12 rows and scrolls inside its box on screen
- **THEN** its image shows the 12 rows

#### Scenario: Box plot
- **WHEN** a member exports a box plot with 6 boxes, 23 of whose outliers are not drawn
- **THEN** the image shows the 6 boxes, the key explaining each mark, and the note that 23 outliers are not drawn

#### Scenario: Stat tile
- **WHEN** a member exports the stat tile "Households visited" as an SVG image
- **THEN** the image shows its title, its value and its description

#### Scenario: Tooltip open
- **WHEN** a bar's tooltip is open when a member exports the chart
- **THEN** the image shows no tooltip and no bar drawn as pointed at

#### Scenario: Subtitle and footnote
- **WHEN** a member exports "2.4. En qué lugares", whose subtitle shows two cut lines on screen and whose footnote is "No suman 100 %: respuesta múltiple.", as a PNG image
- **THEN** the image shows the whole subtitle under the title, and the footnote as the last line of its foot, above the footer

#### Scenario: Caption hidden
- **WHEN** a member exports a column chart that hides its caption, or has a subtitle and does not keep its caption in sight
- **THEN** the image shows no caption line above the plot

#### Scenario: Own caption text under a subtitle
- **WHEN** a member exports as a PNG image "2.1. Asistió a espectáculos, por estrato", whose caption text "% de los hogares que asistieron" is out of sight under its subtitle
- **THEN** the image shows no line above its columns, and the whole caption line as the first line of its foot

#### Scenario: Own caption text
- **WHEN** a member exports a bar chart grouped by `categoria`, with no subtitle, whose caption text is "% de quienes asistieron", in the English interface
- **THEN** the image's caption line reads "% de quienes asistieron by categoria"

#### Scenario: Chip open
- **WHEN** a chip of the widget's foot is open when a member exports the widget
- **THEN** the image shows no chip and no panel, and the foot's texts as lines

### Requirement: Chart annotations in widget images
An image of a bar, column, line or pie widget SHALL show its reference lines with the texts drawn at them on their boxes, its steps and its data labels, as the member sees them. A reference line whose text is not drawn at the line SHALL have its text, label and formatted value, as a line of the image's foot. Its legend SHALL NOT list the reference lines. An image of such a widget shown as a table SHALL list the lines under the table. The lines SHALL come before the footnote.

#### Scenario: Mean line in an image
- **WHEN** a member exports as a PNG image the column chart "2.1. Asistió, por comuna" over `teatro_hoy`, which has a `mean` line at 34.17%
- **THEN** the image shows the dashed line across the columns with "Mean: 34.17% de los hogares" on its box at the line, and no legend entry for it

#### Scenario: Data labels in an image
- **WHEN** a member exports as an SVG image a bar chart with data labels
- **THEN** the image shows each label the chart draws on screen, and no label the chart left out

#### Scenario: Shown as a table
- **WHEN** a member shows a column chart of a `percent` indicator with no unit and an `overall` line at 45.99% as a table, and exports it as a PNG image
- **THEN** the image shows the table and, under it, "Overall: 45.99%"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image a column chart that has a footnote and a `mean` line whose text did not fit at the line
- **THEN** the image shows the line's text as a line of its foot, the footnote under it, and the footer under the footnote

### Requirement: Base n and small bases in widget images
A widget's PNG or SVG image SHALL draw its marks dimmed as the screen does, and a stat tile's figure at full contrast. Under its chart, before its footnote, it SHALL show as lines of whole text the n, small-base and no-data texts that the widget's foot opens.

#### Scenario: Dimmed columns in an image
- **WHEN** a member exports as a PNG image "2.1. Asistió, por comuna", whose `COMUNA 7` and `SIN NOMBRE` columns are dimmed
- **THEN** the image shows those two columns lighter with their dashed outline, and the line "Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed."

#### Scenario: Lines in an image
- **WHEN** a member exports as an SVG image a widget whose foot has the chips "n = 223 Hogares que asistieron" and "No data 2.24%"
- **THEN** the image shows "n = 223 Hogares que asistieron" and "No sabe: 2.24%" under its chart

#### Scenario: Lines before the footnote in an image
- **WHEN** a member exports as a PNG image "2.4. En qué lugares", which shows "n = 223 Hogares que asistieron" and has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the image shows the n line under the chart and the footnote under the n line, above the footer

### Requirement: Stat tile in a widget image
An image of a stat tile SHALL draw its value at full contrast, at the size its tile gives it on screen, at most 30 px and at least 18 px, broken only between words. A word wider than the image SHALL widen the image instead of being cut. Under the value it SHALL show what the tile plots, naming its statistic, unless the widget hides its caption, then its line without filters, then its foot's texts as lines, the n first, the footnote last, and the footer.

#### Scenario: Smaller value
- **WHEN** a member exports the stat tile `46.85% de los hogares`, 3 columns wide, as an SVG image
- **THEN** the image draws its value at no more than 30 px, not at 36 px, with its title and what it plots

#### Scenario: Secondary lines in the image
- **WHEN** a member exports as a PNG image a stat tile that shows `n = 476 Hogares` and `Without filters: 46.85% de los hogares` under the dashboard filter "Comuna"
- **THEN** the image shows the line without filters under what the tile plots, and `n = 476 Hogares` as the first line of its foot

#### Scenario: Statistic in the image
- **WHEN** a member exports a stat tile with the statistic median
- **THEN** what the image says the tile plots starts with "Median of"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image a stat tile that has a footnote and shows `n = 476 Hogares`
- **THEN** the image shows the value, what the tile plots, the n line, the footnote and the footer, in that order

#### Scenario: Long figure
- **WHEN** a member exports a stat tile 1 column wide that shows `1,016,279 pesos`
- **THEN** the image shows `1,016,279` whole, on one line, and is as wide as it needs

#### Scenario: Small base in the image
- **WHEN** a member exports the stat tile "3.4. Mediana de lo que pagan hoy al mes", whose n is 26 under the threshold 30
- **THEN** the image draws `60,000 pesos` at full contrast, and its foot's line says that the figure rests on n = 26, below 30
