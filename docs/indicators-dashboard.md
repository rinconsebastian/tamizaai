# Indicators dashboard

A project's own dashboards are canvases of **widgets**: stat tiles, charts and maps that members of a project arrange over the tables its indicator scripts published. Each widget plots **one indicator**. A project has from 1 to 10 dashboards of its own, each with its name and its widgets, shown as tabs after [Performance](performance-dashboard.md) and [Clean data](clean-data-dashboard.md); a new project starts with one, **Indicators** (see [Dashboards](#dashboards)). Every member can see them all and export the tables; analysts and admins build them.

A dashboard shows what the last successful run of each [indicator script](scripts.md#3-indicator-scripts-computecleaned-ctx) published, and nothing else: it does not read the stored submissions, does not contact Kobo and never starts a run. When a run fails, or one indicator fails, the table of the last successful run stays on screen. The widgets come first. After them, a collapsed list titled **Indicators (N) · M published** shows each indicator with its label, unit and format, whether it has published a table, when, and its row count; open it to read it or to export a table. Every dashboard of the project lists the same indicators, with the same exports. While no indicator has published, every dashboard says so with the list expanded as part of that message; analysts and admins get a link to the scripts, and the widgets already configured on the dashboard are kept and listed, so a dashboard can be laid out before the first run. A widget whose indicator has not published stays short until it does.

## Dashboards

The tab row of a project reads **Performance**, **Clean data**, the project's own dashboards in their order, and, for analysts and admins only, **Settings** at its end. Performance and Clean data always come first and are not among the project's own: they cannot be renamed, moved, duplicated or removed, and no dashboard can take their place or their name. Every member sees every dashboard of the project; there are no dashboards for some members only.

A dashboard's name is shown exactly as it was saved: never translated, and always as text, never as markup. From 768 pixels wide, tabs that do not fit scroll sideways inside the row, never the page: each side that hides tabs shows an arrow that scrolls the row, Settings (for analysts and admins) stays visible at its end, and the open tab is brought into view. A name too long for its tab is cut with an ellipsis; pointing at the tab or reaching it with the keyboard shows the whole name, and screen readers read it whole. Below 768 pixels one select lists Performance, Clean data and the project's dashboards in the same order, then, for analysts and admins, the Settings group.

Each dashboard shows its name as a heading above its widgets. For analysts and admins the heading is followed by the **New dashboard** button and the **Dashboard actions** menu (the button with three dots), with **Rename**, **Duplicate**, **Move left**, **Move right**, **Filters** (see [Defining a dashboard's filters](#defining-a-dashboards-filters)), **Colors** (see [Colors](#colors)), **Source** (see [Dashboard source](#dashboard-source)), **Base sizes** (see [Base sizes of a dashboard](#base-sizes-of-a-dashboard)), **Scheduled email…** where Tamiza sends email (see [operations.md](operations.md#email-optional)), **Version history** (see [Version history](#version-history)) and **Remove**; on a narrow screen they wrap under the name. An item that is unavailable stays in the menu, dimmed, with a second line saying why (for example "It is already the first dashboard."); the arrow keys still reach it, so a screen reader reads the reason too, but it cannot be chosen. Viewers see the heading only.

**New dashboard**, **Rename** and **Duplicate** open the same name form: one **Name** field, of up to 40 characters and different from the names of the project's other dashboards, then **Add dashboard**, **Rename** or **Duplicate** to save, or **Cancel** (or Escape) to close it without a change. A name that is refused (empty, too long, taken, the name of a permanent tab, or holding a control character) is explained under the field, and what you typed is kept.

- **New dashboard** asks for a name, adds the dashboard after the project's others, with no widgets, and opens it with the focus on its heading.
- **Rename** changes only the name: the dashboard keeps its widgets, its place and its address. A message confirms the new name (`Renamed to "Coverage".`), and the focus returns to the actions button.
- **Move left** and **Move right** move the dashboard one place among the project's dashboards, for every member. The first dashboard cannot move left and the last cannot move right. Screen readers are told the dashboard's new position ("Health moved to position 3 of 4"), and the focus returns to the actions button, so a dashboard can be moved several places in a row. While a move is being saved, both items are unavailable.
- **Duplicate** asks for the copy's name, proposed as the original's name followed by " (copy)", the original's name cut where needed so the whole fits in 40 characters (to its first 33 before " (copy)"); when the project already has that name, in any case, it proposes " (copy 2)", " (copy 3)" and so on, cut the same way. The copy is placed right after the original and holds a copy of each of its widgets, with every setting the widget is saved with (its title, chart type, query, display settings, the dashboard filters it ignores, position and size, which include a widget's own [column names](#column-names)), and of the dashboard's settings other than its name (its filters, in the same order with the same defaults, its palette, its colors kept by value and its other display settings; see [Colors](#colors)); it opens with the focus on its heading. A widget you moved or resized a moment before is saved first, so the copy has it in its new place. From then on the two are independent: changing or removing a widget of one, or changing its colors, leaves the other as it is. Each widget's [words](#words-around-a-widget) (subtitle, footnote, caption, unit and description button) are copied with it, and so are its [reference lines, steps and data labels](#reference-lines-steps-and-data-labels), and so is the dashboard's [source](#dashboard-source), which the two then change on their own, and its [base sizes](#base-sizes-of-a-dashboard) (the small-base threshold and the base n line), which the two also change on their own, each widget keeping its own. A dashboard that holds more widgets than the [limit](#limits) allows, which happens only after an operator lowered it, cannot be duplicated: the name form says so ("A dashboard can have at most 50 widgets. Remove one or move it to another dashboard first.") and nothing is added.
- **Remove** asks for confirmation, saying how many widgets go with the dashboard, counted on the server when you ask ("Remove Health and its 5 widgets? This cannot be undone."), then, on **Remove dashboard**, removes the dashboard and all its widgets for every member. It cannot be undone: a removed dashboard cannot be brought back, and its versions go with it. A widget removed from a dashboard can be brought back while a kept version of that dashboard holds it (see [Restoring a version](#restoring-a-version)). The dashboard now in its place opens, or the new last one when the removed dashboard was last, with the focus on its heading, and a message says what was removed (`"Health" and its 5 widgets were removed.`).

The rules:

- **1 to 10 dashboards.** A project always keeps one: on its only dashboard **Remove** is unavailable ("A project needs at least one dashboard."). At 10, **New dashboard** is unavailable, with a line below it that says why ("The project has the most dashboards it can hold (10)."), and so is **Duplicate** ("A project can have at most 10 dashboards."). Both can still be reached with the keyboard, so a screen reader reads why. The 10 is a rule of the product, not a setting.
- **Names** are 1 to 40 characters once leading and trailing spaces are removed (`  Salud  ` is saved as `Salud`), with no control characters such as tabs or line breaks. Two dashboards of a project cannot have names that differ only in case (`Salud` and `SALUD`), and no dashboard can be named Performance, Clean data or Settings, in any case. Another project can use the same names.
- **Who.** Analysts and admins add, rename, move, duplicate and remove dashboards, define their filters, choose their palette and the colors kept by value (and, in the widget form, a widget's color or scale), set their source and their [base sizes](#base-sizes-of-a-dashboard), and read, view and restore their versions. Viewers see every dashboard and its widgets, in the colors chosen, but none of these actions, and the API refuses their requests (403). Someone who is not a member of the project gets the same answer as for a project that does not exist.

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

### Dashboard source

A dashboard can say where its data comes from, such as the survey: a **source** of 1 to 300 characters ("Household survey, zones 1 to 7, 478 responses"). Analysts and admins set it from **Source** in **Dashboard actions**, right after **Colors**, in a form with one field: type the source and **Save**, or empty the field and **Save** to remove it. **Cancel**, or Escape, closes the form without a change, and the focus returns to the actions button. A source is trimmed, cannot hold a line break or another control character, and a longer one is refused under the field ("The source can have at most 300 characters."), and the API refuses it as well (400, with an error on `source`). Nothing is sent when you save what the dashboard already has. A viewer is not offered **Source**, and the API refuses their request (403). The action is not offered either while a [version is shown](#viewing-a-version).

- **Where it shows.** Every member sees it under the dashboard's widgets and before the list of indicators, after the word "Source:" ("Fuente:" in Spanish), as the author wrote it and always as text. A dashboard without a source shows no line. While a version is shown, the page shows that version's source.
- **On paper and in files.** The printed dashboard gives it once, in its header, and not again under the widgets (see [Printing a dashboard](#printing-a-dashboard)). An image of a widget names it in its footer, and a widget workbook lists it in its "Summary" (see [Exporting a widget](#exporting-a-widget)). The [scheduled email](operations.md#email-optional) gives it in its header and in its workbook's "Summary".
- **Versions.** The source is part of the dashboard's versions: saving a different one makes a version that says "Source changed", saving it unchanged makes none, and viewing or restoring a version shows or brings back its source, or removes it when the version has none (see [Version history](#version-history)). A dashboard that never had a source keeps the versions it had before sources existed.
- **Copies.** **Duplicate** copies the source; the copy and the original then change theirs separately.
- **Two members at the same time.** Saving the source of a dashboard another member has removed saves nothing and is handled like any change to it (see [Two members at the same time](#two-members-at-the-same-time)).
- **One source per dashboard.** A dashboard that mixes surveys has one source; a widget's [footnote](#subtitle-and-footnote) can name another meanwhile.

### Base sizes of a dashboard

A dashboard can say how its widgets show the number of people behind each value (see [Bases and small samples](#bases-and-small-samples)): whether each widget shows its **base n** under it, and a **small-base threshold**, a whole number from 1 to 10,000 below which a value is drawn dimmed. Both are off until someone sets them. Analysts and admins set them from **Base sizes** in **Dashboard actions**, right after **Source**, in a form with a **Show each widget's base n under it** box and a **Dim marks with a base below** field (hint: "Common choices are 30 or 50."), and a short text that says where n comes from. **Save base sizes** saves them for every member, the widgets follow at once without asking the server for their rows again, and a message confirms it ("Base sizes saved."). An empty threshold means off; a 0 or any other number that is not a whole number from 1 to 10,000 is refused under the field ("The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off."), and the API refuses it as well (400, with an error on `minN`). **Cancel**, or Escape, closes the form without a change, and the focus returns to the actions button. Nothing is sent, and no version is made, when you save what the dashboard already has. A viewer is not offered **Base sizes**, and the API refuses their request (403). The action is not offered either while a [version is shown](#viewing-a-version).

- **Each widget decides for itself.** A widget follows the dashboard's settings, turns the threshold off with 0, or sets its own, and shows or hides its n line whatever the dashboard says (see [In the widget form](#the-base-group-in-the-widget-form)). The settings in force for a widget are its own, else the dashboard's.
- **Versions.** The settings are part of the dashboard's versions: saving different ones makes a version that says "Base sizes changed", and viewing or restoring a version draws or brings back its settings (see [Version history](#version-history)). A dashboard that never had them keeps the versions it had.
- **Copies and moves.** **Duplicate** copies both settings, and the copy and the original then change theirs separately. A widget moved to another dashboard keeps its own settings and follows the new dashboard's for what it does not set.
- **Two members at the same time.** Saving the base sizes of a dashboard another member has removed saves nothing and is handled like any change to it (see [Two members at the same time](#two-members-at-the-same-time)).

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

A widget can have a **unit of its own**, which takes the place of its indicator's unit for that widget only (see [Unit](#unit)). The indicator's format still applies: a column chart over a `percent` indicator whose unit is "de los hogares", given the unit "de quienes asistieron", reads `11.21% de quienes asistieron`, while the indicator's other widgets keep "de los hogares". A widget's unit of `%` on a `percent` indicator is not repeated after the number.

### Column names

A script names its columns for code (`numerator`, `denominator`, `comuna`, `hogar_menores`), and a dashboard is read by people. **Column names** give a column the name to show for it, in two places:

- **The indicator's.** In the indicator's details above the script editor (see [scripts.md](scripts.md#the-indicators-name-and-metadata)), the **Column names** section offers one optional field per column of its table: the columns of the latest test of the draft, or without one those of the published table, in the table's order. A name is 1 to 80 characters, and an indicator names at most 100 columns. A column with a saved name that the table no longer has is listed after the others, marked **Not in the table**, so its name can be kept or cleared. The names are saved with every version of the script, like the label and the unit, so restoring a version brings its names back; changing one needs no run and makes no [dashboard version](#what-makes-a-version), and every widget over the indicator shows it the next time it is drawn. A save that sends no column names, such as one from an AI client that does not know them, keeps the current ones.
- **A widget's own.** The widget form has a **Column names** group, closed until the widget has names of its own, with a field for each column the widget shows: its measure, or its numerator and denominator, its group column and its series column. The field shows the indicator's name for the column, else the column's own, as its placeholder, and the preview shows what you type without asking for the rows again. A widget names up to 10 columns, on every kind of widget, and keeps them when its chart type changes. A name for a column the widget no longer shows is dropped when you replace the column in the form; saving the form without changing anything keeps the names as they were saved.

The same section also holds the [order of a column's values](#order-of-values) and what an indicator says about the people its rows count: its [base column](#the-base-column), the [values that mean no data](#no-data-values) of a column and its [answer columns](#answer-columns). A column with any of them counts toward the 100 columns with a name, and an older version shows them as text.

The name shown for a column is the widget's own name, else the indicator's, else the column's own. With `numerator` named "Hogares que asistieron" and `denominator` "Hogares" by the indicator `teatro_hoy` (labelled "Teatro hoy"), and `comuna` named "Comuna", a column chart of that ratio by `comuna` reads **Teatro hoy: Hogares que asistieron / Hogares by Comuna**; one widget of the dashboard can still call its denominator "Hogares de la base E1". A widget that plots its indicator's default measure without naming it keeps the indicator's label alone ("Teatro hoy").

Where the names show: the caption and the accessible name of every chart, a stat tile's description, the tooltip lines that name what is plotted, a map's tooltip columns that have no label of their own, the headers of [Show as table](#show-as-table) and of cross tables, [images and workbooks of a widget](#exporting-a-widget), the [printed dashboard](#printing-a-dashboard), and the body, plain text and workbook of the [scheduled email](operations.md#email-optional). Where the column's own name stays, because it is the name you choose or match by: the dashboard filters and the widget's filters, the colors kept by value, the column lists of the widget form, the messages about a column the table no longer has, what the API and AI clients send, and the [export of an indicator's whole table](#export). A name never changes a color. Two columns may have the same name, as two widgets may have the same title. A widget and an indicator without names look, read and export exactly as before.

## Widget types

Every widget has a title, a type, one **indicator**, what to **plot** (a measure or a ratio, see below), the column to **group by**, for many types a second column to split each group by (the [series](#widgets-with-a-series)), up to 50 **filters**, and for a map its **geometry column** and up to 5 [**tooltip columns**](#tooltip-and-tooltip-columns) and a [**basemap**](#basemap). A bar, column or line chart with no series, a histogram and a box plot also have a [**color**](#widget-color-and-scale), a map and a heat map a [**scale**](#widget-color-and-scale), and a box plot an [**orientation**](#box-plot). Any widget can also have a subtitle, a footnote, a caption of its own, a unit of its own and a description button (see [Words around a widget](#words-around-a-widget)). A bar, column or line chart can also have [reference lines](#reference-lines-steps-and-data-labels), a line chart can be drawn with steps, and a bar, column, line or pie chart can write its values on the marks as data labels (see [Reference lines, steps and data labels](#reference-lines-steps-and-data-labels)). Any widget can follow its dashboard's small-base threshold and base n line or set its own, and a bar, column, line, pie, heat map, population pyramid, Likert or cross table widget can leave out the values its indicator declares as no data (see [Bases and small samples](#bases-and-small-samples)). A widget form shows a live preview while you edit.

| Type | Shows | Group by | Notes |
|---|---|---|---|
| **Stat tile** | One number: the measure summed, or the ratio of sums, over every row the query matches. | Optional | Says over how many rows it was summed. |
| **Bar chart** | Horizontal bars, one per group value. | Required | Can be [ordered](#bar-and-column-order), and can have [reference lines and data labels](#reference-lines-steps-and-data-labels). |
| **Column chart** | Vertical columns, one per group value. | Required | Can be [ordered](#bar-and-column-order), and can have [reference lines and data labels](#reference-lines-steps-and-data-labels). |
| **Line chart** | A line through the group values. | Required | Group values are in natural order (2 before 10), so a `round` column of 1, 2, 10 reads as a trend, unless you [declare an order](#order-of-values). Can be drawn with [steps](#reference-lines-steps-and-data-labels) instead of straight segments, and can have reference lines and data labels. |
| **Histogram** | How many rows fall in each of a number of equal-width ranges of the plotted value. | Optional | 10 ranges by default, up to 100. Without a group column each row of the table is counted; with one, each group's value. |
| **Pie chart** | One slice per group value, sized by its share of the total. | Required | See below. Can have [data labels](#reference-lines-steps-and-data-labels). |
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

A line chart has no *sort*: it keeps its natural order (`2` before `10`), and so do the other charts that cannot be sorted, unless you [declare an order of values](#order-of-values), which any chart that lists groups takes. A [box plot](#box-plot) can be sorted by its **median** only.

### Order of values

A survey answer has an order of its own (`Sí`, `Tal vez`, `No`, `No sabe`, or the price ranges of a question), but a chart lists its values alphabetically, by size or in natural order, and puts `Otro`, `No sabe` or `Sin dato` wherever the alphabet does. Declare the order once instead of numbering labels (`1. Sí`, `2. Tal vez`) or publishing an order column. You can declare it in two places:

- **The indicator's column.** Under a column of the indicator's [Column names](#column-names), the **Order** group (closed until you open it) takes the **order of the values** (up to 100), the values to **keep at the end** (up to 20, such as `Otro`, `No sabe` or `Sin dato`, whatever the order or the sort) and **Hide numbered prefixes**. The values the latest test saw are offered, and any other can be typed. It is saved with the script's version, so restoring a version brings it back, and it applies to every widget of the indicator that has no order of its own, on every dashboard. Saving it makes no dashboard version. An AI client sets it with `save_script`'s `columns`.
- **The widget's own.** The widget form has an **Order of values** group for the groups and, when the widget has a series, for the series: the **default order** (the indicator's when it declares one, else the chart's) or an **own order**, set by moving the values the preview shows up and down with buttons that work from the keyboard. Moving a value redraws the preview without asking for the data again. An own order is a display setting, saved with the widget and its versions, and kept by a copy or a move.

**Which order wins.** A widget's own order, else its indicator's, else the chart's own: the group column's order for bars and columns, largest first for a pie, natural order for lines, heat maps, pyramids, Likert bars, cross tables, box plots and series. The values an order does not list follow in the chart's own order, then the values kept at the end, then **(none)** and **Other**. Values match as exact text, accents and case included; a listed value the chart does not show is skipped, and an order never changes which values are shown or combined into **Other**.

**A sort stays in charge.** A bar or column chart with a [sort](#bar-and-column-order), and a box plot ordered by median, keep that order even when the indicator declares one; only the values kept at the end, then **(none)**, move to the end. A widget cannot have both a sort and an own order of groups, and the form turns the order of groups off, saying why, while a sort is chosen. Inside sorted bars the series follow their declared order.

**Where it shows.** On the axes, in the legends, in the order the arrow keys move through the marks, in **Show as table** and the cross tables, in images, on paper, in a scheduled email's body and workbook, in the widget's workbook and in the order of the widget data AI clients read. A population pyramid's two sides and a Likert scale keep the order the widget sets for them, and a pie in a declared order still tells screen readers which slice is the largest.

**Numbered prefixes hidden.** With **Hide numbered prefixes**, a leading number of 1 to 3 digits followed by `.`, `)`, `:`, `-` or `–` and a space (`1. Sí`, `2) No`, `3 - Tal vez`) is not shown. It changes only the text: filters, colors kept by value, dashboard filters and AI clients keep matching the full value, so `1. No pagaría` still selects its rows. A range such as `10 - 14` or a decimal such as `1.5 horas` is not a number to hide, and two values that would read alike (`1. Otro` and `6. Otro`) keep their full text. Only a leading number is hidden; a prefix such as `a)` or `IV.` is not.

**Colors.** A value with no color kept by value takes the palette's colors in the order it is shown, so declaring an order can give series and pie slices other colors. [Keep a color for the value](#colors-kept-by-value) to fix it whatever the order.

**Limits.** The order is applied to the groups the query returns, after its row limit, so it matters past 5,000 groups only; a sort still orders before the limit. A script that renames a value leaves the old one in the order, listed apart in both forms as not shown, until you remove it.

### Filters

Filters work as in the [clean data dashboard](clean-data-dashboard.md#filters): **contains**, **is exactly** and **is one of** for text; **is between** for numbers, dates and timestamps; **is one of** for true/false; **is empty** and **is not empty** for any column. A row has to match all of them. A widget takes at most 50 filters, and **is one of** takes at most 100 values. Filters are added once the indicator's table is published, because they are chosen from its columns. These are the widget's own filters, the same for everyone; a dashboard's [filters](#dashboard-filters) narrow it further, on each member's screen.

### Reference lines, steps and data labels

A bar chart of a rate by zone shows each bar, but not what to compare it with. Three optional settings of a chart answer that: **reference lines** across a bar, column or line chart, **steps** instead of straight segments for a line chart, and **data labels** that write each value on the marks of a bar, column, line or pie chart. Analysts and admins set them in the widget form (see **In the widget form** below); every member sees them, viewers included. They are saved with the widget's display settings (`referenceLines`, `interpolation` and `dataLabels`) and change only how the widget is drawn, never the rows it plots. A widget that sets none looks, prints, exports and is sent exactly as it did before they existed.

**Reference lines.** A widget takes up to 4 lines, each with an optional **label** of up to 40 characters. A line is one of four kinds:

| Kind | The line is at | It needs |
|---|---|---|
| **Mean** | The mean of the bars (or the points of a line chart) the chart draws, after every filter | A chart with no series |
| **Median** | Their median; with an even number of bars, the mean of the two middle ones, as pandas computes it | A chart with no series |
| **Overall** | The indicator's value over all the widget's rows with the same filters, the figure a stat tile with the same indicator, ratio and filters shows | A [ratio](#what-to-plot-a-measure-or-a-ratio) |
| **Fixed value** | A number you type, such as a target of 50 % labelled "Meta" | Nothing |

- **Mean and median** summarize the marks on screen. A **(none)** bar counts like any other bar, and when the widget reaches the row limit they summarize the bars it draws. Every bar weighs the same in them, whatever its base.
- **Overall** is the ratio of the sums of the numerator and the denominator over every row the widget's query matches, with the widget's filters and the dashboard's filters in force, whatever bars are drawn and wherever the row limit falls. The database computes it with the statement of a stat tile, so the line and a tile with the same indicator, ratio and filters always show the same number. It needs a ratio because over a measure (a count or a sum) it would be the sum of every bar, far above all of them and nothing a bar is read against; the form does not offer it there and the API refuses it. An overall line with no value, because the denominators sum to zero, is not drawn. On a chart with a series it is the total of all the widget's rows, the grand total that [Show as table](#show-as-table) gives.
- **A fixed value** is read in the indicator's units: a fraction for a `percent` indicator (0.5 is 50 %), a number of its unit for a `count` or `decimal` one. The widget form takes a percent typed as a percent (type 50) and saves the fraction. A count or a decimal is typed with no thousands separator: the form does not guess whether `1.500` or `1,500` is one and a half or a thousand five hundred, and asks for `1500` (or `1.5`) instead.
- **Mean against overall.** A column chart of an attendance rate by `zona` with only two zones, Zona 1 with 60 of 121 households and Zona 7 with 3 of 16, draws columns at 49.59 % and 18.75 %. Its mean line is at 34.17 %, the mean of the two columns; its overall line is at 45.99 %, 63 of 137 households, because Zona 1 has far more households than Zona 7.
- **Over the answers of one question, the overall line is not a total.** When the bars are the answers of one question, such as places visited or reasons, they share one base: every place has the same denominator, the households asked. Then `sum(numerator) / sum(denominator)` over all the places is only the mean of the bars, and it reads like a total but is not. Use an overall line when the bars are groups of households, such as zonas or estratos. **When the indicator declares its [answer columns](#answer-columns)**, a bar, column or line chart grouped or split by one of them draws no overall line, on screen, in images, on paper, in the scheduled email and in what AI clients read, and says so under the chart ("No overall line: the bars are the answers of one question."); its other lines are drawn as before, the form no longer offers the overall kind for it, a line saved before the declaration stays in the widget, and nothing is refused. A chart grouped by `comuna` in the same indicator keeps its overall line. Until the indicator declares them, nothing tells Tamiza which columns are the answers of one question, so nothing is refused: the widget form says so under the overall kind, and it is up to the author.
- **With a series**, a grouped bar or column chart and a line chart can have overall and fixed-value lines. A stacked chart can have only fixed-value lines, and a bar chart stacked to 100 % only fixed shares from 0 to 1, drawn at that share of every bar (0.5 crosses every bar at its middle). Mean and median lines need a chart with no series.
- **What is refused.** Saving a widget is refused (400) when it has more than 4 lines, a line with no kind or an unknown one, a second mean, median or overall line, a fixed value without its number or another kind with one, a label of more than 40 characters or with a line break or another control character, a line its chart type, series or layout does not take, or an overall line over a measure. The error names the line at fault, such as `config.display.referenceLines[1].label`. Only bar, column and line widgets have reference lines.
- **How a line looks.** A dashed line across the plot at its value, vertical on a bar chart and horizontal on a column or line chart, and the value axis stretches to include it. Lines are in the text's ink, never a palette color, and each has its own dash pattern by its place in the widget's list, so they are told apart by dash and label. At its end the line writes its label and its value, as the widget's tooltips write values: **Mean: 34.17% de los hogares**, or **Promedio: 34,17 % de los hogares** in Spanish. A line without a label of its own takes its kind's name in the interface language (Mean, Median, Overall and Reference; Promedio, Mediana, Valor general and Referencia), and a widget with a unit of its own writes it. A 100 % chart writes a fixed value as a share (**Reference: 50%**).
- **When a text does not fit.** The text at a line is left out when it is wider than the plot or would cover another line's text; the first line of the list keeps its place. It is never cut or moved away from its line, which would risk reading it against the wrong one.
- **The key.** Under the plot, above the widget's footnote, a key lists every line drawn, with a sample of its dash and its whole text, including the lines whose text did not fit at the line. The key belongs to the chart, so it takes its room from the plot as the subtitle and the footnote do (see [Room](#subtitle-and-footnote)). A chart with no lines has no key. Screen readers hear the lines, with their values, at the end of the chart's description; the lines are not stops of the keyboard.

**Steps.** A line chart, with or without a series, joins its points by **straight segments** by default. With **Steps** each point's value holds until the next point, where the line moves straight to the next value; a gap in a series stays a gap. It is meant for demand curves: a chart of the share who would pay at least each price, over the prices `0`, `10000` and `20000`, says that 90 % would pay `$0` or more and 60 % would pay `$10,000` or more. That share holds until the next price on the list, so the line runs level from `$0` to `$10,000` and then drops straight to 60 %; a straight segment between the two would suggest that a price in between had a share nobody asked. The points keep their places and their tooltips, and the choice only redraws the chart. It is saved as the widget's `interpolation`, `linear` or `step`. Only a line chart has it, and the groups stay on the chart's category axis, so the points are equally spaced and not set apart by the size of the price gap.

**Data labels.** A bar, column, line or pie chart can write its values on the chart. They are off by default.

| Chart | The label sits | Left out when |
|---|---|---|
| **Bar chart** (no series, or grouped) | Just past the end of each bar, to the left of a negative one | The bars are thinner than the text, or a negative bar reaches the category names |
| **Column chart** (no series, or grouped) | Just above each column, below a negative one | The label is wider than the column's slot, or a negative column reaches the category names |
| **Line chart** (with steps too) | Above each point | It would cover a label already drawn |
| **Pie chart** | On the slice, at its middle; a pie of one slice, at its center | The slice is too thin for it, or the text would leave the circle |

- **The text** is the value as the widget's tooltips and table write it, with the indicator's unit or the widget's own: the bar of the most visited place reads **31.5% de los hogares** at its end. A bar chart keeps room past its longest bar so that its label fits.
- **Never over each other.** A label that does not fit its mark, or would cover a label already drawn, a reference line's text included, is not drawn: it is never cut or squeezed. The value of a mark without a label stays in its tooltip and in [Show as table](#show-as-table). The labels are for the eye; screen readers read the table and the tooltips.
- **Grouped columns are narrow** (at most 24 pixels), so their labels often do not fit. Horizontal bars suit data labels better than grouped columns, because the plot keeps room past the longest bar for its label, while a column's label has to fit the width of the column.
- **Not offered** on bars stacked or stacked to 100 %, which already show their shares and values in their tooltips, nor on heat maps, population pyramids, Likert bars, maps and the other kinds. Saving data labels turned on for a stacked or 100 % layout, or for a kind that does not take them, is refused (400, with an error on `config.display.dataLabels`); data labels turned off are always accepted.
- **Short numbers** such as "$1,2 M" are not written: a label gives the whole value.

**In the widget form.** After the color fields and before **Text on the widget**, the form shows only what the widget takes:

- **Reference lines**, for a bar, column or line widget: one row for each line with its **kind**, its number (for a fixed value, with the indicator's unit beside it; a percent is typed as a percent), its **label**, whose placeholder is the kind's default label, and a button to remove it, then **Add a reference line**, off at 4 lines. A kind offers what the widget takes: mean and median only with no series, overall only over a ratio, and only the fixed value for a stacked or 100 % layout. Mean, median and overall are listed once each, a fixed value as many times as needed. The help under each row says what its kind is, and for an overall line says that over the answers of one question the line is only the mean of the bars, not a total. A label is saved on one line: a typed or pasted line break becomes a space.
- **Line segments**, for a line widget: **Straight** or **Steps**.
- **Show the values on the chart**, for the widgets that take data labels.
- **Preview.** Editing a line, a label, the segments or the labels redraws the preview at once, without asking the server for the rows again. Only adding or removing an overall line on a widget with no series asks once, for the overall value.
- **A change of chart type, series or layout** saves only what the widget then takes: a column chart turned into a pie keeps its data labels and loses its lines, one given a series loses its mean and median lines, and a stacked layout keeps only its fixed values and no data labels. A note under the settings says what will not be saved and why. The form keeps those settings until it is saved, so changing back before saving brings them back.
- **Errors** from the server show on their own row or field. Saving the form without changing anything keeps the widget as it was saved and makes no [version](#what-makes-a-version).

A widget keeps its reference lines, steps and data labels when it is [moved to another dashboard](#moving-a-widget-to-another-dashboard), [duplicated](#dashboards) with its dashboard or restored from a [version](#version-history). Reference lines also appear in [Show as table](#show-as-table), in [images](#images) and [workbooks](#workbook) of the widget, on [paper](#printing-a-dashboard), in the [scheduled email](operations.md#email-optional) and in what AI clients read (see [mcp.md](mcp.md)); a line is written the same way everywhere.

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
- **Order.** Series are listed in natural order (2 before 10). So are the groups of a line chart, a heat map, a pyramid, Likert bars and a cross table, which cannot be sorted; bars and columns keep the [order](#bar-and-column-order) of their group column or the one you choose. A [declared order of values](#order-of-values) takes the place of the natural order of the series and of the groups. Rows with no value in the series column come next as **(none)**, and the folded series last as **Other**.
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

A **line chart** with a series draws one line per series value, through the groups in natural order (or in a [declared order](#order-of-values)). A series with no value for a group has a gap there.

### Heat map

Groups are rows (in natural order, so `5-9` comes before `10-14`, or in a [declared order](#order-of-values)), series values are columns, and each cell is shaded on the same sequential scale as the maps (the dashboard palette's scale, blue in Tamiza, unless the widget [picks another](#widget-color-and-scale); see [Colors](#colors)), from the smallest to the largest value shown, with a legend of that range. A cell with no value is drawn empty with a hatch, which is not the same as a cell whose value is 0.

### Population pyramid

Pick the two values of the series column that go to the **left** and to the **right** of a central axis, for example `Female` and `Male`. The group values (age bands) are the rows in natural order (`0-4`, `5-9`, `10-14`) or in a [declared order](#order-of-values), the first at the bottom, and both sides use one scale, measured outward and labelled without signs. Values follow the indicator's format: counts as numbers, a percent indicator as percentages.

- A row with any **other** series value is left out. A row with no group or no series value draws no bar. The widget says how many rows it left out.
- If one side has no rows (a dashboard filter on `sex` hides it), that side is drawn empty and the widget is not stale.

### Likert bars

For agreement scales. In the form you order the scale's values (2 to 11) and mark each one **negative**, **neutral** or **positive**: negatives first, at most one neutral, at most 5 on each side. Each group is one bar of its composition in percent, centered on the line between negative and positive: negatives extend left with the values nearest the neutral nearest the center, the neutral share is split in half across the line, and positives extend right. Each value's colour follows its side, a red arm and a blue arm of increasing strength. The tooltips give each value's share and its count.

Values outside the scale (`Don't know`) are left out and counted in a note. Likert bars plot a measure, usually counts of answers.

### Cross table

A table of groups by series values (the groups in natural order, or in a [declared order](#order-of-values)), with the indicator's format in every cell, a **Total** column, a **Total** row and the grand total. A pair without rows reads "—". A table wider than its widget scrolls inside it and keeps the group column in view. It is also what **Show as table** gives for every widget with a series; for stacked-to-100% bars and Likert bars it lists each cell's share.

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
- **Order.** Boxes follow the natural order of the group (2 before 10, `0-4`, `5-9`, `10-14`), the missing group last, or a [declared order](#order-of-values). **Order boxes by** **Median**, ascending or descending, sorts them by their median; a box plot cannot be ordered by a column, nor ordered without a group column.
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

Pointing at a shape, tapping it, or reaching it from the keyboard shows its **tooltip**: the group value as its heading, then the plotted value with the indicator's label, format and unit, then the **tooltip columns** you chose, each under its label, or, when it has none of its own, under the [column name](#column-names) its indicator gives it, else the column's name. After a tap, a click or a keyboard selection the tooltip stays open until you tap or click elsewhere, press **Escape**, move to another shape, scroll the page or move focus away; a tooltip opened by pointing closes when the pointer leaves the shape. Everything in it is shown as text, never as markup.

An analyst or admin chooses up to **5 tooltip columns** in the widget form, under the geometry column. Each is any column of the published table except the geometry column, with an optional **label** of up to 80 characters (the form shows the column's [column name](#column-names) as its placeholder; a column with no label of its own is shown under that name, else its own) and, for a number column, a **format**. Columns can be reordered; the tooltip and **Show as table** list them in that order. Tooltip columns only belong to maps: a map turned into another chart type loses them.

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
- **The other values take the palette in order.** A value with no color kept takes the palette's colors in the order the widget shows its series (its [declared order](#order-of-values), when it has one), after the colors kept in that widget are set aside. A chart whose series are `Piped`, `River` and `Well`, with color 1 kept for `Well`, draws `Well` in color 1, `Piped` in color 2 and `River` in color 3. In a chart with no series, a bar with no color kept is drawn in the widget's own color.
- **Colors follow the value, never its rank.** A [series](#widgets-with-a-series) or a pie slice keeps its color when a dashboard filter changes the rows that are shown, since both come from the widget's own rows.
- **Two indicators with the same column name share its colors.** Colors kept are matched by column *name*, as [dashboard filters](#which-widgets-a-filter-narrows) are, not by indicator. If two indicators have a column called `type` and it means different things, a color kept for `type` = `A` colors the value `A` in both. Give each dimension a name of its own in the indicator scripts when it means something different, and the same name everywhere when it means the same.

## Words around a widget

A title of 150 characters with a question code, a base and a warning in it is hard to read. A widget can carry those words apart from its title instead, on **every type of widget**: a **subtitle** under the title, a **footnote** at its foot, a **caption** of its own (or none), a **unit** of its own, and a button that shows its indicator's **description**. Analysts and admins set them in the [widget form](#the-fields-in-the-widget-form); every member sees them, viewers included. They change only what the widget says, never the rows it plots, so editing them asks the server for nothing. A widget that sets none looks, prints, exports and is sent exactly as it did before these settings existed.

| Setting | What it is | Length |
|---|---|---|
| **Subtitle** | A line under the title, such as the question the chart answers | 1 to 200 characters |
| **Footnote** | A note at the widget's foot, such as why the shares do not add up to 100 % | 1 to 500 characters |
| **Caption** | **Default**, **Own text** or **Hidden** | own text: 1 to 200 characters |
| **Unit** | Takes the place of the indicator's unit for this widget | 1 to 32 characters |
| **Show the indicator's description** | A button in the widget's header | on or off |

A text is saved without the spaces around it, and a text that is empty or only spaces is not set. A text cannot hold a line break, a tab or another control character: the form turns a typed or pasted line break into a space, and the API and AI clients are refused (400, with an error on the field, such as `config.display.subtitle`). A change of a widget's chart type keeps all of them.

### Subtitle and footnote

The **subtitle** shows under the widget's title in muted text, and above a stat tile's figure. A subtitle such as "Which places did you visit last year? (Q4)" keeps the title short. The **footnote** shows under the widget's chart, under its table when it is shown as a table, or under the message that no row matches; not while the widget is loading or failed, or when its indicator has not published or a column is gone, since those messages take the place of the widget's content. A footnote such as "Does not add up to 100 %: more than one answer allowed." goes here.

- **Room.** A widget's tile never changes size, so the texts take room from its plot. On screen each text shows at most two lines, or one line on a tile 2 rows high or less, and a text that is cut ends with an ellipsis; pointing at it shows the whole text, and screen readers read it whole. The chart or table takes the room that is left; a chart that does not fit its room is cut at its own box and never drawn over the texts. The key of a chart's [reference lines](#reference-lines-steps-and-data-labels) takes its room from the plot in the same way, above the footnote. So do the [lines of the bases](#the-lines-under-a-widget) (the small-base note, the base n line and the no-data line), each about 20 pixels, between the chart and the footnote, which stays last.
- **How much fits.** A line of a tile 6 columns wide holds about 90 characters, and one of a tile 3 columns wide about 40. A stat tile 3 columns by 2 rows (its default size) with both a subtitle and a footnote keeps its figure whole and cuts its label; hiding its caption, or making the tile 3 rows high, gives the room back.
- **The form's warning.** The widget form shows a hint, without refusing the save, when the texts are likely to crowd the tile: a tile 2 rows high or less with a subtitle or a footnote, or a subtitle and a footnote that, printed whole, would fill half of the tile's height or more. It says that the texts take room from the chart or the figure and that a taller tile shows more of them. A new widget counts at the default size of its type.
- **Everywhere else the texts are whole**: in an image of the widget, in the [workbook](#workbook)'s "Summary", on [paper](#printing-a-dashboard) and in the [scheduled email](operations.md#email-optional). On paper a tile still never changes size: texts that need more room than the whole tile has are cut by the tile, so give a tile with long texts more rows or columns.

### Caption

The caption is the line that says what a widget plots: above a chart with no series and above a pie, map, heat map and cross table, and a stat tile's label under its figure. By default it names the indicator and what is plotted, with the [column names](#column-names) (**Teatro hoy: Hogares que asistieron / Hogares by Comuna**). The widget can change it in two ways:

- **Own text.** The text takes the place of the indicator's label and the names of the measure or of the ratio's two columns, wherever the widget names what it plots: the caption, a stat tile's label, the tooltip line of the plotted value (a map's included), the value column of [Show as table](#show-as-table), the chart's accessible name, and its image, workbook, printed sheet and scheduled email. The rest of each line stays: with the own text "% of attendees", a bar chart grouped by `zona` reads "% of attendees by zona", a histogram "Rows per range of …", and a heat map "… by … and …". A series legend keeps listing its series. The own text does not follow the indicator: if the ratio changes, change the text as well. The form shows the line your text makes, such as "% of attendees by zona", under the field.
- **Hidden.** The line is not seen and takes no room, but it stays for screen readers. The chart's accessible name, its tooltips, **Show as table** and the value columns of the email and workbook tables still name what is plotted, so only the visible line goes; a stat tile shows its title and its figure, and a series legend is not hidden. An image and a printed sheet leave the line out (and a stat tile's description). A population pyramid, Likert bars and a box plot draw no caption line, so hiding it changes nothing in their charts; the cross table they show as a table has its line hidden.

A caption cannot have its own text and also be hidden: that is refused (400, with an error on `config.display.caption`).

### Unit

A widget's **unit** takes the place of its indicator's unit wherever the widget writes a value with its unit: its chart, its tooltips, **Show as table**, images, the scheduled email, both workbooks and the values AI clients read from `get_widget_data`. It is for a widget whose base differs from the indicator's: the same `encuesta_hoy` can read "de los hogares" in one widget and "de quienes asistieron" in another that is filtered to a subgroup. The indicator's **format** still applies, a map's tooltip columns keep their own formats, and a histogram's range labels are written without a unit, as before. A unit of its own hides a later change of the indicator's unit for that widget, which is why the widget form names the indicator's unit as the field's hint.

### Indicator description

With **Show the indicator's description** on and an indicator that has a description (see [scripts.md](scripts.md#the-indicators-name-and-metadata)), the widget's header has a button named "About" followed by the indicator's label, before the widget menu. It opens the description in a panel next to the button, as plain text; the panel closes with Escape (the focus returns to the button), with Tab or Shift+Tab (the keyboard order continues from the button), with a click outside it or with the button, and opening one closes another. A long description scrolls inside the panel. The button has no effect on the rows. Every member has it, a shown version included (the description is the project's current one). A widget whose indicator has no description has no button, and the option is unavailable in the form saying why. The description is not printed, exported, sent in the email or returned with a widget's data.

### The fields in the widget form

- **Subtitle**, under **Title**: one line of up to 200 characters.
- **Text on the widget**, after the color fields, a group with **Caption** (**Default**, **Own text** or **Hidden**; the own text is a field of up to 200 characters, and its hint shows the line it makes, such as "% of attendees by zona"), **Unit** (up to 32 characters; its hint names the indicator's unit, or says that it has none), **Footnote** (up to 500 characters; a typed or pasted line break is saved as a space) and **Show the indicator's description**.
- **Preview.** The preview shows the subtitle and the footnote, the caption you chose and the unit, and redraws as you type without asking the server for the rows again.
- **Warning.** When the texts are likely to crowd the tile (see "The form's warning" under [Subtitle and footnote](#subtitle-and-footnote)), the **Text on the widget** group shows a hint under its fields.
- **Errors.** An error from the server shows on its own field: a refused subtitle under **Subtitle**, not under **Title**, and a caption error under the caption's options.
- **Saving.** **Own text** with an empty text saves no caption. Saving the form without changing anything keeps the widget as it was saved, and makes no [version](#what-makes-a-version); changing only the unit, for example, does make one.
- **The keyboard** reaches every field and option in the order of the form.

### On paper and in files

Printing keeps a widget's words: its subtitle and footnote print whole (the clamp is lifted), its caption as on screen (its own text, or none when hidden), its values with its own unit, and no description button or description (see [Printing a dashboard](#printing-a-dashboard)). A small tile with a long footnote can leave its chart less room than the chart's least size; the chart is then cut at its own box and never drawn over the texts, and the widget form's warning is the place to notice it beforehand. An image of the widget draws the whole subtitle under the title and the whole footnote above the footer (see [Images](#images)); the workbook's headers use the own caption text and the own unit, and its "Summary" lists the subtitle and the footnote (see [Workbook](#workbook)).

## Bases and small samples

A survey estimate is only as good as the number of people behind it. A column chart of an attendance rate by comuna draws COMUNA 7 at 50 % with the same weight as COMUNA 1, although COMUNA 7 rests on 16 households and COMUNA 1 on 121. Tamiza can say how many people stand behind each value, draw the values that rest on too few dimmed, leave out the values that mean "no data" and say what they held, and do all of it wherever a widget goes. Everything here is **optional and off until someone sets it**: a widget and a dashboard that set nothing look, print, export and are sent as they did before, with two exceptions that show only when a member points at a mark: the richer [tooltips](#tooltips-and-keyboard) and the figures of a rate in what the API and AI clients return.

### What n is

The **base n** of a value (its **n**) is how many units, such as households, it rests on. It is unweighted, and it is counted after the widget's own filters and the dashboard's. Rows a widget [leaves out as no data](#no-data-values) count in no n.

| Mark | Its n |
|---|---|
| A value of a measure or a ratio, when the indicator declares a [base column](#the-base-column) | The sum of the base column over the value's rows |
| A value of a ratio, otherwise | The sum of its denominator |
| A value of a measure, otherwise | None |
| A segment of a 100 % layout, a segment of a Likert bar | Its group's n, the base of the share |
| A total (of a group, a series or all the widget's rows) | The n over the total's rows |
| A box of a box plot | Its number of values |
| A histogram's bar, a pie's **Other** slice | None |

A rate also brings its **numerator** and **denominator**: the sums of both over the value's rows. The API, and AI clients through `query_indicator` and `get_widget_data`, return these three figures with every value of a rate whatever the settings. A measure whose indicator declares no base column returns none of them, as before.

Weights do not exist yet: every n counts each household once, whatever its weight.

### The base column

An indicator can declare one numeric column as its **base column**: the count of units that each row stands for, such as `hogares` in a table of households by estrato. A widget's n is then the sum of that column over its rows. The declaration is made in the indicator's [column names](#column-names), in a **Base column** group that lists the numeric columns of the table, **None** first, and it is saved with the script's version. An indicator has at most one base column; a second is refused (400, with an error on that column). A base column that the published table no longer has, or does not hold as a number, is ignored, and its widgets count their n as if there were none.

### Answer columns

In a **long** table, one row per answer, every household is counted under every answer of a question: `teatro_hoy` has 5,706 rows for 476 households, with `variable` (the question), `orden` (the answer's place), `categoria` (the answer), and the `numerator` and `denominator` of each answer. A bar chart of the five answers of one question would add up the denominators of five answers, 5 × 476. A chart cannot tell the answers of one question from groups such as comunas (each household is counted once there) unless the indicator says which columns hold the answers. An indicator declares them in its [column names](#column-names), with the **Answers of one question** choice of each column: **single choice** or **multiple choice**.

With answer columns declared, an **answer** is one combination of the values of all of them, and an n over the rows of several answers counts the base once: the **largest** of the sums of the base over each answer, not their total. Nothing is plotted differently: the values, the totals and the order are those they were.

- **Declare the question, code and answer columns of a long table.** For `teatro_hoy`, `variable`, `orden` and `categoria`: choose multiple choice when any question of the column is multiple-choice (the table holds single-choice questions, such as E1, and multiple-choice ones, such as E4), single choice otherwise. A chart of the answers of "Lugar (E4)", asked of 223 households, then has the n 223, not 223 times its places. Declaring `orden` with `categoria` changes nothing, since it moves with the answer.
- **Declare `variable` when a widget spans several questions.** In the Comercio study, the widget 0d62 splits the places where households buy by `variable`, "Ropa y calzado" and "Tecnología": two questions asked of the same 476 households, whose answers (the places) share names. With `variable` and `categoria` declared its n is 476; with `categoria` alone the engine would take both questions' households together, 952.
- **Never declare a column that splits the households**, such as `comuna`, `estrato` or `sexo`. Each household is counted once there, and declaring it would stop its widgets from adding their groups up.
- **A count table** (`teatro_conteo`, with `pregunta`, `respuesta` and the households in `value`): declare `value` as the base column, and `pregunta` as answers when a widget spans several questions, since every household is under each question.
- **A wide table**, one row per municipality: declare the denominator itself as the base column, which says that its rows count units once.
- **Do not declare answers for per-answer bases.** A table whose answers do not share one base, such as `centro_motivo_hora` (each reason has its own base) or `teatro_coherencia`, should declare no answers: a declaration would show the largest answer's base as if it were everyone's.

### The n of a widget

A **widget has an n**, and so an n line, only when its marks have one and **its indicator declares a base column or an answer column**. Without a declaration Tamiza cannot tell a group column that holds the answers of one question from one that holds comunas, and adding the n of the marks would count each household once per answer: a bar chart of the five answers of a question of 476 households would read n = 2,380. A wrong n is worse than none, so the widget has none and says why, while each of its marks keeps its own n for its tooltip and its dimming (the sum of its denominator, which is right for a value that holds one answer). The n of a widget is counted over every row it plots, past the row limit too, with the dashboard's filters: a column chart of `teatro_hoy` by comuna has the n 476, and 121 once the dashboard filter "Comuna" is set to COMUNA 1. A histogram and a box plot have none. A stat tile is one mark: it dims by its own n, and its n line needs a declaration like any widget's.

### Small bases

A value whose n is below the **threshold in force** has a **small base**. The threshold is a whole number from 1 to 10,000, off until someone sets it: a dashboard sets one for all its widgets (see [Base sizes of a dashboard](#base-sizes-of-a-dashboard)), and a widget follows it, sets its own, or turns it off with 0. A value with no n never has a small base.

A mark with a small base is drawn **dimmed, never hidden**, in a way that reads without color:

- **Bars, columns, segments, slices and boxes** are lighter (35 % opacity) with a dashed outline in their own color, and keep their size, so nothing reads as a different value. The **points of a line chart** are hollow, with a dashed ring.
- A **heat map cell** keeps its shade, so the scale reads the same, and gets a hatch from its top-left corner to its bottom-right one with a dashed outline. An **empty** cell is hatched the other way, from bottom-left to top-right, with no shade, so the two read apart.
- A **map shape** keeps its class and gets a dashed outline.
- A **stat tile** draws its figure lighter.

While a mark is dimmed, a note under the chart says so ("Marks with a base below 30 are dimmed."), and the chart's description for screen readers adds how many ("…, 2 with a small base"). The tooltip of such a value ends with "Small base: n = 16 (below 30)". Dimming is a saved setting of the dashboard or the widget, not a per-viewer switch.

### The n line

A dashboard can show each widget's n under it, and a widget can show or hide it whatever the dashboard says: "n = 223 Hogares que asistieron", the n with thousands separators and the name of the base column, else of the denominator, as the dashboard names columns (see [Column names](#column-names)). It is off by default. A widget with no n shows no line, and its form says why (see below).

### No-data values

Authors keep "Sin dato" and "No sabe" out of charts with filters that list every other value. Instead an indicator can declare, for a text or true/false column, up to 20 **values that mean no data** (such as `Sin dato`, or `4. No sabe`), each of 1 to 500 characters and matched as exact text: `Sin dato` does not match `SIN NOMBRE` or `sin dato`. A declaration changes no widget that does not ask for it.

A bar, column, line, pie, heat map, population pyramid, Likert or cross table widget can **leave out the values** its indicator declares for its group and series columns, with the **Leave out values that mean no data** box of its form. The rows are then left out of every value, total, series, n, overall line and row limit, with no `in` filter on the column; a stat tile, a histogram, a box plot and a map cannot (saving the option on another kind is refused with an error on `config.missingFooter`), and the rows with no value, shown as **(none)**, keep their mark. The widget says what it left out in a **no-data line** under the chart: the left-out values that had rows, then what they held in the widget's format and unit, and for a measure their share of its total, such as "Sin dato: 4.2%" or "4. No sabe: 11 hogares (2.3%)", with " · n = 11" added while the n line shows. At most three values are named, then an ellipsis. A widget that left nothing out says nothing.

### The lines under a widget

Under its chart, a widget can show up to three lines, in this order: the small-base note, the n line and the no-data line, before the widget's [footnote](#subtitle-and-footnote), which stays last. They take their room from the plot as the subtitle and the footnote do: the tile never changes size, a chart that does not fit is cut at its own box and never drawn over a line, and on screen a line longer than two lines (one on a tile 2 rows high or less) ends with an ellipsis, with its whole text in its tooltip and for screen readers. On paper they print whole. A line takes about 20 pixels: a 6 × 4 tile with a subtitle, a two-line footnote and all three lines leaves its chart about 100 pixels, so the chart is cut at its own box, while a stat tile 3 columns by 2 rows with a subtitle, a footnote and an n line keeps about 25 pixels for its figure and is cut by the tile, so give a stat tile that shows its lines 3 rows. The lines show where the widget shows its chart, its table or its "no rows" message, and not while it loads or fails.

### Tooltips and keyboard

A bar, a column, a point of a line chart, a bar of a histogram and a slice of a pie, without a series, show a **tooltip** on pointing, tapping or focusing, as the charts with a series do, and are read with the keyboard: the chart is one **Tab** stop, the arrow keys move from mark to mark in the chart's order (the **down** arrow along a bar chart, the **right** one along the others and around a pie), **Home** and **End** go to the first and the last, and **Escape** leaves; a screen reader reads the mark reached. A tap keeps the tooltip open until you tap elsewhere. These tooltips replace the browser's own titles on the marks.

After the value, a tooltip gives a rate's **numerator** and **denominator** under their names (the widget's own column names, else the indicator's, else the column's), and the n under the base column's name ("n (hogares): 154") when it is not the denominator shown. The figures are whole numbers with thousands separators, up to two decimals when they are not whole, never in the indicator's format or unit: `56` and `121`, never `5,600%`. A histogram's bar gives its range and its row count only, and a tooltip that lists several series gives each one's n only. A small base adds its line. A map's tooltip adds the figures that its tooltip columns do not already show. This is a default change: it needs no setting.

### The Base group in the widget form

After **Text on the widget**, the widget form has a **Base** group, for every kind but a histogram:

- **Base n under the widget**: **As the dashboard says** (shown or hidden), **Show** or **Hide**. When the n line is on and the widget has no n, the form says why: a box plot has none; the indicator declares neither a base column nor an answer column; or the widget plots a measure and the indicator declares no base column.
- **Small bases**: **As the dashboard says** (below 30, or off), **Off** or **Below**, with a field for the number.
- For the kinds that take it, **Leave out values that mean no data, and say what they held under the chart**. It names the declared values ("comuna: Sin dato"), or says that the indicator declares none for the group and series columns.

Changing the n line or the threshold redraws the preview at once without asking for the rows again; leaving out values asks again. Changing the chart type to a kind that cannot leave values out drops that option and keeps the others. An error from the server shows on its own field, and saving the form without changing anything keeps the widget as it was saved and makes no version.

### Where else the figures go

- **Show as table.** While a widget with no series whose values have an n shows its n line or has a threshold in force, its table has an **n** column after the value, a small-base value is followed by " *", and a note under the table explains the mark ("* Base below 30."). A cross table marks its cells the same way. A widget whose values have no n, such as a measure with no base column, keeps its columns, and a table with no setting in force is as it was.
- **Images and workbooks.** A widget's PNG and SVG image draws its marks dimmed and its lines among its notes under the chart, before the footnote. Its workbook applies the settings the page applies to the widget, its own over the dashboard's (or the shown version's): the data sheet has the n column and the "Summary" the notes. A page of an earlier release sends none, and gets the workbook it always gave.
- **Paper.** The dashes and the hatching are part of the chart, so they print without **Background graphics** and in black and white, and the lines print whole under their charts.
- **Scheduled email.** It applies each widget's settings and its dashboard's: the notes above a table (the small-base note, the n line and the no-data line, with the footnote still last), an n column and a " *" after a small-base value in the body, and in the workbook the numbers alone with the notes on the "Summary". An email of a dashboard with no settings is as it was.
- **AI clients.** `update_dashboard` sets a dashboard's `minN` (0 turns it off) and `showN`, `create_widget` and `update_widget` a widget's `display.minN`, `display.showN` and `missingFooter`, and `save_script` an indicator's `base`, `missing` and `categories`; `get_widget_data` returns each value's figures, which values have a small base and the widget's n (see [mcp.md](mcp.md)).
- **Versions, copies and moves.** See [Base sizes of a dashboard](#base-sizes-of-a-dashboard).

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

When the project has more than one dashboard, **Edit widget** shows a **Dashboard** field after the title, set to the widget's own dashboard. Choose another one and save: the widget keeps every setting it is saved with (its title, chart type, query, display settings, the dashboard filters it ignores and its size), its own [column names](#column-names) and its [words](#words-around-a-widget) (subtitle, footnote, caption, unit and description button) included, and its [reference lines, steps and data labels](#reference-lines-steps-and-data-labels), and goes to the left edge below the widgets of its new dashboard. It leaves the open dashboard, a message names the dashboard it went to ("Widget Coverage moved to Field."), and the focus moves to the open dashboard's heading. Its own [base settings](#the-base-group-in-the-widget-form) (its threshold, its n line and its option to leave out no-data values) travel with it, and for what it does not set it follows the [base sizes](#base-sizes-of-a-dashboard) of its new dashboard: a widget with no threshold of its own moved to a dashboard whose threshold is 30 is drawn with small bases below 30, and one that hides its n line still hides it on a dashboard that shows them.

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

Every widget except the stat tile has **Show as table** in its [widget menu](#building-the-dashboard) (shown once the widget has rows to draw), a checked item while the table is shown, that replaces the picture with the numbers it plots, formatted like the chart (a histogram lists its ranges and their counts, a box plot its boxes with their figures, a map its groups with their tooltip columns, written as in the tooltip, and the kind of shape each has). The button above the grid, **Show all as tables**, does the same for every widget of the dashboard at once, in reading order, and resets the choice made on single widgets. Choosing **Show as table** closes the menu, and a screen reader hears that the widget "is shown as a table" or "as a chart". Each chart also carries a text description for assistive technology. A widget with a [series](#widgets-with-a-series) shows its cross table: groups as rows, series as columns, and a **Total** column and row; for bars stacked to 100% and for Likert bars the cells are shares. The headers name the columns by their [column names](#column-names): the group column's name, and for the plotted value the indicator's label with the names of its measure or of its numerator and denominator (**Comuna** and **Teatro hoy: Hogares que asistieron / Hogares**); the series headers keep the answers. A bar, column or line chart with [reference lines](#reference-lines-steps-and-data-labels) lists them under its table, one for each line with its label and value in the order the widget lists them, above the widget's footnote; the table itself is the same as without them. While a widget with no series whose values have a [base n](#what-n-is) shows its n line or has a small-base threshold in force, its table also has an **n** column after the value, a value with a [small base](#small-bases) is followed by " *", and a note under the table says what the mark means ("* Base below 30."); a cross table marks its cells the same way, and a table with no such setting in force, or whose values have no n, has the columns it always had (see [Where else the figures go](#where-else-the-figures-go)).

## Export

**Export**, the download button at the end of a published indicator's entry in the list, downloads the **whole published table of that indicator**, exactly as published and without the widgets' or the dashboard's filters, as CSV or XLSX. Viewers can export. It is the same file, the same single-use download link and the same limits as the [clean data exports](clean-data-dashboard.md#exports): the CSV has a byte order mark, a geometry column is exported as its WKT text, and an XLSX is refused above 1,048,575 rows. While one indicator's file is being prepared only its button shows that it is busy, and the page ends with one message. Exporting one indicator never delays another indicator's publishing. To take one widget out of Tamiza, as an image or with the data it plots, see [Exporting a widget](#exporting-a-widget). The export of a whole table is not affected by [base sizes](#bases-and-small-samples): it never has an n column, marks or left-out values.

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

- **What an image holds**, from top to bottom: the widget's full title (the screen cuts a long one), its whole [subtitle](#subtitle-and-footnote) when it has one, its notes ("Not filtered by …", the row limit), its caption (its own text when it has one, and none when the widget hides it, a stat tile's description included), the chart or the table, the whole legend, key or scale (a pie's twelve slices, not the part that scrolls), what the widget left out (rows, outliers, groups), its whole footnote when it has one, and a footer. The footer names the project, the dashboard and the version when one is shown, every filter of the dashboard with its value or "All", when the indicator was published as your page knows it, and when the image was made, in the project's time zone, and gives the dashboard's [source](#dashboard-source) ("Source: …") on a line of its own when it has one (the version's while a version is shown). There is no logo. Tooltips, buttons, map controls and focus marks are never in it, nor the indicator's description or text meant only for screen readers. An image of a widget with none of these settings is the same as before.
- **Reference lines, steps and data labels** are in the picture as they are on screen, the labels at the lines and the data labels the chart drew included, and none the chart left out. The legend lists each [reference line](#reference-lines-steps-and-data-labels) with a dashed sample of its pattern, its label and its value, after the series of a widget that has them, and comes before the footnote and the footer. A widget shown as a table lists its lines in the notes under the table.
- **Small bases and the lines of the bases.** Marks with a [small base](#small-bases) are drawn dimmed as on screen (lighter, with their dashed outline or hatching, and a stat tile's figure lighter), and the widget's small-base note, base n line and no-data line (see [The lines under a widget](#the-lines-under-a-widget)) are drawn in whole among the notes under the chart, after the chart's own notes and before the footnote. A widget with no such setting gives the same image as before.
- **Always on white, in the light colors** of the dashboard's palette and the colors kept by value, whatever your theme. The gaps between stacked segments and heat map cells are white. This holds while Tamiza has no dark theme: a theme switch has to keep a light scope for the images.
- **The SVG is a standalone file.** Its text stays text, so you can edit it in a vector editor, and it has no script, style sheet, embedded HTML or link; a group value that looks like markup is written as text. Fonts are the system's: a program that lacks the font substitutes another.
- **The PNG is that SVG at twice its size**, marked 192 dots per inch, so Word and PowerPoint place it at its size on screen. It holds at most 16,000,000 pixels and 8,192 on a side: a very tall widget is drawn smaller than twice, and one that does not fit even at its size on screen is refused with "This widget is too large for a PNG image. Try the SVG image." The same message, with "could not be made", appears where the browser cannot draw it.
- **Tables are capped.** A widget shown as a table, or a cross table, gives an image of its first 100 rows and the columns that fit in 2,400 pixels, always with the first column, and says how many rows and columns it left out ("28 rows not shown in the image.") and that the workbook has them all.
- **Maps carry no basemap.** The image holds the shapes in your current view and the legend, never tiles, the Google logo or map credits, because tiles loaded from another origin cannot be drawn into a file and Google does not allow keeping a copy. When a basemap is shown, the formats say "Shapes and legend, without the basemap" and the image says "The basemap is not included in the image." [Print](#printing-a-dashboard) keeps the basemap.
- **Words and numbers** are in the interface language, and numbers read as on your screen. The caption and the table name the columns as the screen does (see [Column names](#column-names)).
- **The name of a file** is the project, the dashboard (the version's name while one is shown), the widget, `_v9` for version 9 and the day in the project's time zone, each part in lower-case ASCII with dashes: `census-2026_housing_coverage-by-municipality_2026-11-02.png`.

### Workbook

**Excel workbook (XLSX)** is prepared by the server, with the code that builds the widget's table in a [scheduled email](dashboard-reports.md)'s workbook, so the file, the email and **Show as table** list the same numbers. It is downloaded through the same single-use link and has the same file size limit as the other exports.

- **Its first sheet is the widget's table**, named after the title (shortened to Excel's rules): the same columns, totals, "(none)" and "Other" as in the email, with numbers as numbers in the indicator's format (a box plot over a count has two decimals), and text never a formula. A stat tile gives one column and one number. A map lists its groups, values and tooltip columns, with no kind of shape or geometry. It holds the rows the widget plots, up to the widget row limit.
- **Its second sheet, "Summary"**, names the project, the dashboard (and the version), the dashboard's [source](#dashboard-source) when it has one (the version's while a version is shown), the widget and its subtitle, the indicator, what is plotted, its [reference lines](#reference-lines-steps-and-data-labels) when it has some (each with its label and its value, as the widget writes values, in the order the widget lists them), every filter of the dashboard with its value or "All" (or says that no filter applies), the filters that do not narrow the widget, when the indicator was published, when the workbook was made, the number of rows, the widget's footnote and its notes. A widget or dashboard without a subtitle, footnote, source or reference lines has no line for it. The data sheet holds the widget's table, with no row or column for the lines.
- **The widget's saved settings travel with the request**, as the page and the scheduled email use them, so the file reads like the screen: the value column's header and the "Summary" line of what is plotted use the widget's own [caption text](#caption) and still name what is plotted when the caption is hidden, the header names the widget's own [unit](#unit), and a map's tooltip columns are headed by their labels and written in their formats (a `count` column as whole numbers), as in the email's workbook.
- **The base settings are those on your screen too.** The page sends the small-base threshold and the base n line in force for the widget (its own over the dashboard's, or over those of the version shown), so the data sheet has the n column and the "Summary" the small-base note, the n line and the no-data line, as a [scheduled email](operations.md#email-optional)'s workbook has them; a widget that [leaves out no-data values](#no-data-values) leaves those rows out of the file as well. A cell of a small base holds its number alone. A page of an earlier release sends none of this, and the workbook is the one that release gave: no n column and no note.
- **The filters are those on your screen**, apart from the ones the widget ignores: the page sends them with the widget's settings, so a widget of a version the dashboard no longer has can also be exported. If another member changed the dashboard's filters since you opened the page, the export is refused with "The dashboard’s filters changed since the page was opened. Reload the dashboard and try again."
- **Newer data.** The file always reads the table as published at that moment. If a run published it after your page loaded, the message adds when, and that reloading the page shows it.
- **Column names.** The data sheet's headers and the "Summary" line of what the widget plots use the [column names](#column-names), as the email's workbook does: the widget's own names as your screen sent them (so a widget of a version has that version's), and the indicator's names as the current version of its script gives them. A name that looks like a formula is written as text.
- **Words and formats follow your interface language and region** ("Resumen" in Spanish, regional dates in the Summary); names, labels and values stay as created.
- **When it is refused:** the indicator has not published (404), the table lost a column the widget needs (422, naming it), the file would pass the size limit (422), or reading took too long (503). A table that lost all its rows between your page and the file still gives a file, whose first sheet says why.

## Printing a dashboard

**Print**, after the dashboard's name, prints the open dashboard or saves it as PDF through the browser's own dialog. Every member has it, viewers included, once the dashboard has widgets. The Performance and Clean data dashboards have no Print action.

- **Getting ready.** Print lays the dashboard out as it will print, waits for the widgets still loading and for the maps' basemap tiles (at most 20 seconds), then opens the dialog. While it waits, the button is busy and screen readers hear "Preparing the print…". When the dialog closes, whether you printed or cancelled, the dashboard is back as it was, with the focus on **Print**. Printing saves nothing and changes neither the filter values nor the page address.
- **What the sheets show.** The project's name and form, the dashboard's name, when it was printed (in the project's time zone), the latest time one of its widgets' indicators was published, the dashboard filters with the values in force ("Municipality: Cali · Round: All"), the dashboard's [source](#dashboard-source) when it has one ("Source: …", "Fuente: …" in Spanish; the version's while a version is shown), and the widgets as you see them, charts or tables. The source is given once, in this header, and not again under the widgets. They leave out the top bar, the tabs, every button and menu, the widgets' handles, the map controls, notices, tooltips and the list of indicators.
- **Layout.** Pages have 10 mm margins, and the widgets keep the 12 columns, positions and heights they have on a wide screen, even when you print from a phone. A widget that fits on a sheet is never split: it moves whole to the next sheet. Only a widget taller than a sheet continues on the next one. A map shows all its shapes, or the area you zoomed into, with its legend and the basemap's credit.
- **Tables.** A widget shown as a table prints the rows that fit in its box and says how many were not printed ("28 rows not printed."). **Export** gives every row.
- **Column names.** The captions and the headers of tables on paper name the columns as the screen does (see [Column names](#column-names)).
- **Words around a widget.** A widget's subtitle and footnote print whole, where the screen cuts them to two lines, and take their room from the widget's plot inside its box. Its caption prints as on screen (its own text, or no line when the widget hides it), and its values with its own unit. The indicator's description and its button are not printed (see [Words around a widget](#words-around-a-widget)). A widget whose texts leave its chart less room than the chart's least size, such as a column chart 4 columns wide and 3 rows high with a footnote of 500 characters, prints the whole footnote and the chart cut at its own box, never drawn over the footnote; give such a widget a taller tile, which the widget form's warning suggests.
- **Reference lines, steps and data labels.** A chart's [reference lines](#reference-lines-steps-and-data-labels) print as dashed lines with their labels, its steps as steps and its data labels as on screen, all laid out again for the size on paper: a data label that does not fit its mark there is not printed and no label covers another. The key of the lines prints whole and takes its room from the plot like the subtitle and the footnote, so a small tile cuts the chart at its own box, never the key. The one exception is a footnote that fills the whole tile (500 characters in a 4 × 3 tile): the tile clips what does not fit, the key and the chart included, and the footnote prints as far as the tile holds it.
- **Small bases.** Dimmed marks print as on screen: lighter, with the dashed outline or the hatching, which read in black and white and need no **Background graphics**, since they are part of the chart's own drawing. The small-base note, the base n line and the no-data line of each widget print whole under its chart, before its footnote (see [The lines under a widget](#the-lines-under-a-widget)).
- **Printing with Ctrl+P** (or the browser's menu) gives the same sheets, but it cannot wait: a widget still loading says on paper that it had not finished loading, and a map prints the tiles it already has. Prefer **Print**.
- **Orientation.** Choose it in the browser's dialog. In landscape, A4 and Letter print the dashboard at full size. In portrait, the whole dashboard is scaled down to the sheet's width, upright and with its 12 columns, so it reads smaller but more rows fit on each sheet. On a sheet smaller than A4 or Letter, such as A5 in portrait, Chrome and Edge cannot scale it down enough and cut off the right-hand columns: choose landscape or a smaller scale.
- **The browser's options.** Paper size, orientation and scale stay yours to choose; a smaller scale fits more rows on a sheet. Colors print without turning on **Background graphics**. The browser offers the project, the dashboard and the date as the PDF's file name, and prints the same in its own header when **Headers and footers** is on.
- **Browsers.** Printing is made for Chrome, Edge and Firefox on a computer. Other browsers print the same content but may not scale the dashboard to a portrait sheet or keep every widget whole.

## Version history

Tamiza keeps a history of each of a project's own dashboards. Every change saved to a dashboard records a new **version**: the dashboard as it is after the change, with the dashboard's settings (its name, its filters with their defaults, its palette, its colors kept by value, its [source](#dashboard-source) when it has one, its [base sizes](#base-sizes-of-a-dashboard) when it has any and its other display settings) and its widgets, each with every setting it is saved with (its title, chart type, query, display settings such as its color or scale, its orientation, its [words](#words-around-a-widget) (subtitle, footnote, caption, unit and description button), its [reference lines, steps and data labels](#reference-lines-steps-and-data-labels) and its own [column names](#column-names), the dashboard filters it ignores, position and size). An indicator's column names belong to the versions of its script, not to a dashboard's: a version of a dashboard shown later names its columns with the indicator's current names. The newest version is the dashboard as it is. Analysts and admins open the history from **Dashboard actions** with **Version history**; viewers are not offered it, and the API refuses their requests (403). Performance and Clean data have no history.

### What makes a version

- **Every change, however small:** adding the dashboard, renaming or duplicating it, saving its filters, saving its [colors](#colors) (the palette and the colors kept by value), saving its [source](#dashboard-source), saving its [base sizes](#base-sizes-of-a-dashboard), and adding, editing, removing, moving or resizing a widget, or moving one to or from another dashboard. Adding a full stop to a widget's title is a version.
- **One action, one version.** A drag is one version, with the widgets it pushed down, and so is a run of arrow presses on a widget's handle: the page saves them together once the dragging has been still for a moment (see [The grid](#the-grid)). Two drags with a pause between them are two versions. A widget moved to another dashboard makes one version on each: one says the widget went to the other dashboard, the other that it came from it.
- **No change, no version.** Saving the filters, the colors, the source, the base sizes or a widget's form without changing anything makes none, and a dashboard that never had base sizes keeps the versions it had: the first save after they were introduced makes none for them alone. Declaring an indicator's base column, no-data values or answers, like its column names, makes no dashboard version. Changing only a widget's subtitle, footnote, caption, unit or description button, or its own [order of values](#order-of-values), does make one, which says the widget changed; restoring a version brings the widget's own order back.
- **A copy starts its own history.** **Duplicate** gives the copy a version 1 that says which dashboard it was duplicated from, and the original keeps its versions. A move or resize made on the original a moment before is saved first, as the original's own version.
- **Removing a dashboard removes its versions with it.**

These are not changes to the dashboard, and never make a version: runs and the data they publish (the widgets show the new data), a change to an indicator's [column names](#column-names) or to the order, values kept at the end and hidden prefixes of its columns (the widgets show the new names and order, and a version viewed or restored is drawn with the indicator's current ones), synchronizations, the filter values a member picks, a member's basemap and map view, **Show as table**, printing, the order of the project's dashboards (**Move left** and **Move right**) and the scheduled email.

A dashboard that existed before version history began has a version 1, **State before version history**, that holds it as it was then. It is attributed to the last person who changed the dashboard or one of its widgets, at that time.

### Requests from AI clients

Changes that Claude or another AI client makes through the [MCP server](mcp.md) make versions too, attributed to the person it acts for and marked **via MCP**, with the request the client gave in quotes ("Add five water indicators"). The client gives that request with every change it makes for it, so all the changes of one request make one version of each dashboard they change, however many widgets it adds. A new version starts with the next request, when you or another member change the dashboard in between, or when 30 minutes pass between two of its changes. To undo a request, ask the client: it restores, on each dashboard the request changed, the version before it, which is a version of its own, marked **via MCP**, as any restore.

### Ten versions kept

Each dashboard keeps its 10 newest versions, the current one included: the change that makes the eleventh deletes the oldest. Numbers keep counting up and are never reused, so a dashboard that keeps versions 3 to 12 makes version 13 next. The 10 is a rule of the product, not a setting.

### The history

**Version history** first saves a move or resize still waiting for its pause, so the newest version holds the widgets where you left them, then lists the kept versions, newest first. Each shows:

- its number, with **Current** on the newest;
- what changed, up to three lines ("Widget “Water source” added", "Renamed to “Agua”", "Colors changed", "Source changed", "Base sizes changed", "Widgets moved or resized"), then how many more ("and 2 more");
- for a version an AI client made, the request in quotes;
- who made it and when its last change was saved ("Ana Pérez · Oct 31, 2026, 10:32 AM"), followed by **via MCP** for a version an AI client made.

Names and titles are shown as they were at the time, as text. Every version but the current one has **View** (see [Viewing a version](#viewing-a-version)) and **Restore** (see [Restoring a version](#restoring-a-version)). **Close**, or Escape, closes the history and returns the focus to the actions button. On a phone the history fits the screen's width, and the list scrolls.

### Viewing a version

**View** shows the version on the dashboard page, in place of the dashboard as it is: its name as the heading, its filters at their defaults, its palette and colors kept by value, its source (no line when the version has none), its [base sizes](#base-sizes-of-a-dashboard) (its small bases dimmed and its n lines as they were, none when the version has none, whatever the dashboard has now), and its widgets with today's data, each with the words it had. Each widget is drawn from the settings the version holds, so a widget removed since shows again, and one whose indicator or columns are gone since says so, as a [stale widget](#when-a-table-changes) does. A notice above it says which version it is, who saved it and when ("Viewing version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM. It is read-only."); the focus moves to it, so screen readers read it.

- **Read-only.** While a version is shown, the heading has no actions, **Print** included, and there is no **Add widget**, no count of widgets, no handles to move or resize a widget, and the widget menus have neither **Edit widget** nor **Remove widget**. **Show all as tables**, the widget menus' **Show as table** and [Export](#exporting-a-widget) group and the exports in the list of indicators stay: they change nothing. Printing from the browser's menu (Ctrl+P) while a version is shown prints the version, and the sheet names it under the heading ("Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM"), as the PDF's file name does.
- **Filters.** Change the version's filter values as on any dashboard; they change only your screen. The page address keeps the values of the dashboard as it is, so a link copied meanwhile opens the dashboard as it is, not the version. The values a text filter offers come from the indicators of the version's widgets, not of the dashboard's.
- **Back to the current version** shows the dashboard as it is again, with the focus on its heading. Opening another dashboard, or reloading the page, also leaves the version.
- **Restore version 9**, in the notice, restores the version shown, as **Restore** in the history does.

### Restoring a version

**Restore**, in the history or in the notice of a version shown, asks first ("Restore version 9?": "The name, filters, colors and widgets of “Housing” go back to how they were on Oct 31, 2026, 10:32 AM. The current state stays in the history."). On **Restore**, a move or resize still waiting for its pause is saved first, then the dashboard's name, its filters with their defaults, its palette and colors kept by value, its source, its base sizes and its widgets (with their words, reference lines, steps and data labels) become those of the version, for every member. A version recorded before sources existed has none, so restoring it leaves the dashboard without a source; a version recorded before base sizes existed has none either, so restoring it turns the small-base threshold and the n lines off. The restore is a new version that says which version it restored ("Version 9 restored"), so the state it replaced stays in the history, and a restore can itself be undone by restoring the version before it. The dashboard keeps its address, its place among the dashboards and its [scheduled email](operations.md#email-optional), whose recipients, days and time stay as they were and whose next email shows the restored widgets.

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
| Columns an indicator gives a [column name](#column-names), an order, or a [base, no-data values or answers](#bases-and-small-samples) (together) | 100 |
| Columns a widget gives a column name of its own | 10 |
| Length of a column name | 80 characters |
| Values in an [order of values](#order-of-values), of a widget or of an indicator's column | 100 |
| Values an indicator keeps at the end of a column | 20 |
| Base columns of an indicator | 1 |
| Values that mean no data in an indicator's column | 20, each of 1 to 500 characters |
| Small-base threshold of a [dashboard](#base-sizes-of-a-dashboard) | 1 to 10,000 (empty is off) |
| Small-base threshold of a widget | 0 (off for the widget) or 1 to 10,000 |
| Length of a text in a tooltip | 200 characters (cut with an ellipsis) |
| Indicators in a project | 100 |
| Length of a title | 200 characters |
| Length of a widget's [subtitle](#subtitle-and-footnote) | 200 characters |
| Length of a widget's [footnote](#subtitle-and-footnote) | 500 characters |
| Length of a widget's own [caption](#caption) text | 200 characters |
| Length of a widget's own [unit](#unit) | 32 characters |
| Reference lines in a widget | 4 |
| Length of a reference line's label | 40 characters |
| Length of a dashboard's [source](#dashboard-source) | 300 characters |
| Colors kept by value on a dashboard | 64 |
| Length of the value of a color kept | 500 characters |
| Versions a dashboard keeps | 10, the current one included |

An operator can change the first two; the others are fixed. See [operations.md](operations.md#common-changes). A widget whose query matches more rows than the limit shows the first ones and says so ("Only the first 5,000 rows are shown"); add a filter or group by a coarser column. A query that takes longer than the dashboard time limit (25 seconds by default, shared with the clean data dashboard) is stopped with a message.

## What it does not do

- A widget plots **one indicator** with at most **two dimensions** (a group and a series); it does not combine several indicator tables in one chart, add a third dimension, plot two measures or sort its series by value.
- Its colors are those of the dashboard's [palette](#palettes), one of four, its [colors kept by value](#colors-kept-by-value) and, for a few types, the widget's own [color or scale](#widget-color-and-scale). You cannot type a color code, define a palette of your own, color a stat tile, change the red and blue arms of Likert bars or color by a threshold (good, warning, critical), and the scheduled email shows tables with no colors.
- A reference line cannot take its value from another indicator or other filters, mark a band between two values, stand vertical on a line chart's category axis or take a color of its own, and only bar, column and line charts have them; a stat tile, histogram, pie, map, box plot, heat map, pyramid, Likert chart and cross table have none. Data labels are not written on stacked bars, heat maps, pyramids and Likert bars, nor as short numbers. A line chart's steps are placed on its category axis, not at the true distance between prices.
- Its basemap is a background and nothing more: there is no satellite imagery from anyone but Google, no street labels over the imagery, no basemap you define yourself, and no administrative boundaries of its own, since the shapes come from your script.
- It does not save a zoomed view as the map's starting view, and only maps have a tooltip with columns; a tooltip column's numbers are always summed, with no choice of minimum, maximum or average.
- Clicking a bar, slice or shape does not filter the other widgets, a filter's values do not depend on another filter (a department, then its municipalities), and there are no relative periods such as "the last 7 days".
- A dashboard filter matches columns by name: it cannot narrow a column called `municipality` in one indicator and `mpio` in another. Name a dimension the same in every indicator. A color kept by value matches by name too (see [Colors kept by value](#colors-kept-by-value)).
- Your filter values are not remembered between visits, except in the page address.
- An [order of values](#order-of-values) does not apply to the values a filter offers, to the Colors form or to the order editors themselves, which list full values in their own order; nor to a map's shapes, a histogram's ranges or a stat tile. It hides only a leading number (not `a)` or `IV.`), it cannot choose which series or slices are kept past the limit (the largest are kept, whatever the order), and it does not hide values such as "Sin dato".
- [Bases and small samples](#bases-and-small-samples) count units without weights: every n counts each household once. A histogram and a box plot have no n of their own for the widget, a widget has an n only when its indicator declares a base column or answer columns, and the values that mean no data cannot be left out of a stat tile, a histogram, a box plot or a map. Dimming is a setting of the dashboard or the widget, not a switch of each viewer, and Tamiza does not decide which columns hold answers: the indicator declares them. Hiding "Sin dato" for the whole dashboard through its filters, a "not in" filter and n columns in every workbook whatever the settings are not there.
- Every member sees every dashboard: there are no dashboards for some members only, and no dashboards of one's own.
- It does not copy a dashboard or a widget to another project, and a removed dashboard cannot be brought back.
- It does not compare two versions side by side, name or pin a version, or keep more than the 10 newest.
- Tabs cannot be dragged: a dashboard moves with **Move left** and **Move right**, which also work from the keyboard and on a phone.
- An image of a widget has no basemap, no dark theme and no size to choose; it is made at the size of the widget on screen. There is no export of a whole dashboard at once, no PDF made on the server and no widget CSV.
- It does not make a PDF on the server, and a scheduled email attaches no PDF: printing goes through the browser's dialog, which can save as PDF.
- It does not refresh by itself: reload the page after a run, or to see the dashboards other members have added, renamed, moved or removed.
