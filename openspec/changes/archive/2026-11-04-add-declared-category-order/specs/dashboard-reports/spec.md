# Spec Delta

## ADDED Requirements

### Requirement: Declared category order in a scheduled email
A widget's table in a scheduled email's body and in its workbook SHALL list its groups and series in the order of "Declared category order applied", and SHALL write their values with the prefixes hidden as "Numbered category prefixes hidden" says, as the widget's "Show as table" view does. A widget that declares nothing SHALL have the table it had before declared orders existed.

#### Scenario: Answers in order without their numbers
- **WHEN** a scheduled email holds the 100% bar chart "2.8. Iría al teatro, por comuna", whose indicator hides the prefixes of `respuesta`
- **THEN** its table's series columns read `Sí`, `Tal vez`, `No`, `No sabe`, in that order, in the body and in the workbook

#### Scenario: Value kept last
- **WHEN** a scheduled email holds a column chart grouped by `comuna` whose indicator keeps `Sin dato` last
- **THEN** its table lists `Sin dato` after `SIN NOMBRE`, in the body and in the workbook

#### Scenario: Widget's own order
- **WHEN** a scheduled email holds a pie chart with its own order of groups
- **THEN** its table lists the group values in that order

#### Scenario: Nothing declared
- **WHEN** a scheduled email holds widgets that declare no order, no values kept last and no hidden prefixes
- **THEN** their tables list the same rows, in the same order and with the same text, as before declared orders existed
