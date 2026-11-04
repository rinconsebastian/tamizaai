# Spec Delta

## ADDED Requirements

### Requirement: Highlighted values and the neutral color on paper
A printed widget SHALL show its highlighted values, the neutral color of the values the highlight leaves out, the neutral color kept by value and the bold names of highlighted values as the screen shows them, even when the browser's option to print background graphics is off.

#### Scenario: Highlight printed
- **WHEN** a member prints "2. Teatro", whose bar chart "2.4. En qué lugares" highlights `Centro cultural o casa de la cultura`, in a browser whose option to print background graphics is off
- **THEN** that bar prints in color 1 of *Earth* with its label in bold, and the other bars print in the neutral color

#### Scenario: Neutral kept printed
- **WHEN** a member prints a 100 % column chart whose dashboard keeps the neutral color for `4. No sabe` of its series column
- **THEN** the `4. No sabe` segments and legend swatch print in the neutral color
