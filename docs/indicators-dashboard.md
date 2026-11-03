# Indicators dashboard

A project's own dashboards are canvases of **widgets**: stat tiles, charts and maps that members of a project arrange over the tables its indicator scripts published. Each widget plots **one indicator**. A project has from 1 to 10 dashboards of its own, each with its name and its widgets, shown as tabs after [Performance](performance-dashboard.md) and [Clean data](clean-data-dashboard.md); a new project starts with one, **Indicators** (see [Dashboards](#dashboards)). Every member can see them all and export the tables; analysts and admins build them.

A dashboard shows what the last successful run of each [indicator script](scripts.md#3-indicator-scripts-computecleaned-ctx) published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, or one indicator fails, the table of the last successful run stays on screen. The widgets come first. After them, a collapsed list titled **Indicators (N) · M published** shows each indicator with its label, unit and format, whether it has published a table, when, and its row count; open it to read it or to export a table. Every dashboard of the project lists the same indicators, with the same exports. While no indicator has published, every dashboard says so with the list expanded as part of that message; analysts and admins get a link to the scripts, and the widgets already configured on the dashboard are kept and listed, so a dashboard can be laid out before the first run. A widget whose indicator has not published stays short until it does.

## Dashboards

The tab row of a project reads **Performance**, **Clean data**, the project's own dashboards in their order, and, for analysts and admins only, **Settings** at its end. Performance and Clean data always come first and are not among the project's own: they cannot be renamed, moved, duplicated or removed, and no dashboard can take their place or their name. Every member sees every dashboard of the project; there are no dashboards for some members only.

A dashboard's name is shown exactly as it was saved: never translated, and always as text, never as markup. From 768 pixels wide, tabs that do not fit scroll sideways inside the row, never the page: each side that hides tabs shows an arrow that scrolls the row, Settings (for analysts and admins) stays visible at its end, and the open tab is brought into view. A name too long for its tab is cut with an ellipsis; pointing at the tab or reaching it with the keyboard shows the whole name, and screen readers read it whole. Below 768 pixels one select lists Performance, Clean data and the project's dashboards in the same order, then, for analysts and admins, the Settings group.

Each dashboard shows its name as a heading above its widgets. For analysts and admins the heading is followed by the **New dashboard** button and the **Dashboard actions** menu (the button with three dots), with **Rename**, **Duplicate**, **Move left**, **Move right**, **Filters** (see [Defining a dashboard's filters](#defining-a-dashboards-filters)), **Colors** (see [Colors](#colors)), **Scheduled email…** where Tamiza sends email (see [operations.md](operations.md#email-optional)), **Version history** (see [Version history](#version-history)) and **Remove**; on a narrow screen they wrap under the name. An item that is unavailable stays in the menu, dimmed, with a second line saying why (for example "It is already the first dashboard."); the arrow keys still reach it, so a screen reader reads the reason too, but it cannot be chosen. Viewers see the heading only.

**New dashboard**, **Rename** and **Duplicate** open the same name form: one **Name** field, of up to 40 characters and different from the names of the project's other dashboards, then **Add dashboard**, **Rename** or **Duplicate** to save, or **Cancel** (or Escape) to close it without a change. A name that is refused (empty, too long, taken, the name of a permanent tab, or holding a control character) is explained under the field, and what you typed is kept.

- **New dashboard** asks for a name, adds the dashboard after the project's others, with no widgets, and opens it with the focus on its heading.
- **Rename** changes only the name: the dashboard keeps its widgets, its place and its address. A message confirms the new name (`Renamed to "Coverage".`), and the focus returns to the actions button.
- **Move left** and **Move right** move the dashboard one place among the project's dashboards, for every member. The first dashboard cannot move left and the last cannot move right. Screen readers are told the dashboard's new position ("Health moved to position 3 of 4"), and the focus returns to the actions button, so a dashboard can be moved several places in a row. While a move is being saved, both items are unavailable.
- **Duplicate** asks for the copy's name, proposed as the original's name followed by " (copy)", the original's name cut where needed so the whole fits in 40 characters (to its first 33 before " (copy)"); when the project already has that name, in any case, it proposes " (copy 2)", " (copy 3)" and so on, cut the same way. The copy is placed right after the original and holds a copy of each of its widgets, with the same title, type, query, ignored filters, color or scale, position and size, and of its filters, in the same order with the same defaults, and it has the same palette and colors kept by value (see [Colors](#colors)); it opens with the focus on its heading. A widget you moved or resized a moment before is saved first, so the copy has it in its new place. From then on the two are independent: changing or removing a widget of one, or changing its colors, leaves the other as it is. A dashboard that holds more widgets than the [limit](#limits) allows, which happens only after an operator lowered it, cannot be duplicated: the name form says so ("A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first.") and nothing is added.
- **Remove** asks for confirmation, saying how many widgets go with the dashboard, counted on the server when you ask ("Remove Health and its 5 widgets? This cannot be undone."), then, on **Remove dashboard**, removes the dashboard and all its widgets for every member. It cannot be undone: a removed dashboard cannot be brought back, and its versions go with it. A widget removed from a dashboard can be brought back while a kept version of that dashboard holds it (see [Restoring a version](#restoring-a-version)). The dashboard now in its place opens, or the new last one when the removed dashboard was last, with the focus on its heading, and a message says what was removed (`"Health" and its 5 widgets were removed.`).

The rules:

- **1 to 10 dashboards.** A project always keeps one: on its only dashboard **Remove** is unavailable ("A project needs at least one dashboard."). At 10, **New dashboard** is unavailable, with a line below it that says why ("The project has the most dashboards it can hold (10)."), and so is **Duplicate** ("A project can have at most 10 dashboards."). Both can still be reached with the keyboard, so a screen reader reads why. The 10 is a rule of the product, not a setting.
- **Names** are 1 to 40 characters once leading and trailing spaces are removed (`  Salud  ` is saved as `Salud`), with no control characters such as tabs or line breaks. Two dashboards of a project cannot have names that differ only in case (`Salud` and `SALUD`), and no dashboard can be named Performance, Clean data or Settings, in any case. Another project can use the same names.
- **Who.** Analysts and admins add, rename, move, duplicate and remove dashboards, define their filters, choose their palette and the colors kept by value (and, in the widget form, a widget's color or scale), and read, view and restore their versions. Viewers see every dashboard and its widgets, in the colors chosen, but none of these actions, and the API refuses their requests (403). Someone who is not a member of the project gets the same answer as for a project that does not exist.

### Dashboard addresses

Each dashboard has its own address, `/projects/<project id>/dashboards/<dashboard id>`. The identifier does not change when the dashboard is renamed or moved, and the address holds no part of its name, so links and bookmarks keep working. Opening a dashboard from its address or its tab leaves the focus where it is.

- `/projects/<project id>/indicators`, the address of the former Indicators tab, opens the project's first dashboard, and the address becomes that dashboard's own.
- The address of a dashboard the project does not have, such as a removed one, opens the project's first dashboard instead and replaces the address with its own. A notice above it, also announced to screen readers, says that the dashboard asked for does not exist and names the one shown ("The dashboard you opened does not exist. Showing Health instead."). It stays until you **Dismiss** it, which puts the focus on the dashboard's heading, or until another dashboard or tab opens.

### Two members at the same time

Your tab row shows the dashboards as they were when the page loaded. Other members' changes appear when you reload the page, or when one of your own changes is refused because of them:

- A change to a dashboard another member has removed (renaming, moving, duplicating or removing it, or saving its colors) or to one of its widgets (adding, editing, moving, resizing or removing a widget) saves nothing. Then, as when you open such a dashboard from its tab, it leaves your tab row, and the project's first dashboard opens with the focus on its heading and a notice that says what happened (`"Housing" no longer exists. Showing Health instead.`), which stays until you **Dismiss** it or another dashboard or tab opens.
- A move based on an order that has changed since your page loaded, because another member added, removed or moved a dashboard, is refused with a notice above the dashboard ("The project's dashboards have changed. Reload and try again."), and your tab row is reloaded at once. Move it again from there.
- Adding or duplicating a dashboard just after another member reached the 10 is refused in the name form with the rule's message ("A project can have at most 10 dashboards. Remove one before adding another."); removing what has just become the project's only dashboard is refused with a notice ("A project needs at least one dashboard."). Either way your tab row is reloaded.
- Otherwise the last change wins: when two members rename the same dashboard, the later name stays.

## Dashboard filters

A dashboard can have up to 8 **filters** that narrow all its widgets at once: a municipality, a round, a period. Analysts and admins define them for each dashboard (see [Defining a dashboard's filters](#defining-a-dashboards-filters)); every member, viewers included, changes their values on their own screen.

### The filter bar

The filters appear in a row between the dashboard's heading and its widgets, in the order they were defined, each as a button with its label and the value in force: `Municipality: All` while it has none, `Municipality: Cali, Palmira`, `Round: 2 to 5`, `Round: from 2`, `Visit: up to 2026-09-30`. A dashboard without filters shows no row. On a narrow screen the buttons wrap onto as many rows as they need; a button too long for the screen is cut with an ellipsis, and pointing at it shows the whole text, which screen readers read whole.

Choose a filter to change it. It opens the same form as a clean data filter, titled with the filter's label, with the one condition its kind takes:

| Kind | Columns it narrows | You choose |
|---|---|---|
| **Text** | Text columns | One or more values (up to 100). The list shows the 50 most frequent values of that column across the dashboard's indicator tables, without counts (a row of an indicator table is a group, not a submission), and **Search values** finds others that contain what you type, ignoring case. A value not listed can also be typed. |
| **True/false** | True/false columns | True, false, or both. |
| **Number** | Integer and decimal columns | A range: from, to, or both, both included. Decimals are allowed (`2.5`). |
| **Date** | Date and timestamp columns | A range of days, both included, in the project's time zone; a timestamp on the last day counts until midnight. |

**Apply** narrows the widgets at once; **Clear** in the same form sets the filter back to `All`. **Clear filters** after the buttons sets every filter to `All`, and **Default filters** puts each back to the value the analyst gave it, when there is one. Screen readers are told what the dashboard is filtered by ("Dashboard filtered by Municipality, Round."), and the focus returns to the filter you changed.

What you choose changes **only your own screen**: it is never saved, and other members, even with the same dashboard open, keep seeing their own values. When you open a dashboard, each filter starts at its **default**: the value the analyst gave it, or `All`.

### Which widgets a filter narrows

A filter narrows a widget when the published table of the widget's indicator has a **column with the filter's column name and a type of its kind**: an integer and a decimal column both count as number, and a date and a timestamp column both count as date. The rows are narrowed before they are summed, so a column chart shows Cali's sums only, a stat tile sums Cali's rows only, and a map's shapes, row counts and tooltip sums count Cali's rows only. When the widget has a filter of its own on the same column, a row must match both: a widget that keeps Cali and Buga, filtered by the dashboard to Cali and Palmira, shows Cali.

A widget that a filter in force does not narrow still shows all its rows, and says so in a line at its top: **Not filtered by: Municipality**. That happens when its table has no column of that name (or has it with another kind, such as `round` published as text), or when the widget ignores the filter (see [Ignoring dashboard filters](#ignoring-dashboard-filters)). It is not an error, and a filter set to `All` is never listed. For a filter to narrow every widget, the indicator scripts must publish its column; see [Preparing indicators for dashboard filters](#preparing-indicators-for-dashboard-filters).

### Defining a dashboard's filters

Analysts and admins define a dashboard's filters from its **Dashboard actions** menu, with **Filters**. The form lists the dashboard's filters in order, each with:

- a **Label**, 1 to 40 characters once leading and trailing spaces are removed, different from the other filters' labels of the dashboard whatever its case, and shown exactly as typed, never translated;
- a **Column**, chosen among the columns of the published tables of the dashboard's widgets' indicators. Each is offered with its kind and how many of the dashboard's widgets it would narrow (`municipality · text · narrows 4 of 5 widgets`), counting the widgets whose table has it with that kind and that do not ignore it. A column published as an integer by one script and as a decimal by another is offered once, as number; one published as text by one and as a number by another is offered once per kind. A dashboard has at most one filter per column. A saved filter whose column no table has any more stays, marked `in no table`;
- an optional **Default**, set with **Set default** in the same form members use, and removed with **Clear default**. A column of another kind drops the default;
- buttons to move it up or down, and to remove it.

**Add filter** adds the next column not filtered yet, labelled after it (`visit_round` reads "Visit round"); it is unavailable at 8 filters, with a line saying why, and while the dashboard's indicators have published no table ("There are no columns to filter on yet…"). **Save filters** saves the whole list at once, for every member; **Cancel** (or Escape) closes the form without a change. A label that is empty, too long or taken is explained under it, and what you typed is kept. After saving, the dashboard shows the saved filters at their defaults (the values in the address go), a message confirms it ("Filters saved."), and the focus returns to the actions button.

- A filter is checked against nothing but its own shape: it can name a column before the indicator that will have it publishes, as a widget can.
- Viewers see the filters and change their values, but have no **Filters** action, and the API refuses their requests (403).
- Two members saving the same dashboard's filters: the later save wins. Filters saved on a dashboard another member has just removed are not saved, and the first dashboard opens with a notice, as for any change to a removed dashboard.
- Duplicating a dashboard copies its filters; the two lists change independently afterwards.

### Filtered views in the address

The values in force are kept in the page address (`…/dashboards/<dashboard id>?f=…`), so a link opens the dashboard with the same values for any member, and the browser's Back button undoes a change. Without `f`, every filter is at its default; a link made after clearing a filter keeps it at `All`, even when it has a default. Another dashboard, opened from its tab, starts at its own defaults.

A value in the address for a filter the dashboard no longer has, or that its filter cannot take, is dropped instead of failing the page, and a notice above the dashboard says so (`The filter "round" was removed.`); it goes away when you change a filter or open another dashboard. A link holding many long values can be longer than some proxies accept for an address, as with clean data's filters; keep shared views to a few values.

## What an indicator script must provide

An indicator script returns one DataFrame, published as the table named after the indicator, with **at least one numeric column** (integer or decimal) and any other columns you need (see [Rules for what scripts return](scripts.md#rules-for-what-scripts-return)). There is no fixed set of columns. These are the conventions the dashboard follows:

| Column | Used as |
|---|---|
| `value` | The number a widget plots by default. A table without a `value` column starts with its first numeric column. |
| `numerator`, `denominator` | Let a widget plot a **ratio of sums** (a rate) instead of a single column. |
| Any text, date or true/false column | A **dimension**: a widget groups by it (a municipality, a `round` or `year` for a trend) and filters on it. |
| A geometry column | Where a map gets its shapes; see [Maps](#maps). |

The columns listed in the widget form are the ones the published table has. A widget plots a numeric column or a ratio of two numeric columns.

### Preparing indicators for dashboard filters

A [dashboard filter](#dashboard-filters) narrows only the tables that have its column, and the dashboard computes nothing from the cleaned data: it reads what the script published. To let a dashboard be filtered by municipality, sex and round, every indicator on it should publish those columns:

- **Group by every column the dashboard groups or filters by**, not only by the one a chart shows: `groupby(["municipality", "sex", "round"])` instead of `groupby("municipality")`. Nothing is lost: a widget grouped by `municipality` adds the rows of each municipality back up, so the same table serves the chart with and without filters. The [Count by group and Rate by group templates](scripts.md#3-indicator-scripts-computecleaned-ctx) take a list of columns, `GROUPS`, for this.
- **Publish measures that can be added up.** Counts and sums can. A rate cannot: publish its `numerator` and `denominator` (the Rate by group template does) and plot it as a **ratio**, so `sum(numerator) / sum(denominator)` stays right over any rows the filters keep. For an average, publish the sum and the number of cases and plot their ratio. A median, or a count of distinct people across rounds, cannot be added up again: such an indicator does not suit dashboard filters.
- **Count each submission once per row.** A column where one submission falls in several rows, such as a `select_multiple` question spread into one row per option, counts that submission once per option when its rows are added up. Filtering by one value of it is fine; adding it up across values is not.
- **Name and type a dimension the same in every indicator**: always `municipality` as text, never `mpio` in one indicator and `municipality` in another, nor text in one and a number in another. A filter finds its column by name, and only then by kind. An integer and a decimal column count as the same kind, as do a date and a timestamp column, so a pandas column that turned into decimals because it has empty values still matches.
- **Keep in mind the size.** Each column you group by multiplies the rows the table can have. The limit of 5,000 rows a widget plots counts its groups after adding up, not the rows of the table, so it rarely matters; a histogram without a group column is the exception, since it reads row by row.

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

Every widget has a title, a type, one **indicator**, what to **plot** (a measure or a ratio, see below), the column to **group by**, for many types a second column to split each group by (the [series](#widgets-with-a-series)), up to 50 **filters**, and for a map its **geometry column** and up to 5 [**tooltip columns**](#tooltip-and-tooltip-columns) and a [**basemap**](#basemap). A bar, column or line chart with no series, a histogram and a box plot also have a [**color**](#widget-color-and-scale), a map and a heat map a [**scale**](#widget-color-and-scale), and a box plot an [**orientation**](#box-plot). A widget form shows a live preview while you edit.

| Type | Shows | Group by | Notes |
|---|---|---|---|
| **Stat tile** | One number: the measure summed, or the ratio of sums, over every row the query matches. | Optional | Says over how many rows it was summed. |
| **Bar chart** | Horizontal bars, one per group value. | Required | Can be [ordered](#bar-and-column-order). |
| **Column chart** | Vertical columns, one per group value. | Required | Can be [ordered](#bar-and-column-order). |
| **Line chart** | A line through the group values. | Required | Group values are in natural order (2 before 10), so a `round` column of 1, 2, 10 reads as a trend. |
| **Histogram** | How many rows fall in each of a number of equal-width ranges of the plotted value. | Optional | 10 ranges by default, up to 100. Without a group column each row of the table is counted; with one, each group's value. |
| **Pie chart** | One slice per group value, sized by its share of the total. | Required | See below. |
| **Map** | Regions or points, colored by the plotted value. | Required | Needs a geometry column; see [Maps](#maps). |
| **Heat map** | Groups as rows and [series](#widgets-with-a-series) as columns, each cell shaded by its value. | Required | Needs a series column; see [Heat map](#heat-map). |
| **Population pyramid** | Two series values as bars to the left and to the right of a central axis, one row per group. | Required | Needs a series column and its two sides; see [Population pyramid](#population-pyramid). |
| **Likert bars** | Each group's composition over an agreement scale, centered on the neutral value. | Required | Needs a series column and a scale; see [Likert bars](#likert-bars). |
| **Cross table** | A table of groups by series, with totals. | Required | Needs a series column; see [Cross table](#cross-table). |
| **Box plot** | How the rows of each group are spread: a box from the first to the third quartile, the median, the mean, whiskers and outliers. | Optional | Each row of the table is one value; see [Box plot](#box-plot). |

- **Rows with no value** in the plotted measure are left out of every widget.
- A group with no value in the group column is shown as **(none)**.
- The **pie chart** orders slices from the largest. When there are more than 8 groups, the 7 largest keep their slice and the rest are folded into one **Other** slice. A group whose value is zero or negative has no share and is not drawn; the widget says how many, and **Show as table** lists them. The slices' colors, and which groups are folded into **Other**, come from the rows that match the widget's *own* filters, not the dashboard's, so a member who changes a dashboard filter sees each slice in the same color. A group that has no share in those rows (its total there is zero or negative) but a value above zero under a dashboard filter is added to **Other**, which keeps its neutral color.
- Colors come from the dashboard's [palette](#colors), with the widget's own [color or scale](#widget-color-and-scale) where its type has one, and, light or dark, follow the theme.

### What to plot: a measure or a ratio

- A **measure** is a numeric column of the table. `value` is chosen when the table has it, otherwise its first numeric column. Rows that share a group value are **summed**.
- A **ratio** is a numerator column and a denominator column. Rows that share a group value are summed in each, and the widget plots `sum(numerator) / sum(denominator)`. A group whose denominator sums to zero has **no value**.
- A [box plot](#box-plot) is the exception: it sums nothing. Each row is one value, and for a ratio that value is the row's own numerator divided by its own denominator (a row whose denominator is 0 has no value and is left out).

### Duplicate rows are summed

Rows that share the same group value are **summed** before they are plotted, in the database (except in a [box plot](#box-plot), which takes each row as one value and never sums). A table that has one row per form version, for example, still charts as one bar per municipality: two rows for `Cali`, with values 40 and 35, plot 75. Summing is right for counts and wrong for rates, which is why a rate should carry its `numerator` and `denominator` and be plotted as a **ratio**: three rows for `Cali` with numerators 40, 10 and 50 and denominators 100, 50 and 50 plot 0.5, not the sum of three rates.

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

Line charts keep their natural order (`2` before `10`) and, like pie, histogram and map widgets, have no order setting. A [box plot](#box-plot) can be ordered by its **median** only.

### Filters

Filters work as in the [clean data dashboard](clean-data-dashboard.md#filters): **contains**, **is exactly** and **is one of** for text; **is between** for numbers, dates and timestamps; **is one of** for true/false; **is empty** and **is not empty** for any column. A row has to match all of them. A widget takes at most 50 filters, and **is one of** takes at most 100 values. Filters are added once the indicator's table is published, because they are chosen from its columns. These are the widget's own filters, the same for everyone; a dashboard's [filters](#dashboard-filters) narrow it further, on each member's screen.

## Widgets with a series

A widget groups its indicator's table by one column. A **series column** splits each group by a second one: the water sources inside each municipality, the sexes inside each age band, the answers inside each municipality. The widget then has one value for every pair of group and series value, plus a **total** for each group, for each series value and for all its rows.

| Type | Series column |
|---|---|
| Bar, column and line chart | Optional |
| Heat map, population pyramid, Likert bars and cross table | Required |
| Stat tile, histogram, pie chart, map and box plot | Not allowed |

The series column has to be another column than the group column. Everything else about the query is the same: the measure or ratio, the widget's filters and the dashboard's filters.

- **Rows that share a group and a series value are summed**, or combined as a ratio of sums, as rows that share a group are.
- **Totals come from the rows, never from adding the cells.** For a rate, a group's total is the sum of its numerators over the sum of its denominators, not the sum or the mean of its cells' rates.
- **Order.** Series are listed in natural order (2 before 10). So are the groups of a line chart, a heat map, a pyramid, Likert bars and a cross table, which have no order setting; bars and columns keep the [order](#bar-and-column-order) of their group column or the one you choose. Rows with no value in the series column come next as **(none)**, and the folded series last as **Other**.
- **At most 8 series** in a bar, column or line chart, and **30** in a heat map or a cross table. When the widget's rows hold more, the ones with the largest totals are kept, one fewer than the limit, and the rest are combined in one **Other** series. **(none)** is never folded into **Other**.
- **Colors and legend stay put.** The series, their order and their colors come from the rows that match the widget's *own* filters, not the dashboard's, with the dashboard's [palette and colors kept by value](#colors). A member who changes a dashboard filter keeps every series in its color and in the legend, even one that no longer has rows. Editing the widget's own filters can change them.
- **Row limit.** The limit counts cells (a group and a series value). A group that would cross it is left out whole, with the groups after it, and the widget says the limit was reached. The totals of series and of all rows still cover every row the widget matches.
- **Show as table** is the cross table: groups as rows in the chart's order, series as columns, and the totals.

### Bars and columns by series

A bar or column chart with a series has a **layout**:

- **Grouped** (the default): one bar or column per series value, side by side. With many groups and series the bars get thin: twelve municipalities by five sources are sixty bars, so give the widget the full width of the grid, or use a stacked layout.
- **Stacked**: the series end to end from the axis, so the bar's length is the group's total. Negative values stack on the other side of the axis.
- **Stacked to 100%**: each bar shows the shares of its total, so you compare compositions. The tooltip gives the share and the value. A value below zero has no share: it is left out and the widget says how many.

Stacked and 100% layouts add the series up, so they plot a **measure**, not a ratio (rates do not add). A rate by two dimensions belongs in grouped bars, a line chart, a heat map, a pyramid or a cross table. With an [order](#bar-and-column-order) by the plotted value, a bar's value is its total over all series.

A **line chart** with a series draws one line per series value, through the groups in natural order. A series with no value for a group has a gap there.

### Heat map

Groups are rows (in natural order, so `5-9` comes before `10-14`), series values are columns, and each cell is shaded on the same sequential scale as the maps (the dashboard palette's scale, blue in Tamiza, unless the widget [picks another](#widget-color-and-scale); see [Colors](#colors)), from the smallest to the largest value shown, with a legend of that range. A cell with no value is drawn empty with a hatch, which is not the same as a cell whose value is 0.

### Population pyramid

Pick the two values of the series column that go to the **left** and to the **right** of a central axis, for example `Female` and `Male`. The group values (age bands) are the rows in natural order (`0-4`, `5-9`, `10-14`), the first at the bottom, and both sides use one scale, measured outward and labelled without signs. Values follow the indicator's format: counts as numbers, a percent indicator as percentages.

- A row with any **other** series value is left out. A row with no group or no series value draws no bar. The widget says how many rows it left out.
- If one side has no rows (a dashboard filter on `sex` hides it), that side is drawn empty and the widget is not stale.

### Likert bars

For agreement scales. In the form you order the scale's values (2 to 11) and mark each one **negative**, **neutral** or **positive**: negatives first, at most one neutral, at most 5 on each side. Each group is one bar of its composition in percent, centered on the line between negative and positive: negatives extend left with the values nearest the neutral nearest the center, the neutral share is split in half across the line, and positives extend right. Each value's colour follows its side, a red arm and a blue arm of increasing strength. The tooltips give each value's share and its count.

Values outside the scale (`Don't know`) are left out and counted in a note. Likert bars plot a measure, usually counts of answers.

### Cross table

A table of groups by series values (the groups in natural order), with the indicator's format in every cell, a **Total** column, a **Total** row and the grand total. A pair without rows reads "—". A table wider than its widget scrolls inside it and keeps the group column in view. It is also what **Show as table** gives for every widget with a series; for stacked-to-100% bars and Likert bars it lists each cell's share.

### Preparing an indicator with two dimensions

A series widget reads a table with **two text dimension columns and one number**. Publish one row per combination:

```python
def compute(clean, ctx):
    df = clean["main"]
    out = df.groupby(["age_band", "sex"], dropna=False).size().reset_index(name="value")
    out["age_band"] = out["age_band"].astype(str)
    return out
```

- Give every dimension the same name in every indicator, so a [dashboard filter](#dashboard-filters) can narrow them.
- Order bands so that they sort naturally as text: `0-4`, `5-9`, `10-14` sort right (a run of digits compares as a number), `Under 5` does not. To order bars by something else, publish a sort column (see [Bar and column order](#bar-and-column-order)).
- For a Likert widget, keep the answers as their **choice labels** and publish a count per group and answer; the scale in the widget names the values and their sides.
- For a rate by two dimensions, also publish `numerator` and `denominator`, as for any rate.

## Box plot

A box plot shows **how the values of each group are spread**: household income by municipality, travel time by zone. Where a bar adds the rows of a group up, a box plot takes **each row of the table as one value** of its group (its measure, or for a ratio its numerator divided by its own denominator) and never sums them. It draws one box per value of the group column, or one box over every row when it has no group column.

- **The box** runs from the first quartile (Q1) to the third (Q3), so it holds the middle half of the values. A line across it is the **median** and a cross is the **mean**.
- **The whiskers** reach the smallest and the largest value within 1.5 times the box's length (Q3 − Q1) beyond it, as in a Tukey box plot. Values beyond them are **outliers**, drawn as points.
- **The figures** are computed in the database over every row the widget matches, with the quartile method pandas' `quantile` uses by default (linear interpolation), so `df.groupby("municipality")["value"].quantile([0.25, 0.5, 0.75])` on the published table gives the same numbers. Rows with no value, a ratio row whose denominator is 0, and values that are not finite numbers are left out of every box.
- **At most 100 outliers** are drawn for each box, the ones farthest from its median; the widget says how many it did not draw, and the tooltip and **Show as table** count them all. A box of a single value is drawn as one line.
- **Order.** Boxes follow the natural order of the group (2 before 10, `0-4`, `5-9`, `10-14`), the missing group last. **Order boxes by** **Median**, ascending or descending, sorts them by their median; a box plot cannot be ordered by a column, nor ordered without a group column.
- **Orientation.** **Vertical** (the default) puts the first box on the left; **Horizontal** puts it at the top and suits long group names. The orientation is saved with the widget and changing it does not read the table again. A duplicated widget, a widget moved to another dashboard and a restored version keep their orientation.
- **Color.** A box plot is drawn in one of the dashboard palette's colors, like a histogram (see [Widget color and scale](#widget-color-and-scale)), and a box takes the color [kept for its group value](#colors-kept-by-value).
- **Numbers.** The figures are written in the indicator's format and unit, except that a `count` indicator's figures have **two decimals**, because a median or a mean falls between whole numbers; a box's number of values and of outliers are whole numbers.
- **A box plot has no series**: saving one with a series column is refused.
- **Show as table** lists one row per box in the chart's order, with the group, the number of values, the minimum, Q1, the median, Q3, the maximum, the mean and the number of outliers. The scheduled emails list the same table in the body and in the workbook, and the printed dashboard shows the chart or the table as the member left it.
- **The row limit counts boxes**, not the table's rows: a box plot over a table of 200,000 rows with 40 groups draws 40 boxes and reads every row.

### Preparing an indicator for a box plot

A box plot reads a table with **one row per unit** (household, person, visit), its value, and the dimensions to group and filter by. Do not group the units in the script: the widget computes the statistics, and applies dashboard filters to the rows first.

```python
def compute(clean, ctx):
    households = clean["main"]
    return pd.DataFrame({
        "municipality": households["municipality"],
        "household_id": households["_id"].astype(str),
        "value": households["income"],
    })
```

- Return the unit's **id as text**, after the dimensions. It keeps every row unique, so the dashboard view does not warn that rows share their dimensions and will be added up; and a column that follows the dimensions keeps new widgets from starting grouped by the id.
- The same table also feeds bars that count or sum and [dashboard filters](#dashboard-filters), since the widgets add its rows up.
- Publish only the units the dashboard needs: every box plot reads all the rows of the table each time it is drawn, and the statement has a time limit (see [Limits](#limits)).

## Maps

A map draws the shapes of its rows, colored by the plotted value on a sequential scale of seven steps (the dashboard palette's scale unless the widget [picks another](#widget-color-and-scale); see [Colors](#colors)), with the lowest and highest value shown as a legend. It reads the shapes from a **geometry column** that the indicator script publishes: a text column of WKT, chosen in the widget form (any text column of the table is offered).

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
- **Images of a map leave the basemap out.** The tiles, the Google logo and the credits are not in an exported image (see [Exporting a widget](#images)); printing keeps them.
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

## Colors

A dashboard draws its widgets in one **palette**: eight categorical colors in a fixed order, and a default sequential scale for its maps and heat maps. It can also keep a color for chosen values, so the same category reads in the same color in every widget. Analysts and admins set both from **Colors** in **Dashboard actions**, next to **Filters**; every member sees the dashboard in them, viewers included. A viewer is not offered **Colors**, and the API refuses their request (403). The action is not offered either while a [version is shown](#viewing-a-version). A dashboard uses one palette: its widgets do not mix palettes. A widget picks its own color, or its own scale, from that palette in the widget form (see [Widget color and scale](#widget-color-and-scale)).

### Palettes

| Palette | Its eight colors, in order | Its scale for maps and heat maps |
|---|---|---|
| **Tamiza** (the default) | blue, orange, jade, amber, pink, green, violet, red | Blue |
| **Institutional** | navy blue, turquoise, burgundy, sky blue, mustard, slate, salmon, olive green | Teal |
| **Earth** | terracotta, indigo, ochre, wine, moss, brown, plum, teal | Orange |
| **Vivid** | purple, cyan, lime, fuchsia, green, blue, crimson, orange | Purple |

- **Choosing.** **Colors** shows each palette as a card with its name, its eight colors in order and its scale as a gradient; the dashboard's current palette is selected. Pick one and **Save colors**. The change shows at once, without loading the widgets' data again.
- **Fixed sets.** The palettes are a fixed set. You cannot type a color code or add a palette of your own. Each one is checked, in the light and in the dark theme, so that neighbouring colors stay apart for readers with the common color-vision deficiencies and for full-color readers (the eighth counts as the first one's neighbour, since a pie of eight puts them side by side), and so that no color is too pale or too dull.
- **Tamiza is today's colors.** A dashboard has the palette Tamiza until another is chosen, so a dashboard that existed before palettes looks as it did.
- **Other.** The folded series or slice called **Other** is the same neutral grey in every palette, whatever colors are kept.
- **Not covered.** Stat tiles have no color of their own, and the red and blue arms of [Likert bars](#likert-bars) are the same in every palette.
- **Printing.** A printed dashboard uses its palette, with **Background graphics** off, as the other colors do (see [Printing a dashboard](#printing-a-dashboard)).
- **Versions.** The palette and the colors kept by value are part of the dashboard's versions: saving them with a change makes a version, and viewing a version draws it in its own colors. Restoring one brings them back, and a version recorded before palettes restores the palette Tamiza with no colors kept. Saving colors without changing anything makes no version (see [Version history](#version-history)). Duplicating a dashboard copies its colors, and the copy changes them on its own.

### Widget color and scale

A bar, column or line chart with no series, a histogram, a box plot, a map and a heat map can be given a color or a scale in the widget form, from the dashboard's palette. The preview shows the choice in the dashboard's colors before the widget is saved, and choosing asks the server for nothing: the data is not loaded again.

| Widget | Choice in the form | When nothing is chosen |
|---|---|---|
| Bar, column or line chart with **no series**, histogram and box plot | **Color**: one of the palette's eight colors | Color 1 |
| Map and heat map | **Scale**: one of seven sequential scales | **Palette's scale** |
| Any other widget, and a bar, column or line chart **with a series** | None: the colors follow the values (see [Colors kept by value](#colors-kept-by-value)) | |

- **Color.** The eight colors of the dashboard's palette are numbered swatches. Each is read to a screen reader by its number and the name of its hue ("Color 3, jade"), the name of the chosen one is also written under the swatches, and the arrow keys move between them. A column or bar chart with no series draws every bar in the widget's color, except a bar whose value has a [color kept](#colors-kept-by-value), which has that color.
- **Scale.** **Palette's scale** is the default and uses the scale of the dashboard's palette (Blue in Tamiza, Teal in Institutional, Orange in Earth, Purple in Vivid). The other choices are Blue, Teal, Green, Orange, Red, Purple and Gray, each shown as its seven steps and named in words, and the map's shapes and legend, or the heat map's cells and legend, use that scale's steps.
- **A place, not a color code.** A widget keeps its number, not a color. When the dashboard's palette changes, or the widget moves to another dashboard, it keeps its number and is drawn in the palette of the dashboard it is on: color 3 of Tamiza becomes color 3 of Vivid. A widget on **Palette's scale** follows the new palette's scale, and one that has a scale of its own keeps it.
- **Kinds that take none.** The form does not offer a color to a widget of another type or to a chart with a series, nor a scale to anything but a map or a heat map, and the API refuses them (400, with an error on the color or the scale). A widget that is given a series, or changes into another type, is saved without its color; the form keeps the choice until the widget is saved, so changing back finds it.
- **Versions.** A widget's color and scale are part of the widget, so changing one makes a version that says the widget changed ("Widget “Coverage” changed"), and saving the form without changing anything makes none (see [What makes a version](#what-makes-a-version)). A widget saved before colors existed is drawn in color 1, or on its palette's scale, as it always was.

### Colors kept by value

A color kept for a value of a column gives that value one of the palette's eight colors on the whole dashboard: for example `sex` = `Female` in color 5, or `answer` = `No` in color 8. Wherever a widget of the dashboard colors by that column, that value then has that color:

- the series of a bar, column or line chart with a series, in the bars, the lines, the legend and the tooltips;
- the two sides of a population pyramid;
- the slices of a pie chart;
- the bars or columns of a bar or column chart with no series, by its group column, so one value can be highlighted: the bar of `Rionegro` in color 2 while the others keep the widget's color;
- the boxes of a box plot, by its group column, in the same way.

A color kept does not change maps, heat maps, Likert bars, cross tables, histograms, stat tiles or a line chart with no series.

**Colors** lists the colors kept, in the order they were saved. Each has:

- a **column**, chosen among the text columns of the tables of the dashboard's indicators, the same columns the [filters form](#defining-a-dashboards-filters) offers;
- a **value**, which you pick from the column's values, suggested as you type, or write as any text;
- a **color**, one of the palette's eight, shown as numbered swatches. Each is read to a screen reader by its number and the name of its hue ("Color 3, jade"), and reached from the keyboard, where the arrow keys move between the swatches.

**Add a color kept by value** adds a row, up to 64 on a dashboard, and **Remove** takes one out. **Save colors** saves the palette and the whole list at once; saving what the dashboard already has sends nothing.

- **One value, one color; one color, one value.** In a column, a value and a color each appear at most once, so two values of a widget never share a color. The same color may be used in two columns (`Female` of `sex` and `Rural` of `zone`, both in color 5). A list that breaks this is refused, and the form shows the message on the row it names, such as "sex already uses color 5.".
- **Values are matched as exact text.** Case counts: a color kept for `piped` does not color `Piped`. A missing value, shown as **(none)**, and **Other** never take a color kept. A value can hold any character but NUL: a NUL character is refused, next to the value, with "The value cannot contain a NUL character."
- **Entries are not checked against the data.** A value that no widget shows stays saved and changes nothing, so a value can be prepared before the first run or after the data changes.
- **The empty text.** An empty text is a group value, so the API accepts a color kept for it, but the form asks for a value on every row it adds: only the API and MCP can keep a color for the empty text, and **Colors** lists such an entry and saves it as it is.
- **The other values take the palette in order.** A value with no color kept takes the palette's colors in the widget's series order, after the colors kept in that widget are set aside. A chart whose series are `Piped`, `River` and `Well`, with color 1 kept for `Well`, draws `Well` in color 1, `Piped` in color 2 and `River` in color 3. In a chart with no series, a bar with no color kept is drawn in the widget's own color.
- **Colors follow the value, never its rank.** A [series](#widgets-with-a-series) or a pie slice keeps its color when a dashboard filter changes the rows that are shown, since both come from the widget's own rows.
- **Two indicators with the same column name share its colors.** Colors kept are matched by column *name*, as [dashboard filters](#which-widgets-a-filter-narrows) are, not by indicator. If two indicators have a column called `type` and it means different things, a color kept for `type` = `A` colors the value `A` in both. Give each dimension a name of its own in the indicator scripts when it means something different, and the same name everywhere when it means the same.

## Building the dashboard

The **Add widget** button opens the widget form, and the widget is added to the open dashboard. Each widget has one **widget menu** (the button with three dots at the right of its title), with **Show as table**, under the heading **Export** the formats **PNG image**, **SVG image** and **Excel workbook (XLSX)**, and, for analysts and admins, **Edit widget** and **Remove widget**; a line sets each part apart from the next. It is named after the widget ("Options for “Coverage by municipality”"), so a screen reader tells it from the **Dashboard actions** menu in the dashboard's heading. A `viewer` finds the first two parts only. See [Show as table](#show-as-table) and [Exporting a widget](#exporting-a-widget). Above the widgets, editors see how many of the allowed widgets the dashboard holds; when the limit is reached the line says so and **Add widget** is off. All of this is for **analysts and admins**: a `viewer` sees every widget, switches to tables, [exports](#exporting-a-widget) and prints, but has no add, edit, remove or move controls, and the API refuses their requests.

- **A dashboard holds at most 50 widgets** by default. The limit counts the widgets of one dashboard, so a full dashboard does not stop another from getting more. The page shows how many are used ("12 of 50 widgets on this dashboard") and, at the limit, says so ("This dashboard has the most widgets it can hold (50). Remove one or move it to another dashboard to add another.") and disables **Add widget**. An operator can change the limit; see [operations.md](operations.md#common-changes).
- Every member sees the same dashboards: they are saved with the project, not per person. If two members change the same widget at the same time, the last change is kept.
- Removing a widget asks for confirmation and removes it for everyone. The dashboard's kept versions still hold it: restoring one of them brings it back, and also undoes the changes made after that version (see [Restoring a version](#restoring-a-version)).

### Ignoring dashboard filters

On a dashboard with [filters](#dashboard-filters), the widget form lists them under **Dashboard filters**, one box each (**Narrow by Municipality**), all checked for a new widget. Uncheck a filter for a widget it must not narrow, such as a total for every municipality next to charts members filter by municipality: when a member sets that filter, the widget keeps all its rows and says **Not filtered by: Municipality**. A widget ignores at most 8 filters.

- A widget ignores a filter **by its column**, not by its label: it keeps ignoring it when the filter is renamed, removed and defined again on the same column, when the widget is moved to another dashboard with a filter on that column, and in a copy of the dashboard.
- The form's **preview** applies the values in force on your screen when you opened the form, except the filters the widget ignores, and says what it is filtered by ("Preview filtered by: Municipality"); checking or unchecking a box shows the preview again.

### Moving a widget to another dashboard

When the project has more than one dashboard, **Edit widget** shows a **Dashboard** field after the title, set to the widget's own dashboard. Choose another one and save: the widget keeps its title, type, query, the dashboard filters it ignores and its size, and goes to the left edge below the widgets of its new dashboard. It leaves the open dashboard, a message names the dashboard it went to ("Widget Coverage moved to Field."), and the focus moves to the open dashboard's heading.

- A widget that cannot be drawn because its table lost a column can still be moved: change only the dashboard, and it moves and keeps naming what is missing. Changing anything else as well checks the whole widget again.
- A dashboard that already holds the most widgets it can ("A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first."), or that another member has just removed ("The dashboard does not exist."), refuses the widget with an error next to the field; the form stays open and the widget stays where it was. A removed dashboard then leaves the field and the tab row.
- Adding a widget offers no dashboard choice: a new widget goes to the open dashboard.

### The grid

Each dashboard has its own grid of 12 columns. A new widget goes below the existing widgets of its dashboard, 3 columns by 2 rows for a stat tile, 6 by 6 for a map, 6 by 5 for a heat map, a population pyramid and a cross table, and 6 by 4 for the other types; a widget is at most 40 rows high.

- **Drag** a widget by the handle at the left of its title to move it, and drag the corner at the bottom right to resize it. A dashed outline shows where it will land. A widget dropped on others pushes them down; widgets that do not overlap stay where they are.
- **Keyboard.** Focus the move handle and press the arrow keys to move the widget one cell; hold **Shift** with the arrow keys to resize it. On the resize handle, the arrow keys resize. The new place is announced to screen readers. The handles are hidden on narrow screens.
- **Saving.** A move or resize is saved when the dragging has been still for a moment (about 0.4 seconds), not on every step. If it cannot be saved, the page tells you and shows what the server has.
- **Reading order** is top to bottom, then left to right, whatever the order the widgets were created in. Keyboard and screen readers follow it, and so does the screen below 768 px wide, where the grid becomes one column and each widget keeps its height. A printed dashboard keeps the 12 columns (see [Printing a dashboard](#printing-a-dashboard)).

## Show as table

Every widget except the stat tile has **Show as table** in its [widget menu](#building-the-dashboard) (shown once the widget has rows to draw), a checked item while the table is shown, that replaces the picture with the numbers it plots, formatted like the chart (a histogram lists its ranges and their counts, a box plot its boxes with their figures, a map its groups with their tooltip columns, written as in the tooltip, and the kind of shape each has). The button above the grid, **Show all as tables**, does the same for every widget of the dashboard at once, in reading order, and resets the choice made on single widgets. Choosing **Show as table** closes the menu, and a screen reader hears that the widget "is shown as a table" or "as a chart". Each chart also carries a text description for assistive technology. A widget with a [series](#widgets-with-a-series) shows its cross table: groups as rows, series as columns, and a **Total** column and row; for bars stacked to 100% and for Likert bars the cells are shares.

## Export

**Export**, the download button at the end of a published indicator's entry in the list, downloads the **whole published table of that indicator**, exactly as published and without the widgets' or the dashboard's filters, as CSV or XLSX. Viewers can export. It is the same file, the same single-use download link and the same limits as the [clean data exports](clean-data-dashboard.md#exports): the CSV has a byte order mark, a geometry column is exported as its WKT text, and an XLSX is refused above 1,048,575 rows. While one indicator's file is being prepared only its button shows that it is busy, and the page ends with one message. Exporting one indicator never delays another indicator's publishing. To take one widget out of Tamiza, as an image or with the data it plots, see [Exporting a widget](#exporting-a-widget).

## Exporting a widget

Every widget's [widget menu](#building-the-dashboard) has an **Export** group, under its own heading, with three formats: **PNG image**, **SVG image** and **Excel workbook (XLSX)**. They are items of the menu itself, not a second menu. **Every member has them**, viewers included, on every widget of the project's own dashboards and at every width from 360 pixels. It is not printed, and it is not in the widget form's preview or in the script editor. This is not the [Export of an indicator's whole table](#export): a widget's file holds only what the widget plots, with the dashboard's filters in force.

- **Formats that cannot be made yet stay in the group**, announced as unavailable, with the reason under them:
  - while the widget is loading ("The widget is still loading."), after it failed to load, and while its indicator has not published;
  - when its table no longer has a column it needs, naming the column;
  - when no row matches ("No rows match this widget’s query.").
- **A map's images** also wait while the map is being drawn or moved ("The map is still being drawn."), and are unavailable when every row was left out ("The map has no shape to draw."). Its workbook stays available in both cases, since it only needs the rows.
- **While one file is prepared**, only that widget's menu button shows that it is busy ("Options for “Coverage by municipality”, preparing its export"). Its menu still opens: the three formats are unavailable, each reading "Preparing the export…", and **Show as table**, **Edit widget** and **Remove widget** work as always. The other widgets' menus are not affected. The export ends with one message.
- **While a version of the dashboard is shown**, the menu stays with **Show as table** and the formats, and without **Edit widget** and **Remove widget**: exporting changes nothing. Exporting never makes a [version](#what-makes-a-version).

### Images

**PNG image** and **SVG image** show the widget as you see it now, and are made in your browser: they send nothing to the server and save nothing. They use the same rows, the filter values in force, the chart or the table as you show it, your map view and, while a version is shown, that version's widget and colors.

- **What an image holds**, from top to bottom: the widget's full title (the screen cuts a long one), its notes ("Not filtered by …", the row limit), its caption, the chart or the table, the whole legend, key or scale (a pie's twelve slices, not the part that scrolls), what the widget left out (rows, outliers, groups), and a footer. The footer names the project, the dashboard and the version when one is shown, every filter of the dashboard with its value or "All", when the indicator was published as your page knows it, and when the image was made, in the project's time zone. There is no logo. Tooltips, buttons, map controls and focus marks are never in it.
- **Always on white, in the light colors** of the dashboard's palette and the colors kept by value, whatever your theme. The gaps between stacked segments and heat map cells are white. This holds while Tamiza has no dark theme: a theme switch has to keep a light scope for the images.
- **The SVG is a standalone file.** Its text stays text, so you can edit it in a vector editor, and it has no script, style sheet, embedded HTML or link; a group value that looks like markup is written as text. Fonts are the system's: a program that lacks the font substitutes another.
- **The PNG is that SVG at twice its size**, marked 192 dots per inch, so Word and PowerPoint place it at its size on screen. It holds at most 16,000,000 pixels and 8,192 on a side: a very tall widget is drawn smaller than twice, and one that does not fit even at its size on screen is refused with "This widget is too large for a PNG image. Try the SVG image." The same message, with "could not be made", appears where the browser cannot draw it.
- **Tables are capped.** A widget shown as a table, or a cross table, gives an image of its first 100 rows and the columns that fit in 2,400 pixels, always with the first column, and says how many rows and columns it left out ("28 rows not shown in the image.") and that the workbook has them all.
- **Maps carry no basemap.** The image holds the shapes in your current view and the legend, never tiles, the Google logo or map credits, because tiles loaded from another origin cannot be drawn into a file and Google does not allow keeping a copy. When a basemap is shown, the formats say "Shapes and legend, without the basemap" and the image says "The basemap is not included in the image." [Print](#printing-a-dashboard) keeps the basemap.
- **Words and numbers** are in the interface language, and numbers read as on your screen.
- **The name of a file** is the project, the dashboard (the version's name while one is shown), the widget, `_v9` for version 9 and the day in the project's time zone, each part in lower-case ASCII with dashes: `census-2026_housing_coverage-by-municipality_2026-11-02.png`.

### Workbook

**Excel workbook (XLSX)** is prepared by the server, with the code that builds the widget's table in a [scheduled email](dashboard-reports.md)'s workbook, so the file, the email and **Show as table** list the same numbers. It is downloaded through the same single-use link and has the same file size limit as the other exports.

- **Its first sheet is the widget's table**, named after the title (shortened to Excel's rules): the same columns, totals, "(none)" and "Other" as in the email, with numbers as numbers in the indicator's format (a box plot over a count has two decimals), and text never a formula. A stat tile gives one column and one number. A map lists its groups, values and tooltip columns, with no kind of shape or geometry. It holds the rows the widget plots, up to the widget row limit.
- **Its second sheet, "Summary"**, names the project, the dashboard (and the version), the widget, the indicator, what is plotted, every filter of the dashboard with its value or "All" (or says that no filter applies), the filters that do not narrow the widget, when the indicator was published, when the workbook was made, the number of rows and the widget's notes.
- **The filters are those on your screen**, apart from the ones the widget ignores: the page sends them with the widget's settings, so a widget of a version the dashboard no longer has can also be exported. If another member changed the dashboard's filters since you opened the page, the export is refused with "The dashboard’s filters changed since the page was opened. Reload the dashboard and try again."
- **Newer data.** The file always reads the table as published at that moment. If a run published it after your page loaded, the message adds when, and that reloading the page shows it.
- **Words and formats follow your interface language and region** ("Resumen" in Spanish, regional dates in the Summary); names, labels and values stay as created.
- **When it is refused:** the indicator has not published (404), the table lost a column the widget needs (422, naming it), the file would pass the size limit (422), or reading took too long (503). A table that lost all its rows between your page and the file still gives a file, whose first sheet says why.

## Printing a dashboard

**Print**, after the dashboard's name, prints the open dashboard or saves it as PDF through the browser's own dialog. Every member has it, viewers included, once the dashboard has widgets. The Performance and Clean data dashboards have no Print action.

- **Getting ready.** Print lays the dashboard out as it will print, waits for the widgets still loading and for the maps' basemap tiles (at most 20 seconds), then opens the dialog. While it waits, the button is busy and screen readers hear "Preparing the print…". When the dialog closes, whether you printed or cancelled, the dashboard is back as it was, with the focus on **Print**. Printing saves nothing and changes neither the filter values nor the page address.
- **What the sheets show.** The project's name and form, the dashboard's name, when it was printed (in the project's time zone), the latest time one of its widgets' indicators was published, the dashboard filters with the values in force ("Municipality: Cali · Round: All"), and the widgets as you see them, charts or tables. They leave out the top bar, the tabs, every button and menu, the widgets' handles, the map controls, notices, tooltips and the list of indicators.
- **Layout.** Pages have 10 mm margins, and the widgets keep the 12 columns, positions and heights they have on a wide screen, even when you print from a phone. A widget that fits on a sheet is never split: it moves whole to the next sheet. Only a widget taller than a sheet continues on the next one. A map shows all its shapes, or the area you zoomed into, with its legend and the basemap's credit.
- **Tables.** A widget shown as a table prints the rows that fit in its box and says how many were not printed ("28 rows not printed."). **Export** gives every row.
- **Printing with Ctrl+P** (or the browser's menu) gives the same sheets, but it cannot wait: a widget still loading says on paper that it had not finished loading, and a map prints the tiles it already has. Prefer **Print**.
- **Orientation.** Choose it in the browser's dialog. In landscape, A4 and Letter print the dashboard at full size. In portrait, the whole dashboard is scaled down to the sheet's width, upright and with its 12 columns, so it reads smaller but more rows fit on each sheet. On a sheet smaller than A4 or Letter, such as A5 in portrait, Chrome and Edge cannot scale it down enough and cut off the right-hand columns: choose landscape or a smaller scale.
- **The browser's options.** Paper size, orientation and scale stay yours to choose; a smaller scale fits more rows on a sheet. Colors print without turning on **Background graphics**. The browser offers the project, the dashboard and the date as the PDF's file name, and prints the same in its own header when **Headers and footers** is on.
- **Browsers.** Printing is made for Chrome, Edge and Firefox on a computer. Other browsers print the same content but may not scale the dashboard to a portrait sheet or keep every widget whole.

## Version history

Tamiza keeps a history of each of a project's own dashboards. Every change saved to a dashboard records a new **version**: the dashboard as it is after the change, with its name, its filters with their defaults, its palette and colors kept by value, and its widgets with their title, type, query, the dashboard filters they ignore, position and size. The newest version is the dashboard as it is. Analysts and admins open the history from **Dashboard actions** with **Version history**; viewers are not offered it, and the API refuses their requests (403). Performance and Clean data have no history.

### What makes a version

- **Every change, however small:** adding the dashboard, renaming or duplicating it, saving its filters, saving its [colors](#colors) (the palette and the colors kept by value), and adding, editing, removing, moving or resizing a widget, or moving one to or from another dashboard. Adding a full stop to a widget's title is a version.
- **One action, one version.** A drag is one version, with the widgets it pushed down, and so is a run of arrow presses on a widget's handle: the page saves them together once the dragging has been still for a moment (see [The grid](#the-grid)). Two drags with a pause between them are two versions. A widget moved to another dashboard makes one version on each: one says the widget went to the other dashboard, the other that it came from it.
- **No change, no version.** Saving the filters, the colors or a widget's form without changing anything makes none.
- **A copy starts its own history.** **Duplicate** gives the copy a version 1 that says which dashboard it was duplicated from, and the original keeps its versions. A move or resize made on the original a moment before is saved first, as the original's own version.
- **Removing a dashboard removes its versions with it.**

These are not changes to the dashboard, and never make a version: runs and the data they publish (the widgets show the new data), synchronizations, the filter values a member picks, a member's basemap and map view, **Show as table**, printing, the order of the project's dashboards (**Move left** and **Move right**) and the scheduled email.

A dashboard that existed before version history began has a version 1, **State before version history**, that holds it as it was then. It is attributed to the last person who changed the dashboard or one of its widgets, at that time.

### Requests from AI clients

Changes that Claude or another AI client makes through the [MCP server](mcp.md) make versions too, attributed to the person it acts for and marked **via MCP**, with the request the client gave in quotes ("Add five water indicators"). The client gives that request with every change it makes for it, so all the changes of one request make one version of each dashboard they change, however many widgets it adds. A new version starts with the next request, when you or another member change the dashboard in between, or when 30 minutes pass between two of its changes. To undo a request, ask the client: it restores, on each dashboard the request changed, the version before it, which is a version of its own, marked **via MCP**, as any restore.

### Ten versions kept

Each dashboard keeps its 10 newest versions, the current one included: the change that makes the eleventh deletes the oldest. Numbers keep counting up and are never reused, so a dashboard that keeps versions 3 to 12 makes version 13 next. The 10 is a rule of the product, not a setting.

### The history

**Version history** first saves a move or resize still waiting for its pause, so the newest version holds the widgets where you left them, then lists the kept versions, newest first. Each shows:

- its number, with **Current** on the newest;
- what changed, up to three lines ("Widget “Water source” added", "Renamed to “Agua”", "Colors changed", "Widgets moved or resized"), then how many more ("and 2 more");
- for a version an AI client made, the request in quotes;
- who made it and when its last change was saved ("Ana Pérez · Oct 31, 2026, 10:32 AM"), followed by **via MCP** for a version an AI client made.

Names and titles are shown as they were at the time, as text. Every version but the current one has **View** (see [Viewing a version](#viewing-a-version)) and **Restore** (see [Restoring a version](#restoring-a-version)). **Close**, or Escape, closes the history and returns the focus to the actions button. On a phone the history fits the screen's width, and the list scrolls.

### Viewing a version

**View** shows the version on the dashboard page, in place of the dashboard as it is: its name as the heading, its filters at their defaults, its palette and colors kept by value, and its widgets with today's data. Each widget is drawn from the settings the version holds, so a widget removed since shows again, and one whose indicator or columns are gone since says so, as a [stale widget](#when-a-table-changes) does. A notice above it says which version it is, who saved it and when ("Viewing version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM. It is read-only."); the focus moves to it, so screen readers read it.

- **Read-only.** While a version is shown, the heading has no actions, **Print** included, and there is no **Add widget**, no count of widgets, no handles to move or resize a widget, and the widget menus have neither **Edit widget** nor **Remove widget**. **Show all as tables**, the widget menus' **Show as table** and [Export](#exporting-a-widget) group and the exports in the list of indicators stay: they change nothing. Printing from the browser's menu (Ctrl+P) while a version is shown prints the version, and the sheet names it under the heading ("Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM"), as the PDF's file name does.
- **Filters.** Change the version's filter values as on any dashboard; they change only your screen. The page address keeps the values of the dashboard as it is, so a link copied meanwhile opens the dashboard as it is, not the version. The values a text filter offers come from the indicators of the version's widgets, not of the dashboard's.
- **Back to the current version** shows the dashboard as it is again, with the focus on its heading. Opening another dashboard, or reloading the page, also leaves the version.
- **Restore version 9**, in the notice, restores the version shown, as **Restore** in the history does.

### Restoring a version

**Restore**, in the history or in the notice of a version shown, asks first ("Restore version 9?": "The name, filters, colors and widgets of “Housing” go back to how they were on Oct 31, 2026, 10:32 AM. The current state stays in the history."). On **Restore**, a move or resize still waiting for its pause is saved first, then the dashboard's name, its filters with their defaults, its palette and colors kept by value and its widgets become those of the version, for every member. The restore is a new version that says which version it restored ("Version 9 restored"), so the state it replaced stays in the history, and a restore can itself be undone by restoring the version before it. The dashboard keeps its address, its place among the dashboards and its [scheduled email](operations.md#email-optional), whose recipients, days and time stay as they were and whose next email shows the restored widgets.

The page then shows the dashboard as restored, with the focus on its heading, and one message says what was done ("Restored version 9 as version 13."). Values in the page address for filters the restored version does not define are dropped, with the notice for them (see [Filtered views in the address](#filtered-views-in-the-address)). A version that holds the dashboard exactly as it is changes nothing and makes no version, and the message says so ("“Housing” already is as in version 9.").

- **Names.** When another dashboard of the project now has the version's name, in any case, the dashboard keeps its current name, and the message says why ("It keeps the name “Agua”, since another dashboard is named “Water”."). The version's widgets and filters are restored all the same.
- **Widgets moved since.** A widget of the version that is now on another dashboard comes back as a copy, and the other dashboard keeps it as it is. The message names each copy and the dashboard that still has the original ("“Coverage” came back as a copy: the original is still on “Field”."). A widget whose indicator or columns are gone since comes back as it was, and says what is missing (see [When a table changes](#when-a-table-changes)).
- **The limit.** A version that holds more widgets than a dashboard may now hold, which happens only after an operator lowered the [limit](#limits), cannot be restored: the message says so ("A dashboard can have at most 20 widgets. Remove one or move it to another dashboard first."), and nothing changes.
- **Two members at the same time.** A restore replaces the dashboard as it is when it is saved, even when another member changed it after you opened the history; their change stays in the history as a version of its own. A change saved at the very moment of the restore, not yet in a version, is kept as a version before the restore ("Changes kept before a restore"). A version deleted meanwhile, by ten newer ones, is not restored: a message says it is no longer kept, the dashboard is unchanged, and the history opens again with the versions kept. A dashboard another member removed meanwhile is handled as any change to it (see [Two members at the same time](#two-members-at-the-same-time)).

## When a table changes

A run replaces each indicator's table on its own. The page shows when each indicator published and by which run; reload the page to see a newer one.

- A widget whose **indicator has not published** (it was never run, its script was removed, or every run so far failed before publishing) does not break the page. It says `<label> has not published yet`; analysts and admins get a link to the scripts.
- A widget that names a **measure**, a **ratio column**, a **group column**, a **geometry column**, a **tooltip column**, a **sort column** or a column of **one of its own filters** the new table no longer has shows **This widget cannot be drawn** and lists what is missing. Edit the widget to choose others, or restore them in the script; the widget draws again as soon as the table has them.

The other widgets keep working. A dashboard filter on a column the new table no longer has is not a missing column: the widget draws, unfiltered by it, and says **Not filtered by** with the filter's label.

## Limits

| Limit | Default |
|---|---|
| Widgets in a dashboard | 50 |
| Rows one widget plots (group values, after summing; a box plot's boxes) | 5,000 |
| Outliers a box of a box plot draws | 100 (the ones farthest from its median; all are counted) |
| Dashboards of a project's own | 10, and at least 1 |
| Length of a dashboard's name | 40 characters |
| Filters in a widget | 50 |
| Filters on a dashboard | 8 |
| Length of a dashboard filter's label | 40 characters |
| Values in an **is one of** filter | 100 |
| Ranges in a histogram | 100 (10 by default) |
| Series in a bar, column or line chart | 8 (the smallest are combined as Other) |
| Series in a heat map or a cross table | 30 |
| Values in a Likert scale | 2 to 11, at most 5 on each side of the neutral value |
| Tooltip columns in a map | 5 |
| Length of a tooltip column's label | 80 characters |
| Length of a text in a tooltip | 200 characters (cut with an ellipsis) |
| Indicators in a project | 100 |
| Length of a title | 200 characters |
| Colors kept by value on a dashboard | 64 |
| Length of the value of a color kept | 500 characters |
| Versions a dashboard keeps | 10, the current one included |

An operator can change the first two; the others are fixed. See [operations.md](operations.md#common-changes). A widget whose query matches more rows than the limit shows the first ones and says so ("Only the first 5,000 rows are shown"); add a filter or group by a coarser column. A query that takes longer than the dashboard time limit (25 seconds by default, shared with the clean data dashboard) is stopped with a message.

## What it does not do

- A widget plots **one indicator** with at most **two dimensions** (a group and a series); it does not combine several indicator tables in one chart, add a third dimension, plot two measures or sort its series by value.
- Its colors are those of the dashboard's [palette](#palettes), one of four, its [colors kept by value](#colors-kept-by-value) and, for a few types, the widget's own [color or scale](#widget-color-and-scale). You cannot type a color code, define a palette of your own, color a stat tile, change the red and blue arms of Likert bars or color by a threshold (good, warning, critical), and the scheduled email shows tables with no colors.
- Its basemap is a background and nothing more: there is no satellite imagery from anyone but Google, no street labels over the imagery, no basemap you define yourself, and no administrative boundaries of its own, since the shapes come from your script.
- It does not save a zoomed view as the map's starting view, and only maps have a tooltip with columns; a tooltip column's numbers are always summed, with no choice of minimum, maximum or average.
- Clicking a bar, slice or shape does not filter the other widgets, a filter's values do not depend on another filter (a department, then its municipalities), and there are no relative periods such as "the last 7 days".
- A dashboard filter matches columns by name: it cannot narrow a column called `municipality` in one indicator and `mpio` in another. Name a dimension the same in every indicator. A color kept by value matches by name too (see [Colors kept by value](#colors-kept-by-value)).
- Your filter values are not remembered between visits, except in the page address.
- Every member sees every dashboard: there are no dashboards for some members only, and no dashboards of one's own.
- It does not copy a dashboard or a widget to another project, and a removed dashboard cannot be brought back.
- It does not compare two versions side by side, name or pin a version, or keep more than the 10 newest.
- Tabs cannot be dragged: a dashboard moves with **Move left** and **Move right**, which also work from the keyboard and on a phone.
- An image of a widget has no basemap, no dark theme and no size to choose; it is made at the size of the widget on screen. There is no export of a whole dashboard at once, no PDF made on the server and no widget CSV.
- It does not make a PDF on the server, and a scheduled email attaches no PDF: printing goes through the browser's dialog, which can save as PDF.
- It does not refresh by itself: reload the page after a run, or to see the dashboards other members have added, renamed, moved or removed.
