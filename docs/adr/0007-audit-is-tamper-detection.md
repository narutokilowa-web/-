# ADR-0007: The audit trail provides tamper **detection**, not immutability

- **Status:** Accepted
- **Date:** 2026-09-24

## Context
A privileged database administrator can modify any row, and no application design fully prevents that on the same infrastructure. Claiming immutability would be false.

## Decision
- Every state change writes an `AuditEvent` **in the same transaction**. The event records actor, device, time, action, entity, old/new values, reason and command id.
- Events are hash-chained (`hash = SHA-256(prev_hash || payload_hash || seq)`, payload in RFC 8785 canonical JSON).
- Closed work orders carry a **seal** (a hash over their full evidence chain).
- DB grants and a trigger block UPDATE/DELETE/TRUNCATE on audit tables for the application role. This is a guard against accidents and bugs, **not** against a privileged DBA.
- A standalone **verifier** (`tools/audit-verify`) recomputes chains, seals and stock balances and reports the first discrepancy.
- Interfaces for **signed daily checkpoints** and **external WORM storage** exist in the design. Implementing them is **mandatory before a real field pilot (M8)**, not before DEMO 1.
- All documentation and UI text use "tamper-evident / verifiable", never "immutable".
