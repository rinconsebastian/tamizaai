# Spec Delta

## ADDED Requirements

### Requirement: Number formats on paper
A printed dashboard SHALL write its numbers as the screen does: in the project's regional format when the project has one, else in the member's, with each widget's decimals and abbreviated numbers. The sheet's own words and dates SHALL stay in the member's interface language and regional format.

#### Scenario: Printed by an English member
- **WHEN** the Duitama project's regional format is `es-CO` and a member whose browser prefers `en-US` prints "2. Teatro"
- **THEN** the stat tile over `teatro_hoy` prints `46,85 % de los hogares`, with a no-break space before `%` as on screen, and the sheet's own words are in English

#### Scenario: Abbreviated tile printed
- **WHEN** a member prints a dashboard with the abbreviated tile over `comercio_gastos_media`, in a project whose regional format is `es-CO`
- **THEN** the tile prints `1 M pesos`, as on screen
