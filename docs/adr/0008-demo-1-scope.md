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
