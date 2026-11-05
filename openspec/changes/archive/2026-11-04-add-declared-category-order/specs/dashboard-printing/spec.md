# Spec Delta

## ADDED Requirements

### Requirement: Declared category order on paper
A printed widget SHALL show its values in the order and with the text it has on screen, as a chart and as a table.

#### Scenario: Heat map in a declared order
- **WHEN** a member prints a heat map whose rows follow a declared order and whose column values hide their prefixes
- **THEN** the sheet shows its rows in that order and its column labels without the prefixes

#### Scenario: Table in a declared order
- **WHEN** a member shows a column chart that follows a declared order as a table and prints the dashboard
- **THEN** the printed table lists the groups in that order
