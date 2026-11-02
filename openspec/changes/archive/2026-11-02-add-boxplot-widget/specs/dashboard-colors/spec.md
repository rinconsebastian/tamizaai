## MODIFIED Requirements

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
