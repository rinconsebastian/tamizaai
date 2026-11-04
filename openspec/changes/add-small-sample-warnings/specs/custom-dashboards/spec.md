# Spec Delta

## ADDED Requirements

### Requirement: Base settings of a duplicated dashboard
Duplicating a dashboard SHALL copy its base settings, the base n line and the small-base threshold, and each widget keeps its own. Changing them afterwards on the copy or on the original SHALL NOT change the other.

#### Scenario: Base settings copied
- **WHEN** an `analyst` duplicates "Teatro", whose threshold is 30 and which shows its base n lines, as "Teatro 2"
- **THEN** "Teatro 2" has the threshold 30 and shows its base n lines

#### Scenario: Base settings of copies independent
- **WHEN** an `analyst` turns the threshold of "Teatro 2" off
- **THEN** "Teatro" keeps the threshold 30
