# Indicators dashboard

The indicators dashboard is a canvas of **widgets**: stat tiles, charts and maps that members of a project arrange over the indicator table that script 3 published. It is the third tab of a project, after [Performance](performance-dashboard.md) and [Clean data](clean-data-dashboard.md). Every member can see it and export the table; analysts and admins build it.

It shows what the last successful run of [script 3](scripts.md#3-compute_indicatorscleaned-ctx) published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, the table of the last successful run stays on screen. Without a published table the page says that no indicators are published yet; analysts and admins get a link to the scripts, and the widgets already configured are kept and listed, so a dashboard can be laid out before the first run.

## What script 3 must provide

Script 3 returns one DataFrame, published as the table `indicators`, with the five required columns (see [Rules for what scripts return](scripts.md#rules-for-what-scripts-return)):

| Column | Used as |
|---|---|
| `indicator` | The name of the indicator. A widget picks the indicators it plots from this column. |
| `disaggregation` | The group a row belongs to (`total`, `municipality=Cali`). Widgets group by it unless told otherwise. |
| `value` | The number a widget plots by default. |
| `numerator`, `denominator` | The other two numbers. A widget can plot one of them instead of `value`. |

A widget can use **any other column** of the table too, to group by it (a `year` or `round` column for a trend) or to filter on it. The columns listed in the widget form are the ones the published table has. The field a widget plots must be a number column (integer or decimal).

One DataFrame is one table, so a map needs its geometry in the same rows; see [Maps](#maps).

## Widget types

Every widget has a title, a type, one or more **indicators**, the column to **group by** (`disaggregation` by default), the **field to plot** (`value`, `numerator` or `denominator`) and up to 50 **filters**. A widget form shows a live preview while you edit.

| Type | Shows | Indicators | Notes |
|---|---|---|---|
| **Stat tile** | One number: the plotted field summed over every row the query matches. | Exactly one | Says over how many rows it was summed. |
| **Bar chart** | Horizontal bars, one per group value. | One or more | One series per indicator, sharing the group values. |
| **Column chart** | Vertical columns, one per group value. | One or more | As the bar chart. |
| **Line chart** | A line through the group values. | One or more | Group values are in natural order (2 before 10), so a `round` column of 1, 2, 10 reads as a trend. A group an indicator has no row for leaves a gap. |
| **Histogram** | How many rows fall in each of a number of equal-width ranges of the plotted value. | One or more | 10 ranges by default, up to 100. The ranges run from the smallest to the largest value and are shared by all the indicators, so they can be compared. |
| **Pie chart** | One slice per group value, sized by its share of the total. | Exactly one | See below. |
| **Map** | Regions or points, colored by the plotted field. | Exactly one | Needs a geometry column; see [Maps](#maps). |

- A widget plots **at most 8 indicators** (the form stops at 8): each is a series with its own color. Colors, light or dark, follow the theme.
- **Rows with no value** in the plotted field are left out of every widget.
- A group with no value in the group column is shown as **(none)**.
- The **pie chart** orders slices from the largest. When there are more than 8 groups, the 7 largest keep their slice and the rest are folded into one **Other** slice. A group whose value is zero or negative has no share and is not drawn; the widget says how many, and **Show as table** lists them.
- The **histogram** counts the rows after duplicates are summed (see below), so each indicator-and-group pair is one row of the count.

### Duplicate rows are summed

Rows that share the same indicator and the same group value are **summed** before they are plotted, in the database: `value`, `numerator` and `denominator` are each added up. A script that returns one row per form version, for example, still charts as one bar per disaggregation: two rows for `coverage` and `Cali`, with values 40 and 35, plot 75. Summing is right for counts and wrong for rates; if `value` is a percentage, group by a column that tells the rows apart, filter, or return one row per combination from the script.

### Filters

Filters work as in the [clean data dashboard](clean-data-dashboard.md#filters): **contains**, **is exactly** and **is one of** for text; **is between** for numbers, dates and timestamps; **is one of** for true/false; **is empty** and **is not empty** for any column. A row has to match all of them. A widget takes at most 50 filters, and **is one of** takes at most 100 values. Filters are added once an indicator table is published, because they are chosen from its columns.

## Maps

A map draws the shapes of its rows in their relative positions, colored by the plotted field on a sequential scale of seven steps, with the lowest and highest value shown as a legend. It reads the shapes from a **geometry column** that script 3 publishes: a text column of WKT, chosen in the widget form (any text column of the table is offered).

- Only three WKT types are drawn: `POINT`, `POLYGON` (holes included) and `MULTIPOLYGON`. A `Z` or `M` coordinate and an `SRID=4326;` prefix are accepted and ignored. Anything else (`LINESTRING`, `MULTIPOINT`, `GEOMETRYCOLLECTION`, `EMPTY`, text that does not parse) is not drawn.
- A polygon is a filled region and a point is a marker, both colored by the value. A row whose geometry is missing or cannot be read is **left out and counted** next to the map; **Show as table** lists it as "Left out".
- Rows that share a group value are summed, but their shapes are not: the map uses the geometry of one of them. Give each region its own group value, and the same geometry on every row of a region.
- Coordinates are read as **degrees**, x as longitude and y as latitude. **Project to degrees (EPSG:4326) in the script before returning**: the CRS is not kept when geometry is published, so coordinates in meters (EPSG:3116, UTM) are drawn as if they were degrees. The map fits one flat projection to the extent of its own rows; with degrees it narrows longitude by the middle latitude so shapes keep their proportions.

**There is no basemap.** The map shows the regions of your own data and nothing around them: no streets, no place names, no tiles, no pan or zoom. It is meant for a choropleth of the project's disaggregation values (coverage by municipality), where the region you hover shows its group value and number. Its title and legend carry the units. It does not need an internet connection.

Geometry already publishes from any script, and geopandas is available in them: a geometry column becomes plain text on publishing (see [Rules for what scripts return](scripts.md#rules-for-what-scripts-return)). For example, with a [reference table](scripts.md#reference-data) `municipalities` that holds a `boundary_wkt` text column with each municipality's polygon in a projected CRS:

```python
import geopandas as gpd


def compute_indicators(cleaned, ctx):
    households = cleaned["main"].groupby("municipality").size().rename("households").reset_index()
    places = ctx.reference["municipalities"]  # municipality, population, boundary_wkt
    merged = households.merge(places, on="municipality")
    # The boundaries are in a projected CRS (here MAGNA-SIRGAS, EPSG:3116); the map needs degrees.
    shapes = gpd.GeoSeries.from_wkt(merged["boundary_wkt"], crs="EPSG:3116").to_crs("EPSG:4326")
    return gpd.GeoDataFrame(
        {
            "indicator": "households_per_1000_people",
            "disaggregation": merged["municipality"],
            "value": merged["households"] / merged["population"] * 1000,
            "numerator": merged["households"],
            "denominator": merged["population"],
        },
        geometry=shapes,  # published as a text column named "geometry"
    )
```

Then add a **Map** widget for `households_per_1000_people` and choose `geometry` as its geometry column. A table that mixes points and polygons draws both.

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

Every widget except the stat tile has a **Show as table** button that replaces the picture with the numbers it plots (a histogram lists its ranges and their counts, a map its groups with the kind of shape each has). The button above the grid, **Show all as tables**, does the same for every widget at once, in reading order. Each chart also carries a text description for assistive technology.

## Export

**Export** downloads the **whole published indicator table**, exactly as published and without the widgets' filters, as CSV or XLSX. Viewers can export. It is the same file, the same single-use download link and the same limits as the [clean data exports](clean-data-dashboard.md#exports): the CSV has a byte order mark, a geometry column is exported as its WKT text, and an XLSX is refused above 1,048,575 rows. A widget does not export on its own, and a chart cannot be saved as an image; use **Show as table** to see its numbers.

## When the table changes

A run replaces the published table as a whole. The page shows when it was published and by which run; reload the page to see a newer one.

A widget that names an **indicator**, a **group column**, a **geometry column** or a **filter column** the new table no longer has does not break the page. It shows **This widget cannot be drawn** and lists what is missing. Edit the widget to choose others, or restore them in the script; the widget draws again as soon as the table has them. The other widgets keep working.

## Limits

| Limit | Default |
|---|---|
| Widgets in a project | 50 |
| Rows one widget plots (indicator-and-group pairs, after summing) | 5,000 |
| Filters in a widget | 50 |
| Values in an **is one of** filter | 100 |
| Indicators in a widget | 8 |
| Ranges in a histogram | 100 (10 by default) |
| Length of a title | 200 characters |

An operator can change the first two; see [operations.md](operations.md#common-changes). A widget whose query matches more rows than the limit shows the first ones and says so ("Only the first 5,000 rows are shown"); add a filter or group by a coarser column. A query that takes longer than the dashboard time limit (25 seconds by default, shared with the clean data dashboard) is stopped with a message. An indicator table holds one row per indicator and group value, so these limits are far above what a dashboard usually needs.

## What it does not do

- It has no basemap, no tiles, no pan or zoom, and no administrative boundaries of its own: the shapes come from your script.
- It does not filter across widgets or across the whole dashboard; each widget has its own filters.
- A project has one indicators dashboard.
- It does not save a chart or a map as an image.
- It does not refresh by itself: reload the page after a run.
