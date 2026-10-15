# Spec Delta

## MODIFIED Requirements

### Requirement: Form version status
Every project member SHALL be able to see the form versions that the project's non-deleted submissions use. For each version, the system SHALL show its identifier, its deployment date when known, its number of non-deleted submissions, and the status of its definition: `available`, `pending`, `failed` with the reason and the time of the next attempt, or `not_found`. The list SHALL also show how many non-deleted submissions have no form version.

#### Scenario: Viewer reads the versions
- **WHEN** a `viewer` opens the Submissions section of a project with 120 submissions made with `vA`, whose definition is kept, and 15 made with `vB`, whose fetch failed
- **THEN** they see `vA` as available with 120 submissions, and `vB` as failed with 15 submissions, the reason and the time of the next attempt

#### Scenario: Submissions without a version
- **WHEN** 3 of the project's non-deleted submissions have no form version
- **THEN** the list shows that 3 submissions have no form version

#### Scenario: Re-check offered where the versions are listed
- **WHEN** an `analyst` reads a version marked as `failed` or `not_found`
- **THEN** the list offers to re-check the form in place, and a `viewer` reading the same list is told to ask an analyst or admin instead
