## MODIFIED Requirements

### Requirement: Columns available to a widget
A widget's query SHALL be able to name any column of its indicator's published table, discovered the same way the clean data dashboard discovers a table's columns. It SHALL be offered the table's numeric columns as measures, every column as a group column, for a bar or column widget every column as a sort column, and for a map widget every column except its geometry column as a tooltip column.

#### Scenario: Extra column grouped on
- **WHEN** the indicator `attendance` publishes a table with a `round` column and an analyst configures a widget to group by it
- **THEN** the widget's rows are grouped by `round`

#### Scenario: Extra column sorted on
- **WHEN** the indicator `coverage` publishes a table with a `sort_key` column and an analyst configures the order of a column chart over it
- **THEN** `sort_key` is offered as a sort column

#### Scenario: Extra column shown in a tooltip
- **WHEN** the indicator `survey_progress` publishes a table with the columns `block_code`, `address`, `sample_target`, `achieved`, `value` and `geometry`, and an analyst configures a map over it with `geometry` as its geometry column
- **THEN** `block_code`, `address`, `sample_target`, `achieved` and `value` are offered as tooltip columns, and `geometry` is not

### Requirement: Stale widget configuration
A widget SHALL show a message naming what is missing, instead of failing the dashboard, in either of these cases:
- its indicator has not published;
- the published table no longer has its measure, its ratio columns, its group column, its geometry column, its sort column or one of its tooltip columns.

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

### Requirement: Indicator formatting
A widget SHALL show its plotted values with the format and unit of its indicator: `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value. The same SHALL apply to a stat tile, axes, legends, tooltips and "Show as table"; a map's tooltip columns use their own format.

#### Scenario: Rate shown as a percentage
- **WHEN** the indicator `piped_water_rate` has the format `percent` and a stat tile shows the value 0.42
- **THEN** the tile shows `42%`

#### Scenario: Count with a unit
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`, and a column chart shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households`

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over the indicator `survey_progress`, whose format is `percent`, shows the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** the tooltip reads the plotted value as a percentage and the target as `20`, not `2,000%`

## ADDED Requirements

### Requirement: Map shapes at their geographic position
When every coordinate of a map's shapes is a longitude from -180 to 180 and a latitude from -90 to 90, the map SHALL treat them as degrees and draw the shapes at their geographic position on a Web Mercator map, north up. Otherwise it SHALL draw them in their relative positions, with no basemap. A map SHALL open fitted to the extent of its shapes.

#### Scenario: Municipalities in degrees
- **WHEN** a map's geometry column holds municipality polygons in longitude and latitude
- **THEN** the polygons are drawn at their geographic position, and the map opens showing all of them

#### Scenario: Coordinates that are not degrees
- **WHEN** a map's geometry column holds polygons in metres (EPSG:3116), and the widget has the basemap Streets
- **THEN** the polygons are drawn in their relative positions, no basemap is drawn, and the map says the basemap cannot be shown because the coordinates are not degrees

#### Scenario: A single point
- **WHEN** a map's only drawable row is one point
- **THEN** the map opens centred on that point, close enough to see the streets around it when a basemap is shown

### Requirement: Map zoom and pan
A member SHALL be able to zoom and pan every map, with or without a basemap and without an internet connection: with zoom-in, zoom-out and "Show all" buttons, by dragging and double-clicking with a mouse, with the wheel while holding Ctrl (⌘ on macOS), with two fingers on a touch screen, and from the keyboard. "Show all" SHALL return to the fitted view. A map SHALL NOT zoom out more than three levels beyond its fitted view.

#### Scenario: Zoom with the buttons
- **WHEN** a viewer presses zoom-in twice on a map of city blocks
- **THEN** the map shows a smaller area around the same centre, larger, and "Show all" brings back every block

#### Scenario: Offline zoom
- **WHEN** a deployment offers no basemap and has no internet access, and a member zooms into a map
- **THEN** the shapes are zoomed and no request leaves the deployment

#### Scenario: Zoom-out limit
- **WHEN** a member keeps zooming out of a map of one municipality
- **THEN** the map stops three levels beyond the view that fits the municipality

### Requirement: Map gestures do not trap the page
Scrolling over a map SHALL keep scrolling the page: a mouse wheel without Ctrl or ⌘ and a one-finger swipe on a touch screen SHALL NOT zoom or move the map. The map SHALL then show, for a moment, how to zoom or move it.

#### Scenario: Wheel without Ctrl
- **WHEN** a member scrolls the dashboard with the mouse wheel while the pointer is over a map
- **THEN** the page scrolls, the map keeps its zoom, and the map shows "Hold Ctrl and scroll to zoom" ("⌘" on macOS)

#### Scenario: Wheel with Ctrl
- **WHEN** a member holds Ctrl and turns the wheel over a map
- **THEN** the map zooms around the pointer and the page does not scroll

#### Scenario: One finger on a phone
- **WHEN** a member swipes with one finger over a map on a phone
- **THEN** the page scrolls, and the map shows "Use two fingers to move the map"

#### Scenario: Two fingers on a phone
- **WHEN** a member pinches with two fingers over a map on a phone
- **THEN** the map zooms

### Requirement: Map keyboard access
A map SHALL be reachable with Tab. While it has focus, the arrow keys SHALL pan it, `+` and `-` SHALL zoom it, and `0` or Home SHALL show all. Enter SHALL move into its shapes. There, the arrow keys move from shape to shape in the order of its "Show as table" view, showing each one's tooltip, and Escape returns to the map. Its shapes SHALL NOT be stops of their own in the page's Tab order.

#### Scenario: Stepping through blocks
- **WHEN** a keyboard user tabs to a map of 300 blocks, presses Enter and then the right arrow twice
- **THEN** the third block in the table order is highlighted and brought into view, its tooltip is shown, and a screen reader reads the tooltip's text

#### Scenario: One Tab stop
- **WHEN** a keyboard user presses Tab on a map that has focus
- **THEN** focus leaves the map for the next control, without visiting its shapes

#### Scenario: Escape in the widget form
- **WHEN** a keyboard user stepping through the shapes of the map preview in the widget form presses Escape
- **THEN** focus returns to the map and the form stays open; a second Escape closes the form

### Requirement: Map view is not saved
A map's zoom and position SHALL NOT be saved. They SHALL NOT change the widget for other members, SHALL NOT query the indicator again, and SHALL return to the fitted view when the widget's rows change or the page is opened again. Switching the widget to "Show as table" and back SHALL keep them.

#### Scenario: Zoom stays on the viewer's screen
- **WHEN** an analyst zooms into one neighbourhood of a map and another member opens the dashboard
- **THEN** the other member sees the map fitted to all its shapes

#### Scenario: Table and back
- **WHEN** a member zooms a map, switches it to "Show as table" and back
- **THEN** the map shows the same zoom and position

#### Scenario: No new query
- **WHEN** a member pans and zooms a map
- **THEN** the dashboard sends no new query for that widget

### Requirement: Map tooltip
Pointing at a shape, tapping it, or reaching it from the keyboard SHALL show its tooltip. The tooltip SHALL show the group value as its heading, then the plotted value with the indicator's label, format and unit, then the widget's tooltip columns in their order, each with its label. Its text, labels included, SHALL be shown as text, never as markup.

#### Scenario: City block
- **WHEN** a member points at block `0042` on a map of the indicator `survey_progress` (label "Survey progress", format `percent`) whose tooltip columns are `address` labelled "Dirección", `sample_target` labelled "Meta muestral" and `achieved` labelled "Encuestas logradas", and the block's row has `Cra 5 # 10-20`, 20, 9 and a value of 0.45
- **THEN** the tooltip reads `0042`, `Survey progress: 45%`, `Dirección: Cra 5 # 10-20`, `Meta muestral: 20` and `Encuestas logradas: 9`

#### Scenario: Markup in a value
- **WHEN** a tooltip column holds the text `<img src=x onerror=alert(1)>`, or a tooltip column's label is that text
- **THEN** the tooltip shows those characters as text, and nothing runs

### Requirement: Pinned map tooltip
After a tap, a click or a keyboard selection, a shape's tooltip SHALL stay open until the member taps or clicks elsewhere, presses Escape, moves to another shape, scrolls the page or moves focus away, or the map's rows change. A tooltip opened by pointing SHALL close when the pointer leaves the shape.

#### Scenario: Tap on a phone
- **WHEN** a member taps a block on a phone
- **THEN** its tooltip opens and stays open until they tap elsewhere

#### Scenario: Pointer leaves
- **WHEN** a member points at a block and moves the pointer off it without clicking
- **THEN** the tooltip closes

### Requirement: Map tooltip columns
An `analyst` or `admin` SHALL be able to give a map widget up to 5 tooltip columns. Each is a column of the indicator's published table other than the geometry column, in a chosen order, with an optional label of up to 80 characters shown instead of the column name. Saving more than 5, one column twice, the geometry column, or tooltip columns on a widget that is not a map SHALL be rejected with an error on the widget's tooltip.

#### Scenario: Analyst adds tooltip columns
- **WHEN** an analyst saves a map with the tooltip columns `address` labelled "Dirección" and `sample_target` with no label
- **THEN** every member's tooltip shows "Dirección" and `sample_target`, in that order

#### Scenario: Too many tooltip columns
- **WHEN** an analyst saves a map with 6 tooltip columns
- **THEN** the API responds 400 with an error saying a map has at most 5

#### Scenario: Tooltip on another chart type
- **WHEN** an analyst saves a column chart with a tooltip column
- **THEN** the API responds 400 with an error on the widget's tooltip

#### Scenario: Map turned into another chart type
- **WHEN** an analyst edits a map that has tooltip columns and changes its type to bar chart
- **THEN** the saved bar chart has no tooltip columns

### Requirement: Tooltip column values over several rows
A tooltip column's value SHALL be computed over the rows of the shape's group that the widget's query matches. A numeric column SHALL be summed. Any other column SHALL show its smallest value: the earliest for dates and timestamps, false before true, and the first in alphabetical order for text, where an empty text counts as no value. When a group combines more than one row, the tooltip SHALL say how many.

#### Scenario: One row per block
- **WHEN** each block of the indicator `survey_progress` has exactly one row
- **THEN** each tooltip column shows that row's value, and the tooltip says nothing about combined rows

#### Scenario: Two rows for one block
- **WHEN** block `0042` has two rows, with `sample_target` 12 and 8 and `address` `Cra 5 # 10-20` and `Cra 5 # 10-22`
- **THEN** its tooltip shows `Meta muestral: 20`, `Dirección: Cra 5 # 10-20` and "Combines 2 rows"

#### Scenario: Filters apply to the tooltip
- **WHEN** a map has a filter `round is exactly 2`, and block `0042` has rows for rounds 1 and 2
- **THEN** its tooltip columns are computed over the round 2 row only

#### Scenario: Empty text in one row
- **WHEN** block `0042` has two rows whose `address` is an empty text and `Cra 5 # 10-20`
- **THEN** its tooltip shows `Dirección: Cra 5 # 10-20`

### Requirement: Tooltip column formats
A numeric tooltip column SHALL be shown with the format chosen for it, `count`, `decimal` or `percent` as in "Indicator formatting" but without a unit. With no format chosen, it SHALL be a whole number with thousands separators for an integer column, and up to two decimals for a decimal column. A format saved on a column that is no longer a number SHALL be ignored.

#### Scenario: Progress shown as a percentage
- **WHEN** a map shows the decimal tooltip column `progress` with the format `percent`, and a block's progress is 0.45
- **THEN** the tooltip shows `45%`

#### Scenario: Format offered for numbers only
- **WHEN** an analyst picks the text column `address` as a tooltip column
- **THEN** the form offers no format for it, and saving `address` with the format `percent` is rejected

#### Scenario: Column no longer a number
- **WHEN** a map shows `progress` with the format `percent`, and a script change publishes `progress` as text
- **THEN** the tooltip shows the text as it is, without the format, and the widget is not stale

### Requirement: Tooltip values that are not numbers
In a tooltip, dates SHALL follow "Date and time display" (see `web-ui`) and timestamps SHALL too, shown in the project's time zone. True/false SHALL read Yes or No, text over 200 characters SHALL be cut with an ellipsis, and no value SHALL read as a dash.

#### Scenario: Missing value
- **WHEN** a block has no address, no value or an empty text, in every row of its group
- **THEN** its tooltip shows `Dirección: —`

#### Scenario: Timestamp
- **WHEN** a block's tooltip column `last_visit` holds `2026-09-01 21:30:05` in the project's time zone, `America/Bogota`
- **THEN** the tooltip shows it to the minute in the shared date and time format, as `Sep 1, 2026, 9:30 PM`, whatever the viewer's own time zone

### Requirement: Tooltip columns in "Show as table"
A map's "Show as table" view SHALL list each shape's group value, its plotted value, its value for each tooltip column under that column's label, formatted as in its tooltip, and the kind of its geometry, in that column order.

#### Scenario: Table of blocks
- **WHEN** a member switches a map of blocks with the tooltip columns "Dirección" and "Meta muestral" to "Show as table"
- **THEN** each row lists the block, its progress, its address, its sample target and its kind of shape

### Requirement: Map basemap
An `analyst` or `admin` SHALL be able to give a map widget a basemap: None, or Streets or Satellite when the deployment offers it (see `map-basemaps`). A new map widget SHALL start with None. The basemap SHALL be drawn under the shapes and saved with the widget for every member. Changing it SHALL NOT query the indicator again. Saving a basemap on a widget that is not a map, or one that is neither Streets nor Satellite, SHALL be rejected.

#### Scenario: Analyst picks satellite
- **WHEN** a deployment offers Google basemaps and an analyst saves a map with the basemap Satellite
- **THEN** every member who opens the dashboard sees the map's shapes over satellite imagery

#### Scenario: Deployment without basemaps
- **WHEN** a deployment offers no basemap and an analyst creates a map widget, or edits one saved with None
- **THEN** the form shows no basemap choice and the map is saved with None

#### Scenario: Basemap on another chart type
- **WHEN** an analyst saves a pie chart with the basemap Streets
- **THEN** the API responds 400 with an error on the widget's basemap

### Requirement: Basemap switched by a member
Every member, viewers included, SHALL be able to switch the basemap of a map they are viewing to None or to any basemap the deployment offers. The switch SHALL apply only to that map on their screen, SHALL NOT be saved, and SHALL be announced to screen readers. It SHALL last until the member leaves or reopens the dashboard, or the widget's rows change.

#### Scenario: Viewer turns on satellite
- **WHEN** a viewer switches a map saved with Streets to Satellite and another member opens the dashboard
- **THEN** the viewer sees satellite imagery, the other member sees Streets, and the widget is unchanged

#### Scenario: Viewer turns a basemap on
- **WHEN** a deployment offers OpenStreetMap and a viewer opens a map saved with None
- **THEN** the viewer can switch it to Streets for themselves

#### Scenario: Viewer turns the basemap off
- **WHEN** a viewer switches a map saved with Satellite to None
- **THEN** the map draws no basemap and requests no tiles, and the widget still has Satellite for everyone else

### Requirement: Basemap not offered
A map whose saved basemap the deployment no longer offers SHALL be drawn without a basemap and without an error. This SHALL NOT make the widget stale. The widget form SHALL show the saved basemap as no longer offered, and saving the widget without changing it SHALL keep it.

#### Scenario: Provider changed by the operator
- **WHEN** a map was saved with Satellite under Google basemaps, and the operator switches the deployment to OpenStreetMap
- **THEN** the map is drawn without a basemap and with no error, its member switch offers Streets, and the widget form shows Satellite as no longer offered

#### Scenario: Saved basemap kept
- **WHEN** a map saved with Streets is edited on a deployment that now offers no basemap, and the analyst saves it after changing only its title
- **THEN** the form showed Streets as no longer offered beside None, and the widget keeps Streets

### Requirement: Basemap on small maps
A map SHALL draw its basemap only when it is large enough to show the basemap's attribution in full. A smaller map SHALL draw no basemap and request no tiles, and SHALL say that it must be enlarged to show the basemap.

#### Scenario: Map resized to one row
- **WHEN** an analyst resizes a map with the basemap Streets to one row of the grid
- **THEN** the map draws its shapes without the basemap, makes no tile request, and says "Enlarge the map to show the basemap"

### Requirement: Map controls on small maps
When a map is too small for its zoom and basemap buttons, they SHALL be gathered in one menu button. Zoom, "Show all" and the basemap switch SHALL stay available at every size.

#### Scenario: Small map on a phone
- **WHEN** a member opens, on a phone, a map whose box is too small for its buttons
- **THEN** a single map options button offers zoom in, zoom out, "Show all" and the basemap choices

### Requirement: Basemap unavailable
When a basemap's tiles cannot be loaded, the map SHALL go on showing its shapes, legend, zoom, tooltips and table without the basemap. It SHALL say once, in a notice announced to screen readers, that the basemap is unavailable. Shapes SHALL never wait for tiles.

#### Scenario: Tile server unreachable
- **WHEN** a map with the basemap Streets is opened from a network that cannot reach the tile server
- **THEN** the shapes are drawn at once, the map says "Basemap unavailable" once, and zoom, tooltips and "Show as table" keep working
