# deployment Specification

## Purpose

Let an operator install, run and upgrade Tamiza on their own server with Docker Compose, with safe defaults and modest hardware requirements.

## Requirements

### Requirement: Single-command startup
The deployment SHALL start with `docker compose up` from an `.env` file and SHALL include the `ui`, `api`, `analytics` and `db` services. Keycloak is not part of the deployment.

#### Scenario: Fresh install
- **WHEN** the operator copies `.env.example` to `.env`, fills in the required variables and runs `docker compose up`
- **THEN** the four services start, become healthy, and the UI responds at the configured public URL

#### Scenario: Missing required variable
- **WHEN** a required variable is missing from `.env`
- **THEN** `docker compose` refuses to start and the error message names the missing variable

### Requirement: Documented environment variables
The repository SHALL include an `.env.example` listing every variable the deployment reads. Each variable SHALL state its purpose, whether it is required, and its default value when it has one.

#### Scenario: Variable review
- **WHEN** `.env.example` is compared with the variables referenced in the Compose files
- **THEN** every referenced variable is documented in `.env.example`

### Requirement: Healthchecks and startup order
Every service SHALL declare a healthcheck. A service SHALL start only after the services it depends on are healthy: `api` depends on `db`; `analytics` depends on `db` and `api`; `ui` depends on `api`.

#### Scenario: Database unavailable
- **WHEN** `db` stops responding
- **THEN** the `api` healthcheck reports unhealthy for as long as the failure lasts

#### Scenario: Worker stalled
- **WHEN** the `analytics` worker process stops running its loop
- **THEN** the `analytics` healthcheck reports unhealthy

### Requirement: Automatic metadata schema migrations
On startup, `api` SHALL apply pending metadata schema migrations before reporting healthy.

#### Scenario: Empty database
- **WHEN** `api` starts against a newly created database
- **THEN** it creates the full metadata schema and then reports healthy

#### Scenario: Failed migration
- **WHEN** a migration fails
- **THEN** `api` does not report healthy, the services that depend on it do not start, and the error is written to the log

### Requirement: Network exposure
Only the `ui` service SHALL publish a port on the host. The API SHALL be reachable from outside only through `ui`, under the `/api/` path. The `analytics` and `db` services SHALL be reachable only from the internal Compose network.

#### Scenario: Published ports
- **WHEN** the deployment runs from `compose.yaml`
- **THEN** the only port published on the host belongs to `ui`

#### Scenario: Reaching the API through the UI
- **WHEN** an external client calls `<public URL>/api/v1/system/config`
- **THEN** it receives the response from `api`

### Requirement: Configurable memory limits
Every service SHALL have a memory limit configurable through an environment variable. The default limits SHALL add up to an amount that fits on a 4 GB RAM VPS while leaving at least 512 MB for the operating system.

#### Scenario: Default values
- **WHEN** the operator does not set any memory limit
- **THEN** the limits applied to the four services add up to 3.5 GB or less

#### Scenario: Custom limit
- **WHEN** the operator sets the `analytics` memory limit in `.env`
- **THEN** the `analytics` container starts with that limit

### Requirement: Data persistence
The database data and the `api` encryption keys SHALL persist in named volumes that survive container re-creation.

#### Scenario: Deployment restart
- **WHEN** the operator runs `docker compose down` and then `docker compose up`, without removing volumes
- **THEN** the users recorded earlier are still available and the encryption key ring is unchanged
