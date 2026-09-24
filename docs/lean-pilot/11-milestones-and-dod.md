# 11 — Sequential Milestones and Definition of Done

**Team assumption:** 1–2 people using AI-assisted development (Claude Code / Codex for implementation and review), with a human owner approving every milestone.
**Estimates** are in working weeks for that team. They are uncertain (±30%) and will be re-estimated after M1.
**Rule:** every milestone ends with something **runnable and demonstrable**, merged through reviewed PRs with green CI. No milestone starts before the previous one is accepted.

## 11.1 Milestones

| # | Milestone | Deliverables | Accepted when | Est. |
|---|---|---|---|---|
| **M0** | **Foundations** | ADRs (stack; single-organization deployment; auth split Keycloak/our backend; API-streamed uploads; storage product **after licence/image check**; no Redis/broker) · `AGENTS.md` · repo skeleton per 01 §1.3 · Docker Compose with `proxy`, `db`, `keycloak` (realm import), `storage` (+ init with Object Lock buckets), `api`/`worker`/`web` hello-world · `docs/state-machines/*.yaml` and `permissions.yaml` from 03/04 · CI: build, test, lint, format, secret scan, dependency scan | From a clean clone: `make up` brings everything healthy on one machine in ≤ 15 min; CI green; YAML files reviewed | 1 |
| **M1** | **Kernel, audit, access** | `Kernel` (Result, Money, Clock, UUIDv7, state-machine base, CommandEnvelope) · `UnitOfWork` with audit-in-transaction · audit schema, grants, triggers, hash chain · `tools/audit-verify` CLI · Access: users, role assignments, static + dynamic SoD, policy settings (range-validated) · web login (Keycloak PKCE) · authorization pipeline + generated matrix tests · web Admin: users & roles | Admin creates users and grants roles in the web UI; SoD conflicts rejected; every change audited; tamper tests N11/N12 pass; `make verify-audit` green | 1.5–2 |
| **M2** | **Street lights, tags, server-side sync + evidence, seeded history** | Assets module: street light model + JSON Schema, registration state machine (backend), approval commands (web), asset versions + snapshot hash · **server side of the mobile path**: `/sync/v1/push` (command pipeline over HTTP, idempotency, per-command results; no device signing yet) and the **evidence pipeline** (announce → streamed upload → verify → Object Lock copy → derived previews; hash-mismatch and duplicate flags) · tag batches + printable PDF (`tools/tag-print`) · asset list + asset page with **history view** (web) · `tools/seed`: creates users, a pre-approved **dev seed device**, 300 lights registered **through `/sync/v1/push` with synthetic JPEG evidence** and approved by an approver user through the web API, plus items and opening stock | `make seed` yields approved lights with genuine audit trails and verified evidence; the asset page shows registration history; printed tag sheet scans with a phone camera app; evidence tests (N9, N10) pass | 1.5 |
| **M3** | **Mobile foundation** | Flutter shell (Arabic RTL, night mode) · OIDC login · **device registration + approval (web) + revocation (server-side)** · offline grant (issue, local enforcement, renewal) · SQLCipher DB · outbox + `/sync/v1/push` + `/pull` · **scan → asset card** (local index + online resolve) · fault report with power observation (+ dedup) · supervisor fault list (web) | On a real Android phone: install APK → login → approved → scan a printed tag → asset card → report a fault **in airplane mode** → it syncs later and appears on the web; revoking the device blocks the phone's next request | 1.5–2 |
| **M4** | **Work orders I + mobile evidence capture** | WO state machine (backend, all transitions) · web: create from fault, assign/reassign/cancel · mobile: My tasks, Arrive (tag scan), Before photos, Diagnosis, Start, Hold, Resolve-no-work · mobile evidence: in-app camera, on-device hash, encrypted local store, upload queue to the M2 pipeline · presence score · flags for mock location and weak presence | A technician on a phone completes arrival → before photos → diagnosis → start; the supervisor sees verified previews; tests N3–N7 pass | 1.5 |
| **M5** | **Work orders II + minimal inventory + review → FIRST DEMO** | Inventory: receipt, issue, **acknowledge on phone**, consume (serials), core return declare/receive, unused return · checklist templates (grid/solar) + test step + after photos + submit · web **review screen** (approve/return, diff between submissions) · close: seal, asset status, fault resolution · **repeated-failure rule + recommendation** · asset page timeline with audit links · auditor: evidence chain + verify · `Mfop.GoldenPath.Tests` · `docs/runbooks/demo-script.md` | **First runnable demo DoD (11.2) met** | 2 |
| **M6** | **Offline and BYOD hardening** | Trusted time (clock samples, estimation, confidence) · server grant-window rules + quarantine + supervisor resolution UI · full sync conflict scenarios · resumable uploads · **device-key signing** of push/grant · offboarding flow (disable user → Keycloak logout-all → grants revoked → reassignment queue; custody flag) · locked states (grant expired, revoked, update required) · min app version | All sync scenario tests green; a manual test with the phone clock changed and offline for longer than the grace period behaves as specified in 05 | 1.5 |
| **M7** | **Registration and field check on mobile** | Registrar flow (tag from own batch, location accuracy, proximity warning + justification, photos, attributes, power source, responsible party) · approver web review (photos, map, nearby lights, flags) · request field check → inspector flow (registrar photos hidden until capture) | A new light goes from phone capture to `ACTIVE` with a different approver; the proximity flag is shown; a `NOT_FOUND` field check auto-rejects | 1 |
| **M8** | **Oversight: flags, dashboard, map, checkpoints** | Auditor flag workflow · supervisor/auditor dashboard cards (09 §9.5) · MapLibre map with status colors and clustering (optional basemap) · daily signed checkpoints to the `audit-anchors` bucket + download · (optional) perceptual-hash similarity | The auditor can go from a flag to the evidence chain and verify it; a checkpoint verifies with the auditor's public key | 1 |
| **M9** | **Pilot-readiness hardening and handover pack** | Security pass (OWASP ASVS L2 subset checklist, ZAP baseline) · performance on a low-end phone · backup/restore runbook for the compose stack (DB dump + bucket copy) **tested** · Arabic terminology review with real technicians · `docs/handover/` (architecture tour, how to run, how to extend, where the rules live) in Arabic and English · `make demo-reset` | Pilot-ready DoD (11.3) met | 1–1.5 |

**Expected timing:** first demo at the end of **M5 ≈ weeks 9–11**; pilot-ready software at the end of **M9 ≈ weeks 14–18**.

Order rationale: the first demo needs identity, audit, assets, mobile, work orders, evidence and minimal inventory, so those come first. Registration **on mobile** is deferred to M7 because seeded registrations made through the real API already produce genuine approval history. Offline **architecture** (outbox, command IDs, single write path) is built in M3 and used from then on. Only the edge-case hardening waits until M6.

## 11.2 Definition of Done — first runnable demo (end of M5)

### Environment
- [ ] From a clean clone on **one machine**: `make up && make seed` finishes in ≤ 15 minutes, the stack uses ≤ 6 GB RAM, and no government or foreign-cloud service is needed.
- [ ] A real Android phone (8+) connects over USB (`adb reverse`) or LAN with the debug CA.
- [ ] Storage runs with **Object Lock enabled** (not the dev-fs fallback).
- [ ] No secrets in the repository; `.env.example` documents every variable.

### Demo scenario (scripted in `docs/runbooks/demo-script.md`; all must work)
1. The supervisor sees fault reports on the web (one reported from the phone during the demo, with power observation `CONFIRMED_AVAILABLE`).
2. The supervisor creates a work order for `SL-0000xx` and assigns Technician A.
3. The warehouse issues a driver (serial) to Technician A. **Technician A acknowledges it on the phone.**
4. Technician A **scans the printed QR tag**. The asset card shows the light, its status, and "2 failures in the last 180 days".
5. **The phone is switched to airplane mode.**
6. Arrive (scan) → 2 before photos (tag visible) → diagnosis `DRIVER_FAIL` → start work.
7. Consume the driver (serial scanned) → declare old driver "with part" + photo.
8. Test method "photocell covered" → checklist → test photo → 2 after photos → submit.
9. **Airplane mode off.** Everything syncs without duplicates, and the evidence reaches `VERIFIED`.
10. The supervisor opens the review. Approve is disabled until all photos are verified. They then **approve (a different user from the executor)**.
11. The work order is `CLOSED`, the light is `OPERATIONAL`, the fault is `RESOLVED`, and a **repeated-failure recommendation and INFO flag** appear (3rd failure in 180 days).
12. The warehouse receives the failed driver (serial matches), and Technician A returns any unused material.
13. The asset page shows the **complete timeline** (registration → faults → work orders → materials with serial and cost in SYP → closure seal), each row linked to its audit event.
14. The auditor runs **Verify**: chain OK, seals OK, balances OK. `make verify-audit` exits 0.
15. **Tamper demonstration:** in a scripted step, a DB superuser edits one consumed quantity. The verifier reports the exact work order and `seq`.
16. **Revocation demonstration:** the admin revokes Technician A's device. The phone's next sync is rejected, and no new work action is accepted.

### Automated evidence
- [ ] `Mfop.GoldenPath.Tests` executes steps 1–14 through the public API and passes in CI.
- [ ] The state-machine YAML tests pass for Device, Tag, Registration, Asset lifecycle/operational, Fault, WorkOrder, StockIssue, StockReturn, CoreReturn, and Flag.
- [ ] The authorization matrix tests pass for every endpoint that exists at M5.
- [ ] Audit-in-transaction tests pass for every command handler that exists at M5.
- [ ] Negative tests N1–N15 pass.

### Quality
- [ ] The field app's main flow is fully Arabic RTL with no English leftovers. The wording has been reviewed at least once by someone with field-maintenance experience, or the gaps are listed.
- [ ] The field flow runs on a low-end phone without crashes. The observed interaction time for the demo work order is recorded for later comparison with the ≤ 3 min target.
- [ ] Docs are updated: ADRs for any deviation from this spec, and the demo script.

**Explicitly not required for the first demo:** mobile registration and field check (M7), grant-window quarantine and device signing (M6), dashboard, map and checkpoints (M8), security pass and backups (M9).

## 11.3 Definition of Done — pilot-ready (end of M9), summary

Software:
- [ ] All of 11.2, plus M6–M9 acceptance criteria.
- [ ] Sync scenario suite green.
- [ ] Offboarding runbook tested.
- [ ] Backup/restore tested, with the verifier run on the restored copy.
- [ ] Security pass with no open high findings.
- [ ] Handover pack reviewed by someone who did not write the code.

**Organizational prerequisites before any real field pilot** (not needed for prototyping):
- [ ] Weekly municipal decision owner named.
- [ ] Independent auditor named, with custody of the anchor public key.
- [ ] Pilot municipality and area defined.
- [ ] Responsibility boundary with the electricity authority documented, even if partial.
- [ ] Privacy notice text approved by the municipality, plus a legal/HR review of BYOD and employee-data use.
- [ ] Device-approval and offboarding procedure agreed.
- [ ] Opening stock count performed.
- [ ] Hosting location chosen per the long-term spec (inside Syria).
- [ ] Who pays mobile data decided.
