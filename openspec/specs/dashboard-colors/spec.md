# dashboard-colors Specification

## Purpose
Lets each project dashboard have its own colors: a palette chosen from a fixed, validated set, colors kept for chosen values across its widgets, and a color or sequential scale per widget drawn from that palette.

## Requirements

### Requirement: Palettes
Tamiza SHALL offer a fixed set of palettes: *Tamiza*, *Institutional*, *Earth* and *Vivid*. Each palette SHALL have eight categorical colors in a fixed order and one default sequential scale. The neutral color of "Other" SHALL be the same in every palette. Each color SHALL have its own value for the light theme and for the dark theme, as today's chart colors do. A member SHALL NOT be able to type a color code.

#### Scenario: Palettes offered
- **WHEN** an `analyst` opens **Colors** on a dashboard
- **THEN** it offers *Tamiza*, *Institutional*, *Earth* and *Vivid*, and no field for a color code

#### Scenario: No color codes
- **WHEN** an `analyst` saves a dashboard's palette as `#ff0000`
- **THEN** the API responds 400 with an error on the palette

### Requirement: Palette checks
In both themes, every palette's eight colors SHALL pass four checks, and the eighth color SHALL pass the first two against the first color too, because a pie of eight values draws them side by side:
- adjacent colors in its order stay apart for readers with each common color-vision deficiency (protan and deutan);
- adjacent colors stay clearly apart for full-color readers;
- each color is inside the lightness band;
- each color is above the chroma floor.

#### Scenario: Checks on every palette
- **WHEN** the project's color checks run
- **THEN** they check each palette and each sequential scale in both themes, and fail when any color breaks a check

#### Scenario: Eighth color beside the first
- **WHEN** the eighth color of a palette is too close to its first color for a reader with protan or deutan, or for a full-color reader, though it is far from the seventh
- **THEN** the project's color checks fail

### Requirement: Scale checks
Every sequential scale SHALL keep one hue and step its lightness one way, in both themes. Each step's label color SHALL read at 4.5:1 or more on it.

#### Scenario: Scale out of order
- **WHEN** a sequential scale has a second hue, a step that breaks the order of lightness, or a label under 4.5:1 on its step
- **THEN** the project's color checks fail

### Requirement: Dashboard palette
An `analyst` or `admin` SHALL be able to choose the palette of a dashboard. Every member who opens the dashboard SHALL see its widgets in that palette. A `viewer` SHALL NOT be able to change it. A dashboard SHALL have the palette *Tamiza* until one is chosen.

#### Scenario: Palette chosen
- **WHEN** an `analyst` chooses the palette *Earth* for "Housing"
- **THEN** every member who opens "Housing" sees its charts in the colors of *Earth*, and the other dashboards keep theirs

#### Scenario: Viewer cannot change the palette
- **WHEN** a `viewer` tries to change the palette of a dashboard through the API
- **THEN** the API responds 403

#### Scenario: Dashboard saved before palettes
- **WHEN** a member opens a dashboard saved before palettes existed
- **THEN** it has the palette *Tamiza* and its widgets look as before

### Requirement: Widget color
A bar, column or line chart with no series, a histogram and a box plot SHALL be drawn in one of its dashboard's eight palette colors, chosen in the widget form by its number. Color 1 is the default. A widget of any other kind, and a bar, column or line chart with a series, SHALL be refused a color with an error on the color.

#### Scenario: Column chart in color 3
- **WHEN** an `analyst` saves a column chart with no series in color 3 on a dashboard with the palette *Tamiza*
- **THEN** its columns are drawn in the third color of *Tamiza*

#### Scenario: Widget saved before colors
- **WHEN** a member opens a column chart with no series saved before widget colors existed
- **THEN** its columns are drawn in color 1, as before

#### Scenario: Color with a series
- **WHEN** an `analyst` saves a stacked column chart with the series `water_source` and color 3
- **THEN** the API responds 400 with an error on the color

#### Scenario: Box plot in color 4
- **WHEN** an `analyst` saves a box plot in color 4 on a dashboard with the palette *Earth*
- **THEN** its boxes, whiskers and outliers are drawn in the fourth color of *Earth*

### Requirement: Widget sequential scale
A map or a heat map SHALL be drawn on one sequential scale. The scale is chosen in the widget form from blue, teal, green, orange, red, purple and gray, or left to its dashboard palette's default. A widget of any other kind SHALL be refused a scale with an error on the scale.

#### Scenario: Map on the green scale
- **WHEN** an `analyst` saves a map with the green scale
- **THEN** its shapes and its legend use the green scale's steps

#### Scenario: Palette's scale
- **WHEN** a heat map has no scale chosen and its dashboard has the palette *Earth*
- **THEN** its cells use the default scale of *Earth*

#### Scenario: Scale on a pie chart
- **WHEN** an `analyst` saves a pie chart with the green scale
- **THEN** the API responds 400 with an error on the scale

### Requirement: Widget colors follow their dashboard
A widget's color SHALL be its number in the palette, not a color of its own. When its dashboard's palette changes, or the widget moves to another dashboard, the widget SHALL keep its number and be drawn in the palette of the dashboard it is on.

#### Scenario: Palette changed
- **WHEN** a column chart in color 3 is on "Housing" and an `analyst` changes the palette of "Housing" from *Tamiza* to *Vivid*
- **THEN** the chart is drawn in the third color of *Vivid*

#### Scenario: Widget moved
- **WHEN** an `analyst` moves a column chart in color 3 from "Housing", with the palette *Earth*, to "Field", with the palette *Institutional*
- **THEN** on "Field" it is drawn in the third color of *Institutional*

### Requirement: Colors kept by value
An `analyst` or `admin` SHALL be able to keep a color for a value of a column on a dashboard. Each entry names a column, a value as text with no NUL character, and one of the palette's colors by its number. A dashboard SHALL hold at most 64 entries. Within one column, a value and a color SHALL each appear at most once. Saving entries that break these rules SHALL be refused, with an error on the entry, or on the list for the limit.

#### Scenario: Color kept for a value
- **WHEN** an `analyst` keeps color 5 for the value `Female` of the column `sex` on "Housing"
- **THEN** the entry is saved, and every member who opens "Housing" sees it

#### Scenario: Color used twice in a column
- **WHEN** an `analyst` keeps color 5 for both `Female` and `Male` of the column `sex`
- **THEN** the API responds 400 with an error on the second entry

#### Scenario: Same color in two columns
- **WHEN** an `analyst` keeps color 5 for `Female` of `sex` and for `Rural` of `zone`
- **THEN** both entries are saved

### Requirement: Where colors kept by value apply
A color kept for a value of a column SHALL be used wherever the column decides a color:
- the series of a bar, column or line chart with a series;
- the sides of a population pyramid;
- the slices of a pie chart;
- the bars or columns of a chart with no series, by its group column;
- the boxes of a box plot, by its group column.

It SHALL NOT change a map, a heat map, a Likert widget, a cross table, a histogram, a stat tile, or a line chart with no series.

#### Scenario: Same value, same color
- **WHEN** "Housing" keeps color 5 for `Female` of `sex`, and holds a pie chart grouped by `sex` and a stacked column chart with the series `sex`
- **THEN** `Female` is the slice and the segments drawn in color 5 in both widgets

#### Scenario: Pyramid sides
- **WHEN** "Housing" keeps color 5 for `Female` and color 1 for `Male` of `sex`, and holds a pyramid with `Female` on the left
- **THEN** the left bars are drawn in color 5 and the right bars in color 1

#### Scenario: One bar highlighted
- **WHEN** "Housing" keeps color 2 for `Rionegro` of `municipality`, and holds a bar chart grouped by `municipality` in color 1 with no series
- **THEN** the bar of `Rionegro` is drawn in color 2 and every other bar in color 1

#### Scenario: Likert unchanged
- **WHEN** "Housing" keeps color 8 for `Disagree` of `answer`, and holds a Likert widget with the series `answer`
- **THEN** the Likert widget keeps its negative, neutral and positive colors

#### Scenario: One box highlighted
- **WHEN** "Housing" keeps color 5 for `Female` of `sex`, and holds a box plot grouped by `sex` in color 1
- **THEN** the box of `Female` is drawn in color 5 and every other box in color 1

### Requirement: Colors of values with no color kept
In a widget whose colors follow a column, a value with no color kept SHALL take the palette's colors in the widget's series order, after the colors kept by value in that widget are set aside. Two values of one widget SHALL never share a color. "Other" SHALL keep the neutral color. In a chart with no series, a bar with no color kept SHALL be drawn in the widget's color, and so SHALL a box with no color kept in a box plot.

#### Scenario: Colors set aside
- **WHEN** a stacked column chart has the series `Piped`, `River` and `Well`, and its dashboard keeps color 1 for `Well`
- **THEN** `Well` is drawn in color 1, `Piped` in color 2 and `River` in color 3

#### Scenario: Other stays neutral
- **WHEN** a stacked column chart shows 7 series and "Other"
- **THEN** "Other" is drawn in the neutral color whatever colors are kept by value

### Requirement: Values compared as text
A color kept by value SHALL match a value when the value's text equals the entry's value exactly, case included, as the series of a pyramid are matched. An entry SHALL NOT be checked against the data: an entry whose value no widget shows SHALL stay saved and change nothing. A missing value, shown as "(none)", SHALL NOT take a color kept by value.

#### Scenario: Value not in the data
- **WHEN** "Housing" keeps color 4 for `Lake` of `water_source` and no row of its indicators has `Lake`
- **THEN** the entry stays saved and every widget is drawn as without it

#### Scenario: Case differs
- **WHEN** "Housing" keeps color 4 for `piped` of `water_source` and the data holds `Piped`
- **THEN** `Piped` takes no color kept by value

### Requirement: Colors of a dashboard
The dashboard's actions menu SHALL offer **Colors** to an `analyst` or `admin`. It opens a form, with the keyboard focus on the palette in use, that holds:
- the palettes, each shown with its eight colors and its scale;
- the colors kept by value, each with its column, its value and its color.

The columns offered SHALL be the text columns of the dashboard's indicators, and the values those columns' values. Saving SHALL apply the colors and change nothing when nothing changed.

#### Scenario: Palette previewed
- **WHEN** an `analyst` opens **Colors** on "Housing"
- **THEN** each palette shows its eight colors and its scale, and the current one is selected

#### Scenario: Opens on the palette in use
- **WHEN** an `analyst` opens **Colors** on "Housing", which has the palette *Earth*
- **THEN** the keyboard focus is on *Earth*, the selected palette, and the arrow keys move from it to the next palette

#### Scenario: Value picked from the data
- **WHEN** an `analyst` adds a color kept by value in **Colors**, picks the column `water_source` and searches for "pip"
- **THEN** the values offered include `Piped`

#### Scenario: Viewer has no Colors action
- **WHEN** a `viewer` opens the actions menu of a dashboard
- **THEN** it offers no **Colors** action

### Requirement: Color choice in the widget form
The widget form SHALL offer a widget's color as its dashboard palette's eight colors, and a map's or heat map's scale as the scales' steps. Each choice SHALL be named in text by its number and its hue, never by color alone, and SHALL be reachable from the keyboard. The preview SHALL show the widget in the chosen color and in the dashboard's colors kept by value.

#### Scenario: Color picked
- **WHEN** an `analyst` picks color 3 for a column chart in the widget form
- **THEN** the preview shows the columns in color 3 before the widget is saved

#### Scenario: Named choices
- **WHEN** a member using a screen reader moves through the color choices of the widget form
- **THEN** each choice is read by its number and the name of its hue
