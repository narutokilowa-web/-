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
