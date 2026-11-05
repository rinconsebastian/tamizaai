# Spec Delta

## ADDED Requirements

### Requirement: Column labels on paper
A printed dashboard SHALL name every widget's columns as the screen does, in its captions and tables.

#### Scenario: Caption printed
- **WHEN** a member prints "Teatro", whose column chart grouped by `comuna` reads "Teatro hoy: Hogares que asistieron / Hogares by Comuna" on screen
- **THEN** the sheet shows the same caption

#### Scenario: Table printed
- **WHEN** that chart is shown as a table when a member prints "Teatro"
- **THEN** the printed table's headers read "Comuna" and "Teatro hoy: Hogares que asistieron / Hogares"
