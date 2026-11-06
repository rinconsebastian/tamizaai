# Spec Delta

## ADDED Requirements

### Requirement: Stat tile in a scheduled email
In the body of a scheduled email, a stat tile's value SHALL be written at 20 px, under its title and subtitle, followed by what it plots, naming its statistic when it has one and left out when the widget hides its caption, and by the secondary lines the tile asks for, computed with the dashboard filters at their defaults, before the tile's notes and footnote. The plain-text version and the tile's line on the workbook's "Summary" sheet SHALL carry the same secondary lines after the value.

#### Scenario: Smaller value
- **WHEN** the "Teatro" email shows the stat tile `46.85% de los hogares`
- **THEN** its value is written at 20 px, not at 24 px

#### Scenario: Median in the email
- **WHEN** a stat tile of the email has the statistic median
- **THEN** what the body says it plots starts with "Median of", and its value is the median over the rows the dashboard filters' defaults keep

#### Scenario: Line without filters with a default
- **WHEN** the dashboard filter "Comuna" has the default `COMUNA 1` and a stat tile shows the line without filters
- **THEN** the body shows the tile's value for `COMUNA 1` and, under it, its value without the dashboard filters

#### Scenario: Line without filters and no default
- **WHEN** no dashboard filter has a default and a stat tile shows the line without filters
- **THEN** the body shows no "Without filters" line for it

#### Scenario: Lines above the footnote
- **WHEN** the stat tile "Fuera de Duitama" of the email has the footnote "Sin dato aparte (E5)." and shows `n = 476 Hogares`
- **THEN** its cell shows the value, what it plots, the n line and then the footnote

#### Scenario: Lines in the plain text and the workbook
- **WHEN** a stat tile of the email shows `n = 476 Hogares`
- **THEN** the plain-text version and the tile's "Summary" line give `n = 476 Hogares` after the value

### Requirement: Ratio over declared categories in a scheduled email
A widget that shows a message instead of its figures because it would add up several answers of a column declared as the answers of a multiple-choice question SHALL appear in a scheduled email as a widget whose settings no longer fit its table, with a reason in the email's language that names the column and says why. Its totals that would add up several of those answers SHALL be missing values. A ratio over several answers of a single-choice column SHALL be the figure the dashboard shows.

#### Scenario: Refused card in the email
- **WHEN** the email of "Escuelas" holds a stat tile over two reasons of `escuelas_razones_no`, whose `categoria` is declared as the answers of a multiple-choice question
- **THEN** the body shows the tile's title with the reason naming `categoria`, and the other widgets as usual

#### Scenario: Corrected card in the email
- **WHEN** the email holds the stat tile over `cruces_intencion` that keeps `categoria` in `1`, `2`, `3` and `4`, and its `categoria` is declared as the answers of a single-choice question
- **THEN** the body shows `95.17% de los hogares`

#### Scenario: Total row of a declared column
- **WHEN** the email holds a heat map grouped by `categoria`, declared as the answers of a multiple-choice question, with the series `comuna`
- **THEN** its "Total" row reads "—" in the body and is empty in the workbook
