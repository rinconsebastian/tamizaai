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
| `TAMIZA_SUPERADMIN_EMAILS` | Comma-separated emails of the people who administer the installation. |

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
2. **Return to the requested route.** Sign out, then open `<TAMIZA_PUBLIC_URL>/projects`. After signing in, the address bar shows `/projects` again. The page reads "Page not found" until projects exist.
3. **Profile and role.** With the session open, `GET <TAMIZA_PUBLIC_URL>/api/v1/me` with the access token returns your name, email and `"isSuperAdmin": true`.
4. **Sign-out.** Click *Sign out*. You return to Tamiza, which sends you to the Keycloak login page again instead of signing you back in silently.
5. **Session expiry.** Sign in, then end the session in Keycloak (*Users → (user) → Sessions → Sign out*). Within one access-token lifetime, the UI sends you back to the login page when it fails to renew the token.
6. **Keycloak unavailable.** Stop Keycloak (or point `OIDC_AUTHORITY` at an unreachable URL) and reload Tamiza. The page reads "Sign-in is unavailable" instead of staying blank.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "Sign-in is unavailable" with Keycloak running | The browser cannot fetch `<OIDC_AUTHORITY>/.well-known/openid-configuration`, or Tamiza is served over plain HTTP on an address other than `localhost`. |
| Keycloak says "Invalid parameter: redirect_uri" | *Valid redirect URIs* does not include `<TAMIZA_PUBLIC_URL>/*`. |
| Sign-in loops, and API calls return 401 | Access tokens lack the audience: check the audience mapper and `OIDC_AUDIENCE`. A mismatch between `OIDC_AUTHORITY` and the token's `iss` causes the same symptom. |
| API logs show "IDX20803: Unable to obtain configuration" | The `api` container cannot reach the discovery document. Set `OIDC_METADATA_ADDRESS` to a reachable URL. |
| `isSuperAdmin` is false for a listed email | The email is not verified in Keycloak, or `api` was not restarted after editing `.env`. |
