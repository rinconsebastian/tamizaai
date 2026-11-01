## MODIFIED Requirements

### Requirement: Pie chart
A pie chart SHALL show its indicator's measure as one slice per group value, sized by its share of the total. Its slices' colors and which group values fold into "Other" (past the eighth color, the smallest) SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees each slice in the same color. Its colors SHALL follow `dashboard-colors`.

#### Scenario: Shares of a total
- **WHEN** a pie chart is configured for the indicator `households_visited` grouped by `municipality`
- **THEN** each municipality is one slice, sized by its share of the summed total

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a pie chart grouped by `water_source` shows `Piped` larger than `Well`, and a member sets a dashboard filter after which `Well` is larger than `Piped`
- **THEN** `Piped` and `Well` keep their colors, and the slices are sized by the filtered rows

#### Scenario: Other kept under a dashboard filter
- **WHEN** a pie chart's own rows hold 10 group values, so the 3 smallest fold into "Other", and a member sets a dashboard filter after which one of those 3 is larger than the others
- **THEN** that value is still part of "Other"

### Requirement: Series order and colors
A widget's series values SHALL be listed in natural order, then a missing series value, shown as "(none)", then "Other" when some were combined (see "Number of series"). The series a widget shows, their order and their colors SHALL come from the rows that match the widget's own filters, not the dashboard filters, so a member who changes a dashboard filter sees the same series in the same colors and legend. The colors SHALL be the dashboard's palette, with the colors kept by value of `dashboard-colors`.

#### Scenario: Natural order
- **WHEN** a stacked column chart has the series `round` with the values `10`, `2` and `1`
- **THEN** its legend and segments read `1`, `2`, `10`

#### Scenario: Colors kept under a dashboard filter
- **WHEN** a stacked column chart shows `Piped`, `Well` and `River` in three colors, and a member sets a dashboard filter after which no row has `Well`
- **THEN** `Piped` and `River` keep their colors and the legend still lists `Well`

#### Scenario: Color kept by value
- **WHEN** a stacked column chart has the series `water_source` and its dashboard keeps color 6 for `River`
- **THEN** `River`'s segments and its legend entry are in color 6
