# Indicators dashboard

The indicators dashboard is a canvas of **widgets**: stat tiles, charts and maps that members of a project arrange over the tables its indicator scripts published. Each widget plots **one indicator**. It is the third tab of a project, after [Performance](performance-dashboard.md) and [Clean data](clean-data-dashboard.md). Every member can see it and export the tables; analysts and admins build it.

It shows what the last successful run of each [indicator script](scripts.md#3-indicator-scripts-computecleaned-ctx) published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, or one indicator fails, the table of the last successful run stays on screen. At the top the page lists the project's indicators with their label, unit and format and whether each has published a table. While none has, the page says that no indicator has published yet; analysts and admins get a link to the scripts, and the widgets already configured are kept and listed, so a dashboard can be laid out before the first run.

## What an indicator script must provide

An indicator script returns one DataFrame, published as the table named after the indicator, with **at least one numeric column** (integer or decimal) and any other columns you need (see [Rules for what scripts return](scripts.md#rules-for-what-scripts-return)). There is no fixed set of columns. These are the conventions the dashboard follows:

| Column | Used as |
|---|---|
| `value` | The number a widget plots by default. A table without a `value` column starts with its first numeric column. |
| `numerator`, `denominator` | Let a widget plot a **ratio of sums** (a rate) instead of a single column. |
| Any text, date or true/false column | A **dimension**: a widget groups by it (a municipality, a `round` or `year` for a trend) and filters on it. |
| A geometry column | Where a map gets its shapes; see [Maps](#maps). |

The columns listed in the widget form are the ones the published table has. A widget plots a numeric column or a ratio of two numeric columns.

## What a value means: format and unit

The indicator says what its numbers are, so the dashboard does not guess. Its **format** decides how every widget shows the values, in the stat tile, the axes, the legends, the tooltips and **Show as table**:

| Format | A value of | Is shown as |
|---|---|---|
| `count` | 12450 | `12,450` |
| `decimal` | 3.14159 | `3.14` |
| `percent` | 0.42 | `42%` (a **fraction** shown as a percentage) |
| `currency` | 1250.5 | `1,250.50`, with the unit as the currency when it is a currency code such as `COP` |

The **unit**, when there is one, goes after the value (`12,450 households`). A stat tile over a rate stored as a fraction therefore reads `42%`, not the sum of several percentages.

## Widget types

Every widget has a title, a type, one **indicator**, what to **plot** (a measure or a ratio, see below), the column to **group by**, up to 50 **filters**, and for a map its **geometry column**. A widget form shows a live preview while you edit.

| Type | Shows | Group by | Notes |
|---|---|---|---|
| **Stat tile** | One number: the measure summed, or the ratio of sums, over every row the query matches. | Optional | Says over how many rows it was summed. |
| **Bar chart** | Horizontal bars, one per group value. | Required | |
| **Column chart** | Vertical columns, one per group value. | Required | |
| **Line chart** | A line through the group values. | Required | Group values are in natural order (2 before 10), so a `round` column of 1, 2, 10 reads as a trend. |
| **Histogram** | How many rows fall in each of a number of equal-width ranges of the plotted value. | Optional | 10 ranges by default, up to 100. Without a group column each row of the table is counted; with one, each group's value. |
| **Pie chart** | One slice per group value, sized by its share of the total. | Required | See below. |
| **Map** | Regions or points, colored by the plotted value. | Required | Needs a geometry column; see [Maps](#maps). |

- **Rows with no value** in the plotted measure are left out of every widget.
- A group with no value in the group column is shown as **(none)**.
- The **pie chart** orders slices from the largest. When there are more than 8 groups, the 7 largest keep their slice and the rest are folded into one **Other** slice. A group whose value is zero or negative has no share and is not drawn; the widget says how many, and **Show as table** lists them.
- Colors, light or dark, follow the theme.

### What to plot: a measure or a ratio

- A **measure** is a numeric column of the table. `value` is chosen when the table has it, otherwise its first numeric column. Rows that share a group value are **summed**.
- A **ratio** is a numerator column and a denominator column. Rows that share a group value are summed in each, and the widget plots `sum(numerator) / sum(denominator)`. A group whose denominator sums to zero has **no value**.

### Duplicate rows are summed

Rows that share the same group value are **summed** before they are plotted, in the database. A table that has one row per form version, for example, still charts as one bar per municipality: two rows for `Cali`, with values 40 and 35, plot 75. Summing is right for counts and wrong for rates, which is why a rate should carry its `numerator` and `denominator` and be plotted as a **ratio**: three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50 plot 0.5, not the sum of three rates.

### Filters

Filters work as in the [clean data dashboard](clean-data-dashboard.md#filters): **contains**, **is exactly** and **is one of** for text; **is between** for numbers, dates and timestamps; **is one of** for true/false; **is empty** and **is not empty** for any column. A row has to match all of them. A widget takes at most 50 filters, and **is one of** takes at most 100 values. Filters are added once the indicator's table is published, because they are chosen from its columns.

## Maps

A map draws the shapes of its rows in their relative positions, colored by the plotted value on a sequential scale of seven steps, with the lowest and highest value shown as a legend. It reads the shapes from a **geometry column** that the indicator script publishes: a text column of WKT, chosen in the widget form (any text column of the table is offered).

- Only three WKT types are drawn: `POINT`, `POLYGON` (holes included) and `MULTIPOLYGON`. A `Z` or `M` coordinate and an `SRID=4326;` prefix are accepted and ignored. Anything else (`LINESTRING`, `MULTIPOINT`, `GEOMETRYCOLLECTION`, `EMPTY`, text that does not parse) is not drawn.
- A polygon is a filled region and a point is a marker, both colored by the value. A row whose geometry is missing or cannot be read is **left out and counted** next to the map; **Show as table** lists it as "Left out".
- Rows that share a group value are summed, but their shapes are not: the map uses the geometry of one of them. Give each region its own group value, and the same geometry on every row of a region.
- Coordinates are read as **degrees**, x as longitude and y as latitude. **Project to degrees (EPSG:4326) in the script before returning**: the CRS is not kept when geometry is published, so coordinates in meters (EPSG:3116, UTM) are drawn as if they were degrees. The map fits one flat projection to the extent of its own rows; with degrees it narrows longitude by the middle latitude so shapes keep their proportions.

**There is no basemap.** The map shows the regions of your own data and nothing around them: no streets, no place names, no tiles, no pan or zoom. It is meant for a choropleth of the values of a group column (coverage by municipality), where the region you hover shows its group value and number. Its title and legend carry the units. It does not need an internet connection.

Geometry already publishes from any script, and geopandas is available in them: a geometry column becomes plain text on publishing. For example, an indicator `households_per_1000_people`, with a [reference table](scripts.md#reference-data) `municipalities` that holds a `boundary_wkt` text column with each municipality's polygon in a projected CRS:

```python
import geopandas as gpd


def compute(cleaned, ctx):
    households = cleaned["main"].groupby("municipality").size().rename("households").reset_index()
    places = ctx.reference["municipalities"]  # municipality, population, boundary_wkt
    merged = households.merge(places, on="municipality")
    # The boundaries are in a projected CRS (here MAGNA-SIRGAS, EPSG:3116); the map needs degrees.
    shapes = gpd.GeoSeries.from_wkt(merged["boundary_wkt"], crs="EPSG:3116").to_crs("EPSG:4326")
    return gpd.GeoDataFrame(
        {
            "municipality": merged["municipality"],
            "value": merged["households"] / merged["population"] * 1000,
            "numerator": merged["households"],
            "denominator": merged["population"],
        },
        geometry=shapes,  # published as a text column named "geometry"
    )
```

Then add a **Map** widget over `households_per_1000_people`, group by `municipality` and choose `geometry` as its geometry column. A table that mixes points and polygons draws both.

## Building the dashboard

The **Add widget** button opens the widget form. Each widget has an edit and a remove button in its header. All of this is for **analysts and admins**: a `viewer` sees every widget, switches to tables and exports, but has no add, edit, remove or move controls, and the API refuses their requests.

- **A project holds at most 50 widgets** by default; the page shows how many are used and, at the limit, says so and disables **Add widget**. An operator can change the limit; see [operations.md](operations.md#common-changes).
- Every member sees the same dashboard: it is saved with the project, not per person. If two members change the same widget at the same time, the last change is kept.
- Removing a widget asks for confirmation and removes it for everyone.

### The grid

Widgets sit on a grid of 12 columns. A new widget goes below the existing ones, 3 columns by 2 rows for a stat tile, 6 by 6 for a map and 6 by 4 for the other types; a widget is at most 40 rows high.

- **Drag** a widget by the handle at the left of its title to move it, and drag the corner at the bottom right to resize it. A dashed outline shows where it will land. A widget dropped on others pushes them down; widgets that do not overlap stay where they are.
- **Keyboard.** Focus the move handle and press the arrow keys to move the widget one cell; hold **Shift** with the arrow keys to resize it. On the resize handle, the arrow keys resize. The new place is announced to screen readers. The handles are hidden on narrow screens.
- **Saving.** A move or resize is saved when the dragging has been still for a moment (about 0.4 seconds), not on every step. If it cannot be saved, the page tells you and shows what the server has.
- **Reading order** is top to bottom, then left to right, whatever the order the widgets were created in. Keyboard and screen readers follow it, and so does the page below 768 px wide, where the grid becomes one column and each widget keeps its height.

## Show as table

Every widget except the stat tile has a **Show as table** button that replaces the picture with the numbers it plots, formatted like the chart (a histogram lists its ranges and their counts, a map its groups with the kind of shape each has). The button above the grid, **Show all as tables**, does the same for every widget at once, in reading order. Each chart also carries a text description for assistive technology.

## Export

**Export**, in the indicator's entry of the list, downloads the **whole published table of that indicator**, exactly as published and without the widgets' filters, as CSV or XLSX. Viewers can export. It is the same file, the same single-use download link and the same limits as the [clean data exports](clean-data-dashboard.md#exports): the CSV has a byte order mark, a geometry column is exported as its WKT text, and an XLSX is refused above 1,048,575 rows. Exporting one indicator never delays another indicator's publishing, and a widget does not export on its own; a chart cannot be saved as an image, so use **Show as table** to see its numbers.

## When a table changes

A run replaces each indicator's table on its own. The page shows when each indicator published and by which run; reload the page to see a newer one.

- A widget whose **indicator has not published** (it was never run, its script was removed, or every run so far failed before publishing) does not break the page. It says `<label> has not published yet`; analysts and admins get a link to the scripts.
- A widget that names a **measure**, a **ratio column**, a **group column**, a **geometry column** or a **filter column** the new table no longer has shows **This widget cannot be drawn** and lists what is missing. Edit the widget to choose others, or restore them in the script; the widget draws again as soon as the table has them.

The other widgets keep working.

## Limits

| Limit | Default |
|---|---|
| Widgets in a project | 50 |
| Rows one widget plots (group values, after summing) | 5,000 |
| Filters in a widget | 50 |
| Values in an **is one of** filter | 100 |
| Ranges in a histogram | 100 (10 by default) |
| Indicators in a project | 100 |
| Length of a title | 200 characters |

An operator can change the first two; see [operations.md](operations.md#common-changes). A widget whose query matches more rows than the limit shows the first ones and says so ("Only the first 5,000 rows are shown"); add a filter or group by a coarser column. A query that takes longer than the dashboard time limit (25 seconds by default, shared with the clean data dashboard) is stopped with a message.

## What it does not do

- A widget plots **one indicator**; it does not combine several indicator tables in one chart.
- It has no basemap, no tiles, no pan or zoom, and no administrative boundaries of its own: the shapes come from your script.
- It does not filter across widgets or across the whole dashboard; each widget has its own filters.
- A project has one indicators dashboard.
- It does not save a chart or a map as an image.
- It does not refresh by itself: reload the page after a run.
