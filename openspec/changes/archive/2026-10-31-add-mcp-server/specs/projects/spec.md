# Spec Delta

## ADDED Requirements

### Requirement: Access for AI clients
Each project SHALL have an access level for AI clients connected through the MCP server (see `mcp-server`): `off`, `aggregates` (*Summaries only*) or `rows` (*Summaries and rows*). It SHALL be `off` for every new project and for every project that existed before the setting. Only a project `admin` SHALL be able to change it, and the system SHALL reject `rows` while the deployment's maximum is `aggregates`, with a message saying that the operator decides it. Project details SHALL include the level and the deployment's maximum for admins and analysts, and leave them out for a `viewer`, giving each as `null`. When the MCP server is enabled, Settings > General SHALL show the level, with what each level lets a client see, as a choice for admins and as a value for analysts.

#### Scenario: New project
- **WHEN** a user creates a project in a deployment with the MCP server enabled
- **THEN** the project's access for AI clients is off

#### Scenario: Existing project upgraded
- **WHEN** a deployment is upgraded to this version
- **THEN** every existing project's access for AI clients is off

#### Scenario: Admin allows summaries
- **WHEN** an admin chooses *Summaries only* in Settings > General
- **THEN** the project details give `aggregates`, and the project's members can use it from their AI clients

#### Scenario: Rows above the deployment's maximum
- **WHEN** an admin asks for `rows` while the deployment's maximum is `aggregates`
- **THEN** the API rejects it with a message saying that the operator allows rows, and the level is unchanged

#### Scenario: Analyst tries to change it
- **WHEN** an `analyst` tries to set the project's access for AI clients
- **THEN** the API responds 403 and the level is unchanged

#### Scenario: Viewer details
- **WHEN** a `viewer` requests the project details
- **THEN** the access for AI clients and the deployment's maximum are `null`
