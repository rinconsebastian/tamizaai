## MODIFIED Requirements

### Requirement: Same rules as the UI
A tool that changes something SHALL apply the same validation, limits and checks as the REST endpoint for the same operation, and SHALL report a refused value with the field and the message that endpoint gives. Two members, or a member and their own AI client, changing the same thing at once SHALL be handled as for two members in the UI.

#### Scenario: Dashboard limit
- **WHEN** Claude calls `create_dashboard` on a project that has 10 dashboards
- **THEN** the tool is refused with the code `dashboards.limit` and nothing is added

#### Scenario: Dashboard limit described
- **WHEN** an AI client lists the tools of the server
- **THEN** the descriptions of `list_dashboards`, `create_dashboard` and `duplicate_dashboard` say a project has at most 10 dashboards

#### Scenario: Group column required
- **WHEN** Claude calls `create_widget` for a bar chart without a column to group by
- **THEN** the tool is refused naming the field `config.groupBy` with the endpoint's message
