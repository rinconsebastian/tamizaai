# Spec Delta

## ADDED Requirements

### Requirement: Declared category order of an indicator column
An indicator's column metadata SHALL take, for any column, an `order` of at most 100 values, a `last` list of at most 20 values and `stripPrefix`, each value a text of up to 500 characters, none twice in a list nor in both lists; empty lists and an unticked `stripPrefix` are not saved. They SHALL be saved with the script's version and entered in the "Column names" section of the details, which offers the values of the latest test. A broken rule SHALL be refused with an error on that setting.

#### Scenario: Order saved
- **WHEN** an analyst saves `teatro_intencion` with the order `1. Sí`, `2. Tal vez`, `3. No` and the value `4. No sabe` kept last for `categoria`
- **THEN** the new version's metadata holds that order and that value kept last for `categoria`, and the widgets over `categoria` follow them

#### Scenario: Restored with a version
- **WHEN** an analyst saves version 3 of `comercio_lugar_compra` with an order for `categoria` and then restores version 2, which had none
- **THEN** version 4 has no order for `categoria`, and the widgets over `categoria` go back to the order they had before it

#### Scenario: Value in both lists
- **WHEN** an analyst saves an indicator whose order for `respuesta` lists `No sabe` and that also keeps `No sabe` last
- **THEN** the save is refused with an error on the values kept last of `respuesta`, and no version is made

#### Scenario: Too many values
- **WHEN** an analyst saves an indicator whose order for `categoria` lists 101 values
- **THEN** the save is refused with an error on that order naming the limit of 100

#### Scenario: Values of the latest test
- **WHEN** an analyst opens the order of `categoria` in the indicator's details after a test of the script
- **THEN** the form offers the values the test's table holds in `categoria`, and any other value can be typed

#### Scenario: Prefixes hidden without a test
- **WHEN** an analyst ticks "Hide numbered prefixes" for `respuesta` and saves, with no order and no value kept last
- **THEN** the version keeps `stripPrefix` for `respuesta`, and the widgets over `respuesta` keep their order

#### Scenario: Order of a column the latest test lacks
- **WHEN** the current version of `teatro_hoy` keeps `Sin dato` last for `comuna`, and an analyst saves the indicator's details after a test whose table has no `comuna`
- **THEN** the form lists `comuna` apart as not in the table, and the new version still keeps `Sin dato` last for it unless the analyst removed it
