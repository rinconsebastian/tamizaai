# Spec Delta

## ADDED Requirements

### Requirement: Single-use download links
A file prepared for a signed-in user, such as a data export, SHALL be downloadable without an access token only through a link that the API issued for that file to that user after checking their access. The link SHALL hold an unguessable secret, SHALL serve its file once and SHALL expire a few minutes after it was issued. Its endpoint SHALL be declared public and SHALL serve nothing else.

#### Scenario: Link opened by the browser
- **WHEN** the browser opens a fresh download link without an access token
- **THEN** the API streams the prepared file as an attachment

#### Scenario: Guessed or reused link
- **WHEN** a request uses a link secret that the API did not issue, that was already used, or that expired
- **THEN** the API responds 404 and serves nothing

#### Scenario: Link requested without access
- **WHEN** a user who is not a member of a project asks the API to prepare an export of one of its tables
- **THEN** the API responds 404 and issues no link
