# Spec Delta

## Purpose

Shared behavior of the web interface across all screens: it works from phone width to desktop, offers consistent navigation, and tells users what happened when an action fails.

## ADDED Requirements

### Requirement: Responsive layout
Every screen SHALL be usable at viewport widths from 360 CSS pixels to desktop sizes without horizontal scrolling of the page. Content SHALL reflow to the available width. Tables wider than the screen SHALL scroll horizontally inside their own container, without the page scrolling.

#### Scenario: Phone width
- **WHEN** any screen is shown in a viewport 360 pixels wide
- **THEN** all its content and controls are reachable without horizontal page scrolling

#### Scenario: Wide table on a phone
- **WHEN** a table with more columns than fit on screen is shown in a narrow viewport
- **THEN** the table scrolls horizontally inside its container while the rest of the page stays in place

#### Scenario: Desktop width
- **WHEN** a screen is shown in a viewport 1280 pixels wide or more
- **THEN** the content uses the extra width, for example placing forms and lists side by side, instead of a single phone-width column

### Requirement: Navigation shell
Every signed-in screen SHALL show a top bar with the product name, the navigation (Projects), the user's name and the sign-out action. On narrow screens the navigation and user actions SHALL collapse into a menu button. The menu SHALL be operable with the keyboard.

#### Scenario: Narrow screen
- **WHEN** a signed-in screen is shown in a viewport narrower than 768 pixels
- **THEN** the top bar shows the product name and a menu button, and opening the menu reveals the navigation, the user's name and sign-out

#### Scenario: Keyboard use
- **WHEN** a keyboard user focuses the menu button and presses Enter, then Escape
- **THEN** the menu opens on Enter and closes on Escape, returning focus to the button

### Requirement: Feedback on failed actions
When an action fails, the UI SHALL show a message that explains why, using the error the API returned. Validation errors for a form field SHALL appear next to that field. The UI SHALL NOT fail silently.

#### Scenario: Field validation error
- **WHEN** the API rejects a form because one field is invalid
- **THEN** the message for that field appears next to it and the rest of the input is kept

#### Scenario: Request fails
- **WHEN** the API responds with an error that is not tied to a field, or cannot be reached
- **THEN** the UI shows a message describing the problem
