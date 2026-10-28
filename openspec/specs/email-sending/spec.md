# email-sending Specification

## Purpose

Lets Tamiza send email itself through an SMTP server that the operator configures. This capability covers turning sending on, the connection and its security, the sender, and failures. It also protects the server's credentials.

## Requirements

### Requirement: Email available only when configured
Email sending SHALL exist only when the operator sets the SMTP server's host and the sender address in the deployment's environment. Without them, Tamiza SHALL open no SMTP connection, and every feature that sends email SHALL be unavailable. The system configuration the UI reads SHALL say whether email sending is enabled and nothing else about it.

#### Scenario: Not configured
- **WHEN** a deployment sets no SMTP variables
- **THEN** the system configuration reports email as disabled, and no SMTP connection is ever opened

#### Scenario: Configured
- **WHEN** the operator sets the SMTP host and the sender address and restarts the `api`
- **THEN** the system configuration reports email as enabled, without the host, the port, the user name, the password or the sender address

#### Scenario: Partly configured
- **WHEN** the operator sets the SMTP host but not the sender address
- **THEN** email is disabled as if nothing were configured, and the `api` log says once which variable is missing

### Requirement: Invalid email settings stop the API
The `api` SHALL refuse to start when an SMTP setting it reads is invalid: the port is outside 1 to 65535, the connection security is not one of the accepted values, the sender is not an email address, the timeout is outside 5 to 300 seconds, or only one of the user name and the password is set. The log SHALL name the variable and say which values are accepted.

#### Scenario: Port out of range
- **WHEN** the operator sets the SMTP port to `70000`
- **THEN** the `api` does not start, and its log names the port variable and the range 1 to 65535

#### Scenario: User name without password
- **WHEN** the operator sets the SMTP user name and leaves the password empty
- **THEN** the `api` does not start, and its log says that the user name and the password must be set together

#### Scenario: Sender without a domain
- **WHEN** the operator sets the sender address to `tamiza`
- **THEN** the `api` does not start, and its log says the sender must be an email address

### Requirement: Connection security
The connection security SHALL be one of `starttls`, `tls` or `none`, with `starttls` as the default. The default port SHALL be 587 for `starttls`, 465 for `tls` and 25 for `none`. With `starttls`, a server that does not offer STARTTLS SHALL fail the send rather than continue unencrypted. With `starttls` or `tls`, the server's certificate SHALL be validated. `none` is meant for a relay inside the operator's network or a test server.

#### Scenario: Default security and port
- **WHEN** the operator sets only the host, the sender, a user name and a password
- **THEN** Tamiza connects to port 587, upgrades the connection with STARTTLS and then authenticates

#### Scenario: Server without STARTTLS
- **WHEN** the security is `starttls` and the server does not offer STARTTLS
- **THEN** no message is sent and the send fails as "server unavailable"

#### Scenario: Untrusted certificate
- **WHEN** the security is `tls` and the server presents a certificate that does not match its host name
- **THEN** no message is sent and the send fails as "server unavailable"

### Requirement: Authentication
When a user name and a password are set, Tamiza SHALL authenticate with them before sending. When neither is set, it SHALL send without authenticating. The send SHALL fail as "authentication failed" in three cases: the server refuses the credentials, it offers no authentication while credentials are set, or it requires authentication while none are set.

#### Scenario: Authentication required
- **WHEN** no user name or password is set and the server answers that authentication is required
- **THEN** no message is sent and the send fails as "authentication failed"

#### Scenario: Credentials refused
- **WHEN** the server refuses the configured user name and password
- **THEN** no message is sent and the send fails as "authentication failed"

#### Scenario: Relay without authentication
- **WHEN** no user name or password is set and the server accepts unauthenticated mail from the `api`
- **THEN** the message is sent without authenticating

### Requirement: Sender and message headers
Every message SHALL come from the configured sender address, with the configured display name or "Tamiza" when none is set. It SHALL carry a date, a unique message ID and the header `Auto-Submitted: auto-generated`, so that automatic replies are not sent back. A feature MAY add a reply-to address.

#### Scenario: Display name
- **WHEN** the sender is `reports@example.org` and no display name is set
- **THEN** messages come from `Tamiza <reports@example.org>`

#### Scenario: Automatic message marked
- **WHEN** Tamiza sends any message
- **THEN** the message has the header `Auto-Submitted: auto-generated`

### Requirement: Send failures
A send SHALL fail with one of these outcomes:
- "server unavailable": no connection, no secure connection, the server refused the connection itself, or the server did not answer within the timeout;
- "authentication failed";
- "rejected": the server refused the sender, the message or any recipient. When a recipient was refused, the failure SHALL name it.

A failed send SHALL deliver to no one. Tamiza SHALL NOT process bounces that arrive later.

#### Scenario: Recipient refused
- **WHEN** the server refuses `nobody@example.org`, one of three recipients
- **THEN** none of the three receives the message, and the failure is "rejected" naming `nobody@example.org`

#### Scenario: Server down
- **WHEN** the SMTP server does not answer within the configured timeout
- **THEN** the send fails as "server unavailable" after that timeout

### Requirement: Send timeout
The timeout SHALL be 30 seconds unless the operator sets another. It SHALL bound connecting and signing in as a whole, and each exchange with the server while the message is sent, so that a large message on a slow link is not cut off while the server keeps answering.

#### Scenario: Large message on a slow link
- **WHEN** a 9 MB message takes 50 seconds to upload with the default timeout, and the server answers every exchange at once
- **THEN** the message is sent

### Requirement: Email secrets protected
The SMTP password SHALL never appear in logs, API answers or the system configuration. A failure recorded or shown to members SHALL hold its outcome and, for a refused recipient, that address. It SHALL NOT hold the server's reply text, the user name or the password.

#### Scenario: Failure log
- **WHEN** authentication fails
- **THEN** the `api` log names the SMTP host and the outcome, and does not contain the password

#### Scenario: Failure shown to members
- **WHEN** a member reads the last failure of a send that the server refused with a reply naming its internal relay
- **THEN** the failure reads "rejected" and does not include the server's reply
