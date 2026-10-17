# script-assistant Specification

## Purpose

An optional AI assistant in the Scripts section that drafts, rewrites and fixes scripts from the project's schema, the script contract and the analyst's own test results, without sending respondents' data to the model provider.

## Requirements

### Requirement: Available only when configured
The assistant SHALL exist only when the operator has set the provider's base URL, model and API key in the deployment's environment. Without any one of them, the Scripts section SHALL show no assistant, its API routes SHALL answer that the assistant is not available, and no request SHALL be made to any provider. The system configuration that the UI reads SHALL say whether the assistant is enabled and nothing else about it.

#### Scenario: Not configured
- **WHEN** a deployment has no assistant variables and an analyst opens the Scripts section
- **THEN** the side panel shows the tabs Tables, Reference and Versions only, and a request to the assistant API answers 503 with the code `assistant.disabled`

#### Scenario: Partly configured
- **WHEN** the operator sets the base URL and the model but not the API key
- **THEN** the assistant is not available, as if nothing were configured, and the `api` log says once which variable is missing

#### Scenario: Configured
- **WHEN** the three variables are set and the `api` has restarted
- **THEN** the system configuration reports the assistant as enabled, without the provider, the model or the key

### Requirement: Who can use the assistant
Only analysts and admins of the project SHALL be able to use the assistant, and only on the text being edited. A `viewer` SHALL see no Assistant tab and SHALL be refused by its API routes; a user who is not a member SHALL get the answer given for a project that does not exist. While an older version is on view, the tab SHALL say that the assistant works on the current text and offer no new request.

#### Scenario: Analyst
- **WHEN** an analyst opens the Scripts section of a deployment with the assistant enabled
- **THEN** the side panel has a fourth tab, Assistant, after Tables, Reference and Versions

#### Scenario: Viewer
- **WHEN** a `viewer` opens the Scripts section, or calls the assistant API of the project
- **THEN** no Assistant tab is shown, and the API answers 403

#### Scenario: Not a member
- **WHEN** a user who is not a member of the project calls its assistant API
- **THEN** the API answers 404

#### Scenario: Older version on view
- **WHEN** an analyst views version 3 of a script whose current version is 5 and opens the Assistant tab
- **THEN** the tab says that the assistant works on the current text and offers no new request until the analyst returns to it, while a request already being written keeps its waiting state and Cancel

### Requirement: Any OpenAI-compatible provider
The `api` SHALL send each assistant request in the OpenAI chat-completions format to the configured base URL with the configured model, the key only in the `Authorization` header, and the configured extra fields merged into the request body. It SHALL depend on no provider-specific feature, so any compatible server, including one on the local network, works, and changing provider means changing those variables and restarting the `api`.

#### Scenario: Switching provider
- **WHEN** the operator replaces a Gemini base URL, model and key with those of another compatible provider and restarts the `api`
- **THEN** the next request goes to the new provider and the assistant works with no other change

#### Scenario: Server on the local network
- **WHEN** the base URL is `http://192.168.1.20:11434/v1/` of a self-hosted model server
- **THEN** the `api` sends the requests there

#### Scenario: Extra fields
- **WHEN** the operator sets the extra fields to a JSON object that turns off a provider's reasoning mode
- **THEN** every request body includes those fields

#### Scenario: Provider redirects
- **WHEN** the provider answers a request with a redirect
- **THEN** the `api` does not follow it and reports the provider as unavailable, so the key is never sent to another address

### Requirement: Draft a whole script
In the Assistant tab, an analyst SHALL be able to ask for a whole script by describing it. The answer SHALL be an explanation and one complete script for the selected script's kind. When an indicator is selected, a "Draft from the details" action SHALL start the request from the indicator's label, description, unit and format as the details fields hold them, saved or not.

#### Scenario: Draft an indicator
- **WHEN** an analyst selects the indicator `piped_water_rate`, writes "share of households with piped water by municipality" and sends it
- **THEN** the tab shows the explanation and offers a script that defines `compute(cleaned, ctx)`

#### Scenario: Draft from the details
- **WHEN** an indicator has the label "Households with piped water", the unit "households" and the format percent, and the analyst chooses "Draft from the details"
- **THEN** the request is filled from those details, and the analyst can edit it before sending it

#### Scenario: Unsaved details
- **WHEN** the analyst changes the indicator's format to decimal without saving and chooses "Draft from the details"
- **THEN** the request uses the format decimal

### Requirement: Rewrite the cell at the cursor
While the cursor is inside a `# %%` cell, an analyst SHALL be able to ask for that cell to be rewritten; the scope SHALL follow the cursor as it moves, with or without typing. The whole script SHALL be sent as context and the answer SHALL propose a replacement for that cell only, offered as a cell replacement only when it is a single cell that starts with a `# %%` line and exactly one cell of the editor still has the targeted cell's text, wherever it now is.

#### Scenario: Rewrite a cell
- **WHEN** the cursor is in the second of three cells, named `Rate`, and the analyst asks "exclude households with no members"
- **THEN** the answer offers a replacement for the cell `Rate`, and applying it leaves the first and third cells unchanged

#### Scenario: Cursor outside any cell
- **WHEN** the script has no `# %%` line, or the cursor is above the first one
- **THEN** the "This cell" scope is not offered and the tab says that it needs a `# %%` cell

#### Scenario: Cursor moved into a cell
- **WHEN** the cursor is above the first `# %%` line and the analyst clicks inside the cell `Rate` without typing
- **THEN** the "This cell" scope becomes available for the cell `Rate`

#### Scenario: Answer that is not one cell
- **WHEN** the answer to a cell request holds two `# %%` lines
- **THEN** the proposal is offered only for insertion at the cursor, with a note that it is not a single cell

#### Scenario: Cell edited while waiting
- **WHEN** the analyst edits the cell `Rate`, or removes its `# %%` line, while its rewrite is being written
- **THEN** the proposal is offered only for insertion at the cursor, with a note that the cell changed

#### Scenario: Line added above while waiting
- **WHEN** the analyst adds an import at the top of the script while the rewrite of the cell `Rate` is being written
- **THEN** Replace cell is still offered and replaces the cell `Rate`

### Requirement: Fix from a test result
The failed step of the script being tested in the analyst's last sample test, and each warning of an indicator's dashboard view, SHALL offer "Ask the assistant". It SHALL open the Assistant tab with the Fix scope for that test, bring the tab into view and put the focus on its request box. A fix SHALL use only a test that the same analyst started and that has not expired.

#### Scenario: Fix a failed test
- **WHEN** a test of an indicator fails with a `KeyError` and the analyst chooses "Ask the assistant" on the failed step
- **THEN** the Assistant tab opens with the Fix scope for that test, scrolled into view with the focus on the request box, and the answer explains the error and proposes a corrected script

#### Scenario: Dashboard warning
- **WHEN** the dashboard view of a test warns that a percent indicator has values above 1, and the analyst chooses "Ask the assistant" on that warning
- **THEN** the fix request names that warning

#### Scenario: Saved script 1 failed
- **WHEN** a test of an indicator fails in the step that runs the saved script 1
- **THEN** that step offers no "Ask the assistant", because the indicator did not run

#### Scenario: Expired test
- **WHEN** a fix request refers to a test that has expired since the tab was opened
- **THEN** the answer is given without the test and the tab says that the test was no longer available

### Requirement: When a fix is available
"Ask the assistant" and the Fix scope SHALL be available only while the test belongs to the selected script, the editor's text is the text that test ran, no older version is on view and, for a warning, the dashboard view still shows it. When that stops being true, the buttons SHALL disappear and the tab SHALL leave the Fix scope, saying to test again first.

#### Scenario: Text changed since the test
- **WHEN** the analyst edits the script after the test ended
- **THEN** neither the failed step nor the dashboard warnings offer "Ask the assistant", and the test result says to test again first

#### Scenario: Edited after asking
- **WHEN** the analyst chose "Ask the assistant" and then edits the script before sending
- **THEN** the tab leaves the Fix scope and says to test again first

#### Scenario: Test of another script
- **WHEN** the analyst tested indicator A and then selects indicator B, whose text is the same template
- **THEN** no "Ask the assistant" is offered for A's test while B is selected

#### Scenario: Ask while an older version is on view
- **WHEN** the last test failed and the analyst views an older version of the script
- **THEN** neither the failed step nor the dashboard warnings offer "Ask the assistant"

#### Scenario: Details changed after asking
- **WHEN** the analyst asked about the warning that a percent indicator has values above 1 and then changes the indicator's format to decimal
- **THEN** the warning disappears from the view and the tab leaves the Fix scope

#### Scenario: Asking again
- **WHEN** the analyst chose "Ask the assistant", then opened the Tables tab, then chooses "Ask the assistant" on a dashboard warning
- **THEN** the Assistant tab is shown again with the focus on its request box

### Requirement: Text of a fix request
"Ask the assistant" SHALL fill the request box with an editable sentence that names the failed step or the kind of warning and holds no text of the test: neither the error, nor the log, nor a value. The tab SHALL NOT send a request whose box is blank or longer than a request turn may be, and SHALL say so.

#### Scenario: Fix with nothing typed
- **WHEN** the analyst chooses "Ask the assistant" on a failed step and sends without typing
- **THEN** the request's last message is that sentence and holds no text of the step's error or log

#### Scenario: Blank request
- **WHEN** the request box is empty or holds only spaces
- **THEN** Send is not available

#### Scenario: Request too long
- **WHEN** the analyst pastes 20 KB of text into the request box
- **THEN** Send is not available and the tab says how long a request may be

### Requirement: Proposals reach the editor only when applied
A proposal SHALL enter the editor only when the analyst applies it, as one edit that undo reverts. The actions SHALL be Replace script, offered only for a complete proposal that defines the script's contract function, Replace cell, for a cell proposal, and Insert at cursor. When the editor's text changed after the request was sent, applying SHALL ask for confirmation first. The assistant SHALL never save a version nor start a test.

#### Scenario: Apply and undo
- **WHEN** the analyst chooses Replace script on a proposal and then presses Ctrl+Z
- **THEN** the editor shows the text it had before the proposal

#### Scenario: Not saved, not tested
- **WHEN** the analyst applies a proposal
- **THEN** the script has unsaved changes, no version is created, no test starts, and the last test's result is marked as out of date

#### Scenario: Partial proposal
- **WHEN** a draft request is answered with code that does not define `compute`
- **THEN** Replace script is not offered and Insert at cursor is

#### Scenario: Answer cut off
- **WHEN** the provider stopped the answer at its length limit in the middle of the code, after `def compute`
- **THEN** the tab says the answer was cut off and offers the code only for insertion at the cursor

#### Scenario: Several blocks
- **WHEN** an answer to a draft request holds two code blocks
- **THEN** each is offered only for insertion at the cursor, with a note that the answer was not one script

#### Scenario: Edited while waiting
- **WHEN** the analyst types in the editor while the answer is being written and then applies the proposal
- **THEN** the tab asks for confirmation before replacing the text

### Requirement: Proposals are checked before they are offered
Before a proposal is offered, the editor's checks, including the import checks, SHALL run on the text the editor would hold after applying it, and their marks SHALL be shown with the proposal. Marks SHALL NOT prevent applying it.

#### Scenario: Proposal with an unavailable import
- **WHEN** a proposal imports `scipy.stats`
- **THEN** the tab shows the proposal with the warning that `scipy` is not available to scripts, and the analyst can still apply it

### Requirement: What is sent to the model
Each request SHALL send only: the contract rules and the libraries available and unavailable to scripts; the templates of the script's kind; the input tables and columns with types, row counts and question labels; the reference tables' columns and types; the choice lists; the time zone and enumerator question; the saved script 1; the indicator's details; the editor's text; the earlier turns of the conversation; and a fix's test facts.

#### Scenario: Schema and choices
- **WHEN** an analyst sends a request for an indicator in a project whose clean table `main` has 1,204 rows and the select question `water_source`
- **THEN** the request holds `main` with its row count, the column `water_source` with its type and label, and the labels of its choices

#### Scenario: Libraries
- **WHEN** any request is sent
- **THEN** it states the libraries scripts can use with their versions and that scikit-learn, SciPy and statsmodels are not available

#### Scenario: Nothing published yet
- **WHEN** the project has never run
- **THEN** the request says that no input tables are known yet and still holds the form's questions and the choice lists of its select questions

#### Scenario: Earlier answers
- **WHEN** the conversation holds two earlier answers that proposed code
- **THEN** the request holds their explanations, and a short note in place of each one's code

### Requirement: Data that is never sent
No request SHALL hold rows of the sample, of a table preview, of a `ctx.show` table, of a reference table or of any published table, the text of a `ctx.show` value, a log line without a log level, nor a value, total or group of an indicator's dashboard view.

#### Scenario: Preview rows
- **WHEN** a fix refers to a test whose step previews 100 rows of `main` and whose `ctx.show` displayed a table and a text value
- **THEN** the request holds the names, columns, types and row counts of those tables and the name of the text value, and none of their values

#### Scenario: Printed output
- **WHEN** the script printed `df.head()` during the test
- **THEN** none of the printed lines are in the request, because they do not start with a log level

#### Scenario: Chart groups
- **WHEN** the test's dashboard view has a bar chart by `enumerator` with 12 groups and a total of the measure `value`
- **THEN** the request holds that `enumerator` has 12 groups, and neither the group values nor the total

### Requirement: Test facts sent with a fix
Of the test a fix refers to, a request SHALL hold only: each step's name and status and the stop line; the names, columns, types and row counts of its tables and of its `ctx.show` values, with the line of each `ctx.show` call; its error and log lines, masked; the dashboard view's column roles, row counts, counts of empty values and duplicate rows, and number of groups per column; and the kind of warning asked about with its column or count.

#### Scenario: Failed indicator
- **WHEN** an analyst asks for a fix of a test whose flattening and saved script 1 succeeded and whose indicator step failed
- **THEN** the request holds the three steps with their status, the tables of the first two with their columns and row counts, and the masked error of the third

#### Scenario: Warning asked about
- **WHEN** the analyst chose "Ask the assistant" on the warning that the column `year` is plotted as a measure
- **THEN** the request names that warning and the column `year`

#### Scenario: Percent warning
- **WHEN** the analyst asks about the warning that a percent indicator's largest value is 87.5
- **THEN** the request says that values are above 1 and does not hold 87.5

### Requirement: Masked test text
Every name and text of a fix's test facts (its error, log lines, table, column and `ctx.show` names, and the names in the dashboard view and the warning) SHALL have each word and number replaced by `…` unless it is a name of the schema or the form, a word of the editor's text that is not a word of a label, a word of a fixed vocabulary of Python, library and Tamiza messages, or a number of at most six digits.

#### Scenario: Known column kept
- **WHEN** the test failed with `KeyError: 'water_source'` and `water_source` is a column of the schema
- **THEN** the request holds `KeyError: 'water_source'`

#### Scenario: Quoted value masked
- **WHEN** the test failed with `ValueError: could not convert string to float: '3001234567'`
- **THEN** the request holds `ValueError: could not convert string to float: '…'`

#### Scenario: Unquoted value masked
- **WHEN** the test failed with `pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: Juan Perez`
- **THEN** the request holds the message with `…` in place of `Juan Perez`

#### Scenario: Message the script wrote
- **WHEN** the script raised `ValueError(f"only {n} rows, first {name}")` and the test failed with `ValueError: only 3 rows, first Ana Gómez`
- **THEN** the request holds `ValueError: only 3 rows, first …`

#### Scenario: Column made from data
- **WHEN** the script pivoted a table and one output column, which is also the indicator's default measure, is named `juan_perez`
- **THEN** the request names that column `…` wherever it appears

### Requirement: Labels and numbers in masked text
In masked test text, a choice or question label SHALL be kept only as the whole label, never word by word, even when the editor's text holds it. Digits joined by dots, commas, colons, slashes, hyphens or spaces, with or without spaces around them, SHALL count as one number, and so SHALL the digit parts of one name joined by underscores.

#### Scenario: Name split across labels
- **WHEN** the choice lists hold `Santa Ana` and `Gómez Plata`, the editor's text compares with both labels, and the error ends with `first Ana Gómez`
- **THEN** the request holds `first …`

#### Scenario: Whole label kept
- **WHEN** the error is `invalid literal for int() with base 10: 'Santa Ana'` and `Santa Ana` is a choice label
- **THEN** the request keeps `'Santa Ana'`

#### Scenario: Separated numbers
- **WHEN** log lines hold `1.023.456.789`, `300 123 4567`, `300 - 123 - 4567`, `1985-03-12`, `4.6097, -74.0817` and the column name `c_300_123_4567`
- **THEN** each of them becomes `…`

### Requirement: Tracebacks and log lines of a fix
Of an exception the script raised, a fix SHALL send the exception's type and the first line of its message, taken from the end of the traceback and masked, and the line and function of each frame of the script, without source lines or notes. It SHALL send only the log lines that start with a log level, at most the last 60, masked like the error.

#### Scenario: Exception type kept
- **WHEN** the exception is `pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: Juan Perez`
- **THEN** the request keeps that type and masks the value in the message after it

#### Scenario: Message on several lines
- **WHEN** the script raised `ValueError("Duplicated submissions:\njuan.perez: 3")`
- **THEN** the request holds `ValueError: Duplicated submissions:` and nothing of the second line

#### Scenario: Library hint after the message
- **WHEN** `pd.to_datetime` with a format failed on the value `Juan Perez`, and the error's last line is a hint about `format='mixed'`
- **THEN** the request holds `ValueError: time data "…" doesn't match format` with the format, not the hint

#### Scenario: Traceback
- **WHEN** the error's traceback has a frame at line 12 of `compute` with its source line and markers
- **THEN** the request holds that the error was raised at line 12 in `compute`, without repeating the source line

#### Scenario: Syntax error
- **WHEN** the test failed with a `SyntaxError` on line 7
- **THEN** the request holds that the error is at line 7

#### Scenario: Log lines
- **WHEN** the step's log has the line `INFO Dropped 3 test submissions of Ana Gómez`, written by a `ctx.log` call in the editor's text, and a line printed without a level
- **THEN** the request holds `INFO Dropped 3 test submissions of …` and not the printed line

#### Scenario: Long log
- **WHEN** the step's log has 400 lines that start with a level
- **THEN** the request holds the last 60 of them

### Requirement: What will be sent is visible
The Assistant tab SHALL name the model and the provider's host and say in one sentence what is sent and that no rows are. "Show what will be sent" SHALL display the exact text the next request would send for the current script, test and request box, even when the box is empty, and "Copy context" SHALL copy that text to the clipboard. Both SHALL be produced by the same assembly as the requests.

#### Scenario: Disclosure
- **WHEN** an analyst opens the Assistant tab of a deployment configured with the model `gemini-3.8-flash` at `generativelanguage.googleapis.com`
- **THEN** the tab names that model and that host and says that the schema, the choice lists, the saved script 1, the draft and the errors of the analyst's last test are sent, without rows

#### Scenario: Same text as the request
- **WHEN** the analyst opens "Show what will be sent" and then sends a request without changing anything
- **THEN** the context of the request is the text that was shown

#### Scenario: Before typing
- **WHEN** the request box is empty and the analyst opens "Show what will be sent"
- **THEN** the view shows the context that would be sent, without a request

#### Scenario: Copy context
- **WHEN** the analyst chooses "Copy context"
- **THEN** the clipboard holds the text "Show what will be sent" displays, and the page says it was copied

### Requirement: Context size
The `api` SHALL keep each request under the configured context size by leaving out, in a fixed order, earlier turns of the conversation, choice lists and tables the draft and the request do not mention, long labels, the end of script 1 and extra templates, and SHALL never shorten the editor's text. A request whose editor text is larger than 64 KB, or that still does not fit, SHALL be refused with a message.

#### Scenario: Large form
- **WHEN** a form with 400 questions and a 1,100-label municipality list exceeds the context size
- **THEN** the request is sent without the lists and tables the draft does not mention, and "Show what will be sent" shows what was left out

#### Scenario: Large draft
- **WHEN** the editor's text is 70 KB
- **THEN** the request is refused with a message that the script is too large for the assistant

#### Scenario: Still too large
- **WHEN** a 60 KB editor text and the rules exceed the configured context size after everything that can be left out was left out
- **THEN** the request is refused with the same message

### Requirement: Language of answers and code
The assistant SHALL explain in the language of the analyst's request and SHALL write code, identifiers, comments and `ctx.log` messages in English. It SHALL use table and column names exactly as the schema gives them and SHALL compare select answers with their choice labels as the form gives them.

#### Scenario: Request in Spanish
- **WHEN** an analyst writes "tasa de hogares con acueducto por municipio"
- **THEN** the explanation is in Spanish, the code's identifiers and comments are in English, and the code reads the column `municipio` by that name

### Requirement: Waiting for an answer
While an answer is being written, the tab SHALL show that it is writing with the elapsed seconds and a Cancel action. Cancelling SHALL end the request to the provider. A user SHALL have at most one request in progress; another one SHALL be refused until the first ends.

#### Scenario: Cancel
- **WHEN** the analyst cancels a request after 20 seconds
- **THEN** the tab stops waiting, puts the analyst's message back in the request box, and the `api` ends its request to the provider

#### Scenario: Second request at the same time
- **WHEN** an analyst sends a request from one browser tab while another of their requests is in progress
- **THEN** the second is refused with a message that a request is already in progress

### Requirement: A request belongs to its script
A request SHALL stay bound to the script it was sent from: its answer SHALL go into that script's conversation and its proposal SHALL be offered only while that script is selected. Moving to another side tab or another script SHALL NOT cancel it; leaving the Scripts page SHALL cancel it.

#### Scenario: Another side tab while writing
- **WHEN** the analyst opens the Tables tab while an answer is being written and returns to the Assistant tab
- **THEN** the tab shows the waiting state, or the answer and its proposal if it arrived meanwhile

#### Scenario: Typed request kept across side tabs
- **WHEN** the analyst types a request in the This cell scope, opens the Tables tab to look up a column and returns
- **THEN** the request box still holds the text and the scope is still This cell

#### Scenario: Failure while away
- **WHEN** a request for indicator A times out while the analyst looks at the Tables tab or at indicator B
- **THEN** A's Assistant tab says that the provider did not answer in time and A's request box holds the message again, and B's box is unchanged

#### Scenario: Another script while writing
- **WHEN** the analyst sends a request for indicator A and selects indicator B before the answer arrives
- **THEN** the answer goes into A's conversation, B's tab says a request for A is in progress, and A's proposal is offered when A is selected again

#### Scenario: Leaving the Scripts page while writing
- **WHEN** the analyst goes to the dashboards while an answer is being written
- **THEN** the request is cancelled and recorded as cancelled

### Requirement: Daily budget per user
The `api` SHALL count the tokens each user's requests used per day in UTC, across projects, including the reasoning tokens a provider reports only in its total, and SHALL refuse a request once that count has reached the configured daily budget, saying when the budget renews. The default budget SHALL be 500,000 tokens.

#### Scenario: Budget reached
- **WHEN** a user's requests today add up to 500,000 tokens and they send another one
- **THEN** the request is refused with 429 and a message that says when the budget renews

#### Scenario: Reasoning tokens
- **WHEN** the provider reports 758 prompt tokens, 102 completion tokens and a total of 1,725
- **THEN** the request counts 1,725 tokens toward the budget

#### Scenario: Refused request costs nothing
- **WHEN** the provider answers 401 or 429 to a request
- **THEN** that request adds nothing to the user's count for the day

#### Scenario: Provider reports no usage
- **WHEN** the provider's answer does not report the tokens used
- **THEN** the request still succeeds and its tokens are estimated from the size of what was sent and received

### Requirement: Usage records
The `api` SHALL record each request with its user, project, script kind and name, action, model, the size of each part of the context, whether a test was attached, the tokens used, the duration, the outcome and, when the analyst applies the proposal, how it was applied. Records SHALL NOT hold the messages, the editor's text, the context or the answer.

#### Scenario: Request recorded
- **WHEN** an analyst's fix request succeeds and they apply the proposal with Replace script
- **THEN** one record holds the action `fix`, the model, the tokens, the duration and the outcome "replaced script", and no text of the request or the answer

#### Scenario: Failed request recorded
- **WHEN** a request fails because the provider is unavailable
- **THEN** a record holds the failure and its code

### Requirement: Provider failures
When the provider cannot be reached, times out, refuses the key, the model or the account's credit, limits the rate, fails while writing or answers with something other than text, the tab SHALL say so in its own words, put the analyst's message back in the request box, and never show the provider's error body. When the provider limits the rate, the message SHALL say when to retry if the provider said so.

#### Scenario: Wrong key
- **WHEN** the provider answers 401 to a request
- **THEN** the tab says that the assistant is misconfigured and to contact the administrator, and the `api` log records the provider's status without the key

#### Scenario: Out of credit
- **WHEN** the provider answers 402
- **THEN** the tab says that the assistant is misconfigured and to contact the administrator, and the `api` log records the status

#### Scenario: Failed while writing
- **WHEN** the provider answers 200 with partial text and a finish reason of `error`
- **THEN** the tab says the provider failed and offers no proposal

#### Scenario: Timeout
- **WHEN** the provider does not answer within the configured timeout
- **THEN** the tab says that the provider did not answer in time and keeps the message

#### Scenario: Rate limit
- **WHEN** the provider answers 429 with a `Retry-After` of 30 seconds
- **THEN** the tab says to try again in 30 seconds

### Requirement: Conversation kept in the browser session
The conversation of each script SHALL be kept only in the browser tab's session, per user, project and script, and SHALL be cleared on sign-out. The server SHALL NOT store conversations. Reloading the page SHALL keep the conversation and its last proposal; closing the browser tab or signing out SHALL end it. A "New conversation" action SHALL empty it.

#### Scenario: Reload
- **WHEN** an analyst reloads the Scripts page in the same browser tab
- **THEN** the Assistant tab shows the conversation of the selected script again, with its last proposal still offered

#### Scenario: Sign-out
- **WHEN** the analyst signs out and signs in again
- **THEN** the Assistant tab starts with an empty conversation

#### Scenario: Another script
- **WHEN** the analyst selects another indicator
- **THEN** the tab shows that indicator's conversation, not the previous one's

#### Scenario: New conversation
- **WHEN** the analyst chooses "New conversation"
- **THEN** the thread is empty and the next request holds no earlier turn

#### Scenario: New conversation while writing
- **WHEN** an answer for the selected script is being written
- **THEN** "New conversation" is not available until it arrives or is cancelled

### Requirement: Turns of the conversation
A request that fails or is cancelled SHALL add nothing to the conversation. When the conversation is longer than a request may carry, the oldest pairs of a request and its answer SHALL be left out of the request and kept in the thread, so a request always starts with one of the analyst's turns.

#### Scenario: Failed request
- **WHEN** a request times out and the analyst sends the same message again
- **THEN** the thread holds that message once, followed by its answer

#### Scenario: Long conversation
- **WHEN** a script's conversation has 50 turns
- **THEN** the request holds the newest turns that fit, starting with a turn of the analyst, and is accepted, and the thread still shows all 50
