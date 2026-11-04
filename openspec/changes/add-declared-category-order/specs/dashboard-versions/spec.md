# Spec Delta

## ADDED Requirements

### Requirement: Declared category order in dashboard versions
A dashboard version SHALL hold each widget's own order with the widget's other settings, so restoring the version brings it back. An order, values kept last or hidden prefixes declared for an indicator's column SHALL NOT be part of a dashboard version: viewing or restoring a version SHALL draw its widgets with the indicator's current column metadata, and changing that metadata SHALL make no dashboard version.

#### Scenario: Own order restored
- **WHEN** an `analyst` gives a widget of "2. Teatro" its own order of series, and then restores the version before that change
- **THEN** the widget has no order of its own again, and the restore is a new version

#### Scenario: Indicator order and an older version
- **WHEN** `teatro_conteo` declares an order for `respuesta` after version 18 of "2. Teatro" was made, and an `analyst` views version 18
- **THEN** its widgets over `respuesta` follow the order `teatro_conteo` declares now

#### Scenario: Indicator order makes no version
- **WHEN** an `analyst` saves `teatro_conteo` with a new order for `respuesta`
- **THEN** no dashboard gets a new version, and the dashboards over `teatro_conteo` show the new order
