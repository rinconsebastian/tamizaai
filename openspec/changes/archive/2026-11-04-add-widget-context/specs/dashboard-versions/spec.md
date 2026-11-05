# Spec Delta

## ADDED Requirements

### Requirement: Dashboard source in versions
A version SHALL hold the dashboard's source. Saving a different source SHALL make a version that says the source changed, and saving it unchanged SHALL make none. Viewing a version SHALL show its source, and restoring one SHALL bring its source back, or remove the source when the version has none. A dashboard that has never had a source SHALL keep the version content it had before sources existed.

#### Scenario: Source changed
- **WHEN** an `analyst` saves a source on "Teatro", whose newest version is 18
- **THEN** "Teatro" has version 19, holding the source and saying its source changed

#### Scenario: Source saved unchanged
- **WHEN** an `analyst` opens **Source** on "Teatro" and saves it without changing it
- **THEN** "Teatro" has no new version

#### Scenario: Source restored
- **WHEN** an `analyst` changes the source of "Teatro" and then restores the version before that change
- **THEN** the earlier source is back, and the restore is a new version

#### Scenario: Version recorded before sources
- **WHEN** an `analyst` restores version 18 of "Teatro", recorded before it had a source
- **THEN** "Teatro" has no source

#### Scenario: Dashboard saved before sources
- **WHEN** "Escuelas", which has no source and whose newest version was recorded before sources existed, has its filters saved unchanged
- **THEN** "Escuelas" has no new version
