# Spec Delta

## ADDED Requirements

### Requirement: Indicator decimals
An indicator SHALL be able to set how many decimals its widgets show, from 0 to 4, in its details next to its format, or set none. The decimals SHALL be saved with every version of the indicator's script and come back with a restore, and any other value SHALL be rejected. A widget's own decimals go before them (see `dashboard-indicators` "Decimals of a widget"). An indicator with none SHALL show the digits of its format, as before.

#### Scenario: Decimals saved
- **WHEN** an analyst saves `teatro_precio_hogar`, with the format `count` and the unit `pesos`, with 0 decimals
- **THEN** the project lists the indicator with 0 decimals, and its box plots write their figures as whole numbers

#### Scenario: Decimals restored with a version
- **WHEN** an analyst saves version 2 of an indicator with 1 decimal and then restores version 1, which set none
- **THEN** version 3 sets no decimals

#### Scenario: Decimals offered next to the format
- **WHEN** an analyst opens the details of `teatro_hoy`, whose format is `percent` and which sets no decimals, above its script
- **THEN** next to the format a choice of decimals reads "Format's default (up to 2)", and choosing 1 redraws the preview of its last test with one decimal, with no new test

#### Scenario: Decimals out of range
- **WHEN** an analyst saves an indicator with 5 decimals
- **THEN** the save is rejected with an error on `decimals`, and no version is added

#### Scenario: Indicator saved before decimals
- **WHEN** a deployment is upgraded to this version
- **THEN** every existing indicator version sets no decimals, and its widgets show the numbers they showed before
