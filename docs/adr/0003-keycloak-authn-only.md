# ADR-0003: Keycloak for authentication/MFA only; authorization and device trust in our backend

- **Status:** Accepted
- **Date:** 2026-09-24

## Context
We need proven login, password policy and MFA without writing security-critical identity code. BYOD revocation, offline grants and record-level separation of duties are domain logic.

## Decision
- **Keycloak** (OIDC, Authorization Code + PKCE) handles users' credentials, sessions and MFA. Tokens are used for **identity (`sub`) only**.
- **Roles, permissions, separation of duties, device registration and status, offline grants** live in our database and are evaluated by our backend on every request.
- Seeded users in dev have **fixed IDs** in the Keycloak realm, matching `deploy/seed/dev-users.json`, so our DB can map `sub` → user and roles.
- The API trusts tokens whose issuer is the public Keycloak URL. Signing keys are fetched over the internal Docker network (`KC_HOSTNAME` + dynamic backchannel).
- No identity complexity beyond the pilot: no federation, no fine-grained Keycloak authorization services, no Keycloak roles used for business permissions.

## Consequences
- Revoking a role or device takes effect immediately; there is no waiting for token expiry.
- The user list exists in two places (Keycloak and our DB). They are linked by `sub`, and a test checks that the seeded users match.

## Alternatives considered
- **ASP.NET Core Identity in-house:** fewer containers, but more self-written security code, and it would have to be replaced for national SSO later.
- **Business roles in Keycloak:** rejected because it splits authorization across two systems.
