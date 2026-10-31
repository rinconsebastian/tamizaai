# Spec Delta

## MODIFIED Requirements

### Requirement: Network exposure
Only the `ui` service SHALL publish a port on the host. The API SHALL be reachable from outside only through `ui`, under the `/api/` path and, for the MCP server, at `/mcp` and under `/.well-known/oauth-protected-resource`. The `analytics` and `db` services SHALL be reachable only from the internal Compose network.

#### Scenario: Published ports
- **WHEN** the deployment runs from `compose.yaml`
- **THEN** the only port published on the host belongs to `ui`

#### Scenario: Reaching the API through the UI
- **WHEN** an external client calls `<public URL>/api/v1/system/config`
- **THEN** it receives the response from `api`

#### Scenario: Reaching the MCP server through the UI
- **WHEN** the MCP server is enabled and an AI client calls `<public URL>/mcp` or `<public URL>/.well-known/oauth-protected-resource/mcp`
- **THEN** it receives the response from `api`, and a call that waits up to 120 seconds for a test is not cut by the proxy
