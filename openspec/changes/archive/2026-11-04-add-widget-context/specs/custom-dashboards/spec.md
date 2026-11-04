# Spec Delta

## ADDED Requirements

### Requirement: Dashboard source
An `analyst` or `admin` SHALL be able to give a dashboard a source of 1 to 300 characters, such as the survey its data comes from, and to change or remove it, from the **Source** action of its actions menu. A source longer than 300 characters, or holding a line break or another control character, SHALL be refused; an empty source SHALL remove it. A `viewer` SHALL NOT be able to change it.

#### Scenario: Source saved
- **WHEN** an `analyst` chooses **Source** in the actions of "Teatro" and saves "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas"
- **THEN** every member who opens "Teatro" sees that source

#### Scenario: Source removed
- **WHEN** the `analyst` empties the source of "Teatro" and saves
- **THEN** "Teatro" has no source

#### Scenario: Long source
- **WHEN** a request saves a source of 301 characters
- **THEN** the API responds 400 with an error on `source` naming 300 characters, and the source is unchanged

#### Scenario: Viewer cannot change the source
- **WHEN** a `viewer` asks the API to change the source of "Teatro"
- **THEN** the API responds 403

#### Scenario: Source action offered
- **WHEN** an `analyst` opens the actions of a dashboard
- **THEN** **Source** is among them, right after **Colors**

### Requirement: Dashboard source shown
A dashboard that has a source SHALL show it to every member under its widgets, after the interface's word for it ("Source:", "Fuente:"), as its author wrote it. While a version is shown, the page SHALL show that version's source. A dashboard or a version without a source SHALL show no source line.

#### Scenario: Under the widgets
- **WHEN** a `viewer` opens "Teatro", whose source is "Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas", in the English interface
- **THEN** "Source: Encuesta de mercado del Centro Bicentenario de Duitama, 478 encuestas" is shown under its widgets

#### Scenario: Version without a source
- **WHEN** an `analyst` views version 18 of "Teatro", recorded before it had a source
- **THEN** no source line is shown

#### Scenario: No source
- **WHEN** a member opens "Escuelas", which has no source
- **THEN** no source line is shown

### Requirement: Dashboard source in a copy
Duplicating a dashboard SHALL copy its source to the copy. Changing the source of the copy or of the original afterwards SHALL NOT change the other.

#### Scenario: Source duplicated
- **WHEN** an `analyst` duplicates "Teatro", which has a source
- **THEN** "Teatro (copy)" has the same source

#### Scenario: Source of a copy is independent
- **WHEN** the `analyst` then removes the source of "Teatro (copy)"
- **THEN** "Teatro" keeps its source
