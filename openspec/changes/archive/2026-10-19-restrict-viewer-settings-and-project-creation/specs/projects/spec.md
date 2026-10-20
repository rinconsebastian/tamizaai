# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
