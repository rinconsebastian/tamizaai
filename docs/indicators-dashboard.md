# Indicators dashboard

A project's own dashboards are canvases of **widgets**: stat tiles, charts and maps that members of a project arrange over the tables its indicator scripts published. Each widget plots **one indicator**. A project has from 1 to 7 dashboards of its own, each with its name and its widgets, shown as tabs after [Performance](performance-dashboard.md) and [Clean data](clean-data-dashboard.md); a new project starts with one, **Indicators** (see [Dashboards](#dashboards)). Every member can see them all and export the tables; analysts and admins build them.

A dashboard shows what the last successful run of each [indicator script](scripts.md#3-indicator-scripts-computecleaned-ctx) published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, or one indicator fails, the table of the last successful run stays on screen. The widgets come first. After them, a collapsed list titled **Indicators (N) · M published** shows each indicator with its label, unit and format, whether it has published a table, when, and its row count; open it to read it or to export a table. Every dashboard of the project lists the same indicators, with the same exports. While no indicator has published, every dashboard says so with the list expanded as part of that message; analysts and admins get a link to the scripts, and the widgets already configured on the dashboard are kept and listed, so a dashboard can be laid out before the first run. A widget whose indicator has not published stays short until it does.

## Dashboards

The tab row of a project reads **Performance**, **Clean data**, the project's own dashboards in their order, and, for analysts and admins only, **Settings** at its end. Performance and Clean data always come first and are not among the project's own: they cannot be renamed, moved, duplicated or removed, and no dashboard can take their place or their name. Every member sees every dashboard of the project; there are no dashboards for some members only.

A dashboard's name is shown exactly as it was saved: never translated, and always as text, never as markup. From 768 pixels wide, tabs that do not fit scroll sideways inside the row, never the page: each side that hides tabs shows an arrow that scrolls the row, Settings (for analysts and admins) stays visible at its end, and the open tab is brought into view. A name too long for its tab is cut with an ellipsis; pointing at the tab or reaching it with the keyboard shows the whole name, and screen readers read it whole. Below 768 pixels one select lists Performance, Clean data and the project's dashboards in the same order, then, for analysts and admins, the Settings group.

Each dashboard shows its name as a heading above its widgets. For analysts and admins the heading is followed by the **New dashboard** button and the **Dashboard actions** menu (the button with three dots), with **Rename**, **Duplicate**, **Move left**, **Move right** and **Remove**; on a narrow screen they wrap under the name. An item that is unavailable stays in the menu, dimmed, with a second line saying why (for example "It is already the first dashboard."); the arrow keys still reach it, so a screen reader reads the reason too, but it cannot be chosen. Viewers see the heading only.

**New dashboard**, **Rename** and **Duplicate** open the same name form: one **Name** field, of up to 40 characters and different from the names of the project's other dashboards, then **Add dashboard**, **Rename** or **Duplicate** to save, or **Cancel** (or Escape) to close it without a change. A name that is refused (empty, too long, taken, the name of a permanent tab, or holding a control character) is explained under the field, and what you typed is kept.

- **New dashboard** asks for a name, adds the dashboard after the project's others, with no widgets, and opens it with the focus on its heading.
- **Rename** changes only the name: the dashboard keeps its widgets, its place and its address. A message confirms the new name (`Renamed to "Coverage".`), and the focus returns to the actions button.
- **Move left** and **Move right** move the dashboard one place among the project's dashboards, for every member. The first dashboard cannot move left and the last cannot move right. Screen readers are told the dashboard's new position ("Health moved to position 3 of 4"), and the focus returns to the actions button, so a dashboard can be moved several places in a row. While a move is being saved, both items are unavailable.
- **Duplicate** asks for the copy's name, proposed as the original's name followed by " (copy)", the original's name cut where needed so the whole fits in 40 characters (to its first 33 before " (copy)"); when the project already has that name, in any case, it proposes " (copy 2)", " (copy 3)" and so on, cut the same way. The copy is placed right after the original and holds a copy of each of its widgets, with the same title, type, query, position and size, and it opens with the focus on its heading. A widget you moved or resized a moment before is saved first, so the copy has it in its new place. From then on the two are independent: changing or removing a widget of one leaves the other as it is. A dashboard that holds more widgets than the [limit](#limits) allows, which happens only after an operator lowered it, cannot be duplicated: the name form says so ("A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first.") and nothing is added.
- **Remove** asks for confirmation, saying how many widgets go with the dashboard, counted on the server when you ask ("Remove Health and its 5 widgets? This cannot be undone."), then, on **Remove dashboard**, removes the dashboard and all its widgets for every member. It cannot be undone. The dashboard now in its place opens, or the new last one when the removed dashboard was last, with the focus on its heading, and a message says what was removed (`"Health" and its 5 widgets were removed.`).

The rules:

- **1 to 7 dashboards.** A project always keeps one: on its only dashboard **Remove** is unavailable ("A project needs at least one dashboard."). At 7, **New dashboard** is unavailable, with a line below it that says why ("The project has the most dashboards it can hold (7)."), and so is **Duplicate** ("A project can have at most 7 dashboards."). Both can still be reached with the keyboard, so a screen reader reads why. The 7 is a rule of the product, not a setting.
- **Names** are 1 to 40 characters once leading and trailing spaces are removed (`  Salud  ` is saved as `Salud`), with no control characters such as tabs or line breaks. Two dashboards of a project cannot have names that differ only in case (`Salud` and `SALUD`), and no dashboard can be named Performance, Clean data or Settings, in any case. Another project can use the same names.
- **Who.** Analysts and admins add, rename, move, duplicate and remove dashboards. Viewers see every dashboard and its widgets but none of these actions, and the API refuses their requests (403). Someone who is not a member of the project gets the same answer as for a project that does not exist.

### Dashboard addresses

Each dashboard has its own address, `/projects/<project id>/dashboards/<dashboard id>`. The identifier does not change when the dashboard is renamed or moved, and the address holds no part of its name, so links and bookmarks keep working. Opening a dashboard from its address or its tab leaves the focus where it is.

- `/projects/<project id>/indicators`, the address of the former Indicators tab, opens the project's first dashboard, and the address becomes that dashboard's own.
- The address of a dashboard the project does not have, such as a removed one, opens the project's first dashboard instead and replaces the address with its own. A notice above it, also announced to screen readers, says that the dashboard asked for does not exist and names the one shown ("The dashboard you opened does not exist. Showing Health instead."). It stays until you **Dismiss** it, which puts the focus on the dashboard's heading, or until another dashboard or tab opens.

### Two members at the same time

Your tab row shows the dashboards as they were when the page loaded. Other members' changes appear when you reload the page, or when one of your own changes is refused because of them:

- A change to a dashboard another member has removed (renaming, moving, duplicating or removing it) or to one of its widgets (adding, editing, moving, resizing or removing a widget) saves nothing. Then, as when you open such a dashboard from its tab, it leaves your tab row, and the project's first dashboard opens with the focus on its heading and a notice that says what happened (`"Housing" no longer exists. Showing Health instead.`), which stays until you **Dismiss** it or another dashboard or tab opens.
- A move based on an order that has changed since your page loaded, because another member added, removed or moved a dashboard, is refused with a notice above the dashboard ("The project's dashboards have changed. Reload and try again."), and your tab row is reloaded at once. Move it again from there.
- Adding or duplicating a dashboard just after another member reached the 7 is refused in the name form with the rule's message ("A project can have at most 7 dashboards. Remove one before adding another."); removing what has just become the project's only dashboard is refused with a notice ("A project needs at least one dashboard."). Either way your tab row is reloaded.
- Otherwise the last change wins: when two members rename the same dashboard, the later name stays.

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

Every widget has a title, a type, one **indicator**, what to **plot** (a measure or a ratio, see below), the column to **group by**, up to 50 **filters**, and for a map its **geometry column** and up to 5 [**tooltip columns**](#tooltip-and-tooltip-columns) and a [**basemap**](#basemap). A widget form shows a live preview while you edit.

| Type | Shows | Group by | Notes |
|---|---|---|---|
| **Stat tile** | One number: the measure summed, or the ratio of sums, over every row the query matches. | Optional | Says over how many rows it was summed. |
| **Bar chart** | Horizontal bars, one per group value. | Required | Can be [ordered](#bar-and-column-order). |
| **Column chart** | Vertical columns, one per group value. | Required | Can be [ordered](#bar-and-column-order). |
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

### Bar and column order

Without an order, the bars and columns follow the **group column's own order**: numbers from smallest to largest (`1`, `2`, `10`), dates and timestamps from earliest to latest, false before true, and text in alphabetical order (so a *text* column holding `1`, `2` and `10` reads `1`, `10`, `2`). A group with no value, shown as **(none)**, comes last; a group that is an empty text has a blank label and comes first.

A bar or column widget can instead be ordered with **Order bars by** and **Direction** in its form:

- **Plotted value** orders the bars by the number they show (largest first when you pick it), whether that is a measure or a ratio.
- **A column** of the table orders them by that column. This is how a script gives a chart a custom order that no alphabet gives, such as age ranges: publish a column such as `sort_key` (`1` for `0-4`, `2` for `5-9`, `3` for `10-14`) and order by it.
- A bar can stand for several rows. When the order column is not the group column, a bar is placed by the **smallest** value of that column among its rows, in both directions, so switching the direction reverses the order of bars whose values differ.
- A bar with **no value** in the order column goes last, ascending or descending. Bars with equal values stay in the default order between them.
- The order is applied before the row limit, so a widget that reaches the limit shows the first bars of its order.
- **Compare as** appears for a text, integer or decimal column. **Text** puts `10` before `2`; **Number** puts `2` before `10`. A text column is compared as text unless you pick Number, and an integer or decimal column as a number. As a number, a text value is read when it is a plain number: an optional sign, digits with an optional decimal point, and an optional exponent (`-5`, `1.10`, `1e3`), spaces around it allowed. Anything else (`n/a`, an empty text, `1,5` with a decimal comma, `NaN`) **counts as no value** and goes last; it never fails the widget.
- A date, timestamp or true/false column is always compared in its own order, so it has no *Compare as*.
- If a script change removes the order column, the widget names it, as it does for a removed group column.

Line charts keep their natural order (`2` before `10`) and, like pie, histogram and map widgets, have no order setting.

### Filters

Filters work as in the [clean data dashboard](clean-data-dashboard.md#filters): **contains**, **is exactly** and **is one of** for text; **is between** for numbers, dates and timestamps; **is one of** for true/false; **is empty** and **is not empty** for any column. A row has to match all of them. A widget takes at most 50 filters, and **is one of** takes at most 100 values. Filters are added once the indicator's table is published, because they are chosen from its columns.

## Maps

A map draws the shapes of its rows, colored by the plotted value on a sequential scale of seven steps, with the lowest and highest value shown as a legend. It reads the shapes from a **geometry column** that the indicator script publishes: a text column of WKT, chosen in the widget form (any text column of the table is offered).

- Only three WKT types are drawn: `POINT`, `POLYGON` (holes included) and `MULTIPOLYGON`. A `Z` or `M` coordinate and an `SRID=4326;` prefix are accepted and ignored. Anything else (`LINESTRING`, `MULTIPOINT`, `GEOMETRYCOLLECTION`, `EMPTY`, text that does not parse) is not drawn.
- A polygon is a filled region and a point is a marker, both colored by the value. A row whose geometry is missing or cannot be read is **left out and counted** next to the map; **Show as table** lists it as "Left out".
- Rows that share a group value are summed, but their shapes are not: the map uses the geometry of one of them. Give each region its own group value, and the same geometry on every row of a region.
- Coordinates are read as **degrees**, x as longitude and y as latitude. **Project to degrees (EPSG:4326) in the script before returning**: the CRS is not kept when geometry is published, so the map cannot tell coordinates in meters (EPSG:3116, UTM) from degrees except by their size.
- When every x is from -180 to 180 and every y from -90 to 90, the map takes them for degrees and draws the shapes at their **geographic position on a Web Mercator map, north up**. Any other numbers are drawn in their **relative positions**, on a flat plane. A map opens **fitted** to the extent of its shapes; a map with a single point opens centred on it, close enough to see the streets around it.

The map shows the regions of your own data, and nothing around them unless it has a [basemap](#basemap). Its title and legend carry the units.

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

### Zoom and pan

Every map can be zoomed and moved. Zooming and moving need no internet connection of their own, so a map without a basemap works fully offline; a [basemap](#basemap) loads the tiles of the area it moves to.

- **Buttons** at the top right: zoom in, zoom out and **Show all**, which returns to the fitted view. A map does not zoom out more than three levels beyond its fitted view. On a **small map** (under 160 by 140 pixels) the buttons collapse into one **Map options** menu button that holds the same actions.
- **Mouse:** drag to move, double-click to zoom in, and turn the wheel **while holding Ctrl** (⌘ on macOS) to zoom around the pointer. A plain wheel keeps scrolling the page, and the map says "Hold Ctrl and scroll to zoom" for a moment.
- **Touch screen:** two fingers move and pinch the map. One finger keeps scrolling the page, and the map says "Use two fingers to move the map" for a moment.
- **Keyboard:** **Tab** reaches the map as one stop. While it has focus, the arrow keys move it, **+** and **-** zoom, and **0** or **Home** shows all. **Enter** steps into the shapes, the first one highlighted: the arrow keys then move to the next or the previous shape in the order of **Show as table**, **Home** and **End** go to the first and the last, and **Escape** returns to the map. Each shape in turn is brought into view and its tooltip is shown, and a screen reader reads the same text. The shapes are not Tab stops of their own: **Tab** leaves the map for the next control. In the widget form, the first **Escape** only returns from the shapes to the map, and the second closes the form.
- **The view is not saved.** Your zoom and position stay on your screen: they do not change the widget for other members and do not query the indicator again. The map opens fitted again when its rows change or the page is opened again; switching it to **Show as table** and back keeps your view.

### Basemap

A map can have a **basemap** under its shapes, so the streets or the imagery show behind the regions or blocks: **None**, **Streets** or **Satellite**. An analyst or admin chooses it in the widget form, under the geometry column; it is saved with the widget and every member sees it. A new map starts with None.

- **The deployment decides what is offered.** The operator turns basemaps on with `TAMIZA_MAP_BASEMAP_PROVIDER` (see [Map basemaps](operations.md#map-basemaps-optional)): `osm` offers Streets from OpenStreetMap, `google` offers Streets and Satellite from Google, and the default, `none`, offers nothing. Without a provider the widget form shows no basemap choice, the map is drawn on its plain background, and no request is made to any tile provider. A deployment offers one provider only.
- **Each member can switch it for themselves.** Every member, a viewer included, can switch the basemap of a map to None or to any basemap the deployment offers, with the **Basemap** button next to the zoom buttons (or in the **Map options** menu of a small map). The switch applies to that map on your screen only: it is not saved, other members keep the saved basemap, a screen reader is told, and it lasts until you leave or reopen the dashboard, or the widget's rows change. It also means that, once an operator turns a provider on, any member can show a basemap on any map.
- **Shapes over the basemap** are drawn slightly see-through (and the legend with them), so the streets show through.
- **Attribution is always on the map.** At the bottom left, never behind a control: "© OpenStreetMap contributors", linking to OpenStreetMap's copyright page, or the **Google Maps** logo with the data credits Google gives for the area on screen, which change as you move. Google imagery is not shown without its credits. A Google map never asks for imagery deeper than Google has for the area: zooming further enlarges the deepest imagery instead of failing.
- **Small maps.** A map smaller than 200 by 120 pixels is too small to show the attribution in full, so it draws no basemap, makes no tile request and says "Enlarge the map to show the basemap". Resize it and the basemap appears.
- **Degrees only.** A basemap needs coordinates in degrees. A map whose coordinates are not (meters, for example) is drawn in relative positions, with no basemap, and says "The basemap cannot be shown: the coordinates are not degrees."
- **When the tiles cannot load,** the map keeps its shapes, legend, zoom, tooltips and table, and says "Basemap unavailable" once. Switching the basemap again tries again.
- **A basemap the deployment no longer offers** (the operator changed the provider) is drawn as no basemap, without an error, and does not make the widget stale. The widget form shows it as "(no longer offered)", and saving the widget without changing it keeps it; the member switch offers what is available now.
- **What leaves the deployment.** Only the basemap kind and the tile coordinates and, for Google's credits, the zoom and bounds of the area on screen. Never a row, a group value, a geometry or a tooltip value. With `osm`, each viewer's browser contacts OpenStreetMap; with `google`, only the server does, and the key never reaches a browser.

### Tooltip and tooltip columns

Pointing at a shape, tapping it, or reaching it from the keyboard shows its **tooltip**: the group value as its heading, then the plotted value with the indicator's label, format and unit, then the **tooltip columns** you chose, each under its label. After a tap, a click or a keyboard selection the tooltip stays open until you tap or click elsewhere, press **Escape**, move to another shape, scroll the page or move focus away; a tooltip opened by pointing closes when the pointer leaves the shape. Everything in it is shown as text, never as markup.

An analyst or admin chooses up to **5 tooltip columns** in the widget form, under the geometry column. Each is any column of the published table except the geometry column, with an optional **label** of up to 80 characters (the column's name when it has none) and, for a number column, a **format**. Columns can be reordered; the tooltip and **Show as table** list them in that order. Tooltip columns only belong to maps: a map turned into another chart type loses them.

- **Rows that share a group value are combined.** A number column is **summed**; any other column shows its **smallest** value: the earliest for dates and timestamps, false before true, and the first in alphabetical order for text, where an empty text counts as no value. When a group combines more than one row, the tooltip says "Combines N rows". The rows combined are the ones the widget's query matches, so its filters apply to the tooltip too. Because numbers are summed, plot a rate as a [ratio](#what-to-plot-a-measure-or-a-ratio) and do not add a rate as a tooltip column; give each shape one row where you can.
- **Formats.** A number column is shown with the format chosen for it, `count`, `decimal` or `percent` as in [What a value means](#what-a-value-means-format-and-unit) but without a unit; with none chosen, an integer shows whole with thousands separators and a decimal up to two decimals. The indicator's own format is only for the plotted value: a block's target of 20 reads `20`, not `2,000%`. A format saved on a column that has since stopped being a number is ignored, and the widget is not stale.
- **Other values.** A date reads `Sep 1, 2026`; a timestamp reads to the minute in the project's time zone (`Sep 1, 2026, 9:30 PM`) whatever the viewer's own; true and false read **Yes** and **No**; a text over 200 characters is cut with an ellipsis; no value reads as a dash.

An example: following survey progress block by block. The indicator `survey_progress` has the format `percent`, and its table has **one row per block**. A [reference table](scripts.md#reference-data) `blocks` holds each block's `block_code`, `address`, `sample_target` (how many surveys the block needs) and `boundary_wkt`; the clean table `main` has one row per survey, with the `block_code` it was made in:

```python
import geopandas as gpd


def compute(cleaned, ctx):
    surveys = cleaned["main"].groupby("block_code").size().rename("achieved").reset_index()
    blocks = ctx.reference["blocks"]  # block_code, address, sample_target, boundary_wkt
    merged = blocks.merge(surveys, on="block_code", how="left")
    merged["achieved"] = merged["achieved"].fillna(0).astype(int)
    # A fraction from 0 to 1, capped at 1: a block that went over its target is complete, not 120%.
    # A target of 0 would divide by zero; `where` makes its progress empty instead.
    progress = merged["achieved"] / merged["sample_target"].where(merged["sample_target"] > 0)
    # The boundaries are in a projected CRS (here MAGNA-SIRGAS, EPSG:3116); the map needs degrees.
    shapes = gpd.GeoSeries.from_wkt(merged["boundary_wkt"], crs="EPSG:3116").to_crs("EPSG:4326")
    return gpd.GeoDataFrame(
        {
            "block_code": merged["block_code"],
            "address": merged["address"],
            "sample_target": merged["sample_target"],
            "achieved": merged["achieved"],
            "value": progress.clip(upper=1),
        },
        geometry=shapes,  # published as a text column named "geometry"
    )
```

Add a **Map** widget over `survey_progress`, group by `block_code`, choose `geometry` as its geometry column, and add the tooltip columns `address` labelled "Dirección", `sample_target` labelled "Meta muestral" and `achieved` labelled "Encuestas logradas". Pointing at block `0042` then reads `0042`, `Survey progress: 45%`, `Dirección: Cra 5 # 10-20`, `Meta muestral: 20` and `Encuestas logradas: 9`.

- **Capped or not.** `progress.clip(upper=1)` draws a block that went over its target as 100%, so the colors stay between 0 and 100%. Without it a block at 120% stretches the scale and every other block looks paler; `achieved` in the tooltip still tells how many surveys were made.
- **A target of 0.** Dividing by a zero target gives infinity (or not-a-number when nothing was done), which is no progress to plot. Dividing by `sample_target.where(sample_target > 0)`, as above, leaves that block's progress **empty**, and a row with no value in the plotted measure is left out of every widget, so the block is not drawn. To draw it, fill the progress in (`.fillna(0)` for no progress, or `.fillna(1)` for a block with nothing to do) before returning.

## Building the dashboard

The **Add widget** button opens the widget form, and the widget is added to the open dashboard. Each widget has an actions menu in its header with **Edit widget** and **Remove widget**. Above the widgets, editors see how many of the allowed widgets the dashboard holds; when the limit is reached the line says so and **Add widget** is off. All of this is for **analysts and admins**: a `viewer` sees every widget, switches to tables and exports, but has no add, edit, remove or move controls, and the API refuses their requests.

- **A dashboard holds at most 50 widgets** by default. The limit counts the widgets of one dashboard, so a full dashboard does not stop another from getting more. The page shows how many are used ("12 of 50 widgets on this dashboard") and, at the limit, says so ("This dashboard has the most widgets it can hold (50). Remove one or move it to another dashboard to add another.") and disables **Add widget**. An operator can change the limit; see [operations.md](operations.md#common-changes).
- Every member sees the same dashboards: they are saved with the project, not per person. If two members change the same widget at the same time, the last change is kept.
- Removing a widget asks for confirmation and removes it for everyone.

### Moving a widget to another dashboard

When the project has more than one dashboard, **Edit widget** shows a **Dashboard** field after the title, set to the widget's own dashboard. Choose another one and save: the widget keeps its title, type, query and size, and goes to the left edge below the widgets of its new dashboard. It leaves the open dashboard, a message names the dashboard it went to ("Widget Coverage moved to Field."), and the focus moves to the open dashboard's heading.

- A widget that cannot be drawn because its table lost a column can still be moved: change only the dashboard, and it moves and keeps naming what is missing. Changing anything else as well checks the whole widget again.
- A dashboard that already holds the most widgets it can ("A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first."), or that another member has just removed ("The dashboard does not exist."), refuses the widget with an error next to the field; the form stays open and the widget stays where it was. A removed dashboard then leaves the field and the tab row.
- Adding a widget offers no dashboard choice: a new widget goes to the open dashboard.

### The grid

Each dashboard has its own grid of 12 columns. A new widget goes below the existing widgets of its dashboard, 3 columns by 2 rows for a stat tile, 6 by 6 for a map and 6 by 4 for the other types; a widget is at most 40 rows high.

- **Drag** a widget by the handle at the left of its title to move it, and drag the corner at the bottom right to resize it. A dashed outline shows where it will land. A widget dropped on others pushes them down; widgets that do not overlap stay where they are.
- **Keyboard.** Focus the move handle and press the arrow keys to move the widget one cell; hold **Shift** with the arrow keys to resize it. On the resize handle, the arrow keys resize. The new place is announced to screen readers. The handles are hidden on narrow screens.
- **Saving.** A move or resize is saved when the dragging has been still for a moment (about 0.4 seconds), not on every step. If it cannot be saved, the page tells you and shows what the server has.
- **Reading order** is top to bottom, then left to right, whatever the order the widgets were created in. Keyboard and screen readers follow it, and so does the page below 768 px wide, where the grid becomes one column and each widget keeps its height.

## Show as table

Every widget except the stat tile has a table button in its header (shown once the widget has rows to draw) that replaces the picture with the numbers it plots, formatted like the chart (a histogram lists its ranges and their counts, a map its groups with their tooltip columns, written as in the tooltip, and the kind of shape each has). The button above the grid, **Show all as tables**, does the same for every widget of the dashboard at once, in reading order, and resets the choice made on single widgets. Each chart also carries a text description for assistive technology.

## Export

**Export**, the download button at the end of a published indicator's entry in the list, downloads the **whole published table of that indicator**, exactly as published and without the widgets' filters, as CSV or XLSX. Viewers can export. It is the same file, the same single-use download link and the same limits as the [clean data exports](clean-data-dashboard.md#exports): the CSV has a byte order mark, a geometry column is exported as its WKT text, and an XLSX is refused above 1,048,575 rows. While one indicator's file is being prepared only its button shows that it is busy, and the page ends with one message. Exporting one indicator never delays another indicator's publishing, and a widget does not export on its own; a chart cannot be saved as an image, so use **Show as table** to see its numbers.

## When a table changes

A run replaces each indicator's table on its own. The page shows when each indicator published and by which run; reload the page to see a newer one.

- A widget whose **indicator has not published** (it was never run, its script was removed, or every run so far failed before publishing) does not break the page. It says `<label> has not published yet`; analysts and admins get a link to the scripts.
- A widget that names a **measure**, a **ratio column**, a **group column**, a **geometry column**, a **tooltip column**, a **sort column** or a **filter column** the new table no longer has shows **This widget cannot be drawn** and lists what is missing. Edit the widget to choose others, or restore them in the script; the widget draws again as soon as the table has them.

The other widgets keep working.

## Limits

| Limit | Default |
|---|---|
| Widgets in a dashboard | 50 |
| Rows one widget plots (group values, after summing) | 5,000 |
| Dashboards of a project's own | 7, and at least 1 |
| Length of a dashboard's name | 40 characters |
| Filters in a widget | 50 |
| Values in an **is one of** filter | 100 |
| Ranges in a histogram | 100 (10 by default) |
| Tooltip columns in a map | 5 |
| Length of a tooltip column's label | 80 characters |
| Length of a text in a tooltip | 200 characters (cut with an ellipsis) |
| Indicators in a project | 100 |
| Length of a title | 200 characters |

An operator can change the first two; the others are fixed. See [operations.md](operations.md#common-changes). A widget whose query matches more rows than the limit shows the first ones and says so ("Only the first 5,000 rows are shown"); add a filter or group by a coarser column. A query that takes longer than the dashboard time limit (25 seconds by default, shared with the clean data dashboard) is stopped with a message.

## What it does not do

- A widget plots **one indicator**; it does not combine several indicator tables in one chart.
- Its basemap is a background and nothing more: there is no satellite imagery from anyone but Google, no street labels over the imagery, no basemap you define yourself, and no administrative boundaries of its own, since the shapes come from your script.
- It does not save a zoomed view as the map's starting view, and only maps have a tooltip with columns; a tooltip column's numbers are always summed, with no choice of minimum, maximum or average.
- It does not filter across widgets or across the whole dashboard; each widget has its own filters.
- Every member sees every dashboard: there are no dashboards for some members only, and no dashboards of one's own.
- It does not copy a dashboard or a widget to another project, and a removed dashboard cannot be brought back.
- Tabs cannot be dragged: a dashboard moves with **Move left** and **Move right**, which also work from the keyboard and on a phone.
- It does not save a chart or a map as an image.
- It does not refresh by itself: reload the page after a run, or to see the dashboards other members have added, renamed, moved or removed.
