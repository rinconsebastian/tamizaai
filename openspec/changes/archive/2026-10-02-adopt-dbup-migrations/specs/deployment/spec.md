# Spec Delta

## MODIFIED Requirements

### Requirement: Automatic metadata schema migrations
On startup, `api` SHALL apply pending metadata schema migrations before reporting healthy. The metadata schema SHALL be defined only by versioned SQL migration scripts kept in the repository. Each migration SHALL be applied at most once, in version order, and atomically: a migration that fails SHALL leave none of its changes in the database. The database SHALL record which migrations have been applied.

#### Scenario: Empty database
- **WHEN** `api` starts against a newly created database
- **THEN** it creates the full metadata schema, records every migration as applied, and then reports healthy

#### Scenario: Database already up to date
- **WHEN** `api` restarts against a database where every migration has been applied
- **THEN** it applies no migration, leaves the schema and data unchanged, and reports healthy

#### Scenario: Upgrade with new migrations
- **WHEN** the operator upgrades to a release that adds migrations and starts `api`
- **THEN** only the migrations not yet recorded are applied, in version order, and the existing data is kept

#### Scenario: Failed migration
- **WHEN** a migration fails
- **THEN** none of that migration's changes remain in the database, it is not recorded as applied, `api` does not report healthy, the services that depend on it do not start, and the error is written to the log

#### Scenario: Concurrent startups
- **WHEN** two `api` processes start against the same database with pending migrations
- **THEN** each migration is applied exactly once and both processes report healthy after it has been applied

#### Scenario: Database created by the previous migration mechanism
- **WHEN** `api` starts against a database whose schema was created by an earlier version of Tamiza, before SQL migration scripts were introduced, and that database has every table of that version
- **THEN** the existing tables and data are kept, the scripts equivalent to that schema are recorded as applied without being run, later migrations are applied, and `api` reports healthy

#### Scenario: Database at an unsupported earlier state
- **WHEN** `api` starts against a database created by an earlier version of Tamiza that is missing tables of the last version before SQL migration scripts were introduced
- **THEN** `api` changes nothing, does not report healthy, and the log states that the database must be recreated
