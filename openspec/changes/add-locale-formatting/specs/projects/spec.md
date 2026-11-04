# Spec Delta

## ADDED Requirements

### Requirement: Project regional format
Each project SHALL have a regional format for the numbers of its dashboards, unset until an admin sets one. A project `admin` SHALL be able to set it to one of `es-CO`, `es-MX`, `es-ES`, `es-419`, `en-US`, `en-GB` and `pt-BR`, or to clear it, and the system SHALL reject any other value. Project details SHALL include it for every member, `null` while it is unset. Changing it SHALL change no widget, no dashboard and no dashboard version.

#### Scenario: Admin sets the regional format
- **WHEN** an admin of the Duitama project sets its regional format to `es-CO`
- **THEN** the project details give `es-CO` to every member, viewers included

#### Scenario: Unknown regional format
- **WHEN** an admin sets the regional format to `fr-FR`
- **THEN** the API rejects it with a message naming the value and the formats it accepts, and the format is unchanged

#### Scenario: Regional format by a non-admin
- **WHEN** an `analyst` tries to set the project's regional format
- **THEN** the API responds 403 and the format is unchanged

#### Scenario: Regional format cleared
- **WHEN** an admin clears the regional format of a project that had `es-CO`
- **THEN** the project details give `null`, and each member's numbers follow their own browser again

#### Scenario: Existing project upgraded
- **WHEN** a deployment is upgraded to this version
- **THEN** every existing project has no regional format, and every member sees the numbers they saw before the upgrade

### Requirement: Project regional format in Settings
Settings > General SHALL offer an admin the project's regional format as a choice: "Each member's browser" first, then each format with its name and an example of a number and a percentage written in it. Its hint SHALL say that the project's dashboards, emails, exports and AI clients write numbers in it, and that dates and the interface language stay each member's. An `analyst` SHALL see the format as a value, not as a choice.

#### Scenario: Formats offered with an example
- **WHEN** an admin opens Settings > General of a project with no regional format
- **THEN** the choice reads "Each member's browser", the option for Spanish (Colombia) shows `12.345,6` and `46,9 %`, and the option for Portuguese (Brazil) shows `12.345,6` and `46,9%`

#### Scenario: Analyst sees the value
- **WHEN** an `analyst` opens Settings > General of a project whose regional format is `es-CO`
- **THEN** it reads Spanish (Colombia) as a value, with no control to change it
