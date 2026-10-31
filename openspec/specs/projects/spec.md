# projects Specification

## Purpose

Organize work in projects: each one is linked to exactly one Kobo form, holds the people allowed to work on it with their roles, and defines the sampling frame that field coverage is measured against.

## Requirements

### Requirement: Project creation linked to a Kobo form
A signed-in user who is `admin` of at least one project, or who is `superadmin`, SHALL be able to create a project by giving a name, a Kobo server, the form's asset UID and a Kobo API token. Only a membership the user holds at the time of the request SHALL count, not a pending invitation. A `superadmin` SHALL be able to create a project even when no project exists. Any other user SHALL get 403 with the code `project.create_not_allowed`, before the fields are checked and before any request to Kobo, and nothing SHALL be saved. Before saving, the system SHALL check that the token can read both the form definition and its submissions. On success the system SHALL store the token encrypted, generate a webhook secret, and make the creator the project's `admin`. Each project is linked to exactly one form; the server and asset UID cannot be changed after creation.

#### Scenario: Successful creation
- **WHEN** a user who is `admin` of another project creates a project with a reachable server, an existing asset UID and a token that can read the form and its submissions
- **THEN** the project is saved with the form's name, the creator is its `admin`, and the response includes the webhook URL and credentials to configure in Kobo

#### Scenario: Token without access
- **WHEN** a user who may create projects gives a token that Kobo rejects or that cannot read the form's submissions
- **THEN** creation fails with a message saying the token has no access to that form, and nothing is saved

#### Scenario: Form not found
- **WHEN** a user who may create projects gives an asset UID for which no form exists on the server
- **THEN** creation fails with a message saying the form was not found, and nothing is saved

#### Scenario: Server unreachable
- **WHEN** a user who may create projects gives a Kobo server that does not answer or whose TLS connection fails
- **THEN** creation fails with a message saying the server could not be reached, and nothing is saved

#### Scenario: Superadmin on a new installation
- **WHEN** a `superadmin` creates a project on an installation that has no project yet, with a reachable server, an existing asset UID and a token that can read the form and its submissions
- **THEN** the project is saved and the `superadmin` is its `admin`

#### Scenario: Not an admin of any project
- **WHEN** a user who is `analyst` of one project and `viewer` of another, and is not `superadmin`, asks to create a project
- **THEN** the API responds 403 with the code `project.create_not_allowed`, no request is sent to Kobo, and nothing is saved

#### Scenario: User without projects
- **WHEN** a signed-in user who belongs to no project and is not `superadmin` asks to create a project
- **THEN** the API responds 403 with the code `project.create_not_allowed`, and nothing is saved

#### Scenario: Refused before the fields are checked
- **WHEN** a user who may not create projects sends an empty name and a server URL that starts with `http://`
- **THEN** the API responds 403 with the code `project.create_not_allowed`, not with a field message or a message about the server address

#### Scenario: Pending admin invitation
- **WHEN** a user who belongs to no project signs in with an unverified email that has a pending invitation as `admin`, and asks to create a project
- **THEN** the API responds 403 with the code `project.create_not_allowed`, and nothing is saved

#### Scenario: Admin invitation accepted
- **WHEN** a person who belongs to no project and has a pending invitation as `admin` signs in with that email verified, and creates a project with a reachable server, an existing asset UID and a token that can read the form and its submissions
- **THEN** the project is saved and they are its `admin`

#### Scenario: Admin role lost
- **WHEN** an admin of the only project where a user who is not `superadmin` is `admin` changes that user's role to `analyst`, or removes them
- **THEN** the user's next request to create a project gets 403 with the code `project.create_not_allowed`

### Requirement: Kobo server address policy
The system SHALL offer `https://kf.kobotoolbox.org` and `https://eu.kobotoolbox.org` as presets and accept any other server URL that uses HTTPS and resolves only to public IP addresses. Addresses that are loopback, private, link-local, unique-local, multicast or unspecified SHALL be rejected, and so SHALL plain HTTP, unless the operator enables private networks in the deployment configuration. The check SHALL apply to every connection the system opens to a Kobo server, including after redirects and DNS changes.

#### Scenario: Plain HTTP URL
- **WHEN** a user who may create projects gives a server URL that starts with `http://` and private networks are not enabled
- **THEN** the request is rejected with a message that the server must use HTTPS

#### Scenario: Server on a private address
- **WHEN** a user who may create projects gives a server whose host name resolves to a loopback or private address, and private networks are not enabled
- **THEN** no request is sent to it and the user gets a message that the address is not allowed

#### Scenario: Private networks enabled by the operator
- **WHEN** the operator has enabled private networks and a user who may create projects gives the URL of a Kobo server on the local network
- **THEN** the connection is checked like any other server

### Requirement: Confidential Kobo token
The Kobo API token SHALL never appear in any API response or log. A project `admin` SHALL be able to replace it; the new token SHALL pass the same access check against the project's form before it replaces the old one.

#### Scenario: Project details hide the token
- **WHEN** any member requests the project's details
- **THEN** the response contains no token, encrypted or not

#### Scenario: Token replaced
- **WHEN** an admin submits a new token that can read the project's form and submissions
- **THEN** the new token replaces the old one

#### Scenario: Invalid replacement token
- **WHEN** an admin submits a token that fails the access check
- **THEN** the request fails with the reason and the previous token stays in use

### Requirement: Project list and details
The system SHALL list the projects a user belongs to, with the user's role in each; superadmins see every project. Project details SHALL include, for every member, the name, form name, the requesting user's role, when the project was created, and the project's dashboards in their order, each with its identifier and name. For an `admin` or `analyst` they SHALL also include the Kobo server, asset UID, the result of the last form field check and when it ran, the enumerator question, how many form version definitions are unavailable and whether the stored token can be read. A `viewer`'s details SHALL leave these values out, giving each as `null`. A project `admin` SHALL be able to rename the project.

#### Scenario: List shows only own projects
- **WHEN** a user who belongs to two of five projects requests the project list
- **THEN** the list contains those two projects with the user's role in each

#### Scenario: Rename by a non-admin
- **WHEN** an `analyst` tries to rename the project
- **THEN** the API responds 403 and the name is unchanged

#### Scenario: Details list the dashboards
- **WHEN** a `viewer` requests the details of a project whose dashboards are "Health" and then "Field"
- **THEN** the details list "Health" and then "Field", each with its identifier

#### Scenario: Viewer details leave out the configuration
- **WHEN** a `viewer` requests the details of a project linked to the asset UID `aB3dE5` on `https://kf.kobotoolbox.org`
- **THEN** the details give the name, the form name, the role `viewer`, the time zone and the dashboards, and the Kobo server, the asset UID, the field check and when it ran, the enumerator question, the count of unavailable form versions and whether the token can be read are `null`

#### Scenario: Analyst details include the configuration
- **WHEN** an `analyst` requests the details of a project linked to the asset UID `aB3dE5` on `https://kf.kobotoolbox.org`
- **THEN** the details include that server, that asset UID, and the result of the last form field check with when it ran

### Requirement: Form field check for the performance dashboard
When a project is created, whenever an `admin` or `analyst` re-checks the Kobo connection, and on every synchronization of the project's submissions, the system SHALL read the definition of the form's deployed version, keep the list of its fields (each question's name, path, type, label in the form's default language, and whether it is inside a repeat group) and its choice lists (each choice's list, name and label in the form's default language), and check whether the form contains `start`, `end` and an enumerator identifier. The enumerator identifier is the form question the admin selected, while the deployed form still has it, or else the `username` metadata field. The system SHALL report which performance-dashboard metrics will be unavailable when fields are missing, and SHALL report sampling frame coverage as unavailable while the project has no sampling frame or a dimension of its frame maps to a field the form no longer has. An `admin` or `analyst` SHALL be able to read the kept list of fields; the API SHALL answer a `viewer` 403.

#### Scenario: Complete form
- **WHEN** the form has `start`, `end` and `username`, and the project has a sampling frame whose fields are all in the form
- **THEN** the check reports every performance metric as available

#### Scenario: Missing start or end
- **WHEN** the form lacks `start` or `end`
- **THEN** the check reports the completion-time metric as unavailable and names the missing field

#### Scenario: No enumerator identifier
- **WHEN** the form lacks `username` and the admin has not selected an enumerator question
- **THEN** the check reports the per-enumerator detail as unavailable and suggests selecting a question

#### Scenario: Admin selects the enumerator question
- **WHEN** an admin selects a form question as the enumerator identifier
- **THEN** the check reports the per-enumerator detail as available

#### Scenario: Selected question over username
- **WHEN** the form has `username` and an admin selects the question `enumerator_code`
- **THEN** the selection is saved and `enumerator_code` is the enumerator identifier

#### Scenario: Selected question removed by a redeploy
- **WHEN** an admin selected the question `enumerator_code` and the form is redeployed without it
- **THEN** the check uses the `username` field if the form has it, and otherwise reports the per-enumerator detail as unavailable

#### Scenario: No sampling frame
- **WHEN** the project has no sampling frame
- **THEN** the check reports sampling frame coverage as unavailable and suggests defining a frame

#### Scenario: Frame field removed from the form
- **WHEN** a dimension of the sampling frame maps to `municipality` and the form is redeployed without that question
- **THEN** the check reports sampling frame coverage as unavailable and names `municipality`

#### Scenario: Re-check by a viewer
- **WHEN** a `viewer` tries to re-check the Kobo connection
- **THEN** the API responds 403

#### Scenario: Analyst reads the form fields
- **WHEN** an `analyst` reads the form fields of a project whose form has the question `respondent/age` labeled `Age of the respondent`
- **THEN** the response includes the question `age` with the path `respondent/age` and that label

#### Scenario: Viewer reads the form fields
- **WHEN** a `viewer` reads the form fields of a project
- **THEN** the API responds 403 and lists no field

#### Scenario: Form redeployed with new fields
- **WHEN** the form is redeployed in Kobo with `start` and `end` added, and a synchronization of the project then runs
- **THEN** the field check reports the completion-time metric as available without anyone re-checking the connection

#### Scenario: Choice lists kept
- **WHEN** a project is created for a form whose question `municipality` uses the choice list `municipalities` with the choice `cali` labeled `Cali`
- **THEN** the project's form snapshot records that the question uses `municipalities` and that `cali` is labeled `Cali` in it

#### Scenario: Question labels kept
- **WHEN** a project is created for a form whose question `respondent/age` is labeled `Age of the respondent` in the deployed version
- **THEN** the project's form snapshot records the question `age` with the path `respondent/age` and the label `Age of the respondent`

#### Scenario: Questions in repeat groups
- **WHEN** a project is created for a form whose question `member_age` sits inside the repeat group `members`, and whose question `municipality` sits in a plain group
- **THEN** the project's form snapshot marks `member_age` as inside a repeat group and `municipality` as not

#### Scenario: Undeployed edits
- **WHEN** the form's deployed version has 20 questions and someone adds a question `income` in Kobo's form builder without redeploying, and a synchronization runs
- **THEN** the project's form snapshot keeps the 20 deployed questions and does not include `income`

#### Scenario: Deployed version unreadable
- **WHEN** the form has undeployed edits and Kobo cannot return the definition of the deployed version, while the form itself can be read
- **THEN** the creation, re-check or synchronization does not fail, and the snapshot is taken from the form's latest content

#### Scenario: Label missing in the default language
- **WHEN** the form's languages are Spanish (default) and English, and the choice `3` and the question `notes` have an English label but no Spanish one
- **THEN** the project's form snapshot records the choice `3` and the question `notes` without a label

### Requirement: Webhook configuration
Each project SHALL have a webhook secret, generated by the system and stored encrypted. A project `admin` SHALL be able to see the webhook URL and the credentials to enter in Kobo's REST Services, and to regenerate the secret; regenerating SHALL invalidate the previous secret. Other members SHALL NOT see the secret.

#### Scenario: Admin views the webhook settings
- **WHEN** an admin opens the project's webhook settings
- **THEN** they see the webhook URL, built from the installation's public URL, and the credentials to configure in Kobo

#### Scenario: Secret regenerated
- **WHEN** an admin regenerates the webhook secret
- **THEN** a new secret is shown and the previous one is no longer valid

#### Scenario: Analyst requests the secret
- **WHEN** an `analyst` requests the webhook settings
- **THEN** the API responds 403

### Requirement: Member invitations by email
A project `admin` SHALL be able to add a person by email address and role. If a user who has signed in with that email verified already exists, the person SHALL become a member at once. Otherwise the system SHALL keep a pending invitation, and SHALL turn it into a membership when a user signs in whose verified email matches it. Email matching SHALL ignore letter case. Pending invitations SHALL NOT match an unverified email. Without account emails (see `account-emails`), the system sends no email and the admin informs the person; with them, the account email rules apply on top of these.

#### Scenario: Invitee already uses Tamiza
- **WHEN** an admin invites the verified email of a user who has signed in before
- **THEN** that user becomes a member with the chosen role immediately

#### Scenario: Invitee has not signed in yet
- **WHEN** an admin invites an email that no user with a verified email has
- **THEN** a pending invitation with the chosen role is listed for the project

#### Scenario: Invitee signs in
- **WHEN** a person with a pending invitation signs in with that email verified in Keycloak
- **THEN** they become a member of the project with the invited role, and the invitation is no longer pending

#### Scenario: Unverified email
- **WHEN** a person whose email matches a pending invitation signs in with the email unverified
- **THEN** no membership is created and the invitation stays pending

#### Scenario: Invitation revoked
- **WHEN** an admin revokes a pending invitation before the person signs in
- **THEN** a later sign-in with that email does not create a membership

#### Scenario: Already a member
- **WHEN** an admin invites the email of a current member
- **THEN** the request fails with a message that the person is already a member

#### Scenario: No account emails configured
- **WHEN** an admin invites an email that no user with a verified email has, on a deployment without account emails
- **THEN** the pending invitation is saved, no email is sent, and the admin is told to let the person know

### Requirement: Member management
A project `admin` SHALL be able to change members' roles and remove members. An `admin` or `analyst` SHALL be able to see the project's member list, and the API SHALL answer a `viewer` 403; only admins SHALL see pending invitations. A user can belong to several projects with different roles. A project SHALL always keep at least one `admin`.

#### Scenario: Role changed
- **WHEN** an admin changes a member's role from `viewer` to `analyst`
- **THEN** the member has `analyst` permissions on their next request

#### Scenario: Member removed
- **WHEN** an admin removes a member
- **THEN** that user gets 404 for the project afterwards

#### Scenario: Last admin
- **WHEN** an admin tries to remove themselves or change their own role while they are the project's only admin
- **THEN** the request fails with a message that the project needs at least one admin

#### Scenario: Analyst reads the member list
- **WHEN** an `analyst` requests the member list of a project with two members and one pending invitation
- **THEN** the response lists both members with their roles and shows no pending invitation

#### Scenario: Viewer reads the member list
- **WHEN** a `viewer` requests the project's member list
- **THEN** the API responds 403 and lists no member

#### Scenario: Own role lowered to analyst
- **WHEN** an admin of a project that has another admin changes their own role to `analyst` in the Members section
- **THEN** the member list stays open, now without pending invitations, and no error message is shown

#### Scenario: Own removal
- **WHEN** a user who is not `superadmin`, and who is admin of a project that has another admin, removes themselves in the Members section
- **THEN** the project list opens without an error message, and that project is not in it

### Requirement: Sampling frame
Each project SHALL be able to have a sampling frame: a list of dimensions, each with a unique name and the form field it maps to, and a target number of surveys for each combination of dimension values. An `admin` or `analyst` SHALL be able to save, replace and read the frame; the API SHALL answer a `viewer` 403. Coverage on the performance dashboard stays visible to every member (see `dashboard-performance`). The system SHALL reject a frame whose dimensions map to fields that are not in the form, whose targets are not whole numbers of zero or more, whose rows lack a value for a dimension, or that repeats a combination.

#### Scenario: Frame saved
- **WHEN** an analyst saves dimensions mapped to existing form fields and one target per combination
- **THEN** the frame is stored and every `admin` and `analyst` of the project sees the same dimensions and targets

#### Scenario: Unknown field
- **WHEN** a dimension maps to a field the form does not have
- **THEN** the frame is rejected with a message naming the dimension and the field

#### Scenario: Repeated combination
- **WHEN** two rows have the same values for every dimension
- **THEN** the frame is rejected with a message naming the repeated rows

#### Scenario: Viewer saves
- **WHEN** a `viewer` tries to save the sampling frame
- **THEN** the API responds 403 and the frame is unchanged

#### Scenario: Viewer reads the frame
- **WHEN** a `viewer` requests the project's sampling frame
- **THEN** the API responds 403 and returns no dimension or target

### Requirement: Sampling frame import from CSV or XLSX
An `admin` or `analyst` SHALL be able to import a sampling frame from a CSV or XLSX file (its first sheet) whose first non-empty row names the dimensions plus a `target` column, with one combination per row. Importing SHALL only produce a preview: the dimensions found, the targets, any errors and what was left out. Nothing SHALL change until the user maps each dimension to a form field and saves. Files over 5 MB or 10,000 data rows SHALL be rejected; blank lines SHALL NOT count as data rows.

#### Scenario: Valid file
- **WHEN** an analyst imports a CSV or XLSX file with columns `municipality`, `sex`, `age_range` and `target`
- **THEN** the preview lists the three dimensions and every row's target, and the saved frame is unchanged

#### Scenario: Missing target column
- **WHEN** the file has no `target` column
- **THEN** the preview reports that the `target` column is required

#### Scenario: Invalid row
- **WHEN** row 7 has a target that is not empty and is not a whole number of zero or more, such as `1.5` or `-3`
- **THEN** the preview reports the error for row 7

#### Scenario: File too large
- **WHEN** the file exceeds 5 MB or 10,000 data rows
- **THEN** the import is rejected with a message stating the limit

#### Scenario: Blank lines between rows
- **WHEN** a file has 10,000 data rows and five blank lines among them
- **THEN** the preview holds all 10,000 rows and none is dropped

#### Scenario: Row numbers after blank lines
- **WHEN** a CSV file has an empty line 3 and line 7 has the target `1.5`
- **THEN** the preview reports the error for row 7, the line number a spreadsheet program or text editor shows

### Requirement: Project time zone
Each project SHALL have a time zone, `UTC` until an admin sets another. A project `admin` SHALL be able to set it to any time zone name PostgreSQL knows (such as `America/Bogota`); the system SHALL reject any other value. Project details SHALL include it for every member.

#### Scenario: Admin sets the time zone
- **WHEN** an admin sets the project's time zone to `America/Bogota`
- **THEN** the project details show `America/Bogota` to every member

#### Scenario: Unknown time zone
- **WHEN** an admin sets the time zone to `Mars/Olympus`
- **THEN** the API rejects it with a message naming the value, and the time zone is unchanged

#### Scenario: Time zone by a non-admin
- **WHEN** an `analyst` tries to set the project's time zone
- **THEN** the API responds 403 and the time zone is unchanged

### Requirement: Sampling frame import leaves out notes and rows without a target
When a sampling frame is imported, a column whose name starts with `#` SHALL be a note: it SHALL NOT become a dimension and SHALL be listed as ignored. Values under a column without a name SHALL be listed as ignored too. A row whose `target` cell is empty SHALL be left out of the preview without an error, SHALL NOT count as a repeated combination, and SHALL be listed by its row number. A target of `0` SHALL be kept.

#### Scenario: Note column
- **WHEN** a file has the columns `municipio`, `# municipio label`, `sexo` and `target`
- **THEN** the dimensions are `municipio` and `sexo`, and `# municipio label` is listed as an ignored note column

#### Scenario: Values under a column without a name
- **WHEN** column E has no name in the header row but holds values
- **THEN** column E is not a dimension and is listed as ignored because it has no name

#### Scenario: Rows without a target
- **WHEN** rows 4 and 9 have an empty `target` cell
- **THEN** neither row is in the preview, no error is reported for them, and rows 4 and 9 are listed as left out

#### Scenario: Target of zero
- **WHEN** row 5 has the target `0`
- **THEN** row 5 is in the preview with the target 0

#### Scenario: Left-out row repeats a combination
- **WHEN** row 4 has an empty target and row 12 has the same values with the target 30
- **THEN** row 12 is in the preview and no repeated combination is reported

#### Scenario: Every row without a target
- **WHEN** the file has a `target` column and no row has a target
- **THEN** the preview lists the dimensions, holds no rows, lists every row as left out and reports no error

### Requirement: Sampling frame import mapping preselection
After an import, each dimension SHALL be preselected to a form question, trying in order, ignoring case: the question the saved frame maps a dimension of the same name to, while the form has it; a question whose path is the column name; the only question with that name; the only question with that label. When nothing matches, or the saved frame's question differs from a path or name match, no question SHALL be preselected. The user SHALL be able to change every preselection.

#### Scenario: Re-imported frame keeps its mapping
- **WHEN** the saved frame maps the dimension `municipality` to `geo/municipio` and an analyst imports a file with a column `Municipality`
- **THEN** the column is preselected to `geo/municipio`

#### Scenario: Saved question no longer in the form
- **WHEN** the saved frame maps the dimension `municipality` to `geo/municipio`, the form no longer has that question, and the only question named `municipality` is `lugar/municipality`
- **THEN** the column `Municipality` is preselected to `lugar/municipality`

#### Scenario: Column named after a path
- **WHEN** a file has a column `hogar/sexo` and the form has a question with that path
- **THEN** that question is preselected

#### Scenario: Name shared by two questions
- **WHEN** a file has a column `edad`, the saved frame has no such dimension and two questions in different groups are named `edad`
- **THEN** no question is preselected for that column and the user chooses one

#### Scenario: Saved mapping disagrees with the form
- **WHEN** the saved frame maps the dimension `sexo` to `jefe/sexo_jefe` and an analyst imports a file with a column `sexo`, which is also the name of the top-level question `sexo`
- **THEN** no question is preselected for that column, and both `jefe/sexo_jefe` and `sexo` are named as candidates

### Requirement: Form choices
An `admin` or `analyst` SHALL be able to read the choice lists of the project's current form snapshot; the API SHALL answer a `viewer` 403. Each choice SHALL have its list name, its code and the label the snapshot stores for it, if any, in form order.

#### Scenario: Choices listed
- **WHEN** an analyst reads the form choices of a project whose form lists Medellín with the code `05001` in the list `municipalities`
- **THEN** the response includes the choice with list `municipalities`, code `05001` and label `Medellín`

#### Scenario: Viewer reads the choices
- **WHEN** a `viewer` reads the form choices of a project
- **THEN** the API responds 403 and lists no choice

#### Scenario: Not a member
- **WHEN** a user who is not a member reads a project's form choices
- **THEN** the API responds 404

### Requirement: Sampling frame import template
An `admin` or `analyst` SHALL be able to download an XLSX template for 1 to 10 chosen questions. Its first sheet SHALL have a column per question, named after it, then `target`. It SHALL have a row per combination of the chosen values, with codes as text, an empty `target` and a `#` label column after each question with choices. Further sheets SHALL explain the rules and list codes with labels. Once its planned rows have targets and values, it SHALL import with no errors.

#### Scenario: Template for chosen choices
- **WHEN** an analyst requests a template for `municipio` with the choices `05001` (Medellín) and `05088` (Bello), and for `sexo` with every choice (`f`, `m`)
- **THEN** the first sheet has the columns `municipio`, `# municipio label`, `sexo`, `# sexo label` and `target`, and four rows with every `target` empty. `05001` is stored as text and reads `05001`. The guide and choices sheets follow the first sheet

#### Scenario: Question without choices and typed values
- **WHEN** the request includes the integer question `estrato` with the values `1`, `2` and `3`
- **THEN** the template has an `estrato` column without a label column, and each combination of the other questions appears once per value

#### Scenario: Question without choices and no values
- **WHEN** the request includes the text question `barrio` without values, next to `sexo`
- **THEN** the `barrio` column is empty in every row and does not multiply the rows, and importing the file reports "Enter a value for every dimension." for each row with a target until `barrio` is filled in

#### Scenario: Template without rows
- **WHEN** the request includes only the text question `barrio` without values
- **THEN** the first sheet holds only its column names, and importing it gives the dimension `barrio`, no rows and no error

#### Scenario: Name shared by two questions
- **WHEN** a chosen question shares its name with another field of the form, a note or metadata field included
- **THEN** its column is named after the question's path

#### Scenario: Long choice list
- **WHEN** an analyst requests a template for 50 of the 40,000 choices of `vereda` and the full list would make the file larger than the import limit
- **THEN** the choices sheet lists those 50 codes, the guide says the form has more, and the file is under 5 MB

#### Scenario: Filled template imports
- **WHEN** an analyst fills the target of some rows of a downloaded template whose dimension columns all hold values, and imports it
- **THEN** the preview reports no errors, lists the rows without a target as left out and the label columns as ignored, and preselects every column to the question it was made from, unless the saved frame maps a dimension of that name to another question

#### Scenario: Viewer asks for a template
- **WHEN** a `viewer` requests a template
- **THEN** the API responds 403 and no file is prepared

### Requirement: Sampling frame template request rules
The system SHALL refuse a template request, and prepare no file, when it chooses no question or more than 10, the same question twice, a question the form lacks, a multiple-choice or repeat-group question, a top-level question named `target`, questions whose columns would be named alike ignoring case, an empty or unknown choice, a typed value over 500 characters, more than 10,000 rows, or a file over the import limit. The message SHALL name the problem.

#### Scenario: Too many combinations
- **WHEN** an analyst requests every choice of a municipality question with 1,100 choices, of `sexo` with 2 and of `rango_edad` with 5
- **THEN** the request is refused with a message stating 11,000 rows and the limit of 10,000

#### Scenario: Multiple-choice question
- **WHEN** the request includes a multiple-choice question
- **THEN** the request is refused with a message that coverage cannot count that question

#### Scenario: Question named target
- **WHEN** the request includes a top-level question named `Target`
- **THEN** the request is refused with a message that its name is reserved for the target column

#### Scenario: Unknown choice
- **WHEN** the request includes the value `zz` for `municipio`, whose list has no such code
- **THEN** the request is refused with a message naming `zz` and `municipio`

### Requirement: Sampling frame download
An `admin` or `analyst` SHALL be able to download the saved sampling frame as an XLSX workbook in the template's layout, with the saved targets. Columns SHALL be named after the saved dimensions, except that a name starting with `#` or equal to `target` SHALL be written as its question's path. Within the import limits, importing that file again SHALL give back the saved rows and targets. Downloading SHALL be refused with a message when the project has no frame. A `viewer` SHALL get 403, whether or not the project has a frame, and no file SHALL be prepared.

#### Scenario: Analyst downloads the frame
- **WHEN** an analyst downloads the sampling frame of a project that has one
- **THEN** they get a workbook whose first sheet holds the saved dimensions, values and targets

#### Scenario: Viewer downloads the frame
- **WHEN** a `viewer` asks to download the sampling frame, whether or not the project has one
- **THEN** the API responds 403, no file is prepared and no download link is issued

#### Scenario: No frame yet
- **WHEN** an analyst downloads the sampling frame of a project without one
- **THEN** the API refuses with a message that the project has no sampling frame yet, and no file is prepared

#### Scenario: Downloaded frame imports again
- **WHEN** an analyst imports, without changing it, a downloaded frame whose questions are all still in the form
- **THEN** the preview holds the saved rows and targets with no errors, and every column is preselected to its saved question

#### Scenario: Dimension named like a note or the target
- **WHEN** the saved frame has the dimensions `#zona`, mapped to `geo/zona`, and `Target`, mapped to `hogar/target`, and an analyst downloads it and imports it again
- **THEN** those columns are named `geo/zona` and `hogar/target`, the preview holds the saved rows and targets with no errors, and both columns are preselected to their questions

#### Scenario: Frame without rows
- **WHEN** an analyst downloads a frame saved with dimensions and no targets
- **THEN** the first sheet holds the column names only, and importing it gives the dimensions, no rows and no error

### Requirement: Sampling frame start panel
On a project without a sampling frame, the Sampling frame page SHALL show an `admin` or `analyst` a start panel, not an empty editor. It SHALL explain the frame and offer three ways to begin: choose questions, import a file or type it in, with the file guide closed below. A `viewer` who opens the page's address SHALL be taken to the project's performance dashboard instead (see `web-ui`). Cancelling the editor without a frame SHALL return to the panel. When the frame cannot be loaded, the page SHALL show the error with a retry and nothing that edits the frame.

#### Scenario: Analyst without a frame
- **WHEN** an analyst opens the Sampling frame page of a project without a frame
- **THEN** the start panel is shown with the file guide closed below it, and no editor is open

#### Scenario: Viewer without a frame
- **WHEN** a viewer opens the address of the Sampling frame page of a project without a frame
- **THEN** the project's performance dashboard opens instead, and the sampling frame is not requested

#### Scenario: Typing the frame in
- **WHEN** an analyst chooses to type the frame in and then cancels the empty editor
- **THEN** the editor closes and the start panel is shown again

#### Scenario: Frame load fails
- **WHEN** an analyst opens the Sampling frame page and the frame cannot be loaded
- **THEN** the page shows the error and a Retry action, offers no start panel, editor, Import or Download, and shows the frame once Retry succeeds

### Requirement: Sampling frame question picker
The question picker SHALL list the form's questions, without metadata, and let an `admin` or `analyst` choose 1 to 10. A question with choices SHALL use every choice unless narrowed; one without SHALL accept typed values. Multiple-choice, repeat-group and top-level `target` questions SHALL be shown disabled with the reason. The picker SHALL show the row count, block above 10,000, and download the template or fill the editor, keeping targets only when the questions are unchanged.

#### Scenario: Row count
- **WHEN** an analyst chooses 5 municipalities, both choices of `sexo` and 3 age ranges
- **THEN** the picker shows 30 rows

#### Scenario: Too many rows
- **WHEN** the chosen values make 11,000 rows
- **THEN** the picker says that a sampling frame holds at most 10,000 rows, and neither the download nor the rows in the editor can be used

#### Scenario: Multiple-choice question
- **WHEN** the form has a multiple-choice question
- **THEN** the picker lists it disabled, with the reason that coverage cannot count it

#### Scenario: Select all with a search
- **WHEN** every choice of a 1,100-choice question is cleared, the user searches `Bello` and presses Select all
- **THEN** only the matching choices are ticked and the other choices stay cleared

#### Scenario: Rows created in the editor
- **WHEN** an analyst creates the rows in the editor from `municipio` and `sexo`
- **THEN** the editor holds one row per combination with an empty target. Its dimensions are named after the questions and mapped to them. Nothing is saved

#### Scenario: Existing targets kept
- **WHEN** an editor holding the row `05001`, `f` with the target 120 creates its rows again from the same questions with one more municipality
- **THEN** the row `05001`, `f` keeps the target 120 and the new rows have empty targets

#### Scenario: Question added
- **WHEN** an editor whose only dimension maps to `municipio` and holds the row `05001` with the target 120 creates its rows again from `municipio` and `sexo`
- **THEN** the page warns that the current targets will not be kept, and every new row has an empty target

#### Scenario: Dimension name kept
- **WHEN** an editor whose dimension `Municipality` maps to `municipio` creates its rows again from `municipio` with one more municipality
- **THEN** the dimension is still named `Municipality`

#### Scenario: Typed values kept
- **WHEN** an editor holding the integer question `estrato` with the values `1`, `2` and `3` creates its rows again with one more municipality
- **THEN** the picker starts with `1`, `2` and `3` as the values of `estrato`, and the rows that already existed keep their targets

#### Scenario: Values that are not choices
- **WHEN** an editor holding `5001` under `municipio` creates its rows again from questions
- **THEN** the confirmation before replacing says that 1 row has values that are not choices and will not be kept

#### Scenario: Rows without a target removed
- **WHEN** the editor holds 30 rows, 12 of them without a target
- **THEN** the editor offers to remove the 12 rows without a target, and after that 18 rows remain

### Requirement: Sampling frame file guide
The import dialog and the start panel SHALL show a guide to the file layout with an example table. It SHALL cover one column per dimension holding choice codes, an exact `target` column with whole numbers, one row per combination, `#` note columns, empty targets left out, codes kept as text, the accepted formats and the limits. In the import dialog the guide SHALL be open while the project has no saved frame, and SHALL link to the question picker.

#### Scenario: First import
- **WHEN** an analyst opens the import dialog on a project without a saved frame
- **THEN** the guide is open

#### Scenario: Import with a saved frame
- **WHEN** an analyst opens the import dialog on a project with a saved frame
- **THEN** the guide is closed and can be opened

#### Scenario: Template from the guide
- **WHEN** an analyst follows the guide's link to get a template
- **THEN** the import dialog closes and the question picker opens

### Requirement: Project creation offered only to those who may create
The project list SHALL offer "New project", and an empty list SHALL offer "Create a project", only to a user who may create projects, as the user's profile reports. The list SHALL read that right again each time it opens. An empty list SHALL ask any other user to get an invitation from a project admin. Opening `/projects/new` without the right SHALL lead to the project list. A creation the API refuses with `project.create_not_allowed` SHALL show the reason in the interface language.

#### Scenario: Admin sees New project
- **WHEN** a user who is `admin` of one project opens the project list
- **THEN** "New project" is offered

#### Scenario: Viewer and analyst only
- **WHEN** a user who is `viewer` of one project and `analyst` of another, is `admin` of none and is not `superadmin`, opens the project list
- **THEN** both projects are listed and "New project" is not shown

#### Scenario: Empty list without the right
- **WHEN** a signed-in user who belongs to no project and is not `superadmin` opens the project list
- **THEN** the list says there are no projects yet and to ask a project admin to invite them, and offers no "Create a project"

#### Scenario: Superadmin on a new installation
- **WHEN** a `superadmin` opens the project list of an installation that has no project
- **THEN** "Create a project" is offered

#### Scenario: Create page opened directly
- **WHEN** a user who may not create projects opens `/projects/new`
- **THEN** the project list opens instead and the creation form is not shown

#### Scenario: Right lost while the form is open
- **WHEN** an admin of the only project where a user who is not `superadmin` is `admin` changes that user's role to `analyst` while the user's creation form is open, and the user then submits it
- **THEN** the form says, in the interface language, that only project admins and superadmins can create projects, and nothing is saved

#### Scenario: Made admin of a project
- **WHEN** a user who is `viewer` of their only project is made its `admin`, and then opens the project list again
- **THEN** "New project" is offered

### Requirement: Access for AI clients
Each project SHALL have an access level for AI clients connected through the MCP server (see `mcp-server`): `off`, `aggregates` (*Summaries only*) or `rows` (*Summaries and rows*). It SHALL be `off` for every new project and for every project that existed before the setting. Only a project `admin` SHALL be able to change it, and the system SHALL reject `rows` while the deployment's maximum is `aggregates`, with a message saying that the operator decides it. Project details SHALL include the level and the deployment's maximum for admins and analysts, and leave them out for a `viewer`, giving each as `null`. When the MCP server is enabled, Settings > General SHALL show the level, with what each level lets a client see, as a choice for admins and as a value for analysts.

#### Scenario: New project
- **WHEN** a user creates a project in a deployment with the MCP server enabled
- **THEN** the project's access for AI clients is off

#### Scenario: Existing project upgraded
- **WHEN** a deployment is upgraded to this version
- **THEN** every existing project's access for AI clients is off

#### Scenario: Admin allows summaries
- **WHEN** an admin chooses *Summaries only* in Settings > General
- **THEN** the project details give `aggregates`, and the project's members can use it from their AI clients

#### Scenario: Rows above the deployment's maximum
- **WHEN** an admin asks for `rows` while the deployment's maximum is `aggregates`
- **THEN** the API rejects it with a message saying that the operator allows rows, and the level is unchanged

#### Scenario: Analyst tries to change it
- **WHEN** an `analyst` tries to set the project's access for AI clients
- **THEN** the API responds 403 and the level is unchanged

#### Scenario: Viewer details
- **WHEN** a `viewer` requests the project details
- **THEN** the access for AI clients and the deployment's maximum are `null`
