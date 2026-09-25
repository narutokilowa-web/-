# Implementation status

| Milestone | Status | Date |
|---|---|---|
| M0 — Foundations | ✅ **approved by the owner** | 2026-09-24 |
| M1 — Audit core + register / approve / QR (web) | ✅ **approved by the owner** | 2026-09-24 |
| M2 — Android scan + faults + work-order creation | ✅ **approved by the owner** | 2026-09-25 |
| M3 — Field execution + review + dashboard → DEMO 1 | ✅ **approved by the owner** (APK also installed on a real phone) | 2026-09-25 |
| M4 — Offline and BYOD hardening | ✅ **PASS — CI green on all 6 jobs incl. Android emulator; awaiting owner approval; M5 not started** | 2026-09-25 |

Milestone definitions: `docs/lean-pilot/11-milestones-and-dod.md`.

---

## M4 — Offline and BYOD hardening

Scope followed: 11 §11.2 M4 and the owner's M4 list (configurable offline grants, full offline workflow, grant expiry,
conflict detection and resolution, device-key signing, stronger device trust, complete offboarding, secure revocation,
sync recovery after reconnect, tests for offline and revoked-device cases). Nothing from M5 was started. The DEMO 1 UI
is unchanged except where M4 needs it (new admin/supervisor pages, two locked screens on the phone, refusal/held lines).
Personal-phone rules unchanged: no background tracking, no access to other data on the phone, no remote wipe; the app
deletes only its own data and its own Keystore key when the server refuses it.

### Deliverables vs. specification

| # | Deliverable | Status | Where |
|---|---|---|---|
| 1 | **Configurable offline grants** (05 §5.5): `POST /sync/v1/grant` (signed request) issues a server-signed grant (ECDSA P-256) for the caller's ACTIVE device; lifetime from policy `offline_grace_hours` (default 24, 1–72); a new grant revokes the previous one; revoked on device revoke and user disable; the phone verifies the signature with the key it pinned for the key id and renews the grant on every successful sync | ✅ | `Access/Application/GrantService.cs`, `GrantSigner.cs`, `mobile/lib/core/offline_grant.dart` |
| 2 | **Policies are data** (`access.policy_settings`): offline grace hours, grant-window tolerance (10 min), app lock (5 min), minimum app version (0.4.0); seeded by the migrator, changed by the admin with a reason (validated range, audited); sent to the phone with each grant and pull | ✅ | `PolicyService.cs`, web **Settings → Security policies** |
| 3 | **Server grant-window check on trusted time** (05 §5.5, §5.7): every command carries grant id, boot id and `elapsedRealtime`; the server places it on its own clock from a clock sample of the same boot (HIGH) or bounds the phone clock (LOW). Missing → `GRANT_REQUIRED`; foreign/unknown → `GRANT_INVALID` + WARNING flag; after expiry (HIGH) → `GRANT_EXPIRED` + WARNING flag; after revocation → `GRANT_REVOKED`; time not provable → **QUARANTINED** `GRANT_WINDOW_UNPROVEN`; `CLOCK_SKEW` INFO flag when the phone clock is > 5 min off | ✅ | `SyncService.cs`, `DeviceClockService.cs` |
| 4 | **Quarantine + supervisor resolution**: held commands (`sync.quarantine`) listed for SUPERVISOR/AUDITOR; a supervisor other than the author applies (runs as the author, only while the author's account and device are ACTIVE) or discards, both with a reason and audited; `APPLY_FAILED` recorded when the command no longer fits; a resent held command reports the decision | ✅ | `QuarantineService.cs`, `docs/state-machines/sync-quarantine.yaml`, web **Quarantine** |
| 5 | **Conflict detection and resolution** (spec 10 §10.5): commands carry `baseVersion`; a change on a stale view → `WORK_ORDER_CHANGED` (never overwrites); reassigned meanwhile → previous assignee's photos/materials **kept** + `SUBMITTED_BY_NON_ASSIGNEE` flag, changes refused (`WORK_ORDER_REASSIGNED`); cancelled meanwhile → **quarantined**; a stranger → `NOT_ASSIGNEE`; the phone shows refusals and held items on the task | ✅ | `FieldExecutionService.cs`, `mobile/lib/core/field_work.dart` |
| 6 | **Device-key signing / stronger device trust**: EC P-256 key in the Android Keystore (non-exportable), public key required at registration; push and grant signed over method, path, time, nonce and body hash; single-use nonces; ±15 min; stolen token without the key → `SIGNATURE_REQUIRED` / `SIGNATURE_INVALID`; replay → `NONCE_REUSED`; min app version → **426** `UPGRADE_REQUIRED` | ✅ | `DeviceTrustMiddleware.cs`, `MainActivity.kt`, `mobile/lib/core/api_client.dart` |
| 7 | **Complete offboarding** (05 §5.6): admin **Users** page → disable with reason (confirmation) → in one transaction: user DISABLED, active devices REVOKED (`USER_DISABLED`), pending registration REJECTED, grants revoked, Keycloak disable + logout-all queued, open work orders flagged `NEEDS_REASSIGNMENT`, custody ≠ 0 → `CUSTODY_TO_RECONCILE`; Keycloak calls after commit with back-off and an alert after 5 failures; **needs-reassignment queue** for supervisors; enable again restores the account but not the devices | ✅ | `UserAdminService.cs`, `IdentityProvider.cs`, `WorkOrderOffboarding.cs`, `docs/state-machines/user.yaml`, `docs/runbooks/offboarding.md` |
| 8 | **Secure revocation behaviour**: revoked device or disabled user → whole request refused (`DEVICE_REVOKED` / `USER_DISABLED`), nothing applied; grants revoked; device revoke also ends the field-app sessions in Keycloak; the app deletes only its own database, keys, tokens and its own device key | ✅ | `DeviceService.cs`, `app_controller.dart` |
| 9 | **Full offline workflow on the phone + locked states**: work continues offline while the grant is valid (measured on the monotonic clock; after a reboot the wall clock never below the last server time); when it runs out: **"Connect to continue working"** screen, no new work recorded, outbox kept; **update-required** screen on 426 | ✅ | `mobile/lib/app/app_controller.dart`, `device_screens.dart` |
| 10 | **Sync recovery after reconnect**: reconnect back-off (15 s → 15 min); outbox sent first (under the grant it was made with), then held commands rechecked, photos uploaded, grant renewed, tasks pulled; lost responses → resend reports `DUPLICATE`; **resumable photo uploads** (`Content-Range` chunks, resume point from the server) | ✅ | `sync_engine.dart`, `EvidenceService.UploadChunkAsync` |
| 11 | **Tests for offline and revoked-device edge cases** | ✅ | see below |

### Tests run (local, final)

| Suite | Tests | Result |
|---|---|---|
| `Mfop.SpecFiles.Tests` (+ User and SyncQuarantine machines == code; realm: `mfop-backend` is a service account with user management only, no committed secret) | 127 | ✅ |
| `Mfop.Domain.Tests` (+ app versions, policy ranges and defaults, user disable/enable SoD and reason, IdP back-off and alert, quarantine decision guards, grant key loads from PKCS#8 and SEC1, other curves refused) | 73 | ✅ |
| `Mfop.Architecture.Tests` (audit-exempt allow-list: nonces, clock samples, IdP outbox) | 38 | ✅ |
| `Mfop.Api.Tests` — **OfflineTrustTests** (11): signature required / stolen key / tampered body / old timestamp / replayed nonce; public key required; 426 and changing `min_app_version`; policy validation, reason, audit; grant signature verifies and its lifetime follows the policy; grant required / foreign grant flagged; **offline inside the grant, synced 30 h later → applied (HIGH, placed at +2 h)**; **made after expiry → `GRANT_EXPIRED` + flag**; made after revocation → `GRANT_REVOKED`; **double reboot → quarantined, author cannot decide, supervisor applies (author's audit, supervisor's decision), discard reported on resend**; phone clock 3 days back → one `CLOCK_SKEW`, time unaffected | 11 | ✅ |
| `Mfop.Api.Tests` — **SyncScenarioTests** (7): **reassigned while offline** (B's diagnosis stands; A's photo + material kept and flagged; stranger refused); **stale view** → `WORK_ORDER_CHANGED` then OK after refresh; own batch never conflicts; missing version refused; **cancelled while offline → quarantined**, apply → `APPLY_FAILED`, discard; **revoked phone** (push, pull, task, grant, upload all refused; grants revoked; Keycloak field-app logout); **complete offboarding** (one audited transaction, devices revoked/rejected, flags, push refused, held command discard-only, needs-reassignment queue, Keycloak retries + alert, enable keeps devices revoked); **interrupted upload resumes**; **lost response → resend DUPLICATE** | 7 | ✅ |
| `Mfop.Api.Tests` — all M1–M3 tests (now through a signed test phone with grants) | 95 | ✅ |
| `Mfop.GoldenPath.Tests` — **DEMO 1 steps 2–19 unchanged** on a `make seed` database (the seeder now registers a device key and works under a grant) | 1 | ✅ |
| Web: lint, typecheck, build; Vitest (+ offboarding confirmation and reason, admin cannot disable self, quarantine decisions only for others' commands, auditor read-only) | 30 | ✅ |
| Mobile: `flutter analyze` (no issues); `flutter test` (+ 11 M4 tests: key registered, writes signed, per-command grant/clock/version, **grant expiry on the monotonic clock with the phone clock set back**, reboot floor, bad/changed grant key refused, policy-driven lifetime, 426 screen, **disabled employee: own data and key deleted**, refused and held lines, **resumable upload after a cut-off**, reconnect back-off) | 39 | ✅ |
| Secret scan (gitleaks, working tree) | — | ✅ only the git-ignored local `.env` |

**Not run locally** (done by CI): the Compose stack (Playwright E2E incl. the new M4 spec with real Keycloak offboarding,
`make seed`, `make verify-audit`), the Android APK build and the Android emulator run. This sandbox's proxy refuses
`quay.io` (Keycloak image) and `dl.google.com` (Android SDK); network settings were not changed.

### Deviations from the specification

All recorded in ADR-0008 (M4 entries): ECDSA P-256 instead of Ed25519; trust-on-first-use pinning of the server grant key;
device signing in M4 (not M6); boot id from Android's boot count; reactivation of a cancelled work order from the
quarantine is not built (apply → APPLY_FAILED or discard); resumable uploads through the API (no S3 multipart yet);
YAML scope value `m4`.

### Warnings and known limitations

| # | What | Status |
|---|---|---|
| 1 | **Upgrade note:** the Keycloak realm gained the `mfop-backend` client — an existing dev Keycloak volume keeps the old realm; run `make demo-reset` once (and `make env` for the two new secrets). Phones must install app 0.4.0 and register again. | Documented (dev-setup) |
| 2 | Real-phone checks (Keystore on real hardware, offline hours with the clock set back, reassignment while offline, disabled account) are listed as steps 13–19 in `docs/runbooks/android-field-test.md`. | Owner checklist |
| 3 | The phone pins the server's grant key on first use; a key rotation needs a new key id. A key shipped inside the APK is for the pilot build (M8/M9). | By design |
| 4 | Without `MFOP_GRANT_SIGNING_KEY` the API signs grants with an ephemeral key (warning in the log); phones then simply renew their grant after an API restart. `make env` generates the key. | Dev only |

### Decisions needed from the owner

1. Approve M4 (M5 will not start before that).
2. Run steps 13–19 of the real-phone checklist, or accept the emulator result.
3. Still open from M2/M3: SLA hours per priority.

### CI results

Final run [36142939863](https://github.com/narutokilowa-web/-/actions/runs/36142939863) on head `88e103b`: **success, all 6 jobs** (Android emulator finished 14:13 UTC).

| Job | Result |
|---|---|
| Secret scan (gitleaks CLI, full history) | ✅ |
| Web (lint, typecheck, 30 unit tests, build, `npm audit`) | ✅ |
| Backend (build `-warnaserror`, 352 tests: spec files, domain, architecture, API on Testcontainers PostGIS incl. the device-trust and sync scenario suites, **DEMO 1 golden path**; vulnerable-package check) | ✅ |
| Mobile (analyze, 39 tests, **debug APK 0.4.0** → artifact `mfop-field-debug-apk`) | ✅ |
| E2E (Compose on a clean runner with freshly generated secrets and grant key + `make seed`: 9 role sign-ins, wrong password, M1, M2, **M3 / DEMO 1** and **M4** flows — security policies changed with a reason, **inspector1 offboarded → Keycloak sign-in refused → enabled again → sign-in works** — then `make verify-audit` OK) | ✅ |
| **Android emulator** (Android 14, real app, stack via `adb reverse`, after `make seed`): sign-in → **Keystore device key registered** (`hasKey`) → device approval → PIN → **server-signed offline grant received and verified** → My tasks → label QR decoded → arrive, photos, diagnosis, start, material, checklist, test and after photos, submit — **every command signed with the Keystore key, carrying grant and version, all accepted** → photos VERIFIED, supervisor approves → `CLOSED` with a seal → revocation: the app deletes its own data **and its own device key** (a new start creates a different key) | ✅ (screenshots: artifact `android-e2e-screenshots`) |

**Issues found by CI on the way (both M4, both fixed):**

1. **Grant signing key format** (run [36141306809](https://github.com/narutokilowa-web/-/actions/runs/36141306809), head `0c07fbf`, E2E job): `make env` generated `MFOP_GRANT_SIGNING_KEY` with `openssl genpkey -outform DER`, which writes a SEC1 `ECPrivateKey`, not PKCS#8; the API could not import it ("ASN1 corrupted data") and the Compose stack never became healthy. Fix `11a0a62`: `tools/dev/init-env.sh` converts with `openssl pkcs8 -topk8`; `GrantSigner` also accepts SEC1 (keys already in `.env` files keep working) and refuses keys that are not P-256; regression tests for both formats and a P-384 key. The local API tests had not caught it because they use an ephemeral key.
2. **Widget-test timing** (run [36142502826](https://github.com/narutokilowa-web/-/actions/runs/36142502826), head `11a0a62`, Mobile job): `work_screens_test` called `pumpAndSettle` ~200 ms after tapping a photo step while the photo was still being processed in a background isolate (a progress bar animates meanwhile); on a slow runner `pumpAndSettle` timed out. Fix `88e103b` (test only): the helper pumps until the progress bar is gone (bounded, then asserted) before settling. App behaviour unchanged.

The run on `0c07fbf` was cancelled by GitHub when `11a0a62` was pushed; the run on `11a0a62` was superseded by `88e103b`.

---

## M3 — Field execution + review + dashboard → DEMO 1

Scope followed: 11 §11.2 M3 (items 1–10) and the DEMO 1 Definition of Done (11 §11.3). Nothing from M4 has been started (no offline grants, trusted time, resumable uploads, device signing or offboarding). The M2 web UI and its visual direction are unchanged; new screens reuse the same styles, Arabic RTL first. Keycloak protections are unchanged (no security work was removed or weakened).

### Deliverables vs. specification

| # | Deliverable | Status | Where |
|---|---|---|---|
| 1 | **Evidence pipeline:** in-app camera only; on-device re-encode (≤ 2048 px, JPEG q80, **EXIF removed**, stamp) + SHA-256; `AddEvidence` announces the hash; `PUT /sync/v1/evidence/{id}/content` streams to the local object store while hashing; hosted verifier → `VERIFIED` / `HASH_MISMATCH` / `DUPLICATE` (+ CRITICAL flags); EXIF-free previews; previews only for authorized roles (bearer-token fetch), originals auditor-only and audited | ✅ | `Modules/Mfop.Evidence`, `Mfop.Infrastructure/Storage`, `mobile/lib/core/photo_processing.dart`, `web/src/features/workorders/EvidenceImage.tsx` |
| 2 | **Technician flow on the phone:** arrive (tag scan must match; typed code needs a reason) → 2 before photos → diagnosis (seeded codes, power-source filtered) → **Start work** → **one material from own custody** with serial → checklist of the pinned template + test method + test photo → ≥ 2 after photos → **Submit** (W9 guards, core return excluded); returned tasks show reasons and **Resubmit** (additions only) | ✅ | `mobile/lib/features/work`, `Modules/Mfop.WorkOrders/Application/FieldExecutionService.cs` |
| 3 | **Minimal inventory:** 8 seeded items with **SYP** costs (Money = amount + currency + date); `RECEIPT` of opening stock and one-step `ISSUE` to `tech1`/`tech2` by `warehouse1` in `make seed`; `CONSUME` linked to work order + asset; consumption beyond known custody applied and flagged on the review screen; **stock balances page** (warehouse + custody) | ✅ | `Modules/Mfop.Inventory`, `web/src/features/stock` |
| 4 | **Review and closure (web, supervisor ≠ executor):** photos, diagnosis, material with cost, checklist (unexpected answers highlighted), arrivals, submissions (what each resubmission added), flags; **Approve disabled until every photo is VERIFIED**; approve → `CLOSED` + **work-order seal**; return (reasons) → resubmit; light → `OPERATIONAL` (`UNKNOWN` if not tested); fault → `RESOLVED` | ✅ | `ReviewService.cs`, `WorkOrderSeal.cs`, `web/src/features/workorders/ReviewSection.tsx` |
| 5 | **Dashboard:** lights by operational status, open faults (and how many reported without power), open work orders by state, pending review, **recently completed work orders** linking to each (with seal) | ✅ | `web/src/features/dashboard` |
| 6 | **Audit history:** work-order and asset timelines show every command with actor, **device**, time, reason, old → new, event hash; **auditor search by entity** (`GET /api/v1/audit/events`) | ✅ | `Modules/Mfop.Audit/Api`, `web/src/features/audit` |
| 7 | **Verifier extended:** recomputes each `CLOSED` work order's seal from operational tables (independent code in `tools/audit-verify`), compares it with the seal in the audit chain, and recomputes stock balances from movements | ✅ | `tools/audit-verify/WorkOrderSeals.cs` |
| 8 | **Golden path + demo:** `Mfop.GoldenPath.Tests` runs DEMO 1 steps 2–19 through the public API on a `make seed` database; `docs/runbooks/demo-1.md` click-by-click incl. the tamper step (`make tamper-demo` → `make verify-audit` names the seal mismatch) | ✅ | `backend/tests/Mfop.GoldenPath.Tests`, `docs/runbooks/demo-1.md` |
| 9 | **Tests:** YAML tests for WorkOrder (incl. new `in_state_commands`), Evidence and StockLedger machines; N2–N4, N6, N7, N9, N10, N13, N14 | ✅ | see below |
| 10 | **Minimal repeated-failure rule:** evaluated when a fault becomes RESOLVED; counts RESOLVED faults whose resolving work order's diagnosis has `counts_as_failure`, within the window; non-counting codes ignored; `threshold` 3 / `window_days` 180 **from `rule_settings`** (admin settings page, audited, reason required); asset page and phone asset card show **"عطل متكرر — توصية: يُنصح بفحص شامل"** with the counted work orders, threshold and window; `make seed` creates the light with 2 earlier repairs | ✅ | `Modules/Mfop.Rules`, `web/src/features/assets/RecommendationBanner.tsx`, `web/src/features/settings` |

### DEMO 1 Definition of Done (11 §11.3)

| Item | Result |
|---|---|
| Clean clone → `make up && make seed` on one laptop, ≤ 15 min, no government/foreign-cloud dependency | ✅ `make demo-reset` + `make seed` ≈ 6 min with images cached (seed itself ≈ 10 s); first build downloads base images. Running stack ≈ 1.0 GB RAM (Keycloak 0.72, API 0.18, DB 0.09 GB) |
| Real Android phone with `adb reverse` + debug APK | ⚠️ Not available to me. Covered by the Android **emulator** job in CI (real APK) and the owner checklist `docs/runbooks/android-field-test.md` |
| No secrets committed; `.env.example` complete | ✅ gitleaks (full history) green; no new variables needed in `.env` for M3 |
| Steps 1–20 performed following `docs/runbooks/demo-1.md` | ✅ on the Compose stack: steps 2–19 by the golden-path test (API), the M3 Playwright spec (web screens) and the emulator test (phone screens); step 20 by `make tamper-demo` + `make verify-audit` |
| Every command writes its audit event in the same transaction (actor, device, time, reason, old/new) | ✅ UnitOfWork refuses a change without an audit record; audit-action tests per command |
| `make verify-audit` exits 0 on the untouched demo DB | ✅ (seeded DB, and again after all E2E flows) |
| After step 20 the verifier exits non-zero and names the tampered entity | ✅ `SEAL_MISMATCH … WO-2026-000004` + two `STOCK_BALANCE_MISMATCH` findings; exit code non-zero |
| Step 17a recommendation with counted faults, threshold and window from `rule_settings` | ✅ golden path, Playwright, and a test that a changed setting takes effect |
| Maker ≠ checker (N1, N2) | ✅ |
| "Tamper-evident / verifiable", never "immutable" | ✅ locale test forbids the word; code comments and seed text reworded |
| Golden path test green in CI; YAML tests; matrix tests; audit-in-transaction tests; N1–N7, N9–N15 | ✅ (see CI results) |
| Web and mobile main flows in Arabic RTL; remaining English strings listed | ✅ Arabic by default on both. English that remains visible: SKU codes, serials, hashes, asset/work-order codes, the ASCII photo stamp, and server error codes shown inside the generic Arabic error message |
| Phone flow on a low-to-mid Android device without crashes; interaction time recorded | ⚠️ Emulator only; interaction time to be recorded in the real-phone checklist |
| ADR-0008 updated with deviations | ✅ |

### Tests run (local, final)

| Suite | Tests | Result |
|---|---|---|
| `Mfop.SpecFiles.Tests`: + Evidence and StockLedger machines, work-order `in_state_commands` == code | 105 | ✅ |
| `Mfop.Domain.Tests`: + repeated-failure rule (threshold − 1, threshold, outside window, excluded codes, changed/disabled setting), evidence E1–E5, stock ledger, in-state commands, seal formats | 46 | ✅ |
| `Mfop.Architecture.Tests`: + Rules, Inventory, Evidence modules | 38 | ✅ |
| `Mfop.Api.Tests` (real PostGIS): full field flow + seal + verifier, **N2, N3, N4, N6, N7, N9, N10, N13, N14**, start-work guards, arrival tag rules, return/resubmit additions, custody over-consumption flags, checklist blocking/failed/not tested (→ UNKNOWN), preview/original authorization, upload ownership / non-JPEG / repeated upload, **rule setting change takes effect**, matrix now at M3 | 95 | ✅ |
| `Mfop.GoldenPath.Tests`: DEMO 1 steps 2–19 on a `make seed` database, verifier OK at start and end | 1 | ✅ |
| Web: lint, typecheck, build; Vitest (+ money/quantity, photo order, checklist mismatch, balances, audit query, recommendation banner, device in timeline) | 28 | ✅ |
| Mobile: `flutter analyze` (no issues); `flutter test` (+ photo processing: size, EXIF stripped, hash; outbox order and upload-after-APPLIED; refused announcement; offline queue; typed tag code normalised; returned task; widget-level field flow) | 28 | ✅ |
| E2E (Playwright vs. Compose after `make seed`): 9 role sign-ins, wrong password, M1, M2 and **M3 / DEMO 1** flows | 13 | ✅ |
| `make verify-audit` on the seeded DB, after all E2E, after `make tamper-demo` (fails as expected), after `make tamper-undo` | — | ✅ |

### Deviations from the specification

All are recorded in ADR-0008 (2026-09-25 entries). In short: the before-photos guard is on StartWork (W6), not arrival (W5); location/presence capture, the NO_WORK path and the flag workflow stay in M6; photo replacement exists in the API but not yet on the phone; review "opened at" not recorded; SLA hours still unconfigured; the verifier and previews run inside the API process (worker container in M8); the emulator test uses synthetic camera frames.

### Warnings and known limitations

| # | What | Status |
|---|---|---|
| 1 | No physical phone here: browser login, **live in-app camera**, FLAG_SECURE and interaction time need the real-phone checklist (`docs/runbooks/android-field-test.md`, now with steps 10a–10e). | Owner checklist |
| 2 | This sandbox can no longer reach Google's Maven (`dl.google.com` returns 403 at the proxy), so the debug APK with the new `camera` plugin could not be built **locally**; it is built by the CI Mobile and Android jobs. Network settings were not changed. | CI builds it |
| 3 | Evidence is on the laptop's disk (local object store, banner "not WORM"); signed checkpoints are not implemented. Both are mandatory before a real pilot (M8). | By plan |
| 4 | The in-process verifier polls every 2 s; approve becomes available a few seconds after the last upload. | Acceptable for DEMO 1 |
| 5 | The API image grew by ~40 MB (SkiaSharp native library for previews); non-Linux native libraries are removed from the image. | Accepted |
| 6 | The migrator prints "Cannot load library libgssapi_krb5.so.2" (Npgsql's optional Kerberos probe). Harmless, as in M1/M2. | Unchanged |
| 7 | Harmless build warning: web bundle > 500 kB (Vite chunk-size hint). | Unchanged |

### Decisions needed from the owner

1. **Approve M3 / DEMO 1** (M4 will not start before that).
2. **Run the real-phone checklist** (`docs/runbooks/android-field-test.md`, ≈ 20 min) or accept the emulator result for the phone steps.
3. SLA hours per priority (still open from M2).

### CI results

Final run [36118299002](https://github.com/narutokilowa-web/-/actions/runs/36118299002) on head `df67bcc`: **success**, all 6 jobs (Android emulator finished 09:55 UTC). The same 6 jobs were also green on the last code change `919f4b3` (run [36115603952](https://github.com/narutokilowa-web/-/actions/runs/36115603952)); `df67bcc` only updated this document.

| Job | Result |
|---|---|
| Secret scan (gitleaks CLI, full history) | ✅ |
| Web (lint, typecheck, 28 unit tests, build, `npm audit`) | ✅ |
| Backend (build `-warnaserror`, 285 tests: spec files, domain, architecture, API on Testcontainers PostGIS, **DEMO 1 golden path**; vulnerable-package check) | ✅ |
| Mobile (analyze, 28 tests, **debug APK with the in-app camera** → artifact `mfop-field-debug-apk`) | ✅ |
| E2E (Compose on a clean runner + `make seed`: 9 role sign-ins, wrong password, M1, M2 and **M3 / DEMO 1** flows, then `make verify-audit` OK) | ✅ |
| **Android emulator** (Android 14, real app, stack via `adb reverse`, after `make seed`): sign-in → device approval → PIN → My tasks → label QR decoded on the device → asset card → **arrive, 2 before photos, diagnosis, start, material `M-0003` from custody, checklist, test photo, 2 after photos, submit** (on-device processing, hashing, encrypted queue, upload; nothing left waiting) → server: 5 photos VERIFIED, supervisor approves → `CLOSED` with a seal → the task leaves My tasks → revocation wipes only the app's own data | ✅ (screenshots: artifact `android-e2e-screenshots`) |

One real bug was found by the emulator run on the way (previous head `9352938`): a tag code typed in the "damaged tag" field was sent as typed (a pasted label URL, upper-cased), so the server refused the arrival. The phone now normalises a typed code exactly like a scan; a regression test was added (`919f4b3`).

---

## M2 — Android scan + faults + work-order creation

Scope followed: 11 §11.2 M2 (items 1–7). Nothing from M3 has been started: no evidence, arrival, diagnosis, materials, checklist, review, dashboard or seal.

### Deliverables vs. specification

| # | Deliverable | Status | Where |
|---|---|---|---|
| 1 | **Flutter app:** Keycloak login (system browser, PKCE) through `adb reverse`; device registers itself (`PENDING_APPROVAL`); admin/supervisor approves on the web **Devices** page; revoked devices rejected by the API **on every request**; app PIN; encrypted local DB | ✅ | `mobile/lib/`, `Modules/Mfop.Access`, `web/src/features/devices` |
| 2 | **Scan → asset record:** camera QR scan + manual code entry → `GET /sync/v1/tags/{code}` → asset card (code, type, power source, operational status, responsible party, open faults, my open task, last events) | ✅ | `mobile/lib/features/scan`, `Modules/Mfop.Sync` |
| 3 | **Single write path:** `POST /sync/v1/push` (same command pipeline, idempotent by `command_id`, per-command results); mobile persisted command queue with retry. No conflict resolution, no grants | ✅ | `Modules/Mfop.Sync`, `mobile/lib/core/outbox.dart`, `sync_engine.dart` |
| 4 | **Faults (web, supervisor):** "Report fault" on the asset page (category, power at inspection + optional source, description); duplicate → confirmation; dismiss with a reason; light → `FAULTY` per 04 §4.6 | ✅ | `Modules/Mfop.Faults`, `web/src/features/faults` |
| 5 | **Work orders (web, supervisor):** create from a fault (fault → `IN_WORK_ORDER`), assign / reassign **only to a technician with an ACTIVE device**, cancel before evidence; work-order page with timeline | ✅ | `Modules/Mfop.WorkOrders`, `web/src/features/workorders` |
| 6 | **Mobile "My tasks":** assigned work orders (pull), open a task and see the asset | ✅ | `mobile/lib/features/tasks` |
| 7 | **Tests:** YAML tests for Fault, WorkOrder (W1–W4 implemented; whole DEMO 1 table conformance-tested) and Assignment; matrix tests for M2 endpoints; **N5**, **N15** (and **N8**, planned for later, already covered) | ✅ | see below |

### Acceptance: "DEMO 1 steps 6–9 work on a real Android phone"

| Step | Verified how | Result |
|---|---|---|
| 6 Scan the printed QR from Android | **Android emulator (Android 14, x86_64) in CI, real app build:** the label's QR payload is rendered as an image and decoded **on the device** by the app's scanner library (ML Kit via `mobile_scanner`); the decoded value goes through the Scan tab → asset card. Unit/widget tests: QR URL and typed-code parsing | ✅ CI run 36026280425 |
| 7 See the asset record | Emulator: asset card shows the asset code, "معطّل", and "You have a task on this light" | ✅ CI run 36026280425 |
| 8 Create a fault | Web (Playwright, real Keycloak login): supervisor reports a fault on the asset page → light turns FAULTY; API tests (dedup, power unavailable, OTHER, dismiss) | ✅ |
| 9 Create and assign a work order | Web (Playwright): fault → work order → assign to `tech2`, whose phone was approved on the Devices page; the task is in tech2's pull; then revoke → `DEVICE_REVOKED` | ✅ |
| "the task appears on the technician's phone" | Emulator: after sync, **My tasks** lists the work order; opening it shows the asset | ✅ CI run 36026280425 |
| Device registration and revocation | Emulator: sign in → "waiting for approval" → admin approves → PIN → … → admin revokes → the app shows "device revoked" and has deleted its own tokens, device id, PIN and database key | ✅ CI run 36026280425 |

**What was NOT verified by me, and why.** No physical phone is attached to this environment. Two things therefore still need a person with a real phone (`docs/runbooks/android-field-test.md`, 12-step checklist, about 15 minutes):
1. **Keycloak login in the phone's browser.** The automated test performs the same Authorization Code + PKCE exchange with Keycloak over HTTP, because a test cannot type into the system browser. The tokens, audience and every server check are the real ones.
2. **Live camera scan of a printed label.** The automated test decodes the label QR from an image with the same on-device scanner. Lighting, focus, dirt and distance are untested.

### Tests run

| Suite | Tests | Result |
|---|---|---|
| `Mfop.SpecFiles.Tests`: + Device, Fault, WorkOrder, Assignment machines == YAML; every unlisted (state, command) pair rejected; terminal states accept nothing | 100 | ✅ |
| `Mfop.Domain.Tests`: + device D1–D4 and effects, fault confirmation/dismissal, power observation list, work-order W2–W4, operational status OS4 | 29 | ✅ |
| `Mfop.Architecture.Tests`: + Sync module boundaries; **only allow-listed technical records may be audit-exempt** | 34 | ✅ |
| `Mfop.Api.Tests` (real PostGIS): devices (register, supersede, approve, own-device SoD, reject, revoke → next request refused on every endpoint, field-app token without device refused, borrowed device refused); faults (OS4 + status event in history, dedup → confirmation, power unavailable / OTHER leave status unchanged, ASSET_NOT_ACTIVE, dismiss rules); work orders (W1 + fault F4, code format, no invented SLA, duplicate WO refused, **N15**, W2 guards incl. ASSIGNEE_NO_ACTIVE_DEVICE, reassign with reason, **N5**, cancel → fault reopened + assignment ended, terminal, F7); sync (applied once, DUPLICATE on resend, COMMAND_ID_CONFLICT, per-command results in order, rejected commands recorded without audit events, per-command permission, **N8** revoked push applies nothing, telemetry, receipts append-only); audit actions per command; authorization matrix now covers `/sync/*` and all M2 actions | 79 | ✅ |
| Web (Vitest): + fault form | 21 | ✅ |
| Web lint + typecheck + build | — | ✅ |
| Mobile (`flutter test`, host): encrypted DB at rest (file not plaintext, wrong key fails, destroy deletes the file); controller flow (privacy → sign-in → registration → waiting → approval → PIN → tasks; rejected → re-register; background lock + PIN + lockout after 5; revoked → wipes only own data; offline keeps cached tasks); outbox order/idempotency/offline retry; PIN hashing; tag parsing; screens (My tasks → task detail, Scan typed code → asset card, Me) in Arabic RTL; 56 dp targets | 18 | ✅ |
| `flutter analyze` | — | ✅ no issues |
| E2E (Playwright vs. Compose): 9 role sign-ins (incl. new `tech2`), wrong password, M1 flow, **M2 flow** | 12 | ✅ |
| Android emulator integration test (CI) | 1 flow, 8 screenshots | ✅ CI run 36026280425 |
| `make verify-audit` after all E2E | — | ✅ OK (42 events) |

### Deviations from the specification

| Deviation | Why |
|---|---|
| **`fault.yaml` corrected** (F2 split into F2 OPEN→OPEN and F7 IN_WORK_ORDER→IN_WORK_ORDER) | The spec's F2 sent a fault that is in a work order back to `OPEN` on a duplicate report, detaching it from its work order. 04 §4.7 says a duplicate is only a confirmation. |
| `wo_events` table deferred to M3; work-order timeline from the audit log | No technician events exist before M3; the M3 seal needs them. Same approach as the M1 asset history ("history is a view"). |
| Checklist-template pinning and `rework_of_id` (W1 effects) deferred to M3 | Templates and closed work orders only exist from M3. |
| **No SLA values**: `sla_due_at` stays empty until `WorkOrders:SlaHoursByPriority` is configured | The specification defines no hours; inventing them would be inventing municipality policy. |
| Work-order code `WO-<year>-<n>`: `<n>` is one sequence for all years | A per-year counter needs a counter table; the year prefix is from the organization time zone (`Asia/Damascus`, configurable). Gaps possible, as with asset codes. |
| `permissions.yaml` + `device.read`, `fault.read`, `sync.push`, `sync.pull` | Needed by the Devices page, the fault lists and the sync transport. Every pushed command is still authorized by its own action. |
| `GET /sync/v1/devices/current` and `GET /sync/v1/work-orders/{id}` added | The phone needs its own status while waiting, and a fresh copy of a task (also the N5 check). |
| Sync receipts and device-sync telemetry are **audit-exempt** technical records | A receipt is not business state; the applied command's audit events carry the same `command_id`. Allow-list enforced by an architecture test. |
| Only `ReportFault` can be pushed in M2 (SUPERVISOR-only in DEMO 1) | The first technician commands (arrival, evidence…) are M3. The phone's outbox is complete and tested but carries no command in the M2 phone UI. |
| A user cannot approve or reject **their own** device | Not stated in 04 §4.1; follows the "no self-grant" rule of 03. |
| Field-app tokens (`azp = mfop-mobile`) are refused without an ACTIVE device, on **all** endpoints | Otherwise a revoked phone could fall back to the web API with its token. |
| Local DB: SQLite3 Multiple Ciphers with the **SQLCipher v4 scheme** (bundled by the `sqlite3` Dart package), raw 256-bit key in Keystore-backed `flutter_secure_storage` | Same file format/security as SQLCipher; no separate native SQLCipher build to maintain. |
| Keycloak session logout on revocation, offline grants, trusted time, device signatures | M4/M6 by plan. Our API already blocks a revoked device on every request. |
| `FLAG_SECURE` (no screenshots of sensitive screens) deferred to M3 | It matters for evidence screens (M3). |
| New dev user **`tech2`** (Keycloak realm + seed) | Reassignment needs a second technician. |
| Sign-out on the phone forgets the app's tokens; it does not end the Keycloak browser session | Ending the browser session needs another browser round trip; revisit in M4 (offboarding). |

### Warnings and known limitations

| # | What | Status |
|---|---|---|
| 1 | Real-phone checks not done by me (browser login UI, live camera). | Owner checklist `docs/runbooks/android-field-test.md`. |
| 2 | The CI emulator job needs Google/GitHub downloads (system image, Gradle, ML Kit, SQLite3MC binaries). It takes ~15–25 min. | Accepted; it is the only automated Android check. |
| 3 | `sqlite3` Dart package pinned to **3.5.2**: 3.6 needs a newer `meta` than Flutter 3.41.6 ships. | Revisit when Flutter is upgraded. |
| 4 | The phone polls for approval every 10 s while on the waiting screen, and syncs every 15 min in the foreground. There is no push notification (no Google services assumed). | By design (05 §5.1). |
| 5 | App PIN: after 5 wrong entries the app forgets its session (sign-in with password needed). No time-based back-off. | Reasonable for the pilot; review in M9. |
| 6 | The Android integration test contains the dev password only in its CI-built test APK (`--dart-define`), never in the repository or the published APK artifact. | Accepted. |
| 7 | Harmless warnings from M0/M1 remain (Kerberos probe, DataProtection). | Unchanged. |

### Decisions needed from the owner

1. **Approve M2** so M3 can start.
2. **Run the real-phone checklist** (`docs/runbooks/android-field-test.md`, ~15 min) or tell me to accept the emulator result for DEMO 1 steps 6–9.
3. **SLA hours per priority** (P1–P4), or confirm that work orders stay without due times for now.

### CI results

Run [36026280425](https://github.com/narutokilowa-web/-/actions/runs/36026280425) on code head `982052e`: **success**, all 6 jobs.

| Job | Result |
|---|---|
| Secret scan (gitleaks CLI, full history) | ✅ |
| Web (lint, typecheck, 21 unit tests, build, `npm audit`) | ✅ |
| Backend (build `-warnaserror`, 242 tests incl. Testcontainers PostGIS, vulnerable-package check) | ✅ |
| Mobile (analyze, 18 tests, **debug APK** → artifact `mfop-field-debug-apk`) | ✅ |
| E2E (Compose on a clean runner: 9 role sign-ins, wrong password, M1 flow, **M2 flow**) | ✅ |
| **Android emulator** (Android 14; real app build; stack via `adb reverse`): privacy notice → Keycloak PKCE sign-in as `tech1` → device registered (model/Android version seen by the server) → approved by `admin1` → app PIN → supervisor creates fault + work order and assigns it → **My tasks** shows it → task detail shows asset code and "معطّل" → the label QR decoded **on the device** by ML Kit equals the printed payload → asset card with "you have a task on this light" → `admin1` revokes → app shows "device revoked" with its refresh token, device id and PIN deleted | ✅ (screenshots: artifact `android-e2e-screenshots`, 8 images, kept until 2026-10-08) |

Two CI-only problems were fixed on the way (no product change): the runner's disk was too small for the emulator's 7.4 GB data partition (unused preinstalled toolchains are now removed first), and the test's `pageBack()` helper did not find the AppBar back button (the test now pops the route directly).

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
