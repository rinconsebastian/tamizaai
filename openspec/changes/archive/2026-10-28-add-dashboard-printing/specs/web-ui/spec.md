# Spec Delta

## MODIFIED Requirements

### Requirement: Feedback while an action runs
While an action the user started takes longer than a moment (preparing an export, a download, a test), the control that started it SHALL show that it is busy and SHALL NOT be usable again until the action ends; other controls of the same kind SHALL stay usable. When the action ends, the UI SHALL show exactly one message, a success or a failure, except for an action that ends by opening the browser's own dialog, such as printing: that dialog SHALL stand in for the message, and the UI SHALL show none. A notice the UI inserts into the page after an action, such as a dropped filter or republished tables, SHALL be announced to screen readers.

#### Scenario: Export of one indicator
- **WHEN** a member exports the table of the indicator `coverage` from a list of three indicators
- **THEN** only the export control of `coverage` shows the busy state, the other two stay usable, and one message appears when the file is ready

#### Scenario: Filter dropped after a run
- **WHEN** a run removes the column a member filtered by and the next page loads
- **THEN** the notice that the filter was removed is announced to screen readers without stealing focus

#### Scenario: Printing ends in the browser's dialog
- **WHEN** a member prints a dashboard and then prints or cancels in the browser's print dialog
- **THEN** the Print action shows the busy state until the dialog opens, and no success or failure message appears afterwards

## ADDED Requirements

### Requirement: Navigation left out of printouts
Printing any screen SHALL leave out the top bar, the project's back link, role badge and tabs, the secondary navigation of the configuration sections, toasts and open menus, so the sheets start with the screen's content. The sheets SHALL have a white background.

#### Scenario: Performance dashboard printed from the browser's menu
- **WHEN** a member prints the Performance dashboard with Ctrl+P
- **THEN** the sheets show the project's name and the dashboard's content, without the top bar, the tabs or the user's name
