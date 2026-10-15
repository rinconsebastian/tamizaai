# Spec Delta

## MODIFIED Requirements

### Requirement: Test queue and lifetime
Tests SHALL be executed by the same worker as runs and sample downloads, one at a time. A queued test SHALL start before any queued sample download or run, but SHALL wait for a running run, test or sample download to finish; while it waits, its status SHALL say whether it waits for a run, for another test or for a sample download. A user SHALL have at most one queued test per project: a new test SHALL replace that user's queued test in the project, and SHALL NOT affect a test that is already running. A test SHALL be visible only to the user who requested it. A finished test SHALL be deleted 24 hours after it ended. A test interrupted by a restart of the worker SHALL be recorded as failed.

#### Scenario: Test while a run is running
- **WHEN** an analyst starts a test while a run is running and another run is queued
- **THEN** the test shows that it waits for a run, starts when the running run ends, and the queued run starts after the test

#### Scenario: Test while a sample download is prepared
- **WHEN** an analyst starts a test while the worker prepares a sample download and another download is queued
- **THEN** the test shows that it waits for a sample download, starts when that download is ready, and the queued download is prepared after the test

#### Scenario: New test replaces a queued one
- **WHEN** an analyst starts a test while their previous test in the project has not started
- **THEN** the previous test is removed and only the new one runs

#### Scenario: Another member's test
- **WHEN** an admin requests a test that another analyst started
- **THEN** the API responds 404

#### Scenario: Expired test
- **WHEN** a test ended more than 24 hours ago
- **THEN** it can no longer be read

#### Scenario: Worker restarted during a test
- **WHEN** the worker restarts while a test is running
- **THEN** the test is recorded as failed because it was interrupted
