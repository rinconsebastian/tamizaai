# Spec Delta

## MODIFIED Requirements

### Requirement: Moving a widget to another dashboard
When editing a widget, an `analyst` or `admin` SHALL be able to choose another dashboard of the project for it. The widget SHALL keep every setting it is saved with (its title, chart type, query, display settings, the dashboard filters it ignores and its size), and SHALL be placed below the widgets of its new dashboard. A move that changes nothing else SHALL be accepted even when the widget's query is stale. A move SHALL be refused with an error on the dashboard when that dashboard is full or is not one of the project's.

#### Scenario: Widget moved to another dashboard
- **WHEN** an `analyst` edits the widget "Coverage by municipality" on the dashboard "Health" and chooses the dashboard "Field"
- **THEN** the widget is shown on "Field", below its widgets, and no longer on "Health"

#### Scenario: Stale widget moved
- **WHEN** a script change removed the column a widget groups by, and an `analyst` moves the widget to "Field" without changing anything else
- **THEN** the widget moves and still shows a message naming the missing column

#### Scenario: Full dashboard
- **WHEN** the dashboard "Field" holds the maximum number of widgets and an `analyst` moves a widget to it
- **THEN** the API responds 400 with an error on the dashboard naming the limit, and the widget stays where it was

#### Scenario: Dashboard of another project
- **WHEN** an `analyst` asks the API to move a widget to a dashboard of another project
- **THEN** the API responds 400 with an error on the dashboard, and the widget stays where it was

#### Scenario: Target removed meanwhile
- **WHEN** an `analyst` moves a widget to "Field" just after another analyst removed "Field"
- **THEN** the form shows an error on the dashboard, the widget stays where it was, and "Field" is no longer offered in the form or the tab row

#### Scenario: New widget
- **WHEN** an `analyst` adds a widget
- **THEN** the widget form offers no dashboard choice, and the widget is added to the open dashboard

#### Scenario: Ignored filter kept by a move
- **WHEN** a widget on "Health" ignores the dashboard filter on `municipality`, and an `analyst` moves it to "Field", which also has a filter on `municipality`
- **THEN** on "Field" the widget ignores the filter on `municipality` too

#### Scenario: Horizontal box plot moved
- **WHEN** an `analyst` moves a horizontal box plot from "Health" to "Field"
- **THEN** it is horizontal on "Field"

#### Scenario: Column labels kept by a move
- **WHEN** an `analyst` moves a column chart of "Teatro" whose own label for `denominator` is "Hogares que asistieron" to "Cruces"
- **THEN** on "Cruces" the chart still names its denominator "Hogares que asistieron"

## ADDED Requirements

### Requirement: Column labels of a widget
A widget of any kind SHALL be able to give its own label, of 1 to 80 characters, to up to 10 columns, as a setting of how it is drawn. The name shown for a column SHALL be the widget's label, else its indicator's label (see `pipeline` "Column labels of an indicator"), else the column's own name. A widget's label SHALL be trimmed, an empty one SHALL mean none, and it SHALL NOT be checked against the published table. Changing the chart type SHALL keep the labels.

#### Scenario: Widget label over the indicator's
- **WHEN** `teatro_hoy` labels `denominator` "Hogares", and the column chart "2.2. Con qué frecuencia asiste" over it labels `denominator` "Hogares que asistieron"
- **THEN** that chart names its denominator "Hogares que asistieron", and the other widgets over `teatro_hoy` name it "Hogares"

#### Scenario: Indicator label used
- **WHEN** a widget over `teatro_hoy` gives `comuna` no label of its own, and `teatro_hoy` labels it "Comuna"
- **THEN** the widget names the column "Comuna"

#### Scenario: Column's own name
- **WHEN** neither a widget nor its indicator labels `hogar_menores`
- **THEN** the widget names the column `hogar_menores`

#### Scenario: Labels on a stat tile
- **WHEN** an `analyst` saves a stat tile with its own label for `numerator`
- **THEN** the stat tile is saved with that label

#### Scenario: Labelled column removed from the table
- **WHEN** a widget labels `estrato`, which it neither groups nor splits by, and a script change removes `estrato` from the indicator's table
- **THEN** the widget is drawn as before, with no message about a missing column

#### Scenario: Chart type changed
- **WHEN** an `analyst` changes a column chart with its own labels into a bar chart
- **THEN** the bar chart keeps the labels

### Requirement: Column labels of a widget that are refused
Saving a widget SHALL be refused, with an error on its labels and nothing saved, when it has more than 10 labels, when a label is longer than 80 characters or holds a control character, or when a label is given for a name that an indicator table's column cannot have (lowercase letters, digits and underscores, not starting with a digit, up to 63 characters).

#### Scenario: Too many labels
- **WHEN** an `analyst` saves a widget with labels for 11 columns
- **THEN** the API responds 400 with an error on the widget's labels saying a widget labels at most 10 columns

#### Scenario: Label too long
- **WHEN** an `analyst` saves a widget with a label of 81 characters for `numerator`
- **THEN** the API responds 400 with an error on the label of `numerator`

#### Scenario: Not a column's name
- **WHEN** an AI client saves a widget with a label for `Comuna`
- **THEN** the call is refused with an error on the widget's labels saying `Comuna` is not a column's name, and nothing is saved

### Requirement: Column labels in widget texts
Every text of a widget that names one of its columns SHALL use the name shown for it: the caption and accessible name of a chart, a stat tile's description, tooltips, a map's tooltip columns with no label of their own, and the headers of "Show as table" and cross tables. What is plotted SHALL read as the indicator's label and the names of its measure, or of its numerator and denominator. Filters, colors kept by value and the form's column choices SHALL keep the columns' own names.

#### Scenario: Caption of a ratio
- **WHEN** `teatro_hoy`, labelled "Teatro hoy", labels `numerator` "Hogares que asistieron", `denominator` "Hogares" and `comuna` "Comuna", and a column chart plots that ratio grouped by `comuna`
- **THEN** its caption reads "Teatro hoy: Hogares que asistieron / Hogares by Comuna"

#### Scenario: Stat tile description
- **WHEN** a stat tile plots the ratio of `numerator`, labelled "Hogares que asistieron", to `denominator`, labelled "Hogares", of `teatro_hoy`, labelled "Teatro hoy"
- **THEN** its description reads "Teatro hoy: Hogares que asistieron / Hogares"

#### Scenario: Measure named
- **WHEN** a stacked column chart over `teatro_conteo`, labelled "Teatro conteo", plots the measure `value`, labelled "Hogares", grouped by `pregunta`, labelled "Pregunta", with the series `respuesta`, labelled "Respuesta"
- **THEN** its accessible name says it plots "Teatro conteo: Hogares" by "Pregunta" and "Respuesta"

#### Scenario: Table headers
- **WHEN** a member shows the column chart of "Caption of a ratio" as a table
- **THEN** its headers read "Comuna" and "Teatro hoy: Hogares que asistieron / Hogares"

#### Scenario: Cross table headers
- **WHEN** a member shows the stacked column chart of "Measure named" as a table
- **THEN** its first header reads "Pregunta" and its series headers keep the answers, such as "1. Sí" and "2. Tal vez"

#### Scenario: Map tooltip column without a label
- **WHEN** a map's tooltip has the column `registros` with no label of its own, which its indicator labels "Registros recibidos", and the column `meta` with the label "Meta"
- **THEN** the tooltip and "Show as table" name them "Registros recibidos" and "Meta"

#### Scenario: Colors kept by value
- **WHEN** "Teatro" keeps color 8 for "1. Sí" of `respuesta`, and `teatro_conteo` labels `respuesta` "Respuesta"
- **THEN** the segments of "1. Sí" are still drawn in color 8

#### Scenario: Filters keep the column's name
- **WHEN** a widget with a label for `categoria` filters `categoria` equals "Sí"
- **THEN** the filter still names `categoria` in the widget form and keeps matching the rows whose `categoria` is "Sí"

### Requirement: Column labels in the widget form
The widget form SHALL offer a label for each column the widget shows: its measure, or its numerator and denominator, its group column and its series column. Each field SHALL show the indicator's label, else the column's name, as its placeholder, and the preview SHALL show the labels as typed without a new query. A form saved unchanged SHALL keep the widget's labels as saved. A column that the analyst replaces in the form SHALL lose the widget's label.

#### Scenario: Labels offered
- **WHEN** an `analyst` edits a column chart grouped by `comuna` of the ratio of `numerator` to `denominator` of `teatro_hoy`, which labels the three columns "Comuna", "Hogares que asistieron" and "Hogares"
- **THEN** the form offers a label field for each of the three columns, with the indicator's labels as placeholders

#### Scenario: Preview without a query
- **WHEN** the `analyst` types "Hogares que asistieron" as the label of `denominator`
- **THEN** the preview's caption reads it, and no query is sent

#### Scenario: Saved unchanged
- **WHEN** an `analyst` opens a widget of "Teatro" saved by an AI client with labels for `numerator` and `estrato`, which it does not show, and saves the form without changing anything
- **THEN** the widget keeps both labels, and "Teatro" has no new version

#### Scenario: Column replaced
- **WHEN** an `analyst` changes the group column of a widget with its own label for `comuna` to `estrato`, and saves
- **THEN** the saved widget has no label for `comuna`

#### Scenario: Tooltip column placeholder
- **WHEN** an `analyst` adds the tooltip column `registros`, which its indicator labels "Registros recibidos", to a map
- **THEN** the tooltip column's label field shows "Registros recibidos" as its placeholder

### Requirement: Widgets and indicators without column labels
A widget with no labels, over an indicator with none, SHALL look, read and export exactly as before column labels existed, naming its columns by their own names. A widget saved before column labels SHALL keep its stored settings as they are, so opening its form and saving it unchanged makes no version. A widget that plots its indicator's default measure without naming it SHALL keep the indicator's label alone as what it plots.

#### Scenario: Dashboard before labels
- **WHEN** a member opens "Teatro" after the upgrade, and none of its 44 widgets or of their indicators labels a column
- **THEN** every caption, description and table header names `numerator`, `denominator` and the group columns as before

#### Scenario: Stored settings kept
- **WHEN** the API reads and writes again a widget's settings stored before column labels existed
- **THEN** the stored text is the same, byte for byte

#### Scenario: Default measure
- **WHEN** a widget of "Quién respondió" names no measure and no ratio, and its indicator labels `value` "Personas"
- **THEN** its caption names what it plots with the indicator's label alone, as before
