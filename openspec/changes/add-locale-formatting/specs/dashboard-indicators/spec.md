# Spec Delta

## MODIFIED Requirements

### Requirement: Indicator formatting
A widget SHALL show its plotted values with the format and unit of its indicator, the widget's own unit taking the place of the indicator's when the widget has one (see "Widget unit"): `count` as a whole number with thousands separators, `decimal` with two decimals, `percent` as the value times 100 followed by `%`, with at most two decimals, and `currency` with two decimals and the unit as the currency; any other unit SHALL be shown after the value, and a unit of `%` on a `percent` indicator SHALL NOT be repeated after the number. When the widget or its indicator sets a number of decimals, the value SHALL have exactly that many instead, the widget's before the indicator's (see "Decimals of a widget"), except on axis ticks, which keep their short form. The same SHALL apply to a stat tile, axes, legends, tooltips and "Show as table"; a map's tooltip columns use their own format. A box plot over a `count` indicator SHALL show its figures as `decimal` does, with two decimals and the unit, because its quartiles, medians and means can fall between whole numbers, unless the widget or its indicator sets its decimals; a box plot's numbers of values and of outliers SHALL be whole numbers whatever the indicator's format. Separators, and the space before `%` of a Spanish format, SHALL follow the project's regional format when the project has one (see "Regional format of a project's dashboards" and "Percentages in a Spanish regional format").

#### Scenario: Rate shown as a percentage
- **WHEN** the indicator `piped_water_rate` has the format `percent` and a stat tile shows the value 0.42
- **THEN** the tile shows `42%`

#### Scenario: Count with a unit
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`, and a column chart shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households`

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over the indicator `survey_progress`, whose format is `percent`, shows the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** the tooltip reads the plotted value as a percentage and the target as `20`, not `2,000%`

#### Scenario: Median of a count
- **WHEN** a box plot over the indicator `household_size`, whose format is `count` and unit `people`, has a box of 1,200 values whose median is 4.5
- **THEN** the box's median reads `4.50 people` and its number of values reads `1,200`

#### Scenario: Widget's own unit
- **WHEN** a column chart over `households_visited`, whose format is `count` and unit `households`, has the unit `households with children` of its own and shows 12,450 for `Cali`
- **THEN** the column's label reads `12,450 households with children`

#### Scenario: Median of a count with decimals set
- **WHEN** the box plot "2.6. Lo que pagó por la última boleta: mediana y dispersión (E7, pesos)" over `teatro_precio_hogar`, whose format is `count` and unit `pesos`, sets 0 decimals and has a median of 40000 and a mean of 60513.51
- **THEN** the median reads `40,000 pesos` and the mean `60,514 pesos`, as the stat tile over the same indicator does

#### Scenario: Widget's own unit with decimals
- **WHEN** the column chart "2.2. Con qué frecuencia asiste (E2, % de quienes asistieron)" over `teatro_hoy`, whose format is `percent` and unit "de los hogares", has the unit "de quienes asistieron" of its own and sets 1 decimal, and one column is 0.1121
- **THEN** that column's label reads `11.2% de quienes asistieron`, and the indicator's other widgets keep "de los hogares"

#### Scenario: Unit of % not repeated
- **WHEN** a stat tile over `tasa_cobertura`, whose format is `percent` and unit `%`, sets 1 decimal and has the value 0.4685, in a project whose regional format is `es-CO`
- **THEN** it reads `46,9 %`, not `46,9 % %`

## ADDED Requirements

### Requirement: Regional format of a project's dashboards
When the project has a regional format, the numbers its dashboards' widgets draw and list SHALL be written in it for every member: values, totals, axis ticks, shares, a histogram's ranges, a box plot's figures and counts, and tooltip column numbers, also in the widget form's and the script editor's previews. Dates, times, the interface's own words and the counts in its messages SHALL keep each member's format. Without a regional format, the numbers SHALL follow each member's browser, as before.

#### Scenario: Colombian format in an English browser
- **WHEN** the Duitama project's regional format is `es-CO` and a member whose browser prefers `en-US` opens the stat tile over `teatro_precio_hogar`, whose format is `count` and unit `pesos`, with the value 60513.51
- **THEN** it reads `60.514 pesos`

#### Scenario: Colombian percentage
- **WHEN** the project's regional format is `es-CO` and a stat tile over `teatro_hoy`, whose format is `percent` and unit `de los hogares`, has the value 0.4685
- **THEN** it reads `46,85 % de los hogares`, with a no-break space before `%`, for every member

#### Scenario: Spanish of Spain
- **WHEN** the project's regional format is `es-ES` and a column chart over a `count` indicator with the unit `hogares` has the values 1950 and 12345
- **THEN** its labels read `1950 hogares` and `12.345 hogares`

#### Scenario: Dates keep the member's format
- **WHEN** the project's regional format is `es-CO` and a member whose browser prefers `en-US` points at a block whose tooltip column `last_visit` holds `2026-09-01 21:30:05` in the project's time zone, `America/Bogota`
- **THEN** the tooltip shows the time as `Sep 1, 2026, 9:30 PM`

#### Scenario: Project without a regional format
- **WHEN** the project has no regional format and a member whose browser prefers `es-MX` opens the stat tile over `teatro_hoy` with the value 0.4685
- **THEN** it reads `46.85% de los hogares`, as before this change

### Requirement: Percentages in a Spanish regional format
When the project's regional format is Spanish (`es-CO`, `es-MX`, `es-ES` or `es-419`), every percentage its dashboards write SHALL have one no-break space before `%`, as in `46,9 %`: a percent indicator's values and axis ticks, shares, and tooltip columns written as percentages, on screen, in images and on paper. In the other formats percentages SHALL have no space, and without a project format they SHALL read as each member's browser writes them, as before.

#### Scenario: Percentage with a space for a Spanish project format
- **WHEN** the Duitama project's regional format is `es-CO`, and a dashboard has a stat tile over `teatro_hoy` that sets 1 decimal with the value 0.4685 and a stat tile over an indicator whose format is `currency`, unit `$` and decimals 0 with the value 60513.51
- **THEN** the tiles read `46,9 % de los hogares` and `$60.514`

#### Scenario: Share in a Mexican project format
- **WHEN** the project's regional format is `es-MX` and a pie chart has a slice that is one third of the total
- **THEN** the slice's share reads `33.3 %`

#### Scenario: One space in Spanish of Spain
- **WHEN** the project's regional format is `es-ES` and a stat tile over `teatro_hoy` has the value 0.4685
- **THEN** it reads `46,85 % de los hogares`, with a single no-break space before `%`

#### Scenario: Portuguese project format
- **WHEN** the project's regional format is `pt-BR` and a stat tile over `teatro_hoy` has the value 0.4685
- **THEN** it reads `46,85% de los hogares`, with no space before `%`

#### Scenario: Colombian browser without a project format
- **WHEN** the project has no regional format and a member whose browser prefers `es-CO` opens the stat tile over `teatro_hoy` with the value 0.4685
- **THEN** it reads `46,85% de los hogares`, as before this change

### Requirement: Decimals of a widget
A widget of any chart type SHALL be able to set how many decimals its values and a histogram's range ends have, from 0 to 4, saved with the widget; any other value SHALL be rejected, and changing it SHALL NOT query the indicator again. The widget's decimals SHALL go before its indicator's (see `pipeline` "Indicator decimals"), and with neither the format's own digits SHALL apply. Axis ticks, shares, numbers of rows, values and outliers, and tooltip columns SHALL keep their digits.

#### Scenario: One decimal on a rate
- **WHEN** a stat tile over `teatro_hoy`, whose format is `percent`, sets 1 decimal and has the value 0.4685, in a project whose regional format is `es-CO`
- **THEN** it reads `46,9 % de los hogares`

#### Scenario: Trailing zero kept
- **WHEN** the column chart "2.2. Con qué frecuencia asiste (E2, % de quienes asistieron)" over `teatro_hoy` sets 2 decimals and one category has the value 0.157
- **THEN** that column's label reads `15.70% de los hogares`, not `15.7% de los hogares`

#### Scenario: Widget before indicator
- **WHEN** `teatro_precio_hogar` sets 0 decimals and a box plot over it sets 2
- **THEN** the box plot's mean reads `60,513.51 pesos`

#### Scenario: Statistic of a count with a decimal
- **WHEN** a stat tile over `teatro_precio_hogar`, whose format is `count`, with the statistic median, sets 1 decimal and the median of its rows is 4.5
- **THEN** it reads `4.5 pesos`, where a tile that sets none reads `5 pesos`

#### Scenario: Shares keep their digits
- **WHEN** a pie chart that sets 0 decimals has a slice that is one third of the total
- **THEN** the slice's share reads `33.3%`

#### Scenario: Histogram ranges with decimals
- **WHEN** a histogram over a `count` indicator sets 0 decimals and one of its ranges goes from 1250.5 to 2501
- **THEN** that range reads `1,251–2,501`, and its number of rows keeps reading as a whole number

#### Scenario: Decimals out of range
- **WHEN** an analyst saves a widget with 5 decimals
- **THEN** the API rejects it with an error on `display.decimals`, and nothing is saved

#### Scenario: Widget saved before decimals
- **WHEN** a widget saved before this change is read and saved again unchanged
- **THEN** its stored config is the same text as before, and it shows the same numbers

### Requirement: Abbreviated numbers
A stat tile, and a bar, column, line or pie widget, SHALL be able to abbreviate its numbers, saved with the widget: the tile's value, and the chart's data labels (see "Data labels on bars, columns, lines and pies"), are then written short. Any other chart type SHALL refuse the setting. Changing it SHALL NOT query the indicator again, and a widget that does not abbreviate SHALL write its numbers in full, as before.

#### Scenario: Tile abbreviated
- **WHEN** a stat tile over `comercio_gastos_media`, whose format is `count` and unit `pesos`, abbreviates its value of 1016279, in a project whose regional format is `es-CO`
- **THEN** it reads `1 M pesos`

#### Scenario: Data labels abbreviated
- **WHEN** a column chart over a `count` indicator with the unit `pesos` shows data labels, abbreviates its numbers and has a column of 1250000, in a project whose regional format is `es-CO`
- **THEN** that column's label reads `1,3 M pesos`

#### Scenario: Abbreviation refused on a heat map
- **WHEN** an analyst saves a heat map that abbreviates its numbers
- **THEN** the API rejects it with an error on `display.compact`, and nothing is saved

#### Scenario: Tile that does not abbreviate
- **WHEN** a stat tile over `comercio_gastos_media` saved without the setting has the value 1016279, in a project with no regional format and an American English browser
- **THEN** it reads `1,016,279 pesos`, as before this change

### Requirement: Abbreviated number text
An abbreviated number of 1,000 or more in size SHALL be written with at most one decimal and the short word or letter of its regional format for thousands, millions or more, with the unit or the currency placed as in its full value. A number under 1,000 in size, and any percentage, SHALL be written in full.

#### Scenario: Currency abbreviated
- **WHEN** a stat tile over an indicator whose format is `currency`, unit `COP` and decimals 0 abbreviates 1234567, in a project whose regional format is `es-CO`
- **THEN** it reads `$1,2 M`

#### Scenario: Thousands in Spanish of Spain
- **WHEN** a stat tile over a `count` indicator with the unit `pesos` abbreviates 60514, in a project whose regional format is `es-ES`
- **THEN** it reads `60,5 mil pesos`

#### Scenario: Small value not abbreviated
- **WHEN** an abbreviated stat tile over `teatro_promedios`, whose format is `decimal` with no unit, has the value 4.41
- **THEN** it reads `4.41`

#### Scenario: Percentage not abbreviated
- **WHEN** an abbreviated stat tile over `teatro_hoy`, whose format is `percent` and unit `de los hogares`, has the value 0.4685
- **THEN** it reads `46.85% de los hogares`

### Requirement: Full values beside abbreviated numbers
Wherever a widget abbreviates a number, its full value SHALL stay available: a stat tile SHALL give it in its title and its accessible name, and the widget's tooltips and "Show as table" SHALL write it in full. A widget's image and a printed dashboard SHALL show the abbreviated number as the screen does.

#### Scenario: Full value of a tile
- **WHEN** a member points at the abbreviated tile over `comercio_gastos_media` that reads `1 M pesos`, in a project whose regional format is `es-CO`
- **THEN** its title gives `1.016.279 pesos`, and a screen reader reads `1.016.279 pesos`

#### Scenario: Tooltip of an abbreviated label
- **WHEN** a member points at the column whose abbreviated label reads `1,3 M pesos`
- **THEN** its tooltip reads `1.250.000 pesos`

#### Scenario: Image of an abbreviated tile
- **WHEN** a member exports the PNG image of the abbreviated tile over `comercio_gastos_media`
- **THEN** the image shows the value as the screen does, `1 M pesos`

### Requirement: Number format in the widget form
The widget form SHALL offer the decimals for every chart type, as the indicator's default, naming its digits, or 0 to 4, and the abbreviation for a stat tile and for the charts that take it. The preview SHALL redraw without a new query when either changes. Changing the chart type to one that does not take the abbreviation SHALL drop it from the saved widget, and a form saved unchanged SHALL save the config it had.

#### Scenario: Indicator's default named
- **WHEN** an analyst edits a stat tile over `teatro_precio_hogar`, whose format is `count`, and the indicator and the tile set no decimals
- **THEN** the decimals read "Indicator's default (0)"

#### Scenario: Decimals previewed
- **WHEN** an analyst picks 1 decimal for a column chart in the widget form
- **THEN** the preview redraws its labels with 1 decimal and no query is sent

#### Scenario: Chart type change drops the abbreviation
- **WHEN** an analyst turns an abbreviated stat tile into a heat map and saves it
- **THEN** the heat map is saved without the abbreviation, and its decimals stay

#### Scenario: Form saved unchanged
- **WHEN** an analyst opens a widget that sets no decimals and no abbreviation and saves the form without changing anything
- **THEN** the saved config is the one it had, with no decimals and no abbreviation
