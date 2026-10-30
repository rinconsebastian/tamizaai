# Keycloak setup

Tamiza does not ship an identity provider. It signs users in against an existing Keycloak realm over OpenID Connect. This guide covers what the realm needs, the matching `.env` values, and a checklist to confirm sign-in works.

Written against Keycloak 26. Older versions use the same settings, but the admin console labels may differ.

## What Tamiza needs from the realm

| Item | Why |
|---|---|
| A **public client** for the UI (for example `tamiza-ui`), with the standard flow and PKCE (S256) | The browser signs in with the authorization code flow. The client has no secret because it runs in the browser. |
| An **audience mapper** on that client that adds the API audience (for example `tamiza-api`) to access tokens | The API rejects tokens that are not issued for its audience. |
| The default client scopes **`basic`**, **`profile`** and **`email`** | They put `sub`, `name`, `preferred_username`, `email` and `email_verified` in the access token. Tamiza identifies users by `sub` and refuses tokens without it. |
| **Verified emails** for superadmins | The `superadmin` role from `TAMIZA_SUPERADMIN_EMAILS` is granted only when `email_verified` is true. |

## Values for `.env`

| Variable | Value |
|---|---|
| `OIDC_AUTHORITY` | The realm URL as browsers reach it, for example `https://auth.example.org/realms/tamiza`. It must match the `iss` claim of the tokens exactly. |
| `OIDC_CLIENT_ID` | The public client's ID, for example `tamiza-ui`. |
| `OIDC_AUDIENCE` | The audience the mapper adds, for example `tamiza-api`. |
| `OIDC_METADATA_ADDRESS` | Leave empty unless the `api` container cannot reach `OIDC_AUTHORITY` (see [Keycloak on the same host](#keycloak-on-the-same-host)). |
| `OIDC_REQUIRE_HTTPS_METADATA` | `true`, except for a development realm served over plain HTTP. |
| `TAMIZA_SUPERADMIN_EMAILS` | Comma-separated emails of the people who administer the installation. Set your own before you first sign in: only admins of a project and superadmins can create projects, so on a new installation a superadmin creates the first one. |

## Option A: admin console

1. **Realm.** Use an existing realm or create one (for example `tamiza`).
2. **Client.** Go to *Clients → Create client*:
   - *Client type*: OpenID Connect. *Client ID*: `tamiza-ui`.
   - *Client authentication*: **Off** (public client). *Standard flow*: **On**. *Direct access grants*: **Off**.
   - *Valid redirect URIs*: `<TAMIZA_PUBLIC_URL>/*`, for example `https://tamiza.example.org/*`.
   - *Valid post logout redirect URIs*: `+` (same as the redirect URIs).
   - *Web origins*: `+` (the origins of the redirect URIs). The browser calls the token endpoint directly, so CORS must allow it.
   - In the client's *Advanced* tab, set *Proof Key for Code Exchange Code Challenge Method* to **S256**.
3. **Audience mapper.** Open *Clients → tamiza-ui → Client scopes → tamiza-ui-dedicated → Configure a new mapper → Audience*:
   - *Name*: `tamiza-api audience`.
   - *Included Custom Audience*: the value of `OIDC_AUDIENCE`, for example `tamiza-api`.
   - *Add to access token*: **On**. *Add to ID token*: **Off**.
4. **Client scopes.** In *Clients → tamiza-ui → Client scopes*, check that `basic`, `profile` and `email` are assigned as **Default**.
5. **Superadmins.** For each person listed in `TAMIZA_SUPERADMIN_EMAILS`, make sure *Users → (user) → Email verified* is on.

## Option B: `kcadm.sh`

The same configuration from the command line, using the admin CLI that ships with Keycloak. Set the variables first. For a remote server, run `kcadm.sh config credentials` against its URL with an admin account.

```sh
KEYCLOAK_URL=https://auth.example.org      # where kcadm reaches Keycloak
REALM=tamiza
CLIENT_ID=tamiza-ui                        # OIDC_CLIENT_ID
AUDIENCE=tamiza-api                        # OIDC_AUDIENCE
PUBLIC_URL=https://tamiza.example.org      # TAMIZA_PUBLIC_URL

kcadm.sh config credentials --server "$KEYCLOAK_URL" --realm master --user admin   # prompts for the password

# Skip if the realm already exists.
kcadm.sh create realms -s realm="$REALM" -s enabled=true

kcadm.sh create clients -r "$REALM" \
  -s clientId="$CLIENT_ID" \
  -s protocol=openid-connect \
  -s publicClient=true \
  -s standardFlowEnabled=true \
  -s directAccessGrantsEnabled=false \
  -s "redirectUris=[\"$PUBLIC_URL/*\"]" \
  -s 'webOrigins=["+"]' \
  -s 'attributes."pkce.code.challenge.method"=S256' \
  -s 'attributes."post.logout.redirect.uris"=+'

CLIENT_UUID=$(kcadm.sh get clients -r "$REALM" -q clientId="$CLIENT_ID" --fields id --format csv --noquotes)

kcadm.sh create "clients/$CLIENT_UUID/protocol-mappers/models" -r "$REALM" \
  -s name="$AUDIENCE audience" \
  -s protocol=openid-connect \
  -s protocolMapper=oidc-audience-mapper \
  -s "config.\"included.custom.audience\"=$AUDIENCE" \
  -s 'config."access.token.claim"=true' \
  -s 'config."id.token.claim"=false'
```

New clients get the realm's default client scopes (`basic`, `profile`, `email` and others), so no extra step is needed for the claims. To mark a superadmin's email as verified:

```sh
USER_ID=$(kcadm.sh get users -r "$REALM" -q email=admin@example.org --fields id --format csv --noquotes)
kcadm.sh update "users/$USER_ID" -r "$REALM" -s emailVerified=true
```

## Keycloak on the same host

When Keycloak runs on the Docker host, browsers reach it at a host URL (for example `http://localhost:8080`), but inside the `api` container `localhost` is the container itself. Keep `OIDC_AUTHORITY` as the browser URL, because it is the token issuer. Point `OIDC_METADATA_ADDRESS` at a URL the container can reach. Compose maps `host.docker.internal` to the Docker host for the `api` service.

A development realm on your machine, on Keycloak's default port 8080 (Tamiza's UI defaults to 8088, so they do not collide):

```sh
docker run -d --name keycloak-dev -p 127.0.0.1:8080:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=change-me \
  quay.io/keycloak/keycloak:26.8.0 start-dev
```

Configure it with Option A (admin console at `http://localhost:8080`) or Option B with `PUBLIC_URL=http://localhost:8088`. To run the Option B script inside the container, use `KEYCLOAK_URL=http://localhost:8080` (the container's own port):

```sh
docker exec -i keycloak-dev bash -c 'export PATH=$PATH:/opt/keycloak/bin; bash -s' < kcadm-setup.sh
```

Here `kcadm-setup.sh` holds the Option B commands, with `--password change-me` added to `kcadm.sh config credentials` so it does not prompt. If you also run the UI with `ng serve`, register its address too:

```sh
docker exec keycloak-dev /opt/keycloak/bin/kcadm.sh update "clients/$CLIENT_UUID" -r tamiza \
  -s 'redirectUris=["http://localhost:8088/*","http://localhost:4200/*"]'
```

Then set:

```dotenv
TAMIZA_PUBLIC_URL=http://localhost:8088
OIDC_AUTHORITY=http://localhost:8080/realms/tamiza
OIDC_CLIENT_ID=tamiza-ui
OIDC_AUDIENCE=tamiza-api
OIDC_METADATA_ADDRESS=http://host.docker.internal:8080/realms/tamiza/.well-known/openid-configuration
OIDC_REQUIRE_HTTPS_METADATA=false
```

Plain HTTP works only because everything runs on `localhost`. Browsers allow sign-in (WebCrypto for PKCE) only in a secure context: HTTPS, or `localhost`.

## Sign-in checklist

Run these checks after configuring the realm and starting Tamiza. Use a test user whose email is listed in `TAMIZA_SUPERADMIN_EMAILS` and verified.

1. **Sign-in.** Open `<TAMIZA_PUBLIC_URL>/`. The browser goes to the Keycloak login page. After signing in, the top bar shows your name and the home page says "Welcome, <name>".
2. **Return to the requested route.** Sign out, then open `<TAMIZA_PUBLIC_URL>/projects`. After signing in, the address bar shows `/projects` again. On a new installation the page reads "No projects yet" and offers "Create a project".
3. **Profile and role.** With the session open, `GET <TAMIZA_PUBLIC_URL>/api/v1/me` with the access token returns your name, email, `"isSuperAdmin": true` and `"canCreateProjects": true`.
4. **Sign-out.** Click *Sign out*. You return to Tamiza, which sends you to the Keycloak login page again instead of signing you back in silently.
5. **Session expiry.** Sign in, then end the session in Keycloak (*Users → (user) → Sessions → Sign out*). Within one access-token lifetime, the UI sends you back to the login page when it fails to renew the token.
6. **Keycloak unavailable.** Stop Keycloak (or point `OIDC_AUTHORITY` at an unreachable URL) and reload Tamiza. The page reads "Sign-in is unavailable" instead of staying blank.

## Sign-in page in Spanish

The Tamiza interface is in Spanish or English, chosen from the browser's languages. The sign-in request carries the same choice (`ui_locales=es` or `ui_locales=en`), and Keycloak uses it when its login theme has that language. Out of the box a realm shows its login page in English only. To show it in Spanish:

1. In the admin console, open *Realm settings → Localization*.
2. Turn on *Internationalization*, add *Spanish* (`es`) to the supported locales and keep *English* as the default.
3. Save, then open Tamiza in a browser whose first language is Spanish. The login page should be in Spanish; with an English browser it stays in English.

This is optional. Tamiza works the same with a login page that stays in English.

## Account emails (optional)

Without this section, Tamiza sends no email. A project admin who adds someone by email saves a pending invitation, and you create the person's account in Keycloak and tell them yourself. With it, three actions work from **Settings > Members**, and Keycloak sends the emails:

- **Invitation.** Inviting an email that has no account creates one (email as username, no password, email not verified) and Keycloak emails the person a link to set a password and verify the email.
- **Resend invitation email**, in the menu of a pending invitation, for a person who did not use the link in time.
- **Send password reset email**, in the menu of a member, for a member who forgot their password.

Tamiza never changes an account's data. It creates accounts for invitations, and asks Keycloak to email an account in two cases: the unverified accounts Tamiza created, for the invitation email, and a project member's own account, for the password reset. Accounts you made in the Keycloak console are never sent an invitation: the admin is told that the person already has an account and should let them know.

**Use a realm that only Tamiza uses.** The client below can create and manage every user in its realm. If other applications share the realm, anyone who obtains the client secret can reach their users too.

### What you need

| Item | Why |
|---|---|
| **SMTP settings in the realm** (*Realm settings → Email*), checked with *Test connection* | Keycloak sends every email. Without them the actions answer "could not be sent" and the invitation is kept. |
| A **confidential client** for Tamiza (for example `tamiza-admin`) with **Client authentication** On and **Service accounts roles** On. *Standard flow* and *Direct access grants* can stay Off | Tamiza signs in as this client (client credentials) to call Keycloak's admin API. |
| The service account's roles **`manage-users`** and **`view-users`** from the `realm-management` client, and no others | It creates users, looks them up and asks for the emails. |

On Keycloak 26.2 and later, *fine-grained admin permissions* can narrow the service account further. That is optional.

### Option A: admin console

1. Under *Realm settings → Email*, fill in the SMTP host, port, sender and sign-in, then press *Test connection*.
2. Under *Clients → Create client*: *Client type* OpenID Connect, *Client ID* `tamiza-admin`. Set *Client authentication* **On**, *Standard flow* and *Direct access grants* **Off**, and *Service accounts roles* **On**.
3. Open *Clients → tamiza-admin → Service accounts roles → Assign role*, filter by clients, and add `manage-users` and `view-users` of `realm-management`.
4. Copy the secret from *Clients → tamiza-admin → Credentials*.
5. Put the client ID and secret in `.env` and restart `api` (see the variables below).

### Option B: `kcadm.sh`

With the variables of Option B above set and `kcadm.sh config credentials` done:

```sh
# 1. SMTP. Use your own server; "auth" and "starttls" depend on it.
kcadm.sh update "realms/$REALM" \
  -s smtpServer.host=smtp.example.org -s smtpServer.port=587 \
  -s smtpServer.from=no-reply@example.org -s smtpServer.fromDisplayName=Tamiza \
  -s smtpServer.auth=true -s smtpServer.user=smtp-user -s smtpServer.password=smtp-password \
  -s smtpServer.starttls=true

# 2. The confidential client with a service account.
kcadm.sh create clients -r "$REALM" \
  -s clientId=tamiza-admin -s protocol=openid-connect \
  -s publicClient=false -s serviceAccountsEnabled=true \
  -s standardFlowEnabled=false -s directAccessGrantsEnabled=false

# 3. Its roles.
kcadm.sh add-roles -r "$REALM" --uusername service-account-tamiza-admin \
  --cclientid realm-management --rolename manage-users --rolename view-users

# 4. Its secret (the "value" field).
ADMIN_UUID=$(kcadm.sh get clients -r "$REALM" -q clientId=tamiza-admin --fields id --format csv --noquotes)
kcadm.sh get "clients/$ADMIN_UUID/client-secret" -r "$REALM"
```

### Values for `.env`

| Variable | Value |
|---|---|
| `TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID` | The client's ID, for example `tamiza-admin`. |
| `TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET` | The client's secret. Never commit a file that holds it. |
| `TAMIZA_KEYCLOAK_ADMIN_URL` | Optional. A base URL such as `http://keycloak:8080`, when the public address does not serve `/admin` (some proxies block it) or the `api` container cannot reach it. Without it, Tamiza uses the base of `OIDC_METADATA_ADDRESS` when set, and otherwise of `OIDC_AUTHORITY`. Sign-in always keeps using `OIDC_AUTHORITY`. |
| `TAMIZA_KEYCLOAK_LINK_HOURS` | Optional, 1 to 720. How long the links last. Empty keeps the realm's own setting. |
| `TAMIZA_ACCOUNT_EMAILS_PER_HOUR` | Optional. Emails one user may cause per hour, 20 by default. |

The features turn on when the client ID and the secret are both set. `OIDC_AUTHORITY` must then be a realm URL ending in `/realms/<name>`, and the `api` refuses to start otherwise.

### How long the links last

Without `TAMIZA_KEYCLOAK_LINK_HOURS`, Keycloak uses the realm's *Realm settings → Tokens → Action token generated by admin* (12 hours by default). A person who misses the link is sent a new one with *Resend invitation email*. Changing the lifetime in the realm needs no change in Tamiza:

```sh
kcadm.sh update "realms/$REALM" -s actionTokenGeneratedByAdminLifespan=86400   # 24 hours
```

### Limits

Each user can cause 20 account emails per hour (`TAMIZA_ACCOUNT_EMAILS_PER_HOUR`), counting new accounts, resends and password resets together. The same person is never sent two within 10 minutes. The counters are in memory, so restarting `api` resets them.

### Things to know

- **The links use Keycloak's own address.** Keycloak builds the links in its emails from its public hostname. When `api` reaches Keycloak at an internal address (`TAMIZA_KEYCLOAK_ADMIN_URL`, or `OIDC_METADATA_ADDRESS`) and Keycloak has no fixed hostname, the links carry that internal address and do not open for the person. Set Keycloak's hostname to the public URL (for example `KC_HOSTNAME=https://auth.example.org`), and add `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` when `api` must also reach it by another address. Checked this way with Keycloak 26: the account was created through the internal address and the email carried the public one.
- **Language.** A new account gets the interface language of the admin who invited (Spanish or English). Keycloak uses it for the email and the password page when the realm has that language: see [Sign-in page in Spanish](#sign-in-page-in-spanish). Without internationalization the account is still created and the emails are in English.
- **Names.** Keycloak's default user profile may ask a new person for their first and last name after they set the password.
- **Revoking an invitation does not delete the account.** The person can still use the link until it expires and sign in, with no projects. Delete the account in *Users* if you want it gone.
- **Password reset and federated accounts.** A member who signs in with an identity provider linked to Keycloak (for example Google) would get a local password if they use the reset link. Their linked sign-in keeps working.
- **Account found by `sub`.** Tamiza looks up the member's account with the token's `sub`, which in Keycloak is the user ID. A custom *subject* mapper that changes it makes the password reset answer that the member has no active account.

## Client for AI clients (MCP) (optional)

Without this section the MCP server (`TAMIZA_MCP_ENABLED`) has no client to sign people in with, so nobody can connect an AI client such as Claude. See [docs/mcp.md](mcp.md) for what the server does. The `tamiza-ui` client is not touched: UI tokens never carry the MCP audience, and MCP tokens never carry the API's, so a token for one is refused by the other.

### What you need

| Item | Why |
|---|---|
| A **client scope** `tamiza-mcp` with an **audience mapper** that adds `TAMIZA_MCP_AUDIENCE` (`tamiza-mcp` by default) to access tokens only | `/mcp` accepts only tokens issued for that audience. It must differ from `OIDC_AUDIENCE`; the `api` refuses to start otherwise. |
| A **public client** `tamiza-mcp` with the standard flow, PKCE (S256) and *Consent required* | claude.ai, Claude Desktop and Claude Code sign in with the authorization code flow and no secret. The consent screen tells the person which application is asking. |
| The redirect URIs `https://claude.ai/api/mcp/auth_callback` and `http://localhost:<TAMIZA_MCP_CALLBACK_PORT>/callback` (port 33418 by default) | The first is for claude.ai and Claude Desktop, the second for Claude Code, which listens on that port on the analyst's computer. |
| The client scope `tamiza-mcp` as **Default** next to `basic`, `profile` and `email`, and `offline_access` as **Optional** | The audience reaches every token. `offline_access` lets a connection renew its token (see below). |

The client ID is what people enter in the connector (`TAMIZA_MCP_CLIENT_ID`, `tamiza-mcp` by default). Tamiza only shows it; it never talks to Keycloak with it.

### Option A: admin console

1. **Client scope.** *Client scopes → Create client scope*: *Name* `tamiza-mcp`, *Type* **None**, *Protocol* OpenID Connect, *Include in token scope* **On**. Open it, then *Mappers → Configure a new mapper → Audience*:
   - *Name*: `tamiza-mcp audience`.
   - *Included Custom Audience*: the value of `TAMIZA_MCP_AUDIENCE`, for example `tamiza-mcp`.
   - *Add to access token*: **On**. *Add to ID token*: **Off**.
2. **Client.** *Clients → Create client*:
   - *Client type*: OpenID Connect. *Client ID*: `tamiza-mcp`.
   - *Client authentication*: **Off**. *Standard flow*: **On**. *Direct access grants*: **Off**.
   - *Valid redirect URIs*: `https://claude.ai/api/mcp/auth_callback` and `http://localhost:33418/callback`.
   - *Web origins*: leave empty. The clients call the token endpoint from servers or native apps, not from a page.
   - In the *Advanced* tab, set *Proof Key for Code Exchange Code Challenge Method* to **S256**.
   - In the *Login settings* section, turn *Consent required* **On**.
3. **Scopes of the client.** In *Clients → tamiza-mcp → Client scopes → Add client scope*, add `tamiza-mcp` as **Default** and `offline_access` as **Optional**. Check that `basic`, `profile` and `email` are Default.

### Option B: `kcadm.sh`

With the variables of Option B above set (`KEYCLOAK_URL`, `REALM`) and `kcadm.sh config credentials` done:

```sh
MCP_CLIENT_ID=tamiza-mcp           # TAMIZA_MCP_CLIENT_ID
MCP_AUDIENCE=tamiza-mcp            # TAMIZA_MCP_AUDIENCE
MCP_CALLBACK_PORT=33418            # TAMIZA_MCP_CALLBACK_PORT

# 1. The client scope with the audience mapper.
kcadm.sh create client-scopes -r "$REALM" \
  -s name=tamiza-mcp -s protocol=openid-connect \
  -s 'attributes."include.in.token.scope"=true' \
  -s 'attributes."display.on.consent.screen"=false'

# The scope and the client are found by name with grep: kcadm's -q filter does not apply to client scopes.
SCOPE_UUID=$(kcadm.sh get client-scopes -r "$REALM" --fields id,name --format csv --noquotes | grep ',tamiza-mcp$' | cut -d, -f1)

kcadm.sh create "client-scopes/$SCOPE_UUID/protocol-mappers/models" -r "$REALM" \
  -s name="$MCP_AUDIENCE audience" \
  -s protocol=openid-connect \
  -s protocolMapper=oidc-audience-mapper \
  -s "config.\"included.custom.audience\"=$MCP_AUDIENCE" \
  -s 'config."access.token.claim"=true' \
  -s 'config."id.token.claim"=false'

# 2. The public client.
kcadm.sh create clients -r "$REALM" \
  -s clientId="$MCP_CLIENT_ID" \
  -s protocol=openid-connect \
  -s publicClient=true \
  -s standardFlowEnabled=true \
  -s directAccessGrantsEnabled=false \
  -s consentRequired=true \
  -s "redirectUris=[\"https://claude.ai/api/mcp/auth_callback\",\"http://localhost:$MCP_CALLBACK_PORT/callback\"]" \
  -s 'webOrigins=[]' \
  -s 'attributes."pkce.code.challenge.method"=S256'

MCP_UUID=$(kcadm.sh get clients -r "$REALM" -q clientId="$MCP_CLIENT_ID" --fields id --format csv --noquotes)

# 3. Its scopes: the audience by default, offline_access on request.
kcadm.sh update "clients/$MCP_UUID/default-client-scopes/$SCOPE_UUID" -r "$REALM" -n
OFFLINE_UUID=$(kcadm.sh get client-scopes -r "$REALM" --fields id,name --format csv --noquotes | grep ',offline_access$' | cut -d, -f1)
kcadm.sh update "clients/$MCP_UUID/optional-client-scopes/$OFFLINE_UUID" -r "$REALM" -n
```

New clients get the realm's default client scopes (`basic`, `profile`, `email` and others) on their own, and `offline_access` as an optional scope. The last command therefore changes nothing on a default realm and answers nothing; it is there for a realm where `offline_access` is not attached to new clients. Both `update` commands are safe to repeat.

### What `offline_access` means

Claude asks for `offline_access`, so a connection keeps working after the person's Keycloak session ends: it renews its token until the person revokes the application (their account console, *Applications*) or an admin removes it. That avoids asking people to sign in again every few hours. For the stricter setup, remove `offline_access` from the client's optional scopes: a connection then ends with the Keycloak session and the person signs in again.

To end a person's connection as an admin, delete their consent for the client (*Users → (user) → Consents*, or `kcadm.sh delete users/<user id>/consents/tamiza-mcp -r "$REALM"`). That removes the offline session too, so the refresh token stops working, and the next sign-in shows the consent screen again. Deleting only the offline session (*Users → (user) → Sessions*, or `kcadm.sh delete sessions/<session id> -r "$REALM" -q isOffline=true`) also stops the refresh token but keeps the consent, so signing in again does not ask. Either way, an access token that was already issued works until it expires (five minutes by default in Keycloak) plus 30 seconds that `api` allows for clock differences.

### Check it

1. The redirect URIs are the ones above:

   ```sh
   kcadm.sh get clients -r "$REALM" -q clientId="$MCP_CLIENT_ID" --fields clientId,publicClient,redirectUris,consentRequired
   ```

2. A token for the client has `aud` equal to `TAMIZA_MCP_AUDIENCE` and not the API's. Sign in once from Claude Code (`/mcp`), or from the admin console use *Clients → tamiza-mcp → Client scopes → Evaluate*, pick a user, and open *Generated access token*: `aud` must read `tamiza-mcp` and `scope` must include `tamiza-mcp`.
3. The `api` agrees: `curl -i <TAMIZA_PUBLIC_URL>/mcp` answers `401` with a `WWW-Authenticate` header that holds `resource_metadata="<TAMIZA_PUBLIC_URL>/.well-known/oauth-protected-resource/mcp"` and `scope="tamiza-mcp"`, and that document names your realm under `authorization_servers`.

Keycloak ignores the `resource` parameter that Claude sends with the authorization request, which is why the audience comes from the client scope and not from it. If a future Keycloak or client version requires resource indicators, enable them in the realm and set `TAMIZA_MCP_AUDIENCE` to the address of `/mcp`.

## Other OpenID Connect providers

Sign-in uses standard OpenID Connect only, so a provider other than Keycloak can sign users in. Set the `OIDC_*` variables to the provider's values and leave the `TAMIZA_KEYCLOAK_*` ones empty. Account emails then do not exist: the members section offers no email actions, and an invited person must be told by the admin, as in Tamiza without the section above.

What the provider needs:

| Item | Detail |
|---|---|
| Discovery | `<OIDC_AUTHORITY>/.well-known/openid-configuration`, or `OIDC_METADATA_ADDRESS`. `OIDC_AUTHORITY` must equal the tokens' `iss`. |
| A **public client** for the UI | Authorization code flow with PKCE (S256). Allow `<TAMIZA_PUBLIC_URL>/*` as redirect and post-logout URIs, and allow the browser origin (CORS) on the token endpoint. |
| **Signed JWT access tokens** | With `aud` equal to `OIDC_AUDIENCE`, a `sub`, and the standard claims **`name`**, **`email`** and **`email_verified`** inside the access token itself, not only in the ID token. |
| Refresh tokens | Tamiza renews the session with them. If the provider only issues them with `offline_access`, add that scope to `OIDC_SCOPE`. |
| Sign-out | Used when the provider publishes an `end_session_endpoint`. |

What depends on the claims: invitations match the **verified** `email` of the person signing in, and the superadmin role from `TAMIZA_SUPERADMIN_EMAILS` needs `email_verified` true. A provider whose tokens lack them still signs people in, but invitations never turn into memberships and nobody becomes superadmin, so on a new installation nobody can create the first project.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "Sign-in is unavailable" with Keycloak running | The browser cannot fetch `<OIDC_AUTHORITY>/.well-known/openid-configuration`, or Tamiza is served over plain HTTP on an address other than `localhost`. |
| Keycloak says "Invalid parameter: redirect_uri" | *Valid redirect URIs* does not include `<TAMIZA_PUBLIC_URL>/*`. |
| Sign-in loops, and API calls return 401 | Access tokens lack the audience: check the audience mapper and `OIDC_AUDIENCE`. A mismatch between `OIDC_AUTHORITY` and the token's `iss` causes the same symptom. |
| API logs show "IDX20803: Unable to obtain configuration" | The `api` container cannot reach the discovery document. Set `OIDC_METADATA_ADDRESS` to a reachable URL. |
| `isSuperAdmin` is false for a listed email | The email is not verified in Keycloak, or `api` was not restarted after editing `.env`. |
| No **New project** button, or `/projects/new` returns to the project list | Only admins of a project and superadmins can create projects, and a pending invitation does not count. On a new installation, list your verified email in `TAMIZA_SUPERADMIN_EMAILS` and check that `isSuperAdmin` is true (see the row above); otherwise ask an admin of a project to make you admin. |
| Inviting says the email "could not be requested" (`failed`) | Keycloak could not send it. Check *Realm settings → Email* with *Test connection*, and that the `api` reaches Keycloak (`TAMIZA_KEYCLOAK_ADMIN_URL`). The `api` log names the step and the HTTP status. The invitation is kept: use *Resend invitation email* once it is fixed. |
| The `api` log says an admin request was refused (401 or 403) | The `tamiza-admin` client lacks the `manage-users` and `view-users` roles of `realm-management`, or its secret is wrong. |
| Inviting says the person "already has an account" (`existingAccount`) | The email already has an account in Keycloak that Tamiza did not create, or the person already verified theirs. No email was sent: tell them yourself. |
| Password reset says the member "has no active account" (`noAccount`) | The account was deleted or disabled in Keycloak, has no email, or Keycloak's user ID differs from the token's `sub`. |
| The link in an invitation or reset email points to an internal address (`host.docker.internal`, a container name) | Keycloak builds links from the address it was reached at: fix its hostname, see "The links use Keycloak's own address". |
| The `api` does not start and names `OIDC_AUTHORITY` | The Keycloak client variables are set but the authority is not a realm URL ending in `/realms/<name>`. Clear the client variables if you use another provider. |
