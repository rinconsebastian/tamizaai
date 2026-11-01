## MODIFIED Requirements

### Requirement: Colors on paper
Chart marks, map shapes and legend swatches SHALL print in their colors, the dashboard's palette, the widgets' colors and scales and the colors kept by value included, even when the browser's option to print background graphics is off. The sheet's background SHALL be white, and widgets SHALL have a thin border and no shadow.

#### Scenario: Background graphics off
- **WHEN** a member prints a map in a browser whose option to print background graphics is off
- **THEN** the map's legend swatches print in their colors

#### Scenario: Palette printed
- **WHEN** a member prints "Housing", which has the palette *Earth* and a map on the green scale
- **THEN** the charts print in the colors of *Earth* and the map on the green scale
