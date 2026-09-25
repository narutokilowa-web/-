# 09 — Web Console Workflow (React, Arabic RTL)

## 9.1 Layout

- Right-hand side navigation (RTL) that **only shows sections the user's roles allow**.
- The home page for each role is **"What needs me now?"**: a queue, not charts.
- A top bar shows the user, roles, environment badge (`DEV` / `PILOT`), and in dev-fs storage mode a red **LOCAL STORAGE — NOT WORM** banner.

## 9.2 Screens by role

| Role | Home queue | Screens |
|---|---|---|
| **Supervisor** | Faults to triage · WOs pending review · overdue WOs · WOs needing reassignment · devices waiting approval · sync quarantine | Faults · Work orders (create/assign/reassign/cancel) · **Review** · Map & asset search · Asset page · Dashboard · Devices |
| **Approver** | Registrations to decide (proximity-flagged first) | **Registration review** (photos, map, nearby lights, flags, attributes; keyboard shortcuts A/R/F) · field-check results |
| **Warehouse** | Returns and core parts to receive · issues unacknowledged | Stock balances (warehouse + each custody) · Issue stock · Receive returns / core parts · Opening receipt · Item costs (view) |
| **Auditor** | Open flags by severity · last verification result · checkpoints | Flags (decide) · **Evidence chain** of any WO · Audit log search · Verify chain · Download checkpoint · Asset history · Evidence originals |
| **Admin** | Pending device approvals · recent security events | Users (create, roles, disable) · Devices (approve/revoke; shows last sync and pending count) · Policy settings · Tag batches (create, print PDF, void) |

## 9.3 Key screen: work-order review (Supervisor)

```
┌──────────────────────────────────┬───────────────────────────────────┐
│ Photos: BEFORE ⟷ AFTER side by side │ WO-2026-000123 · SL-000042        │
│ TEST photo · click to zoom        │ Diagnosis: DRIVER_FAIL + note      │
│ (derived images; tag highlighted  │ Work time: 34 min (estimated)      │
│  in arrival photo)                │ Materials: LED-DRV-50W ×1 (D-0001) │
│                                   │   expected ✓ · cost 185,000 SYP    │
│ Mini-map: asset ⊙, capture ●,     │ Core return: declared WITH_PART ✓  │
│ accuracy circle                   │ Checklist: 6/6 ✓ · test: night     │
│                                   │ Presence: 90 (breakdown ▾)          │
│ Timeline (all events, times with  │ Risk: STANDARD                      │
│ confidence markers)               │ Flags: none   /  ⚠ list with text  │
│                                   │ Diff vs previous submission (if any)│
├──────────────────────────────────┴───────────────────────────────────┤
│ [Approve]  (disabled until all evidence VERIFIED — shows "waiting for 2 uploads")   │
│ [Return]   (checklist of missing items + note)                                      │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

The server records `opened_at` and `decided_at` for each review (a later rubber-stamping signal; stored only in the pilot).

## 9.4 Key screen: asset page / tamper-evident history (the demo ending)

```
SL-000042 — نقطة إنارة (شمسية)          State: ACTIVE · OPERATIONAL      Tag: T7K3M92QX
Location (map) · address · circuit_ref · responsible party · installed (year) · warranty
Components: luminaire / driver serial D-0001 (installed 2026-10-03 via WO-…)
Recommendation: ⚠ FULL_INSPECTION — 3 failures in 180 days  [details]

Timeline (newest first, every row links to its audit event + hash):
  2026-10-03 14:22  WO-2026-000123 CLOSED by سامر (supervisor)   seal 9f3a…  ✓ verified
  2026-10-03 13:40  Material consumed: LED-DRV-50W D-0001 by أحمد
  2026-10-03 13:05  Work started (arrival presence 90)
  …
  2026-06-01 09:12  Registered by ليلى · approved by خالد · v1 snapshot 4c1e…

Cumulative cost: 412,000 SYP (materials, standard cost; per-currency totals if several)
[Verify this asset's records]  → runs chain + seal verification for the related events (auditor)
```

## 9.5 Dashboard (Supervisor/Auditor, minimal)

One row of cards, each clickable through to the underlying list:

Lights by operational status · open faults (with how many were reported with power unavailable) · open WOs by state · overdue WOs · WOs pending review · repeated-failure recommendations · open flags by severity · custody items unacknowledged over 24 h.

No charts library is required in the first demo; counts and lists are enough.

## 9.6 Map (basic)

MapLibre GL with optional self-hosted PMTiles basemap. Street-light layer colored by operational status, with client-side clustering. Clicking a light opens the asset page. **The list view is always available**, and the map is never the only way to reach anything. When no basemap file is configured, the map shows points on a plain background.
