# Spec Delta

## ADDED Requirements

### Requirement: Publishing while tables are read
When a reader, such as a dashboard page or an export, is reading a step's published tables, the replacement of that schema SHALL wait for it in attempts of a few seconds, so that other readers are never held for longer than one attempt. A step that cannot replace its schema within a limited time SHALL fail with a message that the published tables were in use, and SHALL leave the previous tables published.

#### Scenario: Export running while a run publishes
- **WHEN** script 2 finishes while a member's export is still reading the output tables, and another member opens a page of the clean data dashboard
- **THEN** the page is answered within a few seconds, and the step publishes its tables once the export has read them

#### Scenario: Tables in use for too long
- **WHEN** the output tables are read without pause for longer than the step's publishing time limit
- **THEN** the step fails with a message that the published tables were in use, and the previous output tables stay published
