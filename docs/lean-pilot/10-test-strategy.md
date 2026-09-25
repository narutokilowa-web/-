# 10 — Test Strategy (Lean Pilot)

**Principle:** the dangerous failures here are not crashes. They are an **illegal transition**, a **leaked permission**, a **lost or altered piece of evidence**, and a **change without an audit event**. Test effort goes there first.

This matters doubly because much of the code will be written by AI agents. **Tests are the guardrails that keep generated code honest**, so the specification-derived tests (YAML state machines, permission matrix, golden path) are written or generated **before or together with** the implementation, never after.

## 10.1 Test layers

| Layer | Tooling | What | Gate |
|---|---|---|---|
| **State machines** | xUnit, table-driven from `docs/state-machines/*.yaml` | For every machine: every listed transition succeeds with its guards satisfied; **every unlisted (state, command) pair is rejected**; terminal states accept nothing | CI blocks the merge |
| **Property-based** | FsCheck / CsCheck | Random command sequences never reach an unlisted state · stock ledger: balances = Σ movements, never negative in WAREHOUSE, no stock created from nothing · audit chain: any single-byte change, deletion or reorder is detected by the verifier | CI |
| **Audit-in-transaction** | Integration (Testcontainers PostGIS) | For **every** command handler: success ⇒ exactly the expected audit event(s) in the same commit; forced failure after the entity change ⇒ neither the change nor the audit event persists · `UPDATE`/`DELETE`/`TRUNCATE` on audit tables as `mfop_app` fails | CI |
| **Authorization matrix** | Generated from `docs/lean-pilot/permissions.yaml` | Every (role × action) → allowed/denied exactly as in 03 · every dynamic SoD rule (registrar≠approver, executor≠reviewer, issuer≠receiver, no self-grant) · disabled user / revoked device ⇒ 401/403 on every endpoint | CI |
| **Architecture** | NetArchTest | Modules don't reference other modules' Domain/Infrastructure; Domain has no EF/HTTP references; only `Contracts` cross module lines | CI |
| **Golden path** | Integration, public API only (`Mfop.GoldenPath.Tests`) | The entire vertical slice as one scenario, from seed to verified history, using the same HTTP and sync endpoints as the clients. **This is the executable version of the demo** | CI |
| **Sync scenarios** | Integration with a simulated device client | Duplicate batches, out-of-order dependencies, reassignment/cancellation while offline, clock set back, reboot, expired grant, revoked device, consumption over custody, hash mismatch, duplicate photo, interrupted upload (M6) | CI (from M6) |
| **Evidence pipeline** | Integration with S3 container | Announce → upload → verify → store copy (from M8: Object Lock retention set, delete attempt fails) · mismatch → flag · duplicate → flag · non-JPEG → rejected · > 15 MB → rejected | CI |
| **Mobile** | Flutter unit + widget + `integration_test` on emulator | Outbox ordering and idempotency; wizard blocking rules; locked states (PIN, grant expired, revoked); encrypted DB opens only with the Keystore key; RTL snapshots of main screens | CI (unit/widget), nightly (integration) |
| **Web E2E** | Playwright | Supervisor review approve/return · approver registration decision · auditor verify + evidence chain · admin revoke device | CI (smoke) |
| **Security** | Secret scan, dependency scan (OSV/Trivy), CodeQL/Semgrep; OWASP ZAP baseline (M9) | No secrets committed; no known critical CVEs; no high findings unresolved | CI / M9 |
| **Manual field test** | Checklist on a real low-end Android phone | Night capture, weak network, airplane mode, battery-pull mid-flow, printed tag scanning in sun/rain/dirt, Arabic wording reviewed by a field technician | Before each demo |

## 10.2 Key negative tests (must exist by the first demo)

| # | Attempt | Expected |
|---|---|---|
| N1 | Approver approves own registration | `SOD_VIOLATION`, nothing changes |
| N2 | Supervisor who was assignee approves the WO | `SOD_VIOLATION` |
| N3 | Approve WO while one photo is `UPLOADED` but not `VERIFIED` | `EVIDENCE_NOT_VERIFIED` |
| N4 | Submit repaired WO with 1 after-photo | `EVIDENCE_MISSING` |
| N5 | Technician acts on a WO assigned to someone else | `403` |
| N6 | Cancel WO that already has evidence | `INVALID_TRANSITION` |
| N7 | Any command on a `CLOSED` WO | `INVALID_TRANSITION` |
| N8 | Push from a revoked device | `401/403`, nothing applied |
| N9 | Upload bytes that don't match the announced hash | `HASH_MISMATCH` + CRITICAL flag |
| N10 | Same photo bytes used on two WOs | second `DUPLICATE` + CRITICAL flag |
| N11 | `UPDATE audit.audit_events` as app role | DB error |
| N12 | Manually alter one audit row (as owner, trigger disabled in the test) | verifier exits non-zero at that `seq` |
| N13 | Alter a consumed quantity of a closed WO directly in the DB | verifier: seal mismatch |
| N14 | Issue more stock than the warehouse holds | `INSUFFICIENT_STOCK` |
| N15 | Create a WO for an asset in `PENDING` or `UNDER_INVESTIGATION` | `ASSET_NOT_ACTIVE` |

## 10.3 Test data

- Deterministic synthetic seed (`SEED=42`), generated through the API.
- Integration tests build their own minimal fixtures through the same command handlers (no raw SQL inserts, except in tamper tests).
- No real names, phone numbers or municipality data in the repository.

## 10.4 AI-assisted development guardrails

- `AGENTS.md` lists the non-negotiables (README) and the commands to run before proposing a change (`make test`, `make verify-audit`).
- Behaviour changes must come with a change to the YAML or permission file **in the same PR**. CI fails if code and specification tables disagree.
- Every PR is reviewed by a second agent **and** by the human owner for anything touching audit, authorization, sync, evidence or migrations (listed in `CODEOWNERS`-style path rules).
- Migrations are forward-only. An applied migration is never edited.
- Coverage: ≥ 90% line coverage on `*/Domain` and on the audit/sync/evidence application code. There is no coverage target elsewhere, because coverage for its own sake invites meaningless generated tests.
