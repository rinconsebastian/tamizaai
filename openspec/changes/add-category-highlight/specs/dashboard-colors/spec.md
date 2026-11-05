# Spec Delta

## MODIFIED Requirements

### Requirement: Colors kept by value
An `analyst` or `admin` SHALL be able to keep a color for a value of a column on a dashboard. Each entry names a column, a value as text with no NUL character, and one of the palette's colors by its number, or the neutral color. A dashboard SHALL hold at most 64 entries. Within one column, a value and a palette color SHALL each appear at most once. Saving entries that break these rules SHALL be refused, with an error on the entry, or on the list for the limit.

#### Scenario: Color kept for a value
- **WHEN** an `analyst` keeps color 5 for the value `Female` of the column `sex` on "Housing"
- **THEN** the entry is saved, and every member who opens "Housing" sees it

#### Scenario: Color used twice in a column
- **WHEN** an `analyst` keeps color 5 for both `Female` and `Male` of the column `sex`
- **THEN** the API responds 400 with an error on the second entry

#### Scenario: Same color in two columns
- **WHEN** an `analyst` keeps color 5 for `Female` of `sex` and for `Rural` of `zone`
- **THEN** both entries are saved

#### Scenario: Value kept twice with the neutral
- **WHEN** an `analyst` keeps the neutral color for `4. No sabe` of `respuesta` and also color 7 for `4. No sabe` of `respuesta`
- **THEN** the API responds 400 with an error on the second entry

### Requirement: Where colors kept by value apply
A color kept for a value of a column SHALL be used wherever the column decides a color, unless a highlight leaves it out:
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

#### Scenario: Kept color outside a highlight
- **WHEN** "5. Centro" keeps color 2 for `Carro`, color 3 for `Moto` and color 8 for `Todos los vehículos` of `vehiculo`, and holds a box plot grouped by `vehiculo` that highlights `Todos los vehículos`
- **THEN** the box of `Todos los vehículos` is drawn in color 8, and the boxes of `Carro` and `Moto` in the neutral color

### Requirement: Colors of values with no color kept
In a widget whose colors follow a column, a value with no color kept SHALL take the palette's colors in the widget's series order, after the colors kept by value in that widget are set aside. Two values of one widget SHALL never share a color other than the neutral. "Other" SHALL keep the neutral color. In a chart with no series, a bar with no color kept SHALL be drawn in the widget's color, and so SHALL a box with no color kept in a box plot, unless a highlight leaves it out.

#### Scenario: Colors set aside
- **WHEN** a stacked column chart has the series `Piped`, `River` and `Well`, and its dashboard keeps color 1 for `Well`
- **THEN** `Well` is drawn in color 1, `Piped` in color 2 and `River` in color 3

#### Scenario: Other stays neutral
- **WHEN** a stacked column chart shows 7 series and "Other"
- **THEN** "Other" is drawn in the neutral color whatever colors are kept by value

#### Scenario: Bar left out by a highlight
- **WHEN** a bar chart with no series in color 2 highlights only `Centro cultural o casa de la cultura`, and its dashboard keeps no color for any other place
- **THEN** every other bar is drawn in the neutral color, not in color 2

## ADDED Requirements

### Requirement: Neutral color kept by value
An entry of the colors kept by value SHALL keep the neutral color, the grey of "Other", when it gives the number 0 in place of a palette color. Any number of values of one column SHALL be able to keep it, each entry counting toward the 64 of a dashboard. It SHALL apply wherever a color kept by value applies, and SHALL take none of the palette's colors from the column's other values.

#### Scenario: Neutral for several values
- **WHEN** an `analyst` changes the colors kept for `4. No sabe` and `3. No o no sabe` of `respuesta` on "8. Cruces" to the neutral color
- **THEN** both entries are saved, and both values are drawn in the neutral color wherever `respuesta` decides a color

#### Scenario: Neutral answer
- **WHEN** "2. Teatro" keeps the neutral color for `4. No sabe` of `respuesta`, and holds a 100 % column chart by `comuna` with the series `respuesta`
- **THEN** the `4. No sabe` segments and legend entry are drawn in the neutral color, and the other answers in their colors

#### Scenario: Neutral sets nothing aside
- **WHEN** a 100 % bar chart has the series `1. Sí`, `2. Tal vez`, `3. No` and `4. No sabe`, and its dashboard keeps only the neutral color for `4. No sabe` of that column
- **THEN** `1. Sí`, `2. Tal vez` and `3. No` are drawn in colors 1, 2 and 3, and `4. No sabe` in the neutral color

#### Scenario: Neutral is no widget color
- **WHEN** an `analyst` saves a column chart with no series whose own color is 0
- **THEN** the API responds 400 with an error on the widget's color

#### Scenario: Dashboard saved before the neutral color
- **WHEN** a member opens "2. Teatro", whose colors kept by value were saved before the neutral color existed
- **THEN** its widgets are drawn in the colors they had, and its colors kept by value read as they were saved

### Requirement: Color limits named in refusals
When saving colors kept by value is refused for a limit, the message SHALL name that limit: a dashboard keeps at most 64 colors by value, and a column keeps each of the palette's 8 colors for one value only, while the neutral color can be kept for any number of values.

#### Scenario: Message for a color used twice
- **WHEN** an `analyst` keeps color 5 for both `Acceso` and `Interés` of `barrera_tipo`
- **THEN** the error on the second entry says that a column keeps each of the palette's 8 colors for one value, and that the neutral color can be kept for more

#### Scenario: Sixty-five entries
- **WHEN** an `analyst` saves 65 colors kept by value on "2. Teatro"
- **THEN** the API responds 400 with an error on the list that says a dashboard keeps at most 64 colors by value

### Requirement: Highlighted values
A bar or column chart, a pie chart, a box plot with a group column and a line chart with a series SHALL be able to highlight up to 20 values: values of its series column when it has one, else of its group column. A highlighted value SHALL keep the color it has without the highlight. Every other value, the missing value "(none)" and "Other" SHALL be drawn in the neutral color. Values SHALL be matched as exact text, as colors kept by value are, and SHALL NOT be checked against the data.

#### Scenario: One place highlighted
- **WHEN** "2. Teatro", with the palette *Earth*, holds a bar chart of `teatro_hoy` grouped by `categoria` in color 1 that highlights `Centro cultural o casa de la cultura`
- **THEN** that bar is drawn in color 1 of *Earth* and every other place in the neutral color

#### Scenario: Kept color of a highlighted value
- **WHEN** "3. Escuelas" keeps color 1 for `Culturama (escuelas de formación)` of `categoria`, and holds a pie chart grouped by `categoria` that highlights that value
- **THEN** its slice is drawn in color 1 and every other slice in the neutral color

#### Scenario: Highlighted series
- **WHEN** a line chart of `teatro_demanda` grouped by `precio` with the series `comuna` highlights `COMUNA 1`
- **THEN** the line of `COMUNA 1` keeps its color and is drawn over the other lines, which are in the neutral color

#### Scenario: Highlighted value filtered out
- **WHEN** a member sets a dashboard filter after which no row of a bar chart holds its one highlighted value
- **THEN** every bar of that chart is drawn in the neutral color

#### Scenario: Missing group left out
- **WHEN** a column chart grouped by `comuna` highlights `COMUNA 7` and has rows with no `comuna`
- **THEN** the "(none)" column is drawn in the neutral color

#### Scenario: Widget saved before highlights
- **WHEN** a member opens a bar chart saved before highlights existed
- **THEN** its bars are drawn in the colors they had

### Requirement: Highlighted values checked when saved
Saving a widget whose highlight breaks a rule SHALL be refused, with an error on the highlight or on the value it names: more than 20 values, a value given twice, a null value, a value that holds a NUL character or is longer than 500 characters, or a highlight on a kind that takes none, on a line chart with no series or on a box plot with no group column. The message SHALL name the limit it breaks. An empty list SHALL be saved as no highlight.

#### Scenario: Twenty-one values
- **WHEN** an `analyst` saves a column chart that highlights 21 values
- **THEN** the API responds 400 with an error on the highlight that says a widget highlights at most 20 values

#### Scenario: Value highlighted twice
- **WHEN** an `analyst` saves a line chart with the series `comuna` that highlights `COMUNA 1` twice
- **THEN** the API responds 400 with an error on the second value

#### Scenario: Highlight on a heat map
- **WHEN** an `analyst` saves a heat map that highlights `1. No pagaría`
- **THEN** the API responds 400 with an error on the highlight

#### Scenario: Line chart with no series
- **WHEN** an `analyst` saves a line chart with no series that highlights `$10.000`
- **THEN** the API responds 400 with an error on the highlight that says a line chart highlights its series

#### Scenario: Box plot with no group column
- **WHEN** an `analyst` saves a box plot of `centro_tarifa_hogar` with no group column that highlights `Carro`
- **THEN** the API responds 400 with an error on the highlight that says a box plot highlights its groups

#### Scenario: Empty highlight
- **WHEN** an `analyst` saves a bar chart with an empty list of highlighted values
- **THEN** the widget is saved with no highlight, as it would be without the list

### Requirement: Highlighted values not shown by color alone
Wherever a chart writes the name of a highlighted value, on its axis or in its legend, the name SHALL be in bold, and the chart's accessible name SHALL say which values are highlighted, so a highlight does not rely on color alone. A widget with no highlight SHALL keep its labels and accessible name as they are.

#### Scenario: Bold label
- **WHEN** a member opens the bar chart of "2. Teatro" that highlights `Centro cultural o casa de la cultura`
- **THEN** that place's label on the axis is in bold and the other labels are not

#### Scenario: Bold legend entry
- **WHEN** a member opens a pie chart that highlights `Culturama (escuelas de formación)`
- **THEN** that slice's name in the legend is in bold and the other names are not

#### Scenario: Read to a screen reader
- **WHEN** a member using a screen reader reaches the bar chart that highlights `Centro cultural o casa de la cultura`
- **THEN** the chart's summary says that `Centro cultural o casa de la cultura` is highlighted

### Requirement: Highlighted values saved with the widget
A widget's highlighted values SHALL be part of the widget, as its color is: changing them SHALL make a version that says the widget changed, a version SHALL hold them and a restore SHALL bring them back, and a widget of a duplicated dashboard and a widget moved to another dashboard SHALL keep them. Changing them SHALL NOT query the indicator again. A widget with no highlight SHALL be stored as it was before highlights existed.

#### Scenario: Highlight changed
- **WHEN** an `analyst` makes the column chart "2.5. En qué municipio" of "2. Teatro" highlight `Duitama`
- **THEN** "2. Teatro" has a new version that says the widget "2.5. En qué municipio" changed

#### Scenario: Highlight restored
- **WHEN** an `analyst` restores the version of "2. Teatro" from before that change
- **THEN** the column chart has no highlight again, and the restore is a new version

#### Scenario: Highlight duplicated
- **WHEN** an `analyst` duplicates "2. Teatro"
- **THEN** the copy of "2.5. En qué municipio" highlights `Duitama`

#### Scenario: Highlight moved
- **WHEN** an `analyst` moves "2.5. En qué municipio" to "5. Centro"
- **THEN** on "5. Centro" it still highlights `Duitama`

#### Scenario: Widget form saved unchanged
- **WHEN** an `analyst` opens a widget saved before highlights existed and saves the widget form without changing anything
- **THEN** no version is made and the widget's stored settings are unchanged

### Requirement: Highlighted values in the widget form
The widget form SHALL offer a highlight to the kinds that take one: a list of up to 20 values, each picked from the values the widget plots under its own filters or typed, and each removable. The preview SHALL show the highlight without querying again. While the form is open it SHALL keep the values chosen for each column, and SHALL save those of the column the widget colors by; a kind that takes no highlight SHALL save none.

#### Scenario: Highlighted value picked from the data
- **WHEN** an `analyst` adds a highlighted value to the bar chart "2.4. En qué lugares" and types "Centro"
- **THEN** the values offered include `Centro cultural o casa de la cultura`

#### Scenario: Highlight previewed
- **WHEN** the `analyst` picks `Centro cultural o casa de la cultura`
- **THEN** the preview draws that bar in the widget's color and the others in the neutral color, and no new query is sent

#### Scenario: Series added
- **WHEN** an `analyst` gives the series `sexo` to a column chart that highlights `COMUNA 1` of its group column `comuna`, and saves it
- **THEN** the widget is saved with no highlight

#### Scenario: Column changed back
- **WHEN** an `analyst` changes the group column of a highlighted bar chart and changes it back before saving
- **THEN** the form still holds the values it highlighted

#### Scenario: Kind without a highlight
- **WHEN** an `analyst` changes a highlighted bar chart into a heat map and saves it
- **THEN** the heat map is saved with no highlight

#### Scenario: Refusal shown on the field
- **WHEN** the API refuses a value of the highlight
- **THEN** the form shows the message next to that value

### Requirement: Neutral color in the Colors form
**Colors** SHALL offer the neutral color for every color kept by value, after the palette's eight colors, named in text as the neutral grey and reachable from the keyboard as the others are. A new entry on a column whose entries already use all eight of the palette's colors SHALL start on the neutral color.

#### Scenario: Neutral offered
- **WHEN** an `analyst` adds a color kept by value in **Colors**
- **THEN** the choices are colors 1 to 8 and the neutral grey, each read to a screen reader by its name

#### Scenario: Ninth value of a column
- **WHEN** an `analyst` on "8. Cruces", whose entries of `respuesta` already use colors 1 to 8, adds an entry after one of them
- **THEN** the new entry is on `respuesta` and starts on the neutral color, and saving it with a new value is accepted
