# Lean Pilot Specification — Street Light Vertical Slice

**Status:** v0.2, **approved** (2026-09-24) with the DEMO 1 execution simplification: see [11](11-milestones-and-dod.md). No code until the revised milestones are approved.
**Relationship to `docs/spec/`:** `docs/spec/` (files 01–17, Arabic) is the **long-term product specification** and remains authoritative for the full product. This folder defines the **smallest trustworthy system** that proves one vertical slice end-to-end. Where this document simplifies, the simplification is listed below with the reason; nothing here weakens the long-term design, it only defers parts of it.
**Language:** English, because it is the implementation-facing spec that developers and AI coding agents work from, and code is written in English. UI text is Arabic-first.

## Goal

Get to the **first real demo** as early as it is safe to:

```
scan QR → see street light → execute repair workflow → close it → inspect tamper-evident history
```

and then to a **pilot-ready slice**:

```
Street Light → Register → Verify → Assign QR → View/Scan → Create fault → Create work order
→ Assign technician → Before evidence → Diagnose → Start work → Record material use
→ Functional test → After evidence → Submit → Supervisor review → Close
→ Audit trail → Repeated-failure flag
```

## Scope

| In scope | Out of scope (explicitly) |
|---|---|
| One municipality (single deployment, one organization record) | Citizen portal, citizen reports |
| One department | AI, chatbots, predictive maintenance / ML |
| One asset type: **street light** (grid / solar / hybrid as attributes) | Procurement, finance, ERP |
| One warehouse | Contractor management |
| A small number of users (≈ 5–20) | National integrations, SSO federation |
| Synthetic/demo data first | IoT, smart lighting control systems |
| Minimal inventory: stock → issue → acknowledge → consume → return / failed-part return | Microservices, message brokers, Kubernetes |
| Hash-chained audit trail + verifier tool | Advanced GIS (editing, spatial analysis, layer management) |
| One analytics rule: repeated failure (plus integrity flags that fall out of the design) | Complex BI, report builders |
| Android app on **personal phones (BYOD)**, offline-first | iOS, MDM, remote wipe |
| Web console (Arabic, RTL) | Multi-municipality tenancy |
| Runs fully on **one machine with Docker Compose** | Government infrastructure dependency |

## Unknowns handled as configuration or data, not code

| Unknown (TBD) | How the pilot handles it |
|---|---|
| Pilot municipality / city, number of lights | `organization` record + map center/bounds in config; synthetic data generator with configurable center, count, grid/solar mix |
| Ownership boundary municipality ↔ electricity company | `responsible_party` field on assets **and** faults: `MUNICIPALITY` / `ELECTRICITY_AUTHORITY` / `OTHER_PUBLIC_ENTITY` / `SHARED` (+ free-text entity name) |
| Electricity rationing schedule | No schedule. Each fault/inspection records `power_at_inspection`: `CONFIRMED_AVAILABLE` / `CONFIRMED_UNAVAILABLE` / `UNKNOWN`, plus optional `power_info_source` |
| Mobile data cost / Wi-Fi | App is offline-first and network-agnostic. It syncs over any approved connection. No Wi-Fi assumption |
| Currency | Money = `amount` + ISO 4217 `currency` + `as_of` date. Default operational currency `SYP` (configurable). Reference currency: optional, **not set**, not USD by default |
| Existing asset records | Not imported in the pilot. Import (CSV/Excel/GIS) is planned for later; the model keeps `source` and `external_ref` fields so imports are possible without schema changes |
| Weekly decision owner, independent auditor | Not required for software prototyping. `Auditor` stays a separate role. Both are **mandatory before a real field pilot** (see DoD) |

## Deliberate simplifications vs. the long-term spec

| Long-term spec | Lean Pilot | Why it is safe to defer |
|---|---|---|
| Multi-tenant (`tenant_id` + RLS) | Single organization per deployment | Production in Syria is likely one deployment per municipality/governorate anyway; see ADR in M0 |
| Feeder pillars, circuits as assets, multi-asset work orders | `circuit_ref` text attribute only; one asset per work order | The slice proves the evidence and integrity chain; topology comes with Phase 1 of the product |
| Generic Four-Eyes approval engine | Maker ≠ checker enforced **inside** the two workflows that need it (asset approval, work-order review) | Only two maker/checker points exist in the slice |
| Break Glass | Not implemented; no bypass path exists at all | There is nothing to bypass. Adding it later is additive |
| Random audit, reviewer analytics, consumption outliers | Not implemented | Needs real volume. The data needed for them is captured from day one |
| Health score, replacement review, forecasting | Not implemented | Needs history |
| Stock requests with approval, cycle counts, adjustments | Direct issue by warehouse officer + technician acknowledgment + returns | This is enough to prove the custody chain |
| pHash similarity detection | Exact-duplicate detection (SHA-256) only | pHash is a later milestone (M8 optional) |
| Device-key signing of all evidence | Device-key signing of **sync batches** (covers evidence hashes) from M6; bearer token + device binding before that | Keeps the first demo short without losing the design |
| Field verification sampling by risk | Approver may **request** a field check; no automatic sampling | Sampling needs volume |

## Index

| # | Document | Covers (requested items) |
|---|---|---|
| 01 | [Architecture, topology, repository, API](01-architecture.md) | 1. Architecture · 9. Docker Compose topology · 10. Repository structure · 11. API boundaries |
| 02 | [Minimal domain model](02-domain-model.md) | 2. Minimal domain model |
| 03 | [Roles and permissions](03-roles-permissions.md) | 3. Minimal roles/permissions |
| 04 | [State machines](04-state-machines.md) | 4. Exact state machines |
| 05 | [BYOD and offline security](05-byod-offline-security.md) | 5. BYOD/offline security design |
| 06 | [Audit and evidence](06-audit-and-evidence.md) | 6. Audit design · 7. Evidence design |
| 07 | [Minimal inventory](07-inventory.md) | 8. Minimal inventory workflow |
| 08 | [Mobile workflow](08-mobile-workflow.md) | 12. Mobile workflow |
| 09 | [Web console workflow](09-web-workflow.md) | 13. Web dashboard workflow |
| 10 | [Test strategy](10-test-strategy.md) | 14. Test strategy |
| 11 | [Milestones and Definition of Done](11-milestones-and-dod.md) | 15. Sequential milestones · 16. DoD for first runnable demo |

## Honest integrity claim

The system provides **reliable tamper detection**, not mathematical immutability. A privileged database administrator *can* modify data. The hash chain, work-order seals and the verifier make such a change **detectable**. Before a real field pilot, signed daily checkpoints stored outside the database (WORM) make it detectable even if the whole database is rewritten.

## Non-negotiables (carried over unchanged from the long-term spec)

1. Every state change goes through a **server-side state machine command**. There is no generic update endpoint.
2. Every change writes an **audit event in the same database transaction**. The audit table is append-only and hash-chained.
3. Evidence is captured **in-app only** and hashed **on the device at capture**. The server verifies the hash. Write-once (WORM) storage is **mandatory before a real field pilot** (M8), not for DEMO 1.
4. **Maker ≠ checker:** registrar ≠ approver, executor ≠ reviewer.
5. **Nothing is deleted.** Corrections are new events.
6. **Flags, not verdicts.** The system never labels a person.
7. **Offline-first by construction:** mobile writes are commands in an outbox from the first mobile milestone, even when the phone is online.
