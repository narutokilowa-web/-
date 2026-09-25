# Keycloak dev realm (`realm-mfop.json`)

- **Dev only.** Imported by `docker compose` on the first start (`--import-realm`); ignored if the realm already exists (`make demo-reset` re-imports).
- **Business roles are NOT defined here.** They live in our database (ADR-0003). Keycloak only authenticates.
- Users have fixed ids matching `deploy/seed/dev-users.json` (a SpecFiles test enforces this).
- Passwords are the placeholder `${MFOP_DEV_USER_PASSWORD}`, substituted from `.env` at import time. No password is committed.
- Client `mfop-backend` (M4) is the API's **confidential service account**: client-credentials only (no browser flows), secret `${MFOP_KEYCLOAK_BACKEND_SECRET}` from `.env`, and only the realm-management roles `view-users` and `manage-users`. The API uses it after commit to disable/enable users and end their sessions (offboarding, device revocation).
- Clients `mfop-web` and `mfop-mobile` are public clients with Authorization Code + **PKCE S256**. Password grant and implicit flow are disabled.
- Access tokens carry audience `mfop-api` (audience mapper on each client; do NOT add a `clientScopes` list to the import, it suppresses Keycloak built-in scopes such as `basic`, which provides `sub`) and live 5 minutes. Refresh tokens rotate.
- **MFA is off in the dev realm.** The pilot realm (M8/M9) enables OTP for SUPERVISOR, APPROVER, WAREHOUSE, AUDITOR and ADMIN.
