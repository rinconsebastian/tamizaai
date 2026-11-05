# Spec Delta

## ADDED Requirements

### Requirement: Stat tile on paper
A printed stat tile SHALL size its value to its box on paper by the rule of the screen, at most 30 px and at least 18 px, break it only between words, and print what it plots, unless the widget hides its caption, and its secondary lines inside its box, with its subtitle and footnote as "Widget context on paper" says.

#### Scenario: Tiles of a long dashboard
- **WHEN** a member prints "Teatro", whose 16 stat tiles are 3 or 4 columns wide and 2 or 3 rows high
- **THEN** each tile's value prints whole inside its box, at no more than 30 px, with no break inside a number

#### Scenario: Secondary lines on paper
- **WHEN** a member prints a dashboard while the filter "Comuna" is set to `COMUNA 1` and a stat tile shows both secondary lines
- **THEN** the sheet shows the tile's value, what it plots and both lines inside its box
