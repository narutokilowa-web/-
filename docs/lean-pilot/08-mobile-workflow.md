# 08 — Mobile Workflow (Flutter, Android)

## 8.1 Design constraints

- Arabic, RTL, large touch targets (≥ 56 dp), one primary action per screen, bottom-anchored primary button, night mode.
- Works on a mid/low-end Android 8+ phone. Cold start < 3 s, camera open < 1 s.
- **Every write is a command in the local outbox.** The UI updates optimistically from local state and shows the sync status per item.
- A persistent status strip at the top shows `Online / Offline` · `N waiting to send` · `M photos uploading` · the time left on the grant (only when < 3 h).

## 8.2 Screen map

```
[First run] Privacy notice → Login (system browser) → Device registration → "Waiting for approval"
                                                              │ approved
                                                              ▼
                                               Set app PIN → Initial sync
                                                              │
      ┌───────────────────────────── bottom tabs (role-dependent) ─────────────────────────────┐
      │  My tasks (TECH)  │  Scan (all field roles)  │  Report / Register (INSP / REG)  │  Me  │
      └──────────────────────────────────────────────────────────────────────────────────────┘
[Locked states] App PIN prompt · "Grant expired — connect and sign in" · "Device revoked" · "Update required"
```

## 8.3 Scan → asset card (the first demo entry point)

```
Scan tab → live camera QR reader (also: "type the code" field for damaged tags)
  → resolve locally from zone asset index; else online GET /sync/v1/tags/{code}
  → Asset card:
      SL-000042 · نقطة إنارة · شمسية/شبكة
      الحالة: معطّل  (colored)      المسؤولية: البلدية
      آخر صيانة: 2026-08-12 · أعطال آخر 180 يومًا: 2
      ⚠ توصية: فحص شامل   (if open recommendation)
      Open work order (if assigned to me) → [Open task]
      [Report a fault]  (TECH/INSP/SUP)
      History (collapsed): last 5 events, compact
```

## 8.4 Work order flow (Technician)

A 7-step wizard with a progress bar. Each step is saved immediately, so a dead battery loses nothing.

| Step | Screen | Commands produced | Blocking rules shown in plain Arabic |
|---|---|---|---|
| 0 | **My tasks**: list sorted by priority/SLA with asset code, short address, distance (if a location is available), sync badge | — | — |
| 1 | **Arrive**: "Scan the tag on the pole" (or type the code + reason) | `ArriveOnSite` | Wrong tag → "This tag belongs to SL-000017, not SL-000042" |
| 2 | **Before photos**: 2 guided captures, overlay "Show the tag in this photo" | `AddEvidence` ×2 | Cannot continue without 2 photos |
| 3 | **Diagnosis**: large icon grid of codes (grid/solar codes filtered by `power_source`), short note; buttons for "No fault found", "Power cut (grid)", "Theft", "Vandalism", "Asset not here", "Different asset" | `RecordDiagnosis` (+ `ResolveNoWork` path with 1 photo, and a power observation for grid cuts) | — |
| 4 | **[Start work]** big green button | `StartWork` | — |
| 5 | **Materials**: suggestions from the diagnosis's expected items that are in my custody; +/− steppers; serial scan for tracked items; "old part is with me" / "old part unrecoverable (reason)" + photo | `ConsumeMaterial`, `DeclareCoreReturn` | Required material missing → justification box; core-return declaration mandatory for tracked items |
| 6 | **Test and checklist**: test method (night / photocell covered / bypass / not tested + reason), then one yes/no card per item, then test photo | `RecordChecklist`, `AddEvidence(TEST)` | "Exposed wires = yes" blocks "repaired" and suggests a P1 fault |
| 7 | **After photos** (≥ 2) → **Summary** → **[Send for review]** | `AddEvidence` ×2, `SubmitWorkOrder` | Summary lists anything missing |
| — | Any step: **[Pause]** with reason | `PutOnHold` | Resuming requires a new arrival |

After submitting, the task shows `Sent — waiting for review` (or `Queued — will send when online`). A **returned** task comes back with the reviewer's reasons at the top. The technician can then add evidence and resubmit, or re-arrive on site for rework.

## 8.5 Other field flows

| Flow | Role | Screens | Commands |
|---|---|---|---|
| **Report fault** (inspection) | INSP / TECH / SUP | scan or pick asset → category → **power at this moment: available / unavailable / unknown** (+ source: seen on site / electricity company / schedule / resident / other) → optional photo → send | `ReportFault` (dedup → confirmation) |
| **Register street light** | REG | new → scan tag from my batch → wait for location fix (shows accuracy) → proximity warning if needed (justify) → overview photo + tag photo (+ nameplate for solar) → attributes (defaults from the last registered) → power source → responsible party (default from config) → install date (exact / year / unknown) → observed state → save draft → submit (single or batch) | `RegisterAssetDraft`, `AddEvidence`, `SubmitRegistration` |
| **Field check** | INSP | list of requested checks → scan → photo (the registrar's photos are hidden until after capture) → outcome | `RecordFieldCheck` |
| **Custody** | TECH | Me → My custody: balances; pending issues to acknowledge or dispute; core returns to hand in; declare return | `AcknowledgeStockIssue`, `DisputeStockIssue`, `DeclareStockReturn` |
| **Sync** | all | Me → Sync: queue with per-item status, errors in plain language, "Send now" | — |

## 8.6 Outbox and sync behaviour

- Commands are persisted in the encrypted DB with `command_id` (UUIDv7), `depends_on`, capture time, `grant_id` and `payload_hash`.
- The sync worker runs when connectivity appears, every 15 min while the app is in the foreground, and on "Send now". The order is: push commands in order → upload evidence (resumable from M6) → pull → renew the grant.
- Per-command results: `APPLIED` / `DUPLICATE` (treated as success), `REJECTED(code)` (shown with an explanation; the local state is rolled back to the server state), `QUARANTINED` (shown as "waiting for supervisor decision").
- **The server wins.** After a pull, local optimistic state is replaced by server state. A rejected optimistic action is shown in the queue so the technician understands what happened.

## 8.7 Locked states

| State | Trigger | What the user can do |
|---|---|---|
| PIN lock | backgrounded > `app_lock_minutes` | enter PIN / biometric |
| Grant expired | grace elapsed | see pending count; "Connect and sign in" |
| Device revoked | server `DEVICE_REVOKED` | nothing (the app discards its own key: best-effort hygiene) |
| Update required | server `426` | download the new APK from the municipality link |
