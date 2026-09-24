# 11 — Milestones and Definition of Done (revised around DEMO 1)

**Revision 3 (2026-09-24):** a minimal repeated-failure rule was moved into M3 / DEMO 1 (step 17a, M3 item 10).
**Revision 2 (2026-09-24).** Milestones are now organized around **DEMO 1**: the earliest runnable demo that proves the street-light lifecycle end to end on one machine. Stronger pilot controls come **after** DEMO 1, incrementally, and every one of them is **mandatory before a real field pilot**. The long-term spec (`docs/spec/`) and the rest of this Lean Pilot spec stay unchanged as the target. This file only changes the **order** in which things are built.

**Team assumption:** 1–2 people with AI-assisted development (Claude Code / Codex for implementation and review), and a human owner who accepts each milestone.
**Estimates** are in working weeks, ±30%, and will be re-estimated after M1.
**Rule:** each milestone ends runnable and demonstrable, merged through reviewed PRs with green CI.

---

## 11.1 DEMO 1 — what must physically work

| # | Step | Where |
|---|---|---|
| 1 | Start the system with Docker Compose | laptop |
| 2 | Log in as asset registrar | web |
| 3 | Register a synthetic street light | web |
| 4 | Another role (approver) approves it | web |
| 5 | Print / show its QR | web |
| 6 | Scan that QR from Android | phone |
| 7 | See the asset record | phone |
| 8 | Create a fault | web (supervisor) |
| 9 | Create and assign a work order | web (supervisor) |
| 10 | Technician records before evidence | phone |
| 11 | Technician records diagnosis | phone |
| 12 | Start work | phone |
| 13 | Record one material | phone |
| 14 | Perform the functional-test checklist | phone |
| 15 | Add after evidence | phone |
| 16 | Submit | phone |
| 17 | Supervisor approves and closes | web |
| 17a | For the seeded light with earlier closed faults, the asset page (and phone asset card) shows **Recurrent Failure — Recommendation: Full inspection recommended** | web / phone |
| 18 | Web dashboard shows the completed work order | web |
| 19 | Audit history shows every important change | web |
| 20 | Manually tamper with a DB record; the verifier detects it | terminal |

### What DEMO 1 does **not** require (added after DEMO 1, before any field pilot)

Advanced analytics (beyond the single minimal repeated-failure rule, which **is** in DEMO 1) · random audits · true WORM / Object Lock storage · signed daily checkpoints · advanced GIS / map · full offline conflict resolution · offline grants and grant-window enforcement · device-key signing · complete inventory (issue acknowledgment, returns, core returns) · mobile registration and field check · pre-printed tag batches as controlled stock · TLS reverse proxy · backups.

### State machines implemented for DEMO 1 (all others stay specified in 04, deferred)

| Machine | DEMO 1 subset (from 04) | Deferred parts |
|---|---|---|
| **Asset / AssetVerification** | Registration `DRAFT → SUBMITTED → APPROVED / RETURNED / REJECTED` (registrar ≠ approver); lifecycle `PENDING → ACTIVE / REJECTED`; operational status derived from events | field check, `UNDER_INVESTIGATION`, proximity flag |
| **Fault** | `OPEN → IN_WORK_ORDER → RESOLVED`; `OPEN → DISMISSED`; duplicate → confirmation | `TRANSFERRED`, theft/vandalism re-open paths |
| **WorkOrder** | W1, W2, W4, W5, W6, W9, W10, W11, W12 (create → assign → on-site → in-progress → pending review → closed / returned → resubmit; cancel before evidence) | `ON_HOLD` (W8), `ResolveNoWork` (W7), risk tier |
| **Assignment** | assign; reassign (W3) ends the old assignment record and creates a new one | multi-technician (assistants) |
| **Evidence** | `ANNOUNCED → UPLOADED → VERIFIED / HASH_MISMATCH / DUPLICATE` | presence score, perceptual hash |
| **StockTransaction / MaterialUse** | ledger movements `RECEIPT`, `ISSUE` (warehouse → technician custody, **one step**), `CONSUME` (custody → work order), `REVERSAL` | `IN_TRANSIT` + technician acknowledgment, returns, core returns |
| **Approval / Closure** | registration approval; work-order review (approve / return); closure seal | second approver for high risk, amendments |
| **Audit events** | full DEMO 1 audit design (11.3) | checkpoints, external anchoring |

Supporting (not a business state machine, minimal): **device record** `PENDING_APPROVAL → ACTIVE → REVOKED`. The phone registers itself on first login and an admin approves it with one click. It exists because approved decision #1 (server-side revocation) needs a device identity from the start. The offline grant is **not** in DEMO 1.

### DEMO 1 simplifications and why they are safe

| Simplification | Replaced before field pilot by | Why it's safe for DEMO 1 |
|---|---|---|
| Registration is done **on the web** by the registrar, with synthetic data; photos are optional and marked `capture_channel = WEB` | Mobile field registration with in-app photos + field check (M7) | The data is synthetic. The maker/checker approval chain is what DEMO 1 proves |
| On approval the system **generates** a tag (QR) for the asset | Pre-printed tag batches issued as controlled stock (M7) | Same QR format and resolution path; only the tag's origin changes |
| **Local filesystem** object store behind the S3-compatible `IObjectStore` interface; evidence hashes still verified | S3-compatible storage with Object Lock (M8) | The interface is the same, so the swap is configuration |
| No reverse proxy / TLS; the phone reaches the laptop via `adb reverse` (`localhost`) | Caddy TLS proxy (M8) | Local-only; Keycloak issuer stays `localhost` for both browser and phone |
| Background work (evidence verification) runs as a hosted service **inside the API process** | Separate `worker` container (M8) | Same code, different host process |
| Mobile commands use the single write path (`/sync/v1/push`, `command_id`, idempotent), with a simple persisted retry queue | Offline grants, trusted time, conflict matrix, quarantine (M4) | Offline architecture is already in place; only the hardening waits |
| Material comes from custody **seeded** by the warehouse user (issue movement via API) | Issue + technician acknowledgment + returns + core returns (M5) | The ledger and `CONSUME` linkage to the work order and asset are real |

---

## 11.2 Milestones up to DEMO 1

### M0 — Foundations (≈ 1 week)

**Exact deliverables**

1. **ADRs** in `docs/adr/`:
   - `0001` backend .NET modular monolith
   - `0002` single organization per deployment
   - `0003` Keycloak for authentication/MFA only; permissions, device trust and grants in our backend
   - `0004` evidence uploads streamed through our API
   - `0005` `IObjectStore` abstraction: local filesystem in dev, S3-compatible product chosen later
   - `0006` no Redis or message broker; PostgreSQL outbox
   - `0007` audit = tamper **detection** (hash chain + verifier), not immutability
   - `0008` DEMO 1 scope and deferrals (this file)
2. **Agent rules:** `AGENTS.md` (non-negotiables, commands to run, "never edit an applied migration", "spec tables change in the same PR as the behaviour") and `CLAUDE.md` → `AGENTS.md`.
3. **Repository skeleton** per 01 §1.3, limited to the DEMO 1 modules:
   - backend projects: `Mfop.Api`, `Mfop.Kernel`, `Mfop.Infrastructure`, and modules `Access`, `Assets`, `Faults`, `WorkOrders`, `Evidence`, `Inventory`, `Audit`
   - `tools/audit-verify` project stub
   - React app skeleton (Vite, TypeScript, RTL, Arabic/English i18n) and Flutter app skeleton (Arabic RTL)
4. **Docker Compose (DEMO 1 topology):**
   - `db`: PostGIS 17, with `mfop` and `keycloak` databases and roles `mfop_owner`, `mfop_app`, `mfop_readonly`
   - `keycloak`: realm import with clients `web` and `mobile` (PKCE); seeded demo users `registrar1`, `approver1`, `supervisor1`, `tech1`, `warehouse1`, `auditor1`, `admin1`; dev passwords from `.env`; MFA disabled in the `dev` profile
   - `api`, and `web` (Vite dev server or static)
   - a named volume for the local object store
   - `Makefile`: `up`, `down`, `seed`, `test`, `verify-audit`, `demo-reset`
5. **Spec as data:** `docs/state-machines/*.yaml` for the DEMO 1 machines listed in 11.1, and `docs/lean-pilot/permissions.yaml` (DEMO 1 subset of 03).
6. **CI** (GitHub Actions): backend build + tests, web build + lint, Flutter analyze + unit tests, secret scan, dependency scan.

**Accepted when:** from a clean clone, `make up` starts all services healthy in ≤ 15 min on one laptop; every seeded user can log in to the web and sees a placeholder page showing their role (read from **our** DB, not the token); the Flutter debug APK builds; CI is green.

### M1 — Audit core + register / approve / QR (web) (≈ 1.5–2 weeks)

**Exact deliverables**

1. **Kernel:**
   - `Result`/`Error` with stable codes; `Money` (amount + ISO currency + date); `IClock`; UUIDv7 ids
   - a state-machine base that loads its transition table and is checked against the YAML
   - `CommandEnvelope`
2. **Audit core:**
   - `UnitOfWork` stages an `AuditEvent` for every entity change; commit fails if a change has no audit event
   - `audit_events` records seq, actor, roles, device, time, action, entity, **old/new values**, reason, command id, and `prev_hash`/`hash` (canonical JSON)
   - append-only grants for `mfop_app` and an UPDATE/DELETE/TRUNCATE-rejecting trigger, **documented as a guard against accidents and application bugs, not as protection against a privileged DBA**
3. **`tools/audit-verify` CLI:**
   - recomputes the chain and checks seq continuity and links
   - recomputes asset version snapshot hashes
   - exit code plus a report naming the first broken `seq` and entity
4. **Access (minimal):**
   - user record created or linked on first login (Keycloak `sub`); role assignments seeded
   - authorization pipeline: permission + record policy (maker ≠ checker); matrix tests generated from `permissions.yaml` for M1 endpoints
5. **Assets — registration on web:**
   - registrar form with synthetic street-light attributes (power source, responsible party, lat/lon entry, install date precision), `capture_channel = WEB`, optional photos
   - submit; approver queue: approve / return / reject, approver ≠ registrar
   - on approval: `asset_code` issued (`SL-000001`), asset version v1 + snapshot hash, **generated tag** bound
6. **QR:** asset page shows the QR (tag URL + human-readable code) with **Print** (A6 label layout via the browser print view).
7. **History:** asset page timeline composed from asset versions + audit events, with old → new values, actor, time and reason.
8. **Tests:**
   - YAML tests for the registration and lifecycle machines
   - audit-in-transaction tests for every M1 handler
   - N1 (self-approval rejected), N11 (app role cannot update audit), N12 (tampered audit row detected)

**Accepted when:** DEMO 1 steps **1–5** work; the asset page shows the registration and approval history with old/new values; editing an audit row or an asset row directly in the DB makes `make verify-audit` fail at the right place.

### M2 — Android scan + faults + work-order creation (≈ 1.5–2 weeks)

**Exact deliverables**

1. **Flutter app:**
   - Keycloak login (system browser, PKCE) through `adb reverse`
   - device registers itself (`PENDING_APPROVAL`); admin approves on a minimal web **Devices** page; revoked devices are rejected by the API on every request
   - app PIN lock
   - encrypted local DB (SQLCipher)
2. **Scan → asset record:** camera QR scan (plus manual code entry) → `GET /sync/v1/tags/{code}` → asset card (code, type, power source, operational status, responsible party, last events).
3. **Single write path:** `/sync/v1/push` server endpoint (command pipeline, idempotency by `command_id`, per-command results); the mobile persisted command queue with retry. **No conflict resolution and no grants yet.**
4. **Faults (web, supervisor):**
   - "Report fault" on the asset page: category, power at inspection (`CONFIRMED_AVAILABLE / CONFIRMED_UNAVAILABLE / UNKNOWN`) + optional source, description
   - dedup to a confirmation; dismiss with a reason
   - asset operational status → `FAULTY` per 04 §4.6
5. **Work orders (web, supervisor):** create from a fault (fault → `IN_WORK_ORDER`), assign / reassign to a technician **who has an active device**, cancel before evidence; work-order detail page with timeline.
6. **Mobile "My tasks":** list of assigned work orders (pull), open a task and see the asset.
7. **Tests:** YAML tests for Fault, the WorkOrder transitions W1–W4 and Assignment; matrix tests for M2 endpoints; N5 (acting on another technician's work order) and N15 (work order for a non-active asset).

**Accepted when:** DEMO 1 steps **6–9** work on a real Android phone: scan the printed QR → see the record; supervisor creates the fault and the work order and assigns it; the task appears on the technician's phone.

### M3 — Field execution + review + dashboard → **DEMO 1** (≈ 2–2.5 weeks)

**Exact deliverables**

1. **Evidence pipeline:**
   - in-app camera only (no gallery), on-device JPEG re-encode + SHA-256
   - `AddEvidence` command announces the hash
   - `PUT /sync/v1/evidence/{id}/content` streams to the local object store while computing the server hash
   - hosted service verifies: `VERIFIED` / `HASH_MISMATCH` / `DUPLICATE`, and generates previews without EXIF
   - web shows previews only to authorized roles
2. **Technician flow on the phone:**
   - Arrive: tag scan must match the asset (manual code needs a reason)
   - 2 before photos
   - diagnosis (seeded codes, grid/solar filtered)
   - **Start work**
   - **record one material** from the technician's custody, with serial for tracked items
   - functional-test **checklist** (test method + items, template pinned per work order) + test photo
   - ≥ 2 after photos
   - **Submit** (guards per W9, excluding core return)
3. **Minimal inventory:**
   - seeded items with costs in SYP; `RECEIPT` of opening stock and `ISSUE` to `tech1` custody, done by `warehouse1` via the API in `make seed`
   - `CONSUME` movement linked to work order + asset; consuming more than the known custody is applied and marked in the review screen
   - a stock balances page for warehouse and custody
4. **Review and closure (web, supervisor ≠ executor):**
   - review screen with before/after photos, diagnosis, material, checklist, timeline; **Approve disabled until all evidence is `VERIFIED`**
   - approve → `CLOSED` with **work-order seal** (hash of the work order, events, evidence hashes, material movements, checklist, submissions, reviews)
   - return → resubmit (additions only)
   - asset → `OPERATIONAL` (or `UNKNOWN` if not tested); fault → `RESOLVED`
5. **Dashboard (web):**
   - cards: lights by operational status, open faults, open work orders by state, pending review
   - a **"Recently completed work orders"** list linking to each work order
6. **Audit history:**
   - work-order page and asset page timelines show every command with actor, device, time, reason, and old → new values, each linked to its audit event and hash
   - the auditor can search audit events by entity
7. **Verifier extended:** recomputes each `CLOSED` work order's seal from operational tables and recomputes stock balances from movements.
8. **Golden path + demo:**
   - `Mfop.GoldenPath.Tests` runs DEMO 1 steps 2–19 through the public API
   - `docs/runbooks/demo-1.md` gives the exact click-by-click script, including the tamper step (for example `UPDATE inventory.stock_movements SET quantity = 3 …` as `mfop_owner`), then `make verify-audit` showing the seal mismatch for that work order
9. **Tests:** YAML tests for the WorkOrder (DEMO 1 subset), Evidence and StockTransaction machines; N2–N4, N6, N7, N9, N10, N13, N14.
10. **Minimal repeated-failure rule** (one transparent rule; not an analytics system):
    - evaluated when a fault becomes `RESOLVED`
    - counts the asset's `RESOLVED` faults whose resolving work order has a diagnosis with `counts_as_failure = true`, within the window
    - `NO_FAULT_FOUND`, `GRID_SUPPLY_OFF`, theft and vandalism do not count
    - if the count ≥ threshold, the asset shows **"Recurrent Failure — Recommendation: Full inspection recommended"** (Arabic text in the UI), with the list of the counted faults and the configured threshold and window
    - `threshold` (default 3) and `window_days` (default 180) are read from the `rule_settings` table (seeded data, changeable by admin through the policy settings, audited); nothing is hard-coded in the business logic
    - `make seed` creates a light with 2 earlier resolved faults inside the window, so the DEMO 1 repair produces the recommendation
    - tests: at threshold − 1 no recommendation, at threshold a recommendation, a fault outside the window is ignored, excluded diagnoses are ignored, a changed setting takes effect without a code change
    - **no** scoring, prediction or other rules

**Accepted when:** the **DEMO 1 Definition of Done (11.3)** is met.

**Expected timing to DEMO 1:** ≈ **6–7.5 weeks** from the start of M0 (±30%).

---

## 11.3 Definition of Done — DEMO 1

### Environment
- [ ] Clean clone → `make up && make seed` on one laptop, ≤ 15 min, ≤ 6 GB RAM, with no government or foreign-cloud dependency.
- [ ] A real Android phone (8+) is connected with `adb reverse`, and the debug APK is installed.
- [ ] No secrets committed; `.env.example` complete.

### The 20 steps
- [ ] Steps 1–20 of 11.1 performed live, following `docs/runbooks/demo-1.md`, with no manual DB edits except step 20.

### Integrity (as claimed, no more)
- [ ] Every command in the slice writes an audit event in the same transaction, with actor, device (for mobile), time, reason where required, and old/new values.
- [ ] The hash chain verifies on the untouched demo DB (`make verify-audit` → exit 0).
- [ ] After step 20, the verifier exits non-zero and names the tampered entity (a broken chain at `seq`, or a seal or balance mismatch for the named work order).
- [ ] Step 17a: the recurrent-failure recommendation appears for the seeded light, with the counted faults, threshold and window visible, and the threshold and window come from `rule_settings`.
- [ ] Maker ≠ checker is enforced: the registrar cannot approve their own registration, and the executing technician cannot close their own work order (tests N1, N2).
- [ ] Documentation and UI text say **"tamper-evident / verifiable"** and never "immutable". A privileged DB administrator *can* change data; the system's guarantee is that such a change is **detected** by the verifier.

### Automated
- [ ] Golden path test green in CI.
- [ ] YAML state-machine tests green for every DEMO 1 machine.
- [ ] Authorization matrix tests green for every existing endpoint.
- [ ] Audit-in-transaction tests green for every existing handler.
- [ ] Negative tests N1–N7, N9–N15 green (N8, revoked-device push, is covered from M2 by request rejection).

### Quality
- [ ] Web and mobile main flows in Arabic RTL. Remaining English strings are listed.
- [ ] The phone flow completes on a low-to-mid Android device without crashes. Interaction time is recorded for later comparison.
- [ ] ADR `0008` updated with anything that deviated.

---

## 11.4 After DEMO 1 — stronger pilot controls, incrementally

Each milestone below is **required before a real field pilot**. Order may change based on DEMO 1 feedback.

| # | Milestone | Adds | Est. |
|---|---|---|---|
| **M4** | Offline and BYOD hardening | offline grants with configurable grace (05 §5.5), trusted time, server grant-window checks, quarantine + supervisor resolution, full sync scenario suite, resumable uploads, device-key signing, offboarding flow (disable → Keycloak logout-all → grants revoked → reassignment queue), locked states on the phone, min app version | 1.5–2 |
| **M5** | Inventory completion | `IN_TRANSIT` + technician acknowledgment/dispute, unused returns, core returns with serial matching, inventory flags (04 §4.9–4.11, 07) | 1 |
| **M6** | Integrity and analytics basics | repeated-failure rule extended to auditor flags and dashboard (the minimal rule is already in DEMO 1), integrity flags (mock location, weak presence, hash mismatch/duplicate surfaced as flags), presence score, risk tier, `ON_HOLD` and `ResolveNoWork` (incl. `GRID_SUPPLY_OFF`, theft), auditor flag workflow | 1–1.5 |
| **M7** | Field registration and controlled tags | mobile registration with in-app photos, pre-printed tag batches as controlled stock, field check, proximity flag, `UNDER_INVESTIGATION` | 1–1.5 |
| **M8** | Pilot-grade infrastructure | S3-compatible storage with **Object Lock / WORM** (product chosen after licence check), **signed daily checkpoints** to external/WORM storage with auditor-held public key, separate worker container, Caddy TLS proxy, backup/restore runbook tested (verifier run on the restored copy), basic map | 1–1.5 |
| **M9** | Pilot-readiness and handover | security pass (ASVS L2 subset, ZAP baseline), low-end phone performance, Arabic terminology review with technicians, `docs/handover/` (Arabic + English), `make demo-reset` | 1–1.5 |

**Pilot-ready software ≈ weeks 13–17** (±30%).

## 11.5 Definition of Done — real field pilot (summary)

**Software:**
- [ ] DEMO 1 DoD, plus the M4–M9 acceptance criteria.
- [ ] **WORM / Object Lock storage and signed daily checkpoints active** (mandatory; not optional).
- [ ] Offline grants and server-side revocation tested end to end.
- [ ] Backup/restore tested.
- [ ] Security pass with no open high findings.

**Organizational:**
- [ ] Weekly municipal decision owner named.
- [ ] Independent auditor named, holding the checkpoint public key.
- [ ] Pilot municipality and area defined.
- [ ] Responsibility boundary with the electricity authority documented (even if partial).
- [ ] Privacy notice approved, plus legal/HR review of BYOD and employee data.
- [ ] Device-approval and offboarding procedure agreed.
- [ ] Opening stock count performed.
- [ ] Production hosting chosen inside Syria.
- [ ] Who pays mobile data decided.
