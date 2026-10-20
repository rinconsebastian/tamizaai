# Spec Delta

## MODIFIED Requirements

### Requirement: Sample download in the Scripts section
For analysts and admins, the Scripts section SHALL offer "Download sample" next to "Test on sample", with the same sample size field. While the download is prepared, it SHALL show its status and what it waits for, updated without reloading. When it is ready, it SHALL show the number of submissions, the tables with their rows, the script 1 outcome and the size, a button that downloads it, and a notice that the file holds the project's submissions.

#### Scenario: Prepare and download
- **WHEN** an analyst with a sample size of 300 clicks "Download sample" and the download becomes ready
- **THEN** the section shows the 300 submissions, the tables with their rows and the size, and clicking the download button saves the zip

#### Scenario: Viewer
- **WHEN** a `viewer` opens the address of the project's Scripts section
- **THEN** the project's performance dashboard opens instead, and no "Download sample" action is shown

#### Scenario: Script 1 failed
- **WHEN** the download is ready but the saved script 1 failed on the sample
- **THEN** the section shows a warning with script 1's version and error, and says that the zip holds only the flattened tables

#### Scenario: Download expired
- **WHEN** the analyst clicks the download button more than one hour after the download became ready
- **THEN** the section says that the download expired and offers to prepare a new one
