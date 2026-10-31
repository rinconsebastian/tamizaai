# Spec Delta

## ADDED Requirements

### Requirement: Versions saved through MCP
The Versions tab SHALL mark each version created through the MCP server (a save, a removal or a restore made by an AI client) with "via MCP" next to its author and time. A version created in the editor, including the restore of a version saved through MCP, SHALL carry no mark.

#### Scenario: Version written by Claude
- **WHEN** an analyst's Claude saves version 3 of the indicator `coverage` and the analyst opens its Versions tab
- **THEN** version 3 shows the analyst as its author and "via MCP", and versions 1 and 2 show no mark

#### Scenario: Restored in the editor
- **WHEN** the analyst restores version 3 in the editor
- **THEN** the new version 4 shows no mark
