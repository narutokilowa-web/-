# ADR-0008: DEMO 1 scope and deferrals

- **Status:** Accepted
- **Date:** 2026-09-24

## Decision
Milestones are ordered around **DEMO 1**, the 20-step street-light lifecycle demo on one machine; see `docs/lean-pilot/11-milestones-and-dod.md`.

**In DEMO 1:**
- the state machines for Asset/AssetVerification, Fault, WorkOrder, Assignment, Evidence, StockTransaction/MaterialUse, Approval/Closure and Audit
- a minimal device record
- one transparent **repeated-failure rule**, with threshold and window from `rule_settings` data

**Deferred until after DEMO 1, but mandatory before a real field pilot:**
- offline grants, trusted time, conflict resolution, device signing (M4)
- complete inventory (M5)
- integrity flags (M6)
- mobile registration and controlled tags (M7)
- WORM storage, signed checkpoints, TLS proxy, backups (M8)
- security pass and handover (M9)

## Deviations log
Any deviation discovered during implementation is appended here with the date and the reason.

- 2026-09-24 (M0): Flutter pinned to **3.41.6** because it is the newest version available as a Docker image with the Android SDK (the Android SDK download host is blocked in the build sandbox). CI uses the same version.
- 2026-09-24 (M1): photos on web registration deferred to the evidence pipeline (M3). They are optional in DEMO 1.
- 2026-09-24 (M1): registration YAML gained R7/R8 (`UpdateRegistrationDraft` on DRAFT/RETURNED). `permissions.yaml` gained `registration.read`; `audit.read` moved to M3 and `user.manage` to M4.
- 2026-09-24 (M1): street-light attributes are typed columns, not a JSON-Schema document (single asset type in DEMO 1).
- 2026-09-24 (M1): `audit_events.facts` holds audited values that are not entity columns (e.g. `snapshot_hash`). It is part of the hashed payload.
- 2026-09-24 (M1): `Idempotency-Key` is recorded as `command_id`. De-duplication arrives with the single write path (M2).
- 2026-09-24 (M2): `fault.yaml` corrected: a duplicate report (ConfirmFault) keeps the fault's state. F2 is now OPEN→OPEN and the new F7 is IN_WORK_ORDER→IN_WORK_ORDER (the old F2 sent IN_WORK_ORDER back to OPEN, detaching the fault from its work order).
- 2026-09-24 (M2): `wo_events` table deferred to M3 (first technician events; part of the work-order seal). M2 work-order timelines are built from the audit log, like the asset history.
- 2026-09-24 (M2): checklist-template pinning (W1 effect) and `rework_of_id` deferred to M3 (templates and closed work orders exist only from M3).
- 2026-09-24 (M2): SLA hours per priority are configuration (`WorkOrders:SlaHoursByPriority`), empty by default; no values are invented.
- 2026-09-24 (M2): `permissions.yaml` gained `device.read`, `fault.read`, `sync.push`, `sync.pull` (transport permissions; every pushed command is authorized by its own action).
- 2026-09-24 (M2): sync receipts and device-sync telemetry are `IAuditExempt` technical records (allow-list enforced by an architecture test); the commands they carry have their own audit events.
- 2026-09-24 (M2): the only pushable command in M2 is `ReportFault` (SUPERVISOR-only in DEMO 1); the phone's outbox exists and is tested, the first technician commands come in M3.
- 2026-09-24 (M2): `FLAG_SECURE` (no screenshots of evidence/asset screens) deferred to M3 with evidence capture.
- 2026-09-24 (M2): local database encryption uses SQLite3 Multiple Ciphers with the SQLCipher v4 scheme (bundled by the `sqlite3` package), instead of the SQLCipher library itself.
- 2026-09-25 (M3): the `before_photos` guard moved from W5 (ArriveOnSite) to **W6 (StartWork)**. Photos can only be announced once the technician is on site (AddEvidence is allowed in ON_SITE), so W5 cannot require them; W5 requires the tag scan (or a typed code with a reason). `work-order.yaml` gained an `in_state_commands` section (AddEvidence, RecordDiagnosis, ConsumeMaterial, RecordChecklist) with a conformance test.
- 2026-09-25 (M3): **location and presence capture** at arrival and on photos deferred to M6 (presence score, mock-location flag). Arrivals record the tag scan or typed code + reason, the device and the phone's clock.
- 2026-09-25 (M3): the **NO_WORK diagnosis path** (ResolveNoWork, W7: grid supply off, theft, vandalism, no fault found) stays deferred to M6 as planned; the phone offers only REPAIR codes for the light's power source. The repeated-failure rule already ignores the non-counting codes (unit-tested).
- 2026-09-25 (M3): **replacing a photo** (AddEvidence with `supersedesEvidenceId` + reason) exists in the API and is tested; the phone does not offer it yet. After a return the technician adds new photos (additions only).
- 2026-09-25 (M3): integrity **flags** (hash mismatch, duplicate photo, consumption beyond custody, serial not in custody) are raised and shown on the review screen; the auditor's decide/close workflow on flags is M6.
- 2026-09-25 (M3): the repeated-failure defaults (`threshold` 3, `window_days` 180) are seeded as **data** by the migrator (audited as SYSTEM) and changed by the admin on the settings page (audited, reason required). No value is read from code.
- 2026-09-25 (M3): the review's "opened at" time is not recorded (only decisions are). SLA hours remain unconfigured (unchanged from M2).
- 2026-09-25 (M3): **demo seed** (`make seed`, dev only): the two earlier repairs of the repeat-failure light are run through the real sync pipeline from a dev-only seed device for `tech1`, which `admin1` approves and then revokes (`SEED_COMPLETE`). Their photos are synthetic images made on the laptop. All seeded values are synthetic.
- 2026-09-25 (M3): photos on the phone are kept as BLOBs **inside the encrypted local database** (not loose files) until the server has them, then deleted. The phone re-encodes each photo (longest side ≤ 2048 px, JPEG quality 80, EXIF removed, ASCII stamp: work order, kind, UTC time) and hashes the final bytes.
- 2026-09-25 (M3): evidence verification and EXIF-free previews (SkiaSharp) run in a hosted service **inside the API process**; the separate worker container is M8. Storage is the local disk (`IObjectStore` local mode, banner "not WORM"); WORM / Object Lock is M8.
- 2026-09-25 (M3): the Android emulator test uses **synthetic camera frames**; everything after the shutter (processing, hash, outbox, upload, review, approval) is the real app and server. The live in-app camera is in the real-phone checklist (`docs/runbooks/android-field-test.md`).
- 2026-09-25 (M3): material cost on the review screen = quantity × the item's latest cost (Money: amount + ISO currency + date; the demo items are in SYP). No stock valuation in DEMO 1.
- 2026-09-25 (M4): **grant and device signatures use ECDSA P-256 / SHA-256 (DER)**, not Ed25519 as written in 05 §5.5. P-256 is built into .NET (`ECDsa`) and into the Android Keystore without any extra library. The server grant key is `Grants:SigningKey` (PKCS#8, base64, generated by `make env`, never committed) with key id `Grants:KeyId`; without it the API uses an ephemeral key and logs a warning (dev only).
- 2026-09-25 (M4): the phone **pins the server grant key per key id on first use** (trust-on-first-use over the signed-in HTTPS connection) and refuses a different key for a known id. A key rotation uses a new key id. A pre-provisioned key in the APK is left for M8/M9 (pilot build).
- 2026-09-25 (M4): **device-key signing is in M4** (the milestone table in 11 §11.2 and the owner's M4 instructions), not M6 as the token table in 05 §5.3 says. Every `POST /sync/v1/push` and `POST /sync/v1/grant` is signed over `METHOD\nPATH\nTIMESTAMP\nNONCE\nhex(sha256(body))` with a non-exportable Keystore key; nonces are single-use (`access.device_nonces`); timestamps may differ from the server by at most `Auth:SignatureMaxSkewMinutes` (15). The phone signs with **server-aligned time** (last server time + its monotonic clock), so a wrong phone clock does not block sync. Photo uploads are not signed: their bytes must match the hash announced in the signed `AddEvidence`.
- 2026-09-25 (M4): **trusted time** uses the phone's `elapsedRealtime` and Android's `BOOT_COUNT` (boot id `bc-N`), sent as headers on every request (a clock sample per device and boot) and stored with every command. No other device identifier or personal data is read.
- 2026-09-25 (M4): the **conflict matrix** (spec 10 §10.5) is implemented for work orders: commands carry `baseVersion` (the last `wo_events.seq` the phone saw). Changes (arrive, diagnosis, start, checklist, submit/resubmit) made on a stale view are refused (`WORK_ORDER_CHANGED`), never applied over newer server data; on a work order reassigned meanwhile, the previous assignee's **photos and materials are kept** with a `SUBMITTED_BY_NON_ASSIGNEE` flag while changes are refused (`WORK_ORDER_REASSIGNED`); work on a work order **cancelled** meanwhile is **quarantined** for a supervisor. Reactivating or re-linking a cancelled work order from the quarantine screen (spec 10 §10.5) is not built: the supervisor applies (which fails as `APPLY_FAILED` while the work order stays cancelled) or discards with a reason.
- 2026-09-25 (M4): a quarantined command can be applied only while its author's account and device are still ACTIVE (a revoked or offboarded person adds no new work); otherwise it can only be discarded with a reason. Applying runs the command as its author (their audit events, their device); the decision is a separate audit event of the supervisor.
- 2026-09-25 (M4): **offboarding** (05 §5.6) runs in one transaction; the Keycloak calls (disable user, logout all; enable; field-app logout on device revoke) are an outbox (`access.idp_tasks`) sent after commit by the API with exponential back-off, through a confidential service-account client `mfop-backend` (realm-management `view-users` + `manage-users` only; secret `MFOP_KEYCLOAK_BACKEND_SECRET`). After 5 failed attempts the Users page shows an alert and the log a CRITICAL entry; access is already blocked by our own checks. Enabling a user again does **not** restore devices: a new device must be approved. Offboarding also rejects a pending device registration.
- 2026-09-25 (M4): the **needs-reassignment queue** is computed from live user status (open work orders whose assignee is not ACTIVE) and a `NEEDS_REASSIGNMENT` flag is raised per work order at offboarding; `CUSTODY_TO_RECONCILE` is an INFO flag on the user. Returning the materials is a warehouse task (M5).
- 2026-09-25 (M4): **policies** are data in `access.policy_settings` (seeded defaults, admin changes with a reason, audited): `offline_grace_hours` 24 (1–72), `grant_window_tolerance_minutes` 10 (0–60), `app_lock_minutes` 5 (1–30), `min_app_version` 0.4.0. The phone receives them with every grant and pull.
- 2026-09-25 (M4): **resumable uploads** use `Content-Range` chunks through the API (`PUT /sync/v1/evidence/{id}/content`, `GET …/upload` for the resume point), not pre-signed multipart uploads to S3 (spec 10 §10.3): storage is still the local object store (ADR-0004/0005). The phone sends 256 KB chunks.
- 2026-09-25 (M4): phones registered before M4 have no device key and cannot sync until they register again with app 0.4.0 (the Devices page marks them); `min_app_version` 0.4.0 turns older apps away with 426.
- 2026-09-25 (M4): the new state machines `User` (U0 provision, U1 disable, U2 enable) and `SyncQuarantine` (Q1–Q4) use a new YAML scope value `m4`.
- 2026-09-25 (M4): local full-stack runs (Playwright, emulator) were done in CI only: this sandbox's proxy refuses `quay.io` (Keycloak image) and `dl.google.com` (Android SDK). Network settings were not changed.
