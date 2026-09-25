# AGENTS.md — rules for AI coding agents (Claude Code, Codex) and human contributors

This repository implements the **Municipal Field Operations & Asset Integrity Platform**.
Read before changing anything:
- `docs/lean-pilot/README.md`: what we are building now and why
- `docs/lean-pilot/11-milestones-and-dod.md`: the current milestone and its exact deliverables
- `docs/status/IMPLEMENTATION_STATUS.md`: what is done and what is next
- `docs/adr/`: decisions already taken; do not silently reverse them

## Non-negotiables

1. **Every state change is a command through a server-side state machine.** There are no generic update/PATCH endpoints for operational data.
2. **Every change writes an `AuditEvent` in the same database transaction** (from M1). A handler that changes data without staging an audit event must fail.
3. **Nothing operational is deleted.** Corrections are new records or reversal events.
4. **Maker ≠ checker:** registrar ≠ approver; executor ≠ reviewer; issuer ≠ receiver; nobody grants roles to themselves.
5. **Authorization is evaluated from our database, never from token claims.** The token is identity (`sub`) only.
6. **Flags, not verdicts.** No code, text or UI labels a person as corrupt or guilty.
7. **Say "tamper-evident / verifiable", never "immutable".** A privileged DBA can change data; our guarantee is detection (ADR-0007).
8. **Evidence is captured in-app only and hashed on the device**; the server re-hashes and compares.
9. **Unknown municipality facts are configuration or data, never code:** municipality, rationing, ownership boundaries, currency, thresholds.
10. **Money = amount + ISO currency + date.** Default `SYP`. Never assume USD.

## Working rules

- **Stay inside the current milestone.** Do not implement features of later milestones "while you are there".
- **Specification tables are code.** `docs/state-machines/*.yaml` and `docs/lean-pilot/permissions.yaml` are loaded by tests. A behaviour change must update them **in the same PR**, and CI fails if code and tables disagree.
- **Migrations are forward-only.** Never edit a migration that has been merged.
- **No secrets in the repository.** Use `.env` (git-ignored) and document variables in `.env.example`.
- **Personal data never goes into logs.**
- Code, identifiers and technical comments in **English**. User-facing text **Arabic first** (English secondary) via i18n files, never hard-coded in components.
- Small PRs. Each PR states which milestone deliverable it implements and which tests prove it.
- Changes touching **audit, authorization, sync, evidence, migrations** need human owner review.

## Commands

```bash
make up            # start the stack (Docker Compose)
make down          # stop
make migrate       # apply DB migrations (runs as the owner role, one-shot)
make seed          # load dev seed data
make test          # all automated tests (backend, web, mobile)
make verify-audit  # audit chain verifier (implemented in M1)
make demo-reset    # wipe dev volumes, start again, re-seed
```

Toolchain versions: .NET SDK 10.0 · Node 22 · Flutter 3.41.6 (Android SDK 36) · Docker Compose v2+.
