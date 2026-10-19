# Spec Delta

## MODIFIED Requirements

### Requirement: External identity provider configured through environment variables
The system SHALL authenticate users with OpenID Connect against an existing identity provider that is not part of the deployment. Sign-in SHALL rely only on standard OIDC: discovery, the authorization code flow with PKCE, and signed JWT access tokens with the configured audience, `sub`, and the standard `name`, `email` and `email_verified` claims. The issuer URL, the UI client identifier and the audience SHALL be configured only through environment variables.

#### Scenario: Switching Keycloak instance
- **WHEN** the operator changes the OIDC variables in `.env` and restarts the deployment
- **THEN** the UI and the API use the new instance without rebuilding any image

#### Scenario: Another OIDC provider
- **WHEN** the operator points the OIDC variables at a compliant provider other than Keycloak and sets no Keycloak admin variables
- **THEN** users sign in and out, the API accepts their tokens and shows their profile, invitations by email work as without account emails, and the members section offers no email actions

#### Scenario: Access token without an email
- **WHEN** the provider's access token carries `sub` but no `email`
- **THEN** the user signs in, their local record has no email, and pending invitations are not matched to them
