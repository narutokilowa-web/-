# Developer setup (M2)

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
make e2e           # browser tests (needs `make up && make seed`): every dev user signs in; M1 registration; M2 device/fault/work-order; M3 / DEMO 1 review, seal, recommendation, dashboard, audit
make android-e2e   # field app on a connected Android phone/emulator (needs `make up`, local Flutter + adb)
make verify-audit  # independent audit verifier against the running DB (as mfop_readonly); exit 0 = OK, 1 = findings, 2 = error
make seed          # DEMO 1 data (idempotent): dev users, reference data, synthetic items/stock/lights, the repeat-failure light
make tamper-demo   # DEV ONLY, DEMO 1 step 20: change a consumed quantity as the DB owner; `make verify-audit` then fails
make tamper-undo   # put the recorded quantity back
make demo-reset    # wipe volumes, start from scratch
make down          # stop
make apk           # Android debug APK → mobile/build/app/outputs/flutter-apk/app-debug.apk
```

## Added in M4

- **New secrets in `.env`** (`make env` appends them to an existing `.env`): `MFOP_KEYCLOAK_BACKEND_SECRET` (the API's
  Keycloak service account `mfop-backend`, used to disable users and end sessions) and `MFOP_GRANT_SIGNING_KEY` (EC P-256
  PKCS#8 key that signs offline grants; needs `openssl`).
- **Keycloak realm changed** (new client `mfop-backend`): an existing Keycloak volume keeps the old realm, so run
  `make demo-reset` once after pulling M4.
- **Phones:** install app **0.4.0** and register again (old registrations have no device key). The API refuses older
  apps (`min_app_version`, 426).

**After pulling M5 (material accountability):**

- The inventory migration adds stock issues, returns, core returns and serial units; the demo seed changed (issues now wait
  for the technician's acknowledgment, the earlier repairs declare their failed parts). Run `make demo-reset` once.
- **Phones:** install app **0.5.0** (new **المواد** tab and the failed-part step). A new database has `min_app_version`
  0.5.0; on an existing one the admin raises it on **Settings** when every phone is updated.
- Two new policies (data, **Settings**): `issue_ack_warning_hours` (24) and `core_return_overdue_days` (7). The timers run
  inside the API (`Inventory:RunBackgroundTimers`, `Inventory:IntervalSeconds` 60).
- **API settings:** `Grants:SigningKey`, `Grants:KeyId`, `Keycloak:BaseUrl`, `Keycloak:AdminClientSecret`,
  `Keycloak:TaskIntervalSeconds` (5), `Auth:SignatureMaxSkewMinutes` (15). Security **policies** are data, changed by the
  admin on **Settings** (offline grant hours, grant-window tolerance, app lock minutes, minimum app version).
- New web pages: **Users** (admin, offboarding), **Quarantine** (supervisor/auditor), **Work orders needing reassignment**.
  Runbook: `docs/runbooks/offboarding.md`.

## Added in M3

- **Evidence store:** named volume `objects` mounted at `/var/lib/mfop/objects` in the `api` and `migrate` containers (`Storage__Mode=local`, `Storage__Root`). Local disk, **not WORM** (the web banner says so); WORM / Object Lock arrives in M8. `make demo-reset` wipes it with the database.
- **`make seed`** runs the migrator twice: `migrate --reference-data --seed-dev` (diagnosis codes, checklist templates, dev users, rule defaults) and `seed-demo deploy/seed/demo-data.json` (items with SYP costs, opening stock received and issued by `warehouse1`, 10 synthetic lights, and the repeat-failure light with two earlier repairs). All values are synthetic.
- **Rule settings:** the repeated-failure threshold and window live in `rules.rule_settings` (admin → **الإعدادات**), not in configuration files.
- **API settings (appsettings / env):** `Evidence:MaxBytes` (15 MB), `Evidence:VerifyIntervalSeconds` (2), `Evidence:PreviewMaxPixels` (1280), `Evidence:ThumbnailMaxPixels` (320), `WorkOrders:ReworkWindowDays` (30).
- DEMO 1 click-by-click script: `docs/runbooks/demo-1.md`.

## Configuration added in M1

`make env` (run by `make up`) creates `.env` on the first run. Later it only **appends** variables that are new in `.env.example`, so existing secrets are kept.

| Variable | Purpose |
|---|---|
| `MFOP_TAG_CHECK_KEY` | Key for the 2-character check code of tag human codes (`XXXXXXXX.CC`). **Changing it makes every printed label unresolvable.** Keep it stable and back it up with the database. |

The QR on a label contains `Tags__BaseUrl/<human code>` (Compose: `http://localhost:5173/t/<code>`). Opening it in the web console resolves to the asset.

## Phones (M2)

- The field app talks to the API (`:5080`) and Keycloak (`:8080`) as `localhost` through USB: `adb reverse tcp:5080 tcp:5080 && adb reverse tcp:8080 tcp:8080`.
- A phone must be **approved** before it can do anything: web → **Devices** (supervisor or admin). Nobody approves their own device. Approving a new phone revokes the user's previous one.
- Work orders can only be assigned to a technician whose phone is approved. `tech1` and `tech2` are the dev technicians.
- Manual checklist for a real phone (browser login and live camera scan): `docs/runbooks/android-field-test.md`. CI runs the rest on an Android emulator and uploads screenshots.

| Setting | Where | Default |
|---|---|---|
| `WorkOrders:SlaHoursByPriority` (e.g. `{"P1": 4}`) | API configuration | **empty**: no SLA until the municipality defines it |
| `WorkOrders:TimeZone` | API configuration | `Asia/Damascus` (year in `WO-<year>-<n>`) |
| `Auth:MobileClientId` | API configuration | `mfop-mobile` (tokens of this client always need an ACTIVE device) |

## Audit verification

`make verify-audit` runs `tools/audit-verify` with the read-only role. It checks:
- sequence continuity, each event's content hash, and the `prev_hash` chain;
- asset-version snapshot hashes against the audited `snapshot_hash`;
- that every ACTIVE street light still equals its latest approved snapshot.

The first finding is printed with its `seq` and entity. The verifier cannot detect deletion of the newest events until signed checkpoints arrive (M8). The database is **tamper-evident**, not immutable.

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
| `mfop_app` | the API at runtime | SELECT, INSERT, UPDATE. **No DELETE** (nothing operational is deleted). Append-only tables (`audit.audit_events`, `assets.asset_versions`, `assets.asset_status_events`, `faults.fault_confirmations`, `sync.sync_commands`): SELECT, INSERT only, plus a trigger rejecting UPDATE/DELETE/TRUNCATE (a guard against accidents and bugs, not against a privileged DBA) |
| `mfop_readonly` | auditor tooling (`audit-verify`) | SELECT |

Migrations run as a one-shot `migrate` service before the API starts. The API never migrates.
