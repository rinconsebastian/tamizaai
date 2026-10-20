# Spec Delta

## MODIFIED Requirements

### Requirement: Who can use the assistant
Only analysts and admins of the project SHALL be able to use the assistant, and only on the text being edited. A `viewer` SHALL be refused by its API routes; a user who is not a member SHALL get the answer given for a project that does not exist. While an older version is on view, the tab SHALL say that the assistant works on the current text and offer no new request.

#### Scenario: Analyst
- **WHEN** an analyst opens the Scripts section of a deployment with the assistant enabled
- **THEN** the side panel has a fourth tab, Assistant, after Tables, Reference and Versions

#### Scenario: Viewer
- **WHEN** a `viewer` calls the assistant API of the project
- **THEN** the API answers 403

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project calls its assistant API
- **THEN** the API answers 404

#### Scenario: Older version on view
- **WHEN** an analyst views version 3 of a script whose current version is 5 and opens the Assistant tab
- **THEN** the tab says that the assistant works on the current text and offers no new request until the analyst returns to it, while a request already being written keeps its waiting state and Cancel
