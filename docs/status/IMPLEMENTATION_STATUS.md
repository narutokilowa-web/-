# Implementation status

| Milestone | Status | Date |
|---|---|---|
| M0 — Foundations | ✅ **Complete — awaiting owner approval before M1** | 2026-09-24 |
| M1 — Audit core + register / approve / QR (web) | ⏸ not started (blocked on M0 approval) | — |
| M2 — Android scan + faults + work-order creation | — | — |
| M3 — Field execution + review + dashboard → DEMO 1 | — | — |

Milestone definitions: `docs/lean-pilot/11-milestones-and-dod.md`.

---

## M0 — Foundations

### Deliverables vs. specification (11 §11.2 M0)

| # | Deliverable | Status | Where |
|---|---|---|---|
| 1 | ADRs 0001–0008 | ✅ | `docs/adr/` |
| 2 | `AGENTS.md`, `CLAUDE.md` | ✅ | repo root |
| 3 | Repository skeleton (DEMO 1 modules, audit-verify stub, React + Flutter skeletons) | ✅ | `backend/`, `web/`, `mobile/`, `tools/audit-verify/` |
| 4 | Docker Compose: `db`, `keycloak` (realm + seeded users), `api`, `web`, Makefile | ✅ (plus a one-shot `migrate` service, see deviations) | `deploy/compose/docker-compose.yml`, `Makefile` |
| 5 | Spec as data: state-machine YAML (DEMO 1 set) + `permissions.yaml` | ✅ | `docs/state-machines/`, `docs/lean-pilot/permissions.yaml` |
| 6 | CI: backend, web, Flutter, secret scan, dependency scan | ✅ written; results in the last section | `.github/workflows/ci.yml` |

### Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| `make up` from a clean clone, all services healthy, ≤ 15 min | ✅ **70 s** (base images already cached; a first-ever pull adds ~3.5 GB of images) | fresh `git clone` → `make up` → db, keycloak, api, web healthy; migrate exited 0 |
| Every seeded user can log in to the web and sees their role, **read from our DB** | ✅ 8/8 users + wrong-password rejection | `make e2e` (Playwright, real Keycloak PKCE login): 9/9 passed |
| Flutter debug APK builds | ✅ **in CI** (69.1 MB artifact `mfop-field-debug-apk`). Not buildable in this sandbox: the network policy blocks `dl.google.com` | `flutter analyze` clean and widget tests pass locally; CI `mobile` job green |
| CI green | ✅ all 5 jobs green on head `bb0a372` | run 36004341994 |

### Tests run (local)

| Suite | Tests | Result |
|---|---|---|
| `Mfop.SpecFiles.Tests`: state machines valid; permissions and roles consistent; SoD; dev users ↔ Keycloak realm match; no committed passwords; PKCE-only clients | 84 | ✅ pass |
| `Mfop.Architecture.Tests`: module boundaries, Kernel independence, Domain without EF/HTTP | 22 | ✅ pass |
| `Mfop.Api.Tests`, against **real PostGIS** (Testcontainers, same init script + migrator): health; 401 without auth; `/me` for all 8 seeded users; `USER_NOT_PROVISIONED`; `USER_DISABLED`; revoked role hidden; **runtime role cannot DELETE**; migrator + seed idempotent | 15 | ✅ pass |
| Web (Vitest): Arabic sign-in button, RTL/LTR switch, role list, `fetchMe` mapping, ar/en key parity, "no immutability claims" | 9 | ✅ pass |
| Web lint (ESLint, incl. ban on `localStorage`) + `tsc` typecheck + production build | — | ✅ clean |
| Mobile: `flutter analyze` + widget tests (Arabic RTL default, English LTR, 56 dp touch target) | 3 | ✅ pass / no issues |
| E2E (Playwright vs. Compose, real Keycloak): 8 role sign-ins + bad password | 9 | ✅ pass (on the working copy and on a fresh clone) |
| `dotnet list package --vulnerable` / `npm audit --omit=dev` | — | ✅ none found |
| gitleaks: git history | — | ✅ no leaks (working-tree hits are only in the git-ignored local `.env`) |
| `make verify-audit` | — | runs the stub; the real verifier arrives in M1 |

Extra runtime checks on the fresh-clone stack:
- `GET /api/v1/me` with no token or a garbage token → **401**.
- `mfop_app` has `SELECT, INSERT, UPDATE` on `access.*`, and **no DELETE**.
- Memory: Keycloak ~840 MB, API ~64 MB, DB ~74 MB, web ~5 MB (≈ 1 GB total).

### Deviations from the specification

| Deviation | Why |
|---|---|
| **One-shot `migrate` service** added to Compose (spec listed db, keycloak, api, web) | Spec 06 requires migrations to run as the owner role and never at API startup. A separate one-shot service does exactly that. |
| **`inspector1`** seeded in addition to the seven users listed in 11 §M0 | So that every role in `permissions.yaml` has a dev user (enforced by a test). |
| **Dev users/roles are bootstrap data without audit events** | Audit arrives in M1. From M1, the bootstrap is recorded as genesis audit events and all role changes go through audited commands. |
| **Flutter pinned to 3.41.6** (latest stable is 3.47.5) | 3.41.6 is the newest version available as a Docker image with the Android SDK, which is how this sandbox gets an Android toolchain. CI uses the same version. Upgrade proposed for M2 (see decisions). |
| **Web tokens in `sessionStorage`** (spec 13 prefers no browser token storage / BFF) | Standard SPA + PKCE for M0/DEMO 1. Tokens are short-lived (5 min), `localStorage` is banned by lint, and storage is cleared when the tab closes. Revisit in the M9 security pass. |
| **State-machine YAML schema gained `deferred_exits`** | The spec test found that `AssetLifecycle.ACTIVE` has no exit in DEMO 1: investigation and decommissioning are deferred. The deferral is now explicit and referenced, instead of weakening the rule. |
| Keycloak realm: audience mapper **on each client**, not as a realm client scope | Defining `clientScopes` in a realm import suppresses Keycloak's built-in scopes, including `basic`, which carries `sub`. Found by E2E; a regression test was added. |
| Spec change (owner-requested): minimal repeated-failure rule moved into M3 / DEMO 1 | `docs/lean-pilot/11` step 17a and M3 item 10; `06` updated. |

### Warnings and errors encountered (all resolved or explained)

| # | What | Resolution |
|---|---|---|
| 1 | Docker daemon not running in the sandbox | started `dockerd` (sandbox only) |
| 2 | `dot.net` install script blocked (403) | .NET SDK 10.0.112 installed from the Ubuntu archive |
| 3 | Docker Hub anonymous rate limit (429); `quay.io` blocked | images pulled via `mirror.gcr.io` and re-tagged locally. Compose keeps the canonical names (`quay.io/keycloak/keycloak:26.7.4`, …) |
| 4 | `dl.google.com` blocked (Android SDK, Google Maven) | Android SDK obtained via the Flutter Docker image; **the APK cannot be built here** because Google Maven is unreachable. CI builds it |
| 5 | Maven Central 429 in the sandbox | tried Google's Maven Central mirror via a sandbox-only Gradle init script (not committed); still blocked by #4 |
| 6 | Alpine package CDN blocked (web image `apk add`) | web Dockerfile no longer installs packages; the extra CA goes in via `NODE_EXTRA_CA_CERTS` |
| 7 | Keycloak refused realm file with a `_comment` field | removed; notes moved to `deploy/keycloak/README.md` |
| 8 | API healthcheck used `/bin/sh` (dash has no `/dev/tcp`) | healthcheck runs `bash -c` |
| 9 | All logins returned `USER_NOT_PROVISIONED` (no `sub` claim) | see the realm client-scope deviation above |
| 10 | Build errors during scaffolding (duplicate `Program` type, NetArchTest API name, xUnit analyzer, obsolete Testcontainers ctor) | fixed; the build is warning-free with `-warnaserror` |
| 11 | typescript-eslint does not support TypeScript 7 yet | pinned TypeScript 6.0.3 |
| 12 | Migrator prints `libgssapi_krb5.so.2: cannot open shared object file` | **harmless** Npgsql/.NET Kerberos probe (no Kerberos used); migration succeeds. Not fixed: installing the library needs the Debian mirror, which is blocked here. Can be silenced later |
| 14 | CI `gitleaks-action` failed on PR events (403: it needs pull-request API access; the workflow token is read-only) | replaced with the pinned gitleaks CLI scanning full history; green |
| 15 | CI warning: `actions/*@v4` target deprecated Node 20 (runner forces Node 24) | harmless; bump action majors in a later maintenance PR |
| 13 | ASP.NET Core DataProtection warnings (keys not persisted) in API logs | harmless in M0 (no cookies or protected payloads). Address when a feature needs DataProtection |

### Sandbox-only artefacts (git-ignored, not part of the product)

`deploy/compose/docker-compose.override.yml` (build via host network + proxy), `deploy/certs/sandbox-proxy-ca.crt`, and a Gradle init script under `/tmp`. The committed mechanism (`deploy/certs/*.crt`, optional override file, `FLUTTER_DOCKER_ARGS`) is generic and documented in `docs/runbooks/dev-setup.md` for restricted networks, which is also useful inside government networks.

### Decisions needed from the owner

1. **Approve M0** so M1 can start.
2. **Android build in this environment:** allow `dl.google.com` (and ideally `repo.maven.apache.org`) in the environment's network settings so APKs can be built locally here. Otherwise APKs come from CI artifacts.
3. **Flutter version:** keep 3.41.6, or move to the current stable 3.47.5 in M2 (CI can use it; local builds here need an image with that version).
4. **Android application id:** `local.mfop.mfop_field` is a placeholder. Choose the real identifier before any distribution (it cannot change after install without reinstalling).

### CI results

Run [36004341994](https://github.com/narutokilowa-web/-/actions/runs/36004341994) on head `bb0a372`: **success**.

| Job | Result |
|---|---|
| Backend (.NET build `-warnaserror`, 121 tests incl. Testcontainers PostGIS, vulnerable-package check) | ✅ |
| Web (lint, typecheck, 9 unit tests, build, `npm audit`) | ✅ |
| Mobile (analyze, 3 widget tests, **debug APK**, uploaded as artifact `mfop-field-debug-apk`, 69.1 MB, kept 14 days) | ✅ |
| E2E (`make up` from a clean runner with random secrets; Playwright sign-in for all 8 seeded users + bad password) | ✅ |
| Secret scan (gitleaks CLI, full git history) | ✅ |
