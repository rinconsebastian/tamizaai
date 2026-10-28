# dashboard-reports Specification

## Purpose

Lets a project email a dashboard's figures on a schedule to people who need not be Tamiza users. The email's body holds the numbers and short tables, and an attached XLSX workbook holds every widget's full table.

## Requirements

### Requirement: Scheduled email of a dashboard
Each dashboard SHALL be able to have at most one scheduled email. Its settings are the recipients, the weekdays and time of sending, and whether it is active or paused. Scheduled emails SHALL exist only while email sending is enabled (see `email-sending`).

#### Scenario: Email settings saved
- **WHEN** an `analyst` saves a scheduled email for "Housing" to `ana@partner.org` and `luis@city.gov.co`, on Monday and Thursday at 07:00, active
- **THEN** opening the settings again shows those two recipients, Monday and Thursday, 07:00 and active

#### Scenario: One email per dashboard
- **WHEN** "Housing" already has a scheduled email and an `analyst` saves new settings for it
- **THEN** the new settings replace the old ones, and "Housing" still has one scheduled email

### Requirement: Email recipients
A scheduled email SHALL have 1 to 20 recipients, each a valid email address of any domain, whether or not it belongs to a Tamiza user. Addresses that differ only in case SHALL be kept once. Saving SHALL be refused with an error on the recipients when there are none, more than 20, or one that is not a valid address, and nothing SHALL be saved.

#### Scenario: Address of any domain
- **WHEN** an `analyst` saves the recipients `ana@partner.org` and `luis@city.gov.co`, neither of them a Tamiza user
- **THEN** the settings are saved with both addresses

#### Scenario: Invalid address
- **WHEN** an `analyst` saves the recipients `ana@partner.org` and `luis at city`
- **THEN** the API responds 400 with an error on the recipients naming `luis at city`, and nothing is saved

#### Scenario: No domain or display name
- **WHEN** an `analyst` saves the recipients `luis` and `Ana <ana@partner.org>`
- **THEN** the API responds 400 with an error on the recipients naming both, and nothing is saved

#### Scenario: Too many recipients
- **WHEN** an `analyst` saves 21 recipients
- **THEN** the API responds 400 with an error saying a scheduled email can have at most 20 recipients

#### Scenario: Same address twice
- **WHEN** an `analyst` saves `Ana@Partner.org` and `ana@partner.org`
- **THEN** the settings hold one recipient, `Ana@Partner.org`

### Requirement: Sending days and time
A scheduled email SHALL name one or more weekdays and one time of day, to the minute, in the project's time zone. On each chosen weekday, the email SHALL be sent once, at that time or as soon after it as possible. Saving SHALL be refused with an error when no weekday is chosen or the time is not a valid time of day.

#### Scenario: Weekday schedule
- **WHEN** a project's time zone is `America/Bogota` and its "Housing" email is set for Monday and Thursday at 07:00
- **THEN** an email is sent each Monday and each Thursday at 07:00 Bogotá time, and on no other day

#### Scenario: Every day
- **WHEN** an email is set for all seven weekdays at 18:30
- **THEN** one email is sent every day at 18:30 in the project's time zone

#### Scenario: No weekday
- **WHEN** an `analyst` saves an email with no weekday chosen
- **THEN** the API responds 400 with an error on the weekdays, and nothing is saved

#### Scenario: Time zone changed
- **WHEN** an email is set for 07:00 and an `admin` changes the project's time zone from `UTC` to `America/Bogota`
- **THEN** later emails are sent at 07:00 Bogotá time

### Requirement: First send after saving
Only the latest save of the settings SHALL count. A save made on a chosen weekday after that day's time SHALL NOT cause an email that day. A save made before that day's time SHALL leave the day due, unless that day's scheduled email was already sent. A save SHALL never cause a second scheduled email on a day that already had one.

#### Scenario: Saved after the time
- **WHEN** at 10:00 on a Monday an `analyst` saves an active email for every weekday at 07:00
- **THEN** no email is sent that Monday, and the first goes out on Tuesday at 07:00

#### Scenario: Saved before the time
- **WHEN** at 06:00 on a Monday an `analyst` saves an active email for every weekday at 07:00
- **THEN** the first email goes out that Monday at 07:00

#### Scenario: Time corrected later the same day
- **WHEN** an email is set for every day at 15:00, and on Monday at 10:00 an `analyst` changes the time to 07:00 by mistake, then at 10:30 changes it back to 15:00
- **THEN** Monday's email goes out at 15:00

#### Scenario: Paused edit, then activated
- **WHEN** on Monday at 10:00 an `analyst` edits the recipients of a paused email set for 07:00, and at 10:05 activates it with the time 15:00
- **THEN** Monday's email goes out at 15:00

#### Scenario: Re-saved after the day's email
- **WHEN** Monday's email went out at 07:00, and at 07:30 an `analyst` changes the time to 08:00
- **THEN** no other email is sent that Monday

### Requirement: Sends missed while the system was down
When the `api` was not running at a scheduled time, it SHALL send that email once when it is back, provided that it is still the same day in the project's time zone. It SHALL NOT send emails for earlier days it missed. It SHALL never send a scheduled email twice for the same day, including after a restart in the middle of a send.

#### Scenario: Back the same day
- **WHEN** an email is due at 07:00 and the `api` is down from 06:00 to 09:00 that day
- **THEN** the email is sent once, shortly after 09:00

#### Scenario: Down for several days
- **WHEN** an email is due every day at 07:00 and the `api` is down from Monday 06:00 to Wednesday 08:00
- **THEN** Wednesday's email is sent once, shortly after 08:00, and no email is sent for Monday or Tuesday

#### Scenario: Restart during a send
- **WHEN** the `api` stops while it is sending Monday's email and starts again on Monday
- **THEN** Monday's email is not sent again

### Requirement: Paused email
While a scheduled email is paused, no scheduled email SHALL be sent for it. Its settings SHALL be kept, and sending it now SHALL still be possible. Making it active again SHALL follow the rule for the first send after saving.

#### Scenario: Paused
- **WHEN** an `analyst` pauses the "Housing" email, which is set for every day at 07:00
- **THEN** no email is sent at 07:00 while it stays paused

### Requirement: Removing a scheduled email
An `analyst` or `admin` SHALL be able to remove a dashboard's scheduled email. No email SHALL be sent for it afterwards, and its recipients SHALL be forgotten. Saving it again SHALL NOT send a second scheduled email on a day that already had one, nor allow sending now within 10 minutes of the last request. Removing a dashboard SHALL remove its scheduled email too.

#### Scenario: Email removed
- **WHEN** an `analyst` removes the "Housing" email
- **THEN** no further email is sent for "Housing", and its settings read as none

#### Scenario: Removed and saved again
- **WHEN** the "Housing" email went out at 07:00 and was sent now at 07:20, and at 07:25 an `analyst` removes it and saves it again for 08:00
- **THEN** no other email is sent that day, and sending it now is refused with `reports.tooSoon` until 07:30

#### Scenario: Dashboard removed
- **WHEN** an `analyst` removes the dashboard "Housing", which has an active scheduled email
- **THEN** no further email is sent for it

### Requirement: Author who no longer manages the project
A scheduled email SHALL be sent only while the member who last saved its settings is an `analyst` or `admin` of the project, or a superadmin. Otherwise no email SHALL be sent, and the settings SHALL show the failure "the member who set it up no longer manages the project". When another `analyst` or `admin` saves it again, it becomes theirs and is sent again.

#### Scenario: Author removed
- **WHEN** the analyst who saved the "Housing" email is removed from the project, and the email is due
- **THEN** no email is sent, and the settings show that its author no longer manages the project

#### Scenario: Taken over
- **WHEN** an `admin` then saves the "Housing" email again
- **THEN** the next email is sent, and replies go to that admin

### Requirement: Who manages scheduled emails
Only an `analyst` or `admin` of the project SHALL be able to read, save, remove or send now a dashboard's scheduled email. A `viewer` SHALL be refused with 403 and SHALL NOT be offered the action. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer refused
- **WHEN** a `viewer` asks the API to read or save the "Housing" email settings
- **THEN** the API responds 403

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project asks for the "Housing" email settings
- **THEN** the API responds 404

### Requirement: Scheduled emails while email is off
While email sending is disabled, the UI SHALL NOT offer scheduled emails. The API SHALL answer requests to save settings or send now with 503 and the code `reports.unavailable`. Saved settings SHALL be kept and SHALL NOT be sent. Once sending is enabled again, scheduled emails SHALL resume as after the system was down: a time that passed earlier that same day is sent once, and earlier days are not caught up.

#### Scenario: Turned off
- **WHEN** the operator removes the SMTP variables and restarts the `api`, and "Housing" has an active email
- **THEN** no email is sent, the dashboard menu shows no scheduled email action, and a request to send it now is answered 503 with `reports.unavailable`

#### Scenario: Turned on again
- **WHEN** the SMTP variables were removed on Monday, and the operator restores them on Wednesday at 10:00, and the "Housing" email is set for every day at 07:00
- **THEN** Wednesday's email is sent once, shortly after 10:00, no email is sent for Monday or Tuesday, and the next one goes out on Thursday at 07:00

### Requirement: Email envelope
A scheduled email SHALL carry every recipient in Bcc only, so no recipient sees another's address, and the sender address SHALL NOT be one of its recipients. Its reply-to address SHALL be the email of the member who last saved the settings, when that member has one. Its subject SHALL be the project's name, the dashboard's name and the date of sending in the email's regional format.

#### Scenario: Recipients hidden from each other
- **WHEN** the "Housing" email goes to `ana@partner.org` and `luis@city.gov.co`
- **THEN** each of them receives it with no other recipient's address in its headers

#### Scenario: Sender not a recipient
- **WHEN** the sender address is `reports@example.org` and the "Housing" email goes to `ana@partner.org`
- **THEN** the SMTP server is given only `ana@partner.org` as a recipient

#### Scenario: Replies
- **WHEN** `maria@ngo.org` last saved the "Housing" settings and a recipient replies
- **THEN** the reply is addressed to `maria@ngo.org`

#### Scenario: Subject
- **WHEN** the project "Census 2026" sends its "Housing" email on 28 October 2026 in American English
- **THEN** the subject reads "Census 2026 – Housing – Oct 28, 2026"

### Requirement: Email language and regional format
A scheduled email SHALL use the interface language and regional format of the member who last saved its settings (see `interface-language`); a region the server has no regional data for SHALL use its language's formats. The email's own words, its numbers and its dates SHALL follow them. Project, dashboard, widget, indicator, filter and column names, and data values, SHALL be shown exactly as they were created.

#### Scenario: Region without data
- **WHEN** a member whose browser reports `en-CO` saves the settings
- **THEN** the settings are saved, and the email uses English with the formats of `en`

#### Scenario: Spanish of Colombia
- **WHEN** a member whose interface is in Spanish with the regional format `es-CO` saves the settings, and a stat tile over a `decimal` indicator totals 12345.5
- **THEN** the email's own words are in Spanish and the total reads `12.345,50`

#### Scenario: Percentage in Spanish of Colombia
- **WHEN** the email's regional format is `es-CO` and a stat tile over a `percent` indicator has the value 0.455
- **THEN** the body shows `45,5%`

#### Scenario: Names kept
- **WHEN** a member whose interface is in English saves the settings of the dashboard "Vivienda"
- **THEN** the email's own words are in English, and the dashboard is named "Vivienda"

### Requirement: Filters in a scheduled email
Each widget in a scheduled email SHALL be computed with the dashboard's filter defaults, as they are when the email is sent, as a member sees it when opening the dashboard. A widget SHALL NOT be narrowed by a filter it ignores, and a filter without a default SHALL narrow nothing. The email SHALL list each filter with a default and its value, or say that no filter applies.

#### Scenario: Default applied
- **WHEN** "Housing" has the filter "Round" with the default 3, and the email is sent
- **THEN** every widget that "Round" narrows shows round 3 only, and the email lists "Round: 3"

#### Scenario: Ignored filter
- **WHEN** one widget of "Housing" ignores the filter "Round", whose default is 3
- **THEN** that widget shows every round, with a note saying it is not filtered by "Round"

#### Scenario: Fixed date range
- **WHEN** a date filter's default is the range 2026-10-01 to 2026-10-31 and the email is sent every day
- **THEN** every email uses that same range

### Requirement: Email content follows the dashboard
Each email SHALL hold the dashboard's widgets, settings and data as they are when it is sent. Widgets added, changed, moved or removed after the email settings were saved SHALL be reflected in the next email.

#### Scenario: Widget added later
- **WHEN** an `analyst` adds a widget to "Housing" on Tuesday, and its email is sent on Thursday
- **THEN** Thursday's email includes the new widget

### Requirement: Email header
The email body SHALL open with a header. It SHALL give the project, the dashboard and the time the email was generated, with the project's time zone. It SHALL give the latest time one of the widgets' indicators was published, and the filters in force.

#### Scenario: Header
- **WHEN** the "Housing" email of "Census 2026" is generated at 07:00 in `America/Bogota`, and its indicators were last published at 05:12
- **THEN** its header names "Census 2026" and "Housing", gives 07:00 with the time zone, gives 05:12 as the data's publication time, and lists the filters in force

### Requirement: Email body
After the header, the email body SHALL show the value of each stat tile. Then it SHALL show one block per other widget, in the dashboard's reading order: top to bottom, then left to right. Each block SHALL hold the widget's title and the first 10 rows of its "Show as table" view. When a widget has more rows, the block SHALL say how many more it has. The body SHALL say once where the full tables are: in the attachment, or, when the attachment was left off, that it was too large.

#### Scenario: Stat tiles first
- **WHEN** "Housing" has a column chart at the top, then two stat tiles below it
- **THEN** the body shows the two stat tile values before the column chart's block

#### Scenario: Long table
- **WHEN** a bar chart of "Housing" has 32 bars
- **THEN** its block shows the first 10 rows and says the widget has 22 more rows

#### Scenario: Value formatted
- **WHEN** a stat tile over the indicator `piped_water_rate`, whose format is `percent`, has the value 0.42 in an American English email
- **THEN** the body shows `42%`

### Requirement: Widget tables in a scheduled email
A widget's table in the body and in the workbook SHALL have the same columns and rows as its "Show as table" view, in the same order. The one exception is a map's table, which SHALL NOT include the kind of geometry. A histogram's table SHALL list each range with the number of values in it. A pie chart's table SHALL list every group value with its share.

#### Scenario: Histogram
- **WHEN** a histogram of "Housing" has 10 buckets over the values of 80 municipalities
- **THEN** its table lists the 10 ranges, each with the number of municipalities in it

#### Scenario: Pie chart
- **WHEN** a pie chart of "Housing" has 12 group values and draws the smallest ones as one slice
- **THEN** its table lists all 12 group values, each with its value and share

#### Scenario: Map
- **WHEN** a map of blocks with the tooltip columns "Dirección" and "Meta muestral" is in the email
- **THEN** its table lists each block, its plotted value, its address and its sample target, and no shape kind

#### Scenario: Line chart order
- **WHEN** a line chart is grouped by `round` with the values `1`, `2` and `10`
- **THEN** its table lists them as `1`, `2`, `10`

### Requirement: Missing values in widget tables
A missing group value SHALL read as the interface's text for no group, such as "(none)". A missing value SHALL read as "—" in the body and be an empty cell in the workbook. A widget whose indicator the project no longer lists SHALL use the `decimal` format with no unit, and the indicator's name as its label, as the dashboard does.

#### Scenario: Rows without a group value
- **WHEN** a bar chart of "Housing" has rows whose `municipality` is empty, in an English email
- **THEN** their bar's row reads "(none)"

#### Scenario: Removed indicator
- **WHEN** a widget's indicator script was removed but its table is still published
- **THEN** the widget's values have two decimals and no unit, and its caption uses the indicator's name

### Requirement: Widget notes in a scheduled email
A widget that cannot be shown SHALL appear with its title and the reason in place of a table. Reasons are: its indicator is not published, its table lacks a column the widget needs, its settings no longer fit its table, it could not be read in time, or no row matches. A widget that reached the row limit, or that a filter in force does not narrow, SHALL show its table with a note. One widget's failure SHALL NOT keep the others out.

#### Scenario: Settings no longer fit
- **WHEN** a script change publishes a widget's measure column as text
- **THEN** the email is still sent, and that widget shows a note saying its settings no longer fit its table

#### Scenario: Indicator not published
- **WHEN** one widget of "Housing" uses an indicator whose table is not published
- **THEN** the body shows that widget's title with a note saying its indicator is not published, and the other widgets as usual

#### Scenario: Read timed out
- **WHEN** one widget's query takes longer than the time limit
- **THEN** the email is still sent, and that widget shows a note saying it could not be read in time

### Requirement: Email body size
The HTML body SHALL stay within 80 KB. When the widgets' blocks do not all fit, the body SHALL list each remaining widget by title only and say that its rows are not shown in the body. The stat tile values and the header SHALL always be shown.

#### Scenario: Large dashboard
- **WHEN** a dashboard has 50 widgets with 10 rows each and their blocks would pass 80 KB
- **THEN** the body shows the blocks that fit, and lists the remaining widgets by title as not shown in the body

### Requirement: Plain-text alternative
Every scheduled email SHALL carry a plain-text version with the same header, stat tile values, tables and notes as the HTML body, for mail programs that do not show HTML.

#### Scenario: Text reader
- **WHEN** a recipient's mail program shows only plain text
- **THEN** it shows the header, each stat tile value, and each widget's title, rows and notes

### Requirement: Email footer
The email body SHALL end by saying that a member of the named project scheduled it from Tamiza, that recipients can reply to ask to stop receiving it, and that members can open the dashboard at its address in the deployment.

#### Scenario: Footer
- **WHEN** a recipient reads the "Housing" email of "Census 2026"
- **THEN** its end says that a member of "Census 2026" scheduled it, that replying asks to stop it, and gives the dashboard's address for members

### Requirement: Email workbook
Every scheduled email SHALL attach an XLSX workbook named after the project, the dashboard and the date of sending. Its first sheet, "Summary" in the email's language, SHALL hold the header, each stat tile's title and value as the email shows them, and each widget's notes.

#### Scenario: Summary sheet
- **WHEN** "Housing" has two stat tiles and an English email is sent
- **THEN** the workbook's first sheet is "Summary", with the header and both stat tiles' titles and values as the body shows them

### Requirement: Widget sheets
After the "Summary" sheet, the workbook SHALL have one sheet per widget that is not a stat tile, in reading order, named after its position and title. Each sheet SHALL hold the widget's whole table under a header row, or the reason it cannot be shown. Sheet names SHALL fit Excel's rules.

#### Scenario: Sheets
- **WHEN** "Housing" has a stat tile, then a column chart "Coverage by municipality", then a pie chart "Type of dwelling"
- **THEN** the workbook's sheets are "Summary", "2. Coverage by municipality" and "3. Type of dwelling"

#### Scenario: Whole table
- **WHEN** a bar chart has 32 bars
- **THEN** its sheet holds a header row and 32 rows

#### Scenario: Long title
- **WHEN** a widget's title has 60 characters, one of them `/`
- **THEN** its sheet's name is shortened to 31 characters, without the `/`, and is unique in the workbook

### Requirement: Numbers in the workbook
In each widget's sheet, plotted values, row counts, pie shares and numeric tooltip values SHALL be numbers, not text. Plotted values SHALL follow the indicator's format: `count` a whole number with thousands separators, `decimal` and `currency` two decimals, `percent` a percentage. Row counts SHALL be whole numbers, and shares percentages. A tooltip value SHALL follow its tooltip column's format, as in its tooltip. A unit SHALL be written in the column's header.

#### Scenario: Percentage cell
- **WHEN** a column chart over `piped_water_rate`, whose format is `percent`, has the value 0.42 for `Cali`
- **THEN** the cell holds the number 0.42, shown as a percentage

#### Scenario: Tooltip column keeps its own format
- **WHEN** a map over `survey_progress`, whose format is `percent`, has the tooltip column `sample_target` with no format chosen, and a block's target is 20
- **THEN** its cell holds the number 20, shown as `20`, not as a percentage

#### Scenario: Unit in the header
- **WHEN** the indicator `households_visited` has the format `count` and the unit `households`
- **THEN** its value column's header names `households`, and its cells hold whole numbers

### Requirement: Attachment size
When the workbook would be larger than 7 MB, the email SHALL be sent without it. The body SHALL then say that the attachment was too large and that members can export the indicator tables from the dashboard.

#### Scenario: Workbook too large
- **WHEN** a dashboard's workbook would be 9 MB
- **THEN** the email is sent with its body only, which says the attachment was too large

### Requirement: Nothing to send
An email SHALL NOT be sent when the dashboard has no widget, or when every widget has a note in place of its table for one of these reasons: not published, a missing column, settings that no longer fit, or not read in time. A widget with no matching rows counts as read. The send SHALL be recorded as failed with the reason "nothing to send".

#### Scenario: Empty dashboard
- **WHEN** the "Housing" email is due and "Housing" has no widget
- **THEN** no email is sent, and the settings show the failure "nothing to send"

#### Scenario: Nothing published yet
- **WHEN** the "Housing" email is due and none of its widgets' indicators is published
- **THEN** no email is sent, and the settings show the failure "nothing to send"

### Requirement: Sending now
An `analyst` or `admin` SHALL be able to send a dashboard's email now, whether it is active or paused, using its saved settings. The API SHALL accept the request with 202. The email SHALL be prepared before any scheduled email that has not started yet, and sent once it is built. Sending now SHALL NOT change the schedule.

#### Scenario: Send now
- **WHEN** an `analyst` sends the paused "Housing" email now
- **THEN** the API answers 202, the email is sent, and it stays paused

#### Scenario: Send now during a busy hour
- **WHEN** five scheduled emails are due at 07:00, and an `analyst` sends "Housing" now while the first of them is being prepared
- **THEN** "Housing" is sent right after that first email, before the other four

#### Scenario: Schedule unchanged
- **WHEN** at 06:00 an `analyst` sends now an active email set for every day at 07:00
- **THEN** the scheduled email is still sent at 07:00

### Requirement: Send-now refusals
The API SHALL refuse to send now a dashboard with no saved settings, with 409 `reports.none`. It SHALL refuse with 409 `reports.tooSoon` a request made less than 10 minutes after the last one for the same dashboard.

#### Scenario: No settings
- **WHEN** an `analyst` asks to send now a dashboard that has no scheduled email
- **THEN** the API responds 409 with `reports.none`

#### Scenario: Twice in a row
- **WHEN** an `analyst` sends the "Housing" email now, and asks again 3 minutes later
- **THEN** the second request is answered 409 with `reports.tooSoon`

### Requirement: Send results
The settings SHALL show when the email was last sent and, when the last attempt failed, when it failed and why. Possible reasons are an outcome of `email-sending`, "nothing to send", an author who no longer manages the project, or an unexpected error. A failed send SHALL NOT be retried; the next scheduled time or sending now tries again. A later successful send SHALL clear the failure.

#### Scenario: Failure shown
- **WHEN** the "Housing" email fails because the SMTP server refuses its credentials
- **THEN** its settings show the time of the failure and "authentication failed", and no retry happens before the next scheduled time

#### Scenario: Failure cleared
- **WHEN** the next "Housing" email is sent
- **THEN** its settings show that send's time and no failure

### Requirement: Scheduled email action
While email sending is enabled, the dashboard's actions SHALL include *Scheduled email…* for an `analyst` or `admin`, after the filters action. It SHALL open the dashboard's email settings.

#### Scenario: Action offered
- **WHEN** email sending is enabled and an `analyst` opens the actions of "Housing"
- **THEN** *Scheduled email…* is listed after the filters action

#### Scenario: Not offered without email
- **WHEN** email sending is disabled and an `admin` opens the actions of "Housing"
- **THEN** no scheduled email action is listed

### Requirement: Scheduled email dialog
The dialog SHALL offer the recipients, the weekdays, the time and whether the email is active. It SHALL show the project's time zone, the last send and the last failure. It SHALL let the member save, remove the email, or send it now.

It SHALL warn that the recipients need not be Tamiza users and will receive the dashboard's data outside Tamiza. It SHALL also say that the email uses the filter defaults, which are fixed values.

Validation errors SHALL appear next to their field.

#### Scenario: Warning shown
- **WHEN** an `analyst` opens the scheduled email dialog of "Housing"
- **THEN** it says that the recipients get the dashboard's data outside Tamiza, and that the email uses the filter defaults

#### Scenario: Error next to the field
- **WHEN** an `analyst` enters `luis at city` as a recipient and saves
- **THEN** the dialog stays open and shows the API's error next to the recipients

#### Scenario: Send now while busy
- **WHEN** an `analyst` presses *Send now*
- **THEN** the control shows that it is busy until the API answers, and then a message says the email was queued and will be sent shortly

#### Scenario: Phone width
- **WHEN** an `analyst` opens the dialog in a viewport 360 pixels wide
- **THEN** every field and action is usable without scrolling the page horizontally
