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
