# 07 — Minimal Inventory Workflow

**Purpose in the pilot:** prove the custody chain end to end, and nothing more.

```
warehouse stock → issue → technician acknowledgment (custody) → consumption on a work order
               → unused return / failed-part (core) return
```

> **DEMO 1 subset:** ledger with `RECEIPT`, one-step `ISSUE` to technician custody (performed by the warehouse user in the seed), `CONSUME` on the work order, and `REVERSAL`. Technician acknowledgment, returns and core returns follow in M5, before any field pilot.

## 7.1 Model recap

- **Ledger:** `stock_movements` is append-only. A balance is always the sum of movements. `stock_balances` is maintained in the same transaction for speed and reconciled nightly by the verifier.
- **Locations:** one `WAREHOUSE`, one `IN_TRANSIT`, one `CUSTODY` per technician (created with the user), plus the virtual `CONSUMED`, `CORE_BIN` and `SUPPLIER` locations.
- **Items (seed):**

| SKU | Name (ar) | Unit | Serial | Core return |
|---|---|---|---|---|
| `LED-MOD-50W` | وحدة ليد 50 واط | EA | yes | yes |
| `LED-DRV-50W` | درايفر 50 واط | EA | yes | yes |
| `PHOTOCELL` | خلية ضوئية | EA | no | no |
| `FUSE-10A` | فيوز 10 أمبير | EA | no | no |
| `CABLE-2.5` | كابل 2.5 مم | M | no | no |
| `SOLAR-BATT-100AH` | بطارية 100 أمبير | EA | yes | yes |
| `SOLAR-CTRL` | منظم شحن | EA | yes | yes |
| `SOLAR-PANEL-100W` | لوح شمسي 100 واط | EA | yes | yes |

- **Money:** each item has a standard cost history (`item_costs`: `amount`, `currency`, `as_of`). The default currency is `SYP`. Consumption cost = quantity × the cost effective on the consumption date, **in its own currency**. There is no conversion in the pilot.

## 7.2 Flows

| Flow | Commands | Movement(s) | Controls |
|---|---|---|---|
| Opening balance | `RecordReceipt` (WAREHOUSE) | SUPPLIER → WAREHOUSE | Serials required for serial-tracked items. The opening count is documented (runbook) |
| Issue to technician | `IssueStock` (WAREHOUSE) | WAREHOUSE → IN_TRANSIT | Cannot go negative. Optional `work_order_id` reference and free-text `requested_by`. The request/approval workflow is deferred |
| Acknowledge | `AcknowledgeStockIssue` (TECH, mobile) | IN_TRANSIT → CUSTODY(tech) | **Two-party handover.** Material is not in the technician's custody until they confirm. Serials confirmed by scan or tick |
| Dispute | `DisputeStockIssue` (TECH) | — | Flag. Warehouse cancels (reversal) and re-issues |
| Consume on WO | `ConsumeMaterial` (TECH, mobile, offline-capable) | CUSTODY(tech) → CONSUMED, with `work_order_id`, `asset_id`, serials | Serial must be in the tech's custody (else `WARNING`). Quantity > known custody balance → **applied** + `CONSUMPTION_EXCEEDS_CUSTODY` flag (field reality first, review after). Serial-tracked item installed → `serial_units.installed_asset_id = asset` |
| Core return | `DeclareCoreReturn` (TECH) → `ReceiveCorePart` (WAREHOUSE) | removed part → CORE_BIN (`CORE_RECEIVE`) | Obligation is created on consumption. The WO cannot be submitted without a declaration. The serial is matched against the part installed on that asset (04 §4.11) |
| Unused return | `DeclareStockReturn` (TECH) → `ReceiveStockReturn` / `RejectStockReturn` (WAREHOUSE) | CUSTODY → IN_TRANSIT → WAREHOUSE (or reversal back to CUSTODY) | Two-party |
| Correction | — | `REVERSAL` movements only (by the command that owns the flow) | No edit, no delete, no free adjustments in the pilot |

**Removed part, when there is no known installed serial** (first repair after registration): the declared serial is recorded as the historical installed serial for that asset. Later repairs are then matched against it.

## 7.3 What the demo shows

1. Warehouse view: stock of `LED-DRV-50W` = 10.
2. Warehouse issues 2 drivers (serials `D-0001`, `D-0002`) to Technician A. The drivers show as "in transit".
3. Technician A acknowledges on the phone. Custody = 2, warehouse = 8.
4. On the work order, the technician consumes `D-0001` on `SL-000042` and declares the old driver "with part" with a photo.
5. Warehouse receives the failed part. The core return is `RECEIVED`.
6. The technician returns the unused `D-0002`, and the warehouse receives it. Custody = 0, warehouse = 9, consumed = 1, core bin = 1 failed part.
7. Asset history shows: driver `D-0001` installed on date X by work order `WO-…`, cost `N SYP`.

## 7.4 Deferred (explicitly)

Stock requests with approval, custody limits, cycle counts (blind and surprise), adjustments with four-eyes, multiple warehouses, reorder points and forecasting, supplier receipts linked to purchase orders.
