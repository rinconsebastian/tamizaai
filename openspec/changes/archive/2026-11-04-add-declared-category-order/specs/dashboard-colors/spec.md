# Spec Delta

## ADDED Requirements

### Requirement: Colors follow a declared category order
Where values with no color kept take the palette's colors in a widget's order (the series of a bar, column or line chart, the slices of a pie), that order SHALL be the one "Declared category order applied" gives, so declaring an order can give those values other colors. A value with a color kept by value SHALL keep it whatever the order, and "Other" SHALL keep the neutral color. A pyramid's sides, a bar, a column and a box SHALL keep their colors whatever their place.

#### Scenario: Colors follow the order
- **WHEN** a stacked column chart's series `Piped`, `River` and `Well` have no color kept, and its indicator declares the order `Well`, `Piped`, `River`
- **THEN** `Well` is drawn in color 1, `Piped` in color 2 and `River` in color 3

#### Scenario: Color kept whatever the order
- **WHEN** "2. Teatro" keeps color 7 for `4. No sabe` of `respuesta`, and a widget's own order puts `4. No sabe` first
- **THEN** `4. No sabe` is still drawn in color 7, and the other answers take the free colors in the new order

#### Scenario: Slices colored in their declared order
- **WHEN** a pie chart grouped by `water_source` with no color kept has its own order `River`, `Well`, `Piped`
- **THEN** `River` is drawn in color 1, `Well` in color 2 and `Piped` in color 3, under any dashboard filter

#### Scenario: Bars keep their color
- **WHEN** a bar chart with no series in color 2, which keeps color 1 for `Centro cultural o casa de la cultura` of `categoria`, follows a declared order
- **THEN** that bar is drawn in color 1 and every other bar in color 2, in their new places
