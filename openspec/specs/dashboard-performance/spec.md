# dashboard-performance Specification

## Purpose
Show, while a survey is in the field, how data collection is progressing: submissions per day, completion time, the work of each enumerator and the coverage of the sampling frame, computed from the copy of the project's submissions that Tamiza stores.

## Requirements

### Requirement: Performance dashboard
Every project member SHALL be able to open the project's performance dashboard, which SHALL be the page a project opens on. It SHALL be computed from the copy of the project's submissions that Tamiza has stored, leaving out those marked as deleted and using the answers as Kobo sent them. It SHALL NOT contact Kobo and SHALL NOT depend on the project's scripts or runs. A user who is not a member SHALL get the same answer as for a project that does not exist.

#### Scenario: Viewer opens a project
- **WHEN** a `viewer` opens a project
- **THEN** the performance dashboard is shown

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project requests its performance dashboard
- **THEN** the API responds 404

#### Scenario: Deleted submissions
- **WHEN** 3 of a project's 100 stored submissions are marked as deleted
- **THEN** every part of the dashboard counts 97 submissions

#### Scenario: Independent of the scripts
- **WHEN** the project has no scripts, or its last run failed
- **THEN** the dashboard shows the stored submissions as usual

#### Scenario: Kobo unreachable
- **WHEN** the Kobo server cannot be reached
- **THEN** the dashboard shows the stored submissions as usual

### Requirement: Days in the project's time zone
A submission's day SHALL be the date on which Kobo received it (`_submission_time`), in the project's time zone. A submission without a reception time SHALL be counted in the totals and reported, but SHALL NOT belong to any day.

#### Scenario: Evening submission in Bogotá
- **WHEN** the project's time zone is `America/Bogota` and Kobo received a submission at 2026-09-02 02:30 UTC
- **THEN** the submission belongs to 2026-09-01

#### Scenario: Default time zone
- **WHEN** no time zone has been set for the project
- **THEN** days are counted in UTC and the dashboard says so

#### Scenario: Submission without reception time
- **WHEN** a stored submission has no reception time
- **THEN** it is counted in the total and in the count of submissions without a reception time, belongs to no day, and is left out while a date range is set

### Requirement: Date range
A member SHALL be able to restrict the dashboard to a range of days, from a first day to a last day, both included and both optional. The range SHALL apply to every part of the dashboard. A range whose first day is after its last day SHALL be rejected.

#### Scenario: One week of fieldwork
- **WHEN** a member sets the range from 2026-09-01 to 2026-09-07
- **THEN** totals, days, completion time, enumerators and coverage count only submissions received on those days

#### Scenario: Reversed range
- **WHEN** a member requests the range from 2026-09-07 to 2026-09-01
- **THEN** the API rejects it with a message saying the first day is after the last

### Requirement: Submissions per day
The dashboard SHALL show the number of submissions and, for every day from the first to the last day on which submissions were received within the range (in all of the project's days when none is set), the number received that day, with days without submissions between them as zero. It SHALL show how many submissions have no reception time.

#### Scenario: Daily counts
- **WHEN** 10 submissions arrived on 2026-09-01, none on 2026-09-02 and 4 on 2026-09-03
- **THEN** the dashboard shows 14 submissions and the counts 10, 0 and 4 for those three days

#### Scenario: Range with only a first day
- **WHEN** the range has only the first day 2026-09-05, and submissions arrived on 2026-09-03, 2026-09-06 and 2026-09-09
- **THEN** the days shown run from 2026-09-06 to 2026-09-09 with the counts 1, 0, 0 and 1

### Requirement: Completion time
When the form has `start` and `end` fields, the dashboard SHALL show the time from `start` to `end`: the median, the mean without outliers, and the number of outliers. A duration SHALL be an outlier when it is below the first quartile minus 1.5 times the interquartile range, or above the third quartile plus 1.5 times that range. Durations that cannot be used SHALL be left out of every figure and counted.

#### Scenario: Outliers left out of the mean
- **WHEN** nine surveys take between 20 and 30 minutes and one takes 6 hours
- **THEN** the 6-hour survey counts as an outlier and the mean without outliers is computed from the other nine

#### Scenario: Durations that cannot be used
- **WHEN** one submission has `end` before `start`, one has no `end`, one has a `start` that is not a date, and one has `end` and `start` set to `infinity`
- **THEN** none of them enters the median, the mean or the quartiles, the dashboard reports 4 durations that could not be used, and the other figures are still shown

#### Scenario: Form without start or end
- **WHEN** the form has no `end` field
- **THEN** the completion-time part says it is unavailable because the form lacks `end`, and suggests enabling it in Kobo

### Requirement: Per-enumerator detail
When the project has an enumerator identifier, the dashboard SHALL list each enumerator with their submissions, mean completion time without outliers, number of outliers, and the reception time of their last submission. The identifier SHALL be the question an admin selected, while the deployed form still has it, or else the `username` field. Values that differ only in case or surrounding white space SHALL be the same enumerator.

#### Scenario: Same enumerator written differently
- **WHEN** submissions carry the enumerator values `Ana`, `ana ` and `ANA`
- **THEN** the dashboard shows one enumerator with all of them

#### Scenario: Submissions without an enumerator
- **WHEN** 5 submissions have no value for the enumerator identifier, an empty value, or only spaces
- **THEN** the dashboard shows them together as having no enumerator

#### Scenario: Selected question over username
- **WHEN** the form has `username` and an admin selected the question `enumerator_code`
- **THEN** the dashboard groups submissions by `enumerator_code`

#### Scenario: Selected question removed by a redeploy
- **WHEN** an admin selected the question `enumerator_code`, the form was redeployed without it, and the form has `username`
- **THEN** the dashboard groups submissions by `username`

#### Scenario: No enumerator identifier
- **WHEN** the form has no `username` and no question is selected, or the selected question is no longer in the form
- **THEN** the per-enumerator part says it is unavailable and how an admin can select a question

### Requirement: Sampling frame coverage
For each combination of the sampling frame, the dashboard SHALL show its target, the number of submissions whose answers match it, and the percentage achieved. An answer SHALL match a value when they are equal after removing surrounding white space and ignoring case; the values are the form's codes (choice names). The percentage SHALL NOT be capped, and a combination with a target of 0 SHALL show no percentage.

#### Scenario: Combination achieved
- **WHEN** the frame targets 120 surveys for `cali`, `f`, `18_29`, and 90 submissions answer those three codes
- **THEN** the dashboard shows 90 of 120 for that combination, 75%

#### Scenario: Over target
- **WHEN** 130 submissions match a combination whose target is 100
- **THEN** the dashboard shows 130%

#### Scenario: Case and spaces
- **WHEN** the frame holds the value `Cali` and submissions answer `cali` or ` cali`
- **THEN** they count for that combination

#### Scenario: Target of zero
- **WHEN** a combination has a target of 0
- **THEN** its achieved submissions are shown and no percentage is

### Requirement: Overall coverage progress
The dashboard SHALL show the overall progress of the sampling frame: the sum, over the combinations with a target above 0, of the lesser of achieved and target, divided by the sum of those targets. When no combination has a target above 0 it SHALL show no overall progress.

#### Scenario: Overall progress
- **WHEN** one combination is at 150% of its target of 100 and another at 50% of its target of 100
- **THEN** the overall progress is 75%, because surveys above a target do not make up for another combination's shortfall

#### Scenario: Every target is zero
- **WHEN** every combination of the frame has a target of 0
- **THEN** no overall progress is shown

### Requirement: Labels for coverage values
The dashboard SHALL show the label of each choice next to the codes of a combination, found with the same comparison as answers (white space removed, case ignored). When a code has no label it SHALL show only the code.

#### Scenario: Labels shown
- **WHEN** the frame row `cali`, `f`, `18_29` is shown and the form labels those choices `Cali`, `Female` and `18 to 29`
- **THEN** the row shows the codes with those labels

#### Scenario: Label found despite case
- **WHEN** the frame holds the value `Cali` and the form's choice is named `cali` with the label `Cali municipality`
- **THEN** the row shows `Cali` with the label `Cali municipality`

### Requirement: Submissions outside the frame
The dashboard SHALL count the submissions that match no combination of the sampling frame, including those that leave a dimension empty, and SHALL list the most frequent combinations of their answers, up to 10.

#### Scenario: Lost leading zero
- **WHEN** the frame uses the municipality code `5001` and 40 submissions answer `05001`
- **THEN** those 40 submissions are counted outside the frame and `05001` appears among the most frequent combinations outside it

### Requirement: Coverage dimensions that cannot be counted
The dashboard SHALL name each dimension of the sampling frame that cannot be counted reliably: one whose field the form no longer has, one inside a repeat group, or one on a multiple-choice question. Coverage SHALL still be shown whenever the project has a frame, with that warning.

#### Scenario: Question removed by a redeploy
- **WHEN** a dimension maps to `municipality` and the form was redeployed without that question
- **THEN** the coverage part warns that the dimension `municipality` maps to a field the form no longer has

#### Scenario: Multiple-choice dimension
- **WHEN** a dimension maps to a `select_multiple` question
- **THEN** the coverage part warns that the dimension cannot be counted because an answer can hold several choices

#### Scenario: Dimension inside a repeat group
- **WHEN** a dimension maps to a question inside a repeat group
- **THEN** the coverage part warns that the dimension cannot be counted because the question is answered once per repeat

### Requirement: No sampling frame
Without a sampling frame, the coverage part SHALL say that none is defined. Members who can edit the frame SHALL also be offered a way to configure it.

#### Scenario: Analyst without a frame
- **WHEN** an `analyst` opens the dashboard of a project without a sampling frame
- **THEN** the coverage part says no frame is defined and links to the sampling frame section

#### Scenario: Viewer without a frame
- **WHEN** a `viewer` opens the same dashboard
- **THEN** the coverage part says no frame is defined, with no link to configure it

### Requirement: Freshness
The dashboard SHALL show when its figures were computed and SHALL let a member compute them again. Figures SHALL include every submission stored by a webhook delivery or a finished synchronization before the request. Figures computed less than a minute earlier MAY be shown again, only while no such delivery or synchronization has happened since.

#### Scenario: Refresh after a webhook delivery
- **WHEN** a webhook delivers a submission after the figures were computed and a member presses Refresh
- **THEN** the dashboard counts the new submission and shows a later computation time

#### Scenario: Nothing new
- **WHEN** a member opens the dashboard twice within a minute and nothing has arrived in between
- **THEN** both show the same computation time

#### Scenario: Old figures
- **WHEN** a member opens the dashboard more than a minute after the figures were computed
- **THEN** the figures are computed again

### Requirement: Time limit
A computation of the dashboard that takes longer than 25 seconds SHALL be stopped and reported as such. Other users' requests, and the reception of submissions, SHALL NOT be stopped by it, and a member who leaves the page SHALL NOT stop the computation for another who asked for the same figures.

#### Scenario: Computation too long
- **WHEN** computing the dashboard takes more than 25 seconds
- **THEN** the dashboard shows a message that the computation took too long, and the next request to the project is answered normally

#### Scenario: Member leaves while computing
- **WHEN** two members ask for the same figures and the first closes the page before they are ready
- **THEN** the second still receives them
