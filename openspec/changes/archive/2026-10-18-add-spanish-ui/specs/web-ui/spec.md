# Spec Delta

## MODIFIED Requirements

### Requirement: Feedback on failed actions
When an action fails, the UI SHALL show a message that explains why, using the error the API returned. Validation errors for a form field SHALL appear next to that field. The UI SHALL NOT fail silently. A message the API wrote SHALL be shown as received. A message the UI writes itself, such as the one for an API error code it knows or for a server it cannot reach, SHALL be in the interface language (see `interface-language`).

#### Scenario: Field validation error
- **WHEN** the API rejects a form because one field is invalid
- **THEN** the message for that field appears next to it and the rest of the input is kept

#### Scenario: Request fails
- **WHEN** the API responds with an error that is not tied to a field, or cannot be reached
- **THEN** the UI shows a message describing the problem

#### Scenario: Server unreachable in Spanish
- **WHEN** a member with the Spanish UI saves a form while the API cannot be reached
- **THEN** the UI says in Spanish that it could not reach the server

#### Scenario: API message in the Spanish UI
- **WHEN** a member with the Spanish UI acts on a widget that was just deleted and the API answers "Widget not found." without a code
- **THEN** the UI shows "Widget not found."

### Requirement: Date and time display
Every date and time the UI shows SHALL be written to the minute, without seconds, in one format shared by every screen. The format SHALL name the month, and SHALL follow the order, clock and words of the interface's regional format (see `interface-language`).

#### Scenario: Synchronization time
- **WHEN** a synchronization finished at 22:00:01 on 2 October 2026
- **THEN** the log shows it as the date and 22:00 (or 10:00 PM), without seconds

#### Scenario: American English
- **WHEN** the browser entry that chose English is `en-US` and the UI shows 21:30 on 1 September 2026
- **THEN** it reads `Sep 1, 2026, 9:30 PM`

#### Scenario: British English
- **WHEN** the browser entry that chose English is `en-GB` and the UI shows the same moment
- **THEN** it reads `1 Sept 2026, 21:30`

#### Scenario: Colombian Spanish
- **WHEN** the browser entry that chose Spanish is `es-CO` and the UI shows the same moment
- **THEN** it reads `1 de sept de 2026, 9:30 p. m.`

#### Scenario: Spanish from Spain
- **WHEN** the browser entry that chose Spanish is `es-ES` and the UI shows the same moment
- **THEN** it reads `1 sept 2026, 21:30`
