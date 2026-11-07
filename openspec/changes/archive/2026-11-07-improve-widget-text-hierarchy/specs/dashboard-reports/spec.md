## ADDED Requirements

### Requirement: Widget texts in a scheduled email kept whole
A scheduled email SHALL keep every text it sent before the widget's foot existed, worded as before, in the body, the plain text and the workbook:

- each widget's caption line, under a subtitle too, unless the widget hides it: the email shows tables, which have no foot of chips to carry it;
- its notes: the small-base note, the n line and the no-data line;
- its footnote.

A stat tile's value SHALL stay at full contrast. The chips and the value-axis title of the page SHALL NOT appear in the email.

#### Scenario: Caption under a subtitle in the email
- **WHEN** the "Teatro" email holds the column chart "2.1. Asistió a espectáculos, por estrato", whose subtitle and caption text are both set and whose caption is not kept in sight
- **THEN** its block shows the subtitle under the title and the caption line "% de los hogares que asistieron by Estrato" above its table, in the HTML body and in the plain text

#### Scenario: Notes worded as before
- **WHEN** "Teatro", whose threshold is 30 and which shows base n, sends its email after this change
- **THEN** each widget's notes, n line, no-data line and footnote read as they did before it, and no chip text such as "2 with a small base" is in the body

#### Scenario: Stat tile with a small base
- **WHEN** the email holds a stat tile whose n is 26 under the threshold 30 and which shows its n line
- **THEN** its value is written in full contrast, followed by its line `n = 26 …` as before, with no small-base text and no chip
