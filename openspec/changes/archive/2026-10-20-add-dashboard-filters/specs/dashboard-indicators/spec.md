# Spec Delta

## MODIFIED Requirements

### Requirement: Widget query over one indicator
A widget's query SHALL select one indicator, either a measure (a numeric column of its table, `value` by default when the table has it, otherwise its first numeric column) or a ratio of two numeric columns, a column to group by (required for bar, column, line, pie and map widgets), and up to 50 filters built the same way as the clean data dashboard's. Rows matching the query that share the same group value SHALL be summed, or combined as a ratio of sums, before being plotted. On a dashboard, the rows SHALL also match the dashboard filters that narrow the widget (see `dashboard-filters`); those filters do not count toward the widget's 50.

#### Scenario: Default measure
- **WHEN** a widget is created over an indicator whose table has the columns `municipality`, `value` and `count`, without choosing a measure
- **THEN** it plots `value`

#### Scenario: Duplicate rows summed
- **WHEN** two rows of the indicator `households_visited` share the municipality `Cali`, with values 40 and 35, and a widget sums `value` grouped by `municipality`
- **THEN** the widget plots 75 for `Cali`

#### Scenario: Row limit
- **WHEN** a widget's query matches more rows than the configured limit
- **THEN** the widget shows that the limit was reached instead of silently showing only some of the rows

#### Scenario: Dashboard filter before the sum
- **WHEN** the indicator `households_visited` has rows for `Cali` with `sex` `female` and value 40 and `sex` `male` and value 35, a widget sums `value` grouped by `municipality`, and a member sets the dashboard filter on `sex` to `female`
- **THEN** the widget plots 40 for `Cali`

#### Scenario: Widget with 50 filters on a filtered dashboard
- **WHEN** a widget has 50 filters of its own and a member sets a dashboard filter that narrows it
- **THEN** the widget shows its rows narrowed by all 51 filters

### Requirement: Stale widget configuration
A widget SHALL show a message naming what is missing, instead of failing the dashboard, in either of these cases:
- its indicator has not published;
- the published table no longer has its measure, its ratio columns, its group column, its geometry column, its sort column or one of its tooltip columns.

A dashboard filter whose column the table does not have, or has with another kind, SHALL NOT make a widget stale: the widget is shown without that filter (see `dashboard-filters`).

#### Scenario: Indicator not published
- **WHEN** a widget is configured over the indicator `attendance`, whose script has never published
- **THEN** the widget says `attendance` has not published yet instead of its chart

#### Scenario: Column removed by a script change
- **WHEN** a script change removes the column a widget grouped by
- **THEN** the widget shows a message naming that column instead of its chart

#### Scenario: Sort column removed by a script change
- **WHEN** a script change removes the column a column chart was ordered by
- **THEN** the widget shows a message naming that column instead of its chart

#### Scenario: Tooltip column removed by a script change
- **WHEN** a script change removes the column `address`, which a map widget shows in its tooltip
- **THEN** the widget shows a message naming `address` instead of its map

#### Scenario: Dashboard filter column removed by a script change
- **WHEN** a script change removes the column `municipality` from an indicator, and a member has the dashboard filter on `municipality` set to `Cali`
- **THEN** the widgets of that indicator show their charts, unfiltered by it, and say they are not filtered by that filter

### Requirement: Moving a widget to another dashboard
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep its title, chart type, query and the dashboard filters it ignores, and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

#### Scenario: Widget moved to another dashboard
- **WHEN** an `analyst` edits the widget "Coverage by municipality" on the dashboard "Health" and chooses the dashboard "Field"
- **THEN** the widget is shown on "Field", below its widgets, and no longer on "Health"

#### Scenario: Stale widget moved
- **WHEN** a script change removed the column a widget groups by, and an `analyst` moves the widget to "Field" without changing anything else
- **THEN** the widget moves and still shows a message naming the missing column

#### Scenario: Full dashboard
- **WHEN** the dashboard "Field" holds the maximum number of widgets and an `analyst` moves a widget to it
- **THEN** the API responds 400 with an error on the dashboard naming the limit, and the widget stays where it was

#### Scenario: Dashboard of another project
- **WHEN** an `analyst` asks the API to move a widget to a dashboard of another project
- **THEN** the API responds 400 with an error on the dashboard, and the widget stays where it was

#### Scenario: Target removed meanwhile
- **WHEN** an `analyst` moves a widget to "Field" just after another analyst removed "Field"
- **THEN** the form shows an error on the dashboard, the widget stays where it was, and "Field" is no longer offered in the form or the tab row

#### Scenario: New widget
- **WHEN** an `analyst` adds a widget
- **THEN** the widget form offers no dashboard choice, and the widget is added to the open dashboard

#### Scenario: Ignored filter kept by a move
- **WHEN** a widget on "Health" ignores the dashboard filter on `municipality`, and an `analyst` moves it to "Field", which also has a filter on `municipality`
- **THEN** on "Field" the widget ignores the filter on `municipality` too

## ADDED Requirements

### Requirement: Ignoring dashboard filters
When adding or editing a widget, an `analyst` or `admin` SHALL be able to choose which of the dashboard's filters the widget ignores. The choice SHALL be saved with the widget, by the filters' columns, so a filter removed and later defined again on the same column is still ignored. A widget SHALL ignore at most 8 filters. A dashboard with no filters SHALL offer no such choice.

#### Scenario: Total that does not change
- **WHEN** an `analyst` makes the stat tile "Households visited, all municipalities" ignore the filter "Municipality", and a member sets "Municipality" to `Cali`
- **THEN** the stat tile still sums every municipality and says it is not filtered by "Municipality", while the other widgets show Cali

#### Scenario: Filter defined again
- **WHEN** a widget ignores the filter on `round`, and an `analyst` removes that filter and later adds a filter labelled "Visit round" on `round`
- **THEN** the widget ignores "Visit round"

#### Scenario: No filters to ignore
- **WHEN** an `analyst` edits a widget on a dashboard with no filters
- **THEN** the widget form offers no choice of filters to ignore

#### Scenario: Viewer cannot change what a widget ignores
- **WHEN** a `viewer` asks the API to change the filters a widget ignores
- **THEN** the API responds 403 and the widget is unchanged

### Requirement: Widget preview with the dashboard's filters
The widget form's preview SHALL apply the dashboard filter values in force on the member's screen when the form opened, except the filters the widget ignores, and SHALL say which filters it applies. Changing which filters the widget ignores SHALL update the preview.

#### Scenario: Preview filtered
- **WHEN** an `analyst` has set "Municipality" to `Cali` on their screen and opens the form of a widget over an indicator with a column `municipality`
- **THEN** the preview shows Cali's rows and says it is filtered by "Municipality"

#### Scenario: Ignoring updates the preview
- **WHEN** that `analyst` then marks "Municipality" as ignored in the form
- **THEN** the preview shows every municipality
