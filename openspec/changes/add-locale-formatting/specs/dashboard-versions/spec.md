# Spec Delta

## ADDED Requirements

### Requirement: Number formats and dashboard versions
A project's regional format and its indicators' decimals SHALL NOT be part of any dashboard version: changing them SHALL make no version, and a version viewed or restored SHALL show its numbers in the project's current format, with the indicators' current decimals. A widget's own decimals and abbreviation SHALL be kept in a version with its other display settings, and restored with them.

#### Scenario: Regional format set
- **WHEN** an admin sets the Duitama project's regional format to `es-CO`
- **THEN** none of its 9 dashboards gets a new version

#### Scenario: Version viewed after the format changed
- **WHEN** an analyst views version 12 of "2. Teatro", saved before the project's regional format was set to `es-CO`
- **THEN** the version's stat tile over `teatro_hoy` reads `46,85 % de los hogares`

#### Scenario: Widget decimals restored
- **WHEN** an analyst sets 1 decimal on the stat tile over `teatro_hoy`, which makes version 19 of "2. Teatro", and then restores version 18
- **THEN** the tile sets no decimals again and reads `46.85% de los hogares` in a project with no regional format

#### Scenario: Indicator decimals saved
- **WHEN** an analyst saves `teatro_hoy` with 1 decimal
- **THEN** no dashboard gets a new version, and every widget over `teatro_hoy` that sets no decimals of its own shows one decimal
