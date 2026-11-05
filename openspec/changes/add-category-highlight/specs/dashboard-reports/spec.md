# Spec Delta

## ADDED Requirements

### Requirement: Highlighted values in a scheduled email
The body and the workbook of a scheduled email SHALL show a widget that highlights values, and a widget whose dashboard keeps the neutral color, with the same columns, rows, order and numbers as without them, and with no color or mark for them, since the email shows tables and not charts.

#### Scenario: Highlighted bar chart sent
- **WHEN** the scheduled email of "2. Teatro" is sent while its bar chart "2.4. En qué lugares" highlights `Centro cultural o casa de la cultura`
- **THEN** the widget's table in the body and in the workbook lists every place with its value, as it did before the highlight was set
