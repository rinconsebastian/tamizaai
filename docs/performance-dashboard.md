# Performance dashboard

The performance dashboard shows, while a survey is in the field, whether data collection is on track. It is the first tab of every project and the page a project opens on. Every member can see it, viewers included.

It is computed from the copy of the submissions that Tamiza stores (see [kobo.md](kobo.md)), with those deleted in Kobo left out. It never contacts Kobo, so it works when Kobo is down, and it does not depend on the [scripts](scripts.md): it works before any script exists and is not affected when a run fails. It uses the answers as Kobo sent them, which matters for [coverage](#sampling-frame-coverage): the values it compares are the form's codes, not their labels.

Which parts a project can show depends on its form; the **Form check** card in the Overview lists what is missing (see [kobo.md](kobo.md#5-check-the-form-for-the-performance-dashboard)).

## Days and the date range

A submission's **day** is the date on which Kobo received it (`_submission_time`), in the project's **time zone**. Admins set the time zone in the Overview, under the project settings (for example `America/Bogota`); until then days are counted in UTC, and the dashboard says so. This is the time the server recorded, not the device's clock, so a phone with a wrong date cannot distort it.

The time zone changes only how the days are drawn: if you change it, past submissions move to the day they belong to in the new zone.

The **from** and **to** dates restrict every part of the page to those days, both included. Either can be left empty. Submissions that have no reception time are counted in the totals and in a separate count, but belong to no day, and are left out while a range is set.

## Submissions per day

The total, and one bar per day between the first and the last day with submissions (inside the range, if there is one). Days without submissions in between show as zero. **Show as table** lists the same numbers.

## Completion time

The time from `start` to `end`, when the form has both fields: the **median**, the **mean without outliers**, and how many outliers there are.

- A duration is an **outlier** when it is below the first quartile minus 1.5 times the interquartile range, or above the third quartile plus 1.5 times that range (Tukey's fences). There is nothing to configure. A form left open overnight, or a survey filled in a few seconds, shows up as an outlier instead of distorting the mean.
- A duration **that cannot be used** is left out of every figure and counted apart: `end` before `start`, a missing `end`, or a value that is not a date.
- The fences are shown in minutes so you can see where the line is.

When the form lacks `start` or `end`, the part says which one, and how to enable it in Kobo.

## Detail per enumerator

One row per enumerator: submissions, mean completion time without outliers, outliers, and the time of the last submission.

The enumerator is the question an admin selected as the **enumerator question** or, when none is selected (or the form no longer has it), the `username` field. Values that differ only in case or surrounding spaces are the same person: `Ana`, `ana ` and `ANA` are one row, shown with one of the spellings. Submissions with no value, an empty one, or only spaces are listed together as **No enumerator**.

## Sampling frame coverage

For each combination of the [sampling frame](kobo.md#sampling-frame), the dashboard shows the target, the submissions that match it, and the percentage achieved.

- **Matching.** An answer matches a frame value when they are equal after removing surrounding spaces and ignoring case. The frame holds the form's **codes** (the choice names, such as `cali`), not labels. Next to each code the dashboard shows the choice's label when it has one.
- **Percentage.** It is not capped: 130 submissions for a target of 100 show 130%. The progress bar stops at 100%. A combination with a target of 0 shows its submissions and no percentage.
- **Overall progress.** The sum of the lesser of achieved and target in each combination, over the sum of the targets (only combinations with a target above 0). Submissions above a target do not make up for another combination's shortfall. If every target is 0 there is no overall progress.

### Outside the frame

Submissions that match no combination, or that leave a dimension empty, are counted as **outside the frame**, and the most frequent combinations of their answers (up to 10) are listed. This is where mistakes show up: a code written differently in the form and in the frame, or a code that lost its leading zero. For example, if the frame has `5001` and 40 submissions answer `05001`, those 40 appear here. In a spreadsheet, store such codes as text so `05001` stays `05001`.

### Dimensions that cannot be counted

The dashboard warns, naming the dimension, when a dimension of the frame:

- maps to a question that the form no longer has (for example after a redeploy removed it). Update the frame in the **Sampling frame** section;
- maps to a question inside a **repeat group**, which a submission answers once per repeat, so a submission has no single value to match;
- maps to a **multiple-choice** question (`select_multiple`), whose answer holds several choices.

Coverage is still shown with the warning, but figures for those dimensions will not match what you expect.

### Without a frame

The part says that no sampling frame is defined. Admins and analysts get a link to the **Sampling frame** section; viewers only see the message.

## Freshness

The page shows when its figures were computed. Pressing **Refresh** computes them again.

- Every submission delivered by a webhook, or brought by a finished synchronization, is included the next time you open or refresh the page.
- Figures computed less than a minute earlier may be shown again, but only while nothing new has arrived since.
- While a synchronization is still running, its submissions can take up to a minute to appear.

A computation that takes longer than 25 seconds is stopped, and the page says so; try again or narrow the date range. At most two computations run at a time, so a busy server may make a request wait its turn. This never delays the reception of submissions.

## What it does not do

- It does not filter by enumerator or form version, and does not split coverage by single dimensions.
- It does not support ranges in the sampling frame (an age range over an integer question): the form must store the range as a code.
- It does not exclude test submissions or submissions marked as not approved in Kobo, and does not remove duplicates.
- It locates `start`, `end` and the enumerator with the deployed version's questions for every submission; submissions made with a version that had them elsewhere count as durations that could not be used.
- It has no exports and no automatic refresh.
