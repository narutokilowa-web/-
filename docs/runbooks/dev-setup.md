# Developer setup (M0)

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| Docker + Compose v2 | 24+ / 2.20+ | the whole stack, backend integration tests, Flutter-in-Docker |
| .NET SDK | 10.0 | backend build/tests (`make test-backend`), `make verify-audit` |
| Node.js | 22 | web lint/tests/build, Playwright E2E |
| Flutter | 3.41.6 (optional) | if absent, `make test-mobile`/`make apk` use the `instrumentisto/flutter:3.41.6-androidsdk36-r0` image |

RAM: the running stack needs about 2–3 GB (target ≤ 6 GB).

## First run

```bash
make up        # creates .env with random secrets (first time), builds images, starts db, keycloak, migrate, api, web; waits for health
```

| URL | What |
|---|---|
| http://localhost:5173 | web console (Arabic, RTL) |
| http://localhost:5080/health/ready | API readiness |
| http://localhost:8080 | Keycloak (admin console: user `kcadmin`, password `KEYCLOAK_ADMIN_PASSWORD` in `.env`) |

**Dev users** (all share `MFOP_DEV_USER_PASSWORD` from `.env`): `registrar1`, `approver1`, `supervisor1`, `tech1`, `warehouse1`, `auditor1`, `admin1`, `inspector1`. Each has exactly one role, **stored in our database** (`access.role_assignments`), not in Keycloak.

## Everyday commands

```bash
make test          # backend + web + mobile tests (no running stack needed; backend tests use Docker)
make e2e           # browser test: every dev user signs in and sees their role (needs `make up`)
make seed          # re-apply the dev bootstrap (idempotent)
make demo-reset    # wipe volumes, start from scratch
make down          # stop
make apk           # Android debug APK → mobile/build/app/outputs/flutter-apk/app-debug.apk
```

## Restricted networks (TLS-inspecting proxy, blocked registries)

- Put the proxy's CA certificate in `deploy/certs/*.crt` (git-ignored). The Dockerfiles trust it during builds.
- Build-time proxy settings go into a **git-ignored** `deploy/compose/docker-compose.override.yml` (the Makefile includes it automatically when present). Example:

```yaml
services:
  api:     { build: { network: host, args: { HTTPS_PROXY: "http://proxy:3128" } } }
  migrate: { build: { network: host, args: { HTTPS_PROXY: "http://proxy:3128" } } }
  web:     { build: { network: host, args: { HTTPS_PROXY: "http://proxy:3128" } } }
```

- Flutter in Docker: `make test-mobile FLUTTER_DOCKER_ARGS="--network host --env-file my-proxy.env"`.
- The Android build needs `dl.google.com` (Google Maven) and Maven Central to be reachable.

## Database roles

| Role | Used by | Rights |
|---|---|---|
| `mfop_owner` | migrator only | owns schemas and tables |
| `mfop_app` | the API at runtime | SELECT, INSERT, UPDATE. **No DELETE** (nothing operational is deleted) |
| `mfop_readonly` | auditor tooling (`audit-verify`, from M1) | SELECT |

Migrations run as a one-shot `migrate` service before the API starts. The API never migrates.
