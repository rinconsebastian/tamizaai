# Spec Delta

## ADDED Requirements

### Requirement: Base settings in dashboard versions
A version SHALL hold its dashboard's base settings, the base n line and the small-base threshold, and saving them changed SHALL make a version that says the base sizes changed. Viewing a version SHALL draw its widgets with its settings, and restoring it SHALL bring them back. A version recorded before base settings existed SHALL hold none and restore them off. A dashboard that has none SHALL keep the version content it had.

#### Scenario: Threshold changed
- **WHEN** an `analyst` sets the small-base threshold of "Teatro" to 30
- **THEN** "Teatro" has a new version holding the threshold 30 and saying its base sizes changed

#### Scenario: Base settings restored
- **WHEN** an `analyst` restores the version of "Teatro" before that change
- **THEN** "Teatro" has no threshold again, recorded as a new version

#### Scenario: Version viewed with its threshold
- **WHEN** "Teatro" has no threshold now and an `analyst` views its version 9, which had the threshold 30
- **THEN** the page draws version 9's small bases dimmed

#### Scenario: Version recorded before base settings
- **WHEN** an `analyst` restores a version of "Teatro" recorded before base settings existed, while "Teatro" shows its base n lines
- **THEN** "Teatro" no longer shows them

#### Scenario: Dashboard without base settings
- **WHEN** a dashboard that never had base settings is saved unchanged after the upgrade
- **THEN** its newest version's content is the same, and no version is made for the upgrade alone
