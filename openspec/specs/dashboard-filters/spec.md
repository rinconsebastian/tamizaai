# dashboard-filters Specification

## Purpose
Let the analysts of a project define filters on each of its dashboards, and let every member narrow all the widgets of a dashboard at once by changing those filters on their own screen, without changing the widgets for anyone else.

## Requirements

### Requirement: Dashboard filters
A dashboard SHALL have a list of 0 to 8 filters, in an order, which an `analyst` or `admin` can add, change, reorder and remove. Each filter SHALL have a label, a column name, a kind (text, true/false, number or date) and an optional default. A dashboard SHALL have at most one filter per column. A new dashboard SHALL have none. The filters SHALL be the same for every member who opens the dashboard.

#### Scenario: Analyst adds a filter
- **WHEN** an `analyst` adds to the dashboard "Health" a text filter labelled "Municipality" on the column `municipality`
- **THEN** every member who opens "Health" afterwards sees the filter "Municipality", and no other dashboard has it

#### Scenario: Ninth filter
- **WHEN** a dashboard has 8 filters and an `analyst` asks the API to save 9
- **THEN** the API responds 400 with an error on the filters saying a dashboard can have at most 8, and the dashboard keeps its 8

#### Scenario: Two filters on one column
- **WHEN** an `analyst` asks the API to save two filters on the column `municipality` of one dashboard
- **THEN** the API responds 400 with an error on the second filter saying the column already has a filter, and nothing is saved

#### Scenario: Filters reordered
- **WHEN** an `analyst` moves the filter "Round" before the filter "Municipality" and saves
- **THEN** every member who opens the dashboard afterwards sees "Round" first

#### Scenario: New dashboard
- **WHEN** an `analyst` adds a dashboard
- **THEN** it has no filters

### Requirement: Filter labels and columns
A filter's label SHALL be 1 to 40 characters once leading and trailing spaces are removed, with no control characters, and two filters of a dashboard SHALL NOT have labels that differ only in case. A label SHALL be shown exactly as saved, never translated. A filter's column SHALL be named as scripts name published columns: lowercase letters, digits and underscores, not starting with a digit, at most 63 characters.

#### Scenario: Label trimmed
- **WHEN** an `analyst` labels a filter "  Municipio  "
- **THEN** it is saved and shown as "Municipio"

#### Scenario: Label taken
- **WHEN** a dashboard has a filter labelled "Round" and an `analyst` labels another filter of it "ROUND"
- **THEN** the API responds 400 with an error on that filter's label saying another filter of the dashboard has it

#### Scenario: Column name not allowed
- **WHEN** an `analyst` asks the API to save a filter on the column `Municipality`
- **THEN** the API responds 400 with an error on that filter's column

#### Scenario: Label shown as typed
- **WHEN** an `analyst` labels a filter "<b>Zona</b>"
- **THEN** the filter bar, the widget notes and every message that names it show those characters as text, and nothing is rendered as markup

### Requirement: Filter kinds
A text or true/false filter SHALL take a set of up to 100 values, and SHALL keep the rows whose column holds one of them. A number filter SHALL take a range with an optional minimum and maximum, both included. A date filter SHALL take a range of days in the project's time zone, including the whole last day. A value that cannot be read as the filter's kind, or a range whose minimum is greater than its maximum, SHALL be rejected with an error naming the filter.

#### Scenario: Values of a text filter
- **WHEN** a member sets the filter "Municipality" to `Cali` and `Palmira`
- **THEN** each widget it narrows shows only the rows of Cali or Palmira

#### Scenario: Date range by day
- **WHEN** the project's time zone is `America/Bogota` and a member sets a date filter on `visit_date` from 2026-09-01 to 2026-09-01
- **THEN** the widgets it narrows keep the rows of 2026-09-01 in Bogotá, including a timestamp at 23:30 local time

#### Scenario: Number range with one end
- **WHEN** a member sets the number filter "Round" from 2 without a maximum
- **THEN** the widgets it narrows keep the rows whose `round` is 2 or more

#### Scenario: Value of the wrong kind
- **WHEN** a request applies the number filter "Round" with the minimum `second`
- **THEN** the API rejects it with a validation error naming the filter's column

#### Scenario: Reversed range
- **WHEN** a request applies a date filter from 2026-09-30 to 2026-09-01
- **THEN** the API rejects it with a validation error naming the filter's column

### Requirement: Filter defaults
A filter's default SHALL be a value of its kind, as above, or none. When a member opens a dashboard whose address carries no filter values, every filter SHALL start at its default. A default SHALL be validated like any value of its kind when the filters are saved.

#### Scenario: Default in force
- **WHEN** an `analyst` gives the filter "Round" the default range 3 to 3, and a viewer opens the dashboard
- **THEN** the filter bar shows "Round" set to 3, and the widgets it narrows show round 3 only

#### Scenario: Invalid default
- **WHEN** an `analyst` saves a number filter whose default minimum is `three`
- **THEN** the API responds 400 with an error on that filter's default, and nothing is saved

### Requirement: Columns offered for a filter
When an `analyst` or `admin` adds a filter or changes its column, the form SHALL offer the columns of the published tables of the dashboard's widgets' indicators, each with its kind and the number of the dashboard's widgets it would narrow. Integer and decimal columns SHALL be offered as number, and date and timestamp columns as date. A column whose kind differs between tables SHALL be offered once per kind.

#### Scenario: Columns of several indicators
- **WHEN** a dashboard has 5 widgets, 4 over indicators whose tables have a text column `municipality` and 1 over an indicator without it, and an `analyst` adds a filter
- **THEN** `municipality` is offered as text, narrowing 4 of 5 widgets

#### Scenario: Integer and decimal together
- **WHEN** the indicator `coverage` publishes `round` as an integer and the indicator `attendance` publishes it as a decimal, and both have widgets on the dashboard
- **THEN** `round` is offered once, as number, narrowing both widgets

#### Scenario: No published indicator
- **WHEN** none of the dashboard's widgets' indicators has published
- **THEN** the form says there are no columns to filter on yet and offers no column

### Requirement: Filter bar
A dashboard with filters SHALL show, between its header and its widgets, a filter bar listing its filters in order, each with its label and the value in force, or "All" when it has none. Every member, viewers included, SHALL see it. A dashboard with no filters SHALL show no filter bar. From 360 pixels wide, every filter, the action to clear the filters and the action to go back to the defaults SHALL be reachable without horizontal page scrolling.

#### Scenario: Viewer sees the filter bar
- **WHEN** a `viewer` opens a dashboard with the filters "Municipality" and "Round", neither with a default
- **THEN** the filter bar shows "Municipality: All" and "Round: All" above the widgets

#### Scenario: No filters
- **WHEN** a member opens a dashboard with no filters
- **THEN** no filter bar is shown

#### Scenario: Filter bar at phone width
- **WHEN** a member opens a dashboard with 8 filters, each with a 40-character label and a value, in a viewport 360 pixels wide
- **THEN** every filter and its actions are reachable and the page does not scroll horizontally

### Requirement: Changing filter values
Every member, viewers included, SHALL be able to set, change and clear the value of each filter, clear all the filters at once, and put every filter back to its default. A change SHALL apply at once to every widget the filter narrows, SHALL change only that member's screen, SHALL NOT be saved, and SHALL be announced to screen readers without moving focus.

#### Scenario: Viewer filters a dashboard
- **WHEN** a `viewer` sets "Municipality" to `Cali` while another member has the same dashboard open
- **THEN** the viewer's widgets show Cali only, the other member's widgets are unchanged, and nothing about the dashboard or its widgets is saved

#### Scenario: Filter cleared
- **WHEN** a member clears the filter "Municipality"
- **THEN** it reads "All" and the widgets show every municipality again

#### Scenario: Back to the defaults
- **WHEN** a member has changed "Round", whose default is 3, to 1 and chooses to go back to the defaults
- **THEN** "Round" is 3 again and the widgets show round 3

#### Scenario: Change announced
- **WHEN** a keyboard user sets "Municipality" to `Cali`
- **THEN** a screen reader announces that the dashboard is filtered, and focus returns to the "Municipality" filter

### Requirement: Values offered by a text filter
Setting a text filter SHALL offer the 50 most frequent values of its column across the published tables of the dashboard's widgets' indicators that have it as text, most frequent first, optionally restricted to values that contain a search text, ignoring case. Values longer than 500 characters SHALL NOT be offered. A true/false filter SHALL offer true and false.

#### Scenario: Values of two indicators
- **WHEN** the dashboard's widgets show the indicators `coverage`, whose `municipality` holds `Cali` and `Buga`, and `attendance`, whose `municipality` holds `Cali` and `Palmira`
- **THEN** the filter "Municipality" offers `Cali`, `Buga` and `Palmira`, each once

#### Scenario: Search among values
- **WHEN** the column `municipality` has more than 50 values across those tables and a member searches `pal`
- **THEN** the list shows the most frequent values that contain `pal`, such as `Palmira`

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests the values of one of its dashboards' columns
- **THEN** the API responds 404

### Requirement: Shareable filtered view
The values in force on a member's dashboard SHALL be kept in the page address. Opening that address SHALL show the dashboard with the same values to any member. A value in the address for a filter the dashboard no longer has, or that cannot be read as its filter's kind, SHALL be dropped with a notice, announced to screen readers, instead of failing the page. Opening another dashboard SHALL NOT carry the values over.

#### Scenario: Shared link
- **WHEN** an analyst sends a viewer the address of the dashboard "Health" filtered to `Cali`
- **THEN** the viewer opens "Health" filtered to `Cali`

#### Scenario: Filter removed since
- **WHEN** a member opens an address that sets the filter on `round`, and the dashboard no longer has a filter on `round`
- **THEN** the dashboard opens with its other values and a notice says the filter was removed

#### Scenario: Cleared filter in a link
- **WHEN** a member clears "Round", whose default is 3, and shares the address
- **THEN** the member who opens it sees "Round: All", not the default

#### Scenario: Another dashboard
- **WHEN** a member filtered "Health" to `Cali` and opens "Field"
- **THEN** "Field" opens with its own defaults

### Requirement: Widgets a filter narrows
A filter with a value in force SHALL narrow a widget when the published table of the widget's indicator has a column with the filter's name and kind, and the widget does not ignore it. Integer and decimal columns SHALL count as number, and date and timestamp columns as date. When the widget has its own filter on the same column, a row SHALL match both. The filters SHALL narrow the rows before they are grouped, summed and limited.

#### Scenario: Same column on several widgets
- **WHEN** a member sets "Municipality" to `Cali` on a dashboard whose column chart and stat tile both read indicators with a text column `municipality`
- **THEN** the column chart shows only Cali's column, and the stat tile sums Cali's rows only

#### Scenario: Integer and decimal columns
- **WHEN** a member sets the number filter "Round" from 2 to 2, the indicator `coverage` publishes `round` as an integer and `attendance` publishes it as a decimal
- **THEN** the widgets of both indicators keep the rows of round 2

#### Scenario: Widget filter and dashboard filter on one column
- **WHEN** a widget's own filter keeps `municipality` in `Cali` and `Buga`, and a member sets the dashboard filter "Municipality" to `Cali` and `Palmira`
- **THEN** the widget shows Cali's rows only

#### Scenario: Map tooltip narrowed
- **WHEN** a member sets "Round" to 2 on a dashboard with a map whose tooltip sums `sample_target`
- **THEN** the map's shapes and its tooltip sums count the rows of round 2 only

### Requirement: Widgets a filter does not narrow
A widget SHALL show, as text, which filters with a value in force do not narrow it, by their labels, whether its table has no column of that name and kind or the widget ignores the filter. The widget SHALL still show its rows, unfiltered by those filters; this SHALL NOT be shown as an error or as a missing column. A filter with no value in force SHALL NOT be listed.

#### Scenario: Indicator without the column
- **WHEN** a member sets "Municipality" to `Cali` and a widget's indicator has no column `municipality`
- **THEN** the widget shows all its rows and says it is not filtered by "Municipality"

#### Scenario: Column of another kind
- **WHEN** a member sets the number filter "Round" from 1 to 2 and a widget's indicator publishes `round` as text
- **THEN** the widget shows all its rows and says it is not filtered by "Round"

#### Scenario: Ignored filter
- **WHEN** a member sets "Municipality" to `Cali` and a widget ignores "Municipality"
- **THEN** the widget shows every municipality and says it is not filtered by "Municipality"

#### Scenario: Filter without a value
- **WHEN** no value is set for "Municipality" and a widget's indicator has no column `municipality`
- **THEN** the widget says nothing about "Municipality"

### Requirement: Editing a dashboard's filters
An `analyst` or `admin` SHALL edit a dashboard's filters from the dashboard's actions, in a form that saves the whole list at once. After saving, the dashboard SHALL show the saved filters at their defaults, and focus SHALL return to the dashboard's actions button; cancelling SHALL change nothing and return focus there too. A save on a dashboard another member has removed SHALL be refused as for any change to a removed dashboard. Otherwise the last save SHALL win.

#### Scenario: Filters saved
- **WHEN** an `analyst` adds the filter "Round" with the default 3 to a dashboard filtered on their screen by "Municipality" `Cali`, and saves
- **THEN** the dashboard shows "Municipality: All" and "Round: 3", and focus is on the dashboard's actions button

#### Scenario: Filters saved on a removed dashboard
- **WHEN** one `analyst` removes "Health" while another saves filters on it
- **THEN** nothing is saved, and the second analyst is told "Health" no longer exists and sees the project's first dashboard

#### Scenario: Two saves
- **WHEN** two analysts save different filters on the same dashboard, one after the other
- **THEN** the dashboard has the filters of the later save

#### Scenario: Viewer cannot edit the filters
- **WHEN** a `viewer` opens a dashboard with filters
- **THEN** no action to edit the filters is shown, and a request to the API to save them is answered 403
