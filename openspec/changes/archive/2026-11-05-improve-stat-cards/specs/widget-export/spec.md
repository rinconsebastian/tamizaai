# Spec Delta

## ADDED Requirements

### Requirement: Stat tile in a widget image
An image of a stat tile SHALL draw its value at the size its tile gives it on screen, at most 30 px and at least 18 px, broken only between words; a word wider than the image SHALL widen the image instead of being cut. Under the value it SHALL show what the tile plots, naming its statistic, unless the widget hides its caption, then the secondary lines the tile shows at that moment, as the tile words them, before the notes, the footnote and the footer.

#### Scenario: Smaller value
- **WHEN** a member exports the stat tile `46.85% de los hogares`, 3 columns wide, as an SVG image
- **THEN** the image draws its value at no more than 30 px, not at 36 px, with its title and what it plots

#### Scenario: Secondary lines in the image
- **WHEN** a member exports as a PNG image a stat tile that shows `n = 476 Hogares` and `Without filters: 46.85% de los hogares` under the dashboard filter "Comuna"
- **THEN** the image shows both lines under what the tile plots

#### Scenario: Statistic in the image
- **WHEN** a member exports a stat tile with the statistic median
- **THEN** what the image says the tile plots starts with "Median of"

#### Scenario: Lines above the footnote
- **WHEN** a member exports as a PNG image a stat tile that has a footnote and shows `n = 476 Hogares`
- **THEN** the image shows the value, what the tile plots, the n line, the footnote and the footer, in that order

#### Scenario: Long figure
- **WHEN** a member exports a stat tile 1 column wide that shows `1,016,279 pesos`
- **THEN** the image shows `1,016,279` whole, on one line, and is as wide as it needs

### Requirement: Stat tile lines in a widget workbook
A stat tile's widget workbook SHALL keep one column and one row holding its value on its data sheet. Its "Summary" sheet SHALL name the tile's statistic in what it plots, and SHALL have one more line for each secondary line the tile shows, after what it plots, with the same text as the tile.

#### Scenario: Median tile's workbook
- **WHEN** a member exports the workbook of a stat tile with the statistic median
- **THEN** its data sheet holds one number, the median, and what its "Summary" sheet says the tile plots starts with "Median of"

#### Scenario: Secondary lines in the Summary
- **WHEN** a member exports, with "Comuna" set to `COMUNA 1`, the workbook of a stat tile that shows both secondary lines
- **THEN** its "Summary" sheet has a line with the tile's n and a line with its value without the dashboard filters, as the tile shows them
