# Implementation status

| Milestone | Status | Date |
|---|---|---|
| M0 — Foundations | ✅ **approved by the owner** | 2026-09-24 |
| M1 — Audit core + register / approve / QR (web) | ✅ **PASS — awaiting owner approval before M2** | 2026-09-24 |
| M2 — Android scan + faults + work-order creation | — | — |
| M3 — Field execution + review + dashboard → DEMO 1 | — | — |

Milestone definitions: `docs/lean-pilot/11-milestones-and-dod.md`.

---

## M1 — Audit core + register / approve / QR (web)

Scope followed exactly: 11 §11.2 M1. Nothing from M2 has been started: no mobile scan, no faults, no work orders, no `/sync` endpoints.

### Deliverables vs. specification

| # | Deliverable | Status | Where |
|---|---|---|---|
| 1 | **Kernel:** `Result`/`Error` with stable codes; `Money` (amount + ISO 4217 currency + as-of date; no currency hard-coded, no implicit conversion; no money is used before M3); clock; UUIDv7 ids; state-machine base checked against the YAML; `CommandEnvelope` | ✅ | `backend/src/Mfop.Kernel/` |
| 2 | **Audit core:** `UnitOfWork` (one connection + one transaction across module contexts; the audit event is written **in the same transaction**); commit fails with `AUDIT_MISSING` if a changed entity has no audit record; old/new values captured automatically from the change tracker; DELETE refused; hash chain (`prev_hash`/`hash` over canonical JSON); append-only grants + UPDATE/DELETE/TRUNCATE-rejecting trigger | ✅ | `Mfop.Infrastructure/Persistence/UnitOfWork.cs`, `Modules/Mfop.Audit/` |
| 3 | **`tools/audit-verify`:** recomputes the chain, checks seq continuity and links, recomputes asset-version snapshot hashes, compares ACTIVE street-light rows with their latest approved snapshot; exit code 0/1/2; names the first broken `seq` and entity. Independent implementation (depends only on Npgsql, has its own canonical JSON) | ✅ | `tools/audit-verify/` |
| 4 | **Access (minimal):** user created on first login (Keycloak `sub`), with no roles, audited; dev role assignments seeded **through audited commands**; permission pipeline (`RequirePermission(action)`), roles read from **our DB** on every request; record policies (registrar ≠ approver, own records only); static SoD | ✅ | `Modules/Mfop.Access/`, `Mfop.Http/` |
| 5 | **Registration on the web:** synthetic street-light form (power source, responsible party, lat/lon, install-date precision, solar fields only for SOLAR/HYBRID), `capture_channel = WEB`; draft / edit / submit / discard; approver queue with approve / return / reject (note required for return/reject), approver ≠ registrar; on approval: `SL-000001`-style code, asset version v1 + snapshot hash, **generated tag** bound | ✅ (photos deferred, see deviations) | `Modules/Mfop.Assets/`, `web/src/features/registrations/` |
| 6 | **QR:** asset page shows the QR (tag URL + human code `XXXXXXXX.CC`) and **Print label** (A6 layout, browser print view); `/t/<code>` resolves back to the asset; a forged check code does not resolve | ✅ | `web/src/features/assets/` |
| 7 | **History:** asset-page timeline built from the audit events of the asset, its registrations, tag and versions, showing old → new values, actor (name + roles), time, reason and the event hash | ✅ | `AssetQueries.HistoryAsync`, `HistoryTimeline.tsx` |
| 8 | **Tests:** YAML ↔ code for the registration, lifecycle and operational-status machines; audit-in-transaction for every M1 command; N1, N11, N12 | ✅ | see below |

### Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| DEMO 1 steps **1–5** work | ✅ | `make demo-reset` (clean volumes) → all services healthy in **58 s** → E2E `m1-registration-flow`: registrar1 signs in, registers a synthetic light, submits → approver1 (a different user) approves from the queue → `SL-000001`, QR, human code, A6 label, `/t/<code>` resolves |
| Asset page shows the registration and approval history with old/new values | ✅ | E2E checks the drafted/approved actors, the approval note, `lifecycle_state PENDING → ACTIVE` and the `snapshot_hash` fact; screenshots of the manual walkthrough |
| Editing an **audit row** directly in the DB makes `make verify-audit` fail at the right place | ✅ | manual on the live stack: superuser disables the trigger and changes the `reason` of the `registration.approved` event → `INTEGRITY FAILURE … FIRST: EVENT_CONTENT_CHANGED seq=20 entity=registration/…`; restored → `OK` |
| Editing an **asset row** directly in the DB makes `make verify-audit` fail at the right place | ✅ | manual: `mfop_app` changes `pole_height_m` of SL-000001 without a command → `FIRST: ASSET_ROW_CHANGED … SL-000001: current row differs from its latest approved version in: pole_height_m`; restored → `OK` |
| `mfop_app` cannot change audit rows (N11) | ✅ | live: `UPDATE audit.audit_events` as `mfop_app` → `permission denied`; also covered by tests (UPDATE/DELETE/TRUNCATE for the app role; owner blocked by the trigger) |

### Tests run (local)

| Suite | Tests | Result |
|---|---|---|
| `Mfop.SpecFiles.Tests`: M0 checks + state machines in code == YAML (registration, asset lifecycle, operational status), unlisted (state, command) pairs rejected, static SoD in code == YAML | 92 | ✅ pass |
| `Mfop.Domain.Tests` (new): kernel (Money, Result, state machine, canonical JSON) and assets domain (attribute validation, tag codes and check code, snapshot hash) | 20 | ✅ pass |
| `Mfop.Architecture.Tests`: module boundaries + **new source-level scan** (a module may use another module's `Contracts` only) | 29 | ✅ pass |
| `Mfop.Api.Tests` against real PostGIS (Testcontainers): full registration flow with old/new; sequential asset codes; **N1** self-approval → `SOD_VIOLATION` (even after the registrar is also given the approver role); registrar cannot approve (403); return → edit → resubmit; reject is terminal; discard; approve before submit → `INVALID_TRANSITION`; validation errors; registrars see only their own records; per-command audit actions by `command_id`; a rejected command writes **nothing**; UnitOfWork rolls back when audit is missing; **N11** (app role cannot UPDATE/DELETE/TRUNCATE audit; owner blocked by trigger); **N12** tampered audit row detected at its seq; deleted event → `SEQ_GAP`; asset row edit → `ASSET_ROW_CHANGED`; snapshot tamper detected; canonical JSON of kernel == verifier; **authorization matrix** (every endpoint guarded; code matrix == `permissions.yaml`; every action promised up to M1 has an endpoint; every role gets 403 on every action it lacks) | 53 | ✅ pass |
| Web (Vitest): M0 tests + role-based navigation, registration form mapping, history timeline rendering, idempotency key on POST | 19 | ✅ pass |
| Web lint + typecheck + production build | — | ✅ clean |
| Mobile: `flutter analyze` + 3 widget tests (application id changed only) | 3 | ✅ pass / no issues |
| E2E (Playwright vs. Compose, real Keycloak): 8 role sign-ins + wrong password + **M1 flow** (incl. no horizontal page scroll with long audited values) | 10 | ✅ pass (on a freshly reset stack) |
| `make verify-audit` on the live DB after E2E | — | ✅ `OK` (23 events, 1 version, 1 active asset) |
| `dotnet list package --vulnerable --include-transitive` / `npm audit --omit=dev` | — | ✅ none |

Backend total: **194** tests (M0: 121). Build is warning-free with `-warnaserror`.

### Manual verification (browser)

Walkthrough on the live stack at desktop width (1280 px) and phone width (390 px): registration form with synthetic data → draft → submit (registrar sees no decision panel) → approver queue → decision with note → asset page (code, status chips, QR, human code, approved versions, history) → A6 label → back. One UI defect was found and fixed in this step: the long version-snapshot value in the history widened the page (horizontal scroll). Values now wrap, and the E2E asserts that there is no horizontal overflow.

### Deviations from the specification

| Deviation | Why |
|---|---|
| **No photos on web registration** (spec: "optional photos") | Photos need the evidence pipeline (upload through the API, hashing, `IObjectStore`), which is an M3 deliverable. Adding a second, throw-away upload path now would be removed later. Registration works without them, and they were optional. |
| **Clock = .NET `TimeProvider`** instead of a custom `IClock` | The platform's own abstraction, with a fake for tests. `UtcNowMicros()` truncates to PostgreSQL precision so hashes match what is stored. |
| **`/api/v1/me` is not role-gated**; a first-time user is created **with no roles** and sees "no roles" | 11 §M1 item 4 requires "created or linked on first login". Access is then granted by role assignment (the management UI is in M4). |
| **`audit_events.facts` column** (not in 06) | For audited values that are not entity columns (e.g. `snapshot_hash` on `asset_version.created`). It is covered by the payload hash. |
| **Registration transitions R7/R8** (`UpdateRegistrationDraft` on DRAFT and RETURNED) added to the YAML | Editing a draft or a returned registration is needed by the flow. It is now an explicit, tested self-transition instead of a silent edit. |
| **`permissions.yaml`:** `registration.read` added (M1); `audit.read` → M3; `user.manage` → M4 | The approver queue and registration pages need a read permission. The audit search screen and user management are later milestones. Asset history uses `asset.history.read`. |
| **Street-light attributes are typed columns** (checked by domain validation), not a JSON-Schema document | Only one asset type in DEMO 1. Typed columns give DB constraints and simpler queries. Revisit when a second asset type is added. |
| **Location = `latitude`/`longitude` numeric + generated `geometry(Point,4326)`** with a GiST index | Exact values are kept for hashing and display. The geometry is always derived, never edited separately. |
| **`Idempotency-Key` header is stored as `command_id` only** (no de-duplication yet) | De-duplication by `command_id` is part of the single write path in M2. |
| **Tag is generated on approval** (DEMO 1 simplification in 11 §11.1) | Pre-printed controlled tag batches come in M7. Same QR format and resolution path. |
| **`RoleCodes` moved from `Access.Domain` to `Access.Contracts`** + new source-boundary test | Another module used `Access.Domain` constants. They are inlined at compile time, so the binary architecture test could not see it. |
| **E2E runs with one worker** | Parallel sign-ins of the same Keycloak user trigger its brute-force lockout (see warning 3). Total E2E time is about 15 s. |
| **"Print label" opens the A6 page in the same tab** (with a "back to asset" link) | Web tokens are in `sessionStorage`, which is per tab. A new tab would ask the user to sign in again. |

### Warnings and known limitations

| # | What | Status |
|---|---|---|
| 1 | **The verifier cannot detect deletion of the newest events (tail truncation)** or a consistent rewrite of the whole chain by a privileged DBA. It detects edits, gaps and broken links. | By design until **signed checkpoints (M8)**, as stated in ADR-0007. Wording everywhere remains "tamper-evident / verifiable", never "immutable". |
| 2 | Asset codes come from a PostgreSQL sequence. **A rolled-back approval leaves a gap** (e.g. SL-000004 never used). | Accepted. Codes are unique, not guaranteed contiguous. Documented here so that gaps are not mistaken for deletions. |
| 3 | Keycloak 26 reported `user_temporarily_disabled` when the **same user signed in twice within the same second** (parallel E2E). | Test issue fixed (serial E2E). For the field pilot, the brute-force settings (`failureFactor`, quick-login check) must be tuned when phone and web sign-ins of the same user are common. That review belongs to the M9 security pass. |
| 4 | **Changing `MFOP_TAG_CHECK_KEY` makes every printed label unresolvable** (the 2-character check code is an HMAC of the tag code). | Treat it as a long-lived deployment secret (backup, no rotation without re-labelling). It is a typo and forgery guard, not encryption. The QR itself contains only a URL and a code. |
| 5 | The asset-row check in `audit-verify` covers **ACTIVE street lights and their versioned fields**. `operational_status` is not in the snapshot (it changes through faults and work orders from M2). | Its changes are still covered by the audit chain. The verifier's scope will grow with M2/M3 entities. |
| 6 | The `libgssapi_krb5` message from the migrator and the ASP.NET DataProtection warnings are still present. | Harmless (see M0 #12, #13). |
| 7 | Sandbox only: the build proxy port changed after a container restart. The git-ignored override file was updated. | No product change. |

### New configuration

- `MFOP_TAG_CHECK_KEY` in `.env` (random, 32 characters). `make env` / `tools/dev/init-env.sh` now **appends new variables to an existing `.env`** without touching the existing secrets. Compose refuses to start the API without it.
- `Tags__BaseUrl` (Compose: `http://localhost:${WEB_PORT}/t`). This is configuration, not a hard-coded municipality fact. The organization name on the label comes from `web/public/config.json` (`organizationNameAr`, currently a placeholder "بلدية تجريبية").

### Decisions taken in M1 (owner's instructions applied)

- Android application id **`com.reuxstudio.mfop.field`** (Gradle namespace + applicationId + Kotlin package).
- Flutter stays on **3.41.6**. The network configuration is unchanged: APKs are built by CI.

### Decisions needed from the owner

1. **Approve M1** so that M2 can start.
2. Confirm the **asset-code format** `SL-000001` (prefix `SL`, 6 digits, sequential per deployment, gaps possible) and the **tag human-code format** `XXXXXXXX.CC` (Crockford base32, no I/L/O/U).
3. Confirm that **photos on registration** can wait for the evidence pipeline in M3 (they are optional in DEMO 1).

### CI results

Run [36012229581](https://github.com/narutokilowa-web/-/actions/runs/36012229581) on code head `f801ca3`: **success**, all 5 jobs.

| Job | Result |
|---|---|
| Secret scan (gitleaks CLI 8.28.0, full git history) | ✅ success |
| Web (lint, typecheck, 19 unit tests, production build, `npm audit`) | ✅ success |
| Backend (.NET build `-warnaserror`, 194 tests incl. Testcontainers PostGIS, vulnerable-package check) | ✅ success |
| Mobile (analyze, 3 widget tests, **debug APK with application id `com.reuxstudio.mfop.field`** → artifact `mfop-field-debug-apk`, 69.1 MB, available until 2026-10-08) | ✅ success |
| E2E (`make up` on a clean runner with random secrets, including the new `MFOP_TAG_CHECK_KEY`; 8 role sign-ins + wrong password + full M1 flow) | ✅ success |

The only commit after `f801ca3` changes this document (no code). CI runs on it as well.

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
| CI green | ✅ all 5 jobs green on code head `16c86a7` | run 36005311260 (also green on `bb0a372`, run 36004341994) |

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

### Decisions (resolved by the owner, 2026-09-24)

1. M0 **approved**.
2. Android build in this environment: **network configuration unchanged**; APKs come from CI.
3. Flutter: **stay on 3.41.6** (no upgrade during M1 without a concrete technical reason).
4. Android application id: **`com.reuxstudio.mfop.field`** (applied in M1).

### CI results

Final verification run [36005311260](https://github.com/narutokilowa-web/-/actions/runs/36005311260) on head `16c86a7`: **success** (the previous head `bb0a372`, run 36004341994, was also fully green).

| Job | Result |
|---|---|
| Secret scan (gitleaks CLI 8.28.0, full git history) | ✅ success |
| Web (lint, typecheck, 9 unit tests, production build, `npm audit`) | ✅ success |
| Backend (.NET build `-warnaserror`, 121 tests incl. Testcontainers PostGIS, vulnerable-package check) | ✅ success |
| Mobile (analyze, 3 widget tests, **debug APK** → artifact `mfop-field-debug-apk`, 69.1 MB, available until 2026-10-08) | ✅ success |
| E2E (`make up` on a clean runner with random secrets; Playwright sign-in for all 8 seeded users + wrong-password rejection) | ✅ success |

The only commit after `16c86a7` changes this document (no code); CI runs on it as well.
