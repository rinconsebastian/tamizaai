## MODIFIED Requirements

### Requirement: Widget subtitle
A widget of any chart type SHALL be able to have a subtitle of 1 to 200 characters, such as the question its chart answers. Every member SHALL see it under its title, in 13 px text in a gray darker than the body text. A compact stat tile SHALL show it from its foot instead (see "Subtitle of a compact stat tile"). The subtitle SHALL NOT change the widget's rows. A widget without a subtitle SHALL show none.

#### Scenario: Subtitle under the title
- **WHEN** an `analyst` gives the bar chart "2.4. En qué lugares" of "Teatro" the subtitle "¿En qué lugares ha visto teatro en el último año? (E4)"
- **THEN** every member who opens "Teatro" sees that subtitle under the chart's title

#### Scenario: Subtitle of a stat tile
- **WHEN** the stat tile "Centro cultural o casa de la cultura", 4 columns wide and 3 rows high, has the subtitle "% de quienes asistieron (E4)"
- **THEN** the subtitle shows under the tile's title, above its figure

#### Scenario: Subtitle size and color
- **WHEN** a member opens the bar chart "2.4. En qué lugares" in the light theme
- **THEN** its subtitle is drawn at 13 px, with a contrast of at least 9:1 against the tile

### Requirement: Widget footnote
A widget of any chart type SHALL be able to have a footnote of 1 to 500 characters, shown at its foot, under its chart, its table or its message that no row matches. On screen the footnote SHALL be the note chip of the widget's foot (see "Widget foot"); on paper and in images it SHALL be printed whole. The footnote SHALL NOT change the widget's rows. A widget without a footnote SHALL show none.

#### Scenario: Footnote under the chart
- **WHEN** the bar chart "2.4. En qué lugares" has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** the foot under its bars has a "Note" chip that opens "No suman 100 %: respuesta múltiple."

#### Scenario: Footnote under the table
- **WHEN** a member shows that bar chart as a table
- **THEN** the "Note" chip is in the foot under the table

#### Scenario: No footnote
- **WHEN** a widget has no footnote
- **THEN** its foot has no "Note" chip

### Requirement: Widget context in a tile
A widget's tile SHALL keep its size: its title, subtitle and foot SHALL take room from its plot. On screen the title SHALL show at most two lines, and the subtitle at most two, or one on a tile two rows high or less. A cut text SHALL end with an ellipsis, be read whole by screen readers and be shown whole on pointing. A chart that does not fit SHALL be cut at its own box, never over the texts or the foot. A stat tile short of room SHALL keep its figure whole and cut its label.

#### Scenario: Long subtitle on a column chart
- **WHEN** a column chart 6 columns wide and 4 rows high has a subtitle of 180 characters that needs three lines
- **THEN** its tile is as tall as before, the subtitle shows two lines ending with an ellipsis, and the columns are drawn in the room left

#### Scenario: Stat tile two rows high
- **WHEN** a stat tile 3 columns wide and 2 rows high has a title of two lines, a subtitle and a footnote
- **THEN** its tile is as tall as before, its subtitle and footnote are chips of its foot, and its figure and its label show whole

#### Scenario: Whole text on pointing
- **WHEN** a member points at a cut title or a cut subtitle
- **THEN** its whole text is shown

### Requirement: Widget context in the widget form
The widget form SHALL let an `analyst` or `admin` set:

- a widget's subtitle and footnote;
- its caption (default, own text or hidden) and, on a bar, column, line or pie chart, whether it is kept in sight under the subtitle. The keep-in-sight option SHALL say that, while it is not checked and the widget has a subtitle, the line is out of sight and its own text shows in the widget's foot. While an own text is out of sight that way, the form SHALL warn so beside the caption, without blocking the save;
- its unit, with the indicator's unit given as a hint;
- whether the description button shows. The option SHALL be unavailable, saying why, when the indicator has no description.

Changing any of them SHALL redraw the preview without querying the indicator again. An error from the API SHALL show on its field.

#### Scenario: Preview without a query
- **WHEN** an `analyst` types a footnote and hides the caption in the widget form
- **THEN** the preview shows the footnote's "Note" chip and no caption line, and no new query is sent

#### Scenario: Caption kept in sight in the form
- **WHEN** an `analyst` checks "Keep in sight under the subtitle" for a column chart that has a subtitle
- **THEN** the preview shows the caption line above the columns and no caption chip in its foot, and no new query is sent

#### Scenario: Keep in sight with a hidden caption
- **WHEN** an `analyst` chooses "Hidden" for the caption
- **THEN** "Keep in sight under the subtitle" is unavailable

#### Scenario: Help of the keep-in-sight option
- **WHEN** an `analyst` gives a column chart a subtitle and the caption text "% de los hogares que asistieron", and leaves "Keep in sight under the subtitle" unchecked
- **THEN** the option's help says that the line is out of sight while the widget has a subtitle and that its text shows in the widget's foot, and the preview's foot opens with that text

#### Scenario: Warning for an own text out of sight
- **WHEN** an `analyst` types the caption text "% de los hogares que asistieron" on a column chart that has a subtitle, or types a subtitle on one that has that caption text, with "Keep in sight under the subtitle" unchecked
- **THEN** an amber warning beside the caption says that the text will not show above the chart but in the widget's foot, and that checking "Keep in sight under the subtitle" shows it above, and the form can still be saved

#### Scenario: No warning when nothing moves
- **WHEN** the caption is the default line, is hidden or is kept in sight, the widget has no subtitle, or the widget is a heat map, a map, a cross table, a histogram, a stat tile or a chart with a series
- **THEN** the form shows no such warning

#### Scenario: Option on a heat map
- **WHEN** an `analyst` edits the caption of a heat map, a map or a cross table
- **THEN** the form offers no "Keep in sight under the subtitle" option, since their line always stays in sight

#### Scenario: Unit hint
- **WHEN** an `analyst` edits the unit of a widget over `teatro_hoy`
- **THEN** the unit field says that the indicator's unit is "de los hogares"

#### Scenario: Description option unavailable
- **WHEN** the widget's indicator has no description
- **THEN** the option to show the description is unavailable and says that the indicator has none

#### Scenario: Saved option on an indicator that lost its description
- **WHEN** a widget saved with the description option on is edited after its indicator lost its description
- **THEN** the option stays available, still checked, so that it can be turned off

#### Scenario: Error on its field
- **WHEN** the API refuses a footnote of 501 characters
- **THEN** the error shows on the footnote field

### Requirement: Widget context left unset
A widget with no subtitle, footnote, caption setting, unit or description option SHALL show, print, export and be sent the texts it had before these settings existed. It SHALL use the type sizes, foot and reference line texts every widget has (see "Widgets saved before the text hierarchy"). Its stored settings SHALL stay byte for byte as they were saved, so its dashboard makes no version until someone changes it.

#### Scenario: Widgets saved before
- **WHEN** a member opens a dashboard none of whose widgets has any of these settings
- **THEN** every widget shows the same title, caption, values and units as before

#### Scenario: Form saved unchanged before
- **WHEN** an `analyst` opens a column chart in color 2 saved before these settings existed and saves the form without changing anything
- **THEN** its stored settings are unchanged and the dashboard has no new version

### Requirement: Reference lines drawn
Each reference line SHALL be a dashed line across the plot at its value, on an axis that reaches it:

- vertical on a bar chart, and horizontal on a column or line chart;
- in its own dash pattern and in the text's ink.

Its label and value, written as tooltips write values, SHALL be drawn at the line on a box (see "Reference line text placement"). No key SHALL be drawn under the plot. The accessible description SHALL name each line and its value.

#### Scenario: Mean line on horizontal bars
- **WHEN** a member opens the bar chart "2.4. En qué lugares" with a `mean` line
- **THEN** a vertical dashed line crosses the bars at their mean, labelled with "Mean" and its value

#### Scenario: Axis widened
- **WHEN** every column of a chart is below 30% and it has a `value` line at 0.5
- **THEN** its axis reaches 50% and the line is drawn inside the plot

#### Scenario: Lines close together
- **WHEN** a column chart's `mean` and `median` lines are so close that their labels would overlap
- **THEN** only the first line's label is drawn at the lines, and the foot's reference-lines chip gives the second line with its value

#### Scenario: Text too wide for the plot
- **WHEN** a narrow column chart over `teatro_hoy` has a `value` line of 0.5 labelled "Meta del plan de desarrollo municipal", whose text with its value and unit is wider than the plot
- **THEN** the line is drawn with no text at it, and the reference-lines chip opens "Meta del plan de desarrollo municipal: 50% de los hogares"

#### Scenario: Screen reader
- **WHEN** a screen reader reads a column chart with an `overall` line at 45.99%
- **THEN** it hears the line's label and 45.99% after the chart's summary

#### Scenario: Key above the footnote
- **WHEN** the column chart "2.1. Asistió, por comuna", with the footnote "No suman 100 %: respuesta múltiple.", has an `overall` line labelled "Total Duitama" at 46.74%
- **THEN** "Total Duitama: 46.74%" is drawn at the line, no key is drawn under the columns, and the foot shows the "Note" chip

### Requirement: Base n line under a widget
A dashboard SHALL have a setting, off by default, that shows each widget's base n in its foot, and a widget SHALL be able to show or hide it whatever the dashboard says. The n chip SHALL read "n = ", the n as a whole number with thousands separators, and the name of the base column, else of the denominator, as the dashboard names columns. A chip too narrow for it SHALL end with an ellipsis and open whole. A widget with no base n SHALL show no n chip, and its form SHALL say why.

#### Scenario: Line shown
- **WHEN** "Teatro" shows base n and a column chart over `teatro_hoy` has the n 223 and names its denominator "Hogares que asistieron"
- **THEN** the n chip in the chart's foot reads "n = 223 Hogares que asistieron"

#### Scenario: Hidden by a widget
- **WHEN** "Teatro" shows base n and one of its widgets hides it
- **THEN** that widget shows no n chip, and the others do

#### Scenario: Shown by a widget
- **WHEN** "Teatro" does not show base n and one of its widgets shows it
- **THEN** only that widget shows its n chip

#### Scenario: Widget without a base n
- **WHEN** an analyst shows the base n on a widget whose indicator declares no base column and no answer column
- **THEN** the widget shows no n chip, and the form says that its indicator must declare its base column or its answer columns

### Requirement: Base lines under a widget in their place
A widget's base n, small bases and no-data value SHALL be chips of its foot, in that order, before its reference-lines, detail and note chips (see "Widget foot"). They SHALL NOT be lines under its chart. The foot SHALL take its room from the plot as the subtitle does: the tile keeps its size, and a chart that does not fit is cut at its own box, never over the foot. On paper each SHALL print whole as a line (see "Widget foot on paper").

#### Scenario: Lines above the footnote
- **WHEN** the bar chart "2.4. En qué lugares" of "Teatro" shows "n = 223 Hogares que asistieron" and has the footnote "No suman 100 %: respuesta múltiple."
- **THEN** its foot shows the n chip first and the "Note" chip after it, and no line of text under the bars

#### Scenario: Chart cut above the lines
- **WHEN** a column chart 6 columns wide and 4 rows high has a subtitle, a footnote, dimmed marks, the n and a no-data value
- **THEN** its tile is as tall as before, its foot shows the n, small-base, no-data and note chips, and the columns are drawn in the room left, cut at their own box if they do not fit

### Requirement: Small bases drawn dimmed
A mark with a small base SHALL be drawn dimmed and never hidden: lighter, with a dashed outline that reads without color. A heat map's cell and a map's shape SHALL keep their shade and be marked with hatching or a dashed outline, so their scale reads the same. While a mark is dimmed, the widget's foot SHALL show the small-base chip (see "Small-base chip"). A stat tile with a small base SHALL keep its figure at full contrast and say so with its n chip (see "Base n chip below the threshold").

#### Scenario: Columns dimmed
- **WHEN** "2.1. Asistió, por comuna" has `COMUNA 7` (n 16) and `SIN NOMBRE` (n 20) under the threshold 30
- **THEN** those two columns are lighter with a dashed outline and keep their height, and the foot shows the chip "2 with a small base"

#### Scenario: Heat map cell
- **WHEN** a cell of a heat map has a small base
- **THEN** it keeps its shade and is hatched, with a dashed outline

#### Scenario: Nothing dimmed
- **WHEN** no mark of a widget has a small base
- **THEN** its foot has no small-base chip

#### Scenario: Stat tile not dimmed
- **WHEN** the stat tile "3.4. Mediana de lo que pagan hoy al mes" of "Escuelas", whose threshold is 30, has the n 26
- **THEN** its figure `60,000 pesos` is drawn in the heading color at full opacity, and its n chip is amber

### Requirement: No-data line under a widget
A widget that leaves out no-data values SHALL say what the left-out rows held with a no-data chip in its foot. The chip SHALL read "No data" with their share, else their value. Opened, it SHALL give the whole line:

- the left-out values that had rows;
- their value combined in the widget's format, with its own unit when it has one;
- for a measure, their share of the widget's total;
- their n, while the base n shows.

Nothing SHALL be said when no row was left out. Rows with no value, shown as "(none)", SHALL keep their mark.

#### Scenario: Rate of the left-out rows
- **WHEN** a column chart of the frequency answers of `teatro_hoy` leaves out "No sabe", whose rows hold 5 of 223 households
- **THEN** its no-data chip reads "No data 2.24%" and opens "No sabe: 2.24%" in the indicator's format

#### Scenario: Share of a count
- **WHEN** a 100% column chart of `teatro_conteo` leaves out "4. No sabe", which holds 11 of the 476 households
- **THEN** its no-data chip reads "No data 2.3%" and opens "4. No sabe: 11 hogares (2.3%)"

#### Scenario: Nothing left out
- **WHEN** a widget's own filters already keep the no-data values out
- **THEN** its foot has no no-data chip

#### Scenario: Rows with no value kept
- **WHEN** a widget leaves out no-data values and some of its rows have no `comuna`
- **THEN** their "(none)" column is drawn as before

### Requirement: Stat tile secondary lines
A stat tile SHALL be able to show up to two secondary lines, each chosen by an `analyst` or `admin`:

- its base n, drawn as the n chip of its foot (see "Base n of a widget" and "Stat tile statistic n");
- "Without filters: {value}", under what it plots, only while a dashboard filter narrows the tile.

A line with nothing to show SHALL be left out. The n SHALL be the widget's one n chip, whether the tile or the dashboard asks for it.

#### Scenario: Base under the value
- **WHEN** the stat tile `46.85% de los hogares` over `teatro_hoy`, whose `categoria` is declared as the answers of one question and whose denominator is named "Hogares", shows the n line
- **THEN** the n chip of its foot reads `n = 476 Hogares`

#### Scenario: Base n line of the dashboard
- **WHEN** "Teatro" shows each widget's base n, and that tile does not choose the n line
- **THEN** its foot has one n chip, `n = 476 Hogares`

#### Scenario: Nothing declared
- **WHEN** a stat tile with no statistic plots the ratio of `teatro_hoy`, whose indicator declares no base column and no answer column, and shows the n line
- **THEN** it shows no n chip

#### Scenario: Value without the dashboard filters
- **WHEN** that tile shows the line without filters and a member sets the dashboard filter "Comuna" to `COMUNA 1`
- **THEN** it shows the value of the rows of `COMUNA 1`, and under it `Without filters: 46.85% de los hogares`

#### Scenario: No dashboard filter in force
- **WHEN** a stat tile shows the line without filters and no dashboard filter is set
- **THEN** it shows no "Without filters" line

#### Scenario: Filter the tile does not take
- **WHEN** a stat tile over `escuelas_pago_mediana`, whose table has no `comuna` column, shows the line without filters and a member sets "Comuna" to `COMUNA 1`
- **THEN** it shows no "Without filters" line, and says that it is not filtered by "Comuna"

#### Scenario: Median without the dashboard filters
- **WHEN** the median tile of "Pagó por la última boleta (E7)" shows the line without filters and a member sets "Comuna" to `COMUNA 1`
- **THEN** it shows `35,000 pesos`, and under it `Without filters: 40,000 pesos`

#### Scenario: Lines of a tile that hides its caption
- **WHEN** the stat tile `46.85% de los hogares` over `teatro_hoy` hides its caption and shows the n line
- **THEN** it shows no label under its value, and its foot's n chip reads `n = 476 Hogares`

#### Scenario: Tile short of room
- **WHEN** a stat tile 3 columns wide and 2 rows high has a footnote and shows both secondary lines
- **THEN** its value shows whole, its label and the line without filters are cut as far as the room needs, and its foot holds the n and note chips

#### Scenario: Tile with texts and the n line
- **WHEN** a stat tile 3 columns wide and 2 rows high has a one-line subtitle and a one-line footnote and shows the n line
- **THEN** its tile is as tall as before, its value is drawn whole at no less than 18 px with its label under it, and its foot holds the n, detail and note chips

#### Scenario: Measure with no base
- **WHEN** a stat tile with no statistic plots the measure `value` of an indicator that declares no base column, and shows the n line
- **THEN** it shows no n chip

## ADDED Requirements

### Requirement: Widget title
A widget's title SHALL be drawn at 15 px and weight 600, in the heading color, on up to two lines. On screen a longer title SHALL end with an ellipsis, show whole on pointing and be read whole by screen readers. On paper it SHALL print whole, as far as the tile holds it.

#### Scenario: Title of two lines
- **WHEN** a member opens the box plot "2.6. Lo que pagó por la última boleta: mediana y dispersión" of "Teatro", 3 columns wide, on a 1,280 px screen
- **THEN** its title is drawn at 15 px and weight 600 over two lines

#### Scenario: Title longer than two lines
- **WHEN** a widget 2 columns wide has a title of 120 characters
- **THEN** its title shows two lines ending with an ellipsis, pointing at it shows the 120 characters, and a screen reader reads them all

### Requirement: Widget foot
A widget SHALL have a foot under its chart, its table or its message that no row matches, set off by a line. Its chips SHALL come in this order: the caption's own text out of sight under the subtitle, the base n, the small bases, the no-data value, the reference lines whose text is not drawn at the line, a compact stat tile's subtitle, and the footnote. A chip with nothing to say SHALL be left out, and a widget with none SHALL have no foot. Chips that do not fit one row SHALL wrap.

#### Scenario: Foot of a column chart
- **WHEN** a member opens "2.1. Asistió, por comuna" of "Teatro", which shows base n, has two marks with a small base and has a footnote
- **THEN** its foot shows "n = 475 Hogares", "2 with a small base" and "Note", in that order

#### Scenario: Caption chip first
- **WHEN** a column chart has a subtitle, the caption text "% de los hogares que asistieron" not kept in sight, the base n and a footnote
- **THEN** its foot shows "% de los hogares que asistieron", the n chip and "Note", in that order

#### Scenario: Nothing to say
- **WHEN** a widget shows no base n, has no small base, leaves out no value, has no caption text out of sight and has no footnote
- **THEN** it has no foot

#### Scenario: Chips on a narrow tile
- **WHEN** the foot's chips are wider together than a tile 3 columns wide
- **THEN** they wrap onto a second row, and the chart takes the room left

#### Scenario: Widget that failed
- **WHEN** a widget's query is refused, or its indicator has not published
- **THEN** it shows its message and no foot

### Requirement: Widget foot chips
Each chip SHALL be a button named by its short text, with an icon. It SHALL open a panel with its whole text when it is clicked or tapped, when it gets keyboard focus, or while the pointer is on it. Escape, a click elsewhere and opening another chip SHALL close it. Only one chip or indicator description SHALL be open at a time. A chip SHALL say whether it is open, and screen readers SHALL get its whole text whether or not it is open.

#### Scenario: Opened by pointing
- **WHEN** a member points at the "2 with a small base" chip of "2.1. Asistió, por comuna"
- **THEN** a panel next to it gives its whole text, and it closes when the pointer leaves

#### Scenario: Opened with the keyboard
- **WHEN** a keyboard user tabs to the "Note" chip, reads its panel and presses Escape
- **THEN** the panel opens on focus, Escape closes it, and focus stays on the chip

#### Scenario: Opened by a click
- **WHEN** a member clicks the n chip and moves the pointer away
- **THEN** the panel stays open until the member clicks the chip again, clicks elsewhere or presses Escape

#### Scenario: One at a time
- **WHEN** a member opens the "Note" chip of one widget while another widget's description is open
- **THEN** the description closes and only the note's panel is open

#### Scenario: Screen reader
- **WHEN** a screen reader reaches the closed "Note" chip
- **THEN** it reads the chip's name, that it is collapsed, and the whole footnote as its description

### Requirement: Small-base chip
While marks of a widget are dimmed, its foot SHALL have an amber chip with a warning icon. It SHALL read how many marks have a small base, as "2 with a small base". Opened, it SHALL give:

- the threshold;
- up to five of those marks with their n, in the chart's order, then how many more;
- that those marks are drawn dimmed.

A widget shown as a table SHALL mark its values and explain the mark under the table instead.

#### Scenario: Two comunas
- **WHEN** "2.1. Asistió, por comuna" has `COMUNA 7` (n 16) and `SIN NOMBRE` (n 20) under the threshold 30
- **THEN** its chip reads "2 with a small base" and opens "Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed."

#### Scenario: Cells of a series
- **WHEN** a stacked column chart by `comuna` with the series `respuesta` has the cell `COMUNA 7` and "1. Sí" under the threshold
- **THEN** the chip's text names it "COMUNA 7 · 1. Sí" with its n

#### Scenario: More than five
- **WHEN** eight marks of a heat map have a small base
- **THEN** the chip reads "8 with a small base" and its text names the first five and says "and 3 more"

#### Scenario: Shown as a table
- **WHEN** a member shows that chart as a table
- **THEN** its foot has no small-base chip, and the table marks the values and explains the mark under it

#### Scenario: Not by color alone
- **WHEN** a member reads the foot in grayscale
- **THEN** the small-base chip is told apart by its warning icon and its words

### Requirement: Caption under a subtitle
On a widget with a subtitle, the line that names what it plots SHALL be out of sight above a bar, column or line chart with no series and above a pie, unless the widget keeps it in sight (see "Caption kept in sight"). An own text SHALL move to the foot (see "Caption chip"). A map, heat map or cross table SHALL keep its line in sight: it alone says what their shades or cells measure. Tooltips, "Show as table", the accessible name, both workbooks and the email SHALL still name what is plotted.

#### Scenario: Own text under a subtitle
- **WHEN** a member opens the column chart "2.1. Asistió a espectáculos, por estrato" of "Teatro", whose subtitle is "E1, % de los hogares de cada estrato que asistieron en el último año" and whose caption text is "% de los hogares que asistieron"
- **THEN** no line shows above its columns, its foot opens with the chip "% de los hogares que asistieron", and each column's tooltip still names it

#### Scenario: Pie under a subtitle
- **WHEN** a member opens a pie chart with a subtitle and an own caption text
- **THEN** no line shows above the pie, and its foot opens with the caption chip

#### Scenario: Heat map keeps its line
- **WHEN** a member opens a heat map by `estrato` and `comuna` that has a subtitle and an own caption text
- **THEN** its caption line, naming what its cells measure by estrato and comuna, shows above its grid as before, and its foot has no caption chip

#### Scenario: Without a subtitle
- **WHEN** a column chart with no subtitle and no caption setting is opened
- **THEN** its caption line shows above the columns as before

#### Scenario: Stat tile keeps its label
- **WHEN** a stat tile with a subtitle and the caption text "de los hogares asistió en el último año" is opened
- **THEN** that label shows under its figure

#### Scenario: Histogram keeps its line
- **WHEN** the histogram "2.6. Cuánto pagó por la última boleta", which has a subtitle, is opened
- **THEN** its line naming what its ranges are of shows above its bars

#### Scenario: Series legend kept
- **WHEN** a stacked column chart with the series `barrera_tipo` and a subtitle is opened
- **THEN** its legend of series is shown

#### Scenario: Table header
- **WHEN** a member shows "2.1. Asistió a espectáculos, por estrato" as a table
- **THEN** its value column is headed "% de los hogares que asistieron"

### Requirement: Caption chip
When a subtitle takes out of sight a caption that has its own text, the widget's foot SHALL open with a neutral chip with a ruler icon, named by that text and cut with an ellipsis when longer than the chip. Opened, it SHALL give the whole caption line as it would read above the chart. A default line, a hidden caption and a caption kept in sight SHALL have no chip. On paper and in images the chip SHALL be a line of the foot.

#### Scenario: Chip opened
- **WHEN** a member points at the caption chip of "2.1. Asistió a espectáculos, por estrato" in the English interface
- **THEN** its panel reads "% de los hogares que asistieron by Estrato"

#### Scenario: Default line under a subtitle
- **WHEN** a column chart with a subtitle and no own caption text is opened
- **THEN** no line shows above its columns and its foot has no caption chip, since the subtitle already says what the default line said

#### Scenario: Hidden caption
- **WHEN** a column chart with a subtitle hides its caption
- **THEN** its foot has no caption chip

#### Scenario: Screen reader on the caption chip
- **WHEN** a screen reader reaches the caption chip
- **THEN** it reads the own text as the chip's name and the whole caption line as its description, and does not read the line a second time from the chart

### Requirement: Caption kept in sight
A bar, column, line or pie widget SHALL be able to keep its caption line in sight under its subtitle, with the default line or its own text, and then SHALL have no caption chip. The setting SHALL be saved as `hidden: false` only when chosen, and SHALL change nothing on a widget without a subtitle or on another chart type. A caption that has its own text and is hidden SHALL still be refused.

#### Scenario: Default line kept
- **WHEN** a column chart with a subtitle and no own caption text is saved with its caption kept in sight
- **THEN** it shows its default caption line above the columns, and its stored caption is `{ "hidden": false }`

#### Scenario: Own text kept
- **WHEN** an `analyst` keeps in sight the caption text "% de los hogares que asistieron" of "2.1. Asistió a espectáculos, por estrato"
- **THEN** the line above its columns reads "% de los hogares que asistieron by Estrato" in the English interface, and its stored caption is `{ "text": "% de los hogares que asistieron", "hidden": false }`

#### Scenario: Not chosen
- **WHEN** a widget is saved without the setting
- **THEN** its stored caption has no `hidden` member, as before

#### Scenario: Without a subtitle
- **WHEN** a widget with no subtitle keeps its caption in sight
- **THEN** it looks as it does without the setting

### Requirement: Unit on the value axis
A bar, column or line chart with no series whose caption line is out of sight SHALL write its unit as the title of its value axis, as values write it, with "%" before it for a `percent` indicator. The caption's own text SHALL NOT be the title: it is in the caption chip. The title SHALL be cut with an ellipsis to the plot and be whole in its tooltip. With no unit beyond "%", the axis SHALL have no title. Tooltips SHALL keep writing each value with its unit.

#### Scenario: Own text in the foot, unit on the axis
- **WHEN** "2.1. Asistió a espectáculos, por estrato", over a `percent` indicator with the unit "de los hogares", has its caption out of sight under its subtitle
- **THEN** its value axis is titled "% de los hogares", and "% de los hogares que asistieron" is in its caption chip, not on the axis

#### Scenario: Unit of a percent indicator
- **WHEN** a column chart over `teatro_hoy`, format `percent` and unit "de los hogares", with no own caption text, has its caption out of sight
- **THEN** its value axis is titled "% de los hogares"

#### Scenario: Caption in sight
- **WHEN** a column chart shows its caption line
- **THEN** its value axis has no title, as before

#### Scenario: Percent with no unit
- **WHEN** a column chart of a `percent` indicator with no unit has its caption out of sight
- **THEN** its value axis has no title, since its ticks already read "%"

### Requirement: Reference line text placement
A reference line's text SHALL be drawn on a box in the tile's color with a hairline border.

- On a column or line chart it SHALL sit just above the line, at the first free stretch from the plot's right end to its left: where its box covers no mark and no other text. With no free stretch it SHALL sit at the right end.
- On a bar chart it SHALL sit above the plot beside the line.

A text wider than the plot, or one over another line's text, SHALL go to the reference-lines chip.

#### Scenario: Free stretch on the left
- **WHEN** "3.1. Clases hoy, por estrato" of "Escuelas" has the `overall` line "Total Duitama: 16.67%" and only its last column, Estrato 4 at 28.57%, rises above the line at the right end
- **THEN** the text is drawn on its box above the line where no column reaches it, not over Estrato 4

#### Scenario: No free stretch
- **WHEN** every column of a chart rises above its reference line along the plot
- **THEN** the line's text is drawn at the right end, over the columns, on its box, and stays readable

#### Scenario: Bar chart
- **WHEN** "2.1. Asistió a espectáculos, por comuna" has the `overall` line "Total Duitama: 46.74%"
- **THEN** the text is drawn on its box above the bars, beside the vertical line

#### Scenario: Text left out
- **WHEN** a line's text is wider than the plot
- **THEN** the line has no text at it, and the foot's reference-lines chip gives its text

### Requirement: Data labels over reference lines
Data labels SHALL be drawn over the reference lines, with a halo in the tile's color, so a line that crosses a label never covers its figures. A data label that would cover a reference line's text SHALL NOT be drawn.

#### Scenario: Line crossing a label
- **WHEN** the `overall` line of a column chart with data labels crosses the label "45.76%" of `COMUNA 3`
- **THEN** the label is drawn whole over the line, on its halo

#### Scenario: Label under a line's text
- **WHEN** a column's data label would fall on the box of a reference line's text
- **THEN** that data label is not drawn, and the column's tooltip still gives its value

### Requirement: Base n chip below the threshold
A widget's n chip SHALL be gray while its n is at or above the threshold in force. Below the threshold it SHALL be amber, with a warning icon, and opened it SHALL say that the figures rest on that n, below the threshold, and are to be read with caution. A stat tile SHALL show the amber chip, as "n = 26", whenever it has a small base, whether or not it or its dashboard shows the n. Nothing SHALL be added beside the figure.

#### Scenario: Base enough
- **WHEN** the stat tile "2.1. Asistió a espectáculos en el último año", with the n 476 and the threshold 30, shows its n
- **THEN** its foot has a gray chip `n = 476 Hogares`

#### Scenario: Small base of a stat tile
- **WHEN** the stat tile "3.4. Mediana de lo que pagan hoy al mes", whose dashboard has the threshold 30, has the n 26
- **THEN** its foot has an amber chip `n = 26` with a warning icon that opens a text saying that the figure rests on n = 26, below 30, and the figure keeps full contrast

#### Scenario: Small base with the n not shown
- **WHEN** that tile shows no n line and its dashboard does not show base n
- **THEN** its foot still has the amber chip `n = 26`

#### Scenario: Threshold off
- **WHEN** a stat tile with the n 26 sets its threshold to 0
- **THEN** its n chip, when it shows its n, is gray

### Requirement: Subtitle of a compact stat tile
A stat tile 2 rows high or less SHALL show its subtitle as the "Detail" chip of its foot rather than under its title, at every screen width. A taller stat tile SHALL show its subtitle under its title, whatever its width, on up to two lines. On paper the subtitle SHALL print as a line of the foot, under the figure, never under the title: over the figure it would leave it no room in a tile 2 rows high. Screen readers SHALL read the subtitle as the tile's description wherever it is shown.

#### Scenario: Tile 3 columns wide
- **WHEN** a member opens the stat tile "2.1. Asistió a espectáculos en el último año", 3 columns wide and 3 rows high, whose subtitle is "¿Cuánta gente va hoy? E1, % de los hogares con encuesta completa"
- **THEN** the subtitle shows under its title on up to two lines, its figure and its label show whole, and its foot has no "Detail" chip

#### Scenario: Tile 4 columns wide and 3 rows high
- **WHEN** a stat tile 4 columns wide and 3 rows high has a subtitle
- **THEN** the subtitle shows under its title, and its foot has no "Detail" chip

#### Scenario: Tile 4 columns wide and 2 rows high
- **WHEN** the stat tile "2.9. Veces al año que iría" of "Teatro", 4 columns wide and 2 rows high, has a subtitle
- **THEN** its foot has a "Detail" chip that opens it

#### Scenario: Phone
- **WHEN** a member opens "2.9. Veces al año que iría" on a 390 px phone screen, where it takes the whole width and keeps its 2 rows
- **THEN** its foot still has the "Detail" chip

### Requirement: Widgets saved before the text hierarchy
Every widget, saved before or after this change, SHALL take the title, subtitle, foot and reference line texts of these requirements with no setting. Its stored settings, its dashboard's versions and its query SHALL stay as they were saved. A widget's caption line SHALL leave sight only on a bar, column or line chart with no series or a pie, when the widget has a subtitle and does not keep it in sight.

#### Scenario: Teatro opened
- **WHEN** a member opens "Teatro", whose 42 widgets were saved before this change
- **THEN** each widget shows the new title, subtitle and foot, the same values, and the same queries are sent, and "Teatro" has no new version

#### Scenario: Form saved unchanged
- **WHEN** an `analyst` opens a widget of "Teatro" saved before this change and saves the form without changing anything
- **THEN** its stored settings are unchanged and the dashboard has no new version
